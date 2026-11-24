// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours (the contract): cancelling a swing ("I do like the idea of a dodge
// being possible during a swing that cancels the swing and the dodge happens in the direction the player is moving ... I guess
// I could see having a 'cancel melee attack' key that could also be double bound to crouch as well"). 1.1 has no cancel.
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Control;

/// <summary>What cancelled a swing.</summary>
public enum SwingCancelCause : byte
{
    /// <summary>The cancel action (the player's key, on crouch's key by default).</summary>
    Cancel = 0,
    /// <summary>A dodge begun during the swing.</summary>
    Dodge = 1,
}

/// <summary>
/// A swing cancelled: who, when, which swing (<see cref="Combatant.Swing"/>; 0 for a press dropped before its swing
/// began), why, and whether it was before its <c>Strike</c> mark (a feint, or a dodge out of the wind-up).
/// </summary>
public readonly record struct SwingCancelled(EntityId Entity, long Tick, int Swing, SwingCancelCause Cause, bool BeforeStrike);

/// <summary>
/// The one way to cancel a body's swing (the contract between the guard, which owns the swing and writes these, and
/// Who calls them from the cancel action and the dodge). <see cref="Can"/> says whether the body's swing could be
/// cancelled now for <paramref name="cause"/> (false when it has none); <see cref="Do"/> cancels it (false, and nothing
/// changed, when it cannot).
/// <list type="bullet">
/// <item><b>Cancel</b> (the key) only <b>before the strike</b>: a swing in its wind-up — its clip short of its <c>Strike</c>
/// mark, nothing met yet — ends with no blow (a feint) and is drawn back to guard over
/// <see cref="GuardSettings.CancelReturnSeconds"/>, in which no new swing begins (the body is busy that long, and no longer: a
/// feint is quicker than a whole swing). A press being held (a strong blow charging) or a blow buffered for after the swing is
/// dropped too: no blow comes of it. After the strike, with nothing pressed, it cannot: the swing plays out (else every swing
/// could be cut short).</item>
/// <item><b>Dodge</b> at any point of a swing (its wind-up, its active window, a stop's bring-back, its fade): the swing ends
/// at once — no contact after this tick (contact closes a swing whose action is ending), the action let go over
/// <see cref="GuardSettings.DodgeLetGoSeconds"/> for the dodge's own motion — and the busy time is left as it was, so the next
/// swing waits for the later of the dodge's end (the dodge keeps the body busy for itself) and the moment the cancelled swing
/// would have ended: dodging never quickens attacks. A buffered blow is dropped.</item>
/// </list>
/// Each cancel publishes <see cref="SwingCancelled"/>. The duelling brain reacts to a begun blow (its swing number moving
/// on), so a feint draws its block or dodge as a real wind-up does. Nothing allocated.
/// </summary>
public static class SwingCancel
{
    static readonly GuardSettings Default = new();

    /// <summary>Whether <paramref name="entity"/>'s swing could be cancelled now for <paramref name="cause"/>.</summary>
    public static bool Can(World world, EntityId entity, SwingCancelCause cause) => What(world, entity, cause) != Kind.None;

    /// <summary>Cancels <paramref name="entity"/>'s swing for <paramref name="cause"/>; false, nothing changed, when it cannot.</summary>
    public static bool Do(World world, EntityId entity, SwingCancelCause cause)
    {
        var kind = What(world, entity, cause);
        if (kind == Kind.None) return false;
        var g = world.TryService<GuardSettings>(out var gs) ? gs : Default;
        ref var c = ref world.Store<Combatant>().Get(entity);
        ref var anim = ref world.Store<CharacterAnimation>().Get(entity);
        int swing = 0;
        bool before = true;
        if (kind == Kind.Swing)
        {
            swing = c.Swing;
            before = BeforeStrike(world, entity, in c, in anim);
            if (cause == SwingCancelCause.Cancel)
            {
                ActionChannel.FadeFromHere(ref anim, g.CancelReturnSeconds);
                c.Busy = g.CancelReturnSeconds;   // the return, no more: a feint is quicker than the whole swing
            }
            else ActionChannel.FadeFromHere(ref anim, g.DodgeLetGoSeconds);   // the busy time stands: the cancelled swing's end
            if (world.TryStore<BladeHold>(out var holds))
            {
                ref var h = ref holds.Find(entity);
                if (!Unsafe.IsNullRef(ref h)) h.State = BladeHoldState.None;
            }
            if (world.TryService<CharacterPoses>(out var poses)) poses.Invalidate();
        }
        c.Pending = AttackStrength.None;
        c.Buffered = false;
        c.AttackHeld = -1f;
        // Published where the event is registered (combat's registration does; a world of movement alone may not).
        if (world.TryService<EventQueue<SwingCancelled>>(out var cancelled)) cancelled.Publish(new SwingCancelled(entity, world.Tick, swing, cause, before));
        return true;
    }

    enum Kind { None, Swing, Press }

    static Kind What(World world, EntityId e, SwingCancelCause cause)
    {
        if (!world.TryStore<Combatant>(out var fighters) || !world.TryStore<CharacterAnimation>(out var anims)) return Kind.None;
        ref var c = ref fighters.Find(e);
        ref var anim = ref anims.Find(e);
        if (Unsafe.IsNullRef(ref c) || Unsafe.IsNullRef(ref anim) || ActionRoles.IsDeath(anim.Action)) return Kind.None;
        if (world.TryStore<Dead>(out var dead) && dead.Has(e)) return Kind.None;
        bool swinging = ActionRoles.IsAttack(anim.Action);
        if (cause == SwingCancelCause.Dodge) return swinging ? Kind.Swing : Kind.None;
        // Cancel: a swing in its wind-up; else a press held or buffered.
        if (swinging && !anim.ActionEnding && BeforeStrike(world, e, in c, in anim)) return Kind.Swing;
        return c.AttackHeld >= 0f || c.Pending != AttackStrength.None ? Kind.Press : Kind.None;
    }

    /// <summary>Whether a swing is short of its <c>Strike</c> mark and has met nothing (no contact, no hold, its mark not told).</summary>
    static bool BeforeStrike(World world, EntityId e, in Combatant c, in CharacterAnimation anim)
    {
        if (c.StrikeAt < 0f || anim.ActionTime >= c.StrikeAt || FollowThrough.StruckBefore(world, e, c.Swing)) return false;
        if (world.TryStore<BladeHold>(out var holds))
        {
            ref var h = ref holds.Find(e);
            // A hold this swing ever had means its blade met something, whether or not
            // the hold is still on (a blade that bit a body before its Strike mark and is being brought back was cancellable).
            if (!Unsafe.IsNullRef(ref h) && h.Swing == c.Swing && c.Swing != 0) return false;
        }
        if (world.TryStore<SwingContacts>(out var swings))
        {
            ref var sc = ref swings.Find(e);
            if (!Unsafe.IsNullRef(ref sc) && sc.Swing == c.Swing && c.Swing != 0 && (sc.Met > 0 || sc.Chain > 0 || sc.Stopped)) return false;   // open or closed
        }
        return true;
    }
}
