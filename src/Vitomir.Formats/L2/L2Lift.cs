// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Vitomir.Formats.L2;

/// <summary>
/// **Ours: a ground lift** (manifest key <c>lifts</c>), where the new ground departs from 1.1's. 1.1 has nothing like it. A cell level
/// made on a 1.1 level's plan carries that level's scene by 1.1's x and y, and stands each thing on the new
/// ground found near its 1.1 height. Where the new ground is deliberately not 1.1's — the sunken court before
/// the Old Town's University, made level with the street, is up to 1.78 m above 1.1's — a lift says by how
/// much: a patch of square cells over the changed ground, each holding <em>the new walkable ground's height
/// minus 1.1's at the cell's centre</em>, in metres. The scene's loader adds the lift at a thing's x and y to
/// its 1.1 height before it looks for the ground, so a thing that stood in the bowl stands on the court and
/// a lantern on the bowl's wall keeps its height above the ground.
///
/// The patch is laid out as the generator's plan grid is: <see cref="X0"/>, <see cref="Y0"/> are its
/// corner of least x and y; cell (column <c>i</c> along +x, row <c>j</c> along +y) covers
/// <c>[X0 + i·Cell, X0 + (i+1)·Cell) × [Y0 + j·Cell, Y0 + (j+1)·Cell)</c> and its value is
/// <c>Values[j·Nx + i]</c>. A point takes the value of the cell that holds it — no blending between cells —
/// and a point outside the patch is not lifted, so a patch reaches past the changed ground with zeros at its
/// rim. Patches that overlap add up.
/// </summary>
public sealed class L2Lift
{
    /// <summary>What the patch is, for a reader (<c>university_court</c>).</summary>
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>The patch's corner of least x, metres in the level's frame.</summary>
    [JsonPropertyName("x0")] public float X0 { get; set; }
    /// <summary>The patch's corner of least y, metres.</summary>
    [JsonPropertyName("y0")] public float Y0 { get; set; }
    /// <summary>A cell's side, metres.</summary>
    [JsonPropertyName("cell")] public float Cell { get; set; }
    /// <summary>Cells along +x.</summary>
    [JsonPropertyName("nx")] public int Nx { get; set; }
    /// <summary>Cells along +y.</summary>
    [JsonPropertyName("ny")] public int Ny { get; set; }
    /// <summary>The lifts, metres: row-major, rows going +y, <see cref="Nx"/> × <see cref="Ny"/> of them.</summary>
    [JsonPropertyName("values")] public float[] Values { get; set; } = [];
    /// <summary>Properties this build does not know, kept for the write (the generator's note).</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>
    /// What is wrong with the patch, or null when it is sound: a cell side above zero, at least one cell
    /// each way, as many values as cells, every number finite.
    /// </summary>
    public string? Fault()
    {
        string who = Name.Length > 0 ? Name : "(unnamed)";
        if (!(Cell > 0) || !float.IsFinite(Cell)) return $"lift {who}: its cell side is {Cell}, not a length above zero";
        if (Nx <= 0 || Ny <= 0) return $"lift {who}: {Nx} × {Ny} cells";
        if (!float.IsFinite(X0) || !float.IsFinite(Y0)) return $"lift {who}: its corner is not two finite numbers";
        if (Values.LongLength != (long)Nx * Ny) return $"lift {who}: {Values.Length} values for {Nx} × {Ny} = {(long)Nx * Ny} cells";
        for (int i = 0; i < Values.Length; i++)
            if (!float.IsFinite(Values[i])) return $"lift {who}: value {i} is not a finite number";
        return null;
    }

    /// <summary>The lift at (<paramref name="x"/>, <paramref name="y"/>), metres: the value of the cell that holds the point, 0 outside the patch.</summary>
    public float At(float x, float y)
    {
        int i = (int)MathF.Floor((x - X0) / Cell), j = (int)MathF.Floor((y - Y0) / Cell);
        return i < 0 || j < 0 || i >= Nx || j >= Ny ? 0f : Values[j * Nx + i];
    }

    /// <summary>The lift at a point from every patch of <paramref name="lifts"/> together (they add up), metres; 0 for no patches.</summary>
    public static float At(IReadOnlyList<L2Lift>? lifts, float x, float y)
    {
        if (lifts is null) return 0f;
        float sum = 0f;
        for (int k = 0; k < lifts.Count; k++) sum += lifts[k].At(x, y);
        return sum;
    }

    /// <summary>The greatest and the least value of the patch, metres.</summary>
    public (float Least, float Greatest) Range() => Values.Length == 0 ? (0f, 0f) : (Values.Min(), Values.Max());

    /// <summary>
    /// The lifts in a generator's <c>layout.json</c> (its top-level <c>lifts</c>: each <c>{"name":
    /// "university_court", "x0": 40, "y0": 2, "cell": 0.5, "nx": 40, "ny": 44, "values": […], "note": "…"}</c>,
    /// what else it says kept for the manifest), or null when it has none. A patch that is not sound
    /// (<see cref="Fault"/>) is left out and said in <paramref name="faults"/>: the cook stops on one, since a
    /// lift dropped in silence leaves 1.1's things under the new ground.
    /// </summary>
    public static List<L2Lift>? FromLayout(JsonNode? layout, List<string>? faults = null)
    {
        if (layout is not JsonObject || layout["lifts"] is not JsonArray list) return null;
        var lifts = new List<L2Lift>();
        for (int n = 0; n < list.Count; n++)
        {
            L2Lift? lift;
            try { lift = list[n].Deserialize<L2Lift>(L2Manifest.Json); }
            catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException)
            {
                faults?.Add($"lift {n}: {e.Message}");
                continue;
            }
            if (lift is null) { faults?.Add($"lift {n}: not an object"); continue; }
            if (lift.Fault() is { } fault) { faults?.Add(fault); continue; }
            lifts.Add(lift);
        }
        return lifts.Count > 0 ? lifts : null;
    }

    public override string ToString()
    {
        var (least, greatest) = Range();
        return $"{(Name.Length > 0 ? Name : "(unnamed)")}: {Nx} × {Ny} cells of {Cell:0.###} m from ({X0:0.##}, {Y0:0.##}), {least:0.##} to {greatest:0.##} m";
    }
}
