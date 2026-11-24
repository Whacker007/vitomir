// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Engine/Levels/Cells/TextureCook.cs at b4185ef (the enum only), reworked for the platform.
namespace Vitomir.Platform.Render.Level;

/// <summary>What a level's texture holds, which decides how it is sampled: colour (sRGB), data, a normal map (two channels, Z rebuilt) or a height map.</summary>
internal enum TextureKind
{
    Colour,
    Data,
    Normal,
    Height,
}
