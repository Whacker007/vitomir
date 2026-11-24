// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours, on what play showed (the clothing still went a bit crazy; then, in an earlier build's video, going up and down the stairs, the
// cloth around the legs got very wild): the townsman's tunic, the watchman's and the guard's hauberk hang their skirts on swing bones from
// the two thighs (the character models' swing bones), and the platform leaves swing bones at rest (it does not run their simulator),
// so each half of the skirt went rigidly with its own thigh: lifted out as a flat panel by a raised thigh, the hem torn open between the halves.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>a skirt hung from the thighs, draped</b>. The models repository hangs a close skirt (the townsman's and the watchman's tunic, the guard's
/// hauberk) on 16 swing chains of 5 bones whose roots are children of <c>thigh_l</c> (its left half) and <c>thigh_r</c> (its right half), for its
/// simulator (VRM's spring bone: each bone pulled toward the way it hangs at rest, kept out of the legs' capsules, the hem held together by a
/// web). The platform does not run the simulator — the swing bones stay at rest — so each half went with its own thigh as a rigid plate: a thigh
/// raised by the sprint (113°), the crouch (122°) or the foot's IK on a stair (105° up 0.25 m risers) lifted its half out level like a tray, and
/// the hem opened between the halves (on an earlier build's tape the front seam, 6 cm at rest, up to 14 times as long).
/// <para>
/// The rule is the simulator's resting state, without its swing: each chain's root is carried by the <b>pelvis</b> as it is at rest, every bone
/// hangs the way it hangs at rest (in the pelvis's frame: gravity outweighs the chains' stiffness, 9.8 to 1.5), and a bone whose end would be
/// inside a leg (the thigh and the calf as capsules, the record's radii and the chain's 3.5 cm) is turned about its head just enough to lie on
/// it, its chain below following. So the skirt hangs over a raised knee and falls from it, and neighbouring chains stay together. Stateless,
/// after everything that moves the legs (the IK, the legs' turn, the bend rule), every pose; only swing bones move, which no hit volume rides. Not
/// the simulator: no swing, no lag, no web. <see cref="CharacterAssets.PersonSkirtSplit"/> (<c>--skirt-split</c>) turns it off.
/// </para>
/// </summary>
public sealed class SkirtRule
{
    /// <summary>The legs' capsules the chains are kept out of: the thigh's (its joint to the knee's) and the calf's (the knee's to the ankle's), each side; their radii (m), the chains' own added.</summary>
    public const float ThighRadius = 0.067f, CalfRadius = 0.052f, ChainRadius = 0.035f;

    /// <summary>
    /// One chain: its bones from the root (the root a child of <see cref="Thigh"/>), each one's rest in the pelvis's frame, its length (to the next;
    /// the last as its parent's) and the least room it is given from each leg capsule (the record's, or less where the fit has it nearer at rest).
    /// </summary>
    public sealed record Chain(int Thigh, int[] Bones, BoneTransform[] Rest, Vector3[] Ways, float[] Lengths, float[] Room);

    /// <summary>The chains hung from a thigh.</summary>
    public required Chain[] Chains { get; init; }

    /// <summary>The pelvis, and each side's thigh, calf and foot (left, then right).</summary>
    public required int Hips { get; init; }
    public required int[] Legs { get; init; }

    /// <summary>The bones a chain has (every chain alike), the chains in order round the body, and each one's bones' ends' distance from the next one's at rest (m; chain by level).</summary>
    public int Levels { get; init; }
    public int[] Ring { get; init; } = [];
    public float[] WebRest { get; init; } = [];

    /// <summary>Each chain's last bone (its hem), in order round the body, and the distance from each to the next at rest (m).</summary>
    public int[] Hem { get; init; } = [];
    public float[] HemRest { get; init; } = [];

