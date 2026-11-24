// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Posing a carried model on its holder's bones is taken from src/Vitomir.Engine/Anim/ThingAnimation.cs (PoseCarried) at
// 6fbcf40, reworked for the platform: the carried model's own inverse rest against the holder's posed bone.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation;

/// <summary>What a held thing is, for contact: a blade (a segment with a radius) or a shield (an oriented box).</summary>
public enum HeldKind
{
    Blade,
    Shield,
}

/// <summary>
/// One held thing: the part of a held model's vertices skinned to one bone — 1.1's held models ride the bone their
/// vertices are weighted to, not the motion set's <c>Weapon</c> tag — and its shape in that bone's
/// own frame (the carried model's rest: <c>v × InverseBind[b]</c>): a blade from its guard to its tip with a radius, or a
/// shield's box. rudolfGuard's <c>rapier_shield_0</c> is one model and two things: the rapier on bone 25 and the shield on 30.
/// </summary>
public sealed record HeldThing(HeldKind Kind, int Bone, int Vertices, Vector3 Guard, Vector3 Tip, float Radius, Vector3 Min, Vector3 Max)
{
    /// <summary>
    /// A shield's own box in the bone's frame: its centre, its three axes (the vertices' principal
    /// directions) and half extents along them. The box along the bone's axes (<see cref="Min"/>, <see cref="Max"/>) was
    /// as deep as the kite is tilted — half a metre for rudolfGuard's — and caught most blows below the head unraised.
    /// </summary>
    public Vector3 BoxCentre { get; init; }
    public Vector3 BoxAxisX { get; init; } = Vector3.UnitX;
    public Vector3 BoxAxisY { get; init; } = Vector3.UnitY;
    public Vector3 BoxAxisZ { get; init; } = Vector3.UnitZ;
    public Vector3 BoxHalf { get; init; }

    /// <summary>The blade's length from its guard to its tip (m).</summary>
    public float Length => LimbIK.Length(Tip - Guard);
}

/// <summary>
/// A model a character holds: 1.1's <c>WEAPON_MODEL_SKELETONID</c> (or a shield's <c>ARMOR_MODEL_SKELETONID</c>),
/// which carries the holder's skeleton and is posed through its own inverse rest against the holder's posed bones, and the
/// things it is (<see cref="Fit"/>). Drawn as a second model on the holder's pose; placed for contact by <see cref="HeldItems"/>.
/// </summary>
public sealed class HeldModel
{
    /// <summary>1.1's template it came from (<c>BroadSword</c>, <c>weapRapier_shield</c>).</summary>
    public required string Template { get; init; }
    public required SkinnedModel Model { get; init; }
    public required IReadOnlyList<HeldThing> Things { get; init; }

    /// <summary>The fewest vertices on one bone that make a thing (a few stray vertices on another bone are part of the model, not a thing).</summary>
    public const int LeastVertices = 8;

    /// <summary>A blade is at least this many times longer than it is wide; anything squarer is a shield (ours, SANDBOX).</summary>
    public const float BladeElongation = 2.5f;

