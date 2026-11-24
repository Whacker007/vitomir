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
/// <b>the player's own gaits</b> (the models repository's clips): <c>walk_player</c>, <c>run_player</c> and <c>sprint</c> at their clips' paces with
/// the feet keeping pace, their starts and the stops taken at once by the nearest of four ways in a stride, the capsule by the stop's own speed and its
/// sliding feet let go only in their windows; the lean into turns, the head's look and the chest's lead toward the way as presentation (the
/// digests the same with them on or off); <c>--player-gaits-off</c>'s world as before, digest for digest. Every expectation from the clips' notes; skips without the
/// models repository's files or.
/// </summary>
public sealed class PlayerGaitTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    const float Tick = World.TickSeconds;

    internal static PlayerGaits Gaits(CharacterAssets assets, int kind)
    {
        var g = assets.Kind(kind).Person!.PlayerGaits;
        Assert.SkipUnless(g is not null, "the models repository's files are too old (no walk_player, run_player)");
        Assert.SkipUnless(g!.Loops[2] >= 0 && g.Stops.All(c => c >= 0) && g.Starts.All(c => c >= 0), "the models repository's files are too old (the player's 18 starts and stops)");
        return g;
    }

    /// <summary>
    /// A world as the session's for the player (every tier 1 move, the starts and stops on the spring, the player's own gaits unless <paramref name="gaits"/> is
    /// <see cref="PersonGaitSettings.Off"/>, the lean and the look by <paramref name="present"/>), the body at <paramref name="at"/> facing +X.
    /// </summary>
    internal static (World World, EntityId Body) Make(CharacterAssets assets, int kind, PersonGaitSettings? gaits = null, PersonPresentSettings? present = null,
                                                     Vector3 at = default, float yaw = 0f)
    {
        var level = new TestLevel().Floor(-40, -40, 60, 40, cell: 4f).Build("floor");   // SqueezeCoverTests.Make's
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        StartStopSprings.Register(b, new StartStopSprings { Default = 0.2f });
        StartStopSprings.RegisterPlayer(b);
        b.Animation(assets, starts: new PersonStartSettings(), air: new PersonAirSettings(), over: new PersonOverSettings(), ledge: new PersonLedgeSettings(),
                    prelanding: new PersonPrelandingSettings(), commit: new PersonCommitSettings(), moves: new PersonMovesSettings(), hang: new PersonHangSettings(),
                    wall: new PersonWallSettings(), stealth: new PersonStealthSettings(), gaits: gaits ?? new PersonGaitSettings(), present: present ?? new PersonPresentSettings());
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
        if (world.TryStore<PersonStealth>(out _)) world.Set(e, new PersonStealth());
        world.Set(e, new MasqueradeState { Mode = MasqueradeMode.Masquerade, Profile = -1 });
        return (world, e);
    }

    /// <summary>What is asked on a tick: the wish (x, y in the level), the gait, crouch held; and the body's facing set (radians; NaN: as the motor turns it).</summary>
    internal readonly record struct Ask(Vector2 Wish, Gait Gait = Gait.Run, bool Crouch = false, float Face = float.NaN);

    /// <summary>A tick as it went.</summary>
    internal sealed record Row(int I, string Clip, float Time, StepMode Mode, Vector3 Feet, Vector2 Velocity, float Yaw, Vector3 Pelvis, Vector3 Head, Vector3 BallL,
                               Vector3 BallR, float HoldL, float HoldR, bool PlantedL, bool PlantedR, float Blend, string Previous, float Roll, float Look,
                               Vector3 HandL = default, Vector3 HandR = default)
    {
        public float Speed => Velocity.Length();
    }

    internal static List<Row> Drive(World w, EntityId e, CharacterAssets assets, int kind, Func<int, Body, Ask> plan, int ticks, int settle = 20)
    {
        var rows = new List<Row>(ticks);
        var rig = assets.Kind(kind).Person!.Rig;
        var skeleton = assets.ModelOf(kind).Skeleton;
        int ballL = skeleton.Find("ball_l"), ballR = skeleton.Find("ball_r"), handL = skeleton.Find("hand_l"), handR = skeleton.Find("hand_r");
        for (int i = 0; i < settle; i++) { Set(w, e, new Ask(Vector2.Zero)); w.Step(); }
        for (int i = 0; i < ticks; i++)
        {
            var ask = plan(i, w.Get<Body>(e));
            Set(w, e, ask);
            if (!float.IsNaN(ask.Face)) w.Get<Body>(e).Yaw = ask.Face;
            w.Step();
            var body = w.Get<Body>(e);
            var a = w.Get<CharacterAnimation>(e);
            var st = w.TryStore<PersonStep>(out var ss) && ss.Has(e) ? ss.Get(e) : default;
            var ft = w.TryStore<PersonFeet>(out var fs) && fs.Has(e) ? fs.Get(e) : default;
            var pr = w.TryStore<PersonPresent>(out var ps) && ps.Has(e) ? ps.Get(e) : default;
            // As drawn (the lean, the look, the head's lead, the joins: the renderer's pose).
            rows.Add(new Row(i, assets.Clip(a.Clip).Name, a.Time, st.Mode, body.Feet, new Vector2(body.Velocity.X, body.Velocity.Y), body.Yaw, DrawnBone(w, e, assets, rig.Pelvis),
                             DrawnBone(w, e, assets, rig.Head), DrawnBone(w, e, assets, ballL), DrawnBone(w, e, assets, ballR), ft.LeftHold, ft.RightHold, ft.Planted(true), ft.Planted(false),
                             a.Blend, a.PreviousClip >= 0 ? assets.Clip(a.PreviousClip).Name : "-", pr.Roll, pr.Head,
                             handL >= 0 ? DrawnBone(w, e, assets, handL) : default, handR >= 0 ? DrawnBone(w, e, assets, handR) : default));
        }
        return rows;
    }

    internal static void Set(World w, EntityId e, Ask ask)
    {
        ref var intent = ref w.Get<MoveIntent>(e);
        intent.Direction = ask.Wish;
        intent.Gait = ask.Gait;
        intent.Faces = false;
        intent.Jump = false;
        intent.JumpHeld = false;
        intent.Crouch = ask.Crouch;
        intent.Cancel = false;
    }

    static readonly Vector2 East = new(1f, 0f);
    static readonly (string Name, Gait Gait)[] Paces = [("walk", Gait.Walk), ("run", Gait.Run), ("sprint", Gait.Sprint)];

    /// <summary>The clips' own figures: a gait's loop length and speed, and its stride rate (steps a minute) at a speed.</summary>
    static float StepsAMinute(CharacterAssets assets, int loop, float rate) => 120f / assets.Clip(loop).Duration * rate;

    [Theory]
    [InlineData("man")]
    [InlineData("woman")]
    public void Each_gait_from_standing_plays_its_own_start_and_loop_at_the_clips_pace_with_the_feet_keeping_pace(string who)
    {
        var (assets, kind) = WallClimbTests.Body(who);
        var g = Gaits(assets, kind);
        for (int k = 0; k < 3; k++)
        {
            var (name, gait) = Paces[k];
            var (w, e) = Make(assets, kind);
            var rows = Drive(w, e, assets, kind, (i, b) => new Ask(East, gait), 480);
            string loop = assets.Clip(g.Loops[k]).Name, start = assets.Clip(g.Starts[k * 2]).Name;
            int began = rows.FindIndex(r => r.Clip == start || r.Clip == assets.Clip(g.Starts[k * 2 + 1]).Name);
            int looped = rows.FindIndex(r => r.Clip == loop);
            Assert.True(began >= 0 && began < 3, $"{who} {name}: its start did not play at once ({rows[0].Clip}, {rows[1].Clip})");
            Assert.True(looped > began, $"{who} {name}: its loop never came ({string.Join(",", rows.Select(r => r.Clip).Distinct())})");
            float pace = g.Speeds[k];
            int full = rows.FindIndex(r => r.Speed >= 0.9f * pace);
            var last = rows.Skip(360).ToList();
            float speed = last.Average(r => r.Speed);
            // The rate the loop plays at: its time's advance a tick over the tick.
            float rate = 0f;
            int n = 0;
            for (int i = 361; i < rows.Count; i++)
                if (rows[i].Clip == loop && rows[i - 1].Clip == loop && rows[i].Time > rows[i - 1].Time) { rate += (rows[i].Time - rows[i - 1].Time) / Tick; n++; }
            rate /= Math.Max(1, n);
            output.WriteLine($"{who} {name}: {start} then {loop} from {F(looped * Tick)} s; 90% of {F(pace)} m/s at {F(full * Tick)} s; steady {F(speed)} m/s, the loop's rate {F(rate, "0.000")} "
                             + $"({F(StepsAMinute(assets, g.Loops[k], rate), "0")} steps a minute; the clip's {F(StepsAMinute(assets, g.Loops[k], 1f), "0")})");
            Assert.InRange(speed, pace - 0.02f, pace + 0.02f);
            Assert.InRange(rate, 0.98f, 1.02f);   // the feet keep pace: the loop at its own rate
        }
    }

    /// <summary>Going at <paramref name="gait"/> from standing for <paramref name="go"/> ticks, then let go: every row, and the tick it was let go.</summary>
    static List<Row> StopAfter(CharacterAssets assets, int kind, Gait gait, int go, PersonGaitSettings? gaits = null, int ticks = 300)
    {
        var (w, e) = Make(assets, kind, gaits);
        return Drive(w, e, assets, kind, (i, b) => new Ask(i < go ? East : Vector2.Zero, gait), go + ticks);
    }

    /// <summary>
    /// A stop at once by the nearest of the four ways in — let go at every eighth of a stride through one stride at each gait: the stop begins the tick it is
    /// asked (no wait), by the clip whose enter_phase is nearest the loop's (within an eighth of a stride); its distance and time from the let-go to standing
    /// against the clip's notes; the capsule on the clip's speed; each foot on the ground still outside its slides.
    /// </summary>
    [Theory]
    [InlineData("man")]
    [InlineData("woman")]
    public void A_stop_is_taken_at_once_by_the_nearest_way_in_its_capsule_on_the_clip_and_the_feet_still_outside_their_slides(string who)
    {
        var (assets, kind) = WallClimbTests.Body(who);
        var g = Gaits(assets, kind);
        var bad = new List<string>();
        for (int k = 0; k < 3; k++)
        {
            var (name, gait) = Paces[k];
            float stride = assets.Clip(g.Loops[k]).Duration;
            int strideTicks = (int)MathF.Round(stride / Tick);
            for (int j = 0; j < 8; j++)
            {
                int go = 400 + j * strideTicks / 8;
                var rows = StopAfter(assets, kind, gait, go);
                var at = rows[go - 1];
                float d0 = assets.Clip(g.Loops[k]).Duration, p = at.Time / d0;
                p -= MathF.Floor(p);
                var stop = rows[go];
                var clip = assets.Clip(g.Stops.First(c => assets.Clip(c).Name == stop.Clip)) as Assets.INotedClip;
                var notes = clip!.Notes.StartStop!;
                float off = p - notes.Phase;
                off -= MathF.Round(off);
                // To the stop clip's end (the stand), and to rest.
                int end = rows.FindIndex(go, r => r.Clip != stop.Clip);
                if (end < 0) end = rows.Count - 1;
                float dist = Vector3.Distance(rows[^1].Feet, at.Feet);
                float time = (end - go) * Tick;
                int still = end;
                // The feet: each ball's most per tick over the ground while it is down (under 3 cm) in the stop's own clip (the fade into it over), outside its slides.
                float slip = 0f, slid = 0f, join = 0f;
                string where = "";
                int joined = rows.FindIndex(go, r => r.Previous == "-");   // the loop's pose blended into the stop's over
                int settled = joined + (int)MathF.Ceiling(new PersonFeetSettings().LockSeconds / Tick);   // and the feet's locks taken
                for (int i = go + 1; i <= still && i < rows.Count; i++)
                {
                    var r = rows[i]; var q = rows[i - 1];
                    if (r.Clip != stop.Clip || q.Clip != stop.Clip) continue;
                    if (i <= settled)
                    {
                        foreach (bool left in new[] { true, false })
                        {
                            var a0 = left ? q.BallL : q.BallR; var b0 = left ? r.BallL : r.BallR;
                            if (a0.Z <= 0.015f && b0.Z <= 0.015f && !notes.Sliding(left, r.Time)) join = MathF.Max(join, new Vector2(b0.X - a0.X, b0.Y - a0.Y).Length());
                        }
                        continue;
                    }
                    foreach (bool left in new[] { true, false })
                    {
                        var a = left ? q.BallL : q.BallR; var b = left ? r.BallL : r.BallR;
                        if (a.Z > 0.015f || b.Z > 0.015f) continue;
                        float m = new Vector2(b.X - a.X, b.Y - a.Y).Length();
                        if (notes.Sliding(left, r.Time) || notes.Sliding(left, q.Time)) slid = MathF.Max(slid, m);
                        else if (m > slip) { slip = m; where = $" (tick {i - go}, {(left ? "left" : "right")} at {F(r.Time, "0.000")} s, {F(b.Z * 1000f, "0")} mm up, held {F(left ? r.HoldL : r.HoldR)})"; }
                    }
                }
                string line = $"{who} {name} let go at {F(p, "0.000")} of the stride: {stop.Clip} (enters {F(notes.Phase)}, {F(off, "+0.000;-0.000")} of a stride) at {F(stop.Time, "0.000")} s; "
                              + $"{F(dist)} m in {F(time)} s (the clip's {F(notes.Distance)} m in {F(clip.Duration)} s); the feet's most a tick outside their slides {F(slip * 1000f, "0.0")} mm{where}, in them {F(slid * 1000f, "0.0")} mm; through the join {F(join * 1000f, "0.0")} mm";
                output.WriteLine(line);
                if (MathF.Abs(off) > 0.125f + 0.02f || stop.Mode != StepMode.Stop) bad.Add(line);
                if (MathF.Abs(dist - notes.Distance) > 0.06f || MathF.Abs(time - clip.Duration) > 0.03f) bad.Add("distance or time: " + line);
                if (slip > 0.005f) bad.Add("feet: " + line);   // (the man's run stops drag the closing foot over the ground 4.3 mm a tick at feet_closed_s, in the clip)
            }
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    /// <summary>
    /// A curve at a steady turn rate (<paramref name="degreesASecond"/>, to the left) at <paramref name="gait"/>, the wish turned so from 3 s on: the rows of its
    /// last second (the body settled into it).
    /// </summary>
    static List<Row> Curve(CharacterAssets assets, int kind, Gait gait, float degreesASecond, PersonPresentSettings? present = null)
    {
        var (w, e) = Make(assets, kind, present: present);
        float rate = degreesASecond * DetMath.DegToRad;
        int from = 360;
        var rows = Drive(w, e, assets, kind, (i, b) =>
        {
            float a = i < from ? 0f : (i - from) * Tick * rate;
            return new Ask(new Vector2(MathF.Cos(a), MathF.Sin(a)), gait);
        }, from + 360);
        return rows.Skip(from + 240).ToList();
    }

    /// <summary>
    /// The drawn lean (degrees, toward the body's left positive): the line from the hips up to the head, across the way it goes, from upright (the trunk turned
    /// whole about the feet: its tilt is the lean's; from the feet it reads a degree or three more, the outer leg drawn a little short of its stretch).
    /// </summary>
    static float Leaning(Row r)
    {
        var way = r.Velocity.LengthSquared() > 1e-4f ? Vector2.Normalize(r.Velocity) : new Vector2(MathF.Cos(r.Yaw), MathF.Sin(r.Yaw));
        var left = new Vector2(-way.Y, way.X);
        var up = r.Head - r.Pelvis;
        return MathF.Atan2(up.X * left.X + up.Y * left.Y, up.Z) * DetMath.RadToDeg;
    }

    /// <summary>
    /// In a steady curve at a run and a sprint (and a walk) the drawn body leans into it by atan(v·ω/g), capped (12° running, 18° sprinting, a quarter of the
    /// run's walking), over what the clip itself sways; the balls of the feet where they are without it (the feet's IK keeps them); none on the straight.
    /// </summary>
    [Theory]
    [InlineData("man")]
    [InlineData("woman")]
    public void In_a_curve_the_drawn_body_leans_into_it_by_atan_v_omega_over_g_capped_by_its_pace_with_the_feet_kept(string who)
    {
        var (assets, kind) = WallClimbTests.Body(who);
        var g = Gaits(assets, kind);
        var s = new PersonPresentSettings();
        var bad = new List<string>();
        for (int k = 0; k < 3; k++)
        {
            var (name, gait) = Paces[k];
            foreach (float rate in new[] { 20f, 45f, 90f })
            {
                var on = Curve(assets, kind, gait, rate);
                var off = Curve(assets, kind, gait, rate, PersonPresentSettings.Off);
                float speed = on.Average(r => r.Speed);
                float lean = on.Average(Leaning) - off.Average(Leaning);
                float want = MathF.Atan(speed * rate * DetMath.DegToRad / 9.81f) * DetMath.RadToDeg;
                float share = k == 0 ? s.LeanWalkShare : 1f;
                float cap = k == 0 ? s.LeanRunDegrees * s.LeanWalkShare : k == 1 ? s.LeanRunDegrees : s.LeanSprintDegrees;
                float expect = MathF.Min(want * share, cap);
                float feet = on.Zip(off).Max(p => MathF.Max(Vector3.Distance(p.First.BallL, p.Second.BallL), Vector3.Distance(p.First.BallR, p.Second.BallR)));
                float head = on.Zip(off).Average(p => Vector3.Distance(p.First.Head, p.Second.Head));
                string line = $"{who} {name} at {F(speed)} m/s turning {F(rate, "0")}°/s: leans {F(lean, "0.0")}° (atan(v·ω/g) {F(want, "0.0")}°, by its share and cap {F(expect, "0.0")}°); "
                              + $"the head {F(head * 100f, "0.0")} cm over; the balls at most {F(feet * 1000f, "0")} mm from where they are unleaned";
                output.WriteLine(line);
                if (MathF.Abs(lean - expect) > 1f || feet > 0.05f) bad.Add(line);   // (the feet: the outer leg's reach, soft-limited, a few cm in at the sprint's cap)
            }
            var straight = Curve(assets, kind, gait, 0f);
            float none = MathF.Abs(straight.Average(Leaning) - Curve(assets, kind, gait, 0f, PersonPresentSettings.Off).Average(Leaning));
            if (none > 0.1f) bad.Add($"{who} {name}: leans {F(none, "0.0")}° on the straight");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    /// <summary>
    /// In a curve the head looks into it (toward the way the body goes a little ahead) and the chest follows a share of it, within their limits; on the
    /// straight and standing, nothing (the chain follows the camera as before); falling, the head goes down toward the landing.
    /// </summary>
    [Fact]
    public void The_head_looks_into_a_turn_and_down_toward_a_landing_and_rests_on_the_straight()
    {
        var (assets, kind) = WallClimbTests.Body("man");
        var g = Gaits(assets, kind);
        var s = new PersonPresentSettings();
        var curve = Curve(assets, kind, Gait.Run, 90f);
        float look = curve.Average(r => r.Look) * DetMath.RadToDeg;
        output.WriteLine($"running, turning 90°/s left: the head {F(look, "0.0")}° into the turn (at most {F(s.LookYawDegrees, "0")}°)");
        Assert.InRange(look, 5f, s.LookYawDegrees + 0.01f);
        var right = Curve(assets, kind, Gait.Run, -90f);
        Assert.True(right.Average(r => r.Look) < -5f * DetMath.DegToRad, "turning right, the head looks right");
        var straight = Curve(assets, kind, Gait.Run, 0f);
        Assert.True(straight.Max(r => MathF.Abs(r.Look)) < 0.5f * DetMath.DegToRad, "on the straight the head rests");
        // Falling: the head down, at most its limit.
        var system = new PersonPresentSystem(assets, s);
        var p = new PersonPresent();
        var body = Body.At(new Vector3(0, 0, 3), 0f) with { State = BodyState.Air, Velocity = new Vector3(4f, 0f, -5f) };
        var input = MotionInput.Standing with { Grounded = false };
        for (int i = 0; i < 60; i++) system.Step(ref p, g, body, input, Tick);
        output.WriteLine($"falling at 5 m/s, 4 m/s ahead: the head {F(p.Pitch * DetMath.RadToDeg, "0.0")}° down");
        Assert.InRange(-p.Pitch * DetMath.RadToDeg, 5f, s.LookPitchDegrees + 0.01f);
    }

    /// <summary>
    /// The lean, the look, the head's lead and the joins are presentation: the world's digests are the same tick for tick with them on or off (their store is not digested,
    /// and the simulation's pose — the hit volumes, the cameras' eye — is made without them; only the drawn one has them).
    /// </summary>
    [Fact]
    public void The_lean_look_and_lead_change_no_digest()
    {
        var (assets, kind) = WallClimbTests.Body("man");
        Gaits(assets, kind);
        var digests = new List<ulong>[2];
        for (int run = 0; run < 2; run++)
        {
            var (w, e) = Make(assets, kind, present: run == 0 ? new PersonPresentSettings() : PersonPresentSettings.Off);
            digests[run] = [];
            for (int i = 0; i < 900; i++)
            {
                float a = i < 300 ? 0f : (i - 300) * Tick * 1.5f;
                ref var intent = ref w.Get<MoveIntent>(e);
                intent.Direction = i < 800 ? new Vector2(MathF.Cos(a), MathF.Sin(a)) : Vector2.Zero;
                intent.Gait = i < 500 ? Gait.Run : Gait.Sprint;
                w.Step();
                w.Service<CharacterPoses>().Bones(w, e);   // the poses made every tick, the simulation's and the drawn
                w.Service<CharacterPoses>().DrawnBones(w, e);
                digests[run].Add(w.Digest());
            }
        }
        Assert.Equal(digests[0], digests[1]);
    }

    /// <summary>
    /// <c>--player-gaits-off</c>: a world given the earlier build's gaits (<see cref="PersonGaitSettings.Off"/>) is a world built without them, digest for digest: the townsfolk's
    /// walk and run blends, the earlier build's stops waiting for their phase, the player's old paces (1.3, 3.0, 5.5 m/s).
    /// </summary>
    [Fact]
    public void Player_gaits_off_is_the_townsfolk_s_gaits_digest_for_digest()
    {
        var (assets, kind) = WallClimbTests.Body("man");
        Gaits(assets, kind);
        var digests = new List<ulong>[2];
        float speed = 0f;
        for (int run = 0; run < 2; run++)
        {
            var (w, e) = run == 0 ? Make(assets, kind, PersonGaitSettings.Off) : MakeGaitsOff(assets, kind);
            digests[run] = [];
            for (int i = 0; i < 600; i++)
            {
                ref var intent = ref w.Get<MoveIntent>(e);
                intent.Direction = i < 400 ? East : Vector2.Zero;
                intent.Gait = i < 200 ? Gait.Run : Gait.Sprint;
                w.Step();
                digests[run].Add(w.Digest());
                if (i == 399) speed = new Vector2(w.Get<Body>(e).Velocity.X, w.Get<Body>(e).Velocity.Y).Length();
            }
        }
        output.WriteLine($"the gaits off sprint: {F(speed)} m/s");
        Assert.InRange(speed, 5.45f, 5.55f);
        Assert.Equal(digests[0], digests[1]);
    }

    /// <summary>The earlier build's world for the player exactly (no gaits, no lean), as <see cref="SqueezeCoverTests.Make"/> builds it.</summary>
    static (World, EntityId) MakeGaitsOff(CharacterAssets assets, int kind) => SqueezeCoverTests.Make(assets, kind, new PersonStealthSettings(), Vector3.Zero, 0f, false);
}
