// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The natives that reach a thing's particle emitter: switching it on and off, and
// the member getters and setters the SDK documents and no shipped script calls.
using Vitomir.Engine.Particles;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    void RegisterParticles()
    {
        // `EnableEmitter` / `DisableEmitter`: the emitter's flag 0x10000 off or
        // on, and false for a thing with no emitter. Only the draw reads the flag, so a
        // disabled emitter keeps spawning unseen — the sewers' electric plates and the Haus de Hexe's
        // arcanum door switch theirs this way (13 calls in the single-player scripts).
        Engine.On("CodexThing.EnableEmitter", (self, _) => CodexHost.Box(Flag(self, on: false)));
        Engine.On("CodexThing.DisableEmitter", (self, _) => CodexHost.Box(Flag(self, on: true)));

        // `GetEmitterType`: the kind's number (EMITTER_TYPE_*), 0 with no emitter.
        Engine.On("CodexThing.GetEmitterType", (self, _) => CodexHost.Box(EmitterOf(self) is { } e ? (int)e.Kind : 0));
        // `GetEmitterName`: the block's name, or the SDK's own error string.
        Engine.On("CodexThing.GetEmitterName", (self, _) => EmitterOf(self)?.Name ?? "***ERR:EmitterName***");

        // The members: EMITTER_MEMBER_RATE (1) and
        // _LIFESPAN (2) as whole numbers; _SIZE (500), _MINDIST (501), _MAXDIST (502), and per kind
        // the gas's _DRAG/_GRAVITY/_TURBULENCE/_GROWTHRATE (520–523) and the fire's _TURBULENCE (540)
        // as floats; the liquid's own — _LIQ_NUMSTREAKS/_MINSTREAK/_MAXSTREAK (60–62) and
        // _LIQ_GRAVITY/_COHESION/_SIZEVARIANCE/_VELOCITYVARIANCE/_STREAKCOHESION (562–566).
        // Anything else is −1, −1.0 or false.
        Engine.On("CodexThing.GetEmitterIntMember", (self, a) =>
            CodexHost.Box(EmitterOf(self) is { } e ? EmitterMembers.GetInt(e.Settings, CodexHost.Int(a, 0)) : -1));
        Engine.On("CodexThing.SetEmitterIntMember", (self, a) =>
            CodexHost.Box(EmitterOf(self) is { } e && EmitterMembers.SetInt(e.Settings, CodexHost.Int(a, 0), CodexHost.Int(a, 1))));
        Engine.On("CodexThing.GetEmitterFloatMember", (self, a) =>
            CodexHost.Box(EmitterOf(self) is { } e ? EmitterMembers.GetFloat(e.Settings, CodexHost.Int(a, 0)) : -1f));
        Engine.On("CodexThing.SetEmitterFloatMember", (self, a) =>
            CodexHost.Box(EmitterOf(self) is { } e && EmitterMembers.SetFloat(e.Settings, CodexHost.Int(a, 0), CodexHost.Float(a, 1))));
        // The vector pair: EMITTER_MEMBER_STARTCOLOR/_ENDCOLOR (1000, 1001).
        // **The getter reads nothing**: it finds the emitter and throws it away, and answers a new (−1, −1, −1) whatever the member — for every emitter.
        // The setter packs the three floats into the colour (EmitterMembers.PackColor).
        Engine.On("CodexThing.GetEmitterVectorMember", (_, _) => new[] { -1f, -1f, -1f });
        Engine.On("CodexThing.SetEmitterVectorMember", (self, a) =>
            CodexHost.Box(EmitterOf(self) is { } e && a.Length > 1 && a[1] is float[] { Length: >= 3 } v
                          && EmitterMembers.SetColor(e.Settings, CodexHost.Int(a, 0), v[0], v[1], v[2])));

        // `GetLastEmitterRenderCount` answers a counter which nothing in the
        // original game ever writes: it is 0, always.
        Engine.On("CodexWorld.GetLastEmitterRenderCount", (_, _) => CodexHost.Box(0));
    }

    /// <summary>The emitter the script's thing has, if any.</summary>
    Emitter? EmitterOf(object? self) => Of(self) is { } t && location is not null ? location.Particles.Of(t) : null;

    /// <summary>Sets or clears the hidden flag; false for a thing with no emitter, as the engine answers.</summary>
    bool Flag(object? self, bool on)
    {
        if (EmitterOf(self) is not { } e) return false;
        e.Flags = on ? e.Flags | Emitter.FlagHidden : e.Flags & ~Emitter.FlagHidden;
        return true;
    }
}

/// <summary>The member numbers the emitter natives take (`Codex.java`'s EMITTER_MEMBER_*), as the natives take them.</summary>
public static class EmitterMembers
{
    public const int Rate = 1, Lifespan = 2, Size = 500, MinDist = 501, MaxDist = 502,
                     GasDrag = 520, GasGravity = 521, GasTurbulence = 522, GasGrowthRate = 523, FireTurbulence = 540,
                     LiqNumStreaks = 60, LiqMinStreak = 61, LiqMaxStreak = 62,
                     LiqStartAlpha = 560, LiqEndAlpha = 561, LiqGravity = 562, LiqCohesion = 563,
                     LiqSizeVariance = 564, LiqVelocityVariance = 565, LiqStreakCohesion = 566,
                     StartColor = 1000, EndColor = 1001;