    /// <summary>
    /// The things of a held model (ours): its vertices grouped by the bone each is (mostly) weighted to; each group of
    /// at least <see cref="LeastVertices"/> is a thing, a blade when its span's longest side is at least
    /// <see cref="BladeElongation"/> times its next (a sword, a dagger, an axe, a mace, a pole), else a shield. A blade's
    /// tip is its vertex furthest from the bone (the hand); its guard the middle of the widest slice across the haft
    /// between the hand and nearly half way out (a sword's cross-guard; for a pole, where it widens); its radius the 90th
    /// percentile of the blade's vertices' distances from the guard-to-tip segment. A shield is its box in the bone's frame.
    /// </summary>
    public static HeldModel Fit(string template, SkinnedModel model)
    {
        var skeleton = model.Skeleton;
        var byBone = new SortedDictionary<int, List<Vector3>>();
        foreach (var v in model.Vertices)
        {
            int b = v.Bone(0);
            if (v.Weights.X < 0.5f || (uint)b >= (uint)skeleton.Count) continue;
            if (!byBone.TryGetValue(b, out var list)) byBone[b] = list = [];
            list.Add(PoseMath.Transform(v.Position, skeleton.InverseBind[b]));
        }
        var things = new List<HeldThing>();
        foreach (var (bone, points) in byBone)
        {
            if (points.Count < LeastVertices) continue;
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            foreach (var p in points) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            var span = max - min;
            float[] sides = [span.X, span.Y, span.Z];
            Array.Sort(sides);
            bool blade = sides[1] > 1e-4f && sides[2] / sides[1] >= BladeElongation;
            if (!blade)
            {
                var (centre, ax, ay, az, half) = HeldItems.OwnBox(points);
                things.Add(new HeldThing(HeldKind.Shield, bone, points.Count, default, default, 0f, min, max)
                    { BoxCentre = centre, BoxAxisX = ax, BoxAxisY = ay, BoxAxisZ = az, BoxHalf = half });
                continue;
            }

            var tip = points[0];
            foreach (var p in points) if (LimbIK.Dot(p, p) > LimbIK.Dot(tip, tip)) tip = p;
            float reach = LimbIK.Length(tip);
            var axis = reach > 1e-5f ? tip / reach : Vector3.UnitY;
            // The widest slice across the haft, 2 cm thick, from just behind the hand to 45 % of the way out.
            const float slice = 0.02f;
            int bins = Math.Max(1, (int)MathF.Ceiling((0.45f * reach + 0.05f) / slice));
            var widest = new float[bins];
            var sums = new Vector3[bins];
            var counts = new int[bins];
            foreach (var p in points)
            {
                float s = LimbIK.Dot(p, axis);
                int i = (int)MathF.Floor((s + 0.05f) / slice);
                if (i < 0 || i >= bins) continue;
                float r = LimbIK.Length(p - axis * s);
                widest[i] = MathF.Max(widest[i], r);
                sums[i] += p;
                counts[i]++;
            }
            int best = -1;
            for (int i = 0; i < bins; i++) if (counts[i] > 0 && (best < 0 || widest[i] > widest[best])) best = i;
            var guard = best >= 0 ? sums[best] / counts[best] : Vector3.Zero;
            float guardS = LimbIK.Dot(guard, axis);
            var distances = new List<float>();
            foreach (var p in points)
                if (LimbIK.Dot(p, axis) > guardS + 0.03f) distances.Add(DistanceToSegment(p, guard, tip));
            distances.Sort();
            float radius = distances.Count == 0 ? 0.02f : MathF.Max(0.01f, distances[(int)MathF.Floor(0.9f * (distances.Count - 1))]);
            things.Add(new HeldThing(HeldKind.Blade, bone, points.Count, guard, tip, radius, min, max));
        }
        return new HeldModel { Template = template, Model = model, Things = things };
    }

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float l = LimbIK.Dot(ab, ab);
        float t = l > 0f ? Math.Clamp(LimbIK.Dot(p - a, ab) / l, 0f, 1f) : 0f;
        return LimbIK.Length(p - (a + ab * t));
    }
}

/// <summary>A blade where it is (world, m): its guard, its tip and its radius. Contact sweeps this next.</summary>
public readonly record struct BladePlacement(Vector3 Guard, Vector3 Tip, float Radius)
{
    public float Length => LimbIK.Length(Tip - Guard);
}

/// <summary>A shield where it is (world, m): an oriented box — its centre, its three unit axes and its half sizes along them.</summary>
public readonly record struct BoxPlacement(Vector3 Centre, Vector3 AxisX, Vector3 AxisY, Vector3 AxisZ, Vector3 HalfSize);

/// <summary>One held thing placed: which kind, its blade (for a blade) or its box (for a shield), and the model and thing it is.</summary>
public readonly record struct HeldPlacement(HeldKind Kind, BladePlacement Blade, BoxPlacement Box, int Model, int Thing);

