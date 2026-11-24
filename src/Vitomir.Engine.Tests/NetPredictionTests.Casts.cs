// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Net;
using System.Numerics;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The start of her own discipline predicted** (<see cref="OwnCast"/>): a cast whose start is certain on her machine plays its
/// spell animation there at the click, the host's answer bears it out without starting it again, and the host's refusals take it
/// back cleanly; a discipline whose <c>cancast</c> her machine cannot judge is not guessed; off, as before. The script layers here are
/// <see cref="Scripted"/> (the engine's tests have no Java): the host's answers its <c>cancast</c> as told, hers judges as told;
/// what the shipped scripts read on her machine is <c>Vitomir.Scripting.Tests</c>' <c>CastJudgeTests</c>.
/// </summary>
public sealed partial class NetPredictionTests
{
    /// <summary>A script layer as the engine sees one: it makes an object for every class, <c>cancast</c> says <see cref="Says"/>, a judge on her machine <see cref="Judges"/>.</summary>
    sealed class Scripted : IScriptObjects
    {
        public bool Says = true;
        public bool? Judges = true;
        public int Judged;
        public object? Make(string className, WorldThing owner) => new object();
        public void Free(object script) { }
        public void EffectStarted(object script, WorldThing actor, Effect effect, int duration) { }
        public void EffectEnded(object script, WorldThing actor, Effect effect, int reason) { }
        public bool CanCast(object script, int level, WorldThing caster, WorldThing? target, Vector3? at) => Says;
        public bool? Judge(object script, int level, WorldThing caster, WorldThing? target, Vector3? at) { Judged++; return Judges; }
        public void Cast(object script, CastForm form, int level, WorldThing caster, WorldThing? target, Vector3 at) { }
        public void ActorCast(WorldThing caster, string discipline, int level, int castNum) { }
        public void ActorFeed(WorldThing feeder, WorldThing victim, int status) { }
        public void Stilled(WorldThing actor) { }
        public void Unstill(WorldThing actor) { }
        public void Rehome(object script, WorldThing owner) { }
    }

    /// <summary>
    /// The host teaches her a discipline at level 1 with her blood full, and her machine has it from the host's sheet (with its script
    /// object: one her own install already knew was made before her machine had a script layer). Her copy's instance.
    /// </summary>
    static Discipline Teach(Pair pair, string name)
    {
        var powers = new DisciplineDefinitions(Install.Open());
        var hers = pair.Hers.Actor!;
        Assert.NotNull(DisciplineBook.SetLevel(pair.Hers, powers, name, 1));
        hers.Stat[(int)ActorStat.Blood] = hers.Stat[(int)ActorStat.BloodPool] = 200f;
        // The row's stat checks met (a positive value a minimum, a negative one a maximum), on the host and so in her sheet.
        foreach (var (stat, value) in powers[name]!.Checks)
            hers.Stat[stat] = value > 0f ? Math.Max(hers.Stat[stat], value) : Math.Min(hers.Stat[stat], -value);
        var her = pair.Mine.Player!.Thing;
        pair.Turns(120, () => her.Actor!.Disciplines[name] is { Level: 1 } && her.Actor.Stat[(int)ActorStat.Blood] >= 200f);
        var mine = her.Actor!.Disciplines[name];
        Assert.NotNull(mine);
        Assert.Equal(1, mine!.Level);
        mine.Script ??= new object();
        pair.Turns(15);
        return mine;
    }

    static (Pair Pair, Scripted Host, Scripted Hers) Casting(Vfs.GameFiles files, bool predict = true)
    {
        var (host, hers) = (new Scripted(), new Scripted());
        return (Pair.Open(files, predict, (host, hers)), host, hers);
    }

    /// <summary>Her cast on her machine: the order as the game's right click gives it.</summary>
    static OrderResult CastHere(Pair pair, Discipline power, WorldThing? at = null) =>
        Cast.Order(pair.Mine.Player!.Thing, at, null, power, InputModifier.None);

