// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The one place the engine's diagnostics are switched on, and the one place anything asks for a
// logger. Quiet by default: a player who asks for nothing gets warnings and errors on the console,
// nothing written to disk, and no developer features at all.
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// Engine diagnostics. Call <see cref="Configure"/> once at start-up; everything else asks
/// <see cref="For{T}"/> for a logger and <see cref="Enabled"/> whether developer features exist.
///
/// Settings come from five places, each overriding the one before: built-in defaults, a
/// <c>vitomir.ini</c> beside the executable, the player's own <c>vitomir.ini</c>, the environment,
/// and the command line.
/// </summary>
public static class Diag
{
    static EngineLoggerFactory? factory;

    /// <summary>The settings in force. Safe to read before <see cref="Configure"/>; then they are the defaults.</summary>
    public static DiagnosticSettings Settings { get; private set; } = new();

    /// <summary>
    /// Whether the engine's developer features exist this run. Off unless the player asked, so a
    /// diagnostic overlay, a script trace or a validation layer costs a shipped game nothing.
    /// </summary>
    public static bool Enabled => Settings.Debug;

    /// <summary>Where the settings were read from, in the order they were applied.</summary>
    public static IReadOnlyList<string> Sources => sources;
    static readonly List<string> sources = [];

    /// <summary>A logger for a subsystem. Categories are dotted, like <c>Vitomir.World</c>.</summary>
    public static ILogger For(string category) =>
        factory?.CreateLogger(category) ?? NullLogger.Instance;

    /// <summary>
    /// **Ours**: <paramref name="sink"/> hears every line from now until the result is disposed, beside
    /// whatever the run already logs to — without <see cref="Install"/>'s new factory, which would leave
    /// the loggers classes keep in static fields writing to the old one. With nothing installed yet (a
    /// test), a quiet factory is installed first: no console and no file, but Information and worse built for the
    /// sink. The story sweep uses it.
    /// </summary>
    public static IDisposable Listen(ILogSink sink)
    {
        var on = factory ?? Install(new DiagnosticSettings { Console = false, FileLevel = LogLevel.Information }, openLogFile: false);
        on.Add(sink);
        return new Unlisten(on, sink);
    }

    sealed class Unlisten(EngineLoggerFactory on, ILogSink sink) : IDisposable
    {
        public void Dispose() => on.Remove(sink);
    }

    /// <summary>A logger named after the type asking for it, which is where most calls come from.</summary>
    public static ILogger For<T>() => For(Category(typeof(T)));

    /// <summary>
    /// The category a type logs under: its namespace, trimmed to the subsystem.
    /// <c>Vitomir.Engine.World.Location</c> logs as <c>Vitomir.World</c>, so that
    /// <c>level.Vitomir.World=Debug</c> in the settings file means what a reader expects.
    /// </summary>
    public static string Category(Type type)
    {
        var ns = type.Namespace ?? "Vitomir";
        return ns.StartsWith("Vitomir.Engine.", StringComparison.Ordinal) ? "Vitomir." + ns["Vitomir.Engine.".Length..] : ns;
    }

    /// <summary>
    /// Reads the settings and installs the sinks. <paramref name="args"/> is the command line;
    /// <paramref name="installedDir"/> defaults to the folder the executable is in.
    /// </summary>
    /// <param name="openLogFile">
    /// False to leave the log file alone entirely — no new file, no archive roll. A run that only
    /// prints what the settings are should not cost the player one of their twenty kept runs.
    /// </param>
    public static DiagnosticSettings Configure(string[]? args = null, string? installedDir = null,
                                               IReadOnlyList<ILogSink>? extraSinks = null,
                                               bool openLogFile = true)
    {
        var settings = new DiagnosticSettings();
        var complaints = new List<string>();
        sources.Clear();

        var installed = Path.Combine(installedDir ?? InstalledFolder, FileName);
        Read(installed, settings, SettingSource.InstalledFile, complaints);

        var user = UserSettingsPath;
        if (!string.Equals(user, installed, StringComparison.OrdinalIgnoreCase))
            Read(user, settings, SettingSource.UserFile, complaints);

        var env = EnvironmentSettings(complaints);
        if (env.Count > 0) { settings.Apply(env, SettingSource.Environment, complaints.Add); sources.Add("environment"); }

        // A file the caller named outranks both standard ones, and the rest of the command line
        // outranks it in turn — so --config sets a baseline that a one-off --log can still beat.
        if (args is not null && Value(args, "--config") is { } extra)
            Read(extra, settings, SettingSource.NamedFile, complaints, required: true);

        if (args is not null)
        {
            var pairs = CommandLine(args, complaints);
            if (pairs.Count > 0) { settings.Apply(pairs, SettingSource.CommandLine, complaints.Add); sources.Add("command line"); }
        }

        Install(settings, extraSinks, openLogFile);
        foreach (var c in complaints) For("Vitomir.Diagnostics").LogWarning("settings: {Complaint}", c);
        CatchCrashes();
        return Settings;
    }

