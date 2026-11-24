// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The recooked base game (ours): the original's templates, scenes and camera paths made into text at the import, and its text data
// files laid out, where they live, and the making of them.
using System.Text;

namespace Vitomir.Formats;

/// <summary>
/// **The recooked base game** (ours): what the import makes of the original game's binary data that a person edits — the template
/// files, the scenes and the camera paths — as text (<see cref="NotText"/>, <see cref="NsdText"/>, <see cref="NcpText"/>), in a folder
/// of its own beside the imported data (<see cref="FolderName"/>). The game reads it as the base game's own, over the original archives
/// and under the player's modules, which may carry the same text — a single template, a scene — to change it. The original files stay
/// where they are, untouched; a module or an older chronicle that carries the original binary formats is read as it always was.
/// <para>Each file keeps the original's path with the text's own name:</para>
/// <list type="bullet">
/// <item><c>misc/Templates/darkAges.not</c> → <c>misc/Templates/darkAges/&lt;template&gt;.txt</c>, a file a template, and
/// <c>misc/Templates/darkAges.order</c>, their order.</item>
/// <item><c>scenes/P1_HAVN.nsd</c> → <c>scenes/P1_HAVN.txt</c> and <c>scenes/P1_HAVN.geometry</c>.</item>
/// <item><c>paths/intro.ncp</c> → <c>paths/intro.txt</c>.</item>
/// <item>The text data files (<c>misc/game.ndd</c>, <c>misc/effects.ned</c>, <c>misc/Christof.NPC</c> …, <see cref="TextTable"/>) at
/// their own paths, laid out as tables; read as the original's are, over the archives.</item>
/// </list>
/// The folder is made again whole when the import runs again or this program recooks in another way (<see cref="Version"/>), so a
/// change made to it in place does not last: a change is a module's.
/// </summary>
public static class Recooked
{
    /// <summary>The folder's name, beside the imported data.</summary>
    public const string FolderName = "Recooked";

    /// <summary>The marker, written last: what made it and from where.</summary>
    public const string MarkerName = "vitomir-recooked.txt";

    /// <summary>How this program recooks: a folder made by another is made again.</summary>
    public const int Version = 2;

    public enum Form { None, Templates, Scene, Path }

    /// <summary>What a path of the original's is, as far as recooking goes.</summary>
    public static Form Kind(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".not" => Form.Templates,
        ".nsd" => Form.Scene,
        ".ncp" => Form.Path,
        _ => Form.None,
    };

    static string Stem(string path) => path[..^Path.GetExtension(path).Length];

    /// <summary>A template file's folder of templates: <c>misc/Templates/darkAges/</c>.</summary>
    public static string TemplateFolder(string notPath) => Stem(notPath) + "/";

    /// <summary>A template file's order: <c>misc/Templates/darkAges.order</c>.</summary>
    public static string TemplateOrder(string notPath) => Stem(notPath) + NotText.OrderExtension;

    /// <summary>A scene's or a path's text: <c>scenes/P1_HAVN.txt</c>.</summary>
    public static string TextOf(string path) => Stem(path) + NsdText.Extension;

    /// <summary>A scene's geometry: <c>scenes/P1_HAVN.geometry</c>.</summary>
    public static string GeometryOf(string nsdPath) => Stem(nsdPath) + NsdText.GeometryExtension;

    /// <summary>What a recooking made.</summary>
    public sealed record Report(int TemplateFiles, int Templates, int Scenes, int Paths, long Bytes, int TextFiles = 0);

