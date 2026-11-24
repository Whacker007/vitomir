// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;

namespace Vitomir.Engine.Tests;

/// <summary>
/// An item's `ENUM_ICONSIZE` as the icon's maker reads it: a table, not
/// an encoding. The two values no shipped template uses, 3 and 6, were left 1x1 until the Ui
/// A comparison with 1.1 read the switch: they are 1x4 and 2x4.
/// </summary>
public class IconSizeTests
{
    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 2)]
    [InlineData(2, 1, 3)]
    [InlineData(3, 1, 4)]
    [InlineData(4, 2, 2)]
    [InlineData(5, 2, 3)]
    [InlineData(6, 2, 4)]
    [InlineData(7, 2, 1)]
    public void Each_icon_size_is_the_icon_makers_cells(int size, int across, int down)
    {
        Assert.Equal((across, down), new Item { Name = "x", IconSize = size }.IconCells);
    }
}
