// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Render.Modern;

/// <summary>
/// The modern path's arithmetic that needs no GPU (ours): the tone curve and sRGB encoding the shaders
/// use (mirrored here so tests can pin them), the sun's place and colour, the cascade split scheme and
/// fitting, and the reversed-depth projection. The engine's world is Z up, in 72 units a metre.
/// </summary>
public static partial class ModernMath
{
    public const float UnitsPerMetre = 72f;

    // ----- tone mapping -----

    /// <summary>
    /// ACES filmic tone mapping, Stephen Hill's fit of the reference rendering and output transforms
    /// (the "ACES fitted" curve): the colour taken into the ACES working space, the RRT+ODT curve
    /// applied per channel, and back. Black stays black, mid grey (0.18) comes out about 0.11, 1 about
    /// 0.62, and the curve rolls highlights off smoothly, reaching white only at about 25 (some 140 times
    /// mid grey); bright colours desaturate towards white as film does. The shader
    /// (<c>ModernTonemap.hlsl</c>) runs the same numbers.
    /// </summary>
    public static Vector3 AcesFitted(Vector3 c)
    {
        // sRGB → XYZ → D65→D60 → AP1 → RRT_SAT
        var a = new Vector3(
            0.59719f * c.X + 0.35458f * c.Y + 0.04823f * c.Z,
            0.07600f * c.X + 0.90834f * c.Y + 0.01566f * c.Z,
            0.02840f * c.X + 0.13383f * c.Y + 0.83777f * c.Z);
        a = RrtOdt(a);
        // ODT_SAT → XYZ → D60→D65 → sRGB
        var o = new Vector3(
            1.60475f * a.X - 0.53108f * a.Y - 0.07367f * a.Z,
            -0.10208f * a.X + 1.10813f * a.Y - 0.00605f * a.Z,
            -0.00327f * a.X - 0.07276f * a.Y + 1.07602f * a.Z);
        return Vector3.Clamp(o, Vector3.Zero, Vector3.One);
    }

    static Vector3 RrtOdt(Vector3 v)
    {
        var a = v * (v + new Vector3(0.0245786f)) - new Vector3(0.000090537f);
        var b = v * (0.983729f * v + new Vector3(0.4329510f)) + new Vector3(0.238081f);
        return a / b;
    }

    /// <summary>A linear value 0..1 encoded for an sRGB display (the piecewise curve, not a plain 2.2 power).</summary>
    public static float LinearToSrgb(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x <= 0.0031308f ? 12.92f * x : 1.055f * MathF.Pow(x, 1f / 2.4f) - 0.055f;
    }

    /// <summary>An sRGB-encoded value 0..1 back to linear light.</summary>
    public static float SrgbToLinear(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x <= 0.04045f ? x / 12.92f : MathF.Pow((x + 0.055f) / 1.055f, 2.4f);
    }

    // ----- the sun -----

    /// <summary>
    /// The direction towards the sun, engine space: <paramref name="azimuth"/> in compass degrees
    /// (0 north, 90 east, clockwise seen from above) and <paramref name="elevation"/> above the horizon.
    /// <paramref name="northYaw"/> is where north points as an engine yaw (0 along +X, 90 along +Y,
    /// counter-clockwise seen from above), so a compass bearing a is the yaw northYaw − a. 1.1's levels
    /// have no compass; <see cref="OldTownNorthYaw"/> is what the renderer gives 1.1's Old Town, ours, and
    /// a cell level brings its own (<see cref="Vitomir.Formats.L2.L2North"/>).
    /// </summary>
    public static Vector3 SunDirection(float azimuth, float elevation, float northYaw = OldTownNorthYaw)
    {
        float yaw = (northYaw - azimuth) * MathF.PI / 180f, el = elevation * MathF.PI / 180f;
        return Vector3.Normalize(new Vector3(MathF.Cos(yaw) * MathF.Cos(el), MathF.Sin(yaw) * MathF.Cos(el), MathF.Sin(el)));
    }

    /// <summary>
    /// North in 1.1's Old Town, as an engine yaw: +X (ours). Chosen so that the astronomical clock on the
    /// town hall tower (its face, material <c>Prague\clockface1</c>, looks along −X) faces south, as
    /// Prague's does. It is also the north of a cell level that names none
    /// (<see cref="Vitomir.Formats.L2.L2North.DefaultYaw"/>); the new Old Town names its own (68°, by which
    /// way 1.1's four gates face).
    /// </summary>
    public const float OldTownNorthYaw = 0f;

