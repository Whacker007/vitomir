// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The last step of the soak: a soak that came out other than 0
// is offered to the character's scripts as `soaked` before it is taken off the blow.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// The soak roll's tail: with both rolls made, **a soak of exactly 0 goes nowhere**,
/// and any other is handed to the `soaked` message with the character, the attacker, the blow as
/// `damaged` left it, the damage type and the soak — and what the scripts answer is the soak the blow
/// loses. The scripts are the place's (<see cref="Ground.Scripts.Soaked"/>); with none, the
/// soak stands.
/// </summary>
public static class SoakedMessage
{
    /// <summary>
    /// The soak after the character's scripts have seen it: <paramref name="soak"/> itself when it is 0
    /// No message) or when there are no scripts to tell.
    /// </summary>
    public static float Offer(WorldThing target, WorldThing? attacker, float damage, DamageType type, float soak)
    {
        if (soak == 0f) return soak;
        return target.Place?.ItemScripts?.Soaked is { } scripts
            ? scripts(target, attacker, damage, (int)type, soak)
            : soak;
    }
}
