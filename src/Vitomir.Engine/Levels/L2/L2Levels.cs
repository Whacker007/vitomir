// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.L2;

/// <summary>
/// **Where a level is read from** (ours). Without an L2 folder — the default —
/// every level is its `.nil`, read exactly as before. Given a folder of <c>.l2</c> packages
/// (<c>vitomir --l2 &lt;folder&gt;</c>, or the <see cref="Variable"/> environment variable, which a
/// test can hand a child process), a level that has a package there is read from its legacy chunks
/// instead, into the very same <see cref="NilLevel"/>; any other level is still its `.nil`. What is
/// built from the model after that — the mesh, the sectors, the path graph — does not know which it was.
/// </summary>
public static class L2Levels
{
    /// <summary>The environment variable naming the L2 folder when no <c>--l2</c> was given.</summary>
    public const string Variable = "VITOMIR_L2";

    static string? given;

    /// <summary>
    /// The folder of <c>.l2</c> packages levels are looked for in, or null for none (the faithful
    /// default). <c>--l2</c> sets it; unset, it is <see cref="Variable"/>'s value.
    /// </summary>
    public static string? Folder
    {
        get => given ?? (Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } v ? v : null);
        set => given = string.IsNullOrEmpty(value) ? null : value;
    }

    static readonly Dictionary<string, string> registered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Names one package wherever it is (ours): the level called <paramref name="name"/> is read
    /// from <paramref name="packageFolder"/>, with or without a <see cref="Folder"/>. A chronicle place
    /// played as a cell level is found this way (<see cref="Cells.CellPlaces"/>).
    /// </summary>
    public static void Register(string name, string packageFolder) => registered[name] = packageFolder;

    /// <summary>Forgets every package named by <see cref="Register"/>.</summary>
    public static void Unregister() => registered.Clear();

    /// <summary>The package a level is read from — one named by <see cref="Register"/>, else one in <see cref="Folder"/> — or null for its <c>.nil</c>.</summary>
    public static string? PackageOf(string name) =>
        registered.TryGetValue(name, out var named) ? named : Folder is { } root ? Find(root, name) : null;

    /// <summary>
    /// The level called <paramref name="name"/>: from its package when it has one (<see cref="PackageOf"/>),
    /// else from <c>levels/NAME.nil</c> as always.
    /// </summary>
    public static NilLevel Read(GameFiles files, string name)
    {
        if (PackageOf(name) is { } package)
        {
            var level = Load(package, name);
            Diag.For(typeof(L2Levels).FullName!).LogInformation("{Level}: read from the L2 package {Package}", name, package);
            Console.WriteLine($"l2: {name} read from {package}");
            return level;
        }
        // Ours: a save made in a cell level names the package's level, which no install has.
        if (!files.TryRead($"levels/{name}.nil", out var nil))
            throw new FileNotFoundException($"levels/{name}.nil: the level {name} is neither in the install nor a package this run was given; " +
                                            "a place played as a cell level needs its --place <Place>=<package.l2> (or --l2 <folder>) again");
        return NilLevel.Read(nil, name + ".nil");
    }

    /// <summary>The package folder for <paramref name="name"/> under <paramref name="root"/>, matched without regard to case, or null.</summary>
    public static string? Find(string root, string name)
    {
        var exact = L2Package.PathFor(root, name);
        if (Directory.Exists(exact)) return exact;
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateDirectories(root, "*" + L2Package.Extension)
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), name + L2Package.Extension, StringComparison.OrdinalIgnoreCase))
            ?? StandIn(name);
    }

    /// <summary>
    /// **Ours: cell levels standing in for 1.1 levels**, by the 1.1 level's name. A cell level
    /// that carries a 1.1 scene (<see cref="L2Scene"/>) is played <em>as</em> that level: the chronicle, the
    /// scripts, travel and saves go on naming <c>P1_OTDY</c>, and where its <c>.nil</c> would be read the
    /// package is (<see cref="Find"/>, after a package of the level's own name). Nothing stands in for
    /// anything unless it is put here — <c>--level-l2</c> does it for the package it plays — so the faithful
    /// game is untouched.
    /// </summary>
    public static Dictionary<string, string> StandIns { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The package standing in for the 1.1 level called <paramref name="name"/>, or null.</summary>
    public static string? StandIn(string name) =>
        StandIns.TryGetValue(name, out var package) && Directory.Exists(package) ? package : null;

    /// <summary>
    /// A converted level's model, from its legacy cell; or, for a cooked level of cells (ours), the
    /// shell that stands for its <c>.nil</c> (<see cref="Cells.CellLevel"/>, <see cref="Cells.CellShell"/>).
    /// </summary>
    public static NilLevel Load(string packageFolder, string name)
    {
        var package = L2Package.Open(packageFolder);
        // A cooked level (ours): its shell, which stands for a .nil (Levels.Cells.CellLevel).
        if (package.Manifest.Cells.Any(c => c.Region == Cells.CellCook.Region)) return Cells.CellLevel.Load(packageFolder).Shell;
        var legacy = package.Manifest.Cells.Where(c => c.Region == "legacy").ToArray();
        if (legacy.Length != 1)
            throw new NodFormatException($"{packageFolder}: {legacy.Length} legacy cells; a converted 1.1 level has exactly one");
        return L2Legacy.Decode(package.ReadCell(legacy[0]), $"{name}{L2Package.Extension}/{legacy[0].File}");
    }
}
