// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A party back in a place still loaded** (a crash seen in play). A journey rebuilds each
/// companion from its template, and a place kept loaded keeps the minds of the ones who left it: out
/// of the world, but still in the place's list and still marked as the party's. Vitomir counted a
/// member's slot in the formation (the follow's party slot) over that list, so every return to the Old
/// Town pushed the companions' slots up by the party's size, and on the third the slot ran off the
/// end of the six-slot fan — an unhandled error, coming back from the Jewish Quarter. 1.1's slot is the
/// member's place in the party roster, at most four long.
/// </summary>
public class ReturnedPartyTests
{
    /// <summary>A second in the place, so the minds that arrived join its list (<see cref="Minds.Begin"/>).</summary>
    static void Settle(Location place)
    {
        var sim = new Simulation(place);
        for (int i = 0; i < 30; i++) sim.Step(1f / 30f);
    }

    [Fact]
    public void Companions_back_in_a_place_still_loaded_take_the_first_slots_again()
    {
        using var files = Install.Open();
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        var classes = new AiClasses(files);
        var chronicle = ChronicleSet.Load(files)["prague"]!;
        var session = new Session { Chronicle = chronicle };
        Location Build(string name, string? scene, int entrance)
        {
            var place = Location.Load(files, name, sky: false, scene: scene, player: "Christof", entrance: entrance);
            place.Session = session;
            place.Awaken(classes, 1, effects, powers);
            return place;
        }
        chronicle.Current = chronicle.Find("OldTown");
        var at = chronicle.At(chronicle.Current)!;
        var here = Build(at.LevelName, at.SceneName.Length > 0 ? at.SceneName : null, -1);
        session.Here = here;
        var christof = here.Player!.Thing;
        christof.Actor!.Flags |= Actor.Flag.Party;
        foreach (var name in new[] { "Wilhem", "Serena" })
        {
            var who = here.Place(name, christof.Position + new Vector3(60f, 0f, 0f), default, christof.Sector)!;
            who.Actor!.Flags |= Actor.Flag.Party;
            who.Actor.Flags2 |= Actor.Flag2.InFormation;
            here.Minds!.Adopt(who);
        }
        var campaign = new Campaign { Chronicle = chronicle, Build = Build, Here = here };

        for (int trip = 0; trip < 4; trip++)
        {
            Assert.True(campaign.Chronicle.Jump("NQuarter", 0));           // to the Jewish Quarter
            Assert.NotNull(campaign.Travel());
            Settle(campaign.Here);
            Assert.True(campaign.Chronicle.Jump("OldTown", 11));           // and back to the Old Town
            Assert.True(campaign.Travel()!.Value.Returned);
            Settle(campaign.Here);
        }

        var oldTown = campaign.Here;
        var leader = oldTown.Player!.Thing;
        var party = oldTown.PartyMembers().ToList();
        Assert.Equal(3, party.Count);
        Assert.True(oldTown.Minds!.All.Count(m => m.Thing.Actor?.Has(Actor.Flag.Party) == true) > 3,
                    "the minds of the companions who left are still in the place's list");

        // Each companion far enough from Christof to walk after him: the follow picks its slot.
        var sim = new Simulation(oldTown);
        foreach (var member in party.Skip(1)) member.Position = leader.Position + new Vector3(0f, 900f, 0f);
        for (int i = 0; i < 60; i++) sim.Step(1f / 30f);
        Assert.Equal([0, 1], party.Skip(1).Select(m => Minds.PartySlotOf(oldTown, m)));
    }

    /// <summary>
    /// **The leader steered again in a place still loaded** ("Christof would
    /// move forward but have the standing still animation"). Leave the Old Town steering Wilhem, take
    /// Christof back in the Jewish Quarter, and come home: the Old Town's minds still named the Wilhem
    /// who left it, out of the world, and took the Christof the keys were driving for his follower, so
    /// he slid along in his stand. Who is steered is the engine's, not the place's
    /// (<see cref="Location.Reenter"/>).
    /// </summary>
    [Fact]
    public void A_place_still_loaded_does_not_keep_the_companion_steered_when_the_party_left_it()
    {
        using var files = Install.Open();
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        var classes = new AiClasses(files);
        var chronicle = ChronicleSet.Load(files)["prague"]!;
        var session = new Session { Chronicle = chronicle };
        Location Build(string name, string? scene, int entrance)
        {
            var place = Location.Load(files, name, sky: false, scene: scene, player: "Christof", entrance: entrance);
            place.Session = session;
            place.Awaken(classes, 1, effects, powers);
            return place;
        }
        chronicle.Current = chronicle.Find("OldTown");
        var at = chronicle.At(chronicle.Current)!;
        var here = Build(at.LevelName, at.SceneName.Length > 0 ? at.SceneName : null, -1);
        session.Here = here;
        var christof = here.Player!.Thing;
        christof.Actor!.Flags |= Actor.Flag.Party;
        var wilhem = here.Place("Wilhem", christof.Position + new Vector3(60f, 0f, 0f), default, christof.Sector)!;
        wilhem.Actor!.Flags |= Actor.Flag.Party;
        wilhem.Actor.Flags2 |= Actor.Flag2.InFormation;
        here.Minds!.Adopt(wilhem);
        var campaign = new Campaign { Chronicle = chronicle, Build = Build, Here = here };

        Assert.True(here.Steer(wilhem));                                  // Wilhem steered out of the Old Town
        Assert.True(campaign.Chronicle.Jump("NQuarter", 0));
        Assert.NotNull(campaign.Travel());
        var quarter = campaign.Here;
        Assert.True(quarter.Steer(quarter.Player!.Thing));                // Christof taken back there
        Settle(quarter);
        Assert.True(campaign.Chronicle.Jump("OldTown", 11));
        Assert.True(campaign.Travel()!.Value.Returned);

        var oldTown = campaign.Here;
        var leader = oldTown.Player!;
        Assert.Same(leader.Thing, oldTown.Steered);
        Assert.Same(leader.Thing, oldTown.Selected);
        Assert.Same(leader, oldTown.SteeredWalker);

        // And driven, he runs: the run's frames go on rather than a stand.
        var sim = new Simulation(oldTown);
        for (int i = 0; i < 10; i++)
        {
            leader.Running = true;
            leader.Move(new Vector2(1f, 0f), 1f / 30f);
            sim.Step(1f / 30f);
        }
        var motion = leader.Thing.Animation!.Motion!;
        Assert.Equal("run", motion.Mode.Name);
        Assert.True(motion.Player.Frame > 5f, $"the run is at frame {motion.Player.Frame}");
    }
}
