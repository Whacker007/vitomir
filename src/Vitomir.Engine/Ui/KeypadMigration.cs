// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The keypad's + and − moved to the quick keys in a vitomir.ini written before them (ours). The key
// page writes every binding into [keys], so a file saved before the quick keys names the keypad pair on
// the zoom, as 1.1's own keys have it, and the new defaults never reached it — as in a file saved
// before them: `zoomout = -, Keypad -`, `zoomin = =, Keypad +`, and no quick keys.
using Vitomir.Engine.Diagnostics;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **A one-time move of the keypad pair to the quick keys** (ours). A <c>[keys]</c> section that has
/// **neither** <c>quicksave</c> nor <c>quickload</c> was written before those commands existed; if its zoom
/// entries are **exactly 1.1's defaults** (<c>zoomout = -, Keypad -</c> and <c>zoomin = =, Keypad +</c>,
/// in any order) the keypad pair was never the player's choice, only what the file was given, and it
/// moves: the zoom keeps − and =, the quick save takes the keypad's +, the quick load its −
/// (<see cref="KeyBindings.KeypadZoom"/>). **Anything else the player chose is left alone**: zoom keys
/// changed in any way stay as they are, and a quick key whose default is already bound to another
/// command in the file is written as <c>none</c> rather than doubling that key. Either way the quick keys
/// are then written into the file, so it says what is in force and **the move never runs again** — a
/// file with a quick key in it is past it. A file with no <c>[keys]</c> at all has the defaults already
/// and is not touched. Run once as the program starts, before the keys are read
/// (<see cref="KeyBindings.Configure"/>), on the player's own file only; a replay writes nothing
/// (<see cref="SettingsFile.Replaying"/>).
/// </summary>
public static class KeypadMigration
{
    static readonly KeyChord Minus = new(KeyNames.Minus), EqualsKey = new(KeyNames.EqualsKey),
                             KeypadMinus = new(KeyNames.KeypadMinus), KeypadPlus = new(KeyNames.KeypadPlus);

    /// <summary>What the move writes into <c>[keys]</c> for this file, and whether the keypad pair moved; null when there is nothing to do.</summary>
    public static (IReadOnlyList<KeyValuePair<string, string>> Values, bool Moved)? Changes(IniFile ini)
    {
        var section = ini.Section(KeyBindings.Section).ToList();
        if (section.Count == 0) return null;
        string Id(KeyCommand c) => KeyBindings.Of(c).Id;
        string? Entry(KeyCommand c) => section.LastOrDefault(kv => kv.Key.Equals(Id(c), StringComparison.OrdinalIgnoreCase)).Value;
        if (Entry(KeyCommand.QuickSave) is not null || Entry(KeyCommand.QuickLoad) is not null) return null;

        if (Is(Entry(KeyCommand.ZoomOut), Minus, KeypadMinus) && Is(Entry(KeyCommand.ZoomIn), EqualsKey, KeypadPlus))
            return ([new(Id(KeyCommand.ZoomOut), Minus.ToString()), new(Id(KeyCommand.ZoomIn), EqualsKey.ToString()),
                     new(Id(KeyCommand.QuickSave), KeypadPlus.ToString()), new(Id(KeyCommand.QuickLoad), KeypadMinus.ToString())], true);

        // Not the old defaults: the player's zoom stays, and a quick key does not take a key it has given elsewhere.
        var taken = section.Where(kv => !kv.Key.Equals(Id(KeyCommand.QuickSave), StringComparison.OrdinalIgnoreCase)
                                        && !kv.Key.Equals(Id(KeyCommand.QuickLoad), StringComparison.OrdinalIgnoreCase))
                           .SelectMany(kv => Chords(kv.Value)).ToHashSet();
        string Quick(KeyChord key) => taken.Contains(key) ? "none" : key.ToString();
        return ([new(Id(KeyCommand.QuickSave), Quick(KeypadPlus)), new(Id(KeyCommand.QuickLoad), Quick(KeypadMinus))], false);
    }

    /// <summary>
    /// The move made to the file at <paramref name="path"/>, if it needs it; what was done, for the log,
    /// or null when nothing was.
    /// </summary>
    public static string? Run(string path)
    {
        if (!File.Exists(path) || Changes(IniFile.Read(path)) is not { } change) return null;
        SettingsFile.Set(path, KeyBindings.Section, change.Values);
        return change.Moved
            ? "keys: the keypad's + and - moved from the zoom to the quick save and load (a vitomir.ini from before the quick keys)"
            : "keys: the quick keys written into vitomir.ini (" + string.Join(", ", change.Values.Select(v => $"{v.Key} = {v.Value}")) + ")";
    }

    // Whether an entry binds exactly these two keys, in either order, and nothing else.
    static bool Is(string? value, KeyChord a, KeyChord b) =>
        value is not null && Chords(value) is var chords && chords.Count == 2 && chords.Contains(a) && chords.Contains(b);

    static List<KeyChord> Chords(string value) =>
        [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(KeyChord.Parse).OfType<KeyChord>()];
}
