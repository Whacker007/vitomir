// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;
using Vitomir.Platform.Space;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The rules package's experiment ("Celerity performed physical extra attack"): 1.1's Christof with his broadsword
/// swings at a standing townsman (no shield: the guard's takes every blow) as fast as his blows allow for a Celerity grant's fifteen seconds, once with Celerity paid and
/// once without — real blows, real contact on the drawn bodies, the staged harm. More blows are begun inside the grant; every
/// blow is one animation (one <see cref="AttackBegun"/>) and meets him at most once (one <see cref="ContactMade"/> per
/// swing and target); every contact on his body is resolved once (one <see cref="HarmResolved"/> each), none is made up.
/// Skips without the 1.1 install (VTMR_GAME_DIR). that is the earlier build's curve (all tempo, <c>celerity-curve-tempo</c>); on the
/// default <c>split</c> the grant quickens the blows ×1.41 and each blow on the body carries 1.41 blows (the flurry, every
/// one its own staged harm of the same contact), so the actions — swings and flurry blows — are again about twice.
/// </summary>
public sealed class CelerityFightTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static LevelSpace Floor()
    {
        var v = new List<Vector3>();
        var ix = new List<int>();
        for (int x = -20; x < 20; x += 4)
            for (int y = -20; y < 20; y += 4)
            {
                int at = v.Count;
                v.Add(new(x, y, 0)); v.Add(new(x + 4, y, 0)); v.Add(new(x + 4, y + 4, 0)); v.Add(new(x, y + 4, 0));
                ix.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
            }
        return new LevelSpace("floor", CollisionWorld.FromTriangles([.. v], [.. ix]));
    }

    sealed class Fight
    {
        public required World W;
        public required EntityId Christof, Guard;
        public readonly List<AttackBegun> Begun = [];
        public readonly List<ContactMade> Contacts = [];
        public readonly List<HarmResolved> Harm = [];
        public readonly List<PowerUsed> Used = [];
        public readonly List<PowerEnded> Ended = [];

        public void Step()
        {
            W.Step();
            Begun.AddRange(W.Events<AttackBegun>().ThisTick.Where(b => b.Entity == Christof));
            Contacts.AddRange(W.Events<ContactMade>().ThisTick);
            Harm.AddRange(W.Events<HarmResolved>().ThisTick);
            Used.AddRange(W.Events<PowerUsed>().ThisTick);
            Ended.AddRange(W.Events<PowerEnded>().ThisTick);
        }
    }

    static Fight Make(params string[] switches)
    {
        var lib = Library.Value!;
        var assets = new CharacterAssets();
        int christofKind = OneOneLibrary.Register(assets, lib.Character("Christof", "BroadSword")!);
        int guardKind = OneOneLibrary.Register(assets, lib.Character("Civ12male")!);
        // Severance off — the guard stands the whole grant (a neck cut would stop him whatever his Health); these tests are Celerity's.
        var m = TestData.Sandbox(null, [.. switches, EnhancedModule.SeveranceOffSwitch]);
        var b = new WorldBuilder(21);
        MovementServices.Register(b, Floor());
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        DeathServices.Register(b);
        b.Service(new CombatTrace(null));
        ContactServices.Register(b);
        m.Register(b);
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        var people = w.Service<PeopleHooks>();
        var christof = MovementServices.SpawnBody(w, new(0, 0, 0.01f), DetMath.HalfPi, Locomotion.Sandbox);
        people.Made(christof, PersonRole.Player, "Christof");                         // sandbox.player: Celerity 1, Blood 100
        w.Set(christof, CharacterAnimation.For(assets, christofKind));
        w.Set(christof, MotionInput.Standing);
        CombatServices.Arm(w, christof);
        var guard = MovementServices.SpawnBody(w, new(0, 1.5f, 0.01f), -DetMath.HalfPi, Locomotion.Sandbox);
        people.Made(guard, PersonRole.Townsperson, "Civ12male");                      // sandbox.townsperson_reference
        w.Set(guard, CharacterAnimation.For(assets, guardKind));
        w.Set(guard, MotionInput.Standing);
        CombatServices.Arm(w, guard);
        var sheets = w.Service<ActorSheets>();
        sheets.SetResource(guard, new ResourceState("health", 100000, 100000, 0, 0));   // he stands the whole grant
        return new Fight { W = w, Christof = christof, Guard = guard };
    }

    /// <summary>Christof's quick blows, a press every other tick, for a grant's 15 s (with Celerity paid at its start, or not).</summary>
    static Fight Swing(bool celerity, params string[] switches)
    {
        var f = Make(switches);
        for (int i = 0; i < World.TicksPerSecond; i++) f.Step();
        if (celerity) f.W.Set(f.Christof, new PowerIntent { Slot = 1 });
        for (int i = 0; i < 15 * World.TicksPerSecond; i++)
        {
            f.W.Get<CombatIntent>(f.Christof).Attack = i % 2 == 0;
            f.Step();
        }
        return f;
    }

    [Fact]
    public void More_blows_are_begun_inside_a_Celerity_grant_each_one_animation_one_contact_per_target_and_one_harm_per_contact()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        // With the stagger's push alone (stagger-push-only) — 1.1's hurt clip (the default, played whole) doubles the
        // struck guard over after wounding blows, and a fast attacker's next blows then pass over him (below,
        // The_stagger_against_a_fast_attackers_stream_whole_short_and_push_alone): the bookkeeping, not that, is tested here.
        var plain = Swing(celerity: false, EnhancedModule.CelerityTempoSwitch, EnhancedModule.StaggerPushOnlySwitch);
        var fast = Swing(celerity: true, EnhancedModule.CelerityTempoSwitch, EnhancedModule.StaggerPushOnlySwitch);
        var used = Assert.Single(fast.Used);
        Assert.Equal((1, 30, 15f), (used.Rank, used.Cost, used.Seconds));
        Assert.Empty(fast.Ended);                                                      // the grant ran the whole 15 s
        Assert.Empty(plain.Used);
        Assert.All(fast.Contacts.Concat(plain.Contacts), c => Assert.Equal(ContactMet.Body, c.Met));   // no shield: every blow on the body

        // More blows begun inside the grant: (1 + 1)x the swing and the recovery, so about twice as many.
        Assert.True(plain.Begun.Count >= 5, $"only {plain.Begun.Count} blows without Celerity");
        Assert.InRange(fast.Begun.Count, (int)(plain.Begun.Count * 1.6), (int)(plain.Begun.Count * 2.4) + 1);

        foreach (var f in new[] { plain, fast })
        {
            // Each blow one animation: one AttackBegun per swing number.
            Assert.Equal(f.Begun.Count, f.Begun.Select(b => b.Swing).Distinct().Count());
            // At most one contact per swing and target, and only from blows begun.
            var onGuard = f.Contacts.Where(c => c.Attacker == f.Christof && c.Target == f.Guard).ToList();
            Assert.Equal(onGuard.Count, onGuard.Select(c => c.Swing).Distinct().Count());
            Assert.All(onGuard, c => Assert.Contains(c.Swing, f.Begun.Select(b => b.Swing)));
            Assert.True(onGuard.Count >= f.Begun.Count * 0.8, $"{onGuard.Count} contacts of {f.Begun.Count} blows");
            // One harm per contact on the body; none on a shield or weapon; none made up.
            var body = onGuard.Where(c => c.Met == ContactMet.Body).Select(c => c.ContactId).ToList();
            var harmed = f.Harm.Where(h => h.Attacker == f.Christof).Select(h => h.ContactId).ToList();
            Assert.Equal(body.Order(), harmed.Order());
            Assert.All(f.Harm, h => Assert.Equal((0, 0), (h.Flurry, h.FlurryBlows)));     // all tempo: no flurry
        }
    }

    /// <summary>
    /// Further cases: a guard standing in a fast attacker's stream of blows (Celerity's tempo, 15 s),
    /// staggered by each wounding one: with 1.1's hurt clip (whole, the default; or cut short, stagger-hurt-short) he is
    /// doubled over and some of the next blows pass over him; with the push alone (stagger-push-only) he stays upright and every blow
    /// meets him; the earlier build's flinch (once in 3.5 s) in between. Written out (the measure) and held to its order — at the earlier build's pace
    /// (<c>combat-tempo-off</c>). at the default human pace (the global tempo 0.70) the stream's blows come 1/0.7 as far
    /// apart and every one meets him, the whole hurt included (19 of 19 in its 15 s): the stagger opens a blow, it swallows none.
    /// </summary>
    [Fact]
    public void The_stagger_against_a_fast_attackers_stream_whole_short_and_push_alone()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        (int Contacts, int Blows) Count(params string[] switches)
        {
            var f = Swing(celerity: true, [EnhancedModule.CelerityTempoSwitch, EnhancedModule.CombatTempoOffSwitch, .. switches]);
            return (f.Contacts.Count(c => c.Attacker == f.Christof && c.Target == f.Guard), f.Begun.Count);
        }
        var whole = Count();
        var shortHurt = Count(EnhancedModule.StaggerHurtShortSwitch);
        var push = Count(EnhancedModule.StaggerPushOnlySwitch);
        var flinch = Count(EnhancedModule.StaggerOneOneSwitch);
        output.WriteLine($"contacts of blows: the whole hurt {whole.Contacts} of {whole.Blows}, the short hurt {shortHurt.Contacts} of {shortHurt.Blows}, the push alone {push.Contacts} of {push.Blows}, 1.1's flinch flinch {flinch.Contacts} of {flinch.Blows}");
        Assert.True(push.Contacts >= push.Blows * 0.95, $"push only: {push.Contacts} of {push.Blows}");
        Assert.True(flinch.Contacts >= flinch.Blows * 0.8, $"1.1's flinch: {flinch.Contacts} of {flinch.Blows}");
        Assert.True(whole.Contacts < push.Contacts && shortHurt.Contacts < push.Contacts, $"whole {whole.Contacts}, short {shortHurt.Contacts}, push {push.Contacts}");
        // At the human pace, the whole hurt swallows no blow of the stream.
        var human = Swing(celerity: true, EnhancedModule.CelerityTempoSwitch);
        int humanContacts = human.Contacts.Count(c => c.Attacker == human.Christof && c.Target == human.Guard);
        output.WriteLine($"at the global tempo 0.70: the whole hurt {humanContacts} of {human.Begun.Count}");
        Assert.True(human.Begun.Count < whole.Blows && humanContacts >= human.Begun.Count * 0.95, $"0.70: {humanContacts} of {human.Begun.Count}");
    }

    [Fact]
    public void On_the_split_curve_the_grant_quickens_the_blows_and_each_blow_on_the_body_carries_its_flurry()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var plain = Swing(celerity: false);
        var fast = Swing(celerity: true);                                              // the default, split
        Assert.Single(fast.Used);
        Assert.All(fast.Contacts.Concat(plain.Contacts), c => Assert.Equal(ContactMet.Body, c.Met));
        // The tempo: (1 + 1)^0.5 = x1.41 the swing and the recovery.
        Assert.InRange(fast.Begun.Count, (int)(plain.Begun.Count * 1.15), (int)(plain.Begun.Count * 1.7) + 1);
        Assert.All(plain.Harm, h => Assert.Equal((0, 0), (h.Flurry, h.FlurryBlows)));  // no Celerity, no flurry
        var onGuard = fast.Contacts.Where(c => c.Attacker == fast.Christof && c.Target == fast.Guard).ToList();
        Assert.Equal(onGuard.Count, onGuard.Select(c => c.Swing).Distinct().Count());  // still one contact per swing and target
        // Each contact on the body: its own blow (Flurry 0) and the extra blows its swing carried (1, 2, ...), the same contact.
        var byContact = fast.Harm.Where(h => h.Attacker == fast.Christof).GroupBy(h => h.ContactId).ToList();
        Assert.Equal(onGuard.Select(c => c.ContactId).Order(), byContact.Select(g => g.Key).Order());
        foreach (var g in byContact)
        {
            var blows = g.ToList();
            Assert.Equal(Enumerable.Range(0, blows.Count), blows.Select(h => h.Flurry));
            Assert.All(blows, h => Assert.Equal(blows.Count - 1, h.FlurryBlows));
            Assert.InRange(blows.Count, 1, 2);                                         // rank 1: K = 1.41, 1 or 2 blows a swing
            for (int i = 1; i < blows.Count; i++) Assert.Equal(blows[i - 1].HealthAfter, blows[i].HealthBefore);   // in order
        }
        // The carry: the blows a swing carries average K = 2 / 2^0.5 = 1.41 (floor(carry + K), the fraction kept).
        double perSwing = fast.Harm.Count(h => h.Attacker == fast.Christof) / (double)byContact.Count;
        Assert.InRange(perSwing, 1.3, 1.5);
        // Actions (swings + flurry blows) inside the grant: about (1 + 1)x the blows without, as before pure tempo gave.
        int actions = fast.Begun.Count + fast.Harm.Count(h => h.Attacker == fast.Christof && h.Flurry > 0);
        Assert.InRange(actions, (int)(plain.Begun.Count * 1.6), (int)(plain.Begun.Count * 2.4) + 1);
    }
}
