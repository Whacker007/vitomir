// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Characters;
using Vitomir.Platform.Render.Modern;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// The people's skinning when several are about (<see cref="PeopleSkinning"/>; on the CPU, no GPU): one out of view, its
/// shadow's reach too, is left out; a distant one is skinned every few frames, staggered, and drawn again between moved by how far it went;
/// <see cref="PeopleSkinning.EveryFrame"/> is the earlier build's; 1.1's characters and the viewer's own body are skinned every frame as before; a warm
/// frame allocates nothing.
/// </summary>
public sealed class PeopleSkinningTests
{
    /// <summary>The eye at the origin looking along +X, level, 60° up the picture, 16:9.</summary>
    static SkinView Eye() => new(new ViewPoint(Vector3.Zero, 0f, 0f, 60f), 16f / 9f);

    static SkinnedModelHolder Person() => new(TestPeople.Board(new(TestPeople.Plain("a")), new(TestPeople.Plain("b"))));

    sealed record SkinnedModelHolder(Assets.SkinnedModel Model)
    {
        public DrawnCharacter At(Vector3 feet, ulong entity = 1) => new() { Model = Model, World = Matrix4x4.CreateTranslation(feet), Skin = [Matrix4x4.Identity], Entity = entity };
    }

    [Fact]
    public void The_view_s_cone_sees_ahead_and_beside_within_the_picture_s_corners_and_not_behind()
    {
        var eye = Eye();
        Assert.True(eye.Sees(new Vector3(10, 0, 0), 0.5f));
        Assert.True(eye.Sees(new Vector3(10, 8, 0), 0.5f));       // 39° aside: within the corners' 50°
        Assert.False(eye.Sees(new Vector3(10, 30, 0), 0.5f));     // 72° aside
        Assert.True(eye.Sees(new Vector3(10, 30, 0), 15f));       // ... unless it is large (or its shadow reaches in)
        Assert.False(eye.Sees(new Vector3(-10, 0, 0), 1f));       // behind
        Assert.True(eye.Sees(new Vector3(-0.5f, 0, 0), 1f));      // round the eye
        Assert.Equal(10f, eye.Distance(new Vector3(10, 0, 0)), 4);
        Assert.True(default(SkinView).Sees(new Vector3(-10, 0, 0), 0f));   // no view: everything
        var s = PeopleSkinning.Default;
        Assert.Equal((1, 1, 2, 2, 4), (s.Every(0f), s.Every(12f), s.Every(12.1f), s.Every(30f), s.Every(31f)));
        Assert.Equal(1, PeopleSkinning.EveryFrame.Every(100f));
    }

    [Fact]
    public void A_person_out_of_view_is_left_out_and_one_whose_shadow_may_reach_in_is_kept()
    {
        var p = Person();
        using var batch = new CharacterBatch(new CountingTextures(), []);
        var frame = new List<DrawnCharacter> { p.At(new Vector3(5, 0, 0), 1), p.At(new Vector3(-20, 0, 0), 2), p.At(new Vector3(-2, 5, 0), 3) };
        batch.Build(frame, Eye());
        Assert.Equal((2, 0, 1), batch.LastPeople);                  // the one 20 m behind is out; the one 2 m behind and aside casts in
        Assert.Equal(2 * p.Model.Vertices.Length, batch.Vertices.Length);
        batch.Skinning = PeopleSkinning.Default with { ShadowReach = 0f };
        batch.Build(frame, Eye());
        Assert.Equal((1, 0, 2), batch.LastPeople);                  // ... and without the shadow's reach it is out too
        batch.Skinning = PeopleSkinning.EveryFrame;
        batch.Build(frame, Eye());
        Assert.Equal((3, 0, 0), batch.LastPeople);                  // everyone
        batch.Skinning = PeopleSkinning.Default with { Cull = false };
        batch.Build(frame, Eye());
        Assert.Equal(0, batch.LastPeople.Culled);
        // No view (the old entry): everyone, every frame.
        batch.Skinning = PeopleSkinning.Default;
        batch.Build(frame);
        Assert.Equal((3, 0, 0), batch.LastPeople);
    }

