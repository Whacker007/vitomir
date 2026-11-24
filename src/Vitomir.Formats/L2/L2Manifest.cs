// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Vitomir.Formats.L2;

/// <summary>
/// An L2 level's manifest, <c>level.json</c> (ours): the level's readable index.
/// Lengths are in **metres** (the engine's 72 units to the metre, <see cref="EngineUnitsPerMetre"/>),
/// Z up, angles in degrees. Any property this build does not know — at the top level or in
/// <see cref="Extras"/> — is kept as it was read and written back unchanged.
/// </summary>
public sealed class L2Manifest
{
    public const string FormatName = "vitomir-l2";
    /// <summary>The manifest version of a converted 1.1 level and of a cooked level of one ungridded cell.</summary>
    public const int Version1 = 1;
    /// <summary>
    /// The manifest version of a gridded cell level (ours, Phase 3): <see cref="Grid"/>, the cells' places in it,
    /// the far versions (<see cref="Far"/>) and the cooked textures. This build reads both.
    /// </summary>
    public const int Version2 = 2;
    /// <summary>1 m = 72 engine units (Christof, 128 units tall, is 1.78 m), by design.</summary>
    public const float EngineUnitsPerMetre = 72f;

    [JsonPropertyName("format")] public string Format { get; set; } = FormatName;
    [JsonPropertyName("version")] public int Version { get; set; } = Version1;
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("units")] public L2Units Units { get; set; } = new();
    /// <summary>Where the level came from: for a converted 1.1 level, the `.nil` and its hash.</summary>
    [JsonPropertyName("source")] public L2Source? Source { get; set; }
    [JsonPropertyName("bounds")] public L2Box Bounds { get; set; } = new();
    [JsonPropertyName("cells")] public List<L2CellEntry> Cells { get; set; } = [];
    [JsonPropertyName("materials")] public List<string> Materials { get; set; } = [];
    [JsonPropertyName("globalLight")] public L2GlobalLight? GlobalLight { get; set; }
    [JsonPropertyName("anchors")] public List<L2Anchor> Anchors { get; set; } = [];
    [JsonPropertyName("exits")] public List<L2Exit> Exits { get; set; } = [];
    [JsonPropertyName("extras")] public JsonObject Extras { get; set; } = [];
    /// <summary>A gridded cell level's grid (version 2); null for a converted level or one ungridded cell.</summary>
    [JsonPropertyName("grid")] public L2Grid? Grid { get; set; }
    /// <summary>A gridded cell level's far versions, one per block of cells (version 2).</summary>
    [JsonPropertyName("far")] public List<L2FarEntry>? Far { get; set; }
    /// <summary>A cooked level's material definitions and their textures (version 2, <c>materials/</c> and <c>textures/</c>).</summary>
    [JsonPropertyName("materialFiles")] public List<string>? MaterialFiles { get; set; }
    /// <summary>A cell level's skyline: the city round it, drawn only (ours, <see cref="L2Skyline"/>).</summary>
    [JsonPropertyName("skyline")] public L2Skyline? Skyline { get; set; }
    /// <summary>A cell level's date: the day of the year and perhaps the hour its sun follows (ours, <see cref="L2Date"/>); null keeps mid-May.</summary>
    [JsonPropertyName("date")] public L2Date? Date { get; set; }
    /// <summary>A cell level's compass: where north is, as an engine yaw (ours, <see cref="L2North"/>); null keeps north along +X.</summary>
    [JsonPropertyName("north")] public L2North? North { get; set; }
    /// <summary>A cell level's named volumes, its interiors first (ours, <see cref="L2Area"/>); null for none.</summary>
    [JsonPropertyName("areas")] public List<L2Area>? Areas { get; set; }
    /// <summary>The 1.1 scene a cell level stands for, read from the install when it loads (ours, <see cref="L2Scene"/>); null for none.</summary>
    [JsonPropertyName("scene")] public L2Scene? Scene { get; set; }
    /// <summary>Where a cell level's ground is deliberately not the ground of the 1.1 level whose scene it carries, and by how much (ours, <see cref="L2Lift"/>); null for nowhere.</summary>
    [JsonPropertyName("lifts")] public List<L2Lift>? Lifts { get; set; }
    /// <summary>How a cell level's navigation mesh was baked where it is not Recast's own way (ours, <see cref="L2Navigation"/>); null for Recast's.</summary>
    [JsonPropertyName("navigation")] public L2Navigation? Navigation { get; set; }
    /// <summary>What a cell level says of traversal for the platform to read at run time: its ladders (ours, <see cref="L2Traversal"/>); null for none.</summary>
    [JsonPropertyName("traversal")] public L2Traversal? Traversal { get; set; }
    /// <summary>Top-level properties this build does not know, kept for the write.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static L2Manifest Read(string text, string name = "level.json")
    {
        var m = JsonSerializer.Deserialize<L2Manifest>(text, Json) ?? throw new NodFormatException($"{name}: empty manifest");
        if (m.Format != FormatName) throw new NodFormatException($"{name}: format '{m.Format}', not {FormatName}");
        if (m.Version != Version1 && m.Version != Version2)
            throw new NodFormatException($"{name}: manifest version {m.Version}, this build reads {Version1} and {Version2}");
        return m;
    }

    public string Write() => JsonSerializer.Serialize(this, Json);

    /// <summary>Engine units to metres.</summary>
    public static float[] Metres(System.Numerics.Vector3 v) =>
        [v.X / EngineUnitsPerMetre, v.Y / EngineUnitsPerMetre, v.Z / EngineUnitsPerMetre];
}

