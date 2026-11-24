// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The party's travel, member by member** (a comparison with 1.1
/// gap). Leaving, a member who is not staked has its order cancelled, forced, and its
/// action cleared — nothing it was doing crosses the door; Vitomir only dropped the target.
/// Arriving, a member with no start of its own stands where the leader arrived — Vitomir's own, a
/// repair (<see cref="Repairs.MemberStart"/>), where 1.1 puts it at the map's origin.
/// </summary>
public class PartyTravelTests
{
    static Location Open(GameFiles files) =>
        Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");

    [Fact]
    public void A_member_leaving_drops_its_order_even_when_busy_unless_staked()
    {
        using var files = Install.Open();
        var place = Open(files);
        var who = place.Player!.Thing;
        var me = who.Actor!;
        var sword = place.Place("broadsword", who.Position + new Vector3(400f, 0f, 0f), default, who.Sector)!;
        me.Action = ActionMode.Take;
        me.Target = sword;
        me.BusyUntil = place.Milliseconds + 5_000;           // a flinch: an unforced cancel would keep it

        PartyTravel.Leave(who);
        Assert.Equal(ActionMode.None, me.Action);
        Assert.Equal(0, me.BusyUntil);

        // Staked, it keeps what it has (the staked flag 0x200 is asked first).
        me.Flags |= Actor.Flag.Staked;
        me.Action = ActionMode.Staked;
        PartyTravel.Leave(who);
        Assert.Equal(ActionMode.Staked, me.Action);
    }

    [Fact]
    public void An_order_does_not_cross_the_door()
    {
        var dir = GameFiles.Locate();
        Assert.SkipWhen(dir is null, "no game install");
        using var files = new GameFiles(dir!);
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        var chronicle = where.Chronicle;
        chronicle.Current = chronicle.Find(where.Entry!.Name);
        var classes = new AiClasses(files);
        Location Build(string name, string? scene, int entrance)
        {
            var place = Location.Load(files, name, sky: false, scene: scene, player: "Christof", entrance: entrance);
            place.Awaken(classes, 1);
            return place;
        }
        var here = Location.Load(files, "P1_MON1", sky: false, scene: where.Entry.SceneName, player: "Christof");
        here.Awaken(classes, 1);
        var christof = here.Player!.Thing;
        christof.Actor!.Flags |= Actor.Flag.Party;
        christof.Actor.Action = ActionMode.Take;
        christof.Actor.BusyUntil = here.Milliseconds + 5_000;

        var campaign = new Campaign { Chronicle = chronicle, Build = Build, Here = here };
        Assert.True(campaign.Chronicle.TakeExit(0));
        Assert.NotNull(campaign.Travel());
        var arrived = campaign.Here.Player!.Thing;
        Assert.Same(christof.Actor, arrived.Actor);
        Assert.Equal(ActionMode.None, arrived.Actor!.Action);
        Assert.Null(arrived.Actor.Target);
    }

    /// <summary>
    /// How 1.1 places a member with no start: the fallback in case there is no spawn point
    /// is a good fail-closed catch. The member one past the entrance's starts arrives on the
    /// leader; one with a start of its own arrives on it.
    /// </summary>
    [Fact]
    public void A_member_with_no_start_of_its_own_arrives_on_the_leader()
    {
        using var files = Install.Open();
        var place = Open(files);
        var leader = place.Player!.Thing;
        Assert.NotEmpty(place.Arrivals);
        int past = place.Arrivals.Length;
        Assert.Equal(leader.Position, PartyTravel.Arrival(place, past, leader));
        Assert.NotEqual(Vector3.Zero, PartyTravel.Arrival(place, past, leader));
        Assert.Equal(place.Arrivals[0].Position, PartyTravel.Arrival(place, 0, leader));
        Assert.Contains(Repairs.All, r => r.Name == Repairs.MemberStart);
    }
}

/// <summary>
/// **The selected member leaving a party whose others are all down** (`RemovePlayer`'s branch for
/// it; a comparison with 1.1's gap): the search for the next member standing finds
/// nobody — and asks for the party's end — and then the first member left is revived at a tenth
/// and taken over. Vitomir ended the party and left the rest dead.
/// </summary>
public class PartyLeavingTests
{
    [Fact]
    public void The_one_standing_leaving_revives_the_first_member_left()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var lead = place.Player!.Thing;
        var wilhem = place.Place("Wilhem", lead.Position + new Vector3(96f, 0f, 0f), lead.Orientation, lead.Sector)!;
        wilhem.Actor!.Flags |= Actor.Flag.Party;
        lead.Actor!.Flags |= Actor.Flag.Party;
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        Assert.True(place.Steer(wilhem));                         // steering Wilhem
        Damage.Kill(lead, killer: null, HitKind.Melee, DamageType.Lethal, place.Milliseconds);
        Assert.True(lead.Actor.Dead);                             // Christof down

        wilhem.Actor.Flags &= ~Actor.Flag.Party;                  // a script's RemoveFromParty
        Assert.Same(lead, PartyLeaving.Left(place, wilhem));

        Assert.False(lead.Actor.Dead);
        Assert.Equal(PartyLeaving.ReviveFraction * lead.Actor.MaxHealth, lead.Actor.Health, 3);
        Assert.Same(lead, place.Selected);
        Assert.Same(lead, place.Steered);
    }

    [Fact]
    public void With_somebody_standing_the_next_one_simply_takes_over()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var lead = place.Player!.Thing;
        var wilhem = place.Place("Wilhem", lead.Position + new Vector3(96f, 0f, 0f), lead.Orientation, lead.Sector)!;
        wilhem.Actor!.Flags |= Actor.Flag.Party;
        lead.Actor!.Flags |= Actor.Flag.Party;
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        Assert.True(place.Steer(wilhem));
        float health = lead.Actor.Health;

        wilhem.Actor.Flags &= ~Actor.Flag.Party;
        Assert.Same(lead, PartyLeaving.Left(place, wilhem));
        Assert.Equal(health, lead.Actor.Health);
        Assert.False(lead.Actor.Dead);
    }
}
