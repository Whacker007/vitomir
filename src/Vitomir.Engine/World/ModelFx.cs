// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a render model carries on top of its geometry and animation: its alpha, its scale, a shell,
// and whether its animation is frozen — each with the ramp the engine runs once a frame
// (the render model's update). Disciplines are made of these: Cloak of Shadows fades a character out,
// Fortitude wraps it in a glowing shell, Shape of the Beast freezes nothing but Earth Meld does.
using System.Numerics;

namespace Vitomir.Engine.World;

/// <summary>
/// A model's alpha, scale, shell and freeze (fields of the render model, and its flag bits
/// 0x100, 0x200, 0x400, 0x800, 0x20000), with the engine's own setters and its
/// per-frame update. Times are what the setters take: the alpha ramp in milliseconds, the scale
/// and shell ramps in seconds — the engine is not consistent about it, and the scripts pass what
/// each one wants.
/// </summary>
public sealed class ModelFx
{
    // ---- alpha (and the update's 0x200 case) -------------------------------------------

    /// <summary>The model's alpha, 1 opaque.</summary>
    public float Alpha { get; private set; } = 1f;
    float alphaTarget, alphaRate;

    /// <summary>Whether an alpha ramp is running (bit 0x200).</summary>
    public bool AlphaRamping { get; private set; }

    /// <summary>Where the alpha is going: its ramp's target while one runs, else the alpha itself (a save's question).</summary>
    public float AlphaHeading => AlphaRamping ? alphaTarget : Alpha;

    /// <summary>
    /// The alpha set. The alpha it already has stops any ramp and does nothing else. A ramp of 0 sets it
    /// at once. Otherwise it ramps there at a constant rate over <paramref name="rampMs"/>
    /// **milliseconds** (`× 0.001`) — a negative ramp is taken as it comes and runs the
    /// wrong way, never arriving.
    /// </summary>
    public void SetAlpha(float alpha, float rampMs)
    {
        if (alpha == Alpha) { AlphaRamping = false; return; }
        if (rampMs == 0f) { Alpha = alpha; AlphaRamping = false; return; }
        alphaTarget = alpha;
        alphaRate = (alpha - Alpha) / (rampMs * 0.001f);
        AlphaRamping = true;
    }

    // ---- scale (and the 0x100 arm) ---------------------------------------

    /// <summary>The model's scale on each axis.</summary>
    public Vector3 Scale { get; private set; } = Vector3.One;
    Vector3 scaleFrom, scaleTo;
    float scaleRate, scaleProgress;

    /// <summary>Whether a scale ramp is running (bit 0x100).</summary>
    public bool ScaleRamping { get; private set; }

    /// <summary>Where the scale is going on its first axis: its ramp's end while one runs, else the scale (a save's question).</summary>
    public float ScaleHeading => ScaleRamping ? scaleTo.X : Scale.X;

    /// <summary>Whether the model is drawn at anything but its own size (bit 0x800).</summary>
    public bool Scaled { get; private set; }

    /// <summary>The engine's hold on a scale.</summary>
    public const float MinScale = 0.01f, MaxScale = 50f;

    /// <summary>
    /// The scale set, uniform: held to 0.01–50; a negative ramp sets it at once; otherwise it ramps from
    /// where it is over <paramref name="rampSeconds"/> **seconds** — and a ramp of 0 is an infinite
    /// rate, done at the next update, which is how the MODELSCALE effect sets it.
    /// </summary>
    public void SetScale(float scale, float rampSeconds)
    {
        float s = scale < MinScale ? MinScale : scale > MaxScale ? MaxScale : scale;
        if (rampSeconds < 0f)
        {
            Scale = new Vector3(s);
            ScaleRamping = false;
            Scaled = s != 1f;
            return;
        }
        StartScale(new Vector3(s), rampSeconds);
    }

    /// <summary>
    /// The scale set, per axis: each held to 0.01–50; negative ramp sets it at once — and unlike the
    /// uniform form **does not update <see cref="Scaled"/>**; otherwise the same ramp.
    /// </summary>
    public void SetScale(Vector3 scale, float rampSeconds)
    {
        var s = Vector3.Clamp(scale, new Vector3(MinScale), new Vector3(MaxScale));
        if (rampSeconds < 0f) { Scale = s; ScaleRamping = false; return; }
        StartScale(s, rampSeconds);
    }

    void StartScale(Vector3 to, float rampSeconds)
    {
        scaleProgress = 0f;
        scaleFrom = Scale;
        scaleTo = to;
        scaleRate = 1f / rampSeconds;
        ScaleRamping = true;
    }

    // ---- shell -------------------------------------------------

