// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the crosshair dimmed in melee ("we might dim it substantially while the
// weapon is drawn").
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Presentation;

/// <summary>
/// How strongly the reticle is drawn: in melee the swing's arc is where the blade goes and the crosshair is not (the aim work
/// measured the blade meeting the part the crosshair named 8 times in 72), so while the body could swing — a melee weapon or
/// its fists out (<see cref="SwingArcs.CanSwing"/>) — it is drawn faint, at the setting <c>meleeReticleOpacity</c>; at full
/// strength otherwise (nothing to swing: use, talk) and while a power asks for its target (the pointer's verb is target or
/// refused). A person within talking distance does not make it full while a weapon is out: in a fight the foe always is (the
/// pointer's verb is talk for every person near), and talking is not built yet. Presentation only.
/// </summary>
public static class ReticleLook
{
    /// <summary>The setting's default (per cent; ENGINE_SANDBOX, ours, after looking: still found at the picture's centre over the pale floor and a dark coat, no longer pulling the eye from the arc).</summary>
    public const float DefaultMeleePercent = 20f;

    /// <summary>The opacity (0 to 1) for a body that <paramref name="canSwing"/>, with the pointer's <paramref name="verb"/>, and whether a power waits for a target.</summary>
    public static float Opacity(bool canSwing, PointerVerb verb, bool targetAsked, float meleeOpacity) =>
        canSwing && !targetAsked && verb is not (PointerVerb.Target or PointerVerb.Refused) ? Math.Clamp(meleeOpacity, 0f, 1f) : 1f;

    /// <summary>The opacity for <paramref name="who"/> in <paramref name="world"/> now (the pointer's state read, nothing changed).</summary>
    public static float For(World world, EntityId who, float meleeOpacity)
    {
        if (meleeOpacity >= 1f) return 1f;
        var verb = PointerVerb.None;
        bool asked = false;
        if (world.TryService<PointerState>(out var pointer)) (verb, asked) = (pointer.Free ? PointerVerb.None : pointer.Verb, pointer.Request is not null);
        return Opacity(SwingArcs.CanSwing(world, who), verb, asked, meleeOpacity);
    }
}
