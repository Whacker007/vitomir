// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Formats.L2;

namespace Vitomir.Platform.Space;

/// <summary>
/// What a place is to the Masquerade (ours): <b>public</b>, where a Kindred
/// who passes for human is seen (a populated town), or <b>free</b>, a "free fire zone" (a dungeon, a mission area:
/// the monastery, the mines) where nothing is a breach. A place not marked is public.
/// </summary>
public enum PlaceKind
{
    Public = 0,
    Free = 1,
}

/// <summary>One of a level's areas that says what it is to the Masquerade: its name, its box and its kind.</summary>
public sealed record LevelPlace(string Name, TurnedBox Box, PlaceKind Kind)
{
    /// <summary>The box's volume (m³): the smaller of two places that hold a point decides.</summary>
    public float Volume => Box.Size.X * Box.Size.Y * Box.Size.Z;
}

/// <summary>
/// A level's places (ours), read from its package's manifest: each area
///  whose <c>masquerade</c> key says <c>"public"</c> or <c>"free"</c> (the manifest keeps
/// an area's keys it does not know, <see cref="L2Area.Unknown"/>), and the manifest's own top-level
/// <c>masquerade</c> for the whole level (the cook does not carry one from a layout today; an area over the whole
/// level does the same). <see cref="At"/> answers for a point: the <b>smallest</b> marked area holding it decides
/// (a public square inside a free district is public; between two of one size, public), else the level's own word,
/// else <b>public</b>. A value that is neither word is a problem said once (<see cref="Problems"/>) and the area
/// counts as unmarked. Read only while the world runs; the boxes' turns are worked out once with
/// <see cref="Core.DetMath"/> (<see cref="TurnedBox"/>), so the answer is the same on every machine.
/// </summary>
public sealed class LevelPlaces
{
    /// <summary>The key an area (or the manifest) says it with.</summary>
    public const string Key = "masquerade";

    readonly LevelPlace[] places;

    /// <summary>A level with no places marked: public everywhere.</summary>
    public static LevelPlaces None { get; } = new([], null, []);

    /// <summary>A level that is one kind of place everywhere (a ground built in code: <see cref="BuiltInGrounds"/>).</summary>
    public static LevelPlaces All(PlaceKind kind) => new([], kind, []);

    LevelPlaces(LevelPlace[] places, PlaceKind? whole, IReadOnlyList<string> problems)
    {
        this.places = places;
        Whole = whole;
        Problems = problems;
    }

    /// <summary>The marked areas, the smallest first (the order <see cref="At"/> tries them in).</summary>
    public IReadOnlyList<LevelPlace> Places => places;

    /// <summary>What the manifest says of the whole level, or null when it says nothing.</summary>
    public PlaceKind? Whole { get; }

    /// <summary>What was wrong with the marks (a value neither <c>public</c> nor <c>free</c>), one line each.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>Public or free at <paramref name="point"/> (metres, the level's frame).</summary>
    public PlaceKind At(Vector3 point)
    {
        for (int i = 0; i < places.Length; i++)
            if (places[i].Box.Contains(point)) return places[i].Kind;
        return Whole ?? PlaceKind.Public;
    }

    /// <summary>The name of the place deciding at <paramref name="point"/>: an area's, <c>the level</c>, or <c>unmarked</c>.</summary>
    public string NameAt(Vector3 point)
    {
        for (int i = 0; i < places.Length; i++)
            if (places[i].Box.Contains(point)) return places[i].Name;
        return Whole is null ? "unmarked" : "the level";
    }

    /// <summary>A place's word as the files and the status line write it.</summary>
    public static string Word(PlaceKind kind) => kind == PlaceKind.Free ? "free" : "public";

    /// <summary>The places of a manifest.</summary>
    public static LevelPlaces From(L2Manifest manifest)
    {
        var problems = new List<string>();
        PlaceKind? whole = Read(manifest.Unknown, "the level", problems);
        var found = new List<(LevelPlace Place, int Index)>();
        var areas = manifest.Areas ?? [];
        for (int i = 0; i < areas.Count; i++)
        {
            var a = areas[i];
            if (Read(a.Unknown, $"area {a.Name}", problems) is not { } kind) continue;
            if (TurnedBox.From(a.Box) is not { } box) { problems.Add($"area {a.Name}: its box is not sound; its masquerade mark is passed over"); continue; }
            found.Add((new LevelPlace(a.Name, box, kind), i));
        }
        // The smallest first; of one size, public first; then the manifest's order.
        found.Sort((x, y) =>
        {
            int c = x.Place.Volume.CompareTo(y.Place.Volume);
            if (c != 0) return c;
            c = x.Place.Kind.CompareTo(y.Place.Kind);
            return c != 0 ? c : x.Index.CompareTo(y.Index);
        });
        if (whole is null && found.Count == 0 && problems.Count == 0) return None;
        return new LevelPlaces([.. found.Select(f => f.Place)], whole, problems);
    }

    /// <summary>Places made in code (tests, tools): the areas as given, sorted as a manifest's are.</summary>
    public static LevelPlaces Of(IEnumerable<LevelPlace> places, PlaceKind? whole = null)
    {
        var list = places.Select((p, i) => (Place: p, Index: i)).ToList();
        list.Sort((x, y) =>
        {
            int c = x.Place.Volume.CompareTo(y.Place.Volume);
            if (c != 0) return c;
            c = x.Place.Kind.CompareTo(y.Place.Kind);
            return c != 0 ? c : x.Index.CompareTo(y.Index);
        });
        return new LevelPlaces([.. list.Select(l => l.Place)], whole, []);
    }

    static PlaceKind? Read(Dictionary<string, JsonElement>? keys, string what, List<string> problems)
    {
        if (keys is null || !keys.TryGetValue(Key, out var v)) return null;
        string? word = v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        switch (word)
        {
            case "public": return PlaceKind.Public;
            case "free": return PlaceKind.Free;
            default:
                problems.Add($"{what}: masquerade is {v.GetRawText()}, which is neither \"public\" nor \"free\"; it is taken as unmarked (public unless another place says otherwise)");
                return null;
        }
    }
}
