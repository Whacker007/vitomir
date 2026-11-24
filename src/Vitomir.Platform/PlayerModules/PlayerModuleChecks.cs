// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Formats;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.PlayerModules;

/// <summary>
/// **The game's files with the player modules layered over the base game's**, read as the engine's files read them (later wins; within a
/// module its loose files before its archives; then 1.1's), for the checks: who gives each path, what is under it, and a material's or a
/// texture's path found along 1.1's search path.
/// </summary>
public sealed class LayeredFiles : IDisposable
{
    /// <summary>The base game's layer's index (below every module).</summary>
    public const int Base = -1;

    readonly GameInstall? install;
    readonly List<PlayerModule> modules;
    readonly List<Dictionary<string, ModuleFileRecord>> files = [];
    readonly Dictionary<string, NobArchive> archives = new(StringComparer.Ordinal);

    /// <param name="install">The base game's files (null: none at hand, and nothing is found under the modules).</param>
    /// <param name="order">The modules on, in load order.</param>
    public LayeredFiles(GameInstall? install, IReadOnlyList<PlayerModule> order)
    {
        this.install = install;
        modules = [.. order];
        foreach (var m in modules)
        {
            var map = new Dictionary<string, ModuleFileRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in m.Record.Files.Where(f => f.In is null)) map.TryAdd(f.Path, f);
            foreach (var f in m.Record.Files.Where(f => f.In is not null)) map.TryAdd(f.Path, f);
            files.Add(map);
        }
    }

    public IReadOnlyList<PlayerModule> Modules => modules;

    /// <summary>The module's files by their 1.1 paths.</summary>
    public IReadOnlyDictionary<string, ModuleFileRecord> FilesOf(int layer) => files[layer];

    /// <summary>The top layer that has <paramref name="path"/> (a module's index, or <see cref="Base"/>), or null when none has it.</summary>
    public int? Owner(string path) => Below(path, modules.Count);

    /// <summary>The highest layer under <paramref name="above"/> that has <paramref name="path"/>, or null.</summary>
    public int? Below(string path, int above)
    {
        path = Normal(path);
        for (int i = Math.Min(above, modules.Count) - 1; i >= 0; i--)
            if (files[i].ContainsKey(path)) return i;
        return install?.Exists(path) == true ? Base : null;
    }

    public bool Exists(string path) => Owner(path) is not null;

    /// <summary>Who a layer is, for a line: the module's id, or <c>1.1</c>.</summary>
    public string Name(int layer) => layer == Base ? "1.1" : modules[layer].Id;

    /// <summary>The bytes of <paramref name="path"/> as layer <paramref name="layer"/> has it.</summary>
    public byte[]? Read(string path, int layer)
    {
        path = Normal(path);
        if (layer == Base) return install is not null && install.TryRead(path, out var d) ? d : null;
        if (!files[layer].TryGetValue(path, out var f)) return null;
        var m = modules[layer];
        if (f.In is null) return File.ReadAllBytes(Path.Combine(m.DataFolder, f.Path.Replace('/', Path.DirectorySeparatorChar)));
        string archive = Path.Combine(m.DataFolder, f.In);
        if (!archives.TryGetValue(archive, out var nob)) archives[archive] = nob = new NobArchive(archive);
        return nob.TryRead(path, out var bytes) ? bytes : null;
    }

    /// <summary>The bytes of <paramref name="path"/> as the game reads it (its top layer), or null.</summary>
    public byte[]? Read(string path) => Owner(path) is { } layer ? Read(path, layer) : null;

    /// <summary>A material's or a texture's path found as 1.1 finds one named without a folder: its search path's folders, then the name alone.</summary>
    public string? AlongSearchPath(string name)
    {
        name = Normal(name);
        foreach (var dir in GameInstall.MaterialSearchPath)
            if (Exists(dir + "/" + name)) return dir + "/" + name;
        return Exists(name) ? name : null;
    }

    /// <summary>A file found by its name alone in any layer, the top first (the engine's last way to find a texture), or null.</summary>
    public string? ByFileName(string fileName)
    {
        for (int i = modules.Count - 1; i >= 0; i--)
            foreach (var p in files[i].Keys)
                if (Path.GetFileName(p).Equals(fileName, StringComparison.OrdinalIgnoreCase)) return p;
        return install?.FindByFileName(fileName);
    }

    /// <summary>The base game's paths under <paramref name="folder"/>.</summary>
    public IReadOnlyList<string> BaseList(string folder) => install?.List(folder) ?? [];

    static string Normal(string path) => path.Replace('\\', '/').TrimStart('/');

    public void Dispose()
    {
        foreach (var a in archives.Values) a.Dispose();
        archives.Clear();
    }
}

