// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernAtmosphere.cs at b4185ef, reworked for the platform.
using System.Collections.Concurrent;
using System.Numerics;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// **Ours: a physically based sky and aerial perspective**, after Hillaire's "A Scalable and
/// Production Ready Sky and Atmosphere Rendering Technique" (EGSR 2020): an Earth-like atmosphere of air
/// (Rayleigh scattering, falling off over 8 km), aerosols (Mie scattering and absorption over 1.2 km,
/// strongly forward) and ozone (absorption in a layer round 25 km) over a ground of a given albedo, and four
/// lookup tables worked out from it:
/// <list type="bullet">
/// <item><b>transmittance</b> (256 × 64): the light left after a straight path from a height to the top of the
/// air, by height and angle (Bruneton's mapping);</item>
/// <item><b>multiple scattering</b> (32 × 32): the light scattered two or more times, as Hillaire's isotropic
/// approximation sums every order in closed form, by height and the sun's angle;</item>
/// <item><b>the sky's view</b> (192 × 108): the light reaching the eye from each direction — the sun's single
/// scattering, shadowed by the transmittance to the sun, plus the multiple scattering and the sunlit ground —
/// by the direction's angle round from the sun and its elevation (the rows crowd towards the horizon);</item>
/// <item><b>aerial perspective</b> (32 × 32 directions × 32 distances, out to 64 km): the light added and the
/// light left along a view ray to a surface, by the ray's direction (round from the sun, and elevation) and
/// distance, kept in world directions rather than the screen's so that it changes only with the sun and the
/// eye's height, not as the camera turns.</item>
/// </list>
/// Hillaire works the last two out on the GPU each frame; here they are worked out on the CPU when the sun or
/// the eye's height changes (a few tens of milliseconds) and uploaded, so the GPU only reads them — cheap on
/// any GPU and on the software one in the cloud. All of it is per unit of the sun's illuminance above the air;
/// <see cref="ModernLight"/> scales it by <see cref="ModernSettings.SunIntensity"/>. Distances in kilometres;
/// the planet's centre at the origin, +Z up through the eye.
/// </summary>
internal sealed class ModernAtmosphere
{
    public const float GroundKm = 6360f, TopKm = 6460f;
    public const int TransW = 256, TransH = 64, MsSize = 32, SkyW = 192, SkyH = 108;
    public const int ApAz = 32, ApEl = 32, ApSlices = 32, ApColumns = 8;
    /// <summary>How far the aerial perspective table reaches; beyond, the last slice stands.</summary>
    public const float ApRangeKm = 64f;

    /// <summary>Air's scattering per km at sea level (Hillaire's values, for 680, 550 and 440 nm).</summary>
    public static readonly Vector3 Rayleigh = new(5.802e-3f, 13.558e-3f, 33.1e-3f);
    public const float RayleighHeightKm = 8f;
    /// <summary>Aerosols' scattering and absorption per km at sea level (Hillaire's clear air), times <see cref="MieScale"/>.</summary>
    public const float MieScatter = 3.996e-3f, MieAbsorb = 0.444e-3f, MieHeightKm = 1.2f, MieG = 0.8f;
    /// <summary>Ozone's absorption per km at the middle of its layer (25 km up, falling to none 15 km either side).</summary>
    public static readonly Vector3 Ozone = new(0.650e-3f, 1.881e-3f, 0.085e-3f);

    /// <summary>How many times Hillaire's clear air's aerosols there are (a town's hearths and a summer haze: more).</summary>
    public float MieScale { get; }
    public float GroundAlbedo { get; }

    readonly Vector3[] trans = new Vector3[TransW * TransH];
    readonly Vector3[] ms = new Vector3[MsSize * MsSize];

    public ModernAtmosphere(float mieScale = 1f, float groundAlbedo = 0.3f)
    {
        MieScale = mieScale;
        GroundAlbedo = groundAlbedo;
        for (int y = 0; y < TransH; y++)
            for (int x = 0; x < TransW; x++)
            {
                var (r, mu) = TransRMu((x + 0.5f) / TransW, (y + 0.5f) / TransH);
                trans[y * TransW + x] = TransmittanceExact(r, mu, 40);
            }
        Parallel.For(0, MsSize, y =>
        {
            for (int x = 0; x < MsSize; x++)
                ms[y * MsSize + x] = MultipleScatteringAt(GroundKm + (y + 0.5f) / MsSize * (TopKm - GroundKm), (x + 0.5f) / MsSize * 2f - 1f);
        });
    }

