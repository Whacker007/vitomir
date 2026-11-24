// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The reach highlight (ours): a pure function from the level, the body's feet and view and its
/// numbers to marks along the lips where a move would start, each with the moves the motor would make there — found
/// as the probe finds them, on a small level built here and, when <c>VITOMIR_COURSE_LEVEL</c> names the cooked proving
/// ground, at its lanes — and nothing it does changes the world: a drive with the highlight made every tick keeps
/// the digests of the same drive without it.
/// </summary>
public sealed class ReachHighlightTests
{
    static readonly Locomotion Human = Locomotion.Sandbox;

    /// <summary>
    /// A floor with, round a body at the origin facing +X: ahead a bench, a thin fence and a block of 1.5 m; to the
    /// right a ledge of 2.4 m, to the left a wall of 3.2 m (a little too high); behind, a wall of 6 m.
    /// </summary>
    static LevelSpace Yard() => new TestLevel()
        .Floor(-12, -12, 12, 12)
        .Box(new(2.5f, -4.5f, 0), new(2.9f, -2.5f, 0.45f))   // a bench: hop (yellow)
        .Box(new(2.5f, -1.5f, 0), new(2.6f, 0.5f, 0.8f))     // a thin fence: hurdle and vault
        .Box(new(2.5f, 1.5f, 0), new(4.5f, 3.5f, 1.5f))      // a block of 1.5 m: mantle
        .Box(new(-1, -6, 0), new(1, -4, 2.4f))               // a ledge of 2.4 m, its face to the body at y −4: grab
        .Box(new(-1, 4, 0), new(1, 6, 3.2f))                 // a wall of 3.2 m, its face at y 4: just out of reach
        .Box(new(-4.5f, -1, 0), new(-3.5f, 1, 6f))           // a wall of 6 m behind: nothing
        .Build("reach-yard");

    static List<ReachMark> Marks(LevelSpace level, Vector3 feet, float facing = 0f, float halfAngle = ReachHighlight.HalfAngle)
    {
        var marks = new List<ReachMark>();
        ReachHighlight.Find(level.Collision, feet, facing, Human, marks, ReachHighlight.Range, halfAngle);
        return marks;
    }

    /// <summary>The marks along a lip: their middles within 0.2 m of the line from <paramref name="a"/> to <paramref name="b"/> (level) at about its height.</summary>
    static List<ReachMark> Along(List<ReachMark> marks, Vector2 a, Vector2 b, float top) =>
        marks.Where(m =>
        {
            var p = new Vector2(m.Middle.X, m.Middle.Y);
            var d = b - a;
            float t = Math.Clamp(Vector2.Dot(p - a, d) / d.LengthSquared(), 0f, 1f);
            return Vector2.Distance(p, a + d * t) < 0.2f && MathF.Abs(m.Middle.Z - ReachHighlight.Lift - top) < 0.05f;
        }).ToList();

    static string Say(IEnumerable<ReachMark> marks) =>
        string.Join("; ", marks.Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.Moves} at {m.Middle.X:0.00} {m.Middle.Y:0.00} {m.Middle.Z:0.00}")));

    [Fact]
    public void Each_lip_in_reach_is_marked_with_the_move_that_starts_there_and_its_whole_front_is_lined()
    {
        var level = Yard();
        var marks = Marks(level, new Vector3(0, 0, 0.002f));
        void Expect(string what, Vector2 a, Vector2 b, float top, ReachMoves moves, float covered)
        {
            var on = Along(marks, a, b, top);
            Assert.True(on.Count > 0, $"{what}: no mark along it; all: {Say(marks)}");
            Assert.All(on, m => Assert.Equal(moves, m.Moves));
            // Joined: the marks along the front cover it (each a cell of Spacing), not a dash here and there.
            Assert.True(on.Count * ReachHighlight.Spacing >= covered - 0.3f,
                        $"{what}: {on.Count} marks cover {on.Count * ReachHighlight.Spacing:0.00} m of {covered:0.00} m");
        }
        // A sprint vaults the bench too (the hurdle's orange) across its front; its end, met along it, is deep.
        Expect("the bench's front", new(2.5f, -4.5f), new(2.5f, -2.7f), 0.45f, ReachMoves.Hop | ReachMoves.Hurdle, 2f);
        Expect("the fence's front", new(2.5f, -1.5f), new(2.5f, 0.5f), 0.8f, ReachMoves.Hurdle | ReachMoves.Vault, 2f);
        Expect("the block's front", new(2.5f, 1.5f), new(2.5f, 3.5f), 1.5f, ReachMoves.Mantle, 2f);
        Expect("the ledge's face", new(-1, -4), new(1, -4), 2.4f, ReachMoves.Grab, 2f);
        Expect("the high wall's face", new(-1, 4), new(1, 4), 3.2f, ReachMoves.Out, 2f);
        Assert.DoesNotContain(marks, m => m.Middle.Z > 4f);                                  // the 6 m wall: nothing
        Assert.All(marks, m => Assert.InRange(Vector2.Distance(new(m.Middle.X, m.Middle.Y), Vector2.Zero), 0f, ReachHighlight.Range + 0.5f));
    }

