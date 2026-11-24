// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Vitomir.Formats;

/// <summary>A file did not match the expected layout. The message names the file and the offset.</summary>
public sealed class NodFormatException(string message) : Exception(message);

/// <summary>Little-endian cursor over a byte array, strict about bounds.</summary>
public sealed class BinReader(byte[] data, string name = "<bytes>")
{
    static readonly Encoding Latin1 = Encoding.Latin1;

    public byte[] Data { get; } = data;
    public string Name { get; } = name;
    public int Pos { get; set; }
    public int Remaining => Data.Length - Pos;

    ReadOnlySpan<byte> Take(int n)
    {
        if (n < 0 || Pos + n > Data.Length)
            throw Error($"read past end (need {n}, have {Data.Length - Pos})");
        var s = new ReadOnlySpan<byte>(Data, Pos, n);
        Pos += n;
        return s;
    }

    public NodFormatException Error(string what) => new($"{Name}: {what} at 0x{Pos:x}");

    public byte U8() => Take(1)[0];
    public sbyte I8() => (sbyte)Take(1)[0];
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public short I16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public Vector2 Vec2() => new(F32(), F32());
    public Vector3 Vec3() => new(F32(), F32(), F32());

    public float[] Floats(int n)
    {
        var a = new float[n];
        for (int i = 0; i < n; i++) a[i] = F32();
        return a;
    }

    public ushort[] U16s(int n)
    {
        var a = new ushort[n];
        for (int i = 0; i < n; i++) a[i] = U16();
        return a;
    }

    public uint[] U32s(int n)
    {
        var a = new uint[n];
        for (int i = 0; i < n; i++) a[i] = U32();
        return a;
    }

    /// <summary>A copy of the next <paramref name="n"/> bytes.</summary>
    public byte[] Bytes(int n) => Take(n).ToArray();

    public void Skip(int n) => Take(n);

    /// <summary>Fixed-width, NUL-padded Latin-1 string of <paramref name="n"/> bytes.</summary>
    public string CStr(int n) => CString(Take(n));

    /// <summary>A u32 element count, checked against a sanity limit so corrupt data fails cleanly.</summary>
    public int Count32(int max = 50_000_000)
    {
        uint n = U32();
        if (n > (uint)max) throw Error($"count {n} exceeds {max}");
        return (int)n;
    }

    public void ExpectEof()
    {
        if (Pos != Data.Length)
            throw Error($"{Data.Length - Pos} trailing bytes");
    }

    /// <summary>Latin-1 text up to the first NUL.</summary>
    public static string CString(ReadOnlySpan<byte> raw)
    {
        int cut = raw.IndexOf((byte)0);
        return Latin1.GetString(cut < 0 ? raw : raw[..cut]);
    }
}
