// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
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
using Vitomir.Platform.Space;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The shield read (guard.json's <c>shield_read</c>) read, given to the platform (the geometry's numbers on
/// <see cref="GuardSettings"/>, each fighter's chance on its <see cref="GuardSkill.ShieldRead"/> by its Melee), and turned off by the
/// switch <c>shield-read-off</c> (and with the guard by skill, <c>guard-skill-off</c>). The people need the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class ShieldReadTests
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    [Fact]
    public void The_data_gives_no_read_untrained_a_third_at_Melee_2_and_four_in_five_at_Melee_5_and_the_geometry_to_the_platform()
    {
        var g = TestData.Sandbox().Guard;
        var read = Assert.IsType<ShieldReadData>(g.ShieldRead);
        foreach (var (melee, chance) in new[] { (0, 0f), (1, 0.175f), (2, 0.35f), (3, 0.5f), (4, 0.65f), (5, 0.8f), (9, 0.8f) })
            Assert.Equal(chance, read.ChanceAt(melee), 4);
        Assert.Equal((0.15f, 0.5f, 0.25f), (read.OverMetres, read.OverShare, read.UnderMetres));
        var settings = new GuardSettings { ShieldReadOverMetres = 9f, ShieldReadOverShare = 9f, ShieldReadUnderMetres = 9f };
        g.Apply(settings);
        Assert.Equal((0.15f, 0.5f, 0.25f), (settings.ShieldReadOverMetres, settings.ShieldReadOverShare, settings.ShieldReadUnderMetres));
    }

    [Fact]
    public void A_bad_read_table_is_refused_by_name()
    {
        using var copy = new TestData.Copy();
        copy.Edit("combat/guard.json", s => s
            .Replace("\"melee\": [0, 2, 5],\n    \"chance\": [0.0, 0.35, 0.8]", "\"melee\": [0, 5, 2],\n    \"chance\": [0.0, 1.35, 0.8]"));
        var refused = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        Assert.Contains(refused.Reasons, r => r.Contains("shield_read.chance has 1.35", StringComparison.Ordinal));
    }

    sealed record People(World W, EntityId Townsman, EntityId Guard, EntityId Player);

    static People Make(params string[] switches)
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var lib = Library.Value!;
        var assets = new CharacterAssets();
        int christof = OneOneLibrary.Register(assets, lib.Character("Christof", "BroadSword")!);
        int townsman = OneOneLibrary.Register(assets, lib.Character("Civ12male")!);
        int guard = OneOneLibrary.Register(assets, lib.Character("rudolfGuard")!);
        var m = TestData.Sandbox(null, switches);
        var b = new WorldBuilder(5);
        MovementServices.Register(b, new LevelSpace("floor", CollisionWorld.FromTriangles(
            [new(-20, -20, 0), new(20, -20, 0), new(20, 20, 0), new(-20, 20, 0)], [0, 1, 2, 0, 2, 3])));
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        DeathServices.Register(b);
        ContactServices.Register(b);
        m.Register(b);
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        var people = w.Service<PeopleHooks>();
        EntityId Person(int kind, PersonRole role, string name, float x)
        {
            var e = MovementServices.SpawnBody(w, new(x, 0, 0.01f), 0f, Locomotion.Sandbox);
            people.Made(e, role, name);
            w.Set(e, CharacterAnimation.For(assets, kind));
            w.Set(e, MotionInput.Standing);
            CombatServices.Arm(w, e);
            return e;
        }
        var r = new People(w, Person(townsman, PersonRole.Townsperson, "Civ12male", 0f), Person(guard, PersonRole.Townsperson, "rudolfGuard", 4f),
            Person(christof, PersonRole.Player, "Christof", 8f));
        for (int i = 0; i < 3; i++) MovementServices.Step(w, ActionFrame.Idle);
        return r;
    }

    [Fact]
    public void Each_fighter_reads_by_its_Melee_and_the_switch_turns_every_read_off()
    {
        var p = Make();
        var sheets = p.W.Service<ActorSheets>();
        var read = TestData.Sandbox().Guard.ShieldRead!;
        Assert.Equal(0f, p.W.Get<GuardSkill>(p.Townsman).ShieldRead);                 // the combat rules' townsperson, Melee 0: untrained, no read
        Assert.Equal(0.35f, p.W.Get<GuardSkill>(p.Guard).ShieldRead, 5);              // the combat rules' trained guard, Melee 2
        Assert.Equal(0.35f, GuardSkills.Of(p.W, p.Guard).ShieldRead, 5);              // the platform's lookup carries it
        int melee = sheets.Effective(p.Player, sheets.Layout.Require("melee"));
        Assert.Equal(read.ChanceAt(melee), p.W.Get<GuardSkill>(p.Player).ShieldRead, 5);
        var off = Make(EnhancedModule.ShieldReadOffSwitch);
        Assert.Equal((0f, 0f, 0f), (off.W.Get<GuardSkill>(off.Townsman).ShieldRead, off.W.Get<GuardSkill>(off.Guard).ShieldRead, off.W.Get<GuardSkill>(off.Player).ShieldRead));
        Assert.Equal(p.W.Get<GuardSkill>(p.Guard).Hand, off.W.Get<GuardSkill>(off.Guard).Hand);   // the rest of the guard by skill as it was
        Assert.False(Make(EnhancedModule.GuardSkillOffSwitch).W.TryStore<GuardSkill>(out _));     // with the guard by skill off, no read at all
    }
}