    [Fact]
    public void A_mark_lies_along_its_lip_a_little_above_and_out_from_it_and_says_its_height_over_the_ground()
    {
        var marks = Marks(Yard(), new Vector3(0, 0, 0.002f));
        // The bench's front (its side, towards the fence, is marked too, along X).
        var bench = Along(marks, new(2.5f, -4.5f), new(2.5f, -2.5f), 0.45f).Where(m => m.Normal.X < -0.9f).ToList();
        Assert.NotEmpty(bench);
        foreach (var m in bench)
        {
            Assert.Equal(m.A.Z, m.B.Z, 4);
            Assert.Equal(0.45f + ReachHighlight.Lift, m.A.Z, 3);
            Assert.Equal(2.5f - ReachHighlight.Outward, m.Middle.X, 3);                   // out from the face (−X)
            Assert.Equal(ReachHighlight.Spacing, Vector3.Distance(m.A, m.B), 3);
            Assert.Equal(-1f, m.Normal.X, 3);
            Assert.Equal(0.45f, m.Height, 2);
        }
    }

    [Fact]
    public void The_marks_are_the_same_for_the_same_level_body_and_numbers_and_follow_the_numbers_given()
    {
        var level = Yard();
        var feet = new Vector3(0, 0, 0.002f);
        Assert.Equal(Marks(level, feet), Marks(level, feet));
        // An elder's reach: the 3.2 m wall comes within a grab, and the 1.5 m block within a climb — its own hands and
        // bands; its tap is a human's jump whatever its own, so its higher jump changes nothing here.
        var elder = Human with { JumpImpulseBudget = MathF.Sqrt(2f * Human.Gravity * 1.2f), StandingReach = 2.8f, VaultMax = 1.6f, MantleMax = 2.4f };
        var marks = new List<ReachMark>();
        ReachHighlight.Find(level.Collision, feet, 0f, elder, marks);
        var wall = Along(marks, new(-1, 4), new(1, 4), 3.2f);
        var block = Along(marks, new(2.5f, 1.5f), new(2.5f, 3.5f), 1.5f);
        Assert.NotEmpty(wall);
        Assert.NotEmpty(block);
        Assert.All(wall, m => Assert.Equal(ReachMoves.Grab, m.Moves));
        Assert.All(block, m => Assert.Equal(ReachMoves.ClimbOnto, m.Moves));
    }

    [Fact]
    public void An_elder_unbound_sees_a_wall_of_3_5_m_to_mantle_with_a_tap_and_held_to_a_human_just_out_of_reach()
    {
        // The tap is a human's jump for every body; the bands and the reach are the body's numbers now.
        var level = new TestLevel().Floor(-12, -12, 12, 12).Box(new(2f, -2f, 0), new(4f, 2f, 3.5f)).Build("wall-3.5");
        var elder = Masquerade.WithHumanTap(Human with
        {
            JumpImpulseBudget = Locomotion.ImpulseFor(3f, 9.81f), StandingReach = 2.5f, VaultMax = 3f, MantleMax = 4f,
        }, Human);
        var feet = new Vector3(0, 0, 0.002f);
        List<ReachMark> On(in Locomotion numbers)
        {
            var marks = new List<ReachMark>();
            ReachHighlight.Find(level.Collision, feet, 0f, numbers, marks);
            return Along(marks, new(2f, -2f), new(2f, 2f), 3.5f);
        }
        var free = On(elder);
        Assert.NotEmpty(free);
        Assert.All(free, m => Assert.Equal(ReachMoves.Mantle, m.Moves));
        var held = On(Masquerade.HeldToHuman(elder, Human));
        Assert.NotEmpty(held);
        Assert.All(held, m => Assert.Equal(ReachMoves.Out, m.Moves));
    }

    [Fact]
    public void Only_ahead_and_to_the_sides_of_the_view_and_within_the_range_is_looked_at()
    {
        var level = Yard();
        // Facing −X, 60° either side: the bench, the fence and the block are behind, the ledge and the 3.2 m wall to
        // the sides; only the 6 m wall is ahead, and it has no lip in reach.
        var back = Marks(level, new Vector3(0, 0, 0.002f), DetMath.Pi, halfAngle: 60f * DetMath.DegToRad);
        Assert.Empty(back);
        // Far off, nothing.
        Assert.Empty(Marks(level, new Vector3(-11, -11, 0.002f)));
    }

    // ---------------------------------------------------------------- the proving ground's lanes