    /// <summary>The shell's material, or null when there is none (bit 0x40).</summary>
    public string? Shell { get; private set; }
    /// <summary>The shell's alpha now.</summary>
    public float ShellAlpha { get; private set; }
    float shellFrom, shellTo, shellRate, shellProgress;
    /// <summary>The shell's flag bits as the model keeps them: 0x80, 0x4000 (fade in then out), 0x8000 (pulse).</summary>
    public uint ShellFlags { get; private set; }
    /// <summary>Whether the shell's alpha is ramping (bit 0x400).</summary>
    public bool ShellRamping { get; private set; }

    /// <summary>`SetShell`'s flag that fades the shell in and back out once (bit 0x4000).</summary>
    public const uint ShellInOut = 0x4000;
    /// <summary>`SetShell`'s flag that pulses the shell for ever (bit 0x8000).</summary>
    public const uint ShellPulse = 0x8000;
    /// <summary>`SetShell`'s flag the renderer reads and nothing else does (bit 0x80).</summary>
    public const uint ShellBit80 = 0x80;
    /// <summary>`SetShell`'s priority bits (0x3000): a shell whose bits are lower than the model's is refused — but the model never keeps any (<see cref="SetShell"/>).</summary>
    public const uint ShellPriority = 0x3000;

    /// <summary>
    /// The shell set. No ramp (0 or less) puts the shell straight at <paramref name="end"/>, **unclamped**,
    /// and stops a ramp; a ramp runs from <paramref name="start"/> to <paramref name="end"/>, each held
    /// to 0–1, over <paramref name="rampSeconds"/> seconds. The material is kept when it is the same
    /// name, else replaced — a <paramref name="exists"/> that says no turns the shell off instead.
    /// Then the shell is on and takes the three flag bits from <paramref name="flags"/>. The
    /// engine's `scale` argument is not here because it is never used.
    /// <para>**The priority**: first of all the shell set returns, changing nothing, when
    /// the new flags' <see cref="ShellPriority"/> bits are lower than the model's own (`flags & 0x3000`,
    /// unsigned). **The model never keeps them**: its flags start at 0 and neither this nor
    /// anything else in the model's code writes 0x1000 or 0x2000 into them — the shell set keeps 0x40, 0x80, 0x400,
    /// 0x4000 and 0x8000 only, and its children are called with the parent's word, priority bits and
    /// all, against their own empty ones. So the test is made and never refuses: a steered character's 0x5000
    /// (priority 1) is replaced by a script's shell of priority 0 as by any other. Faithful, and pinned
    /// (`StoryLeftoverTests`).</para>
    /// </summary>
    public void SetShell(string material, uint flags, float start, float end, float rampSeconds,
                         Func<string, bool>? exists = null)
    {
        if ((flags & ShellPriority) < (ShellFlags & ShellPriority)) return;
        ShellEffect = 0;                                                          // a new shell is whoever now says it is theirs (ours)
        if (rampSeconds <= 0f)
        {
            ShellAlpha = end;
            ShellRamping = false;
            ShellFlags &= ~(ShellInOut | ShellPulse);
        }
        else
        {
            shellFrom = Math.Clamp(start, 0f, 1f);
            shellTo = Math.Clamp(end, 0f, 1f);
            shellRate = 1f / rampSeconds;
            shellProgress = 0f;
            ShellRamping = true;
        }
        if (!string.Equals(Shell, material, StringComparison.OrdinalIgnoreCase)
            && exists is not null && !exists(material))
        {
            EndShell();
            return;
        }
        Shell = material;
        ShellFlags = flags & (ShellBit80 | ShellInOut | ShellPulse);
    }

    /// <summary>The shell off — material let go, alpha 0, the ramp stopped.</summary>
    public void EndShell()
    {
        ShellEffect = 0;
        Shell = null;
        ShellAlpha = 0f;
        ShellRamping = false;
        ShellFlags &= ~ShellBit80;
    }

    // ---- whose shell it is, and the shell as it stands (ours) ---------------------------------

    /// <summary>
    /// **Ours**: the GUID of the effect whose script set the shell now on the model (<see cref="EffectMade"/>),
    /// or 0 — a shell set by anything else (the steered character's flash, a line's, the console's), or none.
    /// 1.1 keeps no such record, and no save of its keeps a shell at all (the model's writer writes
    /// none; its reader clears the flag word). A save writes the shell with the effect
    /// (<c>Save.LoadedEffects</c>) for a load to put back (the repair <c>loadedeffects</c>).
    /// </summary>
    public int ShellEffect { get; private set; }

    /// <summary>Says the shell now on the model is an effect's script's (<paramref name="effect"/>, its GUID); nothing with no shell on.</summary>
    public void ShellBy(int effect)
    {
        if (Shell is not null) ShellEffect = effect;
    }

    /// <summary>
    /// A shell as it stands, ramp and all: the material, the three flag bits the model keeps, the
    /// alpha now, and where its ramp is — from, to, the rate and how far along (the shell's tick's words).
    /// </summary>
    public readonly record struct ShellState(string Material, uint Flags, float Alpha, bool Ramping,
                                             float From, float To, float Rate, float Progress);

