// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The swing's arc (<c>Presentation/SwingArc.cs</c>): with the 1.1 install, the blade predicted before a blow is
/// the blade contact sweeps (the swing's trail), place for place, for a standing body, each blow and pitch; during a blow the
/// rest of it; the next blow's choice (the combo's turn, the strong blow once attack is held long enough, a buffered one);
/// the prediction changes nothing (the world's digest) and allocates nothing once made; the ribbon's quads lie on the shown part.
/// </summary>
public sealed class SwingArcTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static (World World, Dictionary<string, int> Kinds) Make(Func<TestLevel, TestLevel>? more = null)
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var assets = new CharacterAssets();
        var kinds = new Dictionary<string, int>
        {
            ["Christof"] = OneOneLibrary.Register(assets, Library.Value!.Character("Christof", "BroadSword")!),
            ["Fists"] = OneOneLibrary.Register(assets, Library.Value!.Character("Christof")!),
            ["Civ12male"] = OneOneLibrary.Register(assets, Library.Value!.Character("Civ12male")!),
        };
        var b = new WorldBuilder(7);
        var level = new TestLevel().Floor(-60, -60, 60, 60);
        MovementServices.Register(b, (more is null ? level : more(level)).Build());
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        ContactServices.Register(b, new ContactConfig());
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        return (w, kinds);
    }

    static EntityId Put(World w, Dictionary<string, int> kinds, string kind, float x, float y, float yawDegrees)
    {
        var e = MovementServices.SpawnBody(w, new(x, y, 0.01f), yawDegrees * DetMath.DegToRad, Locomotion.Sandbox);
        w.Set(e, CharacterAnimation.For(w.Service<CharacterAssets>(), kinds[kind]));
        w.Set(e, MotionInput.Standing);
        CombatServices.Arm(w, e);
        return e;
    }

    /// <summary>The largest distance between the predicted places and the trail's, place for place (tip and guard), m; and the counts.</summary>
    static (float Tip, float Guard, int Predicted, int Swept) Gap(ReadOnlySpan<SwingArcPlace> predicted, List<BladePlacement> swept, float levelAt = -1f)
    {
        float tip = 0f, guard = 0f;
        int n = Compared(predicted.Length, swept.Count, levelAt);
        for (int i = 0; i < n; i++)
        {
            tip = MathF.Max(tip, Vector3.Distance(predicted[i].Blade.Tip, swept[i].Tip));
            guard = MathF.Max(guard, Vector3.Distance(predicted[i].Blade.Guard, swept[i].Guard));
        }
        return (tip, guard, predicted.Length, swept.Count);
    }

    /// <summary>
    /// <paramref name="a"/> aims <paramref name="aimDegrees"/>, stands, has its next quick variant set, presses attack (held
    /// <paramref name="holdTicks"/>), and the arc is predicted on the tick before the blow begins; then the blow plays out and its
    /// trail is read. Answers the prediction's places (copied) and the trail's.
    /// </summary>
    static (SwingArcPlace[] Predicted, SwingArcBlow Blow, List<BladePlacement> Swept, float LevelAt) Swing(World w, EntityId a, int variant, int holdTicks, float aimDegrees)
    {
        ref var intent = ref w.Get<CombatIntent>(a);
        (intent.Aims, intent.AimPitch) = (true, aimDegrees * DetMath.DegToRad);
        for (int i = 0; i < 160; i++) MovementServices.Step(w, ActionFrame.Idle);     // the last blow's busy time and bend run out
        w.Get<Combatant>(a).NextVariant = variant;
        w.Get<CombatIntent>(a).Attack = true;
        for (int i = 0; i < holdTicks; i++) MovementServices.Step(w, ActionFrame.Idle);
        var arcs = new SwingArcs();
        Assert.True(arcs.Predict(w, a));
        var predicted = arcs.Places.ToArray();
        var blow = arcs.Blow;
        float levelAt = arcs.LevelAt;
        w.Get<CombatIntent>(a).Attack = false;
        int swing = w.Get<Combatant>(a).Swings + 1;
        for (int i = 0; i < 200; i++) MovementServices.Step(w, ActionFrame.Idle);
        var trails = w.Service<SwingTrails>();
        Assert.True(trails.Latest(a, out var trail));
        Assert.Equal(swing, trail.Swing);
        var swept = new List<BladePlacement>();
        trails.Places(trail, swept);
        return (predicted, blow, swept, levelAt);
    }

    /// <summary>
    /// The places compared place for place — both have them, and where the blade meets the level, those before the meeting
    /// (the trail's last place is cut at it; the arc goes on past it, faint: the whole front path).
    /// </summary>
    static int Compared(int predicted, int swept, float levelAt) =>
        levelAt >= 0f ? Math.Min(Math.Min(predicted, swept), (int)MathF.Floor(levelAt) + 1) : Math.Min(predicted, swept);

    [Theory]
    [InlineData("quick1", 0, 1, -25f)]
    [InlineData("quick1", 0, 1, 0f)]
    [InlineData("quick1", 0, 1, 20f)]
    [InlineData("quick2", 1, 1, -25f)]
    [InlineData("quick2", 1, 1, 0f)]
    [InlineData("quick2", 1, 1, 20f)]
    [InlineData("strong", 0, 48, -25f)]
    [InlineData("strong", 0, 48, 0f)]
    [InlineData("strong", 0, 48, 20f)]
    public void Standing_the_arc_is_the_blade_contact_sweeps_place_for_place(string blow, int variant, int holdTicks, float aim)
    {
        var (w, kinds) = Make();
        var a = Put(w, kinds, "Christof", 0, 0, 90);
        var (predicted, which, swept, levelAt) = Swing(w, a, variant, holdTicks, aim);
        var gap = Gap(predicted, swept, levelAt);
        output.WriteLine($"{blow} at {aim:+0;-0;0}°: {which}, {gap.Predicted} places predicted, {gap.Swept} swept{(levelAt >= 0f ? $", the level met at place {levelAt:0.00}" : "")}; largest gap tip {gap.Tip * 1000f:0.###} mm, guard {gap.Guard * 1000f:0.###} mm");
        Assert.Equal(holdTicks > 1 ? SwingArcBlow.Strong : SwingArcBlow.Quick, which);
        // Where the blade meets the floor (the strong blow's tip touches it as its window closes) the trail's last place is cut
        // there (SwingTrails.Record); the arc goes on past it (the whole front path, faint), and says where (LevelAt).
        if (levelAt >= 0f)
        {
            Assert.Equal((int)MathF.Floor(levelAt) + 2, swept.Count);
            Assert.True(predicted.Length >= swept.Count);
        }
        else Assert.Equal(swept.Count, predicted.Length);
        Assert.InRange(gap.Tip, 0f, 1e-4f);
        Assert.InRange(gap.Guard, 0f, 1e-4f);
    }

    [Theory]
    [InlineData(0, 25f)]
    [InlineData(0, 0f)]
    [InlineData(0, -25f)]
    [InlineData(1, 0f)]
    public void The_arc_marks_the_part_the_blade_meets_first(int variant, float aim)
    {
        var (w, kinds) = Make();
        var a = Put(w, kinds, "Christof", 0, 0, 90);
        var t = Put(w, kinds, "Civ12male", 0, 1.3f, -90);
        ref var intent = ref w.Get<CombatIntent>(a);
        (intent.Aims, intent.AimPitch) = (true, aim * DetMath.DegToRad);
        for (int i = 0; i < 60; i++) MovementServices.Step(w, ActionFrame.Idle);
        w.Get<Combatant>(a).NextVariant = variant;
        w.Get<CombatIntent>(a).Attack = true;
        MovementServices.Step(w, ActionFrame.Idle);
        var arcs = new SwingArcs();
        Assert.True(arcs.Predict(w, a));
        w.Get<CombatIntent>(a).Attack = false;
        ContactMade? first = null;
        for (int i = 0; i < 200 && first is null; i++)
        {
            MovementServices.Step(w, ActionFrame.Idle);
            foreach (var c in w.Events<ContactMade>().ThisTick) if (c.Attacker == a) first ??= c;
        }
        output.WriteLine($"variant {variant} at {aim:+0;-0;0}°: the arc meets {(arcs.Meets.IsNone ? "no one" : arcs.MeetsShield ? "a shield" : BodyRegions.Name(arcs.MeetsRegion))} at {arcs.MeetsPoint}; the blade met {first?.Site ?? "nothing"} at {first?.Point}");
        Assert.NotNull(first);
        Assert.Equal(t, arcs.Meets);
        Assert.Equal(first!.Value.Site, BodyRegions.Name(arcs.MeetsRegion));
        Assert.InRange(Vector3.Distance(first.Value.Point, arcs.MeetsPoint), 0f, 0.005f);
        // The ribbon is bright up to the meeting and dimmed past it, and a mark is drawn there.
        var quads = new List<SwingArcQuad>();
        arcs.Fill(w.Get<Body>(a).Feet, w.Get<Body>(a).Yaw, quads);
        if (arcs.Places[(int)arcs.MeetsAt].Front) Assert.Single(quads, q => q.Mark);
    }

    [Fact]
    public void The_core_is_the_blade_contact_places_from_the_state_alone_every_tick_moving_or_not()
    {
        // The seam: a pure function of a body's state, its rig, its placement and a clip time.
        var (w, kinds) = Make();
        var a = Put(w, kinds, "Christof", 0, 0, 90);
        var core = new SwingArcCore();
        var assets = w.Service<CharacterAssets>();
        float worst = 0f;
        int compared = 0;
        double spent = 0;
        var clock = new System.Diagnostics.Stopwatch();
        var warm = new System.Diagnostics.Stopwatch();
        for (int i = 0; i < 600; i++)
        {
            ref var intent = ref w.Get<CombatIntent>(a);
            (intent.Aims, intent.AimPitch) = (true, (i % 200 - 100) * 0.004f);
            intent.Attack = i % 120 == 10 || (i > 300 && i % 120 < 50);
            var move = i is > 150 and < 450 ? new System.Numerics.Vector2(0.6f, 0.8f) : default;
            w.Get<MoveIntent>(a).Direction = move;
            MovementServices.Step(w, ActionFrame.Idle);
            if (!HeldItems.Blade(w, a, out var real) || !HeldItems.Placement(w, a, out var place)) continue;
            clock.Start();
            Assert.True(core.BladeAt(assets, 0.06f, w.Get<CharacterAnimation>(a), true, w.Get<PoseRig>(a), place, out var blade));
            clock.Stop();
            warm.Start();
            core.BladeAt(assets, 0.06f, w.Get<CharacterAnimation>(a), true, w.Get<PoseRig>(a), place, out _);   // its samples kept now
            warm.Stop();
            worst = MathF.Max(worst, MathF.Max(Vector3.Distance(real.Tip, blade.Tip), Vector3.Distance(real.Guard, blade.Guard)));
            compared++;
        }
        spent = clock.Elapsed.TotalMilliseconds * 1000.0 / compared;
        output.WriteLine($"{compared} ticks compared (standing, walking, swinging, the aim moving): largest gap {worst * 1000f:0.####} mm; {spent:0.0} µs a call with new samples, {warm.Elapsed.TotalMilliseconds * 1000.0 / compared:0.0} µs with them kept");
        Assert.InRange(worst, 0f, 1e-5f);
    }

    [Fact]
    public void Bare_handed_the_arc_is_the_hands_path()
    {
        var (w, kinds) = Make();
        var a = Put(w, kinds, "Fists", 0, 0, 90);
        var (predicted, which, swept, levelAt) = Swing(w, a, 0, 1, 0f);
        var gap = Gap(predicted, swept, levelAt);
        output.WriteLine($"fists: {which}, {gap.Predicted} places predicted, {gap.Swept} swept; largest gap {gap.Tip * 1000f:0.###} mm");
        Assert.Equal(-1f, levelAt);
        Assert.Equal(swept.Count, predicted.Length);
        Assert.InRange(gap.Tip, 0f, 1e-4f);
    }

    [Fact]
    public void During_a_blow_the_arc_is_its_whole_front_path_the_blade_now_on_it_then_the_next_blow()
    {
        var (w, kinds) = Make();
        var a = Put(w, kinds, "Christof", 0, 0, 90);
        for (int i = 0; i < 30; i++) MovementServices.Step(w, ActionFrame.Idle);
        w.Get<Combatant>(a).NextVariant = 0;
        w.Get<CombatIntent>(a).Attack = true;
        MovementServices.Step(w, ActionFrame.Idle);
        w.Get<CombatIntent>(a).Attack = false;
        var arcs = new SwingArcs();
        // Step into the blow's window and predict there: the part already swept (Past) and the rest of the blow.
        SwingArcPlace[]? whole = null;
        int past = 0;
        BladePlacement now = default;
        for (int i = 0; i < 200 && whole is null; i++)
        {
            MovementServices.Step(w, ActionFrame.Idle);
            var anim = w.Get<CharacterAnimation>(a);
            Assert.True(arcs.Predict(w, a));
            if (arcs.Blow == SwingArcBlow.Current && anim.ActionTime > arcs.Window.From + 0.05f)
            {
                (whole, past) = (arcs.Places.ToArray(), arcs.Past);
                Assert.True(HeldItems.Blade(w, a, out now));
            }
        }
        Assert.NotNull(whole);
        for (int i = 0; i < 200; i++) MovementServices.Step(w, ActionFrame.Idle);
        var trails = w.Service<SwingTrails>();
        Assert.True(trails.Latest(a, out var trail));
        var swept = new List<BladePlacement>();
        trails.Places(trail, swept);
        // The blade now is the first place after the part swept; the rest predicted is the trail's last places.
        Assert.InRange(past, 2, whole!.Length - 3);
        Assert.InRange(Vector3.Distance(now.Tip, whole[past].Blade.Tip), 0f, 1e-4f);
        int offset = swept.Count - (whole.Length - past);
        Assert.InRange(offset, 1, swept.Count - 2);
        float gap = 0f;
        for (int i = past; i < whole.Length; i++) gap = MathF.Max(gap, Vector3.Distance(whole[i].Blade.Tip, swept[offset + i - past].Tip));
        // The part already swept lies on the blade's path as the trail has it (its clip times between the trail's ticks), within a
        // few centimetres: it is posed with the body as it is now (its aim's bend and the action's weight then were a little
        // different; 30 mm measured).
        float behind = 0f;
        for (int i = 0; i < past; i++)
        {
            float best = float.PositiveInfinity;
            for (int k = 1; k <= offset; k++) best = MathF.Min(best, SegmentDistance(whole[i].Blade.Tip, swept[k - 1].Tip, swept[k].Tip));
            behind = MathF.Max(behind, best);
        }
        output.WriteLine($"mid-blow: {past} places already swept and {whole.Length - past} to come, of the trail's {swept.Count}; the rest's largest gap {gap * 1000f:0.###} mm, the swept part's from the trail's path {behind * 1000f:0.###} mm");
        Assert.InRange(gap, 0f, 1e-4f);
        Assert.InRange(behind, 0f, 0.05f);
        // Without WholeBlow: the rest only.
        arcs.Settings = arcs.Settings with { WholeBlow = false };
        // After the window the arc is the next blow: the combo's next quick one.
        Assert.True(arcs.Predict(w, a));
        Assert.Equal(0, arcs.Past);
        Assert.Equal(SwingArcBlow.Quick, arcs.Blow);
        Assert.Equal(w.Service<CharacterAssets>().Kind(w.Get<CharacterAnimation>(a).Kind).Actions!.For(ActionRole.AttackQuick, 1).Clip, arcs.Clip);
    }

    [Fact]
    public void A_blow_held_ready_is_shown_as_a_release_now_would_strike_it()
    {
        // Ce-direction's ready and release holds a blow at its wind-up (the channel's ActionHolds, the block's
        // hold); the arc shows the blow let go now. Here the hold is put on a blow in its wind-up by hand.
        var (w, kinds) = Make();
        var a = Put(w, kinds, "Christof", 0, 0, 90);
        for (int i = 0; i < 30; i++) MovementServices.Step(w, ActionFrame.Idle);
        w.Get<Combatant>(a).NextVariant = 0;
        w.Get<CombatIntent>(a).Attack = true;
        MovementServices.Step(w, ActionFrame.Idle);
        w.Get<CombatIntent>(a).Attack = false;
        MovementServices.Step(w, ActionFrame.Idle);
        var arcs = new SwingArcs();
        Assert.True(arcs.Predict(w, a));
        Assert.Equal(SwingArcBlow.Current, arcs.Blow);
        Assert.True(w.Get<CharacterAnimation>(a).ActionTime < arcs.Window.From, "still in its wind-up");
        var free = arcs.Places.ToArray();
        w.Get<CharacterAnimation>(a).ActionHolds = true;
        Assert.True(arcs.Predict(w, a));
        Assert.True(w.Get<CharacterAnimation>(a).ActionHolds);                      // the body's own state is not touched
        var held = arcs.Places.ToArray();
        Assert.Equal(free.Length, held.Length);
        for (int i = 0; i < free.Length; i++) Assert.Equal(free[i].Blade.Tip, held[i].Blade.Tip);
    }

    static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float l = ab.LengthSquared();
        float t = l < 1e-12f ? 0f : Math.Clamp(Vector3.Dot(p - a, ab) / l, 0f, 1f);
        return Vector3.Distance(p, a + ab * t);
    }

    [Fact]
    public void The_arc_is_in_the_bodys_own_frame_his_velocity_does_not_carry_it()
    {
        // ("It's always right in front of Christof ... even moving around, the arc will stays relative to him"): the
        // prediction is the blow swept from where he stands now; his velocity does not move it (the earlier build's CarryOn did).
        var (w, kinds) = Make();
        var a = Put(w, kinds, "Christof", 0, 0, 90);
        w.Get<MoveIntent>(a).Direction = new Vector2(0f, 1f);
        for (int i = 0; i < 120; i++) MovementServices.Step(w, ActionFrame.Idle);
        var v = w.Get<Body>(a).Velocity;
        Assert.True(v.Length() > 1f, $"walking or running: {v.Length():0.00} m/s");
        var arcs = new SwingArcs();
        Assert.True(arcs.Predict(w, a));
        var moving = arcs.Places.ToArray();
        ref var body = ref w.Get<Body>(a);
        var kept = body.Velocity;
        body.Velocity = Vector3.Zero;
        Assert.True(arcs.Predict(w, a));
        var still = arcs.Places.ToArray();
        Assert.Equal(moving.Length, still.Length);
        for (int i = 0; i < moving.Length; i++) Assert.Equal(still[i].Blade.Tip, moving[i].Blade.Tip);
        // The old way carries it on by the velocity: its last place is ahead by about the speed times the time to it.
        body.Velocity = kept;
        arcs.Settings = arcs.Settings with { CarryOn = true };
        Assert.True(arcs.Predict(w, a));
        var carried = arcs.Places.ToArray();
        float ahead = carried[^1].Blade.Tip.Y - moving[^1].Blade.Tip.Y;
        output.WriteLine($"at {kept.Length():0.00} m/s: the body's own frame, the last tip {moving[^1].Blade.Tip}; carried on (the old way), {ahead:0.00} m further ahead ({arcs.Lead} ticks to the window)");
        Assert.True(ahead > 0.3f);
    }

    [Fact]
    public void Running_the_arc_is_the_blade_swept_in_his_own_frame()
    {
        // Before a blow, running (3 m/s); the trail's places moved back by the body's travel to their tick are the arc's places.
        var (w, kinds) = Make();
        var a = Put(w, kinds, "Christof", 0, 0, 90);
        w.Get<MoveIntent>(a).Direction = new Vector2(0f, 1f);
        for (int i = 0; i < 160; i++) MovementServices.Step(w, ActionFrame.Idle);
        w.Get<Combatant>(a).NextVariant = 0;
        w.Get<CombatIntent>(a).Attack = true;
        MovementServices.Step(w, ActionFrame.Idle);
        var arcs = new SwingArcs();
        Assert.True(arcs.Predict(w, a));
        var predicted = arcs.Places.ToArray();
        int lead = arcs.Lead;
        w.Get<CombatIntent>(a).Attack = false;
        int swing = w.Get<Combatant>(a).Swings + 1;
        var feet = new List<Vector3> { w.Get<Body>(a).Feet };
        for (int i = 0; i < 200; i++) { MovementServices.Step(w, ActionFrame.Idle); feet.Add(w.Get<Body>(a).Feet); }
        var trails = w.Service<SwingTrails>();
        Assert.True(trails.Latest(a, out var trail));
        Assert.Equal(swing, trail.Swing);
        var swept = new List<BladePlacement>();
        trails.Places(trail, swept);
        float world = 0f, own = 0f, travel = 0f;
        int n = Math.Min(predicted.Length, swept.Count);
        for (int i = 0; i < n; i++)
        {
            var moved = feet[lead + i] - feet[0];
            travel = MathF.Max(travel, moved.Length());
            world = MathF.Max(world, Vector3.Distance(predicted[i].Blade.Tip, swept[i].Tip));
            own = MathF.Max(own, Vector3.Distance(predicted[i].Blade.Tip, swept[i].Tip - moved));
        }
        output.WriteLine($"running {w.Get<Body>(a).Velocity.Length():0.00} m/s: {n} places; against the blade in the world {world * 1000f:0} mm (the body's travel, at most {travel * 1000f:0} mm); in his own frame {own * 1000f:0.###} mm");
        Assert.InRange(own, 0f, 1e-3f);
        Assert.InRange(world, travel - 0.01f, travel + 0.01f);
    }

    [Fact]
    public void The_whole_front_path_is_drawn_past_a_wall_faint_beyond_it()
    {
        // A wall 0.9 m before him: the blade will meet it, and the arc goes on past it (not cut), drawn faint there.
        var (w, kinds) = Make(l => l.Box(new Vector3(-3f, 0.9f, 0f), new Vector3(3f, 1.4f, 3f)));
        var a = Put(w, kinds, "Christof", 0, 0, 90);
        for (int i = 0; i < 30; i++) MovementServices.Step(w, ActionFrame.Idle);
        var arcs = new SwingArcs();
        Assert.True(arcs.Predict(w, a));
        output.WriteLine($"{arcs.Places.Length} places, the level met at {arcs.LevelAt:0.00}");
        Assert.True(arcs.EndsAtLevel);
        Assert.True(arcs.Places.Length > (int)MathF.Floor(arcs.LevelAt) + 3, "the arc goes on past the wall");
        var quads = new List<SwingArcQuad>();
        arcs.Fill(w.Get<Body>(a).Feet, w.Get<Body>(a).Yaw, quads);
        float past = arcs.Settings.PastMeetingShare;
        // Every quad's far corner past the meeting is at most the faint share.
        int beyond = 0;
        foreach (var q in quads)
            if (!q.Mark && q.B > 0f && q.A <= past + 1e-4f && q.B <= past + 1e-4f) beyond++;
        Assert.True(beyond > 0, "quads past the wall, faint");
        Assert.All(quads, q => Assert.True(q.Mark || MathF.Max(q.A, q.B) <= 1f));
    }

    [Fact]
    public void The_next_blow_is_the_combos_turn_or_the_strong_one_held_or_buffered()
    {
        var (w, kinds) = Make();
        var a = Put(w, kinds, "Christof", 0, 0, 90);
        var set = w.Service<CharacterAssets>().Kind(w.Get<CharacterAnimation>(a).Kind).Actions!;
        var arcs = new SwingArcs();
        for (int i = 0; i < 30; i++) MovementServices.Step(w, ActionFrame.Idle);
        ref var c = ref w.Get<Combatant>(a);
        c.NextVariant = 1;
        Assert.True(arcs.Predict(w, a));
        Assert.Equal(SwingArcBlow.Quick, arcs.Blow);
        Assert.Equal(set.For(ActionRole.AttackQuick, 1).Clip, arcs.Clip);
        // Held, but not yet long enough: a release now is quick; past StrongAfter, strong.
        w.Get<CombatIntent>(a).Attack = true;
        for (int i = 0; i < 12; i++) MovementServices.Step(w, ActionFrame.Idle);
        Assert.True(arcs.Predict(w, a));
        Assert.Equal(SwingArcBlow.Quick, arcs.Blow);
        for (int i = 0; i < 36; i++) MovementServices.Step(w, ActionFrame.Idle);
        Assert.True(arcs.Predict(w, a));
        Assert.Equal(SwingArcBlow.Strong, arcs.Blow);
        Assert.Equal(set.For(ActionRole.AttackStrong).Clip, arcs.Clip);
        // Released: the strong blow swings; a strong press buffered during it is the next once its window has passed.
        w.Get<CombatIntent>(a).Attack = false;
        MovementServices.Step(w, ActionFrame.Idle);
        Assert.True(arcs.Predict(w, a));
        Assert.Equal(SwingArcBlow.Current, arcs.Blow);
        w.Get<Combatant>(a).Pending = AttackStrength.Strong;
        int waited = 0;
        while (arcs.Predict(w, a) && arcs.Blow == SwingArcBlow.Current && waited++ < 400) MovementServices.Step(w, ActionFrame.Idle);
        Assert.Equal(AttackStrength.Strong, w.Get<Combatant>(a).Pending);         // still waiting for the swing's busy time
        Assert.Equal(SwingArcBlow.Strong, arcs.Blow);
        Assert.True(arcs.Lead > 0);                                                // it waits for the busy time
    }

    [Fact]
    public void Predicting_changes_nothing_and_allocates_nothing_once_made()
    {
        var (w, kinds) = Make();
        var a = Put(w, kinds, "Christof", 0, 0, 90);
        Put(w, kinds, "Civ12male", 0.2f, 1.4f, -90);                                // someone to meet: the meeting's passes run too
        var (w2, kinds2) = Make();
        var a2 = Put(w2, kinds2, "Christof", 0, 0, 90);
        Put(w2, kinds2, "Civ12male", 0.2f, 1.4f, -90);
        var arcs = new SwingArcs();
        int met = 0;
        var quads = new List<SwingArcQuad>();
        for (int i = 0; i < 600; i++)   // long enough for the sample caches' rings to be made (the locomotion's 128)
        {
            bool press = i % 150 == 20;
            w.Get<CombatIntent>(a).Attack = press;
            w2.Get<CombatIntent>(a2).Attack = press;
            MovementServices.Step(w, ActionFrame.Idle);
            MovementServices.Step(w2, ActionFrame.Idle);
            arcs.Predict(w, a);
            if (!arcs.Meets.IsNone) met++;
            arcs.Fill(w.Get<Body>(a).Feet, w.Get<Body>(a).Yaw, quads);
            Assert.Equal(w2.Digest(), w.Digest());
        }
        Assert.True(met > 0);
        // Measured as the allocation tests measure (Allocations.None: the window measured more than once, each the
        // world's next 300 ticks, two blows in each).
        int at = 0;
        Allocations.None("the prediction and the fill over 300 ticks", () =>
        {
            long spent = 0;
            for (int end = at + 300; at < end; at++)
            {
                w.Get<CombatIntent>(a).Attack = at % 150 == 20;
                MovementServices.Step(w, ActionFrame.Idle);
                long before = GC.GetAllocatedBytesForCurrentThread();
                arcs.Predict(w, a);
                arcs.Fill(w.Get<Body>(a).Feet, w.Get<Body>(a).Yaw, quads);
                spent += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            output.WriteLine($"the prediction and the fill allocated {spent} bytes over 300 ticks");
            return spent;
        });
    }

    /// <summary>A timing test: left out of the plain run and run alone by the timing command (the README's Build, test and run); its bounds kept.</summary>
    [Fact, Trait("Category", "Timing")]
    public void The_prediction_costs_well_under_a_millisecond()
    {
        var (w, kinds) = Make();
        var a = Put(w, kinds, "Christof", 0, 0, 90);
        for (int i = 0; i < 30; i++) MovementServices.Step(w, ActionFrame.Idle);
        var arcs = new SwingArcs();
        var quads = new List<SwingArcQuad>();
        for (int i = 0; i < 20; i++) arcs.Predict(w, a);
        // The least of five batches (the machine is shared: the least is the work's own).
        double Least()
        {
            double least = double.PositiveInfinity;
            for (int batch = 0; batch < 5; batch++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 40; i++) { arcs.Predict(w, a); arcs.Fill(w.Get<Body>(a).Feet, w.Get<Body>(a).Yaw, quads); }
                least = Math.Min(least, sw.Elapsed.TotalMilliseconds / 40);
            }
            return least;
        }
        double each = Least();
        output.WriteLine($"a quick blow's prediction: {arcs.Places.Length} places, {each:0.000} ms each (and the fill: {quads.Count} quads)");
        Assert.InRange(each, 0.0, 2.0);   // the budget is well under a millisecond; a loaded 2-CPU machine is given room
        // With a townsman in its path: who it meets is found too (his pose played on to the meeting, once).
        Put(w, kinds, "Civ12male", 0, 1.3f, -90);
        MovementServices.Step(w, ActionFrame.Idle);
        for (int i = 0; i < 20; i++) arcs.Predict(w, a);
        double met = Least();
        arcs.Settings = arcs.Settings with { PoseTheMet = false };
        double once = Least();
        output.WriteLine($"  (as he stands now, one pass: {once:0.000} ms each)");
        arcs.Settings = arcs.Settings with { PoseTheMet = true };
        output.WriteLine($"with a townsman 1.3 m ahead (met: {BodyRegions.Name(arcs.MeetsRegion)}): {met:0.000} ms each");
        Assert.False(arcs.Meets.IsNone);
        Assert.InRange(met, 0.0, 2.0);
    }

    [Fact]
    public void The_ribbon_lies_on_the_part_before_the_body_brightest_at_the_strike()
    {
        var (w, kinds) = Make();
        var a = Put(w, kinds, "Christof", 0, 0, 90);
        for (int i = 0; i < 30; i++) MovementServices.Step(w, ActionFrame.Idle);
        var arcs = new SwingArcs();
        Assert.True(arcs.Predict(w, a));
        var quads = new List<SwingArcQuad>();
        var feet = w.Get<Body>(a).Feet;
        arcs.Fill(feet, w.Get<Body>(a).Yaw, quads);
        Assert.NotEmpty(quads);
        int front = 0;
        foreach (var p in arcs.Places) if (p.Front) front++;
        output.WriteLine($"{arcs.Places.Length} places, {front} before the body, {quads.Count} quads; brightest {quads.Max(q => MathF.Max(q.A, q.B)):0.00}");
        Assert.InRange(front, 3, arcs.Places.Length - 1);                           // the forehand goes round behind the shoulders too
        Assert.All(quads, q => Assert.True(q.TipA.Y - feet.Y > -0.2f && q.TipB.Y - feet.Y > -0.2f));   // facing +Y: before the body
        Assert.InRange(quads.Max(q => MathF.Max(q.A, q.B)), 0.9f, 1f);
        // Stood on a body drawn a little behind and turned: the quads move and turn with it.
        var moved = new List<SwingArcQuad>();
        arcs.Fill(feet + new Vector3(1f, 0f, 0f), w.Get<Body>(a).Yaw, moved);
        Assert.Equal(quads[0].TipA.X + 1f, moved[0].TipA.X, 4);
        // Drawn on him wherever he is drawn — turned a quarter about his feet, the arc turns with him (it stays before him).
        var turned = new List<SwingArcQuad>();
        arcs.Fill(feet, w.Get<Body>(a).Yaw + MathF.PI / 2f, turned);
        var d0 = quads[0].TipA - feet;
        var d1 = turned[0].TipA - feet;
        Assert.Equal(-d0.Y, d1.X, 4);
        Assert.Equal(d0.X, d1.Y, 4);
        Assert.Equal(d0.Z, d1.Z, 4);
    }

    [Fact]
    public void The_crosshair_is_faint_while_a_weapon_or_fists_are_out_and_full_otherwise()
    {
        // Sword or fists out, the reticle at the setting's opacity (a person near — the foe — too: the verb is talk
        // for every person within reach); a body that cannot swing (no fighter, no attacks, dying) or a power's target: full.
        var (w, kinds) = Make();
        var sword = Put(w, kinds, "Christof", 0, 0, 90);
        var fists = Put(w, kinds, "Fists", 3, 0, 90);
        var unarmed = MovementServices.SpawnBody(w, new(6f, 0f, 0.01f), 0f, Locomotion.Sandbox);
        w.Set(unarmed, CharacterAnimation.For(w.Service<CharacterAssets>(), kinds["Civ12male"]));
        w.Set(unarmed, MotionInput.Standing);
        MovementServices.Step(w, ActionFrame.Idle);
        Assert.True(SwingArcs.CanSwing(w, sword));
        Assert.True(SwingArcs.CanSwing(w, fists));
        Assert.False(SwingArcs.CanSwing(w, unarmed));
        Assert.Equal(0.2f, ReticleLook.For(w, sword, 0.2f), 5);
        Assert.Equal(0.2f, ReticleLook.For(w, fists, 0.2f), 5);
        Assert.Equal(1f, ReticleLook.For(w, unarmed, 0.2f));
        Assert.Equal(1f, ReticleLook.For(w, sword, 1f));
        Assert.Equal(0.2f, ReticleLook.Opacity(true, Cameras.PointerVerb.Talk, false, 0.2f));
        Assert.Equal(1f, ReticleLook.Opacity(false, Cameras.PointerVerb.Talk, false, 0.2f));
        Assert.Equal(1f, ReticleLook.Opacity(true, Cameras.PointerVerb.Refused, false, 0.2f));
        Assert.Equal(1f, ReticleLook.Opacity(true, Cameras.PointerVerb.Target, false, 0.2f));
        Assert.Equal(1f, ReticleLook.Opacity(true, Cameras.PointerVerb.None, true, 0.2f));
        Assert.Equal(0f, ReticleLook.Opacity(true, Cameras.PointerVerb.None, false, -1f));
        Assert.Equal(1f, ReticleLook.Opacity(false, Cameras.PointerVerb.None, false, 0.2f));
        // Dying: no blow could come, the crosshair is full again.
        w.Get<CharacterAnimation>(sword).Action = ActionRole.DeathQuick;
        Assert.False(SwingArcs.CanSwing(w, sword));
    }
}
