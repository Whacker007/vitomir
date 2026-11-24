// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Modules.Enhanced.Configuration;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Config;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The configuration, against the rules package's own acceptance items: the integration plan's row
/// ("open shipping profile rejected; sandbox explicitly enabled") and its profile strategy items 1 and 2
/// (the shipping profile "refuses gameplay activation with a precise missing-field report"; the sandbox
/// "opt-in only … parameter provenance every event"); handbook 07's row ("shipping critical numeric nulls
/// rejected with named errors; one unrelated test module loads without Vampire hardcodes"); the math
/// contract's ("a launch config for enhanced shipping must reject unresolved required coefficients");
/// and the checklist's: absent detail shown as open, not invented; no number promoted
/// by being present; a rules-neutral platform, module-defined traits.
/// </summary>
public sealed class ConfigurationTests
{
    static readonly string[] ShippingNulls =
    [
        "ability_factor_max_10", "attack_timing_curves", "auspex_equal_rank_subterfuge_weight", "blood_max_purchase_curve",
        "blood_temp_attribute_per_point", "blood_throughput_purchase_curve", "damage_d10_difficulty", "difficulty_modifiers",
        "dominate_intimidation_weight", "dominate_raw_to_equiv_dice", "familiarity_response_curves", "health_max_purchase_curve",
        "learned_scalar_rest_share", "natural_rest_fraction_by_attribute", "possess_duration_formula",
        "possession_temporary_resistance_formula", "potence_damage_auto_success_per_rank", "raw_to_damage_dice", "raw_to_soak_dice",
        "soak_d10_difficulty", "thaum_intelligence_cast_weight", "thaum_occult_cast_weight", "weapon_damage_bonus_one_hand_sword",
        "xp_purchase_costs",
    ];

    [Fact]
    public void The_shipping_profile_refuses_and_names_every_unresolved_value()
    {
        var m = new EnhancedModule();
        var x = Assert.Throws<ModuleRefusedException>(() => m.Configure(TestData.Context("shipping")));
        Assert.Equal(EnhancedModule.ModuleId, x.ModuleId);
        Assert.Contains(x.Reasons, r => r.Contains("is not enabled") && r.Contains(EnhancedModule.ShippingProfileId));
        Assert.Contains(x.Reasons, r => r.Contains("does not allow shipping"));
        // Every null of the template, each with its gate id; and the two written values, refused as not adopted.
        var gates = JsonDocument.Parse(File.ReadAllText(TestData.File(TestData.GateMappingFile))).RootElement.GetProperty("values");
        foreach (var key in ShippingNulls)
        {
            string gate = gates.GetProperty(key).GetString()!;
            Assert.Contains(x.Reasons, r => r.StartsWith($"value '{key}' is unresolved (gate {gate},", StringComparison.Ordinal));
        }
        Assert.Contains(x.Reasons, r => r.StartsWith("value 'armor_matrix_profile' = \"SOURCE_NEAR_RULES\" is not shipping-approved (gate gate-13"));
        Assert.Contains(x.Reasons, r => r.StartsWith("value 'vampire_bashing_halves_postsoak' = true is not shipping-approved (gate gate-16"));
        Assert.Equal(2 + ShippingNulls.Length + 2, x.Reasons.Count);
        Assert.Throws<InvalidOperationException>(() => m.Stamp);
    }

    [Fact]
    public void The_shipping_profile_is_the_default_and_refuses_with_or_without_its_full_name()
    {
        var byDefault = Assert.Throws<ModuleRefusedException>(() => new EnhancedModule().Configure(TestData.Context()));
        var byName = Assert.Throws<ModuleRefusedException>(() => new EnhancedModule().Configure(TestData.Context(EnhancedModule.ShippingProfileId)));
        Assert.Equal(byDefault.Reasons, byName.Reasons);
    }

    [Fact]
    public void The_sandbox_refuses_without_the_switch_and_names_it()
    {
        var x = Assert.Throws<ModuleRefusedException>(() => new EnhancedModule().Configure(TestData.Context("sandbox")));
        var reason = Assert.Single(x.Reasons);
        Assert.Contains(EnhancedModule.SandboxProfileId, reason);
        Assert.Contains("'sandbox' switch", reason);
    }

    [Fact]
    public void The_sandbox_runs_with_the_switch_and_is_stamped_non_shipping()
    {
        foreach (var profile in new string?[] { null, "sandbox", EnhancedModule.SandboxProfileId })
        {
            var m = new EnhancedModule();
            m.Configure(TestData.Context(profile, null, EnhancedModule.SandboxSwitch));
            Assert.Equal(new ModuleStamp(EnhancedModule.ModuleId, EnhancedModule.SandboxProfileId, "VTMR-H1", true, m.Config.ConfigHash), m.Stamp);
            Assert.True(m.Stamp.NonshippingResearch);
            Assert.Matches("^[0-9a-f]{64}$", m.Stamp.ConfigHash);
            Assert.Contains("RULES SANDBOX", m.Config.Banner);
        }
    }