    /// <summary>
    /// Whether the shell on the model is on its way out with nobody's help: a ramp that ends at nothing
    /// (the shell's tick takes the shell off as it gets there) — a fade out, or a flash (<see cref="ShellInOut"/>) that
    /// started from nothing — and no pulse. A shell held at a level stays until something ends it.
    /// </summary>
    public bool ShellEndsByItself =>
        Shell is not null && ShellRamping && (ShellFlags & ShellPulse) == 0
        && ((ShellFlags & ShellInOut) != 0 ? shellFrom == 0f : shellTo == 0f);

    /// <summary>The shell on the model as it stands, or null with none.</summary>
    public ShellState? ShellNow =>
        Shell is { } material ? new ShellState(material, ShellFlags, ShellAlpha, ShellRamping, shellFrom, shellTo, shellRate, shellProgress) : null;

    /// <summary>
    /// **Ours**: a shell put back exactly as <see cref="ShellNow"/> gave it — the same material and flags, the
    /// same alpha, its ramp where it was, so a flash half over ends when it would have and a pulse goes on —
    /// and marked the effect's again. Not the shell set: nothing is started, and the priority test is not made.
    /// </summary>
    public void PutShell(in ShellState shell, int effect)
    {
        Shell = shell.Material;
        ShellFlags = shell.Flags & (ShellBit80 | ShellInOut | ShellPulse);
        ShellAlpha = shell.Alpha;
        ShellRamping = shell.Ramping;
        (shellFrom, shellTo, shellRate, shellProgress) = (shell.From, shell.To, shell.Rate, shell.Progress);
        ShellEffect = effect;
    }

    // ---- freeze ------------------------------------------------------------------------------

    /// <summary>Bit 0x20000: the keyframe mixer is not advanced; alpha, scale and shell still are.</summary>
    public bool Frozen
    {
        get => frozen;
        set { frozen = value; FrozenEffect = 0; }                                 // whoever sets it now says whose it is (ours)
    }
    bool frozen;

    /// <summary>
    /// **Ours**: the GUID of the effect whose script froze the model (<c>FreezeAnimations</c> called inside an
    /// effect's stretch, <see cref="EffectMade.Current"/>), or 0 — frozen by nothing, or by something that is no
    /// effect (the console's <c>freeze</c>). 1.1 keeps no such record. Read by a save
    /// (<c>Save.LoadedEffects</c>) and by nothing else; unfreezing, or freezing again, clears it.
    /// </summary>
    public int FrozenEffect { get; private set; }

    /// <summary>Marks the freeze now on as <paramref name="effect"/>'s (ours): nothing when the model is not frozen.</summary>
    public void FrozenBy(int effect)
    {
        if (frozen) FrozenEffect = effect;
    }

    // ---- the update --------------------------------------------------------------------------

    /// <summary>
    /// The per-frame half of the model's update, <paramref name="seconds"/> of it: the alpha ramp stops on its
    /// target from whichever side it approaches (and a zero rate simply stops); the scale ramp's
    /// progress climbs to 1, the scale following as a straight blend; the shell ramps as the shell's tick
    /// has it.
    /// </summary>
    public void Tick(float seconds)
    {
        if (AlphaRamping)
        {
            if (alphaRate == 0f) AlphaRamping = false;
            float a = seconds * alphaRate + Alpha;
            Alpha = a;
            if ((alphaRate > 0f && a >= alphaTarget) || (alphaRate < 0f && a <= alphaTarget))
            {
                Alpha = alphaTarget;
                AlphaRamping = false;
            }
        }
        if (ScaleRamping && !(seconds == 0f && float.IsInfinity(scaleRate)))
        {
            float p = seconds * scaleRate + scaleProgress;
            if (p > 1f) p = 1f;
            scaleProgress = p;
            Scale = p * (scaleTo - scaleFrom) + scaleFrom;
            if (p >= 1f) ScaleRamping = false;
            Scaled = Scale != Vector3.One;
        }
        if (ShellRamping) TickShell(seconds);
    }

    /// <summary>The shell's tick.</summary>
    void TickShell(float seconds)
    {
        if (shellRate == 0f) { ShellRamping = false; return; }
        float p = seconds * shellRate + shellProgress;
        shellProgress = p;
        if (p >= 1f)
        {
            shellProgress = 1f;
            if ((ShellFlags & (ShellInOut | ShellPulse)) == 0)
            {
                ShellAlpha = shellTo;
                ShellRamping = false;
                // Faded to nothing: the shell goes.
                if (shellTo == 0f) EndShell();
                return;
            }
            (shellFrom, shellTo) = (shellTo, shellFrom);
            ShellFlags &= ~ShellInOut;
            shellProgress = 0f;
        }
        ShellAlpha = (shellTo - shellFrom) * shellProgress + shellFrom;
    }

    /// <summary>Whether there is anything left for <see cref="Tick"/> to do.</summary>
    public bool Busy => AlphaRamping || ScaleRamping || ShellRamping;
}
