// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;
using Gait = Vitomir.Platform.Bodies.Gait;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// <b>climbing walls</b> — getting on standing and with a run-up and caught
/// in the air, the quick, careful and long moves up, down and across, the corners outside and inside, over the top into the braced hang and on up, off at the foot,
/// the let-go and the push-off; a face with nothing to hold only out of the masquerade, with the splayed grip; a wall marked not to be climbed refused; the level's
/// own holds by the IK; the earlier build's behind the switch. Skips without the models repository's files, or.
/// </summary>
public sealed class WallClimbTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    const float Tick = World.TickSeconds;

    /// <summary>The earlier build's (the earlier build's switch, <see cref="PersonWallSettings.NoFlow"/>): these tests measure the earlier build's climbing.</summary>
    internal static readonly PersonWallSettings NoFlow = PersonWallSettings.NoFlow;

    internal static LedgePaths Paths(CharacterAssets assets, int kind)
    {
        var person = assets.Kind(kind).Person!;
        Assert.SkipUnless(person.Ledge?.Paths.HasWalls == true, "the models repository's files are too old (no wall clips)");
        return person.Ledge!.Paths;
    }

    /// <summary>A wall: a block, and whether its face is in an area of walls with holds (with the level's own holds, when given).</summary>
    internal sealed record Wall(Vector3 Min, Vector3 Max, bool Holds = true, Vector3[]? Given = null, bool NoClimb = false);

    /// <summary>
    /// A world as the session's for the player (the commit point, tier 1's moves, the hang's, the walls': <paramref name="walls"/>, null for the earlier build's), the body at
    /// <paramref name="at"/> facing +X in the masquerade (<paramref name="unbound"/>: out of it), the floor and the walls.
    /// </summary>
    internal static (World World, EntityId Body) Make(CharacterAssets assets, int kind, PersonWallSettings? walls, Vector3 at, bool unbound, params Wall[] blocks)
    {
        var t = new TestLevel().Floor(-40, -40, 200, 40, cell: 4f);
        var areas = new List<HoldsArea>();
        var noClimb = new List<NoClimbArea>();
        foreach (var w in blocks)
        {
            t.Box(w.Min, w.Max);
            var box = new TurnedBox(w.Min - new Vector3(0.6f, 0.6f, 0.2f), 0f, w.Max - w.Min + new Vector3(1.2f, 1.2f, 0.6f));
            if (w.NoClimb) noClimb.Add(new NoClimbArea("noclimb", box));
            else if (w.Holds) areas.Add(new HoldsArea("holds", box, w.Given ?? []));
        }
        var collision = t.Build("floor").Collision.WithClimb(LevelClimb.Of(noClimb, areas));
        var level = new LevelSpace("floor", collision) { Places = LevelPlaces.None };
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        StartStopSprings.Register(b, new StartStopSprings { Default = 0.2f });
        StartStopSprings.RegisterPlayer(b);
        b.Animation(assets, starts: new PersonStartSettings(), air: new PersonAirSettings(), over: new PersonOverSettings(), ledge: new PersonLedgeSettings(),
                    prelanding: new PersonPrelandingSettings(), commit: new PersonCommitSettings(), moves: new PersonMovesSettings(), hang: new PersonHangSettings(), wall: walls);
        b.Service(new PersonHandsSettings());
        b.Component<MasqueradeState>();
        BodyMotionSystem.Register(b);
        var world = b.Build();
        var e = MovementServices.SpawnControlled(world, at + new Vector3(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        world.Remove<Controlled>(e);
        world.Set(e, CharacterAnimation.For(assets, kind));
        world.Set(e, MotionInput.Standing);
        if (world.TryStore<PersonStep>(out _)) world.Set(e, new PersonStep { Curve = 1 });
        if (world.TryStore<PersonAir>(out _)) world.Set(e, new PersonAir { Curve = 1 });
        if (world.TryStore<PersonOver>(out _)) world.Set(e, new PersonOver());
        if (world.TryStore<PersonLedge>(out _)) world.Set(e, new PersonLedge());
        if (world.TryStore<PersonCommit>(out _)) world.Set(e, new PersonCommit());
        if (world.TryStore<PersonLadder>(out _)) world.Set(e, new PersonLadder());
        if (world.TryStore<PersonHang>(out _)) world.Set(e, new PersonHang());
        if (world.TryStore<PersonWall>(out _)) world.Set(e, new PersonWall());
        if (world.TryStore<PersonWallFlow>(out _)) world.Set(e, new PersonWallFlow());
        world.Set(e, new MasqueradeState { Mode = unbound ? MasqueradeMode.Unbound : MasqueradeMode.Masquerade, Profile = -1 });
        return (world, e);
    }

    /// <summary>What is asked on a tick: the wish (x ahead, y to the left of +X), the gait, jump pressed (and held), crouch held, use held, cancel.</summary>
    internal readonly record struct Ask(Vector2 Wish, Gait Gait = Gait.Run, bool Jump = false, bool Crouch = false, bool Use = false, bool Cancel = false);

    /// <summary>A tick as it went: the ledge's clip and its time, the body, as drawn the pelvis, knuckles, wrists and balls; what began or was refused.</summary>
    internal sealed record Row(int I, string Clip, float Time, TraversalKind Move, BodyState State, Vector3 Feet, float Yaw, Vector3 Place, Vector2 In, Vector3 Pelvis,
                               Vector3 KnuckleL, Vector3 KnuckleR, Vector3 WristL, Vector3 WristR, Vector3 BallL, Vector3 BallR, string Drawn, string Began, string Refused,
                               float BendL, float BendR, int Sheer);

    internal static List<Row> Drive(World w, EntityId e, CharacterAssets assets, int kind, Func<int, Body, Ask> plan, int ticks, int settle = 20, Vector3? launch = null,
                                    Vector3? velocity = null)
    {
        var rows = new List<Row>(ticks);
        var rig = assets.Kind(kind).Person!.Rig;
        var paths = assets.Kind(kind).Person!.Ledge?.Paths;
        var skeleton = assets.ModelOf(kind).Skeleton;
        int ballL = skeleton.Find("ball_l"), ballR = skeleton.Find("ball_r");
        var limbs = assets.Kind(kind).Limbs!;
        for (int i = 0; i < settle; i++) { Set(w, e, new Ask(Vector2.Zero)); w.Step(); }
        if (launch is { } at)
        {
            ref var body = ref w.Get<Body>(e);
            body.Feet = at;
            body.Velocity = velocity ?? Vector3.Zero;
            body.State = BodyState.Air;
            body.Jumped = velocity is null ? 0 : 1; body.Rising = 0; body.TakeOffZ = at.Z; body.HighestZ = at.Z; body.AirSeconds = 0.3f; body.Coyote = 0f;
            body.GroundTriangle = -1; body.GroundNormal = Vector3.UnitZ;
        }
        for (int i = 0; i < ticks; i++)
        {
            Set(w, e, plan(i, w.Get<Body>(e)));
            w.Step();
            var body = w.Get<Body>(e);
            var ledge = w.TryStore<PersonLedge>(out var ls) && ls.Has(e) ? ls.Get(e).Move : default;
            var wall = w.TryStore<PersonWall>(out var ws) && ws.Has(e) ? ws.Get(e).Move : default;
            string clip = ledge.Clip > 0 && paths?.At(ledge.Clip - 1) is { } c ? c.Name : "-";
            string began = "", refused = "";
            foreach (var x in w.Events<WallMoveStarted>().ThisTick) if (x.Entity == e) began = $"{x.Act} {x.Left},{x.Up} p{x.Pace}{(x.Outside ? " out" : "")}{(x.Sheer ? " sheer" : "")}";
            foreach (var x in w.Events<WallMoveRefused>().ThisTick) if (x.Entity == e) refused = $"{x.Act} {x.Left},{x.Up} {x.Why}";
            foreach (var x in w.Events<HangMoveRefused>().ThisTick) if (x.Entity == e) refused = $"hang {x.Leap} {x.Why}";
            var a = w.Get<CharacterAnimation>(e);
            var bones = w.Service<CharacterPoses>().Bones(w, e);
            rows.Add(new Row(i, clip, ledge.Time, w.Get<Traversal>(e).Kind, body.State, body.Feet, body.Yaw, ledge.Place, ledge.In, Bone(w, e, assets, rig.Pelvis),
                             Bone(w, e, assets, rig.LeftArm.Tip), Bone(w, e, assets, rig.RightArm.Tip), Bone(w, e, assets, rig.LeftArm.End),
                             Bone(w, e, assets, rig.RightArm.End), ballL >= 0 ? Bone(w, e, assets, ballL) : default, ballR >= 0 ? Bone(w, e, assets, ballR) : default,
                             assets.Clip(a.Clip).Name, began, refused, PersonWallRule.WristBend(bones, limbs.LeftArm), PersonWallRule.WristBend(bones, limbs.RightArm),
                             wall.Sheer));
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
        intent.JumpHeld = ask.Jump;
        intent.Crouch = ask.Crouch;
        intent.UseHeld = ask.Use;
        intent.Cancel = ask.Cancel;
    }

    /// <summary>The wall clips in order (each change), the moves begun and refused, and where it ended.</summary>
    internal static string Story(List<Row> rows)
    {
        var all = new List<string>();
        foreach (var r in rows)
        {
            if (r.Clip != "-" && (all.Count == 0 || all.LastOrDefault(x => !x.StartsWith('<') && !x.StartsWith('[')) != r.Clip)) all.Add(r.Clip);
            if (r.Began != "") all.Add($"[{r.Began}]");
            if (r.Refused != "" && !all.Contains($"<{r.Refused}>")) all.Add($"<{r.Refused}>");
        }
        var end = rows[^1];
        return string.Join(", ", all) + $" | end {end.State} {end.Move} feet {F(end.Feet.X, "0.000")},{F(end.Feet.Y, "0.000")},{F(end.Feet.Z, "0.000")} yaw {F(end.Yaw * 180f / MathF.PI, "0.0")}";
    }

    internal static List<string> Clips(List<Row> rows)
    {
        var all = new List<string>();
        foreach (var r in rows) if (r.Clip != "-" && (all.Count == 0 || all[^1] != r.Clip)) all.Add(r.Clip);
        return all;
    }

    /// <summary>The man's or the woman's.</summary>
    internal static (CharacterAssets Assets, int Kind) Body(string who)
    {
        var (assets, kind, _) = who == "man" ? Player() : Woman();
        return (assets, kind);
    }

    /// <summary>A wall with holds whose face is at x 1 (facing −X), 6 m wide, its top <paramref name="top"/>, 2 m deep (a top to stand on).</summary>
    internal static Wall HoldsWall(float top) => new(new Vector3(1f, -3f, 0f), new Vector3(3f, 3f, top));

    /// <summary>The body 0.378 m out of the face at x 1 (the getting-on's own), at y <paramref name="y"/>.</summary>
    internal static Vector3 Before(float y = 0f) => new(1f - 0.378f, y, 0f);

    internal static Ask Hold(int i, Vector2 wish, int from = 200, Gait gait = Gait.Run, int until = int.MaxValue) => new(i >= from && i < until ? wish : Vector2.Zero, gait, Jump: i == 0);

    /// <summary>The scenes: name, the walls, the body's start (or launch), how it is driven, how long, unbound.</summary>
    internal sealed record Scene(string Name, Wall[] Walls, Vector3 At, Func<int, Body, Ask> Plan, int Ticks, bool Unbound = false, Vector3? Launch = null, Vector3? Velocity = null);

    internal static List<Row> Play(CharacterAssets assets, int kind, PersonWallSettings? walls, Scene scene)
    {
        var (w, e) = Make(assets, kind, walls, scene.At, scene.Unbound, scene.Walls);
        return Drive(w, e, assets, kind, scene.Plan, scene.Ticks, launch: scene.Launch, velocity: scene.Velocity);
    }

    internal static Scene[] Scenes(float top)
    {
        var wall = HoldsWall(top);
        // An outside corner to the left (the wall's end at y 1.1) and an inside one to the right (a block coming toward the climber at y -1.3).
        var outLeft = new Wall(new Vector3(1f, -3f, 0f), new Vector3(3f, 1.1f, top));
        var inRight = new[] { new Wall(new Vector3(1f, -3f, 0f), new Vector3(3f, 3f, top)), new Wall(new Vector3(-1f, -4f, 0f), new Vector3(1f, -1.3f, top)) };
        return
        [
            new("up quick", [wall], Before(), (i, b) => Hold(i, new Vector2(1f, 0f), until: 700), 1000),
            new("up careful", [wall], Before(), (i, b) => Hold(i, new Vector2(1f, 0f), gait: Gait.Walk, until: 700), 1000),
            new("up long", [wall], Before(), (i, b) => Hold(i, new Vector2(1f, 0f), gait: Gait.Sprint, until: 560), 900),
            new("left quick", [wall], Before(), (i, b) => Hold(i, new Vector2(0f, 1f), until: 560), 800),
            new("right careful", [wall], Before(), (i, b) => Hold(i, new Vector2(0f, -1f), gait: Gait.Walk, until: 560), 800),
            new("left long", [wall], Before(), (i, b) => Hold(i, new Vector2(0f, 1f), gait: Gait.Sprint, until: 500), 800),
            new("up and down to the foot", [wall], Before(), (i, b) => Hold(i, i < 520 ? new Vector2(1f, 0f) : new Vector2(-1f, 0f), until: 880), 1400),
            new("let go", [wall], Before(), (i, b) => i >= 300 && i < 640 ? new Ask(new Vector2(1f, 0f)) : new Ask(Vector2.Zero, Jump: i == 0, Cancel: i == 700), 900),
            new("push off", [wall], Before(), (i, b) => i >= 300 && i < 640 ? new Ask(new Vector2(1f, 0f)) : new Ask(i == 700 ? new Vector2(-1f, 0f) : Vector2.Zero, Jump: i == 0 || i == 700), 900),
            new("corner out left", [outLeft], Before(), (i, b) => Hold(i, new Vector2(0f, 1f)), 1100),
            new("corner in right", inRight, Before(), (i, b) => Hold(i, new Vector2(0f, -1f)), 1100),
            new("sheer, the masquerade", [wall with { Holds = false }], Before(), (i, b) => Hold(i, new Vector2(1f, 0f)), 500),
            new("sheer, unbound", [wall with { Holds = false }], Before(), (i, b) => Hold(i, new Vector2(1f, 0f), until: 700), 900, Unbound: true),
            new("no climb", [wall with { NoClimb = true }], Before(), (i, b) => Hold(i, new Vector2(1f, 0f)), 500, Unbound: true),
            new("on with a run-up", [wall], new Vector3(-6f, 0f, 0f), (i, b) => new Ask(new Vector2(1f, 0f), Jump: b.Feet.X > 1f - 0.378f - 0.55f && b.State == BodyState.Ground && i < 400 && b.Feet.X < 0.5f), 500),
            new("jumped at", [wall], new Vector3(-6f, 0f, 0f), (i, b) => new Ask(i < 400 ? new Vector2(1f, 0f) : Vector2.Zero, Gait.Sprint, Jump: b.Feet.X > -1.8f && b.Feet.X < -1.3f && b.State == BodyState.Ground), 500),
        ];
    }

    /// <summary>
    /// Each limb at each hold it takes (its <c>on_s</c> in each wall clip played but a corner's and the clip over the top's): the knuckles (a hand's) or the ball
    /// (a foot's) against the hold's place at the wall position (the lattice's: the clip's own laid from the true wall position; or the level's own nearest):
    /// out of the face (toward the climber positive), along it, and over the hold's top (m). Printed; the largest of each kind returned.
    /// </summary>
    internal static List<(string Clip, int Limb, Vector3 Off)> OnHolds(List<Row> rows, LedgePaths paths, LevelClimb? given = null, float reach = 0.25f)
    {
        var all = new List<(string, int, Vector3)>();
        for (int i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            if (paths.Clips.FirstOrDefault(x => x.Name == r.Clip) is not { OnWall: true } c || c.Role is LedgeRole.WallToHang or LedgeRole.WallDrop or LedgeRole.WallOff) continue;
            var p = rows[i - 1];
            bool fresh = p.Clip != r.Clip;
            foreach (var h in c.Holds)
            {
                if (h.On < 0f || h.On >= c.Duration) continue;
                if (!(r.Time >= h.On - 1e-4f && (fresh || p.Time < h.On - 1e-4f))) continue;
                Vector3 hold;
                var lv = new Vector2(-r.In.Y, r.In.X);
                if (c.Role == LedgeRole.WallCorner)
                {
                    var way = lv * c.Way;
                    var corner = new Vector3(r.Place.X + r.In.X * c.WallFace + way.X * c.CornerAlong, r.Place.Y + r.In.Y * c.WallFace + way.Y * c.CornerAlong, r.Place.Z + h.Up);
                    hold = !h.Other ? corner + new Vector3(way * h.Along, 0f) : corner + new Vector3((c.Outside ? r.In : -r.In) * h.Along, 0f);
                }
                else hold = new Vector3(r.Place.X + r.In.X * c.WallFace + lv.X * h.Left, r.Place.Y + r.In.Y * c.WallFace + lv.Y * h.Left, r.Place.Z + h.Up);
                if (given is not null && given.HoldNear(hold, reach, out var g)) hold = g;
                var at = h.Limb switch { 0 => r.KnuckleL, 1 => r.KnuckleR, 2 => r.BallL, _ => r.BallR };
                var d = at - hold;
                // The face's outward normal at the hold: the first face's, or a corner's other face's (its in turned a quarter).
                var inward = r.In;
                if (c.Role == LedgeRole.WallCorner && h.Other) inward = c.Outside ? -lv * c.Way : lv * c.Way;
                var along = new Vector2(-inward.Y, inward.X);
                all.Add((c.Name, h.Limb, new Vector3(-Vector2.Dot(new Vector2(d.X, d.Y), inward), Vector2.Dot(new Vector2(d.X, d.Y), along), d.Z)));
            }
        }
        return all;
    }

    /// <summary>The rows as the commit tests' (for their pop measures).</summary>
    internal static List<AirCommitTests.Row> Adapt(List<Row> rows) =>
        [.. rows.Select(r => new AirCommitTests.Row(r.Drawn, 0f, 0f, "-", 1f, r.Move, r.State, r.Feet, Vector3.Zero, r.Clip, AirOutcome.None, 0f, false, r.Pelvis,
                                                     r.KnuckleL, r.KnuckleR, r.WristL, r.WristR, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0))];

    /// <summary>Each hand's wrist's bend (radians) through the rows where the wall clip has it on a hold (its on_s to its off_s): the largest, per hand.</summary>
    internal static (float Left, float Right) HeldBends(List<Row> rows, LedgePaths paths)
    {
        float l = 0f, r = 0f;
        foreach (var row in rows)
        {
            if (paths.Clips.FirstOrDefault(x => x.Name == row.Clip) is not { OnWall: true } c) continue;
            foreach (var h in c.Holds)
                if (h.Hand && row.Time >= h.On + 0.1f && row.Time <= h.Off - 0.1f)
                {
                    if (h.Limb == 0) l = MathF.Max(l, row.BendL); else r = MathF.Max(r, row.BendR);
                }
        }
        return (l, r);
    }

    /// <summary>The man's top for the tests: level with the clip over the top's own from the lattice's sixth row (its wall position 2.134 m over the ground).</summary>
    internal const float Top = 4.0107f;

    internal static string Holds(List<(string Clip, int Limb, Vector3 Off)> on, int limb) =>
        on.Where(x => x.Limb == limb).Select(x => x.Off).ToList() is { Count: > 0 } v
            ? $"out {F(v.Min(x => x.X) * 100f, "0.0")}..{F(v.Max(x => x.X) * 100f, "0.0")} along {F(v.Min(x => x.Y) * 100f, "0.0")}..{F(v.Max(x => x.Y) * 100f, "0.0")} up {F(v.Min(x => x.Z) * 100f, "0.0")}..{F(v.Max(x => x.Z) * 100f, "0.0")} cm"
            : "-";

    /// <summary>The pops (the largest change a tick of the change a tick, as drawn) of the pelvis and the hands from <paramref name="from"/> to <paramref name="to"/>.</summary>
    internal static (float Pelvis, float Hands) Pops(List<Row> rows, int from, int to) =>
        (AirCommitTests.Pop(Adapt(rows), from, to, AirCommitTests.PelvisOf), AirCommitTests.Pop(Adapt(rows), from, to, AirCommitTests.HandsOf));

    /// <summary>The pops at every seam between two wall clips (2 ticks either side), the largest.</summary>
    internal static (float Pelvis, float Hands) SeamPops(List<Row> rows)
    {
        float p = 0f, h = 0f;
        for (int i = 1; i < rows.Count; i++)
            if (rows[i].Clip != rows[i - 1].Clip && rows[i].Clip.StartsWith("wall_", StringComparison.Ordinal) && rows[i - 1].Clip.StartsWith("wall_", StringComparison.Ordinal))
            {
                var (a, b) = Pops(rows, i - 2, i + 3);
                p = MathF.Max(p, a);
                h = MathF.Max(h, b);
            }
        return (p, h);
    }

    /// <summary>Asserts a hand's knuckles on its hold as the notes have them (out of the face <c>-hands.fwd_m</c>, over the top <c>hands.up_m</c>) within the clips' own slack.</summary>
    static void HandsAsNotes(List<(string Clip, int Limb, Vector3 Off)> on, LedgeClip idle, float slack, float outBy = 0f)
    {
        foreach (var (clip, limb, off) in on.Where(x => x.Limb < 2))
        {
            Assert.True(MathF.Abs(off.X - (-idle.HandOff - outBy)) <= slack, $"{clip} hand {limb}: {F(off.X * 100f, "0.0")} cm out of the face, the notes' {F((-idle.HandOff - outBy) * 100f, "0.0")}");
            Assert.True(MathF.Abs(off.Y) <= slack, $"{clip} hand {limb}: {F(off.Y * 100f, "0.0")} cm along");
            Assert.True(off.Z >= -0.01f && off.Z <= 0.04f, $"{clip} hand {limb}: {F(off.Z * 100f, "0.0")} cm over the hold's top");
        }
    }

    [Fact]
    public void Getting_on_standing_with_a_run_up_and_jumped_at()
    {
        foreach (var who in new[] { "man", "woman" })
        {
            var (assets, kind) = Body(who);
            var paths = Paths(assets, kind);
            var idle = paths.Wall(LedgeRole.WallIdle)!;
            foreach (var (name, act, clip) in new[] { ("up quick", "OnGround", "wall_on_ground"), ("on with a run-up", "OnJump", "wall_on_jump"), ("jumped at", "Catch", "wall_on_jump") })
            {
                var rows = Play(assets, kind, NoFlow, Scenes(Top).First(x => x.Name == name));
                int start = rows.FindIndex(r => r.Began.StartsWith(act + " ", StringComparison.Ordinal));
                Assert.True(start >= 0, $"{who}, {name}: not on ({Story(rows)})");
                int idleAt = rows.FindIndex(start, r => r.Clip == "wall_idle");
                Assert.True(idleAt > start, $"{who}, {name}: no idle after it ({Story(rows)})");
                var on = OnHolds(rows.GetRange(0, idleAt + 1), paths);
                var (pelvis, hands) = Pops(rows, start - 2, idleAt + 30);
                output.WriteLine($"{who}, {name}: {clip} {F((idleAt - start) * Tick, "0.000")} s from {F(rows[Math.Max(0, start - 1)].Feet.X, "0.000")} (the face at 1); the knuckles at each hold's on_s: left {Holds(on, 0)}, right {Holds(on, 1)}; "
                                 + $"the balls: left {Holds(on, 2)}, right {Holds(on, 3)}; the pelvis's pop {F(pelvis * 1000f, "0.0")} mm, the hands' {F(hands * 1000f, "0.0")} mm; the wall position {F(rows[idleAt].Place.Z, "0.000")} over the ground");
                Assert.Equal(clip, rows[start].Clip);
                Assert.Equal(BodyState.Hanging, rows[idleAt].State);
                Assert.Equal(TraversalKind.Wall, rows[idleAt].Move);
                HandsAsNotes(on, idle, 0.012f);
                // On the lattice: a row of it (the ground's 0.134 m, the man's, and 0.4 m steps).
                float row = (rows[idleAt].Place.Z + paths.WallOn(jump: false)!.WallGround) / 0.4f;
                Assert.InRange(MathF.Abs(row - MathF.Round(row)), 0f, 0.01f);
                Assert.InRange(MathF.Abs(rows[idleAt].Place.Y / 0.4f - MathF.Round(rows[idleAt].Place.Y / 0.4f)), 0f, 0.01f);
            }
        }
    }

    [Fact]
    public void The_moves_quick_careful_and_long_up_and_across_keep_the_hands_and_feet_on_the_lattice_s_holds()
    {
        foreach (var who in new[] { "man", "woman" })
        {
            var (assets, kind) = Body(who);
            var paths = Paths(assets, kind);
            var idle = paths.Wall(LedgeRole.WallIdle)!;
            foreach (var (name, clip, ends) in new[]
            {
                ("up quick", "wall_up_quick", new Vector3(0.64f, 0f, 4 * 0.4f)), ("up careful", "wall_up", new Vector3(0.64f, 0f, 2 * 0.4f)), ("up long", "wall_long_up", new Vector3(0.64f, 0f, 4 * 0.4f)),
                ("left quick", "wall_left_quick", new Vector3(0.64f, 1.2f, 0f)), ("right careful", "wall_right", new Vector3(0.64f, -0.8f, 0f)), ("left long", "wall_long_left", new Vector3(0.64f, 0.8f, 0f)),
            })
            {
                var rows = Play(assets, kind, NoFlow, Scenes(Top).First(x => x.Name == name));
                var moves = rows.Where(r => r.Began.StartsWith("Move ", StringComparison.Ordinal)).ToList();
                var on = OnHolds(rows, paths);
                int first = rows.FindIndex(r => r.Clip == clip);
                var (pelvis, hands) = Pops(rows, first - 2, rows.Count);
                var (sp, sh) = SeamPops(rows);
                var end = rows[^1];
                float t = paths.Clips.First(c => c.Name == clip).Duration;
                output.WriteLine($"{who}, {name}: {moves.Count} × {clip} ({F(t, "0.00")} s each); the knuckles at each hold's on_s: left {Holds(on, 0)}, right {Holds(on, 1)}; the balls: left {Holds(on, 2)}, right {Holds(on, 3)}; "
                                 + $"the pelvis's pop {F(pelvis * 1000f, "0.0")} mm (at the seams {F(sp * 1000f, "0.0")}), the hands' {F(hands * 1000f, "0.0")} mm ({F(sh * 1000f, "0.0")}); ends at {F(end.Place.X, "0.000")},{F(end.Place.Y, "0.000")},{F(end.Place.Z, "0.000")}");
                Assert.True(moves.Count >= 1 && rows.Any(r => r.Clip == clip), $"{who}, {name}: {Story(rows)}");
                Assert.Equal("wall_idle", end.Clip);
                Assert.InRange(Vector2.Distance(new Vector2(end.Place.X, end.Place.Y), new Vector2(ends.X, ends.Y)), 0f, 0.002f);
                Assert.InRange(end.Place.Z - rows[first - 1].Place.Z - ends.Z, -0.002f, 0.002f);
                // The knuckles where the notes say at every hold (the woman's leading hand reaching across 0.13 s late, the models repository's: 5 cm more there).
                HandsAsNotes(on, idle, who == "woman" && name == "right careful" ? 0.05f : 0.012f);
                Assert.InRange(sp, 0f, 0.012f);
            }
        }
    }

    [Fact]
    public void Round_the_corners_outside_and_inside_facing_the_other_face()
    {
        foreach (var who in new[] { "man", "woman" })
        {
            var (assets, kind) = Body(who);
            var paths = Paths(assets, kind);
            foreach (var (name, clip, yaw) in new[] { ("corner out left", "wall_corner_out_left", -90f), ("corner in right", "wall_corner_in_right", -90f) })
            {
                var rows = Play(assets, kind, NoFlow, Scenes(Top).First(x => x.Name == name));
                int start = rows.FindIndex(r => r.Clip == clip);
                Assert.True(start > 0, $"{who}, {name}: {Story(rows)}");
                int end = rows.FindIndex(start, r => r.Clip != clip);
                var at = rows[end];
                float off = MathF.Abs(DetMath.WrapAngle(at.Yaw - yaw * DetMath.DegToRad)) * 180f / MathF.PI;
                var (pelvis, hands) = Pops(rows, start - 2, end + 30);
                var on = OnHolds(rows.GetRange(start, end - start + 1), paths);
                output.WriteLine($"{who}, {name}: {clip} {F((end - start) * Tick, "0.000")} s; the facing after it {F(at.Yaw * 180f / MathF.PI, "0.0")}° ({F(off, "0.00")}° off the face's); the wall position {F(at.Place.X, "0.000")},{F(at.Place.Y, "0.000")}; "
                                 + $"the knuckles at each hold's on_s: left {Holds(on, 0)}, right {Holds(on, 1)}; the pelvis's pop {F(pelvis * 1000f, "0.0")} mm, the hands' {F(hands * 1000f, "0.0")} mm; {Story(rows)}");
                Assert.InRange(off, 0f, 0.5f);
                Assert.Equal(BodyState.Hanging, at.State);
                Assert.InRange(pelvis, 0f, 0.02f);
            }
        }
    }

    [Fact]
    public void Over_the_top_into_the_braced_hang_and_on_up_while_held_else_the_hang_and_off_at_the_foot()
    {
        foreach (var who in new[] { "man", "woman" })
        {
            var (assets, kind) = Body(who);
            Paths(assets, kind);
            // Up held all the way: over the top into the hang and straight on into the pull-up, standing on the top.
            var held = Play(assets, kind, NoFlow, new Scene("over the top", [HoldsWall(Top)], Before(), (i, b) => Hold(i, new Vector2(1f, 0f)), 1500));
            output.WriteLine($"{who}, up held to the top: {Story(held)}");
            var clips = Clips(held);
            int over = clips.IndexOf("wall_to_hang_braced");
            Assert.True(over > 0 && clips[over + 1] == "pull_up_braced", $"{who}: {string.Join(", ", clips)}");
            Assert.InRange(held.First(r => r.Clip == "pull_up_braced").I - held.First(r => r.Clip == "wall_to_hang_braced").I, 1, (int)(1.95f / Tick));   // no wait at the hang
            Assert.Contains(held, r => r.State == BodyState.Ground && MathF.Abs(r.Feet.Z - Top) < 0.02f);
            // Let go of up as it goes over: the braced hang; and where the top is a lip with nothing to stand on (a wall over it): the hang too.
            var stop = Play(assets, kind, NoFlow, new Scene("over, let go", [HoldsWall(Top)], Before(), (i, b) => Hold(i, new Vector2(1f, 0f), until: 900), 1300));
            var lip = Play(assets, kind, NoFlow, new Scene("a lip, no top", [HoldsWall(Top), new Wall(new Vector3(1.15f, -3f, Top), new Vector3(3f, 3f, Top + 0.6f), Holds: false)], Before(),
                                                       (i, b) => Hold(i, new Vector2(1f, 0f)), 1300));
            output.WriteLine($"{who}, up let go at the top: {Story(stop)}; a lip with no top: {Story(lip)}");
            Assert.Contains("wall_to_hang_braced", Clips(stop));
            Assert.Equal(("hang_braced", BodyState.Hanging), (stop[^1].Clip, stop[^1].State));
            Assert.Contains("wall_to_hang_braced", Clips(lip));
            Assert.Equal(("hang_braced", BodyState.Hanging), (lip[^1].Clip, lip[^1].State));
            // Down at the foot: off onto the ground, standing.
            var down = Play(assets, kind, NoFlow, Scenes(Top).First(x => x.Name == "up and down to the foot"));
            output.WriteLine($"{who}, up two and down: {Story(down)}");
            Assert.Contains("wall_off_ground", Clips(down));
            int off = down.FindIndex(r => r.Clip == "wall_off_ground");
            int stood = down.FindIndex(off, r => r.State == BodyState.Ground);
            var (pelvis, hands) = Pops(down, off - 2, stood + 30);
            output.WriteLine($"  off at the foot: standing {F((stood - off) * Tick, "0.000")} s after, at {F(down[stood].Feet.X, "0.000")}, {F(down[stood].Feet.Z, "0.000")}; the pelvis's pop {F(pelvis * 1000f, "0.0")} mm, the hands' {F(hands * 1000f, "0.0")} mm");
            Assert.InRange(down[stood].Feet.Z, -0.01f, 0.02f);
        }
    }

    [Fact]
    public void Let_go_and_push_off_into_the_fall()
    {
        foreach (var who in new[] { "man", "woman" })
        {
            var (assets, kind) = Body(who);
            Paths(assets, kind);
            foreach (var (name, clip) in new[] { ("let go", "wall_let_go"), ("push off", "wall_push_off") })
            {
                var rows = Play(assets, kind, NoFlow, Scenes(Top).First(x => x.Name == name));
                int at = rows.FindIndex(r => r.Clip == clip);
                Assert.True(at > 0, $"{who}, {name}: {Story(rows)}");
                int air = rows.FindIndex(at, r => r.State == BodyState.Air);
                int land = rows.FindIndex(air, r => r.State == BodyState.Ground);
                var (pelvis, hands) = Pops(rows, at - 2, air + 5);
                output.WriteLine($"{who}, {name}: {clip}, in the air {F((air - at) * Tick, "0.000")} s after, the velocity then {F(rows[air].Feet.X - rows[air - 1].Feet.X, "0.000")} m a tick away; landed {F(rows[land].Feet.X, "0.000")} out (the wall position {F(rows[at].Place.X, "0.000")}); "
                                 + $"the pelvis's pop into it {F(pelvis * 1000f, "0.0")} mm, the hands' {F(hands * 1000f, "0.0")} mm");
                Assert.True(air > at && land > air, Story(rows));
                if (name == "push off") Assert.True(rows[land].Feet.X < rows[at].Place.X - 0.6f, "pushed off: landed away from the wall");
                else Assert.True(rows[land].Feet.X > rows[at].Place.X - 0.4f, "let go: landed under it");
            }
        }
    }

    [Fact]
    public void A_sheer_face_only_out_of_the_masquerade_with_the_splayed_grip_its_wrists_within_the_limit()
    {
        foreach (var who in new[] { "man", "woman" })
        {
            var (assets, kind) = Body(who);
            var paths = Paths(assets, kind);
            var ledge = assets.Kind(kind).Person!.Ledge!;
            var idle = paths.Wall(LedgeRole.WallIdle)!;
            var mask = Play(assets, kind, NoFlow, Scenes(Top).First(x => x.Name == "sheer, the masquerade"));
            Assert.DoesNotContain(Clips(mask), c => c.StartsWith("wall_", StringComparison.Ordinal));
            Assert.DoesNotContain(mask, r => r.Refused != "");   // every house's face: not said
            var rows = Play(assets, kind, NoFlow, Scenes(Top).First(x => x.Name == "sheer, unbound"));
            var on = OnHolds(rows, paths);
            var (l, r) = HeldBends(rows, paths);
            output.WriteLine($"{who}, a sheer face unbound: {Story(rows)}; the knuckles at each hold's on_s: left {Holds(on, 0)}, right {Holds(on, 1)} (the splayed grip's notes {F(ledge.SplayNotes * 100f, "0.0")} cm nearer the face than the holds' grip; measured in the clips {F(ledge.SplayNearer * 100f, "0.0")}); "
                             + $"the wrists' bend while held {F(l * 180f / MathF.PI, "0.0")}°, {F(r * 180f / MathF.PI, "0.0")}°");
            Assert.Contains(rows, x => x.Began.Contains(" sheer", StringComparison.Ordinal));
            Assert.Contains(rows, x => x.Clip == "wall_up_quick" && x.Sheer == 1);
            HandsAsNotes(on, idle, 0.012f, outBy: ledge.SplayNotes);
            Assert.InRange(MathF.Max(l, r) * 180f / MathF.PI, 30f, 50.5f);
        }
    }

    [Fact]
    public void A_wall_marked_not_to_be_climbed_is_refused_and_said_once_and_a_wall_within_the_grab_bands_is_grabbed()
    {
        var (assets, kind) = Body("man");
        var paths = Paths(assets, kind);
        var rows = Play(assets, kind, NoFlow, Scenes(Top).First(x => x.Name == "no climb"));
        output.WriteLine($"no climb: {Story(rows)}");
        Assert.DoesNotContain(Clips(rows), c => c.StartsWith("wall_", StringComparison.Ordinal));
        Assert.Single(rows, r => r.Refused.EndsWith(" NoClimb", StringComparison.Ordinal));
        // A wall with holds whose top is a grab's (the standing grab's own height): grabbed, as before.
        float grab = MathF.Round(paths.Of(LedgeRole.GrabStand)!.OwnHeight, 2);
        var low = Play(assets, kind, NoFlow, new Scene("low", [HoldsWall(grab)], new Vector3(0.55f, 0f, 0f), (i, b) => Hold(i, Vector2.Zero), 400));
        output.WriteLine($"a wall with holds {F(grab)} m high: {Story(low)}");
        Assert.DoesNotContain(Clips(low), c => c.StartsWith("wall_", StringComparison.Ordinal));
        Assert.Contains(Clips(low), c => c.StartsWith("grab_jump_up", StringComparison.Ordinal));
    }

    [Fact]
    public void The_level_s_own_holds_are_found_by_the_IK_and_a_move_with_none_there_is_refused()
    {
        var (assets, kind) = Body("man");
        var paths = Paths(assets, kind);
        var idle = paths.Wall(LedgeRole.WallIdle)!;
        // The level's own holds: the lattice's (rows over the ground at the man's wall positions), each nudged up to 3 cm along and 2 cm up; none above 2.2 m.
        var given = new List<Vector3>();
        for (int k = -6; k <= 6; k++)
            for (int j = 0; j < 8; j++)
                for (int n = 0; n < idle.Holds.Length; n++)
                {
                    var h = idle.Holds[n];
                    if (n == 2) continue;   // the idle's hand_r has two holds (the hand shaken out): one place
                    var p = new Vector3(1f, 0.4f * k + h.Left + ((k * 7 + j * 3 + n * 5) % 5 - 2) * 0.015f, -paths.WallOn(jump: false)!.WallGround + 0.4f * j + h.Up + ((k + j + n) % 3 - 1) * 0.02f);
                    if (p.Z < 2.2f) given.Add(p);
                }
        var wall = HoldsWall(Top) with { Given = [.. given] };
        var climb = LevelClimb.Of([], [new HoldsArea("given", new TurnedBox(new Vector3(0.4f, -3.6f, -0.2f), 0f, new Vector3(1.2f, 7.2f, 4.6f)), [.. given])]);
        foreach (var (name, wish) in new[] { ("up", new Vector2(1f, 0f)), ("left", new Vector2(0f, 1f)) })
        {
            var rows = Play(assets, kind, NoFlow, new Scene(name, [wall], Before(), (i, b) => Hold(i, wish, until: 900), 1100));
            var on = OnHolds(rows, paths, climb);
            output.WriteLine($"the level's own holds, {name} held: {Story(rows)}; the knuckles at each hold's on_s (against the level's hold): left {Holds(on, 0)}, right {Holds(on, 1)}; the balls: left {Holds(on, 2)}, right {Holds(on, 3)}");
            HandsAsNotes(on, idle, 0.015f);
            foreach (var (c, limb, off) in on.Where(x => x.Limb >= 2))
                Assert.True(MathF.Abs(off.Z - 0.015f) < 0.03f, $"{c} foot {limb}: {F(off.Z * 100f, "0.0")} cm over its hold");
            if (name == "up") Assert.Contains(rows, r => r.Refused.EndsWith(" Top", StringComparison.Ordinal) || r.Refused.EndsWith(" NoHolds", StringComparison.Ordinal));   // no holds over 2.2 m
        }
    }

    [Fact]
    public void Off_the_switch_no_wall_and_no_store()
    {
        var (assets, kind) = Body("man");
        Paths(assets, kind);
        var (w, _) = Make(assets, kind, null, Before(), unbound: false, HoldsWall(Top));
        Assert.False(w.TryStore<PersonWall>(out _));
        Assert.False(w.TryService<PersonWallSettings>(out _));
        var rows = Play(assets, kind, null, Scenes(Top).First(x => x.Name == "up quick"));
        output.WriteLine($"the walls off: {Story(rows)}");
        Assert.DoesNotContain(Clips(rows), c => c.StartsWith("wall_", StringComparison.Ordinal));
    }

    [Fact]
    public void The_note_says_the_settings_and_none_is_the_walls_off()
    {
        Assert.Equal("on=1 reach=0.25 rate=1 wrist=50", NoFlow.Note());
        Assert.Equal("off", PersonWallSettings.Off.Note());
        Assert.False(NoFlow.WithNote(null).On);
        Assert.False(NoFlow.WithNote("off").On);
        var back = NoFlow.WithNote((NoFlow with { HoldReach = 0.3f, Rate = 1.5f }).Note());
        Assert.Equal((true, 0.3f, 1.5f), (back.On, back.HoldReach, back.Rate));
        Assert.Equal(0.4f, (NoFlow with { HoldReach = 9f }).Rules(null).HoldReach);
        Assert.Equal(3f, (NoFlow with { Rate = 9f }).Rules(null).Rate);
    }

    [Fact]
    public void A_level_marks_walls_with_holds_by_an_area_and_may_give_its_own_holds()
    {
        const string Json = """
            { "format": "vitomir-l2", "version": 1, "name": "t", "areas": [
              { "name": "climbing-wall", "kind": "zone", "climb": "holds", "box": { "origin": [0, 0, 0], "rotZ": 0, "size": [2, 4, 5] } },
              { "name": "tower", "kind": "zone", "climb": "holds", "holds": [[5.0, 1.0, 1.6], [5.0, 1.4, 1.7], [5, "x", 2]], "box": { "origin": [4, 0, 0], "rotZ": 0, "size": [2, 4, 5] } },
              { "name": "roof", "kind": "zone", "climb": "none", "box": { "origin": [9, 0, 0], "rotZ": 0, "size": [2, 2, 2] } } ] }
            """;
        var climb = LevelClimb.From(Vitomir.Formats.L2.L2Manifest.Read(Json));
        Assert.Equal(["climbing-wall", "tower"], climb.HoldsAreas.Select(a => a.Name));
        Assert.Equal(["roof"], climb.Areas.Select(a => a.Name));
        Assert.Single(climb.Problems);   // the hold that is not [x, y, z]
        Assert.Empty(climb.HoldsAt(new Vector3(1f, 1f, 1f))!.Holds);   // the lattice
        Assert.Equal(2, climb.HoldsAt(new Vector3(5f, 1f, 1f))!.Holds.Length);
        Assert.Null(climb.HoldsAt(new Vector3(8f, 1f, 1f)));
        Assert.True(climb.HoldNear(new Vector3(5f, 1.1f, 1.6f), 0.25f, out var h) && h == new Vector3(5f, 1f, 1.6f));
        Assert.False(climb.HoldNear(new Vector3(5f, 2.5f, 1.6f), 0.25f, out _));
        Assert.False(climb.NoClimbAt(new Vector3(1f, 1f, 1f)));
    }
}
