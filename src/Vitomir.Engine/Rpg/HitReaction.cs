// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The hit reaction as one gated step, built .
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **The hit reaction**, which a blow that does not kill calls after the frenzy:
/// the noises and the flinch roll (<see cref="Damage.Hurt"/>), the beheading roll
/// (<see cref="Beheading.Roll"/>), the loose chunk and what the wound shows
/// (<see cref="WoundEffects.Show"/>), in that order. **All of it is for a character in the
/// world** (its world and its sector both set) — a character a script's
/// `DamageActor` hurts while it is not placed gets none of it, and no roll is taken off the stream.
/// Earlier, Vitomir tested the world only for the noises and the flinch.
/// <para>
/// **Every player's machine runs it again** for the host's blow (message 0xd, the damage forced;
/// <see cref="Net.ClientSeen"/>), so on a network host none of what it makes is sent: the noises
/// are played with the foley's relay flag 0 (not relayed), and the loose chunk and the wound
/// effects, flagged `NO NET SYNCH`, are each machine's own (<see cref="Net.MadeHere"/>). The beheading roll is
/// the host's alone (not on a network client), which tells the players' machines when a head comes
/// off (<see cref="Beheading.Behead"/>).
/// </para>
/// </summary>
public static class HitReaction
{
    /// <summary>The damage reaction for a blow of <paramref name="amount"/> that left the character alive.</summary>
    public static void React(WorldThing target, Actor actor, WorldThing? attacker, HitKind kind, DamageType type,
                             float amount, Rng rng, int nowMs)
    {
        if (!target.InWorld) return;
        using (Net.HostSeen.Local()) Damage.Hurt(target, actor, type, amount, nowMs, rng);   // Not sent
        Beheading.Roll(target, attacker, kind, amount, rng, nowMs);
        using (Net.MadeHere.Everywhere(target))
        {
            Damage.SprayOnHit(target, actor, kind, amount, rng);
            WoundEffects.Show(target, attacker, type, amount, rng, nowMs);
        }
    }

    /// <summary>
    /// The damage reaction on a **player's machine**, for the host's blow (the damage with its seventh argument,
    /// The noises, the loose chunk and the wound effects, made here from this machine's own rolls
    /// and under its own no-gore setting. Not here: the beheading roll, the host's alone, and
    /// (ours) the flinch roll — the host's flinch reaches this machine with the snapshot's action
    /// (<see cref="Net.ThingState.ActionMode"/>), so it is not rolled a second time.
    /// </summary>
    public static void Shown(WorldThing target, Actor actor, WorldThing? attacker, HitKind kind, DamageType type,
                             float amount, Rng rng, int nowMs)
    {
        if (!target.InWorld) return;
        Damage.Hurt(target, actor, type, amount, nowMs);                 // the noises, no roll
        Damage.SprayOnHit(target, actor, kind, amount, rng);
        WoundEffects.Show(target, attacker, type, amount, rng, nowMs);
    }
}
