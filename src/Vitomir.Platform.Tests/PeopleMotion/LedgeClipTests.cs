// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;
using Gait = Vitomir.Platform.Bodies.Gait;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// The models repository's clips (<see cref="PersonLedgeClips"/>, <see cref="PeopleLedge"/>, the motor's ledge on clips, BodyMotor.Ledge.cs)
/// on the body's own motor and the player's spring: the notes read; the capsule placed from the real edge by the notes (the knuckles against the edge);
/// the hang free or braced by the wall under the edge (the feet against the wall); whole shimmy cycles at the clips' speed, finished when the wish is let
/// go, none past the lip's end; the braces as the wall comes and goes; the pull-up onto the top; the drop, the fall and the landing by its band; the grabs
/// from standing, running and falling with their hands on the real edge at their contact; no pop at any clip's start and end; the switch. Every
/// expectation from the clips' notes. Skips without the models repository's files, or.
/// </summary>
public sealed class LedgeClipTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    static PersonLedgeClips Ledge(CharacterAssets assets, int kind)
    {
        var ledge = assets.Kind(kind).Person!.Ledge;
        Assert.SkipUnless(ledge is not null && assets.Kind(kind).Person!.Over is not null && assets.Kind(kind).Person!.Air is not null,
                          "the models repository's files are too old (no ledge clips)");
        return ledge!;
    }

    static ClipNotes Notes(CharacterAssets assets, int clip) => ((INotedClip)assets.Clip(clip)).Notes;

    /// <summary>
    /// A world with the motor (the player's spring), the animation with the air's, the moves' and the ledge's clips (<paramref name="ledge"/>; null: the earlier build's),
    /// one body of the kind facing +X at <paramref name="at"/>; the floor at 0 and <paramref name="blocks"/>.
    /// </summary>
    internal static (World World, EntityId Body) Make(CharacterAssets assets, int kind, PersonLedgeSettings? ledge, Vector3 at, params (Vector3 Min, Vector3 Max)[] blocks) =>
        Make(assets, kind, ledge, new PersonOverSettings(), at, blocks);

    internal static (World World, EntityId Body) Make(CharacterAssets assets, int kind, PersonLedgeSettings? ledge, PersonOverSettings over, Vector3 at,
                                                     params (Vector3 Min, Vector3 Max)[] blocks)
    {
        var t = new TestLevel().Floor(-40, -40, 200, 40, cell: 4f);
        foreach (var (min, max) in blocks) t.Box(min, max);
        var level = new LevelSpace("floor", t.Build("floor").Collision) { Places = LevelPlaces.None };
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        StartStopSprings.Register(b, new StartStopSprings { Default = 0.2f });
        StartStopSprings.RegisterPlayer(b);
        b.Animation(assets, starts: new PersonStartSettings(), air: new PersonAirSettings(), over: over, ledge: ledge);
        BodyMotionSystem.Register(b);
        var w = b.Build();
        var e = MovementServices.SpawnControlled(w, at + new Vector3(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        w.Remove<Controlled>(e);
        w.Set(e, CharacterAnimation.For(assets, kind));
        w.Set(e, MotionInput.Standing);
        if (w.TryStore<PersonStep>(out _)) w.Set(e, new PersonStep { Curve = 1 });
        if (w.TryStore<PersonAir>(out _)) w.Set(e, new PersonAir { Curve = 1 });
        if (w.TryStore<PersonOver>(out _)) w.Set(e, new PersonOver());
        if (w.TryStore<PersonLedge>(out _)) w.Set(e, new PersonLedge());
        return (w, e);
    }

    /// <summary>What is asked on a tick: the wish (x ahead, y to the left of +X), the gait, jump pressed, jump held, crouch.</summary>
    internal readonly record struct Ask(Vector2 Wish, Gait Gait = Gait.Walk, bool Jump = false, bool Held = false, bool Crouch = false);

    /// <summary>A tick as it went: the clip and its time, the fade, the move, the body, the ledge's clip, and as drawn (world) the pelvis, both knuckles and both balls.</summary>
    internal sealed record Row(string Clip, float Time, string Previous, float Blend, TraversalKind Move, BodyState State, Vector3 Feet, Vector3 Velocity, string Ledge,
                               int Braced, Vector3 Pelvis, Vector3 KnuckleL, Vector3 KnuckleR, Vector3 BallL, Vector3 BallR, int Shimmy);

    /// <summary><paramref name="ticks"/> ticks asked by <paramref name="plan"/> (the tick, the body), after 20 standing; every tick's row.</summary>
    internal static List<Row> Drive(World w, EntityId e, CharacterAssets assets, int kind, Func<int, Body, Ask> plan, int ticks)
    {
        var rows = new List<Row>(ticks);
        var rig = assets.Kind(kind).Person!.Rig;
        var paths = assets.Kind(kind).Person!.Ledge?.Paths;
        for (int i = 0; i < 20; i++) { Set(w, e, new Ask(Vector2.Zero)); w.Step(); }
        for (int i = 0; i < ticks; i++)
        {
            Set(w, e, plan(i, w.Get<Body>(e)));
            w.Step();
            var body = w.Get<Body>(e);
            var a = w.Get<CharacterAnimation>(e);
            var ledge = w.TryStore<PersonLedge>(out var ls) && ls.Has(e) ? ls.Get(e).Move : default;
            string clip = ledge.Clip > 0 && paths?.At(ledge.Clip - 1) is { } c ? c.Name : "-";
            rows.Add(new Row(assets.Clip(a.Clip).Name, a.Time, a.PreviousClip >= 0 ? assets.Clip(a.PreviousClip).Name : "-", a.PreviousClip >= 0 ? a.Blend : 1f,
                             w.Get<Traversal>(e).Kind, body.State, body.Feet, body.Velocity, clip, ledge.Braced, Bone(w, e, assets, rig.Pelvis),
                             Bone(w, e, assets, rig.LeftArm.Tip), Bone(w, e, assets, rig.RightArm.Tip), Bone(w, e, assets, rig.LeftLeg.Tip),
                             Bone(w, e, assets, rig.RightLeg.Tip), body.Shimmy));
        }
        return rows;
    }

    static void Set(World w, EntityId e, Ask ask)
    {
        ref var intent = ref w.Get<MoveIntent>(e);
        intent.Direction = ask.Wish;
        intent.Gait = ask.Gait;
        intent.Faces = false;
        intent.Jump = ask.Jump;
        intent.JumpHeld = ask.Jump || ask.Held;
        intent.Crouch = ask.Crouch;
    }

    internal void Say(string what, List<Row> rows, int from, int to)
    {
        output.WriteLine(what);
        for (int i = Math.Max(0, from); i < Math.Min(rows.Count, to); i++)
        {
            var r = rows[i];
            output.WriteLine($"  {i,4} {r.Clip,-20} {F(r.Time, "0.000")} {(r.Previous != "-" ? $"<{r.Previous} {F(r.Blend)}" : ""),-24} {r.Move,-6} {r.State,-10} {r.Ledge,-20} "
                             + $"feet {F(r.Feet.X, "0.000")},{F(r.Feet.Y, "0.000")},{F(r.Feet.Z, "0.000")} pelvis {F(r.Pelvis.X, "0.000")},{F(r.Pelvis.Z, "0.000")} "
                             + $"knuckles {F(r.KnuckleL.X, "0.000")},{F(r.KnuckleL.Z, "0.000")} balls {F(r.BallL.X, "0.000")},{F(r.BallL.Z, "0.000")} {F(r.BallR.X, "0.000")},{F(r.BallR.Z, "0.000")}");
        }
    }

    /// <summary>The largest change a tick of the pelvis's change a tick (m: a pop) from <paramref name="from"/> to <paramref name="to"/>.</summary>
    internal static float Pop(List<Row> rows, int from, int to)
    {
        float pop = 0f;
        for (int i = Math.Max(2, from); i < Math.Min(rows.Count, to); i++)
            pop = MathF.Max(pop, ((rows[i].Pelvis - rows[i - 1].Pelvis) - (rows[i - 1].Pelvis - rows[i - 2].Pelvis)).Length());
        return pop;
    }

    /// <summary>Both bodies (the player's man and the woman), each where the set has the ledge.</summary>
    static IEnumerable<(string Who, CharacterAssets Assets, int Kind)> Bodies()
    {
        var (pa, pk, _) = Player();
        yield return ("man", pa, pk);
        var (wa, wk, _) = Woman();
        yield return ("woman", wa, wk);
    }

    /// <summary>A wall across the way at x 1 (its top <paramref name="h"/>, 2 m deep, 6 m wide: braced), or with <paramref name="free"/> a slab 0.3 m thick over open ground (free).</summary>
    static (Vector3 Min, Vector3 Max) Wall(float h, bool free = false, float y0 = -3f, float y1 = 3f) =>
        (new Vector3(1f, y0, free ? h - 0.3f : 0f), new Vector3(3f, y1, h));

    /// <summary>The knuckles (both hands' mean) against the edge at x 1, top <paramref name="h"/>: forward of the face (into the wall positive) and above the top.</summary>
    static Vector2 KnucklesAt(Row r, float h) => new(0.5f * (r.KnuckleL.X + r.KnuckleR.X) - 1f, 0.5f * (r.KnuckleL.Z + r.KnuckleR.Z) - h);

    /// <summary>A standing grab: the body 0.55 m short of the face facing it, jump tapped on the first tick; then <paramref name="then"/> from tick 0 of the hang.</summary>
    List<Row> StandAndGrab(CharacterAssets assets, int kind, float h, bool free, int ticks, Func<int, Body, Ask>? then = null, PersonLedgeSettings? ledge = null,
                           params (Vector3 Min, Vector3 Max)[] more)
    {
        var (w, e) = Make(assets, kind, ledge ?? new PersonLedgeSettings(), new Vector3(0.45f, 0f, 0f), [Wall(h, free), .. more]);
        int hung = -1;
        var rows = Drive(w, e, assets, kind, (i, b) =>
        {
            if (hung < 0 && b.State == BodyState.Hanging) hung = i;
            return hung < 0 ? new Ask(Vector2.Zero, Jump: i == 0) : then?.Invoke(i - hung, b) ?? new Ask(Vector2.Zero);
        }, ticks);
        Assert.True(Finite(w, e));
        return rows;
    }

    [Fact]
    public void The_notes_are_read_for_both_bodies()
    {
        foreach (var (who, assets, kind) in Bodies())
        {
            var ledge = Ledge(assets, kind);
            var paths = ledge.Paths;
            float scale = assets.Kind(kind).Scale;
            var roles = new Dictionary<string, (LedgeRole Role, bool Braced)>
            {
                ["hang_free"] = (LedgeRole.Hang, false), ["hang_braced"] = (LedgeRole.Hang, true), ["hang_brace_on"] = (LedgeRole.BraceOn, true),
                ["hang_brace_off"] = (LedgeRole.BraceOff, false), ["shimmy_left_free"] = (LedgeRole.Shimmy, false), ["shimmy_right_free"] = (LedgeRole.Shimmy, false),
                ["shimmy_left_braced"] = (LedgeRole.Shimmy, true), ["shimmy_right_braced"] = (LedgeRole.Shimmy, true), ["pull_up_free"] = (LedgeRole.PullUp, false),
                ["pull_up_braced"] = (LedgeRole.PullUp, true), ["drop_free"] = (LedgeRole.Drop, false), ["drop_braced"] = (LedgeRole.Drop, true),
                ["grab_jump_up"] = (LedgeRole.GrabStand, false), ["grab_run"] = (LedgeRole.GrabRun, true), ["grab_fall"] = (LedgeRole.GrabFall, false),
            };
            // The braced twins of the standing and the falling grab, when the set has them.
            if (paths.Clips.Any(c => c.Twin)) { roles["grab_jump_up_braced"] = (LedgeRole.GrabStand, true); roles["grab_fall_braced"] = (LedgeRole.GrabFall, true); }
            // The leaps and the corners, when the set has them, after every clip before them.
            if (paths.HasHangMoves)
            {
                foreach (var l in new[] { "left", "right", "up", "down", "back" }) roles["hang_leap_" + l] = (LedgeRole.Leap, true);
                foreach (var k in new[] { "out", "in" }) foreach (var b in new[] { "braced", "free" }) foreach (var w in new[] { "left", "right" })
                    roles[$"hang_corner_{k}_{b}_{w}"] = (LedgeRole.Corner, b == "braced");
                int first = Array.FindIndex(paths.Clips, c => c.Role is LedgeRole.Leap or LedgeRole.Corner);
                Assert.All(paths.Clips.Skip(first).Where(c => !c.OnWall && !c.OnSide), c => Assert.True(c.Role is LedgeRole.Leap or LedgeRole.Corner, c.Name));
            }
            // The walls' clips, when the set has them, after every other.
            if (paths.HasWalls)
            {
                int wall = Array.FindIndex(paths.Clips, c => c.OnWall);
                // The squeeze's and the cover's clips after the walls' (a tape's LedgeMove.Clip keeps its meaning).
                int side = Array.FindIndex(paths.Clips, c => c.OnSide);
                Assert.All(paths.Clips.Skip(wall).Take(side < 0 ? int.MaxValue : side - wall), c => Assert.True(c.OnWall, c.Name));
                if (side >= 0)
                {
                    Assert.All(paths.Clips.Skip(side), c => Assert.True(c.OnSide, c.Name));
                    Assert.True(paths.HasSqueeze && paths.HasCover);
                    Assert.Equal(10, paths.Clips.Count(c => c.OnSide));   // the squeeze's three and the cover's five, and the two quarter turns
                }
                var walls = new Dictionary<string, LedgeRole>
                {
                    ["wall_idle"] = LedgeRole.WallIdle, ["wall_on_ground"] = LedgeRole.WallOn, ["wall_on_jump"] = LedgeRole.WallOn, ["wall_off_ground"] = LedgeRole.WallOff,
                    ["wall_let_go"] = LedgeRole.WallDrop, ["wall_push_off"] = LedgeRole.WallDrop, ["wall_to_hang_braced"] = LedgeRole.WallToHang, ["wall_grip_splay"] = LedgeRole.WallGrip,
                };
                foreach (var d in new[] { "up", "down", "left", "right" })
                {
                    walls[$"wall_{d}"] = LedgeRole.WallMove; walls[$"wall_{d}_quick"] = LedgeRole.WallMove; walls[$"wall_long_{d}"] = LedgeRole.WallMove;
                }
                foreach (var k in new[] { "out", "in" }) foreach (var w in new[] { "left", "right" }) walls[$"wall_corner_{k}_{w}"] = LedgeRole.WallCorner;
                Assert.Equal(walls.Count, paths.Clips.Count(c => c.OnWall));
                foreach (var c in paths.Clips.Skip(wall).Where(c => c.OnWall)) Assert.Equal(walls[c.Name], c.Role);
                Assert.All(paths.Clips.Where(c => c.Role == LedgeRole.WallMove), c => Assert.Equal(c.Name.EndsWith("_quick", StringComparison.Ordinal), c.Quick && !c.Long));
                Assert.True(paths.WallOn(jump: true)!.Name == "wall_on_jump" && paths.WallDrop(push: true)!.Name == "wall_push_off" && paths.WallDrop(push: false)!.Name == "wall_let_go");
            }
            Assert.Equal(roles.Count, paths.Clips.Count(c => !c.OnWall && !c.OnSide));
            foreach (var c in paths.Clips.Where(c => !c.OnWall && !c.OnSide))
            {
                var notes = Notes(assets, c.Clip);
                var tv = notes.Traverse!;
                var hang = tv.Hang!;
                Assert.Equal(roles[c.Name], (c.Role, c.Braced));
                // Every number from the notes, at the body's scale: the hang's place, the path a row a frame relative to it.
                Assert.Equal(hang.EdgeFwd * scale, c.EdgeFwd, 5);
                Assert.Equal(hang.EdgeUp * scale, c.EdgeUp, 5);
                Assert.Equal("knuckles", hang.HandsAt);
                Assert.Equal(tv.Times.Length, c.T.Length);
                Assert.InRange(c.T[^1], c.Duration - 0.02f, c.Duration + 0.02f);
                for (int k = 0; k < c.T.Length; k++) { Assert.Equal(tv.Forward[k] * scale, c.F[k], 5); Assert.Equal(tv.Up[k] * scale, c.U[k], 5); }
                if (c.Role != LedgeRole.Leap) Assert.Equal(c.Role is LedgeRole.Shimmy or LedgeRole.Corner, c.S.Length == c.T.Length);   // a side leap's and the leap back's have it too
                Assert.Equal(c.Loops, c.Role is LedgeRole.Hang or LedgeRole.Shimmy);
                if (c.Role == LedgeRole.Shimmy)
                {
                    // The fourth number: the sideways move, positive to the left, its whole step a cycle; the speed the notes'.
                    Assert.Equal(tv.Shimmy!.Left ? 1 : -1, c.Way);
                    Assert.Equal(tv.Shimmy.StepM * scale * c.Way, c.S[^1], 3);
                    Assert.Equal(0f, c.S[0], 3);
                    Assert.InRange(c.Step / c.Duration, c.Speed - 0.01f, c.Speed + 0.01f);
                    Assert.Equal(c.Braced ? "hang_braced" : "hang_free", tv.LoopsFrom);
                    output.WriteLine($"{who} {c.Name}: {F(c.Step)} m a cycle of {F(c.Duration)} s ({F(c.Step / c.Duration, "0.000")} m/s; the notes' {F(c.Speed, "0.000")})");
                }
                if (c.Role is LedgeRole.GrabStand or LedgeRole.GrabRun or LedgeRole.GrabFall)
                {
                    var g = tv.Grab!;
                    Assert.Equal(g.ContactS, c.ContactS, 4);
                    if (c.Role != LedgeRole.GrabFall)
                    {
                        Assert.Equal(g.Low * scale, c.Low, 4);
                        Assert.Equal(g.High * scale, c.High, 4);
                        Assert.Equal(g.TakeoffS, c.TakeoffS, 4);
                        Assert.True(c.OwnHeight >= c.Low - 0.1f && c.OwnHeight <= c.High + 0.1f, $"{c.Name}: its own edge about its band");
                    }
                    else
                    {
                        Assert.Equal(g.StartsOverHang * scale, c.StartsOver, 4);
                        Assert.Equal(g.StartsOffHang * scale, c.StartsOff, 4);
                        Assert.InRange(c.U[0] - c.StartsOver, -0.005f, 0.005f);   // its first row is where it starts over the hang's place
                    }
                    if (c.Role == LedgeRole.GrabRun) { Assert.Equal(g.EnterPhase, c.EnterPhase, 4); Assert.Equal(g.SpeedMs * scale, c.RunSpeed, 4); }
                    output.WriteLine($"{who} {c.Name}: band {F(c.Low)}-{F(c.High)} m (its own {F(c.OwnHeight, "0.000")}), its face {F(c.OwnFace, "0.000")} m ahead, "
                                     + $"take-off {F(c.TakeoffS, "0.000")} s, contact {F(c.ContactS, "0.000")} s, ends {(c.Braced ? "braced" : "free")}");
                }
            }
            Assert.Equal(paths.Of(LedgeRole.Hang, false)!.EdgeFwd, paths.EdgeFwd);
            Assert.Equal(2, paths.BracedFeet.Length);
            Assert.Equal(2, ledge.Knuckles.Length);
            var hands = Notes(assets, paths.Of(LedgeRole.Hang, false)!.Clip).Traverse!.Hang!;
            var free = paths.Of(LedgeRole.Hang, false)!;
            float knF = 0.5f * (ledge.Knuckles[0].X + ledge.Knuckles[1].X) + free.F[0] - paths.EdgeFwd, knU = 0.5f * (ledge.Knuckles[0].Z + ledge.Knuckles[1].Z) + free.U[0] - paths.EdgeUp;
            output.WriteLine($"{who}: the hang {F(paths.EdgeFwd, "0.0000")} m back from the edge, {F(paths.EdgeUp, "0.0000")} m under its top; its first frame's knuckles "
                             + $"{F(knF * 100f, "0.0")} cm forward of the edge and {F(knU * 100f, "0.0")} cm over it (the notes' hands {F(hands.HandFwd * 100f, "0.0")}, {F(hands.HandUp * 100f, "0.0")}), "
                             + $"{F(MathF.Abs(ledge.Knuckles[0].Y - ledge.Knuckles[1].Y) * 100f, "0.0")} cm apart ({F(hands.HandsApart * 100f, "0.0")}); the braced hang's balls "
                             + string.Join(", ", paths.BracedFeet.Select(f => $"{F(f.X, "0.000")} ahead {F(f.Y, "0.000")} left {F(f.Z, "0.000")} up")));
            // The notes' point of the knuckles against the middle finger's first joint (the rig's tip): the same within a few centimetres (the woman's 2.3 cm
            // nearer the edge than the notes say: theirs a point of the hand's back, ours the joint).
            Assert.InRange(knF - hands.HandFwd, -0.03f, 0.03f);
            Assert.InRange(knU - hands.HandUp, -0.015f, 0.015f);
        }
        // The switch through a tape's note; none is the earlier build's.
        Assert.Equal("clips=1", new PersonLedgeSettings().Note());
        Assert.True(new PersonLedgeSettings().WithNote("clips=1").Clips);
        Assert.Equal(PersonLedgeSettings.Off, new PersonLedgeSettings().WithNote(null));
        Assert.Equal(PersonLedgeSettings.Off, new PersonLedgeSettings().WithNote("off"));
        Assert.False(PersonLedgeSettings.Off.Any);
    }

    /// <summary>The first row from <paramref name="from"/> playing <paramref name="clip"/> (the ledge's), or −1.</summary>
    static int First(List<Row> rows, string clip, int from = 0) => rows.FindIndex(Math.Max(0, from), r => r.Ledge == clip);

    /// <summary>The row after the last of a run of <paramref name="clip"/> begun at <paramref name="start"/>.</summary>
    static int After(List<Row> rows, string clip, int start) { int k = start; while (k < rows.Count && rows[k].Ledge == clip) k++; return k; }

    [Fact]
    public void The_hang_is_placed_from_the_real_edge_braced_on_a_wall_and_free_under_a_slab()
    {
        foreach (var (who, assets, kind) in Bodies())
        {
            var paths = Ledge(assets, kind).Paths;
            float h = MathF.Round(paths.Of(LedgeRole.GrabStand)!.OwnHeight, 2);
            foreach (bool free in new[] { false, true })
            {
                var rows = StandAndGrab(assets, kind, h, free, 1200);
                int hung = rows.FindIndex(r => r.State == BodyState.Hanging);
                Assert.True(hung > 0, "it hangs");
                var r = rows[^1];
                Say($"{who} {(free ? "free" : "braced")} at {F(h)} m", rows, hung - 2, hung + 3);
                var hang = paths.Of(LedgeRole.Hang, !free)!;
                Assert.Equal(hang.Name, r.Ledge);
                Assert.Equal(free ? -1 : hung, First(rows, "hang_brace_on"));   // the grab ends free, the wall brings the brace on at once
                Assert.Equal(-1, First(rows, "hang_brace_off"));
                // The capsule where the notes put it: the place (edge_fwd back from the edge, edge_up under its top) and the clip's own row now.
                var (pf, pu, _) = hang.At(r.Time);
                Assert.Equal(1f - paths.EdgeFwd + pf, r.Feet.X, 3);
                Assert.Equal(h - paths.EdgeUp + pu, r.Feet.Z, 3);
                // Over the hang's loop, from where it began: the knuckles against the edge, the balls against the wall.
                int loop = First(rows, hang.Name);
                var looped = rows.Skip(loop).ToList();
                var knuckles = KnucklesAt(rows[loop], h);
                float kxLo = looped.Min(q => KnucklesAt(q, h).X), kxHi = looped.Max(q => KnucklesAt(q, h).X);
                float kzLo = looped.Min(q => KnucklesAt(q, h).Y), kzHi = looped.Max(q => KnucklesAt(q, h).Y);
                float wallL = rows[loop].BallL.X - 1f, wallR = rows[loop].BallR.X - 1f;
                float ballsLo = looped.Min(q => MathF.Min(q.BallL.X, q.BallR.X)) - 1f, ballsHi = looped.Max(q => MathF.Max(q.BallL.X, q.BallR.X)) - 1f;
                output.WriteLine($"  over its loop ({F((rows.Count - loop) * Tick)} s): the knuckles {F(kxLo * 100f, "0.0")} to {F(kxHi * 100f, "0.0")} cm forward, {F(kzLo * 100f, "0.0")} to "
                                 + $"{F(kzHi * 100f, "0.0")} cm over" + (free ? "" : $"; the balls {F(ballsLo * 100f, "0.0")} to {F(ballsHi * 100f, "0.0")} cm from the wall"));
                output.WriteLine($"{who} hanging {(free ? "free under a slab" : "braced on a wall")} at {F(h)} m: the capsule {F(paths.EdgeFwd - pf, "0.000")} m back from the edge, "
                                 + $"{F(paths.EdgeUp - pu, "0.000")} m under its top; the knuckles {F(knuckles.X * 100f, "0.0")} cm forward of the edge, {F(knuckles.Y * 100f, "0.0")} cm over it"
                                 + (free ? "" : $"; the balls {F(wallL * 100f, "0.0")} and {F(wallR * 100f, "0.0")} cm from the wall"));
                Assert.InRange(knuckles.X, -0.06f, 0f);
                Assert.InRange(knuckles.Y, -0.01f, 0.04f);
                Assert.InRange(kxLo, -0.08f, 0f);
                Assert.InRange(kzLo, -0.01f, 0.04f);   // the woman's braced hang lowers them 0.4 cm under the top at times (out from the face)
                Assert.InRange(kzHi, -0.02f, 0.04f);
                if (!free) { Assert.InRange(wallL, -0.08f, 0.02f); Assert.InRange(wallR, -0.08f, 0.02f); Assert.InRange(ballsHi, -0.1f, 0.03f); }
                Assert.True(WallUnderOf(rows, paths, h) == !free);
            }
        }
    }

    /// <summary>Whether the motor finds a wall under the edge at x 1 for the last row's hang (the clip's place).</summary>
    static bool WallUnderOf(List<Row> rows, LedgePaths paths, float h) =>
        rows[^1].Ledge.Contains("braced", StringComparison.Ordinal);

    /// <summary>A grab made against an edge at x 1, top <paramref name="h"/> (a wall): the rows, the clip, its first tick and its contact's, the knuckles there, the pops.</summary>
    sealed record Grabbed(List<Row> Rows, string Clip, int Start, int Contact, int End, Vector2 Knuckles, float PopIn, float PopOut);

    Grabbed Grab(CharacterAssets assets, int kind, LedgeRole role, float h)
    {
        var paths = Ledge(assets, kind).Paths;
        var c = paths.Of(role)!;
        (World w, EntityId e) made;
        Func<int, Body, Ask> plan;
        int ticks = 360;
        switch (role)
        {
            case LedgeRole.GrabStand:
                made = Make(assets, kind, new PersonLedgeSettings(), new Vector3(0.45f, 0f, 0f), Wall(h));
                plan = (i, b) => new Ask(Vector2.Zero, Jump: i == 0);
                break;
            case LedgeRole.GrabRun:
                // Run at it from 9 m short, jump pressed 3.1 m short of the face.
                made = Make(assets, kind, new PersonLedgeSettings(), new Vector3(-8f, 0f, 0f), Wall(h));
                bool pressed = false;
                plan = (i, b) => { bool press = !pressed && b.Feet.X >= 1f - 3.1f; pressed |= press; return new Ask(new Vector2(b.State == BodyState.Ground ? 1f : 0f, 0f), Gait.Run, Jump: press); };
                ticks = 700;
                break;
            default:
                // Falling past it from a metre over where the grab begins, the capsule its own way out from the hang's place, jump held.
                made = Make(assets, kind, new PersonLedgeSettings(), new Vector3(1f - paths.EdgeFwd + c.StartsOff, 0f, h - paths.EdgeUp + c.StartsOver + 1f), Wall(h));
                plan = (i, b) => new Ask(Vector2.Zero, Held: i < 60);
                break;
        }
        var (w, e) = made;
        var rows = Drive(w, e, assets, kind, plan, ticks);
        int start = First(rows, c.Name);
        if (start < 0) Say("no grab", rows, 0, rows.Count);
        Assert.True(start >= 0, $"{c.Name} began");
        int end = After(rows, c.Name, start);
        int contact = rows.FindIndex(start, r => r.Ledge == c.Name && r.Time >= c.ContactS - 0.5f * Tick);
        Assert.True(Finite(w, e));
        return new Grabbed(rows, c.Name, start, contact, end, KnucklesAt(rows[contact], h), Pop(rows, start - 3, start + 4), Pop(rows, end - 3, end + 4));
    }

    [Fact]
    public void The_grabs_from_standing_running_and_falling_meet_the_real_edge_at_their_contact()
    {
        foreach (var (who, assets, kind) in Bodies())
        {
            var paths = Ledge(assets, kind).Paths;
            foreach (var role in new[] { LedgeRole.GrabStand, LedgeRole.GrabRun, LedgeRole.GrabFall })
            {
                var c = paths.Of(role)!;
                float own = role == LedgeRole.GrabFall ? 2.6f : MathF.Round(c.OwnHeight, 2);
                var at = role == LedgeRole.GrabFall ? new[] { own, 3.2f, 4f } : new[] { own, MathF.Round(c.Low + 0.01f, 2), MathF.Round(c.High - 0.01f, 2) };
                Grabbed? first = null;
                foreach (float h in at)
                {
                    var g = Grab(assets, kind, role, h);
                    first ??= g;
                    Say($"{who} {g.Clip} at {F(h)} m", g.Rows, g.Start - 2, g.Start + 2);
                    Say("  its contact", g.Rows, g.Contact - 1, g.Contact + 1);
                    float dx = g.Knuckles.X - first.Knuckles.X, dz = g.Knuckles.Y - first.Knuckles.Y;
                    output.WriteLine($"{who} {g.Clip} at {F(h)} m: the knuckles at its contact ({F(c.ContactS, "0.000")} s) {F(g.Knuckles.X * 100f, "0.0")} cm forward of the edge, "
                                     + $"{F(g.Knuckles.Y * 100f, "0.0")} cm over it (off its own height's by {F(dx * 100f, "0.0")}, {F(dz * 100f, "0.0")} cm); {F((g.End - g.Start) * Tick)} s "
                                     + $"to the hang ({g.Rows[g.End].Ledge}); the pelvis's pop in {F(g.PopIn * 1000f, "0.0")} mm, out {F(g.PopOut * 1000f, "0.0")} mm");
                    if (role != LedgeRole.GrabFall)
                    {
                        // On the ground to its take-off: the feet on the floor (none under it), from the stand or the run.
                        int off = g.Start + (int)(c.TakeoffS / Tick);
                        float under = g.Rows.Skip(g.Start).Take(off - g.Start).Min(r => MathF.Min(r.BallL.Z, r.BallR.Z));
                        output.WriteLine($"  the balls' lowest before its take-off {F(under * 100f, "0.0")} cm (from {g.Rows[g.Start].Previous})");
                        Assert.InRange(under, -0.01f, 0.05f);
                    }
                    Assert.InRange(MathF.Abs(dx), 0f, 0.015f);
                    Assert.InRange(MathF.Abs(dz), 0f, 0.015f);
                    Assert.InRange(g.Knuckles.X, -0.08f, 0.02f);
                    Assert.InRange(g.Knuckles.Y, -0.03f, 0.08f);
                    Assert.InRange(g.PopOut, 0f, 0.01f);
                    Assert.StartsWith(c.Braced ? "hang_braced" : "hang_brace_on", g.Rows[g.End].Ledge);   // on a wall: braced (the free ones brace on at once)
                }
            }
        }
    }

    [Fact]
    public void A_shimmy_goes_by_whole_cycles_at_the_clips_speed_and_none_past_the_lip_s_end()
    {
        foreach (var (who, assets, kind) in Bodies())
        {
            var paths = Ledge(assets, kind).Paths;
            float h = MathF.Round(paths.Of(LedgeRole.GrabStand)!.OwnHeight, 2);
            foreach (bool free in new[] { false, true })
                foreach (int way in new[] { 1, -1 })
                {
                    var c = paths.Of(LedgeRole.Shimmy, !free, way)!;
                    // Settled in the hang, the wish to the side for 1.6 s (let go mid-cycle), still, then held that way for 10 s (to the lip's end, 3 m off).
                    var rows = StandAndGrab(assets, kind, h, free, 2400, (i, b) => i < 240 ? new Ask(Vector2.Zero)
                        : i < 432 ? new Ask(new Vector2(0f, way)) : i < 700 ? new Ask(Vector2.Zero) : new Ask(new Vector2(0f, way)));
                    int hung = rows.FindIndex(r => r.State == BodyState.Hanging);
                    int go = hung + 240, let = hung + 432;
                    int start = First(rows, c.Name, go);
                    Assert.True(start >= go && start <= go + 2, "the cycle began with the wish");
                    int end = After(rows, c.Name, start);
                    float y0 = rows[start - 1].Feet.Y;
                    float moved = rows[end].Feet.Y - y0;
                    int cycles = (int)MathF.Round(moved / (c.Step * way));
                    Say($"{who} {c.Name}: its last cycle's end", rows, end - 2, end + 2);
                    output.WriteLine($"{who} {c.Name}: the wish held {F((let - go) * Tick)} s, let go {F((end - let) * Tick)} s before it stopped: {cycles} whole cycles, "
                                     + $"{F(moved, "0.000")} m in {F((end - start) * Tick)} s ({F(MathF.Abs(moved) / ((end - start) * Tick), "0.000")} m/s; the notes' {F(c.Speed, "0.000")}); "
                                     + $"the pelvis's pop at the start {F(Pop(rows, start - 3, start + 4) * 1000f, "0.0")} mm, between cycles "
                                     + $"{F(Pop(rows, start + (int)(c.Duration / Tick) - 3, start + (int)(c.Duration / Tick) + 4) * 1000f, "0.0")} mm, at the end {F(Pop(rows, end - 3, end + 4) * 1000f, "0.0")} mm");
                    Assert.True(end > let, "the cycle under way when the wish was let go finished");
                    Assert.Equal(2, cycles);
                    Assert.Equal(cycles * c.Step * way, moved, 2);
                    Assert.InRange(MathF.Abs(moved) / ((end - start) * Tick), c.Speed - 0.01f, c.Speed + 0.01f);
                    Assert.InRange(Pop(rows, start - 3, end + 4), 0f, 0.01f);
                    // Held to the lip's end (3 m off): it stops where the next cycle's leading hand would pass the end, on a whole cycle.
                    var last = rows[^1];
                    float reach = MathF.Abs(last.Feet.Y) + c.Step + 0.5f * c.HandsApart;
                    output.WriteLine($"{who} {c.Name}: held to the lip's end, stopped {F(3f - MathF.Abs(last.Feet.Y), "0.000")} m short of it ({F((MathF.Abs(last.Feet.Y) - MathF.Abs(y0)) / c.Step, "0.00")} cycles from the start)");
                    Assert.Equal(c.Name.Contains("braced") ? "hang_braced" : "hang_free", last.Ledge);
                    Assert.InRange(reach, 3f, 3f + c.Step + 1e-3f);
                    Assert.InRange(MathF.Abs(MathF.Abs(last.Feet.Y - y0) / c.Step - MathF.Round(MathF.Abs(last.Feet.Y - y0) / c.Step)), 0f, 0.01f);
                }
        }
    }

    [Fact]
    public void The_hang_braces_on_and_off_as_the_wall_under_the_edge_comes_and_goes_along_a_shimmy()
    {
        var (assets, kind, _) = Player();
        var paths = Ledge(assets, kind).Paths;
        float h = MathF.Round(paths.Of(LedgeRole.GrabStand)!.OwnHeight, 2);
        // A slab from y −3 to 3 over open ground, a wall under it only to y 0.6: hang at y 0 braced, shimmy left (+y) past the wall's end and back.
        var (w, e) = Make(assets, kind, new PersonLedgeSettings(), new Vector3(0.45f, 0f, 0f), Wall(h, free: true), (new Vector3(1f, -3f, 0f), new Vector3(3f, 0.6f, h - 0.3f)));
        int hung = -1;
        var rows = Drive(w, e, assets, kind, (i, b) =>
        {
            if (hung < 0 && b.State == BodyState.Hanging) hung = i;
            return hung < 0 ? new Ask(Vector2.Zero, Jump: i == 0) : i - hung < 200 ? new Ask(Vector2.Zero) : i - hung < 900 ? new Ask(new Vector2(0f, 1f))
                 : i - hung < 1000 ? new Ask(Vector2.Zero) : i - hung < 1700 ? new Ask(new Vector2(0f, -1f)) : new Ask(Vector2.Zero);
        }, 2000);
        var order = new List<string>();
        foreach (var r in rows) if (r.Ledge != "-" && (order.Count == 0 || order[^1] != r.Ledge)) order.Add(r.Ledge);
        output.WriteLine(string.Join(" > ", order));
        int off = First(rows, "hang_brace_off"), on = First(rows, "hang_brace_on", off);
        Assert.True(off > 0 && on > off, "braced off past the wall and on again over it");
        Say("brace off", rows, off - 2, off + 2);
        Say("brace on", rows, on - 2, on + 2);
        output.WriteLine($"brace off at y {F(rows[off].Feet.Y, "0.000")} (the wall ends at 0.6), on again at y {F(rows[on].Feet.Y, "0.000")}; "
                         + $"pops {F(Pop(rows, off - 3, After(rows, "hang_brace_off", off) + 4) * 1000f, "0.0")} mm, {F(Pop(rows, on - 3, After(rows, "hang_brace_on", on) + 4) * 1000f, "0.0")} mm");
        Assert.Contains("shimmy_left_free", order);
        Assert.Contains("shimmy_right_free", order);
        Assert.InRange(Pop(rows, off - 3, After(rows, "hang_brace_off", off) + 4), 0f, 0.01f);
        Assert.InRange(Pop(rows, on - 3, After(rows, "hang_brace_on", on) + 4), 0f, 0.01f);
    }

    [Fact]
    public void The_pull_up_goes_along_its_path_and_ends_standing_on_the_top()
    {
        foreach (var (who, assets, kind) in Bodies())
        {
            var paths = Ledge(assets, kind).Paths;
            float h = MathF.Round(paths.Of(LedgeRole.GrabStand)!.OwnHeight, 2);
            foreach (bool free in new[] { false, true })
            {
                var c = paths.Of(LedgeRole.PullUp, !free)!;
                var rows = StandAndGrab(assets, kind, h, free, 900, (i, b) => new Ask(Vector2.Zero, Jump: i == 300));
                int start = First(rows, c.Name);
                Assert.True(start > 0, $"{c.Name} began");
                int end = After(rows, c.Name, start);
                var r = rows[^1];
                Say($"{who} {c.Name}", rows, end - 2, end + 3);
                output.WriteLine($"{who} {c.Name} at {F(h)} m: {F((end - start) * Tick)} s, standing {F(r.Feet.X - 1f, "0.000")} m in from the edge, the feet {F((r.Feet.Z - h) * 100f, "0.0")} cm "
                                 + $"over the top, the balls {F((MathF.Min(r.BallL.Z, r.BallR.Z) - h) * 100f, "0.0")} cm; the pelvis's pop in {F(Pop(rows, start - 3, start + 4) * 1000f, "0.0")} mm, "
                                 + $"out {F(Pop(rows, end - 3, end + 4) * 1000f, "0.0")} mm");
                Assert.Equal(BodyState.Ground, rows[end + 1].State);
                Assert.InRange(r.Feet.Z - h, -0.005f, 0.01f);
                Assert.InRange((end - start) * Tick, c.Duration - 2f * Tick, c.Duration + 2f * Tick);
                Assert.InRange(Pop(rows, start - 3, start + 4), 0f, 0.01f);
                Assert.InRange(Pop(rows, end - 3, end + 4), 0f, 0.015f);
            }
        }
    }

    [Fact]
    public void The_drop_falls_from_its_path_and_lands_by_the_drop_s_band()
    {
        var (assets, kind, _) = Player();
        var paths = Ledge(assets, kind).Paths;
        var air = assets.Kind(kind).Person!.Air!;
        foreach (float h in new[] { 2.6f, 4.2f })
        {
            // Falling past a wall of h (jump held) to hang, then crouch pressed.
            var c = paths.Of(LedgeRole.GrabFall)!;
            var (w, e) = Make(assets, kind, new PersonLedgeSettings(), new Vector3(1f - paths.EdgeFwd + c.StartsOff, 0f, h - paths.EdgeUp + c.StartsOver + 0.3f), Wall(h));
            int hung = -1;
            var rows = Drive(w, e, assets, kind, (i, b) =>
            {
                if (hung < 0 && b.State == BodyState.Hanging) hung = i;
                return new Ask(Vector2.Zero, Held: i < 30, Crouch: hung >= 0 && i - hung >= 300 && i - hung < 302);
            }, 900);
            var drop = paths.Of(LedgeRole.Drop, true)!;
            int start = First(rows, drop.Name);
            Assert.True(start > 0, "the drop began");
            int end = After(rows, drop.Name, start);
            int landed = rows.FindIndex(end, r => r.State == BodyState.Ground);
            float fell = rows[start - 1].Feet.Z - rows[landed].Feet.Z;
            string land = rows[landed].Clip;
            var ln = Notes(assets, land is var name ? Enumerable.Range(0, assets.ClipCount).First(k => assets.Clip(k).Name == name) : 0).Landing!;
            Say($"the drop from {F(h)} m", rows, start - 2, start + 3);
            Say("its fall and landing", rows, end - 2, landed + 3);
            output.WriteLine($"a drop from a hang at {F(h)} m: the hang's feet {F(rows[start - 1].Feet.Z, "0.000")} m up, {drop.Name} for {F((end - start) * Tick)} s then the fall "
                             + $"({rows[end + 1].Clip}), landed after {F((landed - start) * Tick)} s on {land} (its band {F(ln.DropLow)}-{F(ln.DropHigh)} m, the fall {F(fell, "0.00")} m); "
                             + $"the pelvis's pop at the drop {F(Pop(rows, start - 3, start + 4) * 1000f, "0.0")} mm, at its end {F(Pop(rows, end - 3, end + 4) * 1000f, "0.0")} mm");
            Assert.Equal(drop.Duration, (end - start) * Tick, 1);
            Assert.True(ln.For(fell), $"{land}'s band holds {fell}");
            Assert.True(air.Stands(Enumerable.Range(0, assets.ClipCount).First(k => assets.Clip(k).Name == land)), "a standing landing");
            Assert.InRange(Pop(rows, start - 3, start + 4), 0f, 0.01f);
            Assert.InRange(Pop(rows, end - 3, end + 4), 0f, 0.01f);
        }
    }

    [Fact]
    public void The_motor_s_own_grab_and_lowering_end_in_the_clip_s_hang_and_the_push_off_leaves_it_as_before()
    {
        var (assets, kind, _) = Player();
        var paths = Ledge(assets, kind).Paths;
        // A wall of 2.6 m (over the standing grab's band) tapped 1.2 m short: the jump's own grab (the old detection, the hands at the lip), eased to the
        // clip's hang place and hung on its clip; then jump with back held pushes off as before (no clip).
        var (w, e) = Make(assets, kind, new PersonLedgeSettings(), new Vector3(-1f, 0f, 0f), Wall(2.6f));
        int hung = -1;
        var rows = Drive(w, e, assets, kind, (i, b) =>
        {
            if (hung < 0 && b.State == BodyState.Hanging) hung = i;
            // Walking at it until the grab begins (forward held through its end would pull up at once, as before).
            return hung < 0 ? new Ask(new Vector2(b.State == BodyState.Traversing || b.Feet.Z > 0.2f ? 0f : 1f, 0f), Jump: b.Feet.X >= 0.1f && b.Feet.X < 0.13f)
                 : i - hung == 200 ? new Ask(new Vector2(-1f, 0f), Jump: true) : new Ask(Vector2.Zero);
        }, 600);
        int grab = rows.FindIndex(r => r.Move == TraversalKind.Grab);
        Assert.True(grab > 0 && hung > grab, "grabbed and hung");
        Assert.Equal("-", rows[grab].Ledge);   // the motor's own grab
        Say("the jump's own grab into the clip's hang", rows, hung - 3, hung + 3);
        Assert.StartsWith("hang_", rows[hung + 20].Ledge);
        // On the clip's hang (the brace on: a wall under it), its knuckles on the edge as after the clips' own grabs; faded in from the stand-in.
        Assert.StartsWith("hang_braced", rows[hung + 150].Ledge);
        Assert.NotEqual("-", rows[hung].Previous);
        Assert.InRange(KnucklesAt(rows[hung + 150], 2.6f).X, -0.06f, 0f);
        Assert.InRange(KnucklesAt(rows[hung + 150], 2.6f).Y, -0.01f, 0.04f);
        int push = hung + 200;
        Assert.Equal(TraversalKind.Drop, rows[push].Move);
        Assert.Equal("-", rows[push].Ledge);
        Assert.True(rows[push + 10].Velocity.X < -1f, "pushed off backwards");
        output.WriteLine($"the jump's grab of a 2.6 m lip: {F((hung - grab) * Tick)} s eased to the clip's place ({F(rows[hung].Feet.X, "0.000")}, {F(rows[hung].Feet.Z, "0.000")}), then {rows[hung].Ledge} faded in, {rows[hung + 150].Ledge}; "
                         + $"the push-off at {F(rows[push + 10].Velocity.X)} m/s back");
        // Crouch held walking off a platform of 3.5 m: the lowering (the motor's own) down to the clip's hang place, facing the wall, on its clip.
        (w, e) = Make(assets, kind, new PersonLedgeSettings(), new Vector3(-2f, 0f, 3.5f), (new Vector3(-20f, -3f, 0f), new Vector3(0f, 3f, 3.5f)));
        hung = -1;
        rows = Drive(w, e, assets, kind, (i, b) =>
        {
            if (hung < 0 && b.State == BodyState.Hanging) hung = i;
            return new Ask(new Vector2(hung < 0 ? 1f : 0f, 0f), Crouch: hung < 0);
        }, 500);
        int lower = rows.FindIndex(r => r.Move == TraversalKind.LowerToHang);
        if (lower < 0 || hung < 0) Say("no lowering", rows, 0, 300);
        Assert.True(lower > 0 && hung > lower, "lowered and hung");
        Say("the lowering into the clip's hang", rows, hung - 2, hung + 3);
        Assert.StartsWith("hang_", rows[hung + 20].Ledge);
        Assert.InRange(rows[hung + 150].KnuckleL.X, 0f, 0.06f);   // the edge at x 0, the body hanging on its far side facing −x
        Assert.InRange(KnucklesAt(rows[hung + 150], 3.5f).Y, -0.01f, 0.04f);
        output.WriteLine($"lowering from a 3.5 m edge: {F((hung - lower) * Tick)} s down to the clip's place ({F(rows[hung].Feet.X, "0.000")}, {F(rows[hung].Feet.Z, "0.000")}), then {rows[^1].Ledge}; "
                         + $"the pelvis's pop as it hangs {F(Pop(rows, hung - 3, hung + 4) * 1000f, "0.0")} mm");
    }

    [Fact]
    public void Off_the_switch_the_world_has_no_store_and_hangs_on_the_motor_s_paths()
    {
        var (assets, kind, _) = Player();
        Ledge(assets, kind);
        var (w14, e14) = Make(assets, kind, PersonLedgeSettings.Off, new Vector3(0.45f, 0f, 0f), Wall(2.25f));
        Assert.False(w14.TryStore<PersonLedge>(out _));
        var rows = Drive(w14, e14, assets, kind, (i, b) => new Ask(Vector2.Zero, Jump: i == 0), 300);
        Assert.Contains(rows, r => r.Move == TraversalKind.Hang);
        Assert.DoesNotContain(rows, r => r.Clip.StartsWith("hang_", StringComparison.Ordinal) || r.Clip.StartsWith("grab_", StringComparison.Ordinal));
        var (w, _) = Make(assets, kind, new PersonLedgeSettings(), Vector3.Zero);
        Assert.True(w.TryStore<PersonLedge>(out _));
        output.WriteLine($"the clips off hang at 2.25 m: the feet {F(rows[^1].Feet.Z, "0.000")} m up, {F(1f - rows[^1].Feet.X, "0.000")} m out from the face (the clips': "
                         + $"{F(2.25f - Ledge(assets, kind).Paths.EdgeUp, "0.000")}, {F(Ledge(assets, kind).Paths.EdgeFwd, "0.000")})");
    }

}
