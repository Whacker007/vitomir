// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The inventory pane, as state rather than as pixels — the third and last of the three, and the only
// one with real mechanics behind it. `Vitomir.Render.InventoryPass` puts it up.
//
// **Almost all of this was already settled before a line of it was written**, which is why it is
// short: the nine-bit slot mask was closed by save experiments; `ui/inventoryPane.nui`
// declares every grid with its own name, position and cell count; `ENUM_ICONSIZE` decides what fits
// where; and the cell is 27 x 32 pixels everywhere in the game. What this file adds is the join.
//
// **One thing was nearly written down wrong, and saves refuted it.** The pane's fill
// switches on an item's slot mask into a grid index — a switch over a byte table
// — and the pane's constructor looks its grids up by name in a fixed order. It
// is tempting to read the lookup order as the array order and so derive a mask-to-slot table from
// it. Doing that gives `0x2 = body`, `0x4 = right weapon`, `0x10 = right hand`. **The saves say
// otherwise and they are unambiguous**: `skullcap` 0x1, `amuletwit` 0x2, `rags` 0x4, `broadsword`
// 0x8, `torchu` 0x10, `silverbracelet` 0x20 and 0x40, `ringman` 0x80, `ringapp` 0x100. So the
// engine's grid array is not in its lookup order, the mask is the one the saves showed,
// and the array order is not read — it does not need to be, because this pane keys by slot name.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// One grid on the pane: the element that declares it, the slot or container it shows, and how many
/// cells it has. A `weargrid` holds one item; an `icongrid` holds many.
/// </summary>
/// <param name="Element">The `.nui` name, which is also how the drawing side finds its rectangle.</param>
/// <param name="Slot">The <see cref="Rpg.Slot"/> value this grid shows.</param>
/// <param name="Across">Cells across, from the element's own `gridsize`.</param>
/// <param name="Down">Cells down.</param>
public readonly record struct InventoryGrid(string Element, int Slot, int Across, int Down)
{
    /// <summary>A container holds many items; a worn slot holds one.</summary>
    public bool IsContainer => (Slot & Rpg.Slot.Containers) != 0;

    /// <summary>The grid's size in pixels, at the game's one cell size.</summary>
    public (int Width, int Height) Pixels =>
        (Across * InventoryPane.CellWidth, Down * InventoryPane.CellHeight);
}

/// <summary>An item placed in a grid, with the cell its top-left corner sits in.</summary>
/// <param name="Item">The item itself.</param>
/// <param name="Grid">Which grid it is in.</param>
/// <param name="X">Cell column, 0-based.</param>
/// <param name="Y">Cell row, 0-based, counting **down** from the grid's top.</param>
public readonly record struct PlacedItem(Item Item, InventoryGrid Grid, int X, int Y)
{
    /// <summary>How many cells it covers, from its `ENUM_ICONSIZE`.</summary>
    public (int Across, int Down) Cells => Item.IconCells;
}

/// <summary>
/// The inventory pane: `ui/inventoryPane.nui` plus the state the engine would have put in it.
/// </summary>
public sealed class InventoryPane
{
    public const string PaneName = "inventoryPane";

    /// <summary>The vault's own pane file, which this same class drives with <see cref="VaultSlots"/>.</summary>
    public const string VaultPaneName = "vaultPane";

    /// <summary>
    /// **One cell, everywhere in the game** (27 across, 32 down). `ui/inventoryPane.nui`
    /// divides every grid it declares into exactly this — 270 x 128 over 10 x 4, 54 x 96 over 2 x 3,
    /// 27 x 32 over 1 x 1 — and so do the status bar's quick-use strip and the haven's vault.
    /// </summary>
    public const int CellWidth = 27, CellHeight = 32;

    /// <summary>
    /// The nine worn slots and the pack, each paired with the `.nui` element that shows it. The
    /// slot values were settled from save files; the element names and cell
    /// counts are the pane's own. **The pairing of the two is by name**, which is why the engine's
    /// internal grid order did not have to be read.
    /// </summary>
    public static readonly (string Element, int Slot)[] Slots =
    [
        ("invHead", Rpg.Slot.Head),
        ("invNeck", Rpg.Slot.Neck),
        ("invBody", Rpg.Slot.Body),
        ("invRightWeapon", Rpg.Slot.RightWeapon),
        ("invLeftWeapon", Rpg.Slot.LeftWeapon),
        ("invRightArm", Rpg.Slot.RightArm),
        ("invLeftArm", Rpg.Slot.LeftArm),
        ("invRightHand", Rpg.Slot.RightHand),
        ("invLeftHand", Rpg.Slot.LeftHand),
        ("invInventory", Rpg.Slot.Inventory),
    ];

