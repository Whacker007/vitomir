// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Finding the player's 1.1: GOG, Steam, the usual folders, the player's own pick.
using System.Text.RegularExpressions;

namespace Vitomir.Platform.Setup;

/// <summary>Which store's copy an install is: GOG's, Steam's, or neither's (the original CD release or another store: not supported).</summary>
public enum Store { Unknown, Gog, Steam }

/// <summary>One install found: its folder, its store, and every way it was found (<c>GOG's registry entry</c>, <c>Steam library D:\…</c>, …).</summary>
public sealed record InstallCandidate(string Folder, Store Store, IReadOnlyList<string> FoundBy)
{
    /// <summary>Steam's or GOG's (only those two are supported).</summary>
    public bool Supported => Store != Store.Unknown;

    /// <summary>The store's name as the setup says it.</summary>
    public string StoreName => SetupWords.StoreName(Store);
}

/// <summary>
/// **Where the player's 1.1 is** (Steam and GOG only). In order: GOG by its registry entry (the game's
/// product number, 1207658831, under <c>GOG.com\Games</c>) and its <c>goggame-1207658831.info</c>; Steam by its app manifest
/// (<c>appmanifest_559680.acf</c>) in every library folder its <c>libraryfolders.vdf</c> names, Steam itself found by its registry
/// entries and its usual folder, and by its uninstall entry for the game; then the usual folders on each fixed drive; then
/// <c>VTMR_GAME_DIR</c> (a developer's); then the folder the player names. A folder counts only when it holds <c>Resource.nob</c>;
/// each is given its store by what is in it (<see cref="StoreOf"/>). **Found by itself, only a store's own copy is listed** (GOG's or
/// Steam's marks in it): a player's own copies elsewhere, often modified and sometimes many, are left off, though one can always be
/// named, and a named one that is neither store's is listed as not supported. Nothing is written.
/// </summary>
public static class InstallFinder
{
    /// <summary>GOG's product number for the game (its <c>goggame-*.info</c>, its registry key).</summary>
    public const string GogProduct = "1207658831";

    /// <summary>Steam's app number for the game.</summary>
    public const string SteamApp = "559680";

    /// <summary>The folder both stores install the game in.</summary>
    public const string StoreFolderName = "Vampire The Masquerade - Redemption";

    /// <summary>GOG's file in its install that names the product.</summary>
    public const string GogInfo = "goggame-" + GogProduct + ".info";

    /// <summary>GOG's registry keys for the game, 64-bit Windows' and 32-bit's (each with the value <c>path</c>).</summary>
    public static readonly string[] GogKeys =
    [
        @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\GOG.com\Games\" + GogProduct,
        @"HKEY_LOCAL_MACHINE\SOFTWARE\GOG.com\Games\" + GogProduct,
    ];

    /// <summary>Steam's own folder in the registry: the user's (<c>SteamPath</c>) and the machine's (<c>InstallPath</c>).</summary>
    public static readonly (string Key, string Value)[] SteamKeys =
    [
        (@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath"),
        (@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
        (@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath"),
    ];

    /// <summary>Steam's uninstall entry for the game (value <c>InstallLocation</c>).</summary>
    public const string SteamUninstallKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App " + SteamApp;

    /// <summary>
    /// The usual folders, under each fixed drive's root: where GOG, GOG Galaxy and Steam put the game. Only the stores' own: a copy a
    /// player made elsewhere (often a modified one, sometimes several) is not looked for, but can always be named (Whacker007,
    /// 2026-11-23).
    /// </summary>
    public static readonly string[][] UsualFolders =
    [
        ["GOG Games", StoreFolderName],
        ["Program Files (x86)", "GOG Galaxy", "Games", StoreFolderName],
        ["Program Files (x86)", "Steam", "steamapps", "common", StoreFolderName],
        ["Program Files", "Steam", "steamapps", "common", StoreFolderName],
        ["SteamLibrary", "steamapps", "common", StoreFolderName],
        ["Games", "Steam", "steamapps", "common", StoreFolderName],
    ];

    /// <summary>Every install found, each once (the first way it was found first, every other way after it), in the order above.</summary>
    public static IReadOnlyList<InstallCandidate> Find(ISetupMachine machine, string? named = null)
    {
        var found = new List<(string Folder, List<string> By)>();
        void Add(string? folder, string by, bool official = true)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            string full;
            try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Trim().Trim('"'))); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return; }
            if (!IsInstall(full)) return;
            // Found by itself, a copy must be a store's own (its GOG or Steam marks in it); one named is taken as it is.
            if (official && StoreOf(full) == Store.Unknown) return;
            var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            int at = found.FindIndex(f => f.Folder.Equals(full, cmp));
            if (at < 0) found.Add((full, [by]));
            else if (!found[at].By.Contains(by)) found[at].By.Add(by);
        }

