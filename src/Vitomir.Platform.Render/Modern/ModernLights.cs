// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernLights.cs at b4185ef, reworked for the platform.
using System.Numerics;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// A point or spot light of the modern path (ours): where it is (engine units), its colour times its
/// intensity (relative units: the irradiance it gives one metre away, facing it), how far it reaches
/// (engine units; the light fades to nothing there), and for a spot its direction and cone.
/// </summary>
internal readonly record struct ModernPointLight(Vector3 Position, Vector3 Colour, float Range,
                                               Vector3 Direction = default, float CosInner = 1f, float CosOuter = -2f)
{
    public bool Spot => CosOuter > -1.5f;
}

/// <summary>
/// The scene's point and spot lights, sorted into clusters (ours).
/// The view is cut into tiles on the screen (<see cref="TileSize"/> pixels square) and into slices by
/// distance, evenly in its logarithm from <see cref="ClusterNear"/> to <see cref="ClusterFar"/>, so near
/// slices are thin and far ones deep; each cluster lists the lights whose reach overlaps it. A pixel then
/// shades only its own cluster's lights, which is what lets a street carry hundreds of torches and
/// windows at the cost of the few that touch each pixel. The sorting is done on the CPU each frame
/// (hundreds of lights take well under a millisecond); ModernPbr.hlsl reads the result.
/// </summary>
internal static class ModernLights
{
    public const int TileSize = 64;
    public const int Slices = 16;
    /// <summary>The first slice starts here (engine units, 1 m); nearer pixels use the first slice.</summary>
    public const float ClusterNear = 72f;
    /// <summary>The last slice ends here (engine units, 400 m); farther pixels use the last slice.</summary>
    public const float ClusterFar = 400f * ModernMath.UnitsPerMetre;
    /// <summary>At most this many lights in a frame, and light indices over all clusters.</summary>
    public const int MaxLights = 4096, MaxIndices = 1 << 18;

    /// <summary>
    /// The irradiance a light gives at distance <paramref name="distance"/> (engine units), per unit of its
    /// intensity: the inverse square in metres (floored at 10 cm, so a surface at the bulb does not blow
    /// up), times glTF's recommended window (1 − (d / range)⁴)², which reaches zero at the range and
    /// leaves the inverse square nearly untouched well inside it. The shader's LightFalloff is the same.
    /// </summary>
    public static float Attenuation(float distance, float range)
    {
        float r = distance * distance / (range * range);
        float window = Math.Clamp(1f - r * r, 0f, 1f);
        float metres = distance / ModernMath.UnitsPerMetre;
        return window * window / MathF.Max(metres * metres, 0.01f);
    }

    /// <summary>A spot's cone: 1 inside the inner angle, 0 outside the outer, smooth (squared) between (glTF's).</summary>
    public static float Cone(float cosAngle, float cosInner, float cosOuter)
    {
        float t = Math.Clamp((cosAngle - cosOuter) / MathF.Max(cosInner - cosOuter, 1e-4f), 0f, 1f);
        return t * t;
    }

    /// <summary>
    /// How far a light without a range of its own reaches: where its irradiance falls to
    /// <paramref name="cutoff"/>, √(intensity / cutoff) metres, in engine units.
    /// </summary>
    public static float ReachFor(float intensity, float cutoff) =>
        MathF.Sqrt(MathF.Max(intensity, 0f) / MathF.Max(cutoff, 1e-6f)) * ModernMath.UnitsPerMetre;

    /// <summary>
    /// The modern path's lights for a glTF scene's <c>KHR_lights_punctual</c> point and spot lights.
    /// The file's intensities are photometric (candela); one relative unit is taken as 683 lux, the
    /// luminous efficacy Blender's exporter uses for a watt, so a scene lit in Blender's watts comes back
    /// in the same relative units as the looks' suns. Directional lights are left to the sun's settings.
    /// </summary>
    public static List<ModernPointLight> FromGltf(GltfScene scene, float cutoff)
    {
        var list = new List<ModernPointLight>();
        foreach (var l in scene.Lights)
        {
            if (l.Type == GltfLightType.Directional) continue;
            float intensity = l.Intensity / 683f;
            if (intensity <= 0f) continue;
            float range = l.Range > 0f ? l.Range : ReachFor(intensity * MaxChannel(l.Colour), cutoff);
            list.Add(l.Type == GltfLightType.Spot
                ? new ModernPointLight(l.Position, l.Colour * intensity, range, l.Direction, MathF.Cos(l.InnerCone), MathF.Cos(l.OuterCone))
                : new ModernPointLight(l.Position, l.Colour * intensity, range));
        }
        return list;
    }

