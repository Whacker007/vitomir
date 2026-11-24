// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Dressing a player's character when the host makes it: the same `.NPC` sheet, starting kit and
// disciplines the single-player loader gives the player (`Program.cs`, the `ACTOR_RPGSTATS` step and
// the loader's `discipline` arm), so a network character is not a 20-in-everything shell.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Net;

public static class Outfit
{
    /// <summary>
    /// The template's `ACTOR_RPGSTATS` sheet applied (stats and their bounds), its items given, its
    /// disciplines learnt. Answers a line for the log, or null when the template names no sheet.
    /// </summary>
    public static string? Dress(Location place, WorldThing thing)
    {
        // The host reads the sheet as it makes the thing (World.CharacterSheets),
        // the kit's numbers before the character's own; this finishes one the place was not awake to give.
        if (CharacterSheets.Given(thing) is { } read) return read;
        if (CharacterSheets.Waiting(thing)) return CharacterSheets.Give(place, thing);
        if (thing.Actor is not { RpgStats.Length: > 0 } sheet || place.Files is not { } files) return null;
        string path = NpcFile.PathFor(sheet.RpgStats);
        if (!files.TryRead(path, out var data)) return $"no {path}";
        var npc = NpcFile.Read(data, path);
        int stats = CharacterSheets.Read(sheet, npc);
        int items = 0, learnt = 0;
        if (npc.Items.Count > 0 && place.Templates is { } templates && place.Effects is { } effects)
            items = StartingKit.Give(thing, npc, templates, effects, place.Milliseconds).Count(g => g.Placed);
        if (place.Powers is { } powers)
            foreach (var line in npc.Disciplines)
            {
                if (powers[line.Name] is not { } def || DisciplineBook.Make(thing, def) is not { } inst) continue;
                DisciplineBook.SetLevel(thing, powers, line.Name, line.Level);
                if (line.Quick >= 0) inst.QuickSlot = line.Quick;
                learnt++;
            }
        return $"{npc.Name} from {path}: {stats} stats, {items} items, {learnt} disciplines";
    }
}
