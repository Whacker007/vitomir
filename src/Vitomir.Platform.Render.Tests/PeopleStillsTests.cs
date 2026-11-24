// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Characters;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// The models repository's player drawn (<c>VITOMIR_MODELS_DIR</c> and the East Gate; skipped without them):
/// at rest and in a pose of <c>walk</c>, from the front, the side and the back, his face and his right hand close, and in first
/// person with his head collapsed; the stills written to <c>VITOMIR_RENDER_OUT</c> when it is set, to be looked at (without it,
/// the front alone, small, his textures at 1024). What is asked of the pictures here is only that he is there; the rest is for
/// the eye.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class PeopleStillsTests
{
    static PeopleLibrary Library()
    {
        var folder = Environment.GetEnvironmentVariable("VITOMIR_MODELS_DIR");
        Assert.SkipUnless(folder is { Length: > 0 } && Directory.Exists(folder), "no models repository's files (set VITOMIR_MODELS_DIR)");
        return PeopleLibrary.TryOpen(folder!, out var why) ?? throw new InvalidOperationException(why);
    }

    /// <summary>A line of what was found, in the test's output (and <c>people-stills.txt</c> beside the stills when they are written).</summary>
    static void Say(string line)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(line);
        if (Environment.GetEnvironmentVariable("VITOMIR_RENDER_OUT") is { Length: > 0 } folder)
            File.AppendAllText(Path.Combine(folder, "people-stills.txt"), line + Environment.NewLine);
    }

    /// <summary>Whether the stills are being looked at (<c>VITOMIR_RENDER_OUT</c> names a folder for them).</summary>
    static bool Looking => Environment.GetEnvironmentVariable("VITOMIR_RENDER_OUT") is { Length: > 0 };

    /// <summary>
    /// The stills to draw: <c>VITOMIR_PEOPLE_STILLS</c>'s (names, comma separated; a harness aid), else all when they are looked at,
    /// else only the front (the whole set at 4096 wants about a gigabyte, and the suites share the machine).
    /// </summary>
    static bool Wanted(string name) =>
        Environment.GetEnvironmentVariable("VITOMIR_PEOPLE_STILLS") is { Length: > 0 } only ? only.Split(',').Contains(name)
        : Looking || name == "player-front";

    /// <summary>The skin matrices of <paramref name="local"/> on <paramref name="skeleton"/>, and the bones' model matrices.</summary>
    static Matrix4x4[] Skin(Skeleton skeleton, BoneTransform[] local, out Matrix4x4[] model)
    {
        model = new Matrix4x4[skeleton.Count];
        var skin = new Matrix4x4[skeleton.Count];
        PoseEvaluator.ToModel(skeleton, local, model);
        PoseEvaluator.ToSkin(skeleton, model, skin);
        return skin;
    }

    static ViewPoint Look(Vector3 eye, Vector3 at, float fov, float near = 0f)
    {
        var d = at - eye;
        return new ViewPoint(eye, MathF.Atan2(d.Y, d.X), MathF.Atan2(d.Z, MathF.Sqrt(d.X * d.X + d.Y * d.Y)), fov, near);
    }

    [Fact]
    public void The_player_is_drawn_from_every_side_close_and_in_first_person()
    {
        var level = Stills.Level();
        var library = Library();
        var clock = Stopwatch.StartNew();
        var person = library.Person(PersonRecipe.Player);
        Say($"Person(Player): {clock.Elapsed.TotalSeconds:0.0} s; {person.Model.Vertices.Length:N0} vertices, {person.Model.Indices.Length / 3:N0} triangles, "
            + $"{person.Model.Parts.Length} parts, {person.Model.Skeleton.Count} bones, {person.Textures.Count} textures, {person.HiddenTriangles:N0} body triangles hidden");
        var model = person.Model;
        var skeleton = model.Skeleton;
        var textures = person.Textures.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var part in model.Parts)
            if (part.Material is { } m)
                Say($"  part {m.Name}: {part.IndexCount / 3:N0} triangles, base {m.BaseColor ?? "-"}, normal {m.Normal ?? "-"} ×{m.NormalScale}, orm {m.Orm ?? "-"}, "
                    + $"factor {m.BaseColorFactor}, rough {m.Roughness}, metal {m.Metallic}, cut {m.AlphaCutoff}, {(m.DoubleSided ? "two" : "one")}-sided"
                    + $"{(m.VertexColor ? ", vertex colour" : "")}{(m.Hidden ? ", hidden" : "")}");

        var mesh = PeopleMesh.For(model)!;
        Say($"drawn: {mesh.Vertices.Length:N0} of {model.Vertices.Length:N0} vertices skinned ({mesh.Unused:N0} unused), {mesh.Indices.Length / 3:N0} triangles, {mesh.Bones} bones named");

        var rest = new BoneTransform[skeleton.Count];
        PoseEvaluator.Rest(skeleton, rest);
        var restSkin = Skin(skeleton, rest, out var restBones);
        var walk = library.Motions("male").First(c => c.Name == "walk");
        var walking = new BoneTransform[skeleton.Count];
        walk.Sample(walk.Duration * 0.3f, skeleton, walking);
        var walkSkin = Skin(skeleton, walking, out _);

        // The CPU's cost a frame of skinning him (both ways), as the renderer does it.
        using (var batch = new CharacterBatch(new CountingTextures(), []))
        {
            var frame = new List<DrawnCharacter> { new() { Model = model, World = Matrix4x4.Identity, Skin = walkSkin } };
            // The fastest of many frames (the machine is shared: the slower ones measure the others' work), and the middle one.
            (double Fastest, double Middle) Cost(bool parallel)
            {
                batch.Parallel = parallel;
                for (int i = 0; i < 60; i++) batch.Build(frame);
                var times = new double[60];
                for (int i = 0; i < times.Length; i++)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    batch.Build(frame);
                    times[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                }
                Array.Sort(times);
                return (times[0], times[times.Length / 2]);
            }
            var one = Cost(false);
            var many = Cost(true);
            Say($"skinning the player a frame (third person): {1 + SkinWorkers.Helpers} threads {many.Fastest:0.00} ms at best ({many.Middle:0.00} the middle), "
                + $"one thread {one.Fastest:0.00} ms ({one.Middle:0.00})");
            var collapsedSkin = (Matrix4x4[])walkSkin.Clone();
            HeadCollapse.For(skeleton, person.Rig.Head, model.Vertices)!.Apply(collapsedSkin);
            frame[0] = new DrawnCharacter { Model = model, World = Matrix4x4.Identity, Skin = collapsedSkin, ShadowSkin = walkSkin };
            var firstOne = Cost(false);
            var firstMany = Cost(true);
            var plan = FirstPersonMesh.For(PeopleMesh.For(model)!, collapsedSkin, walkSkin);
            Say($"first person (whole for the shadow, {plan.Extra.Length:N0} vertices again for the picture, {plan.Indices.Length / 3:N0} triangles seen): "
                + $"{1 + SkinWorkers.Helpers} threads {firstMany.Fastest:0.00} ms ({firstMany.Middle:0.00}), one thread {firstOne.Fastest:0.00} ms ({firstOne.Middle:0.00})");
        }
        // His textures' GPU memory at each limit (RGBA8 with mips; the same pixels once).
        foreach (int limit in CharacterTextureSize.Limits)
        {
            long bytes = 0;
            foreach (var t in person.Textures.DistinctBy(t => t.Rgba))
            {
                int tw = t.Width, th = t.Height;
                while (tw > limit || th > limit) { tw = Math.Max(1, tw / 2); th = Math.Max(1, th / 2); }
                bytes += CharacterTextureSize.GpuBytes(tw, th);
            }
            Say($"textures at {limit}: {bytes / 1048576.0:0} MB");
        }

        // Where he stands: 3 m ahead of the first start, facing back toward it (his model's +Y to the start).
        var (start, startYaw) = Stills.FirstStart(level);
        var ahead = new Vector3(MathF.Cos(startYaw), MathF.Sin(startYaw), 0);
        var feet = start + ahead * 3f;
        float facing = startYaw + MathF.PI;
        var place = Matrix4x4.CreateRotationZ(facing - MathF.PI / 2) * Matrix4x4.CreateTranslation(feet);
        Vector3 Toward(float turn) => new(MathF.Cos(facing + turn), MathF.Sin(facing + turn), 0);
        var up = Vector3.UnitZ;

        var (w, h) = Looking ? Stills.Size(960, 540) : Stills.Size(480, 270);
        string quality = Environment.GetEnvironmentVariable("VITOMIR_RENDER_QUALITY") ?? "medium";
        var collapse = HeadCollapse.For(skeleton, person.Rig.Head, model.Vertices)!;
        // One renderer for the stills (his textures uploaded once: about 400 MB at 4096, and the machine is shared), made again
        // only for another texture limit; each view drawn as many times as a still needs, twice over, so the temporal passes
        // forget the view before.
        PlatformRenderer? renderer = null;
        int rendererLimit = -1;
        try
        {
            void Still(string name, ViewPoint view, Matrix4x4[] skin, Matrix4x4[]? shadow = null, int limit = 0)
            {
                if (!Wanted(name)) return;
                if (renderer is null || rendererLimit != limit)
                {
                    renderer?.Dispose();
                    renderer = null;
                    GC.Collect();
                    // Not looked at, his textures at 1024 (100 MB): the suites share the machine's memory.
                    renderer = PlatformRenderer.CreateHeadless(w, h, new RendererOptions(Quality: quality, CharacterTextureLimit: Looking ? limit : 1024));
                    renderer.LoadLevel(level);
                    renderer.CharacterTextures = n => textures.GetValueOrDefault(n);
                    // VITOMIR_KEEP_COVERAGE=0: the cut-outs with the GPU's own mips, to compare (a harness aid).
                    renderer.KeepCoverage = Environment.GetEnvironmentVariable("VITOMIR_KEEP_COVERAGE") != "0";
                    rendererLimit = limit;
                }
                var r = renderer;
                // VITOMIR_FIRST_PERSON_CUT: first person's cut for these stills (a tuning aid).
                if (float.TryParse(Environment.GetEnvironmentVariable("VITOMIR_FIRST_PERSON_CUT"), System.Globalization.CultureInfo.InvariantCulture, out float cut))
                    r.FirstPersonCut = cut;
                var frame = new FrameView { View = view, ShowReticle = false };
                Stills.Still(r, frame);
                Stills.Still(r, frame);
                var bare = r.Pixels(out _, out _);
                frame.Characters.Add(new DrawnCharacter { Model = model, World = place, Skin = skin, ShadowSkin = shadow, Entity = 1 });
                Stills.Still(r, frame);
                Stills.Still(r, frame);
                Stills.Save(r, name);
                var px = r.Pixels(out _, out _);
                float changed = Stills.Differing(bare, px, 10);
                Say($"{name}: {changed:P0} of the picture is him; textures {r.CharacterTextureBytes / 1048576.0:0} MB at {r.CharacterTextureLimit}");
                Assert.True(changed > 0.01f, $"{name}: he is drawn");
            }

            Still("player-front", Look(feet + Toward(0) * 2.8f + up * 1.1f, feet + up * 0.95f, 45f), restSkin);
            Still("player-far", Look(feet + Toward(0.3f) * 9f + up * 1.6f, feet + up * 1.2f, 25f), restSkin);
            Still("player-side", Look(feet + Toward(MathF.PI / 2) * 2.8f + up * 1.1f, feet + up * 0.95f, 45f), restSkin);
            Still("player-back", Look(feet + Toward(MathF.PI) * 2.8f + up * 1.1f, feet + up * 0.95f, 45f), restSkin);
            var head = Vector3.Transform(restBones[person.Rig.Head].Translation, place);
            Still("player-face", Look(head + Toward(0.35f) * 0.55f + up * 0.04f, head + up * 0.06f, 32f), restSkin);
            var hand = Vector3.Transform(restBones[person.Rig.RightArm.End].Translation, place);
            Still("player-eyes", Look(head + Toward(0.2f) * 0.32f + up * 0.07f, head + up * 0.07f + Toward(0) * 0.08f, 20f), restSkin);
            Still("player-hand", Look(hand + Toward(-0.6f) * 0.5f + up * 0.1f, hand, 32f), restSkin);
            Still("player-walk-front", Look(feet + Toward(0.25f) * 2.8f + up * 1.1f, feet + up * 0.95f, 45f), walkSkin);
            Still("player-walk-side", Look(feet + Toward(MathF.PI / 2) * 2.8f + up * 1.1f, feet + up * 0.95f, 45f), walkSkin);
            // First person: the head (and all on it) collapsed in the picture, the shadow cast whole.
            var collapsed = (Matrix4x4[])walkSkin.Clone();
            var eyes = Vector3.Transform(collapse.EyesIn(walkSkin), place);
            collapse.Apply(collapsed);
            Still("player-first-person", new ViewPoint(eyes, facing, -55f * MathF.PI / 180f, 80f, FirstPersonView.NearPlane), collapsed, shadow: walkSkin);
            var restCollapsed = (Matrix4x4[])restSkin.Clone();
            collapse.Apply(restCollapsed);
            Still("player-first-person-rest", new ViewPoint(Vector3.Transform(collapse.EyesIn(restSkin), place), facing, -55f * MathF.PI / 180f, 80f, FirstPersonView.NearPlane),
                  restCollapsed, shadow: restSkin);
            Still("player-first-person-collar", new ViewPoint(eyes, facing, -80f * MathF.PI / 180f, 80f, FirstPersonView.NearPlane), collapsed, shadow: walkSkin);
            Still("player-collapse-outside", Look(feet + Toward(0.6f) * 2.6f + up * 1.6f, feet + up * 0.9f, 45f), collapsed, shadow: walkSkin);
            Still("player-collapse-neck", Look(feet + Toward(0.6f) * 0.8f + up * 1.75f, feet + up * 1.45f, 35f), collapsed, shadow: walkSkin);
            Still("player-face-4096", Look(head + Toward(0.35f) * 0.55f + up * 0.04f, head + up * 0.06f, 32f), restSkin, limit: 4096);
        }
        finally
        {
            renderer?.Dispose();
        }
    }
}
