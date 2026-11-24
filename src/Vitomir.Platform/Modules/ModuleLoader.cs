// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Config;
using Vitomir.Platform.Control;

namespace Vitomir.Platform.Modules;

/// <summary>What the loader is given: where modules are, the game, 1.1's install, the switches and profile asked for.</summary>
public sealed record ModuleLoadOptions
{
    /// <summary>Folders of module folders, in the order searched (the program's <c>Modules/</c>, then any <c>--modules</c>).</summary>
    public required IReadOnlyList<string> ModuleFolders { get; init; }

    public required GameList Game { get; init; }

    /// <summary>The player's 1.1 install behind <c>oneone:</c> ids, or null.</summary>
    public GameInstall? OneOne { get; init; }

    /// <summary>Switches given on the command line; each module is given those its manifest declares.</summary>
    public IReadOnlySet<string> Switches { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>A profile asked for by name, given to every module with code (null for each one's default).</summary>
    public string? Profile { get; init; }

    public Action<string> Say { get; init; } = _ => { };

    /// <summary>
    /// Module data values changed for this load only (a scenario's <c>overrides.data</c>), applied to the
    /// registry's reads before any module is configured, so each module reads and checks the changed data; none by default.
    /// </summary>
    public IReadOnlyList<DataOverride> DataOverrides { get; init; } = [];
}

/// <summary>A module as loaded: its manifest, its code (null for data only), the hash of its files, the switches it was given.</summary>
public sealed record LoadedModule(ModuleManifest Manifest, IModule? Code, string FilesHash, IReadOnlySet<string> Switches);

/// <summary>A game's modules loaded, checked and configured, in load order, with the registry they read through.</summary>
public sealed class LoadedGame
{
    internal LoadedGame(GameList game, IReadOnlyList<LoadedModule> modules, AssetRegistry assets)
    {
        Game = game;
        Modules = modules;
        Assets = assets;
    }

    public GameList Game { get; }

    public IReadOnlyList<LoadedModule> Modules { get; }

    public AssetRegistry Assets { get; }

    /// <summary>
    /// The module that keeps the game's own time and its loop (<see cref="IModule.Loop"/>), or null when the game runs
    /// in the platform's world (the enhanced game). The loader refuses a game with two.
    /// </summary>
    public (LoadedModule Module, IGameLoop Loop)? Loop =>
        Modules.FirstOrDefault(m => m.Code?.Loop is not null) is { } l ? (l, l.Code!.Loop!) : null;

    /// <summary>The modules with code, in load order.</summary>
    public IEnumerable<IModule> Code => Modules.Where(m => m.Code is not null).Select(m => m.Code!);

    /// <summary>Registers every module with code in load order (the digest's order).</summary>
    public void Register(WorldBuilder world)
    {
        foreach (var m in Modules) m.Code?.Register(world);
    }

    /// <summary>What the modules ask the program to show on screen while they run (the sandbox's warning), or null.</summary>
    public string? Banner
    {
        get
        {
            var lines = Code.Select(c => c.Banner).Where(b => !string.IsNullOrEmpty(b)).ToList();
            return lines.Count == 0 ? null : string.Join("   ", lines);
        }
    }

    /// <summary>The modules as a recording names them: ids, versions and the hashes of their files, in load order.</summary>
    public List<TapeModule> Recorded => [.. Modules.Select(m => new TapeModule(m.Manifest.Id, m.Manifest.Version.ToString(), m.FilesHash))];

    /// <summary>
    /// How these modules differ from the ones a recording names, plainly. <paramref name="sameModules"/> is false
    /// when the ids, versions or order differ (a replay would mean nothing); a difference only in the files'
    /// hashes leaves it true (the same versions rebuilt or edited: the digests decide).
    /// </summary>
    public IReadOnlyList<string> Differences(IReadOnlyList<TapeModule> recorded, out bool sameModules)
    {
        var said = new List<string>();
        sameModules = true;
        if (recorded.Count == 0)
        {
            said.Add("the recording names no modules (it was made before recordings named them); checking it against this game's");
            return said;
        }
        var here = Recorded;
        foreach (var r in recorded)
        {
            var l = here.FirstOrDefault(h => h.Id == r.Id);
            if (l is null) { said.Add($"it was recorded with module {r.Id} {r.Version}, which game {Game.Id} does not load here"); sameModules = false; }
            else if (l.Version != r.Version) { said.Add($"module {r.Id} is version {l.Version} here and was {r.Version} when recorded"); sameModules = false; }
            else if (l.Hash != r.Hash) said.Add($"module {r.Id} {r.Version}'s files differ from the recording's (hash {Short(l.Hash)} here, {Short(r.Hash)} recorded)");
        }
        foreach (var l in here)
            if (!recorded.Any(r => r.Id == l.Id)) { said.Add($"module {l.Id} {l.Version} is loaded here and was not when recorded"); sameModules = false; }
        if (sameModules && !here.Select(h => h.Id).SequenceEqual(recorded.Select(r => r.Id)))
        {
            said.Add($"the modules load in another order here ({string.Join(", ", here.Select(h => h.Id))}) than recorded ({string.Join(", ", recorded.Select(r => r.Id))})");
            sameModules = false;
        }
        return said;
    }

    static string Short(string hash) => hash.Length > 12 ? hash[..12] : hash;
}

/// <summary>
/// The loader (ours). It is the platform's, not the
/// program's, so the modules' own tests and tools load a module exactly as the program does, headless; the
/// program only says where to look. It finds the module folders, has the <see cref="ModuleChecker"/> check them
/// and order the game (fail closed, every problem named), logs every override applied, loads each module's
/// assembly, makes its <see cref="IModule"/>, checks that the code and the manifest agree on the version, and
/// configures each in load order with a <see cref="ModuleConfigContext"/> whose data is read through the registry
/// by id.
/// <para><b>Assemblies</b> load into the default load context: our own modules are built with the solution
/// against the same <c>Vitomir.Platform</c>, and one context keeps one <see cref="IModule"/> type. A module loaded from its
/// folder has its own libraries found in its <c>bin/</c> (<see cref="ModuleDependencies"/>). For mods
/// later, a context per module would need: a resolver that loads the module's own <c>bin/</c> dependencies
/// into its context while handing back the default context's <c>Vitomir.Platform</c> (so the interface types
/// stay one), a rule for two modules shipping different versions of one library, and unloading (a collectible
/// context) only if a game can drop a module without restarting.</para>
/// </summary>
public static class ModuleLoader
{
    /// <summary>The folder of module folders beside the program.</summary>
    public const string ModulesFolderName = "Modules";

    /// <summary>The folder of game files beside the program.</summary>
    public const string GamesFolderName = "Games";

    /// <summary>The game played when none is named.</summary>
    public const string DefaultGame = "enhanced";

    /// <summary>The program's own <c>Modules/</c>.</summary>
    public static string ProgramModules => Path.Combine(AppContext.BaseDirectory, ModulesFolderName);

    /// <summary>The program's own <c>Games/</c>.</summary>
    public static string ProgramGames => Path.Combine(AppContext.BaseDirectory, GamesFolderName);

    /// <summary>The base game's id (<c>Games/classic.json</c>): the one game of release 1's package.</summary>
    public const string ClassicGame = "classic";

    /// <summary>
    /// **The game a command line that names none plays**, by what <paramref name="gamesFolder"/> holds: null (the
    /// <see cref="DefaultGame"/>) when it holds the default's file or no base game's either; <see cref="ClassicGame"/> when it holds
    /// the base game's and not the default's, as release 1's package does (tools/package.ps1 leaves the enhanced game out), so the
    /// program started with no switch plays the base game there.
    /// </summary>
    public static string? WhenNoneNamed(string gamesFolder) =>
        !File.Exists(Path.Combine(gamesFolder, DefaultGame + ".json")) && File.Exists(Path.Combine(gamesFolder, ClassicGame + ".json"))
            ? ClassicGame : null;

    /// <summary>
    /// The game named by <paramref name="idOrFile"/>: a game file's path (<c>.json</c>), or an id looked up as
    /// <c>&lt;id&gt;.json</c> in each of <paramref name="gameFolders"/> in turn; <see cref="DefaultGame"/> when
    /// null. Throws <see cref="ModuleCheckException"/> naming what is wrong.
    /// </summary>
    public static GameList FindGame(string? idOrFile, IEnumerable<string> gameFolders)
    {
        var problems = new List<string>();
        string asked = idOrFile ?? DefaultGame;
        if (asked.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || asked.Contains('/') || asked.Contains('\\'))
            return GameList.Read(asked, problems) ?? throw new ModuleCheckException(problems);
        var folders = gameFolders.ToList();
        foreach (var folder in folders)
        {
            string path = Path.Combine(folder, asked + ".json");
            if (!File.Exists(path)) continue;
            var game = GameList.Read(path, problems) ?? throw new ModuleCheckException(problems);
            if (game.Id != asked) throw new ModuleCheckException([$"{game.File}: its id is '{game.Id}', not '{asked}' as its name says"]);
            return game;
        }
        throw new ModuleCheckException([$"no game '{asked}' (looked for {asked}.json in {string.Join(", ", folders.Select(Path.GetFullPath))})"]);
    }

    /// <summary>The checker alone (<c>--check-modules</c>): every problem of every module found and of the game.</summary>
    public static ModuleCheck Check(IReadOnlyList<string> moduleFolders, GameList game, GameInstall? oneOne = null) =>
        ModuleChecker.Check(moduleFolders, game, oneOne);

    /// <summary>
    /// Checks, orders, loads and configures the game's modules. Throws <see cref="ModuleCheckException"/> with
    /// every problem of the modules (checked, or found loading their code), or the first
    /// <see cref="ModuleRefusedException"/> a module's configuration gives (with a hint naming the switches its
    /// manifest declares).
    /// </summary>
    public static LoadedGame Load(ModuleLoadOptions options)
    {
        var say = options.Say;
        var check = ModuleChecker.Check(options.ModuleFolders, options.Game, options.OneOne);
        if (!check.Ok) throw new ModuleCheckException(check.Problems);
        foreach (var p in check.Elsewhere) say($"modules: not loaded, a problem: {p}");
        var registry = check.Assets!;
        say($"game: {options.Game.Id} ({options.Game.Name}), modules {string.Join(", ", check.Order.Select(m => $"{m.Id} {m.Version}"))}");
        foreach (var o in registry.Overrides) say(o.ToString());
        // The data overrides, applied before any module reads its data (a problem refuses the load).
        if (options.DataOverrides.Count > 0)
        {
            var dataProblems = new List<string>();
            registry.ApplyData(options.DataOverrides, dataProblems);
            if (dataProblems.Count > 0) throw new ModuleCheckException(dataProblems);
            foreach (var d in registry.DataOverrides) say(d.ToString());
        }

        // The code of each, all problems gathered before any is configured.
        var problems = new List<string>();
        var code = new Dictionary<string, IModule>(StringComparer.Ordinal);
        foreach (var m in check.Order)
        {
            if (!m.HasCode) continue;
            var made = Make(m, problems, say);
            if (made is not null) code[m.Id] = made;
        }
        if (problems.Count > 0) throw new ModuleCheckException(problems);

        var loaded = new List<LoadedModule>();
        foreach (var m in check.Order)
        {
            var given = new HashSet<string>(m.Switches.Select(s => s.Name).Where(options.Switches.Contains), StringComparer.Ordinal);
            var c = code.GetValueOrDefault(m.Id);
            if (c is not null)
            {
                var context = new ModuleConfigContext { Assets = registry.For(m.Id), Switches = given, Profile = options.Profile };
                try { c.Configure(context); }
                catch (ModuleRefusedException refused)
                {
                    // A module that names the switch that mends its refusal keeps its own hint (the enhanced module's
                    // other switches only turn things off); else every switch not given is hinted at, as before.
                    throw new ModuleRefusedException(refused.ModuleId, refused.Reasons)
                    {
                        Hint = m.Switches.Count == 0 || refused.Hint.Length > 0 ? refused.Hint
                            : string.Join("\n", m.Switches.Where(s => !given.Contains(s.Name)).Select(s => $"Start with --{s.Name} to {s.Help}.")),
                    };
                }
            }
            loaded.Add(new LoadedModule(m, c, FilesHash(m.Folder), given));
        }
        // At most one module keeps the game's time (IModule.Loop); two would each want every frame.
        var loops = loaded.Where(l => l.Code?.Loop is not null).Select(l => l.Manifest.Id).ToList();
        if (loops.Count > 1) throw new ModuleCheckException([$"game {options.Game.Id}: modules {string.Join(" and ", loops)} each offer their own frame loop; a game is run by one"]);
        return new LoadedGame(options.Game, loaded, registry);
    }

    /// <summary>A module's <see cref="IModule"/>, made from its assembly and type, or null with the problems named.</summary>
    static IModule? Make(ModuleManifest m, List<string> problems, Action<string> say)
    {
        string path = Path.GetFullPath(Path.Combine(m.Folder, m.Assembly!));
        Assembly assembly;
        try { assembly = LoadAssembly(path, m, say); }
        catch (Exception e) when (e is IOException or BadImageFormatException)
        {
            problems.Add($"module {m.Id}: its assembly {m.Assembly} could not be loaded ({e.Message})");
            return null;
        }
        var type = assembly.GetType(m.Type!, throwOnError: false);
        if (type is null) { problems.Add($"module {m.Id}: its assembly {m.Assembly} has no type {m.Type}"); return null; }
        if (!typeof(IModule).IsAssignableFrom(type) || type.IsAbstract)
        {
            problems.Add($"module {m.Id}: {m.Type} is not a module (it does not implement {typeof(IModule).FullName})");
            return null;
        }
        if (type.GetConstructor(Type.EmptyTypes) is null) { problems.Add($"module {m.Id}: {m.Type} has no public constructor without parameters"); return null; }
        IModule made;
        try { made = (IModule)Activator.CreateInstance(type)!; }
        catch (TargetInvocationException e) { problems.Add($"module {m.Id}: {m.Type} could not be made ({e.InnerException?.Message ?? e.Message})"); return null; }
        if (!ModuleVersion.TryParse(made.Info.Version, out var codeVersion) || codeVersion != m.Version)
        {
            problems.Add($"module {m.Id}: its code says version {made.Info.Version} and its {ModuleManifest.FileName} {m.Version}; they must agree");
            return null;
        }
        return made;
    }

    /// <summary>
    /// The module's assembly in the default load context. When an assembly of that name is already loaded, or is
    /// one of the program's own (a module's tests reference its project), that copy is used and the log says so.
    /// </summary>
    static Assembly LoadAssembly(string path, ModuleManifest m, Action<string> say)
    {
        var name = AssemblyName.GetAssemblyName(path);
        foreach (var a in AssemblyLoadContext.Default.Assemblies)
            if (string.Equals(a.GetName().Name, name.Name, StringComparison.Ordinal))
            {
                if (!string.Equals(a.Location, path, StringComparison.Ordinal)) say($"module {m.Id}: {name.Name} is already loaded from {a.Location}; that copy is used");
                else ModuleDependencies.Add(m.Id, Path.GetDirectoryName(path)!, say);   // loaded from its folder before: its log is this load's now
                return a;
            }
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa
            && tpa.Split(Path.PathSeparator).Any(p => string.Equals(Path.GetFileNameWithoutExtension(p), name.Name, StringComparison.Ordinal)))
        {
            var own = AssemblyLoadContext.Default.LoadFromAssemblyName(name);
            say($"module {m.Id}: {name.Name} is one of the program's own assemblies ({own.Location}); that copy is used");
            return own;
        }
        // The module's own libraries (its bin\: the Classic module's engine, scripting, IKVM and the rest) are found there.
        ModuleDependencies.Add(m.Id, Path.GetDirectoryName(path)!, say);
        return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }

    /// <summary>
    /// The hash of a module's files (what a recording names): SHA-256 over every file in its folder but debug
    /// symbols (<c>.pdb</c>), each as its path (relative, <c>/</c>) and its own SHA-256, in ordinal order of path.
    /// </summary>
    public static string FilesHash(string folder)
    {
        var entries = new List<(string Rel, string Full)>();
        foreach (var full in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (full.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            entries.Add((Path.GetRelativePath(folder, full).Replace('\\', '/'), full));
        }
        entries.Sort((a, b) => string.CompareOrdinal(a.Rel, b.Rel));
        var text = new StringBuilder();
        foreach (var (rel, full) in entries) text.Append(rel).Append('\n').Append(ContentHash.Sha256OfFile(full)).Append('\n');
        return ContentHash.Sha256Hex(text.ToString());
    }
}