    // ----- the media -----

    /// <summary>Scattering (air per channel, aerosols as one) and extinction per km at an altitude.</summary>
    public (Vector3 Air, float Aerosol, Vector3 Extinction) Medium(float altitudeKm)
    {
        float h = MathF.Max(altitudeKm, 0f);
        float dr = MathF.Exp(-h / RayleighHeightKm), dm = MathF.Exp(-h / MieHeightKm);
        float doz = MathF.Max(0f, 1f - MathF.Abs(h - 25f) / 15f);
        var air = Rayleigh * dr;
        float aerosol = MieScatter * MieScale * dm;
        var ext = air + new Vector3(aerosol + MieAbsorb * MieScale * dm) + Ozone * doz;
        return (air, aerosol, ext);
    }

    public static float RayleighPhase(float mu) => 3f / (16f * MathF.PI) * (1f + mu * mu);

    /// <summary>Cornette and Shanks' phase function (Henyey–Greenstein's with Rayleigh's shape), as Hillaire uses for aerosols.</summary>
    public static float MiePhase(float mu, float g = MieG)
    {
        float g2 = g * g;
        return 3f / (8f * MathF.PI) * (1f - g2) * (1f + mu * mu) / ((2f + g2) * MathF.Pow(MathF.Max(1f + g2 - 2f * g * mu, 1e-4f), 1.5f));
    }

    // ----- geometry -----

    /// <summary>The distance from radius r along cosine mu to the sphere of radius R from inside (the far root), or −1.</summary>
    public static float ToSphere(float r, float mu, float radius)
    {
        float disc = r * r * (mu * mu - 1f) + radius * radius;
        return disc < 0f ? -1f : -r * mu + MathF.Sqrt(disc);
    }

    /// <summary>The distance to the ground from radius r along cosine mu, or −1 when the ray misses it.</summary>
    public static float ToGround(float r, float mu)
    {
        if (mu >= 0f) return -1f;
        float disc = r * r * (mu * mu - 1f) + GroundKm * GroundKm;
        return disc < 0f ? -1f : -r * mu - MathF.Sqrt(disc);
    }

    // ----- transmittance -----

    static readonly float H = MathF.Sqrt(TopKm * TopKm - GroundKm * GroundKm);

    /// <summary>Bruneton's mapping of the transmittance table: a texel's (u, v) to (r, mu).</summary>
    public static (float R, float Mu) TransRMu(float u, float v)
    {
        float rho = H * v;
        float r = MathF.Sqrt(rho * rho + GroundKm * GroundKm);
        float dmin = TopKm - r, dmax = rho + H;
        float d = dmin + u * (dmax - dmin);
        float mu = d == 0f ? 1f : (H * H - rho * rho - d * d) / (2f * r * d);
        return (r, Math.Clamp(mu, -1f, 1f));
    }

    /// <summary>The inverse of <see cref="TransRMu"/>, as the shaders compute it.</summary>
    public static (float U, float V) TransUv(float r, float mu)
    {
        float rho = MathF.Sqrt(MathF.Max(r * r - GroundKm * GroundKm, 0f));
        float d = MathF.Max(ToSphere(r, mu, TopKm), 0f);
        float dmin = TopKm - r, dmax = rho + H;
        return ((d - dmin) / MathF.Max(dmax - dmin, 1e-6f), rho / H);
    }

    /// <summary>Transmittance from radius r along cosine mu to the top of the air, integrated in <paramref name="steps"/> steps.</summary>
    public Vector3 TransmittanceExact(float r, float mu, int steps = 500)
    {
        float d = MathF.Max(ToSphere(r, mu, TopKm), 0f);
        var depth = Vector3.Zero;
        float dt = d / steps;
        for (int i = 0; i < steps; i++)
        {
            float t = (i + 0.5f) * dt;
            float ri = MathF.Sqrt(r * r + t * t + 2f * r * mu * t);
            depth += Medium(ri - GroundKm).Extinction * dt;
        }
        return new Vector3(MathF.Exp(-depth.X), MathF.Exp(-depth.Y), MathF.Exp(-depth.Z));
    }

