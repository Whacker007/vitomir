// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Perception;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The enhanced module's contact data (Data/combat/): the active window's rule, the
/// weapons' matter by carry class and by template, the shields', the body's and the level's surfaces, the parry rule and
/// the protected people read from the module's folder (SANDBOX) and given to the platform's contact; violence in the act
/// table; and a field missing, unknown, out of range or of the wrong kind refused by name. where the blade stops (the
/// <c>stop</c> block), read, turned off by <c>--contact-stop-off</c>, and refused by name.
/// </summary>
public sealed class ContactDataTests
{
    static ContactConfig Registered(EnhancedModule? module = null)
    {
        var m = module ?? TestData.Sandbox();
        var b = new WorldBuilder(1);
        PerceptionServices.Register(b);
        ContactServices.Register(b);
        m.Register(b);
        return b.Build().Service<ContactConfig>();
    }

    [Fact]
    public void The_window_the_matter_the_surfaces_and_the_protected_people_are_read_and_given_to_the_platform_s_contact()
    {
        var c = Registered();
        Assert.True(c.Sandbox);
        Assert.Equal((0.4f, 1f / 240f, 0.05f, 0.1f), (c.Window.ShareOfPeak, c.Window.SampleSeconds, c.Window.BridgeSeconds, c.Window.LeastSeconds), new ToleranceComparer());
        Assert.Equal(ParryRule.WhileBlocking, c.Parry);
        Assert.Equal((0.06f, 3.5f), (c.HandRadius, c.ReachAtMost));
        Assert.Equal(ContactMaterial.Flesh, c.Body);
        Assert.Equal(5, c.Classes.Length);
        Assert.Equal(new WeaponMatter(ContactMaterial.Flesh, 0.6f), c.ClassOf(0));
        Assert.Equal(new WeaponMatter(ContactMaterial.Steel, 1.4f), c.ClassOf(1));
        Assert.Equal(new WeaponMatter(ContactMaterial.Steel, 3.0f), c.ClassOf(4));
        Assert.Equal(new WeaponMatter(ContactMaterial.Steel, 2.5f), c.Weapons["GreatSword"]);
        Assert.Equal(new WeaponMatter(ContactMaterial.Wood, 0.5f), c.Weapons["stake"]);
        Assert.Equal(ContactMaterial.Wood, c.Shields["weapRapier_shield"]);
        Assert.Equal((ContactMaterial.Wood, ContactMaterial.Stone), (c.ShieldDefault, c.LevelDefault));
        Assert.Contains(new SurfaceRule("wood", ContactMaterial.Wood), c.Surfaces);
        Assert.Contains(new SurfaceRule("straw", ContactMaterial.Earth), c.Surfaces);
        Assert.Contains("Merchant", c.Protected);
        Assert.Contains("Civ03merchant1", c.Protected);
    }

    [Fact]
    public void Violence_is_in_the_act_table_little_strange_very_threatening_up_to_alarmed()
    {
        var b = new WorldBuilder(1);
        PerceptionServices.Register(b);
        TestData.Sandbox().Register(b);
        var p = b.Build().Service<PerceptionConfig>();
        var v = p.Act(MasqueradeAct.Violence);
        Assert.Equal(new ActResponse(0.3f, 0.9f, Awareness.Alarmed), v);
        Assert.True(v.Strangeness < p.Act(MasqueradeAct.Jump).Strangeness && v.Threat > p.Act(MasqueradeAct.Jump).Threat);
    }