    /// <summary>
    /// A cast she is sure of (Blood Healing, on herself) plays its spell animation on her machine at the click, ACTION 13 and busy, before
    /// the host has heard of it, with nothing of the effect: her blood as it was. The host's answer bears it out (the host casting it),
    /// the host's snapshot of the cast does not start the animation again, her recast clock is the host's, and at the cast's end her copy
    /// is out of ACTION 13 as one the host alone cast leaves it.
    /// </summary>
    [Fact]
    public void A_cast_she_is_sure_of_starts_at_once_and_the_hosts_answer_bears_it_out_without_a_restart()
    {
        using var files = Install.Open();
        var (pair, _, judge) = Casting(files);
        using var _p = pair;
        var power = Teach(pair, "BloodHealing");
        var her = pair.Mine.Player!.Thing;
        var casts = pair.Client.Casts;
        float blood = her.Actor!.Stat[(int)ActorStat.Blood];
        Assert.Equal(OrderResult.Started, CastHere(pair, power));
        var spell = her.Animation!.Action;
        Assert.NotNull(spell);
        Assert.Equal(Cast.MotionBase + power.Definition.CastAnimation, spell!.Mode.Index);
        Assert.Equal(ActionMode.Cast, her.Actor.Action);
        Assert.True(her.Actor.BusyUntil > pair.Mine.Milliseconds);
        Assert.True(casts.Pending);
        Assert.Equal(1, judge.Judged);
        Assert.Equal(blood, her.Actor.Stat[(int)ActorStat.Blood]);               // the blood is the host's to spend
        Assert.NotEqual(ActionMode.Cast, pair.Hers.Actor!.Action);              // the host has not heard of it yet
        pair.Turns(120, () => casts.Done.Confirmed > 0);
        Assert.Equal(1, casts.Done.Confirmed);
        Assert.Equal(ActionMode.Cast, pair.Hers.Actor.Action);
        Assert.True(casts.Blood(her) < her.Actor.Stat[(int)ActorStat.Blood], "what the begun cast will spend is not counted");
        pair.Turns(10);
        Assert.Same(spell, her.Animation.Action);                                // the host's snapshot did not start it again
        Assert.True((uint)power.RecastUntil > (uint)pair.Mine.Milliseconds, "her recast clock is not running");
        pair.Turns(150, () => her.Actor.Action != ActionMode.Cast);
        Assert.Equal(ActionMode.None, her.Actor.Action);
        Assert.Equal(1, casts.Done.Predicted);
        Assert.Equal(0, casts.Done.TakenBack);
        Assert.True(power.Definition.CostAt(1) > 0 && pair.Hers.Actor.Stat[(int)ActorStat.Blood] < 200f, "the host never spent the blood");
    }

    /// <summary>
    /// The host refuses what her machine guessed — her blood gone there, its recast clock running there, the discipline's
    /// <c>cancast</c> saying no there, the target gone from the host's world, or her character busy there with something her machine
    /// has not heard of. Her cast is taken back at the host's answer: the animation stopped, ACTION 13 gone, her busy time and recast
    /// clock as before (for the clock, the host's), counted with the host's reason; nothing of the effect after it.
    /// </summary>
    [Theory]
    [InlineData("blood", CastWhy.Blood)]
    [InlineData("recast", CastWhy.Recast)]
    [InlineData("script", CastWhy.Script)]
    [InlineData("gone", CastWhy.Gone)]
    [InlineData("busy", CastWhy.Busy)]
    public void A_cast_the_host_refuses_is_taken_back_cleanly(string why, CastWhy reason)
    {
        using var files = Install.Open();
        var (pair, host, _) = Casting(files);
        using var _p = pair;
        var power = Teach(pair, "EyesOfChaos");
        var (there, here) = Beside(pair);
        var her = pair.Mine.Player!.Thing;
        var casts = pair.Client.Casts;
        var hers = pair.Hers.Actor!;
        int busyBefore = her.Actor!.BusyUntil, recastBefore = power.RecastUntil;
        switch (why)
        {
            case "blood": hers.Stat[(int)ActorStat.Blood] = 0f; break;
            case "recast": hers.Disciplines["EyesOfChaos"]!.RecastUntil = pair.Host.Milliseconds + 60_000; break;
            case "script": host.Says = false; break;
            case "gone": pair.Host.Remove(there, drop: false); break;
            case "busy": hers.BusyUntil = pair.Host.Milliseconds + 60_000; break;
        }
        Assert.Equal(OrderResult.Started, CastHere(pair, power, here));
        Assert.True(casts.Pending, $"not guessed: {casts.NotWhy}");
        Assert.NotNull(her.Animation!.Action);
        pair.Turns(120, () => !casts.Pending);
        Assert.False(casts.Pending);
        Assert.Equal(1, casts.Done.Refused);
        Assert.Equal(1, casts.Done.RefusedFor.GetValueOrDefault(reason));
        Assert.Equal(0, casts.Done.Confirmed);
        Assert.Null(her.Animation.Action);
        Assert.Equal(ActionMode.None, her.Actor.Action);
        Assert.Equal(busyBefore, her.Actor.BusyUntil);
        if (why == "recast") Assert.True(power.RecastUntil - pair.Mine.Milliseconds > 50_000, $"her clock {power.RecastUntil - pair.Mine.Milliseconds} ms, not the host's");
        else Assert.Equal(recastBefore, power.RecastUntil);
        pair.Turns(30);
        Assert.Null(her.Animation.Action);                                       // no stuck pose, and no cast of the host's after it
        Assert.NotEqual(ActionMode.Cast, pair.Hers.Actor!.Action);
    }

