// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What 1.1 does to every character once a load has put it back: the post-load fix-up, called for each
// actor by the load and by the network join.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Save;

/// <summary>
/// **1.1's post-load fix-ups**, in its order:
/// <list type="number">
/// <item>the weapon swap's two kept pointers resolved from their GUIDs and the armour, off hand and weapon set again through their setters — which
/// <see cref="SaveApply"/> does as it puts the pack back (<see cref="Equipment.Restore"/>, and
/// <see cref="Override"/> for a swap);</item>
/// <item>a shape change in force made again, forced
/// (<see cref="ShapeShift.Remake"/>);</item>
/// <item>a player-type character's <c>THING_AF_PARTY</c> cleared — the roster's own load puts it
/// back as it adds each member; in Vitomir the roster <i>is</i> that bit and the save
/// keeps it, so this step is not taken (ours);</item>
/// <item><c>ef_disc_earthmeld</c> and <c>ef_disc_mistform</c> taken off, with their scripts told
/// (with notify) — a load never leaves anybody sunk in the ground or turned to mist;</item>
/// <item>every live effect's kind pushed into the character again: each kind and
/// subtype once, through the recompute, and the instant kinds marked folded — which is
/// what puts a frenzy back on a character whose Frenzy stat was over the line (the stat's setter
/// runs the frenzy check).</item>
/// </list>
/// </summary>
public static class PostLoad
{
    /// <summary>The two effects no load keeps.</summary>
    public const string Earthmeld = "ef_disc_earthmeld", MistForm = "ef_disc_mistform";

    /// <summary>The fix-up on one character, after its pack, its effects and its links are back.</summary>
    public static void FixUp(WorldThing thing)
    {
        if (thing.Actor is not { } actor) return;
        // A shape in force made again with its own name and flags, forced,
        // over the character as the load put it back (Rpg.ShapeShift).
        if (actor.ShapeChange is not null) ShapeShift.Remake(thing);
        // The fix-up sets the weapon again with the load flag up, so no blade trail is hung.
        thing.Place?.Particles.Blades?.Loaded(thing);
        if (thing.Effects.Find(Earthmeld) is { } meld) EffectRuntime.Remove(thing, meld, notify: true);
        if (thing.Effects.Find(MistForm) is { } mist) EffectRuntime.Remove(thing, mist, notify: true);
        Refold(thing);
    }

    /// <summary>
    /// Every live effect's kind and subtype recomputed once (the fold marks what it has
    /// counted, so a second instance of a kind is not recomputed twice), and the instant kinds — base
    /// stat, base soak, damage and heal (1, 4, 7, 9) — marked folded, so they are never paid again.
    /// </summary>
    public static void Refold(WorldThing thing)
    {
        var done = new HashSet<(EffectKind, int)>();
        foreach (var e in thing.Effects.All.ToArray())
        {
            if (!done.Add((e.Kind, e.Subtype))) continue;
            EffectRuntime.Recompute(thing, e.Kind, e.Subtype);
            if (e.Kind is EffectKind.BaseStatInstant or EffectKind.BaseSoakInstant
                       or EffectKind.DamageInstant or EffectKind.HealInstant)
                e.Folded = true;
        }
    }

    /// <summary>
    /// A weapon swap put back: the swapped-in weapon made again from its template and put in the
    /// hand, and what it put aside kept aside — the hand's item's weapon (or the character's own when
    /// the save names none), and the off hand's item, taken out of the off hand. The bit itself came
    /// back with the flag word. **Ours in one respect**: 1.1 resolves the hand's weapon from the
    /// GUID of the thing the swap made, and whether a save keeps that thing — it is in
    /// no world and no pack — has not been read; Vitomir keeps it, so a load in the middle of Feral
    /// Claws still has the claws.
    /// </summary>
    public static void Override(WorldThing thing, Actor actor, OverrideSave save, Func<string, Item?> item)
    {
        if (thing.Place?.Templates is { } templates && save.Weapon.Length > 0 && templates.Has(save.Weapon)
            && Weapon.From(save.Weapon, templates.Resolve(save.Weapon)) is { } swapped)
            actor.Weapon = swapped;
        actor.PrevWeapon = save.PrevWeapon is { } w && item(w)?.Weapon is { } kept ? kept : actor.DefaultWeapon;
        actor.PrevOffhand = save.PrevOffhand is { } o ? item(o) : null;
        if (actor.PrevOffhand is not null && ReferenceEquals(actor.Offhand, actor.PrevOffhand)) actor.Offhand = null;
        actor.Flags2 |= Actor.Flag2.WeaponOverride;
        thing.Locomotion?.Reanimate();
    }
}