/// <summary>
/// **The checks of the modules on, together** (each problem a line naming the module, the file and what to do;
/// <see cref="PlayerModuleProblem"/>). Errors, which refuse the start: a file that did not decode at its import; a model of a module's
/// whose material or texture is found nowhere after layering. Warnings, which the player accepts once for the combination: two modules
/// replacing the same file (which wins, by order); **made for**: a texture a module gives that a model of another module's uses, when
/// the texture was made for another base (its <c>madeFor</c>, or inferred: a module that ships no model using a texture of 1.1's it
/// replaces made it for 1.1's model), unless its author says it fits (<c>fits</c>); a replacement texture of another shape (aspect
/// ratio) than the one it replaces; a replacement without the alpha the one it replaces uses, where the material needs it (masked or
/// translucent); a module whose <c>kind</c> says less than its files are. The resolution's problems (<see cref="PlayerModuleResolver"/>)
/// come first.
/// </summary>
public static class PlayerModuleChecks
{
    /// <summary>A module's film that takes 1.1's out: empty, or not a Bink film (and an older import's record of one, as a problem).</summary>
    public static bool IsBlankedFilm(ModuleFileRecord f) =>
        f.Path.EndsWith(".bik", StringComparison.OrdinalIgnoreCase)
        && (f.Empty == true || f.NotFilm == true || f.Problem?.StartsWith("cannot be read as a Bink film", StringComparison.Ordinal) == true);

    /// <summary>A file that did not decode at its import (an error at every check), a blanked film apart.</summary>
    public static bool DoesNotRead(ModuleFileRecord f) => f.Problem is not null && !IsBlankedFilm(f);

