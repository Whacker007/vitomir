// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vitomir.Formats.L2;

/// <summary>
/// **Ours (manifest version 2): a cell level's grid.** The level is cut into square cells of
/// <see cref="CellSize"/> metres, <see cref="Columns"/> across X and <see cref="Rows"/> across Y from
/// <see cref="Origin"/>; cell (c, r) covers X from <c>origin.x + c·size</c> and Y from
/// <c>origin.y + r·size</c>, one size on. Only cells with anything in them are written. The cells are
/// grouped into square blocks of <see cref="BlockCells"/> × <see cref="BlockCells"/>, each with one far
/// version (<see cref="L2FarEntry"/>). The navigation mesh is one Detour tile per cell, on the same grid:
/// <see cref="NavOrigin"/> is the grid's corner in Recast's frame (x, z, −y; the least Recast z is the
/// grid's greatest y), and a cell's tile is (column, rows − 1 − row).
/// </summary>
public sealed class L2Grid
{
    [JsonPropertyName("origin")] public float[] Origin { get; set; } = [0, 0];
    [JsonPropertyName("cellSize")] public float CellSize { get; set; }
    [JsonPropertyName("columns")] public int Columns { get; set; }
    [JsonPropertyName("rows")] public int Rows { get; set; }
    [JsonPropertyName("blockCells")] public int BlockCells { get; set; } = 2;
    /// <summary>The navigation mesh's origin in Recast's frame (metres, Y up): Detour's <c>DtNavMeshParams.orig</c>.</summary>
    [JsonPropertyName("navOrigin")] public float[] NavOrigin { get; set; } = [0, 0, 0];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>A gridded cell's id: <c>c&lt;column&gt;_&lt;row&gt;</c>.</summary>
    public static string CellId(int column, int row) => $"c{column}_{row}";

    /// <summary>A block's far version's id: <c>f&lt;column&gt;_&lt;row&gt;</c>, in blocks.</summary>
    public static string FarId(int column, int row) => $"f{column}_{row}";

    /// <summary>The cell a point (metres) falls in, unclamped.</summary>
    public (int Column, int Row) CellAt(float x, float y) =>
        ((int)MathF.Floor((x - Origin[0]) / CellSize), (int)MathF.Floor((y - Origin[1]) / CellSize));

    /// <summary>A cell's square (metres), X and Y only.</summary>
    public (float X0, float Y0, float X1, float Y1) Square(int column, int row) =>
        (Origin[0] + column * CellSize, Origin[1] + row * CellSize, Origin[0] + (column + 1) * CellSize, Origin[1] + (row + 1) * CellSize);

    /// <summary>The Detour tile a cell's navigation mesh is: (column, rows − 1 − row), since Recast's z is −y.</summary>
    public (int X, int Z) Tile(int column, int row) => (column, Rows - 1 - row);
}

/// <summary>
/// Manifest version 2: one block's far version — a merged, simplified stand-in for all of the block's
/// cells, drawn beyond the distance at which its cells are, so that a big machine
/// draws the whole city. <see cref="File"/> is a cell file holding one <c>MESH</c> chunk.
/// </summary>
public sealed class L2FarEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    /// <summary>The block's column and row, in blocks.</summary>
    [JsonPropertyName("at")] public int[] At { get; set; } = [0, 0];
    [JsonPropertyName("bounds")] public L2Box Bounds { get; set; } = new();
    [JsonPropertyName("file")] public string File { get; set; } = "";
    /// <summary>The ids of the cells it stands for.</summary>
    [JsonPropertyName("cells")] public List<string> Cells { get; set; } = [];
    [JsonPropertyName("bytes")] public long? Bytes { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}
