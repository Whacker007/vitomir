// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The swivel lock's centred look** (ours). With the lock on (Ctrl and the middle
/// button) the mouse turns the camera and the game's pointer sits at the middle of **the world's view** —
/// the columns a pane leaves it and the rows above the status bar, not the whole window — with
/// the ankh's red tip, its hotspot, exactly there; a click is a click at the middle, so it acts on what is
/// under it; and anything that wants the pointer, opening, ends the lock and leaves the pointer free where
/// it was drawn. These tests hold the place for several windows and pane layouts, the ray through it, the
/// ankh's tip, the click, and the panes that end it.
/// </summary>
public class CentreLookTests
{
    static int Rows(int height, bool bar) => bar ? (int)MathF.Round(height * StatusBar.WorldShare) : height;

    static ViewColumns Layout(bool bar, bool left, bool right) => WorldView.Columns(bar, left, right, 80f);

    [Theory]
    // width, height, bar up, left pane (the character sheet), right pane (the inventory), the middle's x and y
    [InlineData(1600, 900, true, false, false, 800f, 341f)]     // a 1600 x 900 window, the bar up: 682 of the 900 rows are the world's
    [InlineData(1600, 900, false, false, false, 800f, 450f)]    // the bar hidden (F11): the whole window
    [InlineData(1600, 900, true, false, true, 400f, 341f)]      // the inventory open: the world is the left half
    [InlineData(1600, 900, true, true, false, 1200f, 341f)]     // the character sheet open: the right half
    [InlineData(1024, 768, true, false, false, 512f, 291f)]     // 582 rows
    [InlineData(1280, 720, true, false, true, 320f, 273f)]      // 546 rows, the left half
    [InlineData(1920, 1080, false, true, false, 960f, 540f)]    // a pane with the bar hidden: nothing is narrowed
    public void The_pointer_is_held_at_the_middle_of_the_world_s_view(int w, int h, bool bar, bool left, bool right, float cx, float cy)
    {
        var view = Layout(bar, left, right);
        if (!bar) Assert.Equal(ViewColumns.Whole, view);
        var at = CentreLook.At(w, h, Rows(h, bar), view)!.Value;
        // The mouse's place is the pixel whose middle is the view's middle (a mouse at p means p + 0.5, Pick.Ray).
        Assert.Equal(cx - 0.5f, at.X, 3);
        Assert.Equal(cy - 0.5f, at.Y, 3);
        // The cursor's point, in the 640 x 480 .nui space it is drawn in (y up), is the same place exactly.
        // (Wherever the layout puts that space on the picture: UiLayout)
        var (tipX, tipY) = UiLayout.ToPicture(at.TipX, at.TipY, w, h);
        Assert.Equal(cx, tipX, 2);
        Assert.Equal(cy, tipY, 2);
        // And the ray through it is the camera's own axis, whatever the layout.
        var cam = new FreeCamera { Position = new Vector3(10, 20, 30), Yaw = 37f, Pitch = -21f };
        var (_, direction) = Pick.Ray(cam, at.X, at.Y, w, h, Rows(h, bar), view);
        Assert.True(Vector3.Dot(direction, cam.Forward) > 0.999999f, $"{direction} is not {cam.Forward}");
    }

    [Fact]
    public void With_a_pane_on_each_side_there_is_no_world_to_look_at()
    {
        Assert.False(Layout(true, true, true).Drawn);
        Assert.Null(CentreLook.At(1600, 900, Rows(900, true), Layout(true, true, true)));
        Assert.Null(CentreLook.At(0, 0, 0, ViewColumns.Whole));
    }

    [Fact]
    public void The_ankh_s_red_tip_is_its_hotspot_and_lands_on_the_middle()
    {
        // The default cursor's point is the art's top left (mode 1), and so the
        // sword's and the hand's; the draw puts the art's top-left corner on the pointer (Cursors.Corner).
        Assert.Equal(CursorAnchor.TopLeft, Cursors.Anchor(CursorId.Default));
        var at = CentreLook.At(1600, 900, Rows(900, true), ViewColumns.Whole)!.Value;
        var (x, y) = Cursors.Corner(Cursors.Anchor(CursorId.Default), at.TipX, at.TipY, Cursors.Size, Cursors.Size);
        Assert.Equal(at.TipX, x, 4);                    // the art's left edge
        Assert.Equal(at.TipY, y + Cursors.Size, 4);     // and its top (the quad's corner is its bottom left, y up)

        // And that corner of the ankh is its red tip: the art's first opaque pixels, along the blade from
        // the top left, are red.
        using var files = Install.Open();
        var art = TgaImage.Read(files.Read($"ui/Materials/{Cursors.Name(CursorId.Default)}.tga"));
        (byte R, byte G, byte B, byte A) Px(int px, int py) { int i = (py * art.Width + px) * 4; return (art.Rgba[i], art.Rgba[i + 1], art.Rgba[i + 2], art.Rgba[i + 3]); }
        Assert.True(Px(0, 0).A > 0x80, "the ankh's top-left pixel is not drawn");
        foreach (var (px, py) in new[] { (0, 0), (1, 1), (2, 2), (3, 3) })
        {
            var p = Px(px, py);
            Assert.True(p.R > 2 * p.G && p.R > 2 * p.B, $"the ankh at {px},{py} is not red: {p}");
        }
    }

