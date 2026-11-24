// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

public class IniFileTests
{
    const string Sample = """
        ; a comment
        # and another
        stray = before any section

        [diagnostics]
        level = Information
        level.Vitomir.Render = Trace
        debug = yes
        file = C:\logs\engine.log

        [display]
        size = 1280x720
        note = a value with = in it
        level = Information
        """;

    [Fact]
    public void Keys_are_read_under_their_section()
    {
        var ini = IniFile.Parse(Sample);
        Assert.Equal("Information", ini["diagnostics.level"]);
        Assert.Equal("1280x720", ini["display.size"]);
        Assert.Equal("before any section", ini["stray"]);
        Assert.Null(ini["diagnostics.nothing"]);
    }

    [Fact]
    public void A_value_may_contain_an_equals_sign_and_a_windows_path()
    {
        var ini = IniFile.Parse(Sample);
        Assert.Equal("a value with = in it", ini["display.note"]);
        Assert.Equal(@"C:\logs\engine.log", ini["diagnostics.file"]);
    }

    [Fact]
    public void The_same_key_in_two_sections_stays_two_keys()
    {
        var ini = IniFile.Parse(Sample);
        Assert.Equal(["level", "level.Vitomir.Render", "debug", "file"],
                     ini.Section("diagnostics").Select(kv => kv.Key));
        Assert.Equal("Information", ini["display.level"]);
    }

    [Fact]
    public void Lookup_is_case_insensitive_as_a_hand_edited_file_deserves()
    {
        var ini = IniFile.Parse(Sample);
        Assert.Equal("Information", ini["Diagnostics.Level"]);
        Assert.True(IniFile.IsTrue(ini["DIAGNOSTICS.DEBUG"]));
    }

    [Theory]
    [InlineData("1")] [InlineData("true")] [InlineData("TRUE")] [InlineData("yes")] [InlineData("on")]
    public void True_is_spelled_several_ways(string text) => Assert.True(IniFile.IsTrue(text));

    [Theory]
    [InlineData("0")] [InlineData("false")] [InlineData("")] [InlineData(null)] [InlineData("maybe")]
    public void Everything_else_is_false(string? text) => Assert.False(IniFile.IsTrue(text));
}

public class DiagnosticSettingsTests
{
    [Fact]
    public void The_default_is_quiet_and_has_no_developer_features()
    {
        var s = new DiagnosticSettings();
        Assert.Equal(LogLevel.Warning, s.Level);
        Assert.False(s.Debug);
        Assert.Equal("", s.File);
        Assert.True(s.Console);
    }

    [Fact]
    public void A_category_level_covers_that_category_and_everything_under_it()
    {
        var s = new DiagnosticSettings();
        s.Categories["Vitomir.World"] = LogLevel.Trace;
        Assert.Equal(LogLevel.Trace, s.LevelFor("Vitomir.World"));
        Assert.Equal(LogLevel.Trace, s.LevelFor("Vitomir.World.Chronicle"));
        // …and stops at a dot, so a longer name that merely starts the same way is not caught.
        Assert.Equal(LogLevel.Warning, s.LevelFor("Vitomir.Worldly"));
        Assert.Equal(LogLevel.Warning, s.LevelFor("Vitomir.Render"));
    }

    [Fact]
    public void The_longest_matching_category_wins()
    {
        var s = new DiagnosticSettings { Level = LogLevel.Error };
        s.Categories["*"] = LogLevel.Information;
        s.Categories["Vitomir"] = LogLevel.Warning;
        s.Categories["Vitomir.Scripting"] = LogLevel.Debug;
        Assert.Equal(LogLevel.Debug, s.LevelFor("Vitomir.Scripting"));
        Assert.Equal(LogLevel.Warning, s.LevelFor("Vitomir.Render"));
        Assert.Equal(LogLevel.Information, s.LevelFor("SomethingElse"));
    }

