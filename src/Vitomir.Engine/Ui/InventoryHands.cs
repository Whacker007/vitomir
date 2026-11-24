// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Moving an item with the mouse: pick one up off the pane, carry it, put it down somewhere else.
//
// **The rules here follow the original game's item pane, not a guess.** It is the icon-element
// layer an earlier note said was missing, and it works as
// follows:
//
//   * The click on an icon: It refuses outright (the quarter-second negative cursor)
//     while the player is **busy** (`now` before its busy time) or **dead** (flag `0x10000000`);
//     otherwise, with an empty hand, it takes the icon out of its grid and calls the hold to put
//     the item in the actor's hand. With something already in hand it forwards to the drop.
//   * The drop: The target must be a grid (element type 5) or a weargrid (0xe), the
//     carried icon's grid mask must include the target's id, a **wear** grid
//     (bits 0x1..0x100) additionally asks the use test whether the actor may equip it, and then
//     the overlap count decides: **0** places it, **1** swaps — the displaced item goes onto the
//     cursor — and **2 or more refuses**. Every refusal is the negative cursor.
//   * The cell an item lands in is written to the icon, which is the pair
//     this project already reads out of a save as `Item.Cell`.
//
// **What is still ours** is narrow and marked: which free cell a drop with no explicit target
// chooses (the engine has its own search, not read), the right-click
// shortcut, which the original does not have at all, and the drag while a shop is up.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>What a click on the inventory did.</summary>
public enum HandAction
{
    /// <summary>The point was not over any grid.</summary>
    Nothing,
    /// <summary>An item left the pane and is now on the cursor.</summary>
    Lifted,
    /// <summary>The carried item went into a container cell.</summary>
    Dropped,
    /// <summary>The carried item went onto the body.</summary>
    Equipped,
    /// <summary>An item came off the body and into the pack.</summary>
    Unequipped,
    /// <summary>One item was displaced and is now on the cursor in the other's place.</summary>
    Swapped,
    /// <summary>The item was used where it lay — read, drunk, cast.</summary>
    Used,
    /// <summary>The carried item was put back where it came from.</summary>
    Returned,
    /// <summary>It would not go there, and why is in the report. The cursor flashes.</summary>
    Refused,
    /// <summary>A shop is open and the item was offered for sale rather than lifted.</summary>
    Offered,
    /// <summary>A shop is open and a right click sold the item at once (ours).</summary>
    Sold,
}

/// <summary>One click's outcome, in words a journal line can carry.</summary>
public readonly record struct HandResult(HandAction What, Item? Item, int Slot, string Say);

/// <summary>
/// The cursor's hand. The item itself lives on the actor (<see cref="Actor.Held"/>,
/// the actor's hand); this is the gesture that puts it there and takes it out again.
/// </summary>
public sealed class InventoryHands(InventoryPane pane, WorldThing owner, EffectDefinitions effects)
{
    /// <summary>
    /// Every pane whose grids this hand can reach, in the order a click tries them. It starts as
    /// the one pane the hand was built on and the caller adds the others as they open.
    ///
    /// **This is the engine's own shape, not a convenience.** An icon grid carries its container
    /// mask and the drop — the one drop handler — resolves the actor with
    /// `GetCurrentControl()` rather than from the pane. So a grid belongs to a *character*, and the
    /// pane it is drawn on only decides where on screen it is. Dragging out of the vault and into
    /// the pack is therefore not a transfer between two inventories; it is one item changing its
    /// `Location` word, exactly like moving it within the pack.
    ///
    /// A pane is in this list only while it is up, because the vault's close **destroys** the
    /// vault's elements when it closes: a click where the grid used to be must miss.
    /// </summary>
    public List<InventoryPane> Panes { get; } = [pane];

    /// <summary>The cursor to flash on a refusal. Null simply skips the flash.</summary>
    public CursorState? Cursor { get; set; }

    /// <summary>
    /// Which quality-of-life switches are in force. Null takes <see cref="Qol.Current"/>, which is
    /// what the game does; a test passes its own rather than installing the global.
    /// </summary>
    public Qol? Rules { get; set; }

    /// <summary>
    /// What a right click hands the item to — `Rpg.Items.Use` in the game, the same hook the
    /// quick-use strip takes. A hook rather than a call so the pane does not reach into the RPG
    /// layer and a headless test can watch what would be used.
    /// </summary>
    public Func<Item, string>? Use { get; set; }

    /// <summary>The clock a refusal's quarter second is measured against.</summary>
    public long NowMs { get; set; }

    /// <summary>
    /// **While a shop is open, a click on an item offers it for sale and never lifts it**
    /// (the click hands the icon to the identify instead of picking it up) — in the pack, on the body
    /// and on the belt alike, so nothing can be moved while shopping. The hook answers whether the
    /// shop would look at it; a refusal flashes the negative cursor. Null when no shop is open.
    /// Ours, with <c>quickequip</c> on: a press that is dragged lifts the item after all, and shift and
    /// a right click puts it on or takes it off (<see cref="Press"/>, <see cref="RightClick"/>).
    /// </summary>
    public Func<Item, bool>? Shop { get; set; }

