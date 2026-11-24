// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// How long a frame's step may be: the engine clock's own bounds, read in a comparison with 1.1
// (the frame's first call) and matched.
//
namespace Vitomir.Engine.World;

/// <summary>
/// **A frame is taken as between one millisecond and one second of game time**. The first
/// thing every frame does is step the engine's clock object:
/// it reads <c>timeGetTime</c> until at least a millisecond has passed since the
/// last frame (a <c>Sleep(0)</c> loop), then for each of its two clocks — the interface's
/// and the game's — scales the milliseconds by the clock's rate (1) and truncates them toward zero,
/// and clamps the result, unsigned, to <b>1 at least</b> and <b>1000 at most</b>.
/// That step, in seconds (× 0.001), is what every
/// system of the frame is stepped by — the minds, the walkers, the projectiles, the particles, the
/// timers. So a machine drawing four frames a second plays at the wall clock's pace in long steps, and
/// the one frame after a long stall (a window dragged) is a second of play at most. A place's load is no
/// such stall: the scene's start sets the clock's last frame to its own moment
/// (<see cref="SceneStart"/>), so the next frame is measured from there.
///
/// **Vitomir capped it at a tenth of a second** from its first build until then (ours, unlabelled), so
/// below ten frames a second its game ran slower than the clock, and the step after a stall was a tenth
/// of 1.1's. Vitomir keeps 1.1's bound. A step under a millisecond — which a vsynced Vitomir never
/// takes — is raised to one here rather than waited out as 1.1 waits it (ours: the same step); and the
/// step keeps its fraction of a millisecond, where 1.1's clock counts whole ones (ours: no difference
/// that adds up, since each frame is measured from the last).
/// </summary>
public static class FrameStep
{
    /// <summary>The shortest step a frame is taken as, in milliseconds.</summary>
    public const int ShortestMs = 1;

    /// <summary>The longest step a frame is taken as, in milliseconds.</summary>
    public const int LongestMs = 1000;

    /// <summary>
    /// The step a frame is taken as, in seconds, for <paramref name="elapsedNs"/> nanoseconds of wall
    /// clock since the last one: clamped to <see cref="ShortestMs"/>..<see cref="LongestMs"/>.
    /// </summary>
    public static float Seconds(ulong elapsedNs) =>
        (float)Math.Clamp(elapsedNs / 1e9, ShortestMs / 1000.0, LongestMs / 1000.0);

    /// <summary>
    /// The game's clock for a frame of <paramref name="seconds"/> at the console's <c>timescale</c>
    /// <paramref name="rate"/> (never below 0): the clock's step scales the
    /// frame's milliseconds by the rate **before** it clamps them, so the game's step is bounded too —
    /// and a rate of 0 does not stop the game, it steps a millisecond a frame (the clamp's floor).
    /// The Pause key and the menus stop it; the caller answers 0 for those.
    /// </summary>
    public static float Scaled(float seconds, float rate) =>
        Math.Clamp(seconds * Math.Max(rate, 0f), ShortestMs / 1000f, LongestMs / 1000f);
}
