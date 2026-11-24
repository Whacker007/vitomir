// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The repair `packcells`: a character's pack, belt and vault made sane as a save is
// loaded: saves made before it carry used-up potions at −1..−5 and two
// potions on one belt cell, written while Vitomir left a consumable's last charge in the list.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Save;

/// <summary>
/// **A load leaves no used-up consumable behind and no two items on one cell.**
///
/// 1.1 takes a loaded item's cell on trust: two records naming one cell are drawn one over the other, and a click finds whichever the list
/// meets first. 1.1 never writes such a save — its placement search looks for a free cell
/// and its spend destroys the last charge (<see cref="UsedUp"/>) — but Vitomir did, until
/// And a save carries that on. So as each character's items are made again
/// (<see cref="SaveApply"/>), two things are put right, and neither has a switch (<see cref="Repairs.PackCells"/>):
///
/// <list type="number">
/// <item>**A used-up consumable is not made again**: an item that is
/// <see cref="ItemFlags.DestroyedAtZeroCharges"/>, has no charge left (amount ≤ 0) and whose template
/// starts it with some. 1.1 would have destroyed it at its last use; what a save holds of it is only
/// the leak. A letter (<c>CuthbertLetter</c>: that flag, and 0 charges from its template) and a spent
/// item that is not destroyed at zero (<see cref="ItemFlags.Spent"/>, dead weight in 1.1 too) are kept.</item>
/// <item>**Every item in a grid gets a cell of its own.** The pack, the belt and the vault are walked
/// in the character's own list order — the order the strip and the pane find an item by, so the one
/// the player saw and clicked keeps its cell — and an item whose footprint leaves its grid or covers
/// one already seated is put in the first free cell of the same grid by 1.1's own search
/// (<see cref="Stow.Place"/>, row by row from the top left). When that grid is full, a one-cell item
/// goes to the belt from the pack or to the pack from the belt, as the pick-up's cascade would try
/// them, and anything else to the pack; with no room anywhere it stays where it was, owned
/// and overlapping (ours: nothing is thrown away to make room).</item>
/// </list>
///
/// The cell a save omits is (0, 0) (<c>SaveCapture</c> writes no <c>cell</c> for it), so a record with
/// no <c>cell</c> is at the grid's top-left corner, not lost.
/// </summary>
public static class LoadedPack
{
    /// <summary>One thing the mend did, for the log and for a test.</summary>
    public readonly record struct Change(Item Item, string What);

    /// <summary>The containers, in the order they are seated: the belt before the pack, whose one-cell overflow it takes.</summary>
    static readonly int[] Grids = [Slot.QuickUse, Slot.Inventory, Slot.Vault];

    /// <summary>
    /// Whether <paramref name="item"/> is a consumable used to nothing that 1.1 would have destroyed:
    /// destroyed at zero charges, none left, and <paramref name="startedWith"/> — its template's
    /// <c>ITEM_AMOUNT</c> — more than none.
    /// </summary>
    public static bool UsedToNothing(Item item, int startedWith) =>
        (item.Flags & ItemFlags.DestroyedAtZeroCharges) != 0 && item.Amount <= 0 && startedWith > 0;

    /// <summary>
    /// Mends <paramref name="actor"/>'s carried items in place. <paramref name="startedWith"/> answers an
    /// item's template amount; <paramref name="dropped"/> is told of each used-up item taken out of the
    /// list, so the caller can let its thing go. Answers what was changed.
    /// </summary>
    public static List<Change> Mend(Actor actor, Func<Item, int> startedWith, Action<Item>? dropped = null)
    {
        var changes = new List<Change>();
        foreach (var item in actor.Inventory.Items.ToArray())
        {
            if (!UsedToNothing(item, startedWith(item))) continue;
            actor.Inventory.Remove(item);
            changes.Add(new(item, $"{item.Name} at {item.Amount} (used up) not made again"));
            dropped?.Invoke(item);
        }
        if (actor.Held is { } held && UsedToNothing(held, startedWith(held)))
        {
            actor.Held = null;
            changes.Add(new(held, $"{held.Name} on the pointer at {held.Amount} (used up) not made again"));
            dropped?.Invoke(held);
        }

        // Seat each grid in list order; whatever will not sit where its record says is set aside.
        var loose = new List<Item>();
        foreach (int grid in Grids)
        {
            var (width, height) = Slot.Size(grid);
            var taken = new bool[width, height];
            foreach (var item in actor.Inventory.Items)
            {
                if (item.Location != grid || item.Destroyed) continue;
                var (across, down) = item.IconCells;
                var (x, y) = item.Cell;
                bool fits = x >= 0 && y >= 0 && x + across <= width && y + down <= height
                            && (grid != Slot.QuickUse || (across, down) == (1, 1));
                for (int dx = 0; fits && dx < across; dx++)
                    for (int dy = 0; fits && dy < down; dy++)
                        if (taken[x + dx, y + dy]) fits = false;
                if (!fits) { loose.Add(item); continue; }
                for (int dx = 0; dx < across; dx++)
                    for (int dy = 0; dy < down; dy++)
                        taken[x + dx, y + dy] = true;
            }
        }

        // Out of the way while they are placed, so the search counts none of them against another.
        var from = loose.Select(i => (Item: i, Was: i.Location, Cell: i.Cell)).ToList();
        foreach (var item in loose) item.Location = Slot.None;
        foreach (var (item, was, cell) in from)
        {
            int[] tries = was switch
            {
                Slot.Inventory => [Slot.Inventory, Slot.QuickUse],
                Slot.QuickUse => [Slot.QuickUse, Slot.Inventory],
                _ => [was, Slot.Inventory, Slot.QuickUse],
            };
            bool placed = false;
            foreach (int to in tries)
                if (Stow.Place(actor, item, to)) { placed = true; break; }
            if (placed)
                changes.Add(new(item, $"{item.Name} from {Slot.Name(was)} {cell.X},{cell.Y} to {Slot.Name(item.Location)} {item.Cell.X},{item.Cell.Y}"));
            else
            {
                item.Location = was;
                item.Cell = cell;
                changes.Add(new(item, $"{item.Name} left at {Slot.Name(was)} {cell.X},{cell.Y}: no free cell anywhere"));
            }
        }
        return changes;
    }
}
