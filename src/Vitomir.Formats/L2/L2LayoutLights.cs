// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json.Nodes;

namespace Vitomir.Formats.L2;

/// <summary>
/// **Ours: the lights a generator's layout places** (<c>layout.json</c>'s <c>lights</c>;
/// areas). Until now a cooked level's only lights were the cook's own torches, one of a
/// fixed strength at every cluster of flame-coloured triangles; a room needs lights that are placed and
/// measured -- the forge's fire lighting the workshop from before the hearth, a lantern's light under its
/// lantern -- so the layout may list them:
/// <c>{"name": "smithy_forge", "position": [x, y, z], "colour": [r, g, b], "intensity": 3, "range": 11}</c>,
/// metres in the level's frame, the intensity in the renderer's units (the irradiance a metre away, the looks'
/// suns being about 6), the range where it reaches nothing. They go into the cells' <c>LITE</c> chunks beside
/// the torches, and a torch nearer than <see cref="Join"/> to a placed light is left out: the placed light is
/// that flame's.
/// </summary>
public static class L2LayoutLights
{
    /// <summary>How near a placed light a flame's own torch is taken to be the same light, metres (ours: the cook's own distance for joining flames).</summary>
    public const float Join = 0.6f;
    /// <summary>The reach of a placed light that gives none, metres (ours: the torches').</summary>
    public const float DefaultRange = 8f;

    /// <summary>The layout's lights; those without a name, a position of three finite numbers or an intensity above zero are left out.</summary>
    public static List<L2Light> FromLayout(JsonNode? layout)
    {
        var lights = new List<L2Light>();
        if (layout?["lights"] is not JsonArray list) return lights;
        foreach (var node in list)
        {
            if (node is not JsonObject o || Text(o["name"]) is not { Length: > 0 } name || Three(o["position"]) is not { } position) continue;
            if (Number(o["intensity"]) is not { } intensity || intensity <= 0) continue;
            var colour = Three(o["colour"]) ?? Vector3.One;
            float range = Number(o["range"]) is { } r && r > 0 ? r : DefaultRange;
            lights.Add(new L2Light(name, L2Light.Point, position, colour, intensity, range));
        }
        return lights;
    }

    /// <summary>The cook's torches and the layout's lights together: each torch nearer than <see cref="Join"/> to a placed light left out, the placed lights after the rest.</summary>
    public static List<L2Light> With(IReadOnlyList<L2Light> torches, IReadOnlyList<L2Light> placed) =>
        [.. torches.Where(t => !placed.Any(p => Vector3.Distance(p.Position, t.Position) < Join)), .. placed];

    /// <summary>
    /// **Ours: the street's lanterns by day.** 1.1 has two Old Towns, the day one with unlit lanterns
    /// (<c>lantern1_off</c>) and no light at them, the night one with lit ones; a generated level has one layout
    /// with a date, and the generator says in <c>layout.json</c>'s <c>lanterns</c> whether its street lanterns
    /// burn at that date: <c>{"state": "out", "reason": "…", "flames": [[x, y, z], …]}</c>, the flames' places
    /// in metres. This is the reason when they are out, and null when they burn or the layout says nothing.
    /// </summary>
    public static string? LanternsOut(JsonNode? layout) =>
        layout?["lanterns"] is JsonObject o && Text(o["state"]) == "out" ? Text(o["reason"]) ?? "the layout says so" : null;

    /// <summary>Where the layout's street lanterns' flames are (or would be, unlit): <c>lanterns.flames</c>; those that are not three finite numbers are left out.</summary>
    public static List<Vector3> LanternFlames(JsonNode? layout) =>
        layout?["lanterns"]?["flames"] is JsonArray list ? [.. list.Select(Three).OfType<Vector3>()] : [];

    /// <summary>
    /// The cook's torches without those at an unlit lantern: a torch nearer than <see cref="Join"/> to one of the
    /// <paramref name="flames"/> is left out. The generator builds an unlit lantern without a flame, so there is
    /// normally none to leave out; this holds for a scene whose lanterns still carry one.
    /// </summary>
    public static List<L2Light> WithoutLanterns(IReadOnlyList<L2Light> torches, IReadOnlyList<Vector3> flames) =>
        [.. torches.Where(t => !flames.Any(f => Vector3.Distance(f, t.Position) < Join))];

    static string? Text(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : null;
    static float? Number(JsonNode? n) => n is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d) ? (float)d : null;
    static Vector3? Three(JsonNode? n) =>
        n is JsonArray { Count: 3 } a && Number(a[0]) is { } x && Number(a[1]) is { } y && Number(a[2]) is { } z ? new Vector3(x, y, z) : null;
}
