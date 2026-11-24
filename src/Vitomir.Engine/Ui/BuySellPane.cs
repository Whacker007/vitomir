// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The shop pane, as state rather than pixels: the shelf's rows and their colours,
// what is selected, what the value box says, which buttons are live, and where the list is scrolled.
// `Vitomir.Render.BuySellPass` draws it; the rules are `Rpg.Shops`'.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>One shelf row: <c>"%s (%d)"</c> of the item's name and price, in white or grey.</summary>
public readonly record struct ShelfRow(string Text, uint Colour, TreasureRecord Treasure);

/// <summary>
/// `ui/buySellPane.nui` — the left half of the screen while a shop is open, with the inventory
/// pane up on the right. The pieces, and what each does, are 1.1's:
/// <list type="bullet">
/// <item>**the shelf** (`bsStoreList`) lists every stocked line in file order as <c>name
/// (price)</c> — white when the purse covers it, grey (148) when it does not. **Stock counts are
/// never shown.** The current row's text is red and the hovered row's yellow; there is no bar;</item>
/// <item>**clicking a row** shows the item in the display box, its price in `bsValue` (red when it
/// cannot be afforded) and lights **Buy** only when it can be;</item>
/// <item>**clicking one of your own items** — in the pack, on the paperdoll or the belt — selects it
/// for sale instead of lifting it: its sell price, and **Sell**. An item the shop will not take is
/// refused with the negative cursor and sound;</item>
/// <item>**Done** and **Escape** close it.</item>
/// </list>
/// </summary>
public sealed class BuySellPane(NuiPane pane)
{
    public const string PaneName = "buySellPane";
    public const string List = "bsStoreList", ScrollBar = "bsScrollBar", Slider = "bsScrollSlider",
                        DisplayBox = "bsDisplayBox", BuyButton = "bsBuyButton", SellButton = "bsSellButton",
                        DoneButton = "bsDoneButton", ValueBox = "bsValue", NameBox = "bsStoreName";

    /// <summary>A row the purse covers and one it does not.</summary>
    public const uint Affordable = 0xFFFFFFFFu, TooDear = 0xFF949494u;
    /// <summary>The value box's price when it cannot be afforded.</summary>
    public const uint ValueTooDear = 0xFFFF0000u;
    /// <summary>The textlistbox's own colours: current red, hovered yellow.</summary>
    public const uint CurrentColour = 0xFFFF0000u, HoverColour = 0xFFFFFF00u;
    /// <summary>`itemspacing=1`: the gap between rows, so the pitch is the font's height plus one.</summary>
    public const int ItemSpacing = 1;
    /// <summary>What a page is: the wheel and a click on the track each move the list by 255.</summary>
    public const int Page = 255;
    /// <summary>One inventory cell, which is what an icon's size is counted in (`InventoryPane.CellWidth`).</summary>
    public const int CellWidth = InventoryPane.CellWidth, CellHeight = InventoryPane.CellHeight;

    public NuiPane Pane { get; } = pane;

    /// <summary>The font's line height — `defaultFont.nfd`, 10 — which the row height is (<see cref="Letter"/>).</summary>
    public int LineHeight { get; set; } = 10;

    /// <summary>The font the list and both text boxes are drawn in: none of them names one.</summary>
    public const string FontName = "DefaultFont";

    /// <summary>
    /// The rows laid out with the font they are drawn in (ours; earlier, the buy and sell pass wrote it at each draw, the
    /// A comparison with 1.1's section 1.3): <see cref="FontName"/>'s line height, which is what a click on a row hits.
    /// </summary>
    public void Letter(UiAssets assets) => LineHeight = assets.Font(FontName).LineHeight;

    /// <summary>The open store, or null.</summary>
    public Store? Store { get; set; }

    /// <summary>The string table, for names.</summary>
    public Strings? Text { get; set; }

    /// <summary>`misc/effects.ned`, which the prices and the rollover read.</summary>
    public NedFile? Effects { get; set; }

