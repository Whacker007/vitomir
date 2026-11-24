// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The original's text files laid out as tables from the tools (ours), each proved to read as it did.
using System.Text.Json;
using Vitomir.Engine.Rpg;
using Vitomir.Formats;

namespace Vitomir;

static class FormatCommands
{
    static readonly JsonSerializerOptions Dump = new()
    {
        IncludeFields = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
    };

    /// <summary>
    /// **What the game reads from a text file**, written out in full (every value of the reader's result), so two files that read the
    /// same give the same answer: the reader the game reads the file with, by its extension. A treasure file is read with every template
    /// there and every effect (each by its name alone), so a line is kept or dropped by its own words only.
    /// </summary>
    public static string Reading(string path, byte[] data)
    {
        string name = Path.GetFileName(path);
        object result = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ndd" => NddFile.Read(data, name),
            ".ndg" => NdgFile.Read(data),
            ".ned" => NedFile.Read(data, name),
            ".npd" => NpdFile.Read(data, name),
            ".npc" => NpcFile.Read(data, name),
            ".ncd" => Clans(new Clans(data)),
            ".ntt" => Treasure(new Treasure(data, _ => new Dictionary<uint, NodTag>(), AnyEffect, seed: 1)),
            var ext => throw new ArgumentException($"no reader for {ext}"),
        };
        return JsonSerializer.Serialize(result, result.GetType(), Dump);
    }

    static NedEffect AnyEffect(string name) => new(name, "", 0, "", default, 0, default, default, 0, 0, 0, new float[NedEffect.Levels]);

    static object Clans(Clans c) => new { All = c.All.OrderBy(x => x.Name, StringComparer.Ordinal).ToList(), c.Listed };

    static object Treasure(Treasure t) => new
    {
        Groups = t.Groups.OrderBy(x => x.Key, StringComparer.Ordinal).ToList(),
        Alters = t.Alters.OrderBy(x => x.Key, StringComparer.Ordinal).ToList(),
        Categories = t.Categories.OrderBy(x => x.Key, StringComparer.Ordinal).ToList(),
        Classes = Enumerable.Range(0, Engine.Rpg.Treasure.Classes).Select(t.Class).ToList(),
        t.Loaded,
    };

    static bool Under(string file, string folder)
    {
        string f = Path.GetFullPath(file), d = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return f.StartsWith(d, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    /// A file laid out as a table, or why it is not: null when it fails the formatter's own check (<see cref="TextTable.Checked"/>), or when
    /// the game's reader reads it differently laid out.
    /// </summary>
    public static byte[]? Formatted(string path, byte[] data, out string? why)
    {
        if (TextTable.Checked(data, out why) is not { } laid) return null;
        if (Reading(path, laid) != Reading(path, data)) { why = "it would read differently laid out"; return null; }
        return laid;
    }

    /// <summary>
    /// `vitomir-tools --format &lt;file or folder&gt; [--check]`: the original's text data files (<see cref="TextTable"/>) laid out as
    /// tables, in place — a folder's every such file under it. A file is written only when it reads exactly as it did
    /// (<see cref="Reading"/>) and is not already laid out. `--check` writes nothing and says which files would change (exit code 1 when
    /// any would). A file that would read differently is left as it is, said, and the exit code is 1. The game's own files (the install
    /// or the imported data the tools find) are never written: a copy is laid out.
    /// </summary>
    public static int Format(string? what, bool check)
    {
        if (what is null || what.StartsWith("--")) { Console.Error.WriteLine("format: name a file or a folder"); return 2; }
        string[] files;
        if (Directory.Exists(what))
            files = [.. Directory.EnumerateFiles(what, "*", SearchOption.AllDirectories).Where(TextTable.Knows).Order(StringComparer.OrdinalIgnoreCase)];
        else if (File.Exists(what))
        {
            if (!TextTable.Knows(what)) { Console.Error.WriteLine($"format: {what} is not one of the text data files ({string.Join(' ', TextTable.Extensions)})"); return 2; }
            files = [what];
        }
        else { Console.Error.WriteLine($"format: no file or folder {what}"); return 2; }

        // The game's own files are the original's, and the data check holds them to their hashes: they are laid out in a copy.
        if (!check && Engine.Vfs.GameFiles.Locate() is { } game && files.Any(f => Under(f, game)))
        {
            Console.Error.WriteLine($"format: {what} is in the game's own files ({game}); lay out a copy (a module's folder, say), or --check");
            return 2;
        }
        int changed = 0, same = 0, refused = 0;
        foreach (var f in files)
        {
            var data = File.ReadAllBytes(f);
            var laid = Formatted(f, data, out var why);
            if (laid is null) { refused++; Console.WriteLine($"  left as it is: {f} ({why})"); continue; }
            if (laid.AsSpan().SequenceEqual(data)) { same++; continue; }
            changed++;
            if (check) Console.WriteLine($"  would lay out: {f}");
            else { File.WriteAllBytes(f, laid); Console.WriteLine($"  laid out: {f}"); }
        }
        Console.WriteLine($"format: {files.Length} files, {changed} {(check ? "would change" : "laid out")}, {same} already laid out" +
                          (refused > 0 ? $", {refused} left as they are" : ""));
        return refused > 0 || (check && changed > 0) ? 1 : 0;
    }
}