        foreach (var key in GogKeys) Add(machine.RegistryValue(key, "path"), "GOG's registry entry");
        foreach (var library in SteamLibraries(machine))
            if (AppManifestFolder(library) is { } steamFolder) Add(steamFolder, $"Steam's app manifest in the library {library}");
        Add(machine.RegistryValue(SteamUninstallKey, "InstallLocation"), "Steam's uninstall entry");
        foreach (var drive in machine.FixedDrives())
            foreach (var parts in UsualFolders)
                Add(Path.Combine([drive, .. parts]), "a usual folder");
        Add(machine.Environment("VTMR_GAME_DIR"), "VTMR_GAME_DIR (a developer's)", official: false);
        Add(named, "the folder named", official: false);
        return [.. found.Select(f => new InstallCandidate(f.Folder, StoreOf(f.Folder), f.By))];
    }

    /// <summary>Whether <paramref name="folder"/> is a 1.1 install: it holds <c>Resource.nob</c> (looked for case-insensitively).</summary>
    public static bool IsInstall(string folder) => Directory.Exists(folder) && FileIn(folder, "Resource.nob") is not null;

    /// <summary>
    /// **Whose copy it is**, by what is in it: GOG's (its <c>goggame-1207658831.info</c>), Steam's (its app manifest in the library
    /// the folder is in, or Steam's own <c>installscript.vdf</c> beside the game, which only Steam's copy has: the survey), or neither's.
    /// </summary>
    public static Store StoreOf(string folder)
    {
        if (FileIn(folder, GogInfo) is not null) return Store.Gog;
        if (FileIn(folder, "installscript.vdf") is not null) return Store.Steam;
        // <library>\steamapps\common\<game>: the library's app manifest names this folder.
        var common = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));
        var steamapps = common is null ? null : Path.GetDirectoryName(common);
        if (steamapps is not null && Path.GetFileName(common!).Equals("common", StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(steamapps) is { } library && AppManifestFolder(library) is { } named
            && Path.GetFileName(named).Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)), StringComparison.OrdinalIgnoreCase))
            return Store.Steam;
        return Store.Unknown;
    }

    /// <summary>Steam's library folders: Steam's own folders (the registry's, the usual one), then each one its <c>libraryfolders.vdf</c> names.</summary>
    public static IReadOnlyList<string> SteamLibraries(ISetupMachine machine)
    {
        var roots = new List<string>();
        foreach (var (key, value) in SteamKeys)
            if (machine.RegistryValue(key, value) is { Length: > 0 } r) roots.Add(r.Replace('/', Path.DirectorySeparatorChar));
        foreach (var drive in machine.FixedDrives())
        {
            roots.Add(Path.Combine(drive, "Program Files (x86)", "Steam"));
            roots.Add(Path.Combine(drive, "Program Files", "Steam"));
        }
        var libraries = new List<string>();
        void Add(string l)
        {
            try { l = Path.TrimEndingDirectorySeparator(Path.GetFullPath(l)); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return; }
            if (Directory.Exists(l) && !libraries.Contains(l, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)) libraries.Add(l);
        }
        foreach (var root in roots)
        {
            Add(root);
            if (FileIn(Path.Combine(root, "steamapps"), "libraryfolders.vdf") is { } vdf)
                foreach (var path in VdfValues(SafeRead(vdf), "path")) Add(path);
        }
        return libraries;
    }

    /// <summary>The game's folder a library's <c>appmanifest_559680.acf</c> names (its <c>installdir</c> under <c>steamapps\common</c>), or null.</summary>
    public static string? AppManifestFolder(string library)
    {
        string steamapps = Path.Combine(library, "steamapps");
        if (FileIn(steamapps, $"appmanifest_{SteamApp}.acf") is not { } acf) return null;
        var dir = VdfValues(SafeRead(acf), "installdir").FirstOrDefault();
        return string.IsNullOrWhiteSpace(dir) ? null : Path.Combine(steamapps, "common", dir);
    }

    /// <summary>Every value of <paramref name="key"/> in a Valve key-values text (<c>"key"  "value"</c>, backslashes escaped).</summary>
    public static IEnumerable<string> VdfValues(string text, string key) =>
        Regex.Matches(text, "\"" + Regex.Escape(key) + "\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase)
             .Select(m => Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1"));

    static string SafeRead(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>The file <paramref name="name"/> in <paramref name="folder"/> however its case is spelt, or null.</summary>
    public static string? FileIn(string folder, string name)
    {
        try
        {
            if (!Directory.Exists(folder)) return null;
            string direct = Path.Combine(folder, name);
            if (File.Exists(direct)) return direct;
            return Directory.EnumerateFiles(folder).FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}

/// <summary>The setup's words for what it reports, shared by the command line and the launcher's Setup page.</summary>
public static class SetupWords
{
    public static string StoreName(Store store) => store switch
    {
        Store.Gog => "GOG",
        Store.Steam => "Steam",
        _ => "neither GOG's nor Steam's",
    };

    /// <summary>The store as the imported data's marker records it: <c>GOG</c>, <c>Steam</c> or <c>unknown</c>.</summary>
    public static string StoreKey(Store store) => store switch { Store.Gog => "GOG", Store.Steam => "Steam", _ => "unknown" };

    /// <summary>What is said of an install that is neither store's.</summary>
    public const string Unsupported =
        "not a GOG or Steam copy: the original CD release and other stores are not supported (Vitomir supports 1.1 as GOG and Steam sell it)";

    /// <summary>
    /// **A line as plain ASCII punctuation** (a console showed "&#x2426; " for an arrow: a console's code page has none of
    /// these): the arrows, the ellipsis, the dashes, the curly quotes and the multiplication sign as their ASCII forms (<c>-&gt;</c>,
    /// <c>...</c>, <c>-</c>, <c>'</c>, <c>"</c>, <c>x</c>), a no-break space as a space. Every line <c>--setup</c> prints goes through it, so
    /// a path's or a save's own words come out plain too; a letter that is not ASCII (a folder's name) is left as it is.
    /// </summary>
    public static string Plain(string line)
    {
        bool plain = true;
        foreach (char c in line) if (c > '\u007e') { plain = false; break; }
        if (plain) return line;
        var b = new System.Text.StringBuilder(line.Length + 8);
        foreach (char c in line)
            b.Append(c switch
            {
                '\u2192' or '\u27f6' or '\u21d2' => "->",
                '\u2190' => "<-",
                '\u2026' => "...",
                '\u2014' or '\u2013' or '\u2012' or '\u2212' => "-",
                '\u2018' or '\u2019' or '\u201a' or '\u2032' => "'",
                '\u201c' or '\u201d' or '\u201e' or '\u2033' => "\"",
                '\u00d7' => "x",
                '\u00a0' or '\u2009' or '\u202f' => " ",
                _ => c.ToString(),
            });
        return b.ToString();
    }

    /// <summary>Bytes as the setup says them, in decimal units as the survey and the docs give them (1.26 GB for the game's files).</summary>
    public static string Size(long bytes) => bytes >= 1_000_000_000
        ? (bytes / 1e9).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " GB"
        : (bytes / 1e6).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
}
