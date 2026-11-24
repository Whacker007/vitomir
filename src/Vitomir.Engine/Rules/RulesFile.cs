// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The rules file (ours): rules.cfg, an ini file of [Section] Key = value lines read by the settings' own reader (IniFile), laid
// in layers — the defaults in code, the Classic module's shipped copy, then each player module's, the later winning.
using System.Globalization;
using System.Text;
using Vitomir.Engine.Diagnostics;

namespace Vitomir.Engine.Rules;

/// <summary>One layer of rules: who it is (<c>classic</c>, a player module's id), the file it came from (null for none) and its keys.</summary>
public sealed record RulesLayer(string Source, string? Path, IniFile Ini)
{
    /// <summary>A layer of a file's text (a test's, or a file read).</summary>
    public static RulesLayer Of(string source, string text, string? path = null) => new(source, path, IniFile.Parse(text));

    /// <summary>The layer of a file, or null when there is none.</summary>
    public static RulesLayer? Read(string source, string path) => File.Exists(path) ? new(source, path, IniFile.Read(path)) : null;
}

/// <summary>The rules a run resolved: the rules, where each key's value came from (<see cref="RulesFile.Builtin"/> for the defaults), and what was said.</summary>
public sealed record ResolvedRules(GameRules Rules, IReadOnlyDictionary<RuleKey, string> Sources, IReadOnlyList<string> Said);

/// <summary>
/// **The rules file**: its name (<see cref="FileName"/>, in a module's <c>Data\</c>), the layers resolved (<see cref="Resolve"/>), the
/// shipped copy written (<see cref="Write"/>), and the rules as a tape line (<see cref="TapeFields"/>, <see cref="FromTape"/>).
/// </summary>
public static class RulesFile
{
    /// <summary>The file's name, at the top of a module's <c>Data\</c> folder.</summary>
    public const string FileName = "rules.cfg";

    /// <summary>The source of a key nobody's file changed: the defaults in code, 1.1's.</summary>
    public const string Builtin = "1.1";

    /// <summary>The Classic module's own layer's source name.</summary>
    public const string Classic = "classic";

    /// <summary>The keys said once a process: an unknown key or a refused value is warned the first time, not at every run of a session.</summary>
    static readonly HashSet<string> warned = [];

    /// <summary>
    /// **The rules of the layers, in order, the later winning**, over the defaults. A key the game does not have is said once and
    /// passed over; a value that does not read as the key's type (or is out of its range) is said and the layer below kept; a fixed
    /// key's (<see cref="RuleAttribute.Fixed"/>) other value is said and refused.
    /// </summary>
    public static ResolvedRules Resolve(IEnumerable<RulesLayer> layers, Action<string>? say = null)
    {
        var rules = GameRules.Defaults();
        var defaults = GameRules.Defaults();
        var sources = RuleKey.All.ToDictionary(k => k, _ => Builtin);
        var said = new List<string>();
        void Say(string once, string line)
        {
            lock (warned) if (!warned.Add(once)) return;
            said.Add(line);
            say?.Invoke(line);
        }
        foreach (var layer in layers)
        {
            string where = layer.Path is null ? $"{FileName} ({layer.Source})" : $"{layer.Path} ({layer.Source})";
            foreach (var (id, written) in layer.Ini.Values)
            {
                if (RuleKey.Find(id) is not { } key)
                {
                    int dot = id.IndexOf('.');
                    Say($"unknown {layer.Source} {id}", dot < 0
                        ? $"rules: {where}: {id} is outside any [Section]: passed over"
                        : $"rules: {where}: [{id[..dot]}] {id[(dot + 1)..]} is no rule of the game: passed over");
                    continue;
                }
                // A comment may follow a value on its line (no value of the file has a ; or a #).
                string text = written.IndexOfAny([';', '#']) is var cut and >= 0 ? written[..cut].TrimEnd() : written;
                if (key.Parse(text, out var why) is not { } value)
                {
                    Say($"bad {layer.Source} {key.Id} {text}", $"rules: {where}: [{key.Section}] {key.Name} = {text}: {why}; {key.Format(rules)} kept");
                    continue;
                }
                if (key.Rule.Fixed && FormatOf(key, value) != key.Format(defaults))
                {
                    Say($"fixed {layer.Source} {key.Id} {text}", $"rules: {where}: [{key.Section}] {key.Name} = {text}: fixed in this release"
                        + (key.Rule.FixedWhy is { } fixedWhy ? $" ({fixedWhy})" : "") + $"; {key.Format(defaults)} kept");
                    continue;
                }
                key.Set(rules, value);
                sources[key] = layer.Source;
            }
        }
        return new ResolvedRules(rules, sources, said);
    }

