// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The models repository's glTF axes into the platform's model space.
using System.Numerics;

namespace Vitomir.Platform.Assets.People;

/// <summary>
/// The change from a models-stream glTF file's axes (Y up, the person facing +Z, their left hand at +X) into the platform's
/// model space for people: Z up, the person facing +Y with the right hand at +X, as 1.1's models stand
/// (<c>CharacterKind.FacingYaw</c> = π/2). A point (x, y, z) becomes (−x, z, y): a half turn about the vertical on the
/// level reader's (x, −z, y). The change is a rotation (its determinant is +1), so triangles keep their winding and
/// tangents their handedness; it is its own inverse.
/// <para>Every frame is carried over, not only model space: a bone's local transform <c>L</c> becomes <c>C·L·C</c>, its
/// quaternion (qx, qy, qz, qw) becoming (−qx, qz, qy, qw) and its translation like a point, so its own axes are turned
/// the same way (a bone's glTF +Y along its length is +Z here, its +X is −X). A thing attached at a socket is converted
/// the same way and placed with the socket's converted frame, and lands where the files put it.</para>
/// </summary>
public static class PeopleSpace
{
    /// <summary>The change as a row-vector matrix (<c>p' = p × C</c>); <c>C × C</c> is the identity.</summary>
    public static readonly Matrix4x4 FromGltf = new(
        -1, 0, 0, 0,
        0, 0, 1, 0,
        0, 1, 0, 0,
        0, 0, 0, 1);

    /// <summary>A point or a direction.</summary>
    public static Vector3 Point(Vector3 g) => new(-g.X, g.Z, g.Y);

    /// <summary>A tangent: its direction like a point, its handedness (w) unchanged.</summary>
    public static Vector4 Tangent(Vector4 g) => new(-g.X, g.Z, g.Y, g.W);

    /// <summary>A rotation: the axis like a point, the angle unchanged.</summary>
    public static Quaternion Rotation(Quaternion g) => new(-g.X, g.Z, g.Y, g.W);

    /// <summary>A bone's (or a socket's) local transform.</summary>
    public static BoneTransform Transform(Quaternion rotation, Vector3 translation) => new(Rotation(rotation), Point(translation));

    /// <summary>
    /// An affine matrix in glTF's frames (an inverse bind matrix, a node's world matrix) as the same map in the platform's:
    /// <c>C × M × C</c>, written out (it only permutes and negates entries, so it is exact).
    /// </summary>
    public static Matrix4x4 Matrix(in Matrix4x4 m) => new(
        m.M11, -m.M13, -m.M12, -m.M14,
        -m.M31, m.M33, m.M32, m.M34,
        -m.M21, m.M23, m.M22, m.M24,
        -m.M41, m.M43, m.M42, m.M44);
}
