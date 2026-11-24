// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the base's motions played on a person baked at its own proportions (the models repository).
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// One of the base's motions played on a person of other proportions — the models repository's baked crowd (
/// <i>"the motions are the base's, played on the person's own rest pose (local rotations); scale the pelvis's keyed height by
/// hip_height_m / base_hip_height_m, or let the foot IK take it"</i>). The rotations are the motion's own, each bone's length the
/// person's (its skeleton's rest, as <see cref="GltfClip"/> keeps it); the one bone the motions move, <c>pelvis</c>, has its keyed place
/// scaled by <see cref="Factor"/> — all three of its numbers, so the hips' sway and lean keep to the shorter or longer legs as their
/// height does — and the notes' ground speed and stride by as much (a leg <see cref="Factor"/> as long strides as far in the same
/// time), so everything that reads them (the gaits' blends, a styled walk, the sprint, the townsperson's pace) is drawn for the
/// person's own strides. The scale rather than the foot IK: the IK would bend the knees of a shorter person under hips at the base's
/// height, and straighten (or leave floating) a taller one's.
/// <para>A clip that keys no pelvis place (not one of the models repository's motions) is played as it is. Pure arithmetic in a fixed
/// order (a product of floats); allocates nothing.</para>
/// </summary>
public sealed class ProportionedClip : INotedClip
{
    readonly INotedClip inner;
    readonly bool keysPelvis;
    PelvisOf? last;   // a reference, so a pose made on another thread reads it whole

    /// <summary><paramref name="inner"/> with its pelvis's keyed place scaled by <paramref name="factor"/>.</summary>
    public ProportionedClip(INotedClip inner, float factor)
    {
        ArgumentNullException.ThrowIfNull(inner);
        this.inner = inner;
        Factor = factor > 0f && float.IsFinite(factor) ? factor : 1f;
        keysPelvis = inner is GltfClip g && g.MovedBones.Contains("pelvis", StringComparer.OrdinalIgnoreCase);
        Notes = inner.Notes with { GroundSpeed = inner.Notes.GroundSpeed * Factor, StrideM = inner.Notes.StrideM * Factor };
    }

    /// <summary>The scale of the pelvis's keyed place (the person's hips' height over its base's).</summary>
    public float Factor { get; }

    /// <summary>The motion as made.</summary>
    public INotedClip Inner => inner;

    public string Name => inner.Name;
    public float Duration => inner.Duration;
    public bool Loops => inner.Loops;
    public ClipNotes Notes { get; }

    public void Sample(float seconds, Skeleton skeleton, Span<BoneTransform> local)
    {
        inner.Sample(seconds, skeleton, local);
        if (!keysPelvis || Factor == 1f) return;
        int pelvis = Pelvis(skeleton);
        if ((uint)pelvis < (uint)local.Length) local[pelvis] = local[pelvis] with { Translation = local[pelvis].Translation * Factor };
    }

    /// <summary>The pelvis's index in <paramref name="skeleton"/>, found once a skeleton (a person's kinds share one).</summary>
    int Pelvis(Skeleton skeleton)
    {
        var have = last;
        if (have is not null && ReferenceEquals(have.Skeleton, skeleton)) return have.Pelvis;
        int pelvis = skeleton.Find("pelvis");
        last = new PelvisOf(skeleton, pelvis);
        return pelvis;
    }

    sealed record PelvisOf(Skeleton Skeleton, int Pelvis);

    /// <summary>
    /// The set <paramref name="motions"/> for a person of <paramref name="factor"/> (<see cref="CrowdPerson.Proportion"/>): each clip
    /// wrapped, in the set's order; the set itself when the factor is 1.
    /// </summary>
    public static IReadOnlyList<INotedClip> Set(IReadOnlyList<INotedClip> motions, float factor) =>
        factor == 1f || !(factor > 0f) ? motions : [.. motions.Select(c => (INotedClip)new ProportionedClip(c, factor))];
}