    /// <summary>
    /// **The haven vault, which is a tenth grid and not a place.** `ui/VaultPane.nui` declares
    /// exactly three elements — the 10 x 9 `vaultGrid`, a `vaultOKButton` and the
    /// `vaultCurrentChar` textbox — and the vault's open hands the grid the container mask
    /// `0x4000` and then registers **the inventory drop** on binding 0: the
    /// same drop handler every other grid in the game gets. So one entry is the whole table.
    ///
    /// The short of it: the items live on the
    /// **character**, not in the haven, so this pane is the inventory pane with one grid.
    /// </summary>
    public static readonly (string Element, int Slot)[] VaultSlots = [("vaultGrid", Rpg.Slot.Vault)];

    /// <summary>
    /// Whether the vault may open now (the vault's single-player gates, a comparison with 1.1): **not while it
    /// is already up**, and **only for a player-type character** — the steered
    /// thing's type must be 7, <see cref="ThingType.Player"/>. The companions are type 7 as well, so
    /// a steered Wilhem opens it on his own page (as in 1.1);
    /// a body taken over with Possession, an actor (type 0), gets nothing. Vitomir opened it for
    /// whoever was steered until a comparison with 1.1. (A network game's host passes it on to the client
    /// whose character it is — multiplayer, not read.)
    /// </summary>
    public static bool VaultMayOpen(WorldThing? steered, bool alreadyUp) =>
        !alreadyUp && steered?.Type == ThingType.Player;

    /// <summary>
    /// Whose stored page the vault shows when the steer changes: the
    /// character's own only when it is player-type (type 7: Christof or a companion) — anything else,
    /// a possessed body, gets an empty grid and no name. Vitomir showed any steered thing's page until the
    /// A comparison with 1.1.
    /// </summary>
    public static Actor? VaultSubject(WorldThing? steered) =>
        steered?.Type == ThingType.Player ? steered.Actor : null;

    /// <summary>
    /// The **linked pairs** of worn slots. The pane's set-up wires two
    /// of them, each way — `invRightWeapon` with `invLeftWeapon`, and `invRightArm` with
    /// `invLeftArm` — and wires no others. The link is set in **code**: no shipped `.nui` declares
    /// it, so a pane file cannot change it.
    ///
    /// What it is for: the drop tests the carried item's mask against the grid you dropped on, and
    /// **if it does not match and the grid is a weargrid, it follows the link and tests again**. So
    /// a one-handed sword dropped on the off-hand slot lands in the sword hand instead of being
    /// refused, which is what the base game does. Head, body, neck and the two ring hands have no
    /// link, so a ring dropped on the wrong hand is refused rather than moved.
    /// </summary>
    public static readonly (int From, int To)[] Links =
    [
        (Rpg.Slot.RightWeapon, Rpg.Slot.LeftWeapon),
        (Rpg.Slot.LeftWeapon, Rpg.Slot.RightWeapon),
        (Rpg.Slot.RightArm, Rpg.Slot.LeftArm),
        (Rpg.Slot.LeftArm, Rpg.Slot.RightArm),
    ];

    /// <summary>The slot a weargrid hands a refused drop on to, or <see cref="Rpg.Slot.None"/>.</summary>
    public static int LinkOf(int slot)
    {
        foreach (var (from, to) in Links) if (from == slot) return to;
        return Rpg.Slot.None;
    }

    /// <summary>That link as a grid of this pane, when the pane declares one.</summary>
    public InventoryGrid? Linked(InventoryGrid grid)
    {
        int slot = LinkOf(grid.Slot);
        return slot == Rpg.Slot.None ? null : GridOf(slot);
    }

    /// <summary>The grid of this pane that shows a slot, when the pane declares one.</summary>
    public InventoryGrid? GridOf(int slot)
    {
        foreach (var grid in Grids()) if (grid.Slot == slot) return grid;
        return null;
    }

