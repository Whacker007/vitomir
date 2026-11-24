// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernWater.cs at b4185ef, reworked for the platform.
using System.Diagnostics;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// **Ours: open water**. A material whose name begins with
/// <see cref="Prefix"/> — the city generator's river — is drawn by the surface shader's
/// water path (<c>Shaders/ModernWater.hlsli</c>): four small wave trains moving on its normal (fading with
/// distance so it does not shimmer), the sky reflected by the Fresnel term and the sun's glint from the
/// material's low roughness, through the same lighting as every surface. The flag and the waves' clock
/// ride in the material's constants (<c>Emissive.w</c>, unused before): 0 for any other surface, else 1
/// plus the seconds since the renderer started, wrapping every 600 s.
/// </summary>
internal static class ModernWater
{
    /// <summary>The start of a water material's name.</summary>
    public const string Prefix = "water_";
    static readonly Stopwatch Clock = Stopwatch.StartNew();

    /// <summary>Whether a material is drawn as open water.</summary>
    public static bool IsWater(GltfMaterial m) => m.Name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The material constants' <c>Emissive.w</c> for <paramref name="m"/>: 0, or 1 + the waves' time.</summary>
    public static float Flag(GltfMaterial m) => IsWater(m) ? 1f + Seconds : 0f;

    /// <summary>The waves' clock, wrapping every 600 s (screen-space reflections read the same, ours).</summary>
    public static float Seconds => (float)(Clock.Elapsed.TotalSeconds % 600.0);
}
