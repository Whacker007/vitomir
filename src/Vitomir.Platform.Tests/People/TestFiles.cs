// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace Vitomir.Platform.Tests.People;

/// <summary>A <c>.glb</c> written in a test: buffer views and accessors into one binary chunk, the rest JSON as given.</summary>
internal sealed class GlbWriter
{
    readonly MemoryStream bin = new();
    readonly JsonArray views = [], accessors = [];

    public JsonObject Root { get; } = new() { ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "Vitomir tests" } };

    /// <summary>A buffer view of <paramref name="data"/>, four-byte aligned; <paramref name="stride"/> 0 for none.</summary>
    public int View(byte[] data, int stride = 0)
    {
        while (bin.Length % 4 != 0) bin.WriteByte(0);
        var view = new JsonObject { ["buffer"] = 0, ["byteOffset"] = bin.Length, ["byteLength"] = data.Length };
        if (stride > 0) view["byteStride"] = stride;
        bin.Write(data);
        views.Add(view);
        return views.Count - 1;
    }

    public int Accessor(int view, int componentType, string type, int count, bool normalized = false, int byteOffset = 0, JsonObject? sparse = null, float[]? min = null, float[]? max = null)
    {
        var a = new JsonObject { ["componentType"] = componentType, ["type"] = type, ["count"] = count };
        if (view >= 0) a["bufferView"] = view;
        if (byteOffset != 0) a["byteOffset"] = byteOffset;
        if (normalized) a["normalized"] = true;
        if (sparse is not null) a["sparse"] = sparse;
        if (min is not null) a["min"] = new JsonArray(min.Select(v => (JsonNode)v).ToArray());
        if (max is not null) a["max"] = new JsonArray(max.Select(v => (JsonNode)v).ToArray());
        accessors.Add(a);
        return accessors.Count - 1;
    }

    static int Width(string type) => type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 1 };

    public int Floats(string type, params float[] v)
    {
        var b = new byte[v.Length * 4];
        for (int i = 0; i < v.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(4 * i), v[i]);
        return Accessor(View(b), 5126, type, v.Length / Width(type),
                        min: type == "SCALAR" ? [v.Min()] : null, max: type == "SCALAR" ? [v.Max()] : null);
    }

    public int Bytes(string type, bool normalized, params byte[] v) => Accessor(View(v), 5121, type, v.Length / Width(type), normalized);

    public int UShorts(string type, bool normalized, params ushort[] v)
    {
        var b = new byte[v.Length * 2];
        for (int i = 0; i < v.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2 * i), v[i]);
        return Accessor(View(b), 5123, type, v.Length / Width(type), normalized);
    }

    public int Shorts(string type, bool normalized, params short[] v)
    {
        var b = new byte[v.Length * 2];
        for (int i = 0; i < v.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(2 * i), v[i]);
        return Accessor(View(b), 5122, type, v.Length / Width(type), normalized);
    }

    /// <summary>Matrices as glTF stores them: System.Numerics' row-vector matrix, element for element, is glTF's column-major one.</summary>
    public int Matrices(params Matrix4x4[] m) => Floats("MAT4", m.SelectMany(x => new[]
    {
        x.M11, x.M12, x.M13, x.M14, x.M21, x.M22, x.M23, x.M24, x.M31, x.M32, x.M33, x.M34, x.M41, x.M42, x.M43, x.M44,
    }).ToArray());

    public byte[] Build()
    {
        while (bin.Length % 4 != 0) bin.WriteByte(0);
        Root["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = bin.Length });
        Root["bufferViews"] = views.DeepClone();
        Root["accessors"] = accessors.DeepClone();
        var json = Encoding.UTF8.GetBytes(Root.ToJsonString()).ToList();
        while (json.Count % 4 != 0) json.Add((byte)' ');
        var o = new MemoryStream();
        var w = new byte[4];
        void U32(uint v) { BinaryPrimitives.WriteUInt32LittleEndian(w, v); o.Write(w); }
        U32(0x46546C67); U32(2); U32((uint)(12 + 8 + json.Count + 8 + bin.Length));
        U32((uint)json.Count); U32(0x4E4F534A); o.Write(json.ToArray());
        U32((uint)bin.Length); U32(0x004E4942); o.Write(bin.ToArray());
        return o.ToArray();
    }
}

