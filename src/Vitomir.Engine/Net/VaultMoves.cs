// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The haven vault's moves in a network game: 1.1's message 0x16 — a player's machine asks
// (from the item pane's drop and click), the host makes the move on its copy
// and tells the players, and each player's machine makes it on its own.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// One vault move as 1.1's message 0x16 carries it: three GUIDs — the one moving, the item put **in** the vault
/// (slot 0x4000; 0 for none) and the item taken **out** (0 for none). The same shape both ways: a player's
/// machine to the host (reliably) and the host to the players.
/// </summary>
public readonly record struct VaultMove(int Mover, int In, int Out)
{
    public void Write(NetWriter w) => w.I32(Mover).I32(In).I32(Out);
    public static VaultMove Read(NetReader r) => new(r.I32(), r.I32(), r.I32());
}

/// <summary>
/// **The vault's moves on a player's machine** (1.1's message 0x16). A vault a script opens there (<see cref="StoryVault"/>) is the host's to change: the item pane's gestures on a vault grid make no
/// move on the player's machine, they ask the host for one and return (net mode 1, a client):
/// <list type="bullet">
/// <item>**A click on a vault item with nothing in hand** (after the busy gate): the item
/// out, nothing in — (control, 0, item).</item>
/// <item>**A drop on the vault grid**: the grid's overlap count first (the
/// pane's own overlap test); none — (control, held, 0); one — the swap, (control, held, the one under it); more is
/// the refusal, as it is in single player. **The cell is not sent**: the host puts the item in the
/// vault's first free cell (searched from 0,0), wherever it was dropped.</item>
/// </list>
/// The host (<see cref="Host"/>) makes the move on its copy and tells every player (<see cref="Apply"/> on each machine), which is when the player's own pane changes: what went in
/// leaves the hand, and what came out goes into the hand of the machine's own character when that hand is empty,
/// the pack otherwise. A round trip after the click — the vault's only.
/// </summary>
public static class VaultMoves
{
    /// <summary>What the pane answers for a move sent (ours: 1.1's gesture returns with nothing said).</summary>
    public const string Asked = "asked the host";

    /// <summary>
    /// A gesture on a vault grid on a **player's machine**: sends the move and
    /// answers what the pane says; null when this machine is not a player's (single player and the host make
    /// the move in the pane) or the gesture asks nothing (an empty cell with nothing in hand). A drop that
    /// would cover two items is refused here, as the pane refuses it (<see cref="HandAction.Refused"/>, with no
    /// message sent).
    /// </summary>
    public static HandResult? Ask(WorldThing owner, Item? carrying, InventoryPane on, InventoryPane.Spot spot)
    {
        if (NetOrders.Mode(owner.Place) != NetMode.Client) return null;
        var grid = spot.Grid;
        if (carrying is null)
        {
            if (spot.Item is not { } lift) return null;                                              // nothing to ask
            NetOrders.TellVault(owner, new VaultMove(owner.Guid, 0, lift.Thing?.Guid ?? 0));
            return new(HandAction.Nothing, lift, grid.Slot, $"{lift.Name} out of the vault: {Asked}");
        }
        if (!InventoryPane.Fits(carrying, grid))
            return new(HandAction.Refused, carrying, grid.Slot, $"{carrying.Name} is too big for {grid.Element}");
        var (cx, cy) = DropCell.Of(carrying, spot);                                                  // as the pane finds it
        if (!DropCell.Inside(carrying, grid, cx, cy))
            return new(HandAction.Refused, carrying, grid.Slot, $"{carrying.Name} would hang off {grid.Element}");
        var hit = on.Overlapping(grid, carrying, cx, cy);                                           // the pane's overlap test
        if (hit.Count > 1)
            return new(HandAction.Refused, carrying, grid.Slot, $"{carrying.Name} would cover {hit.Count} items");
        var swapped = hit.Count == 1 ? hit[0] : null;
        NetOrders.TellVault(owner, new VaultMove(owner.Guid, carrying.Thing?.Guid ?? 0, swapped?.Thing?.Guid ?? 0));
        return new(HandAction.Nothing, carrying, grid.Slot,
                   $"{carrying.Name} into the vault{(swapped is null ? "" : $" for {swapped.Name}")}: {Asked}");
    }

