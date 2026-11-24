// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Scene;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

public class RenderKindTests
{
    [Fact]
    public void Mode_names_are_the_original_table_in_its_own_order()
    {
        Assert.Equal(43, RenderKinds.Names.Length);
        Assert.Equal("sky", RenderKinds.Names[0]);
        Assert.Equal("full_sub", RenderKinds.Names[^1]);
        // A name's place in the table is its mode number, counting from one.
        for (int i = 0; i < RenderKinds.Names.Length; i++)
        {
            Assert.True(RenderKinds.TryParse(RenderKinds.Names[i], out var kind));
            Assert.Equal(i + 1, (int)kind);
        }
        Assert.Equal(RenderKind.LightVolume, RenderKinds.Parse("LIGHTVOLUME"));
    }

    [Fact]
    public void An_unknown_mode_name_falls_back_to_rgb()
    {
        // As in the original: the name lookup fails and the pass keeps the 3 the parser put there.
        Assert.False(RenderKinds.TryParse("modplusdest", out _));
        Assert.Equal(RenderKind.Rgb, RenderKinds.Parse("modplusdest"));
        Assert.Equal(RenderKind.Rgb, RenderKinds.Parse("modulatepost"));
    }

    [Fact]
    public void Every_mode_but_one_has_state()
    {
        var missing = Enum.GetValues<RenderKind>()
            .Where(k => k != RenderKind.None && RenderKinds.State(k) is null).ToArray();
        // The release renderer's block for maskedlightmap is empty: it leaves the device alone.
        Assert.Equal([RenderKind.MaskedLightmap], missing);
        // 43 named modes plus the renderer's two unnamed ones, less the empty one.
        Assert.Equal(44, RenderKinds.WithState.Count());
    }

    [Fact]
    public void The_modes_the_shipped_materials_use_have_the_state_the_renderer_sets()
    {
        // Window light shafts: two-sided, texture only, alpha from the vertex, added to the frame,
        // depth-tested but not written, fog off (and towards black where it is on).
        var lightVolume = RenderKinds.State(RenderKind.LightVolume)!;
        Assert.Equal(new TexStage(TexOp.SelectArg1, TexArg.Texture, TexArg.Diffuse), lightVolume.Color);
        Assert.Equal(new TexStage(TexOp.SelectArg2, TexArg.Texture, TexArg.Diffuse), lightVolume.Alpha);
        Assert.Equal((Blend.SrcAlpha, Blend.One, true), (lightVolume.SrcBlend, lightVolume.DestBlend, lightVolume.BlendEnable));
        Assert.Equal((false, Cull.None, false), (lightVolume.DepthWrite, lightVolume.Cull, lightVolume.Fog));
        Assert.True(lightVolume.FogBlack);

        // Water's middle pass multiplies the frame by the texture; its last pass multiplies the lightmap.
        var modPlusDest = RenderKinds.State(RenderKind.FullModPlusDest)!;
        Assert.Equal((Blend.DestColor, Blend.One, true), (modPlusDest.SrcBlend, modPlusDest.DestBlend, modPlusDest.BlendEnable));
        var lightmapMod = RenderKinds.State(RenderKind.LightmapMod)!;
        Assert.Equal((Blend.DestColor, Blend.Zero, true), (lightmapMod.SrcBlend, lightmapMod.DestBlend, lightmapMod.BlendEnable));
        Assert.Equal(1, lightmapMod.TexCoord);      // a lightmap pass samples with the second coordinate set

        // Plain rgb, the mode an unknown name falls back to: opaque, texture × vertex light, fogged.
        var rgb = RenderKinds.State(RenderKind.Rgb)!;
        Assert.Equal(new TexStage(TexOp.Modulate, TexArg.Texture, TexArg.Diffuse), rgb.Color);
        Assert.Equal((true, false, true), (rgb.DepthWrite, rgb.BlendEnable, rgb.Fog));

        // Only additive modes push the fog towards black.
        foreach (var (kind, state) in RenderKinds.WithState)
            Assert.Equal(state.FogBlack, state.BlendEnable && state.DestBlend == Blend.One
                                         && state.SrcBlend is Blend.One or Blend.SrcAlpha);
    }

