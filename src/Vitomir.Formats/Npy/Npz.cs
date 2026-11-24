// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Numpy's .npy and .npz, for the models repository's fit data (<outfit>_<sex>_fit.npz).
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Vitomir.Formats.Npy;

/// <summary>
/// One numpy array as a <c>.npy</c> holds it: its type (<see cref="Descr"/>: <c>&lt;i4</c>, <c>&lt;f4</c>,
/// <c>|b1</c>, <c>&lt;U16</c> …), <see cref="Shape"/>, whether it is in Fortran order, and its bytes. The readers
/// answer the elements in the file's order (C order: the last index fastest, unless <see cref="FortranOrder"/>).
/// </summary>
public sealed partial class NpyArray
{
    /// <summary>The type as numpy writes it: byte order (<c>&lt;</c>, <c>&gt;</c>, <c>|</c>, <c>=</c>), kind and size.</summary>
    public required string Descr { get; init; }
    public required int[] Shape { get; init; }
    public bool FortranOrder { get; init; }
    /// <summary>The elements' bytes as stored.</summary>
    public required byte[] Data { get; init; }

    /// <summary>The kind: <c>i</c> signed, <c>u</c> unsigned, <c>f</c> float, <c>b</c> boolean, <c>U</c> text (UTF-32), <c>S</c> bytes.</summary>
    public char Kind => Descr.Length > 1 ? Descr[1] : '?';

    /// <summary>Bytes an element (for <c>U</c>, four a character).</summary>
    public int ItemSize => Kind == 'U' ? 4 * Size : Size;

    int Size => int.Parse(Descr.AsSpan(2), CultureInfo.InvariantCulture);

    bool BigEndian => Descr.Length > 0 && Descr[0] == '>';

    /// <summary>How many elements: the shape's product (1 for a scalar's empty shape).</summary>
    public long Count => Shape.Aggregate(1L, (a, b) => a * b);

    /// <summary>The elements as 32-bit integers (from any integer or boolean type; a value out of range throws).</summary>
    public int[] ToInt32() => ToInt64().Select(v => checked((int)v)).ToArray();

    /// <summary>The elements as 64-bit integers (from any integer or boolean type).</summary>
    public long[] ToInt64()
    {
        var r = new long[Count];
        int size = Size;
        if (Kind is not ('i' or 'u' or 'b') || size is not (1 or 2 or 4 or 8)) throw new InvalidDataException($"an array of {Descr}, not integers");
        for (int i = 0; i < r.Length; i++)
        {
            var b = Data.AsSpan(i * size, size);
            r[i] = (Kind, size) switch
            {
                ('b', _) or ('u', 1) => b[0],
                ('i', 1) => (sbyte)b[0],
                ('i', 2) => BigEndian ? BinaryPrimitives.ReadInt16BigEndian(b) : BinaryPrimitives.ReadInt16LittleEndian(b),
                ('u', 2) => BigEndian ? BinaryPrimitives.ReadUInt16BigEndian(b) : BinaryPrimitives.ReadUInt16LittleEndian(b),
                ('i', 4) => BigEndian ? BinaryPrimitives.ReadInt32BigEndian(b) : BinaryPrimitives.ReadInt32LittleEndian(b),
                ('u', 4) => BigEndian ? BinaryPrimitives.ReadUInt32BigEndian(b) : BinaryPrimitives.ReadUInt32LittleEndian(b),
                ('i', 8) => BigEndian ? BinaryPrimitives.ReadInt64BigEndian(b) : BinaryPrimitives.ReadInt64LittleEndian(b),
                _ => checked((long)(BigEndian ? BinaryPrimitives.ReadUInt64BigEndian(b) : BinaryPrimitives.ReadUInt64LittleEndian(b))),
            };
        }
        return r;
    }

    /// <summary>The elements as doubles (from any number type).</summary>
    public double[] ToDouble()
    {
        if (Kind is 'i' or 'u' or 'b') return ToInt64().Select(v => (double)v).ToArray();
        int size = Size;
        if (Kind != 'f' || size is not (2 or 4 or 8)) throw new InvalidDataException($"an array of {Descr}, not numbers");
        var r = new double[Count];
        for (int i = 0; i < r.Length; i++)
        {
            var b = Data.AsSpan(i * size, size);
            r[i] = size switch
            {
                2 => (double)(BigEndian ? BinaryPrimitives.ReadHalfBigEndian(b) : BinaryPrimitives.ReadHalfLittleEndian(b)),
                4 => BigEndian ? BinaryPrimitives.ReadSingleBigEndian(b) : BinaryPrimitives.ReadSingleLittleEndian(b),
                _ => BigEndian ? BinaryPrimitives.ReadDoubleBigEndian(b) : BinaryPrimitives.ReadDoubleLittleEndian(b),
            };
        }
        return r;
    }

    /// <summary>The elements as floats (from any number type; a double is rounded).</summary>
    public float[] ToSingle() => ToDouble().Select(v => (float)v).ToArray();

