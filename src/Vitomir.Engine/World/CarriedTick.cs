// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A character's carried things' own ticks.
using Vitomir.Engine.Rpg;

namespace Vitomir.Engine.World;

public sealed partial class Simulation
{
    /// <summary>
    /// **A character's carried things' own ticks**: after the
    /// effects and the action, the character tick walks everything it owns — worn, on the belt, in
    /// the pack, in the vault; the actor's list, newest first, read before each
    /// tick — and then the item on the pointer, and runs the generic thing tick
    /// on each. Only two of that tick's parts can act on a carried thing, and only those
    /// are run here, and only for a thing that has something for them to do:
    /// <list type="number">
    /// <item><b>The first step</b>: an expiry that has passed destroys it,
    /// and a thing destroyed — by that, or by a script's `Remove` on something in a pack,
    /// which only flags it — is deleted there, which for an item is the item's delete: out of its
    /// keeper's keeping, silently, taken off first if it is worn (<see cref="Ground.Deleted"/>); its
    /// scripts hear of it.</item>
    /// <item><b>Its model and its riders</b>, which the thing's tick runs only for a
    /// thing in a world: of carried things only the utility item is (the wear puts it in
    /// the world at the hand), and it is also in the world's list — so 1.1 moves its model
    /// on twice a frame, once in its own place in the walk and once here. Its riders are not posed
    /// here: the attachment pass poses a thing's riders only when it rides nothing itself, and the
    /// utility item rides the hand; its holder's own riders pass carries it and its flame, after
    /// this.</item>
    /// </list>
    /// A carried thing that has none of these costs one test. Nothing else of the thing's tick can act on
    /// one.
    /// </summary>
    void CarriedTick(Actor actor, float seconds)
    {
        var items = actor.Inventory.Items;
        bool any = false;
        foreach (var item in items)
            if (Needs(item)) { any = true; break; }
        if (any)
            foreach (var item in items.ToArray())                        // each one's next read before its tick
                if (actor.Inventory.Items.Contains(item)) CarriedThingTick(item, seconds);
        if (actor.Held is { } held && Needs(held)) CarriedThingTick(held, seconds);
    }

    /// <summary>Whether a carried thing has anything for its tick to do (ours: 1.1 ticks every one).</summary>
    bool Needs(Item item) =>
        item.Thing is { } t
        && (t.ExpireAt != 0 || t.Removed || (t.Flags & WorldThing.Flag.Remove) != 0
            || (t.InWorld && (animatedOf.ContainsKey(t) || t.Fx is { Busy: true }
                              || (t.Attachments.Count > 0 && t.AttachedTo is null))));

    /// <summary>One carried thing's tick: its first step, then — while it is in a world — its model and its riders.</summary>
    void CarriedThingTick(Item item, float seconds)
    {
        if (item.Thing is not { } thing) return;
        if (ExpireCarried(thing, item)) return;
        if (!thing.InWorld) return;
        StepModel(thing, seconds);
        if (thing.InWorld && thing.Attachments.Count > 0 && thing.AttachedTo is null) Attachments.Update(thing);
    }

    /// <summary>
    /// The first step for a carried thing: the expiry destroys it — an item is never the steered
    /// character nor a projectile, so neither of <see cref="Expire"/>'s other turns applies — and a
    /// destroyed one is deleted: the destroy's own work (<see cref="Location.Remove"/>,
    /// which lets go of what it rides and what rides it), then out of its keeper's keeping
    /// (<see cref="Ground.Deleted"/>), then its scripts told. True when it is gone.
    /// </summary>
    bool ExpireCarried(WorldThing thing, Item item)
    {
        if (thing.ExpireAt != 0 && thing.ExpireAt <= Milliseconds)
        {
            thing.ExpireAt = 0;
            thing.Flags |= WorldThing.Flag.Remove;
        }
        if (!thing.Removed && (thing.Flags & WorldThing.Flag.Remove) == 0) return false;
        Location.Remove(thing);
        Ground.Deleted(thing, item);
        Scripts.DeleteThing(thing);
        return true;
    }
}
