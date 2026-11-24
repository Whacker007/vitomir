// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The actor sheet, against the rules package's own acceptance items: the integration plan's row
/// ("save/load, 0/100/101/1000 legal, no hidden era/generation change, named option gate"); handbook 07's
/// row ("0/100/101/1000 purchase boundary, temp overcap, same actor clone with era/lineage flip unchanged,
/// save/reload"); handbook 10's ("exact per-point invariance … changing actual genealogy or scenario year
/// alone does not alter action results"); the math contract's rules: Appearance 0 valid; an option explicitly
/// owned; purchased 0..1000, every +1 exactly +1, rejected above 1000; effective = purchased + Σ
/// legal deltas, may pass 1000; another generation or year resolves identically; resource
/// bounds; no duplicate stacking, purchased never mutated; familiarity 0..100; versioned
/// saves; unlearned denied, high Ability not enabling an unlearned power; and the items of the
/// combat rules' checklist
/// that the sheets answer.
/// </summary>
public sealed class ActorSheetTests
{
    static (World W, ActorSheets S, EntityId E) Vampire()
    {
        var (w, s, _) = TestData.World();
        var e = w.Create();
        s.Give(e, "vampire", "13", "modern");
        return (w, s, e);
    }

    [Fact]
    public void Zero_one_hundred_one_hundred_and_one_and_one_thousand_are_legal_purchased_attributes()
    {
        var (_, s, e) = Vampire();
        foreach (var t in s.Layout.Traits.Where(t => t.Kind == TraitKind.Attribute))
            foreach (int v in new[] { 0, 100, 101, 1000 })
            {
                s.SetPurchased(e, t.Id, v);
                Assert.Equal(v, s.Purchased(e, t.Id));
                Assert.Equal(v, s.Effective(e, t.Id));
            }
        s.SetPurchased(e, "appearance", 0);          // Appearance 0 is valid (a Nosferatu's)
        Assert.Equal(0, s.Effective(e, "appearance"));
        Assert.Equal(9, s.Layout.Traits.Count(t => t.Kind == TraitKind.Attribute));
        Assert.Equal(30, s.Layout.Traits.Count(t => t.Kind == TraitKind.Ability));
    }

    [Fact]
    public void One_thousand_and_one_and_minus_one_are_refused_by_name()
    {
        var (_, s, e) = Vampire();
        s.SetPurchased(e, "strength", 640);
        var high = Assert.Throws<SheetValueException>(() => s.SetPurchased(e, "strength", 1001));
        Assert.Equal("purchased strength", high.What);
        Assert.Contains("0..1000", high.Message);
        var low = Assert.Throws<SheetValueException>(() => s.SetPurchased(e, "wits", -1));
        Assert.Equal("purchased wits", low.What);
        Assert.Equal(640, s.Purchased(e, "strength"));   // refused, not clamped
        Assert.Throws<UnknownTraitException>(() => s.SetPurchased(e, "humanity", 5));
    }

    [Fact]
    public void Abilities_take_zero_to_ten_and_eleven_is_refused_by_name()
    {
        var (_, s, e) = Vampire();
        foreach (var t in s.Layout.Traits.Where(t => t.Kind == TraitKind.Ability))
        {
            s.SetPurchased(e, t.Id, 0);
            s.SetPurchased(e, t.Id, 10);
            Assert.Equal(10, s.Purchased(e, t.Id));
        }
        Assert.Equal("purchased melee", Assert.Throws<SheetValueException>(() => s.SetPurchased(e, "melee", 11)).What);
        Assert.Equal("purchased melee", Assert.Throws<SheetValueException>(() => s.SetPurchased(e, "melee", -1)).What);
    }

    [Fact]
    public void Every_single_point_purchase_moves_the_effective_value_by_exactly_one()
    {
        var (_, s, e) = Vampire();
        foreach (var (from, to) in new[] { (0, 1), (99, 100), (100, 101), (400, 401), (999, 1000) })
        {
            s.SetPurchased(e, "dexterity", from);
            int before = s.Effective(e, "dexterity");
            s.SetPurchased(e, "dexterity", to);
            Assert.Equal(1, s.Effective(e, "dexterity") - before);
        }
    }

