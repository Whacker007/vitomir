// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A weapon swapped into the hands for a while, and the hands given back.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **The claws swap.** Feral Claws, the Black Gloves, and a score of cutscenes that put a pistol or
/// a stake in somebody's hand reach it through `CodexActor.OverrideActorWeapon` and
/// `CancelOverrideActorWeapon`. The engine's side is two small functions and one bit:
/// <c>THING_AF2_WEAPONOVERRIDE</c> (second flag word bit 4) says a swap is in force, and while it
/// is, a second swap does nothing at all, a weapon cannot be used or equipped, and the
/// weapon it replaced cannot be taken off.
/// </summary>
public static class WeaponOverride
{
    /// <summary>The two named shapes of a character's own model that are its bare fists.</summary>
    public const string LeftFist = NamedShapes.FistLeft, RightFist = NamedShapes.FistRight;

    /// <summary>
    /// The swap. Nothing happens when a swap is already in force (<c>flags2 &amp; 4</c>). Otherwise
    /// the weapon in the hand is kept at <see cref="Actor.PrevWeapon"/> and
    /// <paramref name="weapon"/> goes in with `SetWeapon` — which chooses the locomotion
    /// again, so a character that had a sword stands empty-handed-with-claws at once. With
    /// <paramref name="clearOffhand"/> the off hand is kept at <see cref="Actor.PrevOffhand"/>
    /// and emptied (`SetOffhand(null)`); without it the kept off hand is cleared and
    /// the shield stays. With <paramref name="hideFists"/> the fist shapes are hidden on the
    /// character's model, when it has one. The bit is set last.
    ///
    /// The engine does the fist calls twice, once on the model and once on
    /// sub-model slot 0 when it is filled — the armour that stands in for the body;
    /// <see cref="NamedShapes.Fists"/> does both.
    /// </summary>
    /// <returns>False when a swap was already in force and nothing changed.</returns>
    public static bool Override(WorldThing thing, Weapon weapon, bool clearOffhand, bool hideFists)
    {
        if (thing.Actor is not { } actor) return false;
        if (actor.Has2(Actor.Flag2.WeaponOverride)) return false;
        actor.PrevWeapon = actor.Weapon;
        SetWeapon(thing, actor, weapon);
        if (!clearOffhand) actor.PrevOffhand = null;
        else
        {
            actor.PrevOffhand = actor.Offhand;
            actor.Offhand = null;                                         // SetOffhand(null)
        }
        if (hideFists) NamedShapes.Fists(thing, shown: false);
        actor.Flags2 |= Actor.Flag2.WeaponOverride;
        return true;
    }

    /// <summary>
    /// The swap's end, the reverse, and only when a swap is in force. The bit comes off first; the weapon
    /// kept aside goes back into the hand when there is one (the engine tests it is a
    /// weapon thing, type 5) — **when there is none, the swapped-in weapon stays in the hand**,
    /// which cannot happen to a character with an `ACTOR_WEAPON`, since its bare hands are a weapon
    /// too; the off hand kept aside goes back when there is one (an armour thing, type 6);
    /// both are forgotten. Then the fists are shown again — **whether or not the swap hid them** —
    /// unless the talking hands are up (<c>THING_AF2_USINGHANDS</c>, which hides them for a
    /// conversation of its own).
    /// </summary>
    /// <returns>False when there was no swap to end.</returns>
    public static bool Cancel(WorldThing thing)
    {
        if (thing.Actor is not { } actor) return false;
        if (!actor.Has2(Actor.Flag2.WeaponOverride)) return false;
        actor.Flags2 &= ~Actor.Flag2.WeaponOverride;
        if (actor.PrevWeapon is { } was) SetWeapon(thing, actor, was);
        actor.PrevWeapon = null;
        if (actor.PrevOffhand is { } shield) actor.Offhand = shield;       // SetOffhand
        actor.PrevOffhand = null;
        if (!actor.Has2(Actor.Flag2.UsingHands)) NamedShapes.Fists(thing, shown: true);
        return true;
    }

    /// <summary>Whether a swap is in force: <c>THING_AF2_WEAPONOVERRIDE</c>.</summary>
    public static bool Active(Actor actor) => actor.Has2(Actor.Flag2.WeaponOverride);

    /// <summary>
    /// `SetWeapon` as far as Vitomir models it: the weapon in the hand, and the
    /// locomotion chosen again. The held model follows from
    /// <see cref="Actor.Weapon"/> (<see cref="HeldModel.Weapon"/>): a claw carries
    /// `SPECIFIC MODELS`, so it is drawn as `christof_claws` and not by skeleton.
    /// </summary>
    static void SetWeapon(WorldThing thing, Actor actor, Weapon? weapon)
    {
        if (ReferenceEquals(actor.Weapon, weapon)) return;               // The same one is left alone
        actor.Weapon = weapon ?? actor.DefaultWeapon;
        thing.Locomotion?.Reanimate();
    }
}
