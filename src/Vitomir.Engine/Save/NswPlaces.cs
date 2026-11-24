// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The `.nsw` files of a 1.1 save folder, read and turned into Vitomir places.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Save;

/// <summary>
/// A 1.1 save folder's `location_&lt;n&gt;.nsw` files — one for every place the game still remembered,
/// numbered by the chronicle's own location index — and each made into a
/// Vitomir place record (<see cref="LocationSave"/>). Only ever reads the folder.
/// </summary>
public static class NswPlaces
{
    /// <summary>Every `.nsw` in <paramref name="folder"/>, by location; a file that cannot be read is noted and left out.</summary>
    public static SortedDictionary<int, NswWorld> Read(string? folder, List<string> notes)
    {
        var places = new SortedDictionary<int, NswWorld>();
        if (folder is null || !Directory.Exists(folder)) return places;
        foreach (var path in Directory.EnumerateFiles(folder, "location_*.nsw"))
        {
            int index = NswWorld.LocationOf(path);
            if (index < 0) continue;
            try { places[index] = NswWorld.ReadFile(path); }
            catch (Exception e) when (e is IOException or NodFormatException or UnauthorizedAccessException)
            {
                notes.Add($"{Path.GetFileName(path)} could not be read ({e.Message}); that place will be built fresh");
            }
        }
        return places;
    }

    /// <summary>
    /// Location <paramref name="index"/> as its file has it: built from the level and the scene the
    /// story gives it (the chronicle record in `!main.nsg`), the file laid over it, and written as a
    /// Vitomir place. Null, with a note, when the chronicle has no such location or its level is not
    /// in the install.
    /// </summary>
    public static (string Name, LocationSave Save)? Place(GameFiles files, Chronicle chronicle, NsgSave nsg, int index,
                                                         NswWorld file, long clockMs, EffectDefinitions effects,
                                                         DisciplineDefinitions powers, List<string> notes)
    {
        if (chronicle.At(index) is not { } location || index >= nsg.Chronicle.Locations.Length)
        {
            notes.Add($"location_{index}.nsw is for a location {chronicle.Name} does not have; left out");
            return null;
        }
        var state = nsg.Chronicle.Locations[index];
        string level = Strip(state.Level);
        string? scene = Strip(state.Scene) is { Length: > 0 } s && !s.Equals("null", StringComparison.OrdinalIgnoreCase) ? s : null;
        // The scene the place was saved with, when the story has moved it on since (a chapter's
        // scene changed while the party was away): the file's own is what its records index into.
        if (file.ChapterScene is { Name.Length: > 0 } saved && !string.Equals(Strip(saved.Name), scene, StringComparison.OrdinalIgnoreCase))
            scene = Strip(saved.Name);
        Location place;
        try { place = Location.Load(files, level, things: false, sky: false, scene: scene,
                                    guids: new ThingGuids { Next = (int)nsg.ThingCounter, Loading = true }); }   // 1.1's numbers, from the file
        catch (Exception e) when (e is IOException or KeyNotFoundException or NodFormatException or InvalidDataException)
        {
            notes.Add($"{location.Name} ({level}) could not be built ({e.Message}); left out");
            return null;
        }
        place.Milliseconds = (int)clockMs;
        var scripts = NswImport.Apply(place, file, clockMs, effects, powers, notes);
        var save = SaveCapture.OfPlace(place);
        NswImport.Into(save, scripts, clockMs);
        return (location.Name, save);
    }

    static string Strip(string s)
    {
        int dot = s.LastIndexOf('.');
        return dot < 0 ? s : s[..dot];
    }
}