    [Fact]
    public void Overcap_appears_only_as_a_temporary_effective_value_and_goes_when_it_expires()
    {
        var (w, s, e) = Vampire();
        s.SetPurchased(e, "strength", 1000);
        Assert.Throws<SheetValueException>(() => s.SetPurchased(e, "strength", 1050));
        s.AddModifier(e, new TemporaryModifier("strength", 50, "common.blood_strength", 3));
        Assert.Equal(1050, s.Effective(e, "strength"));
        Assert.Equal(1000, s.Purchased(e, "strength"));   // the purchased value is never rewritten
        // An Ability's domain allows no overcap: its effective value stops at 10.
        s.SetPurchased(e, "melee", 9);
        s.AddModifier(e, new TemporaryModifier("melee", 4, "test.focus", 3));
        Assert.Equal(10, s.Effective(e, "melee"));
        w.Step(); w.Step();
        Assert.Equal(1050, s.Effective(e, "strength"));
        w.Step();                                           // tick 3: the modifiers' end
        Assert.Equal(1000, s.Effective(e, "strength"));
        Assert.Equal(9, s.Effective(e, "melee"));
        Assert.Equal(2, s.ExpireModifiers());
        Assert.Empty(s.Modifiers(e));
        // Below the floor an effective value stops at the domain's minimum; an already expired modifier is refused.
        s.AddModifier(e, new TemporaryModifier("wits", -5, "test.daze", 10));
        Assert.Equal(0, s.Effective(e, "wits"));
        Assert.Throws<SheetValueException>(() => s.AddModifier(e, new TemporaryModifier("wits", 1, "test.late", w.Tick)));
    }

    [Fact]
    public void A_source_applying_its_modifier_again_replaces_it_rather_than_stacking()
    {
        var (_, s, e) = Vampire();
        s.SetPurchased(e, "stamina", 300);
        s.AddModifier(e, new TemporaryModifier("stamina", 40, "common.blood_stamina", 100));
        s.AddModifier(e, new TemporaryModifier("stamina", 40, "common.blood_stamina", 200));
        Assert.Equal(340, s.Effective(e, "stamina"));
        s.AddModifier(e, new TemporaryModifier("stamina", 10, "fortitude.passive", 100));
        Assert.Equal(350, s.Effective(e, "stamina"));
        Assert.Equal(2, s.Modifiers(e).Count);
    }

    [Fact]
    public void A_clone_with_another_generation_or_era_resolves_every_effective_value_identically()
    {
        var (w, s, a) = Vampire();
        int i = 0;
        foreach (var t in s.Layout.Traits) s.SetPurchased(a, t.Id, t.Kind == TraitKind.Attribute ? 37 * ++i % 1001 : i % 11);
        s.AddModifier(a, new TemporaryModifier("strength", 120, "common.blood_strength", 50));
        s.Learn(a, "dominate", 5, "dominate.command");
        s.SetResource(a, new ResourceState("blood", 7, 10, 1, 3));
        var data = s.Export(a);
        foreach (var (gen, era) in new[] { ("4", "dark_ages"), ("15", "modern"), ("", ""), ("13", "1194") })
        {
            var b = w.Create();
            s.Import(b, data with { Generation = gen, Era = era });
            foreach (var t in s.Layout.Traits)
                Assert.Equal(s.Effective(a, t.Id), s.Effective(b, t.Id));
            Assert.Equal(s.AdmitOption(a, "dominate", "dominate.command"), s.AdmitOption(b, "dominate", "dominate.command"));
            Assert.Equal(s.AdmitOption(a, "dominate", "dominate.possession"), s.AdmitOption(b, "dominate", "dominate.possession"));
            Assert.Equal(s.Resource(a, "blood"), s.Resource(b, "blood"));
            Assert.Equal(gen, s.Generation(b));
            Assert.Equal(era, s.Era(b));
        }
    }

