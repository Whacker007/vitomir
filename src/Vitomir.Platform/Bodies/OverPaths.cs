// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository moves over and onto things carry their capsule (CONVENTIONS, Notes, traverse): the motor
// moves the capsule along the clip's path, warped so the hands meet the real edge (BodyMotor.Over.cs).
using System.Numerics;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>one clip's move</b> for the motor (the models repository's <c>traverse</c> note, read by the people's motion and handed here as plain numbers):
/// which move it is, the heights it serves, its path (a row a frame: the capsule's forward and up from where it stood, m, already at the body's
/// scale), the obstacle it was made for and the window in which the path is warped so the hands meet the real edge.
/// </summary>
public sealed class OverClip
{
    /// <summary>The clip (the character assets' index), for the animation to play.</summary>
    public required int Clip { get; init; }
    /// <summary>Its name, for the log and the tests.</summary>
    public required string Name { get; init; }
    /// <summary>The move it plays: <see cref="TraversalKind.HopUp"/>, <see cref="TraversalKind.Vault"/>, <see cref="TraversalKind.ClimbOnto"/>, <see cref="TraversalKind.Mantle"/>.</summary>
    public required TraversalKind Kind { get; init; }
    /// <summary>The heights it serves (<c>height_range_m</c>, m above the ground it starts from).</summary>
    public required float Low { get; init; }
    public required float High { get; init; }
    /// <summary>Its length (s).</summary>
    public required float Duration { get; init; }
    /// <summary>The path: each row's time (s), forward and up (m).</summary>
    public required float[] T { get; init; }
    public required float[] F { get; init; }
    public required float[] U { get; init; }
    /// <summary>The obstacle it was made for: the near face ahead of the capsule's centre at the first frame, and its top (m).</summary>
    public required float FaceM { get; init; }
    public required float TopM { get; init; }
    /// <summary>The window in which the start's difference is taken out (s of the clip: the warp's, else the rise's first half).</summary>
    public required float WarpFrom { get; init; }
    public required float WarpTo { get; init; }
    /// <summary>The up the height's difference goes by (m): the path's up at its end for a move onto the top, its highest for a vault.</summary>
    public required float RiseTo { get; init; }
    /// <summary>Whether it ends standing on the top (a hop up, a climb, a mantle); else it goes over and on (a vault).</summary>
    public required bool EndsOnTop { get; init; }
    /// <summary>The first hand's contact (s; NaN for none) and where it goes (forward and up from where the capsule stood, m).</summary>
    public float ContactS { get; init; } = float.NaN;
    public float HandForward { get; init; }
    public float HandUp { get; init; }
    /// <summary>A vault's: the run's phase it is entered at and left at, and the capsule's speed out of it (m/s; 0 for none).</summary>
    public float EnterPhase { get; init; } = float.NaN;
    public float ExitPhase { get; init; } = float.NaN;
    public float ExitSpeed { get; init; }
    /// <summary>
    /// Each hand put on the top (<c>traverse.hands</c>): which (left or right), from when to when (s of the clip), for the hands' IK onto the real top
    /// (the people's <c>PersonHands</c>); empty for none.
    /// </summary>
    public OverHand[] Hands { get; init; } = [];
    /// <summary>A vault's capsule speed in (m/s; its <c>speed_m_s</c>'s first; 0 for none): the speed its clip is played at its own rate for (<see cref="BodyMotor.VaultRate"/>).</summary>
    public float SpeedIn { get; init; }

    /// <summary>
    /// A low vault the run takes by itself and nothing else does — <c>hurdle_low</c> (<see cref="Hurdle"/>: a
    /// thin top, no hands) and <c>vault_speed</c> (a low deep top, its depths <see cref="DepthLow"/> to <see cref="DepthHigh"/>, m). Never the vault's own
    /// (<see cref="OverPaths.For"/>): <see cref="OverPaths.LowFor"/>.
    /// </summary>
    public bool RunOnly { get; init; }

    /// <summary>
    /// The models repository's note, meanwhile: for each of <see cref="Hands"/>, how far its clip's own knuckles stand over its own top at the hand's contact, beyond
    /// the palm's few centimetres (m; measured at load, <c>PersonOverClips.MeasureHands</c>; 0 where the clip has the hand on its top — every clip but
    /// <c>vault_speed</c>, whose left knuckles are some 29 cm over its top): the hands' IK lowers the hand by as much while it is down, the body not with it.
    /// </summary>
    public float[] HandDrop { get; set; } = [];
    public bool Hurdle { get; init; }
    public float DepthLow { get; init; } = float.NaN;
    public float DepthHigh { get; init; } = float.NaN;

    /// <summary>Whether a height of <paramref name="h"/> m is within its band (a millimetre over for the level's rounding).</summary>
    public bool Serves(float h) => h >= Low - BodyMotor.Within && h <= High + BodyMotor.Within;

    /// <summary>The path's forward and up at <paramref name="t"/> s (between its rows linearly; held before the first and after the last).</summary>
    public (float Forward, float Up) At(float t)
    {
        var ts = T;
        if (ts.Length == 0) return (0f, 0f);
        if (t <= ts[0]) return (F[0], U[0]);
        if (t >= ts[^1]) return (F[^1], U[^1]);
        int lo = 0, hi = ts.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (ts[mid] <= t) lo = mid; else hi = mid;
        }
        float span = ts[hi] - ts[lo], k = span > 1e-6f ? (t - ts[lo]) / span : 0f;
        return (F[lo] + (F[hi] - F[lo]) * k, U[lo] + (U[hi] - U[lo]) * k);
    }
}