    /// <summary>A liquid's three clump numbers first, then the birth rate and the lifespan, as they are stored.</summary>
    public static int GetInt(EmitterSettings s, int member)
    {
        if (s.Kind == EmitterKind.Liquid)
            switch (member)
            {
                case LiqNumStreaks: return (int)s.NumGroups;
                case LiqMinStreak: return (int)s.MinGroup;
                case LiqMaxStreak: return (int)s.MaxGroup;
            }
        return member switch
        {
            Rate => (int)s.BirthRate,
            Lifespan => (int)s.Lifespan,
            _ => -1,
        };
    }

    /// <summary>
    /// The lifespan is stored as its absolute value (the setter's <c>abs</c>); the rate and
    /// a liquid's clump numbers as given. (A clump count raised after a liquid's first update would
    /// read past its clumps in 1.1; ours keeps to the clumps it made.)
    /// </summary>
    public static bool SetInt(EmitterSettings s, int member, int value)
    {
        if (s.Kind == EmitterKind.Liquid)
            switch (member)
            {
                case LiqNumStreaks: s.NumGroups = unchecked((uint)value); return true;
                case LiqMinStreak: s.MinGroup = unchecked((uint)value); return true;
                case LiqMaxStreak: s.MaxGroup = unchecked((uint)value); return true;
            }
        switch (member)
        {
            case Rate: s.BirthRate = unchecked((uint)value); return true;
            case Lifespan: s.Lifespan = (uint)Math.Abs((long)value); return true;
            default: return false;
        }
    }

    /// <summary>The kind's own members first, then the three every kind has.</summary>
    public static float GetFloat(EmitterSettings s, int member)
    {
        if (s.Kind == EmitterKind.Gas)
            switch (member)
            {
                case GasDrag: return s.Drag;
                case GasGravity: return s.Gravity;
                case GasTurbulence: return s.Turbulence;
                case GasGrowthRate: return s.GrowthRate;
            }
        if (s.Kind == EmitterKind.Fire && member == FireTurbulence) return s.Turbulence;
        if (s.Kind == EmitterKind.Liquid)
            switch (member)
            {
                case LiqGravity: return s.Gravity;
                case LiqCohesion: return s.Cohesion;
                case LiqSizeVariance: return s.SizeVariance;
                case LiqVelocityVariance: return s.VelocityVariance;
                case LiqStreakCohesion: return s.GroupCohesion;
            }
        return member switch
        {
            Size => s.StartSize,
            MinDist => s.MinDist,
            MaxDist => s.MaxDist,
            _ => -1f,
        };
    }

    /// <summary>As the getter, writing.</summary>
    public static bool SetFloat(EmitterSettings s, int member, float value)
    {
        if (s.Kind == EmitterKind.Gas)
            switch (member)
            {
                case GasDrag: s.Drag = value; return true;
                case GasGravity: s.Gravity = value; return true;
                case GasTurbulence: s.Turbulence = value; return true;
                case GasGrowthRate: s.GrowthRate = value; return true;
            }
        if (s.Kind == EmitterKind.Fire && member == FireTurbulence) { s.Turbulence = value; return true; }
        if (s.Kind == EmitterKind.Liquid)
            switch (member)
            {
                // The two alphas are accepted and stored nowhere (members 0x230/0x231).
                case LiqStartAlpha: case LiqEndAlpha: return true;
                case LiqGravity: s.Gravity = value; return true;
                case LiqCohesion: s.Cohesion = value; return true;
                case LiqSizeVariance: s.SizeVariance = value; return true;
                case LiqVelocityVariance: s.VelocityVariance = value; return true;
                case LiqStreakCohesion: s.GroupCohesion = value; return true;
            }
        switch (member)
        {
            case Size: s.StartSize = value; return true;
            case MinDist: s.MinDist = value; return true;
            case MaxDist: s.MaxDist = value; return true;
            default: return false;
        }
    }

    /// <summary>
    /// <c>EMITTER_MEMBER_STARTCOLOR</c> (1000) writes <c>startcolor</c>,
    /// <c>_ENDCOLOR</c> (1001) <c>endcolor</c>, from the three floats as red, green and blue
    /// (<see cref="PackColor"/>); anything else is false. Every kind has the two fields, whether or
    /// not its draw reads them.
    /// </summary>
    public static bool SetColor(EmitterSettings s, int member, float r, float g, float b)
    {
        switch (member)
        {
            case StartColor: s.StartColor = PackColor(r, g, b, s.StartColor); return true;
            case EndColor: s.EndColor = PackColor(r, g, b, s.EndColor); return true;
            default: return false;
        }
    }

    /// <summary>
    /// The original's packing: each float rounded by adding 1.5 × 2²³ and
    /// taking the float's bits — red's bits shifted up 16, green's mantissa plus 0xc00000 shifted
    /// up 8, blue's mantissa less 2²² — OR'd together, so a component outside 0..255 spills
    /// into its neighbours as it does in 1.1. **The alpha is the low byte of a fourth rounded float
    /// read from a stack slot nothing wrote** (the array has three); ours keeps the colour's alpha.
    /// </summary>
    public static uint PackColor(float r, float g, float b, uint was)
    {
        const float Magic = 12582912f;   // 1.5 × 2²³
        uint rb = (uint)BitConverter.SingleToInt32Bits(r + Magic);
        uint gb = (uint)BitConverter.SingleToInt32Bits(g + Magic);
        uint bb = (uint)BitConverter.SingleToInt32Bits(b + Magic);
        uint rgb = unchecked((rb << 16) | (((gb & 0x7fffff) + 0xc00000) << 8) | ((bb & 0x7fffff) - 0x400000));
        return (was & 0xff000000) | rgb;
    }
}