    [Fact]
    public void A_distant_person_is_skinned_every_few_frames_staggered_and_drawn_again_between_moved_with_it()
    {
        var p = Person();
        using var batch = new CharacterBatch(new CountingTextures(), []);
        var far = p.At(new Vector3(40, 0, 0), 5);                   // every 4th frame
        var near = p.At(new Vector3(5, 0, 0), 6);                   // every frame
        var frame = new List<DrawnCharacter> { near, far };
        int n = p.Model.Vertices.Length;
        var skinnedFar = new List<int>();
        Vector3 lastOrigin = default;
        Vector3[] last = [];
        for (int f = 0; f < 12; f++)
        {
            var feet = new Vector3(40 + 0.02f * f, 0.01f * f, 0);  // walking away a little each frame
            far.World = Matrix4x4.CreateTranslation(feet);
            far.Skin[0] = Matrix4x4.CreateRotationZ(0.01f * f);     // its pose changing each frame
            batch.Build(frame, Eye());
            var (skinned, reused, culled) = batch.LastPeople;
            Assert.Equal(0, culled);
            var mine = batch.Vertices.Slice(n, n).ToArray().Select(v => v.Position).ToArray();
            var origin = feet * ModernMath.UnitsPerMetre;
            if (skinned == 2)
            {
                skinnedFar.Add(f);
                var fresh = new Vector3[n];
                for (int i = 0; i < n; i++) fresh[i] = Vector3.Transform(Vector3.Transform(p.Model.Vertices[i].Position, far.Skin[0]), far.World * Matrix4x4.CreateScale(ModernMath.UnitsPerMetre));
                for (int i = 0; i < n; i++) Assert.True(Vector3.Distance(fresh[i], mine[i]) < 1e-3f, $"frame {f}: skinned as it stands");
            }
            else
            {
                Assert.Equal((1, 1), (skinned, reused));
                // Drawn again: the last skinning moved by how far it went since.
                for (int i = 0; i < n; i++) Assert.True(Vector3.Distance(last[i] + (origin - lastOrigin), mine[i]) < 1e-3f, $"frame {f}: carried along");
                continue;
            }
            (last, lastOrigin) = (mine, origin);
        }
        // The first frame, then every 4th, staggered by its entity (5 % 4 = 1: its second skinning a frame early).
        Assert.Equal([0, 3, 7, 11], skinnedFar);
    }

    [Fact]
    public void The_viewer_s_own_body_and_one_one_s_characters_are_skinned_every_frame_wherever_they_are()
    {
        var p = Person();
        using var batch = new CharacterBatch(new CountingTextures(), []);
        var own = p.At(new Vector3(-50, 0, 0), 1);
        own.ShadowSkin = [Matrix4x4.Identity];
        var oneOne = new DrawnCharacter { Model = TestColumn.Make(), World = Matrix4x4.CreateTranslation(-50, 0, 0), Skin = TestColumn.Bent(0.3f) };
        batch.Build([own, oneOne], Eye());
        Assert.Equal((1, 0, 0), batch.LastPeople);   // the own body skinned (1.1's is not a person), nothing culled
        Assert.True(batch.Vertices.Length >= p.Model.Vertices.Length + oneOne.Model.Vertices.Length);
    }

    [Fact]
    public void A_warm_frame_of_people_near_far_and_out_of_view_allocates_nothing()
    {
        var p = Person();
        using var batch = new CharacterBatch(new CountingTextures(), []);
        var frame = new List<DrawnCharacter> { p.At(new Vector3(5, 0, 0), 1), p.At(new Vector3(20, 1, 0), 2), p.At(new Vector3(45, -2, 0), 3), p.At(new Vector3(-30, 0, 0), 4) };
        IReadOnlyList<DrawnCharacter> characters = frame;
        var eye = Eye();
        for (int i = 0; i < 8; i++) batch.Build(characters, eye);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 8; i++) batch.Build(characters, eye);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
