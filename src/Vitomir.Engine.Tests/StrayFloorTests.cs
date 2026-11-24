// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Where a dazed or frightened character strays to** (the dazed handler and the fear's,
/// through the ground lookup with a reach of 100), found by a comparison with 1.1. The
/// engine asks for the floor under the point 50 up the way the spawn does — the character's own
/// surface at any height if the point is over it, else its sector's faces 0..128 below, else the
/// point's sector by a ray 228 down, never a floor above — and orders the walk to the floor point it
/// found. Vitomir asked the walk map for the highest node 100 below to 16 above.
/// </summary>
public class StrayFloorTests
{
    static (Location Place, Minds Minds, WorldThing Christof, Mind Mind) Open(Vfs.GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Steered!;
        var mind = minds.All.First(m => !ReferenceEquals(m.Thing, christof) && m.Thing.Actor is not null
                                        && m.Class.MindFlags == 0 && (m.Flags & Mind.FlagUnaffected) == 0);
        return (place, minds, christof, mind);
    }

    /// <summary>
    /// A point over the character's own surface is on that surface whatever its height — the first
    /// loop of the ground lookup — where the walk map's search (100 below, 16 above) finds nothing.
    /// </summary>
    [Fact]
    public void The_floor_under_a_point_over_the_characters_own_surface_is_found_at_any_height()
    {
        using var files = Install.Open();
        var (place, _, _, mind) = Open(files);
        var feet = mind.Thing.Position;
        var high = feet + new Vector3(0f, 0f, 300f);
        Assert.Equal(-1, place.Walk!.Find(high, Minds.StrayReach));
        var floor = place.FloorUnder(high, mind.Thing, Minds.StrayReach);
        Assert.NotNull(floor);
        Assert.Equal(feet.Z, floor!.Value.Z, 2f);
        Assert.Equal(high.X, floor.Value.X, 0.01f);
    }

    /// <summary>
    /// A dazed townsperson strays within a couple of thinks, and each walk it is given ends on a floor
    /// no higher than the point 50 above where it stood.
    /// </summary>
    [Fact]
    public void A_dazed_character_strays_onto_a_floor_below_the_lifted_point()
    {
        using var files = Install.Open();
        var (_, minds, christof, mind) = Open(files);
        christof.Position = mind.Thing.Position + new Vector3(200f, 0f, 0f);
        minds.Daze(mind, christof, force: true);
        int walks = 0;
        for (int i = 0; i < 20 && walks == 0; i++)
        {
            float z = mind.Thing.Position.Z;
            minds.Tick(0.25f);
            if (!mind.Walker.Walking) continue;
            walks++;
            Assert.True(mind.Walker.Destination.Z <= z + Minds.StrayLift + 0.01f,
                        $"stray goal {mind.Walker.Destination} above the lifted point from {z}");
        }
        Assert.Equal(AiMode.Dazed, mind.Mode);
        Assert.True(walks > 0, "the dazed character never strayed");
    }
}