    /// <summary>
    /// **A right click on an item sells it at once while a shop is up** (ours, always on with no
    /// switch): the hook answers whether it sold; a refusal flashes the negative cursor
    /// as a refused offer does. 1.1's right click does nothing while shopping. Null when no
    /// shop is open.
    /// </summary>
    public Func<Item, bool>? QuickSell { get; set; }

    /// <summary>What a right click on an icon says while a shop is up (ours; 1.1 says nothing).</summary>
    public const string NotWhileShopping = "nothing while shopping";

    /// <summary>What is on the cursor, or null. The hand is the actor's.</summary>
    /// <remarks>
    /// **Read-only, deliberately.** The hand has two ends — the actor's hand and the item's
    /// owner — and it is *outside* the inventory list, because the hold removes the item from
    /// its owner before it writes either. Setting it here wrote one end and left the other two
    /// facts wrong, which is how a letter vanished: taken to the cursor off the floor, put
    /// into the belt by this class, and drawn by nobody, because the strip lists an inventory the
    /// letter was no longer in. <see cref="Ground.Hand"/> and <see cref="Ground.Keep"/> are the
    /// only two ways in and out now.
    /// </remarks>
    public Item? Carrying => owner.Actor?.Held;

    /// <summary>
    /// Whose hands these are. A journey gives the character a new body in the new place (the old
    /// one stays with the place it left), so the harness points this at the new one on arrival —
    /// otherwise a weapon put on in the pack would be put on the body left behind.
    /// </summary>
    public WorldThing Owner { get => owner; set => owner = value; }

    int fromSlot;
    (int X, int Y) fromCell;

    HandResult Refuse(Item? item, int slot, string say)
    {
        Cursor?.Refuse(NowMs);
        return new(HandAction.Refused, item, slot, say);
    }

    /// <summary>
    /// The icon click's gate, which an item icon's left click runs before anything else it could do: a
    /// character who is mid-action (<c>now &lt) or restrained (flag <c>0x80</c>)
    /// cannot move items, and the click is answered with the negative cursor rather than being
    /// ignored. **The dead pass** — the gate tests the dead bit with the other two and then again,
    /// on its own, to skip the refusal — so a steered character that has died
    /// can still lift and drop. Odd, and 1.1's (a comparison with 1.1; Vitomir refused the dead and let the
    /// restrained through). Only an icon's click is gated: a drop on an empty cell is the grid's
    /// own click, the drop, which has no such test.
    /// </summary>
    public bool CanHandle => owner.Actor is { } a
        && (a.Dead || (a.BusyUntil <= NowMs && !a.Has2(Actor.Flag2.Restrained)));

    /// <summary>Puts whatever is carried back where it was lifted from. Safe to call at any time.</summary>
    public HandResult PutBack()
    {
        if (Carrying is not { } item) return new(HandAction.Nothing, null, Slot.None, "nothing in hand");
        Net.NetOrders.TellPack(owner, new Net.PackGesture(0, Net.PackKind.PutBack));      // a network player's pack

        Ground.Keep(owner, item, fromSlot, fromCell);
        return new(HandAction.Returned, item, fromSlot, $"{item.Name} back to {Slot.Name(fromSlot)}");
    }

    /// <summary>
    /// Takes an item out of the pane and puts it in the hand: the icon click's arm with nothing carried.
    /// **First it is taken out of its keeper's keeping** — the release with the noise
    /// (<see cref="Ground.Release"/>) — and only when that answers yes does it go onto the cursor;
    /// a no is a silent nothing, no red cross (the return). The release is what
    /// **ends a weapon swap** when the item is the keeper's weapon in the hand
    /// or the one the swap put aside, before the unwear takes it off
    /// with the swap bit already clear; so a sword lifted off the hand under Feral Claws turns the
    /// claws off and leaves the fists (SetWeapon(0)), as in 1.1 (the weapon
    /// override cleared, the broadsword in the hand, the weapon from <c>claw01</c> to <c>punch</c>). Once
    /// Vitomir called <see cref="Equipment.Unequip"/> directly, which refuses the put-aside weapon
    /// while the swap lasts, and ignored the refusal: the sword went on the pointer with the claws
    /// still on, and the claws' end put it back in the hand as well — one sword in two places.
    /// The other refusals are the release's too: a keeper in another shape, and a worn item whose
    /// `unworn` (or the wearer's `actorunworn`) says no.
    /// </summary>
    HandResult Lift(Item lift)
    {
        var (slot, cell) = (lift.Location, lift.Cell);
        if (!Ground.Release(lift.Thing, lift, owner, null))
            return new(HandAction.Nothing, lift, slot, $"{lift.Name} will not come out");
        fromSlot = slot;
        fromCell = cell;
        Ground.Hand(owner, lift);
        return new(HandAction.Lifted, lift, fromSlot, $"holding {lift.Name}");
    }