    readonly List<ShelfRow> rows = [];
    public IReadOnlyList<ShelfRow> Rows => rows;

    /// <summary>
    /// The list's current row, red. **The pane's own "clear selection" never resets
    /// it**, so after you click one of your own items the last shelf row you chose stays red.
    /// </summary>
    public int Current { get; private set; } = -1;
    /// <summary>The row under the pointer, yellow, or −1.</summary>
    public int Hovered { get; private set; } = -1;
    /// <summary>How far the list is scrolled, in pixels; 0 is the top.</summary>
    public int Scroll { get; private set; }

    /// <summary>The shelf item selected, or null.</summary>
    public TreasureRecord? SelectedTreasure { get; private set; }
    /// <summary>The player's own item selected for sale, or null.</summary>
    public WorldThing? SelectedItem { get; private set; }
    /// <summary>What the display box shows: the icon's texture, or null for the pane's own art.</summary>
    public Item? Shown { get; private set; }
    /// <summary>`bsValue`: the number, and its colour (0 for the box's own).</summary>
    public string Value { get; private set; } = "";
    public uint ValueColour { get; private set; }
    /// <summary>Whether Buy and Sell are live; greyed otherwise (state 3).</summary>
    public bool BuyEnabled { get; private set; }
    public bool SellEnabled { get; private set; }

    /// <summary>The store's name, for `bsStoreName`.</summary>
    public string Title => Store?.Name ?? "";

    public NuiElement? Element(string name) =>
        Pane.Elements.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a point in the 640x480 space is on the pane's artwork.</summary>
    public bool Covers(float x, float y)
    {
        float px = Pane.Pos.Length > 1 ? Pane.Pos[0] : 0, py = Pane.Pos.Length > 1 ? Pane.Pos[1] : 0;
        return Pane.Quads.Any(q => x >= px + q.Pos[0] && x <= px + q.Pos[0] + q.Size[0]
                                   && y >= py + q.Pos[1] && y <= py + q.Pos[1] + q.Size[1]);
    }

    /// <summary>Whether a point is inside a named element.</summary>
    public bool Over(string name, float x, float y) =>
        Element(name) is { Size.Length: > 1, Pos.Length: > 1 } e
        && x >= e.Pos[0] && x <= e.Pos[0] + e.Size[0] && y >= e.Pos[1] && y <= e.Pos[1] + e.Size[1];

    int Gold => Store?.Trader is { } t ? Shops.CashOf(t) : 0;

    // --- the shelf ---------------------------------------------------------------------

    /// <summary>
    /// The shelf rebuilt — on opening, after every purchase and sale, and when the trader changes:
    /// the selection cleared, the list cleared (current, hover and scroll with it), one row per
    /// stocked line in file order, priced for the trader now, and then **row 0 selected**, which
    /// fires the row's own selection — so on opening the first item is already chosen.
    /// </summary>
    public void Refresh()
    {
        ClearSelection();
        rows.Clear();
        Current = Hovered = -1;
        Scroll = 0;
        if (Store is not { } store) return;
        int gold = Gold;
        foreach (var line in store.Lines)
        {
            if (line.Treasure is not { } t) continue;
            int price = Shops.BuyPrice(store, t, Effects);
            rows.Add(new ShelfRow($"{NameOf(t)} ({price})", (uint)gold >= (uint)price ? Affordable : TooDear, t));
        }
        if (rows.Count > 0) SelectRow(0);
    }

    /// <summary>
    /// An item's name as the shelf gives it: its identified description's text when the
    /// template has one, its plain description's otherwise — and with neither, **"Missing
    /// description!!!"** (the shelf's own fallback string; a comparison with 1.1 —
    /// Vitomir showed the template's name).
    /// </summary>
    public string NameOf(TreasureRecord t)
    {
        string key = t.Item.IdentifiedDescriptionId.Length > 0 ? t.Item.IdentifiedDescriptionId : t.Item.DescriptionId;
        return key.Length == 0 ? MissingDescription : Text is null ? key : Text[key];
    }