    /// <summary>
    /// **What a worn slot holds, the other half of a two-handed pair included.** The weargrid's
    /// placing puts the item in its grid and, when the item's flags carry
    /// `0x8000` (TWO HANDED) and the linked grid is empty, **the placing puts the same item into the
    /// linked grid too** — so a pair of gloves is in both arm grids and a two-handed weapon in both
    /// weapon grids, drawn in both, described from both, lifted from both; taking it off (and the
    /// weargrid's destructor) clears the partner that holds the same item. The pane
    /// is refilled through that same placing (the fill → the grid's placing), so a
    /// loaded save shows the pair too. Vitomir records a worn item in one slot
    /// (<see cref="Item.Location"/>, the grid it was put in), so the partner is found here: this
    /// slot's own item first, else a two-handed item in its linked slot (<see cref="Links"/>). Until
    /// The partner was drawn empty — gloves on Wilhem showed one glove.
    /// </summary>
    public Item? WornIn(int slot)
    {
        if (Subject is null) return null;
        int link = LinkOf(slot);
        Item? partner = null;
        foreach (var item in Subject.Inventory.Items)
        {
            if (item.Destroyed) continue;
            if (item.Location == slot) return item;
            if (link != Rpg.Slot.None && item.Location == link && item.TwoHanded) partner ??= item;
        }
        return partner;
    }

    /// <summary>
    /// The suffix of the hover picture, `_R`: an icon loads **`<icon>`** and
    /// **`<icon>_R`**, and the shipped `_r` files are
    /// the icon with a red outline. The discipline slots use the same suffix for the selected one.
    /// </summary>
    public const string HoverSuffix = "_R";

    /// <summary>
    /// Where the pointer is, in the same 640 x 480 space as <see cref="At"/>, or null when it is not
    /// on this pane's space (a menu over it). The frame loop keeps it; <see cref="Outlined"/> reads it.
    /// </summary>
    public (int X, int Y)? Pointer { get; set; }

    /// <summary>
    /// **Whether a placed item draws its red-outlined picture** (<see cref="HoverSuffix"/>) this frame.
    /// <para>
    /// **A worn slot** (the weargrid's draw, run every frame): its picture
    /// index is 1 when this weargrid is the element under the pointer
    /// and 0 otherwise, and it gives its linked grid the same index when that holds
    /// the same item — so **a pair shows plain, and both halves outline red while either is hovered**.
    /// The draw takes the bitmap of the icon's picture array, centred, through the
    /// plain blits (a colour-keyed copy, no blend), so the two halves are the same
    /// picture at the same strength: nothing in 1.1 draws the second one fainter.
    /// </para>
    /// <para>
    /// **An icon in a grid** (the pack, the vault; the icon's slot 11): its surface
    /// is the `_R` bitmap while it is the element under the pointer and the plain one
    /// otherwise, so a hovered pack item outlines red as well. An item with no `_R` in the install gets
    /// a null bitmap there and draws nothing while hovered (the draw tests it and draws only a
    /// surface it has); Vitomir's pass draws the plain picture there instead (the repair
    /// `Repairs.HoverPicture`).
    /// </para>
    /// </summary>
    public bool Outlined(PlacedItem placed)
    {
        if (Pointer is not var (x, y) || At(x, y) is not { } under) return false;
        if (placed.Grid.IsContainer)
            return under.Grid.Slot == placed.Grid.Slot && ReferenceEquals(under.Item, placed.Item);
        if (under.Grid.Slot == placed.Grid.Slot) return true;
        return under.Grid.Slot == LinkOf(placed.Grid.Slot) && ReferenceEquals(under.Item, placed.Item);
    }

    public NuiPane Pane { get; }

    /// <summary>Whose inventory this is. Null draws the pane empty, which is what no player gives.</summary>
    public Actor? Subject { get; set; }

    /// <summary>
    /// The party's purse, which is not the actor's — the pane shows one number for all of it. 1.1
    /// refreshes the box when money changes; Vitomir's `invGold` reads it live.
    /// </summary>
    public int Gold { get; set; }

    readonly (string Element, int Slot)[] slots;

    /// <summary>
    /// A pane over a slot table. The table is a parameter because the engine's own icon grids are
    /// not owned by one pane: every grid carries its container mask and
    /// every one of them is driven by the same handler, so which pane a grid is drawn on is a
    /// layout fact and nothing more. The vault is the second pane to prove it.
    /// </summary>
    public InventoryPane(NuiPane pane, (string Element, int Slot)[]? slots = null)
    {
        Pane = pane;
        this.slots = slots ?? Slots;
    }

