// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Scene;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// How a shell is drawn: the materials the scripts name, found the engine's way, and
/// the pass `rlDirect.dll` makes of them — what the renderer then draws.
/// </summary>
public class ShellDrawTests
{
    /// <summary>
    /// Every shipped shell material is a <c>pass 0 … full_add</c> line in <c>materials/General</c>;
    /// a name with only a texture is a plain material; a name with neither is the default. The
    /// white flash of a character taken over is <c>shellSprite_white</c> added over the body.
    /// </summary>
    [Fact]
    public void Shell_materials_are_found_the_engines_way()
    {
        using var files = Install.Open();
        var cache = new MaterialCache(files);
        var white = cache.Items[cache.Named(Location.SteerShell)!.Value];
        Assert.Equal("materials/general/shellSprite_white", white.Name);
        Assert.Equal(RenderKind.FullAdd, Assert.Single(white.Passes).Kind);

        var cloud = cache.Items[cache.Named("redCloudShell")!.Value];
        Assert.Equal(new Vector2(0.1f, 0.1f), cloud.Passes[0].Slide);

        var plain = cache.Items[cache.Named("white")!.Value];
        Assert.Equal("materials/general/white", plain.Name);
        Assert.Empty(plain.Passes);
        Assert.Equal(RenderKind.BlendParticle, DrawPlan.ForParticle(plain.Traits));

        Assert.Equal("3d/materials/dflt", cache.Items[cache.Named("red")!.Value].Name);
        Assert.Equal(cache.Named("red"), cache.Named("RED"));
        Assert.Equal(RenderKind.AddParticle, DrawPlan.ForParticle(cache.Items[cache.Named("BlueLightning")!.Value].Traits));
    }

    /// <summary>
    /// **A level's `notexture` is the default material**: 81 surfaces in 22 levels name a
    /// material that is neither a `.nam` nor a texture; the world load hands each name to the material cache,
    /// which retries with `dflt` when nothing is found — the grey 64×64 texture, not a
    /// checkerboard. A material that is there is itself, and `dflt` asked for directly is not looped.
    /// </summary>
    [Fact]
    public void A_level_material_that_is_nowhere_is_the_default_one()
    {
        using var files = Install.Open();
        var cache = new MaterialCache(files);
        var missing = cache.Items[cache.Get("materials/notexture")];
        Assert.Equal("3d/materials/dflt", missing.Name);
        Assert.False(missing.Missing);
        Assert.Equal(64, Assert.IsType<Vitomir.Formats.TgaImage>(missing.Images[0]).Width);
        Assert.Equal(cache.Get("materials/notexture"), cache.Named("dflt"));
        Assert.Equal("materials/General/white", cache.Items[cache.Get("materials/General/white")].Name);
        Assert.False(cache.Items[cache.Get("materials/General/white")].Missing);
    }

    /// <summary>
    /// The steer flash as a pass: <c>full_add</c> with <c>pass 0</c>'s texture, the vertices' alpha
    /// the shell's as it ramps, and nothing at all once the shell is at 0 or off.
    /// </summary>
    [Fact]
    public void The_steer_flash_is_the_body_again_added_in_white()
    {
        using var files = Install.Open();
        var cache = new MaterialCache(files);
        var shell = cache.Items[cache.Named(Location.SteerShell)!.Value];
        var fx = new ModelFx();
        Assert.Null(ShellDraw.For(fx, shell, null));

        fx.SetShell(Location.SteerShell, 0x5000, 0f, 1f, Location.SteerShellRamp);
        Assert.Null(ShellDraw.For(fx, shell, null));                    // at 0: no pass
        fx.Tick(0.05f);
        var draw = ShellDraw.For(fx, shell, null)!.Value;
        Assert.Equal(RenderKind.FullAdd, draw.Kind);
        Assert.Equal(shell.Passes[0].Image, draw.Image);
        Assert.Equal(ShellDraw.AlphaOf(fx.ShellAlpha), draw.Alpha);
        Assert.True(draw.Alpha > 0f);
        Assert.False(ShellDraw.HidesModel(fx));

        fx.EndShell();
        Assert.Null(ShellDraw.For(fx, shell, null));
    }

    /// <summary>
    /// The alpha a vertex gets is the shell's times 255, truncated, in eight bits — so the 2 and 3
    /// the scripts give with no ramp wrap to 254 and 253.
    /// </summary>
    [Fact]
    public void The_shells_alpha_is_eight_bits_of_it_times_255()
    {
        Assert.Equal(1f, ShellDraw.AlphaOf(1f));
        Assert.Equal(178f / 255f, ShellDraw.AlphaOf(0.7f));
        Assert.Equal(254f / 255f, ShellDraw.AlphaOf(2f));
        Assert.Equal(253f / 255f, ShellDraw.AlphaOf(3f));
    }

    /// <summary>
    /// Bit 0x80 leaves the model's own pass out, so only the shell is drawn — and with no model
    /// pass before it, the model's own slide is not added to the shell's. A plain shell material
    /// takes its second mode and its current frame.
    /// </summary>
    [Fact]
    public void Bit_80_draws_the_shell_in_place_of_the_body()
    {
        using var files = Install.Open();
        var cache = new MaterialCache(files);
        var cloud = cache.Items[cache.Named("redCloudShell")!.Value];
        var sliding = cache.Items[cache.Named("shellSprite_blue")!.Value];
        var fx = new ModelFx();
        fx.SetShell("redCloudShell", 0x5000, 0f, 1f, 0f);
        Assert.Equal(new Vector2(0.6f, 0.6f), ShellDraw.For(fx, cloud, sliding)!.Value.Slide);

        fx.SetShell("redCloudShell", ModelFx.ShellBit80, 0f, 1f, 0f);
        Assert.True(ShellDraw.HidesModel(fx));
        Assert.Equal(new Vector2(0.1f, 0.1f), ShellDraw.For(fx, cloud, sliding)!.Value.Slide);

        var plain = cache.Items[cache.Named("white")!.Value];
        fx.SetShell("white", 0, 0f, 0.5f, 0f);
        var draw = ShellDraw.For(fx, plain, null)!.Value;
        Assert.Equal((RenderKind.BlendParticle, plain.CurrentImage, 127f / 255f), (draw.Kind, draw.Image, draw.Alpha));
    }
}