    /// <summary>
    /// The rule for <paramref name="skeleton"/>: every swing chain (<c>swing_…</c>) whose root is a child of <c>thigh_l</c> or <c>thigh_r</c>, the
    /// thighs children of one bone (the pelvis); null for a body without one (no skirt, a long skirt hung from the hips).
    /// </summary>
    public static SkirtRule? For(Skeleton skeleton)
    {
        int[] legs = [skeleton.Find("thigh_l"), skeleton.Find("calf_l"), skeleton.Find("foot_l"), skeleton.Find("thigh_r"), skeleton.Find("calf_r"), skeleton.Find("foot_r")];
        if (legs.Any(b => b < 0)) return null;
        var bones = skeleton.Bones;
        int hips = bones[legs[0]].Parent;
        if (hips < 0 || bones[legs[3]].Parent != hips || bones[legs[1]].Parent != legs[0] || bones[legs[4]].Parent != legs[3]
            || bones[legs[2]].Parent != legs[1] || bones[legs[5]].Parent != legs[4]) return null;
        int n = skeleton.Count;
        var rest = new BoneTransform[n];
        for (int b = 0; b < n; b++) rest[b] = bones[b].Rest;
        var capA = new Vector3[4];
        var capB = new Vector3[4];
        Capsules(rest, legs, capA, capB);
        var chains = new List<Chain>();
        var hems = new List<(int Bone, float Round)>();
        var model = new Matrix4x4[n];
        PoseEvaluator.ToModel(skeleton, rest, model);
        for (int b = 0; b < n; b++)
        {
            int p = bones[b].Parent;
            if ((p != legs[0] && p != legs[3]) || !bones[b].Name.StartsWith("swing_", StringComparison.Ordinal)) continue;
            // The chain: each next the swing bone under the last.
            var list = new List<int> { b };
            for (bool more = true; more;)
            {
                more = false;
                for (int d = list[^1] + 1; d < n; d++)
                    if (bones[d].Parent == list[^1] && bones[d].Name.StartsWith("swing_", StringComparison.Ordinal)) { list.Add(d); more = true; break; }
            }
            var inHips = new BoneTransform[list.Count];
            inHips[0] = Compose(rest[b], rest[p]);
            for (int k = 1; k < list.Count; k++) inHips[k] = Compose(rest[list[k]], inHips[k - 1]);
            var ways = new Vector3[list.Count];
            var lengths = new float[list.Count];
            var room = new float[list.Count * 4];
            for (int k = 0; k < list.Count; k++)
            {
                var d = k + 1 < list.Count ? inHips[k + 1].Translation - inHips[k].Translation : (k > 0 ? ways[k - 1] * lengths[k - 1] : Vector3.Zero);
                lengths[k] = d.Length();
                ways[k] = lengths[k] > 1e-6f ? d / lengths[k] : -Vector3.UnitZ;
                var tail = inHips[k].Translation + ways[k] * lengths[k];
                for (int c = 0; c < 4; c++)
                {
                    float nominal = (c % 2 == 0 ? ThighRadius : CalfRadius) + ChainRadius;
                    room[k * 4 + c] = MathF.Max(0f, MathF.Min(nominal, Distance(tail, capA[c], capB[c]) - 1e-3f));
                }
            }
            chains.Add(new Chain(p, [.. list], inHips, ways, lengths, room));
            var hem = model[list[^1]].Translation - model[hips].Translation;
            hems.Add((list[^1], DetMath.Atan2(hem.X, hem.Y)));
        }
        if (!chains.Any(c => c.Thigh == legs[0]) || !chains.Any(c => c.Thigh == legs[3])) return null;
        var ring = hems.OrderBy(h => h.Round).Select(h => h.Bone).ToArray();
        var gaps = new float[ring.Length];
        for (int k = 0; k < ring.Length; k++) gaps[k] = Vector3.Distance(model[ring[k]].Translation, model[ring[(k + 1) % ring.Length]].Translation);
        // The web: the chains round the body (by their hems), each level's ends' rest distance to the next chain's.
        int levels = chains.Min(c => c.Bones.Length);
        var order = Enumerable.Range(0, chains.Count).OrderBy(c => hems[c].Round).ToArray();
        bool alike = chains.All(c => c.Bones.Length == levels);
        var web = new float[chains.Count * levels];
        for (int r = 0; r < order.Length; r++)
        {
            var (ci, cj) = (chains[order[r]], chains[order[(r + 1) % order.Length]]);
            for (int k = 0; k < levels; k++)
                web[r * levels + k] = Vector3.Distance(ci.Rest[k].Translation + ci.Ways[k] * ci.Lengths[k], cj.Rest[k].Translation + cj.Ways[k] * cj.Lengths[k]);
        }
        return new SkirtRule { Chains = [.. chains], Hips = hips, Legs = legs, Hem = ring, HemRest = gaps, Levels = levels, Ring = alike ? order : [], WebRest = web };
    }

