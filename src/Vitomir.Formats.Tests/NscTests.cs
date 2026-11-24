// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using Vitomir.Formats;

namespace Vitomir.Formats.Tests;

/// <summary>
/// The chronicle reader, against a file written the way the shipped ones are: hash comments,
/// tab-aligned columns, names and numbers separated by a comma or by nothing at all.
/// </summary>
public class NscTests
{
    static NscChronicle Parse(string text) =>
        NscChronicle.Read(Encoding.Latin1.GetBytes(text.ReplaceLineEndings("\r\n")), "Test.nsc");

    const string Sample = """
        ##########################################################
        # A chronicle, written the way the shipped ones are.     #
        ##########################################################
        ; a second comment marker the parser also honours

        location	Town	P1_TOWN.nil	TOWN_1_1.nsd	map.nui	mapTown	0x0060
        location	Shop	P1_SHOP.nil	SHOP_1_1.nsd	map.nui	mapShop	0x0020
        location	Vault	P1_VALT.nil	null    	map.nui	mapVault
        location	Cave	P1_CAVE.nil	CAVE_1_1.nsd	map.nui	mapCave	0x0

        # Town exits
        exit	Town,1	Shop,0
        exit	Town,2	Vault, 0	0x1
        exit	Town,4	Cave,0		0x100

        #exit	Town,9	Nowhere,0

        script		TestChronicle
        metafile	test.nmf
        portal		Shop 3
        reviveloc	Cave 2
        flags		0x10001
        """;

    [Fact]
    public void It_reads_the_locations_in_file_order()
    {
        var c = Parse(Sample);
        Assert.Equal(4, c.Locations.Count);
        Assert.Equal(["Town", "Shop", "Vault", "Cave"], c.Locations.Select(l => l.Name));
        Assert.Equal("P1_TOWN", c.At(0)!.LevelName);
        Assert.Equal("TOWN_1_1", c.At(0)!.SceneName);
        // The first location listed is where the chronicle starts (NodSDK\Docs\nsc.htm).
        Assert.Equal("Town", c.Start!.Name);
    }

    [Fact]
    public void A_location_with_no_scene_says_so()
    {
        // `null` is written literally, for a location that exists only so that exits into it can
        // be locked and the locked-door text works.
        Assert.Equal("", Parse(Sample).At("Vault")!.SceneName);
    }

    [Fact]
    public void The_flags_argument_is_optional_and_hexadecimal()
    {
        var c = Parse(Sample);
        Assert.Equal(0x60u, c.At("Town")!.Flags);
        Assert.Equal(0u, c.At("Vault")!.Flags);            // the line stops after the map marker
        Assert.Equal(0u, c.At("Cave")!.Flags);
        // 0x0060 is what the game's big outdoor hubs carry: no teleports, and clear the buffer.
        var town = (NscLocationFlags)c.At("Town")!.Flags;
        Assert.True(town.HasFlag(NscLocationFlags.NoTeleports));
        Assert.True(town.HasFlag(NscLocationFlags.ClearBuffer));
    }

    [Fact]
    public void An_exit_links_both_ways_unless_it_says_one_way()
    {
        var c = Parse(Sample);
        var town = c.At("Town")!;
        var shop = c.At("Shop")!;
        var cave = c.At("Cave")!;

        Assert.Equal(c.Find("Shop"), town.Exits[1].To);
        Assert.Equal(0, town.Exits[1].Entrance);
        // The loader writes the backward link, which is why a chronicle lists each link once.
        Assert.Equal(c.Find("Town"), shop.Exits[0].To);
        Assert.Equal(1, shop.Exits[0].Entrance);
        // ONEWAY suppresses it: the cave has no way back to town.
        Assert.Equal(c.Find("Cave"), town.Exits[4].To);
        Assert.Equal(-1, cave.Exits[0].To);
    }

    [Fact]
    public void Closed_is_set_on_both_sides_at_load()
    {
        var c = Parse(Sample);
        Assert.True(c.At("Town")!.Exits[2].Closed);
        Assert.True(c.At("Vault")!.Exits[0].Closed);
        Assert.False(c.At("Town")!.Exits[1].Closed);
    }

    [Fact]
    public void The_exit_count_is_one_past_the_highest_number_used()
    {
        var c = Parse(Sample);
        Assert.Equal(5, c.At("Town")!.ExitCount);           // exits 1, 2 and 4 named
        Assert.Equal(1, c.At("Shop")!.ExitCount);           // only the backward link
        Assert.Equal(1, c.At("Cave")!.ExitCount);           // ONEWAY still counts the entrance
    }

    [Fact]
    public void A_comma_is_a_separator_like_a_space()
    {
        // `exit Town,2 Vault, 0` — the shipped files write it both ways on the same line.
        var c = Parse(Sample);
        Assert.Equal(c.Find("Vault"), c.At("Town")!.Exits[2].To);
        Assert.Equal(0, c.At("Town")!.Exits[2].Entrance);
    }

    [Fact]
    public void It_reads_the_chronicle_settings()
    {
        var c = Parse(Sample);
        Assert.Equal(["TestChronicle"], c.Scripts);
        Assert.Equal("test.nmf", c.Metafile);
        Assert.Equal(c.Find("Shop"), c.PortalLocation);
        Assert.Equal(3, c.PortalEntrance);
        Assert.Equal(c.Find("Cave"), c.ReviveLocation);
        Assert.Equal(2, c.ReviveEntrance);
        Assert.Equal(0x10001u, c.Flags);
        Assert.True(c.Kind.HasFlag(NscChronicleFlags.ModernDay));
        Assert.True(c.Kind.HasFlag(NscChronicleFlags.CampaignStart));
    }

    [Fact]
    public void Comment_lines_and_unknown_directives_are_left_alone()
    {
        var c = Parse(Sample + "\nwibble  Town  3\n");
        Assert.DoesNotContain(c.Locations, l => l.Name.StartsWith('#'));
        Assert.Contains("wibble  Town  3", c.Ignored);
        // The commented-out exit names a location that does not exist, and was never parsed.
        Assert.DoesNotContain(c.Ignored, l => l.Contains("Nowhere"));
    }

    [Fact]
    public void An_exit_naming_a_location_that_does_not_exist_is_reported_not_applied()
    {
        var c = Parse(Sample + "\nexit  Town,7  Atlantis,0\n");
        Assert.Contains(c.Ignored, l => l.Contains("Atlantis"));
        Assert.Equal(5, c.At("Town")!.ExitCount);
    }

    [Fact]
    public void An_exit_number_past_the_thirty_two_a_location_has_is_refused()
    {
        // The original bounds-checks the near side and not the far side, where it would write past
        // the record. Both are refused here: a memory bug is a code bug.
        var c = Parse(Sample + "\nexit  Town,40  Shop,0\nexit  Town,6  Shop,40\n");
        Assert.Equal(2, c.Ignored.Count(l => l.Contains("Shop")));
        Assert.Equal(5, c.At("Town")!.ExitCount);
    }

    [Theory]
    [InlineData("0x0020", 0x20u)]
    [InlineData("0X1f", 0x1fu)]
    [InlineData("9", 9u)]
    [InlineData("b4", 0xb4u)]
    [InlineData("zz", 0u)]
    [InlineData("0x10000", 0x10000u)]
    public void Flags_parse_the_way_C_reads_percent_x(string text, uint expected) =>
        Assert.Equal(expected, NscChronicle.Read(
            Encoding.Latin1.GetBytes($"location A a.nil a.nsd a.nui a {text}"), "T.nsc").At(0)!.Flags);
}
