// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The renderer's five detail presets behind the Display and Advanced screens: a table of
// 0x58 bytes a preset, which the renderer's constructor fills (with the model
// LOD's and the shadows' helpers) and `masquerade.ini`'s `[renderCustom]` overwrites the fifth of,
// and the Advanced screen's show hook reads into its boxes and bars. Read.
// **Inert in Vitomir**: the screens show and keep
// them, and the renderer draws with `DetailPresets.Maxed` whatever they say.
using System.Globalization;
using Vitomir.Engine.Diagnostics;

namespace Vitomir.Engine.Ui;

/// <summary>
/// One preset of the renderer (0x58 bytes a level), field by field in the
/// order the ini names them (`[renderCustom]`): <c>detailFlags</c>,
/// <c>optionFlags</c>, <c>modelLODIndex</c> and its scale, minimum distance, minimum and
/// maximum, <c>maxMipSize</c>, <c>maxMips</c>, <c>skipMips</c>,
/// <c>maxThingLights</c> (a byte), <c>shadowMode</c>, <c>shadowDetailIndex</c>,
/// the shadow's detail, height and two distances, <c>maxThingShadows</c>,
/// <c>maxDecals</c>, <c>particleLOD</c>, <c>uiHeadLOD</c>, and last the 32-bit
/// textures bit (`optionFlags` bit 1).
/// </summary>
public readonly record struct DetailPreset(
    uint DetailFlags, uint OptionFlags, int ModelLodIndex, float ModelLodScale, float ModelLodMinDist,
    float ModelLodMin, float ModelLodMax, int MaxMipSize, int MaxMips, int SkipMips, int MaxThingLights,
    int ShadowMode, int ShadowDetailIndex, float ShadowDetail, float ShadowHeight, float ShadowMaxDist,
    float ShadowFullDist, int MaxThingShadows, int MaxDecals, float ParticleLod, float HeadLod, bool Textures32);

/// <summary>The five presets and what the Advanced screen shows of one.</summary>
public static class DetailPresets
{
    /// <summary>The model LOD's four numbers for an index: scale, minimum distance, minimum, maximum.</summary>
    static (float Scale, float MinDist, float Min, float Max) ModelLod(int index) => index switch
    {
        0 => (0.04f, 100f, 0f, 0.5f),
        1 => (0.03f, 100f, 0f, 0.6f),
        2 => (0.025f, 130f, 0f, 0.8f),
        3 => (0.02f, 150f, 0.1f, 0.9f),
        4 => (0.015f, 180f, 0.1f, 1f),
        5 => (0.015f, 200f, 0.2f, 1f),
        6 => (0.01f, 200f, 0.2f, 1f),
        7 => (0.005f, 400f, 0.4f, 1f),
        8 => (0.002f, 800f, 0.6f, 1f),
        _ => (0.001f, 1000f, 1f, 1f),
    };

    /// <summary>The shadow's five numbers for an index: mode, detail, height, full and maximum distance.</summary>
    static (int Mode, float Detail, float Height, float FullDist, float MaxDist) Shadow(int index) => index switch
    {
        0 => (0, 0f, 2f, 500f, 1000f),
        1 => (1, 0.1f, 2f, 500f, 1000f),
        2 => (1, 0.1f, 2f, 1000f, 2000f),
        3 => (2, 0.15f, 2f, 1000f, 2000f),
        4 => (2, 0.15f, 3f, 1600f, 3000f),
        5 => (3, 0.15f, 3f, 1600f, 3000f),
        6 => (3, 0.25f, 3f, 1600f, 3000f),
        7 => (3, 0.5f, 3f, 2000f, 4000f),
        8 => (3, 0.8f, 3f, 2000f, 4000f),
        _ => (3, 1f, 4f, 3000f, 6000f),
    };

    /// <summary>
    /// A preset with its shadow index set and the shadow's five filled from it, as the Advanced
    /// screen's read-back does with its shadow-detail bar. The shadow helper sets all
    /// five for 0..9; for 10 it sets the index alone (case 9 falls into the default, which only
    /// stores it), and for more than 10 it changes nothing.
    /// </summary>
    public static DetailPreset WithShadow(DetailPreset p, int index)
    {
        if (index is < 0 or > 10) return p;
        if (index == 10) return p with { ShadowDetailIndex = index };
        var s = Shadow(index);
        return p with { ShadowDetailIndex = index, ShadowMode = s.Mode, ShadowDetail = s.Detail, ShadowHeight = s.Height,
                        ShadowMaxDist = s.MaxDist, ShadowFullDist = s.FullDist };
    }

