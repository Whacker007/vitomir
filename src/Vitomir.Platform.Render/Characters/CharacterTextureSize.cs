// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Render.Characters;

/// <summary>
/// How large a character's textures are on the GPU. The models repository's bodies carry one 4096² set each
/// (base colour, normal, occlusion-roughness-metal: about 256 MB as RGBA8 with mips, the body alone), so the player's
/// <c>characterTextureLimit</c> (1024, 2048 or 4096; 2048 by default, which looks the same at the distances people are seen
/// at for half the memory) caps a texture's side when it is uploaded: a larger one is halved by a 2 × 2 box filter until it
/// fits (a base colour averaged in linear light, a data map as it is stored).
/// </summary>
public static class CharacterTextureSize
{
    /// <summary>The limit by default (the stills at 2048 look as at 4096 at normal distances, for half the memory and upload).</summary>
    public const int DefaultLimit = 2048;

    /// <summary>The limits a player can choose.</summary>
    public static readonly int[] Limits = [1024, 2048, 4096];

    /// <summary>The limit for an asked number: 1024, 2048 or 4096, the largest not above it (at least 1024); 0 or less for the default.</summary>
    public static int Limit(int asked) => asked <= 0 ? DefaultLimit : asked >= 4096 ? 4096 : asked >= 2048 ? 2048 : 1024;

    /// <summary>The GPU memory of a <paramref name="width"/> × <paramref name="height"/> RGBA8 texture with its whole mip chain, bytes.</summary>
    public static long GpuBytes(int width, int height)
    {
        long sum = 0;
        while (true)
        {
            sum += (long)width * height * 4;
            if (width == 1 && height == 1) return sum;
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }
    }

    /// <summary>
    /// <paramref name="image"/> no larger than <paramref name="limit"/> a side: itself when it fits, else halved (each texel the
    /// mean of the 2 × 2 it covers) until it does. <paramref name="srgb"/>: the colour is averaged in linear light and
    /// encoded again (a base colour), else as stored (a normal or occlusion-roughness-metal map); alpha linearly either way.
    /// </summary>
    internal static TextureImage Fit(TextureImage image, int limit, bool srgb)
    {
        limit = Limit(limit);
        while (image.Width > limit || image.Height > limit) image = Halve(image, srgb);
        return image;
    }

    /// <summary>One halving by a 2 × 2 box filter (an odd last row or column is folded into its neighbour's texel); the platform's <see cref="TextureHalving"/>, moved there.</summary>
    internal static TextureImage Halve(TextureImage image, bool srgb) => TextureHalving.Halve(image, srgb);

    /// <summary>
    /// A cut-out texture's whole mip chain kept from thinning (after Castaño, "Computing Alpha Mipmaps", 2010): each level the box
    /// filter's halving of the one before (as <see cref="Halve"/>); where fewer of its texels pass <paramref name="cutoff"/> than
    /// of the full image's, its alpha is raised until the share is as near the image's as it can be. A box filter averages a
    /// thin strand's alpha with the gaps beside it, so a sparse cut-out vanishes as it is seen smaller (the eyelashes at 0.5:
    /// 19 % of the map passes, 3 % of its 32 × 8 level). Alpha is never lowered: where the box filter thickens a map (the hair's
    /// atlas, 42 % passing at 0.35, its dense locks to 94 % far off) the hair keeps reading as a mass at a distance, as a
    /// cut-out can show it without blending; one scale for the whole map would thin its sparser columns (the beard's tufts)
    /// to keep the whole's share. Level 0 is the image's own pixels.
    /// </summary>
    internal static byte[][] CoverageMips(TextureImage image, float cutoff, bool srgb)
    {
        int count = Modern.ModernDevice.MipLevels(image.Width, image.Height);
        var levels = new byte[count][];
        levels[0] = image.Rgba;
        float target = Coverage(image.Rgba, cutoff, 1f);
        var plain = image;
        for (int l = 1; l < count; l++)
        {
            plain = Halve(plain, srgb);
            float lo = 0f, hi = 16f;
            for (int i = 0; i < 14; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Coverage(plain.Rgba, cutoff, mid) < target) lo = mid; else hi = mid;
            }
            // The scale whose coverage is nearest the image's (the shares come in steps on a small level); never below 1.
            float scale = MathF.Abs(Coverage(plain.Rgba, cutoff, lo) - target) < MathF.Abs(Coverage(plain.Rgba, cutoff, hi) - target) && lo > 0f ? lo : hi;
            scale = MathF.Max(1f, scale);
            var scaled = (byte[])plain.Rgba.Clone();
            for (int a = 3; a < scaled.Length; a += 4) scaled[a] = Scaled(scaled[a], scale);
            levels[l] = scaled;
        }
        return levels;
    }

    /// <summary>
    /// The share of texels whose alpha, scaled by <paramref name="scale"/> as <see cref="CoverageMips"/> stores it, passes
    /// <paramref name="cutoff"/> (the shaders keep a texel whose alpha is at least the cut-off), 0 to 1.
    /// </summary>
    internal static float Coverage(byte[] rgba, float cutoff, float scale)
    {
        float edge = cutoff * 255f;
        int pass = 0, n = rgba.Length / 4;
        for (int a = 3; a < rgba.Length; a += 4) if (Scaled(rgba[a], scale) >= edge) pass++;
        return n == 0 ? 0f : (float)pass / n;
    }

    static byte Scaled(byte alpha, float scale) => (byte)Math.Min(255, (int)(alpha * scale + 0.5f));
}
