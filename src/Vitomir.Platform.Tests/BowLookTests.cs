// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The player's bow drawn: Christof carrying 1.1's <c>longBow</c> (darkAges.not: carry class 7, CARRY BOW; its attack
/// mode 72, <c>bow</c>) plays his own bow clips from <c>Christof.nms</c> — <c>stand_bow</c>, <c>walk_bow</c>, <c>run_bow</c>, the shot
/// <c>bow</c> (<c>chrisAttackBow</c>, its <c>Fire</c> mark the release) and <c>hurt_bow</c> — and holds 1.1's bow model; the shot's
/// draw and release moments are found from its hands. Skips without the 1.1 install.
/// </summary>
public sealed class BowLookTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static OneOneLibrary Lib()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        return Library.Value!;
    }

    [Fact]
    public void Christof_with_1_1_s_longBow_plays_his_own_bow_clips_and_holds_its_model()
    {
        var c = Lib().Character("Christof", "longBow")!;
        Assert.Equal(7, c.Weapon!.Carry);                                                       // CARRY BOW
        Assert.Equal(("stand_bow", "ChrisStandBow"), (c.Role(MotionRole.Idle).Mode, c.Role(MotionRole.Idle).Clip!.Name));
        Assert.Equal(("walk_bow", "ChrisDWalkLongbow"), (c.Role(MotionRole.Walk).Mode, c.Role(MotionRole.Walk).Clip!.Name));
        Assert.Equal(("run_bow", "ChrisRunBows"), (c.Role(MotionRole.Run).Mode, c.Role(MotionRole.Run).Clip!.Name));
        var shot = c.Actions!.Quick[0];
        Assert.Equal(("bow", "chrisAttackBow"), (shot.Mode, ((NadClip)shot.Clip).Name));
        Assert.Equal(19f / 30f, shot.StrikeSeconds, 3);                                         // its Fire mark, frame 19
        Assert.Equal("hurt_bow", c.Actions.Hurt!.Mode);
        Assert.Equal("bow_0", Assert.Single(c.Held).Model.Name);                                 // WEAPON_MODEL bow, skeleton 0
    }

    [Fact]
    public void The_shot_s_draw_and_release_are_found_from_its_hands()
    {
        var assets = new CharacterAssets();
        int kind = OneOneLibrary.Register(assets, Lib().Character("Christof", "longBow")!);
        var shot = BowShot.Of(assets, kind)!;
        output.WriteLine($"draw start {shot.DrawStart * 30:0} f, full draw {shot.FullDraw * 30:0} f, release {shot.Release * 30:0.#} f of {shot.Duration * 30:0}; separation {shot.Separation[0]:0.000} to {shot.Separation.Max():0.000} m");
        Assert.InRange(shot.DrawStart * 30f, 1.5f, 4.5f);       // the bow raised, the draw hand leaving it from frame 2 or 3
        Assert.InRange(shot.FullDraw * 30f, 18.5f, 19.5f);      // the hands furthest apart at the mark
        Assert.Equal(19f / 30f, shot.Release, 3);
        Assert.Equal(shot.DrawStart, shot.TimeAt(0f), 3);
        Assert.Equal(shot.FullDraw, shot.TimeAt(1f), 3);
        float half = shot.TimeAt(0.5f);
        Assert.InRange(half, shot.DrawStart, shot.FullDraw);
        for (float s = 0.1f; s <= 1f; s += 0.1f) Assert.True(shot.TimeAt(s) >= shot.TimeAt(s - 0.1f));   // the draw only goes back
    }
}
