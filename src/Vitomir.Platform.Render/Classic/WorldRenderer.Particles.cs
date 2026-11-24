// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
using SDL;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>What <see cref="ParticleRenderer"/> borrows from the world renderer beyond what the sky and the walk ring do.</summary>
public sealed unsafe partial class WorldRenderer
{
    /// <summary>Whether a material's textures are up yet (<see cref="Sync"/> uploads new ones once a frame).</summary>
    internal bool HasMaterial(int index) => index >= 0 && index < materialTextures.Length;

    /// <summary>One of a material's images by number, held to the ones it has.</summary>
    internal SDL_GPUTexture* MaterialImage(int index, int image)
    {
        var images = materialTextures[index];
        return images[Math.Clamp(image, 0, images.Length - 1)];
    }

    /// <summary>The sampler a mode's addressing and mipmapping ask for.</summary>
    internal SDL_GPUSampler* SamplerFor(bool clamp, bool mipmaps) => Sampler(clamp, mipmaps);
}
