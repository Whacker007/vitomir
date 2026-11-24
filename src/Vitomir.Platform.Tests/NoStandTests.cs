// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// <b>The no-stand flag</b> (ours): a level marks a top no body stands on (its
/// collision triangles carry <see cref="L2Surface.NoStand"/>, never Walk, as the cook writes them). Pinned on small
/// levels in memory beside unmarked twins: a body dropped onto a marked top, or put on one standing, slides off towards
/// its nearest edge and ends on the floor beside it within two seconds, the same way every time; walking stops at the
/// edge of a marked top level with the floor, and a marked kerb is not stepped onto; no mantle, hop up or pull-up ends on
/// one (the probe reads it as not standable, a grab of its lip still hangs, and the reach highlight shows no move onto
/// it); a vault over a thin marked top still goes over.
/// </summary>
public sealed class NoStandTests
{
    /// <summary>A level of quads, each marked no-stand or not (a marked one loses Walk, as the cook makes it).</summary>
    sealed class Marked
    {
        readonly List<Vector3> v = [];
        readonly List<int> idx = [];
        readonly List<bool> marked = [];

        public Marked Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool noStand = false)
        {
            int at = v.Count;
            v.AddRange([a, b, c, d]);
            idx.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
            marked.AddRange([noStand, noStand]);
            return this;
        }

        /// <summary>A floor of 2 m squares from −10 to 10, those whose west side is at <paramref name="markedFrom"/> or east of it marked.</summary>
        public Marked Floor(float markedFrom = float.PositiveInfinity)
        {
            for (int x = -10; x < 10; x += 2)
                for (int y = -10; y < 10; y += 2)
                    Quad(new(x, y, 0), new(x + 2, y, 0), new(x + 2, y + 2, 0), new(x, y + 2, 0), x >= markedFrom);
            return this;
        }

        /// <summary>A solid box standing on the floor (top and four sides), its top marked or not.</summary>
        public Marked Box(Vector3 a, Vector3 b, bool noStand = false)
        {
            Vector3 P(float x, float y, float z) => new(x, y, z);
            Quad(P(a.X, a.Y, b.Z), P(b.X, a.Y, b.Z), P(b.X, b.Y, b.Z), P(a.X, b.Y, b.Z), noStand);
            Quad(P(a.X, a.Y, a.Z), P(b.X, a.Y, a.Z), P(b.X, a.Y, b.Z), P(a.X, a.Y, b.Z));
            Quad(P(a.X, b.Y, a.Z), P(a.X, b.Y, b.Z), P(b.X, b.Y, b.Z), P(b.X, b.Y, a.Z));
            Quad(P(a.X, a.Y, a.Z), P(a.X, a.Y, b.Z), P(a.X, b.Y, b.Z), P(a.X, b.Y, a.Z));
            Quad(P(b.X, a.Y, a.Z), P(b.X, b.Y, a.Z), P(b.X, b.Y, b.Z), P(b.X, a.Y, b.Z));
            return this;
        }

