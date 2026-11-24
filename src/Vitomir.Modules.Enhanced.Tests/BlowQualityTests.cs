// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// Blow quality (the rules package's gate-08): the stage alone (Q to the factor, the cap, no
/// floor but a pool of nothing), the hilt, tempo cancelling, <see cref="ResistedBy.LowWork"/> decided by the same dice, the switch
/// (off: the earlier build's stages draw for draw), the cut share re-based on Q, determinism, and no allocation per contact.
/// </summary>
public sealed class BlowQualityTests
{
    const string Damage = "enhanced.harm.damage", Soak = "enhanced.harm.soak", Pool = "enhanced.harm.pool";
    static readonly EntityId A = new(1, 0), T = new(2, 0);

    /// <summary>Dice given in advance, by purpose; a purpose run dry gives 5 (no success, no one) and counts it.</summary>
    sealed class FixedDice : IDeterministicRandom
    {
        readonly Dictionary<string, Queue<int>> d10 = new(StringComparer.Ordinal);
        readonly Dictionary<string, Queue<double>> units = new(StringComparer.Ordinal);
        readonly Dictionary<string, long> positions = new(StringComparer.Ordinal);
        public FixedDice D10(string purpose, params int[] values) { d10[purpose] = new Queue<int>(values); return this; }
        public FixedDice Unit(string purpose, params double[] values) { units[purpose] = new Queue<double>(values); return this; }
        public int NextD10(string purpose) { positions[purpose] = Position(purpose) + 1; return d10.TryGetValue(purpose, out var q) && q.Count > 0 ? q.Dequeue() : 5; }
        public double NextUnitInterval(string purpose) { positions[purpose] = Position(purpose) + 1; return units.TryGetValue(purpose, out var q) && q.Count > 0 ? q.Dequeue() : 0.5; }
        public long Position(string purpose) => positions.TryGetValue(purpose, out var p) ? p : 0;
    }

    /// <summary>A body contact on the chest whose striking point met at <paramref name="impact"/> m/s along the normal, its clip's peak <paramref name="peak"/> m/s, <paramref name="along"/> along the blade.</summary>
    static ContactMade Contact(float impact, float peak = 10f, float along = 0.5f, string site = "chest") =>
        new(1, A, 1, T, ContactMet.Body, site, ContactMaterial.Cloth, 10, new Vector3(1, 2, 1.3f), new Vector3(-1, 0, 0), impact, impact * 1.4f,
            false, false, true, false, PointSpeed: MathF.Max(impact, 0.01f) * 1.2f, AlongBlade: along, TipSpeed: peak * 0.9f, PeakSpeed: peak);

    /// <summary>The reference this file's blows play: clip "clipA", q_ref 0.5.</summary>
    static readonly QualityReference Ref = new("clipA", null, null, 0.5, 100, "test", "ENGINE_SANDBOX", "test");

    static Striker Striker(int strength, string weapon = "BroadSword", string? clip = "clipA", int potence = 0) =>
        new(A, "vampire", strength, potence, potence > 0, weapon, SwingClip: clip);

    static Struck Human(int stamina = 20, long health = 100) => new(T, "human", stamina, 0, false, true, health, 100);

    static HarmRules Rules(bool on = true, bool hilt = true, BlowQualityData? data = null)
    {
        var m = TestData.Sandbox();
        var r = HarmRules.Build(m.Config, m.Harm);
        r.Quality = new BlowQuality(data ?? m.Quality!.With([Ref]), on, hilt);
        return r;
    }

    [Fact]
    public void The_module_reads_its_quality_data_and_runs_it_by_default()
    {
        var m = TestData.Sandbox();
        Assert.NotNull(m.Quality);
        Assert.True(m.QualityOn);
        Assert.EndsWith("rules/quality", m.Quality!.File);
        Assert.Equal((1.0, 1.25, false), (m.Quality.Exponent, m.Quality.Cap, m.Quality.PotenceScaled));
        Assert.Equal(0.1f, m.Quality.HiltBelow);
        Assert.Contains("sword_one_hand", m.Quality.HiltClasses);
        Assert.DoesNotContain("claws", m.Quality.HiltClasses);
        Assert.Contains(m.Quality.References, r => r.Clip == "chrisAttackSword");
        Assert.All(m.Quality.References, r => Assert.Equal("ENGINE_SANDBOX", r.Status));
        Assert.False(TestData.Sandbox(null, EnhancedModule.BlowQualityOffSwitch).QualityOn);
    }

