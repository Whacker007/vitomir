// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The recooked base game from the tools (ours): made from an install or the imported data, and a template shown with what it inherits.
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir;

static class RecookCommands
{
    /// <summary>
    /// `vitomir-tools --recook &lt;out&gt; [--game &lt;install or imported data&gt;]`: the original's templates, scenes and camera paths
    /// recooked as text, and its text data files laid out, into a folder (<see cref="Recooked.Make"/>), as the import does beside the imported data. A game run over an
    /// install reads it with `--recooked &lt;out&gt;`.
    /// </summary>
    public static int Recook(string? to, string? game)
    {
        if (to is null || to.StartsWith("--")) { Console.Error.WriteLine("recook: name the folder to recook into"); return 2; }
        if ((game ?? GameFiles.Locate()) is not { } from) { Console.Error.WriteLine("recook: no game files (--game <folder>)"); return 2; }
        var began = System.Diagnostics.Stopwatch.StartNew();
        var r = Recooked.Make(from, Path.GetFullPath(to), "vitomir-tools");
        Console.WriteLine($"recook: {r.TemplateFiles} template files ({r.Templates} templates), {r.Scenes} scenes, {r.Paths} camera paths, {r.TextFiles} text data files, " +
                          $"{r.Bytes / (1024.0 * 1024.0):0.0} MB, into {Path.GetFullPath(to)} in {began.ElapsedMilliseconds} ms");
        return 0;
    }

    /// <summary>
    /// `vitomir-tools --template &lt;name&gt; [--templates darkAges] [--game ...] [--recooked ...]`: one template with everything it
    /// inherits, each value with the template it comes from, as the game reads them (the recooked text over the original, and a template
    /// file of the era's over <c>Global</c>'s, as the game looks a parent up).
    /// </summary>
    public static int Template(string? name, string? era, string? game, string? recookedFolder)
    {
        if (name is null || name.StartsWith("--")) { Console.Error.WriteLine("template: name the template"); return 2; }
        if ((game ?? GameFiles.Locate()) is not { } from) { Console.Error.WriteLine("template: no game files (--game <folder>)"); return 2; }
        using var files = new GameFiles(from, recookedFolder: recookedFolder);
        var global = NotFile.Read(files.Read("misc/Templates/Global.not"), "Global.not");
        var files2 = new List<(string File, NotFile Not)>();
        foreach (var f in new[] { era ?? "darkAges", "modernDay", "Multiplayer" }.Distinct(StringComparer.OrdinalIgnoreCase))
            if (files.TryRead($"misc/Templates/{f}.not", out var d)) files2.Add((f, NotFile.Read(d, f)));
        var (file, not) = files2.FirstOrDefault(x => x.Not.ByName.ContainsKey(name));
        if (not is null && global.ByName.ContainsKey(name)) (file, not) = ("Global", global);
        if (not is null) { Console.Error.WriteLine($"template: no template {name}"); return 1; }
        var chain = new List<(string File, NotTemplate T)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string cur = name;
        while (cur.Length > 0 && seen.Add(cur))
        {
            if (not.ByName.TryGetValue(cur, out var t)) chain.Add((file, t));
            else if (global.ByName.TryGetValue(cur, out t)) chain.Add(("Global", t));
            else { Console.WriteLine($"  (its parent {cur} is in no template file)"); break; }
            cur = t.Parent;
        }
        Console.WriteLine($"{name} ({file}), from {string.Join(" <- ", chain.Select(c => c.T.Name))}" + (files.RecookedFolder is { } rf ? $"; recooked: {rf}" : ""));
        var from2 = new Dictionary<uint, (NodTag Tag, string Who)>();
        for (int i = chain.Count - 1; i >= 0; i--)
            foreach (var tag in chain[i].T.Tags) from2[tag.Id] = (tag, chain[i].T.Name);
        int width = from2.Values.Max(v => TagTable.NameOf(v.Tag.Id).Length);
        foreach (var (tag, who) in from2.Values.OrderBy(v => v.Tag.Id))
            Console.WriteLine($"  {TagTable.NameOf(tag.Id).PadRight(width)} = {TagTable.Write(tag)}{(who.Equals(name, StringComparison.OrdinalIgnoreCase) ? "" : $"    ({who})")}");
        return 0;
    }
}
