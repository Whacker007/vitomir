// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Save;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The teleporters through the engine, with no scripts: the pair Walk the Abyss makes,
/// the two ends used, the journey of state 6 (the arrival with −2),
/// the closing rules and the save. The shipped scripts driving the same thing from a 1.1 save are
/// in `Vitomir.Scripting.Tests.TeleporterScriptTests`.
/// </summary>
public class TeleportTests
{
    sealed class Game
    {
        public required GameFiles Files { get; init; }
        public required Chronicle Chronicle { get; init; }
        public required Campaign Campaign { get; init; }
        public required Session Session { get; init; }
        public Location Here => Campaign.Here;
        public WorldThing Leader => Here.Player!.Thing;
        public Teleports Table => Session.Teleports;
        public int Haven => Chronicle.Portal.Location;

        /// <summary>The main loop's half: the journey, and the place handed the session as the host does.</summary>
        public Campaign.Arrival? Travel()
        {
            var arrival = Campaign.Travel();
            if (arrival is { } a) { a.To.Session = Session; Session.Here = a.To; }
            return arrival;
        }

        public static IEnumerable<WorldThing> Portals(Location place) =>
            place.Things.Where(t => !t.Removed && t.Template.Equals("havenPortal", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The monastery (chapter 8), Christof in it as the party's leader, a campaign and a session.</summary>
    static Game Open(GameFiles files, bool companion = false)
    {
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        var chronicle = where.Chronicle;
        chronicle.Current = where.Location;
        Location Build(string level, string? scene, int entrance) =>
            Location.Load(files, level, sky: false, scene: scene, player: "Christof", entrance: entrance);
        var here = Build("P1_MON1", where.Entry!.SceneName, -1);
        here.Player!.Thing.Actor!.Flags |= Actor.Flag.Party;
        if (companion && here.Place("Wilhem", here.Player.Thing.Position + new Vector3(60, 0, 0), Vector3.Zero, here.Player.Thing.Sector) is { } wilhem)
            wilhem.Actor!.Flags |= Actor.Flag.Party;
        var session = new Session { Here = here, Chronicle = chronicle };
        here.Session = session;
        var campaign = new Campaign { Chronicle = chronicle, Build = Build, Here = here };
        session.Teleports.Campaign = campaign;
        return new Game { Files = files, Chronicle = chronicle, Campaign = campaign, Session = session };
    }

    static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));

    /// <summary>The placing's "just clear of each other": the two radii, each counted when it has a shape, times 1.1.</summary>
    static float Clear(WorldThing a, WorldThing b) =>
        ((a.Collide != CollideShape.None ? a.Radius : 0f) + (b.Collide != CollideShape.None ? b.Radius : 0f)) * 1.1f;

    [Fact]
    public void Walk_the_Abyss_opens_a_portal_beside_the_caster_and_its_twin_on_the_haven_s_portal_start()
    {
        using var files = Install.Open();
        var g = Open(files);
        var christof = g.Leader;

        Assert.True(g.Table.Create(christof, "havenPortal", "havenPortal", 0, 0));

        // The near end: one `havenPortal` beside Christof, just clear of him (flags 3).
        var near = Assert.Single(Game.Portals(g.Here));
        Assert.True(near.InWorld);
        Assert.Equal(Clear(near, christof), Flat(near.Position, christof.Position), 1f);
        Assert.Equal("Teleporter", near.Script);
        Assert.True(near.Has(WorldThing.Flag.Teleport));             // the template's THING_FLAG_TELEPORT

        // The far end: the chronicle's portal location — Prague's is the Haven, entrance 15 — loaded
        // without the party, the portal on the first (unnumbered) start of that entrance.
        Assert.Equal("Haven", g.Chronicle.NameOf(g.Haven));
        Assert.Equal(15, g.Chronicle.Portal.Entrance);
        var haven = g.Campaign.Resident[g.Haven];
        var far = Assert.Single(Game.Portals(haven));
        Assert.Equal(new Vector3(193, -616, -368), far.Position);
        // The haven's own player thing stands aside until the party comes.
        Assert.False(haven.Player!.Thing.InWorld);

        var slot = g.Table.Slots[0];
        Assert.Equal("party:Christof", slot.Creator);
        Assert.Equal((g.Haven, ThingIds.Of(haven, far)), (slot.InLocation, slot.InThing));
        Assert.Equal((g.Chronicle.Current, ThingIds.Of(g.Here, near)), (slot.OutLocation, slot.OutThing));
        Assert.Equal(0u, slot.Flags);
        Assert.Equal(0, slot.ExpireMs);
        Assert.All(g.Table.Slots.Skip(1), s => Assert.True(s.Free));
    }

