// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
//
// Adapted from DotRecast 2026.3.1's RcFilters.FilterLedgeSpans (a C# port of Recast 1.6's rcFilterLedgeSpans),
// altered for Vitomir: the steep-slope test takes its own limit. The original's notice (zlib):
//   Copyright (c) 2009-2010 Mikko Mononen memon@inside.org; recast4j copyright (c) 2015-2019 Piotr Piastucki
//   piotr@jtilia.org; DotRecast Copyright (c) 2023-2024 Choi Ikpil ikpil@naver.com.
//   This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held
//   liable for any damages arising from the use of this software. Permission is granted to anyone to use this software
//   for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the
//   following restrictions: 1. The origin of this software must not be misrepresented; you must not claim that you
//   wrote the original software. If you use this software in a product, an acknowledgment in the product
//   documentation would be appreciated but is not required. 2. Altered source versions must be plainly marked as such,
//   and must not be misrepresented as being the original software. 3. This notice may not be removed or altered from
//   any source distribution.
using DotRecast.Recast;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// <b>The stairs' ledge filter</b> (for cell levels; Recast's own, altered). Recast marks
/// a walkable span a <i>ledge</i> (not walkable) in two cases: a neighbour with room for the agent drops more than
/// the climb below it (the edge of a roof, a wall's top), and the spread between its highest and lowest accessible
/// neighbours (each within the climb) is more than the climb — "a steep slope". On a steep stair the second
/// catches the treads themselves: open-riser flights of 0.2 m risers on 0.22 m treads put a 0.15 m column between a
/// tread down and a tread up 0.39 m apart, 8 layers of 0.05 m against the human's climb of 7, and every flight of
/// the East Gate's towers lost its route though the body climbs them. Here the first case is Recast's unchanged and
/// the second is judged against <paramref name="spreadLimit"/> — twice the climb in the cook (a tread sees the tread
/// above and the one below) — so a single riser over the climb is still a ledge (the drop) and the slopes too steep
/// to walk are still not ground (their triangles are not walkable at all).
/// </summary>
public static class CellLedges
{
    /// <summary>
    /// Marks the ledges of <paramref name="heightfield"/> unwalkable, as Recast's <c>rcFilterLedgeSpans</c> does (all
    /// in voxels: the agent's <paramref name="walkableHeight"/> and <paramref name="walkableClimb"/>), but with the
    /// steep-slope spread allowed up to <paramref name="spreadLimit"/> (Recast's is the climb itself).
    /// </summary>
    public static void Filter(RcHeightfield heightfield, int walkableHeight, int walkableClimb, int spreadLimit)
    {
        int MaxHeight = RcRecast.RC_SPAN_MAX_HEIGHT;
        int xSize = heightfield.width, zSize = heightfield.height;
        for (int z = 0; z < zSize; ++z)
            for (int x = 0; x < xSize; ++x)
                for (RcSpan? span = heightfield.spans[x + z * xSize]; span != null; span = span.next)
                {
                    if (span.area == RcRecast.RC_NULL_AREA) continue;
                    int floor = span.smax;
                    int ceiling = span.next != null ? span.next.smin : MaxHeight;
                    // The lowest neighbour's floor under this one (any with room to pass), and the highest and lowest
                    // of those within the climb.
                    int lowestNeighborFloorDifference = MaxHeight;
                    int lowestTraversableNeighborFloor = span.smax;
                    int highestTraversableNeighborFloor = span.smax;
                    for (int direction = 0; direction < 4; ++direction)
                    {
                        int nx = x + RcRecast.GetDirOffsetX(direction);
                        int nz = z + RcRecast.GetDirOffsetY(direction);
                        if (nx < 0 || nz < 0 || nx >= xSize || nz >= zSize)
                        {
                            lowestNeighborFloorDifference = -walkableClimb - 1;
                            break;
                        }
                        RcSpan? neighborSpan = heightfield.spans[nx + nz * xSize];
                        // Under the neighbour's first span: room to drop to the bottom of the world.
                        int neighborCeiling = neighborSpan != null ? neighborSpan.smin : MaxHeight;
                        if (Math.Min(ceiling, neighborCeiling) - floor >= walkableHeight)
                        {
                            lowestNeighborFloorDifference = -walkableClimb - 1;
                            break;
                        }
                        for (; neighborSpan != null; neighborSpan = neighborSpan.next)
                        {
                            int neighborFloor = neighborSpan.smax;
                            neighborCeiling = neighborSpan.next != null ? neighborSpan.next.smin : MaxHeight;
                            if (Math.Min(ceiling, neighborCeiling) - Math.Max(floor, neighborFloor) < walkableHeight) continue;
                            int neighborFloorDifference = neighborFloor - floor;
                            lowestNeighborFloorDifference = Math.Min(lowestNeighborFloorDifference, neighborFloorDifference);
                            if (Math.Abs(neighborFloorDifference) <= walkableClimb)
                            {
                                lowestTraversableNeighborFloor = Math.Min(lowestTraversableNeighborFloor, neighborFloor);
                                highestTraversableNeighborFloor = Math.Max(highestTraversableNeighborFloor, neighborFloor);
                            }
                            else if (neighborFloorDifference < -walkableClimb)
                                break;   // a ledge already
                        }
                    }
                    if (lowestNeighborFloorDifference < -walkableClimb)
                        span.area = RcRecast.RC_NULL_AREA;                                     // a drop: Recast's
                    else if (highestTraversableNeighborFloor - lowestTraversableNeighborFloor > spreadLimit)
                        span.area = RcRecast.RC_NULL_AREA;                                     // too steep: ours
                }
    }
}
