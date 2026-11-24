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
/// The models repository's prelandings (<see cref="PeoplePrelanding"/>, <see cref="PersonPrelandingSettings"/>) on the body's own motor and the
/// player's spring: the notes read; the prelanding chosen with the landing by the drop's band and by who lands; begun from its first frame its lead before the
/// contact, faster to fit a shorter fall, never part way in; the landing begun on the contact's tick from its first frame (the prelanding's last) with no
/// blend; a standing jump's descent; the switch and a set without them digesting as before. Every expectation from the clips' notes. Skips
/// without the models repository's files, or.
/// </summary>
/// <remarks>These are the earlier build's rules (<see cref="PersonPrelandingSettings.WithoutCommit"/>: faster to fit, the fade in its own time); the earlier build's short falls and the
/// fade in game time are <see cref="AirCommitTests"/>'.</remarks>
public sealed class PrelandingClipTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    static PersonAirClips Air(CharacterAssets assets, int kind)
    {
        var person = assets.Kind(kind).Person!;
        Assert.SkipUnless(person.Air is { Prelandings.Length: > 0 } && person.Over is not null, "the models repository's files are too old (no prelandings)");
        return person.Air!;
    }

    static ClipNotes Notes(CharacterAssets assets, int clip) => ((INotedClip)assets.Clip(clip)).Notes;

    static int Clip(CharacterAssets assets, string name)
    {
        for (int i = 0; i < assets.ClipCount; i++) if (assets.Clip(i).Name == name) return i;
        return -1;
    }

    /// <summary>
    /// A world with the motor (the player's spring), the animation with the air's, the moves' and the ledge's clips and <paramref name="pre"/> (null: none
    /// given), one body of the kind (its air's curve <paramref name="curve"/>: 1 the player's) facing +X at <paramref name="at"/>; the floor at 0 and
    /// <paramref name="blocks"/>.
    /// </summary>
    static (World World, EntityId Body) Make(CharacterAssets assets, int kind, PersonPrelandingSettings? pre, Vector3 at, int curve = 1,
                                             params (Vector3 Min, Vector3 Max)[] blocks)
    {
        var t = new TestLevel().Floor(-40, -40, 200, 40, cell: 4f);
        foreach (var (min, max) in blocks) t.Box(min, max);
        var level = new LevelSpace("floor", t.Build("floor").Collision) { Places = LevelPlaces.None };
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        StartStopSprings.Register(b, new StartStopSprings { Default = 0.2f });
        StartStopSprings.RegisterPlayer(b);
        b.Animation(assets, starts: new PersonStartSettings(), air: new PersonAirSettings(), over: new PersonOverSettings(), ledge: new PersonLedgeSettings(), prelanding: pre);
        BodyMotionSystem.Register(b);
        var w = b.Build();
        var e = MovementServices.SpawnControlled(w, at + new Vector3(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        w.Remove<Controlled>(e);
        w.Set(e, CharacterAnimation.For(assets, kind));
        w.Set(e, MotionInput.Standing);
        if (w.TryStore<PersonStep>(out _)) w.Set(e, new PersonStep { Curve = curve });
        if (w.TryStore<PersonAir>(out _)) w.Set(e, new PersonAir { Curve = curve });
        if (w.TryStore<PersonOver>(out _)) w.Set(e, new PersonOver());
        if (w.TryStore<PersonLedge>(out _) && curve == 1) w.Set(e, new PersonLedge());
        return (w, e);
    }

    /// <summary>
    /// A tick as it went: the clip, its time and rate, the fade, the air's mode, the body, and as drawn over the capsule's feet (the drawn body's own
    /// motion, the flight taken out) the pelvis and both hands.
    /// </summary>
    sealed record Row(string Clip, float Time, float Rate, string Previous, float Blend, AirMode Mode, bool Ground, Vector3 Feet, float Vz, Vector3 Pelvis,
                      Vector3 HandL, Vector3 HandR);

    /// <summary><paramref name="ticks"/> ticks, standing (jump pressed on tick <paramref name="press"/>, −1 never, or going ahead at <paramref name="ahead"/> of the walk); every tick's row.</summary>
    static List<Row> Drive(World w, EntityId e, CharacterAssets assets, int kind, int ticks, int press = -1, float ahead = 0f)
    {
        var rows = new List<Row>(ticks);
        var rig = assets.Kind(kind).Person!.Rig;
        for (int i = 0; i < ticks; i++)
        {
            ref var intent = ref w.Get<MoveIntent>(e);
            intent.Direction = new Vector2(ahead, 0f);
            intent.Gait = Gait.Walk;
            intent.Faces = true;
            intent.Facing = 0f;
            intent.Jump = i == press;
            intent.JumpHeld = i == press;
            w.Step();
            var body = w.Get<Body>(e);
            var a = w.Get<CharacterAnimation>(e);
            var mode = w.TryStore<PersonAir>(out var airs) && airs.Has(e) ? airs.Get(e).Mode : AirMode.None;
            rows.Add(new Row(assets.Clip(a.Clip).Name, a.Time, a.Rate, a.PreviousClip >= 0 ? assets.Clip(a.PreviousClip).Name : "-", a.PreviousClip >= 0 ? a.Blend : 1f,
                             mode, body.State == BodyState.Ground, body.Feet, body.Velocity.Z, Bone(w, e, assets, rig.Pelvis) - body.Feet,
                             Bone(w, e, assets, rig.LeftArm.Tip) - body.Feet, Bone(w, e, assets, rig.RightArm.Tip) - body.Feet));
        }
        return rows;
    }

    /// <summary>The first tick from <paramref name="from"/> on the ground after being in the air (the contact), or −1.</summary>
    static int Contact(List<Row> rows, int from = 1)
    {
        for (int i = Math.Max(1, from); i < rows.Count; i++) if (rows[i].Ground && !rows[i - 1].Ground) return i;
        return -1;
    }

    /// <summary>The largest change a tick of the change a tick (m: a pop) of <paramref name="of"/> (over the capsule's feet) from <paramref name="from"/> to <paramref name="to"/>.</summary>
    static float Pop(List<Row> rows, Func<Row, Vector3> of, int from, int to)
    {
        float pop = 0f;
        for (int i = Math.Max(2, from); i < Math.Min(rows.Count, to); i++)
            pop = MathF.Max(pop, ((of(rows[i]) - of(rows[i - 1])) - (of(rows[i - 1]) - of(rows[i - 2]))).Length());
        return pop;
    }

    void Say(string what, List<Row> rows, int from, int to)
    {
        output.WriteLine(what);
        for (int i = Math.Max(0, from); i < Math.Min(rows.Count, to); i++)
        {
            var r = rows[i];
            output.WriteLine($"  {i,4} {r.Clip,-24} {F(r.Time, "0.0000")} x{F(r.Rate, "0.00")} {(r.Previous != "-" ? $"<{r.Previous} {F(r.Blend)}" : ""),-22} {r.Mode,-5} "
                             + $"{(r.Ground ? "ground" : "air"),-6} z {F(r.Feet.Z, "0.000")} vz {F(r.Vz, "0.00")} pelvis {F(r.Pelvis.Z, "0.000")} hand {F(r.HandL.X, "0.000")},{F(r.HandL.Z, "0.000")}");
        }
    }

    /// <summary>What a fall did: the prelanding's first tick and name, its rate, the contact, the landing and its fade, and the pops.</summary>
    sealed record Fell(int Start, string Pre, float Rate, int Contact, string Land, string LandPrevious, float LandTime, float PreLeft, float PreRate,
                       float PelvisIn, float HandIn, float PelvisLand, float HandLand);

    static Fell Read(List<Row> rows, int from = 1)
    {
        int contact = Contact(rows, from);
        Assert.True(contact > 0, "it landed");
        int start = rows.FindIndex(from, r => r.Mode == AirMode.Pre);
        if (start < 0)
        {
            // None: "into" is into the fall loop.
            int fall = rows.FindIndex(from, r => r.Mode == AirMode.Fall);
            float pi = fall < 0 ? 0f : Pop(rows, r => r.Pelvis, fall - 2, fall + 6);
            float hi = fall < 0 ? 0f : MathF.Max(Pop(rows, r => r.HandL, fall - 2, fall + 6), Pop(rows, r => r.HandR, fall - 2, fall + 6));
            return new Fell(-1, fall < 0 ? "-" : "(the fall)", 0f, contact, rows[contact].Clip, rows[contact].Previous, rows[contact].Time, float.NaN, float.NaN, pi, hi,
                            Pop(rows, r => r.Pelvis, contact - 2, contact + 6), MathF.Max(Pop(rows, r => r.HandL, contact - 2, contact + 6), Pop(rows, r => r.HandR, contact - 2, contact + 6)));
        }
        var before = rows[contact - 1];
        return new Fell(start, rows[start].Clip, rows[start + 1].Rate, contact, rows[contact].Clip, rows[contact].Previous, rows[contact].Time,
                        before.Mode == AirMode.Pre ? before.Time : float.NaN, before.Rate,
                        Pop(rows, r => r.Pelvis, start - 2, start + 6), MathF.Max(Pop(rows, r => r.HandL, start - 2, start + 6), Pop(rows, r => r.HandR, start - 2, start + 6)),
                        Pop(rows, r => r.Pelvis, contact - 2, contact + 6), MathF.Max(Pop(rows, r => r.HandL, contact - 2, contact + 6), Pop(rows, r => r.HandR, contact - 2, contact + 6)));
    }

    string Line(string what, Fell f, float lead) =>
        $"{what}: {f.Pre} from tick {f.Start} ({F((f.Contact - f.Start) * Tick, "0.000")} s before the contact, its lead {F(lead)}), rate {F(f.Rate, "0.000")}; "
        + $"at tick {f.Contact} {f.Land} at {F(f.LandTime, "0.000")}{(f.LandPrevious != "-" ? $" faded from {f.LandPrevious}" : " (no blend)")}, the prelanding "
        + $"{F(f.PreLeft, "0.0000")} s in the tick before; pops (mm, per tick, over the capsule): into the prelanding pelvis {F(f.PelvisIn * 1000f, "0.0")} hands "
        + $"{F(f.HandIn * 1000f, "0.0")}, into the landing pelvis {F(f.PelvisLand * 1000f, "0.0")} hands {F(f.HandLand * 1000f, "0.0")}";

    [Fact]
    public void The_notes_are_read_for_both_bodies()
    {
        foreach (var (who, (assets, kind, _)) in new[] { ("man", Player()), ("woman", Woman()) })
        {
            var air = Air(assets, kind);
            Assert.Equal(3, air.Prelandings.Length);
            foreach (int p in air.Prelandings)
            {
                var n = Notes(assets, p);
                var pre = n.Airborne!.Prelanding!;
                int land = Clip(assets, pre.Then);
                output.WriteLine($"{who}: {assets.Clip(p).Name} {F(assets.Clip(p).Duration, "0.000")} s, lead {F(pre.LeadS)}, from {pre.From}, then {pre.Then} "
                                 + $"(its after {Notes(assets, land).Airborne?.After}, band {F(Notes(assets, land).Landing!.DropLow)}-{F(Notes(assets, land).Landing!.DropHigh)}"
                                 + $"{(Notes(assets, land).Landing!.Who is { Length: > 0 } w ? ", " + w : "")})");
                Assert.False(assets.Clip(p).Loops);
                Assert.True(pre.LeadS > 0f);
                Assert.Equal(pre.LeadS, assets.Clip(p).Duration, 2);
                Assert.Equal("fall", pre.From);
                Assert.True(air.Stands(land), $"{pre.Then} is a standing landing");
                Assert.Equal(assets.Clip(p).Name, Notes(assets, land).Airborne?.After);
                Assert.Equal(p, air.PrelandingFor(assets, land));
                Assert.True(air.Holds(p));
                Assert.False(air.Stands(p));
            }
            Assert.True(air.PlayerLanding >= 0);
        }
    }

    [Fact]
    public void The_prelanding_is_chosen_with_the_landing_by_the_drop_s_band_and_by_who_lands()
    {
        var (assets, kind, _) = Player();
        var air = Air(assets, kind);
        foreach (var (h, curve) in new[] { (1.0f, 1), (2.5f, 1), (1.0f, 2), (2.5f, 2) })
        {
            var (w, e) = Make(assets, kind, new PersonPrelandingSettings().WithoutCommit, new Vector3(0f, 0f, h), curve);
            var rows = Drive(w, e, assets, kind, 180);
            var f = Read(rows);
            int land = Clip(assets, f.Land);
            float drop = w.Get<Body>(e).LastFallHeight;
            int want = air.Standing(assets, drop, curve == 1);
            output.WriteLine(Line($"{(curve == 1 ? "the player" : "everyone else")} falling {F(h)} m (drop {F(drop, "0.000")})", f, 0.3f));
            Assert.True(f.Start > 0, "a prelanding played");
            Assert.Equal(want, land);
            Assert.Equal(assets.Clip(air.PrelandingFor(assets, want)).Name, f.Pre);
            Assert.Equal(curve == 1 && h > 1.2f, Notes(assets, land).Landing!.Who == "player");
            Assert.True(Notes(assets, land).Landing!.For(drop), $"{f.Land}'s band holds {drop}");
        }
    }

    [Fact]
    public void It_begins_from_its_first_frame_its_lead_before_the_contact_and_the_landing_carries_on_from_its_last()
    {
        foreach (var (who, (assets, kind, _)) in new[] { ("man", Player()), ("woman", Woman()) })
        {
            Air(assets, kind);
            foreach (float h in new[] { 1.0f, 2.5f })
            {
                var (w, e) = Make(assets, kind, new PersonPrelandingSettings().WithoutCommit, new Vector3(0f, 0f, h));
                var rows = Drive(w, e, assets, kind, 180);
                var f = Read(rows);
                int pre = Clip(assets, f.Pre);
                float lead = Notes(assets, pre).Airborne!.Prelanding!.LeadS, d = assets.Clip(pre).Duration;
                Say($"{who} falling {F(h)} m", rows, f.Start - 2, f.Start + 3);
                Say("…its contact", rows, f.Contact - 3, f.Contact + 3);
                output.WriteLine(Line($"{who} falling {F(h)} m", f, lead));
                // From its first frame, drawn on its first tick, its lead before the contact (in whole ticks), at its own rate.
                Assert.Equal(0f, rows[f.Start].Time);
                Assert.Equal(AirMode.Fall, rows[f.Start - 1].Mode);
                Assert.InRange((f.Contact - f.Start) * Tick, lead - 1.5f * Tick, lead + 1e-4f);
                Assert.InRange(f.Rate, 0.95f, d / ((f.Contact - f.Start) * Tick) + 0.05f);
                // The landing on the contact's tick from its first frame with no blend; the prelanding the tick before within a tick or two of its end.
                Assert.Equal(f.Pre, Notes(assets, Clip(assets, f.Land)).Airborne?.After);
                Assert.Equal(0f, f.LandTime);
                Assert.Equal("-", f.LandPrevious);
                Assert.InRange(d - f.PreLeft, 1e-5f, 2.5f * Tick * MathF.Max(1f, f.PreRate) + 1e-4f);
                for (int i = f.Start; i < f.Contact; i++) Assert.Equal(AirMode.Pre, rows[i].Mode);
            }
        }
    }

    [Fact]
    public void Short_of_time_it_plays_faster_to_fit_never_part_way_in()
    {
        var (assets, kind, _) = Player();
        Air(assets, kind);
        foreach (float h in new[] { 0.35f, 0.2f, 0.12f })
        {
            var (w, e) = Make(assets, kind, new PersonPrelandingSettings().WithoutCommit, new Vector3(0f, 0f, h));
            var rows = Drive(w, e, assets, kind, 120);
            var f = Read(rows);
            int fall = rows.FindIndex(r => r.Mode == AirMode.Fall);
            output.WriteLine(Line($"falling {F(h)} m (the fall loop from tick {fall}, {F((f.Contact - fall) * Tick, "0.000")} s before the contact)", f, 0.3f));
            var (w14, e14) = Make(assets, kind, PersonPrelandingSettings.Off, new Vector3(0f, 0f, h));
            output.WriteLine(Line($"  no prelanding: falling {F(h)} m", Read(Drive(w14, e14, assets, kind, 120)), 0.3f));
            if (f.Start < 0)
            {
                // The fall never came (slower than FallFrom to the ground), or under PeoplePrelanding.Fewest ticks from it: the earlier build's landing.
                Assert.True(fall < 0 || f.Contact - fall < PeoplePrelanding.Fewest, "no prelanding only with too little fall");
                continue;
            }
            Say($"falling {F(h)} m", rows, f.Start - 1, f.Contact + 2);
            int pre = Clip(assets, f.Pre);
            float d = assets.Clip(pre).Duration, lead = Notes(assets, pre).Airborne!.Prelanding!.LeadS;
            float ticks = (f.Contact - f.Start) * Tick;
            Assert.True(ticks < lead, "short of its lead");
            Assert.Equal(0f, rows[f.Start].Time);   // never part way in
            Assert.InRange(f.Rate, d / (ticks + 1.01f * Tick), d / (ticks - 0.99f * Tick));   // lead_s over the time to contact (foreseen to a tick)
            Assert.True(f.Rate > 1.05f);
            Assert.Equal(0f, f.LandTime);
        }
    }

    [Fact]
    public void A_standing_jump_s_descent_plays_it_from_the_jump_s_clip()
    {
        var (assets, kind, _) = Player();
        var air = Air(assets, kind);
        var (w, e) = Make(assets, kind, new PersonPrelandingSettings().WithoutCommit, Vector3.Zero);
        var rows = Drive(w, e, assets, kind, 240, press: 60);
        var f = Read(rows, from: 60);
        int apex = rows.FindIndex(60, r => !r.Ground && r.Vz <= 0f);
        Say("a standing jump", rows, f.Start - 3, f.Start + 3);
        Say("…its contact", rows, f.Contact - 2, f.Contact + 3);
        output.WriteLine(Line($"a standing jump (its apex at tick {apex}; before the prelanding {rows[f.Start - 1].Clip} in {rows[f.Start - 1].Mode})", f, 0.3f));
        Assert.True(f.Start > apex);
        Assert.Equal("prelanding_soft", f.Pre);
        Assert.Equal("land_soft", f.Land);
        Assert.Equal("-", f.LandPrevious);
        Assert.Equal(0f, rows[f.Start].Time);
        Assert.Contains(rows[f.Start].Previous, new[] { assets.Clip(air.Jump).Name, assets.Clip(air.Fall).Name });
        var (w14, e14) = Make(assets, kind, PersonPrelandingSettings.Off, Vector3.Zero);
        output.WriteLine(Line("no prelanding: standing jump", Read(Drive(w14, e14, assets, kind, 240, press: 60), from: 60), 0.3f));
    }

    [Fact]
    public void The_contact_under_one_tick_away_is_foreseen_as_one_tick()
    {
        var level = new LevelSpace("floor", new TestLevel().Floor(-10, -10, 10, 10).Build("floor").Collision) { Places = LevelPlaces.None };
        var loco = Locomotion.Sandbox;
        var (n, t) = PeoplePrelanding.ToContact(new OffWorld(level.Collision, loco, new Vector3(0f, 0f, 0.02f), new Vector3(0f, 0f, -3f)), 0.5f);
        output.WriteLine($"2 cm over the floor at 3 m/s down: {n} tick(s), {F(t, "0.0000")} s");
        Assert.Equal(1, n);
        Assert.True(n < PeoplePrelanding.Fewest);
        var (n2, t2) = PeoplePrelanding.ToContact(new OffWorld(level.Collision, loco, new Vector3(0f, 0f, 1f), Vector3.Zero), 0.5f);
        // From 1 m standing still: met when the feet come within PeoplePrelanding.Meets of the floor; the drop counted to the skin the feet then rest at.
        Assert.Equal((int)MathF.Ceiling(MathF.Sqrt(2f * (1f - PeoplePrelanding.Meets) / loco.Gravity) / Tick), n2);
        Assert.Equal(1f - BodyMotor.Skin, PeoplePrelanding.Drop(new OffWorld(level.Collision, loco, new Vector3(0f, 0f, 1f), Vector3.Zero), 1f, t2), 3);
    }

    [Fact]
    public void Off_the_switch_and_on_a_set_without_them_it_digests_as_with_no_prelanding()
    {
        var (assets, kind, _) = Player();
        bool has = assets.Kind(kind).Person!.Air is { Prelandings.Length: > 0 };
        foreach (float h in new[] { 1.0f, 2.5f })
        {
            var (w0, e0) = Make(assets, kind, null, new Vector3(0f, 0f, h));
            var (w1, e1) = Make(assets, kind, PersonPrelandingSettings.Off, new Vector3(0f, 0f, h));
            var (w2, e2) = Make(assets, kind, new PersonPrelandingSettings().WithoutCommit, new Vector3(0f, 0f, h));
            // Tick by tick: the earlier build's (no prelanding given), the switch, and the prelanding on.
            var r0 = new List<Row>(); var r1 = new List<Row>(); var r2 = new List<Row>();
            int differ = 0;
            for (int k = 0; k < 150; k++)
            {
                r0.AddRange(Drive(w0, e0, assets, kind, 1, press: k == 100 ? 0 : -1));
                r1.AddRange(Drive(w1, e1, assets, kind, 1, press: k == 100 ? 0 : -1));
                r2.AddRange(Drive(w2, e2, assets, kind, 1, press: k == 100 ? 0 : -1));
                Assert.Equal(w0.Digest(), w1.Digest());
                if (w0.Digest() != w2.Digest()) differ++;
            }
            Assert.DoesNotContain(r1, r => r.Mode == AirMode.Pre || r.Clip.StartsWith("prelanding", StringComparison.Ordinal));
            if (has)
            {
                Assert.Contains(r2, r => r.Mode == AirMode.Pre);
                Assert.True(differ > 0, "the drawn body (digested) differs while it plays");
                output.WriteLine($"falling {F(h)} m and a standing jump: {differ} of 150 ticks digest otherwise than no prelanding: ({r2.Count(r => r.Mode == AirMode.Pre)} in a prelanding)");
                // The capsule's flight is untouched: the body's every tick as before.
                for (int i = 0; i < r0.Count; i++) Assert.Equal(r0[i].Feet, r2[i].Feet);
                var f1 = Read(r1);
                output.WriteLine(Line($"no prelanding: falling {F(h)} m", f1, 0.3f));
            }
            else
            {
                Assert.Equal(0, differ);
                output.WriteLine($"no prelandings in this set: falling {F(h)} m digests as no prelanding:");
            }
        }
    }
}
