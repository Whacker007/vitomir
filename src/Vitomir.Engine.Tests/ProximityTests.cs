// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Who touches a proximity trigger** (<see cref="Proximity"/>) — found by
/// A comparison with 1.1. 1.1 asks with the template's `PROXRADIUS` (tag 234), for the
/// characters only (the type mask 0xffffff7e), never the trigger itself, and
/// hands them out nearest first. Vitomir had asked with the body's radius, for every
/// type, the trigger first — so the satchel charge, whose script explodes at any toucher, went off
/// on its own the moment it armed, and the spider's egg sac (250 against a body of 33) burst only
/// when walked into. The shipped values are pinned from the install; the answers in the first
/// monastery's hall, Christof where the scene puts him.
/// </summary>
public class ProximityTests
{
    static (Location Place, WorldThing Christof) Hall()
    {
        var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        return (place, place.Player!.Thing);
    }

    static WorldThing Put(Location place, string template, Vector3 at, long sector) =>
        place.Place(template, at, Vector3.Zero, sector) ?? throw new InvalidOperationException(template);

    [Fact]
    public void The_shipped_triggers_reach_much_further_than_their_bodies()
    {
        var (place, christof) = Hall();
        var sac = Put(place, "spiderSack", christof.Position, christof.Sector);
        var satchel = Put(place, "satchelCharge", christof.Position, christof.Sector);
        Assert.Equal(250f, sac.ProxRadius);
        Assert.Equal(33f, sac.Radius);
        Assert.Equal(128f, satchel.ProxRadius);
        Assert.Equal(6f, satchel.Radius);
    }

    [Fact]
    public void A_trigger_reaches_as_far_as_its_PROXRADIUS_and_no_further()
    {
        var (place, christof) = Hall();
        // A cylinder (the satchel's collide 2) against Christof's: across the ground, reach plus his radius.
        var edge = 128f + christof.Radius;
        var near = Put(place, "satchelCharge", christof.Position + new Vector3(edge - 4f, 0f, 0f), christof.Sector);
        var far = Put(place, "satchelCharge", christof.Position + new Vector3(0f, edge + 4f, 0f), christof.Sector);
        Assert.Contains(christof, Proximity.Touching(place, near));
        Assert.DoesNotContain(christof, Proximity.Touching(place, far));
    }

    [Fact]
    public void Only_characters_touch_and_never_the_trigger_itself()
    {
        var (place, christof) = Hall();
        var satchel = Put(place, "satchelCharge", christof.Position + new Vector3(40f, 0f, 0f), christof.Sector);
        var other = Put(place, "satchelCharge", christof.Position + new Vector3(60f, 0f, 0f), christof.Sector);
        var touching = Proximity.Touching(place, satchel);
        Assert.Contains(christof, touching);
        Assert.DoesNotContain(satchel, touching);
        Assert.DoesNotContain(other, touching);
        Assert.All(touching, t => Assert.True(t.Type is ThingType.Actor or ThingType.Player));
    }

    [Fact]
    public void The_nearest_toucher_is_told_first()
    {
        var (place, christof) = Hall();
        // The thrown satchel is a sphere (collide 1): its answers carry their distance across the ground.
        var wilhem = Put(place, "wilhem", christof.Position + new Vector3(60f, 0f, 0f), christof.Sector);
        var shot = Put(place, "proj_satchel", christof.Position + new Vector3(100f, 0f, 0f), christof.Sector);
        var touching = Proximity.Touching(place, shot);
        Assert.True(touching.IndexOf(wilhem) >= 0 && touching.IndexOf(christof) > touching.IndexOf(wilhem));
    }
}
