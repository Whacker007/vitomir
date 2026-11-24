// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;
using Vitomir.Render;

namespace Vitomir;

/// <summary>
/// **The interface art tool** (ours): a pane's art as one picture to
/// look at, the same as an outline to paint to, and a painted picture cut back into the files the engine
/// reads (<see cref="PaneArt"/>, <see cref="UiAssets.Finer"/>). It writes only into the folder it is given.
///
///   vitomir --ui-art-list                                  the panes that have art, and their sizes
///   vitomir --ui-art-sheet &lt;pane|all&gt; [--ui-art-out &lt;folder&gt;] [--ui-art-scale 6]
///   vitomir --ui-art-cut &lt;pane&gt; --ui-art-from &lt;picture&gt; [--ui-art-out &lt;data folder&gt;]
///   vitomir --ui-art-one &lt;texture&gt; --ui-art-from &lt;picture&gt; [--ui-art-out &lt;data folder&gt;]
/// </summary>
public static class UiArtCommands
{
    public const string DefaultSheets = @"C:\Games\Vitomir\art\sheets", DefaultData = @"C:\Games\Vitomir\art\data";

    static IEnumerable<string> Panes(GameFiles files) =>
        files.List("ui/").Where(f => f.EndsWith(".nui", StringComparison.OrdinalIgnoreCase))
             .Select(f => Path.GetFileNameWithoutExtension(f)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

    public static int List(GameFiles files)
    {
        var assets = new UiAssets(files);
        foreach (string name in Panes(files))
        {
            PaneArt.Sheet? sheet;
            try { sheet = PaneArt.Of(assets, name); } catch (Exception e) when (e is InvalidDataException or IOException) { continue; }
            if (sheet is null) continue;
            Console.WriteLine($"{name,-28} {sheet.Width,4}x{sheet.Height,-4} {sheet.Tiles.Select(t => t.Material).Distinct(StringComparer.OrdinalIgnoreCase).Count(),3} textures  {sheet.Slots.Count,3} controls");
        }
        return 0;
    }

    public static int Sheet(GameFiles files, string pane, string? to, string? scaleWord)
    {
        if (!int.TryParse(scaleWord ?? "6", out int scale) || !UiAssets.FinerScales.Contains(scale))
        {
            Console.Error.WriteLine($"ui-art-sheet: --ui-art-scale {scaleWord} is not one of {string.Join(", ", UiAssets.FinerScales)}");
            return 2;
        }
        string folder = to ?? DefaultSheets;
        Directory.CreateDirectory(folder);
        var assets = new UiAssets(files);
        int made = 0;
        foreach (string name in pane.Equals("all", StringComparison.OrdinalIgnoreCase) ? Panes(files) : [pane])
        {
            PaneArt.Sheet? sheet;
            try { sheet = PaneArt.Of(assets, name); }
            catch (Exception e) when (e is InvalidDataException or IOException or FileNotFoundException) { Console.Error.WriteLine($"ui-art-sheet: {name}: {e.Message}"); continue; }
            if (sheet is null) { if (!pane.Equals("all", StringComparison.OrdinalIgnoreCase)) Console.Error.WriteLine($"ui-art-sheet: {name} has no art of its own"); continue; }
            Png.Write(Path.Combine(folder, sheet.Pane + ".original.png"), sheet.Width, sheet.Height, PaneArt.Original(assets, sheet));
            Png.Write(Path.Combine(folder, sheet.Pane + $".outline@{scale}x.png"), sheet.Width * scale, sheet.Height * scale, PaneArt.Template(assets, sheet, scale));
            File.WriteAllLines(Path.Combine(folder, sheet.Pane + ".txt"), PaneArt.Describe(sheet, scale));
            Console.WriteLine($"{sheet.Pane}: {sheet.Width}x{sheet.Height}, to paint at {sheet.Width * scale}x{sheet.Height * scale}");
            made++;
        }
        Console.WriteLine($"ui-art-sheet: {made} pane(s) in {folder} — .original.png is 1.1's own art, to look at and never to ship");
        return made > 0 ? 0 : 1;
    }

    /// <summary>A painted picture: a PNG or a TGA, as R, G, B, A with the top row first.</summary>
    static byte[]? Picture(string path, out int width, out int height)
    {
        width = height = 0;
        if (!File.Exists(path)) { Console.Error.WriteLine($"ui-art: no file {path}"); return null; }
        if (path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var image = TgaImage.Read(File.ReadAllBytes(path), path);
                (width, height) = (image.Width, image.Height);
                return image.Rgba;
            }
            catch (InvalidDataException e) { Console.Error.WriteLine($"ui-art: {e.Message}"); return null; }
        }
        var rgba = Png.Read(path, out width, out height);
        if (rgba is null) Console.Error.WriteLine($"ui-art: {path} is not a PNG this reads — save it as a plain 8-bit PNG (not interlaced), or as a 32-bit TGA");
        return rgba;
    }

