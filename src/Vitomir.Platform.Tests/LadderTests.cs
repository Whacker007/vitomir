// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// <b>Ladders, the hang's shimmy and its push-off</b> (BodyMotor.Ladder.cs): use at a ladder's foot or top puts the
/// body on it, forward climbs and carries it over the top, back goes down and steps off at the foot, jump pushes off,
/// crouch or cancel lets go; nothing but use puts a body on one; hanging, the wish's sideways part shimmies along the lip
/// to its end and jump with back held pushes off the wall; a world with a ladder replays digest for digest.
/// </summary>
public class LadderTests
{
    static readonly Locomotion Human = Locomotion.Sandbox;

    /// <summary>
    /// A wall 4 m high from x 2 to 4.5 (its top a floor), on a floor; a ladder up its face at x 2, y 0, climbed from −X.
    /// The body on it stands at x 2 − 0.3 − 0.05.
    /// </summary>
    static LevelSpace Wall()
    {
        var level = new TestLevel().Floor(-10, -6, 12, 6).Box(new(2, -3, 0), new(4.5f, 3, 4)).Build("ladder");
        var ladders = LevelLadders.Of([new L2Ladder { Name = "wall", Foot = [2, 0, 0], Top = [2, 0, 4], Width = 0.5f, Facing = 180 }]);
        return new LevelSpace("ladder", level.Collision.WithLadders(ladders));
    }

    /// <summary>The proving ground's shimmy ledge: a wall 4 m high from x 2 to 2.3, y −5 to 5, and a ledge 0.15 m out at 2.4 m along it.</summary>
    static LevelSpace Ledge() => new TestLevel().Floor(-10, -8, 12, 8)
        .Box(new(2, -5, 0), new(2.3f, 5, 4)).Box(new(1.85f, -5, 2.3f), new(2, 5, 2.4f)).Build("ledge");

    sealed class Drove
    {
        public readonly List<TraversalKind> Started = [];
        public readonly List<LadderEnd> On = [];
        public readonly List<LadderLeave> Off = [];
        public readonly List<int> ShimmyStarts = [];
        public readonly List<ShimmyStop> ShimmyStops = [];
        public int PushedOffLip, OnLadderTicks;
        public float Lowest = float.MaxValue, Highest = float.MinValue;
        public Body End;
        public override string ToString() =>
            $"moves [{string.Join(", ", Started)}], on [{string.Join(", ", On)}], off [{string.Join(", ", Off)}], shimmy [{string.Join(", ", ShimmyStarts)}] / [{string.Join(", ", ShimmyStops)}], ended at {End.Feet} ({End.State})";
    }

    static Drove Go(LevelSpace level, Body body, int ticks, Func<int, Body, MoveIntent> intent, Traversal move = default)
    {
        var d = new Drove();
        d.End = Drive.Moves(level, body, ref move, ticks, intent, (i, b, m, o) =>
        {
            if (o.MoveStarted != TraversalKind.None) d.Started.Add(o.MoveStarted);
            if (o.LadderOn != LadderEnd.None) d.On.Add(o.LadderOn);
            if (o.LadderOff != LadderLeave.None) d.Off.Add(o.LadderOff);
            if (o.ShimmyStarted != 0) d.ShimmyStarts.Add(o.ShimmyStarted);
            if (o.ShimmyStopped != ShimmyStop.None) d.ShimmyStops.Add(o.ShimmyStopped);
            if (o.PushedOffLip) d.PushedOffLip++;
            if (b.State == BodyState.Ladder) d.OnLadderTicks++;
            d.Lowest = MathF.Min(d.Lowest, b.Feet.X);
            d.Highest = MathF.Max(d.Highest, b.Feet.Z);
        }, Human);
        return d;
    }

    /// <summary>Use on the first tick, then <paramref name="then"/>.</summary>
    static Func<int, Body, MoveIntent> UseThen(Func<int, Body, MoveIntent> then) => (i, b) =>
    {
        var m = then(i, b);
        m.Use = i == 0;
        return m;
    };

    static MoveIntent Still(float yaw) => new() { Facing = yaw, Faces = true };

    static MoveIntent Wish(float x, float y) => new() { Direction = Vector2.Normalize(new Vector2(x, y)), Gait = Gait.Run };