    static DetailPreset Make(uint flags, int lod, int shadow, int lights, int shadows, int decals, float particles, float heads)
    {
        var m = ModelLod(lod);
        var s = Shadow(shadow);
        return new DetailPreset(flags, 0, lod, m.Scale, m.MinDist, m.Min, m.Max, 0x100, 4, 0, lights,
                                s.Mode, shadow, s.Detail, s.Height, s.MaxDist, s.FullDist, shadows, decals, particles, heads, false);
    }

    /// <summary>
    /// The four named presets as the renderer builds them — very low (`detailFlags` 0x39d, model LOD 1,
    /// shadows 0, one light, no thing shadows or decals, particles and heads at 0.2), low (0x210, 3,
    /// 2, two lights, one shadow, 250 decals, 0.5), normal (0, 6, 5, four lights, one shadow, 500
    /// decals, 1.0) and high (0, 8, 7, the same) — all with 256-pixel mips, four of them and none
    /// skipped, and 16-bit textures. The fifth, custom, starts as a copy of normal (the 0x16-dword
    /// copy at the end of the renderer's constructor) until `[renderCustom]` says otherwise.
    /// </summary>
    public static readonly DetailPreset[] Named =
    [
        Make(0x39d, 1, 0, 1, 0, 0, 0.2f, 0.2f),
        Make(0x210, 3, 2, 2, 1, 250, 0.5f, 0.5f),
        Make(0, 6, 5, 4, 1, 500, 1f, 1f),
        Make(0, 8, 7, 4, 1, 500, 1f, 1f),
    ];

    /// <summary>
    /// **What Vitomir renders with, always** — every one of the twenty fields at its top (by design:
    /// the graphics settings are presentation, not rules, and
    /// "maxed out" is the faithful look; the Display screen's detail level, the Advanced screen's
    /// boxes and bars, <c>masquerade.ini</c>'s <c>[render]</c>/<c>[renderCustom]</c> and our
    /// <c>adv.*</c> keys are read, shown and kept, and reach nothing until the graphics page is
    /// redone after v1.0). The one place the renderer's settings come from.
    /// <list type="bullet">
    /// <item><c>detailFlags</c> 0x20: every feature-off bit clear — lightmaps (not vertex lighting),
    /// light blobs, sky (<c>opdaHWTrans</c>), reflections, styled lights, 3-D heads, decals fading —
    /// and bit 5 set, trilinear rather than bilinear;</item>
    /// <item><c>optionFlags</c> 2: 32-bit textures;</item>
    /// <item>the model LOD at index 9, the bar's top (<c>quanta=9</c>) — 0.001, 1,000, minimum and
    /// maximum 1: the whole model at every distance, which is what Vitomir draws anyway;</item>
    /// <item>512-pixel mips (the mip-size bar's top, stop 4), six of them (the mips bar's top, stop
    /// 5 + 1), none skipped;</item>
    /// <item>eight lights a thing (the lights bar's top, stop 7 + 1; the picker asserts no more);</item>
    /// <item>the shadow index 9, the bar's top — full texture, detail 1.0, height 4, full to 3,000,
    /// farthest 6,000 — and four shadows a thing (the shadows bar's top, <c>quanta=4</c>);</item>
    /// <item>500 decals — normal's and high's, the most any preset gives: the custom bar's top (stop
    /// 9 × 50) reads back only 450;</item>
    /// <item>particles and heads at 1.0 (both bars' tops, stop 10).</item>
    /// </list>
    /// </summary>
    public static readonly DetailPreset Maxed = Make(0x20, 9, 9, 8, 4, 500, 1f, 1f) with
    {
        OptionFlags = 2, Textures32 = true, MaxMipSize = 0x200, MaxMips = 6,
    };

    /// <summary>The <c>detailFlags</c> bit that turns reflections off (the Advanced screen's box, inverted).</summary>
    public const uint NoReflections = 0x10;

    /// <summary>Whether the mirrors are drawn: <see cref="Maxed"/>'s reflections bit clear — always.</summary>
    public static bool Reflections => (Maxed.DetailFlags & NoReflections) == 0;

    /// <summary>The preset for a level 0..3, or <paramref name="custom"/> for 4.</summary>
    public static DetailPreset Of(int level, DetailPreset? custom = null) =>
        level is >= 0 and < 4 ? Named[level] : custom ?? Named[2];