    static string Materials(string? to)
    {
        string folder = Path.Combine(to ?? DefaultData, "ui", "materials");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static int Cut(GameFiles files, string pane, string? from, string? to)
    {
        if (from is null) { Console.Error.WriteLine("ui-art-cut: name the painted picture with --ui-art-from <file>"); return 2; }
        var assets = new UiAssets(files);
        PaneArt.Sheet? sheet;
        try { sheet = PaneArt.Of(assets, pane); }
        catch (Exception e) when (e is InvalidDataException or IOException or FileNotFoundException) { Console.Error.WriteLine($"ui-art-cut: {pane}: {e.Message}"); return 2; }
        if (sheet is null) { Console.Error.WriteLine($"ui-art-cut: {pane} has no art of its own"); return 2; }
        if (Picture(from, out int w, out int h) is not { } rgba) return 2;
        int scale = PaneArt.ScaleOf(sheet, w, h);
        if (scale == 0)
        {
            Console.Error.WriteLine($"ui-art-cut: {from} is {w}x{h}; {sheet.Pane} is {sheet.Width}x{sheet.Height}, so the picture has to be exactly " +
                                    string.Join(", ", UiAssets.FinerScales.Select(s => $"{sheet.Width * s}x{sheet.Height * s}")) + " — nothing written");
            return 1;
        }
        string folder = Materials(to);
        var notes = new List<string>();
        foreach (var cut in PaneArt.Cuts(sheet, rgba, scale, notes))
        {
            File.WriteAllBytes(Path.Combine(folder, cut.File), cut.Data);
            Console.WriteLine($"  {cut.File}  {cut.Width}x{cut.Height}");
        }
        foreach (string note in notes) Console.WriteLine($"  note: {note}");
        Console.WriteLine($"ui-art-cut: {sheet.Pane} at {scale} to a unit, in {folder} — play with --data {Path.GetFullPath(Path.Combine(folder, "..", ".."))}");
        return 0;
    }

    public static int One(GameFiles files, string texture, string? from, string? to)
    {
        if (from is null) { Console.Error.WriteLine("ui-art-one: name the painted picture with --ui-art-from <file>"); return 2; }
        var assets = new UiAssets(files);
        if (assets.Texture(texture) is not { Width: > 0, Height: > 0 } original) { Console.Error.WriteLine($"ui-art-one: the game has no interface texture named {texture}"); return 2; }
        if (Picture(from, out int w, out int h) is not { } rgba) return 2;
        int scale = PaneArt.ScaleOf(original.Width, original.Height, w, h);
        if (scale == 0)
        {
            Console.Error.WriteLine($"ui-art-one: {from} is {w}x{h}; {texture} is {original.Width}x{original.Height}, so the picture has to be exactly " +
                                    string.Join(", ", UiAssets.FinerScales.Select(s => $"{original.Width * s}x{original.Height * s}")) + " — nothing written");
            return 1;
        }
        string folder = Materials(to), file = PaneArt.FinerName(texture, scale);
        File.WriteAllBytes(Path.Combine(folder, file), TgaImage.Write(w, h, rgba));
        Console.WriteLine($"ui-art-one: {file} {w}x{h} in {folder} — play with --data {Path.GetFullPath(Path.Combine(folder, "..", ".."))}");
        return 0;
    }
}