    [Fact]
    public void A_missing_an_unknown_a_wrong_and_an_out_of_range_field_are_each_refused_by_name()
    {
        using var copy = new TestData.Copy();
        copy.Edit("combat/weapons.json", s => s
            .Replace("\"shareOfPeak\": 0.4", "\"shareOfPeek\": 0.4")
            .Replace("\"parry\": \"whileBlocking\"", "\"parry\": \"sometimes\"")
            .Replace("\"GreatSword\": { \"material\": \"steel\", \"massKg\": 2.5 }", "\"GreatSword\": { \"material\": \"glass\", \"massKg\": 2.5 }")
            .Replace("\"handRadius\": 0.06", "\"handRadius\": 2")
            .Replace("\"2\": { \"name\": \"two-handed blunt\", \"material\": \"steel\", \"massKg\": 3.0 },", ""));
        copy.Edit("combat/protected.json", s => s.Replace("\"people\": [ \"Merchant\"", "\"people\": [ 7"));
        var refused = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        void Has(string text) => Assert.Contains(refused.Reasons, r => r.Contains(text, StringComparison.Ordinal));
        Has("enhanced:combat/weapons: activeWindow: unknown field 'shareOfPeek'");
        Has("enhanced:combat/weapons: activeWindow: 'shareOfPeak' is missing");
        Has("enhanced:combat/weapons: parry is 'sometimes'");
        Has("enhanced:combat/weapons: weapons: 'GreatSword': material 'glass' is none of");
        Has("enhanced:combat/weapons: 'handRadius' is 2; it must be 0.005 to 0.5");
        Has("enhanced:combat/weapons: classes: carry class '2' is missing");
        Has("enhanced:combat/protected: people: each is the name of a kind of character");
        Assert.Equal(7, refused.Reasons.Count);
    }

    [Fact]
    public void Where_the_blade_stops_is_read_given_to_contact_turned_off_by_the_switch_and_refused_by_name()
    {
        // The data's stop block, the platform's BladeStopRule.
        var stop = Registered().Stop;
        Assert.True(stop.Enabled);
        Assert.Equal((0.05f, 0.1f, 0.15f, 2f), (stop.BiteSeconds, stop.ReboundSeconds, stop.ReboundMetres, stop.ReboundRate), new ToleranceComparer());
        Assert.Equal((0.15f, 0.8f, 0f, 0f), (stop.ReadySecondsLeast, stop.ReadySecondsMost, stop.BlockedStaggerSeconds, 0f), new ToleranceComparer());
        Assert.Equal(BringBackKind.Blend, stop.BringBack);
        Assert.False(stop.StopFreesOwnerAtOnce);
        // --contact-stop-off: the first contact rule, the rest of the numbers as read.
        var off = Registered(TestData.Sandbox(null, EnhancedModule.ContactStopOffSwitch)).Stop;
        Assert.Equal(stop with { Enabled = false }, off);
        using var copy = new TestData.Copy();
        copy.Edit("combat/weapons.json", s => s
            .Replace("\"bringBack\": \"blend\"", "\"bringBack\": \"backwards\"")
            .Replace("\"biteSeconds\": 0.05", "\"biteSecs\": 0.05")
            .Replace("\"readySecondsMost\": 0.8", "\"readySecondsMost\": 0.1"));
        var refused = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        void Has(string text) => Assert.Contains(refused.Reasons, r => r.Contains(text, StringComparison.Ordinal));
        Has("enhanced:combat/weapons: stop: bringBack is 'backwards'; it is reverse or blend");
        Has("enhanced:combat/weapons: stop: unknown field 'biteSecs'");
        Has("enhanced:combat/weapons: stop: 'biteSeconds' is missing");
        Has("enhanced:combat/weapons: stop: 'readySecondsMost' is less than 'readySecondsLeast'");
    }