    /// <summary>Drapes each chain in <paramref name="local"/> (the pose's local transforms, the legs as posed). Allocates nothing.</summary>
    public void Apply(Span<BoneTransform> local)
    {
        if (local.Length <= Hips) return;
        foreach (int leg in Legs) if (leg >= local.Length) return;
        foreach (var chain in Chains) if ((uint)chain.Bones[^1] >= (uint)local.Length) return;
        Span<Vector3> a = stackalloc Vector3[4];
        Span<Vector3> b = stackalloc Vector3[4];
        Capsules(local, Legs, a, b);
        int count = Chains.Length, levels = Levels + 1;
        Span<Vector3> at = count * levels <= 256 ? stackalloc Vector3[count * levels] : new Vector3[count * levels];
        for (int c = 0; c < count; c++) at[c * levels] = Chains[c].Rest[0].Translation;
        // Level by level: each bone hangs as at rest, kept out of the legs; then the web holds neighbours to WebStretch of their rest, and the legs again.
        for (int k = 0; k < Levels; k++)
        {
            for (int c = 0; c < count; c++)
            {
                var chain = Chains[c];
                at[c * levels + k + 1] = Out(chain, k, at[c * levels + k] + chain.Ways[k] * chain.Lengths[k], a, b);
            }
            if (Ring.Length != count) continue;
            for (int pass = 0; pass < WebPasses; pass++)
            {
                for (int r = 0; r < count; r++)
                {
                    int i = Ring[r] * levels + k + 1, j = Ring[(r + 1) % count] * levels + k + 1;
                    var d = at[j] - at[i];
                    float l = d.Length(), most = WebStretch * WebRest[r * Levels + k];
                    if (l <= most || l < 1e-6f) continue;
                    var pull = d * (0.5f * (l - most) / l);
                    at[i] += pull;
                    at[j] -= pull;
                }
                for (int c = 0; c < count; c++)
                {
                    var chain = Chains[c];
                    var head = at[c * levels + k];
                    var t = at[c * levels + k + 1] - head;
                    float tl = t.Length();
                    var tail = tl > 1e-6f ? head + t * (chain.Lengths[k] / tl) : head + chain.Ways[k] * chain.Lengths[k];
                    at[c * levels + k + 1] = Out(chain, k, tail, a, b);
                }
            }
        }
        // The bones from their places: each turned from its rest way to its own, its chain under it following.
        for (int c = 0; c < count; c++)
        {
            var chain = Chains[c];
            var parent = local[chain.Thigh];   // the root's parent, in the pelvis's frame
            for (int k = 0; k < Levels; k++)
            {
                var head = at[c * levels + k];
                var t = at[c * levels + k + 1] - head;
                float tl = t.Length();
                var q = tl > 1e-6f ? LimbIK.FromTo(chain.Ways[k], t / tl) : Quaternion.Identity;
                var mine = new BoneTransform(LimbIK.Normalize(PoseMath.Multiply(q, chain.Rest[k].Rotation)), head);
                var back = Compose(mine, Inverse(parent));
                local[chain.Bones[k]] = new BoneTransform(LimbIK.Normalize(back.Rotation), back.Translation);
                parent = mine;
            }
        }
    }

    /// <summary>How long the web lets neighbouring chains' bones be from each other (times their rest; the record's 1.15), and how many passes hold it.</summary>
    public const float WebStretch = 1.15f;
    public const int WebPasses = 3;

