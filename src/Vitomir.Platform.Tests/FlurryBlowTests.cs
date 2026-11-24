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
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The platform's side of a Celerity swing's extra blows (each extra blow its own strike that
/// must still find its target at its own moment): each blow checked at its moment from the geometry — the blade's line carried
/// with the attacker's body, swept against the target's guard and body as they are then — and published as a
/// <see cref="FlurryStrike"/>. A test stands in for the rules (it begins every body contact's flurry with two extra blows). Each
/// failure reason alone: a standing target met by both; one carried out of reach; one dodging away; a shield raised since the
/// first blow; the attacker's cancel; the target's stop. Ids apart from the contact's; the moments; the same run twice the same
/// strikes and digest; nothing allocated per tick. Every test skips without the 1.1 install (VTMR_GAME_DIR).
/// </summary>
[Collection(AllocationCollection.Name)]   // it measures allocations (AllocationTests.cs)
public sealed class FlurryBlowTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static OneOneLibrary Lib()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        return Library.Value!;
    }

    static readonly Dictionary<string, OneOneCharacter> Loaded = [];

    static OneOneCharacter Character(string name, string? weapon)
    {
        var lib = Lib();
        lock (Loaded)
        {
            string key = name + "/" + weapon;
            if (!Loaded.TryGetValue(key, out var c)) Loaded[key] = c = lib.Character(name, weapon) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
            return c;
        }
    }

    static int Ticks(float seconds) => (int)MathF.Round(seconds * World.TicksPerSecond);

    sealed class Ring
    {
        public required World World { get; init; }
        public required Dictionary<string, int> Kinds { get; init; }
        public CharacterAssets Assets => World.Service<CharacterAssets>();
        public readonly List<ContactMade> Contacts = [];
        public readonly List<FlurryStrike> Strikes = [];
        /// <summary>The extra blows the stand-in for the rules begins on each body contact.</summary>
        public int Extra = 2;
        /// <summary>Run after each tick (a test's hand in the world between blows), and in each tick (in <see cref="Phase.Act"/>: an event it publishes is the tick's).</summary>
        public Action<Ring>? After, During;

        public void Idle(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, ActionFrame.Idle);
                Contacts.AddRange(World.Events<ContactMade>().ThisTick);
                Strikes.AddRange(World.Events<FlurryStrike>().ThisTick);
                After?.Invoke(this);
            }
        }

        public EntityId Put(string kind, float x, float y, float yawDegrees)
        {
            float yaw = yawDegrees * DetMath.DegToRad;
            var e = MovementServices.SpawnBody(World, new(x, y, 0.01f), yaw, Locomotion.Sandbox);
            World.Set(e, CharacterAnimation.For(Assets, Kinds[kind]));
            World.Set(e, MotionInput.Standing);
            CombatServices.Arm(World, e);
            return e;
        }

        public void Swing(EntityId who, float aimDegrees = 0f)
        {
            ref var intent = ref World.Get<CombatIntent>(who);
            intent.Aims = true;
            intent.AimPitch = aimDegrees * DetMath.DegToRad;
            intent.Attack = true;
            Idle(1);
            World.Get<CombatIntent>(who).Attack = false;
        }
    }

    static Ring Make(ulong seed = 7, Action<FlurrySettings>? tune = null)
    {
        var assets = new CharacterAssets();
        var kinds = new Dictionary<string, int>
        {
            ["Christof"] = OneOneLibrary.Register(assets, Character("Christof", "BroadSword")),
            ["rudolfGuard"] = OneOneLibrary.Register(assets, Character("rudolfGuard", null)),
            ["Civ12male"] = OneOneLibrary.Register(assets, Character("Civ12male", null)),
        };
        var level = new TestLevel().Floor(-60, -60, 60, 60).Build();
        var b = new WorldBuilder(seed);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        DeathServices.Register(b);
        // As with the rules (the harm's registration): the struck flinch for harm done, not for every touch — no harm here, no flinch.
        if (b.Peek.TryService<HurtRule>(out var hurt)) hurt.OnContact = false; else b.Service(new HurtRule { OnContact = false });
        ContactServices.Register(b, new ContactConfig());
        FlurryBlowServices.Register(b);
        Ring? ring = null;
        // The stand-in for the rules: every body contact's swing carries ring.Extra extra blows (the rules' call, in Resolve).
        b.System(Phase.Resolve, 100, "test.rules", new DelegateSystem(w =>
        {
            var made = w.Events<ContactMade>().ThisTick;
            for (int i = 0; i < made.Count; i++)
                if (made[i].Met == ContactMet.Body && made[i].Chain == 0) FlurryBlows.Begin(w, made[i], ring!.Extra, AngleBand.Normal, 1f, false, 0f);
        }));
        b.System(Phase.Act, 999, "test.hand", new DelegateSystem(_ => ring!.During?.Invoke(ring)));
        var world = b.Build();
        world.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(world);
        tune?.Invoke(world.Service<FlurrySettings>());
        ring = new Ring { World = world, Kinds = kinds };
        return ring;
    }

    /// <summary>Christof's quick forehand at a townsman (or <paramref name="target"/>) 1.3 m in front, level; <paramref name="hand"/> run after each tick.</summary>
    (Ring R, EntityId A, EntityId T) Blow(string target = "Civ12male", Action<Ring, EntityId>? hand = null, Action<FlurrySettings>? tune = null, float aim = 0f)
    {
        var r = Make(tune: tune);
        var a = r.Put("Christof", 0, 0, 90);
        var t = r.Put(target, 0, 1.3f, -90);
        if (hand is not null) r.After = x => hand(x, t);
        r.Idle(30);
        r.Swing(a, aim);
        r.Idle(Ticks(1.3f));
        output.WriteLine($"contacts: {string.Join("; ", r.Contacts.Select(c => $"#{c.ContactId} {c.Met} {c.Site} at tick {c.Tick}"))}");
        output.WriteLine($"strikes: {string.Join("; ", r.Strikes.Select(s => $"{FlurryBlows.Text(s.Strike)} {s.Check} {s.Met} {BodyRegions.Name(s.Region)} at tick {s.Tick}, moved {s.Moved:0.000} m"))}");
        return (r, a, t);
    }

    static ContactMade FirstBody(Ring r) => r.Contacts.First(c => c.Met == ContactMet.Body);

    [Fact]
    public void A_standing_target_is_met_by_every_extra_blow_at_its_own_moment_each_its_own_strike()
    {
        var (r, a, t) = Blow();
        var c = FirstBody(r);
        Assert.Equal(2, r.Strikes.Count);
        Assert.All(r.Strikes, s => Assert.Equal(FlurryCheck.Met, s.Check));
        Assert.All(r.Strikes, s => Assert.Equal((c.ContactId, a, t, c.Swing, 2), (s.ContactId, s.Attacker, s.Target, s.Swing, s.Count)));
        // Logical strikes apart from the physical contact: their own ids, the contact's kept.
        Assert.Equal([FlurryBlows.Id(c.ContactId, 1), FlurryBlows.Id(c.ContactId, 2)], r.Strikes.Select(s => s.Strike));
        Assert.Equal([1, 2], r.Strikes.Select(s => s.Index));
        Assert.Equal($"{c.ContactId}/1", FlurryBlows.Text(r.Strikes[0].Strike));
        Assert.All(r.Strikes, s => Assert.NotEqual(s.ContactId, s.Strike));
        // At their moments: 0.04 s apart (the data's fixed spacing; at 120 ticks a second, 5 ticks).
        Assert.Equal([c.Tick + 5, c.Tick + 10], r.Strikes.Select(s => s.Tick));
        // Each strike's evidence: the contact's id, the part met now, its moment; the body met, a part of the target's.
        Assert.All(r.Strikes, s => Assert.Equal((c.ContactId, s.Tick, ContactMet.Body, BodyRegions.Name(s.Region)), (s.Contact.ContactId, s.Contact.Tick, s.Contact.Met, s.Contact.Site)));
        Assert.All(r.Strikes, s => Assert.NotEqual(BodyRegion.None, s.Region));
        Assert.All(r.Strikes, s => Assert.True(s.Moved >= 0f));   // the struck arm moves with the townsman's hurt: still met
        Assert.False(r.World.Has<FlurryPending>(a));   // done: taken away after the last
    }

    [Fact]
    public void A_target_carried_away_before_the_blow_is_out_of_reach()
    {
        bool moved = false;
        var (r, _, t) = Blow(hand: (x, t) =>
        {
            if (moved || !x.World.TryStore<FlurryPending>(out var p) || p.Count == 0) return;
            x.World.Get<Body>(t).Feet += new Vector3(0, 0.6f, 0);     // stepped 0.6 m straight back, between the blow and the next
            moved = true;
        });
        Assert.Equal(2, r.Strikes.Count);
        Assert.All(r.Strikes, s => Assert.Equal(FlurryCheck.OutOfReach, s.Check));
        Assert.All(r.Strikes, s => Assert.True(s.Moved > 0.15f, $"moved {s.Moved}"));
    }

    [Fact]
    public void A_target_dodging_away_is_dodged()
    {
        bool moved = false;
        var (r, _, t) = Blow(hand: (x, t) =>
        {
            if (moved || !x.World.TryStore<FlurryPending>(out var p) || p.Count == 0) return;
            x.World.Get<Body>(t).Feet += new Vector3(-0.3f, 0.6f, 0);   // carried 0.6 m back and 0.3 m aside by a dodge
            x.World.Set(t, new Dodging { Phase = DodgePhase.Burst, Began = x.World.Tick });
            moved = true;
        });
        Assert.Equal(2, r.Strikes.Count);
        Assert.All(r.Strikes, s => Assert.Equal(FlurryCheck.Dodged, s.Check));
    }

    [Fact]
    public void A_guard_raised_after_the_first_blow_meets_the_later_ones()
    {
        // The guard, facing Christof with his guard down: the first blow (aimed high) meets his head; he raises his block then, and
        // the blows, spread wide enough for the guard to come up (0.3 s apart here; the data's 0.04 s is quicker than any guard),
        // meet his guarding blade: a guard contact for the rules' verdict (held, parried or overpowered).
        bool raised = false;
        var (r, _, t) = Blow("rudolfGuard", (x, t) =>
        {
            if (raised || !x.World.TryStore<FlurryPending>(out var p) || p.Count == 0) return;
            x.World.Get<CombatIntent>(t).Block = true;
            raised = true;
        }, s => s.SpacingSeconds = 0.3f, aim: 25f);
        var c = FirstBody(r);
        Assert.Equal("head", c.Site);
        Assert.Equal(2, r.Strikes.Count);
        Assert.All(r.Strikes, s => Assert.Equal(FlurryCheck.Guarded, s.Check));
        Assert.All(r.Strikes, s => Assert.Contains(s.Met, new[] { ContactMet.Shield, ContactMet.Weapon }));
        Assert.All(r.Strikes, s => Assert.Contains(s.Guard, new[] { GuardMeet.Shield, GuardMeet.Blade, GuardMeet.Parry }));
        Assert.All(r.Strikes, s => Assert.Equal((s.Met, s.Guard), (s.Contact.Met, s.Contact.Guard)));
        Assert.All(r.Strikes, s => Assert.Equal(BodyRegion.Head, s.Region));   // the part behind the guard (an overpowering verdict's)
        Assert.Equal([c.Tick + Ticks(0.3f), c.Tick + Ticks(0.6f)], r.Strikes.Select(s => s.Tick));
        // Without the guard raised the same blows meet the head.
        var (open, _, _) = Blow("rudolfGuard", tune: s => s.SpacingSeconds = 0.3f, aim: 25f);
        Assert.All(open.Strikes, s => Assert.Equal((FlurryCheck.Met, BodyRegion.Head), (s.Check, s.Region)));
    }

    [Fact]
    public void A_cancel_ends_the_flurry_and_the_target_s_stop_ends_it_too()
    {
        // The attacker's cancel (a dodge cancelling its swing, SwingCancel's own call, in the tick) before the first extra blow:
        // both told AttackerStopped, at once.
        var r = Make();
        var a = r.Put("Christof", 0, 0, 90);
        r.Put("Civ12male", 0, 1.3f, -90);
        bool cancelled = false;
        r.During = x =>
        {
            if (cancelled || !x.World.Has<FlurryPending>(a)) return;
            Assert.True(SwingCancel.Do(x.World, a, SwingCancelCause.Dodge));
            cancelled = true;
        };
        r.Idle(30);
        r.Swing(a);
        r.Idle(Ticks(1.3f));
        Assert.Equal([FlurryCheck.AttackerStopped, FlurryCheck.AttackerStopped], r.Strikes.Select(s => s.Check));
        Assert.Equal(r.Strikes[0].Tick, r.Strikes[1].Tick);
        Assert.False(r.World.Has<FlurryPending>(a));

        // The target stopped (the platform's Dead) between the blows: TargetDown, and none after.
        bool down = false;
        var (r2, _, _) = Blow(hand: (x, t) =>
        {
            if (down || !x.World.TryStore<FlurryPending>(out var p) || p.Count == 0) return;
            x.World.Set(t, new Dead { Tick = x.World.Tick });
            down = true;
        });
        Assert.Equal([FlurryCheck.TargetDown, FlurryCheck.TargetDown], r2.Strikes.Select(s => s.Check));
    }

    [Fact]
    public void The_same_run_gives_the_same_strikes_and_digest_and_the_flurry_digested_while_pending()
    {
        static (List<FlurryStrike> Strikes, List<ulong> Digests) Go(FlurryBlowTests t)
        {
            var r = Make();
            var a = r.Put("Christof", 0, 0, 90);
            r.Put("Civ12male", 0, 1.3f, -90);
            var digests = new List<ulong>();
            r.After = x => digests.Add(x.World.Digest());
            r.Idle(30);
            r.Swing(a);
            r.Idle(Ticks(1.3f));
            return (r.Strikes, digests);
        }
        var x = Go(this);
        var y = Go(this);
        Assert.Equal(x.Strikes.Select(s => (s.Strike, s.Check, s.Tick, s.Region, s.Point)), y.Strikes.Select(s => (s.Strike, s.Check, s.Tick, s.Region, s.Point)));
        Assert.Equal(x.Digests, y.Digests);
        Assert.Equal(2, x.Strikes.Count);
    }

    [Fact]
    public void A_pending_flurry_is_in_the_digest_and_moves_it()
    {
        var r = Make();
        var a = r.Put("Christof", 0, 0, 90);
        r.Put("Civ12male", 0, 1.3f, -90);
        r.Idle(30);
        r.Swing(a);
        while (!r.World.Has<FlurryPending>(a) && r.World.Tick < 400) r.Idle(1);
        Assert.True(r.World.Has<FlurryPending>(a));
        ulong with = r.World.Digest();
        ref var p = ref r.World.Get<FlurryPending>(a);
        p.Next++;   // a blow fewer to fall: the digest sees it
        Assert.NotEqual(with, r.World.Digest());
        p.Next--;
        Assert.Equal(with, r.World.Digest());
    }

    [Fact]
    public void Checking_blows_allocates_nothing_per_tick()
    {
        var r = Make();
        var a = r.Put("Christof", 0, 0, 90);
        r.Put("Civ12male", 0, 1.3f, -90);
        r.Extra = 3;
        r.Idle(30);
        // Warm: one swing's flurry through.
        r.Swing(a);
        r.Idle(Ticks(1.3f));
        Assert.NotEmpty(r.Strikes);
        Allocations.None("a swing's extra blows checked", () =>
        {
            r.World.Get<CombatIntent>(a).Attack = true;
            MovementServices.Step(r.World, ActionFrame.Idle);
            r.World.Get<CombatIntent>(a).Attack = false;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Ticks(1.3f); i++) MovementServices.Step(r.World, ActionFrame.Idle);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });
    }

    [Fact]
    public void With_the_motion_on_each_extra_blow_is_seen_re_striking_back_to_the_contact_s_pose_at_its_moment()
    {
        // The clip pulled back from the contact and driven through it again for each blow, arriving at the contact's
        // pose at each moment (0.12 s apart here), then brought back to guard; the strikes the same as without the motion.
        static (Ring R, EntityId A, List<(long Tick, float Clip, BladeHoldState Hold)> Clips) Go(bool motion)
        {
            var r = Make(tune: s => { s.SpacingSeconds = 0.12f; s.Motion = motion; });
            var a = r.Put("Christof", 0, 0, 90);
            r.Put("Civ12male", 0, 1.3f, -90);
            var clips = new List<(long, float, BladeHoldState)>();
            r.After = x =>
            {
                var hold = x.World.Has<BladeHold>(a) ? x.World.Get<BladeHold>(a).State : BladeHoldState.None;
                clips.Add((x.World.Tick - 1, x.World.Get<CharacterAnimation>(a).ActionTime, hold));
            };
            r.Idle(30);
            r.Swing(a);
            r.Idle(Ticks(1.3f));
            return (r, a, clips);
        }
        var (on, a, clips) = Go(true);
        var (off, _, plain) = Go(false);
        var c = FirstBody(on);
        Assert.Equal(2, on.Strikes.Count);
        Assert.Equal(off.Strikes.Select(s => (s.Strike, s.Check, s.Tick, s.Region)), on.Strikes.Select(s => (s.Strike, s.Check, s.Tick, s.Region)));
        float At(List<(long Tick, float Clip, BladeHoldState Hold)> l, long t) => l.First(x => x.Tick == t).Clip;
        float contact = At(clips, c.Tick);
        // At each moment the clip is back at the contact's time; half way between, pulled back by the motion's amount.
        foreach (var s in on.Strikes) Assert.Equal(contact, At(clips, s.Tick), 3);
        long mid1 = (c.Tick + on.Strikes[0].Tick) / 2, mid2 = (on.Strikes[0].Tick + on.Strikes[1].Tick) / 2;
        Assert.True(contact - At(clips, mid1) > 0.05f, $"pulled back {contact - At(clips, mid1):0.000} s of clip at {mid1}");
        Assert.True(contact - At(clips, mid2) > 0.05f, $"pulled back {contact - At(clips, mid2):0.000} s of clip at {mid2}");
        Assert.All(clips.Where(x => x.Tick > c.Tick && x.Tick < on.Strikes[1].Tick), x => Assert.Equal(BladeHoldState.Restrike, x.Hold));
        // Then back to guard as without the motion (the default bring-back blends the swing out: the hold let go).
        Assert.Equal(BladeHoldState.None, clips.First(x => x.Tick == on.Strikes[1].Tick + 1).Hold);
        Assert.Contains(plain, x => x.Tick > c.Tick && x.Hold == BladeHoldState.None);
        Assert.DoesNotContain(plain, x => x.Hold == BladeHoldState.Restrike);
        Assert.False(on.World.Has<FlurryPending>(a));
        output.WriteLine(string.Join(" ", clips.Where(x => x.Tick >= c.Tick && x.Tick <= on.Strikes[1].Tick + 2).Select(x => $"{x.Tick}:{x.Clip:0.000}")));
    }
}