    [Fact]
    public void Use_at_the_foot_facing_it_puts_the_body_on_forward_climbs_at_a_metre_a_second_and_carries_it_over_the_top()
    {
        var level = Wall();
        var body = Drive.Settle(level, new Vector3(1.3f, 0, 0.02f));
        // A second of forward: up about a metre (the getting-on's 0.3 s first).
        var rise = Go(level, body, (int)(1.3f * World.TicksPerSecond), UseThen((i, b) => Wish(1, 0)));
        Assert.Equal([LadderEnd.Foot], rise.On);
        Assert.Equal(BodyState.Ladder, rise.End.State);
        Assert.Equal(1.65f, rise.End.Feet.X, 0.01f);
        Assert.InRange(rise.End.Feet.Z, 0.95f, 1.05f);
        Assert.Equal(0f, rise.End.Yaw, 0.01f);

        // Forward until it stands on the top (then still: the wall's top is 2.5 m deep).
        var all = Go(level, body, 6 * World.TicksPerSecond, UseThen((i, b) => b.State == BodyState.Ground && b.Feet.Z > 3.9f ? Still(0) : Wish(1, 0)));
        Assert.Equal([TraversalKind.LadderOn, TraversalKind.Ladder, TraversalKind.PullUp], all.Started);
        Assert.Equal([LadderLeave.Top], all.Off);
        Assert.Equal(BodyState.Ground, all.End.State);
        Assert.Equal(4f, all.End.Feet.Z, 0.01f);
        Assert.True(all.End.Feet.X > 2.3f, all.ToString());
        Assert.Equal(0, all.End.Ladder);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(MathF.PI / 2f)]   // the view looking along the wall
    [InlineData(MathF.PI)]        // the view looking away from the ladder
    public void Forward_is_the_stick_s_as_the_view_faces_so_it_climbs_whichever_way_the_view_looks_and_back_goes_down(float view)
    {
        var level = Wall();
        var body = Drive.Settle(level, new Vector3(1.3f, 0, 0.02f));
        var (s, c) = DetMath.SinCos(view);
        MoveIntent Stick(float y) => new() { Direction = new Vector2(c, s) * y, Facing = view, Gait = Gait.Run };
        var up = Go(level, body, 2 * World.TicksPerSecond, UseThen((i, b) => i < 40 ? Still(0) : Stick(1f)));
        Assert.Equal(BodyState.Ladder, up.End.State);
        Assert.True(up.End.Feet.Z > 1.2f, up.ToString());
        var down = Go(level, body, 5 * World.TicksPerSecond, UseThen((i, b) => i < 40 ? Still(0) : i < 240 ? Stick(1f) : Stick(-1f)));
        Assert.Equal([LadderLeave.Foot], down.Off);
    }

    [Theory]
    [InlineData(MathF.PI)]   // facing the drop
    [InlineData(0f)]         // facing away from it
    public void Use_at_the_top_puts_the_body_on_from_above_back_goes_down_and_steps_off_at_the_foot(float yaw)
    {
        var level = Wall();
        var body = Drive.Settle(level, new Vector3(2.35f, 0, 4.02f), yaw);
        Assert.Equal(BodyState.Ground, body.State);
        // On from above: the feet a metre under the top, on the ladder's line, facing it.
        var on = Go(level, body, (int)(0.7f * World.TicksPerSecond), UseThen((i, b) => Still(yaw)));
        Assert.Equal([LadderEnd.Top], on.On);
        Assert.Equal(BodyState.Ladder, on.End.State);
        Assert.Equal(3f, on.End.Feet.Z, 0.01f);
        Assert.Equal(1.65f, on.End.Feet.X, 0.01f);
        Assert.Equal(0f, DetMath.WrapAngle(on.End.Yaw), 0.05f);
        // Back (away from the ladder) all the way: down at 1.3 m/s and off onto the ground.
        var down = Go(level, body, 4 * World.TicksPerSecond, UseThen((i, b) => i < 84 ? Still(yaw) : Wish(-1, 0)));
        Assert.Equal([LadderLeave.Foot], down.Off);
        Assert.Equal(BodyState.Ground, down.End.State);
        Assert.True(down.End.Feet.Z < 0.01f && down.End.Feet.X < 1.6f, down.ToString());
        Assert.Equal(0, down.End.Ladder);
    }

