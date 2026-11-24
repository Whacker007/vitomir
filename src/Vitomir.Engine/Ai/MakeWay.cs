// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A companion steps out of the driven character's way. Ours, like driving itself (the QoL `driving`).
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ai;

public sealed partial class Minds
{
    /// <summary>How far a companion steps aside, a body's width and a bit (ours; the sidestep's own is 50–80).</summary>
    public const float MakeWayStep = 70f;
    /// <summary>The turn off the driver's heading it steps along, in degrees, to the side it already stands on (ours).</summary>
    public const float MakeWayTurn = 70f;
    /// <summary>How long before the same companion is asked again, and before it thinks of going back (ours).</summary>
    public const int MakeWayEvery = 500;

    readonly Dictionary<Mind, int> madeWay = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// **A companion in the driven character's way steps aside** (ours): driving mode
    /// needs some kind of minor push, at least for party characters, to get them
    /// out of the way; a click to move moves them, but not in driving
    /// mode. 1.1 has no driving: a click's route walks round a body (the detour), and the keys,
    /// which are Vitomir's, simply stopped against one. So when the keys push the steered character
    /// into a party member who is doing nothing — alive, not steered, not paused, with no target and
    /// free to take a move order — that member is sent, at a walk, <see cref="MakeWayStep"/> units
    /// off to the side it already stands on (<see cref="MakeWayTurn"/> degrees off the push), or to the
    /// other side, or straight on, whichever has ground and nobody on it; the driver walks through
    /// the gap. One ask a member every <see cref="MakeWayEvery"/> ms. It draws no random number, so
    /// the AI's rolls are what they were. Nobody else is moved: not an enemy, a townsman or a member
    /// in a fight.
    /// </summary>
    /// <returns>Whether the companion was sent.</returns>
    public bool MakeWay(WorldThing driver, WorldThing blocker, Vector2 push)
    {
        if (ReferenceEquals(blocker, driver) || ReferenceEquals(blocker, Steered)) return false;
        if (blocker.Mind is not { } mind || blocker.Actor is not { } who) return false;
        if (!who.Has(Actor.Flag.Party) || who.Has(Actor.Flag.Dead) || who.Has(Actor.Flag.AiPaused)) return false;
        if ((MainFlags & AiMainFlag.Paused) != 0 || mind.Paused || mind.Target is not null) return false;
        if (push.LengthSquared() < 1e-6f) return false;
        if (madeWay.TryGetValue(mind, out int asked) && Now - asked < MakeWayEvery) return false;

        var ahead = new Vector3(Vector2.Normalize(push), 0f);
        var off = blocker.Position - driver.Position;
        float side = ahead.X * off.Y - ahead.Y * off.X >= 0f ? 1f : -1f;     // the side of the push it stands on
        foreach (float turn in (ReadOnlySpan<float>)[side * MakeWayTurn, -side * MakeWayTurn, 0f])
        {
            var spot = blocker.Position + Turned(ahead, turn) * MakeWayStep;
            int node = walk?.Beneath(spot) ?? -1;
            if (node < 0 || !Go(mind, spot, node, run: false)) continue;
            madeWay[mind] = Now;
            mind.NextThink = Math.Max(mind.NextThink, Now + MakeWayEvery);
            return true;
        }
        return false;
    }
}
