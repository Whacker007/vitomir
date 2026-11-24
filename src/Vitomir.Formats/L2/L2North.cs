// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Vitomir.Formats.L2;

/// <summary>
/// **Ours: a cell level's compass** (manifest key <c>north</c>).
/// 1.1's levels have none; a cell level says where north is with one number, <see cref="Yaw"/>:
/// north as an engine yaw in degrees, counter-clockwise seen from above from the level's +X (0: +X is north,
/// 90: +Y is). It is also the compass bearing of the level's +X axis, and a compass bearing a (0 north, 90
/// east) lies along the yaw <c>Yaw − a</c>. What follows it is everything that asks for a bearing: the
/// modern renderer's sun and so its sky, the clouds' drift and their shadows' shift, the wind, and the
/// cook's light probes; the generator's layout carries the same number (<c>frame.north.yaw</c>) to the
/// city round the level and to whatever it grows on a north side.
/// A package without the key has north along +X (<see cref="DefaultYaw"/>), as every cell level had before
/// it; a build that does not know the key keeps it and ignores it.
/// </summary>
public sealed class L2North
{
    /// <summary>North in a level that says nothing: along +X (ours; every cell level's).</summary>
    public const float DefaultYaw = 0f;

    /// <summary>North as an engine yaw, degrees counter-clockwise seen from above from +X.</summary>
    [JsonPropertyName("yaw")] public float Yaw { get; set; } = DefaultYaw;
    /// <summary>Properties this build does not know, kept for the write (the generator's note, its north and east vectors).</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>The yaw of north a level's manifest gives, or <see cref="DefaultYaw"/> when it gives none.</summary>
    public static float Of(L2Manifest manifest) => manifest.North?.Yaw ?? DefaultYaw;

    /// <summary>
    /// The compass in a generator's <c>layout.json</c> (its <c>frame.north</c>: <c>{"yaw": 68}</c>, what else it
    /// says kept for the manifest), or null when the layout has none or its yaw is not a finite number.
    /// </summary>
    public static L2North? FromLayout(JsonNode? layout)
    {
        if (layout is not JsonObject || layout["frame"] is not JsonObject frame || frame["north"] is not JsonObject n) return null;
        L2North? north;
        try { north = JsonSerializer.Deserialize<L2North>(n, L2Manifest.Json); }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException) { return null; }
        if (north is null || n["yaw"] is null || !float.IsFinite(north.Yaw)) return null;
        return north;
    }

    /// <summary>The compass bearing (0 north, 90 east, 0..360) of the direction (x, y) in a level whose north is <paramref name="yaw"/>.</summary>
    public static float Bearing(float x, float y, float yaw)
    {
        float b = (yaw - MathF.Atan2(y, x) * 180f / MathF.PI) % 360f;
        return b < 0 ? b + 360f : b;
    }

    public override string ToString() => $"north at yaw {Yaw:0.##}°";
}
