// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The places a chronicle loads before its first: their numbers and their loading.
//
// 1.1's chronicle start loads every location flagged 0x80 before it enters the first one
// (the location load for each) — in Prague that is the haven, `P1_HAVN` with `HAVN_7_7`
// (flags 0xb4) — so the haven's things are numbered after the player and before the convent's: in
// a new-game save (`SaveGames\SP5`) Christof is 5, the convent's first thing 35, and the 29
// numbers between are the haven's. Vitomir builds the first place before the early ones, so the
// numbers are set aside here as the first place is numbered (`ThingGuids.Pending`, `Reserved`), and
// the early places are then loaded on them — built whole, their scripts made and told
// `created`, counted among the loaded, no scene begun — so that the first visit enters the place
// already built (`CampaignPreloads.cs`, `CodexScenes.Preloads.cs`).
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

public static class Preloads
{
    /// <summary>The location flag that loads a place at the chronicle's start (<c>0x80</c>).</summary>
    public const uint Flag = 0x80;

    /// <summary>
    /// The counter a new game starts with: 1, and the numbers every preloaded place's scene
    /// will take waiting to be set aside once the player is made (<see cref="ThingGuids.Pending"/>).
    /// </summary>
    public static ThingGuids NewGame(Chronicle chronicle, GameFiles files)
    {
        var guids = new ThingGuids();
        guids.Pending.AddRange(Of(chronicle, files));
        return guids;
    }

    /// <summary>
    /// Every location of <paramref name="chronicle"/> its start loads early, in its order, with the
    /// numbers its scene takes — for a new game, and
    /// for a chronicle changed to (state 7, <see cref="Campaign.ChangeChronicle"/>).
    /// </summary>
    public static IEnumerable<(string Key, int Count)> Of(Chronicle chronicle, GameFiles files)
    {
        for (int i = 0; i < chronicle.Count; i++)
        {
            if (chronicle.At(i) is not { } at || (at.Flags & Flag) == 0 || at.LevelName.Length == 0) continue;
            string? scene = at.SceneName.Length > 0 ? at.SceneName : null;
            int count = Location.SceneGuidCount(files, at.LevelName, scene);
            if (count > 0) yield return (Location.PlaceKey(at.LevelName, scene), count);
        }
    }
}

public sealed partial class Location
{
    /// <summary>What a place's scene is called in <see cref="ThingGuids.Reserved"/>: its level and chapter scene.</summary>
    public static string PlaceKey(string level, string? scene) =>
        string.IsNullOrEmpty(scene) || scene.Equals(level, StringComparison.OrdinalIgnoreCase) ? level : $"{level}/{scene}";

    /// <summary>
    /// How many numbers a place's scene takes when it is read: one a thing, one more for
    /// each character's fallback weapon (<see cref="SpendFallback"/>), and a player-type thing's kit
    /// (<see cref="CharacterSheets.KitCount"/>).
    /// </summary>
    public static int SceneGuidCount(GameFiles files, string level, string? scene)
    {
        var world = ReadScene(files, level);
        var chapter = string.IsNullOrEmpty(scene) || scene.Equals(level, StringComparison.OrdinalIgnoreCase) ? null : ReadScene(files, scene);
        if (world is null && chapter is null) return 0;
        var templates = new TemplateSet(files);
        foreach (var extra in (world?.TemplateFiles ?? []).Concat(chapter?.TemplateFiles ?? []))
            if (files.TryRead("misc/templates/" + (extra.Contains('.') ? extra : extra + ".not"), out var notData))
                templates.AddFile(NotFile.Read(notData, extra));
        int count = 0;
        foreach (var t in (world?.Things ?? []).Concat(chapter?.Things ?? []))
        {
            count++;
            var tags = templates.Resolve(t);
            var type = tags.TryGetValue(Tags.Type, out var ty) ? (ThingType)ty.AsUInt() : ThingType.None;
            if (type is ThingType.Actor or ThingType.Player && tags.TryGetValue(Tags.ActorWeapon, out var weapon)
                && weapon.AsString() is { Length: > 0 } name && templates.Has(name))
                count++;
            // A companion's kit, made before it (CharacterSheets.Hold).
            if (type is ThingType.Player && tags.TryGetValue(Tags.ActorRpgStats, out var sheet) && sheet.AsString() is { Length: > 0 } rpg)
                count += CharacterSheets.KitCount(rpg, files, templates);
        }
        return count;
    }
}