    [Theory]
    [InlineData(0.0f, 0.0, 0.0)]      // Q 0: nothing to deliver, a pool of nothing
    [InlineData(2.5f, 0.5, 0.5)]      // Q 0.5: half the potential
    [InlineData(5.0f, 1.0, 1.0)]      // Q 1: the blow's typical contact keeps its harm
    [InlineData(10.0f, 2.0, 1.25)]    // Q 2: capped at 1.25
    public void The_stage_scales_the_potential_by_min_cap_Q_with_no_roll(float impact, double quality, double factor)
    {
        // Strength 60: 3 dice, + 2 for the sword: 5. q = impact / 10, q_ref 0.5.
        var r = new HarmResult();
        Rules().Resolve(Contact(impact), Striker(60), Human(), new FixedDice(), r);
        Assert.True(r.QualityJudged && r.QualityMeasured);
        Assert.Equal(quality, r.Quality, 5);
        Assert.Equal(factor, r.QualityFactor, 5);
        Assert.Equal(5.0, r.PotentialBeforeQuality, 6);
        Assert.Equal(5.0 * factor, r.Potential, 5);
        Assert.True(r.Outcome is HarmOutcome.Applied or HarmOutcome.Resisted);   // never a miss: the contact is resolved
        Assert.DoesNotContain(r.Draws, d => d.Purpose.Contains("quality"));      // no die of its own
    }

    [Fact]
    public void A_blow_with_no_reference_or_no_peak_speed_is_Q_one()
    {
        var r = new HarmResult();
        Rules().Resolve(Contact(1f), Striker(60, clip: "someOtherClip"), Human(), new FixedDice(), r);
        Assert.True(r.QualityMeasured);
        Assert.Null(r.QualityReference);
        Assert.Equal((1f, 1f), (r.Quality, r.QualityFactor));
        Assert.Equal(5.0, r.Potential, 6);
        Rules().Resolve(Contact(1f, peak: 0f), Striker(60), Human(), new FixedDice(), r);
        Assert.False(r.QualityMeasured);
        Assert.Equal((1f, 1f), (r.Quality, r.QualityFactor));
    }

    [Fact]
    public void Tempo_cancels_the_same_contact_at_three_times_the_rate_is_the_same_Q_and_the_same_harm()
    {
        // A swing played three times as fast (Celerity's or Melee's tempo) meets three times as fast and its clip's peak is three
        // times as high: q and Q are the same, so the same dice give the same harm (no second benefit from speed).
        var one = new HarmResult();
        var three = new HarmResult();
        int[] dice = [7, 6, 2, 9, 1, 8];
        Rules().Resolve(Contact(3.3f, peak: 10f), Striker(60), Human(), new FixedDice().D10(Damage, dice).Unit(Pool, 0.3), one);
        Rules().Resolve(Contact(9.9f, peak: 30f), Striker(60), Human(), new FixedDice().D10(Damage, dice).Unit(Pool, 0.3), three);
        Assert.Equal(one.Quality, three.Quality, 5);
        Assert.Equal(one.QualityFactor, three.QualityFactor, 5);
        Assert.Equal((one.DamagePool, one.DamageSuccesses, one.Wounds), (three.DamagePool, three.DamageSuccesses, three.Wounds));
    }

    [Fact]
    public void The_angle_enters_once_through_q_and_the_cut_share_is_rebased_on_Q()
    {
        // An acute contact (sine 0.3: acute by the angle's bands) that still carried a typical blow's work into the surface (Q 1.2):
        // with quality on, its cut share is the normal band's (Q at or above 0.6), not the acute 0.6 on top of Q.
        var m = TestData.Sandbox();
        var c = Contact(6f) with { PointSpeed = 20f };   // sine 6/20 = 0.3; q 0.6, Q 1.2
        var r = new HarmResult();
        Rules().Resolve(c, Striker(60), Human(), new FixedDice(), r);
        var sever = new SeveranceRules(m.Severance, on: true);
        sever.Judge(in c, r, 0, 100, m.Severance.DefaultBody, 2);
        Assert.Equal(AngleBand.Acute, r.Angle);                 // still measured and traced
        Assert.Equal(AngleBand.Normal, r.QualityCutBand);
        Assert.True(r.CutShareFromQuality);
        Assert.Equal(1f, r.CutShare);
        Assert.Equal("quality", BlowQuality.CutShareFrom(r));
        // Off: the angle's band as before.
        var off = new HarmResult();
        Rules(on: false).Resolve(c, Striker(60), Human(), new FixedDice(), off);
        sever.Judge(in c, off, 0, 100, m.Severance.DefaultBody, 2);
        Assert.Equal(0.6f, off.CutShare, 5);
        Assert.Equal("band", BlowQuality.CutShareFrom(off));
        // A blow of too little work (Q under 0.1) never cuts, however hard: Strength 1000, square on.
        var weak = Contact(0.4f) with { PointSpeed = 0.4f };   // sine 1, q 0.04, Q 0.08
        var w = new HarmResult();
        Rules().Resolve(weak, Striker(1000), Human(), new FixedDice().D10(Damage, Enumerable.Repeat(10, 60).ToArray()), w);
        sever.Judge(in weak, w, 0, 100, m.Severance.DefaultBody, 2);
        Assert.Equal(AngleBand.Normal, w.Angle);
        Assert.Equal(AngleBand.Glancing, w.QualityCutBand);
        Assert.Equal(SeverVerdict.Glancing, w.Verdict);
        Assert.Contains("too little work", SeveranceRules.Reason(w));
    }

