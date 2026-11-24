// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The dodge drawn (the survey: 1.1's <c>dodge</c> and <c>dodge_onehand</c> are Christof's alone, <c>ChrisDodgeFists</c>
/// and <c>ChrisDodgeSword</c>, 39 frames, a duck of 0.25 m with a crossover step and a lean to his right, the root back where
/// it began): Christof plays his own by his carry class, the guard (skeleton 0) is lent Christof's, and the slot plays by the
/// dodge's progress — its lowest pose (frame 18) as the burst ends — whatever the way. Skips without the 1.1 install.
/// </summary>
public sealed class DodgeMotionTests
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static OneOneLibrary Lib()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        return Library.Value!;
    }

    [Fact]
    public void Christof_plays_his_own_dodge_by_his_weapon_and_the_guard_is_lent_it()
    {
        var lib = Lib();
        var armed = lib.Character("Christof", "Rapier")!.Role(MotionRole.Dodge);
        Assert.Equal(("dodge_onehand", "ChrisDodgeSword"), (armed.Mode, armed.Clip!.Name));
        var bare = lib.Character("Christof", "Punch")!.Role(MotionRole.Dodge);
        Assert.Equal(("dodge", "ChrisDodgeFists"), (bare.Mode, bare.Clip!.Name));
        Assert.Equal(39f, armed.Clip.Frames);
        var guard = lib.Character("rudolfGuard", "Rapier")!.Role(MotionRole.Dodge);
        Assert.Equal("ChrisDodgeSword", guard.Clip!.Name);                         // not in his set: Christof's, lent on skeleton 0
    }

    [Fact]
    public void The_dodge_s_slot_plays_by_its_progress_any_way_and_gives_way_to_the_locomotion_when_it_ends()
    {
        var lib = Lib();
        var assets = new CharacterAssets();
        int kind = OneOneLibrary.Register(assets, lib.Character("Christof", "Rapier")!);
        var system = new AnimationSystem(assets);
        var a = CharacterAnimation.For(assets, kind);
        for (int i = 0; i < 30; i++) system.Step(ref a, MotionInput.Standing, World.TickSeconds);
        int dodgeClip = assets.Kind(kind).Motion(MotionRole.Dodge).Clip;
        float length = assets.Clip(dodgeClip).Duration;
        foreach (float aside in new[] { 1f, 0f, -1f })
            foreach (float progress in new[] { 0.1f, 18f / 39f, 0.9f })
            {
                system.Step(ref a, new MotionInput { Grounded = true, GroundSpeed = 3.3f, Dodging = true, DodgeProgress = progress, DodgeAside = aside }, World.TickSeconds);
                Assert.Equal(MotionRole.Dodge, a.Role);
                Assert.Equal(dodgeClip, a.Clip);
                Assert.Equal(progress * length, a.Time, 3);
            }
        for (int i = 0; i < 30; i++) system.Step(ref a, MotionInput.Standing, World.TickSeconds);
        Assert.Equal(MotionRole.Idle, a.Role);
    }
}
