// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using System.Numerics;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Players in different places** (`Net.HostPlaces`, `GameServer.Places.cs`, `Campaign.Visit`;
/// 1.1's mover, its revive and the chronicle's loading and unloading). The
/// host keeps several of the chronicle's places loaded, each player is in whichever one they went to,
/// and each player's machine has only its own place: an exit taken, the Storyteller's jump or a revive
/// moves one player, whose machine loads the new place alone while the others play on where they are.
/// Driven with *To Curse the Darkness*' places — the inn where players join, Sorvena's cottage and the
/// catacombs, whose first two are joined by an exit — over 127.0.0.1 in one process, with no scripts.
/// </summary>
public class NetPlacesTests
{
    const int Temesvar = 1, Cottage = 2, Catacombs1 = 4, Catacombs2 = 5;

    /// <summary>Polls every end until the condition holds or twenty seconds pass (a place loads inside a turn).</summary>
    static bool Until(Func<bool> done, params Action[] turns)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 20000)
        {
            foreach (var t in turns) t();
            if (done()) return true;
            Thread.Sleep(2);
        }
        return done();
    }

    sealed class Game : IDisposable
    {
        public required GameServer Server;
        public required Location Place;
        public required Simulation Sim { get; set; }
        public required Chronicle Story;
        public Session? Session;
        public Campaign? Campaign;
        public readonly Dictionary<int, Location> Loaded = [];
        public int Ms;
        public void Step(float seconds = 1f / 30)
        {
            Sim.Step(seconds);
            Server.Places.Step(seconds);
            Ms += (int)(seconds * 1000);
            Server.Poll(Ms);
        }
        public void Dispose() => Server.Dispose();
    }

    sealed class Player : IDisposable
    {
        public required GameClient Client;
        public Location? Place;
        public Simulation? Sim;
        public int Loads;
        public void Step(float seconds = 1f / 30)
        {
            Client.Poll(seconds);
            Sim?.Step(seconds);
        }
        public void Dispose() => Client.Dispose();
    }

    /// <summary>
    /// A host of *To Curse the Darkness* at the inn. With <paramref name="campaign"/> its places are a
    /// campaign's (`Campaign.Visit`) and the session's teleporters are the host's, as the game's runs wire
    /// them (`NetPlaces.Wire`); without, a plain loader.
    /// </summary>
    static Game Host(GameFiles files, bool campaign = false, bool awaken = false)
    {
        var story = ChronicleSet.Load(files)["MP_CurseDarkness"]!;
        var settings = GameSettings.For(story, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        // With minds and effects, when a test needs them (the mover's minds).
        Location Awake(Location built)
        {
            if (awaken) built.Awaken(new Ai.AiClasses(files), 1, new EffectDefinitions(files));
            return built;
        }
        Awake(place);
        var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
        var game = new Game { Server = server, Place = place, Sim = new Simulation(place), Story = story };
        story.Current = story.Data.ReviveLocation;
        game.Loaded[story.Current] = place;
        server.Places.Chronicle = story;
        if (campaign)
        {
            var session = new Session { Here = place, Chronicle = story };
            place.Session = session;
            Location Build(string level, string? scene, int entrance)
            {
                var built = Location.Load(files, level, scene: scene, entrance: entrance);
                built.Session = session;
                return built;
            }
            var c = new Campaign { Chronicle = story, Build = Build, Here = place, KeepLoaded = Campaign.KeptPlacesHosting };
            c.Occupied = server.Places.Occupied;
            c.Discarded += server.Places.Forget;
            session.Teleports.Campaign = c;
            session.Teleports.Network = server.Places;
            game.Session = session;
            game.Campaign = c;
            server.Places.Load = (index, entrance) => c.Visit(index, entrance) is { } there ? game.Loaded[index] = there : null;
        }
        else
            server.Places.Load = (index, entrance) =>
            {
                if (game.Loaded.TryGetValue(index, out var had)) return had;
                var at = story.At(index)!;
                return game.Loaded[index] = Awake(Location.Load(files, at.LevelName, scene: at.SceneName.Length > 0 ? at.SceneName : null, entrance: entrance));
            };
        server.Start();
        server.Enter(place, null);
        return game;
    }

    static Player Join(GameFiles files, Game game, string name, string template)
    {
        var p = new Player
        {
            Client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, game.Server.Peer.Port),
                                    new NetCharacter { Name = name, Template = template }),
        };
        p.Client.Find = new Strings(files).Find;
        p.Client.Started += () =>
        {
            var s = p.Client.Settings!;
            p.Place = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: template, entrance: s.Entrance);
            p.Sim = new Simulation(p.Place);
            p.Loads++;
            p.Client.Enter(p.Place, p.Place.Player?.Thing);
        };
        return p;
    }

    static bool Playing(Player p, string level) =>
        p.Client.State == ClientState.Playing && p.Client.Avatar is not null
        && p.Place?.Name.Equals(level, StringComparison.OrdinalIgnoreCase) == true;

    static NetPlayer Named(Game game, string name) => game.Server.Players.Single(p => p.Name == name);

    [Fact]
    public void Two_players_go_different_ways_and_each_machine_has_only_its_own_place()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        string inn = game.Place.Name;
        Assert.True(Until(() => Playing(anna, inn) && Playing(bob, inn), () => game.Step(), () => anna.Step(), () => bob.Step()), "never both played");

        // The Storyteller's jump sends Bob alone to Sorvena's cottage.
        var bobs = Named(game, "Bob");
        Assert.True(game.Server.Places.Move(bobs.Avatar!, Cottage, 0));
        string cottage = game.Story.At(Cottage)!.LevelName;
        Assert.True(Until(() => Playing(bob, cottage), () => game.Step(), () => anna.Step(), () => bob.Step()), $"Bob never arrived: {bob.Client.State} in {bob.Place?.Name}");

        // The host has two places; Bob's character is in the cottage, Anna's still in the inn, and
        // Anna's machine never loaded anything again.
        Assert.Equal(2, game.Server.Places.All.Count);
        var there = game.Loaded[Cottage];
        Assert.Same(there, bobs.Avatar!.Place);
        Assert.Same(game.Place, Named(game, "Anna").Avatar!.Place);
        Assert.Equal(1, anna.Loads);
        Assert.Equal(2, bob.Loads);
        Assert.Equal("MP_Erik", bobs.Avatar.Template);
        // Bob's GUID names a thing of the cottage, so the inn's machine is told his character is nowhere it has.
        Assert.True(Until(() => anna.Client.Players.Single(p => p.Name == "Bob").Avatar == 0, () => game.Step(), () => anna.Step(), () => bob.Step()));

        // The cottage is stepped on the host, and Bob's click walks him there.
        var me = bob.Place!.Player!;
        var from = bobs.Avatar.Position;
        var to = FloorNear(there, from, 120f);
        Assert.True(me.WalkTo(to, bob.Place.Walk!.Find(to, 256f)));
        Assert.True(Until(() => Vector3.Distance(bobs.Avatar.Position, from) > 30f, () => game.Step(), () => anna.Step(), () => bob.Step()), "the host never walked Bob in the cottage");
        Assert.NotNull(game.Server.Places.Of(there)!.Sim);
        Assert.True(Until(() => Vector3.Distance(me.Thing.Position, from) > 30f, () => game.Step(), () => anna.Step(), () => bob.Step()), "Bob's machine never saw himself walk");

        // Anna, in the inn, still walks as she did.
        var annas = Named(game, "Anna").Avatar!;
        var her = anna.Place!.Player!;
        var start = annas.Position;
        var spot = FloorNear(game.Place, start, 120f);
        Assert.True(her.WalkTo(spot, anna.Place.Walk!.Find(spot, 256f)));
        Assert.True(Until(() => Vector3.Distance(annas.Position, start) > 30f, () => game.Step(), () => anna.Step(), () => bob.Step()), "the host never walked Anna");
    }

    /// <summary>
    /// **An exit is the character's own place's**: a player in the first
    /// catacomb takes its exit 1 and arrives alone in the second; a closed exit is refused as the single
    /// player's is.
    /// </summary>
    [Fact]
    public void An_exit_taken_by_a_player_moves_that_player_alone()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        string inn = game.Place.Name;
        Assert.True(Until(() => Playing(anna, inn) && Playing(bob, inn), () => game.Step(), () => anna.Step(), () => bob.Step()), "never both played");
        var bobs = Named(game, "Bob");
        Assert.True(game.Server.Places.Move(bobs.Avatar!, Catacombs1, 0));
        string first = game.Story.At(Catacombs1)!.LevelName, second = game.Story.At(Catacombs2)!.LevelName;
        Assert.True(Until(() => Playing(bob, first), () => game.Step(), () => anna.Step(), () => bob.Step()), "Bob never reached the catacombs");

        // The inn has no exit 1 of its own for Anna (her place's exits, not Bob's).
        Assert.False(game.Server.Places.TakeExit(Named(game, "Anna").Avatar!, 1));
        // A closed exit is refused.
        game.Story.At(Catacombs1)!.Exits[1].Closed = true;
        Assert.False(game.Server.Places.TakeExit(bobs.Avatar!, 1));
        game.Story.At(Catacombs1)!.Exits[1].Closed = false;
        Assert.True(game.Server.Places.TakeExit(bobs.Avatar!, 1));
        Assert.True(Until(() => Playing(bob, second), () => game.Step(), () => anna.Step(), () => bob.Step()), "Bob never took the exit");
        Assert.Equal(1, anna.Loads);
        Assert.Same(game.Loaded[Catacombs2], bobs.Avatar!.Place);
        Assert.Equal(3, game.Server.Places.All.Count);                       // the first catacomb stays loaded, empty
    }

    /// <summary>
    /// **A frenzied player is still frenzied where the mover took him** (the mover's mind reset
    /// skips the mind's reset for mode 9; `MoverMinds`). Bob, frenzied in the inn, is moved to the
    /// catacombs: his character is made again there from his sheet, `ef_frenzy` with it, and its new mind
    /// is put back in frenzy — so the frenzy's end (which asks for mode 9) still finds it. Anna,
    /// afraid when moved, arrives in her home mode.
    /// </summary>
    [Fact]
    public void A_frenzied_player_is_still_frenzied_where_the_mover_took_him()
    {
        using var files = Install.Open();
        using var game = Host(files, awaken: true);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        string inn = game.Place.Name;
        Assert.True(Until(() => Playing(anna, inn) && Playing(bob, inn), () => game.Step(), () => anna.Step(), () => bob.Step()), "never both played");
        var (bobs, annas) = (Named(game, "Bob"), Named(game, "Anna"));
        Frenzy.Begin(bobs.Avatar!);
        Assert.True(Frenzy.Frenzied(bobs.Avatar!));
        game.Place.Minds!.Frighten(annas.Avatar!.Mind!, null, force: true);
        Assert.Equal(Ai.AiMode.Afraid, annas.Avatar.Mind!.Mode);

        // As each arrives (the frenzy's own decay may end it on the way, as it would have in 1.1).
        var arrived = new Dictionary<string, (Ai.AiMode Mode, bool Marker)>();
        game.Server.Arrived += p => arrived[p.Name] = (p.Avatar!.Mind!.Mode, p.Avatar.Effects.Find(LoadedMinds.FrenzyMarker) is { Spent: false });
        Assert.True(game.Server.Places.Move(bobs.Avatar!, Catacombs1, 0));
        Assert.True(game.Server.Places.Move(annas.Avatar!, Cottage, 0));
        string first = game.Story.At(Catacombs1)!.LevelName, cottage = game.Story.At(Cottage)!.LevelName;
        Assert.True(Until(() => Playing(bob, first) && Playing(anna, cottage) && bobs.Avatar is not null && annas.Avatar is not null,
                          () => game.Step(), () => anna.Step(), () => bob.Step()), "they never arrived");
        Assert.Same(game.Loaded[Catacombs1], bobs.Avatar!.Place);
        Assert.Equal((Ai.AiMode.Frenzied, true), arrived["Bob"]);
        Assert.Equal(annas.Avatar!.Mind!.BaseMode, arrived["Anna"].Mode);
    }

    /// <summary>
    /// **A revive moves the character to the chronicle's revive location** (the revive → the mover, the
    /// location `SetReviveLocation` moves): Anna dies in the inn with the revive location moved to the
    /// cottage, asks to come back, and wakes there at full health.
    /// </summary>
    [Fact]
    public void A_revived_player_wakes_at_the_chronicle_s_revive_location()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        string inn = game.Place.Name;
        Assert.True(Until(() => Playing(anna, inn), () => game.Step(), () => anna.Step()), "never played");
        var annas = Named(game, "Anna");
        Assert.True(game.Story.SetRevive(Cottage, 0));
        Damage.Kill(annas.Avatar!, null, HitKind.Script, DamageType.Normal, game.Ms);
        Assert.True(annas.Avatar!.Actor!.Dead);
        anna.Client.Revive();
        string cottage = game.Story.At(Cottage)!.LevelName;
        Assert.True(Until(() => Playing(anna, cottage), () => game.Step(), () => anna.Step()), $"Anna never woke in the cottage: {anna.Place?.Name}");
        Assert.False(annas.Avatar!.Actor!.Dead);
        Assert.Equal(annas.Avatar.Actor.MaxHealth, annas.Avatar.Actor.Health);
        Assert.Same(game.Loaded[Cottage], annas.Avatar.Place);
    }

    /// <summary>
    /// A revive where the revive location is the place the character is in stands it on the entrance
    /// there (ours: see `GameServer.RevivedAt`) — its machine loads nothing again.
    /// </summary>
    [Fact]
    public void A_revive_in_the_revive_location_itself_loads_nothing()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        string inn = game.Place.Name;
        Assert.True(Until(() => Playing(anna, inn), () => game.Step(), () => anna.Step()), "never played");
        var annas = Named(game, "Anna");
        Damage.Kill(annas.Avatar!, null, HitKind.Script, DamageType.Normal, game.Ms);
        anna.Client.Revive();
        Assert.True(Until(() => annas.Avatar is { Actor.Dead: false }, () => game.Step(), () => anna.Step()), "never revived");
        for (int i = 0; i < 30; i++) { game.Step(); anna.Step(); }
        Assert.Equal(1, anna.Loads);
        Assert.Same(game.Place, annas.Avatar!.Place);
    }

    /// <summary>
    /// **The host's own exit takes the host alone** (slot 0's own journey): the host's place
    /// becomes the cottage and Anna stays in the inn — her machine loads nothing, the inn goes on being
    /// stepped for her by the host's places, and her clicks still walk her there.
    /// </summary>
    [Fact]
    public void The_host_goes_alone_and_the_players_stay_where_they_are()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        string inn = game.Place.Name;
        Assert.True(Until(() => Playing(anna, inn), () => game.Step(), () => anna.Step()), "never played");
        var cottage = game.Server.Places.Load!(Cottage, 0)!;
        game.Server.TravelAlone(cottage, null);
        game.Sim = new Simulation(cottage);
        Assert.Same(cottage, game.Server.Place);
        Assert.Same(cottage, game.Server.Places.Home!.Place);
        var annas = Named(game, "Anna");
        Assert.Same(game.Place, game.Server.PlaceOf(annas));
        Assert.True(game.Server.Places.Occupied(game.Place));

        var her = anna.Place!.Player!;
        var start = annas.Avatar!.Position;
        var spot = FloorNear(game.Place, start, 120f);
        Assert.True(her.WalkTo(spot, anna.Place.Walk!.Find(spot, 256f)));
        Assert.True(Until(() => Vector3.Distance(annas.Avatar.Position, start) > 30f, () => game.Step(), () => anna.Step()), "the inn is no longer stepped");
        Assert.True(Until(() => Vector3.Distance(her.Thing.Position, start) > 30f, () => game.Step(), () => anna.Step()), "her machine never saw it");
        Assert.Equal(1, anna.Loads);
        Assert.NotNull(game.Server.Places.Of(game.Place)!.Sim);
    }

    /// <summary>
    /// **A newcomer starts at the revive location**, wherever
    /// the host is: with the revive location moved to the cottage, Bob joins and arrives there.
    /// </summary>
    [Fact]
    public void A_newcomer_starts_at_the_chronicle_s_revive_location()
    {
        using var files = Install.Open();
        using var game = Host(files);
        Assert.True(game.Story.SetRevive(Cottage, 0));
        using var bob = Join(files, game, "Bob", "MP_Erik");
        string cottage = game.Story.At(Cottage)!.LevelName;
        Assert.True(Until(() => Playing(bob, cottage), () => game.Step(), () => bob.Step()), $"Bob never started in the cottage: {bob.Place?.Name}");
        var bobs = Named(game, "Bob");
        Assert.Same(game.Loaded[Cottage], bobs.Avatar!.Place);
        Assert.False(bobs.Joining);
        Assert.Equal(1, bob.Loads);                                          // told the cottage in the join's answer
    }

    /// <summary>
    /// **`TeleportPartyToConversation` for one player** (the native's network branch): with a remote
    /// player's GUID only that character goes to the conversation start (group n, the first) — the other
    /// player stays where she is; and nobody's character but a player's is left to the party's move.
    /// </summary>
    [Fact]
    public void A_conversation_start_takes_one_player_alone()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        string inn = game.Place.Name;
        Assert.True(Until(() => Playing(anna, inn) && Playing(bob, inn), () => game.Step(), () => anna.Step(), () => bob.Step()), "never both played");
        // The inn has no conversation starts of its own: one is laid down a few paces from Anna.
        var annaNow = Named(game, "Anna").Avatar!;
        var spot = FloorNear(game.Place, annaNow.Position, 150f);
        game.Place.ConversationStarts = [new Formats.NsdStart((uint)Math.Max(annaNow.Sector, 0), [1, 0, 0, 0, 1, 0, 0, 0, 1], spot, 7, 0)];
        uint group = 7;
        var start = game.Place.ConversationStart(group, 1)!;
        var annas = Named(game, "Anna").Avatar!;
        var bobs = Named(game, "Bob").Avatar!;
        var bobWas = bobs.Position;
        // And nothing but the standing: the standing cancels no order (Vitomir cleared it outright).
        annas.Actor!.Action = Rpg.ActionMode.Use;
        annas.Actor.Target = bobs;
        Assert.True(game.Server.Places.ToConversation(annas, group, out bool refused));
        Assert.False(refused);
        Assert.Equal(Rpg.ActionMode.Use, annas.Actor.Action);
        Assert.Same(bobs, annas.Actor.Target);
        Assert.Equal(start.Position.X, annas.Position.X, 1);
        Assert.Equal(start.Position.Y, annas.Position.Y, 1);
        Assert.Equal(bobWas, bobs.Position);
        var keeper = game.Place.Things.First(t => t.Actor is not null && !ReferenceEquals(t, annas) && !ReferenceEquals(t, bobs));
        Assert.False(game.Server.Places.ToConversation(keeper, group, out _));
    }

    // ---- the teleporters ------------------------------------------------------------------------

    /// <summary>
    /// **Walk the Abyss for one player** (`TeleportsNetwork.cs`).
    /// Bob, alone in the first catacomb, opens a pair: its far end stands on his own start of
    /// the portal entrance (Temesvar 15, the third — his slot is 2); he goes through alone and lands beside
    /// it while Anna stays in the inn; his way back closes the pair behind him. A pair he opens again is
    /// shut when he is kicked.
    /// </summary>
    [Fact]
    public void A_player_goes_through_a_teleporter_alone_and_only_his_way_back_closes_it()
    {
        using var files = Install.Open();
        using var game = Host(files, campaign: true);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        string inn = game.Place.Name;
        Assert.True(Until(() => Playing(anna, inn) && Playing(bob, inn), () => game.Step(), () => anna.Step(), () => bob.Step()), "never both played");
        var bobs = Named(game, "Bob");
        Assert.Equal(ThingType.Player, bobs.Avatar!.Type);
        Assert.True(game.Server.Places.Move(bobs.Avatar!, Catacombs1, 0));
        string catacomb = game.Story.At(Catacombs1)!.LevelName, town = game.Story.At(Temesvar)!.LevelName;
        Assert.True(Until(() => Playing(bob, catacomb), () => game.Step(), () => anna.Step(), () => bob.Step()), "Bob never reached the catacombs");

        var pairs = game.Session!.Teleports;
        Assert.True(pairs.Create(bobs.Avatar!, "havenPortal", "havenPortal", 0, 0));
        var slot = pairs.Slots[0];
        Assert.Equal($"net:{bobs.Slot}", slot.Creator);                    // his slot (2 when Anna joined first; under the simulator either may)
        Assert.Equal(Catacombs1, slot.OutLocation);
        Assert.Equal(Temesvar, slot.InLocation);
        var there = game.Campaign!.Resident[Temesvar];
        var far = there.Things.Single(t => t.Template.Equals("havenPortal", StringComparison.OrdinalIgnoreCase));
        var third = Location.PickStart(there.AllStarts, 15, (uint)(bobs.Slot + 1))!;   // his slot's start (the third when Anna joined first)
        Assert.Equal(third.Position.X, far.Position.X, 1);                  // his own start of the portal entrance
        Assert.Equal(third.Position.Y, far.Position.Y, 1);

        // Through: Bob alone, beside the far end; the host's own journey is never asked for.
        var near = game.Loaded[Catacombs1].Things.Single(t => t.Template.Equals("havenPortal", StringComparison.OrdinalIgnoreCase));
        Assert.True(pairs.Activate(near.Guid, bobs.Avatar!));
        Assert.Null(game.Story.Leaving);
        Assert.True(Until(() => Playing(bob, town) && bobs.Avatar is { InWorld: true }, () => game.Step(), () => anna.Step(), () => bob.Step()), "Bob never went through");
        Assert.True(Vector3.Distance(bobs.Avatar!.Position, far.Position) < 200f, $"Bob is {Vector3.Distance(bobs.Avatar.Position, far.Position):0} from the far end");
        Assert.Equal(1, anna.Loads);

        // Back: he made it, so the pair closes behind him — the far end at once, the near end as he lands.
        Assert.True(pairs.Activate(far.Guid, bobs.Avatar!));
        Assert.True(far.Removed || !far.InWorld);
        Assert.True(Until(() => Playing(bob, catacomb) && bobs.Avatar is { InWorld: true }, () => game.Step(), () => anna.Step(), () => bob.Step()), "Bob never came back");
        Assert.True(Vector3.Distance(bobs.Avatar!.Position, near.Position) < 200f);
        Assert.True(slot.Free);
        Assert.False(near.InWorld);

        // A pair he leaves open is shut when he goes.
        Assert.True(pairs.Create(bobs.Avatar!, "havenPortal", "havenPortal", 0, 0));
        Assert.False(pairs.Slots[0].Free);
        game.Server.Kick(bobs.Slot);                                         // his slot (2 when Anna joined first)
        // Gone once the kick is delivered (DisconnectWhenDelivered): at once on the plain wire, a round trip or a resend later
        // under the simulator.
        Assert.True(Until(() => pairs.Slots[0].Free, () => game.Step(), () => anna.Step(), () => bob.Step()), "the pair he left open was never shut");
        Assert.DoesNotContain(there.Things, t => t.Template.Equals("havenPortal", StringComparison.OrdinalIgnoreCase) && t.InWorld);
    }

    /// <summary>
    /// **A character nobody plays through a teleporter's end** (the teleport's network branch → the mover's last
    /// branch with entrance −2; `HostPlaces.CarryThrough`). Anna opens a pair in the inn; a townsman the
    /// Storyteller steers uses its near end and is put beside the far end in the town (flags 3), its
    /// sheet carried and the town told (`Came`); one nobody steers uses it and goes nowhere — and neither use asks
    /// for the host's own journey (the teleport answers 1 in a network game whatever the mover did). The pair stays
    /// open: only a player's way back closes it.
    /// </summary>
    [Fact]
    public void A_steered_character_nobody_plays_goes_through_a_teleporter_beside_its_end_and_another_goes_nowhere()
    {
        using var files = Install.Open();
        using var game = Host(files, campaign: true);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        string inn = game.Place.Name;
        Assert.True(Until(() => Playing(anna, inn), () => game.Step(), () => anna.Step()), "never played");
        var annas = Named(game, "Anna");
        Assert.True(game.Server.Places.Move(annas.Avatar!, Catacombs1, 0));
        string catacomb = game.Story.At(Catacombs1)!.LevelName;
        Assert.True(Until(() => Playing(anna, catacomb) && annas.Avatar is { InWorld: true }, () => game.Step(), () => anna.Step()), "Anna never reached the catacombs");
        var pairs = game.Session!.Teleports;
        Assert.True(pairs.Create(annas.Avatar!, "havenPortal", "havenPortal", 0, 0));
        var crypt = game.Loaded[Catacombs1];
        var near = crypt.Things.Single(t => t.InWorld && t.Template.Equals("havenPortal", StringComparison.OrdinalIgnoreCase));
        var town = game.Campaign!.Resident[Temesvar];
        var far = town.Things.Single(t => t.InWorld && t.Template.Equals("havenPortal", StringComparison.OrdinalIgnoreCase));

        town.Awaken(new Ai.AiClasses(files));
        var townsfolk = town.Things.Where(t => t.InWorld && t.Type == ThingType.Actor && t.Actor is { Dead: false } && t.Mind is not null).ToList();
        Assert.True(townsfolk.Count >= 2, "the town has nobody to steer");
        var (held, other) = (townsfolk[0], townsfolk[1]);

        // Not steered: the use is taken (the host's journey is not) and nobody moves.
        Assert.True(pairs.Activate(far.Guid, other));
        Assert.Null(game.Story.Leaving);
        Assert.True(other.InWorld);
        Assert.Same(town, other.Place);

        // Steered: back through the far end, beside the near end in the catacomb, as itself.
        Assert.True(town.SetSteered(held));
        var sheet = held.Actor!;
        WorldThing? came = null;
        game.Server.Places.Came += (_, t) => came = t;
        Assert.True(pairs.Activate(far.Guid, held));
        Assert.Null(game.Story.Leaving);
        Assert.False(held.InWorld);
        Assert.NotNull(came);
        Assert.Same(crypt, came!.Place);
        Assert.Same(sheet, came.Actor);
        Assert.True(Vector3.Distance(came.Position, near.Position) < 200f, $"it stands {Vector3.Distance(came.Position, near.Position):0} from the near end");
        Assert.True(Until(() => anna.Client.Mirror?.Find(came.Guid) is { InWorld: true }, () => game.Step(), () => anna.Step()), "Anna's machine never saw it come");
        Assert.True(far.InWorld && near.InWorld);                               // the pair stays open
        Assert.False(pairs.Slots[0].Free);
    }

    /// <summary>
    /// **The rollover names a teleporter's end's owner in a network game** (the rollover's prop branch:
    /// `(%s)` of the thing its owner field names). Anna's pair (opened in the catacomb) has ends carrying her GUID, so
    /// on the host each reads *(Anna)* under its name — the far end too, once she has gone through and stands
    /// by it with a new body (ours: Vitomir's body is made again; the pair's creator is found by her slot); her
    /// machine is told the owner with the thing (protocol 19) and names it the same; the
    /// same end with no network game names nobody.
    /// </summary>
    [Fact]
    public void A_teleporter_s_end_names_its_owner_on_the_host_and_on_her_machine()
    {
        using var files = Install.Open();
        using var game = Host(files, campaign: true);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        string inn = game.Place.Name;
        Assert.True(Until(() => Playing(anna, inn), () => game.Step(), () => anna.Step()), "never played");
        var annas = Named(game, "Anna");
        Assert.True(game.Server.Places.Move(annas.Avatar!, Catacombs1, 0));
        string catacomb = game.Story.At(Catacombs1)!.LevelName;
        Assert.True(Until(() => Playing(anna, catacomb) && annas.Avatar is { InWorld: true }, () => game.Step(), () => anna.Step()), "Anna never reached the catacombs");
        var pairs = game.Session!.Teleports;
        Assert.True(pairs.Create(annas.Avatar!, "havenPortal", "havenPortal", 0, 0));
        var near = game.Loaded[Catacombs1].Things.Single(t => t.InWorld && t.Template.Equals("havenPortal", StringComparison.OrdinalIgnoreCase));
        var far = game.Campaign!.Resident[Temesvar].Things.Single(t => t.InWorld && t.Template.Equals("havenPortal", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ThingType.Prop, near.Type);
        Assert.Equal(annas.Avatar!.Guid, near.PropOwner);
        Assert.Equal(annas.Avatar!.Guid, far.PropOwner);

        Assert.Equal(new RolloverLine("(Anna)", Rollover.White), Rollover.Owner(near));
        Assert.Equal(new RolloverLine("(Anna)", Rollover.White), Rollover.Owner(far));   // in another place: found all the same

        // Her machine: the end arrives with its owner, which is her own character there.
        Assert.True(Until(() => anna.Client.Mirror?.Find(near.Guid) is { InWorld: true }, () => game.Step(), () => anna.Step()), "the end never reached her");
        var hers = anna.Client.Mirror!.Find(near.Guid)!;
        Assert.Equal(anna.Client.Avatar!.Guid, hers.PropOwner);
        Assert.Equal(new RolloverLine("(Anna)", Rollover.White), Rollover.Owner(hers));

        // Through: a new body in the town, and the far end still names her.
        Assert.True(pairs.Activate(near.Guid, annas.Avatar!));
        string townLevel = game.Story.At(Temesvar)!.LevelName;
        Assert.True(Until(() => Playing(anna, townLevel) && annas.Avatar is { InWorld: true }, () => game.Step(), () => anna.Step()), "Anna never went through");
        Assert.NotEqual(near.PropOwner, annas.Avatar!.Guid);
        Assert.Equal(new RolloverLine("(Anna)", Rollover.White), Rollover.Owner(far));

        // No network game, no line (`*` zero).
        var single = Location.Load(files, game.Place.Name);
        var alone = single.Make("havenPortal")!;
        alone.PropOwner = 5;
        Assert.Null(Rollover.Owner(alone));
    }

    // ---- the places kept loaded ----------------------------------------------------------------
    // ---- the places kept loaded ----------------------------------------------------------------

    /// <summary>Eight small places of Prague, the first where the party starts (as `CampaignUnloadTests`).</summary>
    static readonly string[] Tour = ["Smithy", "Unorna", "StThomas", "University", "SmithyDay", "UnornaDay", "Inn4StagsDay", "ConventDay"];

    static (Campaign Campaign, GameFiles Files) Prague()
    {
        var files = Install.Open();
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        var chronicle = ChronicleSet.Load(files)["prague"]!;
        var session = new Session { Chronicle = chronicle };
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
        return (new Campaign { Chronicle = chronicle, Build = Build, BuildSaved = BuildSaved, Here = here }, files);
    }

    /// <summary>
    /// **A place a player is in is never let go** (the unload's slot walk on a host): with the smithy
    /// loaded first for a player alone and six more after it, the one let go is the next earliest.
    /// And a place loaded for a player (`Campaign.Visit`) leaves the party where it is.
    /// </summary>
    [Fact]
    public void A_place_a_player_is_in_is_never_the_one_let_go()
    {
        var (campaign, files) = Prague();
        using var _ = files;
        var here = campaign.Here;
        int smithy = campaign.Chronicle.Find(Tour[4]);                       // SmithyDay, loaded for a player alone
        var players = campaign.Visit(smithy, 0)!;
        Assert.Same(here, campaign.Here);
        Assert.Equal(campaign.Chronicle.Find(Tour[0]), campaign.Chronicle.Current);
        Assert.Same(players, campaign.Visit(smithy, 1));                      // a place with a world is not built again
        campaign.Occupied = p => ReferenceEquals(p, players);
        foreach (var name in Tour[1..4].Concat(Tour[5..8])) Assert.NotNull(campaign.Visit(campaign.Chronicle.Find(name), 0));
        Assert.Contains(smithy, campaign.Resident.Keys);
        Assert.DoesNotContain(smithy, campaign.LetGo);
        Assert.Equal([campaign.Chronicle.Find(Tour[1])], campaign.LetGo);   // the seventh load lets the next earliest go
    }

    /// <summary>
    /// **Every other player is told where a player goes** (the mover sends its 0x3f to every
    /// client): Bob goes alone to the cottage, and Anna's machine in the
    /// inn is told where, and takes his character out of its world at once — stopped, not waiting for
    /// the inn's next snapshot. Bob's own machine is not told of himself.
    /// </summary>
    [Fact]
    public void Every_other_player_is_told_where_a_player_goes()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        string inn = game.Place.Name;
        Assert.True(Until(() => Playing(anna, inn) && Playing(bob, inn), () => game.Step(), () => anna.Step(), () => bob.Step()), "never both played");
        var bobs = Named(game, "Bob");
        Assert.True(Until(() => anna.Client.Mirror?.Find(bobs.Avatar!.Guid) is { InWorld: true }, () => game.Step(), () => anna.Step(), () => bob.Step()), "Anna never saw Bob");
        var bobOnAnnas = anna.Client.Mirror!.Find(bobs.Avatar!.Guid)!;

        Assert.True(game.Server.Places.Move(bobs.Avatar!, Cottage, 0));
        // Only the host's messages are polled on Anna's side: the Went arrives before any snapshot of the inn.
        Assert.True(Until(() => anna.Client.Whereabouts.ContainsKey(bobs.Slot), () => game.Server.Poll(game.Ms), () => anna.Client.Poll(0f)), "Anna was never told");
        Assert.Equal((Cottage, game.Story.NameOf(Cottage)), anna.Client.Whereabouts[bobs.Slot]);
        Assert.False(bobOnAnnas.InWorld);
        Assert.Equal(0, anna.Client.Players.Single(p => p.Name == "Bob").Avatar);
        string cottage = game.Story.At(Cottage)!.LevelName;
        Assert.True(Until(() => Playing(bob, cottage), () => game.Step(), () => anna.Step(), () => bob.Step()), "Bob never arrived");
        Assert.DoesNotContain(bobs.Slot, bob.Client.Whereabouts.Keys);
        Assert.Equal(1, anna.Loads);
    }

    /// <summary>
    /// **The host's own journey goes alone, always** (slot 0's own journey): with Bob
    /// alone in the cottage and Anna in the inn, the host's exit to the first catacomb leaves both where
    /// they are and tells both where the host went (its 0x3f goes to every client too); then with nobody
    /// left in the catacomb, its next exit still moves nobody else — earlier that one was taken as the
    /// whole game's journey and dragged Anna and Bob along. A script's `Jump` still takes everybody.
    /// </summary>
    [Fact]
    public void The_host_s_own_journey_never_moves_another_player()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        string inn = game.Place.Name;
        Assert.True(Until(() => Playing(anna, inn) && Playing(bob, inn), () => game.Step(), () => anna.Step(), () => bob.Step()), "never both played");
        Assert.True(game.Server.Places.Move(Named(game, "Bob").Avatar!, Cottage, 0));
        string cottage = game.Story.At(Cottage)!.LevelName;
        Assert.True(Until(() => Playing(bob, cottage), () => game.Step(), () => anna.Step(), () => bob.Step()), "Bob never arrived");

        // An exit is the host's alone; so is the Storyteller's jump of its own character — a script's jump is not.
        Assert.True(game.Server.Alone(new SequenceRequest(SequenceKind.Exit, Catacombs1, 0, Exit: 0)));
        Assert.True(game.Server.Alone(new SequenceRequest(SequenceKind.Teleport, Catacombs1, -2)));
        Assert.False(game.Server.Alone(new SequenceRequest(SequenceKind.Jump, Catacombs1, 0)));

        var first = game.Server.Places.Load!(Catacombs1, 0)!;
        Assert.True(game.Server.Journey(first, null, alone: true));               // Anna stays in the inn it left
        Assert.True(Until(() => anna.Client.Whereabouts.ContainsKey(0) && bob.Client.Whereabouts.ContainsKey(0),
                          () => game.Step(), () => anna.Step(), () => bob.Step()), "the players were never told where the host went");
        Assert.Equal(game.Story.NameOf(Catacombs1), anna.Client.Whereabouts[0].Name);
        Assert.Equal(game.Story.NameOf(Catacombs1), bob.Client.Whereabouts[0].Name);

        var second = game.Server.Places.Load!(Catacombs2, 0)!;
        Assert.False(game.Server.Journey(second, null, alone: true));             // nobody was left in the first catacomb
        for (int i = 0; i < 30; i++) { game.Step(); anna.Step(); bob.Step(); }
        Assert.Equal(1, anna.Loads);
        Assert.Equal(2, bob.Loads);
        Assert.Same(game.Place, game.Server.PlaceOf(Named(game, "Anna")));
        Assert.Same(game.Loaded[Cottage], game.Server.PlaceOf(Named(game, "Bob")));
        Assert.Same(second, game.Server.Place);
        Assert.Equal(game.Story.NameOf(Catacombs2), anna.Client.Whereabouts[0].Name);
    }

    /// <summary>
    /// The Storyteller's Jump to Location of the host's own character is
    /// the host's journey **alone**, as its exit is: the request the jump puts on the chronicle is the
    /// one <see cref="GameServer.Alone"/> answers for, and a script's own `Jump` to the same place is not
    /// once the host has moved.
    /// </summary>
    [Fact]
    public void The_storyteller_s_jump_of_the_host_s_own_character_is_the_host_s_alone()
    {
        using var files = Install.Open();
        using var game = Host(files);
        var mine = game.Place.Place("MP_Christof_DA", game.Place.StartPosition, Vector3.Zero, game.Place.At(game.Place.StartPosition, 0))!;
        game.Server.Slots[0]!.Avatar = mine;
        Assert.True(game.Server.Places.JumpTo(mine, Cottage));
        Assert.Equal(SequenceKind.Jump, game.Story.Pending!.Value.Kind);
        Assert.True(game.Server.Alone(game.Story.Pending));
        var cottage = game.Server.Places.Load!(Cottage, 0)!;
        game.Server.Journey(cottage, mine, alone: true);
        game.Story.Arrive();
        Assert.True(game.Story.Jump(game.Story.NameOf(Cottage), 0));             // a script's jump, the same place
        Assert.False(game.Server.Alone(game.Story.Pending));
    }

    /// <summary>
    /// **A character nobody plays, moved between places** (the mover's last branch): only the one
    /// being steered goes (the Storyteller's possessed body). A townsman of the inn, possessed,
    /// is jumped to the cottage where Anna is: it leaves the inn, stands at the cottage's first start with
    /// its own sheet, the cottage's scripts are told (`Came`) and Anna's machine is sent it;
    /// one not steered stays where it is.
    /// </summary>
    [Fact]
    public void A_possessed_character_nobody_plays_is_moved_to_another_place()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        string inn = game.Place.Name;
        Assert.True(Until(() => Playing(anna, inn), () => game.Step(), () => anna.Step()), "never played");
        Assert.True(game.Server.Places.Move(Named(game, "Anna").Avatar!, Cottage, 0));
        string cottage = game.Story.At(Cottage)!.LevelName;
        Assert.True(Until(() => Playing(anna, cottage), () => game.Step(), () => anna.Step()), "Anna never reached the cottage");

        game.Place.Awaken(new Ai.AiClasses(files));
        var townsfolk = game.Place.Things.Where(t => t.InWorld && t.Type == ThingType.Actor && t.Actor is { Dead: false } && t.Mind is not null).ToList();
        Assert.True(townsfolk.Count >= 2, "the inn has nobody to possess");
        var (held, other) = (townsfolk[0], townsfolk[1]);
        Assert.False(game.Server.Places.Move(other, Cottage, 0));                // not steered: 1.1's mover does nothing
        Assert.True(other.InWorld);

        Assert.True(game.Place.SetSteered(held));
        var sheet = held.Actor!;
        WorldThing? came = null;
        game.Server.Places.Came += (_, t) => came = t;
        Assert.True(game.Server.Places.Move(held, Cottage, 0));
        Assert.False(held.InWorld);
        var there = game.Loaded[Cottage];
        Assert.NotNull(came);
        Assert.Same(there, came!.Place);
        Assert.Same(sheet, came.Actor);                                         // its own character, carried
        var start = Location.PickStart(there.AllStarts, 0, 1)!;
        Assert.True(Vector2.Distance(new Vector2(start.Position.X, start.Position.Y), new Vector2(came.Position.X, came.Position.Y)) < 1f);
        Assert.True(Until(() => anna.Client.Mirror?.Find(came.Guid) is { InWorld: true }, () => game.Step(), () => anna.Step()), "Anna's machine never saw it come");
    }

    /// <summary>
    /// **The camera goes with it**: the body a carry made is known
    /// (`Carried`), and the place the camera then looks into (`Watched`) is kept — the unload passes over
    /// the camera's place, as over a player's — and stepped though nobody plays there, by the simulation
    /// a view of it draws from (`Stepper`). Let go of, it is no longer kept, but it is still stepped: a
    /// host ticks every world it has loaded (until then it stood still).
    /// </summary>
    [Fact]
    public void The_place_the_camera_followed_a_carried_body_into_is_kept_and_stepped()
    {
        using var files = Install.Open();
        using var game = Host(files);
        game.Place.Awaken(new Ai.AiClasses(files));
        var held = game.Place.Things.First(t => t.InWorld && t.Type == ThingType.Actor && t.Actor is { Dead: false } && t.Mind is not null);
        Assert.True(game.Place.SetSteered(held));
        var places = game.Server.Places;
        Assert.True(places.Move(held, Cottage, 0));
        var body = places.Carried;
        Assert.NotNull(body);
        var there = game.Loaded[Cottage];
        Assert.Same(there, body!.Place);
        Assert.False(places.Kept(there));                                        // nobody plays there
        Assert.Null(places.Stepper(game.Place));                                 // the host's own is the run's to step

        places.Watched = there;
        Assert.True(places.Kept(there));
        var sim = places.Stepper(there)!;
        float was = sim.Seconds;
        places.Step(0.5f);
        Assert.True(sim.Seconds > was, "the watched place was not stepped");

        places.Watched = null;
        Assert.False(places.Kept(there));
        was = sim.Seconds;
        places.Step(0.5f);
        Assert.True(sim.Seconds > was, "a loaded place nobody is in was not ticked");
    }

    /// <summary>
    /// **Nothing is cancelled at an entrance**: the mover's last branch calls neither the cancel nor
    /// the stop — those are the player branch's — so a steered body carried to another
    /// place in mid-swing arrives still swinging, busy for the same time. Vitomir cancelled it, forced,
    /// until then.
    /// </summary>
    [Fact]
    public void A_steered_body_carried_through_an_entrance_keeps_what_it_was_doing()
    {
        using var files = Install.Open();
        using var game = Host(files);
        game.Place.Awaken(new Ai.AiClasses(files));
        var held = game.Place.Things.First(t => t.InWorld && t.Type == ThingType.Actor && t.Actor is { Dead: false } && t.Mind is not null);
        Assert.True(game.Place.SetSteered(held));
        var me = held.Actor!;
        me.Action = ActionMode.Swing;
        me.BusyUntil = int.MaxValue;
        var places = game.Server.Places;
        Assert.True(places.Move(held, Cottage, 0));
        var body = places.Carried!;
        Assert.Same(me, body.Actor);
        Assert.Equal(ActionMode.Swing, me.Action);                               // no cancel on this branch
        Assert.Equal(int.MaxValue, me.BusyUntil);
    }

    static Vector3 FloorNear(Location place, Vector3 from, float distance)
    {
        var map = place.Walk!;
        for (int a = 0; a < 16; a++)
        {
            float r = a * MathF.PI / 8;
            var p = from + new Vector3(MathF.Cos(r), MathF.Sin(r), 0) * distance;
            int node = map.Find(p, 256f);
            // Not where anything with a body stands: the move order refuses a spot another body is on
            // — in the inn a `tracker_thing` 96 units up is one.
            if (place.Things.Any(t => t.Collide != CollideShape.None && !t.Has(WorldThing.Flag.DisableCollide)
                                      && Vector2.Distance(new(t.Position.X, t.Position.Y), new(p.X, p.Y)) < t.Radius + 48f)) continue;
            if (map.IsGround(node) && map.Nodes[node].Contains(new Vector2(p.X, p.Y))) return p with { Z = map.Nodes[node].Height(p.X, p.Y) };
        }
        return from;
    }
}
