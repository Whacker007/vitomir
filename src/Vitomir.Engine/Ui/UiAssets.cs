// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The UI's own assets, out of the player's install: panes (`ui/*.nui`), fonts (`ui/*.nfd`) and the
// textures they name. Nothing here knows about a GPU — it hands back parsed panes, fonts and RGBA.
//
// Names come out of a `.nui` lower-cased, because the engine lower-cases every line before parsing
// it, and a quad names a material with no extension while a control names a bitmap with one. Both
// resolve through the original's own material search path (`GameFiles.TryReadMaterialFile`), so a
// texture pack that replaces a UI texture is picked up for free.
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>Panes, fonts and textures from the install, each loaded once.</summary>
public sealed class UiAssets(GameFiles files)
{
    readonly Dictionary<string, NuiPane> panes = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, NfdFont> fonts = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, TgaImage?> textures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The space every `.nui` is authored in, with the origin at the <b>bottom</b> left.</summary>
    public const int Width = 640, Height = 480;

    /// <summary>Names that could not be resolved, so a caller can report them once instead of per frame.</summary>
    public List<string> Missing { get; } = [];

    /// <summary>A pane by its file name, with or without the extension (<c>statusBarSP</c>).</summary>
    public NuiPane Pane(string name)
    {
        if (panes.TryGetValue(name, out var pane)) return pane;
        string file = name.EndsWith(".nui", StringComparison.OrdinalIgnoreCase) ? name : name + ".nui";
        pane = NuiPane.Read(files.Read("ui/" + file), file);
        panes[name] = pane;
        if (ModernDayPanes.Contains(Path.GetFileNameWithoutExtension(file)))
        {
            originals[pane] = pane.Quads.Select(q => q.Material).ToArray();
            if (ModernDay) Dress(pane, true);
        }
        return pane;
    }

    // ---- the modern-day art  ------------------------------------------

    /// <summary>
    /// The panes loaded with the modern-day switch (they are reloaded, when it changes, through
    /// their loaders — the inventory, the character sheet, the quests, the
    /// disciplines, the status bar — and the shop and the
    /// advancement bar pass the same flag). No other pane is.
    /// </summary>
    public static readonly HashSet<string> ModernDayPanes = new(StringComparer.OrdinalIgnoreCase)
    {
        "statusBarSP", "statusBarADV", "buySellPane", "characterPane", "disciplinePane", "inventoryPane", "questPane",
    };

    readonly Dictionary<NuiPane, string[]> originals = new(ReferenceEqualityComparer.Instance);

    /// <summary>Whether those panes wear their modern-day art (<see cref="Session.ModernDayArt"/>).</summary>
    public bool ModernDay { get; private set; }

    /// <summary>
    /// The art changed — every quad of those panes named again. The pane loader,
    /// given the flag, takes each quad's <c>mat=</c> token, cuts its extension
    /// (the last dot after the last backslash) and adds <c>md</c> — so `DisciplinePane01` becomes
    /// `DisciplinePane01md` and `InventoryPane01.tga` `InventoryPane01md`. Every one of the 48 quads
    /// has its `MD` picture in `Local_Eng.nob`. **Ours in method:** the engine loads the panes again;
    /// here the quads of the panes already loaded are renamed in place, so the controls built on
    /// them keep their state.
    /// </summary>
    public void SetModernDay(bool md)
    {
        if (md == ModernDay) return;
        ModernDay = md;
        foreach (var pane in originals.Keys) Dress(pane, md);
    }

    void Dress(NuiPane pane, bool md)
    {
        var names = originals[pane];
        for (int i = 0; i < pane.Quads.Count && i < names.Length; i++)
            pane.Quads[i] = pane.Quads[i] with { Material = md ? ModernDayName(names[i]) : names[i] };
    }

    /// <summary>A quad's material with the modern-day ending: its extension cut and <c>md</c> added.</summary>
    public static string ModernDayName(string material)
    {
        int slash = Math.Max(material.LastIndexOf('\\'), material.LastIndexOf('/'));
        int dot = material.LastIndexOf('.');
        string stem = dot > slash ? material[..dot] : material;
        return stem + "md";
    }

    /// <summary>A font by its file name, with or without the extension (<c>defaultFont10</c>).</summary>
    public NfdFont Font(string name)
    {
        if (fonts.TryGetValue(name, out var font)) return font;
        string file = name.EndsWith(".nfd", StringComparison.OrdinalIgnoreCase) ? name : name + ".nfd";
        font = NfdFont.Read(files.Read("ui/" + file), file);
        fonts[name] = font;
        return font;
    }

    /// <summary>
    /// A UI texture by the name a pane gives it. Tries the name as written and then with
    /// <c>.tga</c>, because quads name a material and controls name a bitmap. Null, once, for a
    /// name the install has not got.
    /// </summary>
    public TgaImage? Texture(string name)
    {
        if (textures.TryGetValue(name, out var cached)) return cached;
        TgaImage? image = null;
        foreach (var candidate in Candidates(name))
            if (files.TryReadMaterialFile(candidate, out var data))
            {
                image = TgaImage.Read(data, candidate);
                break;
            }
        if (image is null) Missing.Add(name);
        textures[name] = image;
        return image;
    }