    /// <summary>What a shelf row says for an item with no description at all.</summary>
    public const string MissingDescription = "Missing description!!!";

    /// <summary>
    /// The pane's "clear selection", inlined in six places: the selected icon let go, both
    /// selections and the display box emptied, Buy and Sell greyed, the value box blanked. The
    /// list's current row is not touched.
    /// </summary>
    public void ClearSelection()
    {
        SelectedItem = null;
        SelectedTreasure = null;
        Shown = null;
        BuyEnabled = SellEnabled = false;
        Value = "";
        ValueColour = 0;
    }

    // --- the list ---------------------------------------------------------------------------------

    /// <summary>The height of the list box, and the row height and pitch.</summary>
    int BoxHeight => Element(List) is { Size.Length: > 1 } e ? e.Size[1] : 255;
    int Pitch => LineHeight + ItemSpacing;

    /// <summary>
    /// A row's bottom, in the list box's own bottom-up space, before scrolling: row 0 flush with the
    /// top of the box and each row one pitch lower.
    /// </summary>
    public int RowY(int row) => BoxHeight - LineHeight - row * Pitch;

    /// <summary>Whether a row is drawn at all at the current scroll.</summary>
    public bool Visible(int row) => RowY(row) + Scroll < BoxHeight && RowY(row) + Scroll + LineHeight >= 0;

    /// <summary>
    /// How far the list can scroll: past the last row by a quarter of a line, and
    /// not at all when everything fits.
    /// </summary>
    public int MaxScroll
    {
        get
        {
            if (rows.Count == 0) return 0;
            int last = RowY(rows.Count - 1), quarter = LineHeight / 4;
            return last - quarter < 0 ? quarter - last : 0;
        }
    }

    void ScrollTo(int value) => Scroll = Math.Clamp(value, 0, MaxScroll);

    /// <summary>
    /// The pane selecting a row itself: it becomes current, its selection runs, and the
    /// list scrolls to put it at the top — **except that a row below the first page cannot be
    /// scrolled to**: its offset comes out negative and clamps to 0, so choosing row 23 or later
    /// this way jumps the list back to the top with the red row out of sight.
    /// </summary>
    public void SelectRow(int row)
    {
        if (row < 0 || row >= rows.Count) return;
        Current = row;
        Chose(row);
        int y = RowY(row);
        ScrollTo(y > 0 ? BoxHeight - LineHeight - y : y - LineHeight);
    }

    /// <summary>
    /// A click on the list (on the **press**): the hovered row becomes current and its
    /// selection runs, even when it was current already. No scrolling.
    /// </summary>
    public void ClickList()
    {
        if (Hovered < 0) return;
        Current = Hovered;
        Chose(Hovered);
    }

    /// <summary>
    /// The row's selection: the selection cleared, the item into the display box, its
    /// price into the value box — in red, with Buy left grey, when the purse will not cover it.
    /// </summary>
    void Chose(int row)
    {
        ClearSelection();
        var t = rows[row].Treasure;
        Shown = t.Item;
        SelectedTreasure = t;
        if (Store is not { } store) return;
        int price = Shops.BuyPrice(store, t, Effects);
        Value = price.ToString();
        if ((uint)Gold >= (uint)price) BuyEnabled = true;
        else ValueColour = ValueTooDear;
    }

    /// <summary>
    /// The hover (every frame): the first visible row whose band the pointer is strictly
    /// inside, the pointer being over the list at all. Answers whether it changed, which is when
    /// the description boxes are rewritten.
    /// </summary>
    public bool HoverAt(float x, float y)
    {
        int was = Hovered;
        Hovered = -1;
        if (Element(List) is { Pos.Length: > 1 } box && Over(List, x, y))
        {
            float local = y - box.Pos[1];
            for (int i = 0; i < rows.Count; i++)
            {
                if (!Visible(i)) continue;
                int bottom = RowY(i) + Scroll;
                if (bottom < local && local < bottom + LineHeight) { Hovered = i; break; }
            }
        }
        return Hovered != was;
    }

