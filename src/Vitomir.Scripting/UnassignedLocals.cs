// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The reads of an unassigned local that JDK 1.1's javac let through, found in a compiled class and given the
// source path's fix in compiled form (the compiled twin of Compat).
using System.Buffers.Binary;

namespace Vitomir.Scripting;

/// <summary>
/// **A local read before any store to it, on some path** (the probe's one-entry table made generic).
/// JDK 1.1's javac let a script read a local that a path leaves unassigned (`DisciplineEyesOfChaos.cast`: a <c>switch</c> with no
/// default), and 1.1's VM did not verify classes off the class path, so it ran. IKVM verifies every class it loads and refuses such
/// a class whole (<c>java.lang.VerifyError</c>). <see cref="Find"/> is the verifier's own question asked of the bytecode — for each
/// instruction, which locals are assigned on **every** path that reaches it (the parameters on entry; a store assigns; paths
/// merge by what both have; an exception handler gets what every instruction it covers has; a <c>jsr</c>'s subroutine what
/// every caller has, and its <c>ret</c> gives each caller back its own plus the subroutine's) — and answers each load,
/// <c>iinc</c> or <c>ret</c> of a local that is not. <see cref="Fix"/> gives each such local its default before the method's
/// first instruction (<c>aconst_null</c>, <c>iconst_0</c>, <c>fconst_0</c>, <c>lconst_0</c>, <c>dconst_0</c> and the store),
/// as <c>Compat</c> gives the source its <c>= null</c>: the one thing that changes is the read of garbage on that path, which now
/// reads the default.
/// <para>**Why the fix cannot break a class the verifier took.** A store before the first instruction only adds a type where
/// the verifier had none: at any merge the incoming state from the method's start had the slot unusable either way (nothing, or
/// a default of a type the later paths may not share), and a read of the slot on a path that stored it first sees that store. So
/// a method that verified still verifies, and one that read the slot unassigned now reads the default. The prologue is padded
/// with <c>nop</c> to a multiple of four bytes, so every <c>tableswitch</c> and <c>lookupswitch</c> keeps its padding and every
/// branch its relative offset; the exception table and the line and local-variable tables move by it (major 45 has no stack
/// maps). A slot read as two kinds, or a <c>ret</c> of one never stored, is not fixed (it is said instead): no default fits.</para>
/// </summary>
public static class UnassignedLocals
{
    /// <summary>One read of a local some path leaves unassigned: the method, the slot, what is read (<c>I J F D A</c>, or <c>R</c> for a <c>ret</c>'s address), and where.</summary>
    public sealed record Read(string Method, string Descriptor, int Slot, char Kind, int Pc);

    /// <summary>Every read of an unassigned local in <paramref name="c"/>'s methods, in method order then by pc.</summary>
    /// <exception cref="InvalidDataException">A method's code holds an instruction this does not know.</exception>
    public static List<Read> Find(ClassFile c)
    {
        var reads = new List<Read>();
        foreach (var m in c.Methods)
        {
            int at = m.Attributes.FindIndex(a => c.Utf8(a.Name) == "Code");
            if (at < 0) continue;                                            // abstract or native
            string name = c.Utf8(m.Name), desc = c.Utf8(m.Descriptor);
            foreach (var (slot, kind, pc) in Method(m.Attributes[at].Data, desc, (m.Access & ClassFile.AccStatic) != 0))
                reads.Add(new(name, desc, slot, kind, pc));
        }
        return reads;
    }

