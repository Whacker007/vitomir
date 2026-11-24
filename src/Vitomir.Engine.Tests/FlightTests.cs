// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Fliers. The engine's <c>pedfly</c> sends a crow to its next perch by calling the
/// walker's <c>StartWalk</c> itself: one straight leg, no search, no ground. In 1.1
/// (Old Town by day) the crow flies every few seconds, every
/// flight a straight line at 250 units a second between perches from 316 to 683 units up, its
/// heading changing only as it takes off.
/// </summary>
public class FlightTests
{
    static float Towards(Vector3 from, Vector3 to) => MathF.Atan2(-(to.X - from.X), to.Y - from.Y) * (180f / MathF.PI);

    static Player Christof(Vfs.GameFiles files) =>
        Location.Load(files, "P1_OTDY", things: false, sky: false, player: "Christof").Player!;

    [Fact]
    public void A_flight_is_one_straight_line_through_the_air()
    {
        using var files = Install.Open();
        var bird = Christof(files);
        var from = bird.Position;
        var to = from + new Vector3(300f, 400f, 200f);        // 538.5 away, climbing 0.37 of it
        int arrived = 0;
        bird.Thing.Place!.Arrived += (thing, _) => { if (ReferenceEquals(thing, bird.Thing)) arrived++; };

        Assert.True(bird.FlyTo(to, 250f));
        // Faced the way it is going as it sets off, flat, before a single step.
        Assert.Equal(Towards(from, to), bird.Heading, 0.01f);
        float heading = bird.Heading, clock = 0f;
        var line = Vector3.Normalize(to - from);
        for (; clock < 10f && bird.Walking; clock += 1f / 60f)
        {
            bird.Follow(1f / 60f);
            var off = bird.Position - from;
            // On the line the whole way: nothing pulls it down to the ground.
            Assert.True((off - Vector3.Dot(off, line) * line).Length() < 0.01f, $"off the line at {bird.Position}");
            Assert.Equal(heading, bird.Heading);
        }
        Assert.False(bird.Walking);
        Assert.False(bird.GaveUp);
        Assert.Equal(to.X, bird.Position.X, 0.1f);
        Assert.Equal(to.Y, bird.Position.Y, 0.1f);
        Assert.Equal(to.Z, bird.Position.Z, 0.1f);
        Assert.InRange(clock, 538.5f / 250f - 0.05f, 538.5f / 250f + 0.05f);
        Assert.Equal(1, arrived);                              // and it says so, as a walk does
        Assert.Equal(0f, bird.Speed);
    }

    [Fact]
    public void A_flight_straight_up_turns_nobody()
    {
        using var files = Install.Open();
        var bird = Christof(files);
        float heading = bird.Heading;
        // Straight up is 1.0 of the leg vertical, over the 0.8 beyond which StartWalk
        // leaves the heading alone.
        Assert.True(bird.FlyTo(bird.Position + new Vector3(1f, 0f, 300f), 250f));
        Assert.Equal(heading, bird.Heading);
        for (float clock = 0; clock < 5f && bird.Walking; clock += 1f / 60f) bird.Follow(1f / 60f);
        Assert.Equal(heading, bird.Heading);
    }

