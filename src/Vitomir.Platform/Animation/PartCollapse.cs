// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: 1.1 had beheadings (a head model thrown off, Beheading.cs in the old engine) but no lost limbs.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Combat;

namespace Vitomir.Platform.Animation;

/// <summary>
/// A severed part drawn gone (the rules package's severance): the bones of a part a person
/// lacks (<see cref="MissingParts"/>, written by the rules) <b>collapsed at the joint above it</b>, as first person collapses
/// the head (<see cref="HeadCollapse"/>: scaled by a thousandth, never to zero) — in every view and in the shadow, and the
/// things held in it with them. One per skeleton the anatomy tables know (<see cref="AnatomyTables"/>), made once.
/// <para>
/// <b>The region map</b>: a part's <i>roots</i> are its bones whose parent is of another region (the hand's is the wrist's
/// bone; the forearm's the elbow's; the upper arm's the shoulder's; the neck's the neck's), and what collapses is every bone
/// under a lacked part's root — so a hand lost collapses the hand (and the weapon's mount on it); a forearm the forearm and the
/// hand (and the shield's mount on the forearm); an upper arm the whole arm; the neck the neck and the head. Legs the same
/// (the rules sever none yet). The <b>trunk</b> (chest, belly, hips) never collapses: cut through, the body is stopped by
/// the rules (<see cref="ActorStop.SeveredStop"/>), and a half falling away is the models repository's (deferred), as is any part
/// falling away; contact skips only the trunk's own lost volumes (<see cref="VolumeGone"/>).
/// </para>
/// </summary>
public sealed class PartCollapse
{
    readonly int[] parents;
    readonly BodyRegion[] regions;
    readonly int[] scratch;

    PartCollapse(int[] parents, BodyRegion[] regions)
    {
        this.parents = parents;
        this.regions = regions;
        scratch = new int[parents.Length];
    }

    /// <summary>How many bones the skeleton has.</summary>
    public int Bones => parents.Length;

    /// <summary>The collapse for <paramref name="skeleton"/>, or null when no anatomy table knows it (it then loses nothing drawn).</summary>
    public static PartCollapse? For(Skeleton skeleton)
    {
        if (AnatomyTables.For(skeleton) is not { } table) return null;
        var parents = new int[skeleton.Count];
        var regions = new BodyRegion[skeleton.Count];
        for (int b = 0; b < skeleton.Count; b++)
        {
            parents[b] = skeleton.Bones[b].Parent;
            regions[b] = table.Of(b);
        }
        return new PartCollapse(parents, regions);
    }

    /// <summary>Whether losing <paramref name="region"/> collapses it and what hangs from it (every region but the trunk's).</summary>
    public static bool Collapses(BodyRegion region) => region is not (BodyRegion.None or BodyRegion.Chest or BodyRegion.Belly or BodyRegion.Hips);

    /// <summary>The bone's region (<see cref="BodyRegion.None"/> beyond the skeleton).</summary>
    public BodyRegion RegionOf(int bone) => (uint)bone < (uint)regions.Length ? regions[bone] : BodyRegion.None;

    /// <summary>
    /// Whether <paramref name="bone"/> is gone with a part the person lacks (<paramref name="missing"/>, <see cref="MissingParts.Bits"/>):
    /// it, or a bone above it, is of a lacked region that collapses.
    /// </summary>
    public bool Gone(uint missing, int bone)
    {
        if (missing == 0) return false;
        for (int b = bone, guard = 0; (uint)b < (uint)parents.Length && guard < parents.Length; b = parents[b], guard++)
            if (Lacked(missing, regions[b])) return true;
        return false;
    }

    /// <summary>Whether a hit volume of <paramref name="region"/> on <paramref name="bone"/> is gone: its part lacked (the trunk's too), or a part above it.</summary>
    public bool VolumeGone(uint missing, BodyRegion region, int bone) =>
        missing != 0 && ((region != BodyRegion.None && (missing & (1u << (int)region)) != 0) || Gone(missing, bone));

    static bool Lacked(uint missing, BodyRegion r) => Collapses(r) && (missing & (1u << (int)r)) != 0;

    /// <summary>
    /// Collapses the lacked parts in a set of skin matrices (the character's own, or a held model's on the character's pose,
    /// <see cref="HeldItems.Skin"/>): each gone bone's matrix followed by a shrink to a thousandth about its part's root joint
    /// where the pose has put it (<paramref name="bones"/>, the pose's model-space bones). The other bones are not touched.
    /// Allocates nothing.
    /// </summary>
    public void Apply(uint missing, ReadOnlySpan<Matrix4x4> bones, Span<Matrix4x4> skin)
    {
        if (missing == 0) return;
        int n = Math.Min(parents.Length, bones.Length);
        // Each bone's collapsing root (−1 for none), parents first: the topmost lacked part's root wins.
        for (int b = 0; b < n; b++)
        {
            int p = parents[b];
            if (p >= 0 && p < b && scratch[p] >= 0) scratch[b] = scratch[p];
            else scratch[b] = Lacked(missing, regions[b]) ? b : -1;
        }
        for (int b = 0; b < n && b < skin.Length; b++)
        {
            int root = scratch[b];
            if (root < 0) continue;
            var pivot = bones[root].Translation;
            ref var m = ref skin[b];
            m.M11 *= HeadCollapse.Scale; m.M12 *= HeadCollapse.Scale; m.M13 *= HeadCollapse.Scale;
            m.M21 *= HeadCollapse.Scale; m.M22 *= HeadCollapse.Scale; m.M23 *= HeadCollapse.Scale;
            m.M31 *= HeadCollapse.Scale; m.M32 *= HeadCollapse.Scale; m.M33 *= HeadCollapse.Scale;
            m.M41 = (m.M41 - pivot.X) * HeadCollapse.Scale + pivot.X;
            m.M42 = (m.M42 - pivot.Y) * HeadCollapse.Scale + pivot.Y;
            m.M43 = (m.M43 - pivot.Z) * HeadCollapse.Scale + pivot.Z;
        }
    }
}