    /// <summary>
    /// A left click at a point in the pane's space. With an empty hand it lifts whatever is there;
    /// with something in hand it tries to put it down, and a worn slot equips rather than stacks.
    /// </summary>
    public HandResult Click(int x, int y)
    {
        foreach (var on in Panes)
            if (on.At(x, y) is { } spot)
            {
                // A network player's pack: the host makes the same click.
                if (spot.Grid.Slot != Slot.Vault) Net.NetOrders.TellPack(owner, new Net.PackGesture(0, Net.PackKind.Click, x, y));
                return Click(on, spot);
            }
        return new(HandAction.Nothing, null, Slot.None, "not over a grid");
    }

    /// <summary>Whether any open pane owns this point — what decides if a click reaches the world.</summary>
    public bool Over(int x, int y)
    {
        foreach (var on in Panes) if (on.At(x, y) is not null) return true;
        return false;
    }

    /// <summary>
    /// The same click, on a grid the pane does not own. **The quick-use strip is one of these**: it
    /// lives on the status bar, but the status bar gives it grid id `0x200` and then registers
    /// **the inventory drop** on binding 0 — the very handler every inventory grid gets. So the belt is
    /// an ordinary icon grid and a left click on it picks up or drops, exactly like the pack.
    /// </summary>
    public HandResult ClickIn(InventoryGrid grid, int cx, int cy)
    {
        Net.NetOrders.TellPack(owner, new Net.PackGesture(0, Net.PackKind.ClickIn, cx, cy, grid.Slot));
        return Click(pane, new InventoryPane.Spot(grid, cx, cy, pane.ItemIn(grid, cx, cy)));
    }