    /// <summary>
    /// Installs the sinks for a set of settings, which is also how the tests drive this. Opens the
    /// log file unless the settings turn it off, archiving the previous run's first.
    /// </summary>
    public static EngineLoggerFactory Install(DiagnosticSettings settings, IReadOnlyList<ILogSink>? extraSinks = null,
                                              bool openLogFile = true)
    {
        factory?.Dispose();
        var complaints = new List<string>();
        var sinks = new List<ILogSink>();
        if (settings.Console) sinks.Add(new ConsoleLogSink());

        FileLogSink? file = null;
        Folder = null;
        Archived = null;
        if (settings.FileLevel != LogLevel.None && openLogFile)
        {
            string path;
            if (settings.File.Length > 0)
            {
                // A path the caller named: theirs to manage, so no folder and no archive.
                path = settings.File;
            }
            else
            {
                Folder = new LogFolder(settings.Folder.Length > 0
                    ? Path.GetFullPath(settings.Folder)
                    : Path.Combine(UserDataFolder, "Logs"));
                Archived = Folder.Roll(settings.Keep, complaints.Add);
                path = Path.Combine(Folder.Root, "vitomir" + LogFolder.Extension);
            }
            file = Folder is not null ? OpenIn(Folder.Root, Header(settings)) : new FileLogSink(path, Header(settings));
            sinks.Add(file);
        }

        if (extraSinks is not null) sinks.AddRange(extraSinks);
        FilePath = file?.Path;
        Settings = settings;
        factory = new EngineLoggerFactory(settings, [.. sinks]);

        var log = For("Vitomir.Diagnostics");
        foreach (var c in complaints) log.LogWarning("{Complaint}", c);
        if (file?.Error is { } why) log.LogError("cannot write the log file {Path}: {Why}", file.Path, why);
        else if (file is not null) log.LogInformation("logging to {Path}", file.Path);
        return factory;
    }

    /// <summary>
    /// The run's log in the logs folder: `vitomir.log`, or, when **a second game running at the same
    /// time** (a host and a player on one machine) finds that one held by the first, `vitomir-2.log`
    /// and on to 9, instead of logging nothing, as a run of two windows once did.
    /// </summary>
    public static FileLogSink OpenIn(string root, IEnumerable<string>? header = null)
    {
        for (int n = 1; n < 9; n++)
        {
            var path = Path.Combine(root, (n == 1 ? "vitomir" : $"vitomir-{n}") + LogFolder.Extension);
            if (File.Exists(path) && LogFolder.InUse(path)) continue;
            var file = new FileLogSink(path, header);
            if (file.Error is null || !File.Exists(path)) return file;
            file.Dispose();
        }
        return new FileLogSink(Path.Combine(root, "vitomir-9" + LogFolder.Extension), header);
    }

    /// <summary>
    /// A log line of the Fixes and QoL in force, for a change after the header was written: a later
    /// run of the session, the Fixes page.
    /// </summary>
    public static void InForce(string why)
    {
        For("Vitomir").LogInformation("fixes: {Fixes}; quality of life: {Qol} ({Why})", World.Fixes.Current, World.Qol.Current, why);
        For("Vitomir").LogInformation("features: {Features} ({Why})", FeaturesInForce, why);
    }

    /// <summary>
    /// The registry's line of what is in force: the profile, the features on that make a
    /// game modded by name, and how many repairs every game has — <c>faithful rules; 10 repairs always on</c>
    /// for a plain run, <c>modded rules (fixes.shapetorch); 10 repairs always on</c> with one gameplay Fix.
    /// </summary>
    public static string FeaturesInForce => Features.GameFeatures.Registry.Line(World.Fixes.Current, World.Qol.Current);

    /// <summary>The comment lines every log file opens with, so an archived one explains itself.</summary>
    static IEnumerable<string> Header(DiagnosticSettings settings)
    {
        yield return $"{Net.NetIdentity.ProgramName} on {System.Environment.OSVersion} ({RuntimeInformation.FrameworkDescription})";
        yield return $"settings: {settings}";
        // What is not 1.1 in this run, so a play-test's log says it: a `--fix shapetorch`
        // run left nothing in the log to show the fix was on. Read when the file opens, so the
        // program configures the switches before the diagnostics; a later run of the same session
        // (a load from the menus) or a change on the Fixes page logs a line of its own.
        yield return $"fixes: {World.Fixes.Current}";
        yield return $"quality of life: {World.Qol.Current}";
        yield return $"features: {FeaturesInForce}";
        // The launch's random seed, so a run can be repeated with `--seed N`.
        if (Ai.Chance.Game is { } chance)
        {
            yield return $"random seed: {chance.Seed}";
            if (chance.AiSeed is { } ai) yield return $"ai seed: {ai}";
        }
        yield return "lines are: level  subsystem  message";
    }

