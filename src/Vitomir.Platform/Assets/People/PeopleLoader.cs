// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The contract's PeopleLibrary filled — the models repository's bodies, outfits, hair and motions read from its
// repository (or a package's copy) into the platform's SkinnedModel, Skeleton and clips.
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Vitomir.Formats.Gltf;
using Vitomir.Formats.Npy;
using Vitomir.Formats.Png;

namespace Vitomir.Platform.Assets.People;

public sealed partial class PeopleLibrary
{
    /// <summary>Where the models repository is looked for when no folder, setting or <see cref="FolderVariable"/> names one.</summary>
    public const string DefaultFolder = @"C:\Games\Vitomir\models";

    /// <summary>The environment variable that names the folder when none is given (the tests read it too).</summary>
    public const string FolderVariable = "VITOMIR_MODELS_DIR";

    readonly object gate = new();
    readonly object building = new();
    readonly Dictionary<string, Lazy<PersonModel>> people = new(StringComparer.Ordinal);
    readonly Dictionary<string, Lazy<IReadOnlyList<INotedClip>>> motions = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Every image decoded, by its name; and each image's name by its PNG's content (<see cref="PngDecoder.ContentKey"/>), so an image that repeats across files (the tiles, the hair's strands) is decoded and kept once.</summary>
    readonly Dictionary<string, TextureImage> textures = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> textureByContent = new(StringComparer.Ordinal);
    // An image a file refers to by its uri (the crowd/shared/), by its full path: read and named once for every person.
    readonly Dictionary<string, string> textureByFile = new(StringComparer.Ordinal);
    readonly List<string> log = [];

    /// <summary>A line for each load: what was built, its size and what the time went on.</summary>
    public IReadOnlyList<string> Log { get { lock (log) return log.ToArray(); } }

    /// <summary>
    /// Whether people's images are decoded (the default). Without them a person has its parts' texture names (the same
    /// names) and no <see cref="PersonModel.Textures"/>: what the simulation, a headless run or a test of shapes and
    /// motions needs, without the 300 MB of maps a person holds and the second it takes to decode them.
    /// </summary>
    public bool DecodeImages { get; init; } = true;

    /// <summary><see cref="TryOpen(string, out string)"/>, decoding people's images or not (<see cref="DecodeImages"/>).</summary>
    public static PeopleLibrary? TryOpen(string folder, out string why, bool decodeImages) => TryOpen(folder, out why, decodeImages, 0);

    /// <summary><see cref="TryOpen(string, out string, bool)"/>, with images capped at <paramref name="maxTextureSide"/> (<see cref="MaxTextureSide"/>; 0: none).</summary>
    public static PeopleLibrary? TryOpen(string folder, out string why, bool decodeImages, int maxTextureSide) =>
        Open(folder, out why) is { } library
            ? new PeopleLibrary { Folder = library.Folder, Commit = library.Commit, DecodeImages = decodeImages, MaxTextureSide = Math.Max(0, maxTextureSide) }
            : null;

    /// <summary>
    /// The largest side an image is kept at (0, the default: as the file has it): one larger is halved as it is decoded,
    /// by the filter the renderer caps a character's texture with on upload (<see cref="TextureHalving"/>, a base colour in linear
    /// light), so a session that draws at the player's <c>characterTextureLimit</c> keeps the 4096 maps at 2048 (a quarter of the
    /// memory, the same pixels on the GPU). The images a person's base colour names are halved as colours, the rest as data.
    /// </summary>
    public int MaxTextureSide { get; init; }

    /// <summary>The bytes of every image decoded and kept (RGBA, eight bits a channel).</summary>
    public long TextureBytes { get { lock (gate) return textures.Values.Sum(t => (long)t.Rgba.Length); } }

    void Note(string line) { lock (log) log.Add(line); }

    /// <summary>The lines said once (<see cref="NoteOnce"/>).</summary>
    readonly HashSet<string> said = new(StringComparer.Ordinal);

    /// <summary><paramref name="line"/> in the log the first time it is said.</summary>
    internal void NoteOnce(string line) { lock (said) if (!said.Add(line)) return; Note(line); }

    /// <summary>
    /// The models repository's files: <paramref name="folder"/> when one is named, else the folder <see cref="FolderVariable"/>
    /// names, else <see cref="DefaultFolder"/>; null with the reason when it does not hold them.
    /// </summary>
    public static PeopleLibrary? Locate(string? folder, out string why, bool decodeImages = true, int maxTextureSide = 0)
    {
        string? chosen = !string.IsNullOrWhiteSpace(folder) ? folder
            : Environment.GetEnvironmentVariable(FolderVariable) is { Length: > 0 } env ? env : DefaultFolder;
        return TryOpen(chosen, out why, decodeImages, maxTextureSide);
    }

    static PeopleLibrary? Open(string folder, out string why)
    {
        if (string.IsNullOrWhiteSpace(folder)) { why = "no folder named"; return null; }
        string full;
        try { full = Path.GetFullPath(folder); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { why = $"{folder}: {e.Message}"; return null; }
        if (!Directory.Exists(full)) { why = $"{full} does not exist"; return null; }
        foreach (var sub in new[] { "characters", "motions" })
            if (!Directory.Exists(Path.Combine(full, "assets", sub)))
            {
                why = $"{full} has no assets/{sub}/: not the models repository's files";
                return null;
            }
        why = "";
        return new PeopleLibrary { Folder = full, Commit = ReadCommit(full) };
    }

    /// <summary>
    /// The commit the files came from: the repository's <c>.git</c> (a folder, or a worktree's file naming one), its
    /// <c>HEAD</c> followed to the ref's file or <c>packed-refs</c> (no git process), else a package's <c>source.json</c>
    /// (<c>{"commit": "…"}</c>); null when neither says.
    /// </summary>
    internal static string? ReadCommit(string folder)
    {
        try
        {
            string git = Path.Combine(folder, ".git");
            string? gitDir = Directory.Exists(git) ? git : File.Exists(git) ? GitDirFromFile(folder, git) : null;
            if (gitDir is not null && File.Exists(Path.Combine(gitDir, "HEAD")))
            {
                string head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
                if (!head.StartsWith("ref:", StringComparison.Ordinal)) return IsHash(head) ? head : null;
                string name = head[4..].Trim();
                string common = gitDir;
                string commondir = Path.Combine(gitDir, "commondir");
                if (File.Exists(commondir)) common = Path.GetFullPath(Path.Combine(gitDir, File.ReadAllText(commondir).Trim()));
                foreach (var dir in new[] { gitDir, common })
                {
                    string file = Path.Combine(dir, name.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(file) && File.ReadAllText(file).Trim() is var hash && IsHash(hash)) return hash;
                }
                foreach (var dir in new[] { gitDir, common })
                {
                    string packed = Path.Combine(dir, "packed-refs");
                    if (!File.Exists(packed)) continue;
                    foreach (var line in File.ReadLines(packed))
                    {
                        if (line.Length == 0 || line[0] is '#' or '^') continue;
                        int space = line.IndexOf(' ');
                        if (space > 0 && line[(space + 1)..].Trim() == name && IsHash(line[..space])) return line[..space];
                    }
                }
                return null;
            }
            string source = Path.Combine(folder, "source.json");
            if (File.Exists(source))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(source));
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("commit", out var c) && c.ValueKind == JsonValueKind.String)
                    return c.GetString();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return null;
    }

    static string? GitDirFromFile(string folder, string file)
    {
        string text = File.ReadAllText(file).Trim();
        if (!text.StartsWith("gitdir:", StringComparison.Ordinal)) return null;
        string dir = text[7..].Trim();
        return Path.GetFullPath(Path.IsPathRooted(dir) ? dir : Path.Combine(folder, dir));
    }

    static bool IsHash(string s) => s.Length is 40 or 64 && s.All(Uri.IsHexDigit);

    PersonModel Build(PersonRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        Lazy<PersonModel> lazy;
        lock (gate)
            if (!people.TryGetValue(recipe.Key, out lazy!))
                people[recipe.Key] = lazy = new Lazy<PersonModel>(() =>
                {
                    // A person with a look is the plain build of its files (built once, shared) with the look laid on it.
                    if (recipe.Look is { } look) return PersonLooks.Apply(Build(recipe with { Look = null }), recipe, look, Note);
                    lock (building) return new PersonBuilder(this, recipe).Build();
                });
        try { return lazy.Value; }
        catch
        {
            lock (gate) people.Remove(recipe.Key);
            throw;
        }
    }

    IReadOnlyList<INotedClip> LoadMotions(string sex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sex);
        Lazy<IReadOnlyList<INotedClip>> lazy;
        lock (gate)
            if (!motions.TryGetValue(sex, out lazy!))
                motions[sex] = lazy = new Lazy<IReadOnlyList<INotedClip>>(() =>
                {
                    string path = Path.Combine(Folder, "assets", "motions", $"motions_{sex}.glb");
                    if (!File.Exists(path)) throw new FileNotFoundException($"no motions for \"{sex}\" in the models repository's files", path);
                    var watch = Stopwatch.StartNew();
                    var file = GltfFile.Load(path);
                    long read = watch.ElapsedMilliseconds;
                    var clips = GltfClip.FromFile(file);
                    Note($"motions_{sex}: {clips.Length} clips in {watch.ElapsedMilliseconds} ms (the file read and parsed {read} ms, the clips made {watch.ElapsedMilliseconds - read} ms)");
                    return clips;
                });
        try { return lazy.Value; }
        catch
        {
            lock (gate) motions.Remove(sex);
            throw;
        }
    }