    /// <summary>Transmittance to the top of the air from the table, bilinearly (as the GPU would read it).</summary>
    public Vector3 Transmittance(float r, float mu)
    {
        var (u, v) = TransUv(Math.Clamp(r, GroundKm, TopKm), mu);
        return Bilinear(trans, TransW, TransH, u, v);
    }

    /// <summary>The sun's light reaching a point at radius r with the sun at cosine muS: nothing where the planet is in the way.</summary>
    Vector3 SunAt(float r, float muS)
    {
        r = MathF.Max(r, GroundKm + 1e-3f);
        float horizon = -MathF.Sqrt(MathF.Max(1f - GroundKm * GroundKm / (r * r), 0f));
        if (muS < horizon) return Vector3.Zero;
        return Transmittance(r, muS);
    }

    static Vector3 Bilinear(Vector3[] lut, int w, int h, float u, float v)
    {
        float x = Math.Clamp(u * w - 0.5f, 0f, w - 1f), y = Math.Clamp(v * h - 0.5f, 0f, h - 1f);
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
        float fx = x - x0, fy = y - y0;
        return Vector3.Lerp(Vector3.Lerp(lut[y0 * w + x0], lut[y0 * w + x1], fx), Vector3.Lerp(lut[y1 * w + x0], lut[y1 * w + x1], fx), fy);
    }

    // ----- multiple scattering -----

