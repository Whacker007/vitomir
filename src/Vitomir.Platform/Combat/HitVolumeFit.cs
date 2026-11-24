// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using static Vitomir.Platform.Combat.Geometry;

namespace Vitomir.Platform.Combat;

/// <summary>
/// One hit volume of a fitted body (ours): a capsule on a bone, its ends <see cref="A"/> and <see cref="B"/> in
/// the bone's own frame (the frame its <see cref="Skeleton.InverseBind"/> takes the model's vertices into, metres),
/// its radius, its region, and how many of the model's vertices it was fitted to.
/// </summary>
public readonly record struct BoneVolume(int Bone, BodyRegion Region, Vector3 A, Vector3 B, float Radius, int Vertices);

/// <summary>
/// A model's hit volumes (ours): a capsule for each bone of its body with enough vertices, made once per model
/// (<see cref="HitVolumeFit"/>), and the reach that bounds where they can be in any pose (<see cref="Reach"/>).
/// </summary>
public sealed class FittedBody
{
    public required string Model { get; init; }

    /// <summary>The anatomy table its regions came from (<see cref="AnatomyTables"/>).</summary>
    public required string Table { get; init; }

    public required BoneVolume[] Volumes { get; init; }

    /// <summary>The root bone (the hips).</summary>
    public required int Root { get; init; }

    /// <summary>
    /// The farthest any volume's surface can be from the root's joint in any pose that turns the bones without
    /// stretching them (m, the model's own size): along the chain of bones from the root, each joint-to-joint length,
    /// then the volume's own far end and radius. The broad test (<see cref="HitVolumes.Broad"/>) grows the body's
    /// capsule by this.
    /// </summary>
    public required float Reach { get; init; }
}

/// <summary>
/// Fitting a model's hit volumes from its skin (ours, WYSIWYG).
/// For each bone the anatomy table gives a region, its vertices (a weight of at least <see cref="WeightAtLeast"/> on
/// it) are taken into its own frame. A bone with one child in another region (a thigh to its shin, an upper arm to its
/// forearm, the neck to the head) gets a capsule <b>along the line from its joint to its child's joint</b>, moved across
/// to the middle of its vertices (<see cref="ChainVerticesAtLeast"/> of them at least); one with none or several, or
/// whose child is the same region (the hips, the upper chest, the head, a hand, a foot), a capsule along its vertices'
/// longest axis through their middle (<see cref="VerticesAtLeast"/> at least). Either way the ends stop a radius short
/// of the vertices' own ends, so the round ends stay within what the bone draws. The radius is the
/// <see cref="RadiusShare"/> percentile of the vertices' distances from the line (<see cref="FewRadiusShare"/> for a bone
/// with few), so a few outlying vertices (a sleeve's hem, a nose) do not swell it beyond the drawn body, and at most
/// <see cref="Slack"/> beyond the least the vertices reach round the line (half a robe). Cloth (no region) gets none.
/// The numbers were chosen by looking.
/// </summary>
public static class HitVolumeFit
{
    /// <summary>A vertex counts for a bone whose weight on it is at least this.</summary>
    public const float WeightAtLeast = 0.5f;

    /// <summary>The percentile of the vertices' distances from the line the radius is taken at.</summary>
    public const float RadiusShare = 0.9f;

    /// <summary>
    /// A bone with fewer vertices than this (a robe's thigh is a ring or two of them) takes its radius at the
    /// <see cref="FewRadiusShare"/> percentile instead: with so few, the 90th is nearly the farthest, and a ball that
    /// reaches the farthest vertex of a sparse ring stands out of the robe between them.
    /// </summary>
    public const int FewVertices = 12;

    /// <summary>The percentile the radius of a bone with few vertices is taken at.</summary>
    public const float FewRadiusShare = 0.5f;

    /// <summary>How far a radius may reach beyond the vertices' least reach round the line (m; <see cref="Across"/>).</summary>
    public const float Slack = 0.04f;

    /// <summary>The fewest vertices a bone needs for a volume along its vertices' own axis.</summary>
    public const int VerticesAtLeast = 6;

    /// <summary>The fewest a bone running joint to joint needs (the axis is the bones', so a few vertices give the radius: a monk's thigh under his robe).</summary>
    public const int ChainVerticesAtLeast = 3;

