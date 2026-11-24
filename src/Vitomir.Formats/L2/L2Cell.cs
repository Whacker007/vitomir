// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Buffers.Binary;
using System.Text;

namespace Vitomir.Formats.L2;

/// <summary>
/// One chunk of an L2 cell file (ours): a four-letter tag, the chunk's own
/// version, and its payload. A reader that does not know the tag keeps the chunk as it is, so a
/// cell read and written back carries every chunk it had, known or not — the format's "inert
/// extras".
/// </summary>
public sealed record L2Chunk(string Tag, uint Version, byte[] Payload)
{
    /// <summary>A tag is exactly four printable ASCII characters.</summary>
    public static bool IsTag(string tag) => tag.Length == 4 && tag.All(c => c is >= ' ' and <= '~');
}

/// <summary>
/// An L2 cell file (ours): the 4-byte magic <c>L2CL</c>, a u32 file version, then chunks to the end
/// of the file, each a 4-byte ASCII tag, a u32 chunk version, a u64 payload length and the payload.
/// Everything little-endian, nothing padded. The chunks keep their order through a read and a write.
/// </summary>
public sealed class L2Cell
{
    public const string Magic = "L2CL";
    /// <summary>The cell-file version this code writes and reads.</summary>
    public const uint Version1 = 1;
    const int HeaderSize = 8, ChunkHeaderSize = 16;

    public uint Version { get; init; } = Version1;
    public List<L2Chunk> Chunks { get; init; } = [];

    /// <summary>The first chunk with this tag, or null.</summary>
    public L2Chunk? Find(string tag) => Chunks.FirstOrDefault(c => c.Tag == tag);

    /// <summary>The first chunk with this tag, or an error naming the cell.</summary>
    public L2Chunk Need(string tag, string name) =>
        Find(tag) ?? throw new NodFormatException($"{name}: no {tag} chunk");

    public static L2Cell Read(byte[] data, string name = "<cell>")
    {
        if (data.Length < HeaderSize || Encoding.ASCII.GetString(data, 0, 4) != Magic)
            throw new NodFormatException($"{name}: not an L2 cell (no {Magic} magic)");
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        if (version != Version1) throw new NodFormatException($"{name}: cell version {version}, this build reads {Version1}");
        var chunks = new List<L2Chunk>();
        long pos = HeaderSize;
        while (pos < data.Length)
        {
            if (data.Length - pos < ChunkHeaderSize) throw new NodFormatException($"{name}: truncated chunk header at 0x{pos:x}");
            var span = data.AsSpan((int)pos);
            string tag = Encoding.ASCII.GetString(span[..4]);
            uint cv = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
            ulong length = BinaryPrimitives.ReadUInt64LittleEndian(span[8..]);
            if (length > (ulong)(data.Length - pos - ChunkHeaderSize))
                throw new NodFormatException($"{name}: chunk {tag} at 0x{pos:x} runs past the end ({length} bytes)");
            chunks.Add(new L2Chunk(tag, cv, span.Slice(ChunkHeaderSize, (int)length).ToArray()));
            pos += ChunkHeaderSize + (long)length;
        }
        return new L2Cell { Version = version, Chunks = chunks };
    }

    /// <summary>
    /// Reads only the chunks <paramref name="want"/> accepts from the cell file at <paramref name="path"/>,
    /// seeking past the rest (ours, Phase 3): the simulation's streamer takes <c>COLL</c>, <c>NAVM</c>,
    /// <c>LITE</c> and <c>ANCH</c>, the renderer's <c>MESH</c>, and neither reads the other's bytes.
    /// </summary>
    public static L2Cell ReadFile(string path, Func<string, bool> want)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        Span<byte> head = stackalloc byte[ChunkHeaderSize];
        if (f.Read(head[..HeaderSize]) != HeaderSize || Encoding.ASCII.GetString(head[..4]) != Magic)
            throw new NodFormatException($"{path}: not an L2 cell (no {Magic} magic)");
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(head[4..]);
        if (version != Version1) throw new NodFormatException($"{path}: cell version {version}, this build reads {Version1}");
        var chunks = new List<L2Chunk>();
        long length = f.Length;
        while (f.Position < length)
        {
            long pos = f.Position;
            if (length - pos < ChunkHeaderSize || f.Read(head) != ChunkHeaderSize) throw new NodFormatException($"{path}: truncated chunk header at 0x{pos:x}");
            string tag = Encoding.ASCII.GetString(head[..4]);
            uint cv = BinaryPrimitives.ReadUInt32LittleEndian(head[4..]);
            ulong size = BinaryPrimitives.ReadUInt64LittleEndian(head[8..]);
            if (size > (ulong)(length - pos - ChunkHeaderSize)) throw new NodFormatException($"{path}: chunk {tag} at 0x{pos:x} runs past the end ({size} bytes)");
            if (!want(tag)) { f.Seek((long)size, SeekOrigin.Current); continue; }
            var payload = new byte[size];
            f.ReadExactly(payload);
            chunks.Add(new L2Chunk(tag, cv, payload));
        }
        return new L2Cell { Version = version, Chunks = chunks };
    }

    public byte[] Write()
    {
        using var ms = new MemoryStream();
        Span<byte> head = stackalloc byte[ChunkHeaderSize];
        ms.Write(Encoding.ASCII.GetBytes(Magic));
        BinaryPrimitives.WriteUInt32LittleEndian(head, Version);
        ms.Write(head[..4]);
        foreach (var c in Chunks)
        {
            if (!L2Chunk.IsTag(c.Tag)) throw new ArgumentException($"chunk tag '{c.Tag}' is not four printable ASCII characters");
            Encoding.ASCII.GetBytes(c.Tag, head);
            BinaryPrimitives.WriteUInt32LittleEndian(head[4..], c.Version);
            BinaryPrimitives.WriteUInt64LittleEndian(head[8..], (ulong)c.Payload.Length);
            ms.Write(head);
            ms.Write(c.Payload);
        }
        return ms.ToArray();
    }
}
