// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The tools' switches, which the platform's program refuses by name (they were once taken as developer switches and ignored).
namespace Vitomir.Platform.Modules;

/// <summary>
/// **The switches that name one of <c>vitomir-tools</c>' commands** (the tools moved out of
/// the old program into <c>vitomir-tools</c>): the platform's program plays games and runs no tool, so a
/// command line naming one is refused in one line that names <c>vitomir-tools</c>, with exit code 2, as an undeclared switch is
/// (<see cref="DeveloperSwitch.Refusal"/>) — and with <c>--developer</c> too, which until then took them as developer switches and
/// went on into the game. The platform never references <c>vitomir-tools</c> (<c>PlatformBoundaryTests</c>), so the names are
/// kept here, and <c>ToolsProgramTests</c> holds them equal to the tools' own list (<c>ToolsProgram.Commands</c>).
/// </summary>
public static class ToolSwitches
{
    /// <summary>The 33 switches, in the tools' own list's order.</summary>
    public static readonly string[] Names =
    [
        "--install-data", "--verify-data", "--make-setup-manifest", "--recook", "--template", "--format", "--export-gltf", "--convert-l2", "--cook-l2", "--relayout-l2", "--probes-at",
        "--level-l2", "--l2", "--place", "--stream-fly", "--view-gltf",
        "--ui-art-list", "--ui-art-sheet", "--ui-art-cut", "--ui-art-one",
        "--sweep", "--story-sweep",
        "--list-saves", "--chronicles", "--saves", "--gait",
        "--things", "--scripts-all", "--storm", "--sky-groups", "--modes", "--find",
        "--replay-folder",
    ];

    /// <summary>
    /// The switches the platform's own game (the enhanced one) reads itself under the same names (<c>--place</c>, the session's place;
    /// <c>--gait</c>, the snag check's), which are not refused there; under a game played by a module's loop every one is.
    /// </summary>
    public static readonly string[] AlsoThePlatforms = ["--place", "--gait"];

    /// <summary>The first tool switch in <paramref name="args"/>, or null; <paramref name="moduleLoop"/>: the game is played by a module's loop (the Classic game).</summary>
    public static string? First(IEnumerable<string> args, bool moduleLoop) =>
        args.FirstOrDefault(a => Array.IndexOf(Names, a) >= 0 && (moduleLoop || Array.IndexOf(AlsoThePlatforms, a) < 0));

    /// <summary>The one line a refused start says.</summary>
    public static string Refusal(string toolSwitch) =>
        $"refused: {toolSwitch} is a tool's switch, and tools are vitomir-tools' (vitomir-tools {toolSwitch} ...; vitomir-tools --help lists them "
        + "with what each does): vitomir-platform plays games and runs no tool";
}
