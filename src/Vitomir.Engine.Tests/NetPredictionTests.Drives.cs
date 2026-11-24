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
/// **Her own keyboard walk predicted** (<see cref="OwnWalk.Drive"/>): the push (<c>[qol] driving</c>) moves her on
/// her machine at once, the host's snapshots correct her, letting go stands her at once, and every refusal of the host's is either
/// seen on her machine first or given back to the snapshots. The rules on the plain wire, turned frame by frame; the measure at five
/// round trips, a timing test.
/// </summary>
public sealed partial class NetPredictionTests
{
    /// <summary>A push toward a walkable point 200 to 400 units off (seeded), flat and of length one.</summary>
    static Vector2 Toward(Location place, Vector3 from, int seed)
    {
        var to = Far(place, from, new Random(seed));
        var d = new Vector2(to.X - from.X, to.Y - from.Y);
        return d.LengthSquared() > 1e-6f ? Vector2.Normalize(d) : Vector2.UnitX;
    }

    static float Apart(Pair pair) => Vector3.Distance(pair.Mine.Player!.Thing.Position, pair.Hers.Position);

    /// <summary>
    /// The push moves her at her next frame, before the host has heard of it; the host walks her the same way; letting go stands her
    /// at once, and once the host has her standing too she is the snapshots' again, where the host has her.
    /// </summary>
    [Fact]
    public void A_push_moves_her_at_once_letting_go_stands_her_and_she_ends_where_the_host_has_her()
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: true);
        var her = pair.Mine.Player!.Thing;
        var from = her.Position;
        var hostFrom = pair.Hers.Position;
        pair.Keys = true;
        pair.Push = Toward(pair.Mine, from, 5);
        pair.Turns(1);
        Assert.True(Vector3.Distance(her.Position, from) > 1f, "she did not move at once");
        Assert.True(pair.Client.Own.Driving);
        Assert.Equal(hostFrom, pair.Hers.Position);                            // the host has not heard yet
        var trace = new List<string>();
        for (int i = 0; i < 45; i++)
        {
            pair.Turns(1);
            var hw = pair.Host.WalkerFor(pair.Hers);
            trace.Add($"{i}: her {her.Position.X:0.0},{her.Position.Y:0.0} host {pair.Hers.Position.X:0.0},{pair.Hers.Position.Y:0.0} blocked {pair.Mine.Player!.Blocked}/{hw?.Blocked} by {pair.Mine.Player!.BlockedBy?.Template}/{hw?.BlockedBy?.Template} node {pair.Mine.Player!.Node}/{hw?.Node}");
        }
        Assert.True(pair.Client.Own.Driving, $"her walk here was given back: {Said(pair)}\n{string.Join("\n", trace)}");
        pair.Push = Vector2.Zero;
        pair.Turns(1);
        var stood = her.Position;
        pair.Turns(1);
        Assert.True(Vector3.Distance(her.Position, stood) < OwnWalk.Noise, "she walked on after the keys were let go");
        pair.Turns(120, () => !pair.Client.Own.Active);
        pair.Turns(30);
        var done = pair.Client.Own.Done;
        Assert.False(pair.Client.Own.Active);
        Assert.True(Vector3.Distance(pair.Hers.Position, hostFrom) > 100f, "the host did not walk her");
        Assert.True(Apart(pair) < 2f, $"she stands {Apart(pair):0.0} units from where the host has her; {Said(pair)}");
        output.WriteLine(Said(pair));
        Assert.Equal(1, done.Drives);
        Assert.True(done.Compared > 5, Said(pair));
        Assert.Equal(0, done.Snapped);
        Assert.Equal(0, done.DriveRefused + done.DriveStopped + done.DriveShoved);
    }

    static string Said(Pair pair)
    {
        var d = pair.Client.Own.Done;
        return $"drives {d.Drives}, compared {d.Compared}, blended {d.Blended}, snapped {d.Snapped}, refused {d.DriveRefused}, stopped {d.DriveStopped}, " +
               $"shoved {d.DriveShoved}, largest {d.Largest:0.0} u, errors {string.Join(' ', d.Errors.TakeLast(12).Select(e => e.ToString("0.0")))}";
    }

    /// <summary>
    /// The refusals her machine sees for itself are not walked here: the host's pause (it has said so), her character dead, and an
    /// order of hers the host has not answered. She stands, and so does the host's copy of her.
    /// </summary>
    [Theory]
    [InlineData("paused")]
    [InlineData("dead")]
    [InlineData("busy")]
    public void A_push_her_machine_knows_the_host_refuses_does_not_move_her(string why)
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: true);
        var her = pair.Mine.Player!.Thing;
        if (why == "paused") pair.Server.SetPaused(true);
        else if (why == "dead") pair.Hers.Actor!.Flags |= Actor.Flag.Dead;
        // Her machine told (under the simulator a loss of the pause's message costs a resend).
        pair.Turns(300, () => why switch { "paused" => pair.Client.Paused, "dead" => her.Actor!.Dead, _ => true });
        pair.Turns(3);
        if (why == "busy") her.Actor!.BusyUntil = pair.Mine.Milliseconds + 60_000;   // a swing of hers guessed here, say
        var from = her.Position;
        var hostFrom = pair.Hers.Position;
        pair.Keys = true;
        pair.Push = Toward(pair.Mine, from, 5);
        pair.Turns(1);
        Assert.Equal(from, her.Position);
        Assert.False(pair.Client.Own.Driving);
        pair.Turns(30);
        Assert.Equal(0, pair.Client.Own.Done.Drives);
        if (why != "busy")
        {
            Assert.Equal(hostFrom, pair.Hers.Position);
            Assert.True(Vector3.Distance(her.Position, from) < 1f);
        }
    }

    /// <summary>
    /// The refusals her machine cannot see: her character busy on the host with something her machine has not heard of, the host's
    /// <c>driving</c> off (her frame would make no push then), the Storyteller holding her body. She walked here at once; the host never
    /// moved her, so within two round trips her walk here is given back to the snapshots, which ease her to where the host has her; and
    /// it is not started again until she lets go.
    /// </summary>
    [Theory]
    [InlineData("busy")]
    [InlineData("driving off")]
    [InlineData("possessed")]
    public void A_push_the_host_refuses_unseen_is_given_back_within_two_round_trips(string why)
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: true);
        var her = pair.Mine.Player!.Thing;
        if (why == "busy") pair.Hers.Actor!.BusyUntil = pair.Host.Milliseconds + 60_000;
        else if (why == "driving off") pair.Server.Qol!.Driving = false;
        else pair.Server.Possessed = pair.Hers;
        var hostFrom = pair.Hers.Position;
        pair.Keys = true;
        pair.Push = Toward(pair.Mine, her.Position, 5);
        pair.Turns(1);
        Assert.True(pair.Client.Own.Driving);
        int started = pair.Mine.Milliseconds;
        pair.Turns(120, () => pair.Client.Own.Done.DriveRefused > 0);
        Assert.Equal(1, pair.Client.Own.Done.DriveRefused);
        Assert.InRange(pair.Mine.Milliseconds - started, OwnWalk.DriveWaitFloorMs, pair.Client.Own.DriveWaitMs + 100);
        Assert.False(pair.Client.Own.Active);
        pair.Turns(30);
        Assert.Equal(1, pair.Client.Own.Done.Drives);                          // held still: not again
        Assert.Equal(hostFrom, pair.Hers.Position);
        pair.Turns(15);
        Assert.True(Apart(pair) < 2f, $"she stands {Apart(pair):0.0} units from where the host has her");
        pair.Push = Vector2.Zero;
        pair.Turns(3);
        pair.Push = Toward(pair.Mine, her.Position, 5);
        pair.Turns(1);
        Assert.Equal(2, pair.Client.Own.Done.Drives);                          // a new press is guessed again
    }

    /// <summary>
    /// The host stands her in mid-walk (busy there all at once, a thing her machine has not heard of): her machine walks on, and once
    /// the host's snapshots have had her standing for <see cref="OwnWalk.HostStoppedMs"/> her walk here is given back; she ends where
    /// the host has her.
    /// </summary>
    [Fact]
    public void A_walk_the_host_stops_in_mid_push_is_given_back()
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: true);
        var her = pair.Mine.Player!.Thing;
        pair.Keys = true;
        pair.Run = false;
        pair.Push = Toward(pair.Mine, her.Position, 5);
        pair.Turns(120, () => pair.Client.Own.Done.Compared >= 3);
        Assert.True(pair.Client.Own.Driving, Said(pair));
        pair.Hers.Actor!.BusyUntil = pair.Host.Milliseconds + 60_000;
        pair.Turns(120, () => pair.Client.Own.Done.DriveStopped > 0);
        Assert.Equal(1, pair.Client.Own.Done.DriveStopped);
        Assert.False(pair.Client.Own.Active);
        pair.Turns(60);
        Assert.True(Apart(pair) < 2f, $"she stands {Apart(pair):0.0} units from where the host has her; {Said(pair)}");
    }

    /// <summary>
    /// The host's copy of her put elsewhere in mid-push (a shove her machine knows nothing of): a small difference is blended in over a
    /// few frames, a large one snaps her there and she walks on from there; either way she ends where the host has her.
    /// </summary>
    [Theory]
    [InlineData(30f, false)]
    [InlineData(150f, true)]
    public void A_difference_in_her_keyboard_walk_is_blended_when_small_and_snapped_when_large(float shove, bool snaps)
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: true);
        var her = pair.Mine.Player!.Thing;
        pair.Keys = true;
        pair.Run = false;
        pair.Push = Toward(pair.Mine, her.Position, 5);
        pair.Turns(120, () => pair.Client.Own.Done.Compared >= 3);
        Assert.True(pair.Client.Own.Driving, Said(pair));
        var hers = pair.Hers;
        var aside = new Vector3(-pair.Push.Y, pair.Push.X, 0f);
        var to = hers.Position + aside * shove;
        if (pair.Host.Walk!.Find(to, 64f) < 0) to = hers.Position - aside * shove;
        pair.Host.Move(hers, to);
        if (pair.Host.WalkerFor(hers) is { } walker) walker.Ground(snap: false);
        pair.Turns(20);
        pair.Push = Vector2.Zero;
        pair.Turns(150, () => !pair.Client.Own.Active);
        pair.Turns(60);
        var done = pair.Client.Own.Done;
        output.WriteLine(Said(pair));
        if (snaps) Assert.True(done.Snapped >= 1, $"no snap: {Said(pair)}");
        else
        {
            Assert.True(done.Blended >= 1, $"no blend: {Said(pair)}");
            Assert.Equal(0, done.Snapped);
        }
        Assert.Equal(0, done.DriveShoved);
        Assert.True(Apart(pair) < 2f, $"she stands {Apart(pair):0.0} units from where the host has her; {Said(pair)}");
    }

    /// <summary>
    /// A click while her keys push her: on the host the push cancels the click's walk at its next turn, and so here; her keyboard walk
    /// goes on. Let go and click: the click's walk is hers here at once, as before, and she ends where the host has her.
    /// </summary>
    [Fact]
    public void A_click_while_she_drives_gives_way_to_the_keys_and_after_letting_go_walks_as_a_click_does()
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: true);
        var walker = pair.Mine.Player!;
        pair.Keys = true;
        pair.Push = Toward(pair.Mine, walker.Thing.Position, 5);
        pair.Turns(15);
        Click(pair);
        pair.Turns(1);
        Assert.True(pair.Client.Own.Driving);
        Assert.False(walker.Walking);                                           // the push cleared the route, as the host's turn does there
        Assert.Equal(0, pair.Client.Own.Done.Predicted);
        pair.Turns(15);
        Assert.False(pair.Host.WalkerFor(pair.Hers)!.Walking, "the host walks her by the click while the push is held");
        pair.Push = Vector2.Zero;
        pair.Turns(1);
        Click(pair);
        pair.HerSim.Step(Step);
        Assert.False(pair.Client.Own.Driving);
        Assert.True(pair.Client.Own.Active && walker.Walking);
        Assert.Equal(1, pair.Client.Own.Done.Predicted);
        pair.Keys = false;                                                      // the keys left alone (and the turns unpaced)
        pair.Turns(600, () => !walker.Walking && !pair.Client.Own.Active && pair.Hers.Actor?.Action != ActionMode.Move);
        pair.Turns(30);
        Assert.True(Apart(pair) < 2f, $"she stands {Apart(pair):0.0} units from where the host has her; {Said(pair)}");
    }

    /// <summary>With prediction off (the developer's <c>--no-predict</c>) her push waits for the host, as earlier: nothing moves her here but the snapshots.</summary>
    [Fact]
    public void With_prediction_off_her_push_waits_for_the_host_as_before()
    {
        using var files = Install.Open();
        using var pair = Pair.Open(files, predict: false);
        var her = pair.Mine.Player!.Thing;
        var from = her.Position;
        pair.Keys = true;
        pair.Push = Toward(pair.Mine, from, 5);
        pair.Turns(1);
        Assert.Equal(from, her.Position);
        pair.Turns(120, () => Vector3.Distance(her.Position, from) > 50f);
        Assert.True(Vector3.Distance(her.Position, from) > 50f, "the host's walk never reached her machine");
        Assert.Equal(0, pair.Client.Own.Done.Drives);
    }

    // ---- measured (a timing test) ------------------------------------------------------------------------------------------

    sealed record Drove(string Kind, double FirstStepMs, long Frames, OwnWalk.Counts Counts, float Apart, float Moved, string Note);

    /// <summary>
    /// **Her keyboard walk measured**: at each round trip, with the simulator on her machine alone (2 % loss, 20 ms jitter) and 30
    /// frames a second, a press off (the earlier build's way) and, with prediction, a press of each kind — a run let go in the open (a stop), a walk
    /// turned half-way, a run into a wall and along it, a run into another character standing in her way: from the press to her first
    /// step on her machine, and how often and how far the host's snapshots corrected her, and where she and the host's copy of her stand
    /// once both have stopped.
    /// </summary>
    [Fact, Trait("Category", "Timing")]
    public void Her_keyboard_walk_measured_at_five_round_trips_with_loss_and_jitter()
    {
        using var files = Install.Open();
        var lines = new List<string>
        {
            "her own keyboard walk on her machine, 30 frames a second, the simulator on her machine alone (loss 2 %, jitter 20 ms):",
            "  round trip | first step off | on | kind | compared | blended | snapped | given back | mean err | largest | apart at rest",
        };
        var failed = new List<string>();
        foreach (int rtt in RoundTripsMeasured)
        {
            var off = Keyboard(files, rtt, predict: false);
            var on = Keyboard(files, rtt, predict: true);
            foreach (var d in on)
            {
                var c = d.Counts;
                lines.Add($"  {rtt,4} ms    | {off[0].FirstStepMs,6:0} ms | {d.FirstStepMs,3:0} ms | {d.Kind,-14} | {c.Compared,8} | {c.Blended,7} | {c.Snapped,7} | {c.DriveRefused,10} | {(c.Compared == 0 ? 0 : c.Total / c.Compared),5:0.0} u | {c.Largest,5:0.0} u | {d.Apart,5:0.0} u  (walked {d.Moved:0} u; {d.Note})");
                if (d.Apart >= 8f) failed.Add($"she and the host's copy of her stand {d.Apart:0.0} units apart once both stopped: {lines[^1]}");
                // Her first step is the press's own frame (the wall clock's ms say the same but for a hitch of the machine's).
                if (!(d.FirstStepMs < off[0].FirstStepMs) || d.Frames != 1) failed.Add($"her first step on her machine with prediction: {d.FirstStepMs:0} ms: {lines[^1]}");
            }
        }
        foreach (var l in lines) output.WriteLine(l);
        if (Environment.GetEnvironmentVariable("VITOMIR_DRIVE_REPORT") is { Length: > 0 } report) File.WriteAllLines(report, lines);
        Assert.True(failed.Count == 0, string.Join("\n", failed));
    }

    static List<Drove> Keyboard(Vfs.GameFiles files, int rtt, bool predict)
    {
        var settings = GameSettings.For(ChronicleSet.Load(files)["MP_DAInn"]!, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        place.Awaken(new Ai.AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        using var server = new GameServer(settings, new NetPeer(0) { Sim = null }) { Qol = new Qol(), Fixes = new Fixes() };
        server.Start();
        server.Enter(place, null);
        var sim = new Simulation(place);
        var peer = new NetPeer(0) { Sim = new NetSim(new NetSim.Spec(LatencyMs: rtt, JitterMs: 20, Loss: 2, Seed: 23 + rtt), 23 + rtt) };
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
        var push = Vector2.Zero;
        bool run = true;
        var wall = Stopwatch.StartNew();
        long frame = 0;
        void Turn()
        {
            sim.Step(Step);
            server.Poll((int)wall.ElapsedMilliseconds);
            client.Poll(Step);
            if (herSim is not null && mine?.Player is { } me) client.Drive(push, run, me, Step);
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
        var results = new List<Drove>();
        // One press: the push (turned to `then` half-way when given), held `ms`, let go; both at rest, and a second more. `during`
        // is asked each frame of it (a door's turn).
        void Press(string kind, Vector2 dir, int ms, bool runs, Vector2? then = null, Action? during = null)
        {
            var c = client.Own.Done;
            var (compared, blended, snapped, total, errors) = (c.Compared, c.Blended, c.Snapped, c.Total, c.Errors.Count);
            int refused = c.DriveRefused + c.DriveStopped + c.DriveShoved;
            int drives = c.Drives;
            long pressedAt = frame;
            long firstFrame = -1;
            var from = her.Position;
            run = runs;
            push = dir;
            var pressed = Stopwatch.StartNew();
            double first = -1;
            Until(() =>
            {
                if (first < 0 && Vector3.Distance(her.Position, from) > 1f) { first = pressed.Elapsed.TotalMilliseconds; firstFrame = frame - pressedAt; }
                if (then is { } turn && pressed.ElapsedMilliseconds >= ms / 2) push = turn;
                during?.Invoke();
                return pressed.ElapsedMilliseconds >= ms;
            }, ms + 50);
            // A press of hers is drawn the frame after the key: a step taken in that frame shows then, a frame's 33 ms on.
            if (first >= 0 && first < Step * 1000) first = Step * 1000;
            push = Vector2.Zero;
            Until(() => !client.Own.Active && Vector3.Distance(hers.Position, her.Position) < 8f && place.WalkerFor(hers) is not { Speed: > 0f }, 6_000);
            Until(() => false, 1_000);
            var d = new OwnWalk.Counts
            {
                Drives = c.Drives, Compared = c.Compared - compared, Blended = c.Blended - blended, Snapped = c.Snapped - snapped,
                Total = c.Total - total, DriveRefused = c.DriveRefused + c.DriveStopped + c.DriveShoved - refused,
            };
            d.Errors.AddRange(c.Errors.Skip(errors));
            d.Largest = d.Errors.Count == 0 ? 0 : d.Errors.Max();
            results.Add(new Drove(kind, first < 0 ? double.PositiveInfinity : first, firstFrame, d, Vector3.Distance(her.Position, hers.Position), Vector3.Distance(from, her.Position),
                                  $"drives {c.Drives - drives}, frames {firstFrame}"));
        }
        if (!predict)
        {
            Press("a stop", Toward(mine, her.Position, rtt), 1200, runs: true);
            return results;
        }
        Press("a stop", Toward(mine, her.Position, rtt), 1200, runs: true);
        var dir = Toward(mine, her.Position, rtt + 1);
        Press("turned", dir, 1200, runs: false, then: new Vector2(-dir.Y, dir.X));
        Press("into a wall", Toward(mine, her.Position, rtt + 2), 3000, runs: true);
        // Another character standing in her way, 120 units on, on the host; her machine shows it once a snapshot has.
        var way = Toward(mine, her.Position, rtt + 3);
        var dummy = place.Place("MP_Erik", hers.Position + new Vector3(way * 120f, 0f), hers.Orientation, hers.Sector)!;
        dummy.Actor!.Flags |= Actor.Flag.AiPaused;
        Until(() => client.Mirror?.Find(dummy.Guid) is { } seen && client.Mirror.Still(seen, 300), 10_000);
        Press("into a body", way, 1500, runs: true);
        place.Remove(dummy, drop: false);
        Until(() => client.Mirror?.Find(dummy.Guid) is not { InWorld: true }, 5_000);
        // A door (the inn has none: a table stands for one, a body the host puts in her way or takes out of it): opened on the host
        // as she comes up to it, which her machine hears a half round trip and a snapshot later; and shut in front of her there.
        WorldThing Door(Vector2 at, float ahead)
        {
            var door = place.Place("bartable", hers.Position + new Vector3(at * ahead, 0f), new Vector3(0, 0, MathF.Atan2(-at.X, at.Y) * 180f / MathF.PI + 90f), hers.Sector)!;
            Until(() => client.Mirror?.Find(door.Guid) is { InWorld: true }, 10_000);
            return door;
        }
        var opens = Toward(mine, her.Position, rtt + 4);
        var shut = Door(opens, 160f);
        Press("a door opening", opens, 1500, runs: true, during: () =>
        {
            if (shut.InWorld && Vector3.Distance(hers.Position, shut.Position) < hers.Radius + shut.Radius + 40f) place.Remove(shut, drop: false);
        });
        var shuts = Toward(mine, her.Position, rtt + 5);
        var shutFrom = hers.Position;
        WorldThing? closed = null;
        Press("a door shutting", shuts, 1500, runs: true, during: () =>
        {
            if (closed is null && Vector3.Distance(hers.Position, shutFrom) > 60f)
                closed = place.Place("bartable", hers.Position + new Vector3(shuts * (hers.Radius + 55f + 30f), 0f),
                                     new Vector3(0, 0, MathF.Atan2(-shuts.X, shuts.Y) * 180f / MathF.PI + 90f), hers.Sector);
        });
        if (closed is not null) place.Remove(closed, drop: false);
        return results;
    }
}