    public NuiElement? Control(string name) =>
        Pane.Elements.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The button under a point, by name, or null — which is how the vault's **OK** is found
    /// (`vaultOKButton`, 26 x 15 at 269,154, whose callback is registered by the vault's open and is
    /// the close's body inlined: the pane put away, its three globals cleared, the pane flag dropped
    /// and the cursor put back). The pack's own buttons are the status bar's business, not this.
    /// </summary>
    public string? ButtonAt(int x, int y)
    {
        int px = Pane.Pos.Length > 1 ? Pane.Pos[0] : 0, py = Pane.Pos.Length > 1 ? Pane.Pos[1] : 0;
        foreach (var e in Pane.Elements)
        {
            if (e.Type != "button" || e.Pos.Length < 2 || e.Size.Length < 2) continue;
            int bx = px + e.Pos[0], by = py + e.Pos[1];
            if (x >= bx && y >= by && x < bx + e.Size[0] && y < by + e.Size[1]) return e.Name;
        }
        return null;
    }

    /// <summary>The vault's OK button (ours to name; the pane's own).</summary>
    public const string VaultOk = "vaultOKButton";

    /// <summary>
    /// Every grid the pane declares, in the order <see cref="Slots"/> lists them, skipping any the
    /// `.nui` does not have — a mod may remove one, and a missing element is not an error.
    /// </summary>
    public IEnumerable<InventoryGrid> Grids()
    {
        foreach (var (element, slot) in slots)
        {
            if (Control(element) is not { } e) continue;
            var (across, down) = GridSize(e, slot);
            if (across > 0 && down > 0) yield return new InventoryGrid(element, slot, across, down);
        }
    }

    /// <summary>
    /// A grid's cell count. The element's own `gridsize` is the authority — that is what a mod
    /// would change — and `Slot.Size` is the fallback, which is the engine's own table at
    /// the inventory's own search. The two agree on every shipped pane.
    /// </summary>
    public static (int Across, int Down) GridSize(NuiElement e, int slot)
    {
        if (e.Get("gridsize") is { } g && NuiPane.Ints(g) is [var across, var down, ..] && across > 0 && down > 0)
            return (across, down);
        return Rpg.Slot.Size(slot);
    }

    /// <summary>
    /// Everything the pane would draw, grid by grid. A worn slot shows its one item at cell (0, 0) —
    /// a two-handed item in **both** grids of its pair (<see cref="WornIn"/>); the pack shows each
    /// item at the cell its own record names.
    /// </summary>
    public IEnumerable<PlacedItem> Placed()
    {
        if (Subject is null) yield break;
        foreach (var grid in Grids())
        {
            if (!grid.IsContainer)
            {
                if (WornIn(grid.Slot) is { } worn) yield return new PlacedItem(worn, grid, 0, 0);
                continue;
            }
            foreach (var item in Subject.Inventory.Items)
                if (item.Location == grid.Slot && !item.Destroyed)
                    yield return new PlacedItem(item, grid, item.Cell.X, item.Cell.Y);
        }
    }

    /// <summary>
    /// Where an item's bottom-left corner sits, in the pane's own space. **Cells count down from
    /// the top of a grid and `.nui` space counts up from the bottom**, so a row index has to be
    /// flipped against the grid's own height — the same flip the character pane's label order
    /// needed, and the one thing on this pane that is easy to get silently backwards.
    /// <para>
    /// **A worn slot draws its item centred** (the weargrid's draw): the offset is half
    /// of what the slot is bigger than the icon, each way, in whole pixels (`(slot − icon) / 2`,
    /// C's division, so toward zero). Only the two hand slots, 2 x 3, ever hold something smaller
    /// — a 1 x 2 dagger or stake sits 13 px in and 16 px up, a 1 x 1 grenade 13 and 32, a 2 x 2
    /// buckler or SMG 0 and 16. The slot's size is its element's own, which
    /// is the grid's cells in every shipped pane. Being symmetrical, the offset needs no flip.
    /// </para>
    /// </summary>
    public static (float X, float Y) Corner(NuiElement grid, PlacedItem placed)
    {
        if (!placed.Grid.IsContainer)
        {
            var (slotW, slotH) = grid.Size.Length > 1 ? (grid.Size[0], grid.Size[1]) : placed.Grid.Pixels;
            int iconW = placed.Cells.Across * CellWidth, iconH = placed.Cells.Down * CellHeight;
            return (grid.Pos[0] + (slotW - iconW) / 2, grid.Pos[1] + (slotH - iconH) / 2);
        }
        float x = grid.Pos[0] + placed.X * CellWidth;
        int rowsBelow = placed.Grid.Down - placed.Y - placed.Cells.Down;
        return (x, grid.Pos[1] + rowsBelow * CellHeight);
    }

