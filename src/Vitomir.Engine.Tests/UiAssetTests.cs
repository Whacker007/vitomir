// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The UI's assets against a real install. There is no GPU in the test environment, so these check
/// the half that does not need one: that every pane parses, that every texture and font a pane
/// names actually resolves, and that the status bar still declares the controls the HUD binds by
/// name — which is what breaks first if a pane is edited or a texture pack renames something.
/// </summary>
public class UiAssetTests
{
    [Fact]
    public void Every_shipped_pane_parses_with_nothing_the_engine_would_drop()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        int panes = 0;
        var complaints = new List<string>();
        foreach (var path in files.List("ui/").Where(n => n.EndsWith(".nui", StringComparison.OrdinalIgnoreCase)))
        {
            var pane = assets.Pane(Path.GetFileName(path));
            panes++;
            complaints.AddRange(pane.Warnings);
        }
        Assert.Equal(63, panes);
        Assert.Empty(complaints);
    }

    /// <summary>
    /// Every quad's material and every control's bitmap resolves through the original's own search
    /// path. A miss here is a texture the HUD would silently not draw.
    /// </summary>
    [Fact]
    public void Every_texture_the_status_bar_names_is_in_the_install()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var pane = assets.Pane("statusBarSP");

        foreach (var quad in pane.Quads) assets.Texture(quad.Material);
        foreach (var element in pane.Elements)
            foreach (var key in new[] { "bitmap", "checkedbitmap", "depressed", "rollover", "greyed", "fillbitmap", "image0", "image1" })
                if (element.Get(key) is { Length: > 0 } name) assets.Texture(name);

        Assert.Empty(assets.Missing);
        Assert.Equal(7, pane.Quads.Count);
    }

    /// <summary>The controls the status bar pass binds by name, and the font it draws messages in.</summary>
    [Fact]
    public void The_status_bar_declares_what_the_hud_binds_to()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var pane = assets.Pane("statusBarSP");
        var names = pane.Elements.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < 4; i++)
            foreach (var prefix in new[] { "statusIndicators", "statusHealth", "statusBlood", "statusMana", "statusFrenzy", "statusFaith", "statusParty" })
                Assert.Contains($"{prefix}{i}", names);

        var box = pane.Elements.Single(e => e.Name.Equals("sbTextbox", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("textbox", box.Type);
        var font = assets.Font(box.Get("font")!);
        Assert.Equal(10, font.LineHeight);

        // The 640x480 space: the bar fills the width and sits on the bottom edge.
        Assert.Equal(0, pane.Quads.Min(q => q.Pos[0]));
        Assert.Equal(0, pane.Quads.Min(q => q.Pos[1]));
        Assert.Equal(UiAssets.Width, pane.Quads.Max(q => q.Pos[0] + q.Size[0]));
    }

    /// <summary>
    /// Blood and mana share one track and frenzy and faith share another — the pairs sit at the
    /// same height, which is why only one of each can be shown at a time.
    /// </summary>
    [Fact]
    public void The_five_status_bars_sit_on_three_tracks()
    {
        using var files = Install.Open();
        var pane = new UiAssets(files).Pane("statusBarSP");
        NuiElement Find(string name) => pane.Elements.First(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(Find($"statusBlood{i}").Pos[1], Find($"statusMana{i}").Pos[1]);
            Assert.Equal(Find($"statusFrenzy{i}").Pos[1], Find($"statusFaith{i}").Pos[1]);
            var heights = new[] { $"statusHealth{i}", $"statusBlood{i}", $"statusFrenzy{i}" }
                .Select(n => Find(n).Pos[1]).Distinct().ToArray();
            Assert.Equal(3, heights.Length);
        }
    }

    /// <summary>
    /// **An icon is never stretched, and the shipped files prove it.** The engine's icon blit is a
    /// 1:1 rectangle copy from the source's origin, so an icon file is the cell-sized artwork in a
    /// power-of-two canvas with transparent padding at the top and right that the blit never
    /// reaches — 27 x 32 art in a 32 x 32 file, 54 x 96 in a 64 x 128 one.
    ///
    /// The test is the strong form of that: across every icon in the install, **no plain icon's
    /// artwork is wider or taller than the cell rectangle its canvas implies**, so cropping to the
    /// cell never clips a pixel anybody drew. If it ever does, either the rule is wrong or a mod
    /// has authored an icon the original could not have drawn.
    /// </summary>
    [Fact]
    public void An_icon_is_cell_sized_art_in_a_power_of_two_canvas()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        // Canvas → the cell rectangle the engine takes out of it. Derived from the `_r` variants,
        // which the artists authored at exactly these sizes.
        var cells = new Dictionary<(int, int), (int W, int H)>
        {
            [(32, 32)] = (27, 32), [(32, 64)] = (27, 64), [(32, 128)] = (27, 96),
            [(64, 32)] = (54, 32), [(64, 64)] = (54, 64), [(64, 128)] = (54, 96),
        };
        int checked_ = 0;
        var clipped = new List<string>();
        foreach (var path in files.List("ui/Icons/")
                                  .Where(n => n.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)))
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            if (stem.EndsWith("_r", StringComparison.OrdinalIgnoreCase)
                || stem.EndsWith("_l", StringComparison.OrdinalIgnoreCase)) continue;
            if (assets.Texture(stem) is not { HasAlpha: true } image) continue;
            if (!cells.TryGetValue((image.Width, image.Height), out var cell)) continue;
            checked_++;
            // The artwork's own extent, measured by the same code the renderer places icons with.
            // The engine's origin is the bottom left, so the padding has to be at the top and the
            // right for a cell-sized copy from that origin to take everything.
            var (_, top, right, _) = assets.Bounds(stem);
            if (right > cell.W || image.Height - top > cell.H)
                clipped.Add($"{stem} ({image.Width}x{image.Height}): art {right} wide, "
                          + $"{image.Height - top} tall, cell {cell.W}x{cell.H}");
        }
        Assert.True(checked_ > 300, $"only {checked_} icons checked — is the install complete?");
        Assert.True(clipped.Count == 0, string.Join("\n", clipped.Take(10)));
    }

    /// <summary>
    /// And the measurement itself: a 32 x 32 icon file holds 27 x 32 of artwork at the engine's
    /// origin, so its bounds are the left 27 columns and every row — which is the number
    /// <c>Vitomir.Platform.Render.Classic.UiRenderer</c> places a quick-use icon by.
    /// </summary>
    [Fact]
    public void An_icons_bounds_are_its_artwork_and_not_its_canvas()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        Assert.SkipWhen(assets.Texture("vitaeIcon") is null, "vitaeIcon not in the install");
        Assert.Equal((32, 32), (assets.Texture("vitaeIcon")!.Width, assets.Texture("vitaeIcon")!.Height));
        Assert.Equal((0, 0, 27, 32), assets.Bounds("vitaeIcon"));
        // The large variant is authored at exactly its control's 40 x 48, and its art reaches every
        // edge but the top row — so it needs no placing at all, which is why `Icon` draws it 1:1.
        Assert.Equal((0, 1, 40, 48), assets.Bounds("celerityIcon" + StatusBar.LargeSuffix));
        // And a name the install has not got answers with nothing rather than throwing.
        Assert.Equal((0, 0, 0, 0), assets.Bounds("noSuchIconAnywhere"));
    }

    /// <summary>
    /// Every discipline has **three** bitmaps in the install and each is authored at exactly the
    /// size of the control that draws it: the plain one for a quick slot (27 x 32 of a 32 x 32
    /// canvas), `_R` with its red selection ring for the slot holding the selected discipline, and
    /// `_L` at 40 x 48 for `sbCurrentDiscipline`. The original asserts on the `_L` while it
    /// reads `game.ndd`, so a missing one is a broken install rather than a thing to tolerate.
    /// </summary>
    [Fact]
    public void Every_discipline_ships_all_three_of_its_icons_at_the_sizes_the_bar_draws()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var powers = new Vitomir.Engine.Rpg.DisciplineDefinitions(files);
        Assert.SkipWhen(powers.File is null, "misc/game.ndd not in the install");

        var missing = new List<string>();
        foreach (var d in powers.File!.All)
        {
            Check(d.Icon, 32, 32);
            Check(d.Icon + StatusBar.SelectedSuffix, StatusBar.CellWidth, StatusBar.CellHeight);
            Check(d.Icon + StatusBar.LargeSuffix, 40, 48);

            void Check(string name, int w, int h)
            {
                if (assets.Texture(name) is not { } art) { missing.Add($"{name}: not in the install"); return; }
                if (art.Width != w || art.Height != h)
                    missing.Add($"{name}: {art.Width}x{art.Height}, expected {w}x{h}");
            }
        }
        Assert.Equal(73, powers.File!.All.Count);
        Assert.True(missing.Count == 0, string.Join("\n", missing.Take(10)));
    }
}