    HandResult Click(InventoryPane on, InventoryPane.Spot spot, bool offers = true)
    {
        // An icon's click is gated; an empty cell's is the grid's own drop, which is not.
        if (spot.Item is not null && !CanHandle) return Refuse(Carrying, spot.Grid.Slot, "busy");
        if (offers && Shop is { } shop)
        {
            if (spot.Item is not { } offered) return new(HandAction.Nothing, null, spot.Grid.Slot, "nothing there");
            return shop(offered)
                ? new(HandAction.Offered, offered, spot.Grid.Slot, $"{offered.Name} offered for sale")
                : Refuse(offered, spot.Grid.Slot, $"the shop will not take {offered.Name}");
        }

        // A player's machine asks the host for a vault move and moves nothing (Net.VaultMoves).
        if (spot.Grid.Slot == Slot.Vault && Net.VaultMoves.Ask(owner, Carrying, on, spot) is { } asked)
            return asked.What == HandAction.Refused ? Refuse(asked.Item, asked.Slot, asked.Say) : asked;

        if (Carrying is null)
        {
            if (spot.Item is not { } lift)
                return new(HandAction.Nothing, null, spot.Grid.Slot, $"{spot.Grid.Element} is empty there");
            return Lift(lift);
        }

        var item = Carrying;
        if (spot.Grid.IsContainer)
        {
            if (!InventoryPane.Fits(item, spot.Grid))
                return Refuse(item, spot.Grid.Slot, $"{item.Name} is too big for {spot.Grid.Element}");

            // Where its picture is: the drawn top-left rounded to a cell, refused when the
            // footprint leaves the grid (the fit test → an overlap count of -1). `DropCell` has the detail.
            var (cx, cy) = DropCell.Of(item, spot, Rules);
            if (!DropCell.Inside(item, spot.Grid, cx, cy))
                return Refuse(item, spot.Grid.Slot, $"{item.Name} would hang off {spot.Grid.Element}");

            // The overlap count, which the drop asks for before it commits.
            var hit = on.Overlapping(spot.Grid, item, cx, cy);
            if (hit.Count > 1)
                return Refuse(item, spot.Grid.Slot, $"{item.Name} would cover {hit.Count} items");

            if (hit.Count == 1)
            {
                var displaced = hit[0];
                fromSlot = displaced.Location;
                fromCell = displaced.Cell;
                if ((displaced.Location & Slot.Worn) != 0) Equipment.Unequip(owner, displaced, effects);
                Ground.Keep(owner, item, spot.Grid.Slot, (cx, cy));
                Worn(item);
                Ground.Hand(owner, displaced);
                return new(HandAction.Swapped, displaced, spot.Grid.Slot,
                           $"{item.Name} into {Slot.Name(spot.Grid.Slot)}, holding {displaced.Name}");
            }

            Ground.Keep(owner, item, spot.Grid.Slot, (cx, cy));
            Worn(item);
            return new(HandAction.Dropped, item, spot.Grid.Slot,
                       $"{item.Name} into {Slot.Name(spot.Grid.Slot)} at {cx},{cy}");
        }

        // A worn slot (placement: goes in if the mask matches and the slot is empty). The drop
        // asks the use test whether the actor may equip it, which is what `Equipment` answers here; the
        // overlap rule then applies as it does to a container, and a weargrid is one item deep, so the
        // occupant — if there is exactly one — is the swap. A two-handed item that would displace
        // **both** halves of a pair is the two-overlap case (the overlap count's result 2) — gloves dropped
        // between two bracelets are refused; a bracelet dropped on worn gloves counts one and swaps.
        //
        // **But the mask is tested twice.** When it does not match, the drop follows the weargrid's
        // link and tests the partner — which is why a one-handed sword dropped on
        // the off-hand lands in the sword hand rather than being refused. Only the two weapon slots
        // and the two arm slots are linked; a ring on the wrong hand is still a refusal.
        //
        // **A click on the other half of a two-handed pair drops on the half the item was put in.** A
        // weargrid holding an item clicks that item's icon, and with something in
        // hand the click hands the drop to the grid the pane finds — the weargrid holding this item
        // **whose mask is the icon's own grid** (written by the placing), which is
        // the item's `Location`, never the partner. So a bracelet clicked onto the left glove of gloves
        // put on the right arm swaps onto the right arm.
        var target = spot.Item is { } worn && worn.Location != spot.Grid.Slot
                     && on.GridOf(worn.Location) is { } own ? own : spot.Grid;
        if ((item.Slots & target.Slot) == 0 && on.Linked(target) is { } linked
            && (item.Slots & linked.Slot) != 0)
            target = linked;

        if ((item.Slots & target.Slot) == 0)
            return Refuse(item, target.Slot, $"{item.Name} does not go in {Slot.Name(target.Slot)}");

        // The drop asks the use test before the count: may not use it is the red cross, no word said.
        if (!Equipment.CanUse(owner, item))
            return Refuse(item, target.Slot, $"{item.Name}: {EquipResult.Restricted}");
        var already = Displaced(item, target.Slot);
        if (already.Count > 1)
            return Refuse(item, target.Slot, $"{item.Name} would displace {already.Count} items");

        // **The swap takes the displaced one off first** (WearDrop), so the two-hand
        // rule is asked with the hands already free: a torch on a two-hander is a swap, not a refusal.
        // The swap's release is silent, so a displaced weapon the claws put aside ends the swap
        // first and then comes off, as a lift does (`Ground.EndSwapFor`).
        int displacedFrom = already.Count == 1 ? already[0].Location : Slot.None;
        if (already.Count == 1) Ground.EndSwapFor(owner, already[0]);
        if (already.Count == 1 && !Equipment.Unequip(owner, already[0], effects))
            return Refuse(item, target.Slot, $"{already[0].Name} will not come off");

        // Out of the hand and into the list before the equip, because the hand is outside it and
        // `Equipment.Equip` works on what a character is carrying. A refusal puts it back.
        Ground.Keep(owner, item, item.Location, item.Cell);
        var got = Equipment.Equip(owner, item, target.Slot, effects, 0);
        if (got != EquipResult.Equipped)
        {
            owner.Actor?.Inventory.Remove(item);
            Ground.Hand(owner, item);
            // "I can't wear that" before the red cross; the displaced one goes
            // back on (ours: 1.1 asserts the placing succeeds there).
            bool said = WearDrop.EquipRefused(got);
            if (said) WearDrop.CannotWear(owner);
            if (already.Count == 1) Equipment.Equip(owner, already[0], displacedFrom, effects, 0);
            return Refuse(item, target.Slot, $"{item.Name}: {got}{(said ? WearDrop.Said : "")}");
        }

        // **By slot, not by reference.** `InventoryGrid` is a record struct, so `ReferenceEquals`
        // boxes both sides and is always false — which said "moved from" on every equip, including
        // the ones that went exactly where they were dropped. CA2013 caught it; this is the note
        // that it was a real answer and not a style complaint.
        string where = target.Slot == spot.Grid.Slot ? "" : $" (moved from {Slot.Name(spot.Grid.Slot)})";
        Worn(item);
        if (already.Count == 1)
        {
            var displaced = already[0];
            fromSlot = displacedFrom == Slot.None ? target.Slot : displacedFrom;
            fromCell = displaced.Cell;
            Ground.Hand(owner, displaced);
            return new(HandAction.Swapped, displaced, target.Slot,
                       $"{item.Name} on {Slot.Name(target.Slot)}{where}, holding {displaced.Name}");
        }

        return new(HandAction.Equipped, item, target.Slot,
                   $"{item.Name} on {Slot.Name(target.Slot)}{where}");
    }

    // ---- The drag while a shop is up (ours, under `quickequip`) ----
    //
    // In 1.1 nothing can be lifted while shopping: a click on one of your own items offers it for sale
    // (the `Shop` hook above). Ours, with `quickequip` on: a press that moves past a few pixels before it
    // is let go lifts the item instead, and the release puts it down by the pane's own rules, so a coat
    // just bought can be tried on without closing the shop. A press let go without moving is still
    // 1.1's offer. The lifted item never stays on the cursor past the release: a drop off the
    // player's own grids, or one the pane refuses, puts it back where it came from.

    /// <summary>
    /// How far the pointer moves, in window pixels, before a press on an item in a shop is a drag
    /// (ours): the launcher's drag start, measured the same way, as a straight-line distance.
    /// </summary>
    public const float ShopDragStart = 5f;