    /// <summary>A bone's end <paramref name="tail"/> put out of the legs' capsules, each by the chain's room there, its length from its head kept by the caller.</summary>
    static Vector3 Out(Chain chain, int k, Vector3 tail, ReadOnlySpan<Vector3> a, ReadOnlySpan<Vector3> b)
    {
        for (int c = 0; c < 4; c++)
        {
            float room = chain.Room[k * 4 + c];
            if (room <= 0f) continue;
            var near = Nearest(tail, a[c], b[c]);
            var off = tail - near;
            float d = off.Length();
            if (d >= room) continue;
            tail = d > 1e-6f ? near + off * (room / d) : near + Vector3.UnitY * room;
        }
        return tail;
    }

    /// <summary>How far the hem is stretched in a pose (<paramref name="model"/>, the bones' model matrices): the largest distance between neighbouring chains' hems over its rest.</summary>
    public float Stretch(ReadOnlySpan<Matrix4x4> model)
    {
        float most = 0f;
        for (int k = 0; k < Hem.Length; k++)
        {
            int a = Hem[k], b = Hem[(k + 1) % Hem.Length];
            if ((uint)a >= (uint)model.Length || (uint)b >= (uint)model.Length || HemRest[k] <= 1e-6f) continue;
            most = MathF.Max(most, Vector3.Distance(model[a].Translation, model[b].Translation) / HemRest[k]);
        }
        return most;
    }

    /// <summary>
    /// How far each chain's bones' ends are inside the legs' capsules in a pose (<paramref name="model"/>), the deepest (m; 0 when none is): the
    /// thigh and the calf, the record's radii without the chain's own.
    /// </summary>
    public float Deepest(ReadOnlySpan<Matrix4x4> model)
    {
        float most = 0f;
        for (int c = 0; c < 4; c++)
        {
            int side = c / 2;
            var from = model[Legs[side * 3 + c % 2]].Translation;
            var to = model[Legs[side * 3 + c % 2 + 1]].Translation;
            float radius = c % 2 == 0 ? ThighRadius : CalfRadius;
            foreach (var chain in Chains)
                for (int k = 1; k < chain.Bones.Length; k++)
                {
                    // A bone's head is its parent's end: tested where the fit leaves it the record's room at rest (the skirt's top lies on the hips).
                    if (chain.Room[(k - 1) * 4 + c] < radius + ChainRadius - 2e-3f) continue;
                    most = MathF.Max(most, radius - Distance(model[chain.Bones[k]].Translation, from, to));
                }
        }
        return most;
    }

    /// <summary>The legs' four capsules in the pelvis's frame (the left thigh, the left calf, the right thigh, the right calf) from a pose's locals.</summary>
    static void Capsules(ReadOnlySpan<BoneTransform> local, int[] legs, Span<Vector3> a, Span<Vector3> b)
    {
        for (int side = 0; side < 2; side++)
        {
            var thigh = local[legs[side * 3]];
            var calf = Compose(local[legs[side * 3 + 1]], thigh);
            var foot = Compose(local[legs[side * 3 + 2]], calf);
            (a[side * 2], b[side * 2]) = (thigh.Translation, calf.Translation);
            (a[side * 2 + 1], b[side * 2 + 1]) = (calf.Translation, foot.Translation);
        }
    }

    static Vector3 Nearest(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float l = Vector3.Dot(ab, ab);
        float t = l > 1e-12f ? Math.Clamp(Vector3.Dot(p - a, ab) / l, 0f, 1f) : 0f;
        return a + ab * t;
    }

    static float Distance(Vector3 p, Vector3 a, Vector3 b) => Vector3.Distance(p, Nearest(p, a, b));

    /// <summary><paramref name="a"/> in <paramref name="b"/>'s frame (row vectors: a, then b), as one transform.</summary>
    static BoneTransform Compose(in BoneTransform a, in BoneTransform b) =>
        new(PoseMath.Multiply(b.Rotation, a.Rotation), Turn(a.Translation, b.Rotation) + b.Translation);

    static BoneTransform Inverse(in BoneTransform a)
    {
        var r = LimbIK.Conjugate(a.Rotation);
        return new BoneTransform(r, -Turn(a.Translation, r));
    }

    static Vector3 Turn(Vector3 v, Quaternion q) => PoseMath.TransformNormal(v, PoseMath.ToMatrix(new BoneTransform(q, Vector3.Zero)));
}