    [Fact]
    public void A_later_source_overrides_an_earlier_one_and_says_so()
    {
        var s = new DiagnosticSettings();
        s.Apply([new("level", "Information"), new("file", "a.log")], SettingSource.InstalledFile);
        s.Apply([new("level", "Trace")], SettingSource.CommandLine);
        Assert.Equal(LogLevel.Trace, s.Level);
        Assert.Equal("a.log", s.File);
        Assert.Equal(SettingSource.CommandLine, s.Provenance["level"]);
        Assert.Equal(SettingSource.InstalledFile, s.Provenance["file"]);
    }

    [Fact]
    public void A_setting_that_makes_no_sense_is_reported_and_changes_nothing()
    {
        var s = new DiagnosticSettings();
        var complaints = new List<string>();
        s.Apply([new("level", "Loud"), new("wibble", "3")], SettingSource.UserFile, complaints.Add);
        Assert.Equal(LogLevel.Warning, s.Level);
        Assert.Equal(2, complaints.Count);
        Assert.Contains(complaints, c => c.Contains("Loud"));
        Assert.Contains(complaints, c => c.Contains("wibble"));
    }

    [Theory]
    [InlineData("Trace", LogLevel.Trace)]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("INFORMATION", LogLevel.Information)]
    [InlineData("info", LogLevel.Information)]
    [InlineData("warn", LogLevel.Warning)]
    [InlineData("off", LogLevel.None)]
    [InlineData("Critical", LogLevel.Critical)]
    public void Levels_are_Microsofts_with_a_few_shorthands(string text, LogLevel expected)
    {
        Assert.True(DiagnosticSettings.TryLevel(text, out var level));
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData("verbose")] [InlineData("7")] [InlineData("")]
    public void Anything_else_is_not_a_level(string text) =>
        Assert.False(DiagnosticSettings.TryLevel(text, out _));
}

public class CommandLineDiagnosticsTests
{
    [Fact]
    public void Nothing_on_the_command_line_leaves_the_defaults_alone()
    {
        var s = Diag.FromCommandLine(["--level", "P1_HAVN", "--size", "800x600"]);
        Assert.Equal(LogLevel.Warning, s.Level);
        Assert.False(s.Debug);
    }

    [Fact]
    public void Debug_turns_on_the_developer_features_and_opens_the_log()
    {
        var s = Diag.FromCommandLine(["--debug"]);
        Assert.True(s.Debug);
        Assert.Equal(LogLevel.Debug, s.Level);
    }

    [Fact]
    public void A_level_after_debug_still_wins_because_it_comes_later()
    {
        var s = Diag.FromCommandLine(["--debug", "--log", "Warning"]);
        Assert.True(s.Debug);
        Assert.Equal(LogLevel.Warning, s.Level);
    }

    [Fact]
    public void Log_takes_a_level_or_a_category_equals_level_and_repeats()
    {
        var s = Diag.FromCommandLine(["--log", "Error", "--log", "Vitomir.Scripting=Trace",
                                      "--log", "Vitomir.World=Debug", "--log-file", "run.log",
                                      "--no-log-console"]);
        Assert.Equal(LogLevel.Error, s.Level);
        Assert.Equal(LogLevel.Trace, s.LevelFor("Vitomir.Scripting"));
        Assert.Equal(LogLevel.Debug, s.LevelFor("Vitomir.World"));
        Assert.Equal("run.log", s.File);
        Assert.False(s.Console);
    }

    [Fact]
    public void A_type_logs_under_its_subsystem()
    {
        Assert.Equal("Vitomir.World", Diag.Category(typeof(Location)));
        Assert.Equal("Vitomir.World", Diag.Category(typeof(Chronicle)));
        Assert.Equal("Vitomir.Vfs", Diag.Category(typeof(Vfs.GameFiles)));
    }
}

public class LoggerTests
{
    /// <summary>A factory with one sink on the console's threshold, which is what these check.</summary>
    static (EngineLoggerFactory Factory, MemoryLogSink Sink) Make(DiagnosticSettings settings)
    {
        var sink = new MemoryLogSink(SinkChannel.Console);
        return (new EngineLoggerFactory(settings, sink), sink);
    }

