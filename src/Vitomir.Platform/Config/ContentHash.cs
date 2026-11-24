// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Security.Cryptography;
using System.Text;

namespace Vitomir.Platform.Config;

/// <summary>
/// SHA-256 as lower-case hex: the identity of a data file or of a configuration (the rules package names
/// its files by SHA-256, and a trace carries the configuration's). Unlike <see cref="Core.Fnv1a64"/>,
/// which is for per-tick digests, this one is meant to be compared with hashes made by other tools.
/// </summary>
public static class ContentHash
{
    /// <summary>The SHA-256 of some bytes, as 64 lower-case hex digits.</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>The SHA-256 of a text's UTF-8 bytes.</summary>
    public static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    /// <summary>The SHA-256 of a file's bytes as they are on disk.</summary>
    public static string Sha256OfFile(string path) => Sha256Hex(File.ReadAllBytes(path));

    /// <summary>The SHA-256 of a JSON text's canonical form (<see cref="CanonicalJson"/>): the same for any layout of the same document.</summary>
    public static string Sha256OfCanonicalJson(string json) => Sha256Hex(CanonicalJson.Canonicalize(json));
}
