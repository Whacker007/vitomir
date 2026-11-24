// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **Whether a ranged attacker has a clear shot** — read whole and
/// asked by both AI fights before a ranged attack (the party's and the pet's). A
/// blocked shot makes the shooter reposition instead of loosing.
///
/// The line runs from the weapon's muzzle (<see cref="Projectiles.WeaponMuzzle"/>) to the
/// middle of the target's body (the same half-height the shot itself aims at), and it is
/// cast through the collision query with **things only** — the flag word is set to 1 with bit 1, the
/// surfaces, cleared — and a type mask of `0xffffff7e`, which
/// excludes every type but **0 and 7, the characters**. The shooter itself is the query's own
/// exclusion. The query's usual filter also passes over anything
/// with `THING_FLAG_DISABLECOLLIDE` and any thing with no collide shape, and — since this query does
/// not set its bit 4 — does **not** pass over the dead. The first result decides: nothing,
/// or the target itself, is clear; anybody else is in the way.
///
/// So in 1.1 **a body blocks a shot and a wall does not.** A companion, an enemy or a corpse standing
/// nearer on the line than the target makes the archer step round; a pillar between them does not,
/// and the arrow is loosed into the stone. The sight test that picked the target in the first place
/// is the one that knows about walls. Vitomir asked <see cref="Sight.Sees"/> — walls,
/// never bodies — earlier, which was 1.1's rule inverted.
///
/// The line is the engine's own sweep (<see cref="WorldSweep"/>), cast from the
/// shooter's sector with the sphere, cylinder and box tests. Until then
/// it was ours in method — <see cref="Pick.Trace"/>, an exact ray against each body's standing
/// cylinder, where the engine's cylinder test is its own approximate one.
/// </summary>
public static class ClearShot
{
    /// <summary>The query's type mask: every type excluded but actor (0) and player (7).</summary>
    public const uint TypeMask = 0xffffff7e;

    /// <summary>Whether <paramref name="shooter"/> has a clear shot at <paramref name="target"/>.</summary>
    public static bool Clear(WorldThing shooter, WorldThing target)
    {
        if (shooter.Place is not { } place) return true;
        var from = shooter.Actor?.Weapon is { } weapon ? Projectiles.WeaponMuzzle(shooter, weapon) : shooter.Position;
        var to = Projectiles.Aim(target);
        var line = to - from;
        float length = line.Length();
        if (length <= 0f) return true;
        // The first result decides — none, or the target, is clear.
        var met = WorldSweep.Line(place.Level, place.Things.Where(t => t.Actor is not null), (int)shooter.Sector, from, line * (1f / length), length,
                                  new WorldSweep.Asks(Pick.Tests.Things, Ignore: shooter, TypeMask: TypeMask));
        return met.Count == 0 || ReferenceEquals(met[0].Thing, target);
    }

    /// <summary>
    /// What the query can be stopped by: a character (the type mask), with a collide shape, and
    /// without `THING_FLAG_DISABLECOLLIDE`. Dead or alive.
    /// </summary>
    public static bool Blocks(WorldThing thing) =>
        (TypeMask & (1u << ((int)thing.Type & 0x1f))) == 0
        && thing.Collide != CollideShape.None
        && !thing.Has(WorldThing.Flag.DisableCollide);
}