    [Fact]
    public void At_the_default_level_only_warnings_and_worse_are_written()
    {
        var (factory, sink) = Make(new DiagnosticSettings());
        var log = factory.CreateLogger("Vitomir.World");
        log.LogTrace("t"); log.LogDebug("d"); log.LogInformation("i");
        log.LogWarning("w"); log.LogError("e"); log.LogCritical("c");
        Assert.Equal(["w", "e", "c"], sink.Lines.Select(l => l.Message));
    }

    [Fact]
    public void A_level_that_is_off_never_formats_its_message()
    {
        // With no log file there is nothing more verbose than the console asking for the line.
        var (factory, _) = Make(new DiagnosticSettings { FileLevel = LogLevel.None });
        var log = factory.CreateLogger("Vitomir.Render");
        Assert.False(log.IsEnabled(LogLevel.Debug));
        Assert.True(log.IsEnabled(LogLevel.Warning));
        // The guard is what keeps a per-frame log line free when nobody is listening.
        int formatted = 0;
        if (log.IsEnabled(LogLevel.Trace)) formatted++;
        Assert.Equal(0, formatted);
    }

    [Fact]
    public void One_subsystem_can_be_turned_up_without_the_rest()
    {
        var settings = new DiagnosticSettings();
        settings.Categories["Vitomir.Scripting"] = LogLevel.Trace;
        var (factory, sink) = Make(settings);
        factory.CreateLogger("Vitomir.Scripting").LogTrace("script detail");
        factory.CreateLogger("Vitomir.Render").LogTrace("render detail");
        Assert.Equal(["script detail"], sink.Lines.Select(l => l.Message));
    }

    [Fact]
    public void None_silences_a_category_completely()
    {
        var settings = new DiagnosticSettings { FileLevel = LogLevel.None };
        settings.Categories["Vitomir.Scripting"] = LogLevel.None;
        var (factory, sink) = Make(settings);
        var log = factory.CreateLogger("Vitomir.Scripting");
        log.LogCritical("the sky is falling");
        Assert.False(log.IsEnabled(LogLevel.Critical));
        Assert.Empty(sink.Lines);
    }

    [Fact]
    public void A_line_names_its_level_and_its_subsystem()
    {
        Assert.Equal("warn   World  a chronicle names a scene that is not installed",
                     ConsoleLogSink.Format(LogLevel.Warning, "Vitomir.World",
                                           "a chronicle names a scene that is not installed", null));
        Assert.Contains("ERROR", ConsoleLogSink.Format(LogLevel.Error, "Vitomir.Render", "x", null));
        Assert.Contains("InvalidOperationException: boom",
                        ConsoleLogSink.Format(LogLevel.Error, "Vitomir.Render", "x", new InvalidOperationException("boom")));
    }

    [Fact]
    public void A_log_file_the_engine_cannot_open_is_reported_not_thrown()
    {
        using var sink = new FileLogSink(Path.Combine(Path.GetTempPath(), "no-such-dir\0bad", "x.log"));
        Assert.NotNull(sink.Error);
        sink.Write(LogLevel.Error, "Vitomir.World", "still fine", null);   // and writing is still safe
    }