    /// <summary>
    /// A discipline whose <c>cancast</c> reads what her machine does not have (her judge answers nothing) is not guessed: nothing at the
    /// click, counted; the cast goes as before and the host's start comes back in its snapshot.
    /// </summary>
    [Fact]
    public void A_discipline_whose_script_her_machine_cannot_judge_is_not_guessed()
    {
        using var files = Install.Open();
        var (pair, _, judge) = Casting(files);
        using var _p = pair;
        judge.Judges = null;
        var power = Teach(pair, "BloodHealing");
        var her = pair.Mine.Player!.Thing;
        Assert.Equal(OrderResult.Started, CastHere(pair, power));
        Assert.Null(her.Animation!.Action);
        Assert.False(pair.Client.Casts.Pending);
        Assert.Equal(1, pair.Client.Casts.Done.Unjudged);
        Assert.Equal("cancast not judged here", pair.Client.Casts.NotWhy);
        pair.Turns(120, () => her.Animation.Action is not null);
        Assert.NotNull(her.Animation.Action);                                    // the host's, from its snapshot
        Assert.Equal(0, pair.Client.Casts.Done.Predicted);
    }

    /// <summary>
    /// Her machine keeps the recast clock: after a cast the host began, a second click once her busy time is over but inside the
    /// discipline's recast (Eyes of Chaos: 2 s, its animation 1.6) is not guessed — the host would refuse it — and a clock the host
    /// runs that her machine did not start (set there) reaches her with the host's next sheet.
    /// </summary>
    [Fact]
    public void Her_machine_keeps_the_recast_clock_from_the_hosts_answers_and_sheets()
    {
        using var files = Install.Open();
        var (pair, _, _) = Casting(files);
        using var _p = pair;
        var power = Teach(pair, "EyesOfChaos");
        var (_, here) = Beside(pair);
        var her = pair.Mine.Player!.Thing;
        var casts = pair.Client.Casts;
        Assert.Equal(OrderResult.Started, CastHere(pair, power, here));
        Assert.True(casts.Pending, $"not guessed: {casts.NotWhy}");
        pair.Turns(120, () => casts.Done.Confirmed > 0);
        Assert.Equal(1, casts.Done.Confirmed);
        pair.Turns(150, () => her.Actor!.BusyUntil <= pair.Mine.Milliseconds && her.Animation?.Action is null);
        Assert.True((uint)pair.Mine.Milliseconds < (uint)power.RecastUntil, "the recast was over with the animation");
        Assert.Null(casts.Sure(her, here, null, power, InputModifier.None));
        pair.Turns(120, () => (uint)pair.Mine.Milliseconds >= (uint)power.RecastUntil);
        pair.Turns(30);
        Assert.NotNull(casts.Sure(her, here, null, power, InputModifier.None));
        pair.Hers.Actor!.Disciplines["EyesOfChaos"]!.RecastUntil = pair.Host.Milliseconds + 30_000;
        pair.Turns(60, () => power.RecastUntil - pair.Mine.Milliseconds > 20_000);
        Assert.True(power.RecastUntil - pair.Mine.Milliseconds > 20_000, "the host's sheet did not bring its clock");
        Assert.Null(casts.Sure(her, here, null, power, InputModifier.None));
    }

    /// <summary>With prediction off (the developer's <c>--no-predict</c>) her cast waits for the host, as before: nothing guessed, nothing numbered.</summary>
    [Fact]
    public void With_prediction_off_her_cast_waits_for_the_host()
    {
        using var files = Install.Open();
        var (pair, _, judge) = Casting(files, predict: false);
        using var _p = pair;
        var power = Teach(pair, "BloodHealing");
        var her = pair.Mine.Player!.Thing;
        Assert.Equal(OrderResult.Started, CastHere(pair, power));
        Assert.Null(her.Animation!.Action);
        Assert.NotEqual(ActionMode.Cast, her.Actor!.Action);
        Assert.Equal(0, judge.Judged);
        pair.Turns(120, () => her.Animation.Action is not null);
        Assert.NotNull(her.Animation.Action);
        Assert.Equal(0, pair.Client.Casts.Done.Predicted);
    }