    [Fact]
    public void An_unlearned_option_is_refused_by_name()
    {
        var (_, s, e) = Vampire();
        s.Learn(e, "dominate", 5, "dominate.command");
        Assert.Equal(Admission.Yes, s.AdmitOption(e, "dominate", "dominate.command"));
        // Rank 5 brings no option with it (options are bought): Possession is refused.
        var possession = s.AdmitOption(e, "dominate", "dominate.possession");
        Assert.False(possession.Accepted);
        Assert.Equal(DenialCode.Unlearned, possession.Code);
        Assert.Contains("option 'dominate.possession' of power 'dominate' is not learned", possession.Reason);
        // High Intelligence and Occult do not make an unlearned sorcery possible (the math contract).
        s.SetPurchased(e, "intelligence", 1000);
        s.SetPurchased(e, "occult", 10);
        var theft = s.AdmitOption(e, "thaum.path_blood", "thaum.path_blood.theft_of_vitae");
        Assert.Equal(DenialCode.Unlearned, theft.Code);
        Assert.Contains("power 'thaum.path_blood' is not learned", theft.Reason);
        // Learning the option opens it; nothing else changes.
        s.Learn(e, "dominate", 5, "dominate.possession");
        Assert.True(s.AdmitOption(e, "dominate", "dominate.possession").Accepted);
        Assert.Equal(new[] { "dominate.command", "dominate.possession" }, s.Powers(e).Single().Options);
        Assert.Equal(DenialCode.NoSheet, s.AdmitOption(EntityId.None, "dominate", "dominate.command").Code);
        Assert.Throws<SheetValueException>(() => s.Learn(e, "celerity", 11));
    }

    [Fact]
    public void Save_then_load_gives_an_equal_sheet_and_the_same_digest()
    {
        var (w1, s1, e1) = Vampire();
        Fill(s1, e1);
        string save = SheetJson.Write(s1.Export(e1));

        var (w2, s2, _) = TestData.World();
        var e2 = w2.Create();
        s2.Import(e2, SheetJson.Read(save));

        Assert.Equal(s1.Export(e1), s2.Export(e2));
        Assert.Equal(save, SheetJson.Write(s2.Export(e2)));
        Assert.Equal(s1.DigestOf(e1), s2.DigestOf(e2));
        Assert.Equal(w1.Digest(), w2.Digest());
        Assert.Equal(37.5, s2.Familiarity(e2, "weapons"));
        Assert.Equal(1080, s2.Effective(e2, "strength"));
    }

    static void Fill(ActorSheets s, EntityId e)
    {
        s.SetPurchased(e, "strength", 1000);
        s.SetPurchased(e, "appearance", 0);
        s.SetPurchased(e, "firearms", 7);
        s.SetFamiliarity(e, "weapons", 37.5);
        s.SetFamiliarity(e, "stealth_sneaking", 100);
        s.SetResource(e, new ResourceState("blood", 6, 10, 1, 3));
        s.SetResource(e, new ResourceState("health", 12, 12, 0, 0));
        s.Learn(e, "thaum.path_blood", 4, "thaum.path_blood.theft_of_vitae", "thaum.path_blood.blood_rage");
        s.Learn(e, "common", 0, "common.feed", "common.blood_healing");
        s.SetCondition(e, "torpor", "false");
        s.AddModifier(e, new TemporaryModifier("strength", 80, "common.blood_strength", 600));
    }

    [Fact]
    public void A_save_of_another_format_version_or_with_an_unknown_or_missing_trait_is_refused_with_its_reasons()
    {
        var (w, s, e) = Vampire();
        string save = SheetJson.Write(s.Export(e));
        var newer = Assert.Throws<SheetFormatException>(() => SheetJson.Read(save.Replace("\"format_version\": 1", "\"format_version\": 2")));
        Assert.Contains(newer.Problems, p => p.Contains("format_version is 2"));
        var b = w.Create();
        var unknown = Assert.Throws<SheetFormatException>(() => s.Import(b, SheetJson.Read(save.Replace("\"strength\":", "\"brawn\":"))));
        Assert.Contains(unknown.Problems, p => p.Contains("purchased strength is missing"));
        Assert.Contains(unknown.Problems, p => p.Contains("no trait 'brawn'"));
        var tooHigh = Assert.Throws<SheetFormatException>(() => s.Import(b, SheetJson.Read(save.Replace("\"wits\": 0", "\"wits\": 1001"))));
        Assert.Contains(tooHigh.Problems, p => p.Contains("purchased wits = 1001"));
        Assert.False(s.Has(b));   // all or nothing
    }

    [Fact]
    public void Resources_keep_capacity_current_and_throughput_apart()
    {
        var (_, s, e) = Vampire();
        s.SetResource(e, new ResourceState("blood", 5, 10, 1, 2));
        s.SetResource(e, new ResourceState("blood", 5, 20, 1, 2));        // a bigger reserve, the same spending rate
        Assert.Equal(new ResourceState("blood", 5, 20, 1, 2), s.Resource(e, "blood"));
        Assert.Contains("current of resource blood", Assert.Throws<SheetValueException>(() => s.SetResource(e, new ResourceState("blood", 21, 20, 1, 2))).Message);
        Assert.Throws<SheetValueException>(() => s.SetResource(e, new ResourceState("blood", -1, 20, 1, 2)));
        Assert.Throws<SheetValueException>(() => s.SetResource(e, new ResourceState("blood", 0, 20, -1, 2)));
        Assert.Null(s.Resource(e, "mana"));
    }

