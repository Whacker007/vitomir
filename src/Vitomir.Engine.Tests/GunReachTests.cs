// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The Fix `gunreach`** (ours). An attacker may fire when the
/// gap between the two bodies is within the weapon's reach, and the shot is a ray of the
/// projectile's range from the muzzle — which stands less far forward than the shooter's
/// radius, so from the very edge of the reach the ray ends short of the target. The Fix lengthens the
/// ray by the shooter's radius and <see cref="Fixes.GunReachSlack"/>.
/// </summary>
public class GunReachTests
{
    /// <summary>A shot from the edge of the reach: the bodies' gap exactly the bullet's range, the muzzle 24 ahead of the shooter.</summary>
    static (WorldThing? Hit, WorldThing Target) Fire(Vfs.GameFiles files, bool fix)
    {
        var place = Location.Load(files, "P1_OLDT", sky: false, scene: "OLDT_7_1", player: "Christof");
        place.Fixes = new Fixes { GunReach = fix };
        var christof = place.Player!.Thing;
        // Whichever way the street runs clear for 400 units at the muzzle's height.
        var toward = Enumerable.Range(0, 16)
            .Select(k => new Vector3(MathF.Cos(k * MathF.PI / 8f), MathF.Sin(k * MathF.PI / 8f), 0f))
            .First(d => WorldSweep.FirstSurface(place.Level, (int)christof.Sector, christof.Position + new Vector3(0f, 0f, 60f), d, 400f) is null);
        var target = place.Place("cappadocian", christof.Position + toward * 300f, default, place.At(christof.Position + toward * 300f + new Vector3(0f, 0f, 40f), (int)christof.Sector))!;
        var muzzle = christof.Position + toward * 24f + new Vector3(0f, 0f, 60f);
        var shot = place.Place("proj_bullet_9mm", muzzle, Projectiles.Facing(toward), christof.Sector)!;
        shot.SpawnedBy = christof.Guid;
        shot.ProjectileRange = 300f - christof.Radius - target.Radius;          // the reach test's gap, to the unit
        Assert.True(christof.Radius > 24f, "the muzzle is not inside the shooter's radius");
        return (Projectiles.Hitscan(shot, new Ai.Rng(1)), target);
    }

    [Fact]
    public void As_1_1_has_it_the_shot_from_the_edge_of_the_reach_falls_short()
    {
        using var files = Install.Open();
        Assert.Null(Fire(files, fix: false).Hit);
    }

    [Fact]
    public void With_the_fix_it_hits()
    {
        using var files = Install.Open();
        var (hit, target) = Fire(files, fix: true);
        Assert.Same(target, hit);
    }
}