/// <summary>
/// Where a character's held things are on its pose as the simulation stands now (ours): the carrying bone's posed
/// matrix (<see cref="CharacterPoses.Bones"/>) times the body's placement (<see cref="CharacterKind.Placement"/>), with
/// the thing's shape in that bone's frame. Run in <see cref="Phase.Contact"/> it is this tick's pose with this tick's body
/// (the animation is posed before contact). <see cref="PoseMath"/>'s and <see cref="LimbIK"/>'s scalar arithmetic only,
/// as the pose (no vectorised products), and so are the shapes <see cref="HeldModel.Fit"/> finds.
/// </summary>
public static class HeldItems
{
    /// <summary>The kind's held models, or none (a character without an animation, or holding nothing).</summary>
    public static IReadOnlyList<HeldModel> Of(World world, EntityId e)
    {
        if (!world.TryService<CharacterAssets>(out var assets)) return [];
        ref var anim = ref world.Store<CharacterAnimation>().Find(e);
        return Unsafe.IsNullRef(ref anim) ? [] : assets.Kind(anim.Kind).Held;
    }

    /// <summary>The body's placement in the world (its feet and yaw), or false when it has no body or animation.</summary>
    public static bool Placement(World world, EntityId e, out Matrix4x4 place)
    {
        place = Matrix4x4.Identity;
        if (!world.TryService<CharacterAssets>(out var assets)) return false;
        ref var anim = ref world.Store<CharacterAnimation>().Find(e);
        ref var body = ref world.Store<Body>().Find(e);
        if (Unsafe.IsNullRef(ref anim) || Unsafe.IsNullRef(ref body)) return false;
        place = assets.Kind(anim.Kind).Placement(body.Feet, body.Yaw);
        return true;
    }

    /// <summary>A bone's posed matrix in the world (the pose's model space times the placement).</summary>
    public static bool BoneInWorld(World world, EntityId e, int bone, out Matrix4x4 m)
    {
        m = Matrix4x4.Identity;
        if (!Placement(world, e, out var place) || !world.TryService<CharacterPoses>(out var poses)) return false;
        var bones = poses.Bones(world, e);
        if ((uint)bone >= (uint)bones.Length) return false;
        m = PoseMath.Multiply(bones[bone], place);
        return true;
    }

