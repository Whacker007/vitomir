// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

public class LightStyleTests
{
    /// <summary>One second of frames at 60 Hz.</summary>
    static float[] Run(LightStyles styles, int style, int frames = 60, float ms = 1000f / 60f)
    {
        var values = new float[frames];
        for (int i = 0; i < frames; i++) { styles.Step(ms); values[i] = styles.Value(style); }
        return values;
    }

    [Fact]
    public void Style_zero_is_always_full_and_unswitched_styles_are_black()
    {
        var styles = new LightStyles();
        Assert.Equal(1f, styles.Value(0));
        Assert.Equal(255, styles.Brightness(0));
        // 16 to 23 are on at the start of a level, waiting for a script.
        Assert.Equal(1f, styles.Value(16));
        Assert.Equal(1f, styles.Value(23));
        // Everything else starts switched off, which is black rather than dim.
        Assert.Equal(0f, styles.Value(10));
        Assert.Equal(0f, styles.Value(24));
        Assert.Equal(0f, styles.Value(25));
        Assert.Equal(0f, styles.Value(31));
        // A style number no engine table has (one shipped level carries a junk one) is full.
        Assert.Equal(1f, styles.Value(15142));
    }

    [Fact]
    public void A_script_can_switch_a_style_off_and_on()
    {
        var styles = new LightStyles();
        styles.Enable(16, false);
        Assert.Equal(0f, styles.Value(16));
        Assert.Equal(0, styles.Brightness(16));
        styles.Enable(16, true);
        Assert.Equal(1f, styles.Value(16));
    }

    [Theory]
    [InlineData(1, 0.95f)]
    [InlineData(2, 0.8f)]
    [InlineData(3, 0.7f)]
    public void A_torch_flickers_between_its_floor_and_full(int style, float floor)
    {
        var styles = new LightStyles();
        var values = Run(styles, style, frames: 600);
        Assert.All(values, v => Assert.InRange(v, floor - 0.001f, 1.001f));
        // It moves, and it doesn't settle: the phase advances by a random amount each frame.
        Assert.True(values.Max() - values.Min() > (1f - floor) * 0.5f);
        Assert.True(values.Distinct().Count() > 100);
    }

    [Theory]
    [InlineData(4, 0f)]
    [InlineData(5, 0.2f)]
    [InlineData(6, 0.5f)]
    public void A_pulse_ramps_between_its_floor_and_full(int style, float floor)
    {
        var styles = new LightStyles();
        var values = Run(styles, style, frames: 600);
        Assert.True(values.Max() > 0.99f);
        // It starts dark and climbs, as the original's does, and only afterwards swings between
        // its floor and full.
        var settled = values.SkipWhile(v => v < 0.999f).ToArray();
        Assert.All(settled, v => Assert.InRange(v, floor - 0.001f, 1.001f));
        Assert.True(settled.Min() < floor + 0.05f);
        // A triangle: every step is the same size, whichever way it is going, bar the turns.
        var steps = settled.Zip(settled.Skip(1), (a, b) => MathF.Abs(b - a)).Where(d => d > 0.0001f).ToArray();
        float usual = steps.Order().ElementAt(steps.Length / 2);
        Assert.True(steps.Count(d => MathF.Abs(d - usual) < 0.0005f) > steps.Length * 0.9f);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void A_fluorescent_tube_is_on_or_off_and_mostly_on(int style)
    {
        var styles = new LightStyles();
        var values = Run(styles, style, frames: 6000);          // 100 seconds
        Assert.All(values, v => Assert.True(v is 0f or 1f));
        // Its dark blinks are an eighth as long as its lit stretches, so it is lit most of the time.
        float lit = values.Count(v => v == 1f) / (float)values.Length;
        Assert.InRange(lit, 0.7f, 0.95f);
        // And it does blink, more than once.
        int switches = values.Zip(values.Skip(1), (a, b) => a != b ? 1 : 0).Sum();
        Assert.True(switches > 4);
    }

    [Fact]
    public void Brightness_is_the_value_as_the_renderer_reads_it()
    {
        var styles = new LightStyles();
        Assert.Equal(255, styles.Brightness(0));
        styles.Step(16f);
        // Only a style at full brightness lets a surface use the two-texture-stage modes.
        Assert.True(styles.Brightness(2) < 255);
        Assert.Equal(255, styles.Brightness(16));
    }
}