    static string FormatOf(RuleKey key, object value)
    {
        var probe = GameRules.Defaults();
        key.Set(probe, value);
        return key.Format(probe);
    }

    /// <summary>
    /// **The layers a game reads**: the Classic module's own file (<paramref name="installed"/>'s <c>Data\rules.cfg</c>, when the module's
    /// folder is known and has it), then each player module's (<paramref name="modules"/>: its id and its <c>Data\</c> folder), in load order.
    /// </summary>
    public static IEnumerable<RulesLayer> Layers(string? installed, IEnumerable<(string Id, string DataFolder)> modules)
    {
        if (installed is not null && RulesLayer.Read(Classic, System.IO.Path.Combine(installed, "Data", FileName)) is { } own) yield return own;
        foreach (var (id, data) in modules)
            if (RulesLayer.Read(id, System.IO.Path.Combine(data, FileName)) is { } layer) yield return layer;
    }

    /// <summary>
    /// The Classic module's own folder, whose <c>Data\rules.cfg</c> is the shipped layer: the one the platform's program gave
    /// (<see cref="HostFolders.Installed"/>), else the module laid out beside the program (<c>Modules\Classic\</c>, as
    /// <c>vitomir-tools</c> has it), else none.
    /// </summary>
    public static string? ClassicFolder()
    {
        if (HostFolders.Installed is { } given) return given;
        string beside = System.IO.Path.Combine(AppContext.BaseDirectory, "Modules", "Classic");
        return Directory.Exists(beside) ? beside : null;
    }

    /// <summary>
    /// **The run's rules**: the layers of <see cref="ClassicFolder"/> and <paramref name="modules"/> resolved
    /// (<see cref="Resolve"/>) and put in force (<see cref="GameRules.Current"/>), what was refused said on <paramref name="say"/>.
    /// </summary>
    public static ResolvedRules Configure(IEnumerable<(string Id, string DataFolder)> modules, Action<string>? say = null)
    {
        var resolved = Resolve(Layers(ClassicFolder(), modules), say);
        GameRules.Current = resolved.Rules;
        return resolved;
    }

    /// <summary>
    /// **The shipped file**: every key at its default, each under a comment saying what it does (and what of it is ours, fixed or
    /// unsure), by section. Made from the code (a test holds the checked-in copy to it), so the file a player reads is the game's.
    /// </summary>
    public static string Write(GameRules? rules = null)
    {
        rules ??= GameRules.Defaults();
        var w = new StringBuilder();
        void Line(string s = "") => w.Append(s).Append('\n');
        Line("; Vitomir's rules file: the game's rule numbers that 1.1 kept in its program rather than its data.");
        Line("; This copy is the Classic game's own, with every key at its default (1.1's number unless a comment says it is ours).");
        Line("; It is written from the game's code: change the numbers in a module of your own, not here.");
        Line(";");
        Line("; A player module changes a rule by carrying a rules.cfg at the top of its Data folder with only the keys it changes,");
        Line("; under their [Section]. The layers are: the defaults in the code, this file, then each module's in load order, the");
        Line("; later winning. Such a module is a rules module, and a game whose rules differ from 1.1's is modded.");
        Line("; A list is one line, comma separated. ; and # start a comment, at the start of a line or after a value. A key the");
        Line("; game does not know is warned of and passed over; a value that does not read keeps the layer below. A key marked");
        Line("; fixed is listed but kept at its value.");
        Line("; Where an enabled Fix meets a number here, the Fix works on top of the number this file gives.");
        RuleSectionAttribute? section = null;
        foreach (var key in RuleKey.All)
        {
            if (!ReferenceEquals(section, key.SectionRule))
            {
                section = key.SectionRule;
                Line();
                Line($"[{section.Name}]");
                foreach (var l in Wrap(section.What)) Line("; " + l);
            }
            Line();
            foreach (var l in Wrap(key.Rule.What)) Line("; " + l);
            if (key.Rule.Values is { } values) foreach (var l in Wrap("Values: " + values)) Line("; " + l);
            if (key.Rule.Ours) Line("; ours: not 1.1's number.");
            if (key.Rule.Fixed) foreach (var l in Wrap("fixed: " + (key.Rule.FixedWhy ?? "the game is built on it"))) Line("; " + l);
            if (key.Rule.Unsure is { } unsure) foreach (var l in Wrap("unsure: " + unsure)) Line("; " + l);
            Line($"{key.Name} = {key.Format(rules)}");
        }
        return w.ToString();
    }

