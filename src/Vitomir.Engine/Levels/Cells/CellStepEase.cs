// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **A walker is shown climbing a cell level's steps, not hopping up them** (ours; presentation only).
///
/// A walker in a cell level stands on the triangle under its feet (<see cref="CellLevel.Floor"/>), so on a
/// stair of real steps its height changes by a whole riser — 0.17 m, twelve units — in the one tick in which
/// it crosses the riser's line, and the game camera, which looks at the walker's head, jumps with it: at a
/// run that is a jolt five times a second. 1.1's own levels do the same on their own stairs and are left
/// exactly as they are; this is for the levels that are ours.
///
/// Each actor in the place carries a <see cref="WorldThing.ShownLift"/>: how far above (or, having just
/// stepped up, below) where it is the thing is drawn and looked at. A tick's change of height is taken out of
/// the lift, so what is shown does not jump, and the lift is then paid back towards nothing: by
/// <see cref="Slope"/> of the ground covered this tick, so that a run up a flight is shown as one climb, and
/// by <see cref="Settle"/> a second besides, so that a walker who stops on a step comes to stand on it.
/// Where the walker is — its position, the ground it stands on, what it touches, what the scripts are
/// told — is not changed by any of this.
/// </summary>
public sealed class CellStepEase
{
    /// <summary>The most one tick's change of height may be for it to be a step: the cook's agent climbs 0.4 m (<see cref="NavSettings.Default"/>). More is a fall or a move by a script, shown as it is.</summary>
    public const float MaxStep = 0.4f * Formats.L2.L2Manifest.EngineUnitsPerMetre;
    /// <summary>Units of lift paid back per unit of ground covered: a little steeper than a stair of 0.17 m on 0.33 m (0.52), so the walker is level with a tread before the next riser.</summary>
    public const float Slope = 0.55f;
    /// <summary>Units of lift paid back a second whatever the walker does: a riser in a quarter of a second.</summary>
    public const float Settle = 48f;
    /// <summary>Further than this over the ground in one tick is no walk: the thing was put somewhere else, and nothing is eased.</summary>
    public const float Put = 96f;

    readonly Dictionary<WorldThing, Vector3> was = new(ReferenceEqualityComparer.Instance);
    readonly List<WorldThing> gone = [];

    /// <summary>
    /// The lift after a tick in which the thing went from <paramref name="from"/> to <paramref name="to"/>:
    /// the tick's change of height taken out, then <see cref="Slope"/> of the ground covered and
    /// <paramref name="seconds"/> of <see cref="Settle"/> paid back. A change of more than
    /// <see cref="MaxStep"/>, or a move of more than <see cref="Put"/>, clears it.
    /// </summary>
    public static float Next(float lift, Vector3 from, Vector3 to, float seconds)
    {
        float rise = to.Z - from.Z;
        float ground = Vector2.Distance(new Vector2(from.X, from.Y), new Vector2(to.X, to.Y));
        if (MathF.Abs(rise) > MaxStep || ground > Put) return 0f;
        lift -= rise;
        float pay = ground * Slope + MathF.Max(seconds, 0f) * Settle;
        lift = lift > 0f ? MathF.Max(0f, lift - pay) : MathF.Min(0f, lift + pay);
        return Math.Clamp(lift, -MaxStep, MaxStep);
    }

    /// <summary>
    /// One tick, after the things have moved and before the camera looks: every actor in the world gets its
    /// lift for this frame; one seen for the first time, or back in the world, starts with none.
    /// </summary>
    public void Step(IReadOnlyList<WorldThing> things, float seconds)
    {
        for (int i = 0; i < things.Count; i++)
        {
            var thing = things[i];
            if (!thing.IsActor || !thing.InWorld) continue;
            thing.ShownLift = was.TryGetValue(thing, out var from) ? Next(thing.ShownLift, from, thing.Position, seconds) : 0f;
            was[thing] = thing.Position;
        }
        gone.Clear();
        foreach (var thing in was.Keys)
            if (!thing.InWorld) gone.Add(thing);
        foreach (var thing in gone) { thing.ShownLift = 0f; was.Remove(thing); }
    }
}