    public static IReadOnlyList<PlayerModuleProblem> Run(GameInstall? install, IReadOnlyList<PlayerModule> order, CancellationToken cancel = default)
    {
        var problems = new List<PlayerModuleProblem>();
        if (order.Count == 0) return problems;
        using var layers = new LayeredFiles(install, order);

        // ---- each module's own: what did not decode; a kind said that is less than its files ---------------------------------------
        foreach (var m in order)
        {
            // A film taken out (empty, or not a Bink film: a blanked one) is a note; an older import's record named the latter a problem.
            foreach (var f in m.Record.Files.Where(IsBlankedFilm))
                problems.Add(new(ProblemLevel.Note, m.Id, f.Path,
                    $"{(f.Empty == true ? "it is an empty film" : "it is not a film")}: it takes 1.1's film out, so the game skips it", "") { Kind = "film-out" });
            var broken = m.Record.Files.Where(DoesNotRead).ToList();
            foreach (var f in broken)
                problems.Add(new(ProblemLevel.Error, m.Id, f.Path, $"{PackFiles.What(f.Path)} that {f.Problem}",
                    "the pack's file is damaged or not of 1.1's format: get the pack again, or turn the module off") { Kind = "decode" });
            if (m.Manifest.Kind is { } said && (m.Kinds & ~said) != 0)
                problems.Add(new(ProblemLevel.Warning, m.Id, PlayerModuleManifest.FileName,
                    $"it says it is {PackKindWords.Words(said)}, and its files are {PackKindWords.Words(m.Kinds)}" + (m.Rules ? ": it is taken as a rules module (every player of a network game needs it)" : ""),
                    "its files decide; its author can say so in its module.json") { Kind = "kind" });
        }

        // ---- interface actions: each on the list, on an element of a status bar, and not one of 1.1's own --------------------------------
        problems.AddRange(Actions(layers, order));

        // ---- two modules replacing one file ------------------------------------------------------------------------------------------
        for (int b = 1; b < order.Count; b++)
            for (int a = 0; a < b; a++)
            {
                var both = layers.FilesOf(b).Keys.Where(layers.FilesOf(a).ContainsKey).Order(StringComparer.OrdinalIgnoreCase).ToList();
                if (both.Count == 0) continue;
                problems.Add(new(ProblemLevel.Warning, order[b].Id, both[0],
                    $"{order[a].Id} and {order[b].Id} both give {Count(both.Count, "file")}: {order[b].Id}'s {(both.Count == 1 ? "is" : "are")} used, as it loads later",
                    $"to use {order[a].Id}'s, put it after {order[b].Id} (module-order)") { Kind = "overlap", Files = both });
            }

        cancel.ThrowIfCancellationRequested();
        var textures = new TextureFacts(layers);

        // ---- models: their materials and textures found, and the textures made for them ----------------------------------------------
        var madeFor = new Dictionary<(int Texture, int Model), List<string>>();
        for (int i = 0; i < order.Count; i++)
        {
            var missing = new List<string>();
            foreach (var model in layers.FilesOf(i).Values.Where(f => f.Path.EndsWith(".nod", StringComparison.OrdinalIgnoreCase) && f.Problem is null))
            {
                cancel.ThrowIfCancellationRequested();
                if (layers.Owner(model.Path) != i) continue;                                   // a later module's model is used instead
                var uses = TexturesOf(layers, model.Materials ?? [], out var notFound);
                if (notFound.Count > 0 && install is not null && install.TryRead(model.Path, out var original))
                {
                    // 1.1's own model of that path missing the same material is 1.1's gap, not the module's.
                    try
                    {
                        using var baseOnly = new LayeredFiles(install, []);
                        TexturesOf(baseOnly, NodModel.Read(original).Materials, out var baseMissing);
                        notFound.RemoveAll(baseMissing.Contains);
                    }
                    catch (Exception e) when (e is not OutOfMemoryException) { }
                }
                missing.AddRange(notFound.Select(n => $"{model.Path} ({n})"));
                foreach (var texture in uses)
                {
                    if (layers.Owner(texture) is not { } t || t == i || t == LayeredFiles.Base) continue;
                    var maker = order[t];
                    if (maker.Manifest.FitsWith(order[i].Id, texture)) continue;
                    if (maker.Manifest.MadeFor is { } bases && bases.Contains(order[i].Id)) continue;
                    if (!madeFor.TryGetValue((t, i), out var list)) madeFor[(t, i)] = list = [];
                    list.Add($"{texture} ({Path.GetFileName(model.Path)})");
                }
            }
            if (missing.Count > 0)
                problems.Add(new(ProblemLevel.Error, order[i].Id, missing[0],
                    $"{Count(missing.Count, "model")} {(missing.Count == 1 ? "uses a material or texture" : "use materials or textures")} found nowhere, in 1.1's files or the enabled modules (named in brackets)",
                    "the pack is missing files, or needs another module on: get the pack whole, or turn it off") { Kind = "missing", Files = missing });
        }
        foreach (var ((t, i), list) in madeFor.OrderBy(k => k.Key.Texture).ThenBy(k => k.Key.Model))
        {
            var maker = order[t];
            string basis = maker.Manifest.MadeFor is { Count: > 0 } said && !said.Contains(AssetId.OneOne)
                ? $"{string.Join(" and ", said)}'s models" : "1.1's models";
            var first = list[0];
            string texture = first[..first.IndexOf(' ')], model = first[(first.IndexOf('(') + 1)..^1];
            problems.Add(new(ProblemLevel.Warning, maker.Id, first,
                list.Count == 1
                    ? $"{maker.Id}'s {texture} was made for {(basis == "1.1's models" ? "1.1's " + model : basis)}, but {order[i].Id} replaces {model}: the picture may not fit the model"
                    : $"{maker.Id}'s textures ({list.Count}) were made for {basis}, but {order[i].Id} replaces the models that use them: the pictures may not fit the models",
                $"if they fit, {maker.Id}'s author can say so (\"fits\": {{\"{order[i].Id}\": [...]}} in its module.json); else turn one of them off")
                { Kind = "made-for", Files = list });
        }

        // ---- replacement textures: their shape, and their alpha --------------------------------------------------------------------
        for (int i = 0; i < order.Count; i++)
        {
            var shape = new List<string>();
            var alpha = new List<string>();
            foreach (var f in layers.FilesOf(i).Values.Where(f => f.Width is > 0 && f.Height is > 0))
            {
                cancel.ThrowIfCancellationRequested();
                if (layers.Below(f.Path, i) is not { } under) continue;                            // a texture of its own, replacing nothing
                if (textures.Of(f.Path, under) is not { } old) continue;
                if ((long)f.Width!.Value * old.Height != (long)f.Height!.Value * old.Width)
                    shape.Add($"{f.Path} ({f.Width}x{f.Height} over {old.Width}x{old.Height})");
                if (f.AlphaUsed != true && textures.AlphaUsed(f.Path, under) && textures.NeedsAlpha(f.Path))
                    alpha.Add(f.Path);
            }
            if (shape.Count > 0)
                problems.Add(new(ProblemLevel.Warning, order[i].Id, shape[0],
                    $"{Count(shape.Count, "texture")} of another shape (aspect ratio) than the {(shape.Count == 1 ? "one it replaces" : "ones they replace")}: a model's or a wall's picture may be stretched",
                    "accept it if the game looks right; else turn the module off") { Kind = "aspect", Files = shape });
            if (alpha.Count > 0)
                problems.Add(new(ProblemLevel.Warning, order[i].Id, alpha[0],
                    $"{Count(alpha.Count, "texture")} without the alpha (transparency) the {(alpha.Count == 1 ? "one it replaces uses" : "ones they replace use")}, where the material is masked or translucent: those parts will be solid",
                    "accept it if the game looks right; else turn the module off") { Kind = "alpha", Files = alpha });
        }
        return problems;
    }

