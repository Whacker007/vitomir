// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Three of the scene start's own steps, read branch by branch in the
// A comparison with 1.1: the game's pause cleared, the far clip set, the floating
// texts taken down.
using Vitomir.Engine.Ui;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// **What a scene's start does before anything else of it** (every journey, jump, teleport,
/// new chronicle and load ends in it), the parts earlier batches had not built:
/// <list type="bullet">
/// <item>**the frame clock starts again from now** (the clock's last frame
/// set to <c>timeGetTime</c>), so the time the place took to load is no frame's step — the next frame
/// is measured from the scene's start, not from the frame before the journey. With a frame allowed a
/// second (<see cref="FrameStep"/>), without it every journey would begin with a second of play at
/// once;</item>
/// <item>**the game's pause is cleared**  — the Pause key's, and
/// anything else's that set it — so a place is always entered running;</item>
/// <item>**the render view's far clip** is set from the scene in force, the chapter scene's or else the
/// world scene's: its <c>RENDERINFO_ZCLIP</c>, or
/// <b>10,000</b> when that is 0; a place with no
/// scene leaves it as it was. Vitomir set it only when it made a camera, which a journey with a player
/// never does, so the far clip of the first place followed the party everywhere;</item>
/// <item>**every floating text is taken down** (with the end text's pointer
/// dropped first), so a chapter title still fading from the last place goes at once; a place
/// with no scene takes down only the end text — <see cref="EndTextHold.SceneStarted"/>.</item>
/// </list>
/// </summary>
public static class SceneStart
{
    /// <summary>The far clip a scene that leaves <c>RENDERINFO_ZCLIP</c> at 0 gets.</summary>
    public const float DefaultFarClip = 10_000f;

    /// <summary>
    /// The render view's far clip after a scene start with <paramref name="scene"/> in force:
    /// its own when it has one, 10,000 when it is 0, and <paramref name="current"/> when there is no scene.
    /// </summary>
    public static float FarClip(NsdScene? scene, float current) =>
        scene is null ? current : scene.ZClip != 0f ? scene.ZClip : DefaultFarClip;

    /// <summary>
    /// The scene start's first steps for the host: the frame clock's last frame made
    /// <paramref name="now"/>, the pause off and the floating texts down,
    /// before the place's title goes up.
    /// </summary>
    public static void Begin(FrontEnd menus, EndTextHold endText, NsdScene? scene, ref ulong lastFrame, ulong now)
    {
        lastFrame = now;
        menus.Paused = false;
        endText.SceneStarted(scene is not null);
    }
}
