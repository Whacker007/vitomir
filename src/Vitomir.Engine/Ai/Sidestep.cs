// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The sidestep of a companion or a pet whose shot is blocked, which is
// not the enemy's manoeuvre (the enemy mode's tail), and the target's own walk node that the enemy's
// manoeuvre falls back on. Found by a comparison with 1.1 (
// gaps 3 and 8) and built.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ai;

public sealed partial class Minds
{
    /// <summary>What the sidestep sets the next think to before it looks at anything (250 ms).</summary>
    public const int SidestepFirstThink = 250;
    /// <summary>
    /// And after it has stepped, or when it is already touching its target and does not (both
    /// 500 ms).
    /// </summary>
    public const int SidestepThink = 500;
    /// <summary>When there is no ground under the spot it picked (100 ms).</summary>
    public const int SidestepNoGround = 100;
    /// <summary>The spread of the turn off the line to the target, in degrees (50).</summary>
    public const float SidestepArc = 50f;
    /// <summary>The smallest turn, in degrees (40): so the turn is 40–90° to one side or the other.</summary>
    public const float SidestepFloorAngle = 40f;
    /// <summary>The coin that picks the side (0.5): under it, the turn is the other way.</summary>
    public const float SidestepCoin = 0.5f;
    /// <summary>The spread of the step (30), itself scaled by a second roll.</summary>
    public const float SidestepSpread = 30f;
    /// <summary>The shortest step (50): so a step is 50–80 units, and nearer 50 than 80.</summary>
    public const float SidestepFloor = 50f;

    /// <summary>
    /// **A companion or a pet with no clear shot steps aside**, called by
    /// the party's combat decision and the pet's
    /// when the current attack is ranged, the target is within its reach and a body stands in the
    /// line. It is not the enemy's manoeuvre (<see cref="Manoeuvre"/>), which Vitomir
    /// ran here until then; the two differ in every number.
    ///
    /// <list type="number">
    /// <item>The next think is a quarter second off before anything is looked at.</item>
    /// <item>A target that is gone, dead (flag `0x10000000`) or friendly is
    /// dropped and the mind goes back to its base mode (the set-target with nothing, then
    /// the home mode). The combat decision has just tested the same things, so this is a
    /// guard rather than a path anybody takes.</item>
    /// <item>With the heading worked out, a character already **touching** its target —
    /// the distance less both radii below nought — does not move and thinks again
    /// in half a second, without a roll.</item>
    /// <item>Otherwise it turns the line to the target about the vertical by **40–90°**
    /// (`rand × 50 + 40`), to the other side when a second roll falls under a half; steps
    /// `rand × (rand × 30) + 50` along it — **50 to 80 units** — from where it stands; finds the
    /// ground under that spot; refreshes the **target's** walk surface (on the
    /// target, no visible effect here); and, with ground, **walks** there with the move order's
    /// flags `0x20` (a walk, and a spot somebody stands on is refused because `0x100`
    /// is not set) and thinks again in half a second. With no ground it does nothing and thinks
    /// again in a tenth.</item>
    /// </list>
    ///
    /// So a blocked archer shuffles sideways a body's width or two, at a walk, twice a second,
    /// until the line clears — where the manoeuvre turned ±30°, stepped out as far as the class's
    /// `chaseDist`, ran by its attack flags, taunted one time in five and waited the class's
    /// `moveRate`. Four rolls when it steps, none when it is touching.
    ///
    /// 1.1's sidestep takes one argument it never uses (it is always
    /// 0).
    /// </summary>
    public void Sidestep(Mind mind, WorldThing target)
    {
        mind.NextThink = Now + (int)Rules.GameRules.Current.Ai.Sidestep[0];
        if (target.Removed || target.Actor?.Has(World.Actor.Flag.Dead) == true
            || Rpg.Sight.Friendly(mind.Thing, target))
        {
            mind.Aim(null, Now);
            mind.Enter(mind.BaseMode, andBase: false);
            return;
        }

        // The heading: the three-dimensional line to the target and its length.
        var to = target.Position - mind.Thing.Position;
        float gap = to.Length();
        float reach = gap - target.Radius - mind.Thing.Radius;
        // Touching (or on top of it, where the line has no direction — ours, the engine would
        // normalise a zero vector): no step, no roll.
        if (reach < 0f || gap <= 1e-3f) { mind.NextThink = Now + (int)Rules.GameRules.Current.Ai.Sidestep[1]; return; }
        var unit = to / gap;

        float turn = Random.Unit() * Rules.GameRules.Current.Ai.Sidestep[4] + Rules.GameRules.Current.Ai.Sidestep[3];
        if (Random.Unit() < Rules.GameRules.Current.Ai.Sidestep[7]) turn = -turn;
        var heading = Turned(unit, turn);
        float spread = Random.Unit() * Rules.GameRules.Current.Ai.Sidestep[6];
        float step = Random.Unit() * spread + Rules.GameRules.Current.Ai.Sidestep[5];

        var spot = mind.Thing.Position + heading * step;
        int node = walk?.Beneath(spot) ?? -1;
        if (node < 0) { mind.NextThink = Now + (int)Rules.GameRules.Current.Ai.Sidestep[2]; return; }
        Go(mind, spot, node, run: false);
        mind.NextThink = Now + (int)Rules.GameRules.Current.Ai.Sidestep[1];
    }

    /// <summary>
    /// A direction turned about the vertical by <paramref name="degrees"/>, its height
    /// kept — so a spot along it climbs or drops as the line to the target does.
    /// </summary>
    static Vector3 Turned(Vector3 unit, float degrees)
    {
        float radians = degrees * (MathF.PI / 180f);
        float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
        return new Vector3(unit.X * cos - unit.Y * sin, unit.X * sin + unit.Y * cos, unit.Z);
    }

    /// <summary>
    /// The walk node a thing stands on, — what the enemy's manoeuvre hands the move
    /// order when it falls back on walking at its target. The engine reads the target's
    /// cached surface, refreshed just before; here that is the node its walker is on, and
    /// for the steered character — whose mind's walker does not move it — or a thing with no walker
    /// on a node, the node under its feet (<see cref="World.WalkMap.Beneath"/>, ours).
    /// </summary>
    int SurfaceOf(WorldThing thing)
    {
        if (!ReferenceEquals(thing, Steered) && thing.Locomotion is { Node: >= 0 } walker) return walker.Node;
        return walk?.Beneath(thing.Position) ?? -1;
    }
}
