// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The world's share of the screen across while a pane is up:
/// a left pane gives it the right half and a right pane the left, each at half the lens — which draws
/// it larger — a pane on each side leaves it undrawn, and without the status bar nothing is halved.
/// And the pick through a half: the middle of the half is straight ahead.
/// </summary>
public class WorldViewTests
{
    [Fact]
    public void A_pane_on_one_side_leaves_the_world_the_other_half_and_a_pane_on_each_leaves_it_none()
    {
        Assert.Equal(ViewColumns.Whole, WorldView.Columns(barUp: true, leftPane: false, rightPane: false, 80f));
        var right = WorldView.Columns(true, leftPane: false, rightPane: true, 80f);
        Assert.Equal((0f, 0.5f), (right.Left, right.Right));
        var left = WorldView.Columns(true, leftPane: true, rightPane: false, 80f);
        Assert.Equal((0.5f, 1f), (left.Left, left.Right));
        Assert.False(WorldView.Columns(true, true, true, 80f).Drawn);
        // The halving is inside the bar's branch of the view's split.
        Assert.Equal(ViewColumns.Whole, WorldView.Columns(barUp: false, leftPane: true, rightPane: true, 80f));
    }

    [Fact]
    public void Half_the_lens_over_half_the_width_draws_the_world_larger_by_the_tangents()
    {
        // tan(40°) / (2 tan(20°)) at the game's 80.
        Assert.Equal(1.1527f, WorldView.Magnify(80f), 3);
        Assert.Equal(1.1527f, WorldView.Columns(true, false, true, 80f).Magnify, 3);
        Assert.Equal((800, 800), new ViewColumns(0.5f, 1f, 1f).Pixels(1600));
    }

    [Fact]
    public void The_middle_of_the_half_the_world_is_drawn_in_looks_straight_ahead()
    {
        var cam = new FreeCamera { Position = Vector3.Zero };
        var half = WorldView.Columns(true, leftPane: false, rightPane: true, cam.HorizontalFov43);
        var (_, ahead) = Pick.Ray(cam, 399.5f, 299.5f, 1600, 900, 600, half);
        Assert.True(Vector3.Dot(ahead, cam.Forward) > 0.9999f);
        // And a pixel at the half's edge is wider out than the same pixel of the whole view, by less
        // than the whole view's own edge: the half is magnified.
        var (_, edgeHalf) = Pick.Ray(cam, 0f, 299.5f, 1600, 900, 600, half);
        var (_, edgeWhole) = Pick.Ray(cam, 0f, 299.5f, 1600, 900, 600, ViewColumns.Whole);
        Assert.True(Vector3.Dot(edgeHalf, cam.Forward) > Vector3.Dot(edgeWhole, cam.Forward));
    }
}