    [Fact]
    public void A_material_with_pass_lines_resolves_to_a_pass_per_line()
    {
        using var files = Install.Open();
        var water = Materials.Load(files, "materials/General/factoryWater");
        Assert.Equal(3, water.Passes.Length);
        Assert.Equal([RenderKind.FullModPlusDest, RenderKind.FullModPlusDest, RenderKind.LightmapMod],
                     water.Passes.Select(p => p.Kind));
        // Both texture passes name the same TGA, so they share one decoded image.
        Assert.Equal(water.Passes[0].Image, water.Passes[1].Image);
        Assert.Equal(MaterialPass.Lightmap, water.Passes[2].Image);
        Assert.Equal(new Vector2(0.1f, 0.1f), water.Passes[0].Slide);
        Assert.Equal(new Vector2(-0.2f, -0.2f), water.Passes[1].Slide);
        Assert.Equal(Vector2.Zero, water.Passes[2].Slide);
        Assert.NotNull(water.Images[0]);
        Assert.True(water.HasPasses);
        // Its first pass multiplies the frame, so the geometry behind it must be drawn first.
        Assert.True(water.PassesAreTranslucent);
        // Water that starts from an opaque pass draws with the opaque geometry instead.
        var mines = Materials.Load(files, "materials/General/minesWater");
        Assert.Equal([RenderKind.FullBright, RenderKind.FullModulate, RenderKind.LightmapMod],
                     mines.Passes.Select(p => p.Kind));
        Assert.False(mines.PassesAreTranslucent);

        var shaft = Materials.Load(files, "materials/General/lightVolume");
        Assert.Equal([RenderKind.LightVolume], shaft.Passes.Select(p => p.Kind));
        Assert.Equal(new Vector2(0.2f, 0f), shaft.Passes[0].Slide);
        Assert.True(shaft.PassesAreTranslucent);
    }

    [Fact]
    public void A_material_with_a_stray_token_keeps_the_originals_behaviour()
    {
        // waterShell_b.nam reads "pass 0 texture waterShell_b.tga full_add": the pass's texture name
        // is "texture" and its mode name is the TGA, which isn't in the table — so the mode is rgb,
        // not full_add. Four shipped materials carry this typo; no level or model uses them.
        using var files = Install.Open();
        var broken = Materials.Load(files, "materials/General/waterShell_b");
        Assert.Single(broken.Passes);
        Assert.Equal(RenderKind.Rgb, broken.Passes[0].Kind);
        // There is no "texture.tga", and nothing rescues it: Vitomir used to fall back to the
        // material's own name, and that was taken out, since nothing uses these.
        Assert.Null(broken.Images[broken.Passes[0].Image]);
    }

    [Fact]
    public void Only_the_first_four_pass_lines_count()
    {
        var nam = NamMaterial.Parse(string.Join('\n', Enumerable.Range(0, 6).Select(i => $"pass {i} t{i}.tga rgb")));
        Assert.Equal(6, nam.Passes.Count);              // the file says six
        using var files = Install.Open();
        Assert.Equal(4, Materials.MaxPasses);           // the original's parser keeps four
    }
}

public class DrawPlanTests
{
    static readonly MaterialTraits Plain = new();
    static readonly LayerLight[] None = [];
    static readonly LayerLight[] OneSteady = [new(0, 255)];
    /// <summary>A layer lit by a style that is halfway down its flicker.</summary>
    static readonly LayerLight[] Styled = [new(2, 128)];
    static readonly LayerLight[] SteadyAndStyled = [new(0, 255), new(2, 128)];

    static RenderKind[] Plan(MaterialTraits m, LayerLight[] layers, bool translucent = false)
    {
        var steps = new List<DrawStep>();
        DrawPlan.ForSurface(m, layers, translucent, steps);
        return [.. steps.Select(s => s.Kind)];
    }

