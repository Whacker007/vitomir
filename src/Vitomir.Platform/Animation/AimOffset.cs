// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation;

/// <summary>
/// The aim offset's numbers (<i>"how to swing a sword at a head, or leg, or something other than the
/// normal mid torso level"</i>). All SANDBOX: looked at by eye on Christof, until real motions or the rules package
/// give a body's reach.
/// </summary>
public sealed record AimOffsetSettings
{
    /// <summary>The most the upper body bends back to swing high (radians): 25°.</summary>
    public float MostUp { get; init; } = 25f * DetMath.DegToRad;

    /// <summary>The most the upper body bends forwards to swing low (radians): 40° (a body bends further forwards than back).</summary>
    public float MostDown { get; init; } = 40f * DetMath.DegToRad;

    /// <summary>How fast the bend follows its aim and eases in and out (radians a second): about 0.15 s to the full bend.</summary>
    public float Rate { get; init; } = 4f;

    /// <summary>The height (m) over the feet the third-person aim is measured from: the chest, where a level swing lands.</summary>
    public float ChestHeight { get; init; } = 1.3f;

    /// <summary>The least distance (m, over the ground) the aim is measured over, so a point under the body does not swing it straight down.</summary>
    public float LeastReach { get; init; } = 0.75f;
}

/// <summary>
/// The aim offset (ours; Unreal's aim offsets, the swings of Chivalry and Mordhau that follow the camera): while an
/// attack or the block plays, the upper body is bent by a pitch about the body's right axis (the model's +X, a 1.1
/// character facing +Y), split evenly over the spine bones (14, 15 and 16 on skeleton 0), after the motion is sampled —
/// so the swing's own arc is kept and carried up or down with the chest, and the blade at the <c>Strike</c> lands higher
/// or lower. Up is positive, as the view's pitch.
/// </summary>
public static class AimOffset
{
    /// <summary>The pitch clamped to what the body can reach.</summary>
    public static float Clamp(float pitch, AimOffsetSettings s) => Math.Clamp(pitch, -s.MostDown, s.MostUp);

    /// <summary>The pitch from the chest of a body standing at <paramref name="feet"/> to <paramref name="point"/> (third person: the reticle's point).</summary>
    public static float Toward(Vector3 feet, Vector3 point, AimOffsetSettings s)
    {
        float dx = point.X - feet.X, dy = point.Y - feet.Y;
        float across = MathF.Max(MathF.Sqrt(dx * dx + dy * dy), s.LeastReach);
        return DetMath.Atan2(point.Z - (feet.Z + s.ChestHeight), across);
    }

    /// <summary>
    /// Bends <paramref name="local"/> by <paramref name="pitch"/> radians over <paramref name="spine"/>: each bone turned by its
    /// share about the model's +X as its parent sees it, so the turns add up to one turn of the chest about the body's right.
    /// </summary>
    public static void Apply(Skeleton skeleton, ReadOnlySpan<int> spine, float pitch, Span<BoneTransform> local)
    {
        if (pitch == 0f || spine.Length == 0) return;
        var (s, c) = DetMath.SinCos(pitch / spine.Length * 0.5f);
        for (int i = 0; i < spine.Length; i++)
        {
            int b = spine[i];
            if ((uint)b >= (uint)local.Length) continue;
            int parent = skeleton.Bones[b].Parent;
            var seen = parent >= 0 && parent < b ? LimbIK.Rotate(LimbIK.Conjugate(LimbIK.ModelRotation(skeleton, local, parent)), Vector3.UnitX) : Vector3.UnitX;
            seen = LimbIK.Unit(seen);
            var turn = new Quaternion(seen.X * s, seen.Y * s, seen.Z * s, c);
            local[b] = local[b] with { Rotation = LimbIK.Normalize(PoseMath.Multiply(turn, local[b].Rotation)) };
        }
    }
}