    /// <summary>The model's volumes, from the anatomy table for its skeleton (<paramref name="table"/>, or the one <see cref="AnatomyTables.For"/> finds); null when there is none.</summary>
    public static FittedBody? Fit(SkinnedModel model, AnatomyTable? table = null)
    {
        var skeleton = model.Skeleton;
        table ??= AnatomyTables.For(skeleton);
        if (table is null || !table.Matches(skeleton)) return null;
        if (table.Volumes is { } merged) return FitRegions(model, table, merged);   // the people, one volume a region
        int n = skeleton.Count;
        var clusters = new List<Vector3>[n];
        for (int b = 0; b < n; b++) clusters[b] = [];
        foreach (ref readonly var v in model.Vertices.AsSpan())
        {
            for (int k = 0; k < 4; k++)
            {
                float w = k == 0 ? v.Weights.X : k == 1 ? v.Weights.Y : k == 2 ? v.Weights.Z : v.Weights.W;
                if (w < WeightAtLeast) continue;
                int b = v.Bone(k);
                if (b < n) clusters[b].Add(PoseMath.Transform(v.Position, skeleton.InverseBind[b]));
                break;
            }
        }
        // Each joint in the model's space at rest (the bone's origin: the inverse of its inverse bind).
        var joints = new Vector3[n];
        for (int b = 0; b < n; b++) joints[b] = AffineInverseTranslation(skeleton.InverseBind[b]);
        bool Body(int b) => table.Of(b) != BodyRegion.None && clusters[b].Count >= ChainVerticesAtLeast;

        int root = 0;
        for (int b = 0; b < n; b++) if (skeleton.Bones[b].Parent < 0) { root = b; break; }
        var chain = new float[n];
        for (int b = 0; b < n; b++)
        {
            int p = skeleton.Bones[b].Parent;
            chain[b] = p >= 0 && p < b ? chain[p] + Length(joints[b] - joints[p]) : 0f;
        }

        var volumes = new List<BoneVolume>();
        float reach = 0f;
        for (int b = 0; b < n; b++)
        {
            if (!Body(b)) continue;
            var region = table.Of(b);
            int only = -1, count = 0;
            for (int c = b + 1; c < n; c++)
                if (skeleton.Bones[c].Parent == b && Body(c)) { only = c; count++; }
            var points = clusters[b];
            Vector3 a, e;
            float radius;
            if (count == 1 && table.Of(only) != region)
            {
                var child = PoseMath.Transform(joints[only], skeleton.InverseBind[b]);
                (a, e, radius) = AlongJoints(points, child);
            }
            else if (points.Count >= VerticesAtLeast) (a, e, radius) = AlongLongestAxis(points);
            else continue;
            volumes.Add(new BoneVolume(b, region, a, e, radius, points.Count));
            reach = MathF.Max(reach, chain[b] + MathF.Max(Length(a), Length(e)) + radius);
        }
        return new FittedBody { Model = model.Name, Table = table.Name, Volumes = [.. volumes], Root = root, Reach = reach };
    }

