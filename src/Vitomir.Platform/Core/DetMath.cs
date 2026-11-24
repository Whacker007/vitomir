// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Platform.Core;

/// <summary>
/// The simulation's own transcendental functions. <c>MathF.Sin</c> and
/// its kin call the C runtime, whose answers differ in the last bits between Windows, Linux and their
/// versions; a replay that turns a character by a sine would then drift apart on another machine. These
/// are built only from IEEE operations that are exact by the standard (+, −, ×, ÷, √, rounding to an
/// integer, scaling by a power of two), in a fixed order, in double precision, so they give the same bits
/// everywhere; accuracy is far beyond single precision's. <c>PlatformBoundaryTests</c> keeps the C
/// runtime's functions out of the simulation's code.
/// </summary>
public static class DetMath
{
    public const float Pi = 3.14159265358979f, TwoPi = 6.28318530717959f, HalfPi = 1.57079632679490f;
    public const float DegToRad = Pi / 180f, RadToDeg = 180f / Pi;

    // π/2 in three parts (Cody and Waite), so k·π/2 is subtracted with no rounding for any |k| < 2^20.
    const double PiOver2Hi = 1.5707963267341256, PiOver2Mid = 6.077100506506192e-11, PiOver2Lo = 2.0222662487959506e-21;
    const double PiD = 3.141592653589793, PiOver4D = 0.7853981633974483, Ln2Hi = 0.6931471803691238, Ln2Lo = 1.9082149292705877e-10;

    public static float Sin(float x) { SinCosD(x, out double s, out _); return (float)s; }
    public static float Cos(float x) { SinCosD(x, out _, out double c); return (float)c; }

    public static (float Sin, float Cos) SinCos(float x)
    {
        SinCosD(x, out double s, out double c);
        return ((float)s, (float)c);
    }

    public static float Tan(float x)
    {
        SinCosD(x, out double s, out double c);
        return (float)(s / c);
    }

    static void SinCosD(double x, out double sin, out double cos)
    {
        double k = Math.Round(x * (2.0 / PiD), MidpointRounding.ToEven);
        double r = ((x - k * PiOver2Hi) - k * PiOver2Mid) - k * PiOver2Lo;
        double r2 = r * r;
        // Taylor to r^13 and r^14 on |r| <= π/4: error below 1e-15.
        double s = r * (1 + r2 * (-1.0 / 6 + r2 * (1.0 / 120 + r2 * (-1.0 / 5040 + r2 * (1.0 / 362880
                     + r2 * (-1.0 / 39916800 + r2 * (1.0 / 6227020800)))))));
        double c = 1 + r2 * (-0.5 + r2 * (1.0 / 24 + r2 * (-1.0 / 720 + r2 * (1.0 / 40320
                     + r2 * (-1.0 / 3628800 + r2 * (1.0 / 479001600 + r2 * (-1.0 / 87178291200)))))));
        int q = (int)((long)k & 3);
        (sin, cos) = q switch
        {
            0 => (s, c),
            1 => (c, -s),
            2 => (-s, -c),
            _ => (-c, s),
        };
    }

    /// <summary>The arc tangent of <paramref name="y"/>/<paramref name="x"/>, in (−π, π], with the signs choosing the quadrant.</summary>
    public static float Atan2(float y, float x) => (float)Atan2D(y, x);

    public static float Atan(float x) => (float)AtanD(x);

    public static float Asin(float x) => (float)Atan2D(Clamp1(x), Math.Sqrt(Math.Max(0.0, 1.0 - (double)x * x)));
    public static float Acos(float x) => (float)Atan2D(Math.Sqrt(Math.Max(0.0, 1.0 - (double)x * x)), Clamp1(x));

    static double Clamp1(float x) => x > 1f ? 1.0 : x < -1f ? -1.0 : x;

    static double Atan2D(double y, double x)
    {
        if (x > 0) return AtanD(y / x);
        if (x < 0) return y >= 0 ? AtanD(y / x) + PiD : AtanD(y / x) - PiD;
        if (y > 0) return PiD / 2;
        if (y < 0) return -PiD / 2;
        return 0;
    }

    static double AtanD(double x)
    {
        if (double.IsNaN(x)) return x;
        bool neg = x < 0;
        double a = neg ? -x : x;
        double offset = 0;
        bool invert = a > 1;
        if (invert) a = 1 / a;
        // tan(π/8) = √2 − 1: above it, atan(a) = π/4 + atan((a − 1)/(a + 1)), which lands in [−(√2−1), √2−1].
        if (a > 0.41421356237309503) { offset = PiOver4D; a = (a - 1) / (a + 1); }
        double a2 = a * a, term = a, sum = 0;
        for (int n = 0; n < 24; n++)
        {
            sum += term / (2 * n + 1);
            term *= -a2;
        }
        double r = offset + sum;
        if (invert) r = PiD / 2 - r;
        return neg ? -r : r;
    }

