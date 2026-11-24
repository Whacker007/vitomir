// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **How a dagger is held**. A 1.1 screenshot of Christof and Wilhem each
/// holding a dagger is what Vitomir's grip, blade, size and stance must match. What decides
/// them is data the two games share and one rule of 1.1's that Vitomir had to get right: every
/// dagger-class weapon names the one model, `dagger`, and a body with `SKELETONID` 0 — Christof and
/// Wilhem both — holds `dagger_0.nod`; that model is skinned by the holder's posed bones
/// through **its own** rest pose (the sub-model pose: the parent's bone matrix under the
/// thing, then the sub-model's own `RestMatrixInverse`); and a dagger is a one-handed weapon, so
/// both stand in `stand_onehand` — Wilhem through Christof's own motion set.
/// </summary>
public class HeldDaggerTests
{
    static NodModel Model(GameFiles files, string name) =>
        NodModel.Read(files.Read($"3d/models/{name}.nod"), name + ".nod");

    /// <summary>
    /// **Every dagger is the one model, in both hands.** The eighteen dagger-class templates (the
    /// daggers, dirks, poignards and Moro daggers, plain to exquisite) all carry `WEAPON_MODEL
    /// dagger`, none `SPECIFIC MODELS`, so the name is `dagger` and the skeleton id;
    /// Christof and Wilhem are both `SKELETONID 0`, and Wilhem moves with `Christof.nms`.
    /// </summary>
    [Fact]
    public void Every_dagger_is_dagger_0_in_Christof_s_hand_and_in_Wilhem_s()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        foreach (var name in new[] { "Dagger", "DaggerSilver", "DaggerExq", "Dirk", "Poignard", "MoroDagger" })
        {
            var weapon = Weapon.From(name, templates.Resolve(name));
            Assert.NotNull(weapon);
            Assert.Equal("dagger_0", HeldModel.Weapon(weapon, 0, "christof"), ignoreCase: true);
            Assert.Equal("dagger_0", HeldModel.Weapon(weapon, 0, "Wilhem"), ignoreCase: true);
        }
        foreach (var who in new[] { "Christof", "Wilhem" })
        {
            var tags = templates.Resolve(who);
            Assert.Equal(0u, tags[Tags.SkeletonId].AsUInt());
            Assert.Equal("Christof.nms", tags[Tags.MotionSet].AsString(), ignoreCase: true);
        }
    }

    /// <summary>
    /// **The dagger sits in the hand as its own file has it**. `dagger_0` was rigged on a
    /// skeleton whose root stands 7.68 lower than Christof's and Wilhem's (`RestTranslate.Z` 67.9
    /// against 75.6; every other bone's offset, and every rest turn along the arm, is the same).
    /// The sub-model pose gives the dagger's bone the holder's posed bone matrix and then undoes the
    /// **dagger's own** rest (`RestMatrixInverse` of the sub-model's bone, not the holder's), so in the
    /// hand bone's frame every dagger vertex is exactly where the file puts it against the rig's hand.
    /// Undoing the holder's rest instead would slide the whole dagger 7.68 down the blade's line,
    /// through the fist, which is what this pins against.
    /// </summary>
    [Fact]
    public void The_dagger_sits_in_the_hand_as_its_own_file_has_it()
    {
        using var files = Install.Open();
        var christof = Model(files, "christof");
        var dagger = Model(files, "dagger_0");
        float lift = christof.Bones[0].RestTranslate.Z - dagger.Bones[0].RestTranslate.Z;
        Assert.InRange(lift, 7.6f, 7.8f);

        var animation = ThingAnimation.For(christof, "Christof", new MotionSets(files, 1), "stand_onehand")!;
        animation.Advance(0.7f);
        animation.Pose(new Vector3[christof.Vertices.Length]);
        var posed = new Vector3[dagger.Vertices.Length];
        animation.PoseCarried(dagger, posed);
        Assert.True(Matrix4x4.Invert(animation.BoneMatrix(25), out var toHand));
        var own = Skeleton.RestInverse(dagger.Bones[25]);
        var holders = Skeleton.RestInverse(christof.Bones[25]);
        for (int i = 0; i < posed.Length; i++)
        {
            var inHand = Vector3.Transform(posed[i], toHand);
            var file = Vector3.Transform(dagger.Vertices[i].Position, own);
            Assert.True(Vector3.Distance(file, inHand) < 0.05f, $"vertex {i} sits at {inHand} in the hand, not {file}");
            // The other reading: the same vertex through the holder's rest, 7.68 away.
            Assert.InRange(Vector3.Distance(Vector3.Transform(dagger.Vertices[i].Position, holders), inHand), lift - 0.1f, lift + 0.1f);
        }
    }

    /// <summary>
    /// **In the one-handed stand, the blade is up out of the fist.** Christof with a dagger stands in
    /// `stand_onehand` (`ChrisStandSword.nad`: the dagger's melee class is one-handed, as a
    /// sword's is — there is no dagger stance in the set; its `util_daggerstab` and `util_daggercut`
    /// are commented out). Posed there, the hilt is at the hand bone and the blade stands above it —
    /// the grip the 1.1 picture shows, and the 27.7-unit blade of the file, which the engine draws
    /// as it is (no scale on the weapon, the holder or the slot).
    /// </summary>
    [Fact]
    public void In_the_one_handed_stand_the_blade_stands_up_out_of_the_fist()
    {
        using var files = Install.Open();
        var christof = Model(files, "christof");
        var dagger = Model(files, "dagger_0");
        var animation = ThingAnimation.For(christof, "Christof", new MotionSets(files, 1), "stand_onehand")!;
        Assert.Equal("stand_onehand", animation.Motion!.Mode.Name, ignoreCase: true);
        animation.Advance(0.5f);
        animation.Pose(new Vector3[christof.Vertices.Length]);
        var posed = new Vector3[dagger.Vertices.Length];
        animation.PoseCarried(dagger, posed);

        var hand = animation.BoneMatrix(25).Translation;              // every dagger vertex rides bone 25
        Assert.All(dagger.Vertices, v => Assert.Equal(25, v.Bone));
        var blade = dagger.Groups[0];                                  // the blade: twelve corners, the tip among them
        var tip = posed.Skip(blade.FirstVertex).Take(blade.NumVertices).MaxBy(p => p.Z);
        var hilt = posed.Skip(dagger.Groups[1].FirstVertex).Take(dagger.Groups[1].NumVertices).ToArray();
        float hiltToHand = hilt.Min(p => Vector3.Distance(p, hand));
        Assert.True(hiltToHand < 12f, $"the hilt is {hiltToHand:0.0} from the hand bone");
        Assert.True(tip.Z - hand.Z > 20f, $"the tip is only {tip.Z - hand.Z:0.0} above the hand");
    }

    /// <summary>
    /// **The hand is 1.1's own**. 1.1's Christof
    /// standing in `stand_onehand` with the dagger holds the hand bone's (bone 25's)
    /// model-space matrix (the bone record's first matrix) at the rows below while he stands.
    /// Vitomir's pose of the same clip passes through that matrix — each row within 0.02, the hand
    /// within a unit — and the dagger taken through 1.1's hand and its own rest (the sub-model pose runs every
    /// filled slot with the holder's instance as the bone source and the slot model's own
    /// the bone's own matrix) points its blade the way Vitomir's does.
    /// </summary>
    [Fact]
    public void Christof_s_hand_with_a_dagger_is_the_one_1_1_was_measured_holding()
    {
        var measured = new Matrix4x4(
            0.311f, 0.884f, -0.347f, 0,
            -0.109f, 0.396f, 0.911f, 0,
            0.943f, -0.246f, 0.219f, 0,
            33.177f, 9.226f, 70.134f, 1);
        using var files = Install.Open();
        var christof = Model(files, "christof");
        var dagger = Model(files, "dagger_0");
        var animation = ThingAnimation.For(christof, "Christof", new MotionSets(files, 1), "stand_onehand")!;
        var vertices = new Vector3[christof.Vertices.Length];
        float bestTurn = float.MaxValue, bestPlace = float.MaxValue;
        Matrix4x4 closest = default;
        for (int step = 0; step < 120; step++)
        {
            animation.Advance(0.05f);
            animation.Pose(vertices);
            var hand = animation.BoneMatrix(25);
            float turn = Rows(hand).Zip(Rows(measured), (a, b) => (a - b).Length()).Max();
            float place = Vector3.Distance(hand.Translation, measured.Translation);
            if (turn + place / 50f < bestTurn + bestPlace / 50f) { bestTurn = turn; bestPlace = place; closest = hand; }
        }
        Assert.True(bestTurn < 0.02f, $"the hand's turn is {bestTurn:0.000} from 1.1's");
        Assert.True(bestPlace < 1f, $"the hand is {bestPlace:0.00} from 1.1's");

        var own = Skeleton.RestInverse(dagger.Bones[25]);
        Vector3 Blade(Matrix4x4 hand)
        {
            var tip = dagger.Vertices.Select(v => Vector3.Transform(Vector3.Transform(v.Position, own), hand))
                .MaxBy(p => (p - hand.Translation).Length());
            return Vector3.Normalize(tip - hand.Translation);
        }
        float agree = Vector3.Dot(Blade(measured), Blade(closest));
        Assert.True(agree > 0.999f, $"the blades differ by {MathF.Acos(Math.Min(agree, 1f)) * 180 / MathF.PI:0.0} degrees");
    }

    static Vector3[] Rows(Matrix4x4 m) =>
        [new(m.M11, m.M12, m.M13), new(m.M21, m.M22, m.M23), new(m.M31, m.M32, m.M33)];
}