    /// <summary>
    /// <paramref name="c"/> with each local <see cref="Find"/> answered given its default at its method's start, and a 1.1 compat note
    /// in <paramref name="notes"/> for each (or for each that cannot be fixed). False when nothing was read unassigned.
    /// </summary>
    public static bool Fix(ClassFile c, List<string> notes)
    {
        var reads = Find(c);
        if (reads.Count == 0) return false;
        string cls = c.Name;
        bool changed = false;
        foreach (var method in reads.GroupBy(r => (r.Method, r.Descriptor)))
        {
            var m = c.Methods.Single(x => c.Utf8(x.Name) == method.Key.Method && c.Utf8(x.Descriptor) == method.Key.Descriptor);
            var slots = new List<(int Slot, char Kind)>();
            foreach (var bySlot in method.GroupBy(r => r.Slot).OrderBy(g => g.Key))
            {
                var kinds = bySlot.Select(r => r.Kind).Distinct().ToList();
                if (kinds.Count != 1 || kinds[0] == 'R')
                {
                    notes.Add($"left as it is: {cls}.{method.Key.Method}{method.Key.Descriptor} reads local {bySlot.Key} unassigned as "
                              + string.Join(" and ", kinds.Select(KindName)) + ", which no one default fits (IKVM will refuse the class)");
                    continue;
                }
                slots.Add((bySlot.Key, kinds[0]));
                notes.Add($"initialised local {bySlot.Key} ({KindName(kinds[0])}) to {DefaultName(kinds[0])} in {cls}.{method.Key.Method}{method.Key.Descriptor}, "
                          + $"which 1.1's javac let it read unassigned (at pc {string.Join(", ", bySlot.Select(r => r.Pc))}): the compiled form of the source path's fix");
            }
            if (slots.Count == 0) continue;
            int at = m.Attributes.FindIndex(a => c.Utf8(a.Name) == "Code");
            m.Attributes[at] = (m.Attributes[at].Name, Prepend(c, m.Attributes[at].Data, Prologue(slots, out int stack), stack));
            changed = true;
        }
        return changed;
    }

    static string KindName(char k) => k switch { 'A' => "a reference", 'I' => "an int", 'F' => "a float", 'J' => "a long", 'D' => "a double", _ => "a return address" };
    static string DefaultName(char k) => k switch { 'A' => "null", 'F' => "0.0f", 'J' => "0L", 'D' => "0.0", _ => "0" };

    /// <summary>The stores of the defaults, padded with <c>nop</c> to a multiple of four bytes; <paramref name="stack"/> is the stack they need.</summary>
    public static byte[] Prologue(IEnumerable<(int Slot, char Kind)> slots, out int stack)
    {
        var b = new List<byte>();
        stack = 0;
        foreach (var (slot, kind) in slots)
        {
            // aconst_null 01 / iconst_0 03 / fconst_0 0b / lconst_0 09 / dconst_0 0e, then astore 3a / istore 36 / fstore 38 / lstore 37 / dstore 39.
            (byte push, byte store, int width) = kind switch
            {
                'A' => ((byte)0x01, (byte)0x3a, 1), 'I' => ((byte)0x03, (byte)0x36, 1), 'F' => ((byte)0x0b, (byte)0x38, 1),
                'J' => ((byte)0x09, (byte)0x37, 2), 'D' => ((byte)0x0e, (byte)0x39, 2),
                _ => throw new ArgumentException($"no default for a local of kind {kind}", nameof(slots)),
            };
            stack = Math.Max(stack, width);
            b.Add(push);
            if (slot <= 255) { b.Add(store); b.Add((byte)slot); }
            else { b.Add(0xc4); b.Add(store); b.Add((byte)(slot >> 8)); b.Add((byte)slot); }
        }
        while (b.Count % 4 != 0) b.Add(0x00);
        return [.. b];
    }

