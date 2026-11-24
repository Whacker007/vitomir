// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using System.Text;
using System.Text.Json.Nodes;
using Vitomir.Formats.L2;

namespace Vitomir;

/// <summary>
/// **Ours.** <c>vitomir --relayout-l2 &lt;package.l2&gt; --layout &lt;layout.json&gt; [--out &lt;new.l2&gt;]</c>:
/// a cooked cell level's manifest written again from its generator's layout, without cooking — its player
/// starts (every <c>start</c> anchor, by entrance), its exits and its areas (<see cref="L2LayoutExits"/>,
/// <see cref="L2Area.FromLayout"/>). What the cook made of the geometry — cells, collision, the navigation
/// mesh, lights, probes, textures — is not touched. With <c>--out</c> the new manifest goes into a package
/// folder of its own whose other entries are symbolic links to the first one's, so a 1.7 GB package is
/// neither copied nor changed; without it the package's own <c>level.json</c> is replaced.
///
/// The starts of a level are read from the manifest when it loads (<c>CellShell.Scene</c>); each cell's
/// <c>ANCH</c> chunk keeps the anchors it was cooked with, so a full cook is still what brings the two back
/// together.
/// </summary>
public static class RelayoutL2Command
{
    /// <summary>Runs <c>--relayout-l2</c> when asked, answering the exit code; null when it was not.</summary>
    public static int? Handle(string[] args)
    {
        int at = Array.IndexOf(args, "--relayout-l2");
        if (at < 0) return null;
        string? Opt(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        if (at + 1 >= args.Length || Opt("--layout") is not { } layoutPath)
        {
            Console.Error.WriteLine("usage: vitomir --relayout-l2 <package.l2> --layout <layout.json> [--out <new.l2>]");
            return 2;
        }
        var source = Path.GetFullPath(args[at + 1].TrimEnd('/', '\\'));
        if (!File.Exists(Path.Combine(source, L2Package.ManifestFile))) { Console.Error.WriteLine($"relayout: {source} is not a level package"); return 2; }
        if (!File.Exists(layoutPath)) { Console.Error.WriteLine($"relayout: no layout at {layoutPath}"); return 2; }
        var manifest = L2Package.Open(source).Manifest;
        var layout = JsonNode.Parse(File.ReadAllText(layoutPath));
        var report = L2LayoutExits.Relay(manifest, layout);

        var target = Opt("--out") is { } o ? Path.GetFullPath(o.TrimEnd('/', '\\')) : source;
        if (target != source)
        {
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any(e =>
                    Path.GetFileName(e) != L2Package.ManifestFile && new FileInfo(e).LinkTarget is null))
            {
                Console.Error.WriteLine($"relayout: {target} holds files of its own; not writing over it");
                return 2;
            }
            Directory.CreateDirectory(target);
            foreach (var entry in Directory.EnumerateFileSystemEntries(source))
            {
                string name = Path.GetFileName(entry), link = Path.Combine(target, name);
                if (name == L2Package.ManifestFile) continue;
                if (File.Exists(link) || Directory.Exists(link)) continue;
                if (Directory.Exists(entry)) Directory.CreateSymbolicLink(link, entry); else File.CreateSymbolicLink(link, entry);
            }
        }
        File.WriteAllText(Path.Combine(target, L2Package.ManifestFile), manifest.Write(), new UTF8Encoding(false));
        Console.WriteLine($"relayout: {target}: {report}");
        return 0;
    }
}