    /// <summary>
    /// The box of <paramref name="points"/> along their own principal directions: the covariance's
    /// eigenvectors by Jacobi rotations (a fixed number of sweeps: the same answer every time), then the points' extent along
    /// each. For a shield, one axis is its thin side.
    /// </summary>
    internal static (Vector3 Centre, Vector3 X, Vector3 Y, Vector3 Z, Vector3 Half) OwnBox(List<Vector3> points)
    {
        var mean = Vector3.Zero;
        foreach (var p in points) mean += p;
        mean /= points.Count;
        double[,] c = new double[3, 3];
        foreach (var p in points)
        {
            var d = p - mean;
            double[] v = [d.X, d.Y, d.Z];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) c[i, j] += v[i] * v[j];
        }
        double[,] e = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int sweep = 0; sweep < 24; sweep++)
            for (int pI = 0; pI < 2; pI++)
                for (int qI = pI + 1; qI < 3; qI++)
                {
                    if (Math.Abs(c[pI, qI]) < 1e-15) continue;
                    double theta = (c[qI, qI] - c[pI, pI]) / (2 * c[pI, qI]);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    double cs = 1 / Math.Sqrt(t * t + 1), sn = t * cs;
                    for (int k = 0; k < 3; k++)
                    {
                        double ckp = c[k, pI], ckq = c[k, qI];
                        c[k, pI] = cs * ckp - sn * ckq; c[k, qI] = sn * ckp + cs * ckq;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double cpk = c[pI, k], cqk = c[qI, k];
                        c[pI, k] = cs * cpk - sn * cqk; c[qI, k] = sn * cpk + cs * cqk;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double ekp = e[k, pI], ekq = e[k, qI];
                        e[k, pI] = cs * ekp - sn * ekq; e[k, qI] = sn * ekp + cs * ekq;
                    }
                }
        var axes = new Vector3[3];
        for (int k = 0; k < 3; k++) axes[k] = Vector3.Normalize(new Vector3((float)e[0, k], (float)e[1, k], (float)e[2, k]));
        axes[2] = Vector3.Normalize(Vector3.Cross(axes[0], axes[1]));   // right-handed
        axes[1] = Vector3.Cross(axes[2], axes[0]);
        var lo = new Vector3(float.MaxValue); var hi = new Vector3(float.MinValue);
        foreach (var p in points)
        {
            var d = p - mean;
            var q = new Vector3(Vector3.Dot(d, axes[0]), Vector3.Dot(d, axes[1]), Vector3.Dot(d, axes[2]));
            lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q);
        }
        var mid = (lo + hi) * 0.5f;
        var centre = mean + axes[0] * mid.X + axes[1] * mid.Y + axes[2] * mid.Z;
        return (centre, axes[0], axes[1], axes[2], (hi - lo) * 0.5f);
    }

    /// <summary>One thing placed (<paramref name="model"/> of the kind's held models, its <paramref name="thing"/>).</summary>
    public static bool Place(World world, EntityId e, int model, int thing, out HeldPlacement placed)
    {
        placed = default;
        var held = Of(world, e);
        if ((uint)model >= (uint)held.Count || (uint)thing >= (uint)held[model].Things.Count) return false;
        var t = held[model].Things[thing];
        if (Dropped(world, e, t.Bone)) return false;   // held in a part the person lost: dropped
        if (!BoneInWorld(world, e, t.Bone, out var m)) return false;
        if (t.Kind == HeldKind.Blade)
        {
            float scale = LimbIK.Length(PoseMath.TransformNormal(Vector3.UnitX, m));
            placed = new HeldPlacement(HeldKind.Blade, new BladePlacement(PoseMath.Transform(t.Guard, m), PoseMath.Transform(t.Tip, m), t.Radius * scale), default, model, thing);
        }
        else
        {
            // The shield's own box (its principal axes), not the bone's.
            var x = PoseMath.TransformNormal(t.BoxAxisX, m); var y = PoseMath.TransformNormal(t.BoxAxisY, m); var z = PoseMath.TransformNormal(t.BoxAxisZ, m);
            float sx = LimbIK.Length(x), sy = LimbIK.Length(y), sz = LimbIK.Length(z);
            var half = t.BoxHalf;
            var box = new BoxPlacement(PoseMath.Transform(t.BoxCentre, m), x / sx, y / sy, z / sz, new Vector3(half.X * sx, half.Y * sy, half.Z * sz));
            placed = new HeldPlacement(HeldKind.Shield, default, box, model, thing);
        }
        return true;
    }

    /// <summary>The first blade the character holds, placed; false when it holds none.</summary>
    public static bool Blade(World world, EntityId e, out BladePlacement blade)
    {
        blade = default;
        if (!Find(world, e, HeldKind.Blade, out int m, out int t) || !Place(world, e, m, t, out var p)) return false;
        blade = p.Blade;
        return true;
    }

    /// <summary>The first shield the character holds, placed; false when it holds none.</summary>
    public static bool Shield(World world, EntityId e, out BoxPlacement box)
    {
        box = default;
        if (!Find(world, e, HeldKind.Shield, out int m, out int t) || !Place(world, e, m, t, out var p)) return false;
        box = p.Box;
        return true;
    }

    /// <summary>Whether the character holds a shield (placed or not; allocates nothing).</summary>
    public static bool HasShield(World world, EntityId e) => Find(world, e, HeldKind.Shield, out _, out _);

    /// <summary>Every held thing placed, into <paramref name="into"/>; answers how many.</summary>
    public static int All(World world, EntityId e, Span<HeldPlacement> into)
    {
        var held = Of(world, e);
        int n = 0;
        for (int m = 0; m < held.Count; m++)
            for (int t = 0; t < held[m].Things.Count && n < into.Length; t++)
                if (Place(world, e, m, t, out var p)) into[n++] = p;
        return n;
    }

    /// <summary>
    /// The blow's point: the first blade's tip, or (bare hands) the weapon hand's bone (<see cref="ActionSet.WeaponHand"/>);
    /// false when neither is known.
    /// </summary>
    public static bool Point(World world, EntityId e, out Vector3 point)
    {
        point = default;
        if (Blade(world, e, out var blade)) { point = blade.Tip; return true; }
        if (!world.TryService<CharacterAssets>(out var assets)) return false;
        ref var anim = ref world.Store<CharacterAnimation>().Find(e);
        if (Unsafe.IsNullRef(ref anim) || assets.Kind(anim.Kind).Actions is not { WeaponHand: >= 0 } set) return false;
        if (Dropped(world, e, set.WeaponHand)) return false;   // no weapon hand, no blow's point
        if (!BoneInWorld(world, e, set.WeaponHand, out var m)) return false;
        point = m.Translation;
        return true;
    }

    /// <summary>
    /// Whether what <paramref name="e"/> holds on <paramref name="bone"/> is <b>dropped</b> — the bone gone with a part the
    /// person lost (<see cref="Combat.MissingParts"/>, <see cref="CharacterPoses.Gone"/>): hidden (its skin collapsed with the
    /// part, <see cref="CharacterPoses.CopyHeldSkin"/>) and no longer the person's weapon or shield (not placed, so contact meets
    /// it no more and the attacks refuse a person without the weapon hand). The models repository's falling item is deferred.
    /// </summary>
    public static bool Dropped(World world, EntityId e, int bone) =>
        world.TryService<CharacterPoses>(out var poses) && poses.Gone(world, e, bone);

    /// <summary>Whether <paramref name="e"/>'s weapon hand is gone (its <see cref="ActionSet.WeaponHand"/> bone, or its first blade's bone).</summary>
    public static bool WeaponHandGone(World world, EntityId e)
    {
        if (!world.TryService<CharacterAssets>(out var assets) || !world.TryService<CharacterPoses>(out var poses)) return false;
        if (poses.Missing(world, e) == 0) return false;
        ref var anim = ref world.Store<CharacterAnimation>().Find(e);
        if (Unsafe.IsNullRef(ref anim)) return false;
        var kind = assets.Kind(anim.Kind);
        if (kind.Actions is { WeaponHand: >= 0 } set && poses.Gone(world, e, set.WeaponHand)) return true;
        if (Find(world, e, HeldKind.Blade, out int m, out int t) && poses.Gone(world, e, kind.Held[m].Things[t].Bone)) return true;
        return false;
    }

    static bool Find(World world, EntityId e, HeldKind kind, out int model, out int thing)
    {
        var held = Of(world, e);
        for (model = 0; model < held.Count; model++)
            for (thing = 0; thing < held[model].Things.Count; thing++)
                if (held[model].Things[thing].Kind == kind) return true;
        model = thing = -1;
        return false;
    }

    /// <summary>
    /// A held model's skin matrices on its holder's pose (1.1's <c>PoseCarried</c>): for each bone, the carried model's own
    /// inverse rest times the holder's posed bone (model space); a bone past the holder's last is the identity.
    /// </summary>
    public static void Skin(SkinnedModel carried, ReadOnlySpan<Matrix4x4> holderBones, Span<Matrix4x4> skin)
    {
        var inverse = carried.Skeleton.InverseBind;
        int n = Math.Min(inverse.Length, holderBones.Length);
        for (int b = 0; b < n; b++) skin[b] = PoseMath.Multiply(inverse[b], holderBones[b]);
        for (int b = n; b < inverse.Length && b < skin.Length; b++) skin[b] = Matrix4x4.Identity;
    }
}