    [Fact]
    public void The_near_end_takes_the_party_to_the_haven_and_stands_them_round_the_far_end()
    {
        using var files = Install.Open();
        var g = Open(files, companion: true);
        int monastery = g.Chronicle.Current;
        g.Table.Create(g.Leader, "havenPortal", "havenPortal", 0, 0);
        var near = Game.Portals(g.Here).Single();

        Assert.True(g.Table.Activate(near.Guid, g.Leader));
        // A request to the main sequence, not a journey yet (state 6).
        Assert.Equal(SequenceKind.Teleport, g.Chronicle.Pending?.Kind);
        Assert.Equal((0, 1, g.Haven), (g.Chronicle.Pending!.Value.Slot, g.Chronicle.Pending.Value.Way, g.Chronicle.Pending.Value.To));

        var arrival = g.Travel();
        Assert.NotNull(arrival);
        Assert.Equal(Teleports.Entrance, arrival!.Value.Entrance);
        Assert.Equal(2, arrival.Value.Party);
        Assert.Equal(g.Haven, g.Chronicle.Current);
        Assert.Null(g.Chronicle.Pending);
        var far = Game.Portals(g.Here).Single();
        // Everybody round the far end, each just clear of it, the leader first (placed member by member).
        var party = g.Here.PartyMembers().ToList();
        Assert.Equal(2, party.Count);
        foreach (var member in party) Assert.Equal(Clear(member, far), Flat(member.Position, far.Position), 1f);
        Assert.True(Flat(party[0].Position, party[1].Position) > 1f);
        // The arrival stands the camera where the leader is.
        Assert.Equal(g.Leader.Position, g.Here.Arrivals[0].Position);
        // Arriving puts the place on the map, and the monastery's end is still open.
        Assert.NotEqual(0u, g.Chronicle.At(g.Haven)!.Flags & Campaign.ShowInMap);
        Assert.Single(Game.Portals(g.Campaign.Resident[monastery]));
        Assert.False(g.Table.Slots[0].Free);
    }

    [Fact]
    public void The_haven_end_brings_them_back_and_the_pair_closes_behind_them()
    {
        using var files = Install.Open();
        var g = Open(files);
        int monastery = g.Chronicle.Current;
        g.Table.Create(g.Leader, "havenPortal", "havenPortal", 0, 0);
        var near = Game.Portals(g.Here).Single();
        var nearAt = near.Position;
        g.Table.Activate(near.Guid, g.Leader);
        g.Travel();
        var far = Game.Portals(g.Here).Single();

        Assert.True(g.Table.Activate(far.Guid, g.Leader));
        // The far end goes at once, before anybody has moved, and the near one is marked
        // never to be saved; the slot is marked used.
        Assert.True(far.Removed);
        Assert.True(near.Has(WorldThing.Flag.SaveNever));
        Assert.NotEqual(0u, g.Table.Slots[0].Flags & Teleports.Used);
        Assert.Equal((0, 0, monastery), (g.Chronicle.Pending!.Value.Slot, g.Chronicle.Pending.Value.Way, g.Chronicle.Pending.Value.To));

        Assert.NotNull(g.Travel());
        Assert.Equal(monastery, g.Chronicle.Current);
        // Stood round where the near end was, which then goes too, and the slot is free.
        Assert.Equal(Clear(g.Leader, near), Flat(g.Leader.Position, nearAt), 1f);
        Assert.True(near.Removed);
        Assert.Empty(Game.Portals(g.Here));
        Assert.Empty(Game.Portals(g.Campaign.Resident[g.Haven]));
        Assert.All(g.Table.Slots, s => Assert.True(s.Free));
    }