    /// <summary>
    /// The models repository's crowd (<c>assets/characters/crowd/crowd.json</c>), read once; empty when the folder has none.
    /// Throws <see cref="InvalidDataException"/> (or <see cref="JsonException"/>) when the file is not a crowd.
    /// </summary>
    public IReadOnlyList<CrowdPerson> Crowd()
    {
        string path = global::Vitomir.Platform.Assets.People.Crowd.PathIn(Folder);
        lock (gate)
        {
            if (crowdRead is { } have) return have;
            crowdRead = File.Exists(path) ? global::Vitomir.Platform.Assets.People.Crowd.Read(File.ReadAllText(path)) : [];
            Note($"crowd: {crowdRead.Count} people in {path}");
            return crowdRead;
        }
    }

    IReadOnlyList<CrowdPerson>? crowdRead;

    /// <summary>The <c>.glb</c> of a worn file by its stem (<see cref="FindWorn"/>), for a package's list of what is used.</summary>
    public string? FindWornPath(string stem) => FindWorn(stem);

    /// <summary>The <c>.glb</c> of a worn file by its stem: the first under <c>assets/outfits/</c> (any folder), else under <c>assets/hair/</c>.</summary>
    internal string? FindWorn(string stem)
    {
        foreach (var where in new[] { "outfits", "hair" })
        {
            string dir = Path.Combine(Folder, "assets", where);
            if (!Directory.Exists(dir)) continue;
            var found = Directory.EnumerateFiles(dir, stem + ".glb", SearchOption.AllDirectories).Order(StringComparer.Ordinal).FirstOrDefault();
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>The <c>.glb</c> of a base body: <c>assets/characters/&lt;base&gt;/&lt;base&gt;.glb</c>, else anywhere under <c>assets/characters/</c>.</summary>
    internal string? FindBase(string stem)
    {
        string path = Path.Combine(Folder, "assets", "characters", stem, stem + ".glb");
        if (File.Exists(path)) return path;
        string dir = Path.Combine(Folder, "assets", "characters");
        return Directory.EnumerateFiles(dir, stem + ".glb", SearchOption.AllDirectories).Order(StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>
    /// The name an image is kept under: its first file's <c>people:&lt;stem&gt;/&lt;image name&gt;</c>, the same for the
    /// same bytes wherever they come again. A new one is put in <paramref name="pending"/> to be decoded.
    /// </summary>
    internal string TextureName(string stem, string imageName, ArraySegment<byte> png, List<(string Name, ArraySegment<byte> Png, bool Colour)> pending, bool colour)
    {
        string hash = PngDecoder.ContentKey(png) ?? throw new InvalidDataException($"{stem}: the image {imageName} is not a PNG");
        lock (gate)
        {
            if (textureByContent.TryGetValue(hash, out var have)) return have;
            string name = $"people:{stem}/{imageName}";
            for (int n = 2; textures.ContainsKey(name) || textureByContent.ContainsValue(name); n++) name = $"people:{stem}/{imageName}#{n}";
            textureByContent[hash] = name;
            pending.Add((name, png, colour));
            return name;
        }
    }

    /// <summary>The name already given to the image in the file at <paramref name="path"/> (a glb's <c>../shared/</c> map), or null.</summary>
    internal string? TextureOfFile(string path) { lock (gate) return textureByFile.TryGetValue(path, out var name) ? name : null; }

    /// <summary>The image in the file at <paramref name="path"/> is named <paramref name="name"/>.</summary>
    internal void NoteTextureFile(string path, string name) { lock (gate) textureByFile[path] = name; }

    internal void AddTexture(TextureImage image) { lock (gate) textures[image.Name] = image; }

    /// <summary>Names given to images that were then not decoded (a build that failed).</summary>
    internal void Forget(IEnumerable<string> names)
    {
        lock (gate)
            foreach (var name in names)
            {
                foreach (var hash in textureByContent.Where(kv => kv.Value == name).Select(kv => kv.Key).ToArray()) textureByContent.Remove(hash);
                foreach (var file in textureByFile.Where(kv => kv.Value == name).Select(kv => kv.Key).ToArray()) textureByFile.Remove(file);
            }
    }

    internal TextureImage Texture(string name) { lock (gate) return textures[name]; }

    internal void Report(string line) => Note(line);
}

/// <summary>
/// One person put together: the base body's file and each worn file's, on one skeleton, in the platform's space.
/// </summary>
sealed class PersonBuilder(PeopleLibrary library, PersonRecipe recipe)
{
    /// <summary>A file read: its stem, what it is (<c>base</c>, <c>outfit</c>, <c>hair</c>, <c>beard</c>) and its fit data's hidden triangles.</summary>
    sealed record Source(string Stem, string Kind, GltfFile File, bool CoversHair, int[] Hidden)
    {
        /// <summary>A hair's or a beard's vertices' places in the order they grey, by their position in the file (glTF's).</summary>
        public Dictionary<Vector3, float>? Grey { get; init; }
    }

    readonly List<BoneInfo> bones = [];
    readonly List<Matrix4x4> inverseBind = [];
    readonly Dictionary<string, int> boneIndex = new(StringComparer.Ordinal);
    readonly Dictionary<(GltfFile, int), int[]> skinRemap = [];
    readonly List<SkinVertex> vertices = [];
    readonly List<Vector4> tangents = [];
    readonly List<Vector4> colors = [];
    readonly List<float> hairGrey = [];
    bool anyGrey;
    readonly List<uint> indices = [];
    readonly List<ModelPart> parts = [];
    readonly Dictionary<string, PersonSocket> sockets = new(StringComparer.Ordinal);
    readonly List<string> textureNames = [];
    readonly List<(string Name, ArraySegment<byte> Png, bool Colour)> pending = [];
    readonly Dictionary<(GltfFile, int), string?> imageNames = [];
    bool anyColour;

    public PersonModel Build()
    {
        var watch = Stopwatch.StartNew();
        string basePath = (recipe.Folder is not null ? Baked(recipe.Base) : null) ?? library.FindBase(recipe.Base)   // a baked person's own body first
            ?? throw new FileNotFoundException($"no base body {recipe.Base} under {library.Folder}/assets/characters");
        // The files read and parsed side by side.
        var baseFile = Task.Run(() => GltfFile.Load(basePath));
        var wornFiles = recipe.Wear.Select(stem => Task.Run(() => Worn(stem))).ToArray();
        var sources = new List<Source> { new(recipe.Base, "base", baseFile.GetAwaiter().GetResult(), false, []) };
        foreach (var w in wornFiles) sources.Add(w.GetAwaiter().GetResult());
        long read = watch.ElapsedMilliseconds;

        // An outfit that covers the hair (a hood, a head-cloth) leaves hair out; a beard stays.
        bool coversHair = sources.Any(s => s.CoversHair);
        var left = coversHair ? sources.Where(s => s.Kind == "hair").Select(s => s.Stem).ToArray() : [];
        if (left.Length > 0) sources.RemoveAll(s => s.Kind == "hair");

        // The images every part will name, found first so they decode (largest first, on every core) while the rest is built.
        var decoded = Array.Empty<TextureImage>();
        Task[] decoding = [];
        int hiddenCount = 0, hiddenOutside = 0, bodyTriangles = 0;
        long built;
        try
        {
            foreach (var s in sources) NameImages(s);
            if (library.DecodeImages)
            {
                decoded = new TextureImage[pending.Count];
                decoding = Decode(decoded);
            }

            // One skeleton: the base's joints in their order, then each worn file's that the base lacks.
            foreach (var s in sources) AddSkins(s.File);
            if (bones.Count > 256) throw new InvalidDataException($"{recipe.Name}: {bones.Count} bones, more than a SkinVertex can name (256)");

            // The body's triangles that something worn hides: the union of the fit files' lists.
            bodyTriangles = BodyTriangles(sources[0].File);
            var hidden = new bool[bodyTriangles];
            foreach (var s in sources)
                foreach (int t in s.Hidden)
                {
                    if ((uint)t >= (uint)bodyTriangles) { hiddenOutside++; continue; }
                    if (!hidden[t]) { hidden[t] = true; hiddenCount++; }
                }

            int scheduled = pending.Count;
            foreach (var s in sources) AddMeshes(s, s.Kind == "base" ? hidden : null);
            foreach (var s in sources) AddSockets(s.File);
            Debug.Assert(pending.Count == scheduled, "an image named after the decoding began");
            built = watch.ElapsedMilliseconds;
            Task.WaitAll(decoding);
        }
        catch (Exception e)
        {
            // Names given to images that will not be kept are taken back, so a later person does not name a missing image.
            try { Task.WaitAll(decoding); } catch (AggregateException) { }
            library.Forget(pending.Select(p => p.Name));
            if (e is AggregateException { InnerExceptions.Count: 1 } a) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(a.InnerExceptions[0]);
            throw;
        }
        foreach (var image in decoded) library.AddTexture(image);
        long decodedAt = watch.ElapsedMilliseconds;

        var skeleton = new Skeleton { Bones = bones.ToArray(), InverseBind = inverseBind.ToArray() };
        var verts = vertices.ToArray();
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var v in verts) { min = Vector3.Min(min, v.Position); max = Vector3.Max(max, v.Position); }
        var model = new SkinnedModel
        {
            Name = "people:" + recipe.Key,
            Vertices = verts,
            Indices = indices.ToArray(),
            Parts = parts.ToArray(),
            Skeleton = skeleton,
            BoundsMin = verts.Length > 0 ? min : Vector3.Zero,
            BoundsMax = verts.Length > 0 ? max : Vector3.Zero,
            Tangents = tangents.ToArray(),
            Colors = anyColour ? colors.ToArray() : null,
        };
        var images = library.DecodeImages ? textureNames.Select(library.Texture).ToArray() : [];
        long bytes = images.Sum(t => (long)t.Rgba.Length);
        library.Report($"{recipe.Name} ({recipe.Key}): {skeleton.Count} bones, {verts.Length} vertices, {indices.Count / 3} triangles "
                       + $"({hiddenCount} of the body's hidden{(hiddenOutside > 0 ? $", {hiddenOutside} listed past its {bodyTriangles}" : "")}), "
                       + $"{parts.Count} parts, {(library.DecodeImages ? $"{images.Length} textures ({bytes / (1024 * 1024)} MB, {decoded.Length} decoded now)" : $"{textureNames.Count} textures named, not decoded")}"
                       + $"{(left.Length > 0 ? $"; left out under what covers the hair: {string.Join(", ", left)}" : "")}; "
                       + $"{watch.ElapsedMilliseconds} ms: files read and parsed {read} ms, built {built - read} ms"
                       + (library.DecodeImages ? $" (the images decoding meanwhile), then waited {decodedAt - built} ms for the last images" : ""));
        return new PersonModel(recipe, model, PersonRig.From(skeleton, sockets), images)
        {
            HiddenTriangles = hiddenCount,
            HairGrey = anyGrey ? hairGrey.ToArray() : null,
            BaseHeight = BaseHeight(basePath),
            Swing = SwingRecord.Merge(sources.Select(s => SwingRecord.Of(s.File))),   // the worn files' swing records, in the recipe's order
        };
    }

    /// <summary>The base's height from its <c>.json</c> beside its <c>.glb</c> (<c>height_m</c>), 0 when it does not say.</summary>
    static float BaseHeight(string basePath)
    {
        string json = Path.ChangeExtension(basePath, ".json");
        try
        {
            if (!File.Exists(json)) return 0f;
            using var doc = JsonDocument.Parse(File.ReadAllText(json));
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("height_m", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetSingle() : 0f;
        }
        catch (Exception e) when (e is IOException or JsonException) { return 0f; }
    }

    /// <summary>Every image the parts of <paramref name="s"/> will name, given its name now (new ones to decode).</summary>
    void NameImages(Source s)
    {
        var file = s.File;
        foreach (int node in file.SceneOrder())
        {
            int mesh = file.Nodes[node].Mesh;
            if (mesh < 0) continue;
            foreach (var prim in file.Meshes[mesh].Primitives)
            {
                if (prim.Mode is not (4 or 5 or 6) || prim.Attribute("POSITION") < 0 || prim.Material < 0 || prim.Material >= file.Materials.Count) continue;
                var m = file.Materials[prim.Material];
                BaseColour(s, m);
                Texture(s, m.NormalTexture);
                Texture(s, m.MetallicRoughnessTexture ?? m.OcclusionTexture);
            }
        }
    }

    /// <summary>The images in <see cref="pending"/> decoded into <paramref name="into"/> by as many workers as there are cores, the largest first.</summary>
    Task[] Decode(TextureImage[] into)
    {
        var order = Enumerable.Range(0, pending.Count).OrderByDescending(i => pending[i].Png.Count).ToArray();
        int next = -1;
        var workers = new Task[Math.Min(Environment.ProcessorCount, order.Length)];
        for (int w = 0; w < workers.Length; w++)
            workers[w] = Task.Run(() =>
            {
                for (int k; (k = Interlocked.Increment(ref next)) < order.Length;)
                {
                    var (name, png, colour) = pending[order[k]];
                    var rgba = PngDecoder.Decode(png.AsMemory(), out int width, out int height)
                        ?? throw new InvalidDataException($"{recipe.Name}: the image {name} is not a PNG this reader can decode");
                    var image = new TextureImage(name, width, height, rgba);
                    // Capped as it is decoded, as the renderer would cap it on upload (the 4096 maps kept at 2048).
                    into[order[k]] = library.MaxTextureSide > 0 ? TextureHalving.Fit(image, library.MaxTextureSide, colour) : image;
                }
            });
        return workers;
    }

    /// <summary>A baked person's file by its stem in the recipe's <see cref="PersonRecipe.Folder"/>, or null when it is not there.</summary>
    string? Baked(string stem)
    {
        string path = Path.Combine(library.Folder, recipe.Folder!.Replace('/', Path.DirectorySeparatorChar), stem + ".glb");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// A worn file by its stem, with its <c>.json</c> (what it is, whether it covers the hair) and its fit data's <c>hidden</c>. an earlier build:
    /// a baked person's worn file is read from its folder, and what it is, covers and hides from the file it was fitted from
    /// (<see cref="PersonRecipe.FittedFrom"/>); a baked hair carries its own colour and grey, so it has no order of greying.
    /// </summary>
    Source Worn(string stem)
    {
        string? baked = recipe.Folder is not null ? Baked(stem) : null;
        if (recipe.Folder is not null && baked is null && recipe.FittedFrom?.ContainsKey(stem) == true)
            throw new FileNotFoundException($"no baked file {stem} in {library.Folder}/{recipe.Folder}");
        string? from = baked is not null && recipe.FittedFrom?.TryGetValue(stem, out var f) == true ? f : null;
        string path = baked ?? library.FindWorn(stem)
            ?? throw new FileNotFoundException($"no worn file {stem} under {library.Folder}/assets/outfits or assets/hair");
        // What it is, covers and hides: the file it was fitted from (a baked one), else its own beside it.
        string? madeOn = from is not null ? library.FindWorn(from) : baked is null ? path : null;
        if (baked is not null && madeOn is null)
            library.NoteOnce($"people: {stem} has no fit of its own and {(from is null ? "names no file it was fitted from" : $"the file it was fitted from, {from}, is not in the models folder")}: none of the body hidden under it");
        string dir = Path.GetDirectoryName(madeOn ?? path)!;
        string fitStem = madeOn is not null ? Path.GetFileNameWithoutExtension(madeOn) : stem;
        bool inHair = madeOn is not null
            ? Path.GetRelativePath(Path.Combine(library.Folder, "assets", "hair"), madeOn) is var rel && !rel.StartsWith("..", StringComparison.Ordinal)
            : stem.EndsWith("_hair", StringComparison.OrdinalIgnoreCase) || stem.EndsWith("_beard", StringComparison.OrdinalIgnoreCase);
        string kind = inHair ? (fitStem.StartsWith("beard", StringComparison.OrdinalIgnoreCase) || stem.EndsWith("_beard", StringComparison.OrdinalIgnoreCase) ? "beard" : "hair") : "outfit";
        bool covers = false;
        string json = Path.Combine(dir, fitStem + ".json");
        if (File.Exists(json))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(json));
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String && k.GetString() is "hair" or "beard") kind = k.GetString()!;
                covers = root.TryGetProperty("covers_hair", out var c) && c.ValueKind == JsonValueKind.True;
            }
        }
        int[] hidden = [];
        Dictionary<Vector3, float>? grey = null;
        string fit = Path.Combine(dir, fitStem + "_fit.npz");
        if ((madeOn is not null || baked is null) && File.Exists(fit))
        {
            using var npz = NpzArchive.Open(fit);
            if (npz.Contains("hidden")) hidden = npz.Read("hidden").ToInt32();
            if (kind is "hair" or "beard" && baked is null) grey = GreyOrder(npz);   // a baked hair is coloured and greyed already
        }
        return new Source(stem, kind, GltfFile.Load(path), covers, hidden) { Grey = grey };
    }

    /// <summary>
    /// A hair's fit data's order of greying (<c>&lt;mesh&gt;.grey</c>) by each vertex's place as built (<c>&lt;mesh&gt;.S</c>,
    /// Blender's Z up, made glTF's (x, z, −y): the file's own positions exactly, which split a few vertices the fit data keeps whole);
    /// null when it has none.
    /// </summary>
    static Dictionary<Vector3, float>? GreyOrder(NpzArchive npz)
    {
        Dictionary<Vector3, float>? order = null;
        foreach (var name in npz.Names.Where(n => n.EndsWith(".grey", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray())
        {
            string mesh = name[..^".grey".Length];
            if (!npz.Contains(mesh + ".S")) continue;
            var grey = npz.Read(name).ToSingle();
            var at = npz.Read(mesh + ".S").ToSingle();
            if (at.Length != grey.Length * 3) continue;
            order ??= [];
            for (int i = 0; i < grey.Length; i++) order.TryAdd(new Vector3(at[3 * i], at[3 * i + 2], -at[3 * i + 1]), grey[i]);
        }
        return order;
    }

    static int BodyTriangles(GltfFile file)
    {
        int mesh = BodyMesh(file);
        if (mesh < 0) return 0;
        int n = 0;
        foreach (var p in file.Meshes[mesh].Primitives)
            if (p.Mode == 4) n += (p.Indices >= 0 ? file.Accessors[p.Indices].Count : p.Attribute("POSITION") is var a and >= 0 ? file.Accessors[a].Count : 0) / 3;
        return n;
    }

    /// <summary>The base's skin mesh: the mesh named <c>body</c> (else the first).</summary>
    static int BodyMesh(GltfFile file)
    {
        for (int i = 0; i < file.Meshes.Count; i++) if (file.Meshes[i].Name == "body") return i;
        return file.Meshes.Count > 0 ? 0 : -1;
    }

    // ----- the skeleton -----

    /// <summary>Every skin the file's meshes use, in the order its scene first meets them.</summary>
    void AddSkins(GltfFile file)
    {
        foreach (int node in file.SceneOrder())
        {
            int skin = file.Nodes[node].Skin;
            if (skin >= 0 && file.Nodes[node].Mesh >= 0 && !skinRemap.ContainsKey((file, skin))) skinRemap[(file, skin)] = AddSkin(file, skin);
        }
    }

    /// <summary>
    /// A skin's joints the skeleton lacks, appended parents first, each with its parent found by name, its rest from the
    /// file's node and its inverse bind from the file's skin; answers each of the skin's joints' index in the skeleton.
    /// </summary>
    int[] AddSkin(GltfFile file, int skinIndex)
    {
        var skin = file.Skins[skinIndex];
        var ibm = skin.InverseBindMatrices >= 0 ? file.ReadMatrices(skin.InverseBindMatrices) : null;
        if (ibm is not null && ibm.Length < skin.Joints.Length) throw new InvalidDataException($"skin {skin.Name}: {ibm.Length} inverse bind matrices for {skin.Joints.Length} joints");
        foreach (int k in ParentsFirst(file, skin.Joints))
        {
            int node = skin.Joints[k];
            var n = file.Nodes[node];
            if (boneIndex.ContainsKey(n.Name)) continue;   // the base's (or an earlier file's) wins
            var (parent, between) = BoneAbove(file, node);
            boneIndex[n.Name] = bones.Count;
            bones.Add(new BoneInfo(n.Name, parent, Rest(n, between)));
            inverseBind.Add(PeopleSpace.Matrix(ibm?[k] ?? Matrix4x4.Identity));
        }
        var remap = new int[skin.Joints.Length];
        for (int k = 0; k < remap.Length; k++) remap[k] = boneIndex[file.Nodes[skin.Joints[k]].Name];
        return remap;
    }

    /// <summary>The skin's joints in an order with each after the joint above it (the file's own order where it already is).</summary>
    static List<int> ParentsFirst(GltfFile file, int[] joints)
    {
        var at = new Dictionary<int, int>();
        for (int k = 0; k < joints.Length; k++) at[joints[k]] = k;
        var above = new int[joints.Length];
        for (int k = 0; k < joints.Length; k++)
        {
            above[k] = -1;
            for (int p = file.Parents[joints[k]]; p >= 0; p = file.Parents[p])
                if (at.TryGetValue(p, out int j)) { above[k] = j; break; }
        }
        var placed = new bool[joints.Length];
        var order = new List<int>(joints.Length);
        for (bool progress = true; progress && order.Count < joints.Length;)
        {
            progress = false;
            for (int k = 0; k < joints.Length; k++)
                if (!placed[k] && (above[k] < 0 || placed[above[k]])) { placed[k] = true; order.Add(k); progress = true; }
        }
        if (order.Count < joints.Length) throw new InvalidDataException("a skin whose joints' parents form a loop");
        return order;
    }

    /// <summary>The nearest node above <paramref name="node"/> that is a bone of the skeleton (by name), and the transform of the nodes between (glTF's frames).</summary>
    (int Bone, Matrix4x4 Between) BoneAbove(GltfFile file, int node)
    {
        var between = Matrix4x4.Identity;
        for (int p = file.Parents[node]; p >= 0; p = file.Parents[p])
        {
            if (boneIndex.TryGetValue(file.Nodes[p].Name, out int bone)) return (bone, between);
            between *= file.Nodes[p].LocalMatrix;
        }
        return (-1, between);
    }

    /// <summary>A node's transform relative to the bone above it, converted; its scale (1 within 1e-6 in these files) dropped.</summary>
    static BoneTransform Rest(GltfNode node, Matrix4x4 between)
    {
        if (node.Matrix is null && between.IsIdentity) return PeopleSpace.Transform(node.Rotation, node.Translation);
        var m = node.LocalMatrix * between;
        if (!Matrix4x4.Decompose(m, out _, out var r, out var t)) throw new InvalidDataException($"node {node.Name}: a transform that is not a rotation and translation");
        return PeopleSpace.Transform(r, t);
    }

    void AddSockets(GltfFile file)
    {
        for (int i = 0; i < file.Nodes.Count; i++)
        {
            var n = file.Nodes[i];
            if (!n.Name.StartsWith("socket_", StringComparison.Ordinal) || sockets.ContainsKey(n.Name)) continue;
            var (bone, between) = BoneAbove(file, i);
            if (bone >= 0) sockets[n.Name] = new PersonSocket(bone, Rest(n, between));
        }
    }

    // ----- the meshes -----

    void AddMeshes(Source s, bool[]? hidden)
    {
        var file = s.File;
        int body = s.Kind == "base" ? BodyMesh(file) : -1;
        foreach (int node in file.SceneOrder())
        {
            var n = file.Nodes[node];
            if (n.Mesh < 0) continue;
            int[]? remap = n.Skin >= 0 ? skinRemap[(file, n.Skin)] : null;
            // A mesh without a skin rides the bone above it, placed where the file puts it.
            Matrix4x4? world = remap is null ? file.WorldMatrix(node) : null;
            int rigidBone = remap is null ? Math.Max(0, BoneAbove(file, node).Bone) : 0;
            int triangle = 0;
            foreach (var prim in file.Meshes[n.Mesh].Primitives)
                AddPrimitive(s, prim, remap, world, rigidBone, n.Mesh == body ? hidden : null, ref triangle);
        }
    }

    void AddPrimitive(Source s, GltfPrimitive prim, int[]? remap, Matrix4x4? world, int rigidBone, bool[]? hidden, ref int triangle)
    {
        var file = s.File;
        if (prim.Mode is not (4 or 5 or 6) || prim.Attribute("POSITION") < 0) return;
        var pos = file.ReadVector3(prim.Attribute("POSITION"));
        var rawPos = s.Grey is not null ? (Vector3[])pos.Clone() : null;   // as in the file, for the hair's greying
        int count = pos.Length;
        var nrm = prim.Attribute("NORMAL") is var na and >= 0 ? file.ReadVector3(na) : null;
        var uv = prim.Attribute("TEXCOORD_0") is var ua and >= 0 ? file.ReadVector2(ua) : null;
        var tan = prim.Attribute("TANGENT") is var ta and >= 0 ? file.ReadVector4(ta) : null;
        var col = prim.Attribute("COLOR_0") is var ca and >= 0 ? file.ReadVector4(ca) : null;
        var joints = remap is not null && prim.Attribute("JOINTS_0") is var ja and >= 0 ? file.ReadUInts(ja) : null;
        var weights = joints is not null && prim.Attribute("WEIGHTS_0") is var wa and >= 0 ? file.ReadFloats(wa) : null;
        if (joints is not null && (joints.Length != count * 4 || weights is null || weights.Length != count * 4))
            throw new InvalidDataException($"{s.Stem}: a primitive's JOINTS_0 or WEIGHTS_0 is not four a vertex");
        uint[] idx = prim.Indices >= 0 ? file.ReadUInts(prim.Indices) : Enumerable.Range(0, count).Select(i => (uint)i).ToArray();
        idx = prim.Mode switch { 5 => Strip(idx), 6 => Fan(idx), _ => idx };
        int triangles = idx.Length / 3;
        foreach (uint i in idx) if (i >= count) throw new InvalidDataException($"{s.Stem}: an index {i} past its primitive's {count} vertices");

        if (world is { } w)
        {
            Matrix4x4.Invert(w, out var inverse);
            var normalMatrix = Matrix4x4.Transpose(inverse);
            for (int i = 0; i < count; i++)
            {
                pos[i] = Vector3.Transform(pos[i], w);
                if (nrm is not null) nrm[i] = Vector3.TransformNormal(nrm[i], normalMatrix);
                if (tan is not null) tan[i] = new Vector4(Vector3.TransformNormal(new Vector3(tan[i].X, tan[i].Y, tan[i].Z), w), tan[i].W);
            }
        }
        nrm ??= SmoothNormals(pos, idx);

        int first = vertices.Count;
        var p = new Vector3[count];
        var nn = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            p[i] = PeopleSpace.Point(pos[i]);
            var n = PeopleSpace.Point(nrm[i]);
            nn[i] = n.LengthSquared() > 0f ? Vector3.Normalize(n) : Vector3.UnitZ;
            var v = new SkinVertex { Position = p[i], Normal = nn[i], Uv = uv?[i] ?? Vector2.Zero };
            if (joints is not null)
            {
                int B(int c)
                {
                    uint j = joints[i * 4 + c];
                    if (j >= remap!.Length) throw new InvalidDataException($"{s.Stem}: a joint {j} past its skin's {remap.Length}");
                    return remap[j];
                }
                v.Bones = SkinVertex.PackBones(B(0), B(1), B(2), B(3));
                v.Weights = new Vector4(weights![i * 4], weights[i * 4 + 1], weights[i * 4 + 2], weights[i * 4 + 3]);
            }
            else
            {
                v.Bones = SkinVertex.PackBones(rigidBone);
                v.Weights = new Vector4(1f, 0f, 0f, 0f);
            }
            vertices.Add(v);
            colors.Add(col?[i] ?? Vector4.One);
            // A hair's vertex's place in its greying (its position in the file, before any change).
            float order = s.Grey is { } byPlace && byPlace.TryGetValue(rawPos is null ? default : rawPos[i], out float o) ? o : float.NaN;
            hairGrey.Add(order);
            if (!float.IsNaN(order)) anyGrey = true;
        }
        var material = prim.Material >= 0 && prim.Material < file.Materials.Count ? file.Materials[prim.Material] : new GltfMaterial { Name = "default" };
        if (tan is not null)
            for (int i = 0; i < count; i++) tangents.Add(PeopleSpace.Tangent(tan[i]));
        else
            tangents.AddRange(MakeTangents(p, nn, uv, idx));
        if (col is not null) anyColour = true;

        int firstIndex = indices.Count;
        for (int t = 0; t < triangles; t++)
        {
            int body = triangle + t;
            if (hidden is not null && body < hidden.Length && hidden[body]) continue;
            indices.Add((uint)first + idx[3 * t]);
            indices.Add((uint)first + idx[3 * t + 1]);
            indices.Add((uint)first + idx[3 * t + 2]);
        }
        triangle += triangles;
        if (indices.Count == firstIndex) return;

        var pm = Material(s, material, col is not null);
        parts.Add(new ModelPart(firstIndex, indices.Count - firstIndex, pm.BaseColor ?? "", pm.AlphaCutoff > 0f)
        {
            TwoSided = pm.DoubleSided,
            Material = pm,
        });
        foreach (var name in new[] { pm.BaseColor, pm.Normal, pm.Orm })
            if (name is not null && !textureNames.Contains(name)) textureNames.Add(name);
    }

    /// <summary>
    /// A glTF material as the platform draws a person's part: the base colour, normal and ORM maps by their kept names; BLEND
    /// (the eyelashes) a cut-out at 0.5; a clear shell (the cornea, <c>KHR_materials_transmission</c>) hidden.
    /// </summary>
    PartMaterial Material(Source s, GltfMaterial m, bool vertexColour)
    {
        float cutoff = m.AlphaMode switch { GltfAlphaMode.Mask => m.AlphaCutoff, GltfAlphaMode.Blend => 0.5f, _ => 0f };
        bool clear = m.Uses("KHR_materials_transmission") && Transmission(m) > 0f;
        return new PartMaterial(m.Name, BaseColour(s, m), Texture(s, m.NormalTexture),
                                Texture(s, m.MetallicRoughnessTexture ?? m.OcclusionTexture),
                                m.BaseColorFactor, m.RoughnessFactor, m.MetallicFactor, cutoff, m.DoubleSided, vertexColour, clear)
        {
            NormalScale = m.NormalTexture?.Scale ?? 1f,   // the hair's and the beard's 0.6
        };
    }

    static float Transmission(GltfMaterial m)
    {
        if (m.ExtensionsJson is null) return 0f;
        using var doc = JsonDocument.Parse(m.ExtensionsJson);
        return doc.RootElement.TryGetProperty("KHR_materials_transmission", out var t) && t.TryGetProperty("transmissionFactor", out var f) ? f.GetSingle() : 0f;
    }

    /// <summary>
    /// A material's base colour map by its kept name: the file's own, or for the base's skin (its material named <c>skin…</c>) the
    /// recipe's <see cref="PersonRecipe.SkinMap"/> when the folder has it.
    /// </summary>
    string? BaseColour(Source s, GltfMaterial m) =>
        s.Kind == "base" && recipe.SkinMap is not null && m.Name is { } n && n.StartsWith("skin", StringComparison.OrdinalIgnoreCase) && SkinMap() is { } own
            ? own : Texture(s, m.BaseColorTexture, colour: true);

    string? skinMap;
    bool skinMapRead;

    /// <summary>The recipe's own skin map named (read once; a new one to decode), or null when the folder lacks it (said once).</summary>
    string? SkinMap()
    {
        if (skinMapRead) return skinMap;
        skinMapRead = true;
        string path = Path.Combine(library.Folder, recipe.SkinMap!.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            library.NoteOnce($"people: no {recipe.SkinMap} in the models folder (its files are older): the skin is the base's own map");
            return null;
        }
        var png = new ArraySegment<byte>(File.ReadAllBytes(path));
        if (!PngDecoder.IsPng(png))
        {
            library.NoteOnce($"people: {recipe.SkinMap} is not a PNG: the skin is the base's own map");
            return null;
        }
        return skinMap = library.TextureName(recipe.Base, Path.GetFileNameWithoutExtension(path), png, pending, colour: true);
    }

    string? Texture(Source s, GltfTextureRef? slot, bool colour = false)
    {
        if (slot is null || (uint)slot.Index >= (uint)s.File.Textures.Count) return null;
        int image = s.File.Textures[slot.Index].Source;
        if ((uint)image >= (uint)s.File.Images.Count) return null;
        if (!imageNames.TryGetValue((s.File, image), out var name))
        {
            // A map the crowd's files share, kept once beside them (../shared/<file>), read and named once, whoever refers to it.
            string? file = s.File.ImageFile(image);
            if (file is not null && library.TextureOfFile(file) is { } known)
            {
                imageNames[(s.File, image)] = known;
                return known;
            }
            var png = s.File.ImageBytes(image);
            if (!PngDecoder.IsPng(png))
            {
                library.Report($"{recipe.Name}: {s.Stem}'s image {image} ({s.File.Images[image].MimeType}) is not a PNG; drawn without it");
                name = null;
            }
            else
            {
                string imageName = s.File.Images[image].Name is { Length: > 0 } nm ? nm : $"image{image}";
                name = library.TextureName(s.Stem, imageName, png, pending, colour);
                if (file is not null) library.NoteTextureFile(file, name);
            }
            imageNames[(s.File, image)] = name;
        }
        return name;
    }

    static uint[] Strip(uint[] s)
    {
        var o = new List<uint>();
        for (int i = 0; i + 2 < s.Length; i++)
            if (i % 2 == 0) o.AddRange([s[i], s[i + 1], s[i + 2]]); else o.AddRange([s[i + 1], s[i], s[i + 2]]);
        return o.ToArray();
    }

    static uint[] Fan(uint[] s)
    {
        var o = new List<uint>();
        for (int i = 1; i + 1 < s.Length; i++) o.AddRange([s[0], s[i], s[i + 1]]);
        return o.ToArray();
    }

    /// <summary>Normals when a primitive has none: each vertex's faces' normals, weighted by area.</summary>
    static Vector3[] SmoothNormals(Vector3[] pos, uint[] idx)
    {
        var n = new Vector3[pos.Length];
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            var face = Vector3.Cross(pos[idx[t + 1]] - pos[idx[t]], pos[idx[t + 2]] - pos[idx[t]]);
            n[idx[t]] += face; n[idx[t + 1]] += face; n[idx[t + 2]] += face;
        }
        return n;
    }

    /// <summary>
    /// Tangents when a primitive has none (the teeth and the tongue): from the texture coordinates' gradient where it has
    /// them (the render project's <c>GltfReader.MakeTangents</c>, glTF's handedness), else any direction across the normal.
    /// </summary>
    static Vector4[] MakeTangents(Vector3[] p, Vector3[] n, Vector2[]? uv, uint[] idx)
    {
        var tan = new Vector3[p.Length];
        var bit = new Vector3[p.Length];
        if (uv is not null)
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                uint i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2];
                var e1 = p[i1] - p[i0]; var e2 = p[i2] - p[i0];
                var d1 = uv[i1] - uv[i0]; var d2 = uv[i2] - uv[i0];
                float r = d1.X * d2.Y - d2.X * d1.Y;
                if (MathF.Abs(r) < 1e-12f) continue;
                r = 1f / r;
                var sdir = (e1 * d2.Y - e2 * d1.Y) * r;
                var tdir = (e2 * d1.X - e1 * d2.X) * r;
                tan[i0] += sdir; tan[i1] += sdir; tan[i2] += sdir;
                bit[i0] += tdir; bit[i1] += tdir; bit[i2] += tdir;
            }
        var result = new Vector4[p.Length];
        for (int i = 0; i < p.Length; i++)
        {
            var normal = n[i];
            var t = tan[i] - normal * Vector3.Dot(normal, tan[i]);
            if (t.LengthSquared() < 1e-20f)
            {
                t = Vector3.Cross(MathF.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX, normal);
                if (t.LengthSquared() < 1e-20f) t = Vector3.UnitX;
            }
            t = Vector3.Normalize(t);
            // glTF's v runs down the image and its normal maps' +Y up it: the bitangent the map means points along decreasing v.
            float w = bit[i].LengthSquared() == 0f || Vector3.Dot(Vector3.Cross(normal, t), bit[i]) < 0 ? 1f : -1f;
            result[i] = new Vector4(t, w);
        }
        return result;
    }
}
