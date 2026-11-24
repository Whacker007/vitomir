// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Anim;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

public class SkeletonTests
{
    [Theory]
    [InlineData(0f, -90f, 0f)]
    [InlineData(30f, 45f, 60f)]
    [InlineData(-170f, 12.5f, 359f)]
    public void Euler_matches_the_originals_rotation_matrix(float pitch, float roll, float yaw)
    {
        // The original's Euler-to-matrix, written out entry by entry (rows of a row-vector matrix).
        double r = Math.PI / 180;
        double s0 = Math.Sin(pitch * r), c0 = Math.Cos(pitch * r), s1 = Math.Sin(roll * r), c1 = Math.Cos(roll * r);
        double s2 = Math.Sin(yaw * r), c2 = Math.Cos(yaw * r);
        double[] want =
        [
            c1 * c2, c1 * s2, -s1,
            s0 * s1 * c2 - c0 * s2, s0 * s1 * s2 + c0 * c2, c1 * s0,
            c0 * s1 * c2 + s0 * s2, c0 * s1 * s2 - s0 * c2, c0 * c1,
        ];
        var m = Skeleton.Euler(new Vector3(pitch, roll, yaw));
        float[] got = [m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33];
        for (int i = 0; i < 9; i++) Assert.Equal(want[i], got[i], 5);
    }

    [Fact]
    public void Tracks_follow_the_hermite_cubic_and_sum_per_bone()
    {
        NadKey[] keys =
        [
            new(0f, 0.1f, new(1, 2, 3), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)),
            new(10f, float.PositiveInfinity, new(9, 9, 9), default, default, default),
        ];
        var anim = new NadAnimation
        {
            Version = 3, Duration = 10f,
            Tracks = [new NadTrack(1, NadTrack.Rotation, keys), new NadTrack(1, NadTrack.Rotation, keys), new NadTrack(0, NadTrack.Translate, keys)],
        };
        var rot = new Vector3[2]; var trans = new Vector3[2];
        Skeleton.Sample(anim, 5f, rot, trans);   // t = 0.5: V + C/2 + B/4 + A/8
        Assert.Equal(new Vector3(1.5f, 2.25f, 3.125f) * 2, rot[1]);
        Assert.Equal(new Vector3(1.5f, 2.25f, 3.125f), trans[0]);
        Assert.Equal(Vector3.Zero, rot[0]);
        Skeleton.Sample(anim, 0f, rot, trans);   // exactly on a key: its value
        Assert.Equal(new Vector3(1, 2, 3), trans[0]);
    }

    [Fact]
    public void Looping_playback_wraps_by_whole_durations()
    {
        var p = new AnimationPlayer(new NadAnimation { Version = 3, Duration = 100f }, rate: 30f);
        p.Advance(4f);                  // 120 frames
        Assert.Equal(20f, p.Frame, 3);
        p.Advance(10f);                 // +300: 320 → 20
        Assert.Equal(20f, p.Frame, 3);
    }

    /// <summary>
    /// The Prague west-night sky's moon (bone 1), posed at three frames, against values from an
    /// independent reference implementation (numpy) of the same rules.
    /// </summary>
    [Theory]
    [InlineData(0f, 15664f, -707f, 6291f)]
    [InlineData(9999.75f, 9491f, -707f, 14071f)]
    [InlineData(19999.5f, 3f, -707f, 17008f)]
    public void Sky_moon_rises_as_in_the_reference(float frame, float x, float y, float z)
    {
        using var files = Install.Open();
        var model = NodModel.Read(files.Read("3d/models/praguewestnight.nod"));
        var anim = NadAnimation.Read(files.Read("3d/anims/praguewestnight.nad"));
        var rot = new Vector3[model.Bones.Length]; var trans = new Vector3[model.Bones.Length];
        var world = new Matrix4x4[model.Bones.Length]; var skin = new Matrix4x4[model.Bones.Length];
        Skeleton.Sample(anim, frame, rot, trans);
        Skeleton.Pose(model, rot, trans, world);
        Skeleton.Skin(model, world, skin);
        var moon = model.Groups.Single(g => model.MeshNames[g.Mesh].StartsWith("b_moon", StringComparison.OrdinalIgnoreCase));
        var sum = Vector3.Zero;
        for (int i = 0; i < moon.NumVertices; i++) sum += Skeleton.SkinPosition(model, model.Vertices[moon.FirstVertex + i], skin);
        var centre = sum / moon.NumVertices;
        Assert.InRange(Vector3.Distance(centre, new Vector3(x, y, z)), 0f, 1.5f);
    }
}
