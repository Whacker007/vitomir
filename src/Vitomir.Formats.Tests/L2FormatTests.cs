// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json.Nodes;
using Vitomir.Formats.L2;

namespace Vitomir.Formats.Tests;

/// <summary>
/// **The L2 container** (ours): a cell's chunks come back from a write and a
/// read in their order, with the chunks this build does not know kept byte for byte (the format's
/// inert extras); a manifest keeps the properties it does not know; the anchor chunk carries its
/// records whole; and the legacy encoder writes every shipped `.nil` back byte for byte from the
/// model — all but the editor's leftovers after the NUL of a material's name, which the reader never
/// keeps — which is what makes the legacy chunks a faithful carrier.
/// </summary>
public class L2FormatTests
{
    [Fact]
    public void A_cells_chunks_come_back_in_order_with_the_unknown_ones_untouched()
    {
        var cell = new L2Cell
        {
            Chunks =
            [
                new("L1SC", 1, [1, 2, 3]),
                new("ZZZZ", 7, [9, 8, 7, 6, 5]),          // a chunk nobody knows
                new("ANCH", 1, []),
                new("Xtra", 99, new byte[70_000]),
            ],
        };
        var back = L2Cell.Read(cell.Write(), "test");
        Assert.Equal(cell.Chunks.Select(c => (c.Tag, c.Version)), back.Chunks.Select(c => (c.Tag, c.Version)));
        for (int i = 0; i < cell.Chunks.Count; i++) Assert.Equal(cell.Chunks[i].Payload, back.Chunks[i].Payload);
        // Written again from what was read: the same bytes.
        Assert.Equal(cell.Write(), back.Write());
    }

    [Fact]
    public void A_cell_that_is_not_one_or_runs_short_is_refused()
    {
        Assert.Throws<NodFormatException>(() => L2Cell.Read("NOPE\x01\0\0\0"u8.ToArray(), "t"));
        var bytes = new L2Cell { Chunks = [new("L1SC", 1, [1, 2, 3, 4])] }.Write();
        Assert.Throws<NodFormatException>(() => L2Cell.Read(bytes[..^1], "t"));
    }

    [Fact]
    public void A_manifest_keeps_what_it_does_not_know()
    {
        var m = new L2Manifest { Name = "TEST" };
        m.Extras["painter"] = "ours";
        var text = m.Write();
        var node = JsonNode.Parse(text)!.AsObject();
        node["futureThing"] = new JsonObject { ["a"] = 1 };
        node["units"]!.AsObject()["scale2"] = 3;
        var back = L2Manifest.Read(node.ToJsonString());
        var again = JsonNode.Parse(back.Write())!.AsObject();
        Assert.Equal(1, (int)again["futureThing"]!["a"]!);
        Assert.Equal(3, (int)again["units"]!["scale2"]!);
        Assert.Equal("ours", (string)again["extras"]!["painter"]!);
        Assert.Equal(72f, back.Units.EngineUnitsPerMetre);
    }

    [Fact]
    public void An_anchor_chunk_carries_its_anchors_whole()
    {
        L2Anchor[] anchors =
        [
            new() { Id = "S/things/0", Kind = "actor", Scene = "S", Index = 0, Name = "Knight", Template = "Knight", Guid = 271,
                    Script = "Guard", Sector = 12, Position = [1.5f, -2, 3], Rotation = [0, 0, 90] },
            new() { Id = "S/sector-sounds/3", Kind = "sector-sound", Scene = "S", Index = 3, Name = "wind.wav", Sectors = [4, 5, 6], Group = 2 },
        ];
        var back = L2AnchorChunk.Decode(L2AnchorChunk.Encode(anchors), "c");
        Assert.Equal(2, back.Count);
        Assert.Equal(("S/things/0", "actor", "Knight", 271, "Guard", 12L), (back[0].Id, back[0].Kind, back[0].Template, back[0].Guid!.Value, back[0].Script, back[0].Sector!.Value));
        Assert.Equal(anchors[0].Position, back[0].Position);
        Assert.Equal(anchors[0].Rotation, back[0].Rotation);
        Assert.Null(back[1].Guid);
        Assert.Null(back[1].Template);
        Assert.Equal(new uint[] { 4, 5, 6 }, back[1].Sectors);
        Assert.Equal(2u, back[1].Group);
    }

    [Fact]
    public void Every_shipped_level_is_written_back_from_its_model_byte_for_byte()
    {
        using var nob = GameData.Open("Levels.nob");
        int levels = 0;
        long bytes = 0, junk = 0;
        var differ = new List<string>();
        foreach (var name in nob.NamesWithExtension(".nil"))
        {
            var data = nob.Read(name);
            var level = NilLevel.Read(data, name);
            junk += ClearMaterialTails(data, level.Materials.Length);
            var again = L2Legacy.WriteNil(level);
            if (!again.AsSpan().SequenceEqual(data))
                differ.Add($"{name}: {again.Length} vs {data.Length} bytes, first difference at 0x{again.AsSpan().CommonPrefixLength(data):x}");
            // And through the three chunks.
            var cell = L2Cell.Read(new L2Cell { Chunks = [.. L2Legacy.Encode(level)] }.Write(), name);
            Assert.Equal(data, L2Legacy.WriteNil(L2Legacy.Decode(cell, name)));
            levels++; bytes += data.Length;
        }
        Assert.Empty(differ);
        Assert.Equal(112, levels);
        // What the model does not keep: the level editor's leftovers after the NUL of a material's
        // 32-byte name. Nothing reads them; they are counted here so the claim stays exact.
        Assert.InRange(junk, 0, bytes / 100);
    }

    /// <summary>
    /// Zeroes the bytes after the NUL in each of the NIL's 32-byte material names (they follow the
    /// 8-byte header, the 72-byte global light and the 12-byte ambient colour and a u32 count) and
    /// returns how many were not zero already.
    /// </summary>
    static int ClearMaterialTails(byte[] data, int materials)
    {
        int changed = 0;
        for (int m = 0, at = 8 + 72 + 12 + 4; m < materials; m++, at += 32)
        {
            int nul = Array.IndexOf(data, (byte)0, at, 32);
            for (int i = nul + 1; nul >= 0 && i < at + 32; i++)
                if (data[i] != 0) { data[i] = 0; changed++; }
        }
        return changed;
    }
}
