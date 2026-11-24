// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Formats;

namespace Vitomir.Platform.PlayerModules;

/// <summary>
/// **The kinds of a player module's files** (a module may be several). <see cref="Look"/>: textures, materials, skins' sets,
/// sounds, text strings and films, each player's own. <see cref="World"/>: chronicles and their files, levels, scenes, templates,
/// conversations, quests, stores, paths, the game's tables, the models (a model's bounding box is its thing's collision box and
/// reach) and their motions, which change what is played. <see cref="Interface"/>:
/// the interface's layouts (<c>.nui</c>), pictures and fonts (<c>ui\</c>). A module with any <see cref="World"/> file is a
/// <b>rules module</b> (<see cref="PackKindWords.IsRules"/>): every machine of a network game must have it, and a save names it.
/// </summary>
[Flags]
public enum PackKinds { Look = 1, World = 2, Interface = 4 }

/// <summary>The kinds as the manifest and the lines word them.</summary>
public static class PackKindWords
{
    /// <summary>One kind from its word (<c>look</c>, <c>world</c>, <c>interface</c>), or null.</summary>
    public static PackKinds? Parse(string? word) => word switch
    {
        "look" => PackKinds.Look,
        "world" => PackKinds.World,
        "interface" => PackKinds.Interface,
        _ => null,
    };

    /// <summary>The kinds in a set, in the order look, world, interface.</summary>
    public static IEnumerable<string> List(PackKinds kinds)
    {
        if (kinds.HasFlag(PackKinds.Look)) yield return "look";
        if (kinds.HasFlag(PackKinds.World)) yield return "world";
        if (kinds.HasFlag(PackKinds.Interface)) yield return "interface";
    }

    /// <summary>The kinds as one word list (<c>look, interface</c>), or <c>no data</c>.</summary>
    public static string Words(PackKinds kinds) => kinds == 0 ? "no data" : string.Join(", ", List(kinds));

    /// <summary>Whether a module of these kinds changes the rules or the world (a network game's machines must agree on it).</summary>
    public static bool IsRules(PackKinds kinds) => kinds.HasFlag(PackKinds.World);
}

/// <summary>What one file of a pack is to the import.</summary>
public enum PackFileClass
{
    /// <summary>Data of 1.1's that a module may replace or add (its kind in <see cref="PackFile.Kind"/>).</summary>
    Data,
    /// <summary>Script code (a <c>.class</c>, a <c>.java</c>, the scripts' folder): refused.</summary>
    Code,
    /// <summary>A program or what changes one (an <c>.exe</c>, a <c>.dll</c>, an <c>.asi</c>, a wrapper, ReShade): refused.</summary>
    Program,
    /// <summary>Neither (a readme, an <c>.ini</c>, a picture of the pack): kept aside as a document, never read by the game.</summary>
    Document,
    /// <summary>A <c>.nob</c> archive at the pack's top: mounted as the module's archive (its entries are its data).</summary>
    Archive,
}

/// <summary>One file of a pack as the import classes it: its class, its kind when it is data, and what it was for when it is refused.</summary>
public sealed record PackFile(PackFileClass Class, PackKinds Kind, string Why);

/// <summary>
/// **How the import reads a pack's files** by their 1.1 paths: which top folders are 1.1's (<see cref="TopFolders"/>, in 1.1's own
/// spelling), the kind of each data file (by its top folder and extension), and what is refused — script code and what changes the
/// program, each named with what it was for where that is known.
/// </summary>
public static class PackFiles
{
    /// <summary>
    /// Whether a film's first bytes are a Bink film's (<c>BIK</c>, or Bink 2's own three letters). A module's film that is not (empty, or a
    /// blanked <c>#Dummy.bik</c>) takes 1.1's film of that name out: the game skips it, as it skips a film it cannot open.
    /// </summary>
    public static bool IsBinkFilm(ReadOnlySpan<byte> head) =>
        head.Length >= 4 && (head[0] == 'B' && head[1] == 'I' && head[2] == 'K' || head[0] == 'K' && head[1] == 'B' && head[2] == '2');

