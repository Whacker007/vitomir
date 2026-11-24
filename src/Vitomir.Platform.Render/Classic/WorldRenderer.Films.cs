// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
// The card's half of a film on a material.
using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

public sealed unsafe partial class WorldRenderer
{
    /// <summary>
    /// The pictures a material film has painted since the last frame, sent to the card again: 1.1's
    /// <c>bHWUpdate</c> (the card's copy made when there is none, else
    /// reloaded from the picture). A film started without it paints the picture and the card never
    /// hears of it (the engine's <c>MaterialFilms</c>; the snapshot's <see cref="ClassicScene.Repainted"/>). Ours: the smaller mip levels are
    /// made again from the new picture; whether rlDirect's reload remakes them is not read.
    /// Called from <see cref="Sync"/>, outside the frame's command buffer, after any new material's
    /// textures are made.
    /// </summary>
    void UploadFilms(ClassicScene scene)
    {
        foreach (var (m, image, picture) in scene.Repainted)
        {
            if (m >= materialTextures.Length || image >= materialTextures[m].Length) continue;
            gpu.UpdateTexture(materialTextures[m][image], picture.Width, picture.Height, picture.Rgba, mipmaps: true);
        }
    }
}
