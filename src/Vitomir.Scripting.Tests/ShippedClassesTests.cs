// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Buffers.Binary;
using Vitomir.Engine.Vfs;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **The shipped classes, bridged** (probe-classes; the game's): `Codex.nob`'s 465 classes with the 447 natives of its
/// `Codex*` classes given bodies by <see cref="CodexClasses"/>, against the source path's bridge (`CodexBridge`, compiled by ECJ);
/// the reads of an unassigned local the verifier would refuse (<see cref="UnassignedLocals"/>). The comparisons need an install with
/// both `Codex.nob` and the SDK's `Codex\` sources.
/// </summary>
public class ShippedClassesTests
{
    static string? Install(out string why)
    {
        why = "";
        var dir = GameFiles.Locate();
        if (dir is null) { why = "no game install (set VTMR_GAME_DIR to the folder holding Resource.nob)"; return null; }
        if (!File.Exists(Path.Combine(dir, "Codex.nob"))) { why = "the install has no Codex.nob"; return null; }
        if (!Directory.Exists(Path.Combine(dir, "Codex"))) { why = "the install has no Codex sources to compare with"; return null; }
        return dir;
    }

    [Fact]
    public void Every_native_forwards_exactly_as_the_source_bridge_compiles()
    {
        var dir = Install(out var why);
        Assert.SkipWhen(dir is null, why);
        string cache = Path.Combine(Path.GetTempPath(), "vitomir-codex-shipped-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fromSources = new CodexCompiler(Path.Combine(dir!, "Codex"), Path.Combine(dir!, "classes.zip"), cache, JavaLevel.Legacy).Build();
            var fromClasses = CodexCompiler.FromClasses(Path.Combine(dir!, "Codex.nob"), Path.Combine(dir!, "Codex"), Path.Combine(dir!, "classes.zip"), cache, JavaLevel.Legacy).Build();
            Assert.Equal(447, fromClasses.Natives);
            Assert.Equal(fromSources.Natives, fromClasses.Natives);
            var shipped = CodexClasses.Read(Path.Combine(dir!, "Codex.nob"));
            int compared = 0;
            var differ = new List<string>();
            foreach (var (name, bytes) in shipped.Where(kv => CodexClasses.Bridged(kv.Key)))
            {
                var orig = ClassFile.Read(bytes);
                var mine = ClassFile.Read(File.ReadAllBytes(Path.Combine(fromClasses.BaseDir!, name + ".class")));
                var ecj = ClassFile.Read(File.ReadAllBytes(Path.Combine(fromSources.OutputDir, name + ".class")));
                foreach (var m in orig.Methods.Where(m => (m.Access & ClassFile.AccNative) != 0))
                {
                    string n = orig.Utf8(m.Name), d = orig.Utf8(m.Descriptor);
                    var a = Find(mine, n, d); var b = Find(ecj, n, d);
                    if (b is null) { differ.Add($"{name}.{n}{d}: not in the source bridge"); continue; }
                    if (a!.Access != b.Access) differ.Add($"{name}.{n}{d}: access {a.Access:x} against {b.Access:x}");
                    var (ta, la) = Code(mine, a); var (tb, lb) = Code(ecj, b);
                    if (la != lb) differ.Add($"{name}.{n}{d}: max_locals {la} against {lb}");
                    if (!ta.SequenceEqual(tb)) differ.Add($"{name}.{n}{d}:\n  mine {string.Join("; ", ta)}\n  ecj  {string.Join("; ", tb)}");
                    compared++;
                }
            }
            Assert.Equal(447, compared);
            Assert.True(differ.Count == 0, string.Join("\n", differ.Take(10)));

            // The GUID hook: `guid = EngineBridge.pendingGuid` the last thing `Codex()` does, in both.
            var hook = ClassFile.Read(File.ReadAllBytes(Path.Combine(fromClasses.BaseDir!, "Codex.class")));
            var (ctor, _) = Code(hook, Find(hook, "<init>", "()V")!);
            Assert.Equal(["loada 0", "invokespecial method java/lang/Object.<init>:()V", "loada 0",
                          "getstatic field EngineBridge.pendingGuid:I", "putfield field Codex.guid:I", "return"], ctor);
            var src = ClassFile.Read(File.ReadAllBytes(Path.Combine(fromSources.OutputDir, "Codex.class")));
            var (srcCtor, _) = Code(src, Find(src, "<init>", "()V")!);
            Assert.Equal(ctor[^4..], srcCtor[^4..]);
        }
        finally { try { Directory.Delete(cache, true); } catch (IOException) { } }
    }

    /// <summary>
    /// Every shipped class, loaded by IKVM's runtime as it is (major 45) and linked — its methods listed, which is
    /// where IKVM verifies and compiles a class — with the ones it refuses written down.
    /// </summary>
    [Fact]
    public void Every_shipped_class_loads_but_the_ones_written_down()
    {
        var dir = GameFiles.Locate();
        Assert.SkipWhen(dir is null || !File.Exists(Path.Combine(dir, "Codex.nob")), "no install with a Codex.nob");
        string cache = Path.Combine(Path.GetTempPath(), "vitomir-codex-shipped-" + Guid.NewGuid().ToString("N"));
        try
        {
            var rt = ScriptRuntime.Start(CodexCompiler.FromClasses(Path.Combine(dir!, "Codex.nob"), Path.Combine(dir!, "Codex"), Path.Combine(dir!, "classes.zip"), cache), new CodexHost());
            var refused = new List<string>();
            int loaded = 0;
            foreach (var name in rt.ClassNames())
            {
                try
                {
                    var c = rt.ClassOf(name) ?? throw new InvalidOperationException("not found");
                    c.getDeclaredMethods(); c.getDeclaredFields(); c.getDeclaredConstructors();
                    loaded++;
                }
                catch (Exception e) { refused.Add($"{name}: {(e is java.lang.Throwable t ? t.getClass().getName() + " " + t.getMessage() : e.GetType().Name + " " + e.Message)}"); }
            }
            File.WriteAllLines(Path.Combine(Path.GetTempPath(), "vitomir-shipped-refused.txt"), [$"loaded {loaded}", .. refused]);
            // IKVM takes major 45 as it is, and the bad local-variable ranges 1.1's javac wrote (ARC3_Erik's start 65535,
            // which HotSpot refuses); the one class it refused, DisciplineEyesOfChaos, is taken once its local is initialised.
            Assert.Empty(refused);
            Assert.Equal(466, loaded);
            // The SDK's own sources in the install's Codex folder are ignored: nothing in the overlay.
            Assert.Empty(rt.Build.Overlay!);
            Assert.Empty(Directory.GetFiles(rt.Build.OutputDir, "*.class"));
        }
        finally { try { Directory.Delete(cache, true); } catch (IOException) { } }
    }

    /// <summary>
    /// **The verifier's question, asked of every shipped class** (the verifier's): in 1.1's 464 the one local read
    /// unassigned on some path is `DisciplineEyesOfChaos.cast`'s slot 6 (<c>strType</c>, a reference), the case the probe's table
    /// named; its fix is the table's four bytes, <c>aconst_null; astore 6; nop</c>, in front of the method's code as it was, and
    /// the class then verifies (the test above). And **no false alarm**: ECJ's compile of the SDK's 465 sources (at the legacy
    /// level), which every verifier takes, has none; nor has JDK 1.1's own library (<c>classes.zip</c>), whose <c>finally</c> and
    /// <c>synchronized</c> blocks are <c>jsr</c>/<c>ret</c> subroutines.
    /// </summary>
    [Fact]
    public void The_one_local_read_unassigned_is_found_and_given_its_default()
    {
        var dir = Install(out var why);
        Assert.SkipWhen(dir is null, why);
        var shipped = CodexClasses.Read(Path.Combine(dir!, "Codex.nob"));
        var found = shipped.SelectMany(kv => UnassignedLocals.Find(ClassFile.Read(kv.Value)).Select(r => (Class: kv.Key, r.Method, r.Descriptor, r.Slot, r.Kind))).ToList();
        Assert.Equal([("DisciplineEyesOfChaos", "cast", "(III)I", 6, 'A')], found.Distinct().ToList());
        // And JDK 1.1's own library (classes.zip, compiled by the same javac), whose finally and synchronized blocks are jsr/ret
        // subroutines: every class verifies, so any answer would be a false alarm.
        var library = CodexClasses.ReadAll(Path.Combine(dir!, "classes.zip"));
        Assert.True(library.Count > 500);
        Assert.Contains(library, kv => HasJsr(ClassFile.Read(kv.Value)));
        Assert.Empty(library.SelectMany(kv => UnassignedLocals.Find(ClassFile.Read(kv.Value)).Select(r => $"{kv.Key} {r}")));

        var notes = new List<string>();
        var fixedBytes = CodexClasses.Rewrite("DisciplineEyesOfChaos", shipped["DisciplineEyesOfChaos"], notes, out _);
        var note = Assert.Single(notes);
        Assert.StartsWith("initialised local 6 (a reference) to null in DisciplineEyesOfChaos.cast(III)I", note);
        var before = ClassFile.Read(shipped["DisciplineEyesOfChaos"]);
        var after = ClassFile.Read(fixedBytes);
        var (b, a) = (CodeBytes(before, Find(before, "cast", "(III)I")!), CodeBytes(after, Find(after, "cast", "(III)I")!));
        Assert.Equal([0x01, 0x3a, 0x06, 0x00], a[..4]);
        Assert.Equal(b, a[4..]);
        Assert.Empty(UnassignedLocals.Find(after));
        // Every other method of it, and every other class, as it was.
        foreach (var m in before.Methods.Where(m => before.Utf8(m.Name) != "cast"))
            Assert.Equal(CodeBytes(before, m), CodeBytes(after, Find(after, before.Utf8(m.Name), before.Utf8(m.Descriptor))!));
        Assert.All(shipped.Where(kv => kv.Key != "DisciplineEyesOfChaos" && !CodexClasses.Bridged(kv.Key)),
                   kv => Assert.Equal(kv.Value, CodexClasses.Rewrite(kv.Key, kv.Value, notes, out _)));

        string cache = Path.Combine(Path.GetTempPath(), "vitomir-codex-shipped-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ecj = new CodexCompiler(Path.Combine(dir!, "Codex"), Path.Combine(dir!, "classes.zip"), cache, JavaLevel.Legacy).Build();
            var alarms = Directory.GetFiles(ecj.OutputDir, "*.class")
                .SelectMany(f => UnassignedLocals.Find(ClassFile.Read(File.ReadAllBytes(f))).Select(r => $"{Path.GetFileName(f)} {r}")).ToList();
            Assert.True(Directory.GetFiles(ecj.OutputDir, "*.class").Length > 460);
            Assert.Empty(alarms);
        }
        finally { try { Directory.Delete(cache, true); } catch (IOException) { } }
    }

    /// <summary>The prologue for each kind of local: the default's push and the store, padded with <c>nop</c> to four bytes.</summary>
    [Fact]
    public void The_defaults_go_in_front_in_whole_words()
    {
        Assert.Equal([0x01, 0x3a, 0x06, 0x00], UnassignedLocals.Prologue([(6, 'A')], out int s1));
        Assert.Equal(1, s1);
        Assert.Equal([0x03, 0x36, 0x01, 0x0b, 0x38, 0x02, 0x00, 0x00], UnassignedLocals.Prologue([(1, 'I'), (2, 'F')], out _));
        Assert.Equal([0x09, 0x37, 0x04, 0x00], UnassignedLocals.Prologue([(4, 'J')], out int s2));
        Assert.Equal(2, s2);
        Assert.Equal([0x01, 0xc4, 0x3a, 0x01, 0x2c, 0x00, 0x00, 0x00], UnassignedLocals.Prologue([(300, 'A')], out _));
    }

    static byte[] CodeBytes(ClassFile c, ClassFile.Method m)
    {
        var a = m.Attributes.Single(x => c.Utf8(x.Name) == "Code").Data;
        return a.AsSpan(8, (int)BinaryPrimitives.ReadUInt32BigEndian(a.AsSpan(4))).ToArray();
    }

    /// <summary>Whether a class's code holds a <c>jsr</c> (0xa8), so the analysis met one.</summary>
    static bool HasJsr(ClassFile c) =>
        c.Methods.Any(m => m.Attributes.Any(a => c.Utf8(a.Name) == "Code") && UnassignedLocals.Instructions(CodeBytes(c, m)).Any(i => i.Op is 0xa8 or 0xc9));

    static ClassFile.Method? Find(ClassFile c, string name, string desc) =>
        c.Methods.SingleOrDefault(m => c.Utf8(m.Name) == name && c.Utf8(m.Descriptor) == desc);

    /// <summary>A method's code as text, each operand named by what it means, and its max_locals.</summary>
    static (List<string> Ops, int Locals) Code(ClassFile c, ClassFile.Method m)
    {
        var a = m.Attributes.Single(x => c.Utf8(x.Name) == "Code").Data;
        int locals = BinaryPrimitives.ReadUInt16BigEndian(a.AsSpan(2));
        int len = (int)BinaryPrimitives.ReadUInt32BigEndian(a.AsSpan(4));
        var code = a.AsSpan(8, len).ToArray();
        var ops = new List<string>();
        for (int pc = 0; pc < code.Length;)
        {
            int op = code[pc];
            int Cp(int at) => BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(at));
            switch (op)
            {
                case 0x01: ops.Add("aconst_null"); pc++; break;
                case >= 0x02 and <= 0x08: ops.Add("iconst " + (op - 3)); pc++; break;
                case 0x0b: ops.Add("fconst 0"); pc++; break;
                case 0x10: ops.Add("iconst " + (sbyte)code[pc + 1]); pc += 2; break;
                case 0x11: ops.Add("iconst " + (short)Cp(pc + 1)); pc += 3; break;
                case 0x12: ops.Add("ldc " + c.Describe(code[pc + 1])); pc += 2; break;
                case 0x13: ops.Add("ldc " + c.Describe(Cp(pc + 1))); pc += 3; break;
                case >= 0x15 and <= 0x19: ops.Add("load" + "ilfda"[op - 0x15] + " " + code[pc + 1]); pc += 2; break;
                case >= 0x1a and <= 0x2d: ops.Add("load" + "ilfda"[(op - 0x1a) / 4] + " " + (op - 0x1a) % 4); pc++; break;
                case 0x53: ops.Add("aastore"); pc++; break;
                case 0x57: ops.Add("pop"); pc++; break;
                case 0x59: ops.Add("dup"); pc++; break;
                case 0xac: ops.Add("ireturn"); pc++; break;
                case 0xae: ops.Add("freturn"); pc++; break;
                case 0xb0: ops.Add("areturn"); pc++; break;
                case 0xb1: ops.Add("return"); pc++; break;
                case 0xb2: ops.Add("getstatic " + c.Describe(Cp(pc + 1))); pc += 3; break;
                case 0xb5: ops.Add("putfield " + c.Describe(Cp(pc + 1))); pc += 3; break;
                case 0xb7: ops.Add("invokespecial " + c.Describe(Cp(pc + 1))); pc += 3; break;
                case 0xb8: ops.Add("invokestatic " + c.Describe(Cp(pc + 1))); pc += 3; break;
                case 0xbb: ops.Add("new " + c.Describe(Cp(pc + 1))); pc += 3; break;
                case 0xbd: ops.Add("anewarray " + c.Describe(Cp(pc + 1))); pc += 3; break;
                case 0xc0: ops.Add("checkcast " + c.Describe(Cp(pc + 1))); pc += 3; break;
                default: ops.Add($"op {op:x2}?"); pc = code.Length; break;     // nothing a bridged native holds: the lists then differ
            }
        }
        return (ops, locals);
    }
}