    /// <summary>
    /// **The host's move**, in <paramref name="place"/>, where the GUIDs are the host's. It is refused
    /// — nothing done, nobody told — when the mover is not an actor or a player (types 0, 7), or the item out is
    /// named and is not an item, weapon or armour (1, 5, 6; not found is the same). Then, **in 1.1's order**: the
    /// item out, when it is in the vault (its slot is 0x4000) and the mover's (its owner, else its holder), leaves it
    /// With its `remove` noise); the item in, when named, must be an item, weapon or armour or
    /// the move stops there, and must find room in the vault or the move stops there —
    /// **with the item out already out** and in nobody's keeping (1.1 returns before the item out is put anywhere; only a
    /// vault the player's machine saw wrongly can reach it); leaving the hand when it was there. The
    /// item out goes to the pack — **ours**: into the mover's hand when the hand is empty, as the
    /// player's machine puts it, because Vitomir's host replays the player's next gestures with hands of
    /// its own for them (<see cref="HostPack"/>), which must be holding what the player holds. **Ours** as well:
    /// the mover is the player's own character (the caller's), and the items in and out are ones it carries —
    /// 1.1 asks neither (an item out that is somebody else's would be put in the mover's pack). True when the move is made, and the players are to be told.
    /// </summary>
    public static bool Host(Location place, VaultMove m)
    {
        if (place.Thing(m.Mover) is not { Actor: { } actor } mover
            || mover.Type is not (ThingType.Actor or ThingType.Player)) return false;
        var outThing = m.Out != 0 ? place.Thing(m.Out) : null;
        if (m.Out != 0 && outThing?.Item is null) return false;                                   // Types 1, 5, 6
        var inThing = m.In != 0 ? place.Thing(m.In) : null;
        if (inThing?.Item is { } carried && !Carries(mover, actor, inThing, carried)) return false;   // ours
        if (outThing?.Item is { } owned && !Carries(mover, actor, outThing, owned)) return false;     // ours
        if (outThing?.Item is { } taken) Release(mover, outThing, taken);
        if (m.In != 0)
        {
            if (inThing?.Item is not { } put) return false;                                       // Types 1, 5, 6
            if (!Into(mover, actor, put)) return false;                                           // no room
        }
        if (outThing?.Item is { } o) Out(mover, actor, o, toHand: actor.Held is null);            // ours: the hand (the pack in 1.1)
        return true;
    }

    /// <summary>
    /// **A player's machine makes the move** the host has told it of, in its own copy
    /// (<paramref name="mirror"/>'s GUIDs are the host's). The mover must be an actor or a player; an item it
    /// does not know it would ask the host for — **ours**: it is left out, the next sheet bringing
    /// what this machine's character carries. The item out leaves the vault when it is there and the mover's
    /// (with its `remove` noise); the item in goes into the vault's first free cell — none, and nothing more is
    /// done — and leaves the hand when it was there; the item out goes into the mover's hand when the
    /// mover is this machine's control and the hand is empty, into the pack otherwise
    /// (as 1.1 does). The vault pane, when it is up for the control, is filled again — Vitomir's
    /// draws the character's vault every frame. True when something moved.
    /// </summary>
    public static bool Apply(WorldMirror mirror, WorldThing? control, VaultMove m)
    {
        if (mirror.Find(m.Mover) is not { Actor: { } actor } mover
            || mover.Type is not (ThingType.Actor or ThingType.Player)) return false;
        var outThing = m.Out != 0 ? mirror.Find(m.Out) : null;                                  // unknown: left (ours)
        var inThing = m.In != 0 ? mirror.Find(m.In) : null;
        bool moved = false;
        if (outThing?.Item is { } taken) moved |= Release(mover, outThing, taken);
        if (inThing?.Item is { } put)
        {
            if (!Into(mover, actor, put)) return moved;
            moved = true;
        }
        if (outThing?.Item is { } o)
        {
            Out(mover, actor, o, toHand: ReferenceEquals(mover, control) && actor.Held is null);
            moved = true;
        }
        return moved;
    }