    static float MaxChannel(Vector3 c) => MathF.Max(c.X, MathF.Max(c.Y, c.Z));

    /// <summary>
    /// The lights packed for the shader: four float4 each — position and range; colour; a spot's
    /// direction and outer cone cosine (−2 for a point light); the inner cosine, 1 / (inner − outer), and
    /// the light's row in the shadow atlas (−1: no shadows), from <paramref name="shadowed"/>.
    /// </summary>
    public static Vector4[] Pack(IReadOnlyList<ModernPointLight> lights, IReadOnlyList<int>? shadowed = null)
    {
        int n = Math.Min(lights.Count, MaxLights);
        var data = new Vector4[Math.Max(n, 1) * 4];
        var slot = new Dictionary<int, int>();
        for (int k = 0; shadowed is not null && k < shadowed.Count; k++) slot[shadowed[k]] = k;
        for (int i = 0; i < n; i++)
        {
            var l = lights[i];
            data[i * 4] = new Vector4(l.Position, l.Range);
            data[i * 4 + 1] = new Vector4(l.Colour, 0);
            data[i * 4 + 2] = new Vector4(l.Spot ? Vector3.Normalize(l.Direction) : Vector3.UnitZ, l.CosOuter);
            data[i * 4 + 3] = new Vector4(l.CosInner, 1f / MathF.Max(l.CosInner - l.CosOuter, 1e-4f), slot.TryGetValue(i, out int k) ? k : -1, 0);
        }
        return data;
    }

    /// <summary>
    /// Which lights cast shadows this frame: up to <paramref name="budget"/> of those whose reach comes
    /// within <paramref name="near"/> of the eye or is in view, the ones giving most light at the eye
    /// first (colour's brightest channel over the square of the distance, floored at 1 m).
    /// </summary>
    public static List<int> PickShadowed(IReadOnlyList<ModernPointLight> lights, Vector3 eye, int budget, ReadOnlySpan<Vector4> viewPlanes)
    {
        var scored = new List<(int Index, float Score)>();
        int n = Math.Min(lights.Count, MaxLights);
        for (int i = 0; i < n && budget > 0; i++)
        {
            var l = lights[i];
            var r = new Vector3(l.Range);
            if (!ModernMath.BoxVisible(viewPlanes, l.Position - r, l.Position + r)) continue;
            float d = MathF.Max(Vector3.Distance(l.Position, eye) / ModernMath.UnitsPerMetre, 1f);
            scored.Add((i, MathF.Max(l.Colour.X, MathF.Max(l.Colour.Y, l.Colour.Z)) / (d * d)));
        }
        return [.. scored.OrderByDescending(x => x.Score).ThenBy(x => x.Index).Take(Math.Max(budget, 0)).Select(x => x.Index)];
    }

    /// <summary>The six faces of a light's shadow cube, in the order the shader picks them: +X, −X, +Y, −Y, +Z, −Z.</summary>
    public static int CubeFace(Vector3 d)
    {
        var a = Vector3.Abs(d);
        if (a.X >= a.Y && a.X >= a.Z) return d.X >= 0 ? 0 : 1;
        if (a.Y >= a.Z) return d.Y >= 0 ? 2 : 3;
        return d.Z >= 0 ? 4 : 5;
    }

