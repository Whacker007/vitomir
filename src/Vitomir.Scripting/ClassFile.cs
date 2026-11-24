// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A Java class file, read and written back: just enough of the format to add to a class's constant
// pool and give a method a body (probe-classes). Everything this does not change is written back as
// the bytes it read.
using System.Buffers.Binary;
using System.Text;

namespace Vitomir.Scripting;

/// <summary>
/// A class file (JVMS §4) as three raw parts around what we edit: the constant pool (entries kept as
/// their bytes, so an untouched pool writes back byte for byte), the fields (raw), and the methods
/// (access, name, descriptor and attributes, each attribute raw). New pool entries go on the end.
/// </summary>
public sealed class ClassFile
{
    public ushort Minor, Major;
    /// <summary>Entry 0 is unused, and so is the slot after a long or a double, as the format has it.</summary>
    readonly List<byte[]?> pool = [null];
    public ushort Access, This, Super;
    public byte[] Interfaces = [], Fields = [], Attributes = [];
    public ushort FieldCount, AttributeCount;
    public List<Method> Methods { get; } = [];

    public sealed class Method
    {
        public ushort Access, Name, Descriptor;
        public List<(ushort Name, byte[] Data)> Attributes { get; } = [];
    }

    public const ushort AccStatic = 0x0008, AccSynchronized = 0x0020, AccNative = 0x0100;