    static IEnumerable<string> Wrap(string text, int width = 118)
    {
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width) { yield return line.ToString(); line.Clear(); }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) yield return line.ToString();
    }

    /// <summary>
    /// **The resolved rules, a line a key**: <c>[Section] Key = value  (source)</c>, what <c>--rules</c> prints, the sections' heads
    /// between them; a changed key's line is marked with <c>*</c>.
    /// </summary>
    public static IEnumerable<string> Report(ResolvedRules resolved)
    {
        var defaults = GameRules.Defaults();
        yield return $"rules: {Describe(resolved.Rules)}";
        string? section = null;
        foreach (var key in RuleKey.All)
        {
            if (key.Section != section) { section = key.Section; yield return $"[{section}]"; }
            bool changed = key.Format(resolved.Rules) != key.Format(defaults);
            yield return $"{(changed ? "*" : " ")} {key.Name} = {key.Format(resolved.Rules)}  ({resolved.Sources[key]})";
        }
    }

    /// <summary>The rules in a few words: <c>1.1's defaults</c>, or the hash and how many keys differ, for the console and the log.</summary>
    public static string Describe(GameRules rules)
    {
        var changed = rules.Changed();
        return changed.Count == 0 ? "1.1's defaults"
            : $"{changed.Count} {(changed.Count == 1 ? "key differs" : "keys differ")} from 1.1's ({rules.ShortHash()}): {string.Join(", ", changed.Select(k => $"{k.Id} = {k.Format(rules)}"))}";
    }

    /// <summary>
    /// The rules as a tape's <c>rules</c> line writes them: <c>hash=</c> the short hash (<see cref="GameRules.ShortHash"/>), then each key
    /// that differs from the default as <c>Section.Key=value</c>, the value escaped as one word (<see cref="ReplayTape.Escape"/>).
    /// </summary>
    public static string[] TapeFields(GameRules rules) =>
        ["hash=" + rules.ShortHash(), .. rules.Changed().Select(k => $"{k.Id}={ReplayTape.Escape(k.Format(rules))}")];

    /// <summary>
    /// The rules a tape's <c>rules</c> line names: the defaults with each named key's value; null fields (a tape older than the line)
    /// are the defaults. A key this build does not know, or a value it does not read, or a hash that is not what the keys make, is said:
    /// the replay goes on, and may not be the recording.
    /// </summary>
    public static GameRules FromTape(IReadOnlyList<string>? fields, Action<string>? say = null)
    {
        var rules = GameRules.Defaults();
        if (fields is null) return rules;
        string? hash = null;
        foreach (var field in fields)
        {
            int eq = field.IndexOf('=');
            if (eq < 0) continue;
            string name = field[..eq], value = ReplayTape.Unescape(field[(eq + 1)..]);
            if (name == "hash") { hash = value; continue; }
            if (RuleKey.Find(name) is not { } key) { say?.Invoke($"replay: the tape's rules name {name}, which this build does not have"); continue; }
            if (key.Parse(value, out var why) is { } v) key.Set(rules, v);
            else say?.Invoke($"replay: the tape's rules {name} = {value}: {why}");
        }
        if (hash is not null && hash != rules.ShortHash())
            say?.Invoke($"replay: the tape's rules hash {hash} is not what its keys make here ({rules.ShortHash()}): the replay may not be the recording");
        return rules;
    }

    /// <summary>A number in the file's own words, for a line that quotes one.</summary>
    public static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
