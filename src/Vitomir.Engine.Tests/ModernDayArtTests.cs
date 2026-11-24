// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The panes' modern-day art**: a modern-day chronicle that is
/// not a multiplayer one (info bits 1 set, 2 clear) has the seven panes loaded with the
/// flag, which renames every quad's material — its extension cut and `md` added
/// — and the character sheet's soak pie takes its `%s%dMD` frames. London and New York
/// wear it; Prague, Vienna and the multiplayer chronicles do not.
/// </summary>
public class ModernDayArtTests
{
    /// <summary>The name the loader makes: `DisciplinePane01` → `DisciplinePane01md`, `InventoryPane01.tga` → `InventoryPane01md`.</summary>
    [Fact]
    public void A_quads_material_loses_its_extension_and_gains_md()
    {
        Assert.Equal("disciplinepane01md", UiAssets.ModernDayName("disciplinepane01"));
        Assert.Equal("inventorypane01md", UiAssets.ModernDayName("inventorypane01.tga"));
        Assert.Equal(@"ui\art.v2\panemd", UiAssets.ModernDayName(@"ui\art.v2\pane.tga"));
        Assert.Equal(@"ui\art.v2\panemd", UiAssets.ModernDayName(@"ui\art.v2\pane"));
    }

    /// <summary>
    /// The test on the chronicle's info word: London and New York are modern day and not
    /// multiplayer; Prague is neither; a multiplayer modern-day chronicle keeps the Dark Ages art.
    /// </summary>
    [Fact]
    public void London_and_New_York_wear_the_modern_day_art_and_Prague_does_not()
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        Assert.True(new Session { Chronicle = set["London"] }.ModernDayArt);
        Assert.True(new Session { Chronicle = set["New_York"] }.ModernDayArt);
        Assert.False(new Session { Chronicle = set["Prague"] }.ModernDayArt);
        Assert.False(new Session { Chronicle = set["Vienna"] }.ModernDayArt);
        Assert.False(new Session().ModernDayArt);
        foreach (var c in set.All.Where(c => (c.Flags & 2) != 0)) Assert.False(new Session { Chronicle = c }.ModernDayArt);
    }

    /// <summary>
    /// The switch renames the quads of the seven panes — every one of which has its picture in the
    /// install — and back again; a pane outside the seven (the vault) keeps its art either way, and
    /// a pane first loaded while the switch is on comes dressed.
    /// </summary>
    [Fact]
    public void The_switch_dresses_the_seven_panes_and_only_them()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var disc = assets.Pane("disciplinePane");
        var vault = assets.Pane("VaultPane");
        var plain = disc.Quads.Select(q => q.Material).ToArray();
        var vaultPlain = vault.Quads.Select(q => q.Material).ToArray();
        assets.SetModernDay(true);
        Assert.Equal(plain.Select(UiAssets.ModernDayName), disc.Quads.Select(q => q.Material));
        Assert.Equal(vaultPlain, vault.Quads.Select(q => q.Material));
        foreach (var name in UiAssets.ModernDayPanes)
            foreach (var q in assets.Pane(name).Quads)
            {
                Assert.EndsWith("md", q.Material);
                Assert.NotNull(assets.Texture(q.Material));
            }
        assets.SetModernDay(false);
        Assert.Equal(plain, disc.Quads.Select(q => q.Material));
    }
}
