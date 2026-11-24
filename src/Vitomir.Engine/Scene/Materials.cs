// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Scene;

/// <summary>A level material: its NAM definition, its decoded textures and its resolved passes.</summary>
public sealed class LoadedMaterial
{
    public required string Name { get; init; }
    public required NamMaterial Nam { get; init; }
    /// <summary>The material's textures: the <c>texture</c> frames first, then any pass textures.</summary>
    public TgaImage?[] Images { get; init; } = [];
    /// <summary>The <c>pass</c> lines in file order; empty for the ordinary single-texture material.</summary>
    public MaterialPass[] Passes { get; init; } = [];
    /// <summary>
    /// The <c>texture</c> lines as frames, each an index into <see cref="Images"/>. A material
    /// without <c>numframes</c> has exactly one. The original drops any <c>texture</c> line past
    /// <c>numframes</c>, and a frame whose TGA is missing is dropped entirely,
    /// which is why a two-frame material can end up with one frame.
    /// </summary>
    public int[] Frames { get; init; } = [];
    /// <summary>How long each frame of a <c>pageflip</c> material lasts, in milliseconds; 0 when it has none.</summary>
    public int PageFlipMs { get; init; }
    /// <summary>The light style a <c>flicker</c> material follows, or −1.</summary>
    public int FlickerStyle { get; init; } = -1;
    /// <summary>
    /// Neither a <c>.nam</c> nor a texture by the name: what 1.1's loader answers with the default
    /// material instead (the load fails → <c>dflt</c>; <see cref="MaterialCache.Get"/>).
    /// </summary>
    public bool Missing { get; init; }
    /// <summary>Which frame is showing. Page flip, flicker and the script's MaterialSetFrame all move it.</summary>
    public int CurrentFrame { get; private set; }
    /// <summary>When the page-flip clock next advances a frame, on <see cref="MaterialCache.ClockMs"/>.</summary>
    internal double NextFlipMs { get; set; }

    /// <summary>The engine's MaterialSetFrame: clamped to the frames that loaded.</summary>
    public void SetFrame(int frame) => CurrentFrame = Math.Clamp(frame, 0, Math.Max(0, Frames.Length - 1));

    /// <summary>
    /// A <c>flicker</c> material shows frame 1 while its light style is lit and frame 0 while it is
    /// dark — the original tests the style's brightness byte for zero, so any style works, but only
    /// the fluorescent ones (7 to 9) are ever fully dark.
    /// </summary>
    public void ApplyFlicker(LightStyles styles)
    {
        if (FlickerStyle >= 0) SetFrame(styles.Brightness(FlickerStyle) == 0 ? 0 : 1);
    }

    /// <summary>The image the current frame draws: what a single-pass draw uses.</summary>
    public int CurrentImage => Frames.Length > 0 ? Frames[CurrentFrame] : 0;
    /// <summary>The first texture: what a single-pass draw uses.</summary>
    public TgaImage? Image => Images.Length > CurrentImage ? Images[CurrentImage] : null;
    public BlendKind Blend { get; init; }
    /// <summary>What the material's keywords and texture say, as the choice of draw mode needs it.</summary>
    public MaterialTraits Traits { get; init; }
    /// <summary>The <c>glow</c> map's index in <see cref="Images"/>, or −1.</summary>
    public int GlowImage { get; init; } = -1;
    /// <summary>The <c>detail</c> texture's index in <see cref="Images"/>, or −1.</summary>
    public int DetailImage { get; init; } = -1;
    /// <summary>How many times the detail texture repeats over the surface's own texture.</summary>
    public float DetailScale { get; init; } = 1f;
    /// <summary>Whether to honour the <c>pass</c> lines; the viewer can turn them off to compare.</summary>
    public bool DrawPasses { get; set; } = true;
    /// <summary>This material draws once per <c>pass</c> line rather than as one plain draw.</summary>
    public bool HasPasses => DrawPasses && Passes.Length > 0;

    /// <summary>
    /// Whether the passes start with a blended draw, which decides whether the surface belongs with
    /// the opaque geometry (water: an opaque first pass, then multiplying passes) or with the
    /// translucent draws (light volumes, glows, decals: additive from the first pass).
    /// </summary>
    public bool PassesAreTranslucent =>
        HasPasses && (RenderKinds.State(Passes[0].Kind)?.BlendEnable ?? false);
}

