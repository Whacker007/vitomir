// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets.People;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;
using Gait = Vitomir.Platform.Bodies.Gait;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// <b>squeezing through gaps, stealth with cover, and ducking</b> — into a gap by the quarter turn,
/// through it on the side-steps with the slimmer capsule and out the far side facing the way it went; too narrow, bent, low or crouched refused and said once; in
/// stealth slower and lower, the back to a wall behind into cover, along it both ways to its end, out; the walk into the crouch walk and back on the phase-matched
/// blend; the earlier build's behind the switch. Skips without the models repository's files, or.
/// </summary>
public sealed class SqueezeCoverTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.000") => v.ToString(format, CultureInfo.InvariantCulture);

    const float Tick = World.TickSeconds;

    internal static LedgePaths Paths(CharacterAssets assets, int kind)
    {
        var person = assets.Kind(kind).Person!;
        Assert.SkipUnless(person.Ledge?.Paths is { HasSqueeze: true, HasCover: true }, "the models repository's files are too old (no squeeze or cover clips)");
        return person.Ledge!.Paths;
    }

    /// <summary>
    /// A world as the session's for the player (every tier 1 move: <paramref name="stealth"/>, null for the earlier build's), the body at <paramref name="at"/> facing
    /// <paramref name="yaw"/>, the floor and the blocks; in stealth when <paramref name="inStealth"/>.
    /// </summary>
    internal static (World World, EntityId Body) Make(CharacterAssets assets, int kind, PersonStealthSettings? stealth, Vector3 at, float yaw, bool inStealth,
                                                     params (Vector3 Min, Vector3 Max)[] blocks)
    {
        var t = new TestLevel().Floor(-40, -40, 60, 40, cell: 4f);
        foreach (var (min, max) in blocks) t.Box(min, max);
        var level = t.Build("floor");
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        StartStopSprings.Register(b, new StartStopSprings { Default = 0.2f });
        StartStopSprings.RegisterPlayer(b);
        b.Animation(assets, starts: new PersonStartSettings(), air: new PersonAirSettings(), over: new PersonOverSettings(), ledge: new PersonLedgeSettings(),
                    prelanding: new PersonPrelandingSettings(), commit: new PersonCommitSettings(), moves: new PersonMovesSettings(), hang: new PersonHangSettings(),
                    wall: new PersonWallSettings(), stealth: stealth);
        b.Service(new PersonHandsSettings());
        b.Component<MasqueradeState>();
        BodyMotionSystem.Register(b);
        var world = b.Build();
        var e = MovementServices.SpawnControlled(world, at + new Vector3(0, 0, 0.01f), yaw, Locomotion.Sandbox);
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
        if (world.TryStore<PersonStealth>(out _)) world.Set(e, new PersonStealth { On = inStealth ? 1 : 0, Stance = inStealth ? 1f : 0f });
        world.Set(e, new MasqueradeState { Mode = MasqueradeMode.Masquerade, Profile = -1 });
        return (world, e);
    }

    /// <summary>What is asked on a tick: the wish (x, y in the level), the gait, crouch held, cancel, stealth toggled.</summary>
    internal readonly record struct Ask(Vector2 Wish, Gait Gait = Gait.Run, bool Crouch = false, bool Cancel = false, bool Toggle = false);

    /// <summary>A tick as it went: the ledge's clip and its time, the body, the drawn pelvis and the skin's extremes against the walls, what began or was refused.</summary>
    internal sealed record Row(int I, string Clip, float Time, TraversalKind Move, BodyState State, Vector3 Feet, float Yaw, Vector3 Pelvis, Vector3 FootL, Vector3 FootR,
                               string Drawn, string Began, string Refused, float Speed, int Stealth, float Stance, float Blend, int Crouched, Vector3 HandL = default,
                               Vector3 HandR = default);

    internal static List<Row> Drive(World w, EntityId e, CharacterAssets assets, int kind, Func<int, Body, Ask> plan, int ticks, int settle = 20)
    {
        var rows = new List<Row>(ticks);
        var rig = assets.Kind(kind).Person!.Rig;
        var paths = assets.Kind(kind).Person!.Ledge?.Paths;
        var skeleton = assets.ModelOf(kind).Skeleton;
        int ballL = skeleton.Find("ball_l"), ballR = skeleton.Find("ball_r"), handL = skeleton.Find("hand_l"), handR = skeleton.Find("hand_r");
        for (int i = 0; i < settle; i++) { Set(w, e, new Ask(Vector2.Zero)); w.Step(); }
        for (int i = 0; i < ticks; i++)
        {
            var ask = plan(i, w.Get<Body>(e));
            Set(w, e, ask);
            if (ask.Toggle && w.TryStore<PersonStealth>(out var st0) && st0.Has(e)) { ref var ps = ref w.Get<PersonStealth>(e); ps.On = ps.On != 0 ? 0 : 1; }
            w.Step();
            var body = w.Get<Body>(e);
            var ledge = w.TryStore<PersonLedge>(out var ls) && ls.Has(e) ? ls.Get(e).Move : default;
            var st = w.TryStore<PersonStealth>(out var ss) && ss.Has(e) ? ss.Get(e) : default;
            string clip = ledge.Clip > 0 && paths?.At(ledge.Clip - 1) is { } c ? c.Name : "-";
            string began = "", refused = "";
            foreach (var x in w.Events<SideMoveStarted>().ThisTick) if (x.Entity == e) began = $"{x.Act} {x.Way}";
            foreach (var x in w.Events<SideMoveRefused>().ThisTick) if (x.Entity == e) refused = $"{x.Act} {x.Why}";
            var a = w.Get<CharacterAnimation>(e);
            rows.Add(new Row(i, clip, ledge.Time, w.Get<Traversal>(e).Kind, body.State, body.Feet, body.Yaw, Bone(w, e, assets, rig.Pelvis),
                             ballL >= 0 ? Bone(w, e, assets, ballL) : default, ballR >= 0 ? Bone(w, e, assets, ballR) : default, assets.Clip(a.Clip).Name, began, refused,
                             new Vector2(body.Velocity.X, body.Velocity.Y).Length(), st.On, st.Stance, a.Blend, body.Crouched,
                             handL >= 0 ? Bone(w, e, assets, handL) : default, handR >= 0 ? Bone(w, e, assets, handR) : default));
        }
        return rows;
    }

    static void Set(World w, EntityId e, Ask ask)
    {
        ref var intent = ref w.Get<MoveIntent>(e);
        intent.Direction = ask.Wish;
        intent.Gait = ask.Gait;
        intent.Faces = false;
        intent.Jump = false;
        intent.JumpHeld = false;
        intent.Crouch = ask.Crouch;
        intent.Cancel = ask.Cancel;
    }

    /// <summary>The side clips in order (each change), the moves begun and refused, and where it ended.</summary>
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
        return string.Join(", ", all) + $" | end {end.State} {end.Move} feet {F(end.Feet.X)},{F(end.Feet.Y)},{F(end.Feet.Z)} yaw {F(end.Yaw * 180f / MathF.PI, "0.0")}";
    }

    /// <summary>A gap <paramref name="width"/> wide along +X from x 2 to <c>2 + length</c>, its walls 2.4 m high (a bend: after 1.5 m it turns to +Y).</summary>
    internal static (Vector3, Vector3)[] Gap(float width, float length = 3f, bool bend = false, float ceiling = float.NaN)
    {
        float h = width * 0.5f;
        if (bend)
            return
            [
                (new Vector3(2f, h, 0f), new Vector3(3.5f + width, h + 1f, 2.4f)),           // the left wall, ending where the gap turns left
                (new Vector3(2f, -h - 1f, 0f), new Vector3(3.5f + width + 1f, -h, 2.4f)),    // the right wall, on past the turn
                (new Vector3(3.5f + width, -h, 0f), new Vector3(3.5f + width + 1f, h + 2f, 2.4f)),   // across the end: the gap goes on to +Y only
                (new Vector3(2f, h + 1f, 0f), new Vector3(3.5f, h + 3f, 2.4f)),
            ];
        var walls = new List<(Vector3, Vector3)>
        {
            (new Vector3(2f, h, 0f), new Vector3(2f + length, h + 1f, 2.4f)),
            (new Vector3(2f, -h - 1f, 0f), new Vector3(2f + length, -h, 2.4f)),
        };
        if (!float.IsNaN(ceiling)) walls.Add((new Vector3(2.5f, -h - 1f, ceiling), new Vector3(3.5f, h + 1f, ceiling + 0.2f)));
        return [.. walls];
    }

    static (CharacterAssets Assets, int Kind) Body(string who)
    {
        var (assets, kind, _) = who == "man" ? Player() : Woman();
        return (assets, kind);
    }

    /// <summary>The person's skin as drawn now, in the level (every vertex, its four bones' skin matrices blended by their weights, placed at the body).</summary>
    internal static Vector3[] Skin(World w, EntityId e, CharacterAssets assets, PersonModel person, int every = 3) => Skin(w, e, assets, person, every, out _);

    /// <summary>Which of the model's vertices are the body's own skin (its base's <c>skin_</c> part), not the outfit's, the hair's or the eyes'.</summary>
    internal static bool[] BodyVertices(PersonModel person)
    {
        var body = new bool[person.Model.Vertices.Length];
        foreach (var part in person.Model.Parts)
            if (part.Texture.Contains("skin_", StringComparison.Ordinal))
                for (int q = part.FirstIndex; q < part.FirstIndex + part.IndexCount; q++) body[person.Model.Indices[q]] = true;
        return body;
    }

    /// <summary>As the overload before, and for each vertex given whether it is the body's own skin (<paramref name="body"/>).</summary>
    internal static Vector3[] Skin(World w, EntityId e, CharacterAssets assets, PersonModel person, int every, out bool[] own)
    {
        var bodyOwn = BodyVertices(person);
        own = new bool[(person.Model.Vertices.Length + every - 1) / every];
        for (int i = 0, k = 0; i < bodyOwn.Length; i += every, k++) own[k] = bodyOwn[i];
        var skin = w.Service<CharacterPoses>().Skin(w, e);
        var body = w.Get<Body>(e);
        var place = assets.Kind(w.Get<CharacterAnimation>(e).Kind).Placement(body.Feet, body.Yaw);
        var vs = person.Model.Vertices;
        var all = new Vector3[(vs.Length + every - 1) / every];
        for (int i = 0, k = 0; i < vs.Length; i += every, k++)
        {
            var v = vs[i];
            var p = Vector3.Zero;
            float sum = 0f;
            for (int j = 0; j < 4; j++)
            {
                float wt = v.Weights[j];
                if (wt <= 0f) continue;
                p += Vector3.Transform(v.Position, skin[v.Bone(j)]) * wt;
                sum += wt;
            }
            all[k] = Vector3.Transform(sum > 0f ? p / sum : v.Position, place);
        }
        return all;
    }

    /// <summary>The largest change a tick of the change a tick of a point over the rows (mm), and the same at the rows where the clip changes (± 2 ticks).</summary>
    internal static (float All, float Seams) Pops(List<Row> rows, Func<Row, Vector3> of, int from = 1, int to = int.MaxValue) => Pops(rows, of, out _, from, to);

    /// <summary>As the overload before, and where the largest was (the row's tick and its clip as drawn, the one before).</summary>
    internal static (float All, float Seams) Pops(List<Row> rows, Func<Row, Vector3> of, out string where, int from = 1, int to = int.MaxValue)
    {
        float all = 0f, seams = 0f;
        where = "";
        to = Math.Min(to, rows.Count - 1);
        for (int i = Math.Max(1, from); i < to; i++)
        {
            float a = (of(rows[i + 1]) - 2f * of(rows[i]) + of(rows[i - 1])).Length() * 1000f;
            if (a > all) where = $"tick {rows[i].I} {rows[i - 1].Drawn} -> {rows[i].Drawn} -> {rows[i + 1].Drawn} ({rows[i].Move})";
            all = MathF.Max(all, a);
            bool seam = false;
            for (int k = Math.Max(1, i - 2); k <= Math.Min(rows.Count - 1, i + 2); k++) seam |= rows[k].Drawn != rows[k - 1].Drawn;
            if (seam) seams = MathF.Max(seams, a);
        }
        return (all, seams);
    }

    /// <summary>A foot's slide: the most its ball moves along the ground (mm) between two ticks while it is down (under 3 cm over the floor) in a side-step's cycle.</summary>
    internal static float Slide(List<Row> rows, Func<Row, Vector3> foot, string clip)
    {
        float most = 0f, run = 0f;
        for (int i = 1; i < rows.Count; i++)
        {
            var a = foot(rows[i - 1]); var b = foot(rows[i]);
            bool down = rows[i].Drawn == clip && rows[i - 1].Drawn == clip && a.Z < 0.03f && b.Z < 0.03f;
            if (!down) { run = 0f; continue; }
            float d = new Vector2(b.X - a.X, b.Y - a.Y).Length() * 1000f;
            run += d;
            most = MathF.Max(most, d);
        }
        return most;
    }

    static Ask Toward(int i, Vector2 wish, int until, Gait gait = Gait.Run) => new(i < until ? wish : Vector2.Zero, gait);

    /// <summary>
    /// Through a gap 3 m long (x 2 to 5) of each width: walked into from x 0, the quarter turn at its mouth, the side-steps through on the slimmer capsule, out the far
    /// side facing +X standing; the skin as drawn off both walls (nothing through), the pelvis's pops, the feet's slide.
    /// </summary>
    [Theory]
    [InlineData("man", 0.34f)]
    [InlineData("man", 0.40f)]
    [InlineData("man", 0.50f)]
    [InlineData("woman", 0.34f)]
    [InlineData("woman", 0.50f)]
    public void Through_a_gap_on_the_squeeze_clips_the_skin_off_both_walls_and_out_facing_the_way_it_went(string who, float width)
    {
        var (assets, kind) = Body(who);
        var paths = Paths(assets, kind);
        var person = who == "man" ? Player().Person : Woman().Person;
        var (w, e) = Make(assets, kind, new PersonStealthSettings(), new Vector3(0f, 0f, 0f), 0f, false, Gap(width));
        var rows = new List<Row>();
        float nearL = float.PositiveInfinity, nearR = float.PositiveInfinity, wornL = float.PositiveInfinity, wornR = float.PositiveInfinity;
        int t = 0;
        rows = Drive(w, e, assets, kind, (i, b) =>
        {
            t = i;
            return Toward(i, new Vector2(1f, 0f), 1500);
        }, 1700);
        output.WriteLine($"{who} {F(width, "0.00")}: {Story(rows)}");
        // The skin against the walls (y = ±width/2), replayed: every 6th tick in the gap.
        var (w2, e2) = Make(assets, kind, new PersonStealthSettings(), new Vector3(0f, 0f, 0f), 0f, false, Gap(width));
        Drive(w2, e2, assets, kind, (i, b) =>
        {
            if (i % 6 == 0 && w2.Get<Traversal>(e2).Kind == TraversalKind.Squeeze)
            {
                var sk = Skin(w2, e2, assets, person, 3, out var own);
                for (int k = 0; k < sk.Length; k++)
                {
                    var v = sk[k];
                    if (!(v.X > 2.02f && v.X < 4.98f && v.Z > 0.02f && v.Z < 2.38f)) continue;
                    if (own[k]) { nearL = MathF.Min(nearL, width * 0.5f - v.Y); nearR = MathF.Min(nearR, v.Y + width * 0.5f); }
                    else { wornL = MathF.Min(wornL, width * 0.5f - v.Y); wornR = MathF.Min(wornR, v.Y + width * 0.5f); }
                }
            }
            return Toward(i, new Vector2(1f, 0f), 1500);
        }, 1700);
        var pops = Pops(rows, r => r.Pelvis, out string popAt);
        var squeezing = rows.Where(r => r.Move == TraversalKind.Squeeze).ToList();
        float slideL = Slide(rows, r => r.FootL, paths.Side(1, LedgeRole.SideStep, -1)!.Name), slideR = Slide(rows, r => r.FootR, paths.Side(1, LedgeRole.SideStep, -1)!.Name);
        var end = rows[^1];
        output.WriteLine($"  the body's skin off the left wall (ahead) {F(nearL * 100f, "0.0")} cm, off the right (behind) {F(nearR * 100f, "0.0")} cm; the outfit, hair and eyes {F(wornL * 100f, "0.0")}, {F(wornR * 100f, "0.0")} cm; the pelvis's pops {F(pops.All, "0.0")} mm (seams {F(pops.Seams, "0.0")});"
                         + $" a ball's slide a tick at most {F(slideL, "0.0")} / {F(slideR, "0.0")} mm; {squeezing.Count * Tick:0.00} s squeezing; end yaw {F(end.Yaw * 57.29578f, "0.0")}; the largest pop at {popAt}");
        Assert.Contains("SqueezeIn", Story(rows));
        Assert.Contains("SqueezeOut", Story(rows));
        Assert.Equal(BodyState.Ground, end.State);
        Assert.Equal(TraversalKind.None, end.Move);
        Assert.True(end.Feet.X > 5.2f, $"out the far side: {F(end.Feet.X)}");
        Assert.True(MathF.Abs(DetMath.WrapAngle(end.Yaw)) < 0.02f, $"facing the way it went: {F(end.Yaw * 57.29578f, "0.0")}°");
        Assert.True(nearL >= 0f && nearR >= 0f, $"nothing through the walls: {F(nearL)} {F(nearR)}");
        Assert.True(pops.All < 40f, $"no pop: {F(pops.All, "0.0")} mm");
    }

    /// <summary>A gap too narrow (0.30 m), one that bends, one with a low ceiling, and one walked into crouched: refused, each said once, the body stopped at the mouth as before.</summary>
    [Theory]
    [InlineData("narrow")]
    [InlineData("bend")]
    [InlineData("low")]
    [InlineData("crouched")]
    public void A_gap_too_narrow_bent_low_or_walked_into_crouched_is_refused_and_said_once(string what)
    {
        var (assets, kind) = Body("man");
        Paths(assets, kind);
        var gap = what switch { "narrow" => Gap(0.30f), "bend" => Gap(0.40f, bend: true), "low" => Gap(0.40f, ceiling: 1.6f), _ => Gap(0.40f) };
        var (w, e) = Make(assets, kind, new PersonStealthSettings(), new Vector3(0f, 0f, 0f), 0f, false, gap);
        var rows = Drive(w, e, assets, kind, (i, b) => new Ask(new Vector2(1f, 0f), Crouch: what == "crouched"), 600);
        string story = Story(rows);
        output.WriteLine($"{what}: {story}");
        var why = what switch { "narrow" => SideRefusal.Narrow, "bend" => SideRefusal.Bend, "low" => SideRefusal.Low, _ => SideRefusal.Crouched };
        Assert.Equal(1, rows.Count(r => r.Refused == $"SqueezeIn {why}"));
        Assert.DoesNotContain(rows, r => r.Move == TraversalKind.Squeeze);
        Assert.True(rows[^1].Feet.X < 2f, $"stopped at the mouth: {F(rows[^1].Feet.X)}");
    }

    /// <summary>Into a gap and back out the way in (the wish turned round): the side-steps back, out at the mouth facing −X.</summary>
    [Fact]
    public void Into_a_gap_and_back_out_the_way_in()
    {
        var (assets, kind) = Body("man");
        Paths(assets, kind);
        var (w, e) = Make(assets, kind, new PersonStealthSettings(), new Vector3(0f, 0f, 0f), 0f, false, Gap(0.40f));
        var rows = Drive(w, e, assets, kind, (i, b) => new Ask(i < 520 ? new Vector2(1f, 0f) : new Vector2(-1f, 0f)), 1300);
        output.WriteLine(Story(rows));
        var end = rows.First(r => r.I > 520 && r.Move == TraversalKind.None);
        output.WriteLine($"out at x {F(end.Feet.X)} yaw {F(end.Yaw * 57.29578f, "0.0")}");
        Assert.True(end.Feet.X < 2f);
        Assert.True(MathF.Abs(DetMath.WrapAngle(end.Yaw - MathF.PI)) < 0.02f, $"facing back the way in: {F(end.Yaw * 57.29578f, "0.0")}°");
    }

    /// <summary>A wall along y 1 (its face at y 1, toward −Y) from x −3 to 3, 2.4 m high: the body's back to it facing −Y.</summary>
    internal static (Vector3, Vector3) CoverWall(float x0 = -3f, float x1 = 3f) => (new Vector3(x0, 1f, 0f), new Vector3(x1, 2f, 2.4f));

    /// <summary>
    /// In stealth, standing with the back 0.2 m off a wall: into cover (the back as drawn off the wall), along it to its left end (a stop, said once) and back to its
    /// right, then forward out of it, standing; the feet's slide in the side-steps, the pelvis's pops.
    /// </summary>
    [Theory]
    [InlineData("man")]
    [InlineData("woman")]
    public void In_stealth_into_cover_along_it_both_ways_to_its_end_and_out(string who)
    {
        var (assets, kind) = Body(who);
        var paths = Paths(assets, kind);
        var person = who == "man" ? Player().Person : Woman().Person;
        // Facing −Y (away from the wall), the capsule's middle 0.5 m from the face: the back 0.2 m off it.
        var (w, e) = Make(assets, kind, new PersonStealthSettings(), new Vector3(1.0f, 0.5f, 0f), -MathF.PI / 2f, true, CoverWall(-3f, 3f));
        // The body's left (facing −Y) is +X: left to the wall's end at x 3, then right to x −1, then forward (−Y) out.
        float backNear = float.PositiveInfinity, backFar = float.NegativeInfinity, worn = float.PositiveInfinity;
        Ask Plan(int i, Body b) => i < 200 ? new Ask(Vector2.Zero) : i < 1500 ? new Ask(new Vector2(1f, 0f)) : i < 2800 ? new Ask(new Vector2(-1f, 0f)) : i < 2900 ? new Ask(new Vector2(0f, -1f)) : new Ask(Vector2.Zero);
        var rows = Drive(w, e, assets, kind, Plan, 3200);
        output.WriteLine($"{who}: {Story(rows)}");
        var (w2, e2) = Make(assets, kind, new PersonStealthSettings(), new Vector3(1.0f, 0.5f, 0f), -MathF.PI / 2f, true, CoverWall(-3f, 3f));
        var idle = paths.Side(2, LedgeRole.SideIdle)!;
        Drive(w2, e2, assets, kind, (i, b) =>
        {
            if (i % 8 == 0 && w2.TryStore<PersonLedge>(out var ls) && ls.Get(e2).Move.Clip > 0 && paths.At(ls.Get(e2).Move.Clip - 1)?.Role is LedgeRole.SideIdle or LedgeRole.SideStep)
            {
                float near = float.PositiveInfinity;
                var sk = Skin(w2, e2, assets, person, 3, out var own);
                for (int k = 0; k < sk.Length; k++)
                {
                    var v = sk[k];
                    if (v.Z > 0.3f && v.Z < 1.7f && v.X > -2.9f && v.X < 2.9f) { if (own[k]) near = MathF.Min(near, 1f - v.Y); else worn = MathF.Min(worn, 1f - v.Y); }
                }
                backNear = MathF.Min(backNear, near);
                backFar = MathF.Max(backFar, near);
            }
            return Plan(i, b);
        }, 3200);
        var covered = rows.Where(r => r.Move == TraversalKind.Cover && r.Clip == idle.Name).ToList();
        float capsule = covered.Count > 0 ? 1f - covered[0].Feet.Y : float.NaN;
        var pops = Pops(rows, r => r.Pelvis, out string popAt);
        var stepL = paths.Side(2, LedgeRole.SideStep, 1)!.Name; var stepR = paths.Side(2, LedgeRole.SideStep, -1)!.Name;
        output.WriteLine($"  the cover's capsule {F(capsule * 100f, "0.0")} cm off the wall (the notes' {F(idle.WallBack * 100f, "0.0")}); the body's skin's nearest to it {F(backNear * 100f, "0.0")} to {F(backFar * 100f, "0.0")} cm (the outfit's {F(worn * 100f, "0.0")});"
                         + $" the pelvis's pops {F(pops.All, "0.0")} mm (seams {F(pops.Seams, "0.0")}); a ball's slide a tick at most {F(Slide(rows, r => r.FootL, stepL), "0.0")}, {F(Slide(rows, r => r.FootR, stepL), "0.0")} (left),"
                         + $" {F(Slide(rows, r => r.FootL, stepR), "0.0")}, {F(Slide(rows, r => r.FootR, stepR), "0.0")} mm (right); the hips {F(covered.Count > 0 ? covered[0].Pelvis.Z : 0f)} m; the largest pop at {popAt}");
        string story = Story(rows);
        Assert.Contains("[CoverIn 0]", story);
        Assert.Contains("[CoverStep 1]", story);
        Assert.Contains("[CoverStep -1]", story);
        Assert.Contains("[CoverOut 0]", story);
        Assert.Equal(1, rows.Count(r => r.Refused == "CoverStep WallEnds"));
        Assert.True(MathF.Abs(capsule - idle.WallBack) < 0.005f, $"the capsule {F(capsule)} m off the wall");
        Assert.True(backNear >= -0.005f, $"the back not in the wall: {F(backNear)}");
        Assert.Equal(TraversalKind.None, rows[^1].Move);
        Assert.Equal(BodyState.Ground, rows[^1].State);
        Assert.True(rows.Where(r => r.Move == TraversalKind.Cover).Max(r => r.Feet.X) < 3.0f);
    }

    /// <summary>In cover: crouch leaves it (cover_out, then the crouch as ever), and so does stealth turned off; out of stealth no wall is taken as cover.</summary>
    [Fact]
    public void Crouch_or_stealth_off_leaves_cover_and_out_of_stealth_none_is_taken()
    {
        var (assets, kind) = Body("man");
        Paths(assets, kind);
        var (w, e) = Make(assets, kind, new PersonStealthSettings(), new Vector3(1.0f, 0.5f, 0f), -MathF.PI / 2f, true, CoverWall());
        var rows = Drive(w, e, assets, kind, (i, b) => new Ask(Vector2.Zero, Crouch: i >= 400), 700);
        output.WriteLine($"crouch: {Story(rows)}");
        Assert.Contains("[CoverOut 0]", Story(rows));
        Assert.Equal(1, rows[^1].Crouched);
        (w, e) = Make(assets, kind, new PersonStealthSettings(), new Vector3(1.0f, 0.5f, 0f), -MathF.PI / 2f, true, CoverWall());
        rows = Drive(w, e, assets, kind, (i, b) => new Ask(Vector2.Zero, Toggle: i == 400), 700);
        output.WriteLine($"stealth off: {Story(rows)}");
        Assert.Contains("[CoverOut 0]", Story(rows));
        (w, e) = Make(assets, kind, new PersonStealthSettings(), new Vector3(1.0f, 0.5f, 0f), -MathF.PI / 2f, false, CoverWall());
        rows = Drive(w, e, assets, kind, (i, b) => new Ask(i < 100 ? new Vector2(0f, 1f) : Vector2.Zero), 500);
        output.WriteLine($"out of stealth: {Story(rows)}");
        Assert.DoesNotContain(rows, r => r.Move == TraversalKind.Cover);
    }

    /// <summary>
    /// No locked-in clip; Horizon's 0.1 s: in cover, a side-step let go of nearer its start runs back to it, nearer its end on to it, at sideStopRate (2), then
    /// the idle; turned round mid-cycle, the cycle runs back (its time going down, the same clip, no pop); let go for less than 0.1 s and pressed again, it goes on.
    /// </summary>
    [Fact]
    public void A_side_step_stops_when_the_key_does_turns_back_mid_cycle_and_a_short_let_go_is_not_a_stop()
    {
        var (assets, kind) = Body("man");
        var paths = Paths(assets, kind);
        var step = paths.Side(2, LedgeRole.SideStep, 1)!;
        var idle = paths.Side(2, LedgeRole.SideIdle)!;
        var left = new Vector2(1f, 0f);   // facing −Y with the back to the wall at y 1: its left is +X
        List<Row> Play(Func<int, Ask> after, int ticks)
        {
            var (w, e) = Make(assets, kind, new PersonStealthSettings(), new Vector3(0f, 0.5f, 0f), -MathF.PI / 2f, true, CoverWall());
            return Drive(w, e, assets, kind, (i, b) => i < 220 ? new Ask(Vector2.Zero) : after(i - 220), 220 + ticks);
        }
        int Start(List<Row> rows) => rows.FindIndex(r => r.I >= 220 && r.Clip == step.Name);
        // Let go a fifth of the way (0.22 s): back to the start, quickly.
        var early = Play(t => new Ask(t < 26 ? left : Vector2.Zero), 300);
        int s0 = Start(early), rel = s0 + 26 - 1 + (int)MathF.Round(0.1f / Tick);   // the key let go, and the hold's end
        int idleAt = early.FindIndex(rel, r => r.Clip == idle.Name);
        float back = early[rel].Time;
        // Let go two thirds of the way: on to the end.
        var late = Play(t => new Ask(t < 86 ? left : Vector2.Zero), 300);
        int s1 = Start(late), rel1 = s1 + 86 - 1 + (int)MathF.Round(0.1f / Tick);
        int idle1 = late.FindIndex(rel1, r => r.Clip == idle.Name);
        // Turned round half way: the cycle's time goes down, the same clip, to its start, then the other way's.
        var turned = Play(t => new Ask(t < 60 ? left : -left), 260);
        int s2 = Start(turned);
        var downs = turned.Skip(s2 + 62).Take(40).ToList();
        var pops = Pops(turned, r => r.Pelvis, s2 + 40, s2 + 140);
        // Let go for 0.05 s and pressed again: the cycle goes on (no stop, no slow-down of its time).
        var blip = Play(t => new Ask(t is >= 40 and < 46 ? Vector2.Zero : left), 200);
        int s3 = Start(blip);
        bool goesOn = blip.Skip(s3 + 38).Take(14).Zip(blip.Skip(s3 + 39).Take(14)).All(p => p.Second.Time > p.First.Time && p.Second.Clip == step.Name);
        output.WriteLine($"let go at {F(early[s0 + 25].Time)} s of {F(step.Duration)}: {F(back)} at the hold's end, the idle {F((idleAt - rel) * Tick)} s later; let go at {F(late[s1 + 85].Time)}:"
                         + $" the idle {F((idle1 - rel1) * Tick)} s after the hold; turned round: the time from {F(downs[0].Time)} to {F(downs[^1].Time)} on {downs[^1].Clip}, then {Story(turned.Skip(s2).ToList())};"
                         + $" the pelvis's pops {F(pops.All, "0.0")} mm; a 0.05 s let-go goes on: {goesOn}");
        // At the stance within half a second of the hold's end, by the end reached sooner (early: back, its time going down; late: on).
        Assert.True(idleAt > rel && (idleAt - rel) * Tick <= 0.5f && early[idleAt - 1].Time < early[rel].Time, $"stopped back at the start in {F((idleAt - rel) * Tick)} s");
        Assert.True(idle1 > rel1 && (idle1 - rel1) * Tick <= 0.5f && late[idle1 - 1].Time > late[rel1].Time, $"stopped on at the end in {F((idle1 - rel1) * Tick)} s");
        Assert.True(downs.All(r => r.Clip == step.Name) && downs[^1].Time < downs[0].Time, "turned round, the cycle runs back");
        Assert.Contains("[CoverStep -1]", Story(turned.Skip(s2).ToList()));
        Assert.True(pops.All < 10f, $"no pop turning round: {F(pops.All, "0.0")} mm");
        Assert.True(goesOn, "a let-go shorter than the hold is not a stop");
    }

    /// <summary>
    /// As in Gears, cancelled until the back meets the wall: the way into cover let go of with the wish forward before cover_in's contact: the clip run back to the
    /// stand (its two steps undone, at 1.5), then walking on.
    /// </summary>
    [Fact]
    public void The_way_into_cover_is_let_go_of_forward_before_the_back_meets_the_wall()
    {
        var (assets, kind) = Body("man");
        var paths = Paths(assets, kind);
        var cin = paths.Side(2, LedgeRole.CoverIn)!;
        var (w, e) = Make(assets, kind, new PersonStealthSettings(), new Vector3(1.0f, 0.5f, 0f), -MathF.PI / 2f, true, CoverWall());
        var first = Drive(w, e, assets, kind, (i, b) => new Ask(Vector2.Zero), 200);
        int began = first.FindIndex(r => r.Began.StartsWith("CoverIn", StringComparison.Ordinal));
        Assert.True(began >= 0, Story(first));
        (w, e) = Make(assets, kind, new PersonStealthSettings(), new Vector3(1.0f, 0.5f, 0f), -MathF.PI / 2f, true, CoverWall());
        var rows = Drive(w, e, assets, kind, (i, b) => i >= began + 30 ? new Ask(new Vector2(0f, -1f), Gait.Walk) : new Ask(Vector2.Zero), 300);
        output.WriteLine($"cover_in's contact {F(cin.ContactS)} s of {F(cin.Duration)}; {Story(rows)}");
        Assert.False(float.IsNaN(cin.ContactS));
        Assert.Contains("[CoverLetGo 0]", Story(rows));
        Assert.DoesNotContain(rows, r => r.Clip == paths.Side(2, LedgeRole.SideIdle)!.Name);
        Assert.Equal(TraversalKind.None, rows[^1].Move);
        Assert.True(rows[^1].Feet.Y < 0.2f, $"walked on away from the wall: {F(rows[^1].Feet.Y)}");
        var pops = Pops(rows, r => r.Pelvis, began + 20, began + 120);
        output.WriteLine($"the pelvis's pops through the let-go {F(pops.All, "0.0")} mm");
        Assert.True(pops.All < 15f);
    }

    /// <summary>
    /// In stealth: at stealth's pace (0.6 m/s, 46% of the walk) at the run and walking, on the sneak's clip ahead; sprint held the walk's 1.3; crouched
    /// 0.6 (the crouch walk's clips at 0.6 of their rate); the hips about 11 cm lower standing and walking; out of it as ever.
    /// </summary>
    [Fact]
    public void In_stealth_slower_and_lower_standing_and_walking_on_the_sneak()
    {
        var (assets, kind) = Body("man");
        Paths(assets, kind);
        var person = assets.Kind(kind).Person!;
        (float Speed, float HipsStill, float HipsMoving, string Clip, float Rate) Go(bool on, Gait g, bool crouch = false)
        {
            var (w, e) = Make(assets, kind, new PersonStealthSettings(), new Vector3(-20f, 0f, 0f), 0f, on);
            var rows = Drive(w, e, assets, kind, (i, b) => new Ask(i >= 200 && i < 800 ? new Vector2(1f, 0f) : Vector2.Zero, g, Crouch: crouch), 900, settle: 120);
            var a = w.Get<CharacterAnimation>(e);
            var moving = rows.Skip(500).Take(250).ToList();
            string clip = moving.GroupBy(r => r.Drawn).OrderByDescending(x => x.Count()).First().Key;
            return (moving.Average(r => r.Speed), rows.Take(150).Average(r => r.Pelvis.Z), moving.Average(r => r.Pelvis.Z), clip, 0f);
        }
        var run = Go(true, Gait.Run);
        var walk = Go(true, Gait.Walk);
        var sprint = Go(true, Gait.Sprint);
        var crouched = Go(true, Gait.Run, crouch: true);
        var offRun = Go(false, Gait.Run);
        var offWalk = Go(false, Gait.Walk);
        var offCrouch = Go(false, Gait.Run, crouch: true);
        output.WriteLine($"stealth at the run {F(run.Speed, "0.00")} m/s on {run.Clip}, walking {F(walk.Speed, "0.00")} on {walk.Clip}, sprint held {F(sprint.Speed, "0.00")} on {sprint.Clip},"
                         + $" crouched {F(crouched.Speed, "0.00")} on {crouched.Clip}; out of it {F(offRun.Speed, "0.00")}, {F(offWalk.Speed, "0.00")} ({offWalk.Clip}), crouched {F(offCrouch.Speed, "0.00")}");
        output.WriteLine($"  the hips standing {F(run.HipsStill)} against {F(offRun.HipsStill)} ({F((offRun.HipsStill - run.HipsStill) * 100f, "0.0")} cm lower); walking {F(walk.HipsMoving)} against the walk's"
                         + $" {F(offWalk.HipsMoving)} ({F((offWalk.HipsMoving - walk.HipsMoving) * 100f, "0.0")} cm lower); the sneak's own {F(PersonStealthRule.SneakDropOf(assets, kind, person) * 100f, "0.0")} cm");
        Assert.InRange(run.Speed, 0.57f, 0.63f);
        Assert.InRange(walk.Speed, 0.57f, 0.63f);
        Assert.InRange(sprint.Speed, 1.2f, 1.4f);
        Assert.InRange(crouched.Speed, 0.57f, 0.63f);
        Assert.Contains("sneak", run.Clip, StringComparison.Ordinal);
        Assert.True(offRun.Speed > 2.5f && offWalk.Speed > 1.2f && offCrouch.Speed > 0.9f);
        Assert.InRange(offRun.HipsStill - run.HipsStill, 0.08f, 0.14f);
        Assert.InRange(offWalk.HipsMoving - walk.HipsMoving, 0.07f, 0.15f);
    }

    /// <summary>Walking, crouch held: into the crouch walk over 0.4 s phase-matched, let go back out over 0.6 s; the pelvis's pops through both.</summary>
    [Fact]
    public void Ducking_in_and_out_while_walking_blends_over_its_times_phase_matched()
    {
        var (assets, kind) = Body("man");
        Paths(assets, kind);
        var (w, e) = Make(assets, kind, new PersonStealthSettings(), new Vector3(-20f, 0f, 0f), 0f, false);
        var rows = Drive(w, e, assets, kind, (i, b) => new Ask(new Vector2(1f, 0f), Gait.Walk, Crouch: i >= 300 && i < 600), 900);
        int down = rows.FindIndex(r => r.I >= 300 && r.Crouched == 1), downEnd = rows.FindIndex(down, r => r.Blend >= 1f);
        int up = rows.FindIndex(r => r.I >= 600 && r.Crouched == 0), upEnd = rows.FindIndex(up, r => r.Blend >= 1f);
        var popsIn = Pops(rows, r => r.Pelvis, down - 5, downEnd + 5);
        var popsOut = Pops(rows, r => r.Pelvis, up - 5, upEnd + 5);
        var (w0, e0) = Make(assets, kind, null, new Vector3(-20f, 0f, 0f), 0f, false);
        var before = Drive(w0, e0, assets, kind, (i, b) => new Ask(new Vector2(1f, 0f), Gait.Walk, Crouch: i >= 300 && i < 600), 900);
        int d0 = before.FindIndex(r => r.I >= 300 && r.Crouched == 1), u0 = before.FindIndex(r => r.I >= 600 && r.Crouched == 0);
        var popsIn0 = Pops(before, r => r.Pelvis, d0 - 5, before.FindIndex(d0, r => r.Blend >= 1f) + 5);
        var popsOut0 = Pops(before, r => r.Pelvis, u0 - 5, before.FindIndex(u0, r => r.Blend >= 1f) + 5);
        output.WriteLine($"in {F((downEnd - down + 1) * Tick, "0.000")} s ({rows[down].Drawn} from {rows[down - 1].Drawn}), out {F((upEnd - up + 1) * Tick, "0.000")} s;"
                         + $" the pelvis's pops in {F(popsIn.All, "0.0")} mm, out {F(popsOut.All, "0.0")} (the walk's 0.2 s: {F(popsIn0.All, "0.0")}, {F(popsOut0.All, "0.0")})");
        Assert.InRange((downEnd - down + 1) * Tick, 0.38f, 0.42f);
        Assert.InRange((upEnd - up + 1) * Tick, 0.58f, 0.62f);
    }

    /// <summary>Off the switch: the earlier build's — no store, a gap stops the body, nothing presses its back to a wall; and the note: the settings, none the earlier build's.</summary>
    [Fact]
    public void Off_the_switch_no_stealth_and_the_note()
    {
        var (assets, kind) = Body("man");
        Paths(assets, kind);
        var (w, e) = Make(assets, kind, null, new Vector3(0f, 0f, 0f), 0f, false, Gap(0.40f));
        Assert.False(w.TryStore<PersonStealth>(out _));
        var rows = Drive(w, e, assets, kind, (i, b) => new Ask(new Vector2(1f, 0f)), 600);
        Assert.DoesNotContain(rows, r => r.Move == TraversalKind.Squeeze);
        Assert.True(rows[^1].Feet.X < 2f);
        var s = new PersonStealthSettings();
        Assert.Equal("on=1 squeeze=1 cover=1 pace=0.6 crouch=0.6 lower=0.11 sneak=1 reach=0.3 in=0.4 out=0.6 turn=1.5 stop=2 hold=0.1", s.Note());
        Assert.Equal(s, new PersonStealthSettings().WithNote(s.Note()));
        Assert.Equal(PersonStealthSettings.Off, s.WithNote(null));
        Assert.Equal(PersonStealthSettings.Off, s.WithNote("off"));
        Assert.Equal("off", PersonStealthSettings.Off.Note());
        var t = s with { Pace = 0.5f, DuckOut = 0.5f, Sneak = false, TurnRate = 1.2f, Hold = 0f };
        Assert.Equal(t, new PersonStealthSettings().WithNote(t.Note()));
    }
}