    [Fact]
    public void A_models_material_takes_one_mode_from_its_keywords()
    {
        Assert.Equal(RenderKind.Rgb, DrawPlan.ForThing(Plain));
        Assert.Equal(RenderKind.Rgb2Sided, DrawPlan.ForThing(Plain with { TwoSided = true }));
        Assert.Equal(RenderKind.FullBright, DrawPlan.ForThing(Plain with { FullyLit = true }));
        Assert.Equal(RenderKind.RgbMasked, DrawPlan.ForThing(Plain with { Masked = true }));
        Assert.Equal(RenderKind.RgbTexAlpha, DrawPlan.ForThing(Plain with { AlphaTexture = true }));
        Assert.Equal(RenderKind.AddParticle, DrawPlan.ForThing(Plain with { AddBlend = true }));
        Assert.Equal(RenderKind.SubParticle, DrawPlan.ForThing(Plain with { SubBlend = true }));
        // The order the original tests the keywords in: additive first, masked before an alpha texture.
        Assert.Equal(RenderKind.AddParticle, DrawPlan.ForThing(Plain with { AddBlend = true, Masked = true }));
        Assert.Equal(RenderKind.RgbMasked, DrawPlan.ForThing(Plain with { Masked = true, AlphaTexture = true }));
    }

    [Fact]
    public void A_level_surface_with_one_steady_lightmap_is_one_two_stage_draw()
    {
        Assert.Equal([RenderKind.LightmappedWall], Plan(Plain, OneSteady));
        Assert.Equal([RenderKind.MaskedWall], Plan(Plain with { Masked = true }, OneSteady));
        Assert.Equal([RenderKind.AlphaLightmapped], Plan(Plain, OneSteady, translucent: true));
        // Both of those fall back to a single-texture mode without a lightmap to pair with.
        Assert.Equal([RenderKind.RgbMasked], Plan(Plain with { Masked = true }, None));
        Assert.Equal([RenderKind.RgbaTexAlpha], Plan(Plain, None, translucent: true));
        Assert.Equal([RenderKind.RgbaTexAlpha], Plan(Plain with { AlphaTexture = true }, None));
    }

    [Fact]
    public void A_level_surface_without_a_lightmap_takes_the_plain_modes()
    {
        Assert.Equal([RenderKind.Rgb], Plan(Plain, None));
        Assert.Equal([RenderKind.FullBright], Plan(Plain with { FullyLit = true }, None));
        Assert.Equal([RenderKind.RgbAdd], Plan(Plain with { AddBlend = true }, OneSteady));
        // A surface's own two-sidedness is a flag on the draw, not a different mode as it is on a model.
        Assert.Equal([RenderKind.Rgb], Plan(Plain with { TwoSided = true }, None));
    }

    [Fact]
    public void Light_styles_give_a_surface_one_draw_per_lightmap_layer()
    {
        // The first layer lays the light down, later ones add to it, then the texture multiplies over.
        Assert.Equal([RenderKind.LightmapSteady, RenderKind.LightmapAdd, RenderKind.FullModulate],
                     Plan(Plain, SteadyAndStyled));
        // A single layer that a style animates can't use the two-stage mode while it is dimmed...
        Assert.Equal([RenderKind.Lightmap, RenderKind.FullModulate], Plan(Plain, Styled));
        // ...but the same surface goes back to it the moment the style is at full again.
        Assert.Equal([RenderKind.LightmappedWall], Plan(Plain, [new LayerLight(2, 255)]));
    }

    [Fact]
    public void Detail_and_glow_maps_add_a_draw_of_their_own()
    {
        Assert.Equal([RenderKind.LightmappedWall, RenderKind.DetailModulate, RenderKind.SolidGlow],
                     Plan(Plain with { Detail = true, Glow = true }, OneSteady));
        Assert.Equal([RenderKind.Rgb, RenderKind.SolidGlow], Plan(Plain with { Glow = true }, None));
    }
}