    /// <summary>The quick-use belt as a grid, which the pane has no element for (the status bar draws it).</summary>
    static readonly InventoryGrid Belt = new("sbQuickuse", Slot.QuickUse, 5, 1);

    /// <summary>The press waiting to become a click or a drag: the spot, whether it is the belt's, its point and its window pixel.</summary>
    (InventoryPane On, InventoryPane.Spot Spot, bool Belt, int X, int Y, float Wx, float Wy)? pressed;

    /// <summary>Whether the item in hand was lifted by a shop's drag, and where it was lifted from.</summary>
    bool shopDragging;
    int dragFromSlot;
    (int X, int Y) dragFromCell;

    /// <summary>Whether a press on an item can become a drag: a shop is up and the switch is on.</summary>
    public bool ShopDrags => Shop is not null && (Rules ?? Qol.Current).QuickEquip;

    /// <summary>Whether a shop's press or drag is under way, waiting for its release.</summary>
    public bool ShopHeld => pressed is not null || shopDragging;

    /// <summary>
    /// The left button went down at a point in the pane's space (<paramref name="wx"/>,
    /// <paramref name="wy"/>: the same press in window pixels, for the drag's start). With no shop's drag
    /// to wait for this is <see cref="Click(int, int)"/>, at once, as it always was; while shopping
    /// with the switch on, a press on an item waits for <see cref="Drag"/> or the release.
    /// </summary>
    public HandResult Press(int x, int y, float wx, float wy)
    {
        if (ShopDrags && Carrying is null)
            foreach (var on in Panes)
                if (on.At(x, y) is { Item: not null } spot && spot.Grid.Slot != Slot.Vault)
                    return Wait(on, spot, false, x, y, wx, wy);
        return Click(x, y);
    }

    /// <summary>The same press on the quick-use belt, by its cell (the status bar's grid; see <see cref="ClickIn"/>).</summary>
    public HandResult PressIn(InventoryGrid grid, int cx, int cy, float wx, float wy)
    {
        var spot = new InventoryPane.Spot(grid, cx, cy, pane.ItemIn(grid, cx, cy));
        if (ShopDrags && Carrying is null && spot.Item is not null && grid.Slot == Slot.QuickUse)
            return Wait(pane, spot, true, cx, cy, wx, wy);
        return ClickIn(grid, cx, cy);
    }

    HandResult Wait(InventoryPane on, InventoryPane.Spot spot, bool belt, int x, int y, float wx, float wy)
    {
        pressed = (on, spot, belt, x, y, wx, wy);
        return new(HandAction.Nothing, spot.Item, spot.Grid.Slot, $"{spot.Item!.Name} pressed");
    }

    /// <summary>
    /// The pointer moved to window pixel (<paramref name="wx"/>, <paramref name="wy"/>) with the button
    /// down. The move that takes a waiting press <see cref="ShopDragStart"/> or more from where it went
    /// down lifts the item; any other move does nothing.
    /// </summary>
    public HandResult Drag(float wx, float wy)
    {
        if (pressed is not { } p) return new(HandAction.Nothing, null, Slot.None, "no press waiting");
        float dx = wx - p.Wx, dy = wy - p.Wy;
        if (dx * dx + dy * dy < ShopDragStart * ShopDragStart)
            return new(HandAction.Nothing, p.Spot.Item, p.Spot.Grid.Slot, "not a drag yet");
        pressed = null;
        return ShopLift(p.X, p.Y, p.Belt);
    }

    /// <summary>
    /// A shop's drag starting: the item at the pane point (<paramref name="x"/>, <paramref name="y"/>),
    /// or at that cell of the belt, lifted by the ordinary lift. Only while a shop is up with the switch
    /// on — and so the host, which replays it for a player, takes it only while that player shops.
    /// </summary>
    public HandResult ShopLift(int x, int y, bool belt)
    {
        if (!ShopDrags) return new(HandAction.Nothing, null, Slot.None, "no shop's drag");
        if (Carrying is not null) return new(HandAction.Nothing, Carrying, Slot.None, "something is in hand already");
        var (on, spot) = belt ? (pane, new InventoryPane.Spot(Belt, x, y, pane.ItemIn(Belt, x, y))) : SpotAt(x, y);
        if (spot is not { Item: { } item } at || at.Grid.Slot == Slot.Vault)
            return new(HandAction.Nothing, null, Slot.None, "nothing there");
        if (!CanHandle) return Refuse(null, at.Grid.Slot, "busy");
        Net.NetOrders.TellPack(owner, new Net.PackGesture(0, Net.PackKind.ShopLift, x, y, belt ? Slot.QuickUse : 0));
        var got = Lift(item);
        if (got.What == HandAction.Lifted) (shopDragging, dragFromSlot, dragFromCell) = (true, fromSlot, fromCell);
        return got;
    }

    (InventoryPane On, InventoryPane.Spot? Spot) SpotAt(int x, int y)
    {
        foreach (var on in Panes) if (on.At(x, y) is { } spot) return (on, spot);
        return (pane, null);
    }