/// <summary>
/// Loads materials the way the SDK describes: &lt;base&gt;.nam if present, else an implied NAM for
/// &lt;base&gt;.tga. Level materials live under materials/, model materials under 3d/materials/.
/// Texture names inside a NAM are relative to the material's folder.
/// </summary>
public static class Materials
{
    /// <param name="basePath">Path without extension, e.g. "materials/Prague/Uwall2_p".</param>
    public static LoadedMaterial Load(GameFiles files, string basePath)
    {
        string path = basePath.Replace('\\', '/');
        string folder = path[..(path.LastIndexOf('/') + 1)];
        bool hasNam = files.TryRead(path + ".nam", out var namData);
        NamMaterial nam = hasNam
            ? NamMaterial.Read(namData, path + ".nam")
            : NamMaterial.Implied(Path.GetFileName(path) + ".tga");

        var images = new List<TgaImage?>();
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int Image(string texture)
        {
            if (byName.TryGetValue(texture, out int at)) return at;
            images.Add(Decode(files, folder, texture));
            return byName[texture] = images.Count - 1;
        }
        var frames = new List<int>();
        foreach (var frame in nam.Textures.Take(Math.Max(1, nam.NumFrames)))
        {
            int at = Image(frame);
            if (images[at] is not null) frames.Add(at);         // a frame whose TGA is missing is dropped
        }
        if (frames.Count == 0 && images.Count > 0) frames.Add(0);

        var passes = new List<MaterialPass>();
        foreach (var p in nam.Passes.Take(MaxPasses))
        {
            int image = p.Texture.Equals("$lightmap", StringComparison.OrdinalIgnoreCase) ? MaterialPass.Lightmap
                      : p.Texture.Equals("$lightadd", StringComparison.OrdinalIgnoreCase) ? MaterialPass.LightAdd
                      : Image(p.Texture);
            var slide = nam.Slides.TryGetValue(p.Number, out var s) ? new Vector2(s.Du, s.Dv) : Vector2.Zero;
            passes.Add(new MaterialPass(p.Number, image, RenderKinds.Parse(p.Blend), slide, p.NoMip));
        }
        if (images.Count == 0) images.Add(Decode(files, folder, Path.GetFileName(path) + ".tga"));

        int glow = nam.Glow is { Length: > 0 } g ? Image(g) : -1;
        int detail = nam.Detail is { Length: > 0 } d ? Image(d) : -1;
        if (glow >= 0 && images[glow] is null) glow = -1;
        if (detail >= 0 && images[detail] is null) detail = -1;

        var first = frames.Count > 0 ? images[frames[0]] : images[0];
        var blend = nam.AddBlend ? BlendKind.Additive
                  : nam.Masked ? BlendKind.Masked
                  : first is { HasAlpha: true } ? BlendKind.Alpha
                  : BlendKind.Opaque;
        var traits = new MaterialTraits(
            Masked: nam.Masked, AddBlend: nam.AddBlend, SubBlend: nam.SubBlend,
            AlphaTexture: !nam.Masked && first is { HasAlpha: true },
            FullyLit: nam.FullyLit, TwoSided: nam.TwoSided, Detail: detail >= 0, Glow: glow >= 0);
        return new LoadedMaterial
        {
            Name = basePath, Nam = nam, Images = [.. images], Passes = [.. passes], Blend = blend,
            Traits = traits, GlowImage = glow, DetailImage = detail, DetailScale = nam.DetailScale ?? 1f,
            Frames = [.. frames], PageFlipMs = frames.Count > 1 ? nam.PageFlipMs ?? 0 : 0,
            FlickerStyle = nam.Flicker ?? -1,
            Missing = !hasNam && images.All(i => i is null),
        };
    }

    /// <summary>The original's material parser keeps only the first four <c>pass</c> lines.</summary>
    public const int MaxPasses = 4;

    /// <summary>The material the loader falls back on (<c>dflt</c>).</summary>
    public const string Default = "dflt";

    /// <summary>
    /// Where a material named without a folder is loaded from — a script's <c>SetShell</c> material,
    /// say — found the way the original's loader finds one: the name's
    /// <c>.nam</c> along the material search path (resource kind 1); failing that, a
    /// texture of that name, which makes a plain material of it; failing both, the
    /// default material, <see cref="Default"/>, found the same way. Answers the path to load without
    /// its extension, or null when not even the default is there.
    /// </summary>
    public static string? Resolve(GameFiles files, string name)
    {
        foreach (var candidate in name.Equals(Default, StringComparison.OrdinalIgnoreCase) ? [name] : new[] { name, Default })
        {
            if ((Along(candidate + ".nam") ?? Along(candidate + ".tga")) is { } found)
                return found[..found.LastIndexOf('.')];
        }
        return null;

        string? Along(string file)
        {
            foreach (var dir in GameFiles.MaterialSearchPath)
                if (files.TryRead(dir + "/" + file, out _)) return dir + "/" + file;
            return files.TryRead(file, out _) ? file : null;
        }
    }

    /// <summary>
    /// One texture of a material. The original resolves a texture name through a fixed list of
    /// folders (<see cref="GameFiles.MaterialSearchPath"/>) and never relative to the material that
    /// names it, so <c>black.tga</c> in a material under <c>3d/materials/</c> is the one in
    /// <c>materials/prague/</c>. Two fallbacks of our own follow, the material's own folder and then
    /// the archives by file name; no shipped material needs either.
    ///
    /// There used to be a third, the material's own name, which rescued the four <c>waterShell_*</c>
    /// materials whose <c>pass</c> line has a stray token. Nothing in the game uses those four, and
    /// it was taken out, so they now miss
    /// their texture as they do in 1.1.
    /// </summary>
    static TgaImage? Decode(GameFiles files, string folder, string texture)
    {
        if (!texture.Contains('.')) texture += ".tga";
        if (files.TryReadMaterialFile(texture, out var tga) || files.TryRead(folder + texture, out tga)
            || (files.FindByFileName(texture) is { } found && files.TryRead(found, out tga)))
            return TgaImage.Read(tga, folder + texture);
        return null;
    }
}

