// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Navigation;
using Vitomir.Platform.Space;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The enhanced module's movement profiles:
/// the three SANDBOX files read from the module's folder with the movement design's numbers, every wrong, missing or
/// unknown number refused by name (fail closed), the human's required, the configuration hash and the shipping
/// refusal unchanged by them, and each person given its profile — the player the one asked for, the townsfolk human.
/// </summary>
public sealed class MovementProfileTests
{
    static MovementProfile Profile(EnhancedModule m, string name) => m.Movement.Single(p => p.Name == name);

    [Fact]
    public void The_three_profiles_are_read_from_the_module_s_folder_with_the_human_s_numbers_of_the_design()
    {
        var m = TestData.Sandbox();
        Assert.Equal(["human", "elder", "neonate"], m.Movement.Select(p => p.Name));
        Assert.All(m.Movement, p => Assert.True(p.Sandbox));
        Assert.Equal("enhanced:movement/human", m.Movement[0].Id.ToString());
        var human = Profile(m, "human").Numbers;
        Assert.Equal((0.3f, 1.8f, 1.65f, 1.15f), (human.Radius, human.Height, human.EyeHeight, human.CrouchHeight));
        Assert.Equal((0.35f, 40f), (human.StepHeight, human.SteepestGroundDegrees));
        Assert.Equal((1.3f, 3.0f, 5.5f, 1.0f), (human.WalkSpeed, human.RunSpeed, human.SprintSpeed, human.CrouchSpeed));
        Assert.Equal(0.5f, Masquerade.JumpHeight(human), 3);
        Assert.Equal((2.3f, 0.6f, 1.2f, 1.9f), (human.StandingReach, human.VaultMin, human.VaultMax, human.MantleMax));
        // The pace's moves, the same for every body (sandbox), and the resting speed (none for the human).
        Assert.Equal((0.5f, 1.0f, 0.7f, 0f), (human.StepOverMax, human.SprintVaultMax, human.SprintVaultDepth, human.RestingSpeed));
        Assert.True(float.IsPositiveInfinity(human.GripSeconds));
        Assert.True(float.IsPositiveInfinity(human.SprintEndurance));
        Assert.Equal(3.0f / 0.15f, human.AccelerationCapacity, 3);
        var elder = Profile(m, "elder").Numbers;
        Assert.Equal(3.0f, Masquerade.JumpHeight(elder), 3);
        Assert.Equal(9.0f, elder.SprintSpeed);
        Assert.Equal((9.0f, 1.0f), (elder.RestingSpeed, elder.SprintVaultMax));
        Assert.True(Masquerade.SafeFall(elder) > 14f);
        var neonate = Profile(m, "neonate").Numbers;
        Assert.InRange(Masquerade.JumpHeight(neonate), Masquerade.JumpHeight(human) + 0.1f, Masquerade.JumpHeight(elder) - 0.1f);
        Assert.InRange(neonate.SprintSpeed, human.SprintSpeed + 0.1f, elder.SprintSpeed - 0.1f);
        Assert.Equal(7.0f, neonate.RestingSpeed);
    }

    [Fact]
    public void Each_safe_fall_is_its_kind_s_multiplier_times_the_highest_it_gets_up_by_itself_and_the_masquerade_holds_it_to_the_human_s()
    {
        //: human 1.5, neonate 2.5, elder 4 times the larger of the jump and MantleMax.
        var m = TestData.Sandbox();
        foreach (var (name, multiplier, expected) in new[] { ("human", 1.5f, 2.85f), ("neonate", 2.5f, 6.0f), ("elder", 4f, 16.0f) })
        {
            var n = Profile(m, name).Numbers;
            Assert.Equal(expected, n.SafeFall, 3);
            Assert.Equal(multiplier * MathF.Max(Masquerade.JumpHeight(n), n.MantleMax), n.SafeFall, 2);
            Assert.Equal(2.85f, Masquerade.HeldToHuman(n, Profile(m, "human").Numbers).SafeFall, 3);
        }
    }