    /// <summary>
    /// The left button let go at a point in the pane's space. A press that never became a drag is
    /// the click it always was — 1.1's offer for sale, made where it was pressed; a drag puts the
    /// item down there (<see cref="ShopDrop"/>).
    /// </summary>
    public HandResult Release(int x, int y)
    {
        if (pressed is { } p)
        {
            pressed = null;
            return p.Belt ? ClickIn(p.Spot.Grid, p.X, p.Y) : Click(p.X, p.Y);
        }
        return shopDragging ? ShopDrop(x, y, 0) : new(HandAction.Nothing, null, Slot.None, "nothing pressed");
    }

    /// <summary>The same release over a cell of the quick-use belt.</summary>
    public HandResult ReleaseIn(int cx, int cy)
    {
        if (pressed is not null) return Release(0, 0);
        return shopDragging ? ShopDrop(cx, cy, Slot.QuickUse) : new(HandAction.Nothing, null, Slot.None, "nothing pressed");
    }

    /// <summary>The same release over none of the player's grids — the shelf, the shop's buttons, the art between.</summary>
    public HandResult ReleaseOff()
    {
        if (pressed is not null) return Release(0, 0);
        return shopDragging ? ShopDrop(0, 0, Off) : new(HandAction.Nothing, null, Slot.None, "nothing pressed");
    }

    /// <summary>A shop's drop over none of the player's grids (the gesture's slot word).</summary>
    public const int Off = -1;

    /// <summary>
    /// A shop's drag ending: the item in hand put down at the pane point (<paramref name="x"/>,
    /// <paramref name="y"/>) — <paramref name="where"/> 0 — at that cell of the belt
    /// (<see cref="Slot.QuickUse"/>), or nowhere (<see cref="Off"/>). The drop is the pane's own
    /// (<see cref="Click(int, int)"/> with something in hand: the masks, the sizes, the overlaps, the
    /// equip test); what it will not take goes back where it came from, the red cross flashed, and so
    /// does a drop anywhere else — there is no sale by drag and no drop to the ground while shopping.
    /// A swap puts the one displaced where the dragged one came from, else in the pack, else undoes itself.
    /// </summary>
    public HandResult ShopDrop(int x, int y, int where)
    {
        if (!shopDragging) return new(HandAction.Nothing, null, Slot.None, "no shop's drag");
        shopDragging = false;
        if (Carrying is not { } item) return new(HandAction.Nothing, null, Slot.None, "nothing in hand");
        Net.NetOrders.TellPack(owner, new Net.PackGesture(0, Net.PackKind.ShopDrop, x, y, where));
        var (on, spot) = where == Slot.QuickUse ? (pane, new InventoryPane.Spot(Belt, x, y, pane.ItemIn(Belt, x, y)))
                       : where == 0 ? SpotAt(x, y) : (pane, null);
        if (spot is not { } at || at.Grid.Slot == Slot.Vault)
            return GoBack(item, HandAction.Returned, "");

        var got = Click(on, at, offers: false);
        if (got.What == HandAction.Refused) return GoBack(item, HandAction.Refused, got.Say + ", ");
        if (got.What != HandAction.Swapped) return got;
        return Settle(on, at, item, got);
    }

    /// <summary>
    /// After a shop's drop that swapped: the displaced item, now in hand, goes to the cell the dragged
    /// one left when that was a cell of the pack or the belt and it fits there with nothing in the way —
    /// a sword dragged onto the sword hand sends the old one to where the new one was — else to the
    /// pack's first free cell, else the swap is undone and the dragged item goes back.
    /// </summary>
    HandResult Settle(InventoryPane on, InventoryPane.Spot at, Item dragged, HandResult swap)
    {
        var displaced = Carrying!;
        var actor = owner.Actor!;
        var from = dragFromSlot == Slot.QuickUse ? Belt : dragFromSlot == Slot.Inventory ? pane.GridOf(Slot.Inventory) : null;
        if (from is { } g && InventoryPane.Fits(displaced, g) && DropCell.Inside(displaced, g, dragFromCell.X, dragFromCell.Y)
            && pane.Overlapping(g, displaced, dragFromCell.X, dragFromCell.Y).Count == 0)
        {
            Ground.Keep(owner, displaced, dragFromSlot, dragFromCell);
            Worn(displaced);
            return swap with { Say = $"{swap.Say}, {displaced.Name} into {Slot.Name(dragFromSlot)}" };
        }
        if (Stow.FreeCell(actor, displaced, Slot.Inventory) is { } free)
        {
            Ground.Keep(owner, displaced, Slot.Inventory, free);
            Worn(displaced);
            return swap with { Say = $"{swap.Say}, {displaced.Name} into the pack" };
        }
        // No room anywhere: the displaced one back where it was, which hands the dragged one back.
        var undo = Click(on, at, offers: false);
        if (Carrying is { } back) GoBack(back, HandAction.Returned, "");
        Cursor?.Refuse(NowMs);
        return new(HandAction.Refused, dragged, at.Grid.Slot, $"no room for {displaced.Name} ({undo.Say}), {dragged.Name} back");
    }

