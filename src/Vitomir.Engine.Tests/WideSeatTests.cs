// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A full-screen menu inside a game is centred on black, like the front end** (*"I prefer b
/// actually."*). On a picture narrower than 16:9 the 16:9 stretch's sheet is shorter than the picture: it stands on the
/// picture's bottom edge wherever the world shows, and in the middle while a screen that is the whole sheet is up — the
/// Options screen and its pages, the save and load screens, the map (<see cref="FrontEnd.WholeSheet"/>,
/// <see cref="UiLayout.InGame"/>). These go through a game's menus as its frames do
/// (<c>ClassicGame.SeatFrame</c>): a frame's events are mapped with the seat the last picture was drawn with, then the
/// seat is said again for that frame's picture. The layout and the seat are named on each call; none is put in force.
/// <see cref="WideSeatRunTests"/> is the same through the program itself.
/// </summary>
public class WideSeatTests
{
    public static TheoryData<int, int> Pictures => new() { { 1024, 768 }, { 1280, 800 }, { 1920, 1080 } };

    [Fact]
    public void The_installed_full_screen_panes_are_whole_sheets_and_nothing_else_is()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), new Strings(files).Find);
        foreach (string whole in new[] { "Options", "OptionsGameplay", "OptionsControls", "OptionsDisplay", "OptionsDisplayAdv", "OptionsSound",
                                         "spSave", "spLoad", "MainMenu", "sp", "Credits", "TitleScreen", "mapPragueOT", "mapVienna", "mapLondonEast",
                                         "mapNYCdocks", "mpSelect", "mpChat", "mpPlayerCreate" })
            Assert.True(menus.Pane(whole).WholeSheet, whole);
        // The escape menu's ankh, the boxes, the plaques, the bars and the panes leave the world (or the bar) showing.
        foreach (string part in new[] { "escapeMenu", "escapeMenuMP", "messageBox", "inputBox", "LoadScreen", "NetworkLoadScreen", "pauseMenu", "console",
                                        "statusBarSP", "statusBarMP", "statusBarADV", "inventoryPane", "characterPane", "questPane", "disciplinePane",
                                        "buySellPane", "VaultPane", "conversationPane", "ReadTextBox", "subtitle" })
            Assert.False(menus.Pane(part).WholeSheet, part);
        Assert.Equal(SheetSeat.Centre, UiLayout.InGame(true));
        Assert.Equal(SheetSeat.Bottom, UiLayout.InGame(false));
    }

    /// <summary>A game's menus and its frames, as <c>ClassicGame.Frame</c> orders them, on a picture of one size.</summary>
    internal sealed class Game : IDisposable
    {
        readonly GameFilesHold hold = new();
        readonly string ini = Path.Combine(Path.GetTempPath(), $"vitomir-wideseat-{Guid.NewGuid():N}.ini");
        public FrontEnd Menus { get; }
        public OptionsMenu Options { get; }
        public GameOptions Settings { get; } = new();
        public int W { get; }
        public int H { get; }
        /// <summary>The seat the last picture was drawn with.</summary>
        public SheetSeat Seat { get; private set; } = SheetSeat.Bottom;
        public (MenuRequest Request, int Slot) Asked { get; private set; }

        public Game(int w, int h)
        {
            (W, H) = (w, h);
            var assets = new UiAssets(hold.Files);
            var text = new Strings(hold.Files);
            Menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateGame };
            Options = new OptionsMenu(Menus, text.Find, Settings) { SavePath = ini };
            Menus.Options = Options;
        }

        public UiLayout.Map Map => UiLayout.On(W, H, UiLayoutMode.Wide, Seat);

        /// <summary>The window pixel at a point of the sheet as the last picture shows it (its nearest corner: WideClickTests).</summary>
        public (int X, int Y) Pixel(float x, float y)
        {
            var (px, py) = UiLayout.ToPicture(x, y, Map);
            return ((int)MathF.Round(px), (int)MathF.Round(py));
        }

        public (int X, int Y) PixelOf(MenuPane pane, string control)
        {
            var e = pane.Element(control)!;
            var (x, y) = pane.At(e);
            return Pixel(x + e.Size[0] / 2f, y + e.Size[1] / 2f);
        }

        /// <summary>
        /// One frame: its events, if any (a left click at a window pixel, mapped with the seat the last picture was
        /// drawn with), the seat said again, the picture, and the menus' request taken after it.
        /// </summary>
        public void Frame((int X, int Y)? click = null)
        {
            if (click is { } at)
            {
                var unit = StatusBar.FromWindow(at.X, at.Y, W, H, UiLayoutMode.Wide, Seat);
                Menus.PointerAt(unit.X, unit.Y);
                Menus.LeftHeld = true;
                Menus.Press();
                Menus.LeftHeld = false;
                Menus.Release();
            }
            Seat = UiLayout.InGame(Menus.WholeSheet);
            Menus.Frame(Menus.NowMs);
            Menus.Drawn();
            Asked = Menus.TakeRequest();
        }

        /// <summary>A click on a control of a pane where the last picture shows it, on the frame after that picture.</summary>
        public void Click(MenuPane pane, string control) => Frame(PixelOf(pane, control));

        public void Dispose()
        {
            File.Delete(ini);
            hold.Dispose();
        }
    }

    /// <summary>The install, held open for a test's menus.</summary>
    sealed class GameFilesHold : IDisposable
    {
        public Vfs.GameFiles Files { get; } = Install.Open();
        public void Dispose() => Files.Dispose();
    }

    [Theory]
    [MemberData(nameof(Pictures))]
    public void Options_in_a_game_is_centred_its_buttons_answer_there_and_the_bar_s_seat_comes_back(int w, int h)
    {
        using var game = new Game(w, h);
        var menus = game.Menus;
        bool shorter = UiLayout.Below16By9(w, h);
        var bottom = UiLayout.On(w, h, UiLayoutMode.Wide, SheetSeat.Bottom);
        var centre = UiLayout.On(w, h, UiLayoutMode.Wide, SheetSeat.Centre);
        Assert.Equal(shorter, bottom != centre);                               // at 16:9 and wider the seat moves nothing

        // The escape menu is the ankh over the world: at the bottom.
        menus.OpenEscape();
        game.Frame();
        var escape = menus.Pane(FrontEnd.EscapeFile);
        Assert.True(menus.IsUp(escape));
        Assert.False(menus.WholeSheet);
        Assert.Equal(SheetSeat.Bottom, game.Seat);

        // Its Options button, clicked where the bottom-seated picture shows it: the Options screen, the whole sheet.
        game.Click(escape, "emoptionsbutton");
        var options = menus.Pane(OptionsMenu.OptionsFile);
        Assert.True(menus.IsUp(options));
        Assert.True(menus.WholeSheet);
        Assert.Equal(SheetSeat.Centre, game.Seat);                             // said on the frame the screen is first drawn

        // The frame after: the Gameplay button where the centred picture shows it. Through the bottom-seated map the same
        // pixel is another point of the sheet, 80 units up on a 4:3 picture — some other button, or none.
        var gameplayAt = game.PixelOf(options, "opgameplaybutton");
        if (shorter)
        {
            var e = options.Element("opgameplaybutton")!;
            var wrong = StatusBar.FromWindow(gameplayAt.X, gameplayAt.Y, w, h, UiLayoutMode.Wide, SheetSeat.Bottom);
            Assert.NotEqual(e.Name, options.ControlAt(wrong.X, wrong.Y)?.Name);
        }
        game.Frame(gameplayAt);
        var gameplay = menus.Pane(OptionsMenu.GameplayFile);
        Assert.True(menus.IsUp(gameplay));
        Assert.True(gameplay.WholeSheet);
        Assert.Equal(SheetSeat.Centre, game.Seat);

        // A box ticked and OK, both on the centred sheet: kept.
        gameplay.Check(AlwaysRunOption.Box, true);
        game.Click(gameplay, "opgpOKButton");
        Assert.True(game.Settings.AlwaysRun);
        Assert.True(menus.IsUp(options));
        Assert.Equal(SheetSeat.Centre, game.Seat);

        // Done: the game again, nothing of the menus up, the sheet back on the bottom edge on that frame's picture.
        game.Click(options, "opdonebutton");
        Assert.False(menus.Up);
        Assert.False(menus.WholeSheet);
        Assert.Equal(SheetSeat.Bottom, game.Seat);

        // And a click above the bar is the world's again: above the sheet it is off it, in rows the world is drawn in.
        if (shorter)
        {
            using var files = Install.Open();
            var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
            place.Awaken(new Ai.AiClasses(files), 1, new EffectDefinitions(files));
            var bar = new StatusBar(new UiAssets(files).Pane(StatusBar.PaneName), place);
            int row = (int)bottom.Y / 2;
            var above = StatusBar.FromWindow(w / 2, row, w, h, UiLayoutMode.Wide, game.Seat);
            Assert.True(above.Y >= UiAssets.Height);
            Assert.False(bar.Covers(above.X, above.Y));
            int rows = (int)MathF.Round(h * UiLayout.WorldShare(StatusBar.WorldShare, w, h, UiLayoutMode.Wide, game.Seat));
            Assert.True(Pick.OnWorld(row, h, rows));
            // While the screen was up the same pixel was in the black band above the centred sheet: no control's.
            var band = StatusBar.FromWindow(w / 2, (int)centre.Y / 2, w, h, UiLayoutMode.Wide, SheetSeat.Centre);
            Assert.True(band.Y >= UiAssets.Height);
            Assert.Null(options.ControlAt(band.X, band.Y));
        }
    }

    [Theory]
    [MemberData(nameof(Pictures))]
    public void The_save_screen_in_a_game_is_centred_and_its_name_is_typed_there(int w, int h)
    {
        using var game = new Game(w, h);
        var menus = game.Menus;
        menus.OpenEscape();
        game.Frame();
        var escape = menus.Pane(FrontEnd.EscapeFile);

        // Save, on the escape menu: asked for on that frame, the screen opened after the frame's picture (the menus'
        // request), so that picture is still the bottom-seated one and the next frame's is the first centred one.
        game.Click(escape, "emsavebutton");
        var save = menus.Pane(FrontEnd.SaveFile);
        Assert.True(menus.IsUp(save));
        Assert.Equal(SheetSeat.Bottom, game.Seat);
        game.Frame();
        Assert.True(menus.WholeSheet);
        Assert.Equal(SheetSeat.Centre, game.Seat);

        // Its Save button on the centred sheet: the name box over it, the screen still the whole sheet behind the box.
        game.Click(save, "spSaveSaveButton");
        var box = menus.Pane(FrontEnd.InputBoxFile);
        Assert.True(menus.IsUp(box));
        Assert.False(box.WholeSheet);
        Assert.True(menus.WholeSheet);
        Assert.Equal(SheetSeat.Centre, game.Seat);

        // The name field, clicked where the centred picture shows it, is the field under the pointer; a name typed, Enter.
        var field = box.Nui.Elements.First(e => e.Type == "edit");
        var (fx, fy) = box.At(field);
        game.Frame(game.Pixel(fx + field.Size[0] / 2f, fy + field.Size[1] / 2f));
        Assert.Same(box, menus.Active());
        Assert.Equal(field.Name, box.ControlAt(menus.PointerX, menus.PointerY)?.Name);
        box.Edits[field.Name].Set("");
        foreach (char c in "Night one") menus.Type(c);
        menus.Key(MenuKey.Enter);
        game.Frame();
        Assert.Equal(MenuRequest.Save, game.Asked.Request);
        Assert.Equal("Night one", menus.SaveName);

        // Saved: the screen put away, the sheet on the bottom edge again.
        Assert.False(menus.IsUp(save));
        Assert.False(menus.WholeSheet);
        Assert.Equal(SheetSeat.Bottom, game.Seat);
    }

    [Theory]
    [MemberData(nameof(Pictures))]
    public void The_load_screen_and_our_own_pages_are_whole_sheets_and_a_box_over_the_game_is_not(int w, int h)
    {
        using var game = new Game(w, h);
        var menus = game.Menus;
        menus.OpenLoad();
        game.Frame();
        Assert.True(menus.WholeSheet);
        Assert.Equal(SheetSeat.Centre, game.Seat);
        menus.Key(MenuKey.Escape);
        game.Frame();
        Assert.False(menus.Up);
        Assert.Equal(SheetSeat.Bottom, game.Seat);

        // A message box over the world leaves the world showing.
        menus.Message("Load", "No such save.", ok: true, cancel: false);
        game.Frame();
        Assert.True(menus.Up);
        Assert.False(menus.WholeSheet);
        Assert.Equal(SheetSeat.Bottom, game.Seat);
        menus.Key(MenuKey.Enter);
        game.Frame();
        Assert.False(menus.Up);

        // Our own pages off the Options screen (the key bindings, the Fixes) are made on a whole-sheet backdrop too.
        game.Options.Open();
        game.Options.Keys.Open();
        game.Frame();
        Assert.True(menus.Pane(KeysPage.File).WholeSheet);
        Assert.Equal(SheetSeat.Centre, game.Seat);
        menus.Key(MenuKey.Escape);
        game.Options.Fixes.Open();
        game.Frame();
        Assert.True(menus.Pane(FixesPage.File).WholeSheet);
        Assert.Equal(SheetSeat.Centre, game.Seat);
    }
}

