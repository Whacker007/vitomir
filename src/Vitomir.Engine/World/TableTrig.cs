// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The engine's own sine and cosine, read for the game camera's
// steady distance, which is 354.79 in 1.1 and was 354.87 in Vitomir's library cosine.

namespace Vitomir.Engine.World;

/// <summary>
/// **1.1's sine and cosine**: a quarter-wave table of 4,096 sines, read with a
/// straight-line blend between neighbours, not the library's. The angle, in degrees, is first put in
/// 0..360 (<see cref="WorldCamera.Wrap"/>), its quadrant picked against 90, 180 and 270,
/// and scaled by 4096 / 90 — the whole part, truncated, is the
/// entry and the rest the blend.
///
/// Two things make its answers differ from the true ones. The table holds the sines to **six decimal
/// places** (entry 1 is 0.000383), and the cosine is read from the table backwards **one entry short**
/// — entry 0xFFF − i rather than 0x1000 − i, so every cosine is the true one of an angle 90/4096° larger.
/// At the game camera's starting pitch that is cos 75° = 0.258449 against 0.258819, and the steady
/// distance 300 + 212 × that = <b>354.79</b> — the very number the camera is built at
/// (`0x43b1653f`), and the 354.8 1.1's camera stands at, where Vitomir's library cosine gave 354.87.
///
/// **Ours**: the table is made here from the formula, not copied: round(sin(i·π/8192), 6). 4,025 of the
/// 4,096 entries are 1.1's to the bit; the other 71 are a millionth out, the last digit of a rounding the
/// shipped table made some other way (the number that comes out of any use of it moves by at most that).
/// Only the game camera's distance easing uses it so far; every other sine and cosine in
/// Vitomir is still the library's.
/// </summary>
public static class TableTrig
{
    /// <summary>Entries in the quarter wave.</summary>
    public const int Entries = 0x1000;

    /// <summary>Entries a degree (a float 45.511112).</summary>
    public const float PerDegree = 45.511112f;

    static readonly float[] Table = Build();

    static float[] Build()
    {
        var t = new float[Entries];
        for (int i = 0; i < Entries; i++) t[i] = (float)Math.Round(Math.Sin(i * Math.PI / 8192.0), 6);
        return t;
    }

    /// <summary>An entry of the table as the engine reads it, including below 0 and past the end of the quarter.</summary>
    static float T(int i) => Table[i];

    static float Blend(float a, float b, float f) => (b - a) * f + a;

    /// <summary>The sine and the cosine of <paramref name="degrees"/>, as 1.1 answers them.</summary>
    public static (float Sin, float Cos) SinCos(float degrees)
    {
        float a = WorldCamera.Wrap(degrees);
        int quadrant = a < 90f ? 0 : a < 180f ? 1 : a < 270f ? 2 : 3;
        float x = a * PerDegree;
        int i2 = (int)x, i3 = i2 + 1;                  // truncated toward zero
        float f = x - i2;
        switch (quadrant)
        {
            case 0:
                if (i3 == 0x1000) return (1f, 0f);
                return (Blend(T(i2), T(i3), f), Blend(T(0xfff - i2), T(0xfff - i3), f));
            case 1:
                if (i3 == 0x2000) return (0f, -1f);
                return (Blend(T(0x1fff - i2), T(0x1ffe - i2), f), -Blend(T(i2 - 0x1000), T(i3 - 0x1000), f));
            case 2:
                if (i3 == 0x3000) return (-1f, 0f);
                return (-Blend(T(i2 - 0x2000), T(i3 - 0x2000), f), -Blend(T(0x2fff - i2), T(0x2ffe - i2), f));
            default:
                if (i3 == 0x4000) return (0f, 1f);
                return (-Blend(T(0x3fff - i2), T(0x3ffe - i2), f), Blend(T(i2 - 0x3000), T(i3 - 0x3000), f));
        }
    }

    /// <summary>The cosine alone.</summary>
    public static float Cos(float degrees) => SinCos(degrees).Cos;
}