    [Fact]
    public void A_second_cast_shuts_the_first_pair()
    {
        using var files = Install.Open();
        var g = Open(files);
        g.Table.Create(g.Leader, "havenPortal", "havenPortal", 0, 0);
        var first = Game.Portals(g.Here).Single();
        var firstFar = Game.Portals(g.Campaign.Resident[g.Haven]).Single();

        Assert.True(g.Table.Create(g.Leader, "havenPortal", "havenPortal", 0, 0));
        Assert.True(first.Removed);
        Assert.True(firstFar.Removed);
        Assert.Single(Game.Portals(g.Here));
        Assert.Single(Game.Portals(g.Campaign.Resident[g.Haven]));
        // The freed slot is the first free one, so the new pair is in it again.
        Assert.False(g.Table.Slots[0].Free);
        Assert.True(g.Table.Slots[1].Free);
    }

    /// <summary>
    /// A network player leaving the game has their pairs shut on the host: both ends taken
    /// away, forced, and the slot freed — even a pair flag 1 would keep — and nobody else's touched.
    /// </summary>
    [Fact]
    public void A_leaving_players_pairs_are_shut_whatever_their_flags()
    {
        using var files = Install.Open();
        var g = Open(files);
        var other = g.Here.Things.First(t => t.IsActor && !ReferenceEquals(t, g.Leader) && t.Actor?.Has(Actor.Flag.Party) != true);
        Assert.True(g.Table.Create(g.Leader, "havenPortal", "havenPortal", flags: Teleports.KeepAll, expireMs: 0));
        var near = Game.Portals(g.Here).Single();
        Assert.True(g.Table.Create(other, "havenPortal", "havenPortal", 0, 0));
        var theirs = Game.Portals(g.Here).Single(p => !ReferenceEquals(p, near));

        Assert.Equal(1, g.Table.Shut(g.Leader));
        Assert.True(near.Removed);
        Assert.True(g.Table.Slots[0].Free);
        Assert.False(theirs.Removed);
        Assert.False(g.Table.Slots[1].Free);
        Assert.Equal(0, g.Table.Shut(g.Leader));
    }

    [Fact]
    public void Flag_one_keeps_both_ends_and_the_slot_through_a_round_trip_and_a_second_cast()
    {
        using var files = Install.Open();
        var g = Open(files);
        int monastery = g.Chronicle.Current;
        g.Table.Create(g.Leader, "havenPortal", "havenPortal", flags: Teleports.KeepAll, expireMs: 0);
        var near = Game.Portals(g.Here).Single();
        g.Table.Activate(near.Guid, g.Leader);
        g.Travel();
        var far = Game.Portals(g.Here).Single();
        g.Table.Activate(far.Guid, g.Leader);
        g.Travel();

        Assert.Equal(monastery, g.Chronicle.Current);
        Assert.False(near.Removed);
        Assert.False(far.Removed);
        Assert.False(g.Table.Slots[0].Free);
        // A second cast finds the first pair its creator's, and neither closes it nor frees its slot.
        g.Table.Create(g.Leader, "havenPortal", "havenPortal", 0, 0);
        Assert.False(near.Removed);
        Assert.Equal(2, Game.Portals(g.Here).Count());
        Assert.False(g.Table.Slots[1].Free);
    }

    [Fact]
    public void Nothing_opens_where_the_location_says_no_teleports()
    {
        using var files = Install.Open();
        var g = Open(files);
        g.Chronicle.At(g.Chronicle.Current)!.Flags |= Teleports.NoTeleports;
        Assert.False(g.Table.Create(g.Leader, "havenPortal", "havenPortal", 0, 0));
        Assert.Empty(Game.Portals(g.Here));
        Assert.All(g.Table.Slots, s => Assert.True(s.Free));
    }

    [Fact]
    public void A_pair_whose_far_end_has_gone_is_shut_and_nobody_moves()
    {
        using var files = Install.Open();
        var g = Open(files);
        int monastery = g.Chronicle.Current;
        g.Table.Create(g.Leader, "havenPortal", "havenPortal", 0, 0);
        var near = Game.Portals(g.Here).Single();
        var haven = g.Campaign.Resident[g.Haven];
        haven.Remove(Game.Portals(haven).Single());

        g.Table.Activate(near.Guid, g.Leader);
        Assert.Null(g.Travel());
        // Both ends closed, the slot freed whatever its flags, and nobody went anywhere.
        Assert.Equal(monastery, g.Chronicle.Current);
        Assert.Null(g.Chronicle.Pending);
        Assert.True(near.Removed);
        Assert.All(g.Table.Slots, s => Assert.True(s.Free));
    }

