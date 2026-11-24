// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A multiplayer character: `Crypt\npf%06d.npf`.
//
// The file is the network game's local player written whole: the writer opens
// `Crypt\<login>.npf` ("%s\\%s.%s" with "Crypt" and "npf"), writes a 0x6c-byte header and
// then has the player save itself through its own save, the very writer a save's party
// uses (the thing, the actor, the player). So past the
// header the file is one thing record of `!main.nsg`'s grammar (`NsgReader.ReadThing`), and reads to
// its last byte exactly as a save's party member does.
//
// The reader (the select screen's load) reads the header, refuses it unless its
// magic is "npf" and its version 5, reads the thing's type word and hands the rest to the player's
// own load under the save's loading flag (which the save's load sets too).
// The list's check (made for every `Crypt\*.npf`) reads the header alone and
// asks the same two things.
namespace Vitomir.Formats;

/// <summary>
/// The 0x6c bytes at the head of a character file: what the select list shows without
/// loading the character.
/// </summary>
public sealed class NpfHeader
{
    /// <summary>Bytes 0–3: <c>npf\0</c>.</summary>
    public string Magic { get; init; } = "";
    /// <summary>5, the only one 1.1 reads.</summary>
    public uint Version { get; init; }
    /// <summary>At 0x08, 0x20 bytes: the character's name (<see cref="NsgPlayer.Text3C4"/>).</summary>
    public string Name { get; init; } = "";
    /// <summary>At 0x28, 0x20 bytes: the file's own stem (<c>npf000003</c>), which 1.1 names the file by.</summary>
    public string Login { get; init; } = "";
    /// <summary>At 0x48, 0x20 bytes: the clan's name as the string table gives it (<c>Brujah</c>, <c>Human</c>), empty for none.</summary>
    public string ClanName { get; init; } = "";
    /// <summary>The experience ever earned.</summary>
    public uint Experience { get; init; }

    /// <summary>Why 1.1's own list would leave the file out, or null when it would list it.</summary>
    public string? Refusal => Magic != Npf.Magic ? $"its magic is \"{Magic}\", not \"{Npf.Magic}\""
        : Version != Npf.ExpectedVersion ? $"it is version {Version}; 1.1 reads only {Npf.ExpectedVersion}" : null;
}

/// <summary>A whole character file: its header and the player it holds.</summary>
public sealed class NpfCharacter
{
    public NpfHeader Header { get; init; } = new();
    /// <summary>The player, a thing record of type 7 (<see cref="NsgThingType.Player"/>): thing, actor and player parts.</summary>
    public NsgThing Thing { get; init; } = new();
    public NsgActor Actor => Thing.Actor!;
    public NsgPlayer Player => Thing.Player!;
}

/// <summary><c>Crypt\npf%06d.npf</c>, 1.1's multiplayer character.</summary>
public static class Npf
{
    public const string Magic = "npf";
    public const uint ExpectedVersion = 5;
    public const int HeaderSize = 0x6c;
    const int Name = 0x20;

    /// <summary>The header of <paramref name="data"/>; null when it is shorter than one (1.1's <c>fread</c> of 0x6c bytes falls short).</summary>
    public static NpfHeader? ReadHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize) return null;
        var r = new BinReader(data[..HeaderSize].ToArray());
        return new NpfHeader
        {
            Magic = BinReader.CString(r.Bytes(4)), Version = r.U32(),
            Name = r.CStr(Name), Login = r.CStr(Name), ClanName = r.CStr(Name), Experience = r.U32(),
        };
    }

    /// <summary>The header of the file at <paramref name="path"/> (its first 0x6c bytes only), or null when it is shorter.</summary>
    public static NpfHeader? ReadHeaderFile(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var head = new byte[HeaderSize];
        int got = f.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        return ReadHeader(head.AsSpan(0, got));
    }

    /// <summary>
    /// The whole character: the header, which must pass 1.1's own check, then one player record to the
    /// file's last byte. Throws <see cref="NodFormatException"/> otherwise.
    /// </summary>
    public static NpfCharacter Read(byte[] data, string name = "npf000000.npf", NsgReadOptions? options = null)
    {
        var header = ReadHeader(data) ?? throw new NodFormatException($"{name}: {data.Length} bytes, shorter than the 0x6c-byte header");
        if (header.Refusal is { } why) throw new NodFormatException($"{name}: not a character 1.1 reads: {why}");
        var r = new BinReader(data, name) { Pos = HeaderSize };
        var thing = new NsgReader(r, options ?? NsgReadOptions.Default).ReadThing();
        if (thing.Type != NsgThingType.Player || thing.Actor is null || thing.Player is null)
            throw new NodFormatException($"{name}: holds a {thing.Type} record, not a player");
        r.ExpectEof();
        return new NpfCharacter { Header = header, Thing = thing };
    }

    public static NpfCharacter ReadFile(string path, NsgReadOptions? options = null) =>
        Read(File.ReadAllBytes(path), Path.GetFileName(path), options);
}
