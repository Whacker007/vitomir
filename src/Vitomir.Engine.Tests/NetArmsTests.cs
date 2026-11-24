// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using static Vitomir.Engine.Tests.NetShopsTests;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The network arms of the single-player pieces: a throw on the host
/// is replayed on the players' machines with the roll skipped;
/// held automatic fire on a player's machine sends order 0x1e each loop and the host jumps its own
/// copy back to `RepeatTo`; money dropped on a player's machine is
/// dropped by the host; and a player's conversation left by a
/// journey is reported to the host for `convended(aborted)`.
/// </summary>
public class NetArmsTests
{
    [Fact]
    public void A_throw_on_the_host_is_replayed_on_the_players_machines_with_the_roll_skipped()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");
        var wolf = Body(game, "Bob");
        var her = Body(game, "Anna");
        Assert.True(Until(() => anna.Client.Mirror?.Find(wolf.Guid) is not null && bob.Client.Mirror?.Find(her.Guid) is not null, All), "never met");

        // Bob is a werewolf on every machine — the body and motion set that have `throw2`.
        var wolfThere = anna.Client.Mirror!.Find(wolf.Guid)!;
        foreach (var w in new[] { wolf, wolfThere, bob.Client.Avatar! })
        {
            w.Model = "Werewolf.nod";
            w.MotionSet = "werewolf.nms";
            w.Place!.Reshape(w);
        }
        var claw = Weapon.From("Claw_werewolfBig", game.Place.Templates!.Resolve("Claw_werewolfBig"))!;
        wolf.Actor!.Weapon = claw;
        her.Actor!.MaxHealth = her.Actor.Health = 10_000f;
        int under = Enumerable.Range(1, 100_000).First(s => new Rng(s).Unit() < claw.ThrowChance);

        Assert.True(SpecialMove.Try(wolf, her, claw, game.Place.Milliseconds, new Rng(under)), "the host's grab");
        Assert.Equal(ActionMode.Thrown, her.Actor.Action);
        // Her machine plays it as the host did, roll or no roll: she is thrown, Bob throwing.
        var mine = anna.Client.Avatar!;
        Assert.True(Until(() => mine.Actor!.Action == ActionMode.Thrown, All), "her machine never replayed it: " + string.Join("; ", anna.Said));
        Assert.Equal(ActionMode.Throwing, wolfThere.Actor!.Action);
        // And the thrower's own machine too.
        Assert.True(Until(() => bob.Said.Any(s => s.StartsWith("throw:")), All), "Bob's machine never replayed it");

