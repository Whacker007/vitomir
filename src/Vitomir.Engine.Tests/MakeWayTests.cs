// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A companion steps out of the driven character's way** (ours). Driven
/// with the keys straight at Wilhem standing in front of him, Christof used to stop against him for
/// good; now Wilhem walks aside and Christof goes through. An enemy in the same place is not moved.
/// </summary>
public class MakeWayTests
{
    static (Location Place, Simulation Sim, WorldThing Christof, WorldThing Other) Stand(Vfs.GameFiles files, string template, bool party)
    {
        var place = Location.Load(files, "P1_OLDT", sky: false, scene: "OLDT_7_1", player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Player!.Thing;
        christof.Actor!.Flags |= Actor.Flag.Party;
        var other = place.Place(template, christof.Position + new Vector3(70f, 0f, 0f), default, christof.Sector)!;
        if (party)
        {
            other.Actor!.Flags |= Actor.Flag.Party;
            other.Actor.Flags2 |= Actor.Flag2.InFormation;
        }
        else other.Actor!.Flags |= Actor.Flag.AiPaused;                    // it stands where it is put
        place.Minds!.Adopt(other);
        return (place, new Simulation(place), christof, other);
    }

    static float Drive(Location place, Simulation sim, int frames)
    {
        var p = place.Player!;
        float start = p.Thing.Position.X;
        for (int i = 0; i < frames; i++)
        {
            p.Running = true;
            p.Move(new Vector2(1f, 0f), 1f / 30f);
            sim.Step(1f / 30f);
        }
        return p.Thing.Position.X - start;
    }

    [Fact]
    public void A_companion_in_the_way_of_the_keys_steps_aside_and_the_driver_goes_through()
    {
        using var files = Install.Open();
        var (place, sim, christof, wilhem) = Stand(files, "Wilhem", party: true);
        var stood = wilhem.Position;
        float went = Drive(place, sim, 90);
        Assert.True(went > 200f, $"Christof got {went:0} units in three seconds");
        Assert.True(MathF.Abs(wilhem.Position.Y - stood.Y) > 20f || christof.Position.X > stood.X + 60f,
                    "Wilhem stepped off the line, or Christof is past where he stood");
    }

    [Fact]
    public void Somebody_who_is_not_of_the_party_is_not_moved()
    {
        using var files = Install.Open();
        var (place, sim, christof, guard) = Stand(files, "knightofstjohn", party: false);
        var stood = guard.Position;
        float went = Drive(place, sim, 90);
        Assert.True(Vector3.Distance(guard.Position, stood) < 1f, "the knight stands where he stood");
        Assert.True(went < 70f, $"and Christof is stopped by him ({went:0} units)");
    }
}
