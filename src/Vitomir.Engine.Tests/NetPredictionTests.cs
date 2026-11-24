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
/// **Her own walk predicted, measured** (<see cref="OwnWalk"/>). The simulator on her machine alone, as before
/// The latency measure (<see cref="NetLatencyTests"/>): at a round trip of 50, 100 and 150 ms with 2 % loss and 20 ms of jitter, a few walk clicks
/// each, with prediction off (the way before it) and on: the first step on her machine after the click, and with it on how often and how far
/// the host's snapshots corrected her, and where she and the host's copy of her stand once both have stopped. A timing test (paced at
/// the wall clock's 30 frames a second, as a game is), left out of the plain run; the figures are the report's.
/// And (plain) the rules that need no clock: a walk predicted is answered and given back at its end; a refused one is given back
/// at once.
/// </summary>
[Collection(nameof(NetTimingCollection))]
public sealed partial class NetPredictionTests(ITestOutputHelper output)
{
    const float Step = 1f / 30;

    /// <summary>
    /// The round trips the gestures' starts are measured at: 50, 100 and 150 ms, and (his friends across
    /// the US and in Australia) 200 and 250 ms.
    /// </summary>
    internal static readonly int[] RoundTripsMeasured = [50, 100, 150, 200, 250];

    sealed record Walks(double FirstStepMs, int Clicks, OwnWalk.Counts Counts, float Apart);

    [Fact, Trait("Category", "Timing")]
    public void Her_first_step_and_the_corrections_measured_at_five_round_trips_with_loss_and_jitter()
    {
        using var files = Install.Open();
        var lines = new List<string>
        {
            "her own walk on her machine, 30 frames a second, the simulator on her machine alone (loss 2 %, jitter 20 ms):",
            "  round trip | first step off | first step on | compared | blended | snapped | refused/ended there | mean err | largest | apart at rest",
        };
        foreach (int rtt in RoundTripsMeasured)
        {
            var off = Walk(files, rtt, predict: false, clicks: 2);
            var on = Walk(files, rtt, predict: true, clicks: 6);
            var c = on.Counts;
            lines.Add($"  {rtt,4} ms    | {off.FirstStepMs,8:0} ms    | {on.FirstStepMs,7:0} ms     | {c.Compared,8} | {c.Blended,7} | {c.Snapped,7} | {c.Refused,3}/{c.EndedThere,-15} | {(c.Compared == 0 ? 0 : c.Total / c.Compared),5:0.0} u  | {c.Largest,5:0.0} u | {on.Apart,5:0.0} u");
            Assert.True(on.FirstStepMs < off.FirstStepMs, lines[^1]);
            Assert.True(on.FirstStepMs <= 80, $"her first step on her machine with prediction: {on.FirstStepMs:0} ms\n{lines[^1]}");
            Assert.True(on.Apart < 8f, $"she and the host's copy of her stand {on.Apart:0.0} units apart once both stopped\n{lines[^1]}");
        }
        // And among other characters (To Curse the Darkness's first place: four of its own, walking about on the host).
        var among = Walk(files, 100, predict: true, clicks: 6, chronicle: "MP_CurseDarkness");
        var a = among.Counts;
        lines.Add($"  among others, MP_CurseDarkness, 100 ms: first step {among.FirstStepMs:0} ms; compared {a.Compared}, blended {a.Blended}, snapped {a.Snapped}, " +
                  $"refused {a.Refused}, ended there {a.EndedThere}; mean {(a.Compared == 0 ? 0 : a.Total / a.Compared):0.0} u, largest {a.Largest:0.0} u; apart at rest {among.Apart:0.0} u");
        foreach (var l in lines) output.WriteLine(l);
        if (Environment.GetEnvironmentVariable("VITOMIR_PREDICT_REPORT") is { Length: > 0 } report) File.WriteAllLines(report, lines);
    }

    sealed record Swings(double FirstFrameMs, double MeanMs, int Clicks, int Swung, OwnStrike.Counts Counts);

