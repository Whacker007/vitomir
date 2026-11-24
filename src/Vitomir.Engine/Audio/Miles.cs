// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The two laws the Miles Sound System applies between the game's numbers and the speakers, read out
// of the player's own mss32.dll and Miles\MSSFAST.M3D.
using System.Numerics;

namespace Vitomir.Engine.Audio;

/// <summary>
/// What Miles does with a volume and a pan, and what its "Miles Fast 2D Positional Audio" provider
/// — the one `masquerade.ini` names, and the default — does with a position.
///
/// <para><b>The 2D law</b> (mss32.dll 0x21114f50). A sample's volume and pan are 0..127. The
/// volume is scaled by the driver's master volume (127, untouched by the game), and each side gets
/// <c>min(table[p] × volume ÷ 8, 2047)</c> in units of 1/2048, where <c>p</c> is the pan for the right
/// and <c>127 − pan</c> for the left, and <c>table[p]</c> is <c>2p</c> below 63 and 128 from 63
/// up. So a sample in the middle plays at full volume on <b>both</b> sides — there is no pan law
/// dip — and panning only ever takes volume away from the far side.</para>
///
/// <para><b>The Fast 2D provider</b> (MSSFAST.M3D 0x22601260) turns a 3D sample into a 2D one:</para>
/// <list type="bullet">
/// <item>beyond its far distance it is silenced and <b>paused</b>, and picks up where it left off
/// when the listener comes back into range;</item>
/// <item>the volume falls by 6 dB for every doubling of distance past the near distance:
/// <c>round(127 ÷ 10^((6·log2(d ÷ near) − 20·log10(volume ÷ 127)) ÷ 20))</c>, which is very nearly
/// <c>volume × near ÷ d</c>;</item>
/// <item>a sound <b>behind</b> the listener — the listener further along its own facing than the
/// source is — loses 29% more (<c>× 71 ÷ 100</c>, integer);</item>
/// <item>the pan follows the direction: <c>curve[round((1 + direction·right) × 1024)]</c>, with the
/// curve <c>127 − ceil(acos(i ÷ 1024 − 1) × 127 ÷ π)</c> — hard right when the source is straight to
/// the right, centre (63) when it is ahead or behind.</item>
/// </list>
/// <para>The game never gives a sample a velocity, so there is no Doppler.</para>
/// </summary>
public static class Miles
{
    /// <summary>The output (050 Hz, 16-bit, stereo).</summary>
    public const int OutputRate = 22050;

    /// <summary>Unity gain in the 2D law's units.</summary>
    public const int Unity = 2048;

    static readonly int[] PanTable = [.. Enumerable.Range(0, 128).Select(p => p < 63 ? 2 * p : 128)];

    /// <summary>The left and right gains, in 1/2048, for a Miles volume and pan (both 0..127).</summary>
    public static (int Left, int Right) Gains(int volume, int pan, int master = 127)
    {
        int v = Math.Clamp(master * Math.Clamp(volume, 0, 127) / 127, 0, 127);
        pan = Math.Clamp(pan, 0, 127);
        return (Math.Min(PanTable[127 - pan] * v / 8, 2047), Math.Min(PanTable[pan] * v / 8, 2047));
    }

    static readonly byte[] PanCurve = BuildCurve();

    static byte[] BuildCurve()
    {
        var c = new byte[2049];
        for (int i = 0; i <= 2048; i++)
        {
            double a = Math.Acos(Math.Clamp(i / 1024.0 - 1.0, -1.0, 1.0));
            c[i] = (byte)Math.Clamp(127 - (int)Math.Ceiling(a * 127.0 / Math.PI), 0, 127);
        }
        return c;
    }

    /// <summary>What the provider hands the 2D sample: a volume, a pan, and whether it is paused.</summary>
    public readonly record struct Placed(int Volume, int Pan, bool Paused);

    /// <summary>
    /// The Fast 2D provider's update for one sample (0x22601260). <paramref name="volume"/> is the
    /// 3D sample's own 0..127 volume; <paramref name="face"/> and <paramref name="right"/> are the
    /// listener's forward and right.
    /// </summary>
    public static Placed Fast2D(int volume, Vector3 source, Vector3 listener, Vector3 face, Vector3 right,
                                float near, float far)
    {
        var offset = source - listener;
        float d = offset.Length();
        if (d > far) return new Placed(0, 64, true);

        double level = volume < 1e-4 ? 0 : volume / 127.0;
        double volumeDb = level <= 0 ? double.PositiveInfinity : -20.0 * Math.Log10(level);
        float n = Math.Max(near, 1e-4f);
        double distanceDb = d < n ? 0 : 6.0 * Math.Log2(d / n);
        double exact = 127.0 / Math.Pow(10.0, 0.05 * (distanceDb + volumeDb));
        int outVolume = (int)Math.Clamp(Math.Round(exact, MidpointRounding.ToEven), 0, 127);
        if (Vector3.Dot(face, listener) > Vector3.Dot(face, source)) outVolume = outVolume * 71 / 100;

        int pan;
        if (d <= 1e-4f) pan = 64;
        else
        {
            float along = Vector3.Dot(offset / d, right);
            int i = (int)Math.Round((1.0 + along) * 1024.0, MidpointRounding.ToEven);
            pan = PanCurve[Math.Clamp(i, 0, 2048)];
        }
        return new Placed(outVolume, pan, false);
    }

    /// <summary>
    /// The number the game hands Miles for a 0..1 volume:
    /// <c>(int)(clamp(v × effects, 0, 1) × 127)</c>, or without the effects volume for a channel
    /// flagged absolute (0x800, music and the ambient).
    /// </summary>
    public static int Volume(float volume, float effects, bool absolute)
    {
        double v = absolute ? volume : (double)effects * volume;
        return (int)(Math.Clamp(v, 0.0, 1.0) * 127.0);
    }

    /// <summary>A 0..1 pan the same way: <c>(int)(clamp(p, 0, 1) × 127)</c>.</summary>
    public static int Pan(float pan) => (int)(Math.Clamp((double)pan, 0.0, 1.0) * 127.0);
}
