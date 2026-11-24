// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Formats;

/// <summary>A keyframe of one bone track: the value plus precomputed Hermite factors per component.</summary>
public readonly record struct NadKey(float Frame, float FrameScale, Vector3 Value, Vector3 C, Vector3 B, Vector3 A);

public sealed record NadTrack(uint Bone, uint Type, NadKey[] Keys)
{
    public const uint Rotation = 0, Translate = 1, Scale = 2;
}

/// <summary>An event marker on the animation timeline (footsteps, hits, casts…).</summary>
public readonly record struct NadTag(float Frame, uint Type)
{
    static readonly string[] Names =
    [
        "Lwalk", "Rwalk", "Lrun", "Rrun", "Fire", "Strike", "Cast", "Fall", "Project", "Flap", "Suck",
        "Idle", "Idle2", "Codex1", "Codex2", "Codex3", "Repeat", "RepeatTo", "Throwdeath",
    ];
    public string Name => Type < Names.Length ? Names[Type] : $"Tag{Type}";
}

/// <summary>
/// NAD animation, version 3 (spec: NodSDK/Docs/nad.htm). Rotation tracks hold Euler angles
/// (pitch, roll, yaw = about X, Y, Z), each component interpolated separately. Nominal 30 frames/s.
/// </summary>
public sealed class NadAnimation
{
    public const uint Version3 = 3;

    public uint Version { get; init; }
    public uint Flags { get; init; }
    public float Duration { get; init; }
    public NadTrack[] Tracks { get; init; } = [];
    public NadTag[] Tags { get; init; } = [];

    public static NadAnimation Read(byte[] data, string name = "<nad>")
    {
        var r = new BinReader(data, name);
        uint version = r.U32();
        if (version != Version3) throw r.Error($"unexpected NAD version {version}");
        int numTracks = r.Count32(4096);
        uint flags = r.U32();
        float duration = r.F32();
        var tracks = new NadTrack[numTracks];
        for (int t = 0; t < numTracks; t++)
        {
            int nkeys = r.Count32(); uint bone = r.U32(); uint type = r.U32();
            var keys = new NadKey[nkeys];
            for (int k = 0; k < nkeys; k++)
                keys[k] = new NadKey(r.F32(), r.F32(), r.Vec3(), r.Vec3(), r.Vec3(), r.Vec3());
            tracks[t] = new NadTrack(bone, type, keys);
        }
        // Three shipped files end right after the tracks with no tag count; the original tolerates
        // that, so it reads as zero tags.
        int numTags = r.Remaining >= 4 ? r.Count32(100_000) : 0;
        var tags = new NadTag[numTags];
        for (int i = 0; i < numTags; i++) tags[i] = new NadTag(r.F32(), r.U32());
        r.ExpectEof();
        return new NadAnimation { Version = version, Flags = flags, Duration = duration, Tracks = tracks, Tags = tags };
    }
}