    /// <summary>The wheel: any movement is a page, up towards the top.</summary>
    public void Wheel(int notches)
    {
        if (notches > 0) ScrollTo(Scroll - Page);
        else if (notches < 0) ScrollTo(Scroll + Page);
    }

    // --- the scroll bar ---------------------------------------------------------------------------

    int BarY => Element(ScrollBar) is { Pos.Length: > 1 } b ? b.Pos[1] : 176;
    int Travel => (Element(ScrollBar) is { Size.Length: > 1 } b ? b.Size[1] : 257)
                - (Element(Slider) is { Size.Length: > 1 } s ? s.Size[1] : 8);

    /// <summary>
    /// Where the slider's bottom sits. The bar's range runs from the last row's bottom
    /// less two up to 0, and its value is minus the scroll, so an overflowing list at the top has
    /// the slider at the **top** of the track — and a list that fits has it at the **bottom**,
    /// where it cannot be grabbed. It is a fixed 8x8 and never hidden.
    /// </summary>
    public int SliderY
    {
        get
        {
            if (rows.Count == 0) return BarY;
            int min = RowY(rows.Count - 1) - LineHeight / 4, max = 0;
            if (min == max) return BarY;
            int value = Math.Max(-Scroll, min);          // clamped into the range
            if (min > max) value = min;
            return (int)((float)(value - min) / (max - min) * Travel + BarY);
        }
    }

    /// <summary>A click on the track: above the slider pages towards the top, below it away.</summary>
    public void ClickTrack(float y)
    {
        if (y > SliderY) ScrollTo(Scroll - Page);
        else ScrollTo(Scroll + Page);
    }

    /// <summary>Whether the slider can be taken hold of at all: only when the list overflows.</summary>
    public bool CanDrag => rows.Count > 0 && RowY(rows.Count - 1) < 0;

    /// <summary>The slider dragged to put its bottom at <paramref name="y"/>.</summary>
    public void DragTo(float y)
    {
        if (!CanDrag) return;
        int min = RowY(rows.Count - 1) - LineHeight / 4, max = 0;
        float at = Math.Clamp(y, BarY, BarY + Travel);
        int value = (int)((at - BarY) / Travel * (max - min) + min);
        ScrollTo(-value);
    }

    // --- your own items ----------------------------------------------------------------

    /// <summary>
    /// A click on one of the player's item icons while the shop is open — which **selects it for
    /// sale instead of picking it up**; nothing can be lifted or moved while shopping. False, and
    /// the caller shows the negative cursor and plays the refusal, when the shop will not look at
    /// it. Ownership is not tested here; the sale tests it.
    /// </summary>
    public bool ClickItem(WorldThing itemThing)
    {
        if (Store is not { } store || itemThing.Item is not { } item || !Shops.Takes(store, item)) return false;
        ClearSelection();
        SelectedItem = itemThing;
        Shown = item;
        Value = Shops.SellPrice(store, item, Effects).ToString();
        SellEnabled = true;
        return true;
    }

    // --- the buttons ------------------------------------------------------------------------------

