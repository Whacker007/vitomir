// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A host's places are remembered as the host last left them** (`CampaignFiles.cs`, 1.1's
/// the save, the mover, the unload and the swap). 1.1's host writes a place's file only
/// when its own party leaves it (and at a save, for the place it is in); a save copies every file as it
/// stands, and a place let go comes back from its file — so what players change in a place after the
/// host has gone is lost from both, and a place only players have been to has no file at all. That is
/// 1.1's rule. The single-player game keeps its record taken at the let-go, unchanged.
/// Driven through Prague's small interiors (as `CampaignUnloadTests`); a "player's" change is a placed
/// thing taken out of a place the host is not in, which is what a player there could do.
/// </summary>
public class HostPlaceFilesTests
{
    static readonly string[] Tour = ["Smithy", "Unorna", "StThomas", "University"];

    sealed record Game(Campaign Campaign, Session Session, GameFiles Files) : IDisposable
    {
        public void Dispose() => Files.Dispose();
        public int Index(string name) => Campaign.Chronicle.Find(name);
        public SaveGame Save() => SaveCapture.Take(Campaign.Here, Session, Campaign, now: new DateTime(2026, 9, 25));
    }

    static Game Open(bool host)
    {
        var files = Install.Open();
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        var chronicle = ChronicleSet.Load(files)["prague"]!;
        var session = new Session { Chronicle = chronicle };
        session.Messages.Text = new Strings(files);
        Location Build(string name, string? scene, int entrance)
        {
            var place = Location.Load(files, name, things: false, sky: false, scene: scene, player: "Christof", entrance: entrance);
            place.Session = session;
            return place;
        }
        Location BuildSaved(LocationSave record, int entrance)
        {
            var place = Build(record.Level, record.Scene.Length > 0 ? record.Scene : null, entrance);
            SaveApply.Place(place, record, effects, powers);
            return place;
        }
        chronicle.Current = chronicle.Find(Tour[0]);
        var at = chronicle.At(chronicle.Current)!;
        var here = Build(at.LevelName, at.SceneName.Length > 0 ? at.SceneName : null, -1);
        session.Here = here;
        var campaign = new Campaign { Chronicle = chronicle, Build = Build, BuildSaved = BuildSaved, Here = here };
        if (host) campaign.Host();
        return new Game(campaign, session, files);
    }

    static Campaign.Arrival Go(Campaign campaign, string where)
    {
        Assert.True(campaign.Chronicle.Jump(where, 0), $"no {where}");
        return campaign.Travel() ?? throw new InvalidOperationException($"no journey to {where}");
    }

    /// <summary>Takes one placed thing out of <paramref name="place"/> — a change a player there could make — and answers its id.</summary>
    static string TakeOne(Location place, int skip = 0)
    {
        var taken = place.Things.Where(t => !ReferenceEquals(t, place.Player?.Thing) && ThingIds.Placed(place, t) is not null).Skip(skip).First();
        string id = ThingIds.Placed(place, taken)!;
        place.Remove(taken, drop: false);
        return id;
    }

    [Fact]
    public void A_host_s_save_has_a_place_as_the_host_left_it_not_as_the_players_changed_it_after()
    {
        using var game = Open(host: true);
        var smithy = game.Campaign.Here;
        string before = TakeOne(smithy);                                  // while the host is there
        Go(game.Campaign, Tour[1]);                                       // the host leaves: the smithy is filed
        Assert.True(game.Campaign.Files!.ContainsKey(game.Index(Tour[0])));
        string after = TakeOne(smithy);                                   // a player left behind takes another

        var save = game.Save();
        var record = save.World[Tour[0]];
        Assert.Contains(before, record.Removed);
        Assert.DoesNotContain(after, record.Removed);                     // lost: the save copies the file
        // The place the host is in is written fresh, and filed by the save (mode 1).
        Assert.True(save.World.ContainsKey(Tour[1]));
        Assert.True(game.Campaign.Files!.ContainsKey(game.Index(Tour[1])));
    }