    /// <summary>As <see cref="IsBinkFilm(ReadOnlySpan{byte})"/>, of a file's first four bytes.</summary>
    public static bool IsBinkFilm(string path)
    {
        Span<byte> head = stackalloc byte[4];
        using var s = File.OpenRead(path);
        return IsBinkFilm(head[..s.ReadAtLeast(head, 4, throwOnEndOfStream: false)]);
    }

    /// <summary>
    /// 1.1's top folders, as 1.1 spells them: the archives' (<c>3d</c>, <c>materials</c>, <c>ui</c>, …) and the loose ones
    /// (<c>Chronicles</c>, <c>Video</c>; <c>Sounds</c> is both, spelt as the archives do). <c>Codex</c> is 1.1's scripts' folder: its
    /// files are code, refused.
    /// </summary>
    public static readonly string[] TopFolders =
        ["3d", "conversations", "levels", "materials", "misc", "paths", "quests", "scenes", "sounds", "stores", "strings", "ui", "Chronicles", "Video", "Codex"];

    /// <summary>The folders whose files are each player's own looks.</summary>
    static readonly string[] LookFolders = ["materials", "sounds", "strings", "Video"];

    /// <summary>The folders whose files change the world or the rules.</summary>
    static readonly string[] WorldFolders = ["conversations", "levels", "misc", "paths", "quests", "scenes", "stores", "Chronicles"];

