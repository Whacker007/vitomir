// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using System.Diagnostics;
using Vitomir.Engine.Levels.L2;
using Vitomir.Engine.Vfs;

namespace Vitomir;

/// <summary>
/// The L2 level format's two switches (ours):
///
/// - `vitomir --convert-l2 &lt;LEVEL|ALL&gt; &lt;out-folder&gt; [--no-anchors]` writes a 1.1 level as an
///   L2 package, <c>out-folder/LEVEL.l2/</c>. It only reads the game files.
/// - `vitomir --l2 &lt;folder&gt; …` plays with the levels that have a package in that folder read from
///   it (<see cref="L2Levels"/>); every other level is its `.nil`. Without it nothing changes.
/// </summary>
public static class ConvertL2Command
{
    /// <summary>
    /// Takes <c>--l2</c> for the run, and runs <c>--convert-l2</c> when it is asked for: its exit
    /// code then, else null and the run goes on.
    /// </summary>
    public static int? Handle(string[] args)
    {
        int l2 = Array.IndexOf(args, "--l2");
        if (l2 >= 0 && l2 + 1 < args.Length && !args[l2 + 1].StartsWith("--")) L2Levels.Folder = Path.GetFullPath(args[l2 + 1]);
        return args.Contains("--convert-l2") ? Run(args) : null;
    }

    static int Run(string[] args)
    {
        int at = Array.IndexOf(args, "--convert-l2");
        if (at + 2 >= args.Length || args[at + 1].StartsWith("--") || args[at + 2].StartsWith("--"))
        {
            Console.Error.WriteLine("usage: vitomir --convert-l2 <LEVEL|ALL> <out-folder> [--no-anchors] [--game <folder>]");
            return 2;
        }
        string level = args[at + 1], outDir = args[at + 2];
        int g = Array.IndexOf(args, "--game");
        var gameDir = GameFiles.Locate(g >= 0 && g + 1 < args.Length ? args[g + 1] : null);
        if (gameDir is null) { Console.Error.WriteLine("game data not found (pass --game <folder holding Resource.nob>)"); return 2; }
        L2Levels.Folder = null;   // a conversion reads the .nil, whatever --l2 or VITOMIR_L2 say
        Environment.SetEnvironmentVariable(L2Levels.Variable, null);

        using var files = new GameFiles(gameDir);
        var levels = level.Equals("ALL", StringComparison.OrdinalIgnoreCase)
            ? files.List("levels/").Where(n => n.EndsWith(".nil", StringComparison.OrdinalIgnoreCase))
                   .Select(n => Path.GetFileNameWithoutExtension(n)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray()
            : [level];
        foreach (var name in levels)
        {
            var clock = Stopwatch.StartNew();
            var r = L2Converter.Convert(files, name, outDir, anchors: !args.Contains("--no-anchors"));
            Console.WriteLine($"{name}: {r.Package} — {r.PackageBytes:N0} bytes (the .nil {r.NilBytes:N0}), {r.Sectors} sectors, " +
                              $"{r.Anchors} anchors from {r.Scenes.Length} scenes ({string.Join(", ", r.Scenes)}), {r.Exits} exits" +
                              (r.UnattributedScenes.Length > 0 ? $"; unattributed scenes {string.Join(", ", r.UnattributedScenes)}" : "") +
                              $"; {clock.Elapsed.TotalSeconds:0.0} s");
        }
        return 0;
    }
}