    [Fact]
    public void An_open_pair_is_saved_with_its_two_places_and_after_a_load_still_leads_to_the_haven()
    {
        using var files = Install.Open();
        var g = Open(files);
        g.Table.Create(g.Leader, "havenPortal", "havenPortal", 0, 0);
        var nearId = ThingIds.Of(g.Here, Game.Portals(g.Here).Single());

        string path = Path.Combine(Path.GetTempPath(), $"vitomir-teleport-{Guid.NewGuid():N}.vsave");
        SaveGame save;
        try
        {
            SaveArchive.Write(SaveCapture.Take(g.Here, g.Session, g.Campaign), path);
            save = SaveArchive.Read(path);
        }
        finally { File.Delete(path); }

        var kept = Assert.Single(save.Session.Teleports);
        Assert.Equal((0, "Haven", "Monastery", "party:Christof"), (kept.Slot, kept.InLocation, kept.OutLocation, kept.Creator));
        Assert.Contains(kept.OutThing!, save.World["Monastery"].Things.Keys);
        Assert.Contains(kept.InThing!, save.World["Haven"].Things.Keys);

        // A load, as the host does it: the story, the place the party is in built and given back,
        // the session, and every other place parked until somebody goes there.
        var set = ChronicleSet.Load(files);
        var story = set["Prague"]!;
        SaveApply.Chronicle(story, save.Chronicle!);
        var record = save.World[save.Header.Location];
        var here = Location.Load(files, record.Level, sky: false, scene: record.Scene, player: "Christof", entrance: record.Entrance);
        var restore = SaveApply.Place(here, record, null, null);
        SaveApply.Party(here, save.Party, restore);
        var session = new Session { Here = here, Chronicle = story };
        here.Session = session;
        SaveApply.Session(session, save.Session, here);
        var campaign = new Campaign
        {
            Chronicle = story, Here = here,
            Build = (level, scene, entrance) => Location.Load(files, level, sky: false, scene: scene, player: "Christof", entrance: entrance),
            BuildSaved = (r, entrance) =>
            {
                var place = Location.Load(files, r.Level, sky: false, scene: r.Scene.Length > 0 ? r.Scene : null, player: "Christof", entrance: entrance);
                SaveApply.Place(place, r, null, null);
                return place;
            },
        };
        foreach (var (name, other) in save.World)
            if (story.Find(name) is var i && i != story.Current) campaign.Parked[i] = other;
        session.Teleports.Campaign = campaign;
        var g2 = new Game { Files = files, Chronicle = story, Campaign = campaign, Session = session };

        var near = Game.Portals(here).Single();
        Assert.Equal(nearId, ThingIds.Of(here, near));
        Assert.True(g2.Table.Activate(near.Guid, g2.Leader));
        Assert.NotNull(g2.Travel());
        Assert.Equal("Haven", story.NameOf(story.Current));
        var far = Game.Portals(g2.Here).Single();
        Assert.Equal(new Vector3(193, -616, -368), far.Position);
        Assert.Equal(Clear(g2.Leader, far), Flat(g2.Leader.Position, far.Position), 1f);
    }

    [Fact]
    public void A_new_chronicle_empties_the_table_and_a_save_without_one_loads_it_empty()
    {
        using var files = Install.Open();
        var g = Open(files);
        g.Table.Create(g.Leader, "havenPortal", "havenPortal", 0, 0);
        Assert.False(g.Table.Slots[0].Free);
        TeleportSaves.Apply(g.Table, [], g.Chronicle);
        Assert.All(g.Table.Slots, s => Assert.True(s.Free));
        g.Table.Create(g.Leader, "havenPortal", "havenPortal", 0, 0);
        g.Table.Clear();
        Assert.All(g.Table.Slots, s => Assert.True(s.Free));
    }
}
