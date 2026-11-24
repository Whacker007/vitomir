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
/// **Ten or twenty snapshots a second, measured**. A host and four players in one process, each player's machine under the
/// simulator (a round trip of 100 ms, jitter 20, loss 2 %), paced at the wall clock's 30 frames a second, every player clicking a walk
/// every few seconds: the player's view of another (how far behind the host's copy her machine shows him, and how far off), the bytes a
/// second the host sends each player, and the host's own network work a frame (its <c>Poll</c>: the wire read, the orders, the
/// snapshots made and sealed). At 100 ms (the earlier schedule, which counts each interval from the frame that sent the last: 7.6 a second
/// at 33 ms frames) and steady at 100 and 50 ms; steady at 100 is a host's default. A
/// timing test, left out of the plain run; the figures are the report's.
/// </summary>
[Collection(nameof(NetTimingCollection))]
public sealed class NetRateTests(ITestOutputHelper output)
{
    const float Step = 1f / 30;

    sealed record Measured(string Name, int Characters, double Rate, double BytesPerPlayer, double PollMeanMs, double PollP99Ms, double DelayMs, double ErrMean, double ErrP95);

    [Fact, Trait("Category", "Timing")]
    public void Snapshots_ten_and_twenty_a_second_measured_with_four_players()
    {
        using var files = Install.Open();
        var runs = new[]
        {
            Run(files, "MP_DAInn", "100 ms, the base schedule", 100, steady: false),
            Run(files, "MP_DAInn", "100 ms, steady", 100, steady: true),
            Run(files, "MP_DAInn", "50 ms, steady", 50, steady: true),
            Run(files, "MP_CurseDarkness", "100 ms, the base schedule", 100, steady: false),
            Run(files, "MP_CurseDarkness", "100 ms, steady", 100, steady: true),
            Run(files, "MP_CurseDarkness", "50 ms, steady", 50, steady: true),
        };
        var lines = new List<string>
        {
            "four players, each under the simulator (round trip 100 ms, jitter 20, loss 2 %), 30 frames a second, walks clicked every few seconds:",
            "  place, characters, snapshots           | sent a second | down a player          | host Poll a frame (mean, p99) | another player shown: behind by, off by (mean, p95)",
        };
        foreach (var m in runs)
            lines.Add($"  {m.Name,-38} | {m.Rate,6:0.0}        | {m.BytesPerPlayer,7:0} B/s = {m.BytesPerPlayer * 8 / 1000,5:0.0} kbit/s | {m.PollMeanMs,6:0.000} ms, {m.PollP99Ms,6:0.000} ms      | {m.DelayMs,4:0} ms, {m.ErrMean,5:0.0} u, {m.ErrP95,5:0.0} u");
        foreach (var l in lines) output.WriteLine(l);
        if (Environment.GetEnvironmentVariable("VITOMIR_RATE_REPORT") is { Length: > 0 } report) File.WriteAllLines(report, lines);
        Assert.All(runs, m => Assert.True(m.Rate > 5, m.Name));
        // The steady schedule is a host's own, and ten a second is ten, inside the line with eight characters.
        using (var probe = new GameServer(GameSettings.For(ChronicleSet.Load(files)["MP_DAInn"]!, "x", GameFlags.AutoStoryteller), new NetPeer(0) { Sim = null }))
            Assert.True(probe.Steady);
        var ten = runs[4];
        Assert.True(ten.Rate >= 9.5, $"{ten.Name}: {ten.Rate:0.0} a second");
        Assert.True(ten.BytesPerPlayer * 8 / 1000 < 64, $"{ten.Name}: {ten.BytesPerPlayer * 8 / 1000:0.0} kbit/s a player");
        Assert.True(ten.PollMeanMs < 1, $"{ten.Name}: the host's Poll {ten.PollMeanMs:0.000} ms a frame");
    }

    sealed class Machine
    {
        public required GameClient Client;
        public Location? Place;
        public Simulation? Sim;
    }

