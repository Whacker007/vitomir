// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The hit volumes on the drawn body, with 1.1's characters from the player's install (every test skips
/// without it): fitted from the skin of every character the platform draws today, each region is there and covered
/// and no capsule stands out of the drawn mesh by more than a few centimetres; the anatomy tables fit their skeletons
/// with left on the left; the volumes ride the pose (a raised arm carries its capsules up; through a walk and a run the
/// skinned vertices stay covered) and stay inside the broad test's capsule; the reticle names the part of a posed
/// townsman at known points; the cost is small and allocates nothing; and asking for volumes changes no replay.
/// </summary>
public sealed class HitVolumeTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);
    static readonly Dictionary<string, OneOneCharacter> Loaded = [];

    /// <summary>Every character the platform draws today: the catalogue's five (Christof and four townsfolk), the night and day townsfolk, the guard and the night watchman.</summary>
    public static readonly string[] Drawn =
    [
        "Christof", "OldMan", "Merchant", "YoungLady", "Monk",
        "Civ12male", "Civ03merchant1", "Civ10monk2", "Civ06Milkmaid", "Civ11female", "Civ07drunk", "Civ02beggar",
        "KnightOfStJohn_torch", "rudolfGuard",
    ];

    static OneOneCharacter Character(string name)
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        lock (Loaded)
        {
            if (!Loaded.TryGetValue(name, out var c)) Loaded[name] = c = Library.Value!.Character(name) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
            return c;
        }
    }

    /// <summary>The model's bones in its bind pose (the pose its vertices are given in).</summary>
    static Matrix4x4[] BindBones(Skeleton s)
    {
        var bones = new Matrix4x4[s.Count];
        for (int b = 0; b < s.Count; b++) Matrix4x4.Invert(s.InverseBind[b], out bones[b]);
        return bones;
    }

    static HitVolume[] Volumes(FittedBody body, ReadOnlySpan<Matrix4x4> bones)
    {
        var into = new HitVolume[body.Volumes.Length];
        HitVolumes.Pose(body, bones, Matrix4x4.Identity, 1f, into);
        return into;
    }

    /// <summary>The region each vertex is drawn for (its bone's, by the anatomy table; none for cloth).</summary>
    static BodyRegion RegionOf(in SkinVertex v, AnatomyTable table) => table.Of(v.Weights.X >= HitVolumeFit.WeightAtLeast ? v.Bone(0) : v.Bone(1));

    static bool Covered(Vector3 p, ReadOnlySpan<HitVolume> volumes, float margin)
    {
        foreach (var hv in volumes)
            if (Vector3.Distance(Geometry.ClosestOnSegment(p, hv.Capsule.A, hv.Capsule.B, out _), p) <= hv.Capsule.Radius + margin) return true;
        return false;
    }

    /// <summary>The share of the body's vertices (not cloth) within <paramref name="margin"/> of a volume, skinned by <paramref name="bones"/>.</summary>
    static float Coverage(SkinnedModel m, AnatomyTable table, ReadOnlySpan<Matrix4x4> bones, ReadOnlySpan<HitVolume> volumes, float margin, Span<int> perRegion, Span<int> perRegionCovered)
    {
        int total = 0, covered = 0;
        foreach (var v in m.Vertices)
        {
            var r = RegionOf(v, table);
            if (r == BodyRegion.None) continue;
            var p = Vector3.Zero;
            for (int k = 0; k < 4; k++)
            {
                float w = k == 0 ? v.Weights.X : k == 1 ? v.Weights.Y : k == 2 ? v.Weights.Z : v.Weights.W;
                if (w > 0f) p += Vector3.Transform(Vector3.Transform(v.Position, m.Skeleton.InverseBind[v.Bone(k)]), bones[v.Bone(k)]) * w;
            }
            total++; perRegion[(int)r]++;
            if (Covered(p, volumes, margin)) { covered++; perRegionCovered[(int)r]++; }
        }
        return covered / (float)total;
    }

    /// <summary>
    /// How far a point stands out of the mesh: 0 inside it (its winding number, the solid angle of every triangle seen
    /// from it summed, at least a half), else its distance to the nearest triangle.
    /// </summary>
    static float StandsOut(Vector3 p, List<(Vector3 A, Vector3 B, Vector3 C)> triangles)
    {
        double winding = 0;
        float nearest = float.MaxValue;
        foreach (var (a, b, c) in triangles)
        {
            Vector3 A = a - p, B = b - p, C = c - p;
            float la = A.Length(), lb = B.Length(), lc = C.Length();
            double num = Vector3.Dot(A, Vector3.Cross(B, C));
            double den = la * lb * lc + Vector3.Dot(A, B) * lc + Vector3.Dot(A, C) * lb + Vector3.Dot(B, C) * la;
            winding += 2 * Math.Atan2(num, den);
            nearest = MathF.Min(nearest, MathF.Sqrt(SweepMath.SegmentTriangle(p, p, a, b, c, out _, out _)));
        }
        return Math.Abs(winding / (4 * Math.PI)) >= 0.5 ? 0f : nearest;
    }

    [Fact]
    public void Every_character_drawn_has_each_region_covered_and_no_capsule_standing_out_of_its_mesh()
    {
        foreach (var name in Drawn)
        {
            var c = Character(name);
            var m = c.Model;
            var table = AnatomyTables.For(m.Skeleton);
            Assert.NotNull(table);
            var body = HitVolumeFit.Fit(m);
            Assert.NotNull(body);
            var bones = BindBones(m.Skeleton);
            var volumes = Volumes(body, bones);
            int[] perRegion = new int[BodyRegions.Count], perCovered = new int[BodyRegions.Count];
            float coverage = Coverage(m, table, bones, volumes, 0.02f, perRegion, perCovered);
            // Every region the model draws with enough vertices for a volume of its own has one, and is mostly covered.
            foreach (var r in BodyRegions.All)
            {
                if (perRegion[(int)r] >= HitVolumeFit.VerticesAtLeast)
                    Assert.True(volumes.Any(v => v.Region == r) || perCovered[(int)r] >= 0.8f * perRegion[(int)r], $"{name}: no volume for {r} ({perRegion[(int)r]} vertices)");
                if (perRegion[(int)r] >= HitVolumeFit.FewVertices)
                    Assert.True(perCovered[(int)r] >= 0.35f * perRegion[(int)r], $"{name}: {r} covered {perCovered[(int)r]} of {perRegion[(int)r]}");
            }
            var thin = string.Join(", ", BodyRegions.All.ToArray().Where(r => perRegion[(int)r] > 0 && perCovered[(int)r] < 0.75f * perRegion[(int)r])
                                                         .Select(r => $"{BodyRegions.Name(r)} {perCovered[(int)r]}/{perRegion[(int)r]}"));
            Assert.True(coverage >= 0.85f, $"{name}: {coverage:P1} of the body's vertices within 2 cm of a volume");
            // No capsule stands out of the drawn mesh: its surface, sampled, is inside the mesh or near it.
            var triangles = new List<(Vector3, Vector3, Vector3)>();
            for (int i = 0; i < m.Indices.Length; i += 3)
                triangles.Add((m.Vertices[m.Indices[i]].Position, m.Vertices[m.Indices[i + 1]].Position, m.Vertices[m.Indices[i + 2]].Position));
            var outs = new List<float>();
            foreach (var hv in volumes)
            {
                var cap = hv.Capsule;
                var axis = cap.B - cap.A;
                var u = Geometry.Normalize(axis, Vector3.UnitZ);
                var e1 = Geometry.Perpendicular(u);
                var e2 = Vector3.Cross(u, e1);
                for (int s = 0; s <= 4; s++)
                    for (int k = 0; k < 12; k++)
                    {
                        float angle = MathF.Tau * k / 12;
                        outs.Add(StandsOut(cap.A + axis * (s / 4f) + (e1 * MathF.Cos(angle) + e2 * MathF.Sin(angle)) * cap.Radius, triangles));
                    }
                outs.Add(StandsOut(cap.A - u * cap.Radius, triangles));
                outs.Add(StandsOut(cap.B + u * cap.Radius, triangles));
            }
            outs.Sort();
            float p90 = outs[(int)(outs.Count * 0.9)], p99 = outs[(int)(outs.Count * 0.99)], most = outs[^1];
            output.WriteLine($"{name,-22} {volumes.Length,2} volumes, reach {body.Reach:0.00} m; {coverage:P1} covered; standing out p90 {p90 * 100:0.0} cm, p99 {p99 * 100:0.0} cm, most {most * 100:0.0} cm{(thin.Length > 0 ? "; thinly covered: " + thin : "")}");
            Assert.True(p90 <= 0.05f, $"{name}: a tenth of the capsules' surface stands {p90 * 100:0.0} cm out of the mesh");
            Assert.True(p99 <= 0.08f, $"{name}: a hundredth of the capsules' surface stands {p99 * 100:0.0} cm out of the mesh");
            Assert.True(most <= 0.13f, $"{name}: a capsule stands {most * 100:0.0} cm out of the mesh");
        }
    }

    [Fact]
    public void The_anatomy_tables_fit_their_skeletons_with_the_left_on_the_left()
    {
        var seen = new HashSet<string>();
        foreach (var name in Drawn)
        {
            var c = Character(name);
            var s = c.Model.Skeleton;
            var table = AnatomyTables.For(s);
            Assert.NotNull(table);
            seen.Add(table.Name);
            Assert.Equal(s.Count, table.Regions.Length);
            // The regions' sides by the vertices in the bind pose: 1.1's people face +Y with their right along +X (the
            // shoulders and the thighs: hands and feet may cross, a monk's clasped).
            var sum = new Vector3[BodyRegions.Count];
            var count = new int[BodyRegions.Count];
            foreach (var v in c.Model.Vertices) { var r = RegionOf(v, table); sum[(int)r] += v.Position; count[(int)r]++; }
            foreach (var r in BodyRegions.All)
            {
                if (count[(int)r] < 4) continue;
                float x = sum[(int)r].X / count[(int)r];
                if (r is not (BodyRegion.LeftUpperArm or BodyRegion.RightUpperArm or BodyRegion.LeftThigh or BodyRegion.RightThigh)) continue;
                if (BodyRegions.IsLeft(r)) Assert.True(x < 0f, $"{name}: {r} lies at x {x:0.00}");
                if (BodyRegions.IsRight(r)) Assert.True(x > 0f, $"{name}: {r} lies at x {x:0.00}");
            }
            // The head above the neck above the chest above the belly above the hips, above the thighs, the shins and the feet.
            float Z(BodyRegion r) => count[(int)r] > 0 ? sum[(int)r].Z / count[(int)r] : float.NaN;
            BodyRegion[] order = [BodyRegion.Head, BodyRegion.Neck, BodyRegion.Chest, BodyRegion.Belly, BodyRegion.Hips, BodyRegion.LeftThigh, BodyRegion.LeftShin, BodyRegion.LeftFoot];
            float above = float.PositiveInfinity;
            foreach (var r in order)
            {
                if (float.IsNaN(Z(r)) || count[(int)r] < 4) continue;
                Assert.True(Z(r) < above + 0.05f, $"{name}: {r} at z {Z(r):0.00} is not below what is above it ({above:0.00})");
                above = Z(r);
            }
        }
        Assert.Equal(["1.1 man (40 bones)", "1.1 woman (48 bones)"], seen.Order(StringComparer.Ordinal));
        // The tables are what the platform knows; a skeleton of another shape has none (it keeps only its body's capsule).
        var other = new Skeleton { Bones = [new BoneInfo("root", -1, BoneTransform.Identity)], InverseBind = [Matrix4x4.Identity] };
        Assert.Null(AnatomyTables.For(other));
    }

    [Fact]
    public void A_raised_arm_carries_its_volumes_up_and_leaves_the_rest_where_they_were()
    {
        var c = Character("Christof");
        var s = c.Model.Skeleton;
        var body = HitVolumeFit.Fit(c.Model)!;
        var local = new BoneTransform[s.Count];
        var bones = new Matrix4x4[s.Count];
        c.Role(MotionRole.Idle).Clip!.Sample(0f, s, local);
        PoseEvaluator.ToModel(s, local, bones);
        var before = Volumes(body, bones);
        // Raise the right arm: turn the shoulder's bone and all below it 150° about the body's forward axis at the shoulder.
        int shoulder = c.Limbs!.RightArm.Upper;
        var joint = bones[shoulder].Translation;
        var turn = Matrix4x4.CreateTranslation(-joint) * Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, -150f * MathF.PI / 180f) * Matrix4x4.CreateTranslation(joint);
        for (int b = 0; b < s.Count; b++)
            if (Below(s, b, shoulder)) bones[b] = bones[b] * turn;
        var after = Volumes(body, bones);
        for (int i = 0; i < before.Length; i++)
        {
            var r = before[i].Region;
            float rise = after[i].Capsule.Centre.Z - before[i].Capsule.Centre.Z;
            if (r is BodyRegion.RightForearm or BodyRegion.RightHand) Assert.True(rise > 0.5f, $"{r} rose {rise:0.00} m");
            else if (r == BodyRegion.RightUpperArm) Assert.True(rise > 0.15f, $"{r} rose {rise:0.00} m");
            else Assert.True(Vector3.Distance(after[i].Capsule.Centre, before[i].Capsule.Centre) < 1e-5f, $"{r} moved");
        }
        // And the raised arm's skin is still inside its volumes: they follow the drawn pose.
        Span<int> perRegion = stackalloc int[BodyRegions.Count], perCovered = stackalloc int[BodyRegions.Count];
        Coverage(c.Model, AnatomyTables.For(s)!, bones, after, 0.02f, perRegion, perCovered);
        foreach (var r in new[] { BodyRegion.RightUpperArm, BodyRegion.RightForearm, BodyRegion.RightHand })
            Assert.True(perCovered[(int)r] >= 0.75f * perRegion[(int)r], $"{r}: {perCovered[(int)r]} of {perRegion[(int)r]}");
    }

    static bool Below(Skeleton s, int bone, int ancestor)
    {
        for (int b = bone; b >= 0; b = s.Bones[b].Parent)
            if (b == ancestor) return true;
        return false;
    }

    [Fact]
    public void Through_their_motions_the_volumes_cover_the_skin_and_stay_inside_the_broad_capsule()
    {
        foreach (var name in new[] { "Christof", "Civ12male", "rudolfGuard", "OldMan", "YoungLady" })
        {
            var c = Character(name);
            var s = c.Model.Skeleton;
            var table = AnatomyTables.For(s)!;
            var body = HitVolumeFit.Fit(c.Model)!;
            var local = new BoneTransform[s.Count];
            var bones = new Matrix4x4[s.Count];
            var volumes = new HitVolume[body.Volumes.Length];
            int[] perRegion = new int[BodyRegions.Count], perCovered = new int[BodyRegions.Count];
            var broad = Capsule.OfBody(Vector3.Zero, Locomotion.Sandbox.Radius, Locomotion.Sandbox.Height);
            float worstCoverage = 1f, worstOut = float.NegativeInfinity;
            // Each role's motion as it is played (the rough landing from its Fall mark: before it, it falls six metres).
            var clips = c.Roles.Where(r => r.Clip is not null).Select(r => ((IAnimationClip)r.Clip!, r.StartSeconds))
                         .Concat(c.Compositions.Select(k => ((IAnimationClip)k.Clip, 0f))).ToList();
            foreach (var (clip, start) in clips)
                for (int k = 0; k < 8; k++)
                {
                    clip.Sample(start + (clip.Duration - start) * k / 8f, s, local);
                    PoseEvaluator.ToModel(s, local, bones);
                    HitVolumes.Pose(body, bones, Matrix4x4.Identity, 1f, volumes);   // the model's space: feet at the origin, Z up
                    Array.Clear(perRegion); Array.Clear(perCovered);
                    worstCoverage = MathF.Min(worstCoverage, Coverage(c.Model, table, bones, volumes, 0.02f, perRegion, perCovered));
                    foreach (var v in volumes)
                        foreach (var end in new[] { v.Capsule.A, v.Capsule.B })
                        {
                            var on = Geometry.ClosestOnSegment(end, broad.Lower, broad.Upper, out _);
                            worstOut = MathF.Max(worstOut, Vector3.Distance(end, on) + v.Capsule.Radius - (broad.Radius + body.Reach));
                        }
                }
            output.WriteLine($"{name,-12} over {clips.Count} motions: the least covered pose {worstCoverage:P1}; the nearest a volume came to the broad capsule's side {-worstOut:0.00} m inside");
            Assert.True(worstCoverage >= 0.8f, $"{name}: a pose with {worstCoverage:P1} of the skin covered");
            Assert.True(worstOut <= 0f, $"{name}: a volume {worstOut:0.00} m outside the broad capsule");
        }
    }

    // ---------------------------------------------------------------- in a world

    /// <summary>A world as the program makes it (movement, animation, the volumes, the body's motion) on a floor: a townsman standing at the origin facing −X, and the player 4 m off that way.</summary>
    static (World World, EntityId Townsman, EntityId Player) Town(bool volumes = true)
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var assets = new CharacterAssets();
        int townsman = OneOneLibrary.Register(assets, Character("Civ12male"));
        int christof = OneOneLibrary.Register(assets, Character("Christof"));
        var level = new TestLevel().Floor(-30, -30, 30, 30).Build();
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        b.Animation(assets);
        if (volumes) b.HitVolumes();
        BodyMotionSystem.Register(b);
        var w = b.Build();
        if (volumes) w.Service<HitVolumes>().Prepare(assets);
        var t = MovementServices.SpawnBody(w, new Vector3(0, 0, 0.01f), MathF.PI, Locomotion.Sandbox);
        w.Set(t, CharacterAnimation.For(assets, townsman));
        w.Set(t, MotionInput.Standing);
        var p = MovementServices.SpawnControlled(w, new Vector3(-4, 0, 0.01f), 0f, Locomotion.Sandbox);
        w.Set(p, CharacterAnimation.For(assets, christof));
        w.Set(p, MotionInput.Standing);
        for (int i = 0; i < 60; i++) MovementServices.Step(w, ActionFrame.Idle);
        return (w, t, p);
    }

    [Fact]
    public void The_reticle_names_the_part_of_a_posed_townsman_at_known_points()
    {
        var (w, townsman, player) = Town();
        var view = new ViewState { HasView = true, Of = player, Kind = ViewKind.First };
        string? PartAt(float y, float z)
        {
            var from = new Vector3(-3, y, z);
            view.Eyes = from;
            var answer = ReticleSystem.Cast(w, view, from, Vector3.UnitX, 50f);
            Assert.Equal(ReticleTarget.Person, answer.Kind);
            Assert.Equal(townsman.Packed, answer.Entity);
            return answer.Part;
        }
        // He faces −X, towards the ray, so his left is −Y.
        Assert.Equal("head", PartAt(0f, 1.62f));
        Assert.Equal("chest", PartAt(0f, 1.35f));
        Assert.Equal("left shin", PartAt(-0.1f, 0.3f));
        Assert.Equal("right shin", PartAt(0.1f, 0.3f));
        Assert.Equal("left thigh", PartAt(-0.1f, 0.7f));
        // Between the shins, under the hem: his capsule is there but no drawn part.
        Assert.Null(PartAt(0f, 0.15f));
        // The pointer's target hook carries the part: a request taken on his head says so.
        var head = ReticleSystem.Cast(w, view, new Vector3(-3, 0, 1.62f), Vector3.UnitX, 50f);
        Assert.Equal("head", new TargetPicked(player, w.Tick, 1, head.Kind, head.Entity, head.Point, head.Distance, false) { Part = head.Part }.Part);
    }

    /// <summary>
    /// The volumes posed, cast at and the reticle's part taken a thousand times once warm, allocating nothing; the part is the
    /// belly's. In the plain run: split from the timing below, which is load-sensitive (an allocation count is not).
    /// </summary>
    [Fact]
    public void Posing_the_volumes_allocates_nothing() => PosedThousandTimes();

    /// <summary>The town warmed, then posed and cast at a thousand times with nothing allocated (asserted), the part the belly's.</summary>
    static (World W, EntityId Townsman, HitVolumes Volumes, CharacterPoses Poses, Vector3 O) PosedThousandTimes()
    {
        var (w, townsman, player) = Town();
        var volumes = w.Service<HitVolumes>();
        var poses = w.Service<CharacterPoses>();
        var o = new Vector3(-3, 0, 1.3f);
        var view = new ViewState { HasView = true, Of = player, Kind = ViewKind.Third, Eyes = new Vector3(-3.5f, 0.3f, 1.6f) };
        // Warm, posing afresh too (the pose's first recompute with the action channel sizes its buffers once).
        for (int i = 0; i < 200; i++) { poses.Invalidate(); volumes.Posed(w, townsman); volumes.Ray(w, townsman, o, Vector3.UnitX, 10f, out _); volumes.Broad(w, townsman, out _); ReticleSystem.Cast(w, view, o, Vector3.UnitX, 50f); }
        string? part = null;
        // Measured as the allocation tests measure (Allocations.None: the window measured more than once).
        Allocations.None("posing the volumes 1,000 times", () =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                poses.Invalidate();
                volumes.Posed(w, townsman);
                volumes.Ray(w, townsman, o, Vector3.UnitX, 10f, out _);
                volumes.Broad(w, townsman, out _);
                part = ReticleSystem.Cast(w, view, o, Vector3.UnitX, 50f).Part;   // the reticle's part too
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });
        Assert.Equal("belly", part);
        return (w, townsman, volumes, poses, o);
    }

    /// <summary>
    /// **A timing test** (<c>[Trait("Category", "Timing")]</c>, left out of the plain run and run alone by the
    /// timing command, the README's Build, test and run): what posing the volumes costs, measured after the same warming and allocation pass as
    /// <see cref="Posing_the_volumes_allocates_nothing"/>, so the figures are taken as they were; the earlier build's bounds kept.
    /// </summary>
    [Fact, Trait("Category", "Timing")]
    public void Posing_the_volumes_costs_a_few_microseconds()
    {
        var (w, townsman, volumes, poses, o) = PosedThousandTimes();
        // Each figure the best of Runs (a Windows PC once measured 20.97 µs from a kept pose against the 20 µs bound under the whole suite run in
        // parallel, every timing about six times slow then; alone 3 µs): a machine busy for one run is not the code's cost, and the bounds stay.
        const int N = 20000, Runs = 5;
        static double Best(Func<double> run)
        {
            double best = double.MaxValue;
            for (int r = 0; r < Runs; r++) best = Math.Min(best, run());
            return best;
        }
        double cached = Best(() =>
        {
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < N; i++) volumes.Posed(w, townsman);
            return clock.Elapsed.TotalMicroseconds / N;
        });
        double withPose = Best(() =>
        {
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < N / 10; i++) { poses.Invalidate(); volumes.Posed(w, townsman); }
            return clock.Elapsed.TotalMicroseconds / (N / 10);
        });
        double ray = Best(() =>
        {
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < N; i++) volumes.Ray(w, townsman, o, Vector3.UnitX, 10f, out _);
            return clock.Elapsed.TotalMicroseconds / N;
        });
        var far = new Vector3(-3, 20, 1.3f);
        double broadMiss = Best(() =>
        {
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < N; i++) volumes.Ray(w, townsman, far, Vector3.UnitX, 10f, out _);
            return clock.Elapsed.TotalMicroseconds / N;
        });
        int count = volumes.Fitted(w, townsman)!.Volumes.Length;
        output.WriteLine($"a townsman's {count} volumes (each the best of {Runs} runs): posed from a kept pose {cached:0.00} µs; with the pose worked out {withPose:0.0} µs; a ray against them {ray:0.00} µs; a ray the broad test turns away {broadMiss:0.00} µs");
        Assert.True(cached < 20, $"{cached:0.00} µs");
        Assert.True(withPose < 300, $"{withPose:0.0} µs");
    }

    [Fact]
    public void Asking_for_volumes_and_parts_every_tick_changes_no_digest()
    {
        var (a, ta, pa) = Town(volumes: true);
        var (b, _, _) = Town(volumes: false);
        var frame = new ActionFrame(new Vector2(0, 1), new Vector2(0.01f, 0f), Buttons.None, default);
        var view = new ViewState { HasView = true, Of = pa, Kind = ViewKind.First, Eyes = new Vector3(-3, 0, 1.4f) };
        for (int i = 0; i < 240; i++)
        {
            MovementServices.Step(a, frame);
            MovementServices.Step(b, frame);
            a.Service<HitVolumes>().Posed(a, ta);
            ReticleSystem.Cast(a, view, view.Eyes, Vector3.UnitX, 50f);
            if (i % 30 == 0) Assert.Equal(b.Digest(), a.Digest());
        }
        Assert.Equal(b.Digest(), a.Digest());
    }
}