    /// <summary>1.1's spelling of a top folder, matched without regard to case; null when it is none of 1.1's.</summary>
    public static string? TopFolder(string name) =>
        TopFolders.FirstOrDefault(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Extensions of script code.</summary>
    static readonly Dictionary<string, string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".class"] = "script code (a compiled Java class)",
        [".java"] = "script code (a Java source)",
        [".jar"] = "script code (a Java archive)",
    };

    /// <summary>Extensions of programs and what changes one.</summary>
    static readonly Dictionary<string, string> ProgramExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".exe"] = "a program",
        [".dll"] = "a library a program loads",
        [".asi"] = "a plug-in the game's program loads (an ASI loader's)",
        [".m3d"] = "a plug-in of the game's sound library (Miles)",
        [".flt"] = "a plug-in of the game's sound library (Miles)",
        [".bat"] = "a Windows script",
        [".cmd"] = "a Windows script",
        [".ps1"] = "a Windows script",
        [".vbs"] = "a Windows script",
        [".com"] = "a program",
        [".scr"] = "a program",
        [".sys"] = "a driver",
        [".drv"] = "a driver",
        [".ocx"] = "a library a program loads",
        [".cpl"] = "a program",
        [".msi"] = "an installer",
        [".reg"] = "a change to the Windows registry",
        [".fx"] = "a shader of a post-processing injector (ReShade's)",
        [".fxh"] = "a shader of a post-processing injector (ReShade's)",
    };

    /// <summary>Program files known by name, with what each was for.</summary>
    static readonly Dictionary<string, string> KnownPrograms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vampire.exe"] = "the game's program, patched (Vitomir is a program of its own and does not run it)",
        ["patch.dll"] = "a patch to the game's program",
        ["ddraw.dll"] = "a graphics wrapper the game's program loads (dgVoodoo's): Vitomir draws with its own renderer",
        ["d3d8.dll"] = "a graphics wrapper the game's program loads (dgVoodoo's or ReShade's): Vitomir draws with its own renderer",
        ["d3d9.dll"] = "a graphics wrapper the game's program loads (dgVoodoo's or ReShade's): Vitomir draws with its own renderer",
        ["d3dimm.dll"] = "a graphics wrapper the game's program loads (dgVoodoo's): Vitomir draws with its own renderer",
        ["dxgi.dll"] = "a graphics wrapper the game's program loads (ReShade's or dgVoodoo's): Vitomir draws with its own renderer",
        ["d3d11.dll"] = "a graphics wrapper (ReShade's or dgVoodoo's): Vitomir draws with its own renderer",
        ["opengl32.dll"] = "a graphics wrapper: Vitomir draws with its own renderer",
        ["binkw32.dll"] = "the film player's library: Vitomir plays the films itself",
        ["mss32.dll"] = "the sound library (Miles): Vitomir plays the sounds itself",
        ["rldirect.dll"] = "the game program's own library",
        ["dgvoodoocpl.exe"] = "dgVoodoo's settings program, a graphics wrapper's",
        ["dgvoodoo.conf"] = "dgVoodoo's settings, a graphics wrapper's",
        ["reshade.ini"] = "ReShade's settings, a post-processing injector's",
        ["reshadepreset.ini"] = "ReShade's settings, a post-processing injector's",
    };

    /// <summary>Folders whose every file belongs to a program (a wrapper's, an injector's).</summary>
    static readonly Dictionary<string, string> ProgramFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["reshade-shaders"] = "ReShade's shaders, a post-processing injector's",
        ["miles"] = "a plug-in folder of the game's sound library (Miles)",
        ["dgvoodoo"] = "dgVoodoo, a graphics wrapper",
    };

    /// <summary>
    /// The rules file (ours, the Classic game's <c>rules.cfg</c>): at the top of a module's data, the rules numbers it changes. It
    /// is data of the world's kind, so a module carrying one is a rules module (every machine of a network game must have it, and a
    /// save names it).
    /// </summary>
    public const string RulesFile = "rules.cfg";

    /// <summary>
    /// **One file of a pack, by its path from the pack's top** (<c>/</c> separated, the top folder already 1.1's spelling where it is
    /// one of 1.1's): code and program files refused, wherever they are; a <c>.nob</c> at the top an archive; the rules file
    /// (<see cref="RulesFile"/>) at the top data of the world's kind; a file under one of 1.1's top folders data of its kind;
    /// anything else a document.
    /// </summary>
    public static PackFile Classify(string path)
    {
        path = path.Replace('\\', '/').TrimStart('/');
        var segments = path.Split('/');
        string name = segments[^1], ext = Path.GetExtension(name);
        if (KnownPrograms.TryGetValue(name, out var known)) return new(PackFileClass.Program, 0, known);
        if (name.StartsWith("reshade", StringComparison.OrdinalIgnoreCase)) return new(PackFileClass.Program, 0, "ReShade, a post-processing injector");
        if (name.StartsWith("dgvoodoo", StringComparison.OrdinalIgnoreCase)) return new(PackFileClass.Program, 0, "dgVoodoo, a graphics wrapper");
        foreach (var s in segments[..^1])
            if (ProgramFolders.TryGetValue(s, out var folder)) return new(PackFileClass.Program, 0, folder);
        if (CodeExtensions.TryGetValue(ext, out var code)) return new(PackFileClass.Code, 0, code);
        if (ProgramExtensions.TryGetValue(ext, out var program)) return new(PackFileClass.Program, 0, program);
        if (segments.Length == 1 && name.Equals(RulesFile, StringComparison.OrdinalIgnoreCase))
            return new(PackFileClass.Data, PackKinds.World, "");
        if (segments.Length == 1)
            return ext.Equals(".nob", StringComparison.OrdinalIgnoreCase)
                ? new(PackFileClass.Archive, 0, "an archive of 1.1's kind")
                : new(PackFileClass.Document, 0, "a file beside the pack's data (a readme, settings, a picture)");
        if (TopFolder(segments[0]) is not { } top) return new(PackFileClass.Document, 0, $"not in one of 1.1's folders ({segments[0]})");
        if (top == "Codex") return new(PackFileClass.Code, 0, "the scripts' folder (Codex): script code");
        if (ext.Equals(".nob", StringComparison.OrdinalIgnoreCase)) return new(PackFileClass.Document, 0, "an archive inside a folder (1.1 mounts only those at the top)");
        // A .txt is a document but in misc\ (1.1's own), and the recooked text of a template, a scene or a camera path (Formats.Recooked).
        if (DocumentExtensions.Contains(ext) && !(top == "misc" && ext.Equals(".txt", StringComparison.OrdinalIgnoreCase)) && RecookedText(path) is null)
            return new(PackFileClass.Document, 0, "a readme, settings or a picture of the pack's (no file of 1.1's kind)");
        return new(PackFileClass.Data, KindOf(top, ext), "");
    }

    /// <summary>Extensions of files a pack carries for people, not the game (1.1's only such data is <c>misc\</c>'s one <c>.txt</c>).</summary>
    static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".md", ".rtf", ".pdf", ".doc", ".docx", ".htm", ".html", ".url", ".jpg", ".jpeg", ".png", ".gif", ".ini", ".cfg", ".log", ".nfo" };

    /// <summary>
    /// A data file's kind by its top folder (1.1's spelling) and extension: a model is the world's, as its bounding box is its thing's
    /// collision box and reach, and so is a model's motion, as it times what is done; the rest of <c>3d\</c> (textures, materials,
    /// skins' sets) is the look's.
    /// </summary>
    static PackKinds KindOf(string top, string ext)
    {
        if (top == "ui") return PackKinds.Interface;
        if (top == "3d") return ext.Equals(".nad", StringComparison.OrdinalIgnoreCase) || ext.Equals(".nod", StringComparison.OrdinalIgnoreCase)
            ? PackKinds.World : PackKinds.Look;
        if (LookFolders.Contains(top)) return PackKinds.Look;
        if (WorldFolders.Contains(top)) return PackKinds.World;
        return PackKinds.World;
    }

    /// <summary>What a data file is, in a word, for the lines (<c>a texture</c>, <c>a model</c>, …).</summary>
    public static string What(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".tga" => "a texture",
        ".nam" => "a material",
        ".nod" => "a model",
        ".nad" => "a motion",
        ".nms" => "a model's skin set",
        ".wav" or ".mp3" => "a sound",
        ".nls" or ".ntx" or ".nvo" => "text",
        ".bik" => "a film",
        ".nui" => "an interface layout",
        ".nfd" => "a font",
        ".not" => "templates",
        ".nil" => "a level",
        ".nsd" => "a scene",
        ".nsc" => "a chronicle",
        ".cfg" => "the rules",
        ".geometry" => "a scene's geometry",
        ".order" => "templates' order",
        ".txt" when RecookedText(path) is { } form => form switch
        {
            Recooked.Form.Templates => "a template",
            Recooked.Form.Scene => "a scene",
            _ => "a camera path",
        },
        _ => "a file",
    };

    /// <summary>
    /// What recooked text a file is by its path (<see cref="Recooked"/>): a template (<c>misc/Templates/&lt;file&gt;/&lt;name&gt;.txt</c>),
    /// a scene (<c>scenes/&lt;name&gt;.txt</c>) or a camera path (<c>paths/&lt;name&gt;.txt</c>); null for any other.
    /// </summary>
    public static Recooked.Form? RecookedText(string path)
    {
        var parts = path.Replace('\\', '/').TrimStart('/').Split('/');
        if (!parts[^1].EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return null;
        if (parts.Length == 4 && parts[0].Equals("misc", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("templates", StringComparison.OrdinalIgnoreCase))
            return Recooked.Form.Templates;
        if (parts.Length == 2 && parts[0].Equals("scenes", StringComparison.OrdinalIgnoreCase)) return Recooked.Form.Scene;
        if (parts.Length == 2 && parts[0].Equals("paths", StringComparison.OrdinalIgnoreCase)) return Recooked.Form.Path;
        return null;
    }
}

/// <summary>A pack the import knows by its name: the id, name and description it gets, and what the import says of it.</summary>
public sealed record KnownPack(string Key, string Id, string Name, string Description, string Note);

/// <summary>
/// **The packs the import recognises** by their folder's, zip's or archive's name (a small table; nothing of theirs is in Vitomir):
/// each gets a plain id and name instead of one made from its file name, its version from the name (<c>v1.0</c> is 1.0.0), and a note.
/// </summary>
public static class KnownPacks
{
    public static readonly KnownPack[] All =
    [
        new("neural-redemption", "neural-redemption", "Neural Redemption", "1.1's textures upscaled (a texture pack: each player's own looks)",
            "Neural Redemption: every texture replaces one of 1.1's, at up to 512 pixels; nothing of it changes the rules"),
        new("crimson-edition", "crimson-edition", "VTMR Crimson Edition", "textures, the interface's pictures and layouts, and text",
            "Crimson Edition: its own program (a patched Vampire.exe), its graphics wrappers (dgVoodoo, ReShade) and its sound plug-in are refused, "
            + "as Vitomir runs, draws and plays the game itself; its data is imported. Its status bar's level-up and vault buttons (sbLvlUp, sbVault) "
            + "name actions 1.1 does not have: when its StatusBarSP.nui is the one Vitomir recognises, they are bound to Vitomir's own (the advancement screen, the vault)"),
    ];

    /// <summary>The known pack a name is (its id-like form holding the key), or null.</summary>
    public static KnownPack? Of(string name)
    {
        string id = PlayerModuleImport.IdFrom(name);
        return All.FirstOrDefault(k => id.Contains(k.Key, StringComparison.Ordinal));
    }

    /// <summary>
    /// A version from a pack's name, or null when it names none: <c>…_v1.0</c>, <c>v2.1.3</c>; or a download's folder named
    /// <c>&lt;name&gt;-&lt;a&gt;-&lt;b&gt;-&lt;c&gt;</c> (a.b.c). A download from Nexus Mods is named
    /// <c>&lt;name&gt;-&lt;its mod id&gt;-&lt;its version, dashes for dots&gt;-&lt;the Unix time of its upload&gt;</c>, so with that trailing time
    /// (9 to 11 digits) the first number is the site's mod id and the rest the version: <c>VTMR Crimson Edition-12-1-0-1726673430</c>
    /// is Crimson Edition 1.0 (mod 12, uploaded 2024-09-18).
    /// </summary>
    public static Modules.ModuleVersion? VersionIn(string name)
    {
        static int N(string s) => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        var m = System.Text.RegularExpressions.Regex.Match(name, @"[vV](\d{1,4})(?:\.(\d{1,4}))?(?:\.(\d{1,4}))?(?!.*[vV]\d)");
        if (m.Success)
            return new Modules.ModuleVersion(N(m.Groups[1].Value), m.Groups[2].Success ? N(m.Groups[2].Value) : 0, m.Groups[3].Success ? N(m.Groups[3].Value) : 0);
        var nexus = System.Text.RegularExpressions.Regex.Match(name, @"\D-\d{1,7}((?:-\d{1,4}){1,3})-\d{9,11}$");
        if (nexus.Success)
        {
            var parts = nexus.Groups[1].Value.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(N).ToArray();
            return new Modules.ModuleVersion(parts[0], parts.Length > 1 ? parts[1] : 0, parts.Length > 2 ? parts[2] : 0);
        }
        var plain = System.Text.RegularExpressions.Regex.Match(name, @"\D-(\d{1,4})-(\d{1,4})-(\d{1,4})$");
        return plain.Success ? new Modules.ModuleVersion(N(plain.Groups[1].Value), N(plain.Groups[2].Value), N(plain.Groups[3].Value)) : null;
    }

    /// <summary>The first version named by <paramref name="names"/> (<see cref="VersionIn"/>), the pack's own folder's first.</summary>
    public static Modules.ModuleVersion? VersionIn(IEnumerable<string?> names) =>
        names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => VersionIn(n!)).FirstOrDefault(v => v is not null);
}