    /// <summary>
    /// **The start of her own swing, measured** (the second part): as the walk's measure, a character the host puts beside her,
    /// standing still; at each round trip a few attack clicks with prediction off (the swing comes back with the host's strike) and
    /// on, the time from the click to the swing's first frame on her machine, and how often a guessed start was taken back. Between
    /// clicks both machines wait until she is free, the target still and a click past <see cref="OwnStrike.RepeatGapMs"/> (the host
    /// repeats nobody's attack by itself; the line that let a host's repeat go is kept, harmless).
    /// </summary>
    [Fact, Trait("Category", "Timing")]
    public void Her_first_swing_frame_measured_at_five_round_trips_with_loss_and_jitter()
    {
        using var files = Install.Open();
        var lines = new List<string>
        {
            "the start of her own swing on her machine, 30 frames a second, the simulator on her machine alone (loss 2 %, jitter 20 ms):",
            "  round trip | click to swing off (first, mean) | on (first, mean) | clicks | guessed | borne out | taken back (refused, approach, other, no answer)",
        };
        foreach (int rtt in RoundTripsMeasured)
        {
            var off = Swing(files, rtt, predict: false, clicks: 4);
            var on = Swing(files, rtt, predict: true, clicks: 8);
            var c = on.Counts;
            lines.Add($"  {rtt,4} ms    | {off.FirstFrameMs,6:0} ms, {off.MeanMs,6:0} ms             | {on.FirstFrameMs,4:0} ms, {on.MeanMs,4:0} ms     | {on.Clicks,6} | {c.Predicted,7} | {c.Confirmed,9} | {c.TakenBack} ({c.Refused}, {c.Approached}, {c.Differed}, {c.Unanswered})");
            Assert.True(on.Swung == on.Clicks && off.Swung == off.Clicks, lines[^1]);
            Assert.True(on.MeanMs < off.MeanMs, lines[^1]);
            Assert.True(on.MeanMs <= 80, $"her swing's first frame with prediction: {on.MeanMs:0} ms\n{lines[^1]}");
            Assert.True(c.Predicted == on.Clicks && c.TakenBack == 0, lines[^1]);
        }
        foreach (var l in lines) output.WriteLine(l);
        if (Environment.GetEnvironmentVariable("VITOMIR_STRIKE_REPORT") is { Length: > 0 } report) File.WriteAllLines(report, lines);
    }