    [Fact]
    public void The_scaled_rebound_the_recoil_and_the_stun_are_read_the_switches_size_them_and_a_bad_field_is_refused()
    {
        // Stop.scaled, the platform's BladeStopRule's numbers (ENGINE_SANDBOX); the design asks for the
        // distances 1.5 times the earlier build's and the stun counted from the swing's end.
        var stop = Registered().Stop;
        Assert.True(stop.ReboundByImpulse);
        Assert.Equal((0.018f, 0.22f, 0.75f, 0.12f), (stop.ReboundMetresPerImpulse, stop.ReboundLeastMetres, stop.ReboundMostMetres, stop.ReboundScaledSeconds), new ToleranceComparer());
        Assert.Equal((0.0045f, 0.18f, 0.06f, 0.45f), (stop.RecoilMetresPerImpulse, stop.RecoilMostMetres, stop.RecoilPeakSeconds, stop.RecoilSettleSeconds), new ToleranceComparer());
        Assert.True(stop.StunFromSwingEnd);
        // rebound-two-thirds gives the earlier sizes exactly (and nothing else), rebound-stun-at-meeting the earlier stun.
        var old = Registered(TestData.Sandbox(null, EnhancedModule.ReboundTwoThirdsSwitch)).Stop;
        Assert.Equal(stop with { ReboundMetresPerImpulse = 0.012f, ReboundLeastMetres = 0.15f, ReboundMostMetres = 0.5f, RecoilMetresPerImpulse = 0.003f, RecoilMostMetres = 0.12f }, old);
        Assert.Equal(stop with { StunFromSwingEnd = false }, Registered(TestData.Sandbox(null, EnhancedModule.ReboundStunAtMeetingSwitch)).Stop);
        Assert.Equal((1.2f, 2.5f, 0.25f, 0.5f), (stop.RecoilLeanPerMetre, stop.RecoilWristPerMetre, stop.StunHeldSeconds, stop.StunParriedSeconds), new ToleranceComparer());
        Assert.True(stop.GuardAfterRebound);
        Assert.Equal(MaterialFactors.Default, stop.ReboundByMaterial);
        Assert.Equal(0.4f, stop.ReboundByMaterial[ContactMaterial.Earth]);
        Assert.Equal(stop.FixedRebound(), Registered(TestData.Sandbox(null, EnhancedModule.ReboundFixedSwitch)).Stop);
        Assert.Equal(stop.Sized(0.5f), Registered(TestData.Sandbox(null, EnhancedModule.ReboundSmallSwitch)).Stop);
        Assert.Equal(stop.Sized(1.8f), Registered(TestData.Sandbox(null, EnhancedModule.ReboundLargeSwitch)).Stop);
        using var copy = new TestData.Copy();
        copy.Edit("combat/weapons.json", s => s
            .Replace("\"metresPerImpulse\": 0.018", "\"metresPerImpulse\": 0.018, \"bounce\": 1")
            .Replace("\"mostMetres\": 0.75", "\"mostMetres\": 0.1")
            .Replace("\"heldSeconds\": 0.25", "\"heldSeconds\": 9")
            .Replace("\"earth\": 0.4", "\"glass\": 0.4"));
        var refused = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        void Has(string text) => Assert.Contains(refused.Reasons, r => r.Contains(text, StringComparison.Ordinal));
        Has("enhanced:combat/weapons: stop.scaled: unknown field 'bounce'");
        Has("enhanced:combat/weapons: stop.scaled: 'mostMetres' is less than 'leastMetres'");
        Has("enhanced:combat/weapons: stop.scaled.stun: 'heldSeconds' is 9; it must be 0 to 2");
        Has("enhanced:combat/weapons: stop.scaled.levelByMaterial: 'glass' is no material");
    }

    [Fact]
    public void Without_the_matter_file_the_module_refuses_and_other_schemas_in_the_folder_are_left_alone()
    {
        using var copy = new TestData.Copy();
        File.WriteAllText(copy.Path_("combat/someone_elses.json"), "{ \"schema\": \"VITOMIR_SOMETHING_ELSE\", \"numbers\": 3 }");
        Assert.NotNull(TestData.Sandbox(copy.Folder));
        File.Delete(copy.Path_("combat/weapons.json"));
        var refused = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        Assert.Contains(refused.Reasons, r => r.Contains("enhanced:combat/: no VITOMIR_COMBAT_MATTER file", StringComparison.Ordinal));
    }

    sealed class ToleranceComparer : IEqualityComparer<(float, float, float, float)>
    {
        static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-6f;
        public bool Equals((float, float, float, float) x, (float, float, float, float) y) => Near(x.Item1, y.Item1) && Near(x.Item2, y.Item2) && Near(x.Item3, y.Item3) && Near(x.Item4, y.Item4);
        public int GetHashCode((float, float, float, float) obj) => 0;
    }
}