        public LevelSpace Build()
        {
            var surfaces = new L2Surface[marked.Count];
            for (int t = 0; t < surfaces.Length; t++)
            {
                var (a, b, c) = (v[idx[3 * t]], v[idx[3 * t + 1]], v[idx[3 * t + 2]]);
                var n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                surfaces[t] = L2Surface.Solid | (marked[t] ? L2Surface.NoStand : MathF.Abs(n.Z) >= 0.7f ? L2Surface.Walk : 0);
            }
            return new LevelSpace("no-stand", CollisionWorld.FromTriangles([.. v], [.. idx], surfaces));
        }
    }

    /// <summary>A block standing on the floor from x 1 to 1 + <paramref name="depth"/> and y −1 to 1, its top marked or not.</summary>
    static LevelSpace Block(float height, bool noStand, float depth = 2f, float halfWidth = 1f) =>
        new Marked().Floor().Box(new(1, -halfWidth, 0), new(1 + depth, halfWidth, height), noStand).Build();

    sealed class Drove
    {
        public readonly List<TraversalKind> Started = [];
        public int Grabs, Met, OnTop;
        public int OffTick = -1;
        public Body End;
        public override string ToString() => $"started [{string.Join(", ", Started)}], ended at {End.Feet} ({End.State}), slid {OnTop} ticks, off at tick {OffTick}";
    }

    /// <summary>Steps <paramref name="body"/> for <paramref name="ticks"/>, noting the moves, the slide and the tick the feet were first under <paramref name="below"/>.</summary>
    static Drove Go(LevelSpace level, Body body, int ticks, Func<int, Body, MoveIntent> intent, float below = float.NegativeInfinity)
    {
        var d = new Drove();
        var move = default(Traversal);
        d.End = Drive.Moves(level, body, ref move, ticks, intent, (i, b, m, o) =>
        {
            if (o.MoveStarted != TraversalKind.None) d.Started.Add(o.MoveStarted);
            if (o.Grabbed) d.Grabs++;
            if (o.NoStandMet) d.Met++;
            if (o.OnNoStand) d.OnTop++;
            if (d.OffTick < 0 && b.Feet.Z < below) d.OffTick = i;
        });
        return d;
    }

    static Func<int, Body, MoveIntent> Still() => (_, _) => new MoveIntent { Facing = 0f, Faces = true, Gait = Gait.Walk };

    /// <summary>Going along +X at a walk (or standing still facing it), jump pressed at <paramref name="press"/> and held for <paramref name="hold"/> ticks.</summary>
    static Func<int, Body, MoveIntent> Along(int press = -1, int hold = 1, bool still = false) => (i, b) =>
    {
        var m = still ? new MoveIntent { Facing = 0f, Faces = true, Gait = Gait.Walk } : Drive.Toward(1, 0, Gait.Walk);
        m.Jump = i == press;
        m.JumpHeld = press >= 0 && i >= press && i < press + hold;
        return m;
    };

    [Fact]
    public void A_body_dropped_onto_a_marked_top_slides_off_its_nearest_edge_within_two_seconds_and_on_its_twin_it_stands()
    {
        var drop = new Vector3(2.3f, 0.4f, 0.8f);   // over the 0.5 m top from x 1 to 3, y −1 to 1: its nearest edge is y 1, 0.6 m away
        var twin = Go(Block(0.5f, false), Body.At(drop, 0f), 360, Still());
        Assert.True(twin.End.State == BodyState.Ground && MathF.Abs(twin.End.Feet.Z - 0.5f) < 0.02f, $"unmarked: it stands on the top ({twin})");
        Assert.Equal(0, twin.OnTop);

        var marked = Go(Block(0.5f, true), Body.At(drop, 0f), 360, Still(), below: 0.1f);
        Assert.True(marked.End.State == BodyState.Ground && marked.End.Feet.Z < 0.02f, $"marked: on the floor beside it ({marked})");
        Assert.True(marked.End.Feet.Y > 1f + 0.2f, $"marked: off over its nearest edge, y 1 ({marked})");
        Assert.InRange(marked.OffTick, 1, 240);
        Assert.Equal(1, marked.Met);
        Assert.True(marked.OnTop > 0);
    }

    [Theory]
    [InlineData(0.8f)]
    [InlineData(1.5f)]
    [InlineData(2.5f)]
    [InlineData(4.0f)]
    public void A_landing_on_a_marked_top_is_told_once_however_many_ticks_the_motor_meets_it(float from)
    {
        // A walk in play: "land: on a top marked no place to stand" logged three ticks running for one
        // landing. The motor may meet the top coming down on more than one tick; the world tells it once a landing
        // (Body.NoStandHold), and walking straight after onto the floor tells nothing more.
        var level = Block(0.5f, true);
        var drop = new Vector3(2.3f, 0.4f, from);
        int met = Go(level, Body.At(drop, 0f), 360, Still()).Met;
        var w = MovementServices.Register(new WorldBuilder(1), level).Build();
        MovementServices.SpawnBody(w, drop, 0f, Locomotion.Sandbox);
        var told = new List<NoStandLanded>();
        for (int i = 0; i < 360; i++)
        {
            MovementServices.Step(w, ActionFrame.Idle);
            told.AddRange(w.Events<NoStandLanded>().ThisTick);
        }
        Assert.True(met >= 1, "the motor never met the top");
        Assert.True(told.Count == 1, $"told {told.Count} times (the motor met it {met} times): ticks {string.Join(", ", told.Select(t => t.Tick))}");
    }

    [Fact]
    public void A_body_put_standing_in_the_middle_of_a_marked_top_is_never_stuck_and_goes_the_same_way_every_time()
    {
        var level = Block(0.5f, true, depth: 4f, halfWidth: 2f);    // x 1 to 5, y −2 to 2: its middle (3, 0) is 2 m from every edge
        Body Put() => new()
        {
            Feet = new Vector3(3f, 0f, 0.5f + BodyMotor.Skin), Yaw = 0f, State = BodyState.Ground,
            GroundNormal = Vector3.UnitZ, GroundTriangle = -1,
        };
        var first = Go(level, Put(), 600, Still(), below: 0.1f);
        var again = Go(level, Put(), 600, Still(), below: 0.1f);
        Assert.True(first.End.State == BodyState.Ground && first.End.Feet.Z < 0.02f, $"off the top and on the floor ({first})");
        Assert.InRange(first.OffTick, 1, 240);
        Assert.Equal(first.End.Feet, again.End.Feet);
        Assert.Equal(first.OffTick, again.OffTick);
    }

    [Fact]
    public void Walking_stops_at_the_edge_of_a_marked_top_level_with_the_floor_and_its_twin_is_walked_on()
    {
        var start = Drive.Settle(new Marked().Floor().Build(), new(-3f, 1f, 0.02f));
        var twin = Go(new Marked().Floor().Build(), start, 720, Along());
        Assert.True(twin.End.Feet.X > 4f, $"unmarked: walked on ({twin})");

        var marked = Go(new Marked().Floor(markedFrom: 2f).Build(), start, 720, Along());
        Assert.True(marked.End.State == BodyState.Ground && MathF.Abs(marked.End.Feet.Z) < 0.02f, $"still on the floor ({marked})");
        Assert.True(marked.End.Feet.X <= 2f + 0.01f && marked.End.Feet.X > 1.7f, $"stopped at the marked floor's edge, x 2 ({marked})");
        Assert.Equal(0, marked.OnTop);
    }

    [Fact]
    public void The_player_stopped_at_a_marked_floor_or_kerb_is_told_once_it_is_no_place_to_stand()
    {
        // Walking stops at the edge of a no-stand top (the motor takes the step back and has no outcome of it); the
        // world tells the player what stopped it, once a stop (PlayerStopped), and nothing at the unmarked twin.
        List<PlayerStopped> Walk(LevelSpace level)
        {
            var w = MovementServices.Register(new WorldBuilder(1), level).Build();
            MovementServices.SpawnControlled(w, new(-3f, 0f, 0.02f), 0f, Locomotion.Sandbox);
            var told = new List<PlayerStopped>();
            var walk = new ActionFrame(new Vector2(0, 1), Vector2.Zero, Buttons.Walk, default);
            for (int i = 0; i < 720; i++)
            {
                MovementServices.Step(w, walk);
                told.AddRange(w.Events<PlayerStopped>().ThisTick);
            }
            return told;
        }
        var floor = Assert.Single(Walk(new Marked().Floor(markedFrom: 2f).Build()));
        Assert.Equal(StopCause.NoStand, floor.Cause);
        Assert.Equal(Gait.Walk, floor.Gait);
        Assert.True(MathF.Abs(floor.Height) < 0.02f && floor.Feet.X <= 2.01f, floor.ToString());
        var kerb = Assert.Single(Walk(Block(0.2f, true)));
        Assert.Equal(StopCause.NoStand, kerb.Cause);
        Assert.Equal(0.2f, kerb.Height, 2);
        Assert.Empty(Walk(new Marked().Floor().Build()));
        Assert.Empty(Walk(Block(0.2f, false)));
    }

    [Fact]
    public void A_marked_kerb_is_not_stepped_onto_and_its_twin_is()
    {
        var level = Block(0.2f, false);
        var start = Drive.Settle(level, new(-1f, 0f, 0.02f));
        var twin = Go(level, start, 360, Along());
        Assert.True(twin.End.Feet.X > 2f && MathF.Abs(twin.End.Feet.Z - 0.2f) < 0.02f, $"unmarked: stepped onto ({twin})");
        var marked = Go(Block(0.2f, true), start, 360, Along());
        Assert.True(marked.End.Feet.X < 1f && marked.End.Feet.Z < 0.02f, $"marked: stopped at it, as at a slope too steep ({marked})");
    }

    [Fact]
    public void No_mantle_or_hop_up_ends_on_a_marked_top_and_their_twins_are_taken()
    {
        var start = new Vector3(0.5f, 0, 0.01f);
        var plain = Go(Block(1.6f, false), Drive.Settle(Block(1.6f, false), start), 240, Along(press: 6, hold: 60, still: true));
        Assert.Equal([TraversalKind.Mantle], plain.Started);
        var marked = Go(Block(1.6f, true), Drive.Settle(Block(1.6f, true), start), 240, Along(press: 6, hold: 60, still: true));
        Assert.DoesNotContain(TraversalKind.Mantle, marked.Started);
        Assert.True(marked.End.Feet.Z < 0.05f, $"marked: no mantle onto it ({marked})");

        var bench = Go(Block(0.45f, true, depth: 0.4f), Drive.Settle(Block(0.45f, true, depth: 0.4f), start), 360, Along(press: 6));
        Assert.DoesNotContain(TraversalKind.HopUp, bench.Started);
        Assert.True(bench.End.Feet.Z < 0.05f, $"a marked bench: not on it at the end ({bench})");
        var plainBench = Go(Block(0.45f, false, depth: 0.4f), Drive.Settle(Block(0.45f, false, depth: 0.4f), start), 240, Along(press: 6));
        Assert.Contains(TraversalKind.HopUp, plainBench.Started);
    }

    [Fact]
    public void A_marked_lip_is_still_held_but_never_pulled_up_onto()
    {
        var start = new Vector3(0.6f, 0, 0.01f);
        var plain = Go(Block(2.4f, false), Drive.Settle(Block(2.4f, false), start), 360, Along(press: 6, hold: 200));
        Assert.Equal(1, plain.Grabs);
        Assert.Contains(TraversalKind.PullUp, plain.Started);
        var marked = Go(Block(2.4f, true), Drive.Settle(Block(2.4f, true), start), 360, Along(press: 6, hold: 200));
        Assert.Equal(1, marked.Grabs);
        Assert.DoesNotContain(TraversalKind.PullUp, marked.Started);
        Assert.True(marked.End.Feet.Z < 2.4f - 0.5f, $"marked: never on the top ({marked})");
    }

    [Fact]
    public void A_vault_over_a_thin_marked_top_still_goes_over()
    {
        foreach (bool noStand in new[] { false, true })
        {
            var level = Block(0.8f, noStand, depth: 0.3f);
            var d = Go(level, Drive.Settle(level, new(0.5f, 0, 0.01f)), 240, Along(press: 6, hold: 60, still: true));
            Assert.True(d.Started.FirstOrDefault() == TraversalKind.Vault && d.End.Feet.X > 1.3f && d.End.Feet.Z < 0.05f,
                        $"{(noStand ? "marked" : "unmarked")}: vaulted over ({d})");
        }
    }

    [Fact]
    public void The_probe_reads_a_marked_top_as_not_standable_and_the_reach_highlight_shows_no_move_onto_it()
    {
        var loco = Drive.Numbers;
        var feet = new Vector3(0.5f, 0, 0);
        Assert.True(TraversalProbe.Read(Block(1.6f, false).Collision, feet, Vector2.UnitX, loco, 1.0f, 2.2f, out var plain));
        Assert.True(plain.HasTop && plain.Standable && !plain.NoStand, "unmarked: a top that can be stood on");
        Assert.True(TraversalProbe.Read(Block(1.6f, true).Collision, feet, Vector2.UnitX, loco, 1.0f, 2.2f, out var o));
        Assert.True(o.HasTop && !o.Standable && o.NoStand && o.Holdable, "marked: a top, held but not stood on");
        Assert.Equal(1.6f, o.Top, 2);

        var marks = new List<ReachMark>();
        ReachHighlight.Find(Block(1.6f, false).Collision, feet, 0f, loco, marks);
        Assert.Contains(marks, m => (m.Moves & ReachMoves.Mantle) != 0);
        ReachHighlight.Find(Block(1.6f, true).Collision, feet, 0f, loco, marks);
        Assert.DoesNotContain(marks, m => (m.Moves & (ReachMoves.Mantle | ReachMoves.ClimbOnto | ReachMoves.Hop)) != 0);
    }

    [Fact]
    public void Only_the_marked_triangle_is_no_stand()
    {
        var level = Block(0.5f, true).Collision;
        int marked = 0;
        for (int t = 0; t < level.Surfaces.Length; t++)
            if (level.NoStand(t)) { marked++; Assert.True((level.Surfaces[t] & L2Surface.Walk) == 0); }
        Assert.Equal(2, marked);                      // the top's two triangles
        Assert.False(level.NoStand(-1));
        Assert.False(level.NoStand(level.Surfaces.Length));
    }
}