    static Measured Run(Vfs.GameFiles files, string chronicle, string schedule, int every, bool steady)
    {
        var settings = GameSettings.For(ChronicleSet.Load(files)[chronicle]!, "Anna's game", GameFlags.AutoStoryteller, maxPlayers: 5);   // four players and the host's slot
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        place.Awaken(new Ai.AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        using var server = new GameServer(settings, new NetPeer(0) { Sim = null }) { Qol = new Qol(), Fixes = new Fixes(), SnapshotEveryMs = every, Steady = steady };
        server.Start();
        server.Enter(place, null);
        var sim = new Simulation(place);
        int characters = place.Things.Count(t => t.Actor is not null && !NetSynch.Kept(t)) + 4;
        string name = $"{chronicle}, {characters}, {schedule}";
        string[] names = ["Anna", "Bela", "Cyril", "Dora"];
        string[] models = ["MP_Serena", "MP_Erik", "MP_Christof_DA", "MP_Serena"];
        var machines = new List<Machine>();
        for (int i = 0; i < names.Length; i++)
        {
            var peer = new NetPeer(0) { Sim = new NetSim(new NetSim.Spec(LatencyMs: 100, JitterMs: 20, Loss: 2, Seed: 31 + i), 31 + i) };
            var m = new Machine { Client = new GameClient(peer, new IPEndPoint(IPAddress.Loopback, server.Peer.Port), new NetCharacter { Name = names[i], Template = models[i] }) };
            string model = models[i];
            m.Client.Started += () =>
            {
                var s = m.Client.Settings!;
                m.Place = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: model, entrance: s.Entrance);
                m.Sim = new Simulation(m.Place);
                m.Client.Enter(m.Place, m.Place.Player?.Thing);
            };
            machines.Add(m);
        }
        try
        {
            var wall = Stopwatch.StartNew();
            long frame = 0;
            var polls = new List<double>();
            bool measuring = false;
            void Turn()
            {
                sim.Step(Step);
                long t0 = Stopwatch.GetTimestamp();
                server.Poll((int)wall.ElapsedMilliseconds);
                if (measuring) polls.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                foreach (var m in machines) { m.Client.Poll(Step); m.Sim?.Step(Step); }
                frame++;
                long wait = (long)(frame * Step * 1000) - wall.ElapsedMilliseconds;
                if (wait > 0) Thread.Sleep((int)wait);
            }
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 60_000
                   && !(machines.All(m => m.Client.Avatar is not null) && server.Players.Count(p => p.Avatar is not null) >= names.Length)) Turn();
            Assert.True(machines.All(m => m.Client.Avatar is not null), $"{name}: not every player played");
            for (int i = 0; i < 30; i++) Turn();

            // Measured: twenty seconds, each player clicking a walk every 2.5 s (staggered), Bela watched on Anna's machine.
            var bela = server.Players.Single(p => p.Name == "Bela").Avatar!;
            var onlooker = machines[0];
            var belaThere = onlooker.Client.Mirror!.Find(bela.Guid)!;
            var truth = new List<(double Ms, Vector3 At)>();
            var shown = new List<(double Ms, Vector3 At, bool Moving)>();
            var rng = new Random(every + (steady ? 1 : 0));
            long bytes0 = server.Peer.Stats.BytesOut;
            int snaps0 = server.SnapshotsSent;
            var span = Stopwatch.StartNew();
            measuring = true;
            int frames = 0;
            while (span.ElapsedMilliseconds < 20_000)
            {
                if (frames % 75 == 0)
                    for (int i = 0; i < machines.Count; i++)
                    {
                        if ((frames / 75) % machines.Count != i % machines.Count && i != 1) continue;
                        var w = machines[i].Place!.Player!;
                        var to = Far(machines[i].Place!, w.Thing.Position, rng);
                        int node = machines[i].Place!.Walk!.Find(to, 256f);
                        if (node >= 0 && w.WalkTo(to, node, run: true, speed: w.RunSpeed * (w.Thing.Actor?.MoveSpeedScale ?? 1f))) MoveOrder.Taken(w.Thing);
                    }
                Turn();
                frames++;
                double now = span.Elapsed.TotalMilliseconds;
                truth.Add((now, bela.Position));
                shown.Add((now, belaThere.Position, bela.Locomotion?.Walking == true || bela.Actor?.Action == ActionMode.Move));
            }
            measuring = false;
            double seconds = span.Elapsed.TotalSeconds;
            double perPlayer = (server.Peer.Stats.BytesOut - bytes0) / (double)names.Length / seconds;
            double rate = (server.SnapshotsSent - snaps0) / seconds;
            polls.Sort();
            double pollMean = polls.Average(), pollP99 = polls[(int)(polls.Count * 0.99)];

            // How far behind: the delay that best fits what her machine shows of him to the host's track, over the frames he moved.
            Vector3 TruthAt(double ms)
            {
                if (ms <= truth[0].Ms) return truth[0].At;
                for (int i = 1; i < truth.Count; i++)
                    if (truth[i].Ms >= ms)
                    {
                        float f = (float)((ms - truth[i - 1].Ms) / Math.Max(1e-6, truth[i].Ms - truth[i - 1].Ms));
                        return Vector3.Lerp(truth[i - 1].At, truth[i].At, f);
                    }
                return truth[^1].At;
            }
            var moving = shown.Where(s => s.Moving && s.Ms > 1000).ToList();
            double best = 0, bestErr = double.MaxValue;
            for (int d = 0; d <= 600; d += 5)
            {
                double e = moving.Average(s => Vector3.Distance(s.At, TruthAt(s.Ms - d)));
                if (e < bestErr) { bestErr = e; best = d; }
            }
            var errs = moving.Select(s => (double)Vector3.Distance(s.At, TruthAt(s.Ms))).OrderBy(e => e).ToList();
            return new Measured(name, characters, rate, perPlayer, pollMean, pollP99, best, errs.Average(), errs[(int)(errs.Count * 0.95)]);
        }
        finally
        {
            foreach (var m in machines) m.Client.Dispose();
        }
    }

    static Vector3 Far(Location place, Vector3 from, Random rng)
    {
        var map = place.Walk!;
        for (int a = 0; a < 64; a++)
        {
            float r = (float)(rng.NextDouble() * MathF.PI * 2);
            float d = 200f + (float)rng.NextDouble() * 250f;
            var p = from + new Vector3(MathF.Cos(r), MathF.Sin(r), 0) * d;
            if (map.Find(p, 256f) >= 0) return p;
        }
        return from;
    }
}

/// <summary>The network's timing measures, alone: two cores shared with another test would be measured with them.</summary>
[CollectionDefinition(nameof(NetTimingCollection), DisableParallelization = true)]
public sealed class NetTimingCollection;
