// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Levels.Cells;

namespace Vitomir;

/// <summary>
/// **Ours.** Which chronicle places a run plays as cell levels (<see cref="CellPlaces"/>):
/// <c>--place OldTownDay=&lt;package.l2&gt;</c>, any number of times, over
/// the ini's <c>[levels] place.OldTownDay = &lt;package.l2&gt;</c> lines. Without either, every place is
/// its 1.1 level.
/// </summary>
public static class PlaceOptions
{
    /// <summary>Reads the ini's places and the command line's into the map; an exit code when one is wrong, else null.</summary>
    public static int? Handle(string[] args)
    {
        try { CellPlaces.FromIni(IniFile.Read(Diag.UserSettingsPath), Console.WriteLine); }
        catch (IOException) { }
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] != "--place") continue;
            if (!CellPlaces.Parse(args[i + 1], out string wrong)) { Console.Error.WriteLine($"place: {wrong}"); return 2; }
        }
        return null;
    }
}