    /// <summary>
    /// The item in hand back where a shop's drag lifted it: into its cell, or back on the body by the
    /// equip when it was worn (the lift took it off) — and, should the equip refuse, into the pack, and
    /// with no room there, to its old slot as <see cref="PutBack"/> leaves it.
    /// </summary>
    HandResult GoBack(Item item, HandAction what, string why)
    {
        if ((dragFromSlot & Slot.Worn) != 0)
        {
            Ground.Keep(owner, item, Slot.None, item.Cell);
            if (Equipment.Equip(owner, item, dragFromSlot, effects, 0) == EquipResult.Equipped)
                return new(what, item, dragFromSlot, $"{why}{item.Name} back on {Slot.Name(dragFromSlot)}");
            if (owner.Actor is { } a && Stow.Place(a, item, Slot.Inventory))
                return new(what, item, Slot.Inventory, $"{why}{item.Name} into the pack");
        }
        Ground.Keep(owner, item, dragFromSlot, dragFromCell);
        return new(what, item, dragFromSlot, $"{why}{item.Name} back to {Slot.Name(dragFromSlot)}");
    }

    /// <summary>
    /// The shop went with a press or a drag under way (ours): the press is forgotten and a lifted item
    /// goes back where it came from, so nothing is left on the cursor. Safe to call at any time.
    /// </summary>
    public void EndShopDrag()
    {
        pressed = null;
        if (shopDragging) ShopDrop(0, 0, Off);       // a drop nowhere, which a network player's host makes too
    }

    /// <summary>
    /// A drop that went in, pack or body: the item's `wear`, **twice** — once from the placing
    /// (on its owner) and once more from the drop that called it (on the steered
    /// character, or on a swap). Both are the item's own set; the
    /// engine really does start the sound twice.
    /// </summary>
    void Worn(Item item)
    {
        Items.Heard(owner, item, Audio.FoleyEvent.Wear);
        Items.Heard(owner, item, Audio.FoleyEvent.Wear);
    }

    /// <summary>
    /// What wearing <paramref name="item"/> in <paramref name="slot"/> would take off: the slot's
    /// own occupant, plus the other half of a two-handed pair when either item reaches across it.
    /// This is the same rule `Equipment.Equip` applies — asked in advance, because the count is
    /// what the engine's overlap test answers and two is a refusal (0 empty, 1 occupied,
    /// 2 when a two-handed item would displace two different items). The pair is the weargrid's
    /// link — the two weapon grids and the two arms — and else <see cref="Item.Partner"/> (the ring
    /// hands, ours); earlier, the weapon pair was left out, so a torch beside a two-hander counted 0.
    /// </summary>
    public IReadOnlyList<Item> Displaced(Item item, int slot)
    {
        var hit = new List<Item>();
        if (owner.Actor is not { } actor) return hit;
        if (Equipment.InSlot(actor, slot) is { } occupant) hit.Add(occupant);
        // The weargrid's link first (the overlap count reads it): the weapon pair too, whose two-handed
        // weapon the placing writes into both grids (WearDrop).
        int linked = InventoryPane.LinkOf(slot);
        if ((linked != Slot.None ? linked : Item.Partner(slot)) is var partner && partner != Slot.None
            && Equipment.InSlot(actor, partner) is { } other && (item.TwoHanded || other.TwoHanded)
            && !hit.Contains(other))
            hit.Add(other);
        return hit;
    }