/// <summary>How the original lays texture pixels out in memory.</summary>
public static class TextureLayout
{
    /// <summary>
    /// The image's RGBA rows bottom row first: the original reads bottom-up TGAs in file order and
    /// flips top-down ones, so row 0 of every texture is the image's bottom edge and texture v = 0
    /// samples it (the OpenGL/Maya convention). Level and model UVs are authored for that, so
    /// uploading rows top-first would show every texture upside down.
    /// </summary>
    public static byte[] BottomRowFirst(TgaImage image)
    {
        int stride = image.Width * 4;
        var rows = new byte[image.Rgba.Length];
        for (int y = 0; y < image.Height; y++)
            Buffer.BlockCopy(image.Rgba, y * stride, rows, (image.Height - 1 - y) * stride, stride);
        return rows;
    }
}

/// <summary>Materials by path, loaded once; batches refer to them by index.</summary>
public sealed class MaterialCache(GameFiles files)
{
    readonly Dictionary<string, int> index = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, LoadedMaterial> byName = new(StringComparer.OrdinalIgnoreCase);

    public List<LoadedMaterial> Items { get; } = [];

    /// <summary>The page-flip clock: milliseconds since start-up, the original's interface clock.</summary>
    public double ClockMs { get; private set; }

    /// <summary>
    /// A material by path, loaded once. One that is neither a <c>.nam</c> nor a texture is the default
    /// material, as 1.1's loader answers it (it retries with <c>dflt</c> when the load fails; the
    /// level's own list goes through it): the 81 surfaces in 22 levels that name `notexture`
    /// draw `dflt`, the grey 64×64 texture, where Vitomir drew a checkerboard.
    /// </summary>
    public int Get(string basePath)
    {
        if (index.TryGetValue(basePath, out int i)) return i;
        var material = Materials.Load(files, basePath);
        if (material.Missing && !Path.GetFileName(basePath.Replace('\\', '/')).Equals(Materials.Default, StringComparison.OrdinalIgnoreCase)
            && Named(Materials.Default) is { } fallback)
            return index[basePath] = fallback;
        material.NextFlipMs = ClockMs + material.PageFlipMs;
        Items.Add(material);
        // The level names them `materials/Prague\PMwall4_p`; the backslash is a separator on Linux too (ours).
        byName[Path.GetFileNameWithoutExtension(basePath.Replace('\\', '/'))] = material;
        return index[basePath] = Items.Count - 1;
    }

    /// <summary>A material by the bare name a script uses, e.g. <c>lightning1</c>.</summary>
    public LoadedMaterial? Find(string name) => byName.GetValueOrDefault(name);

    /// <summary>
    /// Pictures a material film has painted since the renderer last looked, as (material index,
    /// image): what 1.1's <c>bHWUpdate</c> sends to the card (<see cref="Video.MaterialFilms"/>).
    /// The renderer takes them and clears the list.
    /// </summary>
    public List<(int Material, int Image)> Repainted { get; } = [];

    /// <summary>Asks the renderer to take a picture again (<see cref="Repainted"/>).</summary>
    public void Repaint(LoadedMaterial material, int image)
    {
        int i = Items.IndexOf(material);
        if (i >= 0 && !Repainted.Contains((i, image))) Repainted.Add((i, image));
    }

    readonly Dictionary<string, int?> named = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A material by the bare name a script gives, loaded the way the original loads it
    /// (<see cref="Materials.Resolve"/> — the default material when the name is not there): its
    /// index, or null when not even the default is. What `SetShell` loads.
    /// </summary>
    public int? Named(string name)
    {
        if (named.TryGetValue(name, out var have)) return have;
        return named[name] = Materials.Resolve(files, name) is { } path ? Get(path) : null;
    }

    /// <summary>
    /// One frame of material animation, as the original's list walk does it:
    /// a <c>pageflip</c> material steps a frame every <c>pageflip</c> milliseconds of the clock, and
    /// a <c>flicker</c> material shows frame 1 while its light style is lit and frame 0 while it is
    /// dark. Sliding texture coordinates are the renderer's own business, not this list's.
    /// </summary>
    public void Animate(float milliseconds, LightStyles styles)
    {
        ClockMs += milliseconds;
        foreach (var m in Items)
        {
            if (m.PageFlipMs > 0 && m.Frames.Length > 1)
                while (m.NextFlipMs < ClockMs)
                {
                    m.SetFrame(m.CurrentFrame + 1 >= m.Frames.Length ? 0 : m.CurrentFrame + 1);
                    m.NextFlipMs += m.PageFlipMs;
                }
            m.ApplyFlicker(styles);
        }
    }
}