    static readonly Vector3[] FaceDir = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
    static readonly Vector3[] FaceUp = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY];

    /// <summary>
    /// One face of a light's shadow cube: looking from the light along the face's axis, a square
    /// perspective a little wider than 90° (by <paramref name="guardTexels"/> texels at
    /// <paramref name="resolution"/>, so the filter near a face's edge stays inside the face), depth 0 at
    /// 5 cm and 1 at the light's range.
    /// </summary>
    public static Matrix4x4 CubeFaceViewProjection(Vector3 position, int face, float range, int resolution, float guardTexels = 3f)
    {
        float fov = 2f * MathF.Atan(1f + 2f * guardTexels / resolution);
        var view = Matrix4x4.CreateLookAt(position, position + FaceDir[face], FaceUp[face]);
        return view * Matrix4x4.CreatePerspectiveFieldOfView(fov, 1f, 0.05f * ModernMath.UnitsPerMetre, MathF.Max(range, ModernMath.UnitsPerMetre));
    }

    /// <summary>The cluster grid for a picture: tiles across and down, and slices.</summary>
    public static (int X, int Y, int Z) Grid(int width, int height) =>
        ((width + TileSize - 1) / TileSize, (height + TileSize - 1) / TileSize, Slices);

    /// <summary>The slice a view distance falls in (the shader's formula).</summary>
    public static int SliceOf(float viewDepth) =>
        (int)Math.Clamp(MathF.Floor(MathF.Log(MathF.Max(viewDepth, 1f) / ClusterNear) * SliceScale), 0, Slices - 1);

    /// <summary>Slices per unit of ln(distance): the grid's log spacing.</summary>
    public static float SliceScale => Slices / MathF.Log(ClusterFar / ClusterNear);

    static float SliceStart(int s) => s == 0 ? 0f : ClusterNear * MathF.Exp(s / SliceScale);
    static float SliceEnd(int s) => s == Slices - 1 ? float.MaxValue : ClusterNear * MathF.Exp((s + 1) / SliceScale);

    /// <summary>
    /// Sorts <paramref name="lights"/> into the clusters of a view: <paramref name="view"/> is the
    /// camera's look-at matrix (row vectors, the camera looking along −Z), <paramref name="xScale"/> and
    /// <paramref name="yScale"/> the projection's, the picture <paramref name="width"/> × <paramref name="height"/>.
    /// Returns one buffer: an (offset, count) pair per cluster, x fastest then y then slice, followed by
    /// the light indices the offsets point into. A light is listed in every cluster its sphere of reach
    /// may touch: its depth span picks the slices, its bounding square on the screen the tiles, each
    /// tile's slice then checked against the sphere by its corner rays, so a light is not listed far
    /// outside its reach.
    /// </summary>
    public static uint[] Assign(IReadOnlyList<ModernPointLight> lights, Matrix4x4 view, float xScale, float yScale, int width, int height) =>
        new ClusterSorter().Assign(lights, view, xScale, yScale, width, height).ToArray();

    /// <summary>
    /// <see cref="ModernLights.Assign"/> with its arrays kept from one frame to the next (ours, levels round
    /// 26). The grid was made anew each frame: a list for every
    /// cluster a light touched, an array of those lists, and the buffer grown and then copied. At the
    /// <c>high</c> preset's picture two of those arrays were over 85,000 bytes, so they went to the large
    /// object heap every frame, and its budget began a full garbage collection every few hundred frames.
    /// Here each (cluster, light) pair found is written to two arrays that are kept, the clusters' counts
    /// are summed into their offsets, and the pairs are dealt into one buffer that is kept: the arrays grow
    /// when a frame needs more and are never made smaller, so a frame like the one before allocates
    /// nothing. The buffer's contents are exactly the old ones: the pairs are found light by light, so each
    /// cluster's lights come in the lights' own order, as its list had them, and the offsets are laid out
    /// cluster by cluster as before (pinned against the old code in <c>ModernLightGridAllocationTests</c>).
    /// One sorter serves one renderer; it is not for two threads at once.
    /// </summary>
    public sealed class ClusterSorter
    {
        uint[] data = [];                       // an (offset, count) pair per cluster, then the light indices
        int[] cursor = [];                      // per cluster: its lights counted, then where its next index goes
        int[] hitCluster = [], hitLight = [];   // each (cluster, light) pair, in the order found

        /// <summary>
        /// The clusters' buffer for a view, as <see cref="ModernLights.Assign"/> describes it. The span is
        /// this sorter's own memory: it holds until the next call.
        /// </summary>
        public ReadOnlySpan<uint> Assign(IReadOnlyList<ModernPointLight> lights, Matrix4x4 view, float xScale, float yScale, int width, int height)
        {
            var (gx, gy, gz) = Grid(width, height);
            int clusters = gx * gy * gz;
            if (cursor.Length < clusters) cursor = new int[clusters];
            else Array.Clear(cursor, 0, clusters);
            int hits = 0;
            int n = Math.Min(lights.Count, MaxLights);
            for (int i = 0; i < n; i++)
            {
                var l = lights[i];
                var c = Vector3.Transform(l.Position, view);
                float depth = -c.Z, r = l.Range;
                if (depth + r < 0f) continue;   // wholly behind the eye
                int s0 = SliceOf(depth - r), s1 = SliceOf(depth + r);
                int x0 = 0, x1 = gx - 1, y0 = 0, y1 = gy - 1;
                if (depth - r > 1f)
                {
                    // The sphere's bounding square on the screen: its box's sides seen at the box's nearer and
                    // farther depths, whichever reaches further out.
                    float near = depth - r, far = depth + r;
                    float sx0 = MathF.Min((c.X - r) / near, (c.X - r) / far) * xScale, sx1 = MathF.Max((c.X + r) / near, (c.X + r) / far) * xScale;
                    float sy0 = MathF.Min((c.Y - r) / near, (c.Y - r) / far) * yScale, sy1 = MathF.Max((c.Y + r) / near, (c.Y + r) / far) * yScale;
                    if (sx1 < -1f || sx0 > 1f || sy1 < -1f || sy0 > 1f) continue;
                    x0 = Math.Clamp((int)MathF.Floor((sx0 * 0.5f + 0.5f) * width / TileSize), 0, gx - 1);
                    x1 = Math.Clamp((int)MathF.Floor((sx1 * 0.5f + 0.5f) * width / TileSize), 0, gx - 1);
                    // Screen rows run down, view y up.
                    y0 = Math.Clamp((int)MathF.Floor((0.5f - sy1 * 0.5f) * height / TileSize), 0, gy - 1);
                    y1 = Math.Clamp((int)MathF.Floor((0.5f - sy0 * 0.5f) * height / TileSize), 0, gy - 1);
                }
                for (int s = s0; s <= s1; s++)
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            if (!Touches(c, r, x, y, s, width, height, xScale, yScale)) continue;
                            int k = (s * gy + y) * gx + x;
                            if (hits == hitCluster.Length)
                            {
                                int room = Math.Max(1024, hits * 2);
                                Array.Resize(ref hitCluster, room);
                                Array.Resize(ref hitLight, room);
                            }
                            hitCluster[hits] = k; hitLight[hits] = i; hits++;
                            cursor[k]++;
                        }
            }
            // The clusters' offsets and counts, cluster by cluster: a cluster no light touches is (0, 0);
            // once MaxIndices indices are given out the later clusters get none.
            int head = clusters * 2, need = head + Math.Min(hits, MaxIndices);
            if (data.Length < need) data = new uint[Math.Max(need, Math.Min(data.Length * 2, head + MaxIndices))];
            int used = 0;
            for (int k = 0; k < clusters; k++)
            {
                int count = cursor[k];
                if (count == 0) { data[k * 2] = 0; data[k * 2 + 1] = 0; continue; }
                int take = Math.Min(count, MaxIndices - used);
                data[k * 2] = (uint)(head + used);
                data[k * 2 + 1] = (uint)take;
                cursor[k] = head + used;
                used += take;
            }
            // The pairs dealt to their clusters in the order found, so each cluster's lights are in the lights' order.
            for (int h = 0; h < hits; h++)
            {
                int k = hitCluster[h], at = cursor[k];
                if (at >= (int)(data[k * 2] + data[k * 2 + 1])) continue;   // over MaxIndices: not listed
                data[at] = (uint)hitLight[h];
                cursor[k] = at + 1;
            }
            return data.AsSpan(0, head + used);
        }
    }

    /// <summary>
    /// Whether a sphere (view space, centre <paramref name="c"/>, radius <paramref name="r"/>) touches a
    /// cluster: the distance from the centre to the cluster's box, the box being the tile's four corner
    /// rays cut at the slice's two depths.
    /// </summary>
    static bool Touches(Vector3 c, float r, int tx, int ty, int s, int width, int height, float xScale, float yScale)
    {
        float z0 = SliceStart(s), z1 = MathF.Min(SliceEnd(s), -c.Z + r + 1f);
        z0 = MathF.Max(z0, -c.Z - r - 1f);
        if (z1 < z0) return false;
        float u0 = (float)(tx * TileSize) / width * 2f - 1f, u1 = (float)((tx + 1) * TileSize) / width * 2f - 1f;
        float v1 = 1f - (float)(ty * TileSize) / height * 2f, v0 = 1f - (float)((ty + 1) * TileSize) / height * 2f;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (float z in (ReadOnlySpan<float>)[z0, z1])
            foreach (float u in (ReadOnlySpan<float>)[u0, u1])
                foreach (float v in (ReadOnlySpan<float>)[v0, v1])
                {
                    var p = new Vector3(u / xScale * z, v / yScale * z, -z);
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                }
        var q = Vector3.Clamp(c, min, max);
        return Vector3.DistanceSquared(q, c) <= r * r;
    }
}
