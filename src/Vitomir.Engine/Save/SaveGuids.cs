// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A save's GUIDs and the game's one counter.
//
// A load gives every saved thing the number it was saved with and puts the counter back where the
// save left it, exactly as 1.1 does. Format 1 (every Vitomir save earlier) numbered
// each place on its own, so the same number could name a thing in one place and another thing in the
// next; it is converted as it is read.
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Save;

public static class SaveGuids
{
    static readonly ILogger log = Diag.For(typeof(SaveGuids).FullName!);

    /// <summary>
    /// A format-1 save made into a format-2 one, in memory; the file is not touched. The place the
    /// party is in and the party itself keep every number they were saved with — they were all
    /// numbered by that one place's counter, so they are distinct, and every number a script of theirs
    /// kept (a discipline's pad, a torch's flame, a companion) still names the same thing. The game's
    /// counter starts past all of them. **Every other place the save remembers** was numbered by its
    /// own counter from 1 too and cannot keep its numbers beside them: its things are given new ones
    /// from the game's counter when the party next goes there, and a number one of *its* scripts
    /// kept is left as it was — named in the notes this answers, and in the log.
    /// </summary>
    public static List<string> Convert(SaveGame game)
    {
        var notes = new List<string>();
        if (game.Header.Format >= 2) return notes;
        int counter = 1;
        void See(ThingSave t)
        {
            counter = Math.Max(counter, t.Guid + 1);
            foreach (var item in t.Actor?.Inventory.Values ?? Enumerable.Empty<ThingSave>()) See(item);
        }
        if (game.World.TryGetValue(game.Header.Location, out var here))
        {
            counter = Math.Max(counter, here.NextGuid);
            foreach (var t in here.Things.Values) See(t);
        }
        foreach (var m in game.Party.Members) See(m);
        foreach (var (name, place) in game.World)
        {
            if (ReferenceEquals(place, here)) continue;
            int renumbered = place.Things.Values.Sum(Forget);
            place.NextGuid = 0;
            if (renumbered == 0) continue;
            string note = $"{name}: {renumbered} things get new GUIDs when the party goes back (a format-1 save numbered each place on its own); a GUID one of its scripts kept is not carried over";
            notes.Add(note);
            log.LogWarning("load: {Note}", note);
        }
        game.Session.ThingCounter = counter;
        game.Header.Format = SaveArchive.FormatVersion;
        log.LogInformation("load: a format-1 save, converted — the game's GUID counter starts at {Counter}", counter);
        return notes;
    }

    /// <summary>A thing's number, and every number its pack holds, let go; how many.</summary>
    static int Forget(ThingSave t)
    {
        int n = t.Guid > 0 ? 1 : 0;
        t.Guid = 0;
        foreach (var item in t.Actor?.Inventory.Values ?? Enumerable.Empty<ThingSave>()) n += Forget(item);
        return n;
    }

    /// <summary>
    /// The counter a loaded game's first place is built with. When the save keeps that place, it is
    /// <see cref="ThingGuids.Loading"/>, so the build takes no number and every saved thing is handed
    /// its own afterwards (<see cref="Loaded"/>); when it does not, the place is made as a first visit
    /// makes it, from the save's counter on. Either way the leader's body is the leader's number.
    /// </summary>
    public static ThingGuids ForLoad(SaveGame game)
    {
        var guids = new ThingGuids
        {
            Next = Math.Max(1, game.Session.ThingCounter),
            Loading = game.World.ContainsKey(game.Header.Location),
            Arriving = game.Party.Members.FirstOrDefault()?.Guid ?? 0,
        };
        foreach (var (key, first) in game.Session.ReservedGuids ?? []) guids.Reserved[key] = first;
        return guids;
    }

    /// <summary>
    /// The load is done (the place and the party given their numbers back): the counter goes where the
    /// save left it, and making things takes numbers again. Never below a number in use.
    /// </summary>
    public static void Loaded(Location place, SaveGame game)
    {
        int counter = Math.Max(1, game.Session.ThingCounter);
        foreach (var t in place.Things)
            if (!ThingGuids.IsPlaceholder(t.Guid)) counter = Math.Max(counter, ThingGuids.Raw(t.Guid) + 1);   // by count (a host's bit)
        place.Guids.Loading = false;
        place.Guids.Arriving = 0;
        place.Guids.Resume(counter);
    }
}