    /// <summary>Prague's latitude, for <see cref="SunAt"/>.</summary>
    public const float PragueLatitude = 50.09f;

    /// <summary>
    /// The sun's azimuth and elevation (degrees) at local solar time <paramref name="hour"/> on day
    /// <paramref name="dayOfYear"/> at <paramref name="latitude"/>: the declination from the day
    /// (23.44° · sin(360/365 · (day − 81))) and the hour angle (15° an hour from noon), the textbook
    /// spherical triangle. Good to a degree or so, which is all a picture needs.
    /// </summary>
    public static (float Azimuth, float Elevation) SunAt(float hour, int dayOfYear = 120, float latitude = PragueLatitude)
    {
        double d = 23.44 * Math.PI / 180 * Math.Sin(2 * Math.PI / 365 * (dayOfYear - 81));
        double h = (hour - 12) * 15 * Math.PI / 180;
        double phi = latitude * Math.PI / 180;
        double sinEl = Math.Sin(phi) * Math.Sin(d) + Math.Cos(phi) * Math.Cos(d) * Math.Cos(h);
        double el = Math.Asin(Math.Clamp(sinEl, -1, 1));
        double az = Math.Atan2(Math.Sin(h), Math.Cos(h) * Math.Sin(phi) - Math.Tan(d) * Math.Cos(phi)) + Math.PI;
        return ((float)(az * 180 / Math.PI) % 360f, (float)(el * 180 / Math.PI));
    }

    /// <summary>
    /// The sunlight's colour after its path through the air, as a transmittance per channel: the air
    /// mass for the elevation (Kasten and Young's formula) times a small extinction per channel, blue
    /// losing most. White-ish at noon, gold at 15°, deep orange at the horizon, nothing below it.
    /// </summary>
    public static Vector3 SunTransmittance(float elevation)
    {
        if (elevation <= -2f) return Vector3.Zero;
        float e = MathF.Max(elevation, 0f);
        float airMass = 1f / (MathF.Sin(e * MathF.PI / 180f) + 0.50572f * MathF.Pow(e + 6.07995f, -1.6364f));
        var beta = new Vector3(0.025f, 0.06f, 0.14f);
        var t = new Vector3(MathF.Exp(-beta.X * airMass), MathF.Exp(-beta.Y * airMass), MathF.Exp(-beta.Z * airMass));
        // Below the horizon's edge the disc sinks: fade the last two degrees out.
        return t * Math.Clamp((elevation + 2f) / 2f, 0f, 1f);
    }

    // ----- projection and cascades -----

    /// <summary>
    /// A right-handed perspective projection with **reversed, infinite depth**: depth 1 at the near
    /// plane falling towards 0 at infinity, so there is no far clip at all and a float depth buffer
    /// keeps its precision far out (the distant city). Row-vector convention, as System.Numerics.
    /// </summary>
    public static Matrix4x4 ReversedInfinitePerspective(float verticalFov, float aspect, float near)
    {
        float ys = 1f / MathF.Tan(verticalFov / 2f), xs = ys / aspect;
        return new Matrix4x4(
            xs, 0, 0, 0,
            0, ys, 0, 0,
            0, 0, 0, -1,
            0, 0, near, 0);
    }

    /// <summary>
    /// Where the cascades split along the view, the "practical" scheme: each split a blend, by
    /// <paramref name="lambda"/>, of the logarithmic split n·(f/n)^(i/N) (even texel density) and the
    /// uniform one n + (f − n)·i/N. Answers N + 1 distances, the first <paramref name="near"/> and the
    /// last <paramref name="far"/>.
    /// </summary>
    public static float[] CascadeSplits(float near, float far, int count, float lambda = 0.75f)
    {
        var s = new float[count + 1];
        for (int i = 0; i <= count; i++)
        {
            float f = (float)i / count;
            float log = near * MathF.Pow(far / near, f), uni = near + (far - near) * f;
            s[i] = lambda * log + (1 - lambda) * uni;
        }
        s[0] = near; s[count] = far;
        return s;
    }

