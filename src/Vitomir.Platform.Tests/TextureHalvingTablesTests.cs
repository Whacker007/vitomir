// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Reflection;
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Tests;

/// <summary>Lead: the halving's sRGB tables, now made with <c>DetMath.PowD</c> (the platform's rule), are the C runtime's to the byte.</summary>
public sealed class TextureHalvingTablesTests
{
    [Fact]
    public void The_sRGB_tables_are_the_C_runtimes_to_the_byte()
    {
        var t = typeof(TextureHalving);
        var toLinear = (float[])t.GetField("ToLinear", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var toSrgb = (byte[])t.GetField("ToSrgb", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        for (int i = 0; i < 256; i++)
        {
            double c = i / 255.0;
            Assert.Equal((float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4)), toLinear[i]);
        }
        for (int i = 0; i < toSrgb.Length; i++)
        {
            double l = i / (double)(toSrgb.Length - 1);
            double s = l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055;
            Assert.Equal((byte)Math.Clamp((int)Math.Round(s * 255.0), 0, 255), toSrgb[i]);
        }
    }
}
