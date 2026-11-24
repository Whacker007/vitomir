// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The engine bridge made from the shipped classes instead of the sources (probe-classes).
//
// A store copy of 1.1 (GOG's, Steam's) has no Codex\ sources, only Codex.nob: the 465 classes 1.1 runs,
// compiled by JDK 1.1 javac (class-file major 45). The 18 `Codex*` classes in it declare the 447 natives.
// This gives each native a body that forwards to `EngineBridge.call(key, self, args)`, the bytecode ECJ
// makes of the body `CodexBridge` writes for the same declaration — the same key, the same boxing, the
// same unboxing and default — and gives `Codex` the GUID hook `CodexBridge.AddGuidHook` adds. Everything
// else in each class is kept byte for byte (but a local 1.1's javac let a method read unassigned, given its
// default: UnassignedLocals). It runs at load time, into the cache, from the player's own archive; nothing it
// makes is checked in.
using System.Buffers.Binary;
using System.IO.Compression;

namespace Vitomir.Scripting;

public static class CodexClasses
{
    const string Bridge = "EngineBridge", Obj = "java/lang/Object";
    const string CallDesc = "(Ljava/lang/String;Ljava/lang/Object;[Ljava/lang/Object;)Ljava/lang/Object;";

    /// <summary>The shipped classes of an archive (`Codex.nob`: the ones at its root) or a folder (the layout's `scripts\compiled`), by class name.</summary>
    public static SortedDictionary<string, byte[]> Read(string archiveOrFolder)
    {
        var all = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        if (Directory.Exists(archiveOrFolder))
        {
            foreach (var f in Directory.GetFiles(archiveOrFolder, "*.class"))
                all[Path.GetFileNameWithoutExtension(f)] = File.ReadAllBytes(f);
            return all;
        }
        using var zip = ZipFile.OpenRead(archiveOrFolder);
        foreach (var e in zip.Entries)
        {
            string one = e.FullName.Replace('\\', '/').TrimStart('/');
            if (one.Contains('/') || !one.EndsWith(".class", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            var bytes = new byte[e.Length];
            s.ReadExactly(bytes);
            all[one[..^6]] = bytes;
        }
        return all;
    }

    /// <summary>Every class of a ZIP (a class library: <c>classes.zip</c>), by its path less <c>.class</c>, for the tests' scans.</summary>
    public static SortedDictionary<string, byte[]> ReadAll(string zipPath)
    {
        var all = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase)))
        {
            using var s = e.Open();
            var bytes = new byte[e.Length];
            s.ReadExactly(bytes);
            all[e.FullName[..^6]] = bytes;
        }
        return all;
    }

    /// <summary>Whether a class is one the bridge rewrites: the `Codex*` API classes, as `CodexBridge` takes their sources.</summary>
    public static bool Bridged(string className) =>
        className.StartsWith("Codex", StringComparison.Ordinal)
        && !className.Equals(Path.GetFileNameWithoutExtension(CodexBridge.Excluded), StringComparison.Ordinal);

    /// <summary>
    /// **A shipped class as the build writes it**: any local one of its methods reads unassigned on some path given its
    /// default first (<see cref="UnassignedLocals"/>: the verifier's own question asked of every class — in 1.1's 464 the one answer
    /// is `DisciplineEyesOfChaos.cast`'s <c>strType</c>, slot 6, which without it IKVM refuses whole, taking the Eyes of Chaos out
    /// of the game), then, for a `Codex*` class, its natives bridged (<see cref="Transform"/>). Each fix goes in
    /// <paramref name="notes"/> as a 1.1 compat note; <paramref name="natives"/> is how many natives were given a body. A class that
    /// needs neither comes back as the bytes it was.
    /// </summary>
    public static byte[] Rewrite(string className, byte[] classFile, List<string> notes, out int natives)
    {
        natives = 0;
        var c = ClassFile.Read(classFile);
        byte[] written = UnassignedLocals.Fix(c, notes) ? c.Write() : classFile;
        return Bridged(className) ? Transform(written, out natives) : written;
    }

    /// <summary>
    /// One shipped `Codex*` class with its natives forwarding to the engine (and, for `Codex`, the GUID hook).
    /// Returns the new class file; <paramref name="natives"/> is how many methods were given a body.
    /// </summary>
    public static byte[] Transform(byte[] classFile, out int natives)
    {
        var c = ClassFile.Read(classFile);
        string cls = c.Name;
        ushort codeName = c.AddUtf8("Code");
        natives = 0;
        foreach (var m in c.Methods)
        {
            if ((m.Access & ClassFile.AccNative) == 0) continue;
            m.Access &= unchecked((ushort)~ClassFile.AccNative);
            m.Attributes.Add((codeName, Forward(c, cls, c.Utf8(m.Name), c.Utf8(m.Descriptor), (m.Access & ClassFile.AccStatic) != 0)));
            natives++;
        }
        if (cls == "Codex") AddGuidHook(c, codeName);
        return c.Write();
    }

    /// <summary>
    /// The body ECJ compiles from <c>CodexBridge.Rewrite</c>'s
    /// <c>return EngineBridge.asInt(EngineBridge.call("Cls.Name", this, new Object[]{new Integer(a), s}), 0);</c>,
    /// instruction for instruction (checked against that compile).
    /// </summary>
    static byte[] Forward(ClassFile c, string cls, string name, string desc, bool isStatic)
    {
        var (pars, ret) = Parse(desc);
        var code = new List<byte>();
        void Op(params int[] b) { foreach (var x in b) code.Add((byte)x); }
        void U2(int v) { Op(v >> 8, v & 0xFF); }
        void Ldc(ushort i) { if (i < 256) Op(0x12, i); else { Op(0x13); U2(i); } }
        void Push(int n)
        {
            if (n is >= -1 and <= 5) Op(0x03 + n);
            else if (n is >= -128 and <= 127) Op(0x10, n & 0xFF);
            else { Op(0x11); U2(n); }
        }
        void Load(char kind, int slot)
        {
            // iload 0x15/0x1a, lload 0x16/0x1e, fload 0x17/0x22, dload 0x18/0x26, aload 0x19/0x2a
            (int op, int short0) = kind switch
            {
                'J' => (0x16, 0x1e), 'F' => (0x17, 0x22), 'D' => (0x18, 0x26), 'L' or '[' => (0x19, 0x2a), _ => (0x15, 0x1a),
            };
            if (slot <= 3) Op(short0 + slot); else if (slot <= 255) Op(op, slot); else { Op(0xc4, op); U2(slot); }
        }

        int stack = 0, max = 0;
        void S(int d) { stack += d; max = Math.Max(max, stack); }

        Ldc(c.AddString($"{cls}.{name}")); S(1);
        if (isStatic) Op(0x01); else Op(0x2a);
        S(1);
        Push(pars.Count); S(1);
        Op(0xbd); U2(c.AddClass(Obj));
        int slot = isStatic ? 0 : 1;
        for (int i = 0; i < pars.Count; i++)
        {
            string p = pars[i];
            Op(0x59); S(1);
            Push(i); S(1);
            var box = p switch
            {
                "I" or "S" or "B" => ("java/lang/Integer", "(I)V"),
                "F" => ("java/lang/Float", "(F)V"),
                "D" => ("java/lang/Double", "(D)V"),
                "J" => ("java/lang/Long", "(J)V"),
                "Z" => ("java/lang/Boolean", "(Z)V"),
                "C" => ("java/lang/Character", "(C)V"),
                _ => ((string, string)?)null,
            };
            int width = p is "J" or "D" ? 2 : 1;
            if (box is var (boxClass, ctor))
            {
                Op(0xbb); U2(c.AddClass(boxClass)); S(1);
                Op(0x59); S(1);
                Load(p[0], slot); S(width);
                Op(0xb7); U2(c.AddMethodref(boxClass, "<init>", ctor)); S(-1 - width);
            }
            else { Load(p[0], slot); S(1); }
            Op(0x53); S(-3);
            slot += width;
        }
        Op(0xb8); U2(c.AddMethodref(Bridge, "call", CallDesc)); S(-2);
        switch (ret)
        {
            case "V": Op(0x57, 0xb1); break;
            case "Z": Op(0x03); S(1); Op(0xb8); U2(c.AddMethodref(Bridge, "asBoolean", "(Ljava/lang/Object;Z)Z")); Op(0xac); break;
            case "I": Op(0x03); S(1); Op(0xb8); U2(c.AddMethodref(Bridge, "asInt", "(Ljava/lang/Object;I)I")); Op(0xac); break;
            case "F": Op(0x0b); S(1); Op(0xb8); U2(c.AddMethodref(Bridge, "asFloat", "(Ljava/lang/Object;F)F")); Op(0xae); break;
            case "Ljava/lang/String;":
                Ldc(c.AddString("")); S(1);
                Op(0xb8); U2(c.AddMethodref(Bridge, "asString", "(Ljava/lang/Object;Ljava/lang/String;)Ljava/lang/String;")); Op(0xb0); break;
            case "Ljava/lang/Object;": Op(0xb0); break;                                   // `(Object)call`: ECJ casts nothing
            case var r when r[0] is 'L' or '[':
                Op(0xc0); U2(c.AddClass(r[0] == 'L' ? r[1..^1] : r)); Op(0xb0); break;
            default:
                // `CodexBridge` would write `(short)EngineBridge.call(…)`, which no compiler takes: no shipped native returns one.
                throw new InvalidDataException($"{cls}.{name}{desc}: a native returning {ret} has no bridge");
        }
        return CodeAttribute(Math.Max(max, 1), slot, [.. code]);
    }

    static byte[] CodeAttribute(int maxStack, int maxLocals, byte[] code)
    {
        var a = new byte[2 + 2 + 4 + code.Length + 2 + 2];
        BinaryPrimitives.WriteUInt16BigEndian(a.AsSpan(0), (ushort)maxStack);
        BinaryPrimitives.WriteUInt16BigEndian(a.AsSpan(2), (ushort)maxLocals);
        BinaryPrimitives.WriteUInt32BigEndian(a.AsSpan(4), (uint)code.Length);
        code.CopyTo(a, 8);
        return a;                                                       // no exception table, no attributes
    }

    /// <summary>A method descriptor's parameter descriptors and its return descriptor.</summary>
    public static (List<string> Params, string Return) Parse(string desc)
    {
        var list = new List<string>();
        int i = 1;
        while (desc[i] != ')')
        {
            int j = i;
            while (desc[j] == '[') j++;
            if (desc[j] == 'L') j = desc.IndexOf(';', j);
            list.Add(desc[i..(j + 1)]);
            i = j + 1;
        }
        return (list, desc[(i + 1)..]);
    }

    /// <summary>
    /// `Codex`'s own constructor, as `CodexBridge.AddGuidHook` gives the source one: <c>guid = EngineBridge.pendingGuid;</c>
    /// at its end. The shipped `Codex` declares none, so its constructor is javac's default
    /// (<c>aload_0; invokespecial Object.&lt;init&gt;; return</c> — 1.1's javac writes no code for <c>guid = 0</c>), and
    /// the hook goes before the <c>return</c>; any other shape is refused rather than guessed at.
    /// </summary>
    static void AddGuidHook(ClassFile c, ushort codeName)
    {
        var ctor = c.Methods.SingleOrDefault(m => c.Utf8(m.Name) == "<init>" && c.Utf8(m.Descriptor) == "()V")
            ?? throw new InvalidDataException("Codex has no constructor () to hook");
        int at = ctor.Attributes.FindIndex(a => a.Name == codeName);
        var a = ctor.Attributes[at].Data;
        int len = (int)BinaryPrimitives.ReadUInt32BigEndian(a.AsSpan(4));
        var code = a.AsSpan(8, len);
        int exceptions = BinaryPrimitives.ReadUInt16BigEndian(a.AsSpan(8 + len));
        if (len != 5 || code[0] != 0x2a || code[1] != 0xb7 || code[4] != 0xb1 || exceptions != 0)
            throw new InvalidDataException("the Codex class's constructor is not javac's default one: the GUID hook needs reading again");
        ushort pending = c.AddFieldref(Bridge, "pendingGuid", "I"), guid = c.AddFieldref("Codex", "guid", "I");
        byte[] hooked = [.. code[..4], 0x2a, 0xb2, (byte)(pending >> 8), (byte)pending, 0xb5, (byte)(guid >> 8), (byte)guid, 0xb1];
        var rest = a.AsSpan(8 + len);                                   // the exception table (none) and the attributes, as they were
        var b = new byte[8 + hooked.Length + rest.Length];
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(0), (ushort)Math.Max(2, (int)BinaryPrimitives.ReadUInt16BigEndian(a)));
        a.AsSpan(2, 2).CopyTo(b.AsSpan(2));
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4), (uint)hooked.Length);
        hooked.CopyTo(b, 8);
        rest.CopyTo(b.AsSpan(8 + hooked.Length));
        ctor.Attributes[at] = (codeName, b);
    }
}