    /// <summary>
    /// <b>One volume a region</b>, on the bone
    /// <paramref name="volumes"/> names for it (<see cref="PeopleAnatomy"/>), fitted from the skin of all the region's bones
    /// together: a vertex's weights on the bones of one volume are summed before its volume is chosen (the one with most, at
    /// least <see cref="WeightAtLeast"/>), so the skin of an arm shared between the arm and its twist bones, or a hand's between
    /// the hand and its fingers, is one cluster, taken into the volume's bone's frame. A limb's link — the neck, an upper arm, a
    /// forearm, a thigh, a shin — runs from its bone's joint toward the next volume's joint down the limb (as 1.1's bones with
    /// one child of another region); the head, the trunk, a hand and a foot along their vertices' longest axis. The radii and
    /// ends as for 1.1's bones.
    /// </summary>
    static FittedBody FitRegions(SkinnedModel model, AnatomyTable table, int[] volumes)
    {
        var skeleton = model.Skeleton;
        int n = skeleton.Count;
        var clusters = new List<Vector3>[n];
        for (int b = 0; b < n; b++) clusters[b] = [];
        Span<int> on = stackalloc int[4];
        Span<float> sum = stackalloc float[4];
        foreach (ref readonly var v in model.Vertices.AsSpan())
        {
            int count = 0;
            for (int k = 0; k < 4; k++)
            {
                float w = k == 0 ? v.Weights.X : k == 1 ? v.Weights.Y : k == 2 ? v.Weights.Z : v.Weights.W;
                int bone = v.Bone(k);
                if (w <= 0f || bone >= n || volumes[bone] < 0) continue;
                int vol = volumes[bone], at = 0;
                while (at < count && on[at] != vol) at++;
                if (at == count) { on[count] = vol; sum[count] = 0f; count++; }
                sum[at] += w;
            }
            int best = -1;
            float most = 0f;
            for (int i = 0; i < count; i++) if (sum[i] > most) { most = sum[i]; best = on[i]; }
            if (best >= 0 && most >= WeightAtLeast) clusters[best].Add(PoseMath.Transform(v.Position, skeleton.InverseBind[best]));
        }
        var joints = new Vector3[n];
        for (int b = 0; b < n; b++) joints[b] = AffineInverseTranslation(skeleton.InverseBind[b]);
        int root = 0;
        for (int b = 0; b < n; b++) if (skeleton.Bones[b].Parent < 0) { root = b; break; }
        var chain = new float[n];
        for (int b = 0; b < n; b++)
        {
            int p = skeleton.Bones[b].Parent;
            chain[b] = p >= 0 && p < b ? chain[p] + Length(joints[b] - joints[p]) : 0f;
        }

        var fitted = new List<BoneVolume>();
        float reach = 0f;
        for (int b = 0; b < n; b++)
        {
            if (volumes[b] != b) continue;
            var points = clusters[b];
            if (points.Count < ChainVerticesAtLeast) continue;
            var region = table.Of(b);
            // A limb's link: the one child that carries another region's volume (a forearm for an upper arm, the head for the neck).
            int next = -1, links = 0;
            if (region is BodyRegion.Neck or BodyRegion.LeftUpperArm or BodyRegion.RightUpperArm or BodyRegion.LeftForearm or BodyRegion.RightForearm
                       or BodyRegion.LeftThigh or BodyRegion.RightThigh or BodyRegion.LeftShin or BodyRegion.RightShin)
                for (int c = b + 1; c < n; c++)
                    if (skeleton.Bones[c].Parent == b && volumes[c] == c && table.Of(c) != region) { next = c; links++; }
            Vector3 a, e;
            float radius;
            if (links == 1) (a, e, radius) = AlongJoints(points, PoseMath.Transform(joints[next], skeleton.InverseBind[b]));
            else if (points.Count >= VerticesAtLeast) (a, e, radius) = AlongLongestAxis(points);
            else continue;
            fitted.Add(new BoneVolume(b, region, a, e, radius, points.Count));
            reach = MathF.Max(reach, chain[b] + MathF.Max(Length(a), Length(e)) + radius);
        }
        return new FittedBody { Model = model.Name, Table = table.Name, Volumes = [.. fitted], Root = root, Reach = reach };
    }

    /// <summary>
    /// A capsule along the line from the bone's joint (its frame's origin) to its child's joint <paramref name="child"/>,
    /// moved across to pass through the middle of the bone's vertices (a limb's own line when its flesh is round it;
    /// the middle of a robe's half when a robe hangs from it), its radius the percentile of the vertices' distances from
    /// that line, and its ends a radius short of the vertices' own ends along it, so its round ends stay within what
    /// the bone draws rather than standing out past a knee into the ankle's thinness, or below a heel.
    /// </summary>
    static (Vector3 A, Vector3 B, float Radius) AlongJoints(List<Vector3> points, Vector3 child)
    {
        var u = Normalize(child, Vector3.UnitZ);
        var mid = Vector3.Zero;
        foreach (var p in points) mid += p;
        mid /= points.Count;
        mid -= u * Dot(mid, u);   // across the line only: the line keeps the joints' direction and their place along it
        return Span(points, mid, u);
    }

    /// <summary>The capsule along the line through <paramref name="origin"/> in the unit direction <paramref name="u"/> that the points fill: the radius their distances' percentile, the ends a radius short of theirs (a ball at their middle when they are shorter than its width).</summary>
    static (Vector3 A, Vector3 B, float Radius) Span(List<Vector3> points, Vector3 origin, Vector3 u)
    {
        float radius = Percentile(LineDistances(points, origin, u), points.Count >= FewVertices ? RadiusShare : FewRadiusShare);
        radius = MathF.Min(radius, Across(points, origin, u) + Slack);
        float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
        foreach (var p in points)
        {
            float s = Dot(p - origin, u);
            lo = MathF.Min(lo, s); hi = MathF.Max(hi, s);
        }
        if (hi - lo <= 2f * radius) { var c = origin + u * ((lo + hi) * 0.5f); return (c, c, radius); }
        return (origin + u * (lo + radius), origin + u * (hi - radius), radius);
    }

