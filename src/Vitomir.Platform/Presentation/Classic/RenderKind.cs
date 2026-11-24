// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Platform.Presentation.Classic;

/// <summary>
/// The original renderer's named draw modes, in its own order: the game has a table of these
/// 43 names and looks a name up in it (1-based, case-insensitive) whenever a material file says
/// <c>pass &lt;n&gt; &lt;texture&gt; &lt;mode&gt;</c>; the renderer DLL then has one block of Direct3D
/// state per index. Two further indices (44, 45) exist in the renderer with no name in the table.
/// The SDK's own table of these modes was shipped empty, so <see cref="RenderKinds"/> is the
/// documentation: its state came from reading the renderer.
/// </summary>
public enum RenderKind
{
    /// <summary>Not a mode: what the name lookup returns for a name it doesn't know.</summary>
    None = 0,
    Sky = 1, ClearBuffer = 2, Rgb = 3, Rgba = 4, RgbTexAlpha = 5, RgbaTexAlpha = 6, RgbAddVert = 7,
    FullModulate = 8, FullBright = 9, RgbAdd = 10, ScreenPoly = 11, RgbDecal = 12, Glow = 13,
    RgbMasked = 14, MaskedWall = 15, MaskedLightmap = 16, AddParticle = 17, SubParticle = 18,
    BlendParticle = 19, AddBeam = 20, LightmappedWall = 21, Lightmap = 22, LightmapAdd = 23,
    MaskedWallRgb = 24, Rgb2Sided = 25, FullTexAlpha = 26, FullTexAlphaAdd = 27, FullAddConstant = 28,
    LightBlob = 29, Shadow = 30, ZOnly = 31, AlphaLightmapped = 32, AddLightmapped = 33,
    DetailModulate = 34, SolidGlow = 35, Outline = 36, SaturateDecal = 37, RgbModPlusDest = 38,
    FullModPlusDest = 39, LightmapMod = 40, FullAdd = 41, LightVolume = 42, FullSub = 43,

    /// <summary>
    /// The renderer has two more blocks than the game has names (the name table stops at 43), so
    /// these two names describe what the blocks do rather than repeat the original's. 44 draws a
    /// lightmap as it is — it is what a surface's steady lightmap layer uses, where
    /// <see cref="Lightmap"/> would scale a layer by its light style's current brightness.
    /// </summary>
    LightmapSteady = 44,
    /// <summary>45: the vertex colour alone, through the second coordinate set. Nothing reaches it.</summary>
    VertexColor = 45,
}

/// <summary>A Direct3D 7 texture-stage operation, as the modes use them.</summary>
public enum TexOp { Disable, SelectArg1, SelectArg2, Modulate, ModulateColorAddAlpha }

/// <summary>Where a stage operand comes from. <see cref="Diffuse"/> is the vertex colour.</summary>
public enum TexArg { Diffuse, Current, Texture, TextureFactor }

/// <summary>Direct3D 7 blend factors, with the original's numbering.</summary>
public enum Blend
{
    Zero = 1, One, SrcColor, InvSrcColor, SrcAlpha, InvSrcAlpha, DestAlpha, InvDestAlpha,
    DestColor, InvDestColor, SrcAlphaSat,
}

/// <summary>Direct3D 7 comparison functions, with the original's numbering.</summary>
public enum Compare { Never = 1, Less, Equal, LessEqual, Greater, NotEqual, GreaterEqual, Always }

/// <summary>Direct3D 7 texture addressing, with the original's numbering.</summary>
public enum Address { Wrap = 1, Mirror, Clamp, Border }

/// <summary>Which faces are dropped. The original's own values are 1 none, 2 clockwise, 3 counter.</summary>
public enum Cull { None, Back, Front }

/// <summary>One texture stage's operation and operands.</summary>
public readonly record struct TexStage(TexOp Op, TexArg Arg1, TexArg Arg2)
{
    public static TexStage Off => new(TexOp.Disable, TexArg.Texture, TexArg.Diffuse);
    public bool Enabled => Op != TexOp.Disable;
}

