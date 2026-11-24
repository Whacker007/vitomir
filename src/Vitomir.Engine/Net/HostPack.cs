// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A player rearranging their own pack, carried out on the host. 1.1's server has one handler for each
// move a client's pack makes (take, drop, wear, give,
// use), and each checks the same few things first: the one moving is an actor or a player, the
// item is an item, a weapon or armour (types 1, 5, 6), it is the mover's, and the mover is not the
// host's steered body (the Storyteller's possessed one). **Ours**: rather than the moves, the host is sent the gesture and makes it itself, with
// the same `InventoryHands` over the same `ui/inventoryPane.nui` the player's machine has — so every
// rule of the pane (the grid masks, the overlaps, the links, the equip test) is the one single player
// uses, and nothing the player does not carry can come into their pack.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

public static class HostPack
{
    /// <summary>
    /// The gesture made on <paramref name="p"/>'s character with the host's own hands for them
    /// (made on first use). Answers what the hands said, for the host's console.
    /// </summary>
    /// <param name="shopping">
    /// Whether the player has a shop open (<see cref="HostShops.OpenFor"/>): the host's hands then shop as
    /// the player's do — a click offers rather than lifts, so it changes nothing here, and only then is a
    /// shop's drag taken (ours, under <c>quickequip</c>, <paramref name="rules"/> the host's switches).
    /// </param>
    public static string Apply(Location place, NetPlayer p, PackGesture g, int nowMs, bool shopping = false, Qol? rules = null)
    {
        if (p.Avatar is not { Actor: { } actor } me || place.Files is not { } files) return "no character";
        if (p.Hands is not { } hands)
        {
            var pane = new InventoryPane(new UiAssets(files).Pane(InventoryPane.PaneName)) { Subject = actor };
            p.Hands = hands = new InventoryHands(pane, me, place.Effects ?? new EffectDefinitions(files));
        }
        hands.Owner = me;
        hands.NowMs = nowMs;
        hands.Rules = rules;
        // The offer is the player's pane's own choice; the sale comes on its own (`HostShops.Deal`).
        hands.Shop = shopping ? _ => true : null;
        if (!shopping) hands.EndShopDrag();
        switch (g.Kind)
        {
            case PackKind.Click:
                return hands.Click(g.X, g.Y).Say;
            case PackKind.ClickIn:
            {
                // The quick-use strip is the one grid a click can reach that the pane does not own
                // (the status bar gives it id 0x200 and the ordinary grid handler).
                InventoryGrid? grid = g.Slot == Slot.QuickUse ? new InventoryGrid("sbQuickuse", Slot.QuickUse, 5, 1)
                                    : hands.Panes[0].Grids().Cast<InventoryGrid?>().FirstOrDefault(x => x!.Value.Slot == g.Slot);
                return grid is { } on ? hands.ClickIn(on, g.X, g.Y).Say : "no such grid";
            }
            case PackKind.QuickEquip:
                return hands.RightClick(g.X, g.Y, (InputModifier)g.Mods).Say;
            case PackKind.ShopLift:
                return hands.ShopLift(g.X, g.Y, g.Slot == Slot.QuickUse).Say;
            case PackKind.ShopDrop:
                return hands.ShopDrop(g.X, g.Y, g.Slot).Say;
            case PackKind.PutBack:
                return hands.PutBack().Say;
            case PackKind.Use:
            {
                // The use's checks: an item, and the user's own.
                if (place.Thing(g.Item) is not { Item: { } item } thing || !ReferenceEquals(thing.Owner ?? thing.Holder, me))
                    return "not theirs";
                return $"{item.Name}: {Items.Use(me, item, nowMs, (place.Effects ?? new EffectDefinitions(files)).File)}";
            }
        }
        return "?";
    }
}
