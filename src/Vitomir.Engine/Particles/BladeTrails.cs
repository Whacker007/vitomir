// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A weapon's blade trail: the particle thing the weapon hangs on the hand that holds a sword or
// an axe and takes off it again, both from `SetWeapon`. Read.
// The trail itself is an ordinary trail emitter (`TrailEmitter`, kind 5), laying its
// segments only while the hand is in the middle of a STRIKETRAIL swing.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Particles;

/// <summary>
/// The blade trails of a place. **What 1.1 does**, in `SetWeapon` whenever a character's
/// weapon changes — a new character's first weapon at its finalize, an item equipped, the
/// claws and the stake swapped in and out, the AI's replacement, a load's fix-up:
/// <list type="number">
/// <item>the weapon going out of the hand loses its trail: with <c>BLADE TRAIL</c> set and
/// a trail's GUID kept, the thing of that GUID, if it rides something, is let go
/// — and, hung with <c>ATTACH_FLAG_AUTOREMOVE</c>, destroyed;</item>
/// <item>the weapon coming in gets one when its first attack's word has
/// <c>BLADE TRAIL</c> (0x200) and **no save or saved place is being loaded** (the
/// game's and the network's load flags, and the one set while a saved place is read back): the holder's motion-set tag 0x1a — the weapon hand, the
/// bone a shot leaves from — found (none, no trail); the `WEAPON_TRAIL` template made;
/// with no emitter it is destroyed again; a trail emitter (kind 5) is given the blade's
/// two ends as its <c>startpos</c> and <c>endpos</c> (over the
/// prototype's); its GUID kept on the weapon; and it is hung on the hand (flags 0xc001:
/// destroyed with the hand's owner, no offset, no turn — square on the bone).</item>
/// </list>
/// The trail then lays a segment a frame across the blade while its holder swings an attack marked
/// <c>STRIKETRAIL</c> (<see cref="TrailEmitter.Swinging"/>), and the ribbon fades behind the stroke.
///
/// **A consequence worth checking against 1.1:** the trail things are `SAVE NEVER`, and a load puts
/// every weapon back through `SetWeapon` with the load flag up — so after loading a save, and in a
/// place entered again from the save's memory of it, **nobody's blade leaves a trail until their
/// weapon changes** (drawn and put away again, swapped for another). A place visited for the first
/// time has its trails.
///
/// **Ours in method:** the engine does this inside `SetWeapon`; Vitomir's weapon changes happen in a
/// dozen places, so this watches each character's weapon once a step, from the particle system's
/// own step, and does the same the moment it sees one change — at most a frame later, and a swing
/// is never that quick. A load marks its characters (<see cref="Loaded"/>) so their first sight
/// makes no trail.
/// </summary>
public sealed class BladeTrails
{
    readonly Location location;
    readonly Dictionary<WorldThing, Weapon?> held = new(ReferenceEqualityComparer.Instance);

    /// <summary>The holder's motion-set tag the trail hangs on (0x1a): the weapon hand.</summary>
    public const int HandTag = 0x1a;
    /// <summary>How it is hung (0xc001): auto-remove, no offset, no Euler turn.</summary>
    public const uint HangFlags = Attachments.AutoRemove | Attachments.NoOffset | Attachments.NoEuler;

    public BladeTrails(Location location) => this.location = location;

    /// <summary>
    /// Once a step: every character in the place whose weapon is not the one last seen has the old
    /// one's trail taken off and the new one's hung (a character seen for the first time is given
    /// its weapon's, as the finalize's `SetWeapon` does).
    /// </summary>
    public void Watch()
    {
        List<WorldThing>? gone = null;
        foreach (var (thing, _) in held)
            if (thing.Removed) (gone ??= []).Add(thing);
        if (gone is not null) foreach (var t in gone) held.Remove(t);
        foreach (var thing in location.Things.ToArray())
        {
            if (thing.Type is not (ThingType.Actor or ThingType.Player) || thing.Actor is not { } actor || !thing.InWorld) continue;
            var weapon = actor.Weapon;
            if (held.TryGetValue(thing, out var was))
            {
                if (ReferenceEquals(was, weapon)) continue;
                if (was is not null) Unhang(was);
            }
            held[thing] = weapon;
            if (weapon is not null) Hang(thing, weapon);
        }
    }

    /// <summary>
    /// A load has put this character back (the load sets its weapon again with the load flag up):
    /// whatever trail it has is taken off and its weapon counted as seen, so none is hung.
    /// </summary>
    public void Loaded(WorldThing thing)
    {
        if (held.TryGetValue(thing, out var was) && was is not null) Unhang(was);
        held[thing] = thing.Actor?.Weapon;
    }

    /// <summary>The weapon's trail made and hung on <paramref name="holder"/>'s hand; the thing, or null.</summary>
    public WorldThing? Hang(WorldThing holder, Weapon weapon)
    {
        if (!weapon.Attacks[0].Is(WeaponFlag.BladeTrail) || weapon.Trail.Length == 0) return null;
        int bone = holder.Animation?.TagBone(HandTag) ?? -1;
        if (bone < 0) return null;
        // Its number: a trail still riding its holder from the last place keeps the one it had —
        // in 1.1 it is the same thing, and nothing sets the weapon again at a door;
        // the first trail of a new game's kit has the one its making held (ThingGuids.HoldTrail).
        int number = weapon.TrailGuid != 0 ? weapon.TrailGuid : location.Guids.TakeTrail();
        if (location.Place(weapon.Trail, holder.Position, holder.Orientation, holder.Sector, number) is not { } trail) return null;
        // The template reader gives the thing its emitter as it is made.
        var emitter = location.Templates is { } templates
                      && templates.Resolve(trail.Scene).TryGetValue(ParticleSystem.ParticleNameTag, out var tag)
                      && tag.AsString() is { Length: > 0 } name
            ? location.Particles.Attach(trail, name) : null;
        if (emitter is null) { location.Remove(trail, drop: false); return null; }   // Vtbl[0]
        if (emitter is TrailEmitter)
        {
            emitter.Settings.StartPos = weapon.BladeStart;
            emitter.Settings.EndPos = weapon.BladeEnd;
        }
        weapon.TrailGuid = trail.Guid;
        Attachments.Attach(holder, trail, bone, Vector3.Zero, Matrix4x4.Identity, HangFlags);
        return trail;
    }

    /// <summary>The weapon's trail, if it has one riding something, let go — and so destroyed.</summary>
    public void Unhang(Weapon weapon)
    {
        if (!weapon.Attacks[0].Is(WeaponFlag.BladeTrail) || weapon.TrailGuid == 0) return;
        if (location.Thing(weapon.TrailGuid) is { AttachedTo: { } parent } trail)
            Attachments.Detach(parent, trail);
        weapon.TrailGuid = 0;                                           // gone; the next setting makes a new one
    }
}