    /// <summary>
    /// A Code attribute with <paramref name="prologue"/> (a multiple of four bytes) in front of its code: the exception table and the line
    /// and local-variable tables moved by it, <c>max_stack</c> at least <paramref name="stack"/>, every other byte kept.
    /// </summary>
    public static byte[] Prepend(ClassFile c, byte[] a, byte[] prologue, int stack)
    {
        int shift = prologue.Length;
        if (shift % 4 != 0) throw new ArgumentException("a prologue must keep the switches' padding (a multiple of four bytes)", nameof(prologue));
        int len = (int)BinaryPrimitives.ReadUInt32BigEndian(a.AsSpan(4));
        var b = new byte[a.Length + shift];
        a.AsSpan(0, 4).CopyTo(b);
        if (BinaryPrimitives.ReadUInt16BigEndian(b) < stack) BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)stack);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4), (uint)(len + shift));
        prologue.CopyTo(b, 8);
        a.AsSpan(8, len).CopyTo(b.AsSpan(8 + shift));
        int o = 8 + len, n = 8 + len + shift;
        a.AsSpan(o).CopyTo(b.AsSpan(n));
        int exceptions = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(n));
        for (int e = 0; e < exceptions; e++)
            for (int k = 0; k < 3; k++) Add(b, n + 2 + 8 * e + 2 * k, shift);      // start, end, handler; the type stays
        int p = n + 2 + 8 * exceptions, attrs = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(p));
        p += 2;
        for (int i = 0; i < attrs; i++)
        {
            string name = c.Utf8(BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(p)));
            int alen = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p + 2));
            int q = p + 6, count = alen >= 2 ? BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(q)) : 0;
            if (name == "LineNumberTable") for (int r = 0; r < count; r++) Add(b, q + 2 + 4 * r, shift);
            // A local's range starts later; 1.1's javac writes some with start 65535 (ARC3_Erik), left as they are.
            if (name == "LocalVariableTable")
                for (int r = 0; r < count; r++) if (BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(q + 2 + 10 * r)) != 0xFFFF) Add(b, q + 2 + 10 * r, shift);
            p += 6 + alen;
        }
        return b;
    }

    static void Add(byte[] b, int o, int shift) =>
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(o), (ushort)(BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o)) + shift));

    // ---- the analysis -------------------------------------------------------------------------------------------------------

    /// <summary>The unassigned reads of one method's Code attribute.</summary>
    static IEnumerable<(int Slot, char Kind, int Pc)> Method(byte[] a, string desc, bool isStatic)
    {
        int maxLocals = BinaryPrimitives.ReadUInt16BigEndian(a.AsSpan(2));
        int len = (int)BinaryPrimitives.ReadUInt32BigEndian(a.AsSpan(4));
        var code = a.AsSpan(8, len).ToArray();
        var insts = Decode(code);
        var index = new Dictionary<int, int>(insts.Count);
        for (int i = 0; i < insts.Count; i++) index[insts[i].Pc] = i;

        // The handlers: [start, end) → handler.
        int et = 8 + len, exceptions = BinaryPrimitives.ReadUInt16BigEndian(a.AsSpan(et));
        var handlers = new List<(int Start, int End, int Handler)>();
        for (int e = 0; e < exceptions; e++)
        {
            int o = et + 2 + 8 * e;
            handlers.Add((BinaryPrimitives.ReadUInt16BigEndian(a.AsSpan(o)), BinaryPrimitives.ReadUInt16BigEndian(a.AsSpan(o + 2)),
                          BinaryPrimitives.ReadUInt16BigEndian(a.AsSpan(o + 4))));
        }

        // On entry: `this` and the parameters.
        var entry = new bool[maxLocals];
        int slot = 0;
        if (!isStatic) Set(entry, slot++);
        foreach (var p in CodexClasses.Parse(desc).Params)
        {
            Set(entry, slot);
            if (p is "J" or "D") Set(entry, slot + 1);
            slot += p is "J" or "D" ? 2 : 1;
        }

        var state = new bool[]?[insts.Count];
        // Each jsr's return point and the jsr itself, for the rets.
        var returns = insts.Where(i => i.Op is 0xa8 or 0xc9).Select(i => (Jsr: index[i.Pc], Back: index[i.Pc + i.Length])).ToList();
        Merge(state, 0, entry);
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < insts.Count; i++)
            {
                if (state[i] is not { } s) continue;
                var inst = insts[i];
                var after = (bool[])s.Clone();
                foreach (var (sl, _, store) in Locals(code, inst)) if (store) { Set(after, sl.Slot); if (sl.Wide) Set(after, sl.Slot + 1); }
                foreach (var (start, end, handler) in handlers)
                    if (inst.Pc >= start && inst.Pc < end) changed |= Merge(state, index[handler], s);
                if (inst.Op == 0xa9 || inst.Op == 0xc4 && code[inst.Pc + 1] == 0xa9)
                {
                    foreach (var (jsr, back) in returns)
                        if (state[jsr] is { } caller) changed |= Merge(state, back, Union(after, caller));
                    continue;
                }
                foreach (var target in Targets(code, inst))
                {
                    if (!index.TryGetValue(target, out int t)) throw new InvalidDataException($"a branch to {target}, which is no instruction's start");
                    changed |= Merge(state, t, after);
                }
            }
        }

        for (int i = 0; i < insts.Count; i++)
        {
            if (state[i] is not { } s) continue;                            // never reached
            foreach (var (sl, kind, store) in Locals(code, insts[i]))
                if (!store && (sl.Slot >= s.Length || !s[sl.Slot])) yield return (sl.Slot, kind, insts[i].Pc);
        }
    }

    readonly record struct Inst(int Pc, int Op, int Length);

    static void Set(bool[] s, int i) { if (i < s.Length) s[i] = true; }

    static bool[] Union(bool[] a, bool[] b)
    {
        var u = (bool[])a.Clone();
        for (int i = 0; i < u.Length; i++) u[i] |= b[i];
        return u;
    }

    /// <summary>What both have: the state at <paramref name="at"/> merged with <paramref name="incoming"/>. True when it changed.</summary>
    static bool Merge(bool[]?[] state, int at, bool[] incoming)
    {
        if (state[at] is not { } have) { state[at] = (bool[])incoming.Clone(); return true; }
        bool changed = false;
        for (int i = 0; i < have.Length; i++) if (have[i] && !incoming[i]) { have[i] = false; changed = true; }
        return changed;
    }

    /// <summary>A method's code as its instructions' opcodes, each with its pc.</summary>
    public static IEnumerable<(int Pc, int Op)> Instructions(byte[] code) => Decode(code).Select(i => (i.Pc, i.Op));

    /// <summary>The method's instructions in order (the code is a run of them, a switch's padding and table inside its own).</summary>
    static List<Inst> Decode(byte[] code)
    {
        var list = new List<Inst>();
        for (int pc = 0; pc < code.Length;)
        {
            int op = code[pc], n = Length(code, pc);
            list.Add(new(pc, op, n));
            pc += n;
        }
        return list;
    }

    static int S4(byte[] c, int o) => BinaryPrimitives.ReadInt32BigEndian(c.AsSpan(o));
    static int S2(byte[] c, int o) => BinaryPrimitives.ReadInt16BigEndian(c.AsSpan(o));
    static int U2(byte[] c, int o) => BinaryPrimitives.ReadUInt16BigEndian(c.AsSpan(o));

    /// <summary>An instruction's length in bytes (JVMS §6.5).</summary>
    static int Length(byte[] c, int pc)
    {
        int op = c[pc];
        switch (op)
        {
            case 0xaa:                                                       // tableswitch
                {
                    int p = (pc + 4) & ~3, lo = S4(c, p + 4), hi = S4(c, p + 8);
                    return p - pc + 12 + 4 * (hi - lo + 1);
                }
            case 0xab:                                                       // lookupswitch
                {
                    int p = (pc + 4) & ~3, pairs = S4(c, p + 4);
                    return p - pc + 8 + 8 * pairs;
                }
            case 0xc4: return c[pc + 1] == 0x84 ? 6 : 4;                     // wide
        }
        return op switch
        {
            <= 0x0f => 1,
            0x10 => 2, 0x11 => 3, 0x12 => 2, 0x13 or 0x14 => 3,
            >= 0x15 and <= 0x19 => 2, >= 0x1a and <= 0x35 => 1,
            >= 0x36 and <= 0x3a => 2, >= 0x3b and <= 0x83 => 1,
            0x84 => 3, >= 0x85 and <= 0x98 => 1,
            >= 0x99 and <= 0xa8 => 3, 0xa9 => 2,
            >= 0xac and <= 0xb1 => 1, >= 0xb2 and <= 0xb8 => 3, 0xb9 or 0xba => 5,
            0xbb => 3, 0xbc => 2, 0xbd => 3, 0xbe or 0xbf => 1, 0xc0 or 0xc1 => 3, 0xc2 or 0xc3 => 1,
            0xc5 => 4, 0xc6 or 0xc7 => 3, 0xc8 or 0xc9 => 5,
            _ => throw new InvalidDataException($"opcode 0x{op:x2} at {pc}: not an instruction"),
        };
    }

    /// <summary>Where an instruction goes next (a <c>ret</c>'s are the jsrs' return points, handled by the caller).</summary>
    static IEnumerable<int> Targets(byte[] c, Inst i)
    {
        int pc = i.Pc;
        switch (i.Op)
        {
            case >= 0x99 and <= 0xa6 or 0xc6 or 0xc7: yield return pc + i.Length; yield return pc + S2(c, pc + 1); break;
            case 0xa7 or 0xa8: yield return pc + S2(c, pc + 1); break;      // goto; jsr (its return point is the ret's)
            case 0xc8 or 0xc9: yield return pc + S4(c, pc + 1); break;      // goto_w; jsr_w
            case 0xaa:
                {
                    int p = (pc + 4) & ~3, lo = S4(c, p + 4), hi = S4(c, p + 8);
                    yield return pc + S4(c, p);
                    for (int k = 0; k <= hi - lo; k++) yield return pc + S4(c, p + 12 + 4 * k);
                    break;
                }
            case 0xab:
                {
                    int p = (pc + 4) & ~3, pairs = S4(c, p + 4);
                    yield return pc + S4(c, p);
                    for (int k = 0; k < pairs; k++) yield return pc + S4(c, p + 12 + 8 * k);
                    break;
                }
            case >= 0xac and <= 0xb1 or 0xbf: break;                         // the returns, athrow
            default: yield return pc + i.Length; break;
        }
    }

    /// <summary>The locals an instruction reads or stores: the slot (and whether it takes two), its kind, and whether it is a store.</summary>
    static IEnumerable<((int Slot, bool Wide) Slot, char Kind, bool Store)> Locals(byte[] c, Inst i)
    {
        int op = i.Op, idx;
        bool wide = op == 0xc4;
        if (wide) { op = c[i.Pc + 1]; idx = U2(c, i.Pc + 2); }
        else idx = op is (>= 0x15 and <= 0x19) or (>= 0x36 and <= 0x3a) or 0x84 or 0xa9 ? c[i.Pc + 1] : -1;
        const string Kinds = "IJFDA";
        switch (op)
        {
            case >= 0x15 and <= 0x19: { char k = Kinds[op - 0x15]; yield return ((idx, k is 'J' or 'D'), k, false); break; }
            case >= 0x1a and <= 0x2d: { char k = Kinds[(op - 0x1a) / 4]; yield return (((op - 0x1a) % 4, k is 'J' or 'D'), k, false); break; }
            case >= 0x36 and <= 0x3a: { char k = Kinds[op - 0x36]; yield return ((idx, k is 'J' or 'D'), k, true); break; }
            case >= 0x3b and <= 0x4e: { char k = Kinds[(op - 0x3b) / 4]; yield return (((op - 0x3b) % 4, k is 'J' or 'D'), k, true); break; }
            case 0x84: yield return ((idx, false), 'I', false); yield return ((idx, false), 'I', true); break;   // iinc: read, then store
            case 0xa9: yield return ((idx, false), 'R', false); break;                                        // ret
        }
    }
}