    [Fact]
    public void Familiarity_is_a_percentage_by_family_and_stops_at_one_hundred()
    {
        var (_, s, e) = Vampire();
        s.SetPurchased(e, "melee", 5);
        foreach (double f in new[] { 0, 42.25, 100 })
        {
            s.SetFamiliarity(e, "weapons", f);
            Assert.Equal(f, s.Familiarity(e, "weapons"));
            Assert.Equal(5, s.Effective(e, "melee"));    // familiarity changes no trait
        }
        Assert.Contains("0..100", Assert.Throws<SheetValueException>(() => s.SetFamiliarity(e, "weapons", 100.5)).Message);
        Assert.Throws<SheetValueException>(() => s.SetFamiliarity(e, "weapons", double.NaN));
        Assert.Throws<SheetValueException>(() => s.SetFamiliarity(e, "lockpicks", 50));
    }

    [Fact]
    public void The_sheets_are_in_the_world_digest_in_a_fixed_order()
    {
        var (w1, s1, _) = TestData.World();
        var (w2, s2, _) = TestData.World();
        var a1 = w1.Create(); var b1 = w1.Create();
        var a2 = w2.Create(); var b2 = w2.Create();
        // The same sheets made in a different order of operations. (The entities get their sheets in the same
        // order: the order of a component store is itself state, as the platform's stores keep it.)
        s1.Give(a1, "vampire"); s1.Give(b1, "human");
        s1.Learn(a1, "dominate", 3, "dominate.command"); s1.Learn(a1, "auspex", 2, "auspex.heightened_senses");
        s1.SetCondition(a1, "hunger", "high"); s1.SetCondition(a1, "torpor", "false");
        s2.Give(a2, "vampire"); s2.Give(b2, "human");
        s2.SetPurchased(b2, "wits", 3); s2.SetPurchased(b2, "wits", 0);
        s2.SetCondition(a2, "torpor", "false"); s2.SetCondition(a2, "hunger", "high");
        s2.Learn(a2, "auspex", 2, "auspex.heightened_senses"); s2.Learn(a2, "dominate", 3, "dominate.command");
        Assert.Equal(w1.Digest(), w2.Digest());
        Assert.Equal(s1.DigestOf(a1), s2.DigestOf(a2));
        s2.SetPurchased(a2, "wits", 1);
        Assert.NotEqual(w1.Digest(), w2.Digest());
    }

    [Fact]
    public void A_sheet_goes_with_its_entity_and_its_slot_starts_clean()
    {
        var (w, s, e) = Vampire();
        s.SetPurchased(e, "strength", 500);
        w.Destroy(e);
        w.Step();
        Assert.False(s.Has(e));
        Assert.Empty(s.Actors);
        var f = w.Create();
        Assert.Equal(e.Index, f.Index);
        Assert.False(s.Has(f));
        s.Give(f, "human");
        Assert.Equal(0, s.Purchased(f, "strength"));
        Assert.True(w.Has<ActorIdentity>(f));
        Assert.Equal(ActorIdentity.KeyOf("human"), w.Get<ActorIdentity>(f).SpeciesKey);
    }

    [Fact]
    public void The_townsperson_template_is_a_sandbox_placeholder()
    {
        var (w, s, m) = TestData.World();
        var t = SheetTemplates.Townsperson(m.Config);
        Assert.Equal(SheetTemplates.PlaceholderStatus, t.Status);
        Assert.Contains("research-only", t.Provenance);
        var e = w.Create();
        SheetTemplates.GiveTownsperson(s, m.Config, e);
        Assert.Equal("human", s.Species(e));
        Assert.Equal(SheetTemplates.TownspersonId, s.Template(e));
        Assert.Equal(25, s.Purchased(e, "strength"));
        Assert.Equal(0, s.Purchased(e, "melee"));
        Assert.Equal(DenialCode.Unlearned, s.AdmitOption(e, "common", "common.feed").Code);
    }
}
