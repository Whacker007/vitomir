// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using System.Globalization;
using Vitomir.Engine.Levels.Export;
using Vitomir.Engine.Vfs;

namespace Vitomir;

/// <summary>
/// `vitomir --export-gltf &lt;LEVEL&gt; &lt;out-folder&gt; [--lightmap-materials] [--scale &lt;metres per unit&gt;]`
/// (ours): one 1.1 level written out as glTF 2.0 for looking at in
/// other tools. `ALL` exports every level, each into its own folder under the one given. It only
/// reads the game files; nothing the game does changes.
/// </summary>
public static class ExportGltfCommand
{
    public static int Run(string[] args)
    {
        int at = Array.IndexOf(args, "--export-gltf");
        if (at < 0 || at + 2 >= args.Length || args[at + 1].StartsWith("--") || args[at + 2].StartsWith("--"))
        {
            Console.Error.WriteLine("usage: vitomir --export-gltf <LEVEL|ALL> <out-folder> [--lightmap-materials] [--scale <metres per unit>] [--game <folder>]");
            return 2;
        }
        string level = args[at + 1], outDir = args[at + 2];
        int g = Array.IndexOf(args, "--game");
        var gameDir = GameFiles.Locate(g >= 0 && g + 1 < args.Length ? args[g + 1] : null);
        if (gameDir is null) { Console.Error.WriteLine("game data not found (pass --game <folder holding Resource.nob>)"); return 2; }
        int sc = Array.IndexOf(args, "--scale");
        var options = new GltfExportOptions(MaterialPerLightmapPage: args.Contains("--lightmap-materials"));
        if (sc >= 0 && sc + 1 < args.Length) options = options with { Scale = float.Parse(args[sc + 1], CultureInfo.InvariantCulture) };

        using var files = new GameFiles(gameDir);
        var levels = level.Equals("ALL", StringComparison.OrdinalIgnoreCase)
            ? files.List("levels/").Where(n => n.EndsWith(".nil", StringComparison.OrdinalIgnoreCase))
                   .Select(n => Path.GetFileNameWithoutExtension(n)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray()
            : [level];
        foreach (var name in levels)
        {
            var dir = levels.Length > 1 ? Path.Combine(outDir, name) : outDir;
            var r = GltfLevelExporter.Export(files, name, dir, options);
            Console.WriteLine($"{name}: {r.GltfPath} — {r.Triangles} triangles, {r.Materials} materials, {r.Images} images " +
                              $"({r.LightmapPages} lightmap pages), {r.SkippedGroups} sky/nodraw groups left out; walk data {r.WalkPath}");
        }
        return 0;
    }
}
