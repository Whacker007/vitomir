// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Pulling a walk target inside the ground it landed on, and what happens when that cannot be done.
/// </summary>
public class ClampTests
{
    /// <summary>The monastery landing at the top of the stairs, and Christof's own radius.</summary>
    const string Level = "P1_MON2", Scene = "MON2_8_2";
    const int Standing = 787, Landing = 796;
    static readonly Vector3 Spot = new(2386, -287, -72);

    [Fact]
    public void Four_passes_is_not_always_enough_to_pull_a_point_clear()
    {
        // The shape of the failure, so the test below is known to be testing something. Node 796's
        // stored half-planes meet at an oblique angle — the left edge at x = 2368 and the diagonal
        // the stairs run along — and the loop pushes off one into the other and back. It converges
        // at a radius of 16 and does not at 32.
        using var files = Install.Open();
        var place = Location.Load(files, Level, things: false, sky: false, scene: Scene);
        var finder = new PathFinder(place.Level!);
        var floor = place.Walk!.Nodes[Landing].Plane;

        Assert.True(place.Walk.Nodes[Landing].Contains(new Vector2(Spot.X, Spot.Y)),
                    "the point is not even on the landing");
        var small = Spot; Assert.True(finder.ClampInside(Landing, ref small, 16f, floor));
        var big = Spot; Assert.False(finder.ClampInside(Landing, ref big, 32f, floor));
        // A failed clamp leaves the point exactly as it was (it writes back only on success).
        Assert.Equal(Spot, big);
    }

    [Fact]
    public void A_point_the_clamp_cannot_pull_clear_is_walked_at_anyway()
    {
        // **The clamp's return value is discarded by its only caller** (the move order), so
        // a point the engine cannot pull clear is walked at where it is and the walker's own
        // collision stops it short. It once did the opposite and
        // `Player.WalkTo` implemented that, which is why Christof could not be clicked up the
        // monastery stairs and a Cappadocian could not follow him.
        using var files = Install.Open();
        var place = Location.Load(files, Level, things: true, sky: false, scene: Scene, player: "Christof");
        var walker = place.Player!;
        Assert.Equal(Standing, walker.Node);

        var route = place.Paths!.Find(walker.Position, Standing, Spot, Landing, walker.Radius,
                                     isActor: false, out var waypoints);
        Assert.Equal(PathResult.Path, route);          // the graph has the edge; it always did
        Assert.NotEmpty(waypoints);
        Assert.True(walker.WalkTo(Spot, Landing), "the walk was refused");
    }

    [Fact]
    public void The_refusal_was_costing_about_one_walkable_spot_in_eleven()
    {
        // Measured over a grid of interior points on every piece of ground, at Christof's radius.
        // The number is here so that a change which makes the clamp converge — a different pass
        // count, a different push — is noticed rather than quietly reverting the fix above.
        using var files = Install.Open();
        var place = Location.Load(files, Level, things: false, sky: false, scene: Scene);
        var map = place.Walk!;
        var finder = new PathFinder(place.Level!);
        int tried = 0, refused = 0;
        for (int i = 0; i < map.Nodes.Length; i++)
        {
            var n = map.Nodes[i];
            if (n.Outline.Length == 0) continue;
            for (int gx = 1; gx <= 4; gx++)
                for (int gy = 1; gy <= 4; gy++)
                {
                    var p = new Vector2(n.Min.X + (n.Max.X - n.Min.X) * gx / 5f,
                                        n.Min.Y + (n.Max.Y - n.Min.Y) * gy / 5f);
                    if (!n.Contains(p)) continue;
                    var q = new Vector3(p, n.Height(p.X, p.Y));
                    tried++;
                    if (!finder.ClampInside(i, ref q, 32f, n.Plane)) refused++;
                }
        }
        Assert.True(tried > 9_000, $"only {tried} points sampled");
        // 952 of 10,314 on this level; 9.1% on the Old Town, 6.0% in the haven, 1.7% in East London.
        Assert.InRange(refused / (float)tried, 0.05f, 0.15f);
    }
}
