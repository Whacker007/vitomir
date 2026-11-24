// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Which cells a carried item goes into when it is put down on an item grid: as 1.1 finds them from the grid's own
// cell lookup, its fit test and the carried icon's cursor,
// and the quality-of-life switch `dropcells` over the rounding.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **An item goes where its picture is, not where the pointer is.** The drop asks
/// the grid for the cell through its virtual cell lookup, which for an item grid (the class used by
/// the pack, the vault, a container, the quick-use strip) is:
///
/// <code>
/// x = trunc((pointer.x − cursor.hotspot.x + cellWidth × 0.5) / cellWidth)
/// y = trunc((pointer.y − cursor.hotspot.y + cellHeight × 0.5) / cellHeight)
/// </code>
///
/// with the pointer the grid's own (from its left edge and down from its top),
/// the cursor the one in use and the half cell (0.5). The carried item's
/// cursor is its icon, made with **anchor 0**, whose hotspot is half its size
/// (27 × 32 a cell) — so the icon is drawn centred on the pointer and the sum is **the drawn top-left
/// corner, rounded to the nearest cell**. <c>trunc</c> is toward zero, which is rounding only for a
/// corner inside the grid: one hanging off the top or left by up to a cell and a half comes out as row or
/// column 0, where one hanging off the right or bottom by half a cell or more is out of the grid. The fit
/// test (through the overlap count, which is -1 when it fails) then refuses a
/// footprint that does not lie inside the grid, and the drop handler refuses on anything but 0 or 1.
///
/// For a 1 × 1 item the sum is the cell under the pointer; only a bigger one lands elsewhere. **Until
/// Vitomir took the cell under the pointer as the item's top-left** for every item and pulled a
/// footprint that hung off the grid back into it — so a 2 × 3 sword held by its middle went a cell right
/// and a cell down of where it was drawn as play showed. Now 1.1's rule, with no switch.
///
/// **The switch** (<see cref="Qol.DropCells"/>, ours, on): the corner rounded to the nearest cell at every
/// edge (<c>floor(x + 0.5)</c>), so an item that mostly hangs off the top or left is refused as it is at
/// the other two. Off, 1.1's truncation.
///
/// Only the item grids use this. A worn slot is one cell and takes the item wherever it is dropped,
/// and the quick-use strip takes only 1 × 1 items (<see cref="InventoryPane.Fits"/>), for which
/// the corner's cell is the pointer's.
/// </summary>
public static class DropCell
{
    /// <summary>
    /// The top-left cell <paramref name="item"/> goes into when put down at <paramref name="spot"/> — which
    /// may be outside the grid (<see cref="Inside"/>). A spot named by its cell, not by a point, is that cell.
    /// </summary>
    public static (int X, int Y) Of(Item item, InventoryPane.Spot spot, Qol? rules = null)
    {
        if (spot.Pointer is not { } p) return (spot.X, spot.Y);
        bool nearest = (rules ?? Qol.Current).DropCells;
        var (across, down) = item.IconCells;
        return (Axis(p.X, across * InventoryPane.CellWidth, InventoryPane.CellWidth, nearest),
                Axis(p.Y, down * InventoryPane.CellHeight, InventoryPane.CellHeight, nearest));
    }

    /// <summary>
    /// One axis of the cell lookup: the pointer less the hotspot (half the icon, anchor 0), plus half a
    /// cell, over a cell — truncated toward zero, or with <paramref name="nearest"/> floored, so
    /// that a corner above or left of the grid rounds the same way as one inside it.
    /// </summary>
    public static int Axis(int pointer, int size, int cell, bool nearest)
    {
        float at = (pointer - size * 0.5f + cell * 0.5f) / cell;
        return nearest ? (int)MathF.Floor(at) : (int)at;
    }

    /// <summary>Whether an item's footprint from a top-left cell lies inside the grid — the fit test (its cell, then the far edges).</summary>
    public static bool Inside(Item item, InventoryGrid grid, int x, int y)
    {
        var (across, down) = item.IconCells;
        return x >= 0 && y >= 0 && x + across <= grid.Across && y + down <= grid.Down;
    }
}
