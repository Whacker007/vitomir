// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Animation;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>The fighting pace</b> (<i>"humans moving at human speeds"</i>; the pace survey, the
/// KCD and Bannerlord habit — <b>SANDBOX</b>, the number a rules module's: the enhanced module's <c>Data/combat/pace.json</c>): a
/// body whose blow is <b>swinging or held ready</b> — its action channel playing an attack that has not ended
/// (<see cref="InBlow"/>: from the press, through the hold at the ready point and the swing, to the end of the clip) — moves on
/// the ground at most <see cref="MaxSpeed"/> (1.8 m/s, a brisk walk short of a run), whatever gait it asks for: no run or sprint
/// through a swing. It is one rule in one place, <see cref="BodySystem"/>, so it holds for the player and every brain-driven
/// body alike. The gait asked for becomes a walk at the lesser of its own speed and the cap (<see cref="Capped"/>), so a body
/// in a blow starts no step-over or sprint vault (those are the run's and the sprint's); a walk below the cap stays the walk
/// (the block's walk is <see cref="Control.PlayerControlSystem"/>'s, unchanged); the crouch keeps its pace; a jump's charge keeps the
/// walk it had. A pace scale (Celerity's movement, <see cref="PaceScale"/>) multiplies the cap as it does the gaits (Celerity
/// is meant to break the human pace; the masquerade's mode holds it to the human's). A dodge is the dodge's own step, a move
/// (a vault, a climb, a ladder) and the air are the motor's: none is capped. A world without the service moves as before,
/// digest for digest; <see cref="On"/> false likewise (the switch <c>fighting-pace-off</c>).
/// </summary>
public sealed record FightingPace
{
    /// <summary>Whether the cap applies.</summary>
    public bool On { get; init; } = true;

    /// <summary>The most a body moves on the ground while its blow is swinging or held ready (m/s; 1.8, SANDBOX).</summary>
    public float MaxSpeed { get; init; } = 1.8f;

    /// <summary>Whether a body's action channel holds a blow swinging or held ready: an attack playing that has not ended.</summary>
    public static bool InBlow(in CharacterAnimation a) => ActionRoles.IsAttack(a.Action) && !a.ActionEnding;

    /// <summary>
    /// Whether <paramref name="e"/>'s bow is drawing or held drawn — held to the same pace as a blow held ready (the brief's
    /// rule: an archer at full draw walks; no reason found for a drawn bow to run). Null store (a world without a bow): false.
    /// </summary>
    public static bool BowDrawn(Core.Store<Combat.Bow>? bows, Core.EntityId e)
    {
        if (bows is null) return false;
        ref var bow = ref bows.Find(e);
        return !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref bow) && bow.InHand && bow.Drawn;
    }

    /// <summary>
    /// The numbers a body in a blow moves at when it asks for <paramref name="gait"/>, under a pace factor of
    /// <paramref name="factor"/> (1 for none): <paramref name="moving"/> with its walk the lesser of the asked gait's speed and
    /// <see cref="MaxSpeed"/> × the factor (the gait asked is then a walk: <see cref="Gait.Walk"/>).
    /// </summary>
    public Locomotion Capped(in Locomotion moving, Gait gait, float factor)
    {
        var l = moving;
        l.WalkSpeed = MathF.Min(moving.SpeedOf(gait), MaxSpeed * MathF.Max(1f, factor));
        return l;
    }

    // ---- rushing creatures let through ----
    /// <summary>
    /// Registers <see cref="RushUnpaced"/> (digested), the mark of a body
    /// that keeps its own pace through its blows. Only a world that lets rushing creatures through (the enhanced module's switch
    /// <c>rush-unpaced</c>) registers it, so a world without it is the world before, digest for digest.
    /// </summary>
    public static Modules.WorldBuilder RegisterUnpaced(Modules.WorldBuilder b)
    {
        if (!b.Peek.TryStore<RushUnpaced>(out _)) b.Component<RushUnpaced>();
        return b;
    }

    /// <summary>Whether <paramref name="e"/> keeps its own pace through its blows (it bears <see cref="RushUnpaced"/>; never when the world has no such mark).</summary>
    public static bool Unpaced(Core.Store<RushUnpaced>? marks, Core.EntityId e) => marks is not null && marks.Has(e);
    // ---- end
}

/// <summary>
/// A body that keeps its own pace through its blows — <b>a rushing creature</b>, not held to <see cref="FightingPace"/>'s
/// walk while it swings (the fighting pace held the szlachta's clawing rush too, and half its fights stalled: the tempo work). Which
/// bodies are rushing creatures is a rules module's (the enhanced module's <c>pace.json</c>, <c>rush_unpaced</c>: the szlachta; later
/// beasts), put on with its switch. <see cref="Kind"/> says who put it (1: the module's list), for the trace.
/// </summary>
public struct RushUnpaced
{
    public byte Kind;
}