    static readonly Lazy<(LevelSpace Level, List<(string Start, float Y, float Face, float Top, float Depth)> Lanes)?> Course = new(() =>
    {
        var path = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        if (string.IsNullOrEmpty(path) || !File.Exists(Path.Combine(path, "level.json"))) return null;
        var gltf = Directory.Exists(Path.Combine(path, "assets")) ? Directory.GetFiles(Path.Combine(path, "assets"), "*.gltf").FirstOrDefault() : null;
        if (gltf is null) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(gltf));
        var lanes = new List<(string, float, float, float, float)>();
        foreach (var l in doc.RootElement.GetProperty("extras").GetProperty("provingGround").GetProperty("lanes").EnumerateArray())
        {
            if (!l.TryGetProperty("face", out var face) || !l.TryGetProperty("top", out var top) || !l.TryGetProperty("y", out var y)) continue;
            float depth = l.TryGetProperty("depth", out var d) ? d.GetSingle() : 2f;
            lanes.Add((l.GetProperty("start").GetString()!, y.GetSingle(), face.GetSingle(), top.GetSingle(), depth));
        }
        return (LevelSpace.Open(path), lanes);
    });

    [Theory]
    [InlineData("hop-0.40", ReachMoves.Hop | ReachMoves.Hurdle)]   // 0.4 m deep, a sprint vaults it
    [InlineData("hop-0.60", ReachMoves.Hop | ReachMoves.ClimbOnto | ReachMoves.Hurdle)]   // 0.6 m over the ground: the hop's top and the climb's foot, and a sprint vaults it
    [InlineData("hurdle-1.00", ReachMoves.Hurdle | ReachMoves.Vault)]
    [InlineData("vault-1.20", ReachMoves.Vault)]
    [InlineData("vault-1.30", ReachMoves.Out)]
    [InlineData("vault-climb0.9", ReachMoves.ClimbOnto)]
    [InlineData("mantle-1.50", ReachMoves.Mantle)]
    [InlineData("mantle-2.0", ReachMoves.Grab)]
    [InlineData("grab-2.6", ReachMoves.Grab)]
    [InlineData("grab-3.0", ReachMoves.Out)]
    [InlineData("grab-canopy", ReachMoves.Grab)]
    [InlineData("step-over-0.50", ReachMoves.Hurdle | ReachMoves.Vault)] // a run steps over it, a sprint or a held jump vaults it
    [InlineData("sprint-vault-block", ReachMoves.ClimbOnto)]           // too deep for the sprint vault
    public void At_a_course_lane_the_highlight_marks_the_lip_the_probe_finds_with_the_lane_s_move(string start, ReachMoves moves)
    {
        Assert.SkipUnless(Course.Value is not null, "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        var (level, lanes) = Course.Value!.Value;
        var lane = lanes.Single(l => l.Start == start);
        // Stood 3 m before the obstacle, facing it (east), on the floor.
        var feet = new Vector3(lane.Face - 3f, lane.Y, 0.002f);
        var marks = new List<ReachMark>();
        ReachHighlight.Find(level.Collision, feet, 0f, Human, marks);
        // The probe's own lip, read as the motor reads it from in front of the face.
        var near = new Vector3(lane.Face - Human.Radius - 0.12f, lane.Y, 0.002f);
        Assert.True(TraversalProbe.Read(level.Collision, near, Vector2.UnitX, Human, Human.Radius + BodyMotor.GrabReach, lane.Top + 0.3f, out var ob) && ob.HasTop,
                    $"{start}: the probe finds no top");
        Assert.Equal(lane.Top, ob.Top, 2);
        var on = Along(marks, new(lane.Face, lane.Y - 0.9f), new(lane.Face, lane.Y + 0.9f), ob.Top);
        Assert.True(on.Count >= 6, $"{start}: {on.Count} marks along its front ({Say(on)}); all: {Say(marks.Where(m => MathF.Abs(m.Middle.Y - lane.Y) < 3f))}");
        Assert.All(on, m => Assert.Equal(moves, m.Moves));
    }

    // ---------------------------------------------------------------- presentation only

    [Fact]
    public void A_drive_with_the_highlight_made_every_tick_keeps_the_digests_of_the_same_drive_without_it()
    {
        var level = Yard();
        List<ulong> Drive(bool reach)
        {
            var w = MovementServices.Register(new WorldBuilder(7), level).Build();
            var player = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Human);
            var marks = new List<ReachMark>();
            var digests = new List<ulong>();
            int found = 0;
            for (int t = 0; t < 720; t++)
            {
                // Run east into the fence and the bench, jump held now and then, sprint a while, turn a little.
                var held = (t % 120) < 30 ? Buttons.Jump : Buttons.None;
                if (t is >= 240 and < 360) held |= Buttons.Sprint;
                var frame = new ActionFrame(new Vector2(0, 1), new Vector2(t is >= 400 and < 440 ? 0.02f : 0f, 0f), held, new Vector2(0.5f, 0.5f));
                MovementServices.Step(w, frame);
                if (reach) found += ReachHighlight.For(w, player, marks);
                digests.Add(w.Digest());
            }
            if (reach) Assert.True(found > 0, "the highlight found nothing on the drive");
            return digests;
        }
        Assert.Equal(Drive(false), Drive(true));
    }
}
