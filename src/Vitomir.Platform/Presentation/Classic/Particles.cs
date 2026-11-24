// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a place's particles hand the renderer each frame (earlier, in Engine/Particles; data of the Classic snapshot).
using System.Numerics;

namespace Vitomir.Platform.Presentation.Classic;

/// <summary>Which of the three lists a particle batch indexes.</summary>
public enum ParticleRun
{
    /// <summary>The system's sprites: camera-facing squares (the renderer's sprite draw).</summary>
    Sprites,
    /// <summary>
    /// Its quads: quads laid out in the world — a trail's ribbon,
    /// a shock ring or a pad.
    /// </summary>
    Quads,
    /// <summary>Its streaks: rain streaks, widened along the camera's right.</summary>
    Streaks,
}

/// <summary>A particle as it is drawn: a square facing the camera (24 bytes each in the original's sprite array).</summary>
/// <param name="Position">Its centre.</param>
/// <param name="Color">ARGB, <c>0xAARRGGBB</c>.</param>
/// <param name="Size">Half its width: the renderer puts the corners at ± this along the camera's right and up.</param>
public readonly record struct ParticleSprite(Vector3 Position, uint Color, float Size);

/// <summary>
/// A quad already laid out in the world: corners <see cref="P0"/>–<see cref="P3"/> with texture
/// coordinates <see cref="T0"/>–<see cref="T3"/> and ARGB colours <see cref="C0"/>–<see cref="C3"/>,
/// drawn as (0,1,2) and (0,2,3). A trail's (the short constructor) runs across from start (u 0) to
/// end (u 1) at the older segment's life (<see cref="V01"/>) and the newer's (<see cref="V23"/>).
/// </summary>
public readonly record struct ParticleQuad(Vector3 P0, Vector3 P1, Vector3 P2, Vector3 P3,
                                           Vector2 T0, Vector2 T1, Vector2 T2, Vector2 T3,
                                           uint C0, uint C1, uint C2, uint C3)
{
    /// <summary>A trail's quad: (0, v01), (1, v01), (1, v23), (0, v23), the first two in one colour and the last two in the other.</summary>
    public ParticleQuad(Vector3 P0, Vector3 P1, Vector3 P2, Vector3 P3, float V01, float V23, uint Color01, uint Color23)
        : this(P0, P1, P2, P3, new Vector2(0, V01), new Vector2(1, V01), new Vector2(1, V23), new Vector2(0, V23),
               Color01, Color01, Color23, Color23) { }

    public float V01 => T0.Y;
    public float V23 => T2.Y;
    public uint Color01 => C0;
    public uint Color23 => C2;
}

/// <summary>
/// A rain streak as the rain hands it over: a quad from <see cref="Position"/> to
/// <see cref="Tip"/> — where the drop will be <c>length</c> seconds on — and <see cref="Width"/>
/// out along the camera's right, which only the renderer knows. Its corners, in the renderer's
/// order, are the position (u 0, v 0), the position + width × right (1, 0), the tip +
/// width × right (1, 1) and the tip (0, 1): **one-sided**, not centred on the drop.
/// </summary>
public readonly record struct ParticleStreak(Vector3 Position, Vector3 Tip, uint Color, float Width);
