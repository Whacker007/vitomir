// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Multiplayer's remainder** (a comparison with 1.1's gap 1):
/// a chat line moves the speaker's lips — motion `talk`, the mouth busy three seconds, the `Speaking` bit and the
/// white shell, ended by the actor's frame — on each machine the line reaches; the
/// host's held fire **with a thing** strikes at once and flags the thing `TARGETTED`,
/// where Vitomir gave the attack order; and the host hands the haven vault for another
/// player's character to that player's machine (message 0x47).
/// </summary>
public class NetRemainderTests
{
    static bool Until(Func<bool> done, params Action[] turns)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 10000)
        {
            foreach (var t in turns) t();
            if (done()) return true;
            Thread.Sleep(1);
        }
        return done();
    }

    sealed class Two : IDisposable
    {
        public required GameServer Server;
        public required Location Host;
        public required Simulation HostSim;
        public required GameClient Client;
        public Location? Mine;
        public Simulation? MySim;
        public int Ms;
        public void Step()
        {
            HostSim.Step(1f / 30);
            Ms += 33;
            Server.Poll(Ms);
            Client.Poll(1f / 30);
            MySim?.Step(1f / 30);
        }
        public WorldThing Hers => Server.Players.Single(p => !p.Host).Avatar!;
        public void Dispose() { Client.Dispose(); Server.Dispose(); }
    }

    static Two Start(Vfs.GameFiles files)
    {
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var settings = GameSettings.For(inn, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        var server = new GameServer(settings, new NetPeer(0));
        server.Start();
        server.Enter(place, null);
        var client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, server.Peer.Port),
                                    new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        var two = new Two { Server = server, Host = place, HostSim = new Simulation(place), Client = client };
        client.Started += () =>
        {
            var s = client.Settings!;
            two.Mine = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: "MP_Serena", entrance: s.Entrance);
            two.MySim = new Simulation(two.Mine);
            client.Enter(two.Mine, two.Mine.Player?.Thing);
        };
        Assert.True(Until(() => client.Avatar is not null, two.Step), "never played");
        return two;
    }

    [Fact]
    public void A_chat_line_moves_the_lips_for_three_seconds_and_the_actors_frame_ends_it()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var christof = place.Player!.Thing;
        Assert.Equal("talk", NmsMotionSet.BodyModes[ChatLips.TalkMode]);                     // 0x75
        int now = place.Milliseconds;
        ChatLips.Start(christof, now, place.Motions);
        Assert.Equal(ChatLips.TalkMode, christof.Animation!.Action!.Mode.Index);
        Assert.Equal(now + 3000, christof.MouthBusyUntil);
        Assert.True(christof.Actor!.Has2(Actor.Flag2.Speaking));
        Assert.Equal(ChatLips.Shell, christof.Fx!.Shell);                              // the white shell
        Assert.False(ChatLips.Tick(christof, now + 3000));                             // the line's end before now, strictly
        Assert.True(ChatLips.Tick(christof, now + 3001));
        Assert.False(christof.Actor.Has2(Actor.Flag2.Speaking));
        Assert.Equal(0, christof.MouthBusyUntil);
        Assert.False(ChatLips.Tick(christof, now + 9000));

        // Walking: the bit, but no talk and the mouth's time left as it was; a shell already on stays.
        var monk = place.Things.First(t => t.Type == ThingType.Actor && t.Locomotion is null && t.Animation is not null && !ReferenceEquals(t, christof));
        monk.Walking = true;
        monk.Model3D.SetShell("shellSprite_red", 0, 1f, 1f, 0f);
        ChatLips.Start(monk, now, place.Motions);
        Assert.True(monk.Actor!.Has2(Actor.Flag2.Speaking));
        Assert.Equal(0, monk.MouthBusyUntil);
        Assert.NotEqual(ChatLips.TalkMode, monk.Animation!.Action?.Mode.Index ?? -1);
        Assert.Equal("shellSprite_red", monk.Fx!.Shell);
        Assert.True(ChatLips.Tick(monk, now + 1));                                     // ends on the next frame
    }

    [Fact]
    public void A_players_chat_line_moves_her_lips_on_her_machine_and_on_the_host_only_if_the_host_hears_it()
    {
        using var files = Install.Open();
        using var game = Start(files);
        var hers = game.Hers;
        game.Client.Say("Good evening");
        var mine = game.Client.Avatar!;
        Assert.True(Until(() => mine.Actor!.Has2(Actor.Flag2.Speaking), game.Step), "her machine never moved her lips");
        // The host has no character here, so a said line does not reach its machine and its copy
        // stays still (the lips are each machine's own, `StoryLeftoverTests`); once it talked.
        Assert.False(hers.Actor!.Has2(Actor.Flag2.Speaking));
        // Three seconds of her machine's clock later its actor's frame ends it.
        Assert.True(Until(() => !mine.Actor!.Has2(Actor.Flag2.Speaking), game.Step), "hers never ended");
        Assert.Equal(0, mine.MouthBusyUntil);
    }

    [Fact]
    public void Held_fire_at_a_thing_strikes_at_once_and_flags_it_targetted()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        foreach (var m in minds.All) if (m.Thing.Actor is { } a) a.Flags |= Actor.Flag.AiPaused;
        var christof = place.Steered!;
        var gun = Weapon.From("submachineGun", place.Templates!.Resolve("submachineGun"))!;
        foreach (var attack in gun.Attacks) attack.Ammo = attack.MaxAmmo = 1000;
        var me = christof.Actor!;
        me.Weapon = gun;
        // Somebody far out of reach: 1.1 does not walk to him first (no approach).
        var far = place.Place("MP_Erik", christof.Position + new Vector3(0f, 900f, 0f), christof.Orientation, christof.Sector)!;
        int now = place.Milliseconds;
        var order = new NetOrder(christof.Guid, OrderKind.Aim, far.Guid, default, 0);

        // Busy: the gate turns it away for a thing too — no target, no flag.
        me.BusyUntil = now + 1000;
        Assert.Equal(OrderResult.Ignored, ServerOrders.Apply(place, christof, order, now));
        Assert.Equal(0u, far.Flags & WorldThing.Flag.Targetted);

        me.BusyUntil = now;
        Assert.Equal(OrderResult.Started, ServerOrders.Apply(place, christof, order, now));
        Assert.Equal(ActionMode.Swing, me.Action);                                    // the swing at once
        Assert.Same(far, me.Target);
        Assert.NotEqual(0u, far.Flags & WorldThing.Flag.Targetted);                   // THING_FLAG_TARGETTED (0x2000000) set
        Assert.True(christof.Mind?.Walker.Walking != true);                            // no approach
    }

    [Fact]
    public void The_host_hands_the_vault_for_a_players_character_to_her_machine()
    {
        using var files = Install.Open();
        using var game = Start(files);
        var hers = game.Hers;
        int asked = 0;
        game.Client.VaultAsked += () => asked++;
        // A player's machine does nothing with a script's call; single player's is its own (false).
        Assert.True(StoryVault.Hand(null, client: true, hers.Guid, game.Client.Avatar));
        Assert.False(StoryVault.Hand(null, client: false, hers.Guid, null));
        // The host's control is the one named: the host's own pane.
        Assert.False(StoryVault.Hand(game.Server, client: false, hers.Guid, hers));
        // Nobody's character: nothing, and nothing sent.
        Assert.True(StoryVault.Hand(game.Server, client: false, 0x7654321, null));
        // Hers, not the control: message 0x47 to her machine alone, which opens it there.
        Assert.True(StoryVault.Hand(game.Server, client: false, hers.Guid, null));
        Assert.True(Until(() => asked > 0, game.Step), "her machine was never told");
        Assert.Equal(1, asked);

        // There the pane opens for a player's character (type 7) and not over one already up.
        bool shown = false;
        Assert.Equal(game.Client.Avatar!.Type == ThingType.Player, StoryVault.Asked(game.Client.Avatar, vaultUp: false, () => shown = true));
        Assert.Equal(game.Client.Avatar.Type == ThingType.Player, shown);
        Assert.False(StoryVault.Asked(game.Client.Avatar, vaultUp: true, () => throw new InvalidOperationException()));
    }
}
