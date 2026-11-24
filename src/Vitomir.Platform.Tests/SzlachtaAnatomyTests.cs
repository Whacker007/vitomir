// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Tests;

/// <summary>
/// 1.1's szlachta's anatomy table (<see cref="AnatomyTables.OneOneSzlachta"/>): the Tzimisce war-ghoul Christof
/// fights as a human (the template <c>Szlachta</c>, <c>SZLACHTA.NOD</c>, thirty-four bones). The table fits its skeleton with
/// the right on the right; its hit volumes cover its skin; and its neck is long — six bones reaching from the shoulders to the
/// head, several volumes, far longer than a man's (<i>"they have long necks and are supposed to be fairly weak"</i>).
/// Needs the 1.1 install (skipped without it).
/// </summary>
public sealed class SzlachtaAnatomyTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static OneOneCharacter Character(string name)
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        lock (Library) return Library.Value!.Character(name) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
    }

    static Matrix4x4[] BindBones(Skeleton s)
    {
        var bones = new Matrix4x4[s.Count];
        for (int b = 0; b < s.Count; b++) Matrix4x4.Invert(s.InverseBind[b], out bones[b]);
        return bones;
    }

    static HitVolume[] Volumes(FittedBody body, Skeleton s)
    {
        var into = new HitVolume[body.Volumes.Length];
        HitVolumes.Pose(body, BindBones(s), Matrix4x4.Identity, 1f, into);
        return into;
    }

    /// <summary>The length of a region's volumes along their axes, end cap to end cap, summed (m).</summary>
    static float Along(ReadOnlySpan<HitVolume> volumes, BodyRegion r)
    {
        float sum = 0f;
        foreach (var v in volumes) if (v.Region == r) sum += Vector3.Distance(v.Capsule.A, v.Capsule.B) + 2 * v.Capsule.Radius;
        return sum;
    }

    [Fact]
    public void The_szlachtas_table_fits_its_skeleton_its_volumes_cover_its_skin_and_its_neck_is_long()
    {
        var c = Character("Szlachta");
        var m = c.Model;
        Assert.Same(AnatomyTables.OneOneSzlachta, AnatomyTables.For(m.Skeleton));
        Assert.Equal(m.Skeleton.Count, AnatomyTables.OneOneSzlachta.Regions.Length);
        // Its arms and thighs on their sides (it faces +Y, its right along +X, as 1.1's man).
        var table = AnatomyTables.OneOneSzlachta;
        var sum = new Vector3[BodyRegions.Count];
        var count = new int[BodyRegions.Count];
        foreach (var v in m.Vertices)
        {
            var r = table.Of(v.Weights.X >= HitVolumeFit.WeightAtLeast ? v.Bone(0) : v.Bone(1));
            sum[(int)r] += v.Position;
            count[(int)r]++;
        }
        foreach (var r in new[] { BodyRegion.LeftUpperArm, BodyRegion.LeftForearm, BodyRegion.LeftThigh })
            Assert.True(sum[(int)r].X / count[(int)r] < 0f, $"{r} lies at x {sum[(int)r].X / count[(int)r]:0.00}");
        foreach (var r in new[] { BodyRegion.RightUpperArm, BodyRegion.RightForearm, BodyRegion.RightThigh })
            Assert.True(sum[(int)r].X / count[(int)r] > 0f, $"{r} lies at x {sum[(int)r].X / count[(int)r]:0.00}");
        // The head forward of the neck forward of the chest (its long neck reaches forward from the shoulders).
        float Y(BodyRegion r) => sum[(int)r].Y / count[(int)r];
        Assert.True(Y(BodyRegion.Head) > Y(BodyRegion.Neck) && Y(BodyRegion.Neck) > Y(BodyRegion.Chest), $"head y {Y(BodyRegion.Head):0.00}, neck {Y(BodyRegion.Neck):0.00}, chest {Y(BodyRegion.Chest):0.00}");

        var body = HitVolumeFit.Fit(m);
        Assert.NotNull(body);
        Assert.Equal(table.Name, body.Table);
        var volumes = Volumes(body, m.Skeleton);
        foreach (var r in BodyRegions.All) Assert.True(volumes.Any(v => v.Region == r), $"no volume for {r}");
        // Most of its skin is within 2 cm of a volume.
        var bones = BindBones(m.Skeleton);
        int total = 0, covered = 0;
        foreach (var v in m.Vertices)
        {
            if (table.Of(v.Weights.X >= HitVolumeFit.WeightAtLeast ? v.Bone(0) : v.Bone(1)) == BodyRegion.None) continue;
            var p = Vector3.Zero;
            for (int k = 0; k < 4; k++)
            {
                float w = k == 0 ? v.Weights.X : k == 1 ? v.Weights.Y : k == 2 ? v.Weights.Z : v.Weights.W;
                if (w > 0f) p += Vector3.Transform(Vector3.Transform(v.Position, m.Skeleton.InverseBind[v.Bone(k)]), bones[v.Bone(k)]) * w;
            }
            total++;
            foreach (var hv in volumes)
                if (Vector3.Distance(Geometry.ClosestOnSegment(p, hv.Capsule.A, hv.Capsule.B, out _), p) <= hv.Capsule.Radius + 0.02f) { covered++; break; }
        }
        float coverage = covered / (float)total;
        // The neck: several volumes, much longer than a man's.
        var man = Character("Christof");
        var manBody = HitVolumeFit.Fit(man.Model)!;
        var manVolumes = Volumes(manBody, man.Model.Skeleton);
        int neckVolumes = volumes.Count(v => v.Region == BodyRegion.Neck);
        float neck = Along(volumes, BodyRegion.Neck), manNeck = Along(manVolumes, BodyRegion.Neck);
        float neckRadius = volumes.Where(v => v.Region == BodyRegion.Neck).Max(v => v.Capsule.Radius);
        float manNeckRadius = manVolumes.Where(v => v.Region == BodyRegion.Neck).Max(v => v.Capsule.Radius);
        output.WriteLine($"szlachta: {volumes.Length} volumes, reach {body.Reach:0.00} m, {coverage:P1} of its skin within 2 cm; its neck {neckVolumes} volumes, " +
                         $"{neck:0.00} m along end to end, radius at most {neckRadius * 100:0.0} cm; Christof's neck {manNeck:0.00} m, radius {manNeckRadius * 100:0.0} cm");
        // Its motion set names no right fingers nor a Weapon tag: its weapon hand (the claws' striking point) is its right hand's
        // last bone by the table, 24 (22 the hand, 23 and 24 its fingers), so its swings can meet what they reach.
        Assert.Null(c.Limbs);
        Assert.Equal(24, c.Actions!.WeaponHand);
        Assert.True(coverage >= 0.85f, $"{coverage:P1} of its skin within 2 cm of a volume");
        Assert.True(neckVolumes >= 3, $"{neckVolumes} neck volumes");
        Assert.True(neck > 2.0f * manNeck, $"its neck's volumes {neck:0.00} m along against a man's {manNeck:0.00}");
    }
}
