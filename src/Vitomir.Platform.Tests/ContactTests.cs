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
using Vitomir.Platform.Perception;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// Contact: the active window worked out from the motion; a sword on a body at the site the aim chose; a sword on
/// a shield with the body behind it not hit; a sword into a wall with the person behind it not hit; a miss out of reach;
/// one contact per swing on a body the blade passes through, the rest rejected and counted; no tunnelling at the fastest
/// swing; a moving target swept; a protected person; violence seen; the masquerade's catch let go when the player is
/// struck; replays that agree; nothing allocated per tick. Every test skips without the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class ContactTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static OneOneLibrary Lib()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        return Library.Value!;
    }

    static readonly Dictionary<(string, string?), OneOneCharacter> Loaded = [];

    static OneOneCharacter Character(string name, string? weapon = null)
    {
        var lib = Lib();
        lock (Loaded)
        {
            if (!Loaded.TryGetValue((name, weapon), out var c))
                Loaded[(name, weapon)] = c = lib.Character(name, weapon) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
            return c;
        }
    }

    static int Ticks(float seconds) => (int)MathF.Round(seconds * World.TicksPerSecond);

    /// <summary>A world as the program makes it, contact and all, on a floor (and whatever else the test lays).</summary>
    sealed class Ring
    {
        public required World World { get; init; }
        public required Dictionary<string, int> Kinds { get; init; }
        public CharacterAssets Assets => World.Service<CharacterAssets>();
        public readonly List<ContactMade> Contacts = [];
        public readonly List<ContactDuplicates> Duplicates = [];
        public readonly List<ProtectedProtest> Protests = [];
        public readonly List<CatchLetGo> LetGo = [];
        public readonly List<MasqueradeModeChanged> Modes = [];
        public readonly List<AttackBegun> Begun = [];
        public readonly List<StrikeMoment> Strikes = [];
        public readonly List<MasqueradeRisk> Risks = [];
        public readonly List<AwarenessChanged> Awareness = [];
        public readonly List<ReactionStarted> Reactions = [];

        public void Step(ActionFrame frame, int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, frame);
                Contacts.AddRange(World.Events<ContactMade>().ThisTick);
                Duplicates.AddRange(World.Events<ContactDuplicates>().ThisTick);
                Protests.AddRange(World.Events<ProtectedProtest>().ThisTick);
                LetGo.AddRange(World.Events<CatchLetGo>().ThisTick);
                Modes.AddRange(World.Events<MasqueradeModeChanged>().ThisTick);
                Begun.AddRange(World.Events<AttackBegun>().ThisTick);
                Strikes.AddRange(World.Events<StrikeMoment>().ThisTick);
                Risks.AddRange(World.Events<MasqueradeRisk>().ThisTick);
                if (World.TryService<EventQueue<AwarenessChanged>>(out var a)) Awareness.AddRange(a.ThisTick);
                if (World.TryService<EventQueue<ReactionStarted>>(out var r)) Reactions.AddRange(r.ThisTick);
            }
        }

        public void Idle(int ticks) => Step(ActionFrame.Idle, ticks);

        /// <summary>A character of <paramref name="kind"/> standing at (x, y) facing <paramref name="yawDegrees"/>, armed; the player when <paramref name="player"/>.</summary>
        public EntityId Put(string kind, float x, float y, float yawDegrees, bool player = false, float z = 0.01f)
        {
            float yaw = yawDegrees * DetMath.DegToRad;
            var e = player ? MovementServices.SpawnControlled(World, new(x, y, z), yaw, Locomotion.Sandbox) : MovementServices.SpawnBody(World, new(x, y, z), yaw, Locomotion.Sandbox);
            World.Set(e, CharacterAnimation.For(Assets, Kinds[kind]));
            World.Set(e, MotionInput.Standing);
            CombatServices.Arm(World, e);
            return e;
        }

        /// <summary><paramref name="who"/> taps attack (a quick blow, made on release), aimed <paramref name="aimDegrees"/>.</summary>
        public void Swing(EntityId who, float aimDegrees = 0f, bool strong = false)
        {
            ref var intent = ref World.Get<CombatIntent>(who);
            intent.Aims = true;
            intent.AimPitch = aimDegrees * DetMath.DegToRad;
            intent.Attack = true;
            Idle(strong ? Ticks(0.4f) : 1);
            World.Get<CombatIntent>(who).Attack = false;
        }
    }

    static readonly string[] AllKinds = ["Christof", "rudolfGuard", "Civ12male", "Merchant"];

    static Ring Make(LevelSpace? level = null, ulong seed = 7, Action<WorldBuilder>? more = null, string weapon = "BroadSword", Action<ContactConfig>? tune = null, bool contact = true)
    {
        var assets = new CharacterAssets();
        var kinds = new Dictionary<string, int>();
        foreach (var k in AllKinds) kinds[k] = OneOneLibrary.Register(assets, Character(k, k == "Christof" ? weapon : null));
        level ??= new TestLevel().Floor(-60, -60, 60, 60).Build();
        var b = new WorldBuilder(seed);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        more?.Invoke(b);
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        var config = new ContactConfig();
        config.Protected.Add("Merchant");
        tune?.Invoke(config);
        if (contact) ContactServices.Register(b, config);
        else b.Event<ContactMade>().Event<ContactDuplicates>().Event<ProtectedProtest>().Event<CatchLetGo>();
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        if (contact) ContactServices.Prepare(w);
        return new Ring { World = w, Kinds = kinds };
    }

    // ---- the active window ----

    [Fact]
    public void The_active_window_is_the_fast_part_of_the_swing_round_its_strike()
    {
        var r = Make();
        var assets = r.Assets;
        int kind = r.Kinds["Christof"];
        var set = assets.Kind(kind).Actions!;
        var windows = r.World.Service<ActiveWindows>();
        var rule = r.World.Service<ContactConfig>().Window;
        foreach (var m in set.Quick.Append(set.Strong))
        {
            var w = windows.For(assets, kind, m.Clip, m.StrikeSeconds, rule);
            output.WriteLine($"{assets.Clip(m.Clip).Name}: frames {w.From * 30:0.0} to {w.To * 30:0.0} of {assets.Clip(m.Clip).Duration * 30:0}, strike {w.Strike * 30:0.0}, peak {w.PeakSpeed:0} m/s");
            Assert.True(w.Has && w.FromMotion, $"{assets.Clip(m.Clip).Name}: {w}");
            Assert.InRange(m.StrikeSeconds, w.From, w.To);
            Assert.True(w.To - w.From < 0.5f * assets.Clip(m.Clip).Duration, "the window is the fast part, not the whole swing");
        }
        // The survey's chrisAttackSword: about frames 10 to 18 of 29 (the motion brakes the tip for a moment at 16: bridged).
        var quick = windows.For(assets, kind, set.Quick[0].Clip, set.Quick[0].StrikeSeconds, rule);
        Assert.InRange(quick.From * 30f, 9f, 10.5f);
        Assert.InRange(quick.To * 30f, 17.5f, 19.5f);
        // The rule is the data's: a higher share gives a narrower window.
        var narrow = ActiveWindows.Measure(assets, kind, set.Quick[0].Clip, set.Quick[0].StrikeSeconds, rule with { ShareOfPeak = 0.7f });
        Assert.True(narrow.From > quick.From && narrow.To < quick.To, $"{narrow} within {quick}");
    }

    // ---- a body, a shield, a wall, nothing ----

    /// <summary>Christof swings his first quick blow (chrisAttackSword, a forehand) aimed <paramref name="aim"/>° at a townsman <paramref name="d"/> m in front.</summary>
    (Ring R, EntityId A, EntityId T) Blow(float aim, float d = 1.3f, string target = "Civ12male", int variant = 0, Action<Ring, EntityId>? before = null, LevelSpace? level = null,
        Action<ContactConfig>? tune = null)
    {
        var r = Make(level, tune: tune);
        var a = r.Put("Christof", 0, 0, 90);
        var t = r.Put(target, 0, d, -90);
        before?.Invoke(r, t);
        r.Idle(30);
        r.World.Get<Combatant>(a).NextVariant = variant;
        r.Swing(a, aim);
        r.Idle(Ticks(1.3f));
        output.WriteLine($"aim {aim:+0;-0}° at {d} m: {string.Join("; ", r.Contacts.Select(c => $"{c.Met} {c.Site} {c.Material} at {c.Point.Z:0.00} m, {c.ImpactSpeed:0.0} m/s, impulse {c.NormalImpulse:0.0}"))}");
        return (r, a, t);
    }

    [Theory]
    [InlineData(25f, "head,neck,chest")]
    [InlineData(15f, "head,neck,chest")]
    [InlineData(0f, "chest,belly,left upper arm,left forearm,right upper arm,right forearm")]
    [InlineData(-25f, "left thigh,right thigh,left shin,right shin,hips")]
    public void A_sword_meets_the_body_at_the_site_the_aim_chose(float aim, string sites)
    {
        var (r, a, t) = Blow(aim);
        var c = Assert.Single(r.Contacts);
        Assert.Equal(ContactMet.Body, c.Met);
        Assert.Contains(c.Site, sites.Split(','));
        Assert.Equal(a, c.Attacker);
        Assert.Equal(t, c.Target);
        Assert.Equal(1, c.Swing);
        Assert.Equal(ContactMaterial.Flesh, c.Material);
        Assert.True(c.LineOfFire && !c.PreContactBlocked && !c.HitCoverOrShield && !c.Protected);
        Assert.InRange(c.ImpactSpeed, 5f, 80f);
        Assert.Equal(1.4f * c.ImpactSpeed, c.NormalImpulse, 3);   // the one-handed class's SANDBOX mass × the speed
        Assert.InRange(c.Normal.Length(), 0.99f, 1.01f);
        Assert.True(c.ContactId >= c.Tick * 1000 && c.ContactId < (c.Tick + 1) * 1000);
        // The blow's quality, measured: the whole speed at least its part along the normal, a place on the blade, the
        // tip moving, and the blow's peak (the clip's, at the rate it played) known.
        Assert.True(c.PointSpeed >= c.ImpactSpeed - 1e-3f, $"point {c.PointSpeed}, impact {c.ImpactSpeed}");
        Assert.InRange(c.AlongBlade, 0f, 1f);
        Assert.True(c.TipSpeed > 1f, $"tip {c.TipSpeed}");
        Assert.True(c.PeakSpeed > 10f, $"peak {c.PeakSpeed}");
        // The window: the contact lands between the window's opening and its close (the tick of the strike is 0.5 s of clip time in).
        var begun = Assert.Single(r.Begun);
        Assert.InRange((c.Tick - begun.Tick) / (float)World.TicksPerSecond, 0.25f, 0.8f);
        // The body plays its hurt.
        Assert.True(r.World.Get<CharacterAnimation>(t).Action == ActionRole.Hurt || r.Contacts.Count > 0 && Hurt(r, t), "the struck townsman plays his hurt");
    }

    static bool Hurt(Ring r, EntityId t) => r.World.Get<CharacterAnimation>(t).Action == ActionRole.Hurt;

    [Fact]
    public void Aiming_high_level_and_low_strikes_high_level_and_low()
    {
        float High() => Blow(25f).R.Contacts[0].Point.Z;
        float Level() => Blow(0f).R.Contacts[0].Point.Z;
        float Low() => Blow(-25f).R.Contacts[0].Point.Z;
        float h = High(), l = Level(), d = Low();
        Assert.True(h > l + 0.2f && l > d + 0.3f, $"high {h:0.00}, level {l:0.00}, low {d:0.00}");
    }

    [Fact]
    public void A_sword_on_a_shield_stops_there_and_the_body_behind_is_not_hit()
    {
        var (r, a, t) = Blow(0f, target: "rudolfGuard", before: (r, t) => r.World.Get<CombatIntent>(t).Block = true);
        var c = Assert.Single(r.Contacts);
        Assert.Equal(ContactMet.Shield, c.Met);
        Assert.Equal(t, c.Target);
        Assert.Null(c.Site);
        Assert.Equal(ContactMaterial.Wood, c.Material);
        Assert.True(c.HitCoverOrShield && !c.PreContactBlocked && !c.Protected);
        // The block holds (no hurt), and the swing is cut short: held where it met, fading; its strike mark never reached.
        Assert.NotEqual(ActionRole.Hurt, r.World.Get<CharacterAnimation>(t).Action);
        Assert.True(r.World.Get<Combatant>(t).Blocking);
        Assert.Empty(r.Strikes);
        Assert.DoesNotContain(r.Contacts, x => x.Met == ContactMet.Body);
    }

    [Fact]
    public void Without_the_block_the_shield_still_stops_the_blow_but_the_blade_parries_only_while_blocking()
    {
        // Aimed high at the guard: his raised blade (his guard) parries it while he blocks; without the block it meets his shield.
        // The guard held still (the guard off), as the first guard was: the directed guard is GuardTests'.
        var (blocking, _, t) = Blow(15f, target: "rudolfGuard", before: (r, t) => { r.World.Get<CombatIntent>(t).Block = true; r.World.Service<GuardSettings>().Enabled = false; });
        var parry = Assert.Single(blocking.Contacts);
        Assert.Equal(ContactMet.Weapon, parry.Met);
        Assert.Equal(ContactMaterial.Steel, parry.Material);
        Assert.False(parry.HitCoverOrShield);
        var (open, _, _) = Blow(15f, target: "rudolfGuard");
        Assert.Equal(ContactMet.Shield, Assert.Single(open.Contacts).Met);
        // With parrying off, the blocking guard's blade lets the blow by (to whatever is behind it).
        var r2 = Make(tune: c => c.Parry = ParryRule.Never);
        var a2 = r2.Put("Christof", 0, 0, 90);
        var t2 = r2.Put("rudolfGuard", 0, 1.3f, -90);
        r2.World.Get<CombatIntent>(t2).Block = true;
        r2.Idle(30);
        r2.Swing(a2, 15f);
        r2.Idle(Ticks(1.3f));
        Assert.NotEqual(ContactMet.Weapon, Assert.Single(r2.Contacts).Met);
    }

    [Fact]
    public void A_sword_into_a_wall_stops_there_and_the_person_behind_is_not_hit()
    {
        // A thin wall 0.6 m in front of Christof, a townsman behind it at 1.3 m.
        var wall = new TestLevel().Floor(-60, -60, 60, 60).Box(new(-1.5f, 0.6f, 0f), new(1.5f, 0.75f, 2.5f)).Build();
        var (r, a, t) = Blow(15f, level: wall);
        var c = Assert.Single(r.Contacts);
        Assert.Equal(ContactMet.Level, c.Met);
        Assert.True(c.Target.IsNone);
        Assert.Equal(ContactMaterial.Stone, c.Material);
        Assert.True(c.HitCoverOrShield);
        Assert.InRange(c.Point.Y, 0.55f, 0.8f);
        Assert.NotEqual(ActionRole.Hurt, r.World.Get<CharacterAnimation>(t).Action);
        Assert.Empty(r.Strikes);
        // Without the wall the same blow meets him.
        Assert.Equal(ContactMet.Body, Assert.Single(Blow(15f).R.Contacts).Met);
    }

    [Fact]
    public void A_blow_out_of_reach_meets_nothing()
    {
        var (r, _, t) = Blow(0f, d: 3.2f);
        Assert.Empty(r.Contacts);
        Assert.Single(r.Strikes);   // the swing played through to its strike and on
        Assert.NotEqual(ActionRole.Hurt, r.World.Get<CharacterAnimation>(t).Action);
    }

    /// <summary>The first contact rule, the blade through bodies (the switch <c>contact-stop-off</c>, the data's <c>stop.enabled</c> false).</summary>
    static void StopOff(ContactConfig c) => c.Stop = c.Stop with { Enabled = false };

    [Fact]
    public void A_blade_through_a_body_is_one_contact_and_the_rest_are_rejected_and_counted()
    {
        // With the stop off (with it on the blade stops in him: FollowThroughTests).
        // Close in, the forehand's blade stays in his arm and chest for several ticks.
        var (r, a, t) = Blow(5f, d: 1.0f, tune: StopOff);
        var c = Assert.Single(r.Contacts);
        Assert.Equal(t, c.Target);
        var dup = Assert.Single(r.Duplicates);
        Assert.Equal(a, dup.Attacker);
        Assert.Equal(1, dup.Swing);
        Assert.Equal(1, dup.Contacts);
        Assert.True(dup.Rejected >= 3, $"{dup.Rejected} rejected");
        Assert.True(dup.Tick > c.Tick);
        // A second swing is a new record: it may meet him again.
        r.Idle(Ticks(1f));
        r.Swing(a, 5f);
        r.Idle(Ticks(1.3f));
        Assert.Equal(2, r.Contacts.Count(x => x.Target == t));
        Assert.Equal(2, r.Contacts[^1].Swing);
    }

    [Fact]
    public void Two_people_in_line_are_both_met_by_one_swing_in_the_order_met()
    {
        // With the stop off (with it on the first stops the blade unless he lets it go on: FollowThroughTests).
        var r = Make(tune: StopOff);
        var a = r.Put("Christof", 0, 0, 90);
        var near = r.Put("Civ12male", -0.55f, 1.25f, -90);
        var far = r.Put("Civ12male", 0.6f, 1.3f, -90);
        r.Idle(30);
        r.Swing(a, 0f);
        r.Idle(Ticks(1.3f));
        output.WriteLine(string.Join("; ", r.Contacts.Select(c => $"{c.Target} {c.Site} t{c.Tick}")));
        Assert.True(r.Contacts.Count >= 1);
        Assert.Equal(r.Contacts.Count, r.Contacts.Select(c => c.Target).Distinct().Count());
        for (int i = 1; i < r.Contacts.Count; i++) Assert.True(r.Contacts[i].ContactId > r.Contacts[i - 1].ContactId);
        Assert.All(r.Contacts, c => Assert.Equal(ContactMet.Body, c.Met));
    }

    // ---- sweeping ----

    /// <summary>Each tick's blade and target volumes, recorded after the step (what contact saw this tick).</summary>
    sealed record Seen(long Tick, BladePlacement Blade, HitVolume[] Volumes, bool InWindow);

    static List<Seen> Record(Ring r, EntityId a, EntityId t, int ticks, Action<int>? each = null)
    {
        var seen = new List<Seen>();
        var windows = r.World.Service<ActiveWindows>();
        var rule = r.World.Service<ContactConfig>().Window;
        for (int i = 0; i < ticks; i++)
        {
            each?.Invoke(i);
            r.Idle(1);
            Assert.True(HeldItems.Blade(r.World, a, out var blade));
            var anim = r.World.Get<CharacterAnimation>(a);
            var w = windows.For(r.Assets, anim.Kind, anim.ActionClip, r.World.Get<Combatant>(a).StrikeAt, rule);
            bool inWindow = ActionRoles.IsAttack(anim.Action) && !anim.ActionEnding && w.Overlaps(anim.ActionLast, anim.ActionTime);
            seen.Add(new Seen(r.World.Tick - 1, blade, r.World.Service<HitVolumes>().Posed(r.World, t).ToArray(), inWindow));
        }
        return seen;
    }

    /// <summary>The first tick, and the volume, where the blade sampled <paramref name="steps"/> times a tick between ticks overlaps a volume sampled the same way.</summary>
    static (long Tick, BodyRegion Region)? BruteForce(List<Seen> seen, int steps)
    {
        for (int k = 1; k < seen.Count; k++)
        {
            if (!seen[k].InWindow) continue;
            var (b0, b1) = (seen[k - 1].Blade, seen[k].Blade);
            float best = 2f; BodyRegion region = default;
            for (int v = 0; v < seen[k].Volumes.Length; v++)
                for (int s = 0; s <= steps; s++)
                {
                    float u = s / (float)steps;
                    var c0 = seen[k - 1].Volumes[v].Capsule; var c1 = seen[k].Volumes[v].Capsule;
                    var p = Vector3.Lerp(b0.Guard, b1.Guard, u); var q = Vector3.Lerp(b0.Tip, b1.Tip, u);
                    var ca = Vector3.Lerp(c0.A, c1.A, u); var cb = Vector3.Lerp(c0.B, c1.B, u);
                    float d2 = Geometry.SegmentSegment(p, q, ca, cb, out _, out _, out _, out _);
                    float rr = c0.Radius + (c1.Radius - c0.Radius) * u + b1.Radius;
                    if (d2 <= rr * rr) { if (u < best) { best = u; region = seen[k].Volumes[v].Region; } break; }
                }
            if (best <= 1f) return (seen[k].Tick, region);
        }
        return null;
    }

    [Fact]
    public void The_fastest_swing_never_tunnels_through_a_hand()
    {
        // chrisAttack2Sword, the low backhand: its tip peaks at 75 m/s, 0.6 m a tick; aimed level it meets the townsman's hand
        // (a few centimetres thick) first. Brute force (512 samples a tick) finds the first overlap on the same tick, the same part.
        int checkedCases = 0;
        foreach (var (aim, d) in new[] { (0f, 1.3f), (0f, 1.0f), (-10f, 1.3f), (5f, 1.3f), (-25f, 1.0f) })
        {
            var r = Make(tune: StopOff);   // the recorded blade is the swing's own (with the stop on it is held where it bit)
            var a = r.Put("Christof", 0, 0, 90);
            var t = r.Put("Civ12male", 0, d, -90);
            r.Idle(30);
            r.World.Get<Combatant>(a).NextVariant = 1;
            r.Swing(a, aim);
            var seen = Record(r, a, t, Ticks(1.2f));
            var brute = BruteForce(seen, 512);
            var first = r.Contacts.FirstOrDefault(c => c.Met == ContactMet.Body);
            output.WriteLine($"aim {aim} d {d}: contact {(r.Contacts.Count == 0 ? "none" : $"{first.Site} t{first.Tick}")}, brute force {(brute is { } b ? $"{BodyRegions.Name(b.Region)} t{b.Tick}" : "none")}");
            Assert.Equal(brute is not null, r.Contacts.Any(c => c.Met == ContactMet.Body));
            if (brute is not { } bf) continue;
            Assert.Equal(bf.Tick, first.Tick);
            Assert.Equal(BodyRegions.Name(bf.Region), first.Site);
            checkedCases++;
        }
        Assert.True(checkedCases >= 4);
        // And the tip did move more than a hand's width a tick in the window.
        var r2 = Make();
        var a2 = r2.Put("Christof", 0, 0, 90);
        var t2 = r2.Put("Civ12male", 0, 9f, -90);
        r2.Idle(30);
        r2.World.Get<Combatant>(a2).NextVariant = 1;
        r2.Swing(a2, 0f);
        var all = Record(r2, a2, t2, Ticks(1.2f));
        float most = 0f;
        for (int k = 1; k < all.Count; k++) if (all[k].InWindow) most = MathF.Max(most, Vector3.Distance(all[k].Blade.Tip, all[k - 1].Blade.Tip));
        Assert.True(most > 0.3f, $"the tip moved at most {most:0.00} m a tick");
    }

    [Fact]
    public void A_blade_against_a_moving_turning_box_agrees_with_brute_force()
    {
        // ContactMath's moving box (a shield on a moving arm): swept in the box's own frame. Against brute force (the blade
        // and the box both stepped 2,000 times over the tick, the box's axes mixed and made square again), on seeded cases.
        var rng = new Rng(1234);
        int met = 0;
        for (int n = 0; n < 300; n++)
        {
            Vector3 R(float s) => new((rng.NextFloat() * 2 - 1) * s, (rng.NextFloat() * 2 - 1) * s, (rng.NextFloat() * 2 - 1) * s);
            float turn = (rng.NextFloat() * 2 - 1) * 0.2f;   // up to about 11° over the tick
            var (s0, c0) = DetMath.SinCos(0.3f);
            var (s1, c1) = DetMath.SinCos(0.3f + turn);
            var half = new Vector3(0.25f, 0.03f, 0.35f);
            var box0 = new OrientedBox(R(0.1f), new(c0, s0, 0), new(-s0, c0, 0), Vector3.UnitZ, half);
            var box1 = new OrientedBox(box0.Centre + R(0.05f), new(c1, s1, 0), new(-s1, c1, 0), Vector3.UnitZ, half);
            var p0 = R(0.8f) + new Vector3(0, -0.9f, 0); var q0 = p0 + R(0.6f);
            var p1 = R(0.8f) + new Vector3(0, 0.9f, 0); var q1 = p1 + R(0.6f);
            bool swept = ContactMath.Sweep(p0, q0, p1, q1, 0.012f, box0, box1, out var hit);
            float first = 2f;
            for (int k = 0; k <= 2000; k++)
            {
                float u = k / 2000f;
                // The box as the sweep sees it: the blade's ends taken into the box's frame at each end, moving straight between.
                var lp = Vector3.Lerp(box0.ToLocal(p0), box1.ToLocal(p1), u);
                var lq = Vector3.Lerp(box0.ToLocal(q0), box1.ToLocal(q1), u);
                var local = new OrientedBox(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, half);
                if (Geometry.SegmentBox(lp, lq, local, out _, out _, out _) <= 0.012f * 0.012f) { first = u; break; }
            }
            Assert.Equal(first <= 1f, swept);
            if (!swept) continue;
            met++;
            // Never past the first touch; and where it stops, the blade is within the tolerance of the box (a grazing approach
            // closes slowly, so the stop can come a little before the brute force's first overlapping sample).
            Assert.True(hit.Time <= first + 5e-4f, $"{hit.Time} after {first}");
            var sp = Vector3.Lerp(box0.ToLocal(p0), box1.ToLocal(p1), hit.Time);
            var sq = Vector3.Lerp(box0.ToLocal(q0), box1.ToLocal(q1), hit.Time);
            float gap = MathF.Sqrt(Geometry.SegmentBox(sp, sq, new OrientedBox(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, half), out _, out _, out _)) - 0.012f;
            Assert.True(gap <= ContactMath.Tolerance * 1.5f, $"{gap * 1000:0.000} mm off at the stop");
            Assert.InRange(hit.Normal.Length(), 0.99f, 1.01f);
        }
        Assert.InRange(met, 30, 290);
    }

    [Fact]
    public void A_moving_body_is_swept_from_where_it_was_to_where_it_is()
    {
        // The townsman runs across in front of Christof at 4 m/s (3.3 cm a tick) as the blow comes.
        var r = Make(tune: StopOff);   // the recorded blade is the swing's own (with the stop on it is held where it bit)
        var a = r.Put("Christof", 0, 0, 90);
        var t = r.Put("Civ12male", -2.3f, 1.3f, 0);
        r.Idle(30);
        r.Swing(a, 15f);
        var seen = Record(r, a, t, Ticks(1.2f), i => { ref var b = ref r.World.Get<Body>(t); b.Feet += new Vector3(4f * World.TickSeconds, 0, 0); });
        var c = Assert.Single(r.Contacts, x => x.Met == ContactMet.Body);
        int k = seen.FindIndex(s => s.Tick == c.Tick);
        Assert.True(k > 0);
        // Recomputed from the recorded placements: swept against the moving volumes it lands where contact said; against the
        // volumes where they stood at the tick's end it would not.
        var (b0, b1) = (seen[k - 1].Blade, seen[k].Blade);
        (float T, Vector3 P) Earliest(bool moving)
        {
            float best = 2f; Vector3 at = default;
            for (int v = 0; v < seen[k].Volumes.Length; v++)
            {
                var c1 = seen[k].Volumes[v].Capsule;
                var c0 = moving ? seen[k - 1].Volumes[v].Capsule : c1;
                if (ContactMath.Sweep(b0.Guard, b0.Tip, b1.Guard, b1.Tip, b1.Radius, c0, c1, out var h) && h.Time < best) { best = h.Time; at = h.Point; }
            }
            return (best, at);
        }
        var swept = Earliest(true);
        var still = Earliest(false);
        output.WriteLine($"contact at {c.Point}, swept {swept.P} t {swept.T:0.000}, sampled {still.P} t {still.T:0.000}");
        Assert.True(Vector3.Distance(swept.P, c.Point) < 1e-4f);
        Assert.True(still.T > 1f || Vector3.Distance(still.P, c.Point) > 1e-3f);
        Assert.True(Vector3.Distance(seen[k].Volumes[0].Capsule.A, seen[k - 1].Volumes[0].Capsule.A) > 0.02f, "the body moved over the tick");
    }

    // ---- protected people ----

    [Fact]
    public void A_protected_person_is_struck_and_found_but_not_hurt_and_protests()
    {
        // A merchant, protected by the data (the config's list), and a townsman marked protected for the run (--protected).
        foreach (var (target, mark) in new[] { ("Merchant", false), ("Civ12male", true) })
        {
            var (r, a, t) = Blow(15f, target: target, before: (r, t) => { if (mark) ProtectedPeople.Mark(r.World, t); });
            Assert.True(ProtectedPeople.Is(r.World, t));
            var c = Assert.Single(r.Contacts);
            Assert.Equal(ContactMet.Body, c.Met);
            Assert.True(c.Protected);
            Assert.NotEqual(ActionRole.Hurt, r.World.Get<CharacterAnimation>(t).Action);
            var p = Assert.Single(r.Protests);
            Assert.Equal((t, a, c.ContactId), (p.Who, p.Attacker, p.ContactId));
            // The attempt is violence all the same.
            Assert.Contains(r.Risks, x => x.Act == MasqueradeAct.Violence && x.Entity == a);
        }
        Assert.False(ProtectedPeople.Is(Blow(15f).R.World, EntityId.None));
    }

    // ---- violence seen, and the catch ----

    [Fact]
    public void Violence_is_seen_a_guard_is_alarmed_and_a_townsman_flees()
    {
        var r = Make(more: b => PerceptionServices.Register(b, null));
        var config = r.World.Service<PerceptionConfig>();
        config.Night = false;
        foreach (var (name, numbers) in new[] { ("guard", PerceptionTests.Guard), ("townsman", PerceptionTests.Townsman) })
            config.Add(new ObserverKind(Vitomir.Platform.Modules.AssetId.Of("test", "perception/" + name), name, [], numbers, true));
        var a = r.Put("Christof", 0, 0, 90);
        var t = r.Put("Civ12male", 0, 1.3f, -90);
        var guard = PerceptionServices.Witness(r.World, new WitnessRequest("guard", new(6, 2, 0.01f), 180), config.IndexOf("guard"), Locomotion.Sandbox);
        var townsman = PerceptionServices.Witness(r.World, new WitnessRequest("townsman", new(-5, 1, 0.01f), 0), config.IndexOf("townsman"), Locomotion.Sandbox);
        r.Idle(30);
        Assert.Empty(r.Awareness);
        r.Swing(a, 15f);
        r.Idle(Ticks(1.3f));
        var c = Assert.Single(r.Contacts);
        var v = Assert.Single(r.Risks, x => x.Act == MasqueradeAct.Violence);
        Assert.Equal((a, c.Tick), (v.Entity, v.Tick));
        for (int i = 0; i < Ticks(12f) && r.World.Get<Observer>(guard).Level != Vitomir.Platform.Perception.Awareness.Alarmed; i++) r.Idle(1);
        output.WriteLine(string.Join("; ", r.Awareness.Select(x => $"{(x.Observer == guard ? "guard" : "townsman")} {x.To} at {x.Tick / 120f:0.00} s")));
        output.WriteLine(string.Join("; ", r.Reactions.Select(x => $"{(x.Who == guard ? "guard" : "townsman")} {x.Doing}")));
        Assert.Equal(Vitomir.Platform.Perception.Awareness.Alarmed, r.World.Get<Observer>(guard).Level);
        r.Idle(Ticks(6f));
        Assert.Contains(r.Reactions, x => x.Who == townsman && x.Doing == Reaction.Flee);
        Assert.DoesNotContain(r.Reactions, x => x.Who == guard && x.Doing == Reaction.Flee);
        Assert.Contains(r.Reactions, x => x.Who == guard && x.Doing is Reaction.RunTo or Reaction.Challenge or Reaction.GoLook);
    }

    [Fact]
    public void A_blow_in_a_free_place_is_not_violence_seen_and_a_miss_is_none()
    {
        // The whole floor free: the contact is made, no act is raised.
        var free = new TestLevel().Floor(-60, -60, 60, 60).Build();
        var r = Make(new LevelSpace("free", free.Collision) { Places = LevelPlaces.Of([], PlaceKind.Free) });
        var a = r.Put("Christof", 0, 0, 90);
        r.Put("Civ12male", 0, 1.3f, -90);
        r.Idle(30);
        r.Swing(a, 15f);
        r.Idle(Ticks(1.3f));
        Assert.Single(r.Contacts);
        Assert.DoesNotContain(r.Risks, x => x.Act == MasqueradeAct.Violence);
        Assert.DoesNotContain(Blow(0f, d: 3.2f).R.Risks, x => x.Act == MasqueradeAct.Violence);
    }

    [Fact]
    public void The_catch_lets_go_when_the_player_is_struck_once_per_attack_and_the_key_puts_it_back()
    {
        var r = Make();
        var profiles = r.World.Service<MovementProfiles>();
        int human = profiles.Add(new MovementProfile(Vitomir.Platform.Modules.AssetId.Of("test", "movement/human"), "a human", Locomotion.Sandbox, true), isHuman: true);
        var player = r.Put("Christof", 0, 1.3f, -90, player: true);
        profiles.Give(r.World, player, human);
        Assert.Equal(MasqueradeMode.Masquerade, r.World.Get<MasqueradeState>(player).Mode);
        var guard = r.Put("rudolfGuard", 0, 0, 90);
        r.Idle(30);
        // With the catch on, the player's own attack is refused.
        r.Step(new ActionFrame(Vector2.Zero, Vector2.Zero, Buttons.Attack, default));
        r.Idle(2);
        Assert.Empty(r.Begun);
        r.Swing(guard, 15f);
        r.Idle(Ticks(1.3f));
        var c = Assert.Single(r.Contacts, x => x.Target == player);
        var let = Assert.Single(r.LetGo);
        Assert.Equal((player, guard, c.ContactId, c.Tick), (let.Entity, let.Attacker, let.ContactId, let.Tick));
        var changed = Assert.Single(r.Modes);
        Assert.Equal((MasqueradeMode.Unbound, MasqueradeCause.Attacked), (changed.Mode, changed.Cause));
        Assert.Equal(MasqueradeMode.Unbound, r.World.Get<MasqueradeState>(player).Mode);
        // A second blow finds it already let go: no second letting go.
        r.Idle(Ticks(0.5f));
        r.Swing(guard, 15f);
        r.Idle(Ticks(1.3f));
        Assert.Equal(2, r.Contacts.Count(x => x.Target == player));
        Assert.Single(r.LetGo);
        // The key puts it back.
        r.Step(new ActionFrame(Vector2.Zero, Vector2.Zero, Buttons.Masquerade, default));
        r.Idle(1);
        Assert.Equal(MasqueradeMode.Masquerade, r.World.Get<MasqueradeState>(player).Mode);
        Assert.Equal(MasqueradeCause.Key, r.Modes[^1].Cause);
    }

    // ---- replays and costs ----

    static (ulong[] Digests, List<ContactMade> Contacts) Bout(ulong seed)
    {
        var r = Make(seed: seed);
        var a = r.Put("Christof", 0, 0, 90);
        var g = r.Put("rudolfGuard", 0, 1.3f, -90);
        var script = r.World.Service<ActionScript>();
        Assert.True(ActionScript.TryParse("x:attack@0.5^15,x:strong@2.0^-20,x:attack@4^0,x:attack@5.5^25", r.World.Service<CombatSettings>(), out var mine, out var e1), e1);
        Assert.True(ActionScript.TryParse("x:attack@1.2^10,x:block@3+1.5,x:attack@6^-10", r.World.Service<CombatSettings>(), out var his, out var e2), e2);
        foreach (var line in mine) script.Add(a, line);
        foreach (var line in his) script.Add(g, line);
        var digests = new ulong[Ticks(8f)];
        for (int i = 0; i < digests.Length; i++)
        {
            r.Idle(1);
            digests[i] = r.World.Digest();
        }
        return (digests, r.Contacts);
    }

    [Fact]
    public void A_bout_with_contacts_replays_to_the_same_digests_and_the_same_contacts()
    {
        var (d1, c1) = Bout(5);
        var (d2, c2) = Bout(5);
        output.WriteLine(string.Join("; ", c1.Select(c => $"{c.Attacker}->{c.Target} {c.Met} {c.Site} t{c.Tick}")));
        Assert.True(c1.Count >= 3, $"{c1.Count} contacts");
        Assert.Equal(d1, d2);
        Assert.Equal(c1, c2);
    }

    [Fact]
    public void What_contact_costs_a_tick_in_a_duel()
    {
        // Measured, not asserted (a shared 2-CPU machine): a duel of two fighters and a bystander, swinging every 1.25 s.
        double Run(bool contact)
        {
            var r = Make(contact: contact);
            var a = r.Put("Christof", 0, 0, 90);
            var g = r.Put("rudolfGuard", 0, 1.3f, -90);
            r.Put("Civ12male", 1.0f, 0.9f, -150);
            void Round(int i)
            {
                r.World.Get<CombatIntent>(a).Attack = i % 150 < 2;
                r.World.Get<CombatIntent>(g).Attack = i % 170 < 2;
                MovementServices.Step(r.World, ActionFrame.Idle);
            }
            for (int i = 0; i < 300; i++) Round(i);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 300; i < 1500; i++) Round(i);
            return watch.Elapsed.TotalMilliseconds * 1000.0 / 1200;
        }
        double without = Run(false), with = Run(true);
        output.WriteLine($"a tick of the duel: {without:0} µs without contact, {with:0} µs with it ({with - without:0} µs for contact: the swings swept, the near kept)");
        Assert.True(with > 0 && without > 0);
    }

    [Fact]
    public void Contact_allocates_nothing_per_tick_once_going()
    {
        var r = Make();
        var a = r.Put("Christof", 0, 0, 90);
        var g = r.Put("rudolfGuard", 0, 1.3f, -90);
        var t = r.Put("Civ12male", 1.0f, 0.9f, -150);
        int contacts = 0;
        void Round(int i)
        {
            r.World.Get<CombatIntent>(a).Attack = i % 150 < 2;
            r.World.Get<CombatIntent>(a).Aims = true;
            r.World.Get<CombatIntent>(a).AimPitch = (i / 150 % 3 - 1) * 0.3f;
            r.World.Get<CombatIntent>(g).Attack = i % 170 < 2;
            r.World.Get<CombatIntent>(g).Block = i % 400 is >= 200 and < 300;
            MovementServices.Step(r.World, ActionFrame.Idle);
            contacts += r.World.Events<ContactMade>().ThisTick.Count;
        }
        for (int i = 0; i < 900; i++) Round(i);
        Assert.True(contacts >= 3, $"{contacts} contacts while warming");
        int before = contacts;
        Allocations.None("600 ticks of fighting with contact", () =>
        {
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 900; i < 1500; i++) Round(i);
            return GC.GetAllocatedBytesForCurrentThread() - start;
        });
        Assert.True(contacts > before, "contacts were made while measured");
    }
}