    [Fact]
    public void Each_charges_over_a_second_its_tap_is_a_human_s_jump_and_its_powered_numbers_are_its_own_in_either_mode()
    {
        // ChargeSeconds 1.0 in each file; the tap and the powered jump and reach worked out, never given.
        var m = TestData.Sandbox();
        var (w, profiles) = World("elder");
        foreach (var name in new[] { "human", "neonate", "elder" })
        {
            var own = profiles.Find(name)!.Numbers;
            var file = Profile(m, name).Numbers;
            Assert.Equal(1.0f, file.ChargeSeconds);
            Assert.Equal(file.JumpImpulseBudget, file.PoweredJumpImpulse);
            Assert.Equal((file.StandingReach, file.VaultMax, file.MantleMax), (file.PoweredStandingReach, file.PoweredVaultMax, file.PoweredMantleMax));
            Assert.Equal(0.5f, own.TapHeight, 3);                                                   // a human's, whatever the body
            Assert.Equal(Masquerade.JumpHeight(file), own.PoweredHeight, 3);                        // its own: a full charge
            var held = profiles.Numbers(profiles.IndexOf(name), MasqueradeMode.Masquerade);
            Assert.Equal(own.PoweredHeight, held.PoweredHeight, 3);                                  // kept in the masquerade
            Assert.Equal(own.PoweredMantleMax, held.PoweredMantleMax);
            Assert.Equal(name != "human", own.CanCharge);
            Assert.Equal(name != "human", held.CanCharge);
        }
        _ = w;
    }

    [Fact]
    public void A_file_giving_a_worked_out_number_or_without_its_charge_is_refused_by_name()
    {
        using var copy = new TestData.Copy();
        copy.Edit("movement/elder.json", s => s.Replace("\"ChargeSeconds\": 1.0", "\"ChargeSeconds\": 1.0, \"PoweredJumpImpulse\": 9"));
        copy.Edit("movement/neonate.json", s => s.Replace(",\n    \"ChargeSeconds\": 1.0", ""));
        var refused = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        void Has(string text) => Assert.Contains(refused.Reasons, r => r.Contains(text, StringComparison.Ordinal));
        Has("enhanced:movement/elder: numbers: 'PoweredJumpImpulse' is worked out from the body's own numbers");
        Has("enhanced:movement/neonate: numbers: 'ChargeSeconds' is missing");
        Assert.Equal(2, refused.Reasons.Count);
    }

    [Fact]
    public void A_missing_an_unknown_a_wrong_and_a_doubled_number_are_each_refused_by_name()
    {
        using var copy = new TestData.Copy();
        copy.Edit("movement/human.json", s => s.Replace("\"StepHeight\": 0.35,", "\"Stepheight\": 0.35,"));
        copy.Edit("movement/elder.json", s => s.Replace("\"AirControl\": 0.5,", "\"AirControl\": \"lots\",").Replace("\"JumpHeight\": 3.0,", "\"JumpHeight\": 3.0, \"JumpImpulseBudget\": 7.7,"));
        copy.Edit("movement/neonate.json", s => s.Replace("\"status\": \"SANDBOX\"", "\"status\": \"APPROVED\"").Replace("\"WalkSpeed\": 1.3,", "\"WalkSpeed\": 0,"));
        var refused = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        void Has(string text) => Assert.Contains(refused.Reasons, r => r.Contains(text, StringComparison.Ordinal));
        Has("enhanced:movement/human: numbers: unknown number 'Stepheight'");
        Has("enhanced:movement/human: numbers: 'StepHeight' is missing");
        Has("enhanced:movement/elder: numbers: 'AirControl' must be a finite number");
        Has("enhanced:movement/elder: numbers: give the jump as 'JumpImpulseBudget' or as 'JumpHeight', not both");
        Has("enhanced:movement/neonate: status is 'APPROVED'");
        Has("enhanced:movement/neonate: numbers: 'WalkSpeed' must be above 0");
        Assert.Equal(6, refused.Reasons.Count);
    }

