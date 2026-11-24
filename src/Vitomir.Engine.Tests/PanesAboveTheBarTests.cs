// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The panes above the status bar are centred in the rows above it**, through the program. On a
/// picture narrower than 16:9 the 16:9 stretch's status bar stands on the picture's bottom edge and the pack, the
/// character sheet, the quests, the disciplines, the vault and the shop have a map of their own
/// (<see cref="UiLayout.Panes(float, float)"/>), so a click is placed by the map of what it lands on: a pane's controls
/// through the panes', the bar's through the sheet's. A game is recorded with <c>--no-scripts</c> at 1024x768 and
/// 1280x800, its clicks given as window pixels where each map draws the control, taken from the installed panes:
/// the pack's coin button; an item picked up in the pack and put back; an item taken off the bar's belt, put in the
/// pack, picked up there and dropped on another cell of the belt (pane to bar and back); an item picked up in the pack
/// and put down on the world; a click in the black above the pane and in the black below it (nobody's:
/// <see cref="NobodysGroundTests"/>); a click on the world beside the pane, which walks; and in a shop, a shelf row
/// bought, the pack's sword sold and the shop closed. Each recording then replays the same.
/// Replays: <c>VITOMIR_NO_REPLAYS=1</c> leaves them out.
/// </summary>
public class PanesAboveTheBarRunTests
{
    const UiLayoutMode Mode = UiLayoutMode.Wide;

    /// <summary>A picture's two maps and the installed panes' controls on them.</summary>
    sealed class Picture(int w, int h, UiAssets assets)
    {
        readonly UiLayout.Map sheet = UiLayout.On(w, h, Mode, SheetSeat.Bottom), panes = UiLayout.Panes(w, h, Mode, SheetSeat.Bottom);

        static string At(float x, float y, UiLayout.Map map)
        {
            var (px, py) = UiLayout.ToPicture(x, y, map);
            return $"{(int)MathF.Round(px)},{(int)MathF.Round(py)}";
        }

        /// <summary>A point of the panes above the bar (the sheet's units, y up) as the window pixel it is drawn at.</summary>
        public string Pane(float x, float y) => At(x, y, panes);

        /// <summary>A point of the status bar as the window pixel it is drawn at.</summary>
        public string Bar(float x, float y) => At(x, y, sheet);

        NuiElement Control(string pane, string name) =>
            assets.Pane(pane).Elements.First(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The middle of a control of a pane above the bar.</summary>
        public string PaneControl(string pane, string name)
        {
            var e = Control(pane, name);
            return Pane(e.Pos[0] + e.Size[0] / 2f, e.Pos[1] + e.Size[1] / 2f);
        }

        /// <summary>The middle of the cell <paramref name="column"/> across the top row of a grid, 27 by 32 a cell (<see cref="InventoryPane.CellWidth"/>).</summary>
        public (float X, float Y) Cell(string pane, string grid, int column)
        {
            var e = Control(pane, grid);
            return (e.Pos[0] + InventoryPane.CellWidth * (column + 0.5f), e.Pos[1] + e.Size[1] - InventoryPane.CellHeight / 2f);
        }

        /// <summary>The first row of the shop's shelf.</summary>
        public string ShelfTop()
        {
            var e = Control(BuySellPane.PaneName, BuySellPane.List);
            return Pane(e.Pos[0] + e.Size[0] / 2f, e.Pos[1] + e.Size[1] - 6f);
        }
    }

    static (string App, string Game, string Dir, Dictionary<string, string> Env) Start()
    {
        var (app, game) = ReplayBaselineTests.Needs();
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vitomir-panes-{Guid.NewGuid():N}")).FullName;
        var env = new Dictionary<string, string> { ["VITOMIR_USERDATA"] = Path.Combine(dir, "user") };
        Directory.CreateDirectory(env["VITOMIR_USERDATA"]);
        return (app, game, dir, env);
    }