    /// <summary>A target out of the discipline's range (by the margin) is not guessed: the host decides the walk into range.</summary>
    [Fact]
    public void A_target_out_of_range_is_not_guessed()
    {
        using var files = Install.Open();
        var (pair, _, _) = Casting(files);
        using var _p = pair;
        var power = Teach(pair, "EyesOfChaos");
        var (there, _) = Beside(pair);
        var her = pair.Mine.Player!.Thing;
        float range = power.Definition.MaxDistance;
        pair.Host.Move(there, pair.Hers.Position + new Vector3(range - OwnCast.RangeMargin / 2, 0, 0));
        WorldThing? here = null;
        pair.Turns(300, () => (here = pair.Client.Mirror?.Find(there.Guid)) is not null && pair.Client.Mirror!.Still(here, OwnCast.StillMs + 100)
                              && Vector3.Distance(here.Position, there.Position) < 1f);
        Assert.Null(pair.Client.Casts.Sure(her, here, null, power, InputModifier.None));
        Assert.Equal("out of range", pair.Client.Casts.NotWhy);
    }

    /// <summary>A player's machine of protocol 21 (whose casts are not numbered) is refused at its Hello, as any other protocol is.</summary>
    [Fact]
    public void A_protocol_21_machine_is_refused_at_its_hello()
    {
        using var server = new GameServer(GameSettings.For(ChronicleSet.Load(Install.Open())["MP_DAInn"]!, "Anna's game", GameFlags.AutoStoryteller),
                                          new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
        server.Start();
        using var peer = new NetPeer(0, seed: 9);
        var link = peer.Connect(new IPEndPoint(IPAddress.Loopback, server.Peer.Port));
        string? refused = null;
        int ms = 0;
        var clock = Stopwatch.StartNew();
        while (refused is null && clock.ElapsedMilliseconds < 10_000)
        {
            server.Poll(ms += 10);
            foreach (var e in peer.Poll())
            {
                if (e.Kind == NetEventKind.Connected) peer.Send(link, Channel.Reliable, new NetWriter(Msg.Hello).I32(21).ToArray());
                else if (e.Kind == NetEventKind.Message && new NetReader(e.Data) is { Kind: Msg.Refused } r) refused = r.Str();
            }
            Thread.Sleep(1);
        }
        Assert.Equal(23, Protocol.Version);
        Assert.Equal("UI_NET_BADPROTOCOL", refused);
    }

    sealed record Spells(double FirstFrameMs, double MeanMs, int Clicks, int Cast, OwnCast.Counts Counts);

    /// <summary>
    /// **The start of her own discipline, measured**: as the swing's measure, Blood Healing on herself at each round trip, a few
    /// casts with prediction off (the spell animation comes back in the host's snapshot) and on: the time from the click to the spell
    /// animation's first frame on her machine, and how often a guessed start was taken back. Between clicks both machines wait until
    /// she is free and the recast over.
    /// </summary>
    [Fact, Trait("Category", "Timing")]
    public void Her_first_cast_frame_measured_at_five_round_trips_with_loss_and_jitter()
    {
        using var files = Install.Open();
        var lines = new List<string>
        {
            "the start of her own discipline on her machine, 30 frames a second, the simulator on her machine alone (loss 2 %, jitter 20 ms):",
            "  round trip | click to spell off (first, mean) | on (first, mean) | clicks | guessed | borne out | taken back (refused, approach, other, no answer)",
        };
        foreach (int rtt in RoundTripsMeasured)
        {
            var off = Spell(files, rtt, predict: false, clicks: 4);
            var on = Spell(files, rtt, predict: true, clicks: 8);
            var c = on.Counts;
            lines.Add($"  {rtt,4} ms    | {off.FirstFrameMs,6:0} ms, {off.MeanMs,6:0} ms             | {on.FirstFrameMs,4:0} ms, {on.MeanMs,4:0} ms     | {on.Clicks,6} | {c.Predicted,7} | {c.Confirmed,9} | {c.TakenBack} ({c.Refused}, {c.Approached}, {c.Differed}, {c.Unanswered})");
            Assert.True(on.Cast == on.Clicks && off.Cast == off.Clicks, lines[^1]);
            Assert.True(on.MeanMs < off.MeanMs, lines[^1]);
            Assert.True(on.MeanMs <= 80, $"her spell's first frame with prediction: {on.MeanMs:0} ms\n{lines[^1]}");
            Assert.True(c.Predicted == on.Clicks && c.TakenBack == 0, lines[^1]);
        }
        foreach (var l in lines) output.WriteLine(l);
        if (Environment.GetEnvironmentVariable("VITOMIR_CAST_REPORT") is { Length: > 0 } report) File.WriteAllLines(report, lines);
    }

    static Spells Spell(Vfs.GameFiles files, int rtt, bool predict, int clicks)
    {
        var settings = GameSettings.For(ChronicleSet.Load(files)["MP_DAInn"]!, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        var powers = new DisciplineDefinitions(files);
        place.Awaken(new Ai.AiClasses(files), 1, new EffectDefinitions(files), powers);
        place.ScriptObjects = new Scripted();
        using var server = new GameServer(settings, new NetPeer(0) { Sim = null }) { Qol = new Qol(), Fixes = new Fixes() };
        server.Start();
        server.Enter(place, null);
        var sim = new Simulation(place);
        var peer = new NetPeer(0) { Sim = new NetSim(new NetSim.Spec(LatencyMs: rtt, JitterMs: 20, Loss: 2, Seed: 13 + rtt), 13 + rtt) };
        using var client = new GameClient(peer, new IPEndPoint(IPAddress.Loopback, server.Peer.Port), new NetCharacter { Name = "Anna", Template = "MP_Serena" })
        { Powers = powers };
        client.Own.On = predict;
        Location? mine = null;
        Simulation? herSim = null;
        client.Started += () =>
        {
            var s = client.Settings!;
            mine = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: "MP_Serena", entrance: s.Entrance);
            mine.ScriptObjects = new Scripted();
            herSim = new Simulation(mine);
            client.Enter(mine, mine.Player?.Thing);
        };
        var wall = Stopwatch.StartNew();
        long frame = 0;
        void Turn()
        {
            sim.Step(Step);
            server.Poll((int)wall.ElapsedMilliseconds);
            client.Poll(Step);
            herSim?.Step(Step);
            frame++;
            long wait = (long)(frame * Step * 1000) - wall.ElapsedMilliseconds;
            if (wait > 0) Thread.Sleep((int)wait);
        }
        bool Until(Func<bool> done, int ms)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < ms) { Turn(); if (done()) return true; }
            return done();
        }
        Assert.True(Until(() => client.Avatar is not null && server.Players.Any(p => p.Name == "Anna" && p.Avatar is not null), 30_000), "never played");
        var hers = server.Players.Single(p => p.Name == "Anna").Avatar!;
        Assert.NotNull(DisciplineBook.SetLevel(hers, powers, "BloodHealing", 1));
        var her = mine!.Player!.Thing;
        Discipline? power = null;
        Assert.True(Until(() => (power = her.Actor!.Disciplines["BloodHealing"]) is { Level: 1 }, 10_000), "her machine never had it");
        power!.Script ??= new object();
        Until(() => false, 1_000);
        double first = double.PositiveInfinity, total = 0;
        int cast = 0;
        for (int n = 0; n < clicks; n++)
        {
            hers.Actor!.Stat[(int)ActorStat.Blood] = hers.Actor.Stat[(int)ActorStat.BloodPool] = 500f;
            // Both free, her blood on her machine, both clocks over.
            Until(() => her.Actor!.Action == ActionMode.None && her.Animation?.Action is null && her.Actor.BusyUntil <= mine.Milliseconds
                        && hers.Actor.Action is ActionMode.None or ActionMode.Cast && hers.Actor.BusyUntil <= place.Milliseconds
                        && hers.Animation?.Action is null && !client.Casts.Pending && her.Actor.Stat[(int)ActorStat.Blood] >= 400f
                        && (uint)mine.Milliseconds >= (uint)power.RecastUntil
                        && (uint)place.Milliseconds >= (uint)hers.Actor.Disciplines["BloodHealing"]!.RecastUntil, 10_000);
            Until(() => false, 600);
            var click = Stopwatch.StartNew();
            Cast.Order(her, null, null, power, InputModifier.None);
            double local = her.Animation?.Action is not null ? 0 : -1;
            if (local < 0)
                Until(() =>
                {
                    if (local < 0 && her.Animation?.Action is not null) local = click.Elapsed.TotalMilliseconds;
                    return local >= 0;
                }, 5_000);
            // The click's own frame is the next one drawn: a start at the click shows then, a frame's 33 ms on.
            if (local == 0) local = Step * 1000;
            if (local > 0) { cast++; total += local; if (n == 0) first = local; }
            // The host's cast, and its animation played out.
            Until(() => hers.Actor.Action == ActionMode.Cast && !client.Casts.Pending, 5_000);
            Until(() => false, 2_000);
        }
        return new Spells(first, cast == 0 ? double.PositiveInfinity : total / cast, clicks, cast, client.Casts.Done);
    }
}
