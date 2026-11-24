// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Buffers.Binary;
using System.Numerics;

namespace Vitomir.Formats;

/// <summary>
/// One tag of the engine's tag system (36 B on disk: u32 id + 32 value bytes). Used by NOT
/// templates and NSD things. How the 32 bytes are read depends on the tag's datatype
/// (the SDK's <c>noteditortags.txt</c>, or the engine's own table): numbers use the first 4 bytes,
/// vectors/colours the first 12, strings are NUL-terminated.
/// </summary>
public readonly record struct NodTag(uint Id, byte[] Raw)
{
    public const int Size = 36;

    public string AsString() => BinReader.CString(Raw);
    public uint AsUInt() => BinaryPrimitives.ReadUInt32LittleEndian(Raw);
    public int AsInt() => BinaryPrimitives.ReadInt32LittleEndian(Raw);
    public float AsFloat() => BinaryPrimitives.ReadSingleLittleEndian(Raw);
    public Vector3 AsVector3() => new(
        BinaryPrimitives.ReadSingleLittleEndian(Raw.AsSpan(0)),
        BinaryPrimitives.ReadSingleLittleEndian(Raw.AsSpan(4)),
        BinaryPrimitives.ReadSingleLittleEndian(Raw.AsSpan(8)));

    internal static NodTag Read(BinReader r) => new(r.U32(), r.Bytes(32));

    /// <summary>A tag holding a whole number, written the way the editor writes one.</summary>
    public static NodTag Of(uint id, uint value)
    {
        var raw = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(raw, value);
        return new NodTag(id, raw);
    }

    public static NodTag Of(uint id, float value)
    {
        var raw = new byte[32];
        BinaryPrimitives.WriteSingleLittleEndian(raw, value);
        return new NodTag(id, raw);
    }

    public static NodTag Of(uint id, Vector3 value)
    {
        var raw = new byte[32];
        BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(0), value.X);
        BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(4), value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(8), value.Z);
        return new NodTag(id, raw);
    }

    /// <summary>A tag holding a name. The engine's tag value is 32 bytes, so a name is clipped to 31.</summary>
    public static NodTag Of(uint id, string value)
    {
        var raw = new byte[32];
        var bytes = System.Text.Encoding.Latin1.GetBytes(value);
        bytes.AsSpan(0, Math.Min(bytes.Length, raw.Length - 1)).CopyTo(raw);
        return new NodTag(id, raw);
    }
}