    /// <summary>The managed logs folder this run is using, or null when the caller named a file.</summary>
    public static LogFolder? Folder { get; private set; }
    /// <summary>The zip the previous run's logs were archived into, when there was one.</summary>
    public static string? Archived { get; private set; }

    static bool catching;

    /// <summary>
    /// The reason the log file exists at all: whatever kills the process gets written down first.
    /// The file sink flushes every line, so what is on disk when this runs is already complete.
    /// </summary>
    static void CatchCrashes()
    {
        if (catching) return;
        catching = true;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            For("Vitomir").LogCritical(e.ExceptionObject as Exception, "the engine stopped on an unhandled error");
            // Then the program's own word (a game in a window's message box), the log written first.
            try { Crashed?.Invoke(e.ExceptionObject as Exception); }
            catch (Exception) { }
        };
    }

    /// <summary>Told of an unhandled error after the log has it (a game in a window shows a message box naming the log).</summary>
    public static Action<Exception?>? Crashed { get; set; }

    /// <summary>The log file being written, or null (a failure's message box names it).</summary>
    public static string? FilePath { get; private set; }

    /// <summary>
    /// **A line a game in a window printed**: such a game has no console, so its plain lines go into the log file
    /// (the category <c>Console</c>); the console's own echo of a log line is not given to it (<see cref="ConsoleLogSink.Writing"/>,
    /// the program's <c>ConsoleLines.AlreadyLogged</c>). Answers false while there is no log file to take it (the line waits).
    /// </summary>
    public static bool Plain(string line) => factory is { } f && f.WritePlain("Vitomir.Console", line);

    /// <summary>Silences everything and drops the sinks. Tests and library callers start here.</summary>
    public static void Reset()
    {
        factory?.Dispose();
        factory = null;
        Settings = new DiagnosticSettings();
        sources.Clear();
    }

    public const string FileName = "vitomir.ini";

    /// <summary>
    /// Where the engine keeps what belongs to the player rather than to the install: their settings,
    /// their logs, and later their saves. <c>%LOCALAPPDATA%\Vitomir</c> on Windows and the
    /// equivalent elsewhere, with fallbacks — the folder lookup can come back empty on a stripped
    /// down machine, and a relative path there would drop a Logs folder into whatever directory the
    /// game happened to be started from. The folder's name is the build's own
    /// (<see cref="BuildFlavour.UserFolderName"/>), and
    /// <c>VITOMIR_USERDATA</c> names another outright (<see cref="BuildFlavour.UserDataVariable"/>).
    /// </summary>
    public static string UserDataFolder
    {
        get
        {
            // A host's own folder for the game (the Classic module under the platform's program), else the build's.
            if (HostFolders.User is { } hosted) return hosted;
            if (BuildFlavour.UserDataOverride is { } chosen) return chosen;
            foreach (var folder in new[] { System.Environment.SpecialFolder.LocalApplicationData,
                                           System.Environment.SpecialFolder.ApplicationData,
                                           System.Environment.SpecialFolder.UserProfile })
            {
                var path = System.Environment.GetFolderPath(folder);
                if (path.Length > 0 && Path.IsPathRooted(path)) return Path.Combine(path, BuildFlavour.UserFolderName);
            }
            return Path.Combine(Path.GetTempPath(), BuildFlavour.UserFolderName);
        }
    }

    /// <summary>
    /// The folder the installed <c>vitomir.ini</c> layer is read from: the program's own (<see cref="AppContext.BaseDirectory"/>),
    /// or the folder a host gave (<see cref="HostFolders.Installed"/>: the Classic module's own, read only).
    /// </summary>
    public static string InstalledFolder => HostFolders.Installed ?? AppContext.BaseDirectory;

    /// <summary>The player's own settings file, beside their logs rather than in the install.</summary>
    public static string UserSettingsPath => Path.Combine(UserDataFolder, FileName);

    static void Read(string path, DiagnosticSettings settings, SettingSource source, List<string> complaints,
                     bool required = false)
    {
        var ini = IniFile.Read(path);
        if (ini.Count == 0)
        {
            if (required) complaints.Add($"no settings read from {path}");
            return;
        }
        var pairs = ini.Section(DiagnosticSettings.Section).ToList();
        if (pairs.Count == 0) return;
        settings.Apply(pairs, source, complaints.Add);
        sources.Add(path);
    }

    /// <summary>
    /// <c>VITOMIR_DEBUG</c>, <c>VITOMIR_LOG</c> (a level, or <c>Category=Level</c> pairs separated by
    /// semicolons) and <c>VITOMIR_LOG_FILE</c>. Handy in CI, where there is no command line to edit.
    /// </summary>
    static List<KeyValuePair<string, string>> EnvironmentSettings(List<string> complaints)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        if (System.Environment.GetEnvironmentVariable("VITOMIR_DEBUG") is { Length: > 0 } debug)
            pairs.Add(new("debug", debug));
        if (System.Environment.GetEnvironmentVariable("VITOMIR_LOG") is { Length: > 0 } log)
            foreach (var part in log.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                pairs.Add(Spec(part, complaints));
        if (System.Environment.GetEnvironmentVariable("VITOMIR_LOG_FILE") is { Length: > 0 } file)
            pairs.Add(new("file", file));
        return pairs;
    }

    /// <summary>Just the command line's contribution, which is what the tests check.</summary>
    public static DiagnosticSettings FromCommandLine(string[] args, List<string>? complaints = null)
    {
        complaints ??= [];
        var settings = new DiagnosticSettings();
        settings.Apply(CommandLine(args, complaints), SettingSource.CommandLine, complaints.Add);
        return settings;
    }

    /// <summary>
    /// <c>--debug</c>, <c>--log &lt;level&gt;</c> or <c>--log &lt;Category&gt;=&lt;Level&gt;</c>
    /// (repeatable), <c>--log-file &lt;path&gt;</c>, <c>--log-folder &lt;dir&gt;</c>,
    /// <c>--log-keep &lt;n&gt;</c>, <c>--no-log-file</c> and <c>--no-log-console</c>.
    /// </summary>
    static List<KeyValuePair<string, string>> CommandLine(string[] args, List<string> complaints)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--debug":
                    // Developer mode also loosens the log, because a run with --debug and nothing
                    // to show would be a confusing thing to hand someone.
                    pairs.Add(new("debug", "true"));
                    pairs.Add(new("level", "Debug"));
                    break;
                case "--log" when i + 1 < args.Length: pairs.Add(Spec(args[++i], complaints)); break;
                case "--log-file" when i + 1 < args.Length: pairs.Add(new("file", args[++i])); break;
                case "--log-folder" when i + 1 < args.Length: pairs.Add(new("folder", args[++i])); break;
                case "--log-keep" when i + 1 < args.Length: pairs.Add(new("keep", args[++i])); break;
                case "--no-log-file": pairs.Add(new("file.level", "off")); break;
                case "--no-log-console": pairs.Add(new("console", "false")); break;
            }
        }
        return pairs;
    }

    /// <summary>A bare level sets the default; <c>Category=Level</c> sets one category.</summary>
    static KeyValuePair<string, string> Spec(string text, List<string> complaints)
    {
        int eq = text.IndexOf('=');
        if (eq < 0) return new("level", text);
        var category = text[..eq].Trim();
        if (category.Length == 0) complaints.Add($"'{text}' names no category");
        return new("level." + category, text[(eq + 1)..]);
    }

    static string? Value(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>What the settings came out as and where each part came from, for <c>--diagnostics</c>.</summary>
    public static string Explain()
    {
        var lines = new List<string> { $"diagnostics: {Settings}" };
        if (sources.Count > 0) lines.Add("  read from: " + string.Join(", then ", sources));
        foreach (var (key, source) in Settings.Provenance.OrderBy(p => p.Key, StringComparer.Ordinal))
            lines.Add($"  {key} set by {source}");
        lines.Add($"  version {Net.NetIdentity.ProgramName}, " +
                  $"settings would be read from {UserSettingsPath}");
        var folder = Folder ?? (Settings.File.Length > 0 ? null
            : new LogFolder(Settings.Folder.Length > 0 ? Path.GetFullPath(Settings.Folder)
                                                      : Path.Combine(UserDataFolder, "Logs")));
        if (folder is not null)
        {
            lines.Add($"  logs in {folder.Root}");
            var archives = folder.Archives().ToList();
            lines.Add($"  {archives.Count} archived run{(archives.Count == 1 ? "" : "s")} in {folder.Archive}" +
                      (archives.Count > 0 ? $", newest {Path.GetFileName(archives[0])}" : ""));
        }
        if (Archived is { } zip) lines.Add($"  the previous run was archived to {Path.GetFileName(zip)}");
        return string.Join(System.Environment.NewLine, lines);
    }
}