    /// <summary>
    /// **The mover stops the host's own character** (the mover's player branch for slot 0): before the
    /// place is filed, the host's character is cancelled forced as a release and its walk
    /// stopped — so the body left in the place is not still walking. A single-player journey
    /// does not go through the mover, and neither does a host's advance.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_host_s_own_character_is_stopped_by_the_mover_and_a_single_player_s_is_not(bool host)
    {
        using var game = Open(host);
        var smithy = game.Campaign.Here;
        var walker = smithy.Player!;
        var me = walker.Thing;
        Assert.True(walker.WalkTo(me.Position + new System.Numerics.Vector3(40f, 0f, 0f), walker.Node), "the walk was refused");
        Assert.True(walker.Walking);
        Go(game.Campaign, Tour[1]);
        Assert.Equal(!host, walker.Walking);
    }

    [Fact]
    public void In_single_player_the_same_save_keeps_the_place_as_it_stands()
    {
        using var game = Open(host: false);
        var smithy = game.Campaign.Here;
        Go(game.Campaign, Tour[1]);
        string after = TakeOne(smithy);
        Assert.Null(game.Campaign.Files);
        Assert.Contains(after, game.Save().World[Tour[0]].Removed);
    }

    [Fact]
    public void A_place_let_go_on_a_host_comes_back_from_its_file()
    {
        using var game = Open(host: true);
        game.Campaign.KeepLoaded = 2;                                    // three places are enough to let one go
        var smithy = game.Campaign.Here;
        string before = TakeOne(smithy);
        Go(game.Campaign, Tour[1]);
        string after = TakeOne(smithy);                                   // after the host has gone
        Go(game.Campaign, Tour[2]);                                       // the smithy, loaded first, is let go — writing nothing
        int index = game.Index(Tour[0]);
        Assert.Contains(index, game.Campaign.LetGo);
        Assert.True(game.Campaign.Parked.TryGetValue(index, out var parked));
        Assert.Contains(before, parked!.Removed);
        Assert.DoesNotContain(after, parked.Removed);

        var back = Go(game.Campaign, Tour[0]);                            // read back
        Assert.True(back.Restored);
        Assert.DoesNotContain(back.To.Things, t => ThingIds.Placed(back.To, t) == before);
        Assert.Contains(back.To.Things, t => ThingIds.Placed(back.To, t) == after && t.InWorld);
    }

    [Fact]
    public void A_place_only_players_have_been_to_has_no_file_and_comes_back_as_its_level_made_it()
    {
        using var game = Open(host: true);
        game.Campaign.KeepLoaded = 2;
        int thomas = game.Index(Tour[2]);
        var there = game.Campaign.Visit(thomas, 0)!;                      // a player's journey writes nothing
        string taken = TakeOne(there);
        Assert.False(game.Campaign.Files!.ContainsKey(thomas));
        Assert.False(game.Save().World.ContainsKey(Tour[2]));            // not in a save

        Go(game.Campaign, Tour[1]);                                       // the host moves on; St Thomas, loaded before, goes
        Assert.Contains(thomas, game.Campaign.LetGo);
        Assert.False(game.Campaign.Parked.ContainsKey(thomas));
        var again = game.Campaign.Visit(thomas, 0)!;                      // built fresh
        Assert.Contains(again.Things, t => ThingIds.Placed(again, t) == taken && t.InWorld);
    }

    [Fact]
    public void A_loaded_save_s_place_a_player_changes_is_saved_again_as_it_was_loaded()
    {
        using var game = Open(host: true);
        // The save's record of St Thomas, as a load hands it over (Program's load: Campaign.Remember).
        int thomas = game.Index(Tour[2]);
        var first = game.Campaign.Visit(thomas, 0)!;
        string gone = TakeOne(first);
        var record = SaveCapture.OfPlace(first);
        using var game2 = Open(host: true);
        game2.Campaign.Remember(thomas, record);

        var there = game2.Campaign.Visit(thomas, 0)!;                     // a player goes there: read back from the file
        Assert.DoesNotContain(there.Things, t => ThingIds.Placed(there, t) == gone && t.InWorld);
        string taken = TakeOne(there);                                    // and changes it
        var saved = game2.Save().World[Tour[2]];
        Assert.Contains(gone, saved.Removed);
        Assert.DoesNotContain(taken, saved.Removed);
    }