public sealed class L2Units
{
    [JsonPropertyName("length")] public string Length { get; set; } = "metre";
    [JsonPropertyName("engineUnitsPerMetre")] public float EngineUnitsPerMetre { get; set; } = L2Manifest.EngineUnitsPerMetre;
    [JsonPropertyName("up")] public string Up { get; set; } = "+z";
    [JsonPropertyName("angles")] public string Angles { get; set; } = "degrees";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}

public sealed class L2Source
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("formatVersion")] public uint FormatVersion { get; set; }
    [JsonPropertyName("bytes")] public long Bytes { get; set; }
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>An axis-aligned box in metres.</summary>
public sealed class L2Box
{
    [JsonPropertyName("min")] public float[] Min { get; set; } = [0, 0, 0];
    [JsonPropertyName("max")] public float[] Max { get; set; } = [0, 0, 0];
}

public sealed class L2CellEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("bounds")] public L2Box Bounds { get; set; } = new();
    /// <summary>The cell file, relative to the package folder (<c>cells/&lt;id&gt;.cell</c>).</summary>
    [JsonPropertyName("file")] public string File { get; set; } = "";
    /// <summary>The far version standing for the cell at a distance: its block's (<see cref="L2FarEntry"/>), version 2.</summary>
    [JsonPropertyName("farFile")] public string? FarFile { get; set; }
    /// <summary>A gridded cell's column and row (version 2); null for the resident cell and for an ungridded level.</summary>
    [JsonPropertyName("at")] public int[]? At { get; set; }
    /// <summary>
    /// Version 2: the cell is loaded with the level and never unloaded — the triangles wider than a cell
    /// (a field plane to the horizon), which belong to no one cell.
    /// </summary>
    [JsonPropertyName("resident")] public bool? Resident { get; set; }
    /// <summary>Version 2: how many polygons the cell's navigation-mesh tile has (its walk nodes' slots).</summary>
    [JsonPropertyName("navPolygons")] public int? NavPolygons { get; set; }
    /// <summary>Version 2: the cell file's size in bytes, for a streamer's budget.</summary>
    [JsonPropertyName("bytes")] public long? Bytes { get; set; }
    /// <summary>What kind of region the cell makes: <c>legacy</c> for a converted 1.1 level.</summary>
    [JsonPropertyName("region")] public string Region { get; set; } = "legacy";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>The level's sun or moon: the NIL's global light, in metres. The exact record is in L1SC.</summary>