    [Fact]
    public void A_null_value_read_at_run_time_throws_a_named_unresolved_value()
    {
        var c = TestData.Sandbox().Config;
        var x = Assert.Throws<UnresolvedValueException>(() => c.Number("health_max_purchase_curve"));
        Assert.Equal("health_max_purchase_curve", x.Key);
        Assert.Equal("gate-17", x.GateId);
        Assert.Equal(EnhancedModule.SandboxProfileId, x.ProfileId);
        Assert.Throws<UnresolvedValueException>(() => c.Json("xp_purchase_costs"));
        Assert.Throws<UnresolvedValueException>(() => c.Text("attack_timing_curves"));
        Assert.Throws<KeyNotFoundException>(() => c.Value("no_such_value"));
        // Every null of the sandbox fails closed at its read.
        var nulls = c.Values.Where(v => !v.IsResolved).ToList();
        Assert.Equal(11, nulls.Count);
        foreach (var v in nulls)
        {
            Assert.Equal(ValueStatus.Unresolved, v.Status);
            Assert.Equal(v.GateId, Assert.Throws<UnresolvedValueException>(() => c.Json(v.Key)).GateId);
        }
    }

    [Fact]
    public void A_shipping_profile_runs_once_every_value_is_written_and_every_gate_adopted()
    {
        using var copy = new TestData.Copy();
        copy.Edit(TestData.ShippingFile, s => s.Replace(": null", ": 1")
            .Replace("\"enabled\": false", "\"enabled\": true").Replace("\"shipping_allowed\": false", "\"shipping_allowed\": true"));
        // With every value written but no gate adopted, it still refuses: a written number is not an approved one.
        var x = Assert.Throws<ModuleRefusedException>(() => { var m = new EnhancedModule(); m.Configure(TestData.Context("shipping", copy.Folder)); });
        Assert.Equal(26, x.Reasons.Count);
        Assert.All(x.Reasons, r => Assert.Contains("is not shipping-approved", r));
        copy.Edit(TestData.GateRegisterFile, s => s.Replace("\"game_numeric_adopted\": false", "\"game_numeric_adopted\": true"));
        var ok = new EnhancedModule();
        ok.Configure(TestData.Context(null, copy.Folder));
        Assert.False(ok.Stamp.NonshippingResearch);
        Assert.Equal(EnhancedModule.ShippingProfileId, ok.Stamp.ProfileId);
        Assert.Equal("", ok.Config.Banner);
        Assert.All(ok.Config.Values, v => Assert.Equal(ValueStatus.ShippingApproved, v.Status));
        // The townsperson placeholder is refused outside the sandbox.
        Assert.Throws<InvalidOperationException>(() => SheetTemplates.Townsperson(ok.Config));
    }

    [Fact]
    public void A_written_sandbox_value_carries_its_status_and_provenance_and_is_not_promoted()
    {
        var c = TestData.Sandbox().Config;
        Assert.Equal(2.25, c.Number("ability_factor_max_10"));
        Assert.Equal("raw/20", c.Text("raw_to_damage_dice"));
        Assert.True(c.Flag("vampire_bashing_halves_postsoak"));
        Assert.Equal(0.35, c.Json("natural_rest_fraction_by_attribute").GetProperty("strength").GetDouble());
        var v = c.Value("ability_factor_max_10");
        Assert.Equal(ValueStatus.ResearchOnly, v.Status);
        Assert.Equal("gate-04", v.GateId);
        Assert.Equal("SANDBOX_ONLY_COEFFICIENT", v.GateResearchStatus);
        Assert.False(v.GateAdopted);
        Assert.Contains("SANDBOX_ONLY_COEFFICIENT", v.Provenance);
        // No value of either profile is shipping-approved: the register has adopted none (nothing is promoted by being present).
        Assert.DoesNotContain(c.Values, x => x.Status == ValueStatus.ShippingApproved);
        Assert.Equal(26, c.Values.Count());
    }