        // The host's snapshots carry the same two motions: her machine does not start them again.
        var thrown = mine.Animation!.Action;
        var throwing = wolfThere.Animation!.Action;
        Assert.NotNull(thrown);
        for (int i = 0; i < 10; i++) All();
        Assert.Same(thrown, mine.Animation.Action);
        Assert.Same(throwing, wolfThere.Animation.Action);
    }

    /// <summary>
    /// **Every strike is replayed** (the strike message with its last byte 0, replayed →
    /// Bob's swing at Anna on the host is struck again on her machine — his copy turned
    /// to her, swinging, busy — and the host's next snapshots do not start the swing a second time. A
    /// weapon swapped into his hands on the host (the werewolf's claws, `OverrideActorWeapon`) comes with
    /// the strike and is swapped in on her machine.
    /// </summary>
    [Fact]
    public void A_strike_on_the_host_is_struck_again_on_the_players_machines_with_the_weapon_swapped_in()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");
        var his = Body(game, "Bob");
        var her = Body(game, "Anna");
        Assert.True(Until(() => anna.Client.Mirror?.Find(his.Guid) is not null, All), "never met");
        var hisThere = anna.Client.Mirror!.Find(his.Guid)!;
        her.Actor!.MaxHealth = her.Actor.Health = 10_000f;

        var claws = Weapon.From("Claw_werewolfBig", game.Place.Templates!.Resolve("Claw_werewolfBig"))!;
        Assert.True(WeaponOverride.Override(his, claws, clearOffhand: true, hideFists: true));
        claws.CurrentAttack = 0;
        Actions.Strike(his, her, claws, game.Place.Milliseconds, game.Place.Motions);
        Assert.Equal(ActionMode.Swing, his.Actor!.Action);
        Assert.True(Until(() => anna.Said.Any(s => s.StartsWith("strike:")), All), "her machine never struck it again");
        Assert.Equal(ActionMode.Swing, hisThere.Actor!.Action);
        Assert.Same(anna.Client.Avatar, hisThere.Actor.Target);
        Assert.True(WeaponOverride.Active(hisThere.Actor));
        Assert.Equal("Claw_werewolfBig", hisThere.Actor.Weapon!.Name, ignoreCase: true);
        var swing = hisThere.Animation!.Action;
        Assert.NotNull(swing);
        for (int i = 0; i < 6; i++) All();
        Assert.Same(swing, hisThere.Animation.Action);                               // not started twice
    }

    /// <summary>
    /// **A player's machine does not roll its own throw** (<see cref="Repairs.HostThrows"/>). In 1.1
    /// a replayed ordinary strike  goes through the special move's roll again
    /// on the player's machine (skipped only for a replayed throw), so a werewolf's swing the
    /// host did not turn into a throw could throw its victim on one player's screen and nowhere else.
    /// Here Bob's claws throw on every roll (the big werewolf's chance is 1) but the host's copy of him
    /// has no `throw` motion, so the host's strike is a swing; Anna's machine, where he is a werewolf with
    /// the motion, swings too — and the roll itself, asked on her machine with a generator that wins it,
    /// throws nobody there, where the same roll on the host throws.
    /// </summary>
    [Fact]
    public void A_players_machine_does_not_roll_its_own_throw_on_a_replayed_strike()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");
        var his = Body(game, "Bob");
        var her = Body(game, "Anna");
        Assert.True(Until(() => anna.Client.Mirror?.Find(his.Guid) is not null, All), "never met");
        var hisThere = anna.Client.Mirror!.Find(his.Guid)!;
        foreach (var w in new[] { hisThere, bob.Client.Avatar! })
        {
            w.Model = "Werewolf.nod";
            w.MotionSet = "werewolf.nms";
            w.Place!.Reshape(w);
        }
        her.Actor!.MaxHealth = her.Actor.Health = 10_000f;
        var claws = Weapon.From("Claw_werewolfBig", game.Place.Templates!.Resolve("Claw_werewolfBig"))!;
        Assert.True(claws.Current.Is(WeaponFlag.CanSpecialMove) && claws.ThrowChance >= 1f);
        Assert.True(WeaponOverride.Override(his, claws, clearOffhand: true, hideFists: true));
        claws.CurrentAttack = 0;

        Actions.Strike(his, her, claws, game.Place.Milliseconds, game.Place.Motions);
        Assert.Equal(ActionMode.Swing, his.Actor!.Action);                            // the host's: a swing
        Assert.True(Until(() => anna.Said.Any(s => s.StartsWith("strike:")), All), "her machine never struck it again");
        Assert.Equal(ActionMode.Swing, hisThere.Actor!.Action);
        Assert.NotEqual(ActionMode.Thrown, anna.Client.Avatar!.Actor!.Action);
        Assert.True(Until(() => bob.Said.Any(s => s.StartsWith("strike:")), All), "his machine never struck it again");
        Assert.Equal(ActionMode.Swing, bob.Client.Avatar!.Actor!.Action);

        // The roll on her machine, with a generator that wins it: nobody is thrown there.
        int under = Enumerable.Range(1, 100_000).First(s => new Rng(s).Unit() < claws.ThrowChance);
        hisThere.Actor.BusyUntil = 0;
        Assert.False(SpecialMove.Try(hisThere, anna.Client.Avatar, claws, anna.Client.Mirror!.Place.Milliseconds, new Rng(under)));
        Assert.NotEqual(ActionMode.Thrown, anna.Client.Avatar.Actor.Action);
    }

    [Fact]
    public void Money_dropped_on_a_players_machine_is_dropped_by_the_host_and_taken_from_her_purse_at_once()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        void Two() { game.Step(); anna.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null, Two), "never played");
        var her = Body(game, "Anna");
        game.Place.Session!.AddCash(her, 500);
        Assert.True(Until(() => anna.Session.Gold == 500, Two), "the purse never came");

        // On her machine: sent, and taken here at once — no bag made here.
        int bagsBefore = anna.Place!.Things.Count(t => t.Template.Equals(Ui.DropCash.GoldBag, StringComparison.OrdinalIgnoreCase));
        Assert.True(Ui.DropCash.Drop(anna.Client.Avatar!, 120));
        Assert.Equal(380, anna.Session.Gold);
        Assert.Equal(bagsBefore, anna.Place.Things.Count(t => t.Template.Equals(Ui.DropCash.GoldBag, StringComparison.OrdinalIgnoreCase)));

        // The host drops it from her character's purse, as a bag at her feet.
        Assert.True(Until(() => Shops.CashOf(her) == 380, Two), "the host never dropped it");
        var bag = game.Place.Things.Single(t => t.InWorld && t.Template.Equals(Ui.DropCash.GoldBag, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(120, bag.Item!.Amount);
        Assert.True(Vector3.Distance(bag.Position, her.Position) < 100f);

        // More than the purse holds is refused on her machine, and nothing is sent.
        Assert.False(Ui.DropCash.Drop(anna.Client.Avatar!, 1000));
        Assert.Equal(380, anna.Session.Gold);
    }

    [Fact]
    public void A_players_conversation_left_by_the_journey_is_reported_to_the_host_as_aborted()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        void Two() { game.Step(); anna.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null, Two), "never played");
        var her = Body(game, "Anna");
        var heard = new List<string>();
        Assert.True(game.Sim.Talk!.Execute(game.Place, "12_1_Bill", "12_1_Bill", ConversationFlag.Callbacks, her.Guid, game.Ms,
            npc: 0, wire: c => c.Ended += (id, aborted, code) => heard.Add($"ended {id} {aborted} {code}")));
        Assert.True(Until(() => anna.Talk.Current is { Waiting: true }, Two), "her machine never offered the replies");

        // The game moves on while she is talking: her machine abandons it and tells the host, whose
        // script hears it aborted; then she arrives in the new place.
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var settings = GameSettings.For(inn, "Anna's game", GameFlags.AutoStoryteller);
        var next = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        next.Session = game.Place.Session;
        game.Server.Travel(next, null);
        game.Place = next;
        game.Sim = new Simulation(next) { Talk = new Conversations() };
        Assert.True(Until(() => heard.Count > 0, Two), "the host never heard the end: " + string.Join("; ", anna.Said));
        Assert.Equal($"ended {her.Guid} True 0", heard.Single());
        Assert.True(Until(() => game.Server.Players.Single(p => p.Name == "Anna").Avatar is { InWorld: true }, Two), "she never arrived");
        Assert.Null(game.Server.Players.Single(p => p.Name == "Anna").Talking);
    }

    sealed class Sink : IOrderSink
    {
        public readonly List<NetOrder> Orders = [];
        public void Send(NetOrder order) => Orders.Add(order);
        public void Send(NetCast cast) { }
        public void Send(PackGesture move) { }
    }

    [Fact]
    public void Held_fire_on_a_players_machine_sends_the_aim_order_and_the_host_jumps_its_copy_back()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        foreach (var m in minds.All) if (m.Thing.Actor is { } a) a.Flags |= Actor.Flag.AiPaused;
        var christof = place.Steered!;
        var me = christof.Actor!;
        var uzi = Weapon.From("submachineGun", place.Templates!.Resolve("submachineGun"))!;
        foreach (var attack in uzi.Attacks) attack.Ammo = attack.MaxAmmo = 1000;
        me.Weapon = uzi;
        var other = minds.All.Select(m => m.Thing).First(t => !ReferenceEquals(t, christof) && t.Actor is not null);
        int now = place.Milliseconds;

        // A player's machine: the attack plays but is the host's, so the character is not in the
        // swing here. At the `Repeat` tag it jumps back itself and sends the host order 0x1e.
        Actions.Strike(christof, other, uzi, now, place.Motions);
        me.Action = ActionMode.None;
        var sink = new Sink();
        NetOrders.Attach(place, NetMode.Client, sink);
        try
        {
            me.Pointing = new HeldFire.Pointer(true, other, null);
            Assert.True(HeldFire.Repeat(christof, now));
            Assert.Equal(9f, christof.Animation!.Action!.Player.Frame);
            var sent = sink.Orders.Single();
            Assert.Equal(OrderKind.Aim, sent.Kind);
            Assert.Equal(other.Guid, sent.Target);
        }
        finally { NetOrders.Detach(place); }

        // The host: in the attack, the order jumps the animation back to `RepeatTo`, with no busy time.
        me.Pointing = null;
        me.BusyUntil = 0;
        Actions.Strike(christof, other, uzi, now, place.Motions);
        Assert.Equal(ActionMode.Swing, me.Action);
        var sim = new Simulation(place);
        for (int t = 0; t < 400; t += 33) sim.Step(1f / 30f);
        Assert.True(christof.Animation!.Action!.Player.Frame > 10f);
        int busy = me.BusyUntil;
        var order = new NetOrder(christof.Guid, OrderKind.Aim, other.Guid, default, 0);
        Assert.Equal(OrderResult.Started, ServerOrders.Apply(place, christof, order, place.Milliseconds));
        Assert.Equal(9f, christof.Animation.Action!.Player.Frame);
        Assert.Equal(busy, me.BusyUntil);
    }

    /// <summary>
    /// A moderated game's host with Shift held lowers a stat instead (`DIK_LSHIFT` /
    /// `DIK_RSHIFT`), seated or not and for anybody's character, refunding nothing, and never below 1;
    /// a player's machine, or an unmoderated host, raises as ever.
    /// </summary>
    [Fact]
    public void A_moderated_hosts_shift_lowers_a_stat_but_not_below_one()
    {
        using var files = Install.Open();
        var powers = new DisciplineDefinitions(files);
        var place = Location.Load(files, "p1_inn4", player: "MP_Serena");
        var mine = place.Player!.Thing;
        var theirs = place.Place("MP_Erik", mine.Position + new Vector3(64, 0, 0), mine.Orientation, mine.Sector)!;
        var who = theirs.Actor!;
        who.SpendableExperience = 0;
        bool shift = true;

        var host = new Advancement(theirs, [theirs], powers) { Network = new NetAdvanceRules(true, true, false, mine, () => shift) };
        float before = who.BaseStat[0];
        Assert.True(host.BuyStat(0));
        Assert.Equal(before - 1, who.BaseStat[0]);
        Assert.Equal(0, who.SpendableExperience);
        who.SetBaseStat(0, 1f);
        Assert.True(host.BuyStat(0));                                   // taken, and nothing happens
        Assert.Equal(1f, who.BaseStat[0]);

        // Not moderated, or a player's machine: Shift changes nothing — no experience, no raise.
        who.SetBaseStat(0, before);
        var open = new Advancement(theirs, [theirs], powers) { Network = new NetAdvanceRules(false, true, false, theirs, () => shift) };
        Assert.False(open.BuyStat(0));
        var player = new Advancement(theirs, [theirs], powers) { Network = new NetAdvanceRules(true, false, false, theirs, () => shift) };
        Assert.False(player.BuyStat(0));
        Assert.Equal(before, who.BaseStat[0]);

        // Let go of Shift, and the host's seat raises again, free.
        shift = false;
        var seat = new Advancement(theirs, [theirs], powers) { Network = new NetAdvanceRules(true, true, true, mine, () => shift) };
        Assert.True(seat.BuyStat(0));
        Assert.Equal(before + 1, who.BaseStat[0]);
    }

    /// <summary>
    /// What 1.1 does to a leaving player's character before writing it: effects off, revived
    /// at full health and blood, the disciplines' clocks cleared, at rest, and the hand put in the pack
    /// — so the file that comes back next game holds a live character with its sword in the pack.
    /// </summary>
    [Fact]
    public void A_leaving_players_character_is_revived_at_rest_and_its_hand_put_away_before_it_is_written()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Steered!;
        var me = christof.Actor!;
        var sword = Treasure.Make(place, place.Treasure!.Build("BroadSword")!)!;
        Assert.True(Ground.Hand(christof, sword.Item!));
        foreach (var d in me.Disciplines.All) d.RecastUntil = 99_999;
        me.BusyUntil = 99_999;
        me.Action = ActionMode.Swing;
        Damage.Kill(christof, null, HitKind.Melee, DamageType.Lethal, nowMs: 0);
        Assert.True(me.Dead);

        NetArms.TidyForLeaving(christof);
        Assert.False(me.Dead);
        Assert.Equal(me.MaxHealth, me.Health);
        Assert.Equal(0, me.BusyUntil);
        Assert.Equal(ActionMode.None, me.Action);
        Assert.All(me.Disciplines.All, d => Assert.Equal(0, d.RecastUntil));
        Assert.Null(me.Held);
        Assert.Contains(sword.Item!, me.Inventory.Items);
        var sheet = NetSheet.From(christof);
        Assert.Contains(sheet.Pack, i => i.Template.Equals(sword.Template, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A character's effects are kept with it (1.1's file is the whole actor): what is on it but worn
    /// gear's goes in its sheet with the time it has left, over the wire and back, and comes back on the
    /// character made again from it — counted from the new place's clock. A sheet from before (no
    /// effects known) leaves the character as it is made.
    /// </summary>
    [Fact]
    public void A_characters_effects_are_kept_with_it_and_come_back_with_the_time_they_had_left()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Steered!;
        place.Milliseconds = 10_000;
        EffectRuntime.Apply(christof, "ef_restrain", 0);
        var restrain = christof.Effects.All.Single(e => e.Definition.Name.Equals("ef_restrain", StringComparison.OrdinalIgnoreCase));
        restrain.Until = 15_000;

        christof.Actor!.Cash = 321;
        var sheet = NetSheet.From(christof);
        Assert.Contains(sheet.Effects!, e => e.Name.Equals("ef_restrain", StringComparison.OrdinalIgnoreCase) && e.Left == 5_000);
        Assert.Equal(321, sheet.Cash);
        var w = new NetWriter(Msg.Sheet);
        sheet.Write(w);
        var r = new NetReader(w.ToArray());
        var back = NetSheet.Read(r);
        Assert.Equal(sheet.Effects!.Count, back.Effects!.Count);

        var again = place.Place("Christof", christof.Position + new Vector3(100, 0, 0), christof.Orientation, christof.Sector)!;
        Assert.DoesNotContain(again.Effects.All, e => e.Definition.Name.Equals("ef_restrain", StringComparison.OrdinalIgnoreCase));
        place.Milliseconds = 50_000;
        back.Apply(again, place, withPack: true);
        var kept = again.Effects.All.Single(e => e.Definition.Name.Equals("ef_restrain", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(55_000, kept.Until);
        Assert.Equal(321, again.Actor!.Cash);

        // A sheet written before effects were kept changes none of them.
        var third = place.Place("Christof", christof.Position + new Vector3(200, 0, 0), christof.Orientation, christof.Sector)!;
        int had = third.Effects.All.Count;
        (sheet with { Effects = null }).Apply(third, place, withPack: true);
        Assert.Equal(had, third.Effects.All.Count);
    }

    /// <summary>
    /// In a network game every character spends its own money (the purse's read and write use the
    /// party's purse only outside one): the host's own party character's purse is its cash, not the
    /// session's, while single player's is the party's.
    /// </summary>
    [Fact]
    public void In_a_network_game_the_hosts_own_character_spends_its_own_money()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var session = new Session { Here = place, Gold = 1000 };
        place.Session = session;
        var christof = place.Steered!;
        christof.Actor!.Flags |= Actor.Flag.Party;
        christof.Actor.Cash = 50;
        Assert.Equal(1000, Shops.CashOf(christof));
        NetOrders.Attach(place, NetMode.Server);
        try
        {
            Assert.Equal(50, Shops.CashOf(christof));
            session.AddCash(christof, 25);
            Assert.Equal(75, christof.Actor.Cash);
            Assert.Equal(1000, session.Gold);
        }
        finally { NetOrders.Detach(place); }
    }
}
