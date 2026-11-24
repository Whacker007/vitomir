// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// A model's alpha, scale and shell with their ramps (the render model's setters and their
/// per-frame steps). The units are the engine's inconsistent
/// ones: the alpha ramp in milliseconds, the other two in seconds.
/// </summary>
public class ModelFxTests
{
    [Fact]
    public void Alpha_ramps_in_milliseconds_and_stops_on_its_target()
    {
        var fx = new ModelFx();
        fx.SetAlpha(0.5f, 1000f);
        Assert.True(fx.AlphaRamping);
        fx.Tick(0.5f);
        Assert.Equal(0.75f, fx.Alpha, 5);
        fx.Tick(0.6f);
        Assert.Equal(0.5f, fx.Alpha);
        Assert.False(fx.AlphaRamping);

        fx.SetAlpha(0.2f, 0f);                                    // no ramp: at once
        Assert.Equal(0.2f, fx.Alpha);
        fx.SetAlpha(1f, 500f);
        fx.SetAlpha(0.2f, 500f);                                  // what it already has: the ramp stops
        Assert.False(fx.AlphaRamping);
        Assert.Equal(0.2f, fx.Alpha);
    }

    /// <summary>A ramp of 0 is an infinite rate: the scale arrives on the next update — not on an update of no time.</summary>
    [Fact]
    public void A_zero_ramp_scale_lands_on_the_next_update()
    {
        var fx = new ModelFx();
        fx.SetScale(2f, 0f);
        Assert.Equal(Vector3.One, fx.Scale);
        fx.Tick(0f);
        Assert.Equal(Vector3.One, fx.Scale);
        fx.Tick(1f / 30f);
        Assert.Equal(new Vector3(2f), fx.Scale);
        Assert.True(fx.Scaled);
        Assert.False(fx.ScaleRamping);
    }

    [Fact]
    public void Scale_ramps_in_seconds_and_is_held_between_a_hundredth_and_fifty()
    {
        var fx = new ModelFx();
        fx.SetScale(3f, 2f);
        fx.Tick(1f);
        Assert.Equal(new Vector3(2f), fx.Scale);
        fx.Tick(5f);
        Assert.Equal(new Vector3(3f), fx.Scale);

        fx.SetScale(100f, -1f);
        Assert.Equal(new Vector3(ModelFx.MaxScale), fx.Scale);
        fx.SetScale(0f, -1f);
        Assert.Equal(new Vector3(ModelFx.MinScale), fx.Scale);
        fx.SetScale(1f, -1f);
        Assert.False(fx.Scaled);

        // The per-axis setter's immediate form leaves the "scaled" bit as it was.
        fx.SetScale(new Vector3(1f, 2f, 1f), -1f);
        Assert.False(fx.Scaled);
        Assert.Equal(new Vector3(1f, 2f, 1f), fx.Scale);
    }

    /// <summary>
    /// A fade-in-and-out shell ramps up, turns round once and ramps down — and a shell that fades to
    /// nothing is taken off.
    /// </summary>
    [Fact]
    public void An_in_and_out_shell_turns_round_once_and_goes_when_it_reaches_nothing()
    {
        var fx = new ModelFx();
        fx.SetShell("shellGlow", ModelFx.ShellInOut, 0f, 1f, 1f);
        Assert.Equal("shellGlow", fx.Shell);
        fx.Tick(0.5f);
        Assert.Equal(0.5f, fx.ShellAlpha, 5);
        fx.Tick(0.6f);                                            // arrives, turns round
        Assert.Equal(1f, fx.ShellAlpha, 5);
        Assert.Equal(0u, fx.ShellFlags & ModelFx.ShellInOut);
        fx.Tick(0.5f);
        Assert.Equal(0.5f, fx.ShellAlpha, 5);
        fx.Tick(0.6f);
        Assert.Null(fx.Shell);
        Assert.False(fx.Busy);
    }

    [Fact]
    public void A_pulsing_shell_never_stops_and_an_immediate_one_is_not_clamped()
    {
        var fx = new ModelFx();
        fx.SetShell("shellGlow", ModelFx.ShellPulse, 0.2f, 0.8f, 1f);
        for (int i = 0; i < 100; i++) fx.Tick(0.37f);
        Assert.True(fx.ShellRamping);
        Assert.InRange(fx.ShellAlpha, 0.2f, 0.8f);

        fx.SetShell("shellGlow", 0, 0f, 1.5f, 0f);
        Assert.Equal(1.5f, fx.ShellAlpha);
        Assert.False(fx.ShellRamping);

        // A material the install does not have turns the shell off.
        fx.SetShell("noSuchShell", 0, 0f, 1f, 1f, exists: _ => false);
        Assert.Null(fx.Shell);
        Assert.Equal(0f, fx.ShellAlpha);
    }
}