    [Fact]
    public void The_configuration_hash_is_stable_and_changes_when_one_value_changes()
    {
        string a = TestData.Sandbox().Stamp.ConfigHash;
        string b = TestData.Sandbox().Stamp.ConfigHash;
        Assert.Equal(a, b);
        using (var copy = new TestData.Copy())
        {
            // The same document laid out differently hashes the same.
            copy.Edit(TestData.SandboxFile, s => JsonSerializer.Serialize(JsonDocument.Parse(s).RootElement, new JsonSerializerOptions { WriteIndented = false }));
            Assert.Equal(a, TestData.Sandbox(copy.Folder).Stamp.ConfigHash);
            // One value changed: another hash.
            copy.Edit(TestData.SandboxFile, s => s.Replace("\"ability_factor_max_10\":2.25", "\"ability_factor_max_10\":2.5"));
            Assert.NotEqual(a, TestData.Sandbox(copy.Folder).Stamp.ConfigHash);
        }
        using (var copy = new TestData.Copy())
        {
            copy.Edit(TestData.RegistryFile, s => s.Replace("\"max\": 10,\n    \"min\": 0\n  },\n  \"attribute_domain\"", "\"max\": 9,\n    \"min\": 0\n  },\n  \"attribute_domain\""));
            Assert.NotEqual(a, TestData.Sandbox(copy.Folder).Stamp.ConfigHash);
        }
    }

    [Fact]
    public void The_configuration_hash_is_the_sha256_of_the_canonical_registry_and_profile_together()
    {
        var c = TestData.Sandbox().Config;
        string registry = System.Text.Encoding.UTF8.GetString(CanonicalJson.CanonicalizeFile(TestData.File(TestData.RegistryFile)));
        string profile = System.Text.Encoding.UTF8.GetString(CanonicalJson.CanonicalizeFile(TestData.File(TestData.SandboxFile)));
        Assert.Equal(ContentHash.Sha256Hex($"{{\"profile\":{profile},\"registry\":{registry}}}"), c.ConfigHash);
    }

    [Fact]
    public void The_data_files_are_the_rules_packages_own_unchanged()
    {
        foreach (var (_, file, _, sha) in EnhancedModule.PackageFiles)
            Assert.Equal(sha, ContentHash.Sha256OfFile(TestData.File(file)));
        var c = TestData.Sandbox().Config;
        Assert.All(c.Files.Where(f => f.PackagePath is not null), f => Assert.True(f.MatchesPackage, f.File));
        Assert.Contains(c.Files, f => f.File == $"enhanced:{EnhancedModule.GateMappingAsset}" && f.PackagePath is null);
        string sources = File.ReadAllText(TestData.File("SOURCES.md"));
        Assert.Contains(EnhancedModule.SourcePackage, sources);
        foreach (var (_, file, path, _) in EnhancedModule.PackageFiles)
            Assert.Contains($"| `{file}` | `{path}`", sources);
        Assert.All(EnhancedModule.PackageFiles, f => Assert.Contains(f.Sha256, sources));
    }

    [Fact]
    public void Every_profile_value_answers_to_a_gate_of_the_register()
    {
        var c = TestData.Sandbox().Config;
        foreach (var v in c.Values) Assert.NotNull(c.Gates.Gate(v.GateId));
        Assert.Equal(36, c.Gates.Gates.Count());
        Assert.Equal("VITOMIR_PROPOSED_UNCONFIRMED_BY_RULES_PACKAGE", c.Gates.MappingStatus);
    }

    [Fact]
    public void A_value_with_no_gate_is_refused_by_name()
    {
        using var copy = new TestData.Copy();
        copy.Edit(TestData.SandboxFile, s => s.Replace("\"values\": {", "\"values\": {\n    \"made_up_coefficient\": 3,"));
        var x = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        Assert.Contains(x.Reasons, r => r.Contains("'made_up_coefficient' answers to no gate"));
    }

    [Fact]
    public void A_missing_or_malformed_data_file_or_an_unknown_profile_is_refused_by_name()
    {
        using (var copy = new TestData.Copy())
        {
            File.Delete(copy.Path_(TestData.RegistryFile));
            var x = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
            Assert.Contains(x.Reasons, r => r.StartsWith($"enhanced:{EnhancedModule.RegistryAsset}: missing"));
        }
        using (var copy = new TestData.Copy())
        {
            copy.Edit(TestData.GateRegisterFile, s => s[..^5]);
            var x = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
            Assert.Contains(x.Reasons, r => r.StartsWith($"enhanced:{EnhancedModule.GateRegisterAsset}: not valid JSON"));
        }
        using (var copy = new TestData.Copy())
        {
            copy.Edit(TestData.RegistryFile, s => s.Replace("\"max_purchased\": 1000", "\"max_purchased\": \"lots\"")
                                                       .Replace("VTMR_VAMPIRE_TRAIT_REGISTRY_H1", "SOMETHING_ELSE"));
            var x = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
            Assert.Contains(x.Reasons, r => r.Contains("schema is 'SOMETHING_ELSE'"));
            Assert.Contains(x.Reasons, r => r.Contains("'max_purchased' must be a number"));
        }
        using (var copy = new TestData.Copy())
        {
            copy.Edit(TestData.SandboxFile, s => s.Replace("\"attribute_purchased_cap\": 1000", "\"attribute_purchased_cap\": 500"));
            var x = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
            Assert.Contains(x.Reasons, r => r.Contains("attribute_purchased_cap 500 disagrees with the registry's max_purchased 1000"));
        }
        var unknown = Assert.Throws<ModuleRefusedException>(() => new EnhancedModule().Configure(TestData.Context("balanced", null, "sandbox")));
        Assert.Contains("no profile 'balanced'", Assert.Single(unknown.Reasons));
    }

