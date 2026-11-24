// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation;

/// <summary>
/// The arithmetic a pose is made of, written out in scalar steps in a fixed order (ours). System.Numerics'
/// matrix and quaternion products are vectorised and may use fused multiply-adds where the processor has
/// them, so their last bits can differ between machines; a pose is simulation (by design:
/// hits follow it, and a replay must hit the same), so it is made here instead. Matrices are row-vector,
/// as System.Numerics' and 1.1's: a point is transformed as <c>v × M</c>, and a child's model-space matrix
/// is <c>local × parent</c>. Only affine matrices occur (the last column is 0, 0, 0, 1).
/// </summary>
public static class PoseMath
{
    /// <summary>The Hamilton product <c>a ⊗ b</c>: as a rotation, <paramref name="b"/> first and then <paramref name="a"/>.</summary>
    public static Quaternion Multiply(Quaternion a, Quaternion b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    /// <summary>
    /// 1.1's Euler rotation as a quaternion: degrees about X, then about Y, then about Z (pitch, roll, yaw),
    /// with row vectors — the old engine's <c>Skeleton.Euler</c>, the X rotation's matrix times the Y's times
    /// the Z's, so a point is turned about X first. As quaternions that is <c>qZ ⊗ qY ⊗ qX</c>; each half
    /// angle's sine and cosine are <see cref="DetMath.SinCos"/>'s.
    /// </summary>
    public static Quaternion FromEulerDegrees(Vector3 degrees)
    {
        var (sx, cx) = DetMath.SinCos(degrees.X * DetMath.DegToRad * 0.5f);
        var (sy, cy) = DetMath.SinCos(degrees.Y * DetMath.DegToRad * 0.5f);
        var (sz, cz) = DetMath.SinCos(degrees.Z * DetMath.DegToRad * 0.5f);
        var yx = Multiply(new Quaternion(0, sy, 0, cy), new Quaternion(sx, 0, 0, cx));
        return Multiply(new Quaternion(0, 0, sz, cz), yx);
    }

    /// <summary>A bone transform's matrix: rotate, then translate (row vectors), the same formula as System.Numerics' quaternion matrix, in scalar steps.</summary>
    public static Matrix4x4 ToMatrix(in BoneTransform t)
    {
        var q = t.Rotation;
        float xx = q.X * q.X, yy = q.Y * q.Y, zz = q.Z * q.Z;
        float xy = q.X * q.Y, wz = q.Z * q.W, xz = q.Z * q.X, wy = q.Y * q.W, yz = q.Y * q.Z, wx = q.X * q.W;
        return new Matrix4x4(
            1f - 2f * (yy + zz), 2f * (xy + wz), 2f * (xz - wy), 0f,
            2f * (xy - wz), 1f - 2f * (zz + xx), 2f * (yz + wx), 0f,
            2f * (xz + wy), 2f * (yz - wx), 1f - 2f * (yy + xx), 0f,
            t.Translation.X, t.Translation.Y, t.Translation.Z, 1f);
    }

    /// <summary>The affine product <c>a × b</c> (row vectors: <paramref name="a"/> applied first).</summary>
    public static Matrix4x4 Multiply(in Matrix4x4 a, in Matrix4x4 b) => new(
        a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31,
        a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32,
        a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33, 0f,
        a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31,
        a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32,
        a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33, 0f,
        a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31,
        a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32,
        a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33, 0f,
        a.M41 * b.M11 + a.M42 * b.M21 + a.M43 * b.M31 + b.M41,
        a.M41 * b.M12 + a.M42 * b.M22 + a.M43 * b.M32 + b.M42,
        a.M41 * b.M13 + a.M42 * b.M23 + a.M43 * b.M33 + b.M43, 1f);

    /// <summary>A point through an affine matrix (row vector).</summary>
    public static Vector3 Transform(Vector3 v, in Matrix4x4 m) => new(
        v.X * m.M11 + v.Y * m.M21 + v.Z * m.M31 + m.M41,
        v.X * m.M12 + v.Y * m.M22 + v.Z * m.M32 + m.M42,
        v.X * m.M13 + v.Y * m.M23 + v.Z * m.M33 + m.M43);

    /// <summary>A direction through an affine matrix's 3×3 part (row vector).</summary>
    public static Vector3 TransformNormal(Vector3 v, in Matrix4x4 m) => new(
        v.X * m.M11 + v.Y * m.M21 + v.Z * m.M31,
        v.X * m.M12 + v.Y * m.M22 + v.Z * m.M32,
        v.X * m.M13 + v.Y * m.M23 + v.Z * m.M33);

    /// <summary>A linear blend <c>a + (b − a) × t</c>, component by component.</summary>
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
}
