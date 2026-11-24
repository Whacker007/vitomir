// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json.Nodes;
using Vitomir.Formats.L2;

namespace Vitomir.Render.Modern;

/// <summary>What the light probes are baked from: the level's triangles in metres, each one's albedo, the grid's box and the sun.</summary>
public sealed class ModernProbeBakeInput
{
    /// <summary>Corners in metres, Z up.</summary>
    public required Vector3[] Positions { get; init; }
    public required int[] Indices { get; init; }
    /// <summary>Each triangle's albedo, linear RGB.</summary>
    public required Vector3[] Albedo { get; init; }
    /// <summary>The box the grid covers, metres.</summary>
    public required Vector3 Min { get; init; }
    public required Vector3 Max { get; init; }
    /// <summary>The distance between probes, metres (ours: 1.5).</summary>
    public float Spacing { get; init; } = ModernProbeBake.DefaultSpacing;
    /// <summary>The direction towards the sun the bounce is baked for.</summary>
    public required Vector3 ToSun { get; init; }
    /// <summary>Rays from each probe.</summary>
    public int Rays { get; init; } = ModernProbeBake.DefaultRays;
    public int Threads { get; init; } = 2;
    /// <summary>Bounces followed: 1, or 2 (ours: the default) — the same rays again, each surface they meet lit by the first bounce's light there.</summary>
    public int Bounces { get; init; } = 2;
}

/// <summary>
/// **Ours: the light probes' bake** (the chunk is
/// <see cref="L2ProbeGrid"/>). A grid of probes over a level's open air, 1.5 m apart, from just below its
/// lowest ground to above its roofs; from each, <see cref="ModernProbeBakeInput.Rays"/> rays in fixed,
/// evenly spread directions (a Fibonacci sphere: the same for every probe, so neighbours agree and the bake
/// is the same every time) against the level's triangles (<see cref="ModernBvh"/>), on every CPU asked for:
/// <list type="bullet">
/// <item>a ray that leaves the level sees sky: it adds to the probe's <b>sky visibility</b> (nine spherical
/// harmonics), which the renderer lights by whatever sky it has;</item>
/// <item>a ray that hits a surface's front sees that surface's light: the sun's on it (its albedo / π times
/// the sun's cosine, if a ray from it towards the sun gets out) — the <b>sun's bounce</b> — and the sky's (one
/// ray in a cosine-weighted direction from it: if it gets out upwards, its albedo times the sky's average
/// radiance) — the <b>sky's bounce</b>; both as four RGB harmonics;</item>
/// <item>a ray that hits a surface's back sees darkness; a probe whose rays hit backs half the time or more
/// is inside a wall, a house or the ground. It is tried 0.7 m off along each axis (up first) and baked there
/// if it is out; otherwise it is filled from its neighbours (their average, spreading outwards until none is
/// left), so the renderer's blending between probes never reaches into black.</item>
/// </list>
/// A probe nearer than a fifth of the spacing to a surface is moved off it to there, so its light is not
/// that surface's own.
/// </summary>
public static class ModernProbeBake
{
    /// <summary>The probes' spacing in metres (ours: about two to a street's width, under a storey's height).</summary>
    public const float DefaultSpacing = 1.5f;
    /// <summary>Rays from each probe (ours).</summary>
    public const int DefaultRays = 192;
    const float Far = 3000f, Offset = 0.01f;

    /// <summary>Unit directions spread evenly over the sphere (a Fibonacci lattice).</summary>
    public static Vector3[] Directions(int n)
    {
        var d = new Vector3[n];
        float golden = MathF.PI * (3f - MathF.Sqrt(5f));
        for (int i = 0; i < n; i++)
        {
            float z = 1f - (2f * i + 1f) / n, r = MathF.Sqrt(MathF.Max(0f, 1f - z * z)), phi = i * golden;
            d[i] = new Vector3(r * MathF.Cos(phi), r * MathF.Sin(phi), z);
        }
        return d;
    }