    [Fact]
    public void A_profile_asking_for_an_ancestry_or_era_modifier_is_refused()
    {
        using var copy = new TestData.Copy();
        copy.Edit(TestData.SandboxFile, s => s.Replace("\"no_ancestry_or_era_runtime_modifier\": true", "\"no_ancestry_or_era_runtime_modifier\": false"));
        var x = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        Assert.Contains(x.Reasons, r => r.Contains("no ancestry or era modifier"));
    }

    [Fact]
    public void Registering_before_configuring_is_refused_and_registering_adds_the_component_and_services()
    {
        Assert.Throws<InvalidOperationException>(() => new EnhancedModule().Register(new WorldBuilder(1)));
        var (w, sheets, m) = TestData.World();
        Assert.Same(m.Config, w.Service<EnhancedConfig>());
        Assert.Same(sheets, w.Service<ActorSheets>());
        Assert.Equal(0, w.Store<ActorIdentity>().Count);
        Assert.Equal(["Act/78/enhanced.anatomy", "Act/80/enhanced.powers", "Act/85/enhanced.health-bands", "Act/90/enhanced.skill-tempo", "Act/91/enhanced.dodge-tempo", "Act/92/enhanced.guard-skill", "Act/93/enhanced.brain-pace", "Contact/70/platform.flurry-blows", "Resolve/95/enhanced.guard", "Resolve/100/enhanced.harm", "Resolve/110/enhanced.guard-budget"], WorldBuilder.RunOrder(w));   // the brain's pace; the guard by skill; the platform's flurry check, registered with the harm when the program has not; the guard's verdict and the budget it leaves // an earlier build: the anatomy; the powers; the Health bands; the tempo; the staged harm
        Assert.Equal(39, sheets.Layout.Count);
        Assert.Equal(4, sheets.Layout.Families.Count);
        Assert.Equal(24, m.Config.Powers.Count);
    }

    /// <summary>A module of another game: its own traits, no Vampire anything.</summary>
    sealed class UnrelatedModule : IModule
    {
        public ModuleInfo Info { get; } = new("test-unrelated", "1", "none");
        public ModuleStamp Stamp { get; } = new("test-unrelated", "default", "none", false, "");
        public void Configure(ModuleConfigContext context) { }
        public void Register(WorldBuilder world)
        {
            world.Component<ActorIdentity>();
            // Two traits of its own, 0..5, one of them allowed past its cap when boosted.
            var layout = new TraitLayout(
                [new TraitDef("might", TraitKind.Other, "Might", "body", new TraitDomain(0, 5, true)),
                 new TraitDef("guile", TraitKind.Other, "Guile", "mind", new TraitDomain(0, 5, false))],
                ["swords"], 0, 1, 0, 3);
            world.Service(new ActorSheets(layout, world.Peek));
        }
    }

    [Fact]
    public void An_unrelated_module_with_its_own_traits_runs_on_the_platform_without_vampire_rules()
    {
        var b = new WorldBuilder(7);
        var unrelated = new UnrelatedModule();
        unrelated.Configure(TestData.Context());
        unrelated.Register(b);
        var w = b.Build();
        var sheets = w.Service<ActorSheets>();
        Assert.False(w.TryService<EnhancedConfig>(out _));
        var e = w.Create();
        sheets.Give(e, "golem");
        sheets.SetPurchased(e, "might", 5);
        Assert.Throws<SheetValueException>(() => sheets.SetPurchased(e, "might", 6));
        Assert.Throws<UnknownTraitException>(() => sheets.SetPurchased(e, "strength", 1));
        sheets.AddModifier(e, new TemporaryModifier("might", 2, "rune", 10));
        sheets.AddModifier(e, new TemporaryModifier("guile", 9, "rune", 10));
        Assert.Equal(7, sheets.Effective(e, "might"));
        Assert.Equal(5, sheets.Effective(e, "guile"));
        w.Step();
        Assert.NotEqual(0UL, w.Digest());
    }
}
