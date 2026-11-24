// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Vitomir.Formats.L2;

/// <summary>
/// **Ours: a named volume of a cell level** (manifest key <c>areas</c>).
/// The first kind is an <see cref="Interior"/>: a room entered from the street through a real doorway, a part of
/// the same streamed map (the smithy of the Old Town is the first). An area is a box turned about the vertical
/// axis, in metres in the level's frame, with a name; later rounds hang scripts, sound and culling on it. What
/// reads it today is the modern renderer's exposure: inside an area the picture is exposed <see cref="Exposure"/>
/// stops brighter, as an eye opens in a dim room (<see cref="ExposureStops"/>), since a room lit by a fire and a
/// doorway has a hundredth of the street's light. A package without the key has no areas, and a build that does
/// not know the key keeps it and ignores it. 1.1 has nothing like it: its interiors are levels of their own.
/// </summary>
public sealed class L2Area
{
    /// <summary>The kind of an area that is a room indoors.</summary>
    public const string Interior = "interior";
    /// <summary>
    /// How far outside an area's box the exposure begins to rise, metres (ours: a house wall's thickness, so it
    /// rises through the doorway's depth and an eye in the street, or the game's camera pulled in against a wall
    /// from either side, never stands in the blend).
    /// </summary>
    public const float BlendOutside = 0.4f;

    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = Interior;
    [JsonPropertyName("box")] public L2AreaBox Box { get; set; } = new();
    /// <summary>How many stops brighter the picture is exposed inside the area (0 for none).</summary>
    [JsonPropertyName("exposure")] public float Exposure { get; set; }
    /// <summary>
    /// The chronicle place the area stands for (ours): 1.1's location
    /// name, <c>SmithyDay</c> for the Old Town's smithy by day. Its scene's music, ambient loop and room type
    /// are the area's sound. Optional: an area without one is only a volume.
    /// </summary>
    [JsonPropertyName("place")] public string? Place { get; set; }
    /// <summary>
    /// The exit of the level's own place that 1.1 takes to the area's place (ours): 9 for the Old
    /// Town's smithy. With the room in the map, taking that exit walks the party in through the door instead
    /// of changing the level. Optional.
    /// </summary>
    [JsonPropertyName("exit")] public int? Exit { get; set; }
    /// <summary>The area's doorways (ours; typed): the thresholds the party crosses.</summary>
    [JsonPropertyName("doors")] public List<L2AreaDoor>? Doors { get; set; }
    /// <summary>Properties this build does not know, kept for the write.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>
    /// How deep a point (metres, the level's frame) is inside the area's box: the distance to the nearest face
    /// when inside (positive), minus the distance to the box when outside.
    /// </summary>
    public float Depth(Vector3 point)
    {
        var b = Box;
        float c = MathF.Cos(b.RotZ), s = MathF.Sin(b.RotZ);
        float dx = point.X - b.Origin[0], dy = point.Y - b.Origin[1];
        // into the box's own frame: x and y along its sides from its lowest corner
        Vector3 p = new(c * dx + s * dy, -s * dx + c * dy, point.Z - b.Origin[2]);
        Vector3 size = new(b.Size[0], b.Size[1], b.Size[2]);
        Vector3 inside = Vector3.Min(p, size - p);                 // each axis: how far inside its nearer face
        float least = MathF.Min(inside.X, MathF.Min(inside.Y, inside.Z));
        if (least >= 0) return least;
        return -Vector3.Max(-inside, Vector3.Zero).Length();
    }

    /// <summary>
    /// The stops the picture is exposed brighter for an eye at <paramref name="point"/> (metres): the greatest of
    /// the areas' <see cref="Exposure"/>, each rising smoothly from nothing <see cref="BlendOutside"/> outside its
    /// box to the whole on the box's faces and inside it. 0 with no areas or in the street.
    /// </summary>
    public static float ExposureStops(IReadOnlyList<L2Area>? areas, Vector3 point)
    {
        float stops = 0;
        if (areas is null) return 0;
        foreach (var a in areas)
        {
            if (a.Exposure == 0 || !float.IsFinite(a.Exposure)) continue;
            float t = Math.Clamp(a.Depth(point) / BlendOutside + 1f, 0f, 1f);
            t = t * t * (3 - 2 * t);
            if (MathF.Abs(a.Exposure * t) > MathF.Abs(stops)) stops = a.Exposure * t;
        }
        return stops;
    }

