// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Engine.World;

/// <summary>
/// The engine's light styles: 32 channels, each with a brightness from 0 to 1 that the renderer
/// multiplies into every lightmap layer drawn for that style.
/// The original keeps no curves in its files — the SDK's "engine lightstyles" are three families of
/// procedure, registered at start-up and stepped once a frame with the frame's milliseconds:
/// <list type="bullet">
/// <item>0 — steady, always full.</item>
/// <item>1–3 <b>torch</b>: a sine between the style's floor and full, whose phase advances by a
/// random amount each frame, so the flicker never repeats.</item>
/// <item>4–6 <b>pulse</b>: ramps between the style's floor and full — a triangle for 4, and for 5
/// and 6 a jump back up at the floor (6 is a falling sawtooth).</item>
/// <item>7–9 <b>fluorescent</b>: on or off, with a random on time and a much shorter random off
/// time — a tube that blinks.</item>
/// <item>16–23: steady and on, for scripts to switch (the engine's EnableLightStyle and
/// DisableLightStyle); every other style starts switched off, which means black.</item>
/// </list>
/// The random numbers are ours, not the original's: same shape, different rolls.
/// </summary>
public sealed class LightStyles
{
    public const int Count = 32;
    /// <summary>Styles 1–3, the flicker of a torch.</summary>
    const int FirstTorch = 1, LastTorch = 3;
    /// <summary>Styles 4–6, a light that swells and fades.</summary>
    const int FirstPulse = 4, LastPulse = 6;
    /// <summary>Styles 7–9, a fluorescent tube.</summary>
    const int FirstFluoro = 7, LastFluoro = 9;
    /// <summary>Styles 16–23 are on at the start of a level and wait for a script to switch them.</summary>
    const int FirstSwitchable = 16, LastSwitchable = 23;

    // The original's own numbers, by style.
    static readonly float[] TorchRate = [0f, 0.05f, 0.025f, 0.1f];           // radians of phase per random unit per ms
    static readonly float[] TorchFloor = [0f, 0.95f, 0.8f, 0.7f];            // how far down the flicker dips
    static readonly float[] PulseSpeed = [0f, 0f, 0f, 0f, 0.001f, 0.002f, 0.002f];   // per ms
    static readonly float[] PulseFloor = [0f, 0f, 0f, 0f, 0f, 0.2f, 0.5f];
    static readonly float[] FluoroSpan = [0f, 0f, 0f, 0f, 0f, 0f, 0f, 2000f, 1000f, 600f];  // the longest on time, ms
    static readonly float[] FluoroFirst = [0f, 0f, 0f, 0f, 0f, 0f, 0f, 1000f, 500f, 300f];  // how long the first interval lasts
    static readonly bool[] FluoroLit = [false, false, false, false, false, false, false, false, true, false];
    /// <summary>A tube's dark blink is a fraction of its lit time.</summary>
    const float FluoroDarkFraction = 0.125f;

    readonly float[] value = new float[Count];
    readonly bool[] enabled = new bool[Count];
    readonly bool[] animated = new bool[Count];
    readonly float[] phase = new float[Count];       // torch phase, pulse level, fluorescent clock
    readonly bool[] rising = new bool[Count];        // pulse direction
    readonly bool[] lit = new bool[Count];           // fluorescent state
    readonly float[] until = new float[Count];       // when the fluorescent state ends
    readonly Random random;

    /// <param name="seed">Fixed so a still frame renders the same twice; the original seeds from the clock.</param>
    public LightStyles(int? seed = 1)
    {
        // A game's is the launch's stream, carried on across places (Ai.Chance); a test's its own.
        random = Ai.Chance.Game?.Lights ?? (seed is { } s ? new Random(s) : new Random());
        for (int i = 0; i < Count; i++) value[i] = 1f;
        enabled[0] = animated[0] = true;
        for (int i = FirstSwitchable; i <= LastSwitchable; i++) enabled[i] = animated[i] = true;
        for (int i = FirstTorch; i <= LastFluoro; i++) { enabled[i] = animated[i] = true; value[i] = 0f; }
        for (int i = FirstFluoro; i <= LastFluoro; i++) { lit[i] = FluoroLit[i]; until[i] = FluoroFirst[i]; }
        for (int i = FirstPulse; i <= LastPulse; i++) rising[i] = true;
        Step(0f);
    }