    [Fact]
    public void A_click_while_locked_is_a_click_at_the_middle_and_takes_what_is_under_it()
    {
        // The first monastery's gold bag on its table, looked at from above and behind.
        using var files = Install.Open();
        var loc = Location.Load(files, "P1_MON1", sky: false, scene: ChronicleSet.Load(files).ForLevel("P1_MON1")?.Entry?.SceneName, player: "Christof");
        var bag = loc.Things.First(t => t.Template.Equals("goldbag", StringComparison.OrdinalIgnoreCase));
        var cam = new FreeCamera { Yaw = 30f, Pitch = -35f };
        cam.Orbit(bag.Position, 260f);
        const int W = 1600, H = 900;
        int rows = Rows(H, true);
        var at = CentreLook.At(W, H, rows, ViewColumns.Whole);

        // The system pointer is hidden and wherever the mouse took it — the top left, say. Locked, the click is at the middle...
        var (cx, cy) = CentreLook.Click(at, 12f, 10f);
        Assert.Equal((at!.Value.X, at.Value.Y), (cx, cy));
        var (o, d) = Pick.Ray(cam, cx, cy, W, H, rows, ViewColumns.Whole);
        Assert.Same(bag, Pick.NearestThing([bag], o, d, ignore: loc.Player?.Thing)?.Thing);
        // ...and without the lock it is where it came, which is not on the bag.
        var (fx, fy) = CentreLook.Click(null, 12f, 10f);
        Assert.Equal((12f, 10f), (fx, fy));
        var (fo, fd) = Pick.Ray(cam, fx, fy, W, H, rows, ViewColumns.Whole);
        Assert.Null(Pick.NearestThing([bag], fo, fd, ignore: loc.Player?.Thing));
    }

    [Fact]
    public void A_pane_opening_ends_the_lock_and_one_already_open_does_not()
    {
        var hold = new SwivelHold();
        var look = new CentreLook();
        Assert.True(look.Opened(OpenPanes.Inventory));           // the inventory opened before the lock: nothing to end
        Assert.Equal(SwivelStep.Nothing, hold.End());
        Assert.Equal(SwivelStep.Start, hold.Toggle(gameCamera: true));
        Assert.Equal(SwivelStep.Nothing, hold.Frame(true, false, swivelling: true, held: false));
        Assert.False(look.Opened(OpenPanes.Inventory));          // still up: the lock holds, the middle of the left half
        Assert.True(hold.Locked);
        Assert.False(look.Opened(OpenPanes.None));               // closing it does not end it either
        // The character sheet opening does: the swivel stops and the pointer is free.
        Assert.True(look.Opened(OpenPanes.Character));
        Assert.Equal(SwivelStep.Stop, hold.End());
        Assert.False(hold.Locked);
        Assert.Equal(SwivelStep.Nothing, hold.End());
        // And it does not come back on its own when the pane goes.
        Assert.False(look.Opened(OpenPanes.None));
        Assert.Equal(SwivelStep.Nothing, hold.Frame(true, false, swivelling: false, held: false));
    }

    [Theory]
    [InlineData(OpenPanes.Character)]
    [InlineData(OpenPanes.Inventory)]
    [InlineData(OpenPanes.Quests)]
    [InlineData(OpenPanes.Disciplines)]
    [InlineData(OpenPanes.Vault)]
    [InlineData(OpenPanes.Map)]
    [InlineData(OpenPanes.Shop)]
    [InlineData(OpenPanes.Document)]
    [InlineData(OpenPanes.Answers)]
    [InlineData(OpenPanes.Advancement)]
    [InlineData(OpenPanes.Menu)]
    public void Each_thing_that_wants_the_pointer_ends_it_as_it_opens(OpenPanes pane)
    {
        var look = new CentreLook();
        look.Opened(OpenPanes.None);
        Assert.True(look.Opened(pane));
        Assert.False(look.Opened(pane));
        // One opening beside another already open counts too.
        var other = pane == OpenPanes.Inventory ? OpenPanes.Quests : OpenPanes.Inventory;
        var beside = new CentreLook();
        beside.Opened(other);
        Assert.True(beside.Opened(other | pane));
    }

    [Fact]
    public void The_host_reports_every_kind_of_pane()
    {
        if (Root() is not { } root) { Assert.Skip("not running from inside the tree"); return; }
        // The frame controller's classes (Program.cs's one method until then; the Classic module's library).
        var program = string.Join("\n", Directory.GetFiles(Path.Combine(root, "src", "Vitomir.Classic", "Controller"), "*.cs").Select(File.ReadAllText));
        foreach (var pane in Enum.GetValues<OpenPanes>().Where(p => p != OpenPanes.None))
            Assert.Contains($"OpenPanes.{pane}", program);
        Assert.Contains("CentreLook.Click(lockAt", program);
    }

    static string? Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src", "Vitomir.slnx"))) return dir.FullName;
        return null;
    }
}
