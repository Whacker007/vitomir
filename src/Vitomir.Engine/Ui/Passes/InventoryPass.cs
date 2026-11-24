// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The inventory pane, drawn. Everything about *what* is in it — which grid an item is in, which
// cell, whether it fits — belongs to `Vitomir.Engine.Ui.InventoryPane`, which needs no GPU. This
// file puts it on screen.
//
// **It is a right-hand pane**: `ui/inventoryPane.nui` says `visGroup=2` and its own comment says
// "this is a righthand pane", and its quads run from x=320 to x=640. So it and the character sheet
// occupy opposite halves of the 640x480 space and can be up together, which is what the base game
// does — a screenshot of the original has both open at once.
//
// **Icons are drawn 1:1 rather than stretched**, and that is a reading rather than a preference.
// An icon file is cell-sized artwork in a power-of-two canvas anchored at the engine's own origin
// — 799 files, zero exceptions — so a 2 x 3 item's artwork is already exactly
// 54 x 96, which is exactly what 2 x 3 cells of 27 x 32 measure. There is nothing to scale, and the
// engine has no scaling blit at all: all four of its composite functions are plain rectangle
// copies. `UiCanvas.Icon` is that copy.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>Draws the game's own inventory pane over the finished frame while <see cref="Enabled"/>.</summary>
public sealed class InventoryPass : IUiPass
{
    readonly UiCanvas ui;

    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    /// <summary>What to draw. Null until somebody opens the pack.</summary>
    public InventoryPane? Pane { get; set; }

    /// <summary>Off by default; `--inventory` starts it on and F6 toggles it in play.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The gold button greyed (state 3): the shop's show hook does it (on
    /// `invGoldbutton`) and its hide hook undoes it. The button draws its `greyed=` art then, and
    /// the pane's own art the rest of the time (it names no normal bitmap).
    /// </summary>
    public bool GoldGreyed { get; set; }

    /// <summary>A button of the pane held down (the vault's OK), which draws its `depressed=` art until let go.</summary>
    public string? Pressed { get; set; }

    /// <summary>The font the gold box uses — it declares none, so it takes the pane's default.</summary>
    public NfdFont Font { get; }

    public const string DefaultFont = "DefaultFont";

    /// <summary>Names the pane asked for that the install has not got, reported once.</summary>
    public IReadOnlyList<string> Missing => ui.Assets.Missing;

    public InventoryPass(UiCanvas ui, InventoryPane? pane = null)
    {
        this.ui = ui;
        Pane = pane;
        Font = ui.Assets.Font(DefaultFont);
    }

    /// <summary>Builds the pane from the player's own files.</summary>
    public static InventoryPane Load(UiAssets assets) => new(assets.Pane(InventoryPane.PaneName));

    /// <summary>
    /// The haven vault, which is this same pass over a different file. Nothing here had to change
    /// for it: `ui/VaultPane.nui` is quads, one icon grid and one textbox, and all three are what
    /// this pass already draws. **It is a left-hand pane** — the file says `visGroup=1` and its own
    /// comment says "this is a lefthand pane", and its quads run from x=0 to x=320 — so it sits
    /// opposite the inventory, which is exactly how the original arranges them: the vault's
    /// show hook puts the **inventory pane** up beside it, and its hide hook takes that
    /// one down again, because the point of the vault is dragging between the two.
    /// </summary>
    public static InventoryPane LoadVault(UiAssets assets) =>
        new(assets.Pane(InventoryPane.VaultPaneName), InventoryPane.VaultSlots);

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (!Enabled || Pane is not { } pane || width <= 0 || height <= 0) return;
        ui.BeginPanes(width, height);   // a pane above the bar: the panes' own map (UiLayout.Panes)

        // The backing artwork: seven quads that tile the pane's own rectangle.
        float px = pane.Pane.Pos[0], py = pane.Pane.Pos[1];
        foreach (var q in pane.Pane.Quads)
            ui.Draw(q.Material, px + q.Pos[0], py + q.Pos[1], q.Size[0], q.Size[1]);

        // Then the items, grid by grid, under the text so a number is never hidden by an icon. A
        // two-handed item is in both grids of its pair, and whatever is under the pointer
        // draws its red-outlined `_R` picture instead — both halves of a pair at once.
        // An item with no `_R` in the install draws nothing while hovered in 1.1 (a null
        // bitmap, `InventoryPane.Outlined`); the repair `Repairs.HoverPicture` draws its plain picture instead.
        foreach (var placed in pane.Placed())
        {
            if (pane.Control(placed.Grid.Element) is not { } e || e.Pos.Length < 2) continue;
            var (x, y) = CellCorner(e, placed);
            if (placed.Item.Icon.Length == 0) continue;
            string art = pane.Outlined(placed) ? placed.Item.Icon + InventoryPane.HoverSuffix : placed.Item.Icon;
            float w = placed.Cells.Across * InventoryPane.CellWidth, h = placed.Cells.Down * InventoryPane.CellHeight;
            // `Repairs.HoverPicture`: an icon with no `_R` keeps its plain picture while hovered.
            if (!ui.Icon(art, px + x, py + y, w, h) && !ReferenceEquals(art, placed.Item.Icon))
                ui.Icon(placed.Item.Icon, px + x, py + y, w, h);
        }

        if (GoldGreyed && pane.Control("invGoldButton") is { Pos.Length: > 1, Size.Length: > 1 } gold
            && gold.Get("greyed") is { } greyArt)
            ui.Draw(greyArt, px + gold.Pos[0], py + gold.Pos[1], gold.Size[0], gold.Size[1]);

        if (Pressed is { } held && pane.Control(held) is { Pos.Length: > 1, Size.Length: > 1 } button
            && button.Get("depressed") is { } downArt)
            ui.Draw(downArt, px + button.Pos[0], py + button.Pos[1], button.Size[0], button.Size[1]);

        var texts = pane.Texts();
        foreach (var e in pane.Pane.Elements)
        {
            if (e.Type != "textbox" || e.Name.Length == 0) continue;
            if (!texts.TryGetValue(e.Name, out var line) || line.Length == 0) continue;
            int w = e.Size.Length > 1 ? e.Size[0] : 0, h = e.Size.Length > 1 ? e.Size[1] : 0;
            if (w <= 0 || h <= 0) continue;
            var (tx, ty) = NuiPane.TextBoxOrigin(e, w, h, UiCanvas.Measure(Font, line), Font.LineHeight);
            ui.Text(Font, px + e.Pos[0] + tx, py + e.Pos[1] + ty, line, Tint.White);
        }
    }

    /// <summary>
    /// Where an item's bottom-left corner sits, in the pane's own space: the row flipped against
    /// the grid's height, and a worn slot's item centred in it — <see cref="InventoryPane.Corner"/>.
    /// </summary>
    public static (float X, float Y) CellCorner(NuiElement grid, PlacedItem placed) => InventoryPane.Corner(grid, placed);
}
