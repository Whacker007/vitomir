// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// The models repository's stair clips on a flight (<see cref="PersonStairs"/>, <see cref="PersonFlight"/>): found on a flight and chosen
/// by the way and the gait, played at the rate the flight's tread gives and kept on its treads, eased in and out at the flight's ends; the feet on
/// the treads (how far into a step and above a tread a planted foot is, with the clips and with the earlier build's flat gait and IK); a set without them and
/// The earlier build's switch have no flight store. The body is moved by the test along a built flight, its capsule stepping each riser as the motor does.
/// Skips without the models repository's files, or.
/// </summary>
public sealed class StairClipTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.0") => v.ToString(format, CultureInfo.InvariantCulture);

    const float StairX = 2f, Radius = 0.3f;
    const int Risers = 8;

    static LevelSpace Flight(float riser, float tread)
    {
        var level = new TestLevel().Floor(-20, -20, 30, 20);
        for (int k = 1; k <= Risers; k++)
            level.Box(new Vector3(StairX + (k - 1) * tread, -1f, 0f), new Vector3(StairX + Risers * tread + 3f, 1f, k * riser));
        return level.Build("flight");
    }

    static float Ground(float x, float riser, float tread) => x < StairX ? 0f : Math.Clamp((int)MathF.Floor((x - StairX) / tread) + 1, 0, Risers) * riser;

    static float Capsule(float x, float riser, float tread, bool up)
    {
        // The capsule's front meets a riser going up (stepped in a tick); going down, its back leaves a tread.
        float edge = up ? x + Radius : x - Radius;
        return MathF.Max(Ground(edge, riser, tread), Ground(x, riser, tread));
    }

    static PersonStairs Stairs(CharacterAssets assets, int kind)
    {
        var stairs = assets.Kind(kind).Person!.Stairs;
        Assert.SkipUnless(stairs is not null, "the models repository's files are too old (no stair clips)");
        return stairs!;
    }

    /// <summary>What a drive along a flight measured.</summary>
    sealed record Drive(float Into, float Above, float IntoMid, float AboveMid, float MostStep, List<float> Pelvis, List<float> Share, List<int> Clips, World World, EntityId Person, float StairRate);

    /// <summary>
    /// The player driven up (or down) a flight of <paramref name="riser"/> on <paramref name="tread"/> at <paramref name="speed"/> m/s, then on its landing a
    /// while: how far a planted foot (its ball or heel) goes into a step at most, how far above its tread a planted foot stands at most (the lower of its
    /// ball and heel), the drawn pelvis's largest move in a tick, and the pelvis, the flight's share and the clip every tick.
    /// </summary>
    static Drive Run(PersonGroundSettings rules, float riser, float tread, float speed, bool up)
    {
        var (assets, kind, person) = Player();
        var (world, e) = Make(assets, kind, Flight(riser, tread), Vector3.Zero, up ? 0f : DetMath.Pi, groundRules: rules);
        var sk = person.Model.Skeleton;
        int[] balls = [sk.Find("ball_l"), sk.Find("ball_r")];
        int[] ankles = [sk.Find("foot_l"), sk.Find("foot_r")];
        float top = StairX + (Risers - 1) * tread;   // the last nosing (the landing from it)
        float from = up ? StairX - 2.5f : top + 2.75f, to = up ? top + 2.75f : StairX - 2.5f, dir = up ? 1f : -1f;
        // Standing first: the sole's offsets (each ball and ankle over flat ground).
        for (int i = 0; i < 30; i++) Step(world, e, new Vector3(from, 0f, Capsule(from, riser, tread, up)), up ? 0f : DetMath.Pi, true);
        float ballOff = Bone(world, e, assets, balls[0]).Z - Capsule(from, riser, tread, up), ankleOff = Bone(world, e, assets, ankles[0]).Z - Capsule(from, riser, tread, up);
        var last = new Vector3[2];
        var lastHeel = new Vector3[2];
        var still = new int[2];
        float into = 0f, above = 0f, intoMid = 0f, aboveMid = 0f, mostStep = 0f, stairRate = 0f;
        var pelvis = new List<float>();
        var share = new List<int>();
        var shares = new List<float>();
        float x = from;
        int ticks = (int)(MathF.Abs(to - from) / speed / Tick);
        for (int t = 0; t < ticks; t++)
        {
            x += dir * speed * Tick;
            var feet = new Vector3(x, 0f, Capsule(x, riser, tread, up));
            Step(world, e, feet, up ? 0f : DetMath.Pi, true, velocity: new Vector3(dir * speed, 0f, 0f));
            var p = Bone(world, e, assets, person.Rig.Pelvis);
            if (pelvis.Count > 1) mostStep = MathF.Max(mostStep, MathF.Abs(p.Z - 2f * pelvis[^1] + pelvis[^2]));   // the change of its move a tick
            pelvis.Add(p.Z);
            var flight = world.TryStore<PersonFlight>(out var fs) && fs.Has(e) ? fs.Get(e) : default;
            shares.Add(flight.Share);
            share.Add(world.Get<CharacterAnimation>(e).Clip);
            if (flight.Share > 0.99f && world.Get<CharacterAnimation>(e) is var an1 && assets.Clip(an1.Clip) is GaitBlendClip b1)
                stairRate = MathF.Max(stairRate, an1.Rate * assets.Clip(assets.Kind(kind).Person!.Stairs!.All[(dir > 0 ? 0 : 1) + (speed > 2f ? 2 : 0)]).Duration / b1.Duration);
            bool onFlight = x > StairX - 0.3f && x < top + 0.3f;
            for (int s = 0; s < 2; s++)
            {
                var ball = Bone(world, e, assets, balls[s]);
                var heel = Bone(world, e, assets, ankles[s]);
                // Planted: ball and heel both all but still for three ticks.
                still[s] = t > 0 && (ball - last[s]).Length() / Tick < 0.15f && (heel - lastHeel[s]).Length() / Tick < 0.15f ? still[s] + 1 : 0;
                bool planted = still[s] >= 3;
                (last[s], lastHeel[s]) = (ball, heel);
                if (!onFlight) continue;
                // The ground under each, the lower within 2 cm along (a ball rolled over a nosing's edge is over its drop).
                float Under(float at) => MathF.Min(Ground(at - 0.02f, riser, tread), Ground(at + 0.02f, riser, tread));
                float ballGap = ball.Z - ballOff - Under(ball.X), heelGap = heel.Z - ankleOff - Under(heel.X);
                // Into a step: the ball's sole below the tread it is over, or a heel over a tread (a heel past a nosing hangs over the drop).
                into = MathF.Max(into, MathF.Max(-ballGap, -heelGap));
                bool mid = x > StairX + tread && x < top - tread;   // on the flight, a tread from its ends
                if (mid) intoMid = MathF.Max(intoMid, MathF.Max(-ballGap, -heelGap));
                if (mid && planted) aboveMid = MathF.Max(aboveMid, MathF.Min(ballGap, heelGap));
                if (planted) above = MathF.Max(above, MathF.Min(ballGap, heelGap));
            }
        }
        return new Drive(into, above, intoMid, aboveMid, mostStep, pelvis, shares, share, world, e, stairRate);
    }

    [Fact]
    public void The_stair_clips_are_found_and_chosen_by_way_and_gait()
    {
        var (assets, kind, _) = Player();
        var stairs = Stairs(assets, kind);
        var person = assets.Kind(kind).Person!;
        Assert.All(stairs.All, c => Assert.True(c >= 0));
        int walk = person.Walk!.First, run = person.Run!.First;
        Assert.Equal((stairs.WalkUp, 0, 1f), stairs.For(person, walk, 1, 1f));
        Assert.Equal((stairs.WalkDown, 1, 1f), stairs.For(person, walk, -1, 1f));
        Assert.Equal((stairs.RunUp, 2, 0.5f), stairs.For(person, run, 1, 0.5f));
        Assert.Equal((stairs.RunDown, 3, 1f), stairs.For(person, run, -1, 1f));
        Assert.Equal(-1, stairs.For(person, walk + 90, 1, 1f).Clip);        // sideways: the flat gait with the IK
        Assert.Equal(-1, stairs.For(person, walk + 180, 1, 1f).Clip);       // backwards likewise
        Assert.Equal(-1, stairs.For(person, person.Sprint, 1, 1f).Clip);    // the sprint's own clip: none (on a flight the run's plays)
        Assert.Equal(-1, stairs.For(person, walk, 1, 0f).Clip);             // off a flight
        // Their rate on another flight: (speed / their speed) x (their tread / its tread). The walk's on 0.30 m treads is drawn for 1.25 x 0.30 / 0.24.
        var blend = (GaitBlendClip)assets.Clip(walk);
        var up = (INotedClip)assets.Clip(stairs.WalkUp);
        float on30 = stairs.NaturalSpeed(blend, blend.NaturalSpeed, 0, 1f, 0.30f, 1f);
        Assert.Equal(up.Notes.GroundSpeed * up.Duration * 0.30f / 0.24f / blend.Duration, on30, 4);
        Assert.Equal(blend.NaturalSpeed, stairs.NaturalSpeed(blend, blend.NaturalSpeed, 0, 0f, 0.30f, 1f));
        // The fit by riser and tread: whole on the clips' own, none on a 0.40 m riser or a 0.6 m tread.
        var rules = new PersonGroundSettings();
        Assert.Equal(1f, PersonStairs.Fit(rules, 0.20f, 0.24f));
        Assert.Equal(1f, PersonStairs.Fit(rules, 0.15f, 0.30f));
        Assert.Equal(0f, PersonStairs.Fit(rules, 0.40f, 0.30f));
        Assert.Equal(0f, PersonStairs.Fit(rules, 0.20f, 0.60f));
        Assert.InRange(PersonStairs.Fit(rules, 0.31f, 0.30f), 0.05f, 0.95f);
        // The clips' own treads: their capsule nosing_offset past a nosing at their start (the walk up: its left ball on the tread above, 0.8 cm over it).
        Assert.Equal(0.4f - 0.2f * 0.2376f / 0.24f, stairs.ClipGround(0, 0f, 0.37f), 4);
    }

    [Fact]
    public void The_step_is_kept_on_the_treads()
    {
        var (assets, kind, _) = Player();
        var stairs = Stairs(assets, kind);
        // At its own start the walk up is in step with a body as far past a nosing as its capsule is.
        var up = (INotedClip)assets.Clip(stairs.WalkUp);
        var g = up.Notes.Ground!;
        Assert.Equal(0f, stairs.Lock(0, 0f, g.NosingOffsetM / g.TreadM), 4);
        // A body a quarter of a tread further on: the clip a sixteenth of its cycle behind (four treads a cycle).
        Assert.Equal(1f / 16f, stairs.Lock(0, 0f, g.NosingOffsetM / g.TreadM + 0.25f), 4);
        // Never more than half a tread either way.
        for (float phase = 0f; phase < 1f; phase += 0.05f) Assert.InRange(stairs.Lock(2, 0.3f, phase), -1f / 12f - 1e-4f, 1f / 12f + 1e-4f);
    }

    [Fact]
    public void Up_and_down_the_0_20_flight_the_feet_are_on_the_treads()
    {
        var (assets, kind, _) = Player();
        Stairs(assets, kind);
        var on = new PersonGroundSettings();
        var off = on.WithoutStairClips;
        var lines = new List<string>();
        var drives = new List<(Drive Now, Drive Then)>();
        foreach (var (speed, name) in new[] { (1.25f, "walking"), (3.0f, "running") })
            foreach (bool up in new[] { true, false })
            {
                var now = Run(on, 0.20f, 0.24f, speed, up);
                var then = Run(off, 0.20f, 0.24f, speed, up);
                lines.Add($"{(up ? "up" : "down")} 0.20 on 0.24 {name} at {F(speed, "0.00")} m/s: on the flight a foot at most {F(now.IntoMid * 1000f)} mm into a step (no clips {F(then.IntoMid * 1000f)}), "
                          + $"a planted one {F(now.AboveMid * 1000f)} mm above its tread (no clips {F(then.AboveMid * 1000f)}); at its ends and over all: a foot at most {F(now.Into * 1000f)} mm into a step (no clips {F(then.Into * 1000f)}), "
                          + $"a planted foot at most {F(now.Above * 1000f)} mm above its tread (no clips {F(then.Above * 1000f)}); the drawn pelvis's move a tick changing at most {F(now.MostStep * 1000f)} mm "
                          + $"(no clips {F(then.MostStep * 1000f)}); the stair clip's rate {F(now.StairRate, "0.00")}");
                drives.Add((now, then));
            }
        output.WriteLine(string.Join("\n", lines));
        Assert.True(drives.Sum(d => d.Now.IntoMid) < drives.Sum(d => d.Then.IntoMid) / 10f, "on the flight the feet go a tenth as far into the steps as no clips");
        foreach (var (now, then) in drives)
        {
            Assert.True(now.Share.Max() > 0.99f, "the stair clips came in whole on the flight");
            Assert.InRange(now.Share[^1], 0f, 1e-3f);   // and went out on the landing
            Assert.InRange(now.MostStep, 0f, 0.01f);     // no pop at the flight's ends
            Assert.InRange(now.IntoMid, -1f, 0.015f);   // on the flight, a foot within a centimetre and a half of its tread
            Assert.InRange(now.Into, -1f, 0.08f);       // and at its ends within 8 cm, as it comes in and goes out
            Assert.False(then.World.TryStore<PersonFlight>(out _));   // no flight store
        }
    }

    [Fact]
    public void Risers_out_of_range_keep_the_flat_gait()
    {
        var (assets, kind, _) = Player();
        Stairs(assets, kind);
        var steep = Run(new PersonGroundSettings(), 0.40f, 0.30f, 1.25f, true);
        var shallow = Run(new PersonGroundSettings(), 0.15f, 0.30f, 1.25f, true);
        output.WriteLine($"0.40 m risers: the stair clips' share at most {F(steep.Share.Max(), "0.00")}; 0.15 m: {F(shallow.Share.Max(), "0.00")}, a foot at most {F(shallow.Into * 1000f)} mm into a step, {F(shallow.Above * 1000f)} mm above");
        Assert.InRange(steep.Share.Max(), 0f, 1e-3f);
        Assert.True(shallow.Share.Max() > 0.99f);
    }
}
