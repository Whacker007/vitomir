// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Dropping money from the pack: the coin button's input box takes
/// digits only and asks nothing of a character with no money; OK with a number the purse covers
/// leaves a `GoldBag` of it at the character's feet and takes it from the party's purse; a number
/// too large, 0, or Cancel does nothing. Also the input box's keys, which 1.1 honours only while
/// their button is on the box.
/// </summary>
public class DropCashTests
{
    sealed record Rig(FrontEnd Menus, Location Place, Session Session, Strings Text)
    {
        public WorldThing Me => Place.Steered!;
        public MenuEdit Edit => Menus.Pane(FrontEnd.InputBoxFile).Edits["ibEdit"];
        public int Bags => Place.Things.Count(t => t.Template.Equals(DropCash.GoldBag, StringComparison.OrdinalIgnoreCase));
    }

    static Rig Open()
    {
        var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateGame };
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var session = new Session { Here = place };
        place.Session = session;
        return new Rig(menus, place, session, text);
    }

    static void Type(FrontEnd menus, string s)
    {
        foreach (char c in s) menus.Type(c);
    }

    static void Key(FrontEnd menus, MenuKey key)
    {
        menus.Drawn();
        menus.Key(key);
    }

    [Fact]
    public void The_coin_button_asks_nothing_of_an_empty_purse()
    {
        var r = Open();
        r.Session.Gold = 0;
        Assert.False(DropCash.Ask(r.Menus, r.Me, r.Text.Find));
        Assert.False(r.Menus.Pane(FrontEnd.InputBoxFile).Shown);
    }

    [Fact]
    public void The_box_asks_how_much_and_takes_digits_only()
    {
        var r = Open();
        r.Session.Gold = 300;
        Assert.True(DropCash.Ask(r.Menus, r.Me, r.Text.Find));
        var box = r.Menus.Pane(FrontEnd.InputBoxFile);
        Assert.Equal(r.Text.Find(DropCash.Prompt), box.Texts["ibTitle"]);
        Assert.Equal(EditAccepts.Digits, r.Edit.Accepts);
        r.Menus.Frame(1);
        Type(r.Menus, "-1a2 5");
        Assert.Equal("125", r.Edit.Text);
    }

    [Fact]
    public void OK_leaves_a_bag_at_the_feet_and_empties_the_purse_by_that_much()
    {
        var r = Open();
        r.Session.Gold = 300;
        int before = r.Bags;
        DropCash.Ask(r.Menus, r.Me, r.Text.Find);
        r.Menus.Frame(1);
        Type(r.Menus, "120");
        Key(r.Menus, MenuKey.Enter);
        Assert.False(r.Menus.Pane(FrontEnd.InputBoxFile).Shown);
        Assert.Equal(180, r.Session.Gold);
        Assert.Equal(before + 1, r.Bags);
        var bag = r.Place.Things.Last(t => t.Template.Equals(DropCash.GoldBag, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(120, bag.Item!.Amount);
        Assert.True(System.Numerics.Vector3.Distance(bag.Position, r.Me.Position) < 100, "the bag is at the character's feet");
    }

    /// <summary>
    /// **The bag goes down beside the character, not under him** (the placing,
    /// distance 0 and flags 0xb — the placement a death's loot takes): "just clear" is the two
    /// bodies' radii, each counted only when it collides, times 1.1, in the first of the eight
    /// directions round the character's forward that has a floor. Found by a comparison with 1.1
    /// library; Vitomir had spawned it on the character's own position.
    /// </summary>
    [Fact]
    public void The_bag_is_put_down_just_clear_of_the_character()
    {
        var r = Open();
        var bag = DropCash.Bag(r.Me, 50)!;
        Assert.Equal(50, bag.Item!.Amount);
        float clear = ((bag.Collide != CollideShape.None ? bag.Radius : 0f)
                       + (r.Me.Collide != CollideShape.None ? r.Me.Radius : 0f)) * 1.1f;
        Assert.True(clear > 0f);
        float flat = System.Numerics.Vector2.Distance(new(bag.Position.X, bag.Position.Y), new(r.Me.Position.X, r.Me.Position.Y));
        Assert.Equal(clear, flat, 1);
        Assert.Contains(bag, r.Place.Things);
    }

    /// <summary>
    /// In play the coin button was deaf. The `.nui` reader lowercases the pane's
    /// lines, so the button under the pointer comes back as `invgoldbutton`, and the press and the
    /// release compared it with `invGoldButton` exactly. This goes the way a click does — a window
    /// pixel through <see cref="StatusBar.FromWindow"/>, the installed pane's own hit box, the test
    /// both handlers make (<see cref="DropCash.At"/>), then the box and OK — at 1280 x 960.
    /// </summary>
    [Fact]
    public void A_click_on_the_installed_coin_button_puts_the_box_up_and_drops_the_money()
    {
        var r = Open();
        r.Session.Gold = 500;
        var pack = new InventoryPane(new UiAssets(Install.Open()).Pane(InventoryPane.PaneName));
        Assert.Equal("invgoldbutton", pack.ButtonAt(572, 457));          // as the reader keeps it
        var (x, y) = StatusBar.FromWindow(1144, 44, 1280, 960, UiLayoutMode.Stretch);   // the coin, 560,442 26x32 (1.1's stretch, named: an earlier build)
        Assert.True(DropCash.At(pack, x, y));                              // the press
        Assert.True(DropCash.At(pack, x + 1, y - 1));                      // the release, a pixel over
        Assert.False(DropCash.At(pack, 559, 457));                         // just left of it
        Assert.False(DropCash.At(pack, 572, 441));                         // just under it (y runs up)
        Assert.True(DropCash.Ask(r.Menus, r.Me, r.Text.Find));
        r.Menus.Frame(1);
        Type(r.Menus, "25");
        Key(r.Menus, MenuKey.Enter);
        Assert.Equal(475, r.Session.Gold);
    }

    [Fact]
    public void More_than_the_purse_or_nothing_or_Cancel_drops_nothing()
    {
        var r = Open();
        r.Session.Gold = 50;
        int before = r.Bags;
        Assert.False(DropCash.Answered(r.Me, 1, "51"));
        Assert.False(DropCash.Answered(r.Me, 1, "0"));
        Assert.False(DropCash.Answered(r.Me, 1, ""));
        Assert.False(DropCash.Answered(r.Me, 2, "10"));
        Assert.Equal(50, r.Session.Gold);
        Assert.Equal(before, r.Bags);
        Assert.True(DropCash.Answered(r.Me, 1, "50"));      // all of it is allowed
        Assert.Equal(0, r.Session.Gold);
    }

    /// <summary>`sscanf("%d")`: white space, a sign, digits, and whatever follows ignored.</summary>
    [Theory]
    [InlineData("42", 42)]
    [InlineData("  7x", 7)]
    [InlineData("-3", -3)]
    [InlineData("+9", 9)]
    [InlineData("x1", null)]
    [InlineData("", null)]
    [InlineData("4294967297", 1)]
    public void The_number_is_read_as_sscanf_reads_it(string text, int? expected) =>
        Assert.Equal(expected, DropCash.Scan(text));

    /// <summary>Enter only while OK is on the box, Escape only while Cancel is.</summary>
    [Fact]
    public void The_input_box_keys_answer_only_for_a_button_it_shows()
    {
        var r = Open();
        int heard = 0;
        r.Menus.Input("t", "", 5, (res, _) => heard = res, ok: false, cancel: true);
        r.Menus.Frame(1);
        Key(r.Menus, MenuKey.Enter);
        Assert.Equal(0, heard);
        Assert.True(r.Menus.Pane(FrontEnd.InputBoxFile).Shown);
        Key(r.Menus, MenuKey.Escape);
        Assert.Equal(2, heard);

        r.Menus.Input("t", "", 5, (res, _) => heard = res, ok: true, cancel: false);
        r.Menus.Frame(2);
        Key(r.Menus, MenuKey.Escape);
        Assert.True(r.Menus.Pane(FrontEnd.InputBoxFile).Shown);
        Key(r.Menus, MenuKey.Enter);
        Assert.Equal(1, heard);
    }
}
