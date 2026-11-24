// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// What is wrong with a scenario, by a stable name an author's tools can
/// match. The arena's codes are kept where the scenario has the same thing (<see cref="Arena.ArenaSpecError"/>: a fighter is an
/// actor here, so its <c>TooFewFighters</c> is <see cref="TooFewActors"/>); the rest are the scenario's own.
/// </summary>
public enum ScenarioErrorCode
{
    // ---- the arena's (ArenaSpec.cs), the same meaning
    /// <summary>The file is missing or unreadable.</summary>
    FileMissing,
    /// <summary>The file is not JSON (with comments and trailing commas), or not a JSON object.</summary>
    NotJson,
    /// <summary>A field the format does not have at that place (a typo is never passed over); the nearest names are suggested.</summary>
    UnknownField,
    /// <summary>A field of the wrong kind: a number where text is wanted, a list where an object is, …</summary>
    WrongType,
    /// <summary>A number outside what it may be, or a word that is not one of the words a field takes.</summary>
    OutOfRange,
    /// <summary>No level for the ground: <c>VITOMIR_COURSE_LEVEL</c> is not set and <c>--level</c> not given.</summary>
    NoLevel,
    /// <summary>The level named is not a level package (it has no <c>level.json</c>).</summary>
    LevelNotFound,
    /// <summary>An actor's name is empty, repeated, or holds a character a path or selector cannot carry.</summary>
    BadName,
    /// <summary>A place that is not <c>[x, y]</c>, <c>[x, y, z]</c> or <c>{ "from", "distanceMetres", "bearingDegrees" }</c>, or a relative place that goes round in a circle.</summary>
    BadPlace,
    /// <summary>An actor's <c>ai</c> that is not one of its kinds, or numbers its kind does not take.</summary>
    BadBrain,
    /// <summary>A sheet that is not a template's id or <c>{ "template" | "file", "values", "powers" }</c>.</summary>
    BadSheet,
    /// <summary>A sheet file that is not there.</summary>
    SheetFileMissing,
    /// <summary>A <c>foe</c> (or a step's target) that is no other actor.</summary>
    UnknownFoe,
    /// <summary>A sweep's dimension without a path or values, with values of the wrong kind, or a <c>zip</c> of unequal lengths.</summary>
    BadSweep,
    /// <summary>A power that is not a rank or <c>{ "rank", "options" }</c>, or a nonzero rank given an empty list of options.</summary>
    BadPower,
    /// <summary><c>slots</c> that are not a list of quick slots, 1 to 8, each once.</summary>
    BadSlots,
    /// <summary>A <c>side</c> given to some actors and not others, all on one side, or a foe on its own side.</summary>
    BadSide,

    // ---- the scenario's own
    /// <summary>No <c>"format"</c>, or one this build does not read (it reads <c>vitomir-scenario/1</c>).</summary>
    UnknownFormat,
    /// <summary>Fewer than two actors.</summary>
    TooFewActors,
    /// <summary>A name the game does not have: a character, preset, sheet, trait, weapon, armour, power, option, switch, event, field or actor; the nearest names are suggested.</summary>
    UnknownName,
    /// <summary>A step that is not an object with exactly one kind field, or whose value or fields its kind does not take.</summary>
    BadStep,
    /// <summary>An event selector that names no event, or a field to filter on that the event does not have.</summary>
    BadSelector,
    /// <summary>An observable path (<c>Guard.health</c>, <c>Christof.distanceTo.Guard</c>) that is not one.</summary>
    BadObservable,
    /// <summary>A settable path (a sweep's <c>set</c>, an assertion's <c>over</c> or <c>points</c>) that is not one.</summary>
    BadPath,
    /// <summary>Two tracks of one actor whose steps hold the same channel from the same scheduled time.</summary>
    ChannelClash,
    /// <summary>A place inside a wall, off the floor, or outside the ground.</summary>
    PlaceBlocked,
    /// <summary>A field this build does not have yet; the message names the build that brings it.</summary>
    NotBuilt,
    /// <summary>A trigger without a <c>when</c> or a <c>do</c>, or whose <c>do</c> is not a list of <c>{ "actor", "step" }</c> or <c>{ "end" }</c>.</summary>
    BadTrigger,
    /// <summary>A measure without a name, with a repeated name, or with no value form (or more than one).</summary>
    BadMeasure,
    /// <summary>An assertion without a value form, a comparator, or the fields its scope needs (a trend's <c>over</c> and <c>direction</c>).</summary>
    BadAssertion,
    /// <summary>The game refused what the scenario asks when its world was built (a sheet value out of the module's range, a weapon without the shield asked for).</summary>
    Refused,
    /// <summary>
    /// A player's input (<c>control.playerInput</c>) that is not a list of segments, a segment's field out of its range, a second
    /// actor driven by the player's input, or an input actor also given an AI, a script, a mute or a trigger's step.
    /// </summary>
    BadInput,
    /// <summary>
    /// A fixed contact that is not one — a required field missing (<c>atSeconds</c>, <c>region</c>), <c>part</c> and
    /// <c>alongBladeShare</c> both, a weapon its attacker does not hold, an actor striking itself — or <c>contacts</c> in a fight, or a
    /// native blow (an attack or bow step, the player's input) in a contact scenario.
    /// </summary>
    BadContact,
    /// <summary>
    /// An objective that is not one — no owner (<c>actor</c> or <c>side</c>) or both, no kind or two, a target that is its
    /// owner or on its owner's side, a place that is neither <c>point</c> nor a named place of the level, a radius below 0, a time its kind
    /// does not take, a place-kind (reach, escape, hold) given to a side; a repeated name. And a <c>swap</c> that cannot be made.
    /// </summary>
    BadObjective,
}