    /// <summary>The elements as text (<c>U</c>: UTF-32, trailing nulls dropped; <c>S</c>: bytes as Latin-1).</summary>
    public string[] ToStrings()
    {
        var r = new string[Count];
        int item = ItemSize;
        for (int i = 0; i < r.Length; i++)
        {
            var b = Data.AsSpan(i * item, item);
            string s = Kind switch
            {
                'U' => (BigEndian ? new UTF32Encoding(true, false) : new UTF32Encoding(false, false)).GetString(b),
                'S' => Encoding.Latin1.GetString(b),
                _ => throw new InvalidDataException($"an array of {Descr}, not text"),
            };
            r[i] = s.TrimEnd('\0');
        }
        return r;
    }

    /// <summary>Reads a <c>.npy</c> file's bytes (format versions 1, 2 and 3).</summary>
    public static NpyArray Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 10 || bytes[0] != 0x93 || !bytes.Slice(1, 5).SequenceEqual("NUMPY"u8)) throw new InvalidDataException("not a .npy (no magic)");
        int major = bytes[6];
        int headerLength, start;
        if (major == 1) { headerLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]); start = 10; }
        else if (major is 2 or 3) { headerLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]); start = 12; }
        else throw new InvalidDataException($".npy format version {major}");
        if (headerLength < 0 || start + headerLength > bytes.Length) throw new InvalidDataException("a .npy header past the end");
        string header = (major == 3 ? Encoding.UTF8 : Encoding.Latin1).GetString(bytes.Slice(start, headerLength));
        var descr = DescrPattern().Match(header);
        var fortran = FortranPattern().Match(header);
        var shape = ShapePattern().Match(header);
        if (!descr.Success || !shape.Success) throw new InvalidDataException($"a .npy header numpy did not write: {header.Trim()}");
        int[] dims = shape.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => int.Parse(d.TrimEnd('L'), CultureInfo.InvariantCulture)).ToArray();
        var array = new NpyArray
        {
            Descr = descr.Groups[1].Value,
            Shape = dims,
            FortranOrder = fortran.Success && fortran.Groups[1].Value == "True",
            Data = [],
        };
        if (array.Kind is not ('i' or 'u' or 'f' or 'b' or 'U' or 'S')) throw new InvalidDataException($"a .npy of {array.Descr}, which this reader does not read");
        long size = array.Count * array.ItemSize;
        int at = start + headerLength;
        if (at + size > bytes.Length) throw new InvalidDataException($"a .npy of {size} bytes in {bytes.Length - at}");
        return new NpyArray { Descr = array.Descr, Shape = dims, FortranOrder = array.FortranOrder, Data = bytes.Slice(at, (int)size).ToArray() };
    }

    [GeneratedRegex(@"['""]descr['""]\s*:\s*['""]([^'""]+)['""]")]
    private static partial Regex DescrPattern();

    [GeneratedRegex(@"['""]fortran_order['""]\s*:\s*(True|False)")]
    private static partial Regex FortranPattern();

    [GeneratedRegex(@"['""]shape['""]\s*:\s*\(([^)]*)\)")]
    private static partial Regex ShapePattern();
}

/// <summary>
/// A numpy <c>.npz</c>: a zip of <c>.npy</c> arrays, stored or deflated, each read when asked for by its name
/// (with or without <c>.npy</c>).
/// </summary>
public sealed class NpzArchive : IDisposable
{
    readonly ZipArchive zip;
    readonly Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);

    NpzArchive(ZipArchive zip)
    {
        this.zip = zip;
        foreach (var e in zip.Entries)
            entries[e.FullName.EndsWith(".npy", StringComparison.Ordinal) ? e.FullName[..^4] : e.FullName] = e;
    }

    /// <summary>Opens the file at <paramref name="path"/>.</summary>
    public static NpzArchive Open(string path) => new(new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read, leaveOpen: false));

    /// <summary>Opens an archive in <paramref name="stream"/> (kept open until this is disposed).</summary>
    public static NpzArchive Open(Stream stream) => new(new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false));

    /// <summary>The arrays' names, without <c>.npy</c>, in the archive's order.</summary>
    public IEnumerable<string> Names => zip.Entries.Select(e => e.FullName.EndsWith(".npy", StringComparison.Ordinal) ? e.FullName[..^4] : e.FullName);

    public bool Contains(string name) => entries.ContainsKey(Strip(name));

    /// <summary>The array named <paramref name="name"/>.</summary>
    public NpyArray Read(string name)
    {
        if (!entries.TryGetValue(Strip(name), out var entry)) throw new KeyNotFoundException($"no array {name} in the .npz");
        using var s = entry.Open();
        var bytes = new byte[entry.Length];
        s.ReadExactly(bytes);
        return NpyArray.Read(bytes);
    }

    static string Strip(string name) => name.EndsWith(".npy", StringComparison.Ordinal) ? name[..^4] : name;

    public void Dispose() => zip.Dispose();
}
