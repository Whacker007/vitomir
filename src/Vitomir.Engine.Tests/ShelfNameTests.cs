// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A shelf row for an item with no description says "Missing description!!!"** (the
/// name, the identified description's text else the plain one's, and with neither the
/// shelf's own fallback string). Vitomir showed the template's name until the
/// A comparison with 1.1.
/// </summary>
public class ShelfNameTests
{
    [Fact]
    public void An_item_with_no_description_is_missing_description_on_the_shelf()
    {
        using var files = Install.Open();
        var pane = new BuySellPane(Formats.NuiPane.Read(files.Read("ui/buySellPane.nui")));
        var bare = new TreasureRecord { Template = "someitem", Item = new Item { Name = "someitem" } };
        Assert.Equal(BuySellPane.MissingDescription, pane.NameOf(bare));

        var named = new TreasureRecord { Template = "someitem", Item = new Item { Name = "someitem", DescriptionId = "ITEM_X" } };
        Assert.Equal("ITEM_X", pane.NameOf(named));        // no string table: the key itself
    }
}
