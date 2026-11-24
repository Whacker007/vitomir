// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The first thing a character's damage does once it is not refused (at
// The blow is offered to the character's scripts as `damaged`, and what they
// answer is the blow from then on.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// `damaged` for a **character**: with the refusals passed, the blow — its amount and its
/// damage type, the character and the attacker — goes to the `damaged` message before anything else touches it,
/// and **the answer is the blow**: at or below 0 it does nothing at all (returning
/// 0 — no soak, no block, no hit reaction), above it the soak, the party's share
/// and the rest go on with it (it is handed to the soak roll). The same message a prop hears in an
/// explosion (<see cref="Explosions.ExplosionDamage"/>), through the same seam
/// (<see cref="Ground.Scripts.Damaged"/>); with no scripts the blow stands. No shipped class declares
/// `damaged`, so a shipped game is unchanged; a mod's class is heard. Sent (it had been an
/// unfed parameter of <see cref="Damage.Apply"/> until then).
/// </summary>
public static class DamagedMessage
{
    /// <summary>The blow after the character's scripts have seen it, <paramref name="damage"/> itself with none.</summary>
    public static float Offer(WorldThing target, WorldThing? attacker, float damage, DamageType type) =>
        target.Place?.ItemScripts?.Damaged is { } scripts
            ? scripts(target, attacker, damage, (int)type)
            : damage;
}