/// <summary>A PNG written in a test: any colour type and depth, a chosen filter for each row, interlaced or not.</summary>
internal static class PngWriter
{
    /// <summary>
    /// The PNG of <paramref name="width"/> × <paramref name="height"/> pixels whose samples (each 0 … 2^depth − 1, a pixel's
    /// channels in a row) are <paramref name="samples"/>; row <c>y</c> of each (sub-)image filtered by <paramref name="filter"/>(y).
    /// </summary>
    public static byte[] Write(int width, int height, int colour, int depth, int[] samples, bool interlace, Func<int, int> filter,
                               byte[]? palette = null, byte[]? trns = null)
    {
        int channels = colour switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        int bits = channels * depth, bpp = Math.Max(1, bits / 8);
        var raw = new MemoryStream();
        void Image(int w, int h, Func<int, int, int, int> sample)
        {
            int stride = (w * bits + 7) / 8;
            var prev = new byte[stride];
            for (int y = 0; y < h; y++)
            {
                var row = new byte[stride];
                for (int x = 0; x < w; x++)
                    for (int c = 0; c < channels; c++)
                    {
                        int v = sample(x, y, c);
                        if (depth == 16) { row[(x * channels + c) * 2] = (byte)(v >> 8); row[(x * channels + c) * 2 + 1] = (byte)v; }
                        else if (depth == 8) row[x * channels + c] = (byte)v;
                        else { int bit = (x * channels + c) * depth; row[bit >> 3] |= (byte)(v << (8 - depth - (bit & 7))); }
                    }
                int f = filter(y);
                raw.WriteByte((byte)f);
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= bpp ? row[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                    int p = f switch { 1 => a, 2 => b, 3 => (a + b) >> 1, 4 => Paeth(a, b, c), _ => 0 };
                    raw.WriteByte((byte)(row[i] - p));
                }
                prev = row;
            }
        }
        if (!interlace) Image(width, height, (x, y, c) => samples[(y * width + x) * channels + c]);
        else
        {
            int[] x0 = [0, 4, 0, 2, 0, 1, 0], y0 = [0, 0, 4, 0, 2, 0, 1], dx = [8, 8, 4, 4, 2, 2, 1], dy = [8, 8, 8, 4, 4, 2, 2];
            for (int p = 0; p < 7; p++)
            {
                int w = (width - x0[p] + dx[p] - 1) / dx[p], h = (height - y0[p] + dy[p] - 1) / dy[p];
                if (w <= 0 || h <= 0) continue;
                int pp = p;
                Image(w, h, (x, y, c) => samples[((y0[pp] + y * dy[pp]) * width + x0[pp] + x * dx[pp]) * channels + c]);
            }
        }
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) zs.Write(raw.ToArray());
        var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = (byte)depth; ihdr[9] = (byte)colour; ihdr[12] = (byte)(interlace ? 1 : 0);
        Chunk(png, "IHDR", ihdr);
        if (palette is not null) Chunk(png, "PLTE", palette);
        if (trns is not null) Chunk(png, "tRNS", trns);
        // The image data split over two IDAT chunks, as encoders do: the decoder must read them as one stream.
        var data = z.ToArray();
        Chunk(png, "IDAT", data[..(data.Length / 2)]);
        Chunk(png, "IDAT", data[(data.Length / 2)..]);
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    static void Chunk(Stream o, string type, byte[] data)
    {
        Span<byte> w = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(w, data.Length);
        o.Write(w);
        var td = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        o.Write(td);
        BinaryPrimitives.WriteUInt32BigEndian(w, Crc(td));
        o.Write(w);
    }

    static uint Crc(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return ~c;
    }

    /// <summary>An 8-bit RGBA PNG of <paramref name="rgba"/>.</summary>
    public static byte[] Rgba(int width, int height, byte[] rgba) =>
        Write(width, height, 6, 8, rgba.Select(b => (int)b).ToArray(), false, y => y % 5);
}

/// <summary>A numpy <c>.npy</c> and <c>.npz</c> written in a test.</summary>
internal static class NpyWriter
{
    public static byte[] Npy(string descr, int[] shape, byte[] data, int version = 1, bool fortran = false)
    {
        string dims = shape.Length == 1 ? $"({shape[0]},)" : $"({string.Join(", ", shape)})";
        string header = $"{{'descr': '{descr}', 'fortran_order': {(fortran ? "True" : "False")}, 'shape': {dims}, }}";
        int prefix = version == 1 ? 10 : 12;
        int total = prefix + header.Length + 1;
        header = header.PadRight(header.Length + (64 - total % 64) % 64) + "\n";
        var o = new MemoryStream();
        o.Write([0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y', (byte)version, 0]);
        Span<byte> w = stackalloc byte[4];
        if (version == 1) { BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)header.Length); o.Write(w[..2]); }
        else { BinaryPrimitives.WriteUInt32LittleEndian(w, (uint)header.Length); o.Write(w); }
        o.Write(Encoding.Latin1.GetBytes(header));
        o.Write(data);
        return o.ToArray();
    }

    public static byte[] Int32s(params int[] v)
    {
        var b = new byte[v.Length * 4];
        for (int i = 0; i < v.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4 * i), v[i]);
        return b;
    }

    public static void Npz(string path, params (string Name, byte[] Npy, bool Deflate)[] arrays)
    {
        using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
        foreach (var (name, npy, deflate) in arrays)
        {
            var e = zip.CreateEntry(name + ".npy", deflate ? CompressionLevel.Optimal : CompressionLevel.NoCompression);
            using var s = e.Open();
            s.Write(npy);
        }
    }
}
