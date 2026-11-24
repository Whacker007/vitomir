// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vitomir.Formats.L2;

/// <summary>
/// **Ours: a cell level's exits and entrances, from a generator's <c>layout.json</c>**.
/// A cell level that stands for
/// one of a chronicle's places keeps that place's exit numbers: the layout's <c>exits</c> name each one (its
/// number, where 1.1's chronicle sends it, the door or gate of the new level it is at), and its <c>start</c>
/// anchors are 1.1's player starts, four to an entrance, in the level's own frame. The cook carries both into
/// the manifest: the exits as <see cref="L2Exit"/> lines, the starts as <c>start</c> anchors whose
/// <see cref="L2Anchor.Group"/> is the entrance — so arriving through a chronicle's exit stands the party at
/// the matching door exactly as it does in a 1.1 level, whose starts are grouped the same way
/// (each member stands at the entrance's start one past its slot).
///
/// Where an exit leads is **not** read from here when the game runs: the chronicle is the map,
/// and a script may shut or re-point an exit. What the manifest adds
/// is geometry: where the exit's sign stands, and for an exit at a gate a volume to walk into.
/// </summary>
public static class L2LayoutExits
{
    /// <summary>Half the side of a gate's walk-in volume, metres (ours: the ground a body can stand on stops about a metre short of a shut gate's sign, and 1.1's starts at the Old Town's gates are 3.9 m or more from it).</summary>
    public const float VolumeHalfSide = 2.0f;
    /// <summary>How far below and above its sign a gate's walk-in volume reaches, metres (ours).</summary>
    public const float VolumeBelow = 2.5f, VolumeAbove = 2.0f;
    /// <summary>What a layout calls the door of an exit that is a gate in the town's wall (the Old Town generator's <c>gate_N</c>).</summary>
    public const string GatePrefix = "gate_";

    /// <summary>
    /// The layout's exits that the chronicle knows (those with a <c>toLevel</c>), each with its sign's place
    /// (<see cref="L2Exit.At"/>: the middle of its <c>trackers</c>) and, for a gate — a <c>door</c> named
    /// <c>gate_…</c>, or <c>"walkIn": true</c> — a volume round the sign (<see cref="L2Exit.Volume"/>); a
    /// layout's own <c>volume</c> is kept as it is. Null when the layout names none.
    /// </summary>
    public static List<L2Exit>? Exits(JsonNode? layout)
    {
        if (layout?["exits"] is not JsonArray list) return null;
        var exits = new List<L2Exit>();
        foreach (var node in list)
        {
            if (node is not JsonObject e || Int(e["number"]) is not { } number || Text(e["toLevel"]) is not { Length: > 0 } toLevel) continue;
            var exit = new L2Exit
            {
                Number = number, To = Text(e["to"]) ?? "", ToLevel = toLevel, Entrance = Int(e["entrance"]) ?? 0,
                Closed = e["closed"] is JsonValue c && c.TryGetValue<bool>(out var shut) && shut,
                Door = Text(e["door"]),
            };
            if (e["trackers"] is JsonArray trackers && Middle(trackers) is { } at) exit.At = at;
            if (e["volume"] is JsonObject given)
            {
                try { if (given.Deserialize<L2AreaBox>(L2Manifest.Json) is { Sound: true } box) exit.Volume = box; }
                catch (Exception x) when (x is JsonException or FormatException or InvalidOperationException) { }
            }
            bool walkIn = e["walkIn"] is JsonValue w && w.TryGetValue<bool>(out var wi) ? wi
                        : exit.Door?.StartsWith(GatePrefix, StringComparison.OrdinalIgnoreCase) == true;
            if (exit.Volume is null && walkIn && exit.At is { } p)
                exit.Volume = new L2AreaBox
                {
                    Origin = [p[0] - VolumeHalfSide, p[1] - VolumeHalfSide, p[2] - VolumeBelow],
                    Size = [2 * VolumeHalfSide, 2 * VolumeHalfSide, VolumeBelow + VolumeAbove],
                };
            exits.Add(exit);
        }
        return exits.Count > 0 ? exits : null;
    }