    /// <summary>
    /// Hillaire's multiple scattering at radius r with the sun at cosine muS: the second order L₂ seen from the
    /// point over 64 directions (isotropic phase, the sunlit ground of <see cref="GroundAlbedo"/> included) and
    /// the share f of light that scatters again, every order summed as L₂ / (1 − f).
    /// </summary>
    Vector3 MultipleScatteringAt(float r, float muS)
    {
        const int n = 8;
        var sun = new Vector3(MathF.Sqrt(MathF.Max(1f - muS * muS, 0f)), 0f, muS);
        var origin = new Vector3(0f, 0f, r);
        Vector3 l2 = Vector3.Zero, f = Vector3.Zero;
        const float iso = 1f / (4f * MathF.PI);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                float phi = 2f * MathF.PI * (i + 0.5f) / n, cosT = 1f - 2f * (j + 0.5f) / n, sinT = MathF.Sqrt(1f - cosT * cosT);
                var d = new Vector3(sinT * MathF.Cos(phi), sinT * MathF.Sin(phi), cosT);
                float tg = ToGround(r, d.Z);
                float tmax = tg >= 0f ? tg : MathF.Max(ToSphere(r, d.Z, TopKm), 0f);
                const int steps = 20;
                var tr = Vector3.One;
                for (int s = 0; s < steps; s++)
                {
                    float dt = tmax / steps, t = (s + 0.5f) * dt;
                    var p = origin + d * t;
                    float rp = p.Length();
                    var m = Medium(rp - GroundKm);
                    var scat = m.Air + new Vector3(m.Aerosol);
                    var sample = Exp(-m.Extinction * dt);
                    var integ = (Vector3.One - sample) / Vector3.Max(m.Extinction, new Vector3(1e-7f));
                    var sunHere = SunAt(rp, Vector3.Dot(p, sun) / rp);
                    l2 += tr * scat * sunHere * iso * integ;
                    f += tr * scat * integ;
                    tr *= sample;
                }
                if (tg >= 0f)
                {
                    var p = origin + d * tg;
                    var up = Vector3.Normalize(p);
                    l2 += tr * SunAt(GroundKm, Vector3.Dot(up, sun)) * MathF.Max(Vector3.Dot(up, sun), 0f) * GroundAlbedo / MathF.PI;
                }
            }
        l2 *= 1f / (n * n);
        f *= 1f / (n * n);
        var psi = l2 / (Vector3.One - Vector3.Min(f, new Vector3(0.99f)));
        return psi;
    }

    /// <summary>The multiple-scattering table at radius r, the sun at cosine muS.</summary>
    public Vector3 MultipleScattering(float r, float muS) =>
        Bilinear(ms, MsSize, MsSize, (muS + 1f) * 0.5f, (r - GroundKm) / (TopKm - GroundKm));

    static Vector3 Exp(Vector3 v) => new(MathF.Exp(v.X), MathF.Exp(v.Y), MathF.Exp(v.Z));

    // ----- a ray through the air -----

    /// <summary>
    /// The light scattered towards the eye along a ray (per unit of the sun's illuminance above the air) and
    /// the transmittance, from <paramref name="eye"/> (km, planet-centred) along unit <paramref name="d"/> for
    /// <paramref name="tMax"/> km in <paramref name="steps"/> steps that crowd towards the eye; the sunlit
    /// ground's light where the ray ends on it, if <paramref name="ground"/>.
    /// </summary>
    public (Vector3 L, Vector3 T) March(Vector3 eye, Vector3 d, Vector3 toSun, float tMax, int steps, bool ground)
    {
        float mu = Vector3.Dot(d, toSun);
        float pr = RayleighPhase(mu), pm = MiePhase(mu);
        var l = Vector3.Zero;
        var tr = Vector3.One;
        float prev = 0f;
        for (int i = 0; i < steps; i++)
        {
            float f1 = (i + 1f) / steps;
            float t1 = tMax * f1 * f1;
            float dt = t1 - prev, t = prev + dt * 0.3f;
            prev = t1;
            var p = eye + d * t;
            float rp = p.Length();
            var m = Medium(rp - GroundKm);
            float muS = Vector3.Dot(p, toSun) / rp;
            var sunHere = SunAt(rp, muS);
            var psi = MultipleScattering(Math.Clamp(rp, GroundKm, TopKm), muS);
            var s = sunHere * (m.Air * pr + new Vector3(m.Aerosol * pm)) + psi * (m.Air + new Vector3(m.Aerosol));
            var sample = Exp(-m.Extinction * dt);
            l += tr * s * (Vector3.One - sample) / Vector3.Max(m.Extinction, new Vector3(1e-7f));
            tr *= sample;
        }
        if (ground)
        {
            var p = eye + d * tMax;
            var up = Vector3.Normalize(p);
            float muS = Vector3.Dot(up, toSun);
            l += tr * SunAt(GroundKm, muS) * MathF.Max(muS, 0f) * GroundAlbedo / MathF.PI;
        }
        return (l, tr);
    }

    /// <summary>The sky's light in direction <paramref name="d"/> from <paramref name="altitudeKm"/> up, marched directly (32 steps).</summary>
    public Vector3 Sky(Vector3 d, Vector3 toSun, float altitudeKm)
    {
        float r = GroundKm + MathF.Max(altitudeKm, 0.001f);
        var eye = new Vector3(0, 0, r);
        float tg = ToGround(r, d.Z);
        float tmax = tg >= 0f ? tg : MathF.Max(ToSphere(r, d.Z, TopKm), 0f);
        return March(eye, d, toSun, tmax, 32, tg >= 0f).L;
    }

    // ----- the sky's view -----

    /// <summary>The elevation of the horizon (radians, negative) from altitudeKm up: the planet's edge dips below level.</summary>
    public static float HorizonElevation(float altitudeKm)
    {
        float r = GroundKm + MathF.Max(altitudeKm, 0.001f);
        return -MathF.Acos(Math.Clamp(GroundKm / r, -1f, 1f));
    }

    /// <summary>
    /// The sky-view table's mapping (ours, after Hillaire's): u the angle round from the sun's bearing, φ/π,
    /// square-rooted so the columns crowd near the sun; v 0 at the zenith, ½ at the horizon, 1 at the nadir,
    /// square-rooted either side of the horizon so the rows crowd there.
    /// </summary>
    public static (float U, float V) SkyUv(float phi, float elevation, float horizon)
    {
        float u = MathF.Sqrt(Math.Clamp(phi / MathF.PI, 0f, 1f));
        float v = elevation >= horizon
            ? 0.5f - 0.5f * MathF.Sqrt(Math.Clamp((elevation - horizon) / (MathF.PI / 2f - horizon), 0f, 1f))
            : 0.5f + 0.5f * MathF.Sqrt(Math.Clamp((horizon - elevation) / (horizon + MathF.PI / 2f), 0f, 1f));
        return (u, v);
    }

    /// <summary>The inverse of <see cref="SkyUv"/>: the direction's (φ, elevation).</summary>
    public static (float Phi, float Elevation) SkyDirection(float u, float v, float horizon)
    {
        float phi = MathF.PI * u * u;
        float e = v < 0.5f
            ? horizon + (MathF.PI / 2f - horizon) * (1f - 2f * v) * (1f - 2f * v)
            : horizon - (horizon + MathF.PI / 2f) * (2f * v - 1f) * (2f * v - 1f);
        return (phi, e);
    }

    /// <summary>A direction from the angle round from the sun's bearing and the elevation, with the sun at bearing 0 (+X).</summary>
    static Vector3 Direction(float phi, float elevation) =>
        new(MathF.Cos(elevation) * MathF.Cos(phi), MathF.Cos(elevation) * MathF.Sin(phi), MathF.Sin(elevation));

    /// <summary>The sun's direction with its bearing at +X.</summary>
    static Vector3 SunInPlane(Vector3 toSun) => new(MathF.Sqrt(MathF.Max(1f - toSun.Z * toSun.Z, 0f)), 0f, toSun.Z);

    /// <summary>The sky-view table (RGB, a row of <see cref="SkyW"/> texels after another), for the sun and the eye's altitude.</summary>
    public Vector3[] SkyView(Vector3 toSun, float altitudeKm)
    {
        var lut = new Vector3[SkyW * SkyH];
        float horizon = HorizonElevation(altitudeKm);
        var sun = SunInPlane(toSun);
        Parallel.For(0, SkyH, y =>
        {
            for (int x = 0; x < SkyW; x++)
            {
                var (phi, e) = SkyDirection((x + 0.5f) / SkyW, (y + 0.5f) / SkyH, horizon);
                lut[y * SkyW + x] = Sky(Direction(phi, e), sun, altitudeKm);
            }
        });
        return lut;
    }

    /// <summary>Reads a sky-view table as the sky shader does: the light from direction d (any sun bearing).</summary>
    public static Vector3 ReadSkyView(Vector3[] lut, Vector3 d, Vector3 toSun, float altitudeKm)
    {
        var (phi, e) = Angles(d, toSun);
        var (u, v) = SkyUv(phi, e, HorizonElevation(altitudeKm));
        return Bilinear(lut, SkyW, SkyH, u, v);
    }

    /// <summary>A direction's angle round from the sun's bearing (0..π) and its elevation.</summary>
    public static (float Phi, float Elevation) Angles(Vector3 d, Vector3 toSun)
    {
        var a = new Vector2(d.X, d.Y); var b = new Vector2(toSun.X, toSun.Y);
        float la = a.Length(), lb = b.Length();
        float phi = la < 1e-6f || lb < 1e-6f ? 0f : MathF.Acos(Math.Clamp(Vector2.Dot(a, b) / (la * lb), -1f, 1f));
        return (phi, MathF.Asin(Math.Clamp(d.Z, -1f, 1f)));
    }

    // ----- aerial perspective -----

    /// <summary>
    /// The aerial perspective table's mapping (ours): u the angle round from the sun's bearing, φ/π; v the
    /// elevation, ½ level, square-rooted either side so the rows crowd near level; slice k holds the ray's
    /// first ((k + 1) / 32)² of <see cref="ApRangeKm"/>.
    /// </summary>
    public static float ApV(float elevation) =>
        0.5f + 0.5f * MathF.Sign(elevation) * MathF.Sqrt(Math.Clamp(MathF.Abs(elevation) / (MathF.PI / 2f), 0f, 1f));

    public static float ApSliceKm(int k) => ApRangeKm * (k + 1f) * (k + 1f) / (ApSlices * ApSlices);

    /// <summary>
    /// The aerial perspective tables for the sun and the eye's altitude: in-scattered light and transmittance,
    /// each an atlas of the 32 slices (<see cref="ApColumns"/> across), each slice 32 × 32 (round from the sun
    /// by elevation).
    /// </summary>
    public (Vector3[] Inscatter, Vector3[] Transmittance) AerialPerspective(Vector3 toSun, float altitudeKm)
    {
        int aw = ApAz * ApColumns, ah = ApEl * (ApSlices / ApColumns);
        var ins = new Vector3[aw * ah];
        var tra = new Vector3[aw * ah];
        var sun = SunInPlane(toSun);
        float r = GroundKm + MathF.Max(altitudeKm, 0.001f);
        var eye = new Vector3(0, 0, r);
        Parallel.For(0, ApEl, y =>
        {
            float v = (y + 0.5f) / ApEl;
            float c = (v - 0.5f) * 2f;
            float e = MathF.Sign(c) * c * c * MathF.PI / 2f;
            for (int x = 0; x < ApAz; x++)
            {
                float phi = MathF.PI * (x + 0.5f) / ApAz;
                var d = Direction(phi, e);
                float mu = Vector3.Dot(d, sun);
                float pr = RayleighPhase(mu), pm = MiePhase(mu);
                var l = Vector3.Zero; var tr = Vector3.One;
                float prev = 0f;
                for (int k = 0; k < ApSlices; k++)
                {
                    float end = ApSliceKm(k);
                    for (int sub = 0; sub < 2; sub++)
                    {
                        float t1 = prev + (end - prev) * (sub + 1) / 2f;
                        float t0 = sub == 0 ? prev : prev + (end - prev) * 0.5f;
                        float dt = t1 - t0, t = t0 + dt * 0.5f;
                        var p = eye + d * t;
                        // Below the planet's sphere (a ray down into a valley, past the level's ground): its air.
                        float rp = MathF.Max(p.Length(), GroundKm);
                        var m = Medium(rp - GroundKm);
                        float muS = Vector3.Dot(p, sun) / p.Length();
                        var sunHere = SunAt(rp, muS);
                        var psi = MultipleScattering(Math.Clamp(rp, GroundKm, TopKm), muS);
                        var s = sunHere * (m.Air * pr + new Vector3(m.Aerosol * pm)) + psi * (m.Air + new Vector3(m.Aerosol));
                        var sample = Exp(-m.Extinction * dt);
                        l += tr * s * (Vector3.One - sample) / Vector3.Max(m.Extinction, new Vector3(1e-7f));
                        tr *= sample;
                    }
                    prev = end;
                    int at = ((k / ApColumns) * ApEl + y) * aw + (k % ApColumns) * ApAz + x;
                    ins[at] = l; tra[at] = tr;
                }
            }
        });
        return (ins, tra);
    }

    // ----- shared instances -----

    static readonly ConcurrentDictionary<(float, float), ModernAtmosphere> cache = new();

    /// <summary>The atmosphere for an aerosol scale and ground albedo, made once and kept.</summary>
    public static ModernAtmosphere For(float mieScale, float groundAlbedo) =>
        cache.GetOrAdd((MathF.Round(mieScale, 3), MathF.Round(groundAlbedo, 3)), k => new ModernAtmosphere(k.Item1, k.Item2));

    static readonly ConcurrentDictionary<(ModernAtmosphere, int, int, int), Vector3[]> skyCache = new();

    /// <summary>The sky-view table at the ground for a sun (to a tenth of a degree), made once and kept (the CPU's ambient light reads it).</summary>
    public Vector3[] GroundSkyView(Vector3 toSun)
    {
        var key = (this, (int)MathF.Round(toSun.X * 2000f), (int)MathF.Round(toSun.Y * 2000f), (int)MathF.Round(toSun.Z * 2000f));
        // Asked once for every direction of the ambient light's sum, every frame: a table already made is
        // answered without a closure being made to make it (590 of them a frame, 24 KB).
        if (skyCache.TryGetValue(key, out var made)) return made;
        if (skyCache.Count > 64) skyCache.Clear();
        return skyCache.GetOrAdd(key, SkyView(toSun, 0.002f));
    }
}
