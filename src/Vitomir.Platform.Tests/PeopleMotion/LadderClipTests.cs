// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Formats.L2;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;
using Gait = Vitomir.Platform.Bodies.Gait;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// <b>Ladders on clips</b> (BodyMotor.LadderClips.cs, <see cref="PersonLadderClips"/>): the set by the ladder's
/// lean, on at the foot and the top, the climb by whole cycles at the clips' pace (and the sprint's), over the top and off at the foot, a side landing off and
/// on, the upright ladder to a braced ledge and back, a ladder that fits neither set on the earlier build's stand-in, and the earlier build's behind the switch. Skips without the models
/// stream's files, or.
/// </summary>
public sealed class LadderClipTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    const float Tick = World.TickSeconds;

    /// <summary>The earlier build's rates (the clips' own: the earlier build's ladderClimbRate and ladderMoveRate at 1), for its measures.</summary>
    internal static readonly PersonMovesSettings Moves = new() { LadderClimbRate = 1f, LadderMoveRate = 1f };

    internal static PersonLadderClips Ladders(CharacterAssets assets, int kind)
    {
        var person = assets.Kind(kind).Person!;
        Assert.SkipUnless(person.Ladder is not null && person.Ledge is not null && person.Air is { Prelandings.Length: > 0 },
                          "the models repository's files are too old (no ladder clips)");
        return person.Ladder!;
    }

    /// <summary>The tower's face (x), the ladder's rungs' front 0.2 m out from it (the upright set's), its foot at y 0.</summary>
    internal const float WallX = 2f, Out = 0.2f;

    /// <summary>The upright tower: 3.6 m (rung 12's top, the climb's top position at rung 7, three cycles from the foot).</summary>
    internal const float UprightTop = 3.6f;

    /// <summary>The leaning ladder's floor: rung 12's top along rails 15° off upright (0.3 + 11 × 0.3 cos 15°).</summary>
    internal static readonly float LeaningTop = 0.3f + 11f * 0.3f * MathF.Cos(15f * MathF.PI / 180f);

    /// <summary>
    /// A world with tier 1's moves (<paramref name="moves"/>, none: the earlier build's) and a tower at x 2 to 4.5 (its top a floor <paramref name="top"/> high) with a ladder up
    /// its face leaning <paramref name="lean"/>° (its top's rungs' front 0.2 m out from the face), and <paramref name="blocks"/> (a landing, a ledge).
    /// </summary>
    internal static (World World, EntityId Body) Tower(CharacterAssets assets, int kind, PersonMovesSettings? moves, float top, float lean, Vector3 at,
                                                       params (Vector3 Min, Vector3 Max)[] blocks)
    {
        float x = WallX - Out;
        float foot = x - top * MathF.Tan(lean * MathF.PI / 180f);
        var ladders = LevelLadders.Of([new L2Ladder { Name = "tower", Foot = [foot, 0, 0], Top = [x, 0, top], Width = 0.5f, Facing = 180 }]);
        var all = new List<(Vector3, Vector3)> { (new Vector3(WallX, -3f, 0f), new Vector3(4.5f, 3f, top)) };
        all.AddRange(blocks);
        var (w, e) = AirCommitTests.Make(assets, kind, new PersonCommitSettings(), at, moves, ladders, blocks: [.. all]);
        w.Get<Body>(e).Yaw = 0f;
        return (w, e);
    }

    internal readonly record struct Ask(Vector2 Wish, Gait Gait = Gait.Walk, bool Use = false, bool Jump = false, bool Crouch = false);

    internal sealed record Row(string Clip, float Time, string Previous, TraversalKind Move, BodyState State, Vector3 Feet, Vector3 Pelvis, Vector3 WristL, Vector3 WristR,
                               Vector3 BallL, Vector3 BallR, string Ladder, float LadderTime, int Rung, int Set, Vector3 KnuckleL, Vector3 KnuckleR);

    /// <summary><paramref name="ticks"/> ticks asked by <paramref name="plan"/>, the view looking along +X (the ladder's forward), after 20 standing.</summary>
    internal static List<Row> Drive(World w, EntityId e, CharacterAssets assets, int kind, Func<int, Body, Ask> plan, int ticks)
    {
        var rows = new List<Row>(ticks);
        var person = assets.Kind(kind).Person!;
        var rig = person.Rig;
        var skeleton = assets.ModelOf(kind).Skeleton;
        int ballL = skeleton.Find("ball_l"), ballR = skeleton.Find("ball_r");
        for (int i = 0; i < 20; i++) { Set(w, e, new Ask(Vector2.Zero)); w.Step(); }
        for (int i = 0; i < ticks; i++)
        {
            Set(w, e, plan(i, w.Get<Body>(e)));
            w.Step();
            var body = w.Get<Body>(e);
            var a = w.Get<CharacterAnimation>(e);
            var l = w.TryStore<PersonLadder>(out var ls) && ls.Has(e) ? ls.Get(e).Move : default;
            string clip = l.Clip > 0 && person.Ladder?.Paths.At(l.Clip - 1) is { } c ? c.Name : "-";
            rows.Add(new Row(assets.Clip(a.Clip).Name, a.Time, a.PreviousClip >= 0 ? assets.Clip(a.PreviousClip).Name : "-", w.Get<Traversal>(e).Kind, body.State,
                             body.Feet, Bone(w, e, assets, rig.Pelvis), Bone(w, e, assets, rig.LeftArm.End), Bone(w, e, assets, rig.RightArm.End),
                             ballL >= 0 ? Bone(w, e, assets, ballL) : default, ballR >= 0 ? Bone(w, e, assets, ballR) : default, clip, l.Time, l.Rung, l.Set,
                             Bone(w, e, assets, rig.LeftArm.Tip), Bone(w, e, assets, rig.RightArm.Tip)));
        }
        Assert.True(Finite(w, e));
        return rows;
    }

    static void Set(World w, EntityId e, Ask ask)
    {
        ref var intent = ref w.Get<MoveIntent>(e);
        intent.Direction = ask.Wish;
        intent.Gait = ask.Gait;
        intent.Faces = false;
        intent.Facing = 0f;   // the view along +X
        intent.Jump = ask.Jump;
        intent.JumpHeld = ask.Jump;
        intent.Crouch = ask.Crouch;
        intent.Use = ask.Use;
        intent.UseHeld = ask.Use;
    }

    void Say(string what, List<Row> rows, int from, int to, int every = 1)
    {
        output.WriteLine(what);
        for (int i = Math.Max(0, from); i < Math.Min(rows.Count, to); i += every)
        {
            var r = rows[i];
            output.WriteLine($"  {i,4} {r.Clip,-24} {F(r.Time, "0.000")} {(r.Previous != "-" ? $"<{r.Previous}" : ""),-22} {r.Move,-9} {r.State,-10} {r.Ladder,-24} j{r.Rung} "
                             + $"feet {F(r.Feet.X, "0.000")},{F(r.Feet.Y, "0.000")},{F(r.Feet.Z, "0.000")} pelvis {F(r.Pelvis.X, "0.000")},{F(r.Pelvis.Z, "0.000")}");
        }
    }

    /// <summary>The largest change a tick of a point's change a tick (m) over the rows.</summary>
    static float Pop(List<Row> rows, int from, int to, Func<Row, Vector3> of)
    {
        float pop = 0f;
        for (int i = Math.Max(2, from); i < Math.Min(rows.Count, to); i++)
            pop = MathF.Max(pop, ((of(rows[i]) - of(rows[i - 1])) - (of(rows[i - 1]) - of(rows[i - 2]))).Length());
        return pop;
    }

    [Fact]
    public void The_notes_are_read_into_two_sets()
    {
        foreach (var body in new[] { "man", "woman" })
        {
            var (assets, kind, _) = body == "man" ? Player() : Woman();
            var paths = Ladders(assets, kind).Paths;
            Assert.NotNull(paths.Vertical);
            Assert.NotNull(paths.Leaning);
            var v = paths.Vertical!;
            var l = paths.Leaning!;
            Assert.Equal(0f, v.Tilt, 3);
            Assert.Equal(15f * DetMath.DegToRad, l.Tilt, 3);
            foreach (var s in new[] { v, l })
            {
                Assert.True(s.Idle.Loops);
                Assert.All(new[] { s.Up, s.Down }, c => Assert.InRange(c.Duration, 1.25f, 1.35f));
                Assert.Equal(2f * s.RungUp, s.Up.U[^1] - s.Up.U[0], 1);
                Assert.Equal(6, s.OffTop.TopRung);
                Assert.NotNull(s.OnSide[0]); Assert.NotNull(s.OnSide[1]); Assert.NotNull(s.OffSide[0]); Assert.NotNull(s.OffSide[1]);
                Assert.Equal(2, s.OffSide[0]!.LandRung);
                output.WriteLine($"{body} {(s.Vertical ? "upright" : "leaning")}: rungs {F(s.Rung, "0.000")} apart ({F(s.RungUp, "0.000")} up), rung 1 {F(s.FirstRung)} m up, its front "
                                 + $"{F(s.RungFwd, "0.000")} m ahead and its top {F(s.LowerRungUp, "0.000")} m over the capsule; the floor at the top {F(s.OffTop.FloorUp, "0.000")} m over the top position; "
                                 + $"up {F(s.Up.Duration, "0.000")} s a cycle ({F(2f * s.Rung / s.Up.Duration, "0.000")} m/s along the rails)");
            }
            Assert.NotNull(v.ToHang[0]); Assert.NotNull(v.ToHang[1]); Assert.NotNull(v.FromHang[0]); Assert.NotNull(v.FromHang[1]);
            Assert.Null(l.ToHang[0]);
            Assert.NotNull(l.OffTopRails);
            Assert.Null(v.OffTopRails);
            Assert.Equal(6, v.ToHang[0]!.HangRung);
        }
    }

    /// <summary>
    /// The heights over a ladder's foot a level builds a side landing, a braced ledge and the top's floor to (the climb's stops: the ladder positions, odd j),
    /// as the engine offers them (<see cref="BodyMotor.LadderPosition"/> and the clips' notes).
    /// </summary>
    [Fact]
    public void The_heights_of_the_climb_s_stops()
    {
        foreach (var body in new[] { "man", "woman" })
        {
            var (assets, kind, _) = body == "man" ? Player() : Woman();
            var paths = Ladders(assets, kind).Paths;
            foreach (var s in new[] { paths.Vertical!, paths.Leaning! })
            {
                float Rung(int k) => s.FirstRung + (k - 1) * s.RungUp;
                float Position(int j) => Rung(j) - s.LowerRungUp;
                output.WriteLine($"{body} {(s.Vertical ? "upright" : "leaning")}: rung k's top {F(s.FirstRung, "0.000")} + (k - 1) x {F(s.RungUp, "0.0000")} m over the foot; "
                                 + $"the top's floor at the top position's rung + {s.OffTop.TopRung - 1}, it stands {F(s.OffTop.StandFwd, "0.000")} m in from the position, the lip {F(s.OffTop.LipFwd, "0.000")}");
                foreach (var c in new[] { s.OffSide[0], s.OffSide[1], s.OnSide[0], s.OnSide[1] })
                    if (c is not null) output.WriteLine($"  {c.Name}: the landing's floor at rung j + {c.LandRung - 1} ({F(c.LandUp - (Rung(c.LandRung) - Position(1)), "0.000")} off its top), stood on {F(c.LandLeft, "0.000")} m left, {F(c.LandFwd, "0.000")} ahead of the position");
                foreach (var c in new[] { s.ToHang[0], s.ToHang[1], s.FromHang[0], s.FromHang[1] })
                    if (c is not null && !float.IsNaN(c.EdgeUp))
                        output.WriteLine($"  {c.Name}: the ledge's top at rung j + {c.HangRung - 1} ({F(c.EdgeUp - (Rung(c.HangRung) - Position(1)), "0.000")} off its top), its edge {F(c.EdgeFwd, "0.000")} ahead, the hang's middle "
                                         + $"{F(c.EdgeLeft, "0.000")} left, the hands at {string.Join(", ", c.HandsLeft.Select(h => F(h, "0.000")))}");
                for (int j = 1; j <= 11; j += 2)
                {
                    float land = Position(j) + s.OffSide[0]!.LandUp, top = Position(j) + s.OffTop.FloorUp;
                    float? ledge = s.ToHang[0] is { } h ? Position(j) + h.EdgeUp : null;
                    output.WriteLine($"  j {j,2}: a side landing {F(land, "0.000")} m, a ledge {(ledge is { } e ? F(e, "0.000") : "-")}, the top's floor {F(top, "0.000")} (j's top position)");
                    Assert.Equal(Rung(j + 1), land, 1);
                    Assert.Equal(Rung(j + s.OffTop.TopRung - 1), top, 1);
                    if (ledge is { } eu) Assert.Equal(Rung(j + 5), eu, 1);
                }
            }
        }
    }

    [Fact]
    public void Upright_on_at_the_foot_up_by_whole_cycles_over_the_top_then_on_at_the_top_down_and_off_at_the_foot()
    {
        var (assets, kind, _) = Player();
        var set = Ladders(assets, kind).Paths.Vertical!;
        var (w, e) = Tower(assets, kind, Moves, UprightTop, 0f, new Vector3(WallX - Out - 0.6f, 0f, 0f));
        var m = RoundTrip(w, e, assets, kind, "ladder_v_");
        Say("upright: on at the foot, up and over, on at the top, down and off", m.Rows, 0, m.Rows.Count, 10);
        Check(m, set, UprightTop, "ladder_v_");
    }

    [Fact]
    public void Leaning_the_same_on_its_own_set()
    {
        var (assets, kind, _) = Player();
        var set = Ladders(assets, kind).Paths.Leaning!;
        float foot = WallX - Out - LeaningTop * MathF.Tan(15f * MathF.PI / 180f);
        var (w, e) = Tower(assets, kind, Moves, LeaningTop, 15f, new Vector3(foot - 0.55f, 0f, 0f));
        var m = RoundTrip(w, e, assets, kind, "ladder_");
        Say("leaning: on at the foot, up and over, on at the top, down and off", m.Rows, 0, m.Rows.Count, 10);
        Check(m, set, LeaningTop, "ladder_");
    }

    /// <summary>What a round trip did: its rows and the ticks it went over the top, stood on it, got on at the top, and stood at the foot again.</summary>
    internal sealed record Trip(List<Row> Rows, int OnTop, int Down, int Foot);

    /// <summary>Use at the foot; forward held up and over the top; standing, use (facing the drop); back held down and off at the foot.</summary>
    internal static Trip RoundTrip(World w, EntityId e, CharacterAssets assets, int kind, string prefix, Gait gait = Gait.Walk)
    {
        int stage = 0, onTop = -1, down = -1, foot = -1, i0 = 0;
        var rows = Drive(w, e, assets, kind, (i, b) =>
        {
            if (stage == 0 && i > 30 && b.State == BodyState.Ground && b.Feet.Z > 1f) { stage = 1; onTop = i; i0 = i; }
            if (stage == 2 && b.State == BodyState.Ground && b.Feet.Z < 0.1f && i > i0 + 10) { stage = 3; foot = i; }
            if (stage == 1 && i == i0 + 30) { stage = 2; down = i; return new Ask(Vector2.Zero, Use: true); }
            return stage switch
            {
                0 => new Ask(i < 5 ? Vector2.Zero : new Vector2(1f, 0f), gait, Use: i == 0),
                2 => new Ask(i < down + 5 ? Vector2.Zero : new Vector2(-1f, 0f), gait),
                _ => new Ask(Vector2.Zero),
            };
        }, 2600);
        return new Trip(rows, onTop, down, foot);
    }

    void Check(Trip m, LadderSet set, float top, string prefix, float rate = 1f)
    {
        var up = m.Rows;
        Assert.True(m.OnTop > 0, "stood on the top");
        Assert.True(m.Foot > 0, "back at the foot");
        var clips = new List<string>();
        foreach (var r in up) if (r.Ladder != "-" && (clips.Count == 0 || clips[^1] != r.Ladder)) clips.Add(r.Ladder);
        output.WriteLine(string.Join(", ", clips));
        // Each cycle chained to the next and the climb's end straight into the getting-off (the idle only after getting on, before the wish).
        Assert.Equal([prefix + "on_bottom", prefix + "idle", prefix + "up", prefix + "off_top", prefix + "on_top", prefix + "idle", prefix + "down", prefix + "off_bottom"], clips);
        Assert.InRange(up[m.OnTop].Feet.Z, top - 0.01f, top + 0.01f);
        int cyclesUp = 0, cyclesDown = 0;
        for (int i = 1; i < up.Count; i++)
        {
            bool fresh = up[i - 1].Ladder != up[i].Ladder || up[i].LadderTime < up[i - 1].LadderTime;
            if (fresh && up[i].Ladder == prefix + "up") cyclesUp++;
            if (fresh && up[i].Ladder == prefix + "down") cyclesDown++;
        }
        Assert.Equal(3, cyclesUp);
        Assert.Equal(3, cyclesDown);
        int first = up.FindIndex(r => r.Ladder == prefix + "up"), last = up.FindLastIndex(r => r.Ladder == prefix + "up");
        float perCycle = (last - first + 1) * Tick / cyclesUp;
        float pelvis = Pop(up, 25, m.OnTop, r => r.Pelvis), pelvisDown = Pop(up, m.Down + 2, m.Foot, r => r.Pelvis);
        float hands = Pop(up, 25, m.OnTop, r => 0.5f * (r.WristL + r.WristR));
        output.WriteLine($"a cycle {F(perCycle, "0.000")} s (the clip's {F(set.Up.Duration, "0.000")}, {F(2f * set.Rung / perCycle, "0.000")} m/s along the rails); "
                         + $"the pelvis's pop up {F(pelvis * 1000f, "0.0")} mm, down {F(pelvisDown * 1000f, "0.0")} mm; the hands' {F(hands * 1000f, "0.0")} mm");
        Assert.InRange(perCycle, set.Up.Duration / rate - 0.02f, set.Up.Duration / rate + 0.02f);
        Assert.InRange(pelvis, 0f, 0.02f * rate * rate);   // a clip played faster: its own pops by its rate's square
        Assert.InRange(pelvisDown, 0f, 0.02f * rate * rate);
        Assert.InRange(up[m.Foot].Feet.Z, -0.01f, 0.01f);
    }

    [Fact]
    public void A_side_landing_level_with_rung_2_is_taken_with_the_wish_to_its_side_and_got_on_from_by_use()
    {
        var (assets, kind, _) = Player();
        var set = Ladders(assets, kind).Paths.Vertical!;
        // A landing to the ladder's left (+Y as the body faces it), its top level with rung 2's (0.6 m), up to the wall.
        var landing = (new Vector3(1.0f, 0.32f, 0f), new Vector3(WallX, 2.0f, 0.6f));
        var (w, e) = Tower(assets, kind, Moves, UprightTop, 0f, new Vector3(WallX - Out - 0.6f, 0f, 0f), landing);
        int stage = 0, at = 0, off = -1, on = -1;
        var rows = Drive(w, e, assets, kind, (i, b) =>
        {
            if (stage == 0 && b.State == BodyState.Ladder && i > 30) { stage = 1; at = i; }
            if (stage == 1 && b.State == BodyState.Ground) { stage = 2; at = i; off = i; }
            if (stage == 2 && i == at + 30) { stage = 3; on = i; return new Ask(Vector2.Zero, Use: true); }
            if (stage == 3 && b.State == BodyState.Ladder && i > on + 30) { stage = 4; at = i; }
            return stage switch
            {
                0 => new Ask(Vector2.Zero, Use: i == 0),
                1 => new Ask(i < at + 10 ? Vector2.Zero : new Vector2(0f, 1f)),
                4 => new Ask(i < at + 10 ? Vector2.Zero : new Vector2(-1f, 0f)),
                _ => new Ask(Vector2.Zero),
            };
        }, 1800);
        Say("a side landing: off onto it, on from it, down off at the foot", rows, 0, rows.Count, 10);
        var clips = new List<string>();
        foreach (var r in rows) if (r.Ladder != "-" && (clips.Count == 0 || clips[^1] != r.Ladder)) clips.Add(r.Ladder);
        output.WriteLine(string.Join(", ", clips));
        Assert.Equal(["ladder_v_on_bottom", "ladder_v_idle", "ladder_v_off_side_l", "ladder_v_on_side_l", "ladder_v_idle", "ladder_v_off_bottom"], clips);
        Assert.True(off > 0 && on > 0);
        var stood = rows[off];
        output.WriteLine($"on the landing at {F(stood.Feet.X, "0.000")} {F(stood.Feet.Y, "0.000")} {F(stood.Feet.Z, "0.000")} (the clip's {F(1.42f + set.OffSide[0]!.LandFwd, "0.000")} "
                         + $"{F(set.OffSide[0]!.LandLeft, "0.000")} 0.600); the pelvis's pop {F(Pop(rows, 25, rows.Count, r => r.Pelvis) * 1000f, "0.0")} mm");
        Assert.InRange(stood.Feet.Z, 0.59f, 0.61f);
        Assert.InRange(stood.Feet.Y, 0.4f, 0.65f);
        Assert.Equal(BodyState.Ground, rows[^1].State);
        Assert.InRange(rows[^1].Feet.Z, -0.01f, 0.01f);
        Assert.InRange(Pop(rows, 25, rows.Count, r => r.Pelvis), 0f, 0.025f);
    }

    /// <summary>The upright tower narrowed to y −0.35 and up, a block beside it to its right (−Y) 2.4 m high flush with its face: a braced ledge level with rung 8's top (rung 6 over the ladder position at rung 3).</summary>
    internal static (World World, EntityId Body) TowerAndLedge(CharacterAssets assets, int kind, PersonMovesSettings? moves, Vector3 at)
    {
        float x = WallX - Out;
        var ladders = LevelLadders.Of([new L2Ladder { Name = "tower", Foot = [x, 0, 0], Top = [x, 0, UprightTop], Width = 0.5f, Facing = 180 }]);
        var (w, e) = AirCommitTests.Make(assets, kind, new PersonCommitSettings(), at, moves, ladders,
                                         blocks: [(new Vector3(WallX, -0.35f, 0f), new Vector3(4.5f, 3f, UprightTop)), (new Vector3(WallX, -3f, 0f), new Vector3(4.5f, -0.35f, 2.4f))]);
        w.Get<Body>(e).Yaw = 0f;
        return (w, e);
    }

    [Fact]
    public void Upright_ladder_to_the_braced_ledge_beside_it_and_back()
    {
        var (assets, kind, _) = Player();
        var set = Ladders(assets, kind).Paths.Vertical!;
        var (w, e) = TowerAndLedge(assets, kind, Moves, new Vector3(WallX - Out - 0.6f, 0f, 0f));
        int stage = 0, at = 0, hung = -1, back = -1;
        var rows = Drive(w, e, assets, kind, (i, b) =>
        {
            if (stage == 0 && b.State == BodyState.Ladder && i > 30) { stage = 1; at = i; }
            if (stage == 1 && b.State == BodyState.Ladder && w.Get<PersonLadder>(e).Move.Rung == 3 && w.Get<PersonLadder>(e).Move.Way == 0) { stage = 2; at = i; }
            if (stage == 2 && b.State == BodyState.Hanging && w.Get<PersonLedge>(e).Move.Clip > 0
                && Ladders(assets, kind).Paths.At(w.Get<PersonLadder>(e).Move.Clip - 1) is null) { stage = 3; at = i; hung = i; }
            if (stage == 3 && i == at + 60) { stage = 4; at = i; back = i; }
            if (stage == 4 && b.State == BodyState.Ladder && i > at + 30 && w.Get<PersonLadder>(e).Move.Way == 0) { stage = 5; at = i; }
            return stage switch
            {
                0 => new Ask(Vector2.Zero, Use: i == 0),
                1 => new Ask(w.Get<PersonLadder>(e).Move.Rung < 3 && w.Get<PersonLadder>(e).Move.Time < 1.0f ? new Vector2(1f, 0f) : Vector2.Zero),   // up one cycle to rung 3
                2 => new Ask(new Vector2(0f, -1f)),         // the wish to the right: into the hang
                4 => new Ask(new Vector2(0f, 1f)),          // the wish to the left, toward the ladder: back onto it
                _ => new Ask(Vector2.Zero),
            };
        }, 1700);
        Say("the ladder to the braced ledge and back", rows, 0, rows.Count, 10);
        var clips = new List<string>();
        foreach (var r in rows) if (r.Ladder != "-" && (clips.Count == 0 || clips[^1] != r.Ladder)) clips.Add(r.Ladder);
        output.WriteLine(string.Join(", ", clips));
        Assert.Contains("ladder_v_to_hang_braced_r", clips);
        Assert.Contains("ladder_v_from_hang_braced_r", clips);
        Assert.True(hung > 0, "hanging braced");
        Assert.Equal("hang_braced", rows[hung + 5].Clip);
        Assert.Equal(BodyState.Ladder, rows[^1].State);
        Assert.Equal(3, rows[^1].Rung);
        // The hands on the edge in the hang (the knuckles against the ledge's edge at x 2, top 2.4).
        var k = rows[hung + 30];
        var knuckles = 0.5f * (k.KnuckleL + k.KnuckleR);
        output.WriteLine($"in the hang: the knuckles {F((knuckles.X - WallX) * 100f, "0.0")} cm past the face, {F((knuckles.Z - 2.4f) * 100f, "0.0")} cm over the top, at y {F(knuckles.Y, "0.000")}; "
                         + $"the pelvis's pop {F(Pop(rows, 25, rows.Count, r => r.Pelvis) * 1000f, "0.0")} mm, the hands' {F(Pop(rows, 25, rows.Count, r => 0.5f * (r.WristL + r.WristR)) * 1000f, "0.0")} mm");
        Assert.InRange(knuckles.Z - 2.4f, -0.02f, 0.06f);
        Assert.InRange(Pop(rows, 25, rows.Count, r => r.Pelvis), 0f, 0.03f);
    }

    [Fact]
    public void Sprint_held_plays_the_climb_at_ladderSprintRate()
    {
        var (assets, kind, _) = Player();
        var set = Ladders(assets, kind).Paths.Vertical!;
        foreach (float rate in new[] { 2f, 1.5f })
        {
            var (w, e) = Tower(assets, kind, Moves with { LadderSprintRate = rate }, UprightTop, 0f, new Vector3(WallX - Out - 0.6f, 0f, 0f));
            var rows = Drive(w, e, assets, kind, (i, b) => new Ask(i < 5 ? Vector2.Zero : new Vector2(1f, 0f), Gait.Sprint, Use: i == 0), 900);
            int first = rows.FindIndex(r => r.Ladder == "ladder_v_up"), last = rows.FindLastIndex(r => r.Ladder == "ladder_v_up");
            float perCycle = (last - first + 1) * Tick / 3f;
            output.WriteLine($"sprint at {F(rate)}: a cycle {F(perCycle, "0.000")} s ({F(2f * set.Rung / perCycle, "0.000")} m/s), the pelvis's pop {F(Pop(rows, 25, last, r => r.Pelvis) * 1000f, "0.0")} mm");
            Assert.InRange(perCycle, set.Up.Duration / rate - 0.02f, set.Up.Duration / rate + 0.02f);
        }
    }

    /// <summary>Each ladder clip's time in <paramref name="rows"/>, its first run (s).</summary>
    static Dictionary<string, float> Times(List<Row> rows)
    {
        var all = new Dictionary<string, float>();
        for (int i = 0; i < rows.Count; i++)
        {
            string c = rows[i].Ladder;
            if (c == "-" || all.ContainsKey(c)) continue;
            int j = i;
            while (j < rows.Count && rows[j].Ladder == c) j++;
            all[c] = (j - i) * Tick;
        }
        return all;
    }

    /// <summary>
    /// "the ladder animations need to play faster": the climb at <c>ladderClimbRate</c> (1.5) and the getting-on,
    /// getting-off and transitions at <c>ladderMoveRate</c> (1.5), on both sets, the capsule on the clips' paths; sprint on top of the climb's, at most 3; their
    /// times and the pelvis's and the hands' pops against the earlier build's (both rates 1).
    /// </summary>
    [Fact]
    public void The_ladders_play_at_ladderClimbRate_and_ladderMoveRate()
    {
        var (assets, kind, _) = Player();
        var paths = Ladders(assets, kind).Paths;
        var dc = new PersonMovesSettings();
        Assert.Equal((1.5f, 1.5f), (dc.LadderClimbRate, dc.LadderMoveRate));
        float foot = WallX - Out - LeaningTop * MathF.Tan(15f * MathF.PI / 180f);
        foreach (var (name, set, top, lean, at, prefix) in new[] { ("upright", paths.Vertical!, UprightTop, 0f, new Vector3(WallX - Out - 0.6f, 0f, 0f), "ladder_v_"),
                                                                   ("leaning", paths.Leaning!, LeaningTop, 15f, new Vector3(foot - 0.55f, 0f, 0f), "ladder_") })
        {
            var said = new List<string>();
            foreach (var (what, moves, rate) in new[] { ("at the clips' rates", Moves, 1f), ("faster", dc, 1.5f) })
            {
                var (w, e) = Tower(assets, kind, moves, top, lean, at);
                var m = RoundTrip(w, e, assets, kind, prefix);
                Check(m, set, top, prefix, rate);
                var t = Times(m.Rows);
                float pelvis = Pop(m.Rows, 25, m.Foot, r => r.Pelvis), hands = Pop(m.Rows, 25, m.Foot, r => 0.5f * (r.WristL + r.WristR));
                said.Add($"{what}: on at the foot {F(t[prefix + "on_bottom"], "0.000")} s, over the top {F(t[prefix + "off_top"], "0.000")}, on at the top {F(t[prefix + "on_top"], "0.000")}, "
                         + $"off at the foot {F(t[prefix + "off_bottom"], "0.000")}; the trip's pelvis pop {F(pelvis * 1000f, "0.0")} mm, the hands' {F(hands * 1000f, "0.0")} mm");
                foreach (var (clip, c) in new[] { ("on_bottom", set.OnBottom), ("off_top", set.OffTop), ("on_top", set.OnTop), ("off_bottom", set.OffBottom) })
                    Assert.InRange(t[prefix + clip], c.Duration / rate - 0.02f, c.Duration / rate + 0.02f);
                Assert.InRange(pelvis, 0f, 0.03f * rate * rate);
            }
            foreach (var l in said) output.WriteLine($"{name} {l}");
        }
        // Sprint on top of the climb's rate, at most 3: 1.5 x 2 = 3 (0.433 s a cycle).
        {
            var set = paths.Vertical!;
            var (w, e) = Tower(assets, kind, dc, UprightTop, 0f, new Vector3(WallX - Out - 0.6f, 0f, 0f));
            var rows = Drive(w, e, assets, kind, (i, b) => new Ask(i < 5 ? Vector2.Zero : new Vector2(1f, 0f), Gait.Sprint, Use: i == 0), 700);
            int first = rows.FindIndex(r => r.Ladder == "ladder_v_up"), last = rows.FindLastIndex(r => r.Ladder == "ladder_v_up");
            float perCycle = (last - first + 1) * Tick / 3f;
            output.WriteLine($"sprint at 2 on the climb's 1.5 (3, the most): a cycle {F(perCycle, "0.000")} s ({F(2f * set.Rung / perCycle, "0.000")} m/s), the pelvis's pop {F(Pop(rows, 25, last, r => r.Pelvis) * 1000f, "0.0")} mm");
            Assert.InRange(perCycle, set.Up.Duration / 3f - 0.02f, set.Up.Duration / 3f + 0.02f);
            var (w2, e2) = Tower(assets, kind, dc with { LadderSprintRate = 3f, LadderClimbRate = 3f }, UprightTop, 0f, new Vector3(WallX - Out - 0.6f, 0f, 0f));
            var rows2 = Drive(w2, e2, assets, kind, (i, b) => new Ask(i < 5 ? Vector2.Zero : new Vector2(1f, 0f), Gait.Sprint, Use: i == 0), 700);
            int f2 = rows2.FindIndex(r => r.Ladder == "ladder_v_up"), l2 = rows2.FindLastIndex(r => r.Ladder == "ladder_v_up");
            Assert.InRange((l2 - f2 + 1) * Tick / 3f, set.Up.Duration / 3f - 0.02f, set.Up.Duration / 3f + 0.02f);   // 9 capped at 3
        }
    }

    /// <summary>The side landing's and the ledge's transitions at <c>ladderMoveRate</c>, against the earlier build's.</summary>
    [Fact]
    public void A_side_landing_and_the_hang_beside_the_ladder_at_ladderMoveRate()
    {
        var (assets, kind, _) = Player();
        var set = Ladders(assets, kind).Paths.Vertical!;
        var landing = (new Vector3(1.0f, 0.32f, 0f), new Vector3(WallX, 2.0f, 0.6f));
        foreach (var (what, moves, rate) in new[] { ("at the clips' rates", Moves, 1f), ("faster", new PersonMovesSettings(), 1.5f) })
        {
            // Off onto the landing and back on (as the test), then the ladder to the hang and back.
            var (w, e) = Tower(assets, kind, moves, UprightTop, 0f, new Vector3(WallX - Out - 0.6f, 0f, 0f), landing);
            int stage = 0, at = 0;
            var rows = Drive(w, e, assets, kind, (i, b) =>
            {
                if (stage == 0 && b.State == BodyState.Ladder && i > 30) { stage = 1; at = i; }
                if (stage == 1 && b.State == BodyState.Ground) { stage = 2; at = i; }
                if (stage == 2 && i == at + 30) { stage = 3; return new Ask(Vector2.Zero, Use: true); }
                return stage switch { 0 => new Ask(Vector2.Zero, Use: i == 0), 1 => new Ask(i < at + 10 ? Vector2.Zero : new Vector2(0f, 1f)), _ => new Ask(Vector2.Zero) };
            }, 1200);
            var t = Times(rows);
            var (w2, e2) = TowerAndLedge(assets, kind, moves, new Vector3(WallX - Out - 0.6f, 0f, 0f));
            stage = 0; at = 0;
            var rows2 = Drive(w2, e2, assets, kind, (i, b) =>
            {
                if (stage == 0 && b.State == BodyState.Ladder && i > 30) { stage = 1; at = i; }
                if (stage == 1 && b.State == BodyState.Ladder && w2.Get<PersonLadder>(e2).Move.Rung == 3 && w2.Get<PersonLadder>(e2).Move.Way == 0) { stage = 2; at = i; }
                if (stage == 2 && b.State == BodyState.Hanging && w2.Get<PersonLedge>(e2).Move.Clip > 0 && Ladders(assets, kind).Paths.At(w2.Get<PersonLadder>(e2).Move.Clip - 1) is null) { stage = 3; at = i; }
                if (stage == 3 && i == at + 60) stage = 4;
                return stage switch
                {
                    0 => new Ask(Vector2.Zero, Use: i == 0),
                    1 => new Ask(w2.Get<PersonLadder>(e2).Move.Rung < 3 && w2.Get<PersonLadder>(e2).Move.Time < 1.0f ? new Vector2(1f, 0f) : Vector2.Zero),
                    2 => new Ask(new Vector2(0f, -1f)), 4 => new Ask(new Vector2(0f, 1f)), _ => new Ask(Vector2.Zero),
                };
            }, 1500);
            var t2 = Times(rows2);
            float p1 = Pop(rows, 25, rows.Count, r => r.Pelvis), p2 = Pop(rows2, 25, rows2.Count, r => r.Pelvis);
            float h1 = Pop(rows, 25, rows.Count, r => 0.5f * (r.WristL + r.WristR)), h2 = Pop(rows2, 25, rows2.Count, r => 0.5f * (r.WristL + r.WristR));
            output.WriteLine($"{what}: off onto the side landing {F(t["ladder_v_off_side_l"], "0.000")} s, on from it {F(t["ladder_v_on_side_l"], "0.000")}; to the hang {F(t2["ladder_v_to_hang_braced_r"], "0.000")}, "
                             + $"back from it {F(t2["ladder_v_from_hang_braced_r"], "0.000")}; the pelvis's pop {F(p1 * 1000f, "0.0")} and {F(p2 * 1000f, "0.0")} mm, the hands' {F(h1 * 1000f, "0.0")} and {F(h2 * 1000f, "0.0")} mm");
            foreach (var (clip, c, tt) in new[] { ("ladder_v_off_side_l", set.OffSide[0]!, t), ("ladder_v_on_side_l", set.OnSide[0]!, t), ("ladder_v_to_hang_braced_r", set.ToHang[1]!, t2), ("ladder_v_from_hang_braced_r", set.FromHang[1]!, t2) })
                Assert.InRange(tt[clip], c.Duration / rate - 0.02f, c.Duration / rate + 0.02f);
            Assert.Equal(BodyState.Ladder, rows2[^1].State);
            Assert.InRange(p1, 0f, 0.025f * rate * rate);
            Assert.InRange(p2, 0f, 0.03f * rate * rate);
        }
    }

    [Fact]
    public void A_ladder_that_fits_neither_set_is_climbed_on_the_stand_in_and_so_is_every_ladder_on_the_switch()
    {
        var (assets, kind, _) = Player();
        Ladders(assets, kind);
        foreach (var (what, moves, lean) in new[] { ("leaning 3°", (PersonMovesSettings?)Moves, 3f), ("the moves off", PersonMovesSettings.Off, 0f) })
        {
            var (w, e) = Tower(assets, kind, moves, UprightTop, lean, new Vector3(WallX - Out - lean / 15f - 0.45f, 0f, 0f));
            var rows = Drive(w, e, assets, kind, (i, b) => new Ask(i < 5 ? Vector2.Zero : new Vector2(1f, 0f), Use: i == 0), 300);
            output.WriteLine($"{what}: {string.Join(", ", rows.Select(r => r.Move).Distinct())}; ladder clips {string.Join(", ", rows.Select(r => r.Ladder).Distinct())}");
            Assert.Contains(rows, r => r.State == BodyState.Ladder);
            Assert.All(rows, r => Assert.Equal("-", r.Ladder));
            Assert.Equal(moves!.On, w.TryStore<PersonLadder>(out _));
        }
    }

    [Fact]
    public void The_hands_and_feet_meet_the_rungs_where_the_clips_have_them()
    {
        var (assets, kind, _) = Player();
        var paths = Ladders(assets, kind).Paths;
        foreach (var (what, set, top, lean, prefix) in new[] { ("upright", paths.Vertical!, UprightTop, 0f, "ladder_v_"), ("leaning", paths.Leaning!, LeaningTop, 15f, "ladder_") })
        {
            float tan = MathF.Tan(lean * MathF.PI / 180f), x = WallX - Out, foot = x - top * tan;
            float Front(float z) => foot + z * tan;
            float RungTop(int k) => set.FirstRung + (k - 1) * set.RungUp;
            var (w, e) = Tower(assets, kind, Moves, top, lean, new Vector3(foot - 0.55f, 0f, 0f));
            var m = RoundTrip(w, e, assets, kind, prefix);
            var rows = m.Rows;
            // The hands at each new hold's contact in the climb's cycles (up and down): the knuckles against the rung nearest them.
            var hands = new List<(float Dx, float Dz)>();
            for (int i = 1; i < rows.Count; i++)
            {
                var c = paths.Clips.FirstOrDefault(cl => cl.Name == rows[i].Ladder);
                if (c is null || c.Role is not (LadderRole.Up or LadderRole.Down) || rows[i - 1].Ladder != rows[i].Ladder) continue;
                foreach (var h in c.Hands)
                    if (h.ContactS > 0f && h.ContactS < c.Duration && rows[i - 1].LadderTime < h.ContactS && rows[i].LadderTime >= h.ContactS)
                    {
                        var k = h.Left ? rows[i].KnuckleL : rows[i].KnuckleR;
                        int rung = Enumerable.Range(1, 16).MinBy(n => MathF.Abs(RungTop(n) - k.Z));
                        hands.Add((k.X - Front(RungTop(rung)), k.Z - RungTop(rung)));
                    }
            }
            // The feet in the idle: each ball against the rung under it (the left on rung j, the right on j + 1).
            var feet = new List<(float Dx, float Dz)>();
            foreach (var r in rows.Where(r => r.Ladder == prefix + "idle"))
                foreach (var ball in new[] { r.BallL, r.BallR })
                {
                    int rung = Enumerable.Range(1, 16).MinBy(n => MathF.Abs(RungTop(n) - ball.Z));
                    feet.Add((ball.X - Front(RungTop(rung)), ball.Z - RungTop(rung)));
                }
            string R(IEnumerable<float> v) => $"{F(v.Min() * 100f, "0.0")} to {F(v.Max() * 100f, "0.0")} cm";
            output.WriteLine($"{what}: the knuckles at {hands.Count} holds' contact {R(hands.Select(h => h.Dx))} from the rung's front (toward the wall positive), "
                             + $"{R(hands.Select(h => h.Dz))} over its top; the balls in the idle {R(feet.Select(f => f.Dx))} from the rung's front, {R(feet.Select(f => f.Dz))} over its top");
            Assert.True(hands.Count >= 8);
            Assert.All(hands, h => Assert.InRange(h.Dz, -0.08f, 0.08f));
            Assert.All(feet, f => Assert.InRange(f.Dz, -0.06f, 0.08f));
        }
    }
}