    /// <summary>
    /// **Over the wire**: a host of *To Curse the Darkness* goes to Sorvena's cottage alone while Anna stays
    /// in the inn and plays on there (the inn is hers now, stepped and kept). What changes in the inn after
    /// the host has gone is not in the save the host takes in the cottage — the inn is as the host left it,
    /// with nobody's character in it — and the inn is never let go while she is in it.
    /// </summary>
    [Fact]
    public void A_host_s_save_taken_elsewhere_has_the_players_place_as_the_host_left_it()
    {
        using var files = Install.Open();
        var story = ChronicleSet.Load(files)["MP_CurseDarkness"]!;
        var settings = GameSettings.For(story, "Hosta's game", GameFlags.AutoStoryteller);
        var session = new Session { Chronicle = story };
        Location Build(string level, string? scene, int entrance)
        {
            var built = Location.Load(files, level, scene: scene, entrance: entrance);
            built.Session = session;
            return built;
        }
        story.Current = story.Data.ReviveLocation;
        var at = story.At(story.Current)!;
        var inn = Build(at.LevelName, at.SceneName.Length > 0 ? at.SceneName : null, settings.Entrance);
        session.Here = inn;
        using var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
        var campaign = new Campaign { Chronicle = story, Build = Build, Here = inn };
        campaign.Host();
        server.Places.Chronicle = story;
        campaign.Occupied = server.Places.Kept;
        server.Places.Load = (index, entrance) => campaign.Visit(index, entrance);
        server.Start();
        server.Enter(inn, null);
        var hostSim = new Simulation(inn);
        int ms = 0;
        void HostStep() { hostSim.Step(1f / 30); server.Places.Step(1f / 30); server.Poll(ms += 33); }

        Location? hers = null;
        Simulation? herSim = null;
        using var anna = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, server.Peer.Port),
                                        new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        anna.Started += () =>
        {
            var st = anna.Settings!;
            hers = Location.Load(files, st.Level, scene: st.Scene.Length > 0 ? st.Scene : null, player: "MP_Serena", entrance: st.Entrance);
            herSim = new Simulation(hers);
            anna.Enter(hers, hers.Player?.Thing);
        };
        void All() { HostStep(); anna.Poll(1f / 30); herSim?.Step(1f / 30); }
        Assert.True(NetShopsTests.Until(() => anna.State == ClientState.Playing && anna.Avatar is not null, All), "never played");

        string before = TakeOne(inn);
        Assert.True(story.Jump(story.NameOf(2), 0));                       // the host's own journey, alone
        var went = campaign.Travel()!.Value;
        server.TravelAlone(went.To, null);
        hostSim = new Simulation(went.To);
        Assert.True(campaign.Files!.ContainsKey(campaign.Chronicle.Find(at.Name)));
        Assert.Same(inn, server.PlaceOf(server.Players.Single(p => p.Name == "Anna")));
        for (int i = 0; i < 10; i++) All();                                // she plays on in the inn
        string after = TakeOne(inn);                                       // and it changes

        var save = SaveCapture.Take(went.To, session, campaign, now: new DateTime(2026, 9, 25));
        var record = save.World[at.Name];
        Assert.Contains(before, record.Removed);
        Assert.DoesNotContain(after, record.Removed);
        Assert.DoesNotContain(record.Things.Values, t => t.Template.Equals("MP_Serena", StringComparison.OrdinalIgnoreCase));
        Assert.True(server.Places.Kept(inn));                              // hers: never let go
    }
}
