// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **A blow that meets nobody** — the two ways the melee resolve ends
/// in the swing's own noise before it rolls anything, found by a comparison with 1.1
///
///
/// <list type="bullet">
/// <item>**The one it was swung at has gone** (the action target's GUID no
/// longer finds a thing; or it is in another place) — the weapon says `swing` (foley
/// event 5, on the attacker), and that is all.</item>
/// <item>**It has stepped out of reach**: a swing (action 5) asks the reach
/// test again at the moment the blow lands — the gap between the two bodies against the attack's
/// <c>WEAPON_MAXDISTANCE</c> (the reach test with the attack's distance) — and a target no longer
/// within it is not rolled for, not hurt, and not told it was attacked: the swing whistles through
/// the air. The reach at the start of the swing says nothing about where the target is
/// when the keyframe comes round, so a character that backs off in time is not hit — and the Space
/// key's strike on a target across the room (<see cref="Actions.Attack"/> with <c>strikeNow</c>)
/// swings at the air, where until then it rolled and could hurt from any distance.</item>
/// </list>
///
/// No roll is made on either path, so the shared random stream is not advanced — as the engine's
/// is not.
///
/// <para>The resolve does nothing at all for an action other than a swing or a throw, and a
/// throw (action 18) lands **without** the reach test or the to-hit roll:
/// <see cref="ThrowBlow"/>.</para>
/// </summary>
public static class StrikeReach
{
    /// <summary>
    /// Whether the blow <paramref name="attacker"/>'s keyframe just called for whiffs: its target
    /// gone, or a swing whose target is out of the attack's reach. When it does, the weapon's swing
    /// noise has been made and the caller resolves nothing.
    /// </summary>
    public static bool Whiffs(WorldThing attacker, Actor actor, Weapon weapon)
    {
        if (actor.Target is { } target && !target.Removed && ReferenceEquals(target.Place, attacker.Place)
            && (actor.Action != ActionMode.Swing || InReach(attacker, target, weapon.Current)))
            return false;
        Audio.Noise.Weapon(weapon, attacker, Audio.FoleyEvent.Swing);
        return true;
    }

    /// <summary>
    /// The reach test at the blow (<see cref="Bodies.InReach"/>): the bodies
    /// level with each other and the clear space between them across the ground no more than the
    /// attack's <see cref="WeaponAttack.MaxDistance"/> — the same measure the swing started on.
    /// </summary>
    public static bool InReach(WorldThing attacker, WorldThing target, WeaponAttack attack) =>
        Bodies.InReach(attacker, target, attack.MaxDistance);
}
