// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Space;

/// <summary>
/// <b>A ladder as the simulation uses it</b> (ours): its index in the level's list,
/// its name (or <c>ladder &lt;index&gt;</c>), the middle of its climbing face at the ground (<see cref="Foot"/>) and at the
/// top floor's height (<see cref="Top"/>), its width, and the level unit way its climbing side looks out
/// (<see cref="Out"/>; a body on it faces <c>−Out</c>), worked out once with <see cref="DetMath"/>.
/// </summary>
public readonly record struct Ladder(int Index, string Name, Vector3 Foot, Vector3 Top, float Width, Vector3 Out)
{
    /// <summary>How high it rises (m).</summary>
    public float Rise => Top.Z - Foot.Z;

    /// <summary>The level unit way along it, to the right of a body on it looking at it (<c>Out</c> turned a quarter clockwise).</summary>
    public Vector3 Side => new(-Out.Y, Out.X, 0f);

    /// <summary>The point of the climbing face's middle line at height <paramref name="z"/> (the line goes on past its ends).</summary>
    public Vector3 FaceAt(float z)
    {
        float t = Rise > 1e-5f ? (z - Foot.Z) / Rise : 0f;
        return Foot + (Top - Foot) * t;
    }

    /// <summary>
    /// A ladder from the format's (<see cref="L2Ladder"/>), its facing turned into <see cref="Out"/> once; null when it is
    /// not sound (<see cref="L2Ladder.Fault"/>).
    /// </summary>
    public static Ladder? From(L2Ladder l, int index)
    {
        if (l.Fault(index) is not null) return null;
        var (s, c) = DetMath.SinCos(DetMath.WrapAngle(l.Facing * DetMath.DegToRad));
        return new Ladder(index, string.IsNullOrEmpty(l.Name) ? $"ladder {index}" : l.Name,
                          new Vector3(l.Foot[0], l.Foot[1], l.Foot[2]), new Vector3(l.Top[0], l.Top[1], l.Top[2]), l.Width, new Vector3(c, s, 0f));
    }
}

/// <summary>
/// <b>A level's ladders</b> (ours; 1.1 has none): the manifest's
/// <c>traversal.ladders</c>, in its order. The motor finds them through the collision space it is given
/// (<see cref="CollisionWorld.Ladders"/>); the reach highlight shows them. A ladder that is not sound is passed over and
/// said once (<see cref="Problems"/>). Read only while the world runs.
/// </summary>
public sealed class LevelLadders
{
    readonly Ladder[] ladders;

    /// <summary>A level with no ladders.</summary>
    public static LevelLadders None { get; } = new([], []);

    LevelLadders(Ladder[] ladders, IReadOnlyList<string> problems)
    {
        this.ladders = ladders;
        Problems = problems;
    }

    /// <summary>How many there are.</summary>
    public int Count => ladders.Length;

    /// <summary>The ladder at <paramref name="index"/> (0 to <see cref="Count"/> − 1).</summary>
    public ref readonly Ladder this[int index] => ref ladders[index];

    /// <summary>The ladders, in the manifest's order.</summary>
    public IReadOnlyList<Ladder> All => ladders;

    /// <summary>What was wrong with the manifest's ladders, one line each.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>How far in front of or behind a ladder's face a lip is on the ladder (m): its rails and rungs.</summary>
    public const float FaceReach = 0.3f;

    /// <summary>How far past a ladder's sides a lip is still on it (m).</summary>
    public const float SideReach = 0.05f;

    /// <summary>
    /// How near the top's height a lip is the top floor's own edge, which a pull-up and a grab still take (m), and how
    /// far over the top the ladder's rails may go on (m).
    /// </summary>
    public const float TopEdge = 0.1f, AboveTop = 1.2f;

    /// <summary>
    /// Whether <paramref name="point"/> (a lip) lies on one of the ladders (ours): within its width (and
    /// <see cref="SideReach"/>), within <see cref="FaceReach"/> of its climbing face, and from a little under its foot to
    /// its top — and on over the top by <see cref="AboveTop"/> for rails that go on up — but not within
    /// <see cref="TopEdge"/> of the top's height, the top floor's own edge. The traversal probe takes no such lip
    /// (<see cref="CollisionWorld.NoClimb"/>): a ladder is climbed by use, and a tap at one never hangs from a rung,
    /// whether or not the level marks its mesh.
    /// </summary>
    public bool OnFace(Vector3 point)
    {
        for (int i = 0; i < ladders.Length; i++)
        {
            ref readonly var l = ref ladders[i];
            if (point.Z < l.Foot.Z - TopEdge || point.Z > l.Top.Z + AboveTop || MathF.Abs(point.Z - l.Top.Z) <= TopEdge) continue;
            var d = point - l.FaceAt(point.Z);
            if (MathF.Abs(d.X * l.Out.X + d.Y * l.Out.Y) <= FaceReach && MathF.Abs(d.X * l.Side.X + d.Y * l.Side.Y) <= l.Width * 0.5f + SideReach)
                return true;
        }
        return false;
    }

    /// <summary>The ladders of a manifest (none when it has no <c>traversal</c>).</summary>
    public static LevelLadders From(L2Manifest manifest)
    {
        if (manifest.Traversal?.Ladders is not { Count: > 0 } list) return None;
        var found = new List<Ladder>();
        var problems = new List<string>();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Fault(i) is { } fault) { problems.Add(fault + "; passed over"); continue; }
            found.Add(Ladder.From(list[i], found.Count)!.Value with { Name = string.IsNullOrEmpty(list[i].Name) ? $"ladder {i}" : list[i].Name! });
        }
        return new LevelLadders([.. found], problems);
    }

    /// <summary>Ladders made in code (tests, tools), numbered in the given order.</summary>
    public static LevelLadders Of(IEnumerable<L2Ladder> ladders)
    {
        var found = new List<Ladder>();
        foreach (var l in ladders)
            if (Ladder.From(l, found.Count) is { } ladder) found.Add(ladder);
        return new LevelLadders([.. found], []);
    }
}
