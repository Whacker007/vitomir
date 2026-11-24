// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Audio;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours: the sound of a cell level's areas**. An area that
/// stands for a 1.1 place sounds as that place does: its music, ambient loop and room type are read from the
/// place's scenes by 1.1's own rule for a scene starting — the music and the
/// ambient from the chapter's scene when the place has one, else the level's own; the room type from the
/// level's own scene when it has one, else the chapter's — and they are changed over a short crossfade as
/// the party crosses the threshold, and changed back as it comes out
/// (<see cref="GameAudio.Crossfade"/>). 1.1 gets the same result with a level change, which stops every
/// channel first.
/// </summary>
public sealed class CellAreaSound
{
    /// <summary>What a place's scenes ask of the sound.</summary>
    /// <param name="Place">The chronicle's place.</param>
    /// <param name="Scene">The scene the music and the ambient were read from.</param>
    /// <param name="Music">The music track, "" for none.</param>
    /// <param name="MusicPercent">Its level, 0–100.</param>
    /// <param name="Ambient">The ambient loop, "" for none.</param>
    /// <param name="AmbientPercent">Its level, 0–100.</param>
    /// <param name="RoomType">The room type (the scene's EAX environment number).</param>
    public sealed record Rules(string Place, string Scene, string Music, int MusicPercent, string Ambient, int AmbientPercent, uint RoomType);

    sealed record Outside(Sound? Music, float MusicLevel, Sound? Ambient, float AmbientLevel, uint RoomType);

    Outside? outside;

    /// <summary>Forgets what was playing outside: for an arrival, when the place's own sound has just started.</summary>
    public void Reset() => outside = null;

    /// <summary>
    /// The chronicle place an area stands for now: where the area's exit leads from the place the party is
    /// in (so a script that re-points the exit, or the night's chronicle, is followed), else the place the
    /// area names; null when no chronicle runs or it has neither.
    /// </summary>
    public static NscLocation? PlaceOf(L2Area area, Chronicle? chronicle)
    {
        if (chronicle is null) return null;
        if (chronicle.At(chronicle.Current) is { } here && area.Exit is { } n && n >= 0 && n < here.ExitCount && here.Exits[n].To >= 0)
            return chronicle.At(here.Exits[n].To);
        return area.Place is { Length: > 0 } name ? chronicle.At(name) : null;
    }

    /// <summary>A place's sound by the level start's rule, or null when the install has neither of its scenes.</summary>
    public static Rules? Of(NscLocation place, GameFiles files)
    {
        var level = Read(files, place.LevelName);
        var chapter = place.SceneName.Length > 0 && !place.SceneName.Equals(place.LevelName, StringComparison.OrdinalIgnoreCase)
            ? Read(files, place.SceneName) : null;
        var rules = chapter ?? level;                       // The chapter's scene when there is one
        if (rules is null) return null;
        uint room = (level ?? chapter)!.Eax;                // The level's own scene's, else the chapter's
        return new Rules(place.Name, chapter is not null ? place.SceneName : place.LevelName,
                         rules.Music, (int)rules.Volume, rules.Ambient, (int)rules.AmbientVolume, room);
    }

    static NsdScene? Read(GameFiles files, string name) =>
        name.Length > 0 && files.TryRead($"scenes/{name}.nsd", out var data) ? NsdScene.Read(data, name + ".nsd") : null;

    /// <summary>
    /// The party has gone into <paramref name="area"/>: what is playing is remembered as the outside's and
    /// the area's place's music, ambient and room type take over. Answers a line for the log ("" when the
    /// area stands for no place or the game has no sound).
    /// </summary>
    public string Enter(L2Area area, Location place, Chronicle? chronicle)
    {
        if (PlaceOf(area, chronicle) is not { } to || place.Files is not { } files || Of(to, files) is not { } rules) return "";
        var audio = place.Audio;
        var music = rules.Music.Length > 0 ? audio?.Bank.Load(rules.Music, stream: true) : null;
        var ambient = rules.Ambient.Length > 0 ? audio?.Bank.Load(rules.Ambient, stream: true) : null;
        string Found(string name, Sound? sound) => name.Length == 0 ? "none" : sound is null && audio is not null ? name + " (not in this install)" : name;
        string said = $"{rules.Place}'s sound ({rules.Scene}): music {Found(rules.Music, music)} at {rules.MusicPercent}, " +
                      $"ambient {Found(rules.Ambient, ambient)} at {rules.AmbientPercent}, room type {rules.RoomType}";
        if (audio is null) return said + " (no sound: not played)";
        outside ??= new Outside(audio.Music, audio.MusicLevel, audio.Ambient, audio.AmbientLevel, audio.RoomType);
        audio.Crossfade(music, GameAudio.LevelOf(rules.MusicPercent), ambient, GameAudio.LevelOf(rules.AmbientPercent));
        if (rules.RoomType < GameAudio.RoomTypes) audio.RoomType = rules.RoomType;     // the mixer's test
        return said + $", over {GameAudio.AreaCrossfade:0.#} s";
    }

    /// <summary>The party has come out of <paramref name="area"/>: the outside's sound comes back. Answers a line for the log.</summary>
    public string Leave(L2Area area, Location place)
    {
        if (outside is not { } was || place.Audio is not { } audio) return "";
        outside = null;
        audio.Crossfade(was.Music, was.MusicLevel, was.Ambient, was.AmbientLevel, stopMissing: true);
        audio.RoomType = was.RoomType;
        return $"the outside's sound again: music {Named(was.Music?.Name ?? "")} at {was.MusicLevel * 100f:0}, " +
               $"ambient {Named(was.Ambient?.Name ?? "")} at {was.AmbientLevel * 100f:0}, room type {was.RoomType}, over {GameAudio.AreaCrossfade:0.#} s";
    }

    static string Named(string name) => name.Length > 0 ? name : "none";
}