    /// <summary>
    /// **The interface bindings' checks**: an action not on Vitomir's list (<see cref="InterfaceActions"/>) is an error, the module refused
    /// until it is mended; a binding of an element 1.1's status bars have is a note (the element keeps 1.1's behaviour); an element no
    /// status bar layout of the game (after layering) has is a note (the binding does nothing in this release); two modules binding one
    /// element is a note (the later one's binding is used).
    /// </summary>
    static IEnumerable<PlayerModuleProblem> Actions(LayeredFiles layers, IReadOnlyList<PlayerModule> order)
    {
        if (!order.Any(m => m.Manifest.Actions.Count > 0)) yield break;
        var barElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layout in InterfaceActions.BarLayouts)
            if (layers.Read(layout) is { } bytes)
                try { foreach (var e in NuiPane.Read(bytes, layout).Elements) barElements.Add(e.Name); }
                catch (Exception e) when (e is FormatException or InvalidDataException or ArgumentException) { }
        var boundBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in order)
            foreach (var (element, name) in m.Manifest.Actions)
            {
                if (InterfaceActions.Find(name) is null)
                {
                    yield return new(ProblemLevel.Error, m.Id, PlayerModuleManifest.FileName,
                        $"it binds the element {element} to \"{name}\", which is not one of Vitomir's interface actions ({string.Join(", ", InterfaceActions.All.Select(a => a.Name))})",
                        "its author can bind it to one of those; else turn the module off") { Kind = "action" };
                    continue;
                }
                if (InterfaceActions.OneOneBarElements.Contains(element))
                {
                    yield return new(ProblemLevel.Note, m.Id, PlayerModuleManifest.FileName,
                        $"{element} is one of 1.1's own status bar elements: it keeps 1.1's behaviour, and its binding to {name} is not used", "") { Kind = "action" };
                    continue;
                }
                if (!barElements.Contains(element))
                    yield return new(ProblemLevel.Note, m.Id, PlayerModuleManifest.FileName,
                        $"no status bar layout of the game with these modules has an element {element}: its binding to {name} does nothing", "") { Kind = "action" };
                if (boundBy.TryGetValue(element, out var earlier))
                    yield return new(ProblemLevel.Note, m.Id, PlayerModuleManifest.FileName,
                        $"{earlier} binds {element} too: {m.Id}'s binding ({name}) is used, as it loads later", "") { Kind = "action" };
                boundBy[element] = m.Id;
            }
    }

    static string Count(int n, string what) => n == 1 ? $"1 {what}" : $"{n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} {what}s";

    /// <summary>
    /// The texture paths a model's materials use, found as the engine finds them: each material's <c>.nam</c>, else its <c>.tga</c>, along
    /// 1.1's search path; a material's textures along the search path, then in the material's folder, then by name in any layer.
    /// Names neither is found for are put in <paramref name="notFound"/>.
    /// </summary>
    public static IReadOnlyCollection<string> TexturesOf(LayeredFiles layers, IEnumerable<string> materials, out List<string> notFound)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        notFound = [];
        foreach (var material in materials.Where(m => m.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (layers.AlongSearchPath(material + ".nam") is { } nam)
            {
                NamMaterial parsed;
                try { parsed = NamMaterial.Read(layers.Read(nam) ?? [], nam); }
                catch (Exception e) when (e is not OutOfMemoryException) { continue; }
                string folder = nam[..(nam.LastIndexOf('/') + 1)];
                foreach (var t in Named(parsed))
                {
                    string name = t.Contains('.') ? t : t + ".tga";
                    if ((layers.AlongSearchPath(name) ?? (layers.Exists(folder + name) ? folder + name : null) ?? layers.ByFileName(Path.GetFileName(name))) is { } path)
                        found.Add(path);
                    else notFound.Add(name);
                }
            }
            else if (layers.AlongSearchPath(material + ".tga") is { } tga) found.Add(tga);
            else notFound.Add(material);
        }
        return found;
    }

    /// <summary>A material's textures: its frames, its passes' (but the light maps), its glow and its detail.</summary>
    internal static IEnumerable<string> Named(NamMaterial nam) =>
        nam.Textures.Concat(nam.Passes.Select(p => p.Texture)).Concat(new[] { nam.Glow, nam.Detail }.OfType<string>())
           .Where(t => t.Length > 0 && !t.StartsWith('$')).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the checks learn of textures under the modules' (their size, their alpha) and of the materials that use them, each once.</summary>
    sealed class TextureFacts(LayeredFiles layers)
    {
        readonly Dictionary<(string, int), (int Width, int Height, int Bits)?> headers = [];
        readonly Dictionary<(string, int), bool> alphaUsed = [];
        Dictionary<string, List<NamMaterial>>? byTexture;

        public (int Width, int Height, int Bits)? Of(string path, int layer)
        {
            if (headers.TryGetValue((path, layer), out var known)) return known;
            (int, int, int)? header = null;
            if (layer != LayeredFiles.Base && layers.FilesOf(layer).TryGetValue(path, out var f) && f.Width is { } w && f.Height is { } h)
                header = (w, h, f.Alpha == true ? 32 : 24);
            else if (layers.Read(path, layer) is { Length: >= 18 } data)
                header = (data[12] | data[13] << 8, data[14] | data[15] << 8, data[16]);
            return headers[(path, layer)] = header is { Item1: > 0, Item2: > 0 } ? header : null;
        }

        public bool AlphaUsed(string path, int layer)
        {
            if (alphaUsed.TryGetValue((path, layer), out var known)) return known;
            bool used = false;
            if (layer != LayeredFiles.Base && layers.FilesOf(layer).TryGetValue(path, out var f)) used = f.AlphaUsed == true;
            else if (Of(path, layer) is { Bits: 32 } && layers.Read(path, layer) is { } data)
                try
                {
                    var image = TgaImage.Read(data, path);
                    for (int i = 3; i < image.Rgba.Length && !used; i += 4) used = image.Rgba[i] != 255;
                }
                catch (Exception e) when (e is not OutOfMemoryException) { }
            return alphaUsed[(path, layer)] = used;
        }

        /// <summary>
        /// Whether a material that draws <paramref name="path"/> needs its alpha: one that is masked, or draws it as its first picture
        /// blended by its alpha (the engine blends a picture with alpha that no <c>.nam</c> makes additive); a texture no material names is
        /// drawn as a plain material of its own, blended by its alpha. Only every material naming it being additive or subtractive spares it.
        /// </summary>
        public bool NeedsAlpha(string path)
        {
            byTexture ??= Index();
            if (!byTexture.TryGetValue(Path.GetFileName(path), out var using_)) return true;
            return using_.Any(n => n.Masked || !(n.AddBlend || n.SubBlend));
        }

        Dictionary<string, List<NamMaterial>> Index()
        {
            var index = new Dictionary<string, List<NamMaterial>>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var all = new List<string>();
            for (int i = 0; i < layers.Modules.Count; i++) all.AddRange(layers.FilesOf(i).Keys);
            all.AddRange(BaseMaterials());
            foreach (var nam in all.Where(p => p.EndsWith(".nam", StringComparison.OrdinalIgnoreCase)))
            {
                if (!seen.Add(nam) || layers.Read(nam) is not { } data) continue;
                NamMaterial parsed;
                try { parsed = NamMaterial.Read(data, nam); }
                catch (Exception e) when (e is not OutOfMemoryException) { continue; }
                foreach (var t in Named(parsed))
                {
                    string name = Path.GetFileName(t.Contains('.') ? t : t + ".tga");
                    if (!index.TryGetValue(name, out var list)) index[name] = list = [];
                    list.Add(parsed);
                }
            }
            return index;
        }

        IEnumerable<string> BaseMaterials() => layers.BaseList("");
    }
}
