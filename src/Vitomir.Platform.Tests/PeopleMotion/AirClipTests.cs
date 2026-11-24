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
/// The models repository's jumps, fall and landings (the running jumps; <see cref="PersonAirClips"/>, <see cref="PeopleAir"/>,
/// <see cref="PersonAir"/>) on the body's own motor and the player's spring: the notes read; the player's jump leaving after its clip's squash
/// or at once; the drawn arc timed by the capsule's; the landing chosen by the drop; a running jump carried into its running landing
/// back to back, held on a drop, cut in on a step up; the feet against the ground at contact. A set without them and the earlier build's switch have
/// no store. Every expectation from the clips' notes. Skips without the models repository's files, or.
/// </summary>
public sealed class AirClipTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    static PersonAirClips Air(CharacterAssets assets, int kind)
    {
        var air = assets.Kind(kind).Person!.Air;
        Assert.SkipUnless(air is not null, "the models repository's files are too old (no jumps, fall or landings)");
        return air!;
    }

    static ClipNotes Notes(CharacterAssets assets, int clip) => ((INotedClip)assets.Clip(clip)).Notes;

    static int Clip(CharacterAssets assets, string name)
    {
        for (int i = 0; i < assets.ClipCount; i++) if (assets.Clip(i).Name == name) return i;
        throw new InvalidOperationException(name);
    }

    /// <summary>
    /// A world with the motor (the player's spring), the animation with <paramref name="air"/> (and the starts), one body of the player's kind on the
    /// spring, facing +X at <paramref name="at"/>; the floor at 0, and <paramref name="blocks"/> (min, max) on it.
    /// </summary>
    static (World World, EntityId Body) Make(CharacterAssets assets, int kind, PersonAirSettings? air, Vector3 at, params (Vector3 Min, Vector3 Max)[] blocks)
    {
        var t = new TestLevel().Floor(-40, -40, 200, 40, cell: 4f);
        foreach (var (min, max) in blocks) t.Box(min, max);
        var level = new LevelSpace("floor", t.Build("floor").Collision) { Places = LevelPlaces.None };
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        StartStopSprings.Register(b, new StartStopSprings { Default = 0.2f });
        StartStopSprings.RegisterPlayer(b);
        b.Animation(assets, starts: new PersonStartSettings(), air: air);
        BodyMotionSystem.Register(b);
        var w = b.Build();
        var e = MovementServices.SpawnControlled(w, at + new Vector3(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        w.Remove<Controlled>(e);   // the test asks its intent itself
        w.Set(e, CharacterAnimation.For(assets, kind));
        w.Set(e, MotionInput.Standing);
        if (w.TryStore<PersonStep>(out _)) w.Set(e, new PersonStep { Curve = 1 });
        if (w.TryStore<PersonAir>(out _)) w.Set(e, new PersonAir { Curve = 1 });
        return (w, e);
    }

    /// <summary>One tick asked: ahead at <paramref name="share"/> of <paramref name="gait"/>, jump pressed or not.</summary>
    static void Ask(World w, EntityId e, float share, Gait gait, bool jump)
    {
        ref var intent = ref w.Get<MoveIntent>(e);
        intent.Direction = new Vector2(share, 0f);
        intent.Gait = gait;
        intent.Faces = true;
        intent.Facing = 0f;
        intent.Jump = jump;
        intent.JumpHeld = jump;
    }

    /// <summary>A tick as it went: the clip and its time, the fade, the body, and (as drawn) the pelvis and the lower ball over the capsule's feet.</summary>
    sealed record Row(string Clip, float Time, string Previous, float Blend, AirMode Mode, bool Ground, Vector3 Feet, float Vz, float Pelvis, float Ball, float Rise);

    /// <summary>The ball's joint over the capsule's bottom in the stand (m), the reference the feet's float and sink are measured from.</summary>
    static float StandBall(CharacterAssets assets, int kind)
    {
        var (w, e) = Make(assets, kind, null, Vector3.Zero);
        for (int i = 0; i < 30; i++) { Ask(w, e, 0f, Gait.Walk, false); w.Step(); }
        var rig = assets.Kind(kind).Person!.Rig;
        float feet = w.Get<Body>(e).Feet.Z;
        return MathF.Min(Bone(w, e, assets, rig.LeftLeg.Tip).Z, Bone(w, e, assets, rig.RightLeg.Tip).Z) - feet;
    }

    /// <summary>
    /// Settled, then <paramref name="ticks"/> ticks going ahead at <paramref name="share"/> of <paramref name="gait"/> (none: standing), jump pressed on
    /// tick <paramref name="press"/> (−1: never); every tick's row.
    /// </summary>
    static List<Row> Drive(World w, EntityId e, CharacterAssets assets, int kind, float share, Gait gait, int press, int ticks, int settle = 60, int goFor = int.MaxValue)
    {
        var rows = new List<Row>(ticks);
        var rig = assets.Kind(kind).Person!.Rig;
        for (int i = 0; i < settle; i++) { Ask(w, e, share, gait, false); w.Step(); }
        for (int i = 0; i < ticks; i++)
        {
            Ask(w, e, i < goFor ? share : 0f, gait, i == press);
            w.Step();
            var body = w.Get<Body>(e);
            var a = w.Get<CharacterAnimation>(e);
            var mode = w.TryStore<PersonAir>(out var airs) && airs.Has(e) ? airs.Get(e).Mode : AirMode.None;
            float ball = MathF.Min(Bone(w, e, assets, rig.LeftLeg.Tip).Z, Bone(w, e, assets, rig.RightLeg.Tip).Z) - body.Feet.Z;
            rows.Add(new Row(assets.Clip(a.Clip).Name, a.Time, a.PreviousClip >= 0 ? assets.Clip(a.PreviousClip).Name : "-", a.PreviousClip >= 0 ? a.Blend : 1f, mode,
                             body.State == BodyState.Ground, body.Feet, body.Velocity.Z, Bone(w, e, assets, rig.Pelvis).Z - body.Feet.Z, ball, body.JumpRise));
        }
        return rows;
    }

    /// <summary>The first tick from <paramref name="from"/> on the ground after being in the air (the contact), or −1.</summary>
    static int Contact(List<Row> rows, int from = 0)
    {
        for (int i = Math.Max(1, from); i < rows.Count; i++) if (rows[i].Ground && !rows[i - 1].Ground) return i;
        return -1;
    }

    /// <summary>
    /// Around a contact: the feet's float (+) or sink (−) against the stand's at the contact and the most and least over the next 12 ticks; the
    /// pelvis over the capsule's feet, its largest change a tick and its largest change of that change (a pop: the clip's own fold is a steady
    /// change) from 3 ticks before to 12 after (m).
    /// </summary>
    static (float AtContact, float Most, float Least, float Pelvis, float Pop) Around(List<Row> rows, int contact, float standBall)
    {
        float most = float.MinValue, least = float.MaxValue, pelvis = 0f, pop = 0f;
        for (int i = contact; i < Math.Min(rows.Count, contact + 12); i++)
        {
            most = MathF.Max(most, rows[i].Ball - standBall);
            least = MathF.Min(least, rows[i].Ball - standBall);
        }
        for (int i = Math.Max(2, contact - 3); i < Math.Min(rows.Count, contact + 12); i++)
        {
            float now = rows[i].Pelvis - rows[i - 1].Pelvis, before = rows[i - 1].Pelvis - rows[i - 2].Pelvis;
            pelvis = MathF.Max(pelvis, MathF.Abs(now));
            pop = MathF.Max(pop, MathF.Abs(now - before));
        }
        return (rows[contact].Ball - standBall, most, least, pelvis, pop);
    }

    /// <summary>The largest distance between two clips' joints in the model's space, <paramref name="a"/> at its end and <paramref name="b"/> at its start (m), and the pelvis's.</summary>
    static (float Most, float Pelvis, string Bone) Seam(CharacterAssets assets, int kind, int a, int b)
    {
        var skeleton = assets.Model(assets.Kind(kind).Model).Skeleton;
        int n = skeleton.Count;
        var la = new BoneTransform[n]; var lb = new BoneTransform[n];
        var ma = new Matrix4x4[n]; var mb = new Matrix4x4[n];
        assets.Clip(a).Sample(assets.Clip(a).Duration, skeleton, la);
        assets.Clip(b).Sample(0f, skeleton, lb);
        PoseEvaluator.ToModel(skeleton, la, ma);
        PoseEvaluator.ToModel(skeleton, lb, mb);
        float most = 0f; string bone = "";
        for (int i = 0; i < n; i++)
        {
            if (skeleton.Bones[i].Name.StartsWith("swing", StringComparison.Ordinal)) continue;
            float d = Vector3.Distance(ma[i].Translation, mb[i].Translation);
            if (d > most) (most, bone) = (d, skeleton.Bones[i].Name);
        }
        int pelvis = assets.Kind(kind).Person!.Rig.Pelvis;
        return (most, Vector3.Distance(ma[pelvis].Translation, mb[pelvis].Translation), bone);
    }

    void Say(string what, List<Row> rows, int from, int to)
    {
        output.WriteLine(what);
        for (int i = Math.Max(0, from); i < Math.Min(rows.Count, to); i++)
        {
            var r = rows[i];
            output.WriteLine($"  {i,4} {r.Clip,-16} {F(r.Time, "0.000")} {(r.Previous != "-" ? $"<{r.Previous} {F(r.Blend)}" : ""),-22} {r.Mode,-6} {(r.Ground ? "G" : "A")} "
                             + $"z {F(r.Feet.Z, "0.000")} vz {F(r.Vz, "0.00")} pelvis {F(r.Pelvis, "0.000")} ball {F(r.Ball, "0.000")}");
        }
    }

    [Fact]
    public void The_clips_and_their_notes_are_read_by_name_and_by_their_notes()
    {
        var (assets, kind, _) = Player();
        var air = Air(assets, kind);
        string N(int c) => assets.Clip(c).Name;
        Assert.Equal("jump", N(air.Jump));
        Assert.Equal("jump_natural", N(air.JumpNatural));
        Assert.Equal("fall", N(air.Fall));
        Assert.True(assets.Clip(air.Fall).Loops);
        // The standing landings for everyone (a newer set adds the player's own high one, landing.who player: an earlier build plays it, OverClipTests).
        Assert.Equal(["land_soft", "land_high"], air.Landings.Where(c => Notes(assets, c).Landing!.Who != "player").Select(N));
        // The jumps: the player's leaves before everyone else's; each rises to its apex after its take-off; the standing ones blend into the fall.
        var jump = Notes(assets, air.Jump).Airborne!;
        var natural = Notes(assets, air.JumpNatural).Airborne!;
        Assert.True(jump.Jump && natural.Jump && !jump.Holds && !natural.Holds && !jump.Running);
        Assert.True(jump.TakeoffS < natural.TakeoffS);
        Assert.InRange(jump.ApexS - jump.TakeoffS, 0.9f * jump.VUpMs / 9.81f, 1.1f * jump.VUpMs / 9.81f);   // under gravity, as the notes say
        Assert.InRange(jump.RiseM, 0.9f * jump.VUpMs * jump.VUpMs / (2f * 9.81f), 1.1f * jump.VUpMs * jump.VUpMs / (2f * 9.81f));
        // The landings' bands: from nought, each beginning where the one before ends.
        float edge = 0f;
        foreach (int c in air.Landings.Where(c => Notes(assets, c).Landing!.Who != "player"))
        {
            var l = Notes(assets, c).Landing!;
            Assert.False(l.Running);
            Assert.Equal(edge, l.DropLow, 3);
            Assert.True(l.DropHigh > l.DropLow);
            edge = l.DropHigh;
        }
        // the running jumps: off one foot, held to the ground, ending on the other foot's running landing (each a running landing into the run).
        if (air.RunJumps.All(c => c >= 0))
            for (int foot = 1; foot <= 2; foot++)
            {
                var ab = Notes(assets, air.RunJump(foot)).Airborne!;
                Assert.True(ab.Running && (ab.Holds || ab.Strides));   // Held to the ground; the stride carried on in fall_run
                Assert.Equal(foot == 1 ? "l" : "r", ab.EnterFoot);
                int land = Clip(assets, ab.Land);
                var ln = Notes(assets, land).Landing!;
                Assert.True(ln.Running);
                Assert.Equal(foot == 1 ? "r" : "l", ln.Onto);
                Assert.InRange(ln.ExitPhase, 0f, 1f);
                Assert.InRange(assets.Clip(air.RunJump(foot)).Duration, ab.LevelLandingS - 0.034f, ab.LevelLandingS + 0.001f);   // its last frame at the level landing (a frame's rounding)
                output.WriteLine($"{N(air.RunJump(foot))}: off at {F(ab.TakeoffS)} s, apex {F(ab.ApexS, "0.000")} s, level landing {F(ab.LevelLandingS, "0.000")} s "
                                 + $"(its {F(assets.Clip(air.RunJump(foot)).Duration, "0.000")} s), into {ab.Land} (exit_phase {F(ln.ExitPhase)}); its last frame against "
                                 + $"{ab.Land}'s first: the joints at most {F(Seam(assets, kind, air.RunJump(foot), land).Most * 1000f, "0.0")} mm apart ({Seam(assets, kind, air.RunJump(foot), land).Bone}), "
                                 + $"the pelvis {F(Seam(assets, kind, air.RunJump(foot), land).Pelvis * 1000f, "0.0")} mm");
            }
        output.WriteLine($"jump: off at {F(jump.TakeoffS)} s, {F(jump.RiseM)} m at {F(jump.VUpMs)} m/s, apex {F(jump.ApexS, "0.000")} s; jump_natural off at {F(natural.TakeoffS)} s, "
                         + $"{F(natural.RiseM)} m; landings: " + string.Join(", ", air.Landings.Select(c => $"{N(c)} {F(Notes(assets, c).Landing!.DropLow)}-{F(Notes(assets, c).Landing!.DropHigh)} m")));
        // The landing chosen by the drop: in its band; below every band the lowest, above every band the highest.
        var soft = Notes(assets, air.Landings[0]).Landing!;
        Assert.Equal(air.Landings[0], air.Landing(assets, 0.5f * soft.DropHigh));
        // (the player's high landing shares land_high's band, after it: an earlier build takes the first in a band, and above every band the last.)
        int high = air.Landings.Where(c => Notes(assets, c).Landing!.Who != "player").Last();
        Assert.Equal(high, air.Landing(assets, 0.5f * (Notes(assets, high).Landing!.DropLow + Notes(assets, high).Landing!.DropHigh)));
        Assert.Equal(air.Landings[^1], air.Landing(assets, Notes(assets, air.Landings[^1]).Landing!.DropHigh + 2f));
        // The settings through a tape's note; none is the earlier build's.
        var tuned = new PersonAirSettings { Squash = false, ContactSeconds = 0.05f };
        Assert.Equal(tuned.Note(), new PersonAirSettings().WithNote(tuned.Note()).Note());
        Assert.Equal(PersonAirSettings.Off, new PersonAirSettings().WithNote(null));
        Assert.Equal(PersonAirSettings.Off, new PersonAirSettings().WithNote("off"));
        Assert.False(PersonAirSettings.Off.Any);
    }

    [Fact]
    public void The_player_s_standing_jump_leaves_after_its_squash_its_drawn_arc_on_the_capsule_s_and_lands_softly()
    {
        var (assets, kind, _) = Player();
        var air = Air(assets, kind);
        var jump = Notes(assets, air.Jump).Airborne!;
        float standBall = StandBall(assets, kind);
        var (w, e) = Make(assets, kind, new PersonAirSettings(), Vector3.Zero);
        var rows = Drive(w, e, assets, kind, 0f, Gait.Walk, press: 10, ticks: 260);
        // The squash: the clip from the press, the capsule on the ground until its take-off (12 ticks for 0.10 s), then off.
        Assert.Equal("jump", rows[10].Clip);
        int off = rows.FindIndex(r => !r.Ground);
        Say("standing jump", rows, 8, 30);
        Assert.Equal(10 + (int)MathF.Round(jump.TakeoffS / Tick), off);
        Assert.Equal(AirMode.Squash, rows[off - 1].Mode);
        // In the air its time by the arc: at the capsule's apex (its upward speed nought) the clip's apex.
        int apex = rows.FindIndex(off, r => r.Vz <= 0f);
        Assert.InRange(rows[apex].Time, jump.ApexS - 1.5f * Tick, jump.ApexS + 1.5f * Tick);
        // Past its end the fall loop; the contact a soft landing (a drop of nought), from the air pose with the contact blend.
        int contact = Contact(rows, off);
        Say("its landing", rows, contact - 4, contact + 10);
        Assert.Contains(rows.Skip(off).Take(contact - off), r => r.Clip == "fall");
        Assert.Equal(air.Landings[0], Clip(assets, rows[contact].Clip));
        Assert.Equal("fall", rows[contact].Previous);
        var (at, most, least, pelvis, pop) = Around(rows, contact, standBall);
        output.WriteLine($"the feet at contact {F(at * 100f, "0.0")} cm over the stand's (most {F(most * 100f, "0.0")}, least {F(least * 100f, "0.0")} over the next 12 ticks); "
                         + $"the pelvis over the capsule at most {F(pelvis * 1000f, "0.0")} mm a tick, its change a tick changing at most {F(pop * 1000f, "0.0")} mm");
        Assert.InRange(at, -0.02f, 0.06f);
        Assert.InRange(least, -0.03f, 0.06f);
        Assert.InRange(pop, 0f, 0.01f);
        // Then the stand.
        Assert.Equal("stand", rows[^1].Clip);
        Assert.True(Finite(w, e));
    }

    [Fact]
    public void Jump_at_once_leaves_on_the_press_and_enters_the_clip_at_its_take_off()
    {
        var (assets, kind, _) = Player();
        var air = Air(assets, kind);
        var jump = Notes(assets, air.Jump).Airborne!;
        var (w, e) = Make(assets, kind, new PersonAirSettings { Squash = false }, Vector3.Zero);
        var rows = Drive(w, e, assets, kind, 0f, Gait.Walk, press: 10, ticks: 40);
        Assert.False(rows[10].Ground);
        Assert.Equal("jump", rows[10].Clip);
        Assert.InRange(rows[10].Time, jump.TakeoffS, jump.TakeoffS + 2f * Tick);
        // The same capsule, tick for tick, the stand-in drawn.
        var (w14, e14) = Make(assets, kind, PersonAirSettings.Off, Vector3.Zero);
        Assert.False(w14.TryStore<PersonAir>(out _));
        var then = Drive(w14, e14, assets, kind, 0f, Gait.Walk, press: 10, ticks: 40);
        for (int i = 0; i < rows.Count; i++) Assert.Equal(then[i].Feet, rows[i].Feet);
        Assert.DoesNotContain(then, r => r.Clip is "jump" or "fall");
    }

    [Fact]
    public void A_drop_lands_by_its_height_softly_or_high()
    {
        var (assets, kind, _) = Player();
        var air = Air(assets, kind);
        float standBall = StandBall(assets, kind);
        foreach (float height in new[] { 1.0f, 2.5f })
        {
            // A ledge from x −40 to 0, walked off.
            var (w, e) = Make(assets, kind, new PersonAirSettings(), new Vector3(-2.5f, 0f, height), (new Vector3(-40, -40, 0), new Vector3(0, 40, height)));
            var rows = Drive(w, e, assets, kind, 1f, Gait.Walk, press: -1, ticks: 600, settle: 10, goFor: 360);
            int off = rows.FindIndex(r => !r.Ground);
            int contact = Contact(rows, off);
            Assert.True(contact > off && off > 0, "it walked off and landed");
            int want = air.Landing(assets, height);
            Say($"a drop of {F(height, "0.0")} m", rows, contact - 3, contact + 8);
            Assert.Equal(N(want), rows[contact].Clip);
            Assert.Equal("fall", rows[contact].Previous);
            var (at, most, least, pelvis, pop) = Around(rows, contact, standBall);
            output.WriteLine($"{F(height, "0.0")} m: {rows[contact].Clip}; the feet at contact {F(at * 100f, "0.0")} cm over the stand's (least {F(least * 100f, "0.0")}); "
                             + $"the pelvis over the capsule at most {F(pelvis * 1000f, "0.0")} mm a tick, its change a tick changing at most {F(pop * 1000f, "0.0")} mm");
            Assert.InRange(at, -0.02f, 0.06f);
            Assert.InRange(pop, 0f, 0.01f);
            // Stopped on landing: the landing to its end, then the stand (not the earlier build's stand-ins).
            int odd = rows.FindIndex(r => r.Clip.EndsWith(":Land") || r.Clip.EndsWith(":Fall") || r.Clip.EndsWith(":Jump"));
            if (odd >= 0) Say("a stand-in", rows, odd - 4, odd + 3);
            Assert.Equal("stand", rows[^1].Clip);
            Assert.DoesNotContain(rows, r => r.Clip.EndsWith(":Land") || r.Clip.EndsWith(":Fall") || r.Clip.EndsWith(":Jump"));
            // The earlier build's beside it (the stand-ins and the rig's dip).
            var (w14, e14) = Make(assets, kind, PersonAirSettings.Off, new Vector3(-2.5f, 0f, height), (new Vector3(-40, -40, 0), new Vector3(0, 40, height)));
            var then = Drive(w14, e14, assets, kind, 1f, Gait.Walk, press: -1, ticks: 600, settle: 10, goFor: 360);
            int c14 = Contact(then, then.FindIndex(r => !r.Ground));
            var (at14, most14, least14, pelvis14, pop14) = Around(then, c14, standBall);
            output.WriteLine($"  the clips off: {then[c14].Clip} (from {then[c14].Previous}); the feet at contact {F(at14 * 100f, "0.0")} cm over the stand's (most {F(most14 * 100f, "0.0")}, "
                             + $"least {F(least14 * 100f, "0.0")}); the pelvis at most {F(pelvis14 * 1000f, "0.0")} mm a tick, changing at most {F(pop14 * 1000f, "0.0")} mm");
        }

        string N(int c) => assets.Clip(c).Name;
    }

    /// <summary>A run ahead and a jump pressed; the rows from the press.</summary>
    static List<Row> RunningJump(CharacterAssets assets, int kind, out World w, out EntityId e, float from = 0f, params (Vector3 Min, Vector3 Max)[] blocks)
    {
        (w, e) = Make(assets, kind, new PersonAirSettings(), new Vector3(0f, 0f, from), blocks);
        return Drive(w, e, assets, kind, 1f, Gait.Run, press: 0, ticks: 200, settle: 150);
    }

    [Fact]
    public void A_running_jump_goes_off_the_planted_foot_and_lands_level_into_its_running_landing_back_to_back_then_the_run()
    {
        var (assets, kind, _) = Player();
        var air = Air(assets, kind);
        Assert.SkipUnless(air.RunJumps.All(c => c >= 0), "the models repository's files are too old (no running jumps carried to the landing)");
        float standBall = StandBall(assets, kind);
        var rows = RunningJump(assets, kind, out var w, out var e);
        Assert.StartsWith("jump_run_", rows[0].Clip);
        var ab = Notes(assets, Clip(assets, rows[0].Clip)).Airborne!;
        int off = rows.FindIndex(r => !r.Ground);
        // It left as its clip's feet did: at its take-off, entered where the run had its foot down.
        Assert.InRange(rows[off].Time, ab.TakeoffS, ab.TakeoffS + 2.5f * Tick);
        int contact = Contact(rows, off);
        Say("running jump", rows, contact - 6, contact + 6);
        Assert.Equal(ab.Land, rows[contact].Clip);
        Assert.Equal("-", rows[contact].Previous);   // back to back: no blend
        // Its whole flight run (to within PeopleAir.LevelWithin: the clip ends on its level landing, the arc's time a rounding short of it).
        Assert.InRange(rows[contact - 1].Time, assets.Clip(Clip(assets, rows[0].Clip)).Duration - PeopleAir.LevelWithin, assets.Clip(Clip(assets, rows[0].Clip)).Duration + 1e-4f);
        var (at, most, least, pelvis, pop) = Around(rows, contact, standBall);
        output.WriteLine($"{rows[0].Clip} (entered at {F(rows[0].Time, "0.000")} s, off {F(off * Tick, "0.000")} s after the press) into {rows[contact].Clip}; the feet at contact "
                         + $"{F(at * 100f, "0.0")} cm over the stand's; the pelvis over the capsule at most {F(pelvis * 1000f, "0.0")} mm a tick, its change a tick changing at most "
                         + $"{F(pop * 1000f, "0.0")} mm (the files' seam: {F(Seam(assets, kind, Clip(assets, rows[0].Clip), Clip(assets, rows[contact].Clip)).Pelvis * 1000f, "0.0")} mm)");
        Assert.InRange(at, -0.02f, 0.04f);
        // Back to back with no blend (the notes): the pelvis steps by what the files' seam has, and no more.
        Assert.InRange(pop, 0f, Seam(assets, kind, Clip(assets, rows[0].Clip), Clip(assets, rows[contact].Clip)).Pelvis + 0.008f);
        // The run after it at its exit_phase, with no fade.
        int run = rows.FindIndex(contact, r => r.Clip.Contains(":run@"));
        Assert.True(run > contact);
        Assert.Equal("-", rows[run].Previous);
        var ln = Notes(assets, Clip(assets, rows[contact].Clip)).Landing!;
        float loopD = assets.Clip(assets.Kind(kind).Person!.Run!.First).Duration;
        float phase = rows[run].Time / loopD - ln.ExitPhase;
        phase -= MathF.Round(phase);
        Assert.InRange(phase, -0.02f, 0.06f);
        Assert.True(Finite(w, e));
    }

    [Fact]
    public void A_running_jump_off_a_drop_holds_its_last_frame_and_one_onto_a_step_up_is_cut_into_its_landing()
    {
        var (assets, kind, _) = Player();
        var air = Air(assets, kind);
        Assert.SkipUnless(air.RunJumps.All(c => c >= 0), "the models repository's files are too old");
        Assert.SkipUnless(Notes(assets, air.RunJumps[0]).Airborne!.Holds, "a running jump carries its stride on in fall_run by the rule (OverClipTests), not held");
        // A drop: the run on a ledge 1 m high that ends 0.5 m past where the jump leaves it (the same run on level ground first, to know where).
        var level = RunningJump(assets, kind, out _, out _);
        float edge = level[level.FindIndex(r => !r.Ground)].Feet.X + 0.5f;
        var drop = RunningJump(assets, kind, out _, out _, 1f, (new Vector3(-40, -40, 0), new Vector3(edge, 40, 1f)));
        int off = drop.FindIndex(r => !r.Ground);
        int contact = Contact(drop, off);
        var jump = Clip(assets, drop[0].Clip);
        float d = assets.Clip(jump).Duration;
        int held = drop.Skip(off).Take(contact - off).Count(r => r.Clip == drop[0].Clip && MathF.Abs(r.Time - d) < 1e-4f);
        Say("a running jump off a 1 m ledge", drop, contact - 4, contact + 3);
        Assert.True(drop[contact - 1].Feet.Z < 0.5f, "it came down past the ledge");
        Assert.True(held >= 5, $"the last frame held {held} ticks");
        Assert.Equal(Notes(assets, jump).Airborne!.Land, drop[contact].Clip);
        Assert.Equal("-", drop[contact].Previous);
        // A step up: a block 0.35 m high that it lands on before the jump's flight is over.
        var up = RunningJump(assets, kind, out var w, out var e, 0f, (new Vector3(4.6f, -40, 0), new Vector3(40, 40, 0.35f)));
        int offUp = up.FindIndex(r => !r.Ground);
        int contactUp = Contact(up, offUp);
        Say("a running jump onto a 0.35 m step", up, contactUp - 3, contactUp + 3);
        var jumpUp = Clip(assets, up[0].Clip);
        Assert.True(up[contactUp].Feet.Z > 0.3f, "it landed on the step");
        Assert.True(up[contactUp - 1].Time < assets.Clip(jumpUp).Duration - 2f * Tick, "the ground came before the clip's end");
        Assert.Equal(Notes(assets, jumpUp).Airborne!.Land, up[contactUp].Clip);
        Assert.Equal(up[0].Clip, up[contactUp].Previous);   // cut in, faded
        output.WriteLine($"the drop: {drop[0].Clip}'s last frame held {held} ticks; the step: cut into {up[contactUp].Clip} at {F(up[contactUp - 1].Time, "0.000")} s of its {F(assets.Clip(jumpUp).Duration, "0.000")}");
        Assert.True(Finite(w, e));
    }

    [Fact]
    public void Without_the_clips_or_with_the_air_clips_off_switch_the_world_has_no_store()
    {
        var (assets, kind, _) = Player();
        var (w14, _) = Make(assets, kind, PersonAirSettings.Off, Vector3.Zero);
        Assert.False(w14.TryStore<PersonAir>(out _));
        var (w, _) = Make(assets, kind, new PersonAirSettings(), Vector3.Zero);
        Assert.Equal(assets.Kind(kind).Person!.Air is not null, w.TryStore<PersonAir>(out _));
    }
}
