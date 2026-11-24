// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Which of the world draw's clocks run while the game is paused: read in a comparison with 1.1
// (the world draw and what it calls).
namespace Vitomir.Engine.World;

/// <summary>
/// **The world draw keeps two clocks, and only one of them stops for a pause.** The engine's clock
/// object (stepped at the head of every frame) holds the interface's
/// time — its frame, whose pause word nothing ever sets — and
/// the game's — its frame, zero while the game pause is set:
/// the Pause key, a menu, the advancement screen.
///
/// The world draw runs every frame the game is in play, paused or not, and what it steps
/// reads one or the other:
/// <list type="bullet">
/// <item>**the game's**: the light styles, the dynamic
/// lights, their blobs and the decals' fade, the twinkle's pulse, the
/// emitters — so all of these stand still in a pause;</item>
/// <item>**the interface's**: the animated materials (a page flip against
/// the interface's total, and a sliding texture's offset by the
/// interface's frame),
/// the walk-target ring's age (the interface's frame added to it) and the halos' fade in and out
/// — so a paused game's water, fire and sliding textures go on moving, the ring left
/// by the last click still spins and runs out, and a lantern's halo still fades.</item>
/// </list>
/// Vitomir stepped all of them by the game's clock until then, so a pause froze the textures, the
/// ring and the halos too. The host hands the interface's frame in
/// (<see cref="Simulation.InterfaceSeconds"/>, <c>Render.FrameRenderer.InterfaceSeconds</c>); a
/// harness or a still, which has only one clock, leaves it null and gets the game's.
/// </summary>
public static class DrawClocks
{
    /// <summary>
    /// The interface's seconds for this step of <paramref name="sim"/>, taken once (a second step in
    /// the same frame, a harness's slice, gets its own seconds back).
    /// </summary>
    public static float Interface(Simulation sim, float seconds)
    {
        float taken = sim.InterfaceSeconds ?? seconds;
        sim.InterfaceSeconds = null;
        return taken;
    }
}
