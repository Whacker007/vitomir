// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a thing is called in a save.
//
// **Not its GUID.** A GUID is handed out as the game runs, so it depends on the order things were
// made in; a mod that adds one barrel to a scene would renumber everything after it. A save keys a
// thing by something that stays put:
//
// * a thing the level builder placed — `<scene>#<index>`: the scene file it came from (the level's
//   own, `P1_OLDT`, or the chapter's laid over it, `OLDT_7_1`) and its place in that scene's own
//   list, which is exactly how a script's CODEXPARAMS name it;
// * a thing made while the game ran — a drop, a purchase, a summons, a corpse's gibs — is
//   `<location>~<n>`, where n counts the things made in that place. Two items dropped on exactly
//   the same spot are two numbers; where a thing lies never enters into what it is called;
// * a member of the party — `party:<cast id>`, because the party belongs to no place.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Save;

public static class ThingIds
{
    /// <summary>The prefix of a party member's id.</summary>
    public const string Party = "party:";

    /// <summary>
    /// The id of a thing the scene placed, from its index in the location's joined list — the
    /// world scene's things first, then the chapter scene's. Null for anything the scene did not
    /// place (the player's own thing, which the loader adds after the scene's, or a made thing).
    /// </summary>
    public static string? Placed(Location place, WorldThing thing)
    {
        if (thing.Index < 0) return null;
        int worldCount = place.WorldScene?.Things.Length ?? 0;
        int chapterCount = place.ChapterScene?.Things.Length ?? 0;
        if (place.WorldScene is not null && thing.Index < worldCount) return $"{place.Name}#{thing.Index}";
        int inChapter = thing.Index - worldCount;
        if (place.ChapterScene is not null && inChapter < chapterCount) return $"{place.SceneFile}#{inChapter}";
        return null;
    }

    /// <summary>The id of a thing made here, by the count <see cref="Location.Made"/> gave it.</summary>
    public static string Made(Location place, int n) => $"{place.Name}~{n}";

    /// <summary>A party member's id, by cast id — or template, for a character without one.</summary>
    public static string Member(WorldThing thing) =>
        Party + (thing.Actor is { CastId.Length: > 0 } a ? a.CastId : thing.Template);

    /// <summary>
    /// A thing's id in its place: its placement, its made number, or — for anything else, which the
    /// save does not keep — null.
    /// </summary>
    public static string? Of(Location place, WorldThing thing) =>
        thing.SaveId ?? Placed(place, thing);

    /// <summary>Splits a placed id into its scene and index, or null when it is not one.</summary>
    public static (string Scene, int Index)? ParsePlaced(string id)
    {
        int hash = id.LastIndexOf('#');
        if (hash <= 0 || !int.TryParse(id.AsSpan(hash + 1), out int index)) return null;
        return (id[..hash], index);
    }

    /// <summary>Splits a made id into its place and number, or null when it is not one.</summary>
    public static (string Place, int N)? ParseMade(string id)
    {
        int tilde = id.LastIndexOf('~');
        if (tilde <= 0 || !int.TryParse(id.AsSpan(tilde + 1), out int n)) return null;
        return (id[..tilde], n);
    }
}

/// <summary>
/// How a script is named in a save, where a timer or a capture has to say whose it is. A script
/// belongs to a thing, to a character's effect or discipline, or to the chronicle.
/// </summary>
public static class ScriptKeys
{
    public static string OfThing(string thingId) => thingId;
    public static string OfEffect(string ownerId, int effectGuid) => $"{ownerId}/effect:{effectGuid}";
    public static string OfDiscipline(string ownerId, string name) => $"{ownerId}/discipline:{name}";
    public static string OfChronicle(string className) => $"chronicle:{className}";
}