    /// <summary>
    /// A cascade's light view-projection: the bounding sphere of the camera's view between
    /// <paramref name="from"/> and <paramref name="to"/> (so the map's size does not change as the
    /// camera turns), seen along the sun, with its centre snapped to whole texels (so the edges of
    /// shadows do not crawl as the camera moves). The depth range reaches back to take in
    /// <paramref name="sceneMin"/>..<paramref name="sceneMax"/>, so casters behind the camera still cast.
    /// </summary>
    public static Matrix4x4 FitCascade(Vector3 eye, Vector3 forward, Vector3 up, float verticalFov, float aspect,
                                       float from, float to, Vector3 toSun, int resolution, Vector3 sceneMin, Vector3 sceneMax)
    {
        forward = Vector3.Normalize(forward);
        var right = Vector3.Normalize(Vector3.Cross(forward, up));
        var trueUp = Vector3.Cross(right, forward);
        float ty = MathF.Tan(verticalFov / 2f), tx = ty * aspect;
        Span<Vector3> corners = stackalloc Vector3[8];
        int k = 0;
        foreach (float d in (ReadOnlySpan<float>)[from, to])
            for (int sy = -1; sy <= 1; sy += 2)
                for (int sx = -1; sx <= 1; sx += 2)
                    corners[k++] = eye + forward * d + right * (sx * tx * d) + trueUp * (sy * ty * d);
        // The slice's bounding sphere: its centre on the view axis where it is equidistant from the near and far corners.
        float nearHalf2 = (tx * tx + ty * ty) * from * from, farHalf2 = (tx * tx + ty * ty) * to * to;
        float z = Math.Clamp((to * to + farHalf2 - from * from - nearHalf2) / (2 * (to - from)), from, to);
        var centre = eye + forward * z;
        float radius = 0;
        foreach (var c in corners) radius = MathF.Max(radius, Vector3.Distance(c, centre));
        radius = MathF.Ceiling(radius / 16f) * 16f;

        toSun = Vector3.Normalize(toSun);
        var lightUp = MathF.Abs(toSun.Z) > 0.99f ? Vector3.UnitY : Vector3.UnitZ;
        var view = Matrix4x4.CreateLookAt(Vector3.Zero, -toSun, lightUp);
        // Snap the centre, in the light's frame, to the texel grid.
        var lc = Vector3.Transform(centre, view);
        float texel = 2 * radius / resolution;
        lc.X = MathF.Floor(lc.X / texel) * texel;
        lc.Y = MathF.Floor(lc.Y / texel) * texel;
        // Depth: from the scene's far side to its near side along the light, plus the sphere.
        float zMin = float.MaxValue, zMax = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            var p = new Vector3((i & 1) == 0 ? sceneMin.X : sceneMax.X, (i & 2) == 0 ? sceneMin.Y : sceneMax.Y, (i & 4) == 0 ? sceneMin.Z : sceneMax.Z);
            float pz = Vector3.Transform(p, view).Z;
            zMin = MathF.Min(zMin, pz); zMax = MathF.Max(zMax, pz);
        }
        zMin = MathF.Min(zMin, lc.Z - radius); zMax = MathF.Max(zMax, lc.Z + radius);
        // Right-handed: the view looks down −z, so distances in front are −z.
        var proj = Matrix4x4.CreateOrthographicOffCenter(lc.X - radius, lc.X + radius, lc.Y - radius, lc.Y + radius, -zMax - 1f, -zMin + 1f);
        return view * proj;
    }

    // ----- culling -----

    /// <summary>
    /// The six planes of a view-projection's clip volume (row-vector convention, depth 0..1), each as
    /// (normal, distance) with the inside where n · p + d ≥ 0. For a reversed infinite projection the
    /// far plane is at infinity and never excludes anything.
    /// </summary>
    public static Vector4[] FrustumPlanes(Matrix4x4 m)
    {
        var c0 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c1 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c2 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c3 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        return [c3 + c0, c3 - c0, c3 + c1, c3 - c1, c2, c3 - c2];
    }

    /// <summary>Whether a box can be seen through <paramref name="planes"/>: false only when it lies wholly outside one of them.</summary>
    public static bool BoxVisible(ReadOnlySpan<Vector4> planes, Vector3 min, Vector3 max)
    {
        foreach (var p in planes)
        {
            // The box's corner farthest along the plane's normal.
            var v = new Vector3(p.X >= 0 ? max.X : min.X, p.Y >= 0 ? max.Y : min.Y, p.Z >= 0 ? max.Z : min.Z);
            if (p.X * v.X + p.Y * v.Y + p.Z * v.Z + p.W < 0) return false;
        }
        return true;
    }
}