    [Fact]
    public void A_meeting_at_the_hilt_is_bashing_never_a_cut()
    {
        var m = TestData.Sandbox();
        var hilt = Contact(5f, along: 0.05f, site: "right forearm");
        var r = new HarmResult();
        Rules().Resolve(hilt, Striker(1000), Human(), new FixedDice().D10(Damage, Enumerable.Repeat(10, 60).ToArray()), r);
        Assert.True(r.Hilt);
        Assert.Equal(HarmKind.Bashing, r.Kind);
        Assert.True(r.StaminaSoaks);                           // a human soaks bashing with Stamina
        new SeveranceRules(m.Severance, on: true).Judge(in hilt, r, 0, 100, m.Severance.DefaultBody, 2);
        Assert.Equal(SeverVerdict.KindDoesNotCut, r.Verdict);
        // Beyond the hilt: lethal as the class is.
        Rules().Resolve(Contact(5f, along: 0.15f), Striker(60), Human(), new FixedDice(), r);
        Assert.False(r.Hilt);
        Assert.Equal(HarmKind.Lethal, r.Kind);
        // Claws have no blade (AlongBlade 0 bare-handed): never a hilt.
        Rules().Resolve(Contact(5f, along: 0f), Striker(60, weapon: "Claw01"), Human(), new FixedDice(), r);
        Assert.False(r.Hilt);
        Assert.Equal(HarmKind.Aggravated, r.Kind);
        // With the hilt off (blow-quality-hilt-off): the class's kind.
        Rules(hilt: false).Resolve(hilt, Striker(60), Human(), new FixedDice(), r);
        Assert.True(r.HiltMeasured);
        Assert.False(r.Hilt);
        Assert.Equal(HarmKind.Lethal, r.Kind);
    }

    [Fact]
    public void A_low_work_slide_is_resisted_by_LowWork_by_the_same_dice_never_missed()
    {
        // Receipt (iv), the slide: 0.362 m/s along the normal of a 72.4 m/s blow (q 0.005), q_ref 0.40: Q 0.0125. The newly
        // Embraced's Rapier, 2.75 dice: 0.034 — one unit draw (0.5) makes it 0 dice; at Q = 1 the same draw makes 3 dice. The d10s
        // drawn are the Q = 1 pool's: 8 9 3 — two successes, a human soaks no lethal: two wounds at Q = 1, none dealt.
        var data = TestData.Sandbox().Quality!.With([new QualityReference("clipA", null, null, 0.40, 870, "test", "ENGINE_SANDBOX", "test")]);
        var slide = Contact(0.362f, peak: 72.413f, along: 0.3f);
        var r = new HarmResult();
        var dice = new FixedDice().D10(Damage, 8, 9, 3).Unit(Pool, 0.5);
        Rules(data: data).Resolve(slide, Striker(15, weapon: "Rapier"), Human(), dice, r);
        Assert.Equal(0.0125, r.Quality, 3);
        Assert.Equal(0, r.DamagePool);
        Assert.Equal(3, r.QualityPoolAtOne);
        Assert.Equal(2, r.QualityWoundsAtOne);
        Assert.Equal(HarmOutcome.Resisted, r.Outcome);           // resolved, not missed
        Assert.Equal(ResistedBy.LowWork, r.ResistedBy);
        Assert.Equal(3, r.Draws.Count(d => d.Purpose == Damage)); // the Q = 1 pool's dice, written down
        // The same slide whose dice would have dealt nothing at Q = 1 either: no damage, as ever.
        Rules(data: data).Resolve(slide, Striker(15, weapon: "Rapier"), Human(), new FixedDice().D10(Damage, 2, 3, 4).Unit(Pool, 0.5), r);
        Assert.Equal(ResistedBy.NoDamage, r.ResistedBy);
        // A clean hit above its typical work (Q > 1) is never "low work", whatever its dice.
        Rules(data: data).Resolve(Contact(40f, peak: 72.413f), Striker(15, weapon: "Rapier"), Human(), new FixedDice().D10(Damage, 1, 1, 1, 1), r);
        Assert.False(r.QualityWouldWound);
        Assert.NotEqual(ResistedBy.LowWork, r.ResistedBy);
    }