/// <summary>
/// One error in a scenario: its code, where it is (an RFC 6901 JSON pointer into the file, <c>/actors/1/place</c>; empty
/// for the file itself), what is wrong and what the field takes, and the nearest names when a name was misspelt.
/// </summary>
public sealed record ScenarioError(ScenarioErrorCode Code, string Pointer, string Message, IReadOnlyList<string> Suggestions)
{
    /// <summary>The error as <c>--check</c> prints it: <c>error: /actors/1/place: … [PlaceBlocked]</c>.</summary>
    public string Line() => $"error: {(Pointer.Length == 0 ? "(the file)" : Pointer)}: {Message}{(Suggestions.Count == 0 ? "" : $" (did you mean {string.Join(", ", Suggestions.Select(s => $"\"{s}\""))}?)")} [{Code}]";

    public override string ToString() => Line();
}

/// <summary>JSON pointers (RFC 6901) and the nearest names, for the scenario's errors.</summary>
public static class ScenarioPointer
{
    /// <summary>The pointer of a member of <paramref name="parent"/>: <c>/actors</c> + <c>1</c> → <c>/actors/1</c> (<c>~</c> and <c>/</c> escaped).</summary>
    public static string Of(string parent, string member) => $"{parent}/{member.Replace("~", "~0").Replace("/", "~1")}";

    /// <summary>The pointer of an item of a list.</summary>
    public static string Of(string parent, int index) => $"{parent}/{index}";

    /// <summary>
    /// The names in <paramref name="known"/> nearest to <paramref name="given"/> (case aside): those it differs from by an edit
    /// distance of at most a third of its length (two at least), or that it begins or is contained in; the nearest first, three
    /// at most.
    /// </summary>
    public static IReadOnlyList<string> Nearest(string given, IEnumerable<string> known, int most = 3)
    {
        string g = given.ToLowerInvariant();
        int limit = Math.Max(2, g.Length / 3);
        var scored = new List<(int Score, string Name)>();
        foreach (var name in known.Distinct(StringComparer.Ordinal))
        {
            string n = name.ToLowerInvariant();
            if (n == g) { scored.Add((0, name)); continue; }
            int d = Distance(g, n);
            if (d <= limit) scored.Add((d, name));
            else if (g.Length >= 3 && (n.StartsWith(g, StringComparison.Ordinal) || n.Contains(g, StringComparison.Ordinal) || g.Contains(n, StringComparison.Ordinal) && n.Length >= 3))
                scored.Add((limit + 1 + Math.Abs(n.Length - g.Length), name));
        }
        return [.. scored.OrderBy(s => s.Score).ThenBy(s => s.Name, StringComparer.Ordinal).Take(most).Select(s => s.Name)];
    }

    /// <summary>The Damerau–Levenshtein distance (with adjacent transpositions) between two words.</summary>
    public static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }

    /// <summary>A list of words for a message: <c>"a", "b" or "c"</c>.</summary>
    public static string Words(IEnumerable<string> words, string last = "or")
    {
        var list = words.Select(w => $"\"{w}\"").ToList();
        if (list.Count <= 1) return string.Concat(list);
        var sb = new StringBuilder();
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) sb.Append(i == list.Count - 1 ? $" {last} " : ", ");
            sb.Append(list[i]);
        }
        return sb.ToString();
    }
}