/// <summary>A hand a clip-carried move puts on the top: the left (else the right), and from when to when it is down (s of the clip; <c>contact_s</c>, <c>release_s</c>).</summary>
public readonly record struct OverHand(bool Left, float ContactS, float ReleaseS);

/// <summary>
/// <b>a person's moves over and onto things</b> for the motor (<see cref="BodyMotor"/>, the clip-carried moves): every <see cref="OverClip"/> of
/// the set, lowest band first within a move. Null for a body without them (1.1's, a set).
/// </summary>
public sealed class OverPaths
{
    public required OverClip[] Clips { get; init; }

    /// <summary>The clip that plays <paramref name="kind"/> at a height of <paramref name="h"/> m (the lower of two whose bands share an edge), or null.</summary>
    public OverClip? For(TraversalKind kind, float h)
    {
        foreach (var c in Clips) if (c.Kind == kind && !c.RunOnly && c.Serves(h)) return c;
        return null;
    }

    /// <summary>The first clip of a move (its lowest band), or null (not a low vault the run takes by itself).</summary>
    public OverClip? Any(TraversalKind kind)
    {
        foreach (var c in Clips) if (c.Kind == kind && !c.RunOnly) return c;
        return null;
    }

    /// <summary>
    /// The low vault for a top <paramref name="h"/> m high and <paramref name="depth"/> m deep (<paramref name="thin"/>: shallower than a mantle's
    /// least depth): <c>hurdle_low</c> for a thin top in its band shallower than the speed vault's least depth, else <c>vault_speed</c> in its band and depths;
    /// null for none.
    /// </summary>
    public OverClip? LowFor(float h, float depth, bool thin)
    {
        OverClip? hurdle = null, speed = null;
        foreach (var c in Clips)
        {
            if (!c.RunOnly || !c.Serves(h)) continue;
            if (c.Hurdle) { if (thin) hurdle ??= c; }
            else if (!float.IsNaN(c.DepthLow) && depth >= c.DepthLow - BodyMotor.Within && depth <= c.DepthHigh + BodyMotor.Within) speed ??= c;
        }
        if (hurdle is not null && (speed is null || depth < speed.DepthLow)) return hurdle;
        return speed ?? hurdle;
    }

    /// <summary>Whether the set has a low vault.</summary>
    public bool HasLow
    {
        get
        {
            foreach (var c in Clips) if (c.RunOnly) return true;
            return false;
        }
    }

    /// <summary>The clip of index <paramref name="index"/> (from 0), or null.</summary>
    public OverClip? At(int index) => index >= 0 && index < Clips.Length ? Clips[index] : null;

    /// <summary>Its index (from 0), or −1.</summary>
    public int IndexOf(OverClip clip) => Array.IndexOf(Clips, clip);
}

/// <summary>
/// <b>a clip-carried move's state</b> (the motor's; part of the people's digested <c>PersonOver</c>): which clip carries the move, where its
/// first frame's capsule stands for the real obstacle (<see cref="Origin"/>) and the way it goes (<see cref="Way"/>), the start's difference taken
/// out over the warp (<see cref="Delta"/>), the clip's time it began at (<see cref="Entry"/>: 0 from the ground, the hands' contact from the air),
/// the real top (the collision capsule's floor over it); and a vault's press held for the run's footfall (<see cref="Pending"/>, <see cref="Wait"/>).
/// <see cref="Traversal"/> keeps the move's kind, progress and length as for every move: the path is a function of the two.
/// </summary>
public struct OverMove
{
    /// <summary>1 + the index of the <see cref="OverClip"/> carrying the move under way (<see cref="OverPaths.Clips"/>), 0 for none.</summary>
    public int Clip;
    /// <summary>Where the clip's first frame's capsule stands for the real obstacle (its feet), and the level unit way the clip's forward goes.</summary>
    public Vector3 Origin;
    public Vector2 Way;
    /// <summary>The capsule's difference at the start from the clip's path laid on <see cref="Origin"/> (m), taken out over the warp.</summary>
    public Vector3 Delta;
    /// <summary>The clip's time the move began at (s).</summary>
    public float Entry;
    /// <summary>1 when it began in the air (from a jump: entered at the hands' contact, the difference taken out over <see cref="BodyMotor.AirBlend"/>).</summary>
    public int Air;
    /// <summary>The real obstacle's top (world Z), and its height's difference from the clip's own (m), laid on by the path's rise.</summary>
    public float Top;
    public float Stretch;
    /// <summary>1 while a press of jump made running at a vault is held for the run's footfall (the animation tells how long: <see cref="Wait"/>); 2
    /// while the run's own vault, begun by itself with no press, is held so (<c>BodyMotor.OverPress</c>).</summary>
    public int Pending;
    /// <summary>The seconds left to hold the press (−1: not yet told), and how long it has been held (s).</summary>
    public float Wait;
    public float Held;
    /// <summary>The face's distance ahead at the press (m) and the speed then (m/s), for the animation's choice of the footfall.</summary>
    public float Face;
    public float Speed;
    /// <summary>1 on the tick a press goes to start a clip-carried move from the ground; 2 for the run's own vault (no press), 3 for a press under
    /// the auto vault's rules (both played at the speed met: <see cref="BodyMotor.VaultRate"/>).</summary>
    public int Go;
}