    /// <summary>
    /// **A right click uses the item**, which is the original's own gesture and was missing here
    /// until then: the letter is read by a right click, and a right click on a
    /// usable item uses it (scrolls, potions and the like).
    ///
    /// That correction matters more than it looks. An earlier build gave right-click an equip/unequip
    /// shortcut and called it an invention, because the icon click is the only click path
    /// an icon has and it only ever picks up. It is the only path for the **first** binding; the
    /// second one is registered separately (a per-slot binding) and
    /// the icon's had not been read. So ours was not adding a gesture the original lacked —
    /// it was **taking one away**, and a letter you could not read was the symptom.
    ///
    /// So right-click is faithful now: it hands the item to <see cref="Use"/>, which is
    /// the item code's own path. The equip shortcut moved to <b>shift</b> + right-click and stayed
    /// a quality-of-life switch, because it is still useful and still not the original's.
    /// </summary>
    public HandResult RightClick(int x, int y, InputModifier mods = InputModifier.None)
    {
        InventoryPane.Spot? under = null;
        foreach (var on in Panes) if (on.At(x, y) is { } hit) { under = hit; break; }
        // **While a shop is up an icon's right click does nothing** — no cast at it, no use (the right
        // click tests the shop's flag, which the shop pane's show sets and its hide
        // clears, before anything else). Ours: with `quickequip` on, shift and a right click still puts
        // the item on or takes it off, below, as it does with no shop up.
        bool quickEquip = mods.HasFlag(InputModifier.Shift) && (Rules ?? Qol.Current).QuickEquip;
        if (Shop is not null && !quickEquip && under is { Item: { } shopping } shelf)
        {
            if (QuickSell is { } sell)
                return sell(shopping) ? new(HandAction.Sold, shopping, shelf.Grid.Slot, $"{shopping.Name} sold")
                                      : Refuse(shopping, shelf.Grid.Slot, $"the shop will not buy {shopping.Name}");
            return new(HandAction.Nothing, shopping, shelf.Grid.Slot, NotWhileShopping);
        }
        // An item's icon is a target for the one kind of discipline that takes an item — `TARGET
        // INVENTORY`, Spirit's Touch's — before it is anything else.
        if (under is { Item: { } target } aimed && CastAt(target) is { } cast)
            return new(HandAction.Used, target, aimed.Grid.Slot, cast);
        // **Something on the cursor changes nothing**: the right click has no arm for it, and nothing
        // else in 1.1 takes the right button over the pack — the grids register no right-button
        // handler (the pane fills only slot 0 with the drop; an icon gets the right click in slot
        // 2, fired by the element), and neither the cursor nor the
        // grid's placing looks at the button. So an icon's right click uses the icon's own
        // item with the carried one still on the cursor, and one over an empty cell does nothing.
        // Vitomir put the carried item back (`PutBack`, kept for a network player's pack).
        if (under is not { Item: { } item } spot)
            return new(HandAction.Nothing, null, Slot.None, "nothing there");
        // **No busy test before the use** (the right click goes straight to the use): a character mid-swing
        // drinks from its pack. Vitomir refused it with the red cross until a comparison with 1.1. The gate
        // stays on the quick-equip below, which is ours.
        if (!mods.HasFlag(InputModifier.Shift))
            return Use is null
                ? new(HandAction.Nothing, item, spot.Grid.Slot, $"{item.Name}: nothing to use it with")
                : new(HandAction.Used, item, spot.Grid.Slot, Use(item));

        if (!CanHandle) return Refuse(null, spot.Grid.Slot, "busy");

        if (!(Rules ?? Qol.Current).QuickEquip)
            return new(HandAction.Nothing, null, Slot.None, "the quick-equip shortcut is off");
        // A player's vault is the host's (ours): the quick equip, a QoL, makes no move out of it there.
        if (spot.Grid.Slot == Slot.Vault && Net.NetOrders.Mode(owner.Place) == Net.NetMode.Client)
            return new(HandAction.Nothing, item, Slot.Vault, "the vault's moves are the host's");
        Net.NetOrders.TellPack(owner, new Net.PackGesture(0, Net.PackKind.QuickEquip, x, y, Mods: (int)mods));

        if ((item.Location & Slot.Worn) != 0)
        {
            // Ours, and read as a lift and a drop into the pack: a weapon under a swap ends it first
            // (as `Lift`), and a take-off that is refused leaves it on rather than moving a
            // worn item into the pack as well (earlier, the refusal was not asked).
            // Room is asked first (ours): a take-off with no free cell left the item off
            // and in the pack at the cell it had before it was worn, over whatever holds that now.
            if (Stow.FreeCell(owner.Actor!, item, Slot.Inventory) is null)
                return Refuse(item, item.Location, $"no room in the pack for {item.Name}");
            Ground.EndSwapFor(owner, item);
            if (!Equipment.Unequip(owner, item, effects))
                return Refuse(item, item.Location, $"{item.Name} will not come off");
            Stow.Place(owner.Actor!, item, Slot.Inventory);
            return new(HandAction.Unequipped, item, Slot.Inventory, $"{item.Name} off, into the pack");
        }

        if (item.Slots == Slot.None)
            return Refuse(item, spot.Grid.Slot, $"{item.Name} is not worn or held");
        var got = Equipment.Equip(owner, item, Slot.None, effects, 0);
        return got == EquipResult.Equipped
            ? new(HandAction.Equipped, item, item.Location, $"{item.Name} on {Slot.Name(item.Location)}")
            : Refuse(item, spot.Grid.Slot, $"{item.Name}: {got}");
    }

    /// <summary>
    /// The right click's first two steps: a scroll in the hand whose discipline takes an item is read at
    /// the icon's item; if that was refused — or there was none — the current discipline, when it
    /// takes an item, is cast at it with Space held. Answers what was done, or null when both were
    /// refused or neither applied, which leaves the icon to its ordinary right click.
    /// </summary>
    string? CastAt(Item target)
    {
        if (owner.Actor is not { } me || target.Thing is not { } body) return null;
        var result = OrderResult.Refused;
        if (me.Held is { Type: ItemType.Scroll } scroll
            && owner.Place?.Powers?[scroll.ScrollDiscipline] is { } read && read.Has(Formats.DisciplineFlags.TargetInventory))
            result = Cast.Order(owner, body, null, null, InputModifier.None, scroll);
        if (result == OrderResult.Refused && me.Disciplines.Current is { } cur
            && cur.Definition.Has(Formats.DisciplineFlags.TargetInventory))
            result = Cast.Order(owner, body, null, cur, InputModifier.Space);
        return result == OrderResult.Refused ? null : $"{me.Disciplines.Current?.Name ?? "a scroll"} at {target.Name}: {result}";
    }
}