    /// <summary>
    /// **The old renderer's spare light styles, drawn on purpose** (ours, found).
    /// Earlier, every world renderer of the old program made its own <see cref="LightStyles"/> as it was built — a
    /// default the place's styles were then put over, except for the party heads' and the portraits' renderers, which draw
    /// with it — and a <see cref="LightStyles"/> steps once on making, drawing three numbers from the launch's lights stream
    /// (<c>Chance.Game.Lights</c>). The 17 replay baselines' digests hold those draws in their <c>lights</c> field: the
    /// frame's renderer and the heads' at the start, the frame's again at each place change. The aim is as few
    /// re-recordings as possible) keeps them, so the renderer no longer makes them: whoever builds a renderer that made one
    /// calls this first, at the same point, and hands the renderer what it answers (<c>vitomir-tools</c>' host, and the Classic
    /// module at the same points of its start). One call, three draws.
    /// </summary>
    public static LightStyles DrawTheRenderersSpare() => new();

    /// <summary>Whether a script has this style switched on. A style switched off is black, not dim.</summary>
    public bool IsEnabled(int style) => (uint)style < Count && enabled[style];

    /// <summary>The engine's EnableLightStyle / DisableLightStyle, for the scripting layer.</summary>
    public void Enable(int style, bool on)
    {
        if ((uint)style < Count) enabled[style] = on;
    }

    /// <summary>This style's brightness, 0 to 1. A style out of range, or one no level uses, is full.</summary>
    public float Value(int style) => (uint)style < Count ? (enabled[style] ? value[style] : 0f) : 1f;

    /// <summary>The same as the renderer sees it: 0–255, and 255 exactly when the style is at full.</summary>
    public byte Brightness(int style) => (byte)(int)(Value(style) * 255f);

    /// <summary>Steps every style by one frame. <paramref name="milliseconds"/> is the frame's own time.</summary>
    public void Step(float milliseconds)
    {
        for (int i = 0; i < Count; i++)
        {
            if (!animated[i]) continue;
            if (i is >= FirstTorch and <= LastTorch) value[i] = Torch(i, milliseconds);
            else if (i is >= FirstPulse and <= LastPulse) value[i] = Pulse(i, milliseconds);
            else if (i is >= FirstFluoro and <= LastFluoro) value[i] = Fluorescent(i, milliseconds);
        }
    }

    /// <summary>rand() / RAND_MAX in the original: 0 to 1.</summary>
    float Roll() => random.NextSingle();

    // Styles 1–3: a sine flicker between the floor and full, whose phase jumps by a
    // random amount each frame.
    float Torch(int i, float ms)
    {
        phase[i] += Roll() * TorchRate[i] * ms;
        float floor = TorchFloor[i];
        return floor + (MathF.Sin(phase[i]) + 1f) * (1f - floor) * 0.5f;
    }

    // Styles 4–6: up to full and back by the overshoot; at the floor, set to the floor
    // plus the fraction of where it fell to — a triangle for style 4 (floor 0), a jump to ~0.4 for
    // style 5 and to ~0.95–1 for style 6, whose wave is a falling sawtooth.
    float Pulse(int i, float ms)
    {
        float step = ms * PulseSpeed[i], floor = PulseFloor[i];
        if (rising[i])
        {
            phase[i] += step;
            if (phase[i] > 1f) { rising[i] = false; phase[i] = 1f - (phase[i] - MathF.Truncate(phase[i])); }
        }
        else
        {
            phase[i] -= step;
            if (phase[i] < floor)
            {
                float over = phase[i] - MathF.Truncate(phase[i]);
                phase[i] = floor + (over < 0f ? -over : over);
                rising[i] = true;
            }
        }
        return phase[i];
    }

    // Styles 7–9: on for a random time, off for an eighth of it.
    float Fluorescent(int i, float ms)
    {
        phase[i] += ms;
        if (phase[i] > until[i])
        {
            phase[i] -= until[i];
            lit[i] = !lit[i];
            until[i] = Roll() * FluoroSpan[i] * (lit[i] ? 1f : FluoroDarkFraction);
        }
        return lit[i] ? 1f : 0f;
    }
}
