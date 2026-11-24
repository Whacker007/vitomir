// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The utility item in the off hand — the torch, the flashlight, the Gangrel eye — and the stance it
// puts its holder in (the utility's set, the attack and locomotion choices; the party's leave
// and arrival; the wear's hand test).
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **The one item you can pick up that rides a bone.** A sword is a second model skinned by the
/// character's own bones and a shield the same; a utility item is its own
/// thing, put in the world and **attached** to the hand — the only place 1.1 turns a motion set's
/// `tag` line into an attachment. What it carries then rides with it: the torch's
/// script hangs its flame on the torch's own `USER0` bone when it hears `worn`, and the flame's
/// dynamic light and particles follow the hand.
///
/// And while the item's flags carry <see cref="CarrySpecial"/> the holder is in the **utility
/// stance**: it stands, walks, runs and swings in the `util_` motions.
/// Of the shipped utility items the torch, the flashlight, the flare, the martyr's fire, the Gangrel
/// eye and the holy statuette carry it — every one of them.
/// </summary>
public static class Utility
{
    /// <summary>
    /// `ITEM_FLAG_CARRYSPECIAL` (the SDK's `CARRY SPECIAL`, 0x800): the bit the attack and locomotion
    /// choices test on the utility item to turn the utility stance on.
    /// </summary>
    public const int CarrySpecial = 0x800;

    /// <summary>
    /// The flags the utility's set attaches with: **none** — snap onto the bone, take the offset, the rotation
    /// the bone's own (Euler zeros). See <see cref="Attachments"/>.
    /// </summary>
    public const uint AttachFlags = 0;

    /// <summary>The highest motion tag + 1 the setter will look up (0x2c).</summary>
    public const int TagLimit = 0x2c;

    /// <summary>Whether a character is in the utility stance: a utility item held whose flags carry <see cref="CarrySpecial"/>.</summary>
    public static bool Stance(Actor? actor) =>
        actor?.Utility is { } item && ((int)item.Flags & CarrySpecial) != 0;

    /// <summary>
    /// `gameActor::SetUtility`. With an item (asserted `ITEM_TYPE_UTILITY`, 10):
    /// <list type="number">
    /// <item>the item's thing is put **in the world at the holder's transform**;</item>
    /// <item>its `ITEM_TAG` is looked up in the holder's motion set (the tag table,
    /// five dwords a record: tag, bone, x, y, z) — a tag of 0 or past
    /// <see cref="TagLimit"/>, a holder with no render model, or a set without the tag leaves bone 0
    /// and offset zero;</item>
    /// <item>it is **attached** to the holder on that bone with that offset, Euler zeros and flags
    /// 0;</item>
    /// <item>the item's flag 0x40 — `NO HIGHLIGHT`, so the pointer passes over it;</item>
    /// <item>the utility item and the item's owner are set;</item>
    /// <item>and the locomotion is chosen again, which is what puts the holder in its
    /// `util` stand at once.</item>
    /// </list>
    /// Without an item, what was held is let go, taken out of the world, loses
    /// `NO HIGHLIGHT`, the utility item is cleared, and the locomotion is chosen again.
    ///
    /// **Ours:** a thing already riding something is let go first. The engine's attach refuses a
    /// child that already rides, and its scene exit lets the torch go before the
    /// party leaves, so in 1.1 the case never arises; here a load or a re-equip may meet
    /// it, and the torch would otherwise stay on the old hand.
    /// </summary>
    public static void Set(WorldThing holder, Item? item)
    {
        if (holder.Actor is not { } actor) return;
        if (item is not null)
        {
            if (item.Thing is { } thing && holder.Place is { } place)
            {
                if (thing.AttachedTo is { } riding) Attachments.Detach(riding, thing);
                // The holder's own 3x4 copied and handed to the world entry.
                thing.Orientation = holder.Orientation;
                if (thing.InWorld && ReferenceEquals(thing.Place, place)) place.Move(thing, holder.Position);
                else
                {
                    if (thing.InWorld) thing.Place!.Withdraw(thing);
                    thing.Position = holder.Position;
                    thing.Sector = holder.Sector;
                    place.Enter(thing);
                }
                var (bone, offset) = Bone(holder, item.Tag);
                Attachments.Attach(holder, thing, bone, offset, Matrix4x4.Identity, AttachFlags);
                thing.Flags |= WorldThing.Flag.NoHighlight;
                thing.Owner = holder;
            }
            actor.Utility = item;
        }
        else if (actor.Utility is { } held)
        {
            if (held.Thing is { } thing)
            {
                Attachments.Detach(holder, thing);
                thing.Place?.Withdraw(thing);
                thing.Flags &= ~WorldThing.Flag.NoHighlight;
            }
            actor.Utility = null;
        }
        holder.Locomotion?.Reanimate();
    }

    /// <summary>
    /// The bone and offset a utility item rides on: the holder's motion-set tag
    /// of that number. **Bone 0, not −1**, when there is none — the engine starts the bone at zero, so
    /// an item whose tag the set lacks rides the root bone.
    /// </summary>
    public static (int Bone, Vector3 Offset) Bone(WorldThing holder, int tag)
    {
        if (holder.Animation is not { } animation || tag <= 0 || tag >= TagLimit) return (0, Vector3.Zero);
        foreach (var t in animation.Set.Tags)
            if (t.Index == tag) return (t.Bone, t.Offset);
        return (0, Vector3.Zero);
    }

    /// <summary>
    /// The inventory's second question before a wear, after the item may be used at
    /// all: a **two-handed weapon** (`ITEM_TYPE` 1 with flag 0x8000) will not go on while
    /// a shield or a utility item is held, and a shield or a utility item will not go on while the
    /// weapon held is two-handed. The wear asks the same before it equips.
    /// </summary>
    public static bool HandsFree(Actor actor, Item item)
    {
        if (item.Type == ItemType.Weapon && item.TwoHanded && (actor.Offhand is not null || actor.Utility is not null))
            return false;
        if (item.Type is ItemType.Shield or ItemType.Utility && HeldTwoHanded(actor))
            return false;
        return true;
    }

    /// <summary>Whether the weapon in the hand is an item flagged two-handed.</summary>
    static bool HeldTwoHanded(Actor actor)
    {
        if (actor.Weapon is not { } weapon) return false;
        foreach (var item in actor.Inventory.Items)
            if (ReferenceEquals(item.Weapon, weapon)) return item.TwoHanded;
        return false;
    }

    /// <summary>
    /// The party leaving a scene: each member's utility item is taken out of the world
    /// and let go of — its script's `endscene` takes the flame away — and the member keeps holding
    /// it (the utility item stays set), so the stance goes with them.
    /// </summary>
    public static void Leave(WorldThing member)
    {
        if (member.Actor?.Utility?.Thing is not { } thing) return;
        thing.Place?.Withdraw(thing);
        if (thing.AttachedTo is { } riding) Attachments.Detach(riding, thing);
    }

    /// <summary>
    /// The arrival's last loop, the party entering a scene: every member holding a utility item has
    /// it set again, which puts it in the new world on the hand. It runs before the
    /// scene's scripts hear `beginscene`, and the torch's `beginscene` lights it.
    /// </summary>
    public static void Rejoin(WorldThing member)
    {
        if (member.Actor?.Utility is { } item) Set(member, item);
    }
}
