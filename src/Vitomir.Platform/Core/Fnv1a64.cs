// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Buffers.Binary;
using System.Text;

namespace Vitomir.Platform.Core;

/// <summary>
/// FNV-1a, 64 bits: the platform's hash for digests (two runs compared tick by tick) and for keying
/// random streams by purpose. Not for security. Every multi-byte value goes in little-endian, so the
/// hash is the same on any machine.
/// </summary>
public struct Fnv1a64
{
    public const ulong OffsetBasis = 14695981039346656037UL, Prime = 1099511628211UL;

    public ulong Value;

    public Fnv1a64() { Value = OffsetBasis; }

    public void Add(ReadOnlySpan<byte> bytes)
    {
        ulong h = Value;
        foreach (byte b in bytes) { h ^= b; h *= Prime; }
        Value = h;
    }

    public void Add(ulong v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(b, v);
        Add(b);
    }

    public void Add(long v) => Add(unchecked((ulong)v));
    public void Add(int v) => Add(unchecked((ulong)(uint)v));
    public void Add(float v) => Add(unchecked((ulong)(uint)BitConverter.SingleToInt32Bits(v)));

    public void Add(string s) => Add(Encoding.UTF8.GetBytes(s));

    public static ulong Of(string s)
    {
        var h = new Fnv1a64();
        h.Add(s);
        return h.Value;
    }
}
