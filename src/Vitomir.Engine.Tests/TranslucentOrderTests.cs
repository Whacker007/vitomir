// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Scene;

namespace Vitomir.Engine.Tests;

/// <summary>
/// 1.1's translucent order: rlDirect holds the level's blended groups back
/// and draws them at its flush, after the translucent things the world draw
/// has sorted and drawn farthest first — and it draws its own list by the nearest point's
/// screen depth, least first. Earlier, Vitomir sorted the two together,
/// farthest first, which blended a ghost standing behind a stained window over the window.
/// </summary>
public class TranslucentOrderTests
{
    static DrawBatch Batch(string name, Vector3 centre, BatchLayer layer) =>
        new(0, Lighting.Vertex, 0, false, 1f, 0, 0, centre, name, layer, layer == BatchLayer.Thing ? 0 : -1);

    // The eye at the origin looking along +x (the renderer's convention: z up).
    static readonly Matrix4x4 ViewProj =
        Matrix4x4.CreateLookAt(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ)
        * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 4f / 3f, 4f, 4000f);

    [Fact]
    public void A_ghost_behind_a_window_is_drawn_before_the_window_and_so_is_a_flame_in_front_of_it()
    {
        var window = Batch("window", new(200, 0, 0), BatchLayer.Level);
        var ghost = Batch("ghost", new(300, 0, 0), BatchLayer.Thing);
        var flame = Batch("flame", new(100, 0, 0), BatchLayer.Thing);
        var order = TranslucentOrder.Order([window, flame, ghost], Vector3.Zero, b => 0.5f);
        Assert.Equal(["ghost", "flame", "window"], order.Select(b => b.Source));
    }

    [Fact]
    public void The_levels_translucent_surfaces_are_drawn_nearest_point_first()
    {
        var near = Batch("near", new(100, 0, 0), BatchLayer.Level);
        var far = Batch("far", new(900, 0, 0), BatchLayer.Level);
        var reaching = Batch("reaching", new(1000, 0, 0), BatchLayer.Level);   // centre far, one corner nearest of all
        var points = new Dictionary<DrawBatch, Vector3[]>
        {
            [near] = [new(100, -10, 0), new(100, 10, 0)],
            [far] = [new(900, -10, 0), new(900, 10, 0)],
            [reaching] = [new(50, 0, 0), new(1950, 0, 0)],
        };
        var order = TranslucentOrder.Order([far, near, reaching], Vector3.Zero,
                                           b => TranslucentOrder.Key(points[b], ViewProj));
        Assert.Equal(["reaching", "near", "far"], order.Select(b => b.Source));
    }

    [Fact]
    public void A_point_behind_the_eye_keys_as_the_near_plane()
    {
        Assert.Equal(0f, TranslucentOrder.Key([new Vector3(-50, 0, 0), new Vector3(500, 0, 0)], ViewProj));
        Assert.True(TranslucentOrder.Key([new Vector3(500, 0, 0)], ViewProj) > TranslucentOrder.Key([new Vector3(100, 0, 0)], ViewProj));
    }
}