    /// <summary>Bakes the grid. <paramref name="log"/> takes progress lines.</summary>
    public static L2ProbeGrid Bake(ModernProbeBakeInput input, Action<string>? log = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var bvh = new ModernBvh(input.Positions, input.Indices);
        log?.Invoke($"probes: tree over {bvh.Triangles:N0} triangles ({watch.Elapsed.TotalSeconds:0.0}s)");
        var normals = new Vector3[input.Indices.Length / 3];
        for (int t = 0; t < normals.Length; t++)
        {
            Vector3 a = input.Positions[input.Indices[3 * t]], b = input.Positions[input.Indices[3 * t + 1]], c = input.Positions[input.Indices[3 * t + 2]];
            var n = Vector3.Cross(b - a, c - a);
            normals[t] = n.LengthSquared() > 0 ? Vector3.Normalize(n) : Vector3.UnitZ;
        }

        float sp = input.Spacing;
        var origin = new Vector3(MathF.Floor(input.Min.X / sp) * sp, MathF.Floor(input.Min.Y / sp) * sp, MathF.Floor(input.Min.Z / sp) * sp);
        int nx = Math.Max(2, (int)MathF.Ceiling((input.Max.X - origin.X) / sp) + 1);
        int ny = Math.Max(2, (int)MathF.Ceiling((input.Max.Y - origin.Y) / sp) + 1);
        int nz = Math.Max(2, (int)MathF.Ceiling((input.Max.Z - origin.Z) / sp) + 1);
        int count = nx * ny * nz;
        var dirs = Directions(input.Rays);
        var basis = new float[dirs.Length * 9];
        Span<float> y = stackalloc float[9];
        for (int i = 0; i < dirs.Length; i++) { ModernAmbient.Basis(dirs[i], y); y.CopyTo(basis.AsSpan(i * 9, 9)); }
        float w = 4f * MathF.PI / dirs.Length;
        var toSun = Vector3.Normalize(input.ToSun);

        var state = new byte[count];
        var at = new Vector3[count];   // where each probe was baked
        var vis = new float[count * 9];
        var sun = new float[count * 12];
        var sky = new float[count * 12];
        int done = 0;
        long rays = 0;

        // One probe at a place: false when it is inside something.
        bool BakeAt(int index, Vector3 p, out float nearest, out Vector3 nearestNormal)
        {
            nearest = float.MaxValue; nearestNormal = Vector3.Zero;
            int backs = 0;
            Span<float> v = stackalloc float[9];
            Span<float> s = stackalloc float[12];
            Span<float> k = stackalloc float[12];
            v.Clear(); s.Clear(); k.Clear();
            uint seed = (uint)index * 9781u + 1u;
            for (int r = 0; r < dirs.Length; r++)
            {
                var d = dirs[r];
                var b = basis.AsSpan(r * 9, 9);
                var hit = bvh.Cast(p, d, Far);
                if (hit is not { } h)
                {
                    for (int c = 0; c < 9; c++) v[c] += b[c] * w;
                    continue;
                }
                if (h.Back) { backs++; continue; }
                var n = normals[h.Triangle];
                if (h.Distance < nearest) { nearest = h.Distance; nearestNormal = n; }
                var q = p + d * h.Distance + n * Offset;
                var albedo = input.Albedo[h.Triangle];
                // The sun on it, if a ray towards the sun gets out.
                float cos = Vector3.Dot(n, toSun);
                var lit = Vector3.Zero;
                if (cos > 0f && !bvh.Occluded(q, toSun, Far)) lit = albedo * (cos / MathF.PI);
                // The sky on it: one cosine-weighted direction; out and upwards counts.
                var skyLit = Vector3.Zero;
                var sd = CosineAround(n, Next(ref seed), Next(ref seed));
                if (sd.Z > 0f && !bvh.Occluded(q, sd, Far)) skyLit = albedo;
                for (int c = 0; c < 4; c++)
                {
                    float bw = b[c] * w;
                    s[c * 3] += lit.X * bw; s[c * 3 + 1] += lit.Y * bw; s[c * 3 + 2] += lit.Z * bw;
                    k[c * 3] += skyLit.X * bw; k[c * 3 + 1] += skyLit.Y * bw; k[c * 3 + 2] += skyLit.Z * bw;
                }
            }
            Interlocked.Add(ref rays, dirs.Length);
            if (backs * 2 >= dirs.Length) return false;
            at[index] = p;
            v.CopyTo(vis.AsSpan(index * 9, 9));
            s.CopyTo(sun.AsSpan(index * 12, 12));
            k.CopyTo(sky.AsSpan(index * 12, 12));
            return true;
        }

        // Whether a place is inside something, by a few of the rays.
        bool Inside(Vector3 p)
        {
            int backs = 0, cast = 0;
            for (int r = 0; r < dirs.Length; r += 4, cast++)
                if (bvh.Cast(p, dirs[r], Far) is { Back: true }) backs++;
            return backs * 2 >= cast;
        }

        var tries = new[] { Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitZ };
        Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, input.Threads) }, index =>
        {
            int x = index % nx, yy = index / nx % ny, z = index / (nx * ny);
            var p = origin + new Vector3(x, yy, z) * sp;
            if (BakeAt(index, p, out float nearest, out var nn))
            {
                state[index] = 1;
                // Too near a surface: moved off it.
                float want = sp * 0.2f;
                if (nearest < want && BakeAt(index, p + nn * (want - nearest), out _, out _)) state[index] = 2;
                else if (nearest < want) BakeAt(index, p, out _, out _);
            }
            else
            {
                foreach (var t in tries)
                {
                    var q = p + t * (sp * 0.45f);
                    if (Inside(q)) continue;
                    if (BakeAt(index, q, out _, out _)) { state[index] = 2; break; }
                }
            }
            int d = Interlocked.Increment(ref done);
            if (log is not null && d % 20000 == 0) log($"probes: {d:N0} of {count:N0} ({watch.Elapsed.TotalSeconds:0}s)");
        });

        int baked = state.Count(s => s != 0), moved = state.Count(s => s == 2);
        Fill(state, nx, ny, nz, vis, sun, sky);
        if (input.Bounces >= 2)
        {
            // The second bounce: the same rays again; what each meets is lit by the first bounce's light there,
            // read from the probes as the renderer reads them (half a spacing out along its normal).
            var sun2 = new float[count * 12];
            var sky2 = new float[count * 12];
            Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, input.Threads) }, index =>
            {
                if (state[index] == 0) return;
                var p = at[index];
                Span<float> s2 = stackalloc float[12];
                Span<float> k2 = stackalloc float[12];
                s2.Clear(); k2.Clear();
                for (int r = 0; r < dirs.Length; r++)
                {
                    var d = dirs[r];
                    if (bvh.Cast(p, d, Far) is not { Back: false } h) continue;
                    var n = normals[h.Triangle];
                    var q = p + d * h.Distance + n * (sp * 0.5f);
                    var albedo = input.Albedo[h.Triangle] / MathF.PI;
                    var es = albedo * Irradiance(sun, origin, sp, nx, ny, nz, q, n);
                    var ek = albedo * Irradiance(sky, origin, sp, nx, ny, nz, q, n);
                    var b = basis.AsSpan(r * 9, 9);
                    for (int c = 0; c < 4; c++)
                    {
                        float bw = b[c] * w;
                        s2[c * 3] += es.X * bw; s2[c * 3 + 1] += es.Y * bw; s2[c * 3 + 2] += es.Z * bw;
                        k2[c * 3] += ek.X * bw; k2[c * 3 + 1] += ek.Y * bw; k2[c * 3 + 2] += ek.Z * bw;
                    }
                }
                Interlocked.Add(ref rays, dirs.Length);
                s2.CopyTo(sun2.AsSpan(index * 12, 12));
                k2.CopyTo(sky2.AsSpan(index * 12, 12));
            });
            for (int i = 0; i < sun.Length; i++) { sun[i] += sun2[i]; sky[i] += sky2[i]; }
            Fill(state, nx, ny, nz, vis, sun, sky);
        }
        log?.Invoke($"probes: {nx} × {ny} × {nz} = {count:N0} probes {sp:0.##} m apart from ({origin.X:0.#}, {origin.Y:0.#}, {origin.Z:0.#}) m; " +
                    $"{baked:N0} in the open ({moved:N0} moved off a surface or out of one), {count - baked:N0} inside and filled; " +
                    $"{rays:N0} rays from the probes in {watch.Elapsed.TotalSeconds:0.0}s");
        return new L2ProbeGrid
        {
            Nx = nx, Ny = ny, Nz = nz, Origin = origin, Spacing = sp, ToSun = toSun, Rays = dirs.Length,
            State = state, Visibility = vis, SunBounce = sun, SkyBounce = sky,
        };
    }

    /// <summary>The irradiance on a surface facing <paramref name="n"/> at <paramref name="q"/> (metres) from four RGB coefficients a probe (bands 0 and 1, of radiance), trilinearly between the probes.</summary>
    static Vector3 Irradiance(float[] c, Vector3 origin, float sp, int nx, int ny, int nz, Vector3 q, Vector3 n)
    {
        var g = Vector3.Clamp((q - origin) / sp, Vector3.Zero, new Vector3(nx - 1, ny - 1, nz - 1));
        int x0 = Math.Min((int)g.X, nx - 2), y0 = Math.Min((int)g.Y, ny - 2), z0 = Math.Min((int)g.Z, nz - 2);
        var f = g - new Vector3(x0, y0, z0);
        // The cosine's convolution (π, 2π/3) times the basis at n.
        float w0 = MathF.PI * 0.282095f, w1 = 2f * MathF.PI / 3f * 0.488603f;
        float wy = w1 * n.Y, wz = w1 * n.Z, wx = w1 * n.X;
        var e = Vector3.Zero;
        for (int corner = 0; corner < 8; corner++)
        {
            int dx = corner & 1, dy = (corner >> 1) & 1, dz = corner >> 2;
            float t = (dx == 1 ? f.X : 1 - f.X) * (dy == 1 ? f.Y : 1 - f.Y) * (dz == 1 ? f.Z : 1 - f.Z);
            if (t <= 0f) continue;
            int o = (((z0 + dz) * ny + y0 + dy) * nx + x0 + dx) * 12;
            e += t * (new Vector3(c[o], c[o + 1], c[o + 2]) * w0 + new Vector3(c[o + 3], c[o + 4], c[o + 5]) * wy
                      + new Vector3(c[o + 6], c[o + 7], c[o + 8]) * wz + new Vector3(c[o + 9], c[o + 10], c[o + 11]) * wx);
        }
        return Vector3.Max(e, Vector3.Zero);
    }

    /// <summary>Fills each probe inside something with its baked or filled neighbours' average, outwards a layer at a time, until every probe has a value.</summary>
    static void Fill(byte[] state, int nx, int ny, int nz, float[] vis, float[] sun, float[] sky)
    {
        var has = state.Select(b => b != 0).ToArray();
        if (!has.Any(h => h)) return;
        var next = new List<int>();
        Span<float> v = stackalloc float[9]; Span<float> s = stackalloc float[12]; Span<float> k = stackalloc float[12];
        while (true)
        {
            next.Clear();
            for (int i = 0; i < has.Length; i++)
            {
                if (has[i]) continue;
                int x = i % nx, y = i / nx % ny, z = i / (nx * ny);
                bool any = false;
                for (int dz = -1; dz <= 1 && !any; dz++)
                    for (int dy = -1; dy <= 1 && !any; dy++)
                        for (int dx = -1; dx <= 1 && !any; dx++)
                        {
                            int a = x + dx, b = y + dy, c = z + dz;
                            if (a >= 0 && b >= 0 && c >= 0 && a < nx && b < ny && c < nz && has[(c * ny + b) * nx + a]) any = true;
                        }
                if (any) next.Add(i);
            }
            if (next.Count == 0) break;
            foreach (int i in next)
            {
                int x = i % nx, y = i / nx % ny, z = i / (nx * ny);
                int n = 0;
                v.Clear(); s.Clear(); k.Clear();
                for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int a = x + dx, b = y + dy, c = z + dz;
                            if (a < 0 || b < 0 || c < 0 || a >= nx || b >= ny || c >= nz) continue;
                            int j = (c * ny + b) * nx + a;
                            if (!has[j]) continue;
                            n++;
                            for (int q = 0; q < 9; q++) v[q] += vis[j * 9 + q];
                            for (int q = 0; q < 12; q++) { s[q] += sun[j * 12 + q]; k[q] += sky[j * 12 + q]; }
                        }
                for (int q = 0; q < 9; q++) vis[i * 9 + q] = v[q] / n;
                for (int q = 0; q < 12; q++) { sun[i * 12 + q] = s[q] / n; sky[i * 12 + q] = k[q] / n; }
            }
            foreach (int i in next) has[i] = true;
        }
    }

    static float Next(ref uint s)
    {
        s ^= s << 13; s ^= s >> 17; s ^= s << 5;
        return (s & 0xffffff) / 16777216f;
    }

    static Vector3 CosineAround(Vector3 n, float u1, float u2)
    {
        float r = MathF.Sqrt(u1), phi = 2f * MathF.PI * u2;
        var t = MathF.Abs(n.Z) < 0.9f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, n)) : Vector3.Normalize(Vector3.Cross(Vector3.UnitX, n));
        var b = Vector3.Cross(n, t);
        return Vector3.Normalize(t * (r * MathF.Cos(phi)) + b * (r * MathF.Sin(phi)) + n * MathF.Sqrt(MathF.Max(0f, 1f - u1)));
    }

    /// <summary>
    /// The direction towards the morning look's sun over Prague on the level's
    /// <paramref name="date"/> (ours: its day, at its hour or the look's 10:00; null: day 135, mid-May)
    /// by the level's compass (<paramref name="northYaw"/>: <see cref="L2North"/>; +X without one):
    /// what a cell level is seen in unless asked otherwise.
    /// </summary>
    public static Vector3 MorningSun(L2Date? date = null, float northYaw = L2North.DefaultYaw)
    {
        var s = new ModernSettings { LevelNorthYaw = northYaw };
        if (date is not null) (s.Day, s.Hour) = (date.DayOfYear, date.Hour);
        ModernLooks.Apply(s, "morning");
        return s.ToSun;
    }

    /// <summary>
    /// The cook's step (ours; <c>vitomir --cook-l2</c>, after the cells and the skyline): the level's triangles
    /// from its glTF — all but foliage, blended and passable materials, which light passes — each with its
    /// material's albedo (the base colour map's mean, in linear light, times the factor and the triangle's
    /// vertex colour), over the layout's <c>extent</c> box and 6 m round it (or the whole scene); baked for the
    /// morning look's sun on the level's date — <paramref name="date"/>, else the package's own (
    /// the sun of late August is not May's) — by the level's compass — <paramref name="north"/>, else the
    /// package's own — and written to the package's <see cref="L2ProbeGrid.FileName"/>,
    /// named in the manifest's extras, with the date and the compass it was baked for as the manifest's
    /// <c>date</c> and <c>north</c>.
    /// </summary>
    public static L2ProbeGrid? Cook(GltfScene scene, Func<int, bool> skipMaterial, JsonNode? layout, string package, int threads, Action<string> log,
                                    L2Date? date = null, L2North? north = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var matAlbedo = scene.Materials.Select((m, i) => MaterialAlbedo(scene, m)).ToArray();
        var positions = new List<Vector3>();
        var indices = new List<int>();
        var albedo = new List<Vector3>();
        float scale = ModernMath.UnitsPerMetre;
        foreach (var p in scene.Primitives)
        {
            var m = p.Material >= 0 && p.Material < scene.Materials.Count ? scene.Materials[p.Material] : null;
            if (m is not null && (m.Foliage || m.AlphaMode == GltfAlphaMode.Blend)) continue;
            if (p.Material >= 0 && skipMaterial(p.Material)) continue;
            var a = p.Material >= 0 && p.Material < matAlbedo.Length ? matAlbedo[p.Material] : new Vector3(0.3f);
            int b = positions.Count;
            foreach (var v in p.Vertices) positions.Add(v.Position / scale);
            for (int t = 0; t < p.Indices.Length / 3; t++)
            {
                int i0 = (int)p.Indices[3 * t], i1 = (int)p.Indices[3 * t + 1], i2 = (int)p.Indices[3 * t + 2];
                indices.Add(b + i0); indices.Add(b + i1); indices.Add(b + i2);
                var vc = (Colour(p.Vertices[i0].Color) + Colour(p.Vertices[i1].Color) + Colour(p.Vertices[i2].Color)) / 3f;
                albedo.Add(Vector3.Clamp(a * vc, Vector3.Zero, new Vector3(0.95f)));
            }
        }
        if (indices.Count == 0) return null;

        // The grid's box: the layout's extent and 6 m round it, else everything; from just below the ground to a little above the roofs.
        var lo = new Vector3(float.MaxValue); var hi = new Vector3(float.MinValue);
        foreach (var v in positions) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
        if (layout?["extent"]?["box"] is JsonArray box && box.Count == 4)
        {
            float margin = 6f;
            lo = new Vector3(MathF.Max(lo.X, (float)box[0]! - margin), MathF.Max(lo.Y, (float)box[1]! - margin), lo.Z);
            hi = new Vector3(MathF.Min(hi.X, (float)box[2]! + margin), MathF.Min(hi.Y, (float)box[3]! + margin), hi.Z);
        }
        var heights = positions.Where(v => v.X >= lo.X && v.X <= hi.X && v.Y >= lo.Y && v.Y <= hi.Y).Select(v => v.Z).OrderBy(z => z).ToArray();
        if (heights.Length > 0)
        {
            lo.Z = heights[(int)(heights.Length * 0.01)] - 1f;
            hi.Z = MathF.Min(heights[(int)(heights.Length * 0.99)] + 4f, lo.Z + 80f);
        }
        date ??= L2Package.Open(package).Manifest.Date;
        north ??= L2Package.Open(package).Manifest.North;
        var toSun = MorningSun(date, north?.Yaw ?? L2North.DefaultYaw);
        log($"probes: baked for the morning sun on {date ?? new L2Date()}, {north ?? new L2North()} (towards {toSun.X:0.###}, {toSun.Y:0.###}, {toSun.Z:0.###})");
        log($"probes: {indices.Count / 3:N0} triangles from the level, box ({lo.X:0.#}, {lo.Y:0.#}, {lo.Z:0.#}) to ({hi.X:0.#}, {hi.Y:0.#}, {hi.Z:0.#}) m");
        var grid = Bake(new ModernProbeBakeInput
        {
            Positions = [.. positions], Indices = [.. indices], Albedo = [.. albedo], Min = lo, Max = hi,
            ToSun = toSun, Threads = threads,
        }, log);
        long bytes = grid.WriteFile(package);
        var pkg = L2Package.Open(package);
        pkg.Manifest.Extras[L2ProbeGrid.ExtrasKey] = L2ProbeGrid.FileName;
        if (date is not null) pkg.Manifest.Date = date;   // the probes and the level's sun agree
        if (north is not null) pkg.Manifest.North = north;
        File.WriteAllText(Path.Combine(package, L2Package.ManifestFile), pkg.Manifest.Write(), new System.Text.UTF8Encoding(false));
        log($"probes: {grid.Count:N0} probes, {bytes / 1048576.0:0.0} MB in {L2ProbeGrid.FileName}; {watch.Elapsed.TotalSeconds:0.0}s in all");
        return grid;
    }

    static Vector3 Colour(uint rgba) => new((rgba & 0xff) / 255f, ((rgba >> 8) & 0xff) / 255f, ((rgba >> 16) & 0xff) / 255f);

    /// <summary>A material's mean albedo: its base colour map's mean in linear light (every few texels), times its factor.</summary>
    public static Vector3 MaterialAlbedo(GltfScene scene, GltfMaterial m)
    {
        var f = new Vector3(m.BaseColorFactor.X, m.BaseColorFactor.Y, m.BaseColorFactor.Z);
        var slot = m.BaseColorTexture;
        if (!slot.Present || slot.Texture < 0 || slot.Texture >= scene.Textures.Count) return f;
        int image = scene.Textures[slot.Texture].Image;
        if (image < 0 || image >= scene.Images.Count || !scene.Images[image].Present) return f;
        var img = scene.Images[image];
        // An image the cook left undecoded (GltfReadOptions.DeferImages) has its mean from the texture cook, or is decoded once for it here.
        if (img.Mean is null && img.Decode() is { } rgba) img.Mean = MeanLinear(rgba, img.Width, img.Height);
        return img.Mean is { } mean ? mean * f : f;
    }

    /// <summary>An RGBA8 sRGB image's mean colour in linear light, over every few texels (65,536 of them at most).</summary>
    public static Vector3 MeanLinear(byte[] rgba, int width, int height)
    {
        int pixels = Math.Min(width * height, rgba.Length / 4);
        int step = Math.Max(1, pixels / 65536);
        var sum = Vector3.Zero; int n = 0;
        for (int i = 0; i < pixels; i += step)
        {
            sum += new Vector3(ModernMath.SrgbToLinear(rgba[i * 4] / 255f), ModernMath.SrgbToLinear(rgba[i * 4 + 1] / 255f), ModernMath.SrgbToLinear(rgba[i * 4 + 2] / 255f));
            n++;
        }
        return n > 0 ? sum / n : Vector3.One;
    }
}
