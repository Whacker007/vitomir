// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Text.RegularExpressions;
using Vitomir.Engine.Vfs;
using Vitomir.Tools;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **vitomir-tools**: the tools moved out of the old program as they were. The
/// program's list of commands names every command its tools answer (so none is left behind its gate), a command line that names
/// none is refused with the list, and the tools print what they did (the program built in its own project's folder,
/// <c>src/Vitomir.Tools/bin/&lt;config&gt;/&lt;tfm&gt;/</c>; skipped when it is not).
/// </summary>
public class ToolsProgramTests
{
    /// <summary>The source tree, found by walking up from the tests' folder (null outside one).</summary>
    static string? Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src", "Vitomir.slnx"))) return dir.FullName;
        return null;
    }

    /// <summary>The program in its project's build folder (<c>src/Vitomir.Tools/bin/&lt;config&gt;/&lt;tfm&gt;/vitomir-tools</c>), or null.</summary>
    static string? Tools() => ReplayBaselineTests.Tools();

    static (int Code, string Output) Run(string program, string userData, params string[] args)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) start.ArgumentList.Add(a);
        start.Environment["VITOMIR_USERDATA"] = userData;
        start.Environment["SDL_AUDIODRIVER"] = "dummy";
        using var p = Process.Start(start)!;
        var err = p.StandardError.ReadToEndAsync();
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output + err.Result);
    }

    /// <summary>
    /// **Every command the tools answer is behind vitomir-tools' gate**: each switch <c>ExeTools</c> tests for a command (the ones
    /// its <c>Flag(args, ...)</c> and <c>Arg(args, ...) is</c> lines and answers open with, and the switches its handlers take: the cook's, the
    /// convert's, the relayout's, the probes', the places'), <c>LevelReports</c>' reports and the replay folder.
    /// </summary>
    [Fact]
    public void The_program_names_every_command_its_tools_answer()
    {
        var root = Root();
        Assert.SkipWhen(root is null, "the source tree is not beside the tests");
        var named = ToolsProgram.Commands.Select(c => c.Switch).ToHashSet(StringComparer.Ordinal);
        string tools = File.ReadAllText(Path.Combine(root!, "src", "Vitomir.Tools", "ExeTools.cs"));
        string reports = File.ReadAllText(Path.Combine(root!, "src", "Vitomir.Tools", "LevelReports.cs"));
        // The switches a command opens with (not its modifiers: --install-to, --ui-art-out, --compare, --verbose and the like).
        var opens = Regex.Matches(tools, @"(?:if \(|=> )(?:Flag\(args, ""(--[a-z0-9-]+)""\)|Arg\(args, ""(--[a-z0-9-]+)""\) is)")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .Concat(Regex.Matches(reports, @"if \((?:Flag|Arg)\(""(--[a-z0-9-]+)""\)").Select(m => m.Groups[1].Value))
            .Concat(["--relayout-l2", "--probes-at", "--place", "--cook-l2", "--level-l2", "--convert-l2", "--l2", "--replay-folder"])
            .Distinct().ToList();
        Assert.True(opens.Count >= 29, $"only {opens.Count} commands found in ExeTools.cs and LevelReports.cs: the scan no longer reads them");
        var missing = opens.Where(o => !named.Contains(o)).ToList();
        Assert.True(missing.Count == 0, "answered by the tools but not named by vitomir-tools: " + string.Join(", ", missing));
        var extra = named.Where(n => !opens.Contains(n)).ToList();
        Assert.True(extra.Count == 0, "named by vitomir-tools but answered by no tool: " + string.Join(", ", extra));
    }

    /// <summary>
    /// **The platform's program knows the tools' switches by name**: it refuses each, naming vitomir-tools, and keeps its own copy
    /// of the list (<see cref="Vitomir.Platform.Modules.ToolSwitches"/>; it references nothing of vitomir-tools), which is the tools'
    /// own, in the same order.
    /// </summary>
    [Fact]
    public void The_platforms_list_of_the_tools_switches_is_the_tools_own()
    {
        Assert.Equal(ToolsProgram.Commands.Select(c => c.Switch), Vitomir.Platform.Modules.ToolSwitches.Names);
    }

    /// <summary>A command line that names no tool is not a game here: refused with the list (exit code 2); <c>--help</c> lists them (0).</summary>
    [Fact]
    public void A_line_that_names_no_tool_is_refused_with_the_list()
    {
        var tools = Tools();
        Assert.SkipWhen(tools is null, "vitomir-tools is not built beside the tests (build src/Vitomir.Tools/Vitomir.Tools.csproj in the same configuration)");
        var user = Directory.CreateTempSubdirectory("vitomir-tools-test-").FullName;
        try
        {
            var none = Run(tools!, user, "--level", "P1_HAVN", "--player");
            Assert.Equal(2, none.Code);
            Assert.Contains("vitomir-tools: no command named: --level P1_HAVN --player", none.Output, StringComparison.Ordinal);
            var help = Run(tools, user, "--help");
            Assert.Equal(0, help.Code);
            foreach (var (name, _) in ToolsProgram.Commands) Assert.Contains("  " + name + " ", help.Output, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(user));                 // nothing started: no log, no settings
        }
        finally { Directory.Delete(user, recursive: true); }
    }

    /// <summary>
    /// **A tool prints what it did**: a place refused before the install is looked for (<c>--place</c>, exit code 2), the saves of
    /// an empty save folder (<c>--list-saves</c>), and with the install the animation rates (<c>--gait --verbose</c>, 1,284 lines in
    /// the cloud) and a place's things (<c>--things</c>, which loads the level first). These once compared the old program's output
    /// with this one's, byte for byte; it is retired, so each is held to what it says.
    /// </summary>
    [Fact]
    public void A_tool_prints_what_it_did()
    {
        var tools = Tools();
        Assert.SkipWhen(tools is null, "vitomir-tools is not built beside the tests (build src/Vitomir.Tools/Vitomir.Tools.csproj in the same configuration)");
        var game = GameFiles.Locate();
        var lines = new List<(string[] Line, int Code, string Says)> { (["--place", "NoSuchPlace"], 2, "NoSuchPlace"), (["--list-saves"], 0, "") };
        if (game is not null)
        {
            lines.Add((["--gait", "--verbose", "--game", game], 0, "run"));
            lines.Add((["--things", "--level", "P1_HAVN", "--game", game], 0, "P1_HAVN"));
        }
        foreach (var (line, code, says) in lines)
        {
            var user = Directory.CreateTempSubdirectory("vitomir-tools-test-").FullName;
            try
            {
                var said = Run(tools!, user, [.. line, "--seed", "1", "--no-log-file"]);
                Assert.True(said.Code == code, $"{string.Join(" ", line)}: exit code {said.Code}\n{said.Output}");
                Assert.True(said.Output.Length > 0, string.Join(" ", line) + " printed nothing");
                Assert.Contains(says, said.Output, StringComparison.OrdinalIgnoreCase);
            }
            finally { Directory.Delete(user, recursive: true); }
        }
    }
}