    /// <summary>
    /// The layout's <c>start</c> anchors as the manifest's: each <c>playerstartNN</c> a start of entrance
    /// <c>NN</c> (1.1's own rule: a start's class number is its group), at its position
    /// with its rotation, in the layout's order — which is the order of the party's slots. Null when the
    /// layout has none, and the cook then makes its one start as before.
    /// </summary>
    public static List<L2Anchor>? Starts(JsonNode? layout, string level)
    {
        if (layout?["anchors"] is not JsonArray list) return null;
        var starts = new List<L2Anchor>();
        foreach (var node in list)
        {
            if (node is not JsonObject a || Text(a["kind"]) != "start" || Triple(a["position"]) is not { } position) continue;
            string name = Text(a["name"]) ?? "playerstart00";
            starts.Add(new L2Anchor
            {
                Id = $"{level}/starts/{starts.Count}", Kind = "start", Scene = level, Index = starts.Count, Name = name,
                Position = position, Rotation = Triple(a["rotation"]) ?? [0, 0, 0], Group = GroupOf(name),
            });
        }
        return starts.Count > 0 ? starts : null;
    }

    /// <summary>
    /// Puts the layout's starts, exits, areas and ground lifts (<see cref="L2Lift"/>) into a cooked
    /// level's <paramref name="manifest"/> in place of its own, each only when the layout has some, every start filed under the grid cell it stands in
    /// (<c>vitomir --relayout-l2</c>); answers what it did, for the log.
    /// </summary>
    public static string Relay(L2Manifest manifest, JsonNode? layout)
    {
        var said = new List<string>();
        if (Starts(layout, manifest.Name) is { } starts)
        {
            manifest.Anchors.RemoveAll(a => a.Kind == "start");
            foreach (var s in starts)
                if (manifest.Grid is { } grid)
                {
                    var (column, row) = grid.CellAt(s.Position[0], s.Position[1]);
                    s.Cell = L2Grid.CellId(Math.Clamp(column, 0, grid.Columns - 1), Math.Clamp(row, 0, grid.Rows - 1));
                }
                else s.Cell = manifest.Cells.FirstOrDefault()?.Id ?? "";
            manifest.Anchors.InsertRange(0, starts);
            said.Add($"{starts.Count} starts at {starts.Select(s => s.Group ?? 0).Distinct().Count()} entrances");
        }
        if (Exits(layout) is { } exits)
        {
            manifest.Exits = exits;
            said.Add($"{exits.Count} exits ({string.Join(", ", exits.Select(e => $"{e.Number} {e.To}{(e.Volume is null ? "" : " walk-in")}"))})");
        }
        if (L2Area.FromLayout(layout) is { } areas)
        {
            manifest.Areas = areas;
            said.Add($"{areas.Count} area{(areas.Count == 1 ? "" : "s")}: {string.Join("; ", areas)}");
        }
        // Where the new ground departs from 1.1's (ours; L2Lift): a patch that is not sound is said and left out.
        var faults = new List<string>();
        if (L2Lift.FromLayout(layout, faults) is { } lifts)
        {
            manifest.Lifts = lifts;
            said.Add($"{lifts.Count} ground lift{(lifts.Count == 1 ? "" : "s")}: {string.Join("; ", lifts)}");
        }
        foreach (var fault in faults) said.Add($"NOT TAKEN, the layout's {fault}");
        return said.Count > 0 ? string.Join("; ", said) : "the layout names no starts, exits, areas or lifts; nothing changed";
    }

    /// <summary>The entrance a start's name gives it: the number that ends <c>playerstartNN</c>, 0 without one.</summary>
    public static uint GroupOf(string name)
    {
        int digits = 0;
        while (digits < name.Length && char.IsAsciiDigit(name[^(digits + 1)])) digits++;
        return digits > 0 && uint.TryParse(name[^digits..], NumberStyles.None, CultureInfo.InvariantCulture, out var g) ? g : 0;
    }

    static float[]? Middle(JsonArray points)
    {
        float x = 0, y = 0, z = 0; int n = 0;
        foreach (var p in points)
            if (Triple(p) is { } t) { x += t[0]; y += t[1]; z += t[2]; n++; }
        return n == 0 ? null : [x / n, y / n, z / n];
    }

    static float[]? Triple(JsonNode? node)
    {
        if (node is not JsonArray { Count: 3 } a) return null;
        var t = new float[3];
        for (int i = 0; i < 3; i++)
        {
            if (a[i] is not JsonValue v || !v.TryGetValue<double>(out var d) || !double.IsFinite(d)) return null;
            t[i] = (float)d;
        }
        return t;
    }

    static int? Int(JsonNode? node) => node is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
    static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
