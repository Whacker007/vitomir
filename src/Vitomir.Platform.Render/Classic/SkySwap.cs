// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
// A chapter swap's sky drawn at once (`Engine/World/ChapterSwap.cs`).
using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// **The sky a place has now, not the one it had when its renderer was made.** 1.1's chapter swap lets
/// the old chapter's sky go as it tears the scene down (the model's mixer and its animation) and reads the new chapter's with the rest of the scene;
/// the renderer draws the world's current scene's sky every frame (the world's current scene's,
/// else the level scene's). So a party standing in a place whose chapter changes
/// sees the new sky on the next frame. Earlier, Vitomir's frame made its sky renderer once, with the
/// place, and a new sky showed only from the next journey (the fog changed at once).
/// </summary>
public static class SkySwap
{
    /// <summary>
    /// The sky renderer for the frame's sky as it is now (<paramref name="sky"/>, the snapshot's; the producer sends a new one
    /// when the place's sky changes): <paramref name="had"/> while it draws that sky, otherwise a new one (or none, for a place
    /// with no sky now) — the old let go.
    /// </summary>
    public static SkyRenderer? Take(SkyRenderer? had, ClassicSky? sky, Gpu gpu, WorldRenderer world)
    {
        if (ReferenceEquals(had?.Source, sky)) return had;
        had?.Dispose();
        return sky is not null ? new SkyRenderer(gpu, world, sky) : null;
    }
}