    static Swings Swing(Vfs.GameFiles files, int rtt, bool predict, int clicks)
    {
        var settings = GameSettings.For(ChronicleSet.Load(files)["MP_DAInn"]!, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        place.Awaken(new Ai.AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        using var server = new GameServer(settings, new NetPeer(0) { Sim = null }) { Qol = new Qol(), Fixes = new Fixes() };
        server.Start();
        server.Enter(place, null);
        var sim = new Simulation(place);
        var peer = new NetPeer(0) { Sim = new NetSim(new NetSim.Spec(LatencyMs: rtt, JitterMs: 20, Loss: 2, Seed: 11 + rtt), 11 + rtt) };
        using var client = new GameClient(peer, new IPEndPoint(IPAddress.Loopback, server.Peer.Port), new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        client.Own.On = predict;
        Location? mine = null;
        Simulation? herSim = null;
        client.Started += () =>
        {
            var s = client.Settings!;
            mine = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: "MP_Serena", entrance: s.Entrance);
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
        Until(() => false, 1_000);
        var hers = server.Players.Single(p => p.Name == "Anna").Avatar!;
        var her = mine!.Player!.Thing;
        var weapon = her.Actor!.Weapon!;
        var dummy = place.Place("MP_Erik", hers.Position, hers.Orientation, hers.Sector)!;
        place.Move(dummy, hers.Position + new Vector3(hers.Radius + dummy.Radius + weapon.Current.Reach(attackerMoving: false) - 20f, 0, 0));
        dummy.Actor!.MaxHealth = dummy.Actor.Health = 1_000_000f;
        dummy.Actor.Flags |= Actor.Flag.AiPaused;
        WorldThing? here = null;
        Assert.True(Until(() => (here = client.Mirror?.Find(dummy.Guid)) is not null, 10_000), "her machine never had the target");
        double first = double.PositiveInfinity, total = 0;
        int swung = 0;
        for (int n = 0; n < clicks; n++)
        {
            // Both free and the target still on her screen.
            Until(() => her.Actor.Action == ActionMode.None && her.Animation?.Action is null && her.Actor.BusyUntil <= mine.Milliseconds
                        && hers.Actor!.Action == ActionMode.None && hers.Actor.BusyUntil <= place.Milliseconds && !client.Strikes.Pending
                        && client.Mirror!.Still(here!, OwnStrike.StillMs + 100), 10_000);
            Until(() => false, 300);
            var click = Stopwatch.StartNew();
            Actions.Attack(her, here!, weapon, 0, mine.Milliseconds, motions: mine.Motions);
            double local = her.Animation?.Action is not null ? 0 : -1;
            if (local < 0)
                Until(() =>
                {
                    if (local < 0 && her.Animation?.Action is not null) local = click.Elapsed.TotalMilliseconds;
                    return local >= 0;
                }, 5_000);
            // The click's own frame is the next one drawn: a swing started at the click shows then, a frame's 33 ms on.
            if (local == 0) local = Step * 1000;
            if (local > 0) { swung++; total += local; if (n == 0) first = local; }
            // The host's strike, and its swing played out.
            Until(() => hers.Actor!.Action == ActionMode.Swing && !client.Strikes.Pending, 5_000);
            hers.Actor!.Repeating = null;
            Until(() => false, weapon.Current.ReloadTime + OwnStrike.RepeatGapMs + 100);
        }
        return new Swings(first, swung == 0 ? double.PositiveInfinity : total / swung, clicks, swung, client.Strikes.Done);
    }

    static Walks Walk(Vfs.GameFiles files, int rtt, bool predict, int clicks, string chronicle = "MP_DAInn")
    {
        var settings = GameSettings.For(ChronicleSet.Load(files)[chronicle]!, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        place.Awaken(new Ai.AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        using var server = new GameServer(settings, new NetPeer(0) { Sim = null }) { Qol = new Qol(), Fixes = new Fixes() };
        server.Start();
        if (Environment.GetEnvironmentVariable("VITOMIR_PREDICT_LOG") is { Length: > 0 } log) server.Log += l => File.AppendAllText(log, $"{rtt} {predict}: {l}\n");
        server.Enter(place, null);
        var sim = new Simulation(place);
        var peer = new NetPeer(0) { Sim = new NetSim(new NetSim.Spec(LatencyMs: rtt, JitterMs: 20, Loss: 2, Seed: 7 + rtt), 7 + rtt) };
        using var client = new GameClient(peer, new IPEndPoint(IPAddress.Loopback, server.Peer.Port), new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        client.Own.On = predict;
        Location? mine = null;
        Simulation? herSim = null;
        client.Started += () =>
        {
            var s = client.Settings!;
            mine = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: "MP_Serena", entrance: s.Entrance);
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
            long due = (long)(frame * Step * 1000);
            long wait = due - wall.ElapsedMilliseconds;
            if (wait > 0) Thread.Sleep((int)wait);
        }
        bool Until(Func<bool> done, int ms)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < ms) { Turn(); if (done()) return true; }
            return done();
        }
        Assert.True(Until(() => client.Avatar is not null && server.Players.Any(p => p.Name == "Anna" && p.Avatar is not null), 30_000), "never played");
        Until(() => false, 1_000);
        var hers = server.Players.Single(p => p.Name == "Anna").Avatar!;
        var walker = mine!.Player!;
        double first = double.PositiveInfinity;
        var rng = new Random(rtt);
        for (int n = 0; n < clicks; n++)
        {
            var from = walker.Thing.Position;
            var to = Far(mine, from, rng);
            int node = mine.Walk!.Find(to, 256f);
            bool runs = n % 2 == 1;
            var click = Stopwatch.StartNew();
            Assert.True(walker.WalkTo(to, node, run: runs, speed: (runs ? walker.RunSpeed : walker.WalkSpeed) * (walker.Thing.Actor?.MoveSpeedScale ?? 1f)));
            if (walker.Thing.Actor is { } me) MoveOrder.Taken(walker.Thing);
            double local = -1;
            Until(() =>
            {
                if (local < 0 && Vector3.Distance(walker.Thing.Position, from) > 1f) local = click.Elapsed.TotalMilliseconds;
                return local >= 0;
            }, 6_000);
            if (n == 0) first = local < 0 ? double.PositiveInfinity : local;
            if (Environment.GetEnvironmentVariable("VITOMIR_PREDICT_LOG") is { Length: > 0 } why)
                File.AppendAllText(why, $"{rtt} click {n}: to {to} node {node} her {from} host {hers.Position} hostnode {place.Walk!.Find(to, 256f)} " +
                    $"head {(place.WalkerFor(hers) is { } hw ? MoveOrder.Head(hers, hw, to) : -1)} occupant {place.WalkerFor(hers)?.Occupant?.Template} ready {Actions.ReadyToMove(hers)} refused {client.Own.Done.Refused}\n");
            // Both walks to their end, and a second more for the last snapshot.
            Until(() => !walker.Walking && hers.Actor?.Action != ActionMode.Move && !client.Own.Active, 12_000);
            Until(() => false, 1_000);
        }
        float apart = Vector3.Distance(walker.Thing.Position, hers.Position);
        return new Walks(first, clicks, client.Own.Done, apart);
    }

    /// <summary>A host and her machine over loopback, turned frame by frame (no wall clock): the rules' rig.</summary>
    sealed class Pair : IDisposable
    {
        public required GameServer Server;
        public required GameClient Client;
        public required Simulation HostSim, HerSim;
        public required Location Mine;
        public required Location Host;
        /// <summary>The host's log, its time first (for a failure's message).</summary>
        public List<string> Said { get; } = [];
        int ms;

        /// <param name="scripts">The script layers of the host's place and of hers (<see cref="Scripted"/>), and her machine given the discipline table, for her casts.</param>
        public static Pair Open(Vfs.GameFiles files, bool predict, (IScriptObjects Host, IScriptObjects Hers)? scripts = null)
        {
            var settings = GameSettings.For(ChronicleSet.Load(files)["MP_DAInn"]!, "Anna's game", GameFlags.AutoStoryteller);
            var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
            place.Awaken(new Ai.AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
            if (scripts is { } layers) place.ScriptObjects = layers.Host;
            var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
            server.Start();
            server.Enter(place, null);
            var client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, server.Peer.Port), new NetCharacter { Name = "Anna", Template = "MP_Serena" });
            client.Own.On = predict;
            if (scripts is not null) client.Powers = new DisciplineDefinitions(files);
            Location? mine = null;
            client.Started += () =>
            {
                var st = client.Settings!;
                mine = Location.Load(files, st.Level, scene: st.Scene.Length > 0 ? st.Scene : null, player: "MP_Serena", entrance: st.Entrance);
                if (scripts is { } layers) mine.ScriptObjects = layers.Hers;
                client.Enter(mine, mine.Player?.Thing);
            };
            var pair = new Pair { Server = server, Client = client, HostSim = new Simulation(place), HerSim = null!, Mine = null!, Host = place };
            server.Log += l => pair.Said.Add($"{place.Milliseconds} {l}");
            for (int i = 0; i < 2000 && !(client.Avatar is not null && server.Players.Any(p => p.Name == "Anna" && p.Avatar is not null)); i++)
            {
                pair.Turn(herSim: null);
                Thread.Sleep(2);
            }
            Assert.NotNull(mine);
            Assert.NotNull(client.Avatar);
            pair.Mine = mine!;
            pair.HerSim = new Simulation(mine!);
            pair.Turns(30);
            return pair;
        }

        void Turn(Simulation? herSim)
        {
            HostSim.Step(Step);
            ms += 33;
            Server.Poll(ms);
            Client.Poll(Step);
            if (Keys && herSim is not null) Client.Drive(Push, Run, Mine.Player, Step);   // her frame's keys, where the game's frame reads them
            herSim?.Step(Step);
            Paced();
        }

        /// <summary>Her keys read each frame, as <c>ClassicGame.Frame</c> does once the game camera has the screen (off: nothing sent).</summary>
        public bool Keys;
        /// <summary>The keys' push in world x and y, and whether it runs.</summary>
        public Vector2 Push;
        public bool Run = true;

        readonly Stopwatch wall = Stopwatch.StartNew();
        long turned;
        Stopwatch? keyed;
        long keyedTurns;

        /// <summary>
        /// **Under the simulator, a game's pace** (as <c>NetTests</c>' keyboard drive): the simulator's time is the turns
        /// (<c>frame=33</c>), but the transport sends a lost reliable message again by the wall clock (two round trips, at least
        /// 100 ms), so turned as fast as they go (a few milliseconds a turn) one loss outlasted a test's wait of 120 turns — the
        /// strike or the answer never came (an earlier build's runs under <c>VITOMIR_NET_SIM</c>; which tests met a loss changed with the
        /// tests' order, each transport's seed being the next). The plain wire's runs are not paced.
        /// </summary>
        void Paced()
        {
            turned++;
            // Her keys too, on any wire, from the first turn they are read: her machine sends a held push again by the wall
            // clock (GameClient.DriveResendMs) and the host lets one go stale by its own turns (GameServer.DriveStaleMs), as NetTests'
            // keyboard drive is paced.
            if (NetSim.Default is not { FrameMs: > 0 } sim)
            {
                if (!Keys) return;
                keyed ??= Stopwatch.StartNew();
                long at = ++keyedTurns * 33L;
                while (keyed.ElapsedMilliseconds < at) Thread.Sleep((int)Math.Clamp(at - keyed.ElapsedMilliseconds, 1, 33));
                return;
            }
            long due = turned * sim.FrameMs;
            while (wall.ElapsedMilliseconds < due) Thread.Sleep((int)Math.Clamp(due - wall.ElapsedMilliseconds, 1, 33));
        }

        public void Turns(int n, Func<bool>? until = null)
        {
            for (int i = 0; i < n; i++)
            {
                Turn(HerSim);
                if (until?.Invoke() == true) return;
                Thread.Sleep(1);
            }
        }

        public WorldThing Hers => Server.Players.Single(p => p.Name == "Anna").Avatar!;

        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
        }
    }

    static (Vector3 To, int Node) Click(Pair pair, bool runs = false)
    {
        var walker = pair.Mine.Player!;
        var to = Far(pair.Mine, walker.Thing.Position, new Random(3));
        int node = pair.Mine.Walk!.Find(to, 256f);
        Assert.True(walker.WalkTo(to, node, run: runs, speed: (runs ? walker.RunSpeed : walker.WalkSpeed) * (walker.Thing.Actor?.MoveSpeedScale ?? 1f)));
        MoveOrder.Taken(walker.Thing);
        return (to, node);
    }

    /// <summary>
    /// A walk click on her machine moves her at its next frame, before the host has heard of it; the host answers the numbered order
    /// (<see cref="Msg.Taken"/>), walks her the same way, and once both walks are over she is the snapshots' again, where the host has her.
    /// </summary>
    [Fact]
    public void A_walk_click_moves_her_at_once_the_host_answers_and_at_the_end_she_stands_where_the_host_has_her()
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: true);
        var walker = pair.Mine.Player!;
        var from = walker.Thing.Position;
        var hostFrom = pair.Hers.Position;
        Click(pair);
        pair.HerSim.Step(Step);                                           // her own frame, nothing from the host yet
        Assert.True(Vector3.Distance(walker.Thing.Position, from) > 1f, "she did not move at once");
        Assert.True(pair.Client.Own.Active);
        Assert.Equal(hostFrom, pair.Hers.Position);
        pair.Turns(600, () => !walker.Walking && !pair.Client.Own.Active && pair.Hers.Actor?.Action != ActionMode.Move);
        pair.Turns(30);
        Assert.False(pair.Client.Own.Active);
        Assert.True(Vector3.Distance(pair.Hers.Position, hostFrom) > 100f, "the host did not walk her");
        Assert.True(Vector3.Distance(walker.Thing.Position, pair.Hers.Position) < 2f,
                    $"she stands {Vector3.Distance(walker.Thing.Position, pair.Hers.Position):0.0} units from where the host has her");
        Assert.Equal(1, pair.Client.Own.Done.Predicted);
        Assert.True(pair.Client.Own.Done.Compared > 0);
        Assert.Equal(0, pair.Client.Own.Done.Refused);
    }

    /// <summary>
    /// The host refuses her order (its game paused): her machine walked her at once, the answer says refused, and her walk here stops
    /// and she is given back to the snapshots, which have her where she stood.
    /// </summary>
    [Fact]
    public void A_walk_the_host_refuses_stops_here_and_she_goes_back_to_where_the_host_has_her()
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: true);
        var walker = pair.Mine.Player!;
        var hostFrom = pair.Hers.Position;
        pair.Server.SetPaused(true);
        pair.Turns(10);
        Click(pair);
        pair.HerSim.Step(Step);
        Assert.True(pair.Client.Own.Active);
        pair.Turns(300, () => pair.Client.Own.Done.Refused > 0);
        Assert.Equal(1, pair.Client.Own.Done.Refused);
        Assert.False(pair.Client.Own.Active);
        Assert.False(walker.Walking);
        pair.Turns(90);
        Assert.Equal(hostFrom, pair.Hers.Position);
        Assert.True(Vector3.Distance(walker.Thing.Position, hostFrom) < 2f,
                    $"she stands {Vector3.Distance(walker.Thing.Position, hostFrom):0.0} units from where the host has her");
    }

    /// <summary>
    /// The host's copy of her put elsewhere in mid-walk (a shove her machine knows nothing of): a small difference is blended in over a
    /// few frames, a large one snaps her there and she walks on from there; either way she ends where the host has her.
    /// </summary>
    [Theory]
    [InlineData(30f, false)]
    [InlineData(150f, true)]
    public void A_difference_the_host_makes_is_blended_when_small_and_snapped_when_large(float shove, bool snaps)
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: true);
        var walker = pair.Mine.Player!;
        Click(pair);
        pair.Turns(300, () => pair.Client.Own.Done.Compared >= 3);
        Assert.True(pair.Client.Own.Active, "the walk ended before the shove");
        var hers = pair.Hers;
        var aside = Vector3.Normalize(Vector3.Cross(hers.Locomotion is { } w ? new Vector3(w.Destination.X - hers.Position.X, w.Destination.Y - hers.Position.Y, 0) + new Vector3(1e-3f, 0, 0) : Vector3.UnitX, Vector3.UnitZ));
        hers.Place!.Move(hers, hers.Position + aside * shove);
        pair.Turns(600, () => !walker.Walking && !pair.Client.Own.Active && hers.Actor?.Action != ActionMode.Move);
        pair.Turns(60);
        var done = pair.Client.Own.Done;
        if (snaps) Assert.True(done.Snapped >= 1, $"no snap: blended {done.Blended}, largest {done.Largest:0.0}");
        else
        {
            Assert.True(done.Blended >= 1, $"no blend: snapped {done.Snapped}, largest {done.Largest:0.0}");
            Assert.Equal(0, done.Snapped);
        }
        Assert.True(Vector3.Distance(walker.Thing.Position, hers.Position) < 2f,
                    $"she stands {Vector3.Distance(walker.Thing.Position, hers.Position):0.0} units from where the host has her");
    }

    /// <summary>With prediction off (the developer's <c>--no-predict</c>) her machine waits for the host, as before: nothing numbered, nothing answered.</summary>
    [Fact]
    public void With_prediction_off_her_machine_waits_for_the_host_as_before()
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: false);
        var walker = pair.Mine.Player!;
        var from = walker.Thing.Position;
        Click(pair);
        pair.HerSim.Step(Step);
        Assert.Equal(from, walker.Thing.Position);
        Assert.False(pair.Client.Own.Active);
        pair.Turns(300, () => Vector3.Distance(walker.Thing.Position, from) > 50f);
        Assert.True(Vector3.Distance(walker.Thing.Position, from) > 50f, "the host's walk never reached her machine");
        Assert.Equal(0, pair.Client.Own.Done.Predicted);
    }

    // ---- the start of her own swing (the second part, OwnStrike) ----------------------------------------------------------

    /// <summary>
    /// A character the host puts beside her, in her reach with <paramref name="spare"/> units of it to spare, standing still and
    /// never dying, and her machine's copy of it once its snapshots have it still there.
    /// </summary>
    static (WorldThing There, WorldThing Here) Beside(Pair pair, float spare = 20f)
    {
        var hers = pair.Hers;
        var weapon = pair.Mine.Player!.Thing.Actor!.Weapon;
        Assert.NotNull(weapon);
        var dummy = pair.Host.Place("MP_Erik", hers.Position, hers.Orientation, hers.Sector)!;
        Assert.NotNull(dummy);
        float apart = hers.Radius + dummy.Radius + weapon!.Current.Reach(attackerMoving: false) - spare;
        pair.Host.Move(dummy, hers.Position + new Vector3(apart, 0, 0));
        dummy.Actor!.MaxHealth = dummy.Actor.Health = 100_000f;
        dummy.Actor.Flags |= Actor.Flag.AiPaused;
        WorldThing? here = null;
        pair.Turns(300, () => (here = pair.Client.Mirror?.Find(dummy.Guid)) is not null && pair.Client.Mirror!.Still(here, OwnStrike.StillMs + 100));
        Assert.NotNull(here);
        pair.Turns(15);
        return (dummy, here!);
    }

    /// <summary>Her attack click on her machine: the order as the game's click gives it.</summary>
    static OrderResult Attack(Pair pair, WorldThing at)
    {
        var her = pair.Mine.Player!.Thing;
        return Actions.Attack(her, at, her.Actor!.Weapon, 0, pair.Mine.Milliseconds, motions: pair.Mine.Motions);
    }

    /// <summary>
    /// A click to attack a character standing in her reach swings at once on her machine, turned to it, before the host has heard of
    /// it; the host's strike bears it out and does not start the swing again (the same animation plays on), and nothing is taken back.
    /// </summary>
    [Fact]
    public void A_click_to_attack_in_reach_swings_at_once_and_the_hosts_strike_does_not_start_it_again()
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: true);
        var (there, here) = Beside(pair);
        var her = pair.Mine.Player!.Thing;
        var strikes = pair.Client.Strikes;
        Assert.True(strikes.Sure(her, here, her.Actor!.Weapon, 0), "her machine did not find the swing certain");
        Assert.Equal(OrderResult.Started, Attack(pair, here));
        var swing = her.Animation!.Action;
        Assert.NotNull(swing);
        Assert.Equal(ActionMode.Swing, her.Actor.Action);
        Assert.True(strikes.Pending);
        Assert.NotEqual(ActionMode.Swing, pair.Hers.Actor!.Action);            // the host has not heard of it yet
        pair.Turns(120, () => strikes.Done.Confirmed > 0);
        pair.Turns(3);
        Assert.Same(swing, her.Animation.Action);                              // the host's strike and its snapshot did not start it again
        pair.Turns(120, () => !strikes.Pending);
        Assert.False(strikes.Pending);
        Assert.Equal(1, strikes.Done.Predicted);
        Assert.Equal(1, strikes.Done.Confirmed);
        Assert.Equal(0, strikes.Done.TakenBack);
        Assert.True(pair.Hers.Actor.BusyUntil > 0, "the host never struck");
        Assert.Same(there, pair.Hers.Actor.Target);
    }

    /// <summary>
    /// The host does not strike what her machine guessed: the target gone from the host's world before her order came, or her
    /// character busy there with something her machine has not heard of. Her swing is taken back at the host's answer: the swing
    /// stopped, her action none, her busy time as before, counted refused; nothing of the host's strike follows.
    /// </summary>
    [Theory]
    [InlineData("gone")]
    [InlineData("busy")]
    public void A_swing_the_host_refuses_is_taken_back_cleanly(string why)
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: true);
        var (there, here) = Beside(pair);
        var her = pair.Mine.Player!.Thing;
        var strikes = pair.Client.Strikes;
        int busyBefore = her.Actor!.BusyUntil;
        if (why == "gone") pair.Host.Remove(there, drop: false);
        else pair.Hers.Actor!.BusyUntil = pair.Host.Milliseconds + 60_000;
        Assert.Equal(OrderResult.Started, Attack(pair, here));
        Assert.True(strikes.Pending);
        Assert.NotNull(her.Animation!.Action);
        pair.Turns(120, () => !strikes.Pending);
        Assert.False(strikes.Pending);
        Assert.Equal(1, strikes.Done.Refused);
        Assert.Equal(0, strikes.Done.Confirmed);
        Assert.Null(her.Animation.Action);
        Assert.Equal(ActionMode.None, her.Actor.Action);
        Assert.Equal(busyBefore, her.Actor.BusyUntil);
        pair.Turns(30);
        Assert.Null(her.Animation.Action);                                     // no stuck pose, and no strike of the host's after it
        Assert.Equal(0, strikes.Done.Predicted - strikes.Done.Confirmed - strikes.Done.TakenBack);
    }

    /// <summary>
    /// The outcome differs: the host has the target further off than her machine shows it (a move her machine has not been told of),
    /// so the host closes in instead of striking. Her swing is taken back at the host's answer (counted as an approach), the host
    /// walks her there and strikes, and that strike plays on her machine as any of the host's does.
    /// </summary>
    [Fact]
    public void A_swing_the_host_turns_into_an_approach_is_taken_back_and_the_hosts_own_strike_plays_later()
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: true);
        var (there, here) = Beside(pair);
        var her = pair.Mine.Player!.Thing;
        var strikes = pair.Client.Strikes;
        pair.Host.Move(there, there.Position + new Vector3(40, 0, 0));
        Assert.Equal(OrderResult.Started, Attack(pair, here));
        Assert.True(strikes.Pending);
        pair.Turns(120, () => !strikes.Pending);
        Assert.True(strikes.Done.Approached == 1, $"approached {strikes.Done.Approached}, refused {strikes.Done.Refused}, differed {strikes.Done.Differed}, host's action {pair.Hers.Actor!.Action}, mind {pair.Hers.Mind is not null}");
        Assert.Equal(0, strikes.Done.Confirmed);
        pair.Turns(300, () => her.Animation?.Action is not null);
        Assert.NotNull(her.Animation!.Action);                                 // the host's own strike, replayed here
        Assert.Equal(1, strikes.Done.Predicted);
    }

    /// <summary>With prediction off (the developer's <c>--no-predict</c>) her swing waits for the host's strike, as before: nothing guessed.</summary>
    [Fact]
    public void With_prediction_off_her_swing_waits_for_the_hosts_strike()
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: false);
        var (_, here) = Beside(pair);
        var her = pair.Mine.Player!.Thing;
        Assert.Equal(OrderResult.Started, Attack(pair, here));
        Assert.Null(her.Animation!.Action);
        Assert.False(pair.Client.Strikes.Pending);
        pair.Turns(120, () => her.Animation.Action is not null);
        Assert.True(her.Animation.Action is not null, $"the host's action {pair.Hers.Actor!.Action}\n{string.Join("\n", pair.Said.TakeLast(20))}");
        Assert.Equal(0, pair.Client.Strikes.Done.Predicted);
    }

    /// <summary>A target walking on her screen, or one out of her reach, is not guessed: the host decides the approach.</summary>
    [Fact]
    public void A_target_out_of_reach_is_not_guessed()
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: true);
        var (_, here) = Beside(pair, spare: -40f);
        var her = pair.Mine.Player!.Thing;
        Assert.False(pair.Client.Strikes.Sure(her, here, her.Actor!.Weapon, 0));
        Assert.Equal(OrderResult.Started, Attack(pair, here));
        Assert.False(pair.Client.Strikes.Pending);
        Assert.Equal(0, pair.Client.Strikes.Done.Predicted);
    }

    // ---- her held attack: the host repeats it only on her machine's word --------------------------------

    sealed record Held(int HostSwings, int Orders, int Stops, int SwingsAfterRelease, OwnStrike.Counts Strikes, List<string> Said);

    /// <summary>
    /// Her click to attack the character beside her, then her input as her machine's frame reads it for <paramref name="heldTurns"/>
    /// frames (<see cref="SwingRepeat.Ask"/> with her <paramref name="option"/>, the button held, the target under her pointer, as
    /// <c>ClassicGame.Frame</c> asks it), then let go; <paramref name="turns"/> frames in all. Counts the host's swings (each a new
    /// attack animation on the host's copy of her), the attack and stop orders the host had from her, and the host's swings begun
    /// more than a swing and two seconds after she let go.
    /// </summary>
    static Held Hold(bool option, int heldTurns, int turns = 300)
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: true);
        int orders = 0, stops = 0;
        var said = new List<string>();
        pair.Server.Log += l =>
        {
            if (l.StartsWith("net: Anna", StringComparison.Ordinal)) said.Add($"{pair.Host.Milliseconds} {l}");
            if (l.StartsWith("net: Anna Attack", StringComparison.Ordinal)) orders++;
            if (l.StartsWith("net: Anna Stop", StringComparison.Ordinal)) stops++;
        };
        var (_, here) = Beside(pair);
        var her = pair.Mine.Player!.Thing;
        Assert.Equal(OrderResult.Started, Attack(pair, here));
        int swings = 0, after = 0, letGo = -1;
        object? last = null;
        int reload = her.Actor!.Weapon!.Current.ReloadTime;
        for (int i = 0; i < turns; i++)
        {
            bool held = i < heldTurns;
            her.Actor.Repeating = SwingRepeat.Ask(option, answering: true, here, leftHeld: held, middleHeld: false, InputModifier.None);
            if (!held && letGo < 0) letGo = pair.Host.Milliseconds;
            pair.Turns(1);
            var swing = pair.Hers.Animation?.Action;
            if (swing is not null && !ReferenceEquals(swing, last))
            {
                swings++;
                // An order she sent just before letting go may still come (a loss and its resend under the simulator): a swing
                // begun two seconds after it is one nobody asked for.
                if (letGo >= 0 && pair.Host.Milliseconds - letGo > reload + 2000) after++;
            }
            last = swing;
        }
        return new Held(swings, orders, stops, after, pair.Client.Strikes.Done, said);
    }

    /// <summary>One click, one swing: the host swings once and not again, though she stands beside the target (earlier, it went on until she walked).</summary>
    [Fact]
    public void A_click_to_attack_from_her_machine_is_one_swing_on_the_host()
    {
        var held = Hold(option: true, heldTurns: 1);
        Assert.True(held.HostSwings == 1 && held.Orders == 1, $"host swings {held.HostSwings}, orders {held.Orders}");
        Assert.Equal(0, held.Strikes.TakenBack);
    }

    /// <summary>
    /// Held with her *auto repeat attack* on, the swing repeats: at each swing's end her machine asks (her button, her
    /// option) and sends the next attack order, once, after a stop (the attack order's cancel); the host swings at each. Let go, the next
    /// swing's end asks nothing and the host swings no more. The repeats are not guessed on her machine (her first click is), and
    /// the release is not a take-back.
    /// </summary>
    [Fact]
    public void Held_with_auto_repeat_on_her_attack_repeats_and_let_go_it_stops()
    {
        // Six seconds held: a swing and its reload (767 ms with her weapon) and a round trip each — seven on the plain wire, five or
        // six under the simulator's 80 ms.
        var held = Hold(option: true, heldTurns: 180);
        Assert.True(held.HostSwings >= 4, $"host swings {held.HostSwings}, orders {held.Orders}\n{string.Join("\n", held.Said)}");
        Assert.Equal(held.HostSwings, held.Orders);                             // one order a swing, none sent each frame
        Assert.Equal(held.Orders - 1, held.Stops);                              // each repeat's cancel told the host
        Assert.Equal(0, held.SwingsAfterRelease);
        Assert.Equal(1, held.Strikes.Predicted);
        Assert.Equal(0, held.Strikes.TakenBack);
    }

    /// <summary>Held with her *auto repeat attack* off (the option is her machine's, 1.1's options word on her own Controls screen): one swing.</summary>
    [Fact]
    public void Held_with_auto_repeat_off_her_attack_is_one_swing()
    {
        var held = Hold(option: false, heldTurns: 180);
        Assert.True(held.HostSwings == 1 && held.Orders == 1, $"host swings {held.HostSwings}, orders {held.Orders}");
        Assert.Equal(0, held.Strikes.TakenBack);
    }

    /// <summary>A walkable point 200 to 400 units away in a seeded direction.</summary>
    static Vector3 Far(Location place, Vector3 from, Random rng)
    {
        var map = place.Walk!;
        for (int a = 0; a < 64; a++)
        {
            float r = (float)(rng.NextDouble() * MathF.PI * 2);
            float d = 200f + (float)rng.NextDouble() * 200f;
            var p = from + new Vector3(MathF.Cos(r), MathF.Sin(r), 0) * d;
            if (map.Find(p, 256f) >= 0) return p;
        }
        return from + new Vector3(100, 0, 0);
    }
}
