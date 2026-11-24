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
using Vitomir.Platform.Space;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The blade guard by training (guard.json's <c>skill.blade_guard_least</c>) and the brain's pace by
/// the kind of person (<c>brain_pace</c>) read, given to the people a world makes (a townsman, a trained guard, the player) and
/// turned off by their switches (<c>blade-guard-everyone</c>, <c>pace-off</c>). The people need the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class BrainPaceTests
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    [Fact]
    public void The_data_gives_the_gate_at_Melee_1_and_three_kinds_paces_chosen_by_template_species_and_training()
    {
        var g = TestData.Sandbox().Guard;
        var curve = Assert.IsType<GuardSkillCurve>(g.Skill);
        Assert.Equal(1, curve.BladeGuardLeast);
        Assert.False(curve.Trained(0));
        Assert.True(curve.Trained(1));
        var pace = Assert.IsType<BrainPaceData>(g.Pace);
        Assert.Equal(2, pace.RunMost);
        Assert.Equal((2f, 4f), pace.Kinds["townsfolk"]);
        Assert.Equal((1f, 2.2f), pace.Kinds["guards"]);
        Assert.Equal((0.4f, 1.2f), pace.Kinds["fierce"]);
        Assert.Equal("fierce", pace.KindOf("sandbox.szlachta", "human", trained: true));            // named: a mortal's sheet
        Assert.Equal("fierce", pace.KindOf("sandbox.newly_embraced", "vampire", trained: false));   // Kindred, trained or not
        Assert.Equal("guards", pace.KindOf("sandbox.guard_trained", "human", trained: true));
        Assert.Equal("townsfolk", pace.KindOf(SheetTemplates.TownspersonId, "human", trained: false));
        var p = pace.PaceOf("guards", 2);
        Assert.Equal((1f, 2.2f, 2), (p.PauseLeast, p.PauseMost, p.RunMost));
        // The riposte chance by Melee — 0.05 untrained, 0.30 at 2, 0.70 at 5, straight between, held at the ends.
        Assert.Equal(0.30f, p.RiposteChance, 5);
        foreach (var (melee, chance) in new[] { (0, 0.05f), (1, 0.175f), (2, 0.30f), (3, 0.43333f), (5, 0.70f), (9, 0.70f) })
            Assert.Equal(chance, pace.RiposteAt(melee), 4);
    }

    [Fact]
    public void A_bad_pace_is_refused_by_name()
    {
        using var copy = new TestData.Copy();
        copy.Edit("combat/guard.json", s => s
            .Replace("\"townsfolk\": { \"pause_seconds\": [2.0, 4.0] }", "\"townsfolk\": { \"pause_seconds\": [4.0, 2.0] }")
            .Replace("\"blade_guard_least\": 1,", "\"blade_guard_least\": 1.5,"));
        var refused = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        Assert.Contains(refused.Reasons, r => r.Contains("brain_pace.kinds.townsfolk.pause_seconds", StringComparison.Ordinal));
        Assert.Contains(refused.Reasons, r => r.Contains("skill.blade_guard_least 1.5", StringComparison.Ordinal));
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
    public void The_townsman_is_untrained_and_takes_the_townsfolk_s_pace_the_guard_is_trained_and_takes_the_guards_the_player_the_fierce()
    {
        var p = Make();
        var sheets = p.W.Service<ActorSheets>();
        Assert.Equal("sandbox.townsperson_reference", sheets.Template(p.Townsman));   // the combat rules' townsperson dresses Civ12male
        Assert.Equal("sandbox.guard_trained", sheets.Template(p.Guard));
        Assert.True(p.W.Get<GuardSkill>(p.Townsman).Untrained);
        Assert.False(p.W.Get<GuardSkill>(p.Guard).Untrained);
        Assert.True(GuardSkills.Of(p.W, p.Townsman).Untrained);   // the platform's lookup carries it
        DuelPace Pace(EntityId e) => p.W.Get<DuelPace>(e);
        Assert.Equal((2f, 4f, 2), (Pace(p.Townsman).PauseLeast, Pace(p.Townsman).PauseMost, Pace(p.Townsman).RunMost));
        Assert.Equal((1f, 2.2f, 2), (Pace(p.Guard).PauseLeast, Pace(p.Guard).PauseMost, Pace(p.Guard).RunMost));
        Assert.Equal((0.4f, 1.2f), (Pace(p.Player).PauseLeast, Pace(p.Player).PauseMost));      // a vampire: Kindred
        Assert.Equal(0.05f, Pace(p.Townsman).RiposteChance, 5);                                   // Melee 0
        Assert.Equal(0.30f, Pace(p.Guard).RiposteChance, 5);                                      // the trained guard's Melee 2
    }

    [Fact]
    public void Each_switch_turns_its_own_off_blade_guard_everyone_the_gate_and_pace_off_the_pace()
    {
        var gateOff = Make(EnhancedModule.BladeGuardEveryoneSwitch);
        Assert.False(gateOff.W.Get<GuardSkill>(gateOff.Townsman).Untrained);
        Assert.True(gateOff.W.Has<DuelPace>(gateOff.Townsman));
        var paceOff = Make(EnhancedModule.PaceOffSwitch);
        Assert.False(paceOff.W.TryStore<DuelPace>(out _));
        Assert.True(paceOff.W.Get<GuardSkill>(paceOff.Townsman).Untrained);
    }
}