    /// <summary>
    /// What the pane's textboxes say, for whichever of them the file declares. `invGold` is
    /// `textboxflags=0x01`, so centred across; `vaultCurrentChar` is `0x03` and names whose vault is
    /// on screen — which is the whole reason the element exists, because the vault belongs to one
    /// character and the pane refills when the steered one changes (the vault's refill,
    /// reached from `SetCurrentControl`).
    /// </summary>
    public IReadOnlyDictionary<string, string> Texts()
    {
        var text = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Control("invGold") is not null) text["invGold"] = Gold.ToString();
        if (Control("vaultCurrentChar") is not null) text["vaultCurrentChar"] = SubjectName;
        return text;
    }

    /// <summary>What to call the subject on screen — the same fallback the character sheet uses.</summary>
    public string SubjectName => Subject is { } a ? (a.Name.Length > 0 ? a.Name : a.CastId) : "";

    /// <summary>
    /// Whether an item could go in a grid at all: **an item fits a slot when its icon fits the
    /// grid**, which is the same rule the containers use and is why the quick-use
    /// belt takes only 1 x 1 items. A container additionally needs the cells to be free.
    /// </summary>
    public static bool Fits(Item item, InventoryGrid grid)
    {
        var (across, down) = item.IconCells;
        // The quick-use strip's bit (0x200) is in an icon's grid mask only when the icon is 1 x 1,
        // and the drop tests the mask before anything else — so the 5 x 1 strip
        // refuses the one shipped 2 x 1 item, `launch_grenades`, although it would fit.
        if (grid.Slot == Rpg.Slot.QuickUse && (across, down) != (1, 1)) return false;
        return across <= grid.Across && down <= grid.Down;
    }

    /// <summary>
    /// Whether an item's footprint lies inside its grid. A save is taken on trust on load — the
    /// engine does not check, and two records sharing a cell are drawn on top of each other
    ///  — so this is for the pane's own placement, not for validating a save.
    /// </summary>
    public static bool Inside(PlacedItem p)
    {
        var (across, down) = p.Cells;
        return p.X >= 0 && p.Y >= 0 && p.X + across <= p.Grid.Across && p.Y + down <= p.Grid.Down;
    }

    /// <summary>
    /// The first free top-left cell in a container that would take an item, or null when it will
    /// not fit anywhere. **Row by row from the top left, x fastest** — which is the inventory's
    /// own search, as in 1.1; `Rpg.Stow.Place` is the same walk against an
    /// actor's inventory rather than against this pane.
    /// </summary>
    public (int X, int Y)? FreeCell(InventoryGrid grid, Item item)
    {
        if (!Fits(item, grid)) return null;
        var (across, down) = item.IconCells;
        var taken = new bool[grid.Across, grid.Down];
        foreach (var p in Placed())
        {
            if (p.Grid.Element != grid.Element || ReferenceEquals(p.Item, item)) continue;
            var (pa, pd) = p.Cells;
            for (int x = p.X; x < p.X + pa && x < grid.Across; x++)
                for (int y = p.Y; y < p.Y + pd && y < grid.Down; y++)
                    if (x >= 0 && y >= 0) taken[x, y] = true;
        }
        for (int y = 0; y + down <= grid.Down; y++)
            for (int x = 0; x + across <= grid.Across; x++)
            {
                bool free = true;
                for (int dx = 0; dx < across && free; dx++)
                    for (int dy = 0; dy < down && free; dy++)
                        if (taken[x + dx, y + dy]) free = false;
                if (free) return (x, y);
            }
        return null;
    }

    /// <summary>Which grid and cell a point in the pane's space falls in, if any.</summary>
    /// <param name="Grid">The grid under the point.</param>
    /// <param name="X">Cell column, 0-based.</param>
    /// <param name="Y">Cell row, 0-based, counting **down** from the grid's top.</param>
    /// <param name="Item">Whatever is drawn there, or null for an empty cell.</param>
    public readonly record struct Spot(InventoryGrid Grid, int X, int Y, Item? Item)
    {
        /// <summary>
        /// Where the pointer was in the grid, in pixels from its left edge and **down** from its top —
        /// the element's own pointer that the cell lookup turns into a drop's cell
        /// (<see cref="DropCell"/>). Null for a spot named by its cell rather than by a point.
        /// </summary>
        public (int X, int Y)? Pointer { get; init; }
    }

    /// <summary>
    /// Hit-tests a point in the pane's own 640x480 space. **`.nui` space counts up from the
    /// bottom and cells count down from the top**, so the row is the grid's height minus the
    /// offset — the same flip the drawing side makes, kept here so the two cannot disagree.
    /// </summary>
    public Spot? At(int x, int y)
    {
        int px = Pane.Pos.Length > 1 ? Pane.Pos[0] : 0, py = Pane.Pos.Length > 1 ? Pane.Pos[1] : 0;
        foreach (var grid in Grids())
        {
            if (Control(grid.Element) is not { } e || e.Pos.Length < 2) continue;
            int gx = px + e.Pos[0], gy = py + e.Pos[1];
            var (w, h) = grid.Pixels;
            if (x < gx || y < gy || x >= gx + w || y >= gy + h) continue;

            // A worn slot is one cell: any point in it is its cell (0, 0), so a click
            // anywhere lifts or swaps the worn item and a hover anywhere describes it
            // — the dagger in a 2 x 3 hand answers over the whole hand.
            if (!grid.IsContainer) return new Spot(grid, 0, 0, ItemAt(grid, 0, 0));
            int cx = (x - gx) / CellWidth;
            int cy = grid.Down - 1 - (y - gy) / CellHeight;
            return new Spot(grid, cx, cy, ItemAt(grid, cx, cy)) { Pointer = (x - gx, gy + h - 1 - y) };
        }
        return null;
    }

    /// <summary>
    /// The distinct items an item's footprint would cover if it were put down at
    /// (<paramref name="x"/>, <paramref name="y"/>) — the overlap count the drop
    /// asks for before every drop. The count
    /// is what decides the outcome: **none** places it, **one** swaps, **more than one** refuses.
    /// The item itself never counts against its own move.
    /// </summary>
    /// <para>
    /// **Asked of the grid's own items, by slot** — the pack's, the vault's or the quick-use strip's —
    /// not of what this pane draws. The strip is drawn on the status bar and the pane has no element
    /// for it (<see cref="ItemIn"/>), so until then, when the question went through the pane's
    /// drawn grids, it always answered none for the strip: an item put down on an occupied belt cell
    /// was laid over the one there instead of swapping with it (in play: a Bottle of
    /// Vitae dropped on the Rat, six items on a five-cell belt).
    /// </para>
    public IReadOnlyList<Item> Overlapping(InventoryGrid grid, Item item, int x, int y)
    {
        var (across, down) = item.IconCells;
        var hit = new List<Item>();
        if (Subject is null) return hit;
        foreach (var placed in Subject.Inventory.Items)
        {
            if (placed.Location != grid.Slot || placed.Destroyed || ReferenceEquals(placed, item)) continue;
            int px = grid.IsContainer ? placed.Cell.X : 0, py = grid.IsContainer ? placed.Cell.Y : 0;
            var (pa, pd) = placed.IconCells;
            bool overlaps = x < px + pa && px < x + across
                         && y < py + pd && py < y + down;
            if (overlaps && !hit.Contains(placed)) hit.Add(placed);
        }
        return hit;
    }

    /// <summary>
    /// What covers a cell of **any** container the subject carries, named by slot rather than by
    /// one of this pane's own grids. The quick-use strip needs it: the belt is a grid like any
    /// other but it is drawn on the status bar, so the pane has no element for it.
    /// </summary>
    public Item? ItemIn(InventoryGrid grid, int x, int y)
    {
        if (Subject is null) return null;
        if (!grid.IsContainer) return WornIn(grid.Slot);  // a worn slot is one cell, a pair's partner included
        foreach (var item in Subject.Inventory.Items)
        {
            if (item.Location != grid.Slot || item.Destroyed) continue;
            int ix = item.Cell.X, iy = item.Cell.Y;
            var (across, down) = item.IconCells;
            if (x >= ix && x < ix + across && y >= iy && y < iy + down) return item;
        }
        return null;
    }

    /// <summary>
    /// What covers a cell — an item's **footprint**, not just its corner, so clicking the bottom of
    /// a broadsword picks up the broadsword. A worn slot answers with its item whatever the cell:
    /// the slot is one cell.
    /// </summary>
    public Item? ItemAt(InventoryGrid grid, int x, int y)
    {
        foreach (var placed in Placed())
        {
            if (placed.Grid.Element != grid.Element) continue;
            if (!grid.IsContainer) return placed.Item;
            var (across, down) = placed.Cells;
            if (x >= placed.X && x < placed.X + across && y >= placed.Y && y < placed.Y + down)
                return placed.Item;
        }
        return null;
    }
}