    /// <summary>
    /// The archives of a folder in the order the game mounts them (every <c>.nob</c>, by name in upper case, a name starting with a dot
    /// left out), the first that holds a path giving it.
    /// </summary>
    public static IEnumerable<string> Archives(string folder) =>
        Directory.EnumerateFiles(folder)
                 .Where(p => Path.GetExtension(p).Equals(".nob", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(p).StartsWith('.'))
                 .OrderBy(p => Path.GetFileName(p).ToUpperInvariant(), StringComparer.Ordinal);

    /// <summary>
    /// **Recooks the original game's data** from the archives of <paramref name="data"/> (the imported data, or an install) into
    /// <paramref name="to"/>: made whole in a folder beside it first, then put in its place, so a stopped recooking leaves the last one
    /// (or none) and never half of one. The marker is written last.
    /// </summary>
    public static Report Make(string data, string to, string madeBy, IProgress<string>? progress = null)
    {
        string making = to.TrimEnd('/', '\\') + ".making";
        if (Directory.Exists(making)) Directory.Delete(making, recursive: true);
        Directory.CreateDirectory(making);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int notFiles = 0, templates = 0, scenes = 0, paths = 0, textFiles = 0;
        long bytes = 0;
        void Put(string relative, byte[] content)
        {
            string full = Path.Combine(making, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, content);
            bytes += content.Length;
        }
        foreach (var archive in Archives(data))
        {
            NobArchive nob;
            try { nob = new NobArchive(archive); }
            catch (InvalidDataException) { continue; }                     // not an archive: not mounted
            using (nob)
                foreach (var name in nob.Names.ToList())
                {
                    var form = Kind(name);
                    if (form == Form.None && TextTable.Knows(name) && taken.Add(name))
                    {
                        // The text data files, laid out as tables (the original as it is if it fails the check), so every file a
                        // modder edits as text is here.
                        var text = nob.Read(name);
                        Put(name, TextTable.Checked(text, out _) ?? text);
                        textFiles++;
                        continue;
                    }
                    if (form == Form.None || !taken.Add(name)) continue;
                    var raw = nob.Read(name);
                    switch (form)
                    {
                        case Form.Templates:
                        {
                            var file = NotFile.Read(raw, name);
                            string folder = TemplateFolder(name);
                            foreach (var t in file.Templates)
                            {
                                if (!t.Name.Equals(file.ByName[t.Name].Name, StringComparison.Ordinal)) continue;   // a second of one name: the first is the one read
                                Put(folder + NotText.FileName(t.Name), RecookText.Bytes(NotText.Write(t, name)));
                                templates++;
                            }
                            Put(TemplateOrder(name), RecookText.Bytes(NotText.WriteOrder(file.Templates.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase))));
                            notFiles++;
                            break;
                        }
                        case Form.Scene:
                        {
                            var (text, geometry) = NsdText.Write(NsdScene.Read(raw, name), name);
                            Put(TextOf(name), RecookText.Bytes(text));
                            if (geometry is not null) Put(GeometryOf(name), geometry);
                            scenes++;
                            break;
                        }
                        case Form.Path:
                            Put(TextOf(name), RecookText.Bytes(NcpText.Write(NcpCameraPath.Read(raw, name), name)));
                            paths++;
                            break;
                    }
                    progress?.Report(name);
                }
        }
        var report = new Report(notFiles, templates, scenes, paths, bytes, textFiles);
        Put(MarkerName, Encoding.UTF8.GetBytes(
            $"version {Version}\r\nfrom {data}\r\nby {madeBy}\r\nwhen {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z\r\n"
            + $"{notFiles} template files ({templates} templates), {scenes} scenes, {paths} camera paths, {textFiles} text data files\r\n"));
        if (Directory.Exists(to)) Directory.Delete(to, recursive: true);
        Directory.Move(making, to);
        return report;
    }

    /// <summary>The version a recooked folder was made by, or null when it has no marker (it did not finish, or it is not one).</summary>
    public static int? VersionOf(string folder)
    {
        string marker = Path.Combine(folder, MarkerName);
        if (!File.Exists(marker)) return null;
        foreach (var line in File.ReadLines(marker))
            if (line.StartsWith("version ", StringComparison.Ordinal) && int.TryParse(line.AsSpan(8), out int v)) return v;
        return null;
    }
}