    public static ClassFile Read(byte[] b)
    {
        if (BinaryPrimitives.ReadUInt32BigEndian(b) != 0xCAFEBABE) throw new InvalidDataException("not a class file");
        var c = new ClassFile { Minor = U2(b, 4), Major = U2(b, 6) };
        int o = 10, n = U2(b, 8);
        for (int i = 1; i < n; i++)
        {
            int len = b[o] switch
            {
                1 => 3 + U2(b, o + 1),
                3 or 4 => 5,
                5 or 6 => 9,
                7 or 8 or 16 => 3,
                9 or 10 or 11 or 12 or 18 => 5,
                15 => 4,
                var t => throw new InvalidDataException($"constant pool tag {t} at {o}"),
            };
            c.pool.Add(b[o..(o + len)]);
            if (b[o] is 5 or 6) { c.pool.Add(null); i++; }
            o += len;
        }
        c.Access = U2(b, o); c.This = U2(b, o + 2); c.Super = U2(b, o + 4); o += 6;
        int ni = U2(b, o); c.Interfaces = b[o..(o + 2 + 2 * ni)]; o += 2 + 2 * ni;
        int fieldsAt = o; c.FieldCount = U2(b, o); o += 2;
        for (int f = 0; f < c.FieldCount; f++) { o += 6; o = SkipAttributes(b, o); }
        c.Fields = b[(fieldsAt + 2)..o];
        int mc = U2(b, o); o += 2;
        for (int m = 0; m < mc; m++)
        {
            var method = new Method { Access = U2(b, o), Name = U2(b, o + 2), Descriptor = U2(b, o + 4) };
            int ac = U2(b, o + 6); o += 8;
            for (int a = 0; a < ac; a++)
            {
                int len = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o + 2));
                method.Attributes.Add((U2(b, o), b[(o + 6)..(o + 6 + len)]));
                o += 6 + len;
            }
            c.Methods.Add(method);
        }
        c.AttributeCount = U2(b, o);
        c.Attributes = b[(o + 2)..];
        return c;
    }

    static int SkipAttributes(byte[] b, int o)
    {
        int ac = U2(b, o); o += 2;
        for (int a = 0; a < ac; a++) o += 6 + (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o + 2));
        return o;
    }

    static ushort U2(byte[] b, int o) => BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o));

    public byte[] Write()
    {
        var w = new MemoryStream();
        void P2(int v) { w.WriteByte((byte)(v >> 8)); w.WriteByte((byte)v); }
        void P4(int v) { P2(v >> 16); P2(v); }
        P4(unchecked((int)0xCAFEBABE)); P2(Minor); P2(Major);
        P2(pool.Count);
        foreach (var e in pool) if (e is not null) w.Write(e);
        P2(Access); P2(This); P2(Super);
        w.Write(Interfaces);
        P2(FieldCount); w.Write(Fields);
        P2(Methods.Count);
        foreach (var m in Methods)
        {
            P2(m.Access); P2(m.Name); P2(m.Descriptor); P2(m.Attributes.Count);
            foreach (var (name, data) in m.Attributes) { P2(name); P4(data.Length); w.Write(data); }
        }
        P2(AttributeCount); w.Write(Attributes);
        return w.ToArray();
    }

    // ---- the constant pool -----------------------------------------------------------------------

    public string Utf8(int i) => pool[i] is { } e && e[0] == 1 ? Encoding.UTF8.GetString(e, 3, e.Length - 3) : throw new InvalidDataException($"#{i} is not a Utf8");
    /// <summary>The internal name a Class entry names.</summary>
    public string ClassName(int i) => pool[i] is { } e && e[0] == 7 ? Utf8(U2(e, 1)) : throw new InvalidDataException($"#{i} is not a Class");
    public string Name => ClassName(This);
    public string SuperName => Super == 0 ? "" : ClassName(Super);
    public int PoolCount => pool.Count;

    /// <summary>The entry equal to <paramref name="entry"/>, or a new one at the end.</summary>
    ushort Find(byte[] entry)
    {
        for (int i = 1; i < pool.Count; i++) if (pool[i] is { } e && e.AsSpan().SequenceEqual(entry)) return (ushort)i;
        if (pool.Count >= 0xFFFF) throw new InvalidOperationException("the constant pool is full");
        pool.Add(entry);
        return (ushort)(pool.Count - 1);
    }

    static byte[] Ref(byte tag, int a, int b) => [tag, (byte)(a >> 8), (byte)a, (byte)(b >> 8), (byte)b];
    static byte[] Ref(byte tag, int a) => [tag, (byte)(a >> 8), (byte)a];

    /// <summary>Modified UTF-8 is plain UTF-8 for everything a class or member name holds here (no NUL, nothing outside the BMP).</summary>
    public ushort AddUtf8(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var e = new byte[3 + bytes.Length];
        e[0] = 1; BinaryPrimitives.WriteUInt16BigEndian(e.AsSpan(1), (ushort)bytes.Length); bytes.CopyTo(e, 3);
        return Find(e);
    }
    public ushort AddClass(string internalName) => Find(Ref(7, AddUtf8(internalName)));
    public ushort AddString(string s) => Find(Ref(8, AddUtf8(s)));
    ushort NameAndType(string name, string desc) => Find(Ref(12, AddUtf8(name), AddUtf8(desc)));
    public ushort AddMethodref(string owner, string name, string desc) => Find(Ref(10, AddClass(owner), NameAndType(name, desc)));
    public ushort AddFieldref(string owner, string name, string desc) => Find(Ref(9, AddClass(owner), NameAndType(name, desc)));

    /// <summary>A pool entry as text, the same whatever its index: what a comparison of two classes' code reads.</summary>
    public string Describe(int i)
    {
        var e = pool[i] ?? throw new InvalidDataException($"#{i} is unused");
        return e[0] switch
        {
            1 => "utf8 " + Utf8(i),
            3 => "int " + BinaryPrimitives.ReadInt32BigEndian(e.AsSpan(1)),
            4 => "float " + BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(e.AsSpan(1))).ToString("R"),
            5 => "long " + BinaryPrimitives.ReadInt64BigEndian(e.AsSpan(1)),
            6 => "double " + BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(e.AsSpan(1))).ToString("R"),
            7 => "class " + ClassName(i),
            8 => "string \"" + Utf8(U2(e, 1)) + "\"",
            9 or 10 or 11 => (e[0] == 9 ? "field " : "method ") + ClassName(U2(e, 1)) + "." + NameAndTypeText(U2(e, 3)),
            12 => NameAndTypeText(i),
            var t => $"tag {t}",
        };
    }

    string NameAndTypeText(int i) => Utf8(U2(pool[i]!, 1)) + ":" + Utf8(U2(pool[i]!, 3));

    /// <summary>The fields' names and descriptors, in the order the class file has them.</summary>
    public IEnumerable<(ushort Access, string Name, string Descriptor)> FieldList()
    {
        int o = 0;
        for (int f = 0; f < FieldCount; f++)
        {
            ushort acc = U2(Fields, o), name = U2(Fields, o + 2), desc = U2(Fields, o + 4);
            yield return (acc, Utf8(name), Utf8(desc));
            o = SkipAttributes(Fields, o + 6);
        }
    }
}
