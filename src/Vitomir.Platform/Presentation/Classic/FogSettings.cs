// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Platform.Presentation.Classic;

/// <summary>
/// A scene's fog (RENDERINFO_FOG*), as the 1.1 renderer sets up Direct3D table fog: type 1 is
/// linear from Start to End in world units of view depth, 2 and 3 are exponential and
/// exponential-squared with density = the first distance (no shipped scene uses them). The colour
/// blends in after texturing and lighting; the sky is drawn with fog off.
/// </summary>
public readonly record struct FogSettings(uint Type, Vector3 Color, float Start, float End, float Density)
{
    public static FogSettings None => default;

    public static FogSettings FromScene(NsdScene? scene) => scene is null || scene.FogType is 0 or > 3
        ? None
        : new FogSettings(scene.FogType, scene.FogColor / 255f, scene.FogDist1, scene.FogDist2, scene.FogDist1);
}