    /// <summary>
    /// The custom preset out of `masquerade.ini`'s <c>[renderCustom]</c> — the fifth preset's own
    /// section, read over the copy of normal the constructor made (0x4c2d..: <c>GetPrivateProfileInt</c>
    /// for the integers, the struct form for the floats) — or null when the file has no such section.
    /// </summary>
    public static DetailPreset? Custom(IniFile ini)
    {
        if (!ini.Section("renderCustom").Any()) return null;
        var p = Named[2];
        int I(string key, int was) => ini["renderCustom." + key] is { } v && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : was;
        float F(string key, float was) => IniFile.ProfileFloat(ini["renderCustom." + key], was);
        uint options = (uint)I("optionFlags", (int)p.OptionFlags);
        return p with
        {
            OptionFlags = options, DetailFlags = (uint)I("detailFlags", (int)p.DetailFlags),
            ModelLodIndex = I("modelLODIndex", p.ModelLodIndex), ModelLodScale = F("modelLODScale", p.ModelLodScale),
            ModelLodMinDist = F("modelLODMinDist", p.ModelLodMinDist), ModelLodMin = F("modelLODMin", p.ModelLodMin),
            ModelLodMax = F("modelLODMax", p.ModelLodMax), MaxMipSize = I("maxMipSize", p.MaxMipSize),
            MaxMips = I("maxMips", p.MaxMips), SkipMips = I("skipMips", p.SkipMips),
            ShadowDetailIndex = I("shadowDetailIndex", p.ShadowDetailIndex), ShadowMode = I("shadowMode", p.ShadowMode),
            ShadowDetail = F("shadowDetail", p.ShadowDetail), ShadowHeight = F("shadowHeight", p.ShadowHeight),
            ShadowMaxDist = F("shadowMaxDist", p.ShadowMaxDist), ShadowFullDist = F("shadowFullDist", p.ShadowFullDist),
            MaxThingShadows = I("maxThingShadows", p.MaxThingShadows), MaxThingLights = I("maxThingLights", p.MaxThingLights) & 0xff,
            MaxDecals = I("maxDecals", p.MaxDecals), ParticleLod = F("particleLOD", p.ParticleLod), HeadLod = F("uiHeadLOD", p.HeadLod),
            Textures32 = ((options & 0xff) >> 1 & 1) != 0,
        };
    }

    /// <summary>
    /// What the Advanced screen shows of a preset — its show hook and both detail arrows, **the show hook**,
    /// box by box and bar by bar (the element each call is made on is the one the set-up cached it
    /// in). Most boxes are a <c>detailFlags</c> bit **inverted**: a set bit turns a feature off.
    /// </summary>
    public static Dictionary<string, int> Controls(DetailPreset p)
    {
        uint f = p.DetailFlags;
        int Off(int bit) => (int)(~f >> bit & 1);
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["opdaSLLightmap"] = Off(0),
            ["opdaSLVertex"] = (int)(f & 1),
            ["opdaStyledLights"] = Off(8),
            ["opdaLightblobs"] = Off(2),
            // 32 → 0, 64 → 1, 128 → 2, 512 → 4, and anything else (256 among them) → 3.
            ["opdaMipSelectionScroll"] = p.MaxMipSize switch { 0x20 => 0, 0x40 => 1, 0x80 => 2, 0x200 => 4, _ => 3 },
            ["opdaBit16"] = p.Textures32 ? 0 : 1,
            ["opdaBit32"] = p.Textures32 ? 1 : 0,
            ["opdaTrilinear"] = (f & 0x20) != 0 ? 1 : 0,
            ["opdaBilinear"] = Off(5),
            ["opdaReflections"] = Off(4),
            ["opdaHWtrans"] = Off(3),
            ["opda3dHeads"] = Off(9),
            ["opdaNumDLScroll"] = (byte)p.MaxThingLights - 1,
            ["opdaNumShadowsScroll"] = p.MaxThingShadows,
            ["opdaShadDetScroll"] = p.ShadowDetailIndex,
            ["opdaDecalsScroll"] = (int)((uint)(p.MaxDecals * 10) / 500),
            ["opdaNumMipsScroll"] = p.MaxMips - 1,
            ["opdaModelLODScroll"] = p.ModelLodIndex,
            // Times 10, truncated.
            ["opdaHeadLODScroll"] = (int)(p.HeadLod * 10f),
            ["opdaParticleDetailScroll"] = (int)(p.ParticleLod * 10f),
        };
    }
}
