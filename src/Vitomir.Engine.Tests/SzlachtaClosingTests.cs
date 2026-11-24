// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Text;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **How 1.1's szlachta closes** (*"In the base game they move and pause more frequently, move much
/// more slowly"*): a <c>Szlachta</c> made in Old Town by day, its own mind given Christof standing 15 m (1080 units) off on the plaza's
/// floor, and nothing else thinking — its enemy think (the manoeuvre, <see cref="Minds.Manoeuvre"/>) and its walker, frame by
/// frame at 60 a second, until its claw's reach takes Christof in (<see cref="Bodies.InReach"/>, the fight's own test). The platform's
/// approach <c>oneone</c> (<c>Vitomir.Platform/Combat/DuelApproach.cs</c>) is measured against this;
/// with <c>VITOMIR_CLOSING_OUT</c> set the frames of
/// <c>VITOMIR_CLOSING_RUNS</c> runs (default 50) are written there as a table.
/// </summary>
public class SzlachtaClosingTests
{
    const float Frame = 1f / 60f;
    const float Start = 1080f;

    /// <summary>One closing: each frame's time (s), the szlachta's place (units) and whether it is walking; the time it came within reach.</summary>
    sealed record Closing(List<(float T, Vector3 At, bool Walking, AiMode Mode, long Draws)> Frames, float? Reached, float ReachUnits, Vector3 Target);

