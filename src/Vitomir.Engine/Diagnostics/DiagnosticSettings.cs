// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the engine says about itself, and how loudly.
using Microsoft.Extensions.Logging;

namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// Where a setting came from, lowest precedence first. Each source overrides the ones before it, so
/// a shipped file sets the baseline, the player's own file overrides it, and a launch option wins.
/// </summary>
public enum SettingSource { Default, InstalledFile, UserFile, Environment, NamedFile, CommandLine }

/// <summary>
/// How much the engine logs, and whether its developer features exist at all.
///
/// Levels are Microsoft's, with the meanings this engine gives them:
/// <list type="bullet">
/// <item><c>Critical</c> — the engine cannot carry on.</item>
/// <item><c>Error</c> — something the player will notice went wrong; the engine carries on.</item>
/// <item><c>Warning</c> — the data is odd but handled: a missing texture, a chronicle naming a
///   scene that isn't there, a script class that doesn't exist. <b>The default.</b></item>
/// <item><c>Information</c> — milestones: the level loaded, the GPU chosen, 924 scripts created.</item>
/// <item><c>Debug</c> — per-subsystem detail: every unimplemented native, every material resolved.</item>
/// <item><c>Trace</c> — the firehose: per frame, per thing.</item>
/// </list>
/// </summary>
public sealed class DiagnosticSettings
{
    /// <summary>Developer features. Off unless the player asks for them, which is the point.</summary>
    public bool Debug { get; set; }
    /// <summary>The level the console logs at, and the default for everything.</summary>
    public LogLevel Level { get; set; } = LogLevel.Warning;
    /// <summary>
    /// The level the log file keeps whatever the console is doing — a basic narrative of the run,
    /// so an early crash leaves something to read. <c>None</c> stops the engine writing a log at all.
    /// </summary>
    public LogLevel FileLevel { get; set; } = LogLevel.Information;
    /// <summary>Per-category levels, matched by longest prefix (<c>Vitomir.Scripting</c> covers everything under it).</summary>
    public Dictionary<string, LogLevel> Categories { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// One specific log file instead of the managed folder — a one-off run written where the caller
    /// asked. Setting it turns the archive off, because the caller owns that path.
    /// </summary>
    public string File { get; set; } = "";
    /// <summary>Where the run's logs live. Empty means the standard place beside the player's settings.</summary>
    public string Folder { get; set; } = "";
    /// <summary>How many earlier runs to keep zipped in <c>Logs\Archive</c>. 0 keeps none.</summary>
    public int Keep { get; set; } = 20;
    /// <summary>Write the log to the console. On by default; a windowed build may not have one.</summary>
    public bool Console { get; set; } = true;

    /// <summary>
    /// <c>[log] window = on</c> (ours, a developer's): the game opens its log window, a viewer of its own (a process of its
    /// own whose X ends only it) showing its lines at the console's level, as <c>--log-window</c> and the console's <c>logwindow</c>
    /// do. Windows alone; off by default.
    /// </summary>
    public bool Window { get; set; }
    /// <summary>Where each setting last came from, for <c>--diagnostics</c> to explain itself.</summary>
    public Dictionary<string, SettingSource> Provenance { get; } = new(StringComparer.OrdinalIgnoreCase);

    public const string Section = "diagnostics";

    /// <summary>The level in force for a category: the longest matching prefix, else <see cref="Level"/>.</summary>
    public LogLevel LevelFor(string category)
    {
        LogLevel best = Level;
        int bestLength = -1;
        foreach (var (prefix, level) in Categories)
            if (prefix.Length > bestLength && Matches(category, prefix)) { best = level; bestLength = prefix.Length; }
        return best;
    }

    /// <summary>
    /// The threshold one sink applies. The console follows <see cref="LevelFor(string)"/> exactly;
    /// the file is that or <see cref="FileLevel"/>, whichever is more verbose, so turning a category
    /// up reaches the file too and turning the console down never silences the record.
    /// </summary>
    public LogLevel LevelFor(string category, SinkChannel channel) => channel switch
    {
        SinkChannel.Console => LevelFor(category),
        SinkChannel.File => FileLevel == LogLevel.None ? LogLevel.None : Min(LevelFor(category), FileLevel),
        _ => LowestLevelFor(category),
    };

    /// <summary>The most verbose threshold any sink has, which is what decides if a line is built.</summary>
    public LogLevel LowestLevelFor(string category)
    {
        var level = Console ? LevelFor(category) : LogLevel.None;
        if (FileLevel != LogLevel.None) level = Min(level, Min(LevelFor(category), FileLevel));
        return level;
    }

    static LogLevel Min(LogLevel a, LogLevel b) => (LogLevel)Math.Min((int)a, (int)b);

    static bool Matches(string category, string prefix) =>
        prefix == "*" ||
        (category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
         (category.Length == prefix.Length || category[prefix.Length] == '.'));

    /// <summary>
    /// Applies one source's key/value pairs. Keys are the INI file's, without the section prefix:
    /// <c>debug</c>, <c>level</c>, <c>level.&lt;category&gt;</c>, <c>file</c>, <c>console</c>.
    /// Anything unrecognised, or a level that isn't a level, is reported and otherwise ignored.
    /// </summary>
    public void Apply(IEnumerable<KeyValuePair<string, string>> pairs, SettingSource source, Action<string>? complain = null)
    {
        foreach (var (key, value) in pairs)
        {
            if (key.Equals("debug", StringComparison.OrdinalIgnoreCase))
            {
                Debug = IniFile.IsTrue(value);
                Provenance["debug"] = source;
            }
            else if (key.Equals("level", StringComparison.OrdinalIgnoreCase))
            {
                if (TryLevel(value, out var level)) { Level = level; Provenance["level"] = source; }
                else complain?.Invoke($"{value} is not a log level");
            }
            else if (key.StartsWith("level.", StringComparison.OrdinalIgnoreCase) && key.Length > 6
                     && !key.Equals("file.level", StringComparison.OrdinalIgnoreCase))
            {
                var category = key[6..];
                if (TryLevel(value, out var level)) { Categories[category] = level; Provenance[key] = source; }
                else complain?.Invoke($"{value} is not a log level");
            }
            else if (key.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                File = value;
                Provenance["file"] = source;
            }
            else if (key.Equals("file.level", StringComparison.OrdinalIgnoreCase))
            {
                if (TryLevel(value, out var level)) { FileLevel = level; Provenance["file.level"] = source; }
                else complain?.Invoke($"{value} is not a log level");
            }
            else if (key.Equals("folder", StringComparison.OrdinalIgnoreCase))
            {
                Folder = value;
                Provenance["folder"] = source;
            }
            else if (key.Equals("keep", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out var keep) && keep >= 0) { Keep = keep; Provenance["keep"] = source; }
                else complain?.Invoke($"keep wants a count of runs to archive, not '{value}'");
            }
            else if (key.Equals("console", StringComparison.OrdinalIgnoreCase))
            {
                Console = IniFile.IsTrue(value);
                Provenance["console"] = source;
            }
            else if (key.Equals("window", StringComparison.OrdinalIgnoreCase))
            {
                // The log window (a developer's; the switch --log-window, the console's logwindow): a viewer of its own showing its lines.
                Window = IniFile.IsTrue(value);
                Provenance["window"] = source;
            }
            else complain?.Invoke($"unknown diagnostics setting '{key}'");
        }
    }

    /// <summary>A level by name, case-insensitively, plus the shorthands <c>info</c>, <c>warn</c>, <c>off</c>.</summary>
    public static bool TryLevel(string text, out LogLevel level)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "info": level = LogLevel.Information; return true;
            case "warn": level = LogLevel.Warning; return true;
            case "off": level = LogLevel.None; return true;
            default: return Enum.TryParse(text, ignoreCase: true, out level) && Enum.IsDefined(level);
        }
    }

    public override string ToString() =>
        $"console {(Console ? Level.ToString() : "off")}" +
        (Categories.Count == 0 ? "" : ", " + string.Join(", ", Categories.Select(c => $"{c.Key}={c.Value}"))) +
        $", file {FileLevel}" +
        (File.Length > 0 ? $" to {File}" : Folder.Length > 0 ? $" in {Folder}" : "") +
        $", keep {Keep}" +
        $", debug {(Debug ? "on" : "off")}";
}