    /// <summary>A player's machine takes the host's message.</summary>
    internal static void Moved(GameClient client, NetReader r)
    {
        var m = VaultMove.Read(r);
        if (client.Mirror is { } mirror) Apply(mirror, client.Avatar, m);
    }

    /// <summary>A player's machine is told what her vault holds (<see cref="Msg.VaultItems"/>, ours; <see cref="Fill"/>).</summary>
    internal static void Contents(GameClient client, NetReader r)
    {
        int guid = r.I32();
        var items = new List<(int Guid, string Template, int X, int Y, int Amount)>();
        for (int n = r.U16(); n > 0; n--) items.Add((r.I32(), r.Str(), r.I16(), r.I16(), r.I32()));      // protocol 15: all of it, one message
        if (client.Mirror is { } mirror && mirror.Find(guid) is { } me) Fill(mirror, me, first: true, items);
    }

    /// <summary>
    /// **Ours**: the vault of <paramref name="me"/> on a player's machine made what the host says it holds — emptied
    /// first when <paramref name="first"/> — each item made there when it is not known (as the sheet makes what she
    /// carries), out of the world and the hand, owned by her, at its cell.
    /// </summary>
    public static void Fill(WorldMirror mirror, WorldThing me, bool first, IEnumerable<(int Guid, string Template, int X, int Y, int Amount)> items)
    {
        if (me.Actor is not { } a) return;
        if (first) foreach (var old in a.Inventory.Items.Where(i => i.Location == Slot.Vault).ToList()) a.Inventory.Remove(old);
        foreach (var (guid, template, x, y, amount) in items)
        {
            var t = mirror.Find(guid);
            if (t is null)
            {
                if ((t = mirror.Place.Restore(template, guid, null)) is null) continue;
                mirror.Bind(guid, t);
            }
            if (t.InWorld) mirror.Place.Withdraw(t);
            if (t.Item is not { } item) continue;
            if (ReferenceEquals(a.Held, item)) Ground.ClearHand(me);
            if (!a.Inventory.Items.Contains(item)) a.Inventory.Add(item);
            item.Location = Slot.Vault;
            item.Cell = (x, y);
            item.Amount = amount;
            t.Owner = me;
            t.Holder = null;
        }
    }

    // The item out leaves the vault only when it is in it and the mover's (its slot is 0x4000, and the
    // owner or else the holder is the mover), with its `remove` noise.
    static bool Release(WorldThing mover, WorldThing thing, Item item)
    {
        if (item.Location != Slot.Vault || !ReferenceEquals(thing.Owner ?? thing.Holder, mover)) return false;
        return Ground.Release(thing, item, mover, null);
    }

    // The vault's first free cell, from 0,0; then out of the hand when it was there.
    static bool Into(WorldThing mover, Actor actor, Item item)
    {
        int was = item.Location;
        var cell = item.Cell;
        if (!Stow.Place(actor, item, Slot.Vault)) { item.Location = was; item.Cell = cell; return false; }
        var at = item.Cell;
        bool held = ReferenceEquals(actor.Held, item);
        if (!held && item.Thing?.Owner is { Actor: { } other } && !ReferenceEquals(other, actor)) other.Inventory.Remove(item);
        Ground.Keep(mover, item, Slot.Vault, at);                                                 // clears the hand when it held it
        return true;
    }

    // The item out into the hand or the pack.
    static void Out(WorldThing mover, Actor actor, Item item, bool toHand)
    {
        if (toHand && Ground.Hand(mover, item)) return;
        actor.Inventory.Remove(item);                                                             // still listed: unlinked first
        if (Stow.Put(mover, item, steering: false) == StowResult.Stowed && item.Thing is { } t) { t.Owner = mover; t.Holder = null; }
    }

    // Ours: the item put in is the mover's own — in its hand or among what it carries.
    static bool Carries(WorldThing mover, Actor actor, WorldThing thing, Item item) =>
        ReferenceEquals(actor.Held, item) || actor.Inventory.Items.Contains(item)
        || ReferenceEquals(thing.Owner, mover) || ReferenceEquals(thing.Holder, mover);
}