    [Fact]
    public void A_log_file_is_written_when_one_is_asked_for()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-log-{Guid.NewGuid():N}.log");
        try
        {
            using (var sink = new FileLogSink(path))
            {
                Assert.Null(sink.Error);
                var factory = new EngineLoggerFactory(new DiagnosticSettings(), sink);
                factory.CreateLogger("Vitomir.World").LogWarning("something odd");
                factory.CreateLogger("Vitomir.World").LogDebug("not this");
            }
            var text = File.ReadAllText(path);
            Assert.Contains("something odd", text);
            Assert.DoesNotContain("not this", text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    /// <summary>
    /// The logger is a global and the thing that replaces it may do so while something else is
    /// part-way through a line — another test class on another thread, or a worker still logging
    /// while the game shuts down. It showed up as a passing suite failing in the *logger*:
    /// `ObjectDisposedException: Cannot access a closed Stream`, thrown out of a `LogInformation`
    /// in `Location.Load` because a different test had just disposed the sink underneath it.
    ///
    /// Two things had to be true and neither was: disposal takes the same lock as writing, and a
    /// sink that cannot write goes quiet instead of throwing.
    ///
    /// This one is the belt and braces — it races four writers against a disposal and asserts that
    /// nothing throws, which by its nature only sometimes hit the window on the old code.
    /// <see cref="A_file_sink_that_cannot_write_stops_writing"/> is the deterministic half, and it
    /// fails on the old code every time with the exception this was reported as.
    /// </summary>
    [Fact]
    public async Task Disposing_the_logger_while_something_is_writing_to_it_is_not_a_crash()
    {
        var stop = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-log-race-{Guid.NewGuid():N}.log");
        try
        {
            var settings = new DiagnosticSettings { Debug = true };
            var factory = new EngineLoggerFactory(settings, new FileLogSink(path), new MemoryLogSink());
            var logger = factory.CreateLogger("race");

            using var go = new ManualResetEventSlim();
            var writers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                go.Wait(stop);
                for (int i = 0; i < 2000; i++) logger.LogInformation("line {I}", i);
            }, stop)).ToArray();

            var closer = Task.Run(async () =>
            {
                go.Wait(stop);
                await Task.Delay(2, stop);
                factory.Dispose();
            }, stop);
            go.Set();

            // The point of the test: none of these throws.
            await Task.WhenAll([.. writers, closer]);
            logger.LogInformation("and one more after it is gone");
            factory.Dispose();                                  // twice is not a crash either
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    /// <summary>A sink whose file has been pulled out from under it goes quiet, not bang.</summary>
    [Fact]
    public void A_file_sink_that_cannot_write_stops_writing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-log-shut-{Guid.NewGuid():N}.log");
        try
        {
            var sink = new FileLogSink(path);
            Assert.Null(sink.Error);
            sink.Write(LogLevel.Information, "here", "before", null);
            sink.Dispose();
            sink.Write(LogLevel.Information, "here", "after", null);          // no throw
            sink.Write(LogLevel.Error, "here", "with an error", new InvalidOperationException());
            Assert.Contains("before", File.ReadAllText(path));
            Assert.DoesNotContain("after", File.ReadAllText(path));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

}

public class LogFolderTests
{
    static string Temp() => Path.Combine(Path.GetTempPath(), "vitomir-logs-" + Guid.NewGuid().ToString("N")[..8]);

    static void WriteRun(string root, string launched, string name = "vitomir.log")
    {
        Directory.CreateDirectory(root);
        File.WriteAllLines(Path.Combine(root, name),
            [$"# {FileLogSink.LaunchedAt} {launched}", "info   World  loaded something"]);
    }

    [Fact]
    public void The_previous_runs_logs_are_zipped_under_the_time_that_run_started()
    {
        var root = Temp();
        try
        {
            WriteRun(root, "2026-09-12 18:23:16");
            var folder = new LogFolder(root);
            var zip = folder.Roll(keep: 20);

            Assert.NotNull(zip);
            Assert.Equal("Vitomir_2026-09-12_182316.zip", Path.GetFileName(zip));
            // The plain files are gone; the run is in the archive, not duplicated beside it.
            Assert.Empty(Directory.GetFiles(root, "*.log"));
            using var archive = System.IO.Compression.ZipFile.OpenRead(zip!);
            Assert.Equal(["vitomir.log"], archive.Entries.Select(e => e.Name));
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>
    /// Two games on one machine (a host and a player): the second finds the first's
    /// `vitomir.log` open, leaves it alone rather than archiving a half-written copy, and writes
    /// `vitomir-2.log` of its own.
    /// </summary>
    [Fact]
    public void A_log_another_game_is_writing_is_left_alone_and_the_second_game_writes_its_own()
    {
        var root = Temp();
        try
        {
            var first = new FileLogSink(Path.Combine(root, "vitomir.log"));
            try
            {
                Assert.Null(first.Error);
                Assert.Null(new LogFolder(root).Roll(keep: 20));
                Assert.True(File.Exists(Path.Combine(root, "vitomir.log")));

                using var second = Diag.OpenIn(root);
                Assert.Null(second.Error);
                Assert.Equal("vitomir-2.log", Path.GetFileName(second.Path));
            }
            finally { first.Dispose(); }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Several_log_files_from_one_run_go_into_one_zip()
    {
        var root = Temp();
        try
        {
            WriteRun(root, "2026-09-12 09:00:00");
            WriteRun(root, "2026-09-12 09:00:00", "scripts.log");
            var zip = new LogFolder(root).Roll(keep: 5);
            using var archive = System.IO.Compression.ZipFile.OpenRead(zip!);
            Assert.Equal(["scripts.log", "vitomir.log"], archive.Entries.Select(e => e.Name).Order());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Only_the_newest_runs_are_kept()
    {
        var root = Temp();
        try
        {
            var folder = new LogFolder(root);
            for (int i = 1; i <= 5; i++)
            {
                WriteRun(root, $"2026-09-1{i} 10:00:00");
                var zip = folder.Roll(keep: 50);                  // archive everything for now
                File.SetLastWriteTimeUtc(zip!, new DateTime(2026, 9, 10 + i, 10, 0, 0, DateTimeKind.Utc));
            }
            Assert.Equal(5, folder.Archives().Count());

            folder.Prune(keep: 3);
            Assert.Equal(["Vitomir_2026-09-15_100000.zip", "Vitomir_2026-09-14_100000.zip",
                          "Vitomir_2026-09-13_100000.zip"],
                         folder.Archives().Select(Path.GetFileName));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Keeping_none_still_clears_the_folder_for_this_run()
    {
        var root = Temp();
        try
        {
            WriteRun(root, "2026-09-12 09:00:00");
            Assert.Null(new LogFolder(root).Roll(keep: 0));
            Assert.Empty(Directory.GetFiles(root, "*.log"));
            Assert.Empty(new LogFolder(root).Archives());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void A_first_run_has_nothing_to_archive()
    {
        var root = Temp();
        try
        {
            Assert.Null(new LogFolder(root).Roll(keep: 20));
            Assert.Empty(new LogFolder(root).Archives());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void The_user_data_folder_is_always_an_absolute_path()
    {
        // A relative one would drop a Logs folder into whatever directory the game was started from.
        Assert.True(Path.IsPathRooted(Diag.UserDataFolder), Diag.UserDataFolder);
        if (BuildFlavour.UserDataOverride == null) Assert.EndsWith(BuildFlavour.UserFolderName, Diag.UserDataFolder);
    }

    [Fact]
    public void The_players_data_is_kept_in_the_folder_named_Vitomir()
    {
        // The main build's saves, vitomir.ini and logs are in %LOCALAPPDATA%\Vitomir. A build that kept
        // its own in Vitomir-Levels once carried that name into the main build: its
        // first play found none of the saves and wrote its autosave and its window
        // settings among the other folder's. This is the test that would have said so.
        Assert.Equal("Vitomir", BuildFlavour.UserFolderName);
        Assert.Equal("", BuildFlavour.Name);
        if (BuildFlavour.UserDataOverride == null) Assert.Equal("Vitomir", Path.GetFileName(Diag.UserDataFolder));
        Assert.Equal(Path.Combine(Diag.UserDataFolder, Diag.FileName), Diag.UserSettingsPath);
    }

    [Fact]
    public void VITOMIR_USERDATA_names_the_player_data_folder_outright()
    {
        // An absolute folder wins; a relative or empty value is ignored (it would follow the working directory).
        string chosen = Path.Combine(Path.GetTempPath(), "vit-userdata");
        Assert.Equal(Path.GetFullPath(chosen), BuildFlavour.OverrideFrom(chosen));
        Assert.Null(BuildFlavour.OverrideFrom("relative"));
        Assert.Null(BuildFlavour.OverrideFrom(""));
        Assert.Null(BuildFlavour.OverrideFrom(null));
    }
}

public class FileAndConsoleLevelTests
{
    [Fact]
    public void The_file_keeps_the_story_even_when_the_console_is_quiet()
    {
        var s = new DiagnosticSettings();                       // console Warning, file Information
        Assert.Equal(LogLevel.Warning, s.LevelFor("Vitomir.World", SinkChannel.Console));
        Assert.Equal(LogLevel.Information, s.LevelFor("Vitomir.World", SinkChannel.File));
        Assert.Equal(LogLevel.Information, s.LowestLevelFor("Vitomir.World"));
    }

    [Fact]
    public void Turning_a_category_up_reaches_the_file_too()
    {
        var s = new DiagnosticSettings();
        s.Categories["Vitomir.Scripting"] = LogLevel.Trace;
        Assert.Equal(LogLevel.Trace, s.LevelFor("Vitomir.Scripting", SinkChannel.Console));
        Assert.Equal(LogLevel.Trace, s.LevelFor("Vitomir.Scripting", SinkChannel.File));
        Assert.Equal(LogLevel.Information, s.LevelFor("Vitomir.World", SinkChannel.File));
    }

    [Fact]
    public void Turning_the_console_down_never_silences_the_record()
    {
        var s = new DiagnosticSettings { Level = LogLevel.Critical };
        Assert.Equal(LogLevel.Critical, s.LevelFor("Vitomir.World", SinkChannel.Console));
        Assert.Equal(LogLevel.Information, s.LevelFor("Vitomir.World", SinkChannel.File));
    }

    [Fact]
    public void Off_is_the_only_thing_that_stops_the_file_being_written()
    {
        var s = new DiagnosticSettings { FileLevel = LogLevel.None };
        Assert.Equal(LogLevel.None, s.LevelFor("Vitomir.World", SinkChannel.File));
        Assert.Equal(LogLevel.Warning, s.LowestLevelFor("Vitomir.World"));
    }

    [Fact]
    public void Each_sink_filters_on_its_own_threshold()
    {
        var settings = new DiagnosticSettings();
        var console = new CountingSink(SinkChannel.Console);
        var file = new CountingSink(SinkChannel.File);
        var factory = new EngineLoggerFactory(settings, console, file);
        var log = factory.CreateLogger("Vitomir.World");
        log.LogInformation("a milestone");
        log.LogWarning("a problem");
        Assert.Equal(["a problem"], console.Lines);
        Assert.Equal(["a milestone", "a problem"], file.Lines);
    }

    [Fact]
    public void The_new_settings_are_read_from_the_file_and_the_command_line()
    {
        var ini = IniFile.Parse("""
            [diagnostics]
            file.level = Debug
            folder = D:\logs
            keep = 5
            level.Vitomir.Render = Trace
            """);
        var s = new DiagnosticSettings();
        var complaints = new List<string>();
        s.Apply(ini.Section("diagnostics"), SettingSource.UserFile, complaints.Add);
        Assert.Empty(complaints);
        Assert.Equal(LogLevel.Debug, s.FileLevel);
        Assert.Equal(@"D:\logs", s.Folder);
        Assert.Equal(5, s.Keep);
        Assert.Equal(LogLevel.Trace, s.LevelFor("Vitomir.Render"));

        var cli = Diag.FromCommandLine(["--log-keep", "3", "--no-log-file", "--log-folder", "here"]);
        Assert.Equal(3, cli.Keep);
        Assert.Equal(LogLevel.None, cli.FileLevel);
        Assert.Equal("here", cli.Folder);
    }

    sealed class CountingSink(SinkChannel channel) : ILogSink
    {
        public SinkChannel Channel { get; } = channel;
        public List<string> Lines { get; } = [];
        public void Write(LogLevel level, string category, string message, Exception? error) => Lines.Add(message);
        public void Dispose() { }
    }
}

public class ConfigureTests
{
    [Fact]
    public void Asking_what_the_settings_are_does_not_cost_a_kept_run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "vitomir-cfg-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            // One real run, then three that only report: the real run's log must still be there.
            Diag.Configure(["--log-folder", folder, "--no-log-console"]);
            Assert.True(File.Exists(Path.Combine(folder, "vitomir.log")));
            for (int i = 0; i < 3; i++)
                Diag.Configure(["--log-folder", folder, "--no-log-console"], openLogFile: false);

            Assert.True(File.Exists(Path.Combine(folder, "vitomir.log")));
            Assert.Empty(new LogFolder(folder).Archives());
            // …and it still reports the level a real run would write at, not the one it used.
            Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, Diag.Settings.FileLevel);
            Assert.Contains("logs in", Diag.Explain());
        }
        finally
        {
            Diag.Reset();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
