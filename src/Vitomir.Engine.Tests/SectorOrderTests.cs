// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The order of a sector's things**. The engine links a thing in at the head of its sector's
/// list, both when it is placed and when it crosses into a new sector,
/// and the sphere query walks the list from the head — so the
/// newest arrival is met first. It decides whose shout is heard last in a fight, and with it whom the
/// rally leaves the selected member aiming at: in the story stretch's convent fight 1.1's Christof ends
/// on `anezka_torch` where Vitomir's, walking the list oldest first, ended on the other nun.
/// </summary>
public class SectorOrderTests
{
    [Fact]
    public void A_sectors_things_are_met_newest_first_and_a_newcomer_goes_to_the_head()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, player: "Christof");
        var bodies = place.Props!;
        var (sector, placed) = place.Things.Where(t => t.Sector >= 0 && t.Collide != CollideShape.None)
            .GroupBy(t => (int)t.Sector).Select(g => (g.Key, g.ToList())).First(g => g.Item2.Count >= 3);
        // Placed in the scene's order, each at the head: the last placed comes first.
        var met = bodies.In([sector]).ToList();
        Assert.Equal(Enumerable.Reverse(placed), met);

        // Out of the sector and back: it is the newest now, and the head.
        var oldest = placed[0];
        bodies.Moved(oldest, sector, -1);
        bodies.Moved(oldest, -1, sector);
        Assert.Same(oldest, bodies.In([sector]).First());
    }
}