/// <summary>
/// **The same through the program**: a game recorded in the 16:9 stretch on a 4:3 and a 16:10 picture, its
/// clicks given as window pixels where each picture shows the control — the escape menu's buttons on the bottom-seated
/// sheet, the Options and save screens' on the centred one **on the first frame whose events follow the centred
/// picture**, and the world above the bar again on the first frame after the screen has gone. A click mapped with the
/// other seat lands 80 units of the sheet away (on 4:3) and none of these steps would happen. The recording then replays
/// the same. Replays: <c>VITOMIR_NO_REPLAYS=1</c> leaves them out.
/// </summary>
public class WideSeatRunTests
{
    [Theory]
    [InlineData(1024, 768)]
    [InlineData(1280, 800)]
    public void A_click_lands_on_what_is_drawn_through_the_change_of_seat(int w, int h)
    {
        var (app, game) = ReplayBaselineTests.Needs();
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vitomir-wideseat-{Guid.NewGuid():N}")).FullName;
        bool passed = false;
        try
        {
            // Where each control is drawn, from the installed panes (1.1's lettering, said on the launch, so the rows are 1.1's).
            using var at = new WideSeatTests.Game(w, h);
            var menus = at.Menus;
            string Bottom(MenuPane pane, string control) { var p = at.PixelOf(pane, control); return $"{p.X},{p.Y}"; }
            menus.OpenEscape();
            at.Frame();
            var escape = menus.Pane(FrontEnd.EscapeFile);
            string optionsOnEscape = Bottom(escape, "emoptionsbutton"), saveOnEscape = Bottom(escape, "emsavebutton");
            at.Click(escape, "emoptionsbutton");
            Assert.Equal(SheetSeat.Centre, at.Seat);
            var options = menus.Pane(OptionsMenu.OptionsFile);
            string gameplayOnOptions = Bottom(options, "opgameplaybutton"), doneOnOptions = Bottom(options, "opdonebutton");
            at.Click(options, "opgameplaybutton");
            string cancelOnGameplay = Bottom(menus.Pane(OptionsMenu.GameplayFile), "opgpcancelbutton");
            at.Click(menus.Pane(OptionsMenu.GameplayFile), "opgpcancelbutton");
            at.Click(options, "opdonebutton");
            menus.OpenEscape();
            at.Frame();
            at.Click(escape, "emsavebutton");
            at.Frame();
            Assert.Equal(SheetSeat.Centre, at.Seat);
            string saveOnSave = Bottom(menus.Pane(FrontEnd.SaveFile), "spSaveSaveButton");
            // The alley's cobbles ahead of Christof and at his feet, well above the bar: walkable ground on both pictures.
            string world = $"{w * 547 / 1000},{h * 390 / 1000}", worldAgain = $"{w * 527 / 1000},{h * 586 / 1000}";

            // A click's press goes in on its step's frame and its release on the next (ScriptedInput). So after a click
            // that opens a whole-sheet screen, `wait:1` is the frame the release opens it on and the first centred picture,
            // and the next step's press is the first event made on that picture. Save is opened by the menus' request
            // after its frame's picture: one frame later.
            string steps = string.Join(';', "wait:20", "key:Escape", "wait:3", optionsOnEscape, "wait:1", gameplayOnOptions, "wait:3",
                                       cancelOnGameplay, "wait:3", doneOnOptions, "wait:1", world, "wait:25",
                                       "key:Escape", "wait:3", saveOnEscape, "wait:2", saveOnSave, "wait:3", "text:Night one", "wait:2",
                                       "key:Enter", "wait:3", worldAgain, "wait:20");
            string tape = Path.Combine(dir, "seat.tape");
            var env = new Dictionary<string, string> { ["VITOMIR_USERDATA"] = Path.Combine(dir, "user") };
            Directory.CreateDirectory(env["VITOMIR_USERDATA"]);
            var (code, output) = ReplayBaselineTests.RunWith(env, app, game,
                "--level", "P1_OLDT", "--player", "--hud", "--no-scripts", "--mute", "--frame-ms", "33", "--seed", "1",
                "--size", $"{w}x{h}", "--ui-layout", "wide", "--menu-lettering", "1.1", "--frames", "150", "--no-log-file",
                "--ui-click", steps, "--record", tape);
            Assert.True(code == 0, output);
            Assert.Contains("interface layout=wide sheet=16x9", File.ReadAllText(tape));

            // Every step through the menus happened, in this order.
            var walked = File.ReadAllLines(tape + ".digest").Where(l => l.StartsWith("m ", StringComparison.Ordinal))
                             .Select(l => l[(l.IndexOf(' ', 2) + 1)..].ToLowerInvariant()).ToList();
            string[] wanted =
            [
                "click escapemenu emoptionsbutton", "show options", "click options opgameplaybutton", "show optionsgameplay",
                "click optionsgameplay opgpcancelbutton", "click options opdonebutton",
                "click escapemenu emsavebutton", "show spsave", "click spsave spsavesavebutton", "show inputbox", "hide spsave",
            ];
            int from = 0;
            foreach (string step in wanted)
            {
                int found = walked.FindIndex(from, l => l == step);
                Assert.True(found >= 0, $"no \"{step}\" after step {from} of: {string.Join(" | ", walked)}\n(the run's recording and its digest are kept in {dir})\n{output}");
                from = found + 1;
            }
            // The name typed on the centred screen was the save's, and the world took a click after each screen had gone.
            Assert.Contains("Night one", output);
            Assert.True(File.Exists(Path.Combine(ReplayBaselineTests.SavesOf(env["VITOMIR_USERDATA"]), "SP1.vsave")), output);   // the game's own folder under the platform
            Assert.True(output.Split('\n').Count(l => l.Contains("click: walking to", StringComparison.Ordinal)
                                                     || l.Contains("click: running to", StringComparison.Ordinal)) >= 2, output);

            // And it replays the same: a replay hears the same events on the same frames and says the same seats.
            var (checkedCode, checking) = ReplayBaselineTests.RunWith(env, app, game, "--replay-check", tape, "--mute", "--no-log-file");
            Assert.True(checkedCode == 0 && checking.Contains("replay-check: the same"), ReplayBaselineTests.Checked(checking));
            passed = true;
        }
        // A failed run's folder is kept, its recording and digest in it, so a rare miss can be looked at afterwards.
        finally { if (passed) Directory.Delete(dir, recursive: true); }
    }
}