    [Fact]
    public void Jump_on_a_ladder_pushes_off_backwards_and_crouch_or_cancel_lets_go()
    {
        var level = Wall();
        var body = Drive.Settle(level, new Vector3(1.3f, 0, 0.02f));
        int up = (int)(2.3f * World.TicksPerSecond);   // on, and two metres up
        var push = Go(level, body, up + 240, UseThen((i, b) => { var m = i < up ? Wish(1, 0) : Still(0); m.Jump = i == up; return m; }));
        Assert.Equal([LadderLeave.PushOff], push.Off);
        Assert.Equal(BodyState.Ground, push.End.State);
        Assert.True(push.End.Feet.X < 0.9f, $"a push-off lands well out from the ladder: {push}");
        Assert.True(push.Highest > 2.0f, push.ToString());

        foreach (var how in new[] { "crouch", "cancel" })
        {
            var go = Go(level, body, up + 240, UseThen((i, b) =>
            {
                var m = i < up ? Wish(1, 0) : Still(0);
                if (how == "crouch") m.Crouch = i >= up && i < up + 10; else m.Cancel = i == up;
                return m;
            }));
            Assert.Equal([LadderLeave.LetGo], go.Off);
            Assert.Equal(BodyState.Ground, go.End.State);
            Assert.Equal(1.65f, go.End.Feet.X, 0.02f);   // straight down
            Assert.True(go.End.Feet.Z < 0.01f, go.ToString());
        }
    }

    [Fact]
    public void Nothing_but_use_at_the_foot_facing_it_and_within_reach_puts_a_body_on_a_ladder()
    {
        var level = Wall();
        // Walking, running and jumping into it, no use (as a body on a route: routes never press use).
        var walk = Go(level, Drive.Settle(level, new Vector3(-2, 0, 0.02f)), 4 * World.TicksPerSecond, (i, b) =>
        {
            var m = Drive.Toward(1, 0, i < 200 ? Gait.Walk : Gait.Sprint);
            m.Jump = i % 90 == 45;
            return m;
        });
        Assert.Equal(0, walk.OnLadderTicks);
        Assert.Empty(walk.On);
        // Use facing away, and use too far off.
        var away = Go(level, Drive.Settle(level, new Vector3(1.3f, 0, 0.02f), MathF.PI), 60, UseThen((i, b) => Still(MathF.PI)));
        Assert.Empty(away.On);
        var far = Go(level, Drive.Settle(level, new Vector3(0.9f, 0, 0.02f)), 60, UseThen((i, b) => Still(0)));
        Assert.Empty(far.On);
        var aside = Go(level, Drive.Settle(level, new Vector3(1.3f, 1.0f, 0.02f)), 60, UseThen((i, b) => Still(0)));
        Assert.Empty(aside.On);
    }

    [Fact]
    public void Hanging_from_the_course_s_ledge_the_sideways_wish_shimmies_along_it_to_its_end_and_stops_there()
    {
        var level = Ledge();
        var hang = HangFromLedge(level, out var move);
        Assert.Equal(BodyState.Hanging, hang.State);
        float z = hang.Feet.Z;
        // To the body's right (−Y, facing +X) at 0.6 m/s: 1 s goes 0.6 m.
        var second = Go(level, hang, World.TicksPerSecond, (i, b) => Wish(0, -1), move);
        Assert.Equal(-0.6f, second.End.Feet.Y - hang.Feet.Y, 0.02f);
        Assert.Equal([1], second.ShimmyStarts);
        var all = Go(level, hang, 12 * World.TicksPerSecond, (i, b) => i < 1200 ? Wish(0, -1) : Still(0), move);
        Assert.Equal(BodyState.Hanging, all.End.State);
        Assert.Equal([1], all.ShimmyStarts);
        Assert.Equal([ShimmyStop.End], all.ShimmyStops);
        Assert.Equal(-5f + BodyMotor.ShimmyHand, all.End.Feet.Y, 0.02f);   // the leading hand at the ledge's end
        Assert.Equal(z, all.End.Feet.Z, 0.001f);
        // Back the other way, let go of the key part way: started and stopped (released).
        var back = Go(level, hang, 2 * World.TicksPerSecond, (i, b) => i < 120 ? Wish(0, 1) : Still(0), move);
        Assert.Equal([-1], back.ShimmyStarts);
        Assert.Equal([ShimmyStop.Released], back.ShimmyStops);
    }