    /// <summary>
    /// The crow in Old Town takes off. Its perches are markers up in the air — 34 of the 36 spots it
    /// can pick have no ground anywhere under them — and once Vitomir would not send it to
    /// one, so it sat where it was put for the whole game.
    /// </summary>
    [Fact]
    public void The_crow_flies_between_its_perches()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var minds = place.Awaken(new AiClasses(files), 3);
        var crow = minds.All.Single(m => m.Mode == AiMode.PedFly);
        var start = crow.Thing.Position;
        float low = start.Z, high = start.Z;
        int flights = 0;
        bool was = false;
        for (int i = 0; i < 120 * 30; i++)
        {
            minds.Tick(1f / 30f);
            minds.Follow(1f / 30f);
            bool flying = crow.Moving;
            if (flying && !was) flights++;
            was = flying;
            low = MathF.Min(low, crow.Thing.Position.Z);
            high = MathF.Max(high, crow.Thing.Position.Z);
            if (flying) Assert.Equal(250f, crow.Walker.Speed);     // its bare run speed, 0x218
        }
        Assert.True(flights >= 4, $"{flights} flights in two minutes");
        Assert.True(Vector3.Distance(start, crow.Thing.Position) > 1f || flights > 0);
        // It went up and down between perches rather than keeping to one height.
        Assert.True(high - low > 100f, $"flew only between {low:0} and {high:0}");
    }

    /// <summary>The crow's perches on Petrin Hill by day, as 1.1 flew between them (two
    /// runs), each flight from where it stood to where it landed.</summary>
    static readonly (float X, float Y, float Z)[][] HillFlights =
    [
        [(-144, 432, 136), (-920, -640, 336), (872, 504, 136), (440, 664, 136), (1424, 328, 364),
         (440, 664, 136), (-920, -640, 336), (-144, 432, 136)],
        [(-144, 432, 136), (-920, -640, 336), (-144, 432, 136), (-920, -640, 336), (-864, -24, 336),
         (-920, -640, 336), (-864, -24, 336), (736, -280, 136), (440, 664, 136), (736, -280, 136)],
    ];

    static (Location Place, Minds Minds, Mind Crow) Hill(Vfs.GameFiles files)
    {
        var place = Location.Load(files, "P1_HILLD", things: true, sky: false, scene: "HILLD_4_1");
        var minds = place.Awaken(new AiClasses(files), 1);
        return (place, minds, minds.All.Single(m => m.Mode == AiMode.PedFly));
    }

    static InterestSpot Perch(Minds minds, Mind crow, Vector3 at) =>
        Enumerable.Range(crow.FirstSpot, crow.SpotCount).Select(i => minds.Spots.All[i])
                  .Single(s => Vector3.Distance(s.Position, at) < 1f);

    /// <summary>
    /// **A flier looks along the wrong line, and 1.1's crows prove it.** The engine hands its sight
    /// trace the perch's *position* where the ray's direction belongs (a quirk the original
    /// has, kept), so the ray runs along the perch's coordinates from the level's origin, not towards
    /// the perch (`Minds.Sighted`). Of the sixteen flights 1.1's crow made on Petrin Hill, this reading
    /// allows fifteen; a sight line towards the perch refused four of them — Vitomir's old test, over
    /// the whole level, likewise.
    /// </summary>
    [Fact]
    public void A_flier_sees_along_the_perchs_own_coordinates()
    {
        using var files = Install.Open();
        var (place, minds, crow) = Hill(files);
        int allowed = 0, flights = 0;
        var refused = new List<string>();
        foreach (var run in HillFlights)
            for (int i = 0; i + 1 < run.Length; i++)
            {
                var from = new Vector3(run[i].X, run[i].Y, run[i].Z);
                var to = new Vector3(run[i + 1].X, run[i + 1].Y, run[i + 1].Z);
                place.Move(crow.Thing, from);
                flights++;
                if (minds.Sighted(crow, Perch(minds, crow, to))) allowed++;
                else refused.Add($"{from} → {to}");
            }
        Assert.Equal(16, flights);
        Assert.Equal(15, allowed);
        // The open one: both traces meet the sky, and 1.1 flew it anyway.
        Assert.Equal("<-864, -24, 336> → <736, -280, 136>", Assert.Single(refused));
    }

    /// <summary>
    /// And what that keeps it from: the perch by the path at (1056, −920, 152), a few units off the
    /// ground, cannot be seen from any of the crow's other perches, so it never lands there — as 1.1's
    /// never did in either run. Vitomir's crow took one flight in four to it until then, which is what
    /// raised the question of a flier standing on real ground.
    /// </summary>
    [Fact]
    public void The_crow_on_Petrin_Hill_never_lands_by_the_path()
    {
        using var files = Install.Open();
        var (place, minds, crow) = Hill(files);
        var perches = Enumerable.Range(crow.FirstSpot, crow.SpotCount).Select(i => minds.Spots.All[i]).ToList();
        var path = Perch(minds, crow, new Vector3(1056, -920, 152));
        Assert.Equal(9, perches.Count);
        foreach (var from in perches.Where(p => !p.Equals(path)))
        {
            place.Move(crow.Thing, from.Position);
            Assert.False(minds.Sighted(crow, path), $"seen from {from.Position}");
        }
    }

    /// <summary>
    /// **The crow's flight is its run.** The flight passes the flier's run speed, 250, and the locomotion choice picks the
    /// run for anything over 200; the crow's set lists `crow_fly` for `walk` and for `run` with the same flags. So Vitomir
    /// plays the run, as 1.1 does.
    /// </summary>
    [Fact]
    public void The_crows_flight_plays_its_run()
    {
        using var files = Install.Open();
        var motions = new Anim.MotionSets(files);
        var set = motions.Set("crow");
        Assert.NotNull(set);
        var run = motions.Find(set, Player.ModeRun);
        Assert.NotNull(run);
        Assert.Equal("crow_fly", Path.GetFileNameWithoutExtension(run.Value.Mode.Animation).ToLowerInvariant());
        Assert.True(250f > Player.RunAnimationBeyond);
    }
}