    /// <summary>
    /// The areas in a generator's <c>layout.json</c> (its <c>areas</c>: each <c>{"name": "smithy", "kind":
    /// "interior", "box": {"origin": [x, y, z], "rotZ": radians, "size": [x, y, z]}, "exposure": 2.5}</c>, what
    /// else it says kept for the manifest), or null when it has none. An area without a name or with a box that
    /// is not three finite sizes above zero is left out.
    /// </summary>
    public static List<L2Area>? FromLayout(JsonNode? layout)
    {
        if (layout?["areas"] is not JsonArray list) return null;
        var areas = new List<L2Area>();
        foreach (var node in list)
        {
            L2Area? a;
            try { a = node.Deserialize<L2Area>(L2Manifest.Json); }
            catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException) { continue; }
            if (a is null || a.Name.Length == 0 || !a.Box.Sound) continue;
            if (!float.IsFinite(a.Exposure)) a.Exposure = 0;
            Threshold(a, layout!);
            areas.Add(a);
        }
        return areas.Count > 0 ? areas : null;
    }

    /// <summary>
    /// What the layout's <c>interiors</c> and <c>exits</c> say about an area that does not say it itself
    /// (ours): the interior of the same name gives its door's <c>exit</c> number and the points
    /// just outside and inside the doorway, and the exit of that number names the place the area stands for
    /// (the chronicle's location, the layout's <c>exits[].to</c>). An area that carries <c>place</c>,
    /// <c>exit</c> or a door's points keeps its own.
    /// </summary>
    static void Threshold(L2Area a, JsonNode layout)
    {
        var room = (layout["interiors"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(i => i["name"] is JsonValue n && n.TryGetValue<string>(out var s) && s == a.Name);
        if (room?["door"] is JsonObject door)
        {
            if (a.Exit is null && door["exit"] is JsonValue e && e.TryGetValue<int>(out var number)) a.Exit = number;
            if (a.Doors is { Count: > 0 } doors)
            {
                doors[0].Inside ??= Point(door["inside"]);
                doors[0].Outside ??= Point(door["outside"]);
            }
        }
        if (a.Place is null && a.Exit is { } exit)
            a.Place = (layout["exits"] as JsonArray)?.OfType<JsonObject>()
                .Where(x => x["number"] is JsonValue n && n.TryGetValue<int>(out var i) && i == exit)
                .Select(x => x["to"] is JsonValue t && t.TryGetValue<string>(out var s) ? s : null).FirstOrDefault();
    }

    static float[]? Point(JsonNode? node)
    {
        if (node is not JsonArray { Count: 3 } p) return null;
        var v = new float[3];
        for (int i = 0; i < 3; i++)
        {
            if (p[i] is not JsonValue n || !n.TryGetValue<double>(out var d) || !double.IsFinite(d)) return null;
            v[i] = (float)d;
        }
        return v;
    }

    public override string ToString() => $"{Name} ({Kind}, {Box.Size[0]:0.#} × {Box.Size[1]:0.#} × {Box.Size[2]:0.#} m" +
                                         (Exposure != 0 ? $", {Exposure:+0.#;-0.#} stops" : "") +
                                         (Place is { Length: > 0 } ? $", stands for {Place}" + (Exit is { } e ? $" by exit {e}" : "") : "") + ")";
}

/// <summary>
/// One doorway of an area (ours): where its threshold is, how wide and high the opening, and — for the party
/// walking through it — a point just outside and one just inside, metres in the level's frame.
/// </summary>
public sealed class L2AreaDoor
{
    /// <summary>The middle of the threshold on the floor.</summary>
    [JsonPropertyName("position")] public float[] Position { get; set; } = [0, 0, 0];
    [JsonPropertyName("width")] public float Width { get; set; }
    [JsonPropertyName("height")] public float Height { get; set; }
    /// <summary>A point on the floor just inside the door (ours), where the party is walked to when the area's exit is taken.</summary>
    [JsonPropertyName("inside")] public float[]? Inside { get; set; }
    /// <summary>A point on the ground just outside the door (ours).</summary>
    [JsonPropertyName("outside")] public float[]? Outside { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>An area's box (ours): its lowest corner, its turn about the vertical axis and its sizes along its own sides, metres.</summary>
public sealed class L2AreaBox
{
    /// <summary>The corner the sides run from, metres, the level's frame (Z up).</summary>
    [JsonPropertyName("origin")] public float[] Origin { get; set; } = [0, 0, 0];
    /// <summary>The turn of the box's own x side from the level's x, radians, anticlockwise seen from above.</summary>
    [JsonPropertyName("rotZ")] public float RotZ { get; set; }
    /// <summary>The sizes along the box's own x, y and the vertical, metres.</summary>
    [JsonPropertyName("size")] public float[] Size { get; set; } = [0, 0, 0];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>Three finite numbers each, the sizes above zero.</summary>
    [JsonIgnore] public bool Sound => Origin.Length == 3 && Size.Length == 3 && float.IsFinite(RotZ)
                                      && Origin.All(float.IsFinite) && Size.All(v => float.IsFinite(v) && v > 0);
}