public sealed class L2GlobalLight
{
    [JsonPropertyName("type")] public uint Type { get; set; }
    [JsonPropertyName("color")] public float[] Color { get; set; } = [0, 0, 0];
    [JsonPropertyName("direction")] public float[] Direction { get; set; } = [0, 0, 0];
    [JsonPropertyName("position")] public float[] Position { get; set; } = [0, 0, 0];
    [JsonPropertyName("intensity")] public float Intensity { get; set; }
    [JsonPropertyName("ambient")] public float[] Ambient { get; set; } = [0, 0, 0];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>
/// An anchor: one placed thing, start, spot or sound of a scene, with a stable
/// id, the 1.1 names it stands for and its placement in metres. Nothing reads anchors at run time
/// yet; they are recorded so a later phase can move things by editing anchors.
/// </summary>
public sealed class L2Anchor
{
    /// <summary>Stable id: <c>&lt;scene&gt;/&lt;list&gt;/&lt;index&gt;</c> for a converted level.</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    /// <summary>
    /// What it is: a thing's type from its template (<c>actor</c>, <c>item</c>, <c>prop</c>,
    /// <c>region</c>, <c>container</c> …) or one of the scene's other lists (<c>start</c>,
    /// <c>interest-spot</c>, <c>conversation-start</c>, <c>sector-sound</c>, <c>point-sound</c>).
    /// </summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    /// <summary>The scene file the anchor comes from (without <c>.nsd</c>).</summary>
    [JsonPropertyName("scene")] public string Scene { get; set; } = "";
    /// <summary>Its index in that scene's list — for things, the index a script's CODEXPARAMS use.</summary>
    [JsonPropertyName("index")] public int Index { get; set; }
    /// <summary>The 1.1 name: a thing's template, a sound's wav.</summary>
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("template")] public string? Template { get; set; }
    /// <summary>The GUID the thing gets when its place is loaded on its own (a fresh counter).</summary>
    [JsonPropertyName("guid")] public int? Guid { get; set; }
    [JsonPropertyName("script")] public string? Script { get; set; }
    /// <summary>The 1.1 start sector (engine data, kept for the legacy region).</summary>
    [JsonPropertyName("sector")] public long? Sector { get; set; }
    [JsonPropertyName("position")] public float[] Position { get; set; } = [0, 0, 0];
    /// <summary>Pitch, roll, yaw in degrees (rotations about X, Y, Z), as the scene's orientation tag.</summary>
    [JsonPropertyName("rotation")] public float[] Rotation { get; set; } = [0, 0, 0];
    /// <summary>For a region or a sector sound: the 1.1 sectors it covers (its volume in the legacy region).</summary>
    [JsonPropertyName("sectors")] public uint[]? Sectors { get; set; }
    /// <summary>Starts and spots: the group (entrance) number; exits: the exit number.</summary>
    [JsonPropertyName("group")] public uint? Group { get; set; }
    [JsonPropertyName("cell")] public string Cell { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>One of the chronicle's <c>exit</c> lines naming this level's location.</summary>
public sealed class L2Exit
{
    [JsonPropertyName("chronicle")] public string Chronicle { get; set; } = "";
    [JsonPropertyName("location")] public string Location { get; set; } = "";
    [JsonPropertyName("number")] public int Number { get; set; }
    [JsonPropertyName("to")] public string To { get; set; } = "";
    [JsonPropertyName("toLevel")] public string ToLevel { get; set; } = "";
    [JsonPropertyName("entrance")] public int Entrance { get; set; }
    [JsonPropertyName("closed")] public bool Closed { get; set; }
    /// <summary>A cell level's (ours; <see cref="L2LayoutExits"/>): the building or gate of the new level the exit is at, by the generator's name.</summary>
    [JsonPropertyName("door")] public string? Door { get; set; }
    /// <summary>A cell level's (ours): where the exit's sign stands, metres in the level's frame.</summary>
    [JsonPropertyName("at")] public float[]? At { get; set; }
    /// <summary>A cell level's (ours): a volume that takes the exit when the party's leader walks into it (a gate); null for an exit that is clicked.</summary>
    [JsonPropertyName("volume")] public L2AreaBox? Volume { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}
