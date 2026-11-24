// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
//
// vitomir-tools — the developer's tools.
using Vitomir.Platform.Render.Windowing;

namespace Vitomir.Tools;

/// <summary>
/// **<c>vitomir-tools</c>**: the tool commands of the old program, moved as they were (the same switch names, the same
/// output), run through the same code: a command line that names a tool (<see cref="Commands"/>) is the process
/// <see cref="ExeProgram"/> ran for the old program, with the same tools (<see cref="ExeTools"/>) and the same SDL host, so a report that needs the install, a save or a loaded place reaches it the way it always
/// did. A command line that names none is not a game here: the commands are listed and the exit code is 2 (0 for <c>--help</c>).
/// <c>--replay-folder</c>'s tapes are games, so they are played by the platform's program (<c>vitomir-platform --game
/// classic --developer</c>, beside this program or in its source tree), or <c>--replay-program &lt;path&gt;</c>.
/// </summary>
static class ToolsProgram
{
    /// <summary>
    /// **The switches that name a tool**, each with what it does: the commands that run before
    /// the install is looked for, with the install's files, the reports before and after a place loads, and the replay folder.
    /// <c>--level-l2</c>, <c>--l2</c> and <c>--place</c> are the level viewers' (a cooked level played or stilled).
    /// </summary>
    public static readonly (string Switch, string What)[] Commands =
    [
        ("--install-data", "[<1.1 folder>] [--install-to <folder>]  Vitomir's own copy of the game's files, laid out from the 1.1 install"),
        ("--verify-data", "[--install-to <folder>]  every file of the game folder against its manifest's hash"),
        ("--make-setup-manifest", "[<install>] [--lists <sha> <sizes>] [--same-as <sha> <sizes>] --out <file>  the setup's manifest of 1.1's files"),
        ("--recook", "<out> [--game <folder>]  the original's templates, scenes and camera paths recooked as text, its text data files laid out (what the import makes)"),
        ("--template", "<name> [--templates darkAges] [--game <folder>] [--recooked <folder>]  a template with every value it inherits, and where from"),
        ("--format", "<file|folder> [--check]  the original's text data files laid out as tables, each proved to read as it did"),
        ("--export-gltf", "<LEVEL|ALL> <out> [--lightmap-materials] [--scale m]  a 1.1 level as glTF and its walk polygons"),
        ("--convert-l2", "<LEVEL|ALL> <out> [--no-anchors]  a 1.1 level as a level package"),
        ("--cook-l2", "<scene.glb> [--layout layout.json] <out.l2> [...]  a glTF scene cooked into a cell level"),
        ("--relayout-l2", "<package.l2> --layout <layout.json> [--out <new.l2>]  a cooked level's layout read again, its cells kept"),
        ("--probes-at", "<package.l2> --points \"x,y,z;...\" [--normal x,y,z]  a cooked level's light probes at these points"),
        ("--level-l2", "<package.l2> [game switches]  a level package played or stilled (--l2 <folder> --level <name>)"),
        ("--l2", "<folder>  the levels that have a package in this folder played from it"),
        ("--place", "<Place>=<package.l2>  a chronicle place played as a cell level"),
        ("--stream-fly", "<package.l2> --out <folder> --path x,y,z,yaw,pitch;...  a streamed level flown through, no window"),
        ("--view-gltf", "<file.gltf|glb> [--pos --yaw --pitch --look --headless out.png]  a glTF scene through the modern renderer"),
        ("--ui-art-list", " the panes that have art, and their sizes"),
        ("--ui-art-sheet", "<pane|all> [--ui-art-out <folder>] [--ui-art-scale 6]  a pane's art as one picture and an outline"),
        ("--ui-art-cut", "<pane> --ui-art-from <picture> [--ui-art-out <folder>]  a painted picture cut into the pane's files"),
        ("--ui-art-one", "<texture> --ui-art-from <picture> [--ui-art-out <folder>]  one texture from a painted picture"),
        ("--sweep", "<folder> [--compare <earlier>] [--size WxH]  every level rendered, no window, with sweep.csv"),
        ("--story-sweep", "<folder> [--story-sweep-only <names>] [--story-seconds s] [--story-buff [n]]  every place run a minute with its scripts"),
        ("--list-saves", " the saves in the player's save folder"),
        ("--chronicles", "[--verbose]  the campaign's chronicles and exits"),
        ("--saves", "[dir] [--verbose]  1.1's save games read and reported"),
        ("--gait", "[--verbose]  the walk and run animation rates"),
        ("--things", "--level <name>  every thing in the level"),
        ("--scripts-all", "--level <name>  every script call the run makes"),
        ("--storm", "<seconds>  the Tower of London's lightning measured"),
        ("--sky-groups", "--level <name>  the sky model's groups"),
        ("--modes", "--level <name>  which draw mode every surface ends in"),
        ("--find", "<material> --level <name>  where a material is used"),
        ("--replay-folder", "<folder> [--rebase] [--replay-jobs N] [--replay-program <program>]  every tape of a folder checked by vitomir-platform"),
    ];

    /// <summary>Whether <paramref name="args"/> names a tool.</summary>
    public static bool Named(IReadOnlyList<string> args) => args.Any(a => Commands.Any(c => c.Switch == a));

    /// <summary>The list of the commands, as <c>--help</c> prints it.</summary>
    public static string Usage =>
        "usage: vitomir-tools <command> [switches]\n" +
        string.Join("\n", Commands.Select(c => $"  {c.Switch} {c.What}"));

    static int Main(string[] args)
    {
        bool help = args.Contains("--help") || args.Contains("-h");
        if (help || !Named(args))
        {
            TextConsole.Open(TextConsole.Wanted.Terminal);
            if (help) { Console.WriteLine(Usage); return 0; }
            Console.Error.WriteLine(args.Length == 0 ? "vitomir-tools: name a command" : "vitomir-tools: no command named: " + string.Join(" ", args));
            Console.Error.WriteLine(Usage);
            return 2;
        }
        if (args.Contains("--replay-folder"))
        {
            // A tape is a game: the platform's program plays it (beside this program or in its source tree), or the
            // program named.
            int at = Array.IndexOf(args, "--replay-program");
            string? named = at >= 0 && at + 1 < args.Length ? Path.GetFullPath(args[at + 1]) : null;
            string? player = named ?? ReplayFolder.FindPlatform();
            if (player is null || !File.Exists(player))
            {
                TextConsole.Open(TextConsole.Wanted.Terminal);
                Console.Error.WriteLine(player is null
                    ? $"replay-folder: the tapes are played by vitomir-platform, and it is neither beside vitomir-tools ({AppContext.BaseDirectory}) nor built in its source tree: name it with --replay-program <path>"
                    : $"replay-folder: {player} is not there");
                return 2;
            }
            ReplayFolder.Player = player;
        }
        return ExeProgram.Run(args, TextRuns.WantedFor(args, platform: false), new ExeTools());
    }
}
