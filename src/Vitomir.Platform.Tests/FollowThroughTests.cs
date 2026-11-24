// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Reflection;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The blade stops where it stops: a blow that meets a chest bites and stops there, its
/// blade held at the contact and never through the body, then is brought back to guard; the owner swings again when the
/// whole swing would have ended, with a stop or without; a shield and a wall spring it back (its clip's time goes back); with a
/// stand-in rules verdict that cuts a part off and lets the blade go on, the next contact is the chain's second, the cut part
/// is met no more, and a second person in line is met only when the first lets the blade go on; with no verdict, or a
/// protected person, it stops; a lost part drawn collapsed (its bones a thousandth, the rest untouched) and skipped by contact;
/// a lost weapon hand drops the weapon and refuses the attack; determinism, a mid-bite and mid-bring-back load, and nothing
/// allocated per tick. Every test skips without the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class FollowThroughTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);
    static readonly Dictionary<(string, string?), OneOneCharacter> Loaded = [];

    static OneOneCharacter Character(string name, string? weapon = null)
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        lock (Loaded)
        {
            if (!Loaded.TryGetValue((name, weapon), out var c))
                Loaded[(name, weapon)] = c = Library.Value!.Character(name, weapon) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
            return c;
        }
    }

    static int Ticks(float seconds) => (int)MathF.Round(seconds * World.TicksPerSecond);

    /// <summary>
    /// A stand-in for the rules' side (the enhanced module's): a line for every body contact (never a protected
    /// person's), which cuts off the part met and lets the blade go on when <see cref="Cuts"/> says so — the part lost
    /// (<see cref="MissingParts"/>) and <see cref="PartSevered"/> published, as the contract asks.
    /// </summary>
    sealed class StandInRules(Func<ContactMade, BodyRegion, bool> cuts) : ISystem
    {
        public Func<ContactMade, BodyRegion, bool> Cuts { get; } = cuts;

        public void Run(World world)
        {
            var made = world.Events<ContactMade>().ThisTick;
            for (int i = 0; i < made.Count; i++)
            {
                var c = made[i];
                if (c.Met != ContactMet.Body || c.Protected) continue;
                var region = RegionOf(c.Site);
                bool cut = Cuts(c, region);
                if (cut)
                {
                    ref var m = ref world.Store<MissingParts>().Find(c.Target);
                    if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref m)) { world.Set(c.Target, new MissingParts()); m = ref world.Get<MissingParts>(c.Target); }
                    m.Lose(region);
                    world.Events<PartSevered>().Publish(new PartSevered(c.Target, c.Attacker, region, c.Tick, c.ContactId, 0));
                }
                world.Events<HarmResolved>().Publish(new HarmResolved(c.ContactId, c.Attacker, c.Target, c.Tick, HarmKind.Lethal, 3, 2, 0, 1, 0, 1, 100, 85,
                    Severed: cut ? region : BodyRegion.None, Continues: cut, BudgetLeft: 0.5f));
            }
        }
    }

    static BodyRegion RegionOf(string? site)
    {
        foreach (var r in BodyRegions.All) if (BodyRegions.Name(r) == site) return r;
        return BodyRegion.None;
    }

    sealed class Ring
    {
        public required World World { get; init; }
        public required Dictionary<string, int> Kinds { get; init; }
        public CharacterAssets Assets => World.Service<CharacterAssets>();
        public readonly List<ContactMade> Contacts = [];
        public readonly List<SwingFollowThrough> Follow = [];
        public readonly List<AttackBegun> Begun = [];
        public readonly List<AttackRefused> Refused = [];
        public readonly List<SwingEnded> Ended = [];
        public readonly List<StrikeMoment> Strikes = [];

        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, ActionFrame.Idle);
                Contacts.AddRange(World.Events<ContactMade>().ThisTick);
                Follow.AddRange(World.Events<SwingFollowThrough>().ThisTick);
                Begun.AddRange(World.Events<AttackBegun>().ThisTick);
                Refused.AddRange(World.Events<AttackRefused>().ThisTick);
                Ended.AddRange(World.Events<SwingEnded>().ThisTick);
                Strikes.AddRange(World.Events<StrikeMoment>().ThisTick);
            }
        }

        public EntityId Put(string kind, float x, float y, float yawDegrees)
        {
            var e = MovementServices.SpawnBody(World, new(x, y, 0.01f), yawDegrees * DetMath.DegToRad, Locomotion.Sandbox);
            World.Set(e, CharacterAnimation.For(Assets, Kinds[kind]));
            World.Set(e, MotionInput.Standing);
            CombatServices.Arm(World, e);
            return e;
        }

        /// <summary>A tap of attack aimed <paramref name="aimDegrees"/> (the blow begins as it is let go, the next tick).</summary>
        public void Swing(EntityId who, float aimDegrees = 0f)
        {
            ref var intent = ref World.Get<CombatIntent>(who);
            intent.Aims = true;
            intent.AimPitch = aimDegrees * DetMath.DegToRad;
            intent.Attack = true;
            Step();
            World.Get<CombatIntent>(who).Attack = false;
        }
    }

    static readonly string[] AllKinds = ["Christof", "rudolfGuard", "Civ12male", "Merchant"];

    static Ring Make(LevelSpace? level = null, Func<ContactMade, BodyRegion, bool>? cuts = null, Action<ContactConfig>? tune = null, bool flinch = true)
    {
        var assets = new CharacterAssets();
        var kinds = new Dictionary<string, int>();
        foreach (var k in AllKinds) kinds[k] = OneOneLibrary.Register(assets, Character(k, k == "Christof" ? "BroadSword" : null));
        level ??= new TestLevel().Floor(-60, -60, 60, 60).Build();
        var b = new WorldBuilder(7);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        var config = new ContactConfig();
        config.Protected.Add("Merchant");
        tune?.Invoke(config);
        b.Event<HarmResolved>();
        if (!flinch) b.Service(new HurtRule { OnContact = false });   // the struck stand still (no rules here to flinch them by harm)
        ContactServices.Register(b, config);
        if (cuts is not null) b.System(Phase.Resolve, 100, "test.stand-in-rules", new StandInRules(cuts));
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        return new Ring { World = w, Kinds = kinds };
    }

    /// <summary>How deep the blade is in the person's volumes now (m; 0 or less when it is not in them).</summary>
    static float Depth(World w, EntityId a, EntityId t)
    {
        if (!HeldItems.Blade(w, a, out var blade)) return 0f;
        var vols = w.Service<HitVolumes>().Posed(w, t);
        float deepest = -1f;
        foreach (var v in vols)
        {
            float d2 = Geometry.SegmentSegment(blade.Guard, blade.Tip, v.Capsule.A, v.Capsule.B, out _, out _, out _, out _);
            deepest = MathF.Max(deepest, v.Capsule.Radius + blade.Radius - MathF.Sqrt(d2));
        }
        return deepest;
    }

    // ---- a body stops the blade ----

    [Fact]
    public void A_blade_that_meets_a_chest_bites_and_stops_there_never_through_the_body_then_comes_back_to_guard()
    {
        // The townsman stands still (his flinch would move his chest onto the held blade: the blade's own motion is measured).
        var r = Make(flinch: false);
        var a = r.Put("Christof", 0, 0, 90);
        var t = r.Put("Civ12male", 0, 1.3f, -90);
        r.Step(30);
        r.Swing(a, 15f);
        float deepest = 0f, clipAtContact = -1f, clipMost = 0f;
        Vector3 tipAtContact = default;
        int heldTicks = 0;
        long contactTick = -1;
        for (int i = 0; i < Ticks(1.6f); i++)
        {
            r.Step();
            var anim = r.World.Get<CharacterAnimation>(a);
            if (r.Contacts.Count == 1 && contactTick < 0)
            {
                contactTick = r.Contacts[0].Tick;
                clipAtContact = anim.ActionTime;
                Assert.True(HeldItems.Blade(r.World, a, out var b0));
                tipAtContact = b0.Tip;
                // The blade is drawn at the contact: the point met is on it (within a few centimetres).
                Geometry.ClosestOnSegment(r.Contacts[0].Point, b0.Guard, b0.Tip, out float s);
                var on = Vector3.Lerp(b0.Guard, b0.Tip, s);
                Assert.True(Vector3.Distance(on, r.Contacts[0].Point) < 0.04f, $"the contact {Vector3.Distance(on, r.Contacts[0].Point):0.000} m from the blade");
            }
            if (contactTick >= 0 && ActionRoles.IsAttack(anim.Action))
            {
                float depth = Depth(r.World, a, t);
                if (depth > 0.01f) output.WriteLine($"t{r.World.Tick - 1}: depth {depth * 100:0.0} cm, clip {anim.ActionTime * 30:0.0} weight {anim.ActionWeight:0.00} ending {anim.ActionEnding}, his {r.World.Get<CharacterAnimation>(t).Action}");
                deepest = MathF.Max(deepest, depth);
                clipMost = MathF.Max(clipMost, anim.ActionTime);
                if (HeldItems.Blade(r.World, a, out var b) && Vector3.Distance(b.Tip, tipAtContact) < 1e-4f) heldTicks++;
            }
        }
        var c = Assert.Single(r.Contacts);
        output.WriteLine($"met {c.Site} at {c.Tick}; held {heldTicks} ticks; deepest {deepest * 100:0.0} cm; clip at contact {clipAtContact * 30:0.0} frames, most after {clipMost * 30:0.0}");
        Assert.Equal(ContactMet.Body, c.Met);
        Assert.Equal(0, c.Chain);
        var f = Assert.Single(r.Follow);
        Assert.Equal(BladeOutcome.Stops, f.Outcome);
        Assert.Equal(t, f.Whom);
        Assert.Equal(c.ContactId, f.ContactId);
        Assert.Equal(1, f.Chain);
        Assert.InRange(f.ReadySeconds, 0.15f, 0.8f);
        // Held for the bite (0.05 s: six ticks, the contact's own seventh), never carried on past the contact, never through.
        Assert.InRange(heldTicks, 6, 8);
        Assert.True(clipMost <= clipAtContact + 1e-5f, "no carry-through: the clip never passes the contact");
        Assert.True(deepest < 0.03f, $"the blade went {deepest * 100:0.0} cm into him");
        // The swing ended stopped, and the channel is back to the stance.
        Assert.True(Assert.Single(r.Ended).Stopped);
        Assert.Equal(ActionRole.None, r.World.Get<CharacterAnimation>(a).Action);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void The_owner_swings_again_when_the_whole_swing_would_have_ended_with_a_stop_or_without(bool target, bool stopOff)
    {
        var r = Make(tune: stopOff ? c => c.Stop = c.Stop with { Enabled = false } : null);
        var a = r.Put("Christof", 0, 0, 90);
        r.Put("Civ12male", 0, target ? 1.3f : 9f, -90);
        r.Step(30);
        r.Swing(a, 15f);
        r.Step(10);
        r.Swing(a, 15f);        // a press during the swing: buffered, begun as the busy time ends
        r.Step(Ticks(1.5f));
        Assert.Equal(2, r.Begun.Count);
        float gap = (r.Begun[1].Tick - r.Begun[0].Tick) / (float)World.TicksPerSecond;
        output.WriteLine($"target {target}, stop off {stopOff}: contacts {r.Contacts.Count}, the next blow {gap:0.000} s after the first");
        Assert.Equal(target, r.Contacts.Count > 0);
        Assert.InRange(gap, 1.1f - 1.5f / World.TicksPerSecond, 1.1f + 1.5f / World.TicksPerSecond);   // the broadsword's RELOADTIME
    }

    // ---- a shield, a wall: the rebound ----

    [Fact]
    public void A_blade_on_a_shield_or_a_wall_springs_back_its_clip_going_back_then_comes_back_to_guard()
    {
        foreach (var wall in new[] { false, true })
        {
            var level = wall ? new TestLevel().Floor(-60, -60, 60, 60).Box(new(-1.5f, 0.6f, 0f), new(1.5f, 0.75f, 2.5f)).Build() : null;
            // The earlier build's rebound (0.15 m for every blow, no recoil); the scaled one and the recoil are ReboundTests'.
            var r = Make(level, tune: c => c.Stop = c.Stop.FixedRebound());
            var a = r.Put("Christof", 0, 0, 90);
            var t = r.Put(wall ? "Civ12male" : "rudolfGuard", 0, 1.3f, -90);
            if (!wall) r.World.Get<CombatIntent>(t).Block = true;
            r.Step(30);
            r.Swing(a, wall ? 15f : 0f);
            var times = new List<float>();
            var tips = new List<Vector3>();
            for (int i = 0; i < Ticks(1.4f); i++)
            {
                r.Step();
                var anim = r.World.Get<CharacterAnimation>(a);
                if (r.Contacts.Count > 0 && ActionRoles.IsAttack(anim.Action))
                {
                    times.Add(anim.ActionTime);
                    tips.Add(HeldItems.Blade(r.World, a, out var bl) ? bl.Tip : default);
                }
            }
            var c = Assert.Single(r.Contacts);
            Assert.Equal(wall ? ContactMet.Level : ContactMet.Shield, c.Met);
            var f = Assert.Single(r.Follow);
            Assert.Equal(BladeOutcome.Rebounds, f.Outcome);
            Assert.Equal(c.Met, f.Met);
            float sprang = Vector3.Distance(tips[0], tips[12]);
            output.WriteLine($"{(wall ? "wall" : "shield")}: clip {times[0] * 30:0.00} → {times[12] * 30:0.00} frames over the rebound, the tip {sprang * 100:0} cm back; back to guard in {f.ReadySeconds:0.00} s");
            // The clip's time goes back for the rebound (0.1 s, the tip about 0.15 m: "a slight rebound"), then holds while it blends back.
            Assert.True(times.Count > 12);
            Assert.True(times[12] < times[0] - 1e-4f, $"the clip went back from {times[0] * 30:0.00} to {times[12] * 30:0.00} frames");
            Assert.InRange(sprang, 0.04f, 0.4f);
            for (int k = 1; k < times.Count; k++) Assert.True(times[k] <= times[k - 1] + 1e-5f);
            Assert.True(Assert.Single(r.Ended).Stopped);
        }
    }

    [Fact]
    public void The_kept_choices_cut_a_stop_short_at_once_and_stagger_a_blocked_owner()
    {
        // A stop cut short at once (the first module's fade, no bring-back); a blocked owner busy longer.
        foreach (var (shield, frees, stagger) in new[] { (false, true, 0f), (true, true, 0f), (true, false, 0.3f) })
        {
            var r = Make(tune: c => c.Stop = c.Stop with { StopFreesOwnerAtOnce = frees, BlockedStaggerSeconds = stagger });
            var a = r.Put("Christof", 0, 0, 90);
            var t = r.Put(shield ? "rudolfGuard" : "Civ12male", 0, 1.3f, -90);
            if (shield) r.World.Get<CombatIntent>(t).Block = true;
            r.Step(30);
            r.Swing(a, 0f);
            r.Step(10);
            r.Swing(a, 0f);   // buffered
            bool endedSoon = false;
            for (int i = 0; i < Ticks(1.6f); i++)
            {
                r.Step();
                var anim = r.World.Get<CharacterAnimation>(a);
                if (r.Follow.Count == 1 && r.World.Tick - 1 <= r.Follow[0].Tick + Ticks(0.05f) + 1 && anim.ActionEnding && anim.ActionFadeOut == 0f) endedSoon = true;
            }
            var f = r.Follow[0];
            float gap = (r.Begun[1].Tick - r.Begun[0].Tick) / (float)World.TicksPerSecond;
            output.WriteLine($"shield {shield}, frees {frees}, stagger {stagger}: {f.Outcome}, ready {f.ReadySeconds:0.00} s, cut short at once {endedSoon}, the next blow after {gap:0.000} s");
            Assert.Equal(shield ? BladeOutcome.Rebounds : BladeOutcome.Stops, f.Outcome);
            if (frees) { Assert.Equal(0f, f.ReadySeconds); Assert.True(endedSoon, "cut short at once, faded over the channel's 0.2 s"); }
            Assert.InRange(gap, 1.1f + stagger - 1.5f / World.TicksPerSecond, 1.1f + stagger + 1.5f / World.TicksPerSecond);
        }
    }

    // ---- the rules' verdict: goes on, or stops ----

    static bool Arm(BodyRegion r) => r is BodyRegion.LeftUpperArm or BodyRegion.LeftForearm or BodyRegion.LeftHand
        or BodyRegion.RightUpperArm or BodyRegion.RightForearm or BodyRegion.RightHand;

    [Fact]
    public void A_verdict_that_cuts_an_arm_lets_the_blade_go_on_to_the_torso_the_chain_counting_and_the_arm_met_no_more()
    {
        // Aimed level, the forehand meets the townsman's arm beside his chest first (ContactTests' table).
        var r = Make(cuts: (c, region) => Arm(region));
        var a = r.Put("Christof", 0, 0, 90);
        var t = r.Put("Civ12male", 0, 1.3f, -90);
        r.Step(30);
        r.Swing(a, 0f);
        r.Step(Ticks(1.6f));
        output.WriteLine(string.Join("; ", r.Contacts.Select(c => $"#{c.Chain} {c.Met} {c.Site} t{c.Tick}")));
        output.WriteLine(string.Join("; ", r.Follow.Select(f => $"{f.Outcome} {f.Met} {BodyRegions.Name(f.Region)} chain {f.Chain}")));
        Assert.True(r.Contacts.Count >= 2, "the blade went on and met him again");
        var first = r.Contacts[0];
        var cut = RegionOf(first.Site);
        Assert.True(Arm(cut), $"first met {first.Site}");
        Assert.Equal(0, first.Chain);
        for (int i = 1; i < r.Contacts.Count; i++)
        {
            Assert.Equal(i, r.Contacts[i].Chain);
            Assert.NotEqual(first.Site, r.Contacts[i].Site);   // the cut part is met no more
            Assert.True(r.Contacts[i].Tick > first.Tick + Ticks(0.05f) - 1, "the next contact after the bite");
        }
        Assert.Equal(BladeOutcome.GoesOn, r.Follow[0].Outcome);
        Assert.Equal(cut, r.Follow[0].Region);
        Assert.True(AnatomyServices.Lacks(r.World, t, cut));
        // The last thing met stopped it (the torso is not cut by this stand-in).
        var last = r.Contacts[^1];
        Assert.False(Arm(RegionOf(last.Site)));
        Assert.Equal(BladeOutcome.Stops, r.Follow[^1].Outcome);
        Assert.Equal(t, last.Target);
        Assert.InRange(r.Strikes.Count, 0, 1);   // a blade going on after its bite tells its Strike mark once at most
    }

    [Fact]
    public void Two_people_in_line_are_both_met_only_when_the_first_lets_the_blade_go_on()
    {
        foreach (bool cut in new[] { false, true })
        {
            var r = Make(cuts: cut ? (c, region) => true : null);
            var a = r.Put("Christof", 0, 0, 90);
            var near = r.Put("Civ12male", -0.55f, 1.25f, -90);
            var far = r.Put("Civ12male", 0.6f, 1.3f, -90);
            r.Step(30);
            r.Swing(a, 0f);
            r.Step(Ticks(1.6f));
            output.WriteLine($"cut {cut}: {string.Join("; ", r.Contacts.Select(c => $"#{c.Chain} {(c.Target == near ? "left" : c.Target == far ? "right" : "?")} {c.Site} t{c.Tick}"))}");
            var first = r.Contacts[0].Target;   // the forehand comes from his right: the right one first
            var second = first == near ? far : near;
            if (!cut)
            {
                Assert.Single(r.Contacts);   // no verdict (no rules): the blade stops in the first
                Assert.Equal(BladeOutcome.Stops, Assert.Single(r.Follow).Outcome);
            }
            else Assert.Contains(r.Contacts, c => c.Target == second);
            for (int i = 0; i < r.Contacts.Count; i++) Assert.Equal(i, r.Contacts[i].Chain);
        }
    }

    [Fact]
    public void A_protected_person_stops_the_blade_whatever_the_rules_would_cut()
    {
        var r = Make(cuts: (c, region) => true);
        var a = r.Put("Christof", 0, 0, 90);
        var t = r.Put("Merchant", 0, 1.3f, -90);
        r.Step(30);
        r.Swing(a, 0f);
        r.Step(Ticks(1.6f));
        var c = Assert.Single(r.Contacts);
        Assert.True(c.Protected);
        Assert.Equal(BladeOutcome.Stops, Assert.Single(r.Follow).Outcome);
        Assert.False(r.World.TryStore<MissingParts>(out var m) && m.Has(t) && m.Get(t).Any);
    }

    // ---- a lost part: collapsed, skipped, its weapon dropped ----

    [Fact]
    public void A_lost_forearm_is_drawn_collapsed_its_bones_a_thousandth_the_rest_untouched_and_its_volumes_skipped()
    {
        var r = Make();
        var whole = r.Put("Civ12male", 5, 0, 90);
        var maimed = r.Put("Civ12male", 0, 0, 90);
        r.Step(5);
        var poses = r.World.Service<CharacterPoses>();
        var before = poses.Skin(r.World, maimed).ToArray();
        var m = new MissingParts();
        m.Lose(BodyRegion.RightForearm);
        r.World.Set(maimed, m);
        var after = poses.Skin(r.World, maimed).ToArray();
        var reference = poses.Skin(r.World, whole).ToArray();
        var model = r.Assets.Kind(r.World.Get<CharacterAnimation>(maimed).Kind).Model;
        var collapse = poses.Collapse(model)!;
        int gone = 0;
        for (int b = 0; b < after.Length; b++)
        {
            float scale = new Vector3(after[b].M11, after[b].M12, after[b].M13).Length() / MathF.Max(1e-9f, new Vector3(before[b].M11, before[b].M12, before[b].M13).Length());
            if (collapse.Gone(m.Bits, b))
            {
                gone++;
                Assert.InRange(scale, 0.5e-3f, 2e-3f);
                Assert.True(collapse.RegionOf(b) is BodyRegion.RightForearm or BodyRegion.RightHand, $"bone {b} is {collapse.RegionOf(b)}");
            }
            else Assert.Equal(reference[b], after[b]);
        }
        output.WriteLine($"{gone} bones collapsed (the forearm, the hand and what hangs from them)");
        Assert.True(gone >= 3);
        // Its volumes are skipped by contact and the ray; the upper arm's kept.
        var hv = r.World.Service<HitVolumes>();
        uint missing = hv.Missing(r.World, maimed, out var c2);
        Assert.NotNull(c2);
        var vols = hv.Posed(r.World, maimed);
        Assert.Contains(vols.ToArray(), v => v.Region == BodyRegion.RightForearm && c2!.VolumeGone(missing, v.Region, v.Bone));
        Assert.Contains(vols.ToArray(), v => v.Region == BodyRegion.RightHand && c2!.VolumeGone(missing, v.Region, v.Bone));
        Assert.DoesNotContain(vols.ToArray(), v => v.Region is BodyRegion.RightUpperArm or BodyRegion.Chest && c2!.VolumeGone(missing, v.Region, v.Bone));
    }

    [Fact]
    public void A_lost_weapon_hand_drops_the_weapon_and_the_attack_is_refused()
    {
        var r = Make();
        var a = r.Put("Christof", 0, 0, 90);
        r.Step(5);
        Assert.True(HeldItems.Blade(r.World, a, out _));
        var m = new MissingParts();
        m.Lose(BodyRegion.RightHand);
        r.World.Set(a, m);
        Assert.False(HeldItems.Blade(r.World, a, out _));       // dropped: no longer his weapon
        Assert.False(HeldItems.Point(r.World, a, out _));
        Assert.True(HeldItems.WeaponHandGone(r.World, a));
        // Hidden: the held sword's skin collapsed with the hand.
        var poses = r.World.Service<CharacterPoses>();
        var held = new Matrix4x4[64];
        int n = poses.CopyHeldSkin(r.World, a, 0, held);
        var thing = r.Assets.Kind(r.World.Get<CharacterAnimation>(a).Kind).Held[0].Things.First(x => x.Kind == HeldKind.Blade);
        Assert.True(thing.Bone < n);
        Assert.InRange(new Vector3(held[thing.Bone].M11, held[thing.Bone].M12, held[thing.Bone].M13).Length(), 0f, 0.01f);
        r.Swing(a);
        r.Step(30);
        Assert.Empty(r.Begun);
        Assert.Equal(AttackRefusal.NoWeaponHand, Assert.Single(r.Refused).Why);
    }

    // ---- determinism, a load mid-stop, allocation ----

    /// <summary>A bout with stops, rebounds and cuts: one Christof swinging at a townsman (his arms cut), another at a guard's raised shield.</summary>
    static Ring Bout(out EntityId a)
    {
        var r = Make(cuts: (c, region) => Arm(region));
        a = r.Put("Christof", 0, 0, 90);
        r.Put("Civ12male", 0, 1.3f, -90);
        r.Put("Christof", 6, 0, 90);
        var g = r.Put("rudolfGuard", 6, 1.3f, -90);
        r.World.Get<CombatIntent>(g).Block = true;
        return r;
    }

    static void Act(Ring r, EntityId a, int i)
    {
        int at = i % 150;
        for (int k = 0; k < 2; k++)
        {
            ref var intent = ref r.World.Get<CombatIntent>(new EntityId(a.Index + 2 * k, a.Generation));
            intent.Aims = true;
            intent.AimPitch = (i / 150 % 3 == 2 ? 15f : 0f) * DetMath.DegToRad;
            intent.Attack = at == 2;
        }
    }

    [Fact]
    public void A_bout_with_stops_gives_the_same_digests_and_a_load_mid_bite_or_mid_bring_back_goes_on_the_same()
    {
        var x = Bout(out var a);
        var y = Bout(out _);
        int biteAt = -1, backAt = -1;
        for (int i = 0; i < 900; i++)
        {
            Act(x, a, i); Act(y, a, i);
            x.Step(); y.Step();
            Assert.Equal(x.World.Digest(), y.World.Digest());
            if (x.World.TryStore<BladeHold>(out var holds) && holds.Has(a))
            {
                var s = holds.Get(a).State;
                if (s == BladeHoldState.Bite && biteAt < 0 && holds.Get(a).Since < x.World.Tick - 2) biteAt = i;
                if (s == BladeHoldState.BringBack && backAt < 0) backAt = i;
                var an = x.World.Get<CharacterAnimation>(a);
                if (backAt < 0 && an.ActionEnding && an.ActionFadeOut > 0f && an.ActionWeight < 0.7f) backAt = i;   // blended back: mid-way
            }
        }
        output.WriteLine(string.Join("; ", x.Contacts.Select(c => $"t{c.Tick} {c.Met} {c.Target} {c.Site} #{c.Chain}")));
        output.WriteLine($"{x.Contacts.Count} contacts, {x.Follow.Count} follow-throughs ({string.Join(", ", x.Follow.GroupBy(f => f.Outcome).Select(g => $"{g.Key} {g.Count()}"))}); bite at {biteAt}, bring-back at {backAt}");
        Assert.Contains(x.Follow, f => f.Outcome == BladeOutcome.GoesOn);
        Assert.Contains(x.Follow, f => f.Outcome == BladeOutcome.Rebounds);
        Assert.True(biteAt > 0);
        // A load: a fresh world brought to the same tick idle, then every digested component copied in (what a save holds).
        foreach (int at in new[] { biteAt, backAt < 0 ? biteAt + 7 : backAt })
        {
            var src = Bout(out _);
            for (int i = 0; i <= at; i++) { Act(src, a, i); src.Step(); }
            var dst = Bout(out _);
            dst.Step(at + 1);
            Copy(src.World, dst.World);
            Assert.Equal(src.World.Digest(), dst.World.Digest());
            int contactsBefore = src.Contacts.Count, followBefore = src.Follow.Count;
            for (int i = at + 1; i < at + 300; i++)
            {
                Act(src, a, i); Act(dst, a, i);
                src.Step(); dst.Step();
                Assert.Equal(src.World.Digest(), dst.World.Digest());
            }
            Assert.Equal(src.Contacts.Skip(contactsBefore).Select(c => (c.Tick, c.Site, c.Chain)), dst.Contacts.Select(c => (c.Tick, c.Site, c.Chain)));
            Assert.Equal(src.Follow.Skip(followBefore), dst.Follow);
        }
    }

    static void Copy(World from, World to)
    {
        var method = typeof(FollowThroughTests).GetMethod(nameof(CopyStore), BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var s in from.Stores)
        {
            if (!s.Digested) continue;
            var other = to.Stores.First(o => o.ComponentType == s.ComponentType);
            method.MakeGenericMethod(s.ComponentType).Invoke(null, [s, other]);
        }
    }

    static void CopyStore<T>(Store<T> from, Store<T> to) where T : struct
    {
        var ents = to.Entities.ToArray();
        foreach (var e in ents) if (!from.Has(e)) to.Remove(e);
        var src = from.Entities.ToArray();
        for (int i = 0; i < src.Length; i++) to.Set(src[i], from.Get(src[i]));
    }

    [Fact]
    public void A_tick_of_stops_and_rebounds_allocates_nothing_once_going()
    {
        var r = Bout(out var a);
        var townsman = new EntityId(a.Index + 1, a.Generation);
        // The stand-in's first MissingParts for the townsman is its store's first entry (the rules' own cost, not the platform's).
        r.World.Set(townsman, new MissingParts());
        for (int i = 0; i < 900; i++) { Act(r, a, i); MovementServices.Step(r.World, ActionFrame.Idle); }
        // After a one-off 8,160 bytes in a whole-suite run that no run here repeats: measured as the
        // platform's allocation tests are (AllocationTests' Allocations) — every event queue given room first, and the
        // window measured more than once (the same world's next 900 ticks): a real allocation shows in every measure, a
        // one-off (a type's first use on this thread, a queue's first growth) in one.
        Allocations.Warm(r.World);
        int tick = 900, follows = 0, cuts = 0;
        Allocations.None("900 ticks of stops and rebounds", () =>
        {
            r.World.Get<MissingParts>(townsman).Bits = 0;   // whole again, so his arms are cut again in the window measured
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int end = tick + 900; tick < end; tick++)
            {
                Act(r, a, tick);
                MovementServices.Step(r.World, ActionFrame.Idle);
                follows += r.World.Events<SwingFollowThrough>().ThisTick.Count;
                cuts += r.World.Events<PartSevered>().ThisTick.Count;
            }
            long spent = GC.GetAllocatedBytesForCurrentThread() - before;
            output.WriteLine($"{spent} bytes over 900 ticks ({follows} follow-throughs, {cuts} parts cut so far)");
            return spent;
        });
        Assert.True(follows >= 6 && cuts >= 1);
    }
}