/// <summary>
/// The Direct3D state one <see cref="RenderKind"/> sets. Defaults are the values the original's
/// state setters leave in place, so an entry only lists what that mode changes.
/// Filtering is linear in every mode (the material keyword <c>nofilter</c> has no effect in 1.1);
/// mip filtering is point, which the original raises to linear on cards that report trilinear.
/// Alpha test, where a mode enables it, is "alpha &gt; 0": the original sets the compare once at
/// start-up and never touches the reference value, so a masked texture keeps every pixel that is
/// not fully transparent.
/// </summary>
public sealed record RenderKindState
{
    /// <summary>Depth writes. Off for every blended mode.</summary>
    public bool DepthWrite { get; init; }
    public Compare DepthFunc { get; init; } = Compare.LessEqual;
    /// <summary>Stage 0 colour: the material's texture against the vertex colour or the constant.</summary>
    public TexStage Color { get; init; } = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse);
    public TexStage Alpha { get; init; } = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse);
    /// <summary>Stage 1, the lightmap stage, off in all but four modes.</summary>
    public TexStage Color1 { get; init; } = TexStage.Off;
    public TexStage Alpha1 { get; init; } = TexStage.Off;
    public Blend SrcBlend { get; init; } = Blend.One;
    public Blend DestBlend { get; init; } = Blend.Zero;
    public bool BlendEnable { get; init; }
    /// <summary>
    /// Additive modes set the fog colour to black while they draw (and restore the scene's
    /// afterwards), so distance fades an additive pass out instead of tinting it.
    /// </summary>
    public bool FogBlack { get; init; }
    public Address Address { get; init; } = Address.Wrap;
    public bool Mipmaps { get; init; } = true;
    public Cull Cull { get; init; } = Cull.Back;
    public bool AlphaTest { get; init; }
    public bool Fog { get; init; }
    /// <summary>The original's ZBIAS render state: 0, 1 or 2, nudging the mode towards the eye.</summary>
    public int DepthBias { get; init; }
    /// <summary>Which of the vertex's two texture coordinate sets stage 0 samples with.</summary>
    public int TexCoord { get; init; }
}

/// <summary>The draw modes and their state, as the original renderer has them.</summary>
public static class RenderKinds
{
    /// <summary>
    /// The game's own table of mode names, in its order, so index + 1 is the <see cref="RenderKind"/>.
    /// </summary>
    public static readonly string[] Names =
    [
        "sky", "clearbuffer", "rgb", "rgba", "rgb_texalpha", "rgba_texalpha", "rgb_addvert",
        "fullmodulate", "fullbright", "rgb_add", "screenpoly", "rgb_decal", "glow", "rgb_masked",
        "maskedwall", "maskedlightmap", "addparticle", "subparticle", "blendparticle", "addbeam",
        "lightmappedwall", "lightmap", "lightmapadd", "maskedwallrgb", "rgb2sided", "full_texalpha",
        "full_texalpha_add", "full_addconstant", "lightblob", "shadow", "zonly", "alphalightmapped",
        "addlightmapped", "detailmodulate", "solidglow", "outline", "saturate_decal",
        "rgb_modplusdest", "full_modplusdest", "lightmap_mod", "full_add", "lightvolume", "full_sub",
    ];

