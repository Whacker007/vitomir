// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Vitomir.Platform.Control;

/// <summary>A module as a recording names it: its id, its version and the hash of its files (<c>ModuleLoader.FilesHash</c>).</summary>
public sealed record TapeModule(string Id, string Version, string Hash);

/// <summary>
/// What a recording starts from: enough to build the same world again — the program's build, the
/// game's modules and their configuration hashes, the level, the start, the world's seed — and free-form
/// notes. Stored as JSON at the head of the tape.
/// </summary>
public sealed record TapeHeader
{
    public const string Magic = "VITOMIR-PLATFORM-TAPE";
    /// <summary>2: the picture's shape is recorded (a record of its own whenever it changes). 3: the attack direction's mode changed mid-run (a record of its own before the frame it changes on).</summary>
    public const int CurrentVersion = 3;

    public string Format { get; init; } = Magic;
    public int Version { get; init; } = CurrentVersion;
    public string Build { get; init; } = "";
    public string Game { get; init; } = "";
    public Dictionary<string, string> ConfigHashes { get; init; } = [];
    public string Level { get; init; } = "";
    public string Start { get; init; } = "";
    public ulong Seed { get; init; }
    public Dictionary<string, string> Notes { get; init; } = [];

    /// <summary>The game's modules as recorded, in load order (empty in an older tape).</summary>
    public List<TapeModule> Modules { get; init; } = [];
}

/// <summary>
/// A recording: the header and every tick's <see cref="ActionFrame"/>, with the world's digest every
/// <see cref="DigestEvery"/> ticks so a replay can say where it first parts from the original
/// (per tick, not the window's events; the look stored as its exact float bits).
/// The file is gzip: a line of JSON (the header), then binary records: a frame, a digest, and (version 2) the
/// picture's shape before the first frame that has it and whenever it changes, so every frame's
/// <see cref="ActionFrame.Aspect"/> comes back as it was given. A version-1 tape's frames come back with 0 (not known).
/// Version 3: a frame that changes the player's attack direction mode (<see cref="ActionFrame.AttackMode"/>: the
/// Controls page's row set mid-run) has a mode record before it, so the replay changes it on the same tick; an older tape has none.
/// The mode the run started with is the header's note (<see cref="AttackDirectionInput.TapeNote"/>).
/// </summary>
public sealed class Tape
{
    public const int DigestEvery = 30;
    const byte FrameTag = 1, DigestTag = 2, PictureTag = 3, AttackModeTag = 4;

    public TapeHeader Header { get; init; } = new();
    public List<ActionFrame> Frames { get; } = [];

    /// <summary>The digest after tick <c>key</c> (ticks counted from 1 after the start).</summary>
    public SortedDictionary<long, ulong> Digests { get; } = [];

    public void Write(string path)
    {
        using var file = File.Create(path);
        using var gz = new GZipStream(file, CompressionLevel.Optimal);
        using var w = new BinaryWriter(gz, Encoding.UTF8);
        var head = JsonSerializer.Serialize(Header);
        w.Write(head);
        long tick = 0;
        float aspect = 0f;
        foreach (var f in Frames)
        {
            if (BitConverter.SingleToInt32Bits(f.Aspect) != BitConverter.SingleToInt32Bits(aspect))
            {
                aspect = f.Aspect;
                w.Write(PictureTag); w.Write(aspect);
            }
            if (f.AttackMode is { } mode) { w.Write(AttackModeTag); w.Write((byte)mode); }   // changed on this tick
            w.Write(FrameTag);
            w.Write(f.Move.X); w.Write(f.Move.Y);
            w.Write(f.Look.X); w.Write(f.Look.Y);
            w.Write((uint)f.Held);
            w.Write(f.Pointer.X); w.Write(f.Pointer.Y);
            tick++;
            if (Digests.TryGetValue(tick, out ulong d)) { w.Write(DigestTag); w.Write(tick); w.Write(d); }
        }
    }

    public static Tape Read(string path)
    {
        using var file = File.OpenRead(path);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        using var r = new BinaryReader(gz, Encoding.UTF8);
        var header = JsonSerializer.Deserialize<TapeHeader>(r.ReadString()) ?? throw new InvalidDataException("no tape header");
        if (header.Format != TapeHeader.Magic) throw new InvalidDataException($"{path} is not a platform tape");
        if (header.Version > TapeHeader.CurrentVersion) throw new InvalidDataException($"{path} is tape version {header.Version}; this build reads up to {TapeHeader.CurrentVersion}");
        var tape = new Tape { Header = header };
        float aspect = 0f;
        AttackDirectionMode? mode = null;
        while (true)
        {
            int tag = ReadTag(r);
            if (tag < 0) break;
            if (tag == FrameTag)
            {
                var move = new Vector2(r.ReadSingle(), r.ReadSingle());
                var look = new Vector2(r.ReadSingle(), r.ReadSingle());
                var held = (Buttons)r.ReadUInt32();
                var pointer = new Vector2(r.ReadSingle(), r.ReadSingle());
                tape.Frames.Add(new ActionFrame(move, look, held, pointer, aspect, mode));
                mode = null;
            }
            else if (tag == PictureTag) aspect = r.ReadSingle();
            else if (tag == AttackModeTag)
            {
                byte m = r.ReadByte();
                if (m >= AttackDirectionInput.Names.Count) throw new InvalidDataException($"{path}: an attack direction mode {m}, which this build does not know");
                mode = (AttackDirectionMode)m;
            }
            else if (tag == DigestTag) tape.Digests[r.ReadInt64()] = r.ReadUInt64();
            else throw new InvalidDataException($"{path}: unknown record {tag}");
        }
        return tape;
    }

    static int ReadTag(BinaryReader r)
    {
        try { return r.ReadByte(); }
        catch (EndOfStreamException) { return -1; }
    }
}