    [Fact]
    public void Numbers_that_make_no_sense_together_are_refused_by_name()
    {
        using var copy = new TestData.Copy();
        copy.Edit("movement/elder.json", s => s.Replace("\"EyeHeight\": 1.65,", "\"EyeHeight\": 1.95,").Replace("\"VaultMax\": 3.0,", "\"VaultMax\": 5.0,"));
        var refused = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        Assert.Contains(refused.Reasons, r => r.Contains("enhanced:movement/elder: numbers: EyeHeight 1.95 is above Height 1.8", StringComparison.Ordinal));
        Assert.Contains(refused.Reasons, r => r.Contains("the bands must rise", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_the_human_s_profile_the_module_refuses_naming_it()
    {
        using var copy = new TestData.Copy();
        File.Delete(copy.Path_("movement/human.json"));
        var refused = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        Assert.Contains(refused.Reasons, r => r.StartsWith("enhanced:movement/human: missing", StringComparison.Ordinal));
    }

    [Fact]
    public void The_profiles_change_neither_the_configuration_hash_nor_the_shipping_refusal()
    {
        Assert.StartsWith("8c90cf9aee87", TestData.Sandbox().Stamp.ConfigHash);
        var refused = Assert.Throws<ModuleRefusedException>(() => new EnhancedModule().Configure(TestData.Context()));
        Assert.Equal(28, refused.Reasons.Count);
    }

    static (World World, MovementProfiles Profiles) World(string? playerProfile)
    {
        var m = TestData.Sandbox();
        var level = new LevelSpace("flat", CollisionWorld.FromTriangles(
            [new(-50, -50, 0), new(50, -50, 0), new(50, 50, 0), new(-50, 50, 0)], [0, 1, 2, 0, 2, 3]));
        var b = new WorldBuilder(1);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b);
        m.Register(b);
        var w = b.Build();
        var profiles = w.Service<MovementProfiles>();
        profiles.PlayerProfile = playerProfile;
        return (w, profiles);
    }

    [Fact]
    public void The_player_is_given_the_profile_asked_for_held_to_the_human_s_and_the_townsfolk_the_human_s()
    {
        var (w, profiles) = World("elder");
        Assert.Equal("enhanced:movement/human", profiles.Human!.Id.ToString());
        var people = w.Service<PeopleHooks>();
        var player = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        people.Made(player, PersonRole.Player);
        var walker = MovementServices.SpawnWanderer(w, new(5, 5, 0.01f), 0f, Locomotion.Sandbox);
        people.Made(walker, PersonRole.Townsperson);

        var mine = w.Get<MasqueradeState>(player);
        Assert.Equal(profiles.IndexOf("elder"), mine.Profile);
        Assert.Equal(MasqueradeMode.Masquerade, mine.Mode);
        Assert.Equal(profiles.Human.Numbers.SprintSpeed, w.Get<Locomotion>(player).SprintSpeed);   // in public, as a human
        Assert.Equal(profiles.HumanIndex, w.Get<MasqueradeState>(walker).Profile);
        Assert.Equal(profiles.Human.Numbers, w.Get<Locomotion>(walker));

        MovementServices.Step(w, ActionFrame.Idle with { Held = Buttons.Masquerade });
        Assert.Equal(MasqueradeMode.Unbound, w.Get<MasqueradeState>(player).Mode);
        Assert.Equal(profiles.Find("elder")!.Numbers, w.Get<Locomotion>(player));
        Assert.Equal(MasqueradeMode.Masquerade, w.Get<MasqueradeState>(walker).Mode);           // the key is the player's
    }

    [Fact]
    public void A_player_given_no_profile_is_human_and_a_profile_not_there_is_named()
    {
        var (w, profiles) = World(null);
        var player = MovementServices.SpawnControlled(w, Vector3.Zero, 0f, Locomotion.Sandbox);
        w.Service<PeopleHooks>().Made(player, PersonRole.Player);
        Assert.Equal(profiles.HumanIndex, w.Get<MasqueradeState>(player).Profile);

        var (w2, _) = World("ancient");
        var other = MovementServices.SpawnControlled(w2, Vector3.Zero, 0f, Locomotion.Sandbox);
        var e = Assert.Throws<InvalidOperationException>(() => w2.Service<PeopleHooks>().Made(other, PersonRole.Player));
        Assert.Contains("no movement profile 'ancient' (the profiles are human, elder, neonate)", e.Message);
    }

    /// <summary><c>--snagcheck --profile</c> drives the module's profile as play gives the player — the human by default, a named one held to the human's in the masquerade, its own unbound.</summary>
    [Fact]
    public void The_snag_check_drives_the_profile_asked_for_as_play_gives_it()
    {
        var (_, profiles) = World(null);
        var human = SnagCheck.BodyOf(profiles, null, unbound: false)!.Value;
        Assert.Equal(("human", "masquerade"), (human.Profile, human.Mode));
        Assert.Equal(profiles.Human!.Numbers, human.Numbers);
        Assert.Equal((0.3f, 1.8f, 0.35f, 40f), (human.Numbers.Radius, human.Numbers.Height, human.Numbers.StepHeight, human.Numbers.SteepestGroundDegrees));
        var elder = profiles.IndexOf("elder");
        Assert.Equal(profiles.Numbers(elder, MasqueradeMode.Masquerade), SnagCheck.BodyOf(profiles, "elder", unbound: false)!.Value.Numbers);
        var free = SnagCheck.BodyOf(profiles, "enhanced:movement/elder", unbound: true)!.Value;
        Assert.Equal(("elder", "unbound"), (free.Profile, free.Mode));
        Assert.Equal(profiles.Find("elder")!.Numbers, free.Numbers);
        Assert.NotEqual(human.Numbers.SprintSpeed, free.Numbers.SprintSpeed);
        Assert.Null(SnagCheck.BodyOf(profiles, "ancient", unbound: false));
        Assert.Null(SnagCheck.BodyOf(new MovementProfiles(), null, unbound: false));
    }
}