    [Fact]
    public void Hanging_jump_with_back_held_pushes_off_the_wall_and_jump_alone_still_pulls_up_where_there_is_room()
    {
        var level = Ledge();
        var hang = HangFromLedge(level, out var move);
        var push = Go(level, hang, 240, (i, b) => { var m = Wish(-1, 0); m.Jump = i == 5; return m; }, move);
        Assert.Equal(1, push.PushedOffLip);
        Assert.Equal(BodyState.Ground, push.End.State);
        Assert.True(push.End.Feet.X < 0.8f, push.ToString());

        // A wall of 2.4 m with a top: jump alone pulls up.
        var top = new TestLevel().Floor(-10, -8, 12, 8).Box(new(2, -5, 0), new(4, 5, 2.4f)).Build("top");
        var hung = HangFromLedge(top, out var m2);
        var up = Go(top, hung, 240, (i, b) => { var m = Still(0); m.Jump = i == 5; return m; }, m2);
        Assert.Equal(TraversalKind.PullUp, up.Started[0]);
        Assert.Equal(2.4f, up.End.Feet.Z, 0.01f);
    }

    /// <summary>Runs at the lip along +X, a tap at 1.2 m off it, and waits for the hang (nothing held).</summary>
    static Body HangFromLedge(LevelSpace level, out Traversal move)
    {
        var body = Drive.Settle(level, new Vector3(0.6f, 0, 0.02f));
        move = default;
        int tap = -1;
        body = Drive.Moves(level, body, ref move, 150, (i, b) =>
        {
            if (b.State is BodyState.Hanging or BodyState.Traversing) return Still(0);
            var m = Drive.Toward(1, 0, Gait.Walk);
            if (tap < 0 && b.Feet.X >= 1.0f && b.State == BodyState.Ground) { tap = i; m.Jump = true; m.JumpHeld = true; }
            return m;
        }, numbers: Human);
        Assert.Equal(BodyState.Hanging, body.State);
        return body;
    }

    [Fact]
    public void A_recorded_drive_up_and_down_a_ladder_replays_digest_for_digest()
    {
        var level = Wall();
        static ActionFrame At(int t)
        {
            var held = Buttons.None;
            if (t % 900 == 30) held |= Buttons.Use;
            if (t % 900 == 700) held |= Buttons.Jump;
            var move = (t % 900) switch
            {
                < 20 => new Vector2(0, 0.5f),
                < 400 => new Vector2(0, 1f),
                < 650 => new Vector2(0, -1f),
                > 720 and < 860 => new Vector2(0, 1f),
                _ => Vector2.Zero,
            };
            return new ActionFrame(move, Vector2.Zero, held, default);
        }
        Rig Make()
        {
            var b = new WorldBuilder(5);
            MovementServices.Register(b, level);
            var w = b.Build();
            return new Rig(w, MovementServices.SpawnControlled(w, new(1.1f, 0, 0.01f), 0f, Human));
        }
        var r = Make();
        var tape = new Tape { Header = new TapeHeader { Build = "test", Seed = 5 } };
        var on = new List<LadderMounted>();
        var off = new List<LadderLeft>();
        for (int t = 0; t < 1800; t++)
        {
            var f = At(t);
            MovementServices.Step(r.W, f);
            on.AddRange(r.W.Events<LadderMounted>().ThisTick);
            off.AddRange(r.W.Events<LadderLeft>().ThisTick);
            tape.Frames.Add(f);
            if (r.W.Tick % 60 == 0) tape.Digests[r.W.Tick] = r.W.Digest();
        }
        Assert.True(on.Count >= 2, $"on {on.Count} times");
        Assert.All(on, e => Assert.Equal("wall", e.Name));
        Assert.Contains(off, e => e.How == LadderLeave.PushOff);
        var path = Path.Combine(Directory.CreateTempSubdirectory("vitomir-ladder-").FullName, "t.tape");
        tape.Write(path);
        var back = Tape.Read(path);
        var again = Make();
        int agreed = 0;
        foreach (var f in back.Frames)
        {
            MovementServices.Step(again.W, f);
            if (back.Digests.TryGetValue(again.W.Tick, out var want)) { Assert.Equal(want, again.W.Digest()); agreed++; }
        }
        Assert.True(agreed >= 29, $"only {agreed} digests compared");
    }