    static List<string> Said(string output) =>
        [.. output.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("inv:", StringComparison.Ordinal) || l.StartsWith("ui:", StringComparison.Ordinal)
                                                             || l.StartsWith("click:", StringComparison.Ordinal) || l.StartsWith("shop: ", StringComparison.Ordinal)
                                                             || l.StartsWith("pointer: click", StringComparison.Ordinal))];

    static void InOrder(List<string> said, string output, params string[] wanted)
    {
        int from = 0;
        foreach (string step in wanted)
        {
            int found = said.FindIndex(from, l => l.StartsWith(step, StringComparison.Ordinal));
            Assert.True(found >= 0, $"no \"{step}\" after line {from} of: {string.Join(" | ", said)}\n{output}");
            from = found + 1;
        }
    }

    [Theory]
    [InlineData(1024, 768)]
    [InlineData(1280, 800)]
    public void A_click_lands_on_what_is_drawn_in_the_pack_on_the_bar_and_on_the_world(int w, int h)
    {
        var (app, game, dir, env) = Start();
        try
        {
            using var files = Install.Open();
            var on = new Picture(w, h, new UiAssets(files));
            // The two maps part on this picture: the pack's controls are not where the sheet's map would have them.
            Assert.NotEqual(on.Pane(573f, 458f), on.Bar(573f, 458f));
            string coin = on.PaneControl(InventoryPane.PaneName, "invgoldbutton"), weapon = on.PaneControl(InventoryPane.PaneName, "invrightweapon");
            var pack = on.Cell(InventoryPane.PaneName, "invinventory", 0);
            var belt = on.Cell(StatusBar.PaneName, "sbquickuse", 0);
            var beltNext = on.Cell(StatusBar.PaneName, "sbquickuse", 1);
            string packCell = on.Pane(pack.X, pack.Y), belt0 = on.Bar(belt.X, belt.Y), belt1 = on.Bar(beltNext.X, beltNext.Y);
            // The black above the open pack and the black below it, in its own half; the world in the other half.
            int right = w * 3 / 4;
            var above = NobodysGroundTests.BlackAbove(w, h, Mode);
            var below = NobodysGroundTests.BlackBelow(w, h, Mode, StatusBar.FromWindow(right, 0, w, h, Mode, SheetSeat.Bottom).X);
            string blackAbove = $"{right},{above.Top + above.Rows / 2}", blackBelow = $"{right},{below.Top + below.Rows / 2}";
            // (Two places on the world: the sword is put down at the first, and somebody's sword then lies there.)
            string world = $"{w / 4},{h * 2 / 5}", worldAgain = $"{w / 8},{h / 5}";

            string steps = string.Join(';', "wait:30", "key:I", "wait:8", coin, "wait:4", "key:Escape", "wait:4",
                                       weapon, "wait:6", weapon, "wait:6",
                                       belt0, "wait:6", packCell, "wait:6", packCell, "wait:6", belt1, "wait:6",
                                       weapon, "wait:6", world, "wait:12",
                                       blackAbove, "wait:6", blackBelow, "wait:6", worldAgain, "wait:25");
            string tape = Path.Combine(dir, "pack.tape");
            var (code, output) = ReplayBaselineTests.RunWith(env, app, game,
                "--level", "P1_OLDT", "--player", "--hud", "--no-scripts", "--mute", "--frame-ms", "33", "--seed", "1",
                "--size", $"{w}x{h}", "--ui-layout", "wide", "--frames", "200", "--no-log-file", "--pointer-log", "--gold", "500",
                "--camera-pitch", "-70", "--camera-yaw", "270", "--ui-click", steps, "--record", tape);
            Assert.True(code == 0, output);
            Assert.Contains("interface layout=wide sheet=16x9 panes=centred", File.ReadAllText(tape));

            // The coin button put its box up (the menus' trail), and Escape took it down.
            var trail = File.ReadAllLines(tape + ".digest").Where(l => l.StartsWith("m ", StringComparison.Ordinal))
                            .Select(l => l[(l.IndexOf(' ', 2) + 1)..].ToLowerInvariant()).ToList();
            Assert.Contains("show inputbox", trail);
            var said = Said(output);
            InOrder(said, output,
                "ui: dropped nothing (cancelled)",
                // The sword in the right hand, picked up and put back: the pack's own grid, through the panes' map.
                "inv: holding broadsword", "inv: broadsword on right weapon",
                // The letter off the bar's belt (the sheet's map), into the pack (the panes'), out again, onto the belt's next cell.
                "ui: holding CuthbertLetter", "inv: CuthbertLetter into inventory at 0,0", "inv: holding CuthbertLetter",
                "ui: CuthbertLetter into quick use at 1,0",
                // The sword picked up again and put down on the world beside the pane.
                "inv: holding broadsword", $"pointer: click {world} is the world's", "click: carrying broadsword over to put it down",
                // The black above the pane and below it: nobody's, and nothing follows either.
                $"pointer: click {blackAbove} is nobody's", $"pointer: click {blackBelow} is nobody's",
                // And the world beside the pane again: a walk.
                $"pointer: click {worldAgain} is the world's", "click: ");
            Assert.True(said[^1].StartsWith("click: walking to", StringComparison.Ordinal) || said[^1].StartsWith("click: running to", StringComparison.Ordinal),
                        string.Join(" | ", said));
            Assert.Equal(2, said.Count(l => l.StartsWith("click:", StringComparison.Ordinal)));

            var (checkedCode, checking) = ReplayBaselineTests.RunWith(env, app, game, "--replay-check", tape, "--mute", "--no-log-file");
            Assert.True(checkedCode == 0 && checking.Contains("replay-check: the same"), ReplayBaselineTests.Checked(checking));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData(1024, 768)]
    [InlineData(1280, 800)]
    public void The_shop_buys_and_sells_where_its_panes_are_drawn(int w, int h)
    {
        var (app, game, dir, env) = Start();
        try
        {
            using var files = Install.Open();
            var on = new Picture(w, h, new UiAssets(files));
            string row = on.ShelfTop(), buy = on.PaneControl(BuySellPane.PaneName, BuySellPane.BuyButton),
                   sell = on.PaneControl(BuySellPane.PaneName, BuySellPane.SellButton), done = on.PaneControl(BuySellPane.PaneName, BuySellPane.DoneButton),
                   weapon = on.PaneControl(InventoryPane.PaneName, "invrightweapon");
            string steps = string.Join(';', "wait:30", row, "wait:6", buy, "wait:8", weapon, "wait:6", sell, "wait:8", done, "wait:10");
            string tape = Path.Combine(dir, "shop.tape");
            var (code, output) = ReplayBaselineTests.RunWith(env, app, game,
                "--level", "P1_OLDT", "--player", "--hud", "--no-scripts", "--mute", "--frame-ms", "33", "--seed", "1",
                "--size", $"{w}x{h}", "--ui-layout", "wide", "--frames", "110", "--no-log-file", "--gold", "2000",
                "--shop", "PragueSmithy.nbs", "--ui-click", steps, "--record", tape);
            Assert.True(code == 0, output);
            var said = Said(output);
            InOrder(said, output, "shop: buy ", "inv: broadsword offered for sale", "shop: sell broadsword -> sold", "shop: closed (done)");
            Assert.Contains(said, l => l.StartsWith("shop: buy ", StringComparison.Ordinal) && l.Contains("-> Bought", StringComparison.Ordinal));

            var (checkedCode, checking) = ReplayBaselineTests.RunWith(env, app, game, "--replay-check", tape, "--mute", "--no-log-file");
            Assert.True(checkedCode == 0 && checking.Contains("replay-check: the same"), ReplayBaselineTests.Checked(checking));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