    static Closing Close(GameFiles files, int seed, float seconds = 30f)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), seed, new EffectDefinitions(files));
        var christof = place.Player!.Thing;
        var szlachta = place.Spawn("Szlachta", christof)!;
        Assert.NotNull(szlachta.Actor);
        var mind = minds.Adopt(szlachta)!;
        Assert.NotNull(mind);
        // The first bearing (every 15°) at which it stands on the walk map, level with him, and sees him: the plaza's open floor.
        bool clear = false;
        for (int bearing = 0; bearing < 360 && !clear; bearing += 15)
        {
            float a = bearing * MathF.PI / 180f;
            place.Move(szlachta, christof.Position + new Vector3(MathF.Cos(a) * Start, MathF.Sin(a) * Start, 0f));
            clear = mind.Walker.Ground() && MathF.Abs(szlachta.Position.Z - christof.Position.Z) < 8f && Rpg.Sight.Sees(place.Level, szlachta, christof)
                    && Enumerable.Range(1, 9).All(k => mind.Walker.Map.Find(Vector3.Lerp(szlachta.Position, christof.Position, k / 10f)) >= 0);
        }
        Assert.True(clear, "no bearing from Christof's start has a clear 15 m");
        minds.Begin(0f);                          // it joins
        mind.HasNoticed = true;                   // no wake-up delay: the closing alone is measured
        minds.SetTarget(mind, christof);
        mind.Enter(AiMode.Enemy, andBase: false);
        mind.NextThink = minds.Now;
        float reach = szlachta.Actor!.Weapon!.Current.MaxDistance;

        var frames = new List<(float, Vector3, bool, AiMode, long)>();
        float? reached = null;
        for (float t = 0f; t < seconds; t += Frame)
        {
            frames.Add((t, szlachta.Position, mind.Walker.Walking, mind.Mode, minds.Random.Draws));
            if (Bodies.InReach(szlachta, christof, reach)) { reached = t; break; }
            minds.Begin(Frame);
            minds.ThinkOne(mind);
            minds.Finish();
            minds.FollowOne(mind, Frame);
        }
        return new Closing(frames, reached, reach, christof.Position);
    }

    /// <summary>It closes in steps, standing between some of them, at no more than its run, and arrives.</summary>
    [Fact]
    public void A_szlachta_closes_in_steps_and_pauses_at_its_run()
    {
        using var files = Install.Open();
        int stood = 0;
        for (int seed = 1; seed <= 4; seed++)
        {
            var c = Close(files, seed);
            Assert.True(c.Reached is not null, $"seed {seed}: it never came within reach in 30 s");
            float most = 0f;
            for (int i = 1; i < c.Frames.Count; i++)
            {
                float v = Vector2.Distance(new Vector2(c.Frames[i].At.X, c.Frames[i].At.Y), new Vector2(c.Frames[i - 1].At.X, c.Frames[i - 1].At.Y)) / Frame;
                most = MathF.Max(most, v);
                if (!c.Frames[i].Walking && !c.Frames[i - 1].Walking) stood++;
            }
            Assert.InRange(most, 100f, 125f);   // its ACTOR_RUNSPEED, 120 units a second (the walker's own rounding aside)
        }
        Assert.True(stood > 0, "it never stood between its steps");
    }

    /// <summary>
    /// In reach (the fight's think): its blows' starts — each attack order taken, which sets its busy time to the attack's
    /// RELOADTIME — over <paramref name="seconds"/> once it has come within reach, Christof standing. The swing's own frames are
    /// not stepped (no thing's tick): what is measured is the think's cadence against the busy time, which is all that orders a blow.
    /// </summary>
    static List<(float Begin, float Busy)> Blows(GameFiles files, int seed, float seconds)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), seed, new EffectDefinitions(files));
        var christof = place.Player!.Thing;
        var szlachta = place.Spawn("Szlachta", christof)!;
        var mind = minds.Adopt(szlachta)!;
        place.Move(szlachta, christof.Position + new Vector3(szlachta.Radius + christof.Radius + 20f, 0f, 0f));
        mind.Walker.Ground();
        minds.Begin(0f);
        mind.HasNoticed = true;
        minds.SetTarget(mind, christof);
        mind.Enter(AiMode.Enemy, andBase: false);
        mind.NextThink = minds.Now;
        var me = szlachta.Actor!;
        var blows = new List<(float, float)>();
        int busy = me.BusyUntil;
        for (float t = 0f; t < seconds; t += Frame)
        {
            minds.Begin(Frame);
            minds.ThinkOne(mind);
            minds.Finish();
            if (me.BusyUntil != busy && me.BusyUntil > minds.Now) blows.Add((minds.Now / 1000f, (me.BusyUntil - minds.Now) / 1000f));
            busy = me.BusyUntil;
        }
        return blows;
    }

    /// <summary>Its blows in reach, written as a table for the measurement (only with <c>VITOMIR_CLOSING_CADENCE</c>).</summary>
    [Fact]
    public void Blows_in_reach_are_written_for_measuring_when_asked()
    {
        string? output = Environment.GetEnvironmentVariable("VITOMIR_CLOSING_CADENCE");
        Assert.SkipWhen(output is null, "set VITOMIR_CLOSING_CADENCE to a file to write 1.1's blows in reach");
        using var files = Install.Open();
        var text = new StringBuilder("run,blow,begin_s,busy_s\n");
        for (int run = 0; run < 10; run++)
        {
            var blows = Blows(files, 1 + run, 60f);
            Assert.True(blows.Count > 10, $"run {run}: {blows.Count} blows in a minute");
            for (int i = 0; i < blows.Count; i++)
                text.Append(string.Create(CultureInfo.InvariantCulture, $"{run},{i},{blows[i].Begin:0.###},{blows[i].Busy:0.###}\n"));
        }
        File.WriteAllText(output!, text.ToString());
    }

    /// <summary>The frames of many closings, written as a table for the measurement (only with <c>VITOMIR_CLOSING_OUT</c>).</summary>
    [Fact]
    public void Closings_are_written_for_measuring_when_asked()
    {
        string? output = Environment.GetEnvironmentVariable("VITOMIR_CLOSING_OUT");
        Assert.SkipWhen(output is null, "set VITOMIR_CLOSING_OUT to a file to write 1.1's closings");
        int runs = int.TryParse(Environment.GetEnvironmentVariable("VITOMIR_CLOSING_RUNS"), out int n) ? n : 50;
        using var files = Install.Open();
        var text = new StringBuilder("run,seed,t,x_m,y_m,walking,reached_s,reach_m,mode,draws,target_x_m,target_y_m\n");
        for (int run = 0; run < runs; run++)
        {
            var c = Close(files, 1 + run);
            foreach (var (t, at, walking, mode, draws) in c.Frames)
                text.Append(string.Create(CultureInfo.InvariantCulture,
                    $"{run},{1 + run},{t:0.####},{at.X / 72f:0.####},{at.Y / 72f:0.####},{(walking ? 1 : 0)},{(c.Reached is { } r ? r.ToString("0.###", CultureInfo.InvariantCulture) : "")},{c.ReachUnits / 72f:0.###},{mode},{draws},{c.Target.X / 72f:0.####},{c.Target.Y / 72f:0.####}\n"));
        }
        File.WriteAllText(output!, text.ToString());
    }
}