    [Fact]
    public void Off_the_stages_are_the_harm_without_quality_draw_for_draw()
    {
        // blow-quality-off: Q measured and recorded, the factor 1, the kind the class's, the draws exactly those of the stages with
        // no quality stage at all, over many random contacts from the world's own streams.
        var m = TestData.Sandbox();
        var bare = HarmRules.Build(m.Config, m.Harm);
        var off = Rules(on: false);
        var rng = new Rng(7);
        var a = new HarmResult();
        var b = new HarmResult();
        var ra = new WorldRandom(new RandomStreams(11));
        var rb = new WorldRandom(new RandomStreams(11));
        for (int i = 0; i < 300; i++)
        {
            float impact = rng.NextFloat() * 12f, along = rng.NextFloat();
            var c = Contact(impact, peak: 10f, along: along);
            var s = Striker(10 + (int)(rng.NextFloat() * 400));
            var t = Human(15 + (int)(rng.NextFloat() * 80));
            bare.Resolve(c, s, t, ra, a);
            off.Resolve(c, s, t, rb, b);
            Assert.Equal((a.Kind, a.DamagePool, a.DamageSuccesses, a.SoakPool, a.SoakSuccesses, a.Wounds, a.ResistedBy, a.HealthAfter),
                         (b.Kind, b.DamagePool, b.DamageSuccesses, b.SoakPool, b.SoakSuccesses, b.Wounds, b.ResistedBy, b.HealthAfter));
            Assert.Equal(a.Draws, b.Draws);
            Assert.Equal(1f, b.QualityFactor);
            Assert.True(b.QualityJudged);
        }
    }

    [Fact]
    public void The_same_seed_gives_the_same_harm_and_another_seed_other_dice()
    {
        var rules = Rules();
        (int, int, int, ResistedBy)[] Run(ulong seed)
        {
            var rnd = new WorldRandom(new RandomStreams(seed));
            var r = new HarmResult();
            var list = new List<(int, int, int, ResistedBy)>();
            for (int i = 0; i < 100; i++)
            {
                rules.Resolve(Contact(0.2f + (i % 20) * 0.5f, along: (i % 7) / 7f), Striker(40 + i), Human(), rnd, r);
                list.Add((r.DamagePool, r.DamageSuccesses, r.Wounds, r.ResistedBy));
            }
            return [.. list];
        }
        Assert.Equal(Run(3), Run(3));
        Assert.NotEqual(Run(3), Run(4));
    }

    [Fact]
    public void Potence_s_automatic_successes_are_unscaled()
    {
        var r = new HarmResult();
        Rules().Resolve(Contact(0.1f), Striker(60, potence: 2), Human(), new FixedDice(), r);
        Assert.True(r.QualityFactor < 0.1f);
        Assert.Equal(2, r.AutoSuccesses);
    }
}

/// <summary>The quality stage allocates nothing per contact (a warm rules object, the world's streams).</summary>
[Collection(AllocationCollection.Name)]
public sealed class BlowQualityAllocationTests
{
    [Fact]
    public void Resolving_a_contact_with_quality_allocates_nothing()
    {
        var m = TestData.Sandbox();
        var rules = HarmRules.Build(m.Config, m.Harm);
        rules.Quality = new BlowQuality(m.Quality!, true);
        var a = new Striker(new EntityId(1, 0), "vampire", 60, 0, false, "Rapier", SwingClip: "chrisAttack2Sword");
        var t = new Struck(new EntityId(2, 0), "human", 20, 0, false, true, 100, 100);
        var rnd = new WorldRandom(new RandomStreams(5));
        var r = new HarmResult();
        ContactMade C(float impact) => new(1, a.Who, 1, t.Who, ContactMet.Body, "chest", ContactMaterial.Cloth, 10, Vector3.Zero, Vector3.UnitX, impact, impact,
            false, false, true, false, PointSpeed: impact + 1f, AlongBlade: 0.4f, TipSpeed: 40f, PeakSpeed: 72f);
        for (int i = 0; i < 200; i++) rules.Resolve(C(i % 40), a, t, rnd, r);   // warm: the draws' list at its size
        Allocations.None("a contact's staged harm with blow quality", () =>
        {
            for (int i = 0; i < 200; i++) rules.Resolve(C(i % 40), a, t, rnd, r);
        });
    }
}