    /// <summary>
    /// **A picture of ours under a name of its own** (ours): a pane's tile with 1.1's words taken off it
    /// (<see cref="MenuLettering"/>), made from the player's own art when the pane is built and named after it
    /// (<c>lettered/options/options01.tga</c>), so the pane's quad can name it as it names any texture. Never a
    /// file: it lives as long as these assets do.
    /// </summary>
    public void Put(string name, TgaImage image)
    {
        textures[name] = image;
        finer[name] = null;
    }

    /// <summary>
    /// The scales a finer picture may be at, largest first: the suffix a replacement's name carries (<c>@6x</c>).
    /// Six: on a 3840x2160 screen the fitted sheet is 4.5 pixels a unit, so
    /// art at six times is drawn a little smaller than it was made, and stays sharp.
    /// </summary>
    public static readonly int[] FinerScales = [6, 5, 4, 3, 2];

    readonly Dictionary<string, (TgaImage Image, int Scale)?> finer = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What was said of a finer picture that could not be used, once each (a wrong size).</summary>
    public List<string> FinerRefused { get; } = [];

    /// <summary>
    /// **A finer picture of the same thing** (ours): a file beside the
    /// original, or in a data folder laid over it, named as the original with <c>@6x</c>, <c>@5x</c>, <c>@4x</c>,
    /// <c>@3x</c> or <c>@2x</c> before the extension (<c>StatusBarSP_01@6x.tga</c>) and **exactly that many times the
    /// original's size**, with everything in the same place. The largest scale found is taken. Null when
    /// there is none, which is every texture of 1.1's own.
    ///
    /// 1.1 copies a pane's art one texel to one unit of the sheet and has no scaling blit
    /// (<c>UiCanvas.Draw</c>), so a finer picture cannot simply replace the original: the original stays
    /// what the layout is worked out from — <see cref="Texture"/> and <see cref="Bounds"/> are its, and so is
    /// every hit test — and the finer one is only what the renderer puts on the screen in its place. A
    /// picture whose size is not the multiple its name says is refused, said once, and the original drawn.
    /// </summary>
    public (TgaImage Image, int Scale)? Finer(string name)
    {
        if (finer.TryGetValue(name, out var cached)) return cached;
        (TgaImage, int)? found = null;
        if (Texture(name) is { Width: > 0, Height: > 0 } original)
        {
            string stem = name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
            foreach (int scale in FinerScales)
            {
                string candidate = $"{stem}@{scale}x.tga";
                if (!files.TryReadMaterialFile(candidate, out var data)) continue;
                var image = TgaImage.Read(data, candidate);
                if (image.Width == original.Width * scale && image.Height == original.Height * scale) { found = (image, scale); break; }
                FinerRefused.Add($"{candidate} is {image.Width}x{image.Height}; {scale} times {name}'s {original.Width}x{original.Height} is {original.Width * scale}x{original.Height * scale}: not used");
            }
        }
        finer[name] = found;
        return found;
    }

    readonly Dictionary<string, (int X0, int Y0, int X1, int Y1)> bounds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where a texture's **artwork** actually is inside its file: the bounding box of everything
    /// that is not fully transparent, as (x0, y0) inclusive to (x1, y1) exclusive with row 0 at the
    /// top. Empty when the file has no alpha, which means "all of it".
    ///
    /// This is needed because the shipped icons are not the size they look: an icon file is
    /// cell-sized artwork in a power-of-two canvas with the rest transparent — 27 x 32 of art in a
    /// 32 x 32 file — so anything that wants to place an icon *by its artwork* has to measure it
    /// rather than trust the file's dimensions. Measured once per texture and kept.
    /// </summary>
    public (int X0, int Y0, int X1, int Y1) Bounds(string name)
    {
        if (bounds.TryGetValue(name, out var cached)) return cached;
        var box = (0, 0, 0, 0);
        if (Texture(name) is { } image)
        {
            box = (0, 0, image.Width, image.Height);
            if (image.HasAlpha)
            {
                int x0 = image.Width, y0 = image.Height, x1 = 0, y1 = 0;
                for (int y = 0; y < image.Height; y++)
                    for (int x = 0; x < image.Width; x++)
                        if (image.Rgba[(y * image.Width + x) * 4 + 3] != 0)
                        {
                            if (x < x0) x0 = x;
                            if (y < y0) y0 = y;
                            if (x + 1 > x1) x1 = x + 1;
                            if (y + 1 > y1) y1 = y + 1;
                        }
                box = x1 > x0 && y1 > y0 ? (x0, y0, x1, y1) : (0, 0, image.Width, image.Height);
            }
        }
        bounds[name] = box;
        return box;
    }

    static IEnumerable<string> Candidates(string name)
    {
        yield return name;
        if (!name.Contains('.')) yield return name + ".tga";
    }
}