    /// <summary>
    /// Buy, which does nothing while greyed: the current row's item bought. When the
    /// purchase goes through the shelf has been rebuilt (and row 0 chosen) by the time this
    /// continues, and it then clears the selection and chooses the row it bought from again **if
    /// that row still holds the same item** — the scrolling quirk of <see cref="SelectRow"/> and all.
    /// A line that sold out leaves row 0 red, Buy grey and the value box empty.
    ///
    /// **The list stays where it was** (ours, always on with no switch): 1.1's
    /// rebuild puts the list back at the top, and its re-choice of the row scrolls it to the top of the
    /// box or, below the first page, back to the top of the list; here the row is chosen again without
    /// scrolling and the list keeps the scroll it had.
    /// </summary>
    public BuyResult? ClickBuy(Location place, Shops shops)
    {
        if (!BuyEnabled || Store is not { } store || Current < 0 || Current >= rows.Count) return null;
        int row = Current, kept = Scroll;
        var t = rows[row].Treasure;
        var result = shops.Buy(place, store, t.Id);
        if (result is BuyResult.Bought or BuyResult.Failed)
        {
            ClearSelection();
            if (row < rows.Count && ReferenceEquals(rows[row].Treasure, t)) { Current = row; Chose(row); }
            ScrollTo(kept);
        }
        return result;
    }

    /// <summary>
    /// Sell, which does nothing while greyed: the selected item sold, then the shelf rebuilt
    /// — keeping the list's scroll (ours, as <see cref="ClickBuy"/>).
    /// </summary>
    public bool ClickSell(Location place, Shops shops)
    {
        if (!SellEnabled || Store is not { } store || SelectedItem is not { } item) return false;
        int kept = Scroll;
        if (!shops.Sell(place, store, item)) return false;
        ClearSelection();
        Refresh();
        ScrollTo(kept);
        return true;
    }

    /// <summary>
    /// **A right click on a shelf row buys it at once** (ours, always on with no switch;
    /// 1.1 has no right click on the shop's list): the row is chosen as a left click
    /// chooses it, then bought as Buy buys it — the purse, the pack's room and the stock all as a
    /// purchase asks. Null when there is no such row or the purse will not cover it (the row stays
    /// chosen, its price in red).
    /// </summary>
    public BuyResult? QuickBuy(int row, Location place, Shops shops)
    {
        if (row < 0 || row >= rows.Count) return null;
        Current = row;
        Chose(row);
        return ClickBuy(place, shops);
    }

    /// <summary>
    /// **A right click on one of the player's items sells it at once** (ours, always on with no
    /// switch): offered as a left click offers it (<see cref="ClickItem"/>, the shop's mask), then sold
    /// as Sell sells it (<see cref="Shops.Sell"/>: whose it is, no-sell items, a worn item kept on by its
    /// script). False when either refuses.
    /// </summary>
    public bool QuickSell(WorldThing itemThing, Location place, Shops shops) =>
        ClickItem(itemThing) && ClickSell(place, shops);

    // --- the description boxes --------------------------------------------------------------------

    /// <summary>
    /// What the status bar's description boxes say for the hovered row, or
    /// null to fade them out.
    /// </summary>
    public (RolloverLine Name, IReadOnlyList<RolloverLine> Detail)? DescribeHovered() =>
        Hovered >= 0 && Hovered < rows.Count
            ? Rollover.Treasure(rows[Hovered].Treasure, Store?.Trader, Text, Effects) : null;

    /// <summary>
    /// What they say over the display box: the item selected for sale by the ordinary
    /// item rollover, or the shelf's; nothing selected leaves them as they were.
    /// </summary>
    public (RolloverLine Name, IReadOnlyList<RolloverLine> Detail)? DescribeShown()
    {
        if (SelectedItem is { } mine)
            return Rollover.Name(mine, Text) is { } name ? (name, Rollover.Detail(mine, Text, Effects)) : null;
        if (SelectedTreasure is { } t) return Rollover.Treasure(t, Store?.Trader, Text, Effects);
        return null;
    }

    /// <summary>
    /// Where the display box draws its icon, box-local and bottom-up: the icon's own
    /// size, centred, each half truncated.
    /// </summary>
    public (int X, int Y, int Width, int Height)? IconPlacement()
    {
        if (Shown is not { } item || Element(DisplayBox) is not { Size.Length: > 1 } box) return null;
        var (across, down) = item.IconCells;
        int w = across * CellWidth, h = down * CellHeight;
        return ((box.Size[0] - w) / 2, (box.Size[1] - h) / 2, w, h);
    }
}