    /// <summary>A capsule along the points' longest axis (their covariance's first eigenvector, by power iteration) through their middle, its ends a radius short of theirs.</summary>
    static (Vector3 A, Vector3 B, float Radius) AlongLongestAxis(List<Vector3> points)
    {
        var mid = Vector3.Zero;
        foreach (var p in points) mid += p;
        mid /= points.Count;
        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var p in points)
        {
            var d = p - mid;
            xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z; yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z;
        }
        var u = xx >= yy && xx >= zz ? Vector3.UnitX : yy >= zz ? Vector3.UnitY : Vector3.UnitZ;
        for (int i = 0; i < 64; i++)
        {
            var next = new Vector3(xx * u.X + xy * u.Y + xz * u.Z, xy * u.X + yy * u.Y + yz * u.Z, xz * u.X + yz * u.Y + zz * u.Z);
            u = Normalize(next, u);
        }
        return Span(points, mid, u);
    }

    /// <summary>
    /// The least, over eight ways round the line (every 45°), of how far the points reach from it that way: a radius
    /// above this stands out of the vertices on that side (a capsule on half a robe, which reaches far to the front and
    /// back but little outwards from its middle).
    /// </summary>
    static float Across(List<Vector3> points, Vector3 origin, Vector3 u)
    {
        var e1 = Perpendicular(u);
        var e2 = Cross(u, e1);
        const float H = 0.70710678f;
        ReadOnlySpan<float> cos = [1f, H, 0f, -H, -1f, -H, 0f, H], sin = [0f, H, 1f, H, 0f, -H, -1f, -H];
        float least = float.PositiveInfinity;
        for (int k = 0; k < 8; k++)
        {
            var n = e1 * cos[k] + e2 * sin[k];
            float most = 0f;
            foreach (var p in points) most = MathF.Max(most, Dot(p - origin, n));
            least = MathF.Min(least, most);
        }
        return least;
    }

    static float[] LineDistances(List<Vector3> points, Vector3 origin, Vector3 unit)
    {
        var d = new float[points.Count];
        for (int i = 0; i < d.Length; i++)
        {
            var r = points[i] - origin;
            d[i] = Length(r - unit * Dot(r, unit));
        }
        return d;
    }

    /// <summary>The value a share <paramref name="share"/> of the way up the sorted values (0 the least, 1 the most).</summary>
    internal static float Percentile(float[] values, float share)
    {
        if (values.Length == 0) return 0f;
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        return sorted[(int)MathF.Floor(share * (sorted.Length - 1))];
    }

    /// <summary>Where the origin of an affine matrix's frame is (the translation of its inverse), in scalar steps: −t × R⁻¹.</summary>
    internal static Vector3 AffineInverseTranslation(in Matrix4x4 m)
    {
        // R⁻¹ by cofactors (rows of R are the matrix's first three rows, row vectors).
        float a = m.M11, b = m.M12, c = m.M13, d = m.M21, e = m.M22, f = m.M23, g = m.M31, h = m.M32, k = m.M33;
        float A = e * k - f * h, B = -(d * k - f * g), C = d * h - e * g;
        float det = a * A + b * B + c * C;
        if (MathF.Abs(det) < 1e-20f) return Vector3.Zero;
        float inv = 1f / det;
        // R⁻¹ = adj(R) / det; adj's rows are the cofactors' columns.
        float i11 = A * inv, i12 = -(b * k - c * h) * inv, i13 = (b * f - c * e) * inv;
        float i21 = B * inv, i22 = (a * k - c * g) * inv, i23 = -(a * f - c * d) * inv;
        float i31 = C * inv, i32 = -(a * h - b * g) * inv, i33 = (a * e - b * d) * inv;
        float tx = m.M41, ty = m.M42, tz = m.M43;
        return new Vector3(-(tx * i11 + ty * i21 + tz * i31), -(tx * i12 + ty * i22 + tz * i32), -(tx * i13 + ty * i23 + tz * i33));
    }
}