    static readonly Dictionary<RenderKind, RenderKindState?> states = new()
    {
        [RenderKind.Sky] = new() { Color = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.Zero, Address = Address.Clamp },
        [RenderKind.ClearBuffer] = new() { DepthWrite = true, Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.Zero },
        [RenderKind.Rgb] = new() { DepthWrite = true, Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.Zero, Fog = true },
        [RenderKind.Rgba] = new() { DepthWrite = true, Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg2, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.SrcAlpha, DestBlend = Blend.InvSrcAlpha, BlendEnable = true, Cull = Cull.None, Fog = true },
        [RenderKind.RgbTexAlpha] = new() { DepthWrite = true, Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.SrcAlpha, DestBlend = Blend.InvSrcAlpha, BlendEnable = true, Cull = Cull.None, Fog = true },
        [RenderKind.RgbaTexAlpha] = new() { DepthWrite = true, Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.SrcAlpha, DestBlend = Blend.InvSrcAlpha, BlendEnable = true, Cull = Cull.None, Fog = true },
        [RenderKind.RgbAddVert] = new() { DepthWrite = true, Color = new(TexOp.ModulateColorAddAlpha, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg2, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.Zero, Fog = true },
        [RenderKind.FullModulate] = new() { Color = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.DestColor, DestBlend = Blend.Zero, BlendEnable = true, Fog = true },
        [RenderKind.FullBright] = new() { DepthWrite = true, Color = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.Zero, Fog = true },
        [RenderKind.RgbAdd] = new() { DepthWrite = true, Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.One, BlendEnable = true, FogBlack = true, Fog = true },
        [RenderKind.ScreenPoly] = new() { DepthFunc = Compare.Always, Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.SrcAlpha, DestBlend = Blend.InvSrcAlpha, BlendEnable = true, Address = Address.Clamp, Mipmaps = false, Cull = Cull.None },
        [RenderKind.RgbDecal] = new() { DepthWrite = true, Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.Zero, Address = Address.Clamp, Fog = true },
        [RenderKind.Glow] = new() { DepthFunc = Compare.Always, Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.One, BlendEnable = true, FogBlack = true, Address = Address.Clamp, Fog = true },
        [RenderKind.RgbMasked] = new() { DepthWrite = true, Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.Zero, AlphaTest = true, Fog = true },
        [RenderKind.MaskedWall] = new() { DepthWrite = true, Color = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Color1 = new(TexOp.Modulate, TexArg.Texture, TexArg.Current), Alpha1 = new(TexOp.SelectArg2, TexArg.Diffuse, TexArg.Current), SrcBlend = Blend.One, DestBlend = Blend.Zero, Cull = Cull.None, AlphaTest = true, Fog = true },
        [RenderKind.MaskedLightmap] = null,      // the 1.1 renderer leaves the state alone for this one
        [RenderKind.AddParticle] = new() { Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.One, BlendEnable = true, FogBlack = true, Cull = Cull.None, Fog = true },
        [RenderKind.SubParticle] = new() { Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.Zero, DestBlend = Blend.InvSrcColor, BlendEnable = true, Address = Address.Clamp, Cull = Cull.None, Fog = true },
        [RenderKind.BlendParticle] = new() { Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.SrcAlpha, DestBlend = Blend.InvSrcAlpha, BlendEnable = true, Address = Address.Clamp, Cull = Cull.None, Fog = true },
        [RenderKind.AddBeam] = new() { Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.One, BlendEnable = true, FogBlack = true, Cull = Cull.None, Fog = true },
        [RenderKind.LightmappedWall] = new() { DepthWrite = true, Color = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.Disable, TexArg.Texture, TexArg.Diffuse), Color1 = new(TexOp.Modulate, TexArg.Texture, TexArg.Current), Alpha1 = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.Zero, Cull = Cull.None, Fog = true },
        [RenderKind.Lightmap] = new() { DepthWrite = true, Color = new(TexOp.Modulate, TexArg.Texture, TexArg.TextureFactor), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.Zero, Address = Address.Clamp, Cull = Cull.None, TexCoord = 1 },
        [RenderKind.LightmapAdd] = new() { Color = new(TexOp.Modulate, TexArg.Texture, TexArg.TextureFactor), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.One, BlendEnable = true, FogBlack = true, Address = Address.Clamp, TexCoord = 1 },
        [RenderKind.MaskedWallRgb] = new() { DepthWrite = true, Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.Zero, Cull = Cull.None, AlphaTest = true, Fog = true },
        [RenderKind.Rgb2Sided] = new() { DepthWrite = true, Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.Zero, Cull = Cull.None, Fog = true },
        [RenderKind.FullTexAlpha] = new() { DepthWrite = true, Color = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.SrcAlpha, DestBlend = Blend.InvSrcAlpha, BlendEnable = true, Cull = Cull.None, Fog = true },
        [RenderKind.FullTexAlphaAdd] = new() { DepthWrite = true, Color = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.Zero, Address = Address.Clamp, Cull = Cull.None, Fog = true },
        [RenderKind.FullAddConstant] = new() { Color = new(TexOp.SelectArg2, TexArg.Texture, TexArg.TextureFactor), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.One, BlendEnable = true, FogBlack = true, Fog = true },
        [RenderKind.LightBlob] = new() { Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.One, BlendEnable = true, FogBlack = true, Address = Address.Clamp, DepthBias = 2 },
        [RenderKind.Shadow] = new() { Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.Zero, DestBlend = Blend.InvSrcColor, BlendEnable = true, Address = Address.Clamp, Fog = true, DepthBias = 1 },
        [RenderKind.ZOnly] = new() { DepthWrite = true, DepthFunc = Compare.Always, Color = new(TexOp.SelectArg2, TexArg.Texture, TexArg.TextureFactor), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.Zero, DestBlend = Blend.One, BlendEnable = true, Cull = Cull.None },
        [RenderKind.AlphaLightmapped] = new() { DepthWrite = true, Color = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Color1 = new(TexOp.Modulate, TexArg.Texture, TexArg.Current), Alpha1 = new(TexOp.Modulate, TexArg.Diffuse, TexArg.Current), SrcBlend = Blend.SrcAlpha, DestBlend = Blend.InvSrcAlpha, BlendEnable = true, Cull = Cull.None, Fog = true },
        [RenderKind.AddLightmapped] = new() { Color = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Color1 = new(TexOp.Modulate, TexArg.Texture, TexArg.Current), Alpha1 = new(TexOp.SelectArg2, TexArg.Diffuse, TexArg.Current), SrcBlend = Blend.One, DestBlend = Blend.One, BlendEnable = true, FogBlack = true, Cull = Cull.None, Fog = true },
        [RenderKind.DetailModulate] = new() { Color = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.DestColor, DestBlend = Blend.SrcColor, BlendEnable = true, Cull = Cull.None, Fog = true },
        [RenderKind.SolidGlow] = new() { Color = new(TexOp.Modulate, TexArg.Texture, TexArg.TextureFactor), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.One, BlendEnable = true, FogBlack = true, Fog = true },
        [RenderKind.Outline] = new() { Color = new(TexOp.SelectArg2, TexArg.Texture, TexArg.TextureFactor), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.Zero, Fog = true, DepthBias = 1 },
        [RenderKind.SaturateDecal] = new() { Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.DestColor, DestBlend = Blend.SrcAlpha, BlendEnable = true, Address = Address.Clamp, Fog = true, DepthBias = 1 },
        [RenderKind.RgbModPlusDest] = new() { Color = new(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.DestColor, DestBlend = Blend.One, BlendEnable = true, Cull = Cull.None, Fog = true },
        [RenderKind.FullModPlusDest] = new() { Color = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.DestColor, DestBlend = Blend.One, BlendEnable = true, Cull = Cull.None, Fog = true },
        [RenderKind.LightmapMod] = new() { Color = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.DestColor, DestBlend = Blend.Zero, BlendEnable = true, Address = Address.Clamp, TexCoord = 1 },
        [RenderKind.FullAdd] = new() { Color = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg2, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.SrcAlpha, DestBlend = Blend.One, BlendEnable = true, FogBlack = true, Fog = true },
        [RenderKind.LightVolume] = new() { Color = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg2, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.SrcAlpha, DestBlend = Blend.One, BlendEnable = true, FogBlack = true, Cull = Cull.None },
        [RenderKind.FullSub] = new() { Color = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.Zero, DestBlend = Blend.InvSrcColor, BlendEnable = true, Fog = true },
        [RenderKind.LightmapSteady] = new() { DepthWrite = true, Color = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.Zero, Address = Address.Clamp, Cull = Cull.None, TexCoord = 1 },
        [RenderKind.VertexColor] = new() { DepthWrite = true, Color = new(TexOp.SelectArg2, TexArg.Diffuse, TexArg.Diffuse), Alpha = new(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), SrcBlend = Blend.One, DestBlend = Blend.Zero, Cull = Cull.None, TexCoord = 1 },
    };

    /// <summary>
    /// The mode a material file's <c>pass</c> line names. An unknown name gives
    /// <see cref="RenderKind.Rgb"/>, as it does in the original: the name lookup fails, and the
    /// pass keeps the mode the parser put there before looking (3, rgb). Shipped materials rely on
    /// this — <c>waterTest.nam</c> names modes (<c>modplusdest</c>, <c>modulatepost</c>) that the
    /// release renderer doesn't have.
    /// </summary>
    public static RenderKind Parse(string name) => TryParse(name, out var kind) ? kind : RenderKind.Rgb;

    /// <summary>The mode of this name, or false when the table doesn't have it.</summary>
    public static bool TryParse(string name, out RenderKind kind)
    {
        int i = Array.FindIndex(Names, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        kind = i < 0 ? RenderKind.None : (RenderKind)(i + 1);
        return i >= 0;
    }

    /// <summary>
    /// The state this mode sets, or null when the original has no block for it and leaves the
    /// device as the previous draw left it (<see cref="RenderKind.MaskedLightmap"/>, and the two
    /// unnamed indices).
    /// </summary>
    public static RenderKindState? State(RenderKind kind) => states.GetValueOrDefault(kind);

    /// <summary>Every mode with state, for tests and diagnostics.</summary>
    public static IEnumerable<(RenderKind Kind, RenderKindState State)> WithState =>
        states.Where(p => p.Value is not null).Select(p => (p.Key, p.Value!));
}

/// <summary>What a material says about itself, as the choice of mode needs it.</summary>
/// <param name="AlphaTexture">Its texture has an alpha channel and the material isn't <c>masked</c>.</param>
public readonly record struct MaterialTraits(bool Masked, bool AddBlend, bool SubBlend, bool AlphaTexture,
                                             bool FullyLit, bool TwoSided, bool Detail, bool Glow);

/// <summary>
/// One step of a surface's plan: the mode to draw it in, and what the first texture stage samples.
/// </summary>
/// <param name="Layer">
/// −1 for the material's own texture; otherwise the index of one of the surface's lightmap layers.
/// A mode whose second stage is enabled samples the surface's steady lightmap there as well.
/// </param>
public readonly record struct DrawStep(RenderKind Kind, int Layer = -1)
{
    /// <summary>The material's texture, not a lightmap.</summary>
    public const int Material = -1;
}

/// <summary>
/// One of a surface's lightmap layers, as the choice of mode sees it: which light style drives it
/// and how bright that style is right now (255 being full).
/// </summary>
public readonly record struct LayerLight(int Style, byte Brightness)
{
    /// <summary>Style 0 never varies.</summary>
    public bool Steady => Style == 0;
    /// <summary>At full brightness this layer can go through a mode's second texture stage.</summary>
    public bool Full => Brightness == 255;
}

/// <summary>
/// How the original decides what to draw a surface with. Two rules, as in the original:
/// <see cref="ForThing"/> is the game's, which gives a model's material one mode from its keywords;
/// <see cref="ForSurface"/> is the renderer's, which turns a level surface into a list of draws
/// because its lightmap layers may need one pass each.
/// </summary>
public static class DrawPlan
{
    /// <summary>
    /// The second mode (the material layer's second): what the material is drawn in as a particle or
    /// a shell — <see cref="RenderKind.BlendParticle"/> where <see cref="ForThing"/> is plain
    /// (<see cref="RenderKind.Rgb"/>, <see cref="RenderKind.RgbTexAlpha"/> or
    /// <see cref="RenderKind.Rgb2Sided"/>), and the same as it otherwise.
    /// </summary>
    public static RenderKind ForParticle(MaterialTraits m) => ForThing(m) switch
    {
        RenderKind.Rgb or RenderKind.RgbTexAlpha or RenderKind.Rgb2Sided => RenderKind.BlendParticle,
        var kind => kind,
    };

    /// <summary>
    /// A model's material: the game computes this once per material (the layer's first mode)
    /// and the renderer draws every face of it that way.
    /// </summary>
    public static RenderKind ForThing(MaterialTraits m) =>
          m.AddBlend ? RenderKind.AddParticle
        : m.SubBlend ? RenderKind.SubParticle
        : m.Masked ? RenderKind.RgbMasked
        : m.AlphaTexture ? RenderKind.RgbTexAlpha
        : m.FullyLit ? RenderKind.FullBright
        : m.TwoSided ? RenderKind.Rgb2Sided
        : RenderKind.Rgb;

    /// <summary>
    /// A level surface, given its lightmap <paramref name="layers"/> and whether it is Trans 33/66.
    /// The result is the draws in order: one two-stage draw where a single lightmap at full
    /// brightness allows it, otherwise one draw per lightmap layer and then the texture multiplied
    /// over them. A <c>detail</c> or <c>glow</c> material adds a draw of its own at the end.
    /// The plan can change from frame to frame: a surface lit by a style that dims leaves the
    /// two-stage mode as soon as the style is no longer at full, because the layer then has to be
    /// scaled by the style's brightness, which only the constant colour can do.
    /// </summary>
    public static void ForSurface(MaterialTraits m, ReadOnlySpan<LayerLight> layers, bool translucent, List<DrawStep> into)
    {
        into.Clear();
        // Masked and translucent surfaces pair with a lightmap only when a steady style lights it;
        // an opaque one asks the looser question — is the style at full brightness this frame.
        bool steady = layers.Length > 0 && layers[0].Steady;
        bool full = layers.Length == 1 && layers[0].Full;
        if (m.Masked) into.Add(new DrawStep(steady ? RenderKind.MaskedWall : RenderKind.RgbMasked));
        else if (m.AddBlend) into.Add(new DrawStep(RenderKind.RgbAdd));
        else if (translucent || m.AlphaTexture)
            into.Add(new DrawStep(steady ? RenderKind.AlphaLightmapped : RenderKind.RgbaTexAlpha));
        else if (m.FullyLit) into.Add(new DrawStep(RenderKind.FullBright));
        else if (layers.Length == 0) into.Add(new DrawStep(RenderKind.Rgb));
        else if (full) into.Add(new DrawStep(RenderKind.LightmappedWall));
        else
        {
            // The lightmap layers first, each scaled by its light style's brightness (a layer at
            // full needs no scaling and uses its own mode), then the texture multiplied over them.
            for (int i = 0; i < layers.Length; i++)
                into.Add(new DrawStep(i == 0 ? (layers[0].Full ? RenderKind.LightmapSteady : RenderKind.Lightmap)
                                             : RenderKind.LightmapAdd, i));
            into.Add(new DrawStep(RenderKind.FullModulate));
        }
        if (m.Detail) into.Add(new DrawStep(RenderKind.DetailModulate));
        if (m.Glow) into.Add(new DrawStep(RenderKind.SolidGlow));
    }
}

/// <summary>How a material is blended. A first approximation of the 1.1 renderer's rules, to be refined.</summary>
public enum BlendKind { Opaque, Masked, Alpha, Additive }

/// <summary>
/// One resolved <c>pass</c> line: which of the material's textures it draws, in which
/// <see cref="RenderKind"/>, and how fast its texture coordinates slide.
/// </summary>
/// <param name="Image">Index into the material's images, or <see cref="Lightmap"/>/<see cref="LightAdd"/>.</param>
/// <param name="Slide">Texture coordinates per second, from the material's <c>slide</c> line.</param>
public readonly record struct MaterialPass(int Number, int Image, RenderKind Kind, Vector2 Slide, bool NoMip)
{
    /// <summary>The surface's own lightmap, which the material names <c>$lightmap</c>.</summary>
    public const int Lightmap = -1;
    /// <summary>Light from dynamic sources, which the material names <c>$lightadd</c> (not drawn yet).</summary>
    public const int LightAdd = -2;
}