    /// <summary>e to the power <paramref name="x"/>.</summary>
    public static float Exp(float x) => (float)ExpD(x);

    static double ExpD(double x)
    {
        if (x > 709) return double.PositiveInfinity;
        if (x < -745) return 0;
        double k = Math.Round(x / (Ln2Hi + Ln2Lo), MidpointRounding.ToEven);
        double r = (x - k * Ln2Hi) - k * Ln2Lo;            // |r| <= ln2/2
        double sum = 1, term = 1;
        for (int n = 1; n < 20; n++) { term *= r / n; sum += term; }
        return Math.ScaleB(sum, (int)k);
    }

    /// <summary>The natural logarithm; NaN for negative numbers, −∞ for zero.</summary>
    public static float Log(float x) => (float)LogD(x);

    static double LogD(double x)
    {
        if (double.IsNaN(x) || x < 0) return double.NaN;
        if (x == 0) return double.NegativeInfinity;
        if (double.IsPositiveInfinity(x)) return x;
        int e = Math.ILogB(x);
        double m = Math.ScaleB(x, -e);                     // [1, 2)
        if (m > 1.4142135623730951) { m /= 2; e++; }       // [√½, √2)
        // ln m = 2 atanh((m − 1)/(m + 1)), |t| <= 0.1716
        double t = (m - 1) / (m + 1), t2 = t * t, term = t, sum = 0;
        for (int n = 0; n < 20; n++) { sum += term / (2 * n + 1); term *= t2; }
        return 2 * sum + e * (Ln2Hi + Ln2Lo);
    }

    /// <summary><paramref name="x"/> to the power <paramref name="y"/>, for positive <paramref name="x"/>.</summary>
    /// <summary><see cref="Pow"/> in double precision (the sRGB tables of <c>TextureHalving</c>).</summary>
    public static double PowD(double x, double y) => x == 0 ? (y == 0 ? 1 : 0) : ExpD(y * LogD(x));
    public static float Pow(float x, float y) => x == 0 ? (y == 0 ? 1 : 0) : (float)ExpD(y * LogD(x));

    /// <summary>A turn about the vertical (Z) axis by <paramref name="yaw"/>, as a quaternion.</summary>
    public static Quaternion Yaw(float yaw)
    {
        var (s, c) = SinCos(yaw * 0.5f);
        return new Quaternion(0, 0, s, c);
    }

    /// <summary>A turn about <paramref name="axis"/> (a unit vector) by <paramref name="angle"/>.</summary>
    public static Quaternion AxisAngle(Vector3 axis, float angle)
    {
        var (s, c) = SinCos(angle * 0.5f);
        return new Quaternion(axis * s, c);
    }

    /// <summary>The unit vector of a yaw and pitch (yaw counter-clockwise from +X seen from above, pitch up positive).</summary>
    public static Vector3 Direction(float yaw, float pitch)
    {
        var (sy, cy) = SinCos(yaw);
        var (sp, cp) = SinCos(pitch);
        return new Vector3(cy * cp, sy * cp, sp);
    }

    /// <summary>An angle brought into (−π, π].</summary>
    public static float WrapAngle(float a)
    {
        double k = Math.Round(a / (2 * PiD), MidpointRounding.ToEven);
        double r = a - k * (2 * PiD);
        if (r <= -PiD) r += 2 * PiD;
        else if (r > PiD) r -= 2 * PiD;
        return (float)r;
    }

    /// <summary>
    /// Spherical interpolation between two unit quaternions by <paramref name="t"/>, the shorter way round;
    /// nearly parallel ones are blended linearly and normalised.
    /// </summary>
    public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
    {
        float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        if (dot < 0) { b = new Quaternion(-b.X, -b.Y, -b.Z, -b.W); dot = -dot; }
        float wa, wb;
        if (dot > 0.9995f) { wa = 1 - t; wb = t; }
        else
        {
            float theta = Acos(dot);
            float sin = Sin(theta);
            wa = Sin((1 - t) * theta) / sin;
            wb = Sin(t * theta) / sin;
        }
        var q = new Quaternion(a.X * wa + b.X * wb, a.Y * wa + b.Y * wb, a.Z * wa + b.Z * wb, a.W * wa + b.W * wb);
        float len = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        return len > 0 ? new Quaternion(q.X / len, q.Y / len, q.Z / len, q.W / len) : Quaternion.Identity;
    }
}