    [Fact]
    public void The_reach_highlight_shows_a_ladder_s_foot_and_top_in_range_across_its_width_and_nothing_out_of_range()
    {
        var level = Wall();
        var marks = new List<Presentation.ReachMark>();
        Presentation.ReachHighlight.Find(level.Collision, new Vector3(0, 0, 0), 0f, Human, marks);
        var ladder = marks.Where(m => m.Moves == Presentation.ReachMoves.Ladder).ToList();
        Assert.Equal(2, ladder.Count);
        Assert.Equal(0.5f, Vector3.Distance(ladder[0].A, ladder[0].B), 0.001f);
        Assert.Equal(0f, ladder[0].Middle.Z, 0.05f);
        Assert.Equal(4f, ladder[1].Middle.Z, 0.05f);
        Assert.Equal(4f, ladder[1].Height, 0.001f);
        Presentation.ReachHighlight.Find(level.Collision, new Vector3(-8, 0, 0), 0f, Human, marks);
        Assert.DoesNotContain(marks, m => m.Moves == Presentation.ReachMoves.Ladder);
    }

    /// <summary>
    /// A tower 4 m high from x 2.2 and a ladder mesh up its face that the level does not mark: rails 0.2 m out
    /// (x 2.0 to 2.2), rungs every 0.3 m; the ladder's face at x 2.0. <paramref name="known"/>: the ladder is in the level.
    /// </summary>
    static LevelSpace Rungs(bool known)
    {
        var t = new TestLevel().Floor(-10, -6, 12, 6).Box(new(2.2f, -3, 0), new(4.7f, 3, 4));
        foreach (float y in new[] { -0.22f, 0.22f }) t.Box(new(2.0f, y - 0.03f, 0), new(2.2f, y + 0.03f, 4));
        for (float z = 0.3f; z < 3.9f; z += 0.3f) t.Box(new(2.02f, -0.22f, z - 0.02f), new(2.08f, 0.22f, z + 0.02f));
        var level = t.Build("rungs");
        if (!known) return level;
        var ladders = LevelLadders.Of([new L2Ladder { Name = "rungs", Foot = [2, 0, 0], Top = [2, 0, 4], Width = 0.5f, Facing = 180 }]);
        return new LevelSpace("rungs", level.Collision.WithLadders(ladders));
    }

    [Fact]
    public void A_tap_at_a_ladder_whose_mesh_the_level_does_not_mark_grabs_no_rung_and_use_still_climbs_it()
    {
        Drove Tap(LevelSpace level, float at) => Go(level, Drive.Settle(level, new Vector3(0, 0, 0.02f)), 3 * World.TicksPerSecond, (i, b) =>
        {
            var m = b.Feet.X >= at + 0.2f ? Still(0) : Drive.Toward(1, 0, Gait.Walk);
            m.Jump = m.JumpHeld = b.State == BodyState.Ground && b.Feet.X >= at && b.Feet.X < at + 0.05f;
            return m;
        });
        bool grabbedUnknown = false;
        foreach (float at in new[] { 1.3f, 1.4f, 1.5f })
        {
            // Without the ladder in the level its rungs are lips like any (the test bites)...
            grabbedUnknown |= Tap(Rungs(known: false), at).Started.Contains(TraversalKind.Grab);
            // ...with it, none is taken: the platform reads a lip on a ladder's face as not to be climbed.
            var known = Tap(Rungs(known: true), at);
            Assert.DoesNotContain(TraversalKind.Grab, known.Started);
            Assert.DoesNotContain(TraversalKind.Hang, known.Started);
        }
        Assert.True(grabbedUnknown, "a tap at an unknown ladder's rungs should grab one");
        var rungs = Rungs(known: true);
        var marks = new List<Presentation.ReachMark>();
        Presentation.ReachHighlight.Find(rungs.Collision, new Vector3(0.8f, 0, 0), 0f, Human, marks);
        Assert.DoesNotContain(marks, m => (m.Moves & Presentation.ReachMoves.Grab) != 0 && MathF.Abs(m.Middle.Y) < 0.3f && m.Middle.Z < 3.9f);
        // The top floor's own edge is still a lip (a hang from it pulls up), and use climbs the ladder up and over.
        Assert.False(rungs.Collision.Ladders.OnFace(new Vector3(2.2f, 0, 4f)));
        Assert.True(rungs.Collision.Ladders.OnFace(new Vector3(2.04f, 0.1f, 2.42f)));
        var up = Go(rungs, Drive.Settle(rungs, new Vector3(1.5f, 0, 0.02f)), 6 * World.TicksPerSecond,
                    UseThen((i, b) => b.State == BodyState.Ground && b.Feet.Z > 3.9f ? Still(0) : Wish(1, 0)));
        Assert.Equal([LadderLeave.Top], up.Off);
        Assert.Equal(4f, up.End.Feet.Z, 0.01f);
    }

    sealed record Rig(World W, EntityId Player);
}
