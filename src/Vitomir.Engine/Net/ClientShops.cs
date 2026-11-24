// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A player's machine's half of a shop the host opens for them: the shop built from
// the host's message and opened on this machine's pane, the pane's purchases, sales and
// close sent to the host, and the host's word on each. See
// `Net.HostShops`.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

public sealed class ClientShops(Action<byte[]> send, Func<WorldThing?> me) : IShopWire
{
    /// <summary>The store the host opened here, while its pane is up.</summary>
    public Store? Open { get; private set; }

    /// <summary>What happened, for the console and the tests.</summary>
    public event Action<string>? Said;

    /// <summary>
    /// Message 0x42 read: the shop built as the host's was — `AddBuySell` with the host's
    /// seed (the seeded roll), so the shelf rolls the same — each line's count put as the
    /// host has it (a line at 0 that is not endless emptied), and then opened on this machine's own
    /// character with no flags and no script: the script is the host's.
    /// </summary>
    internal void Opened(NetReader r, WorldMirror? mirror)
    {
        string file = r.Str(), desc = r.Str();
        int keeper = r.I32();
        uint seed = r.U32(), flags = r.U32();
        float buy = r.F32(), sell = r.F32();
        int mask = r.I32();
        var counts = new sbyte[r.U8()];
        for (int i = 0; i < counts.Length; i++) counts[i] = (sbyte)r.U8();
        if (mirror?.Place is not { Session: { } session } place || mirror.Find(keeper) is not { } owner)
        {
            Said?.Invoke($"shop: {file} — no keeper {keeper} here");
            return;
        }
        var shops = session.Shops;
        if (!shops.Add(place, file, owner, desc, flags, buy, sell, mask, seed)
            || shops.All.FirstOrDefault(s => s.OwnerGuid == keeper) is not { } store)
        {
            Said?.Invoke($"shop: {file} could not be built");
            return;
        }
        for (int i = 0; i < Math.Min(counts.Length, store.Lines.Count); i++)
        {
            var line = store.Lines[i];
            line.Remaining = counts[i];
            if (counts[i] == 0 && line.Treasure is not null && line.Copies != StockLine.Infinite) line.Treasure = null;
        }
        shops.Wire = this;
        // The trader is this machine's own character, whoever the place steers.
        store.Trader = null;
        if (!shops.Execute(place, owner, 0, null))
        {
            Said?.Invoke($"shop: {file} — a shop is already open");
            return;
        }
        if (me() is { } body) store.Trader = body;
        Open = store;
        Said?.Invoke($"shop: {store.Name} opened, {store.Lines.Count(l => l.Treasure is not null)} on the shelf");
    }

    /// <summary>
    /// The host's word (message 0x43 read; nothing without a shop open): kind 0, a
    /// purchase made — the line counted down here; kind 1, a sale made.
    /// </summary>
    internal void Answered(NetReader r, WorldMirror? mirror)
    {
        int kind = r.U8();
        if (Open is not { } store || mirror?.Place is not { Session: { } session } place) return;
        if (kind == 0)
        {
            int record = r.I32(), guid = r.I32();
            session.Shops.Bought(store, record);
            Said?.Invoke($"shop: bought {record} (thing {guid})");
        }
        else if (kind == 1)
        {
            int guid = r.I32();
            session.Shops.SoldThere(place, store);
            Said?.Invoke($"shop: sold {guid}");
        }
    }

    /// <summary>
    /// The host's purse for this machine's character (ours): its own cash, and — as this machine's
    /// pack pane and <see cref="Shops.CashOf"/> read a party member's from the session's purse — the
    /// session's too. A player's machine has no purse of its own; the host's is the word.
    /// </summary>
    internal void Purse(NetReader r, WorldMirror? mirror)
    {
        int guid = r.I32(), cash = r.I32();
        if (mirror?.Find(guid) is not { Actor: { } actor } body) return;
        actor.Cash = cash;
        if (!ReferenceEquals(body, me()) || mirror.Place.Session is not { } session) return;
        session.Gold = cash;
        if (session.Shops is { Active: { } store } shops && ReferenceEquals(store.Trader, body)) shops.Changed?.Invoke(store);
    }

    // ---- the pane's three, sent (IShopWire) ----

    /// <summary>A purchase by the shelf's record number (message 0x43 kind 0).</summary>
    public void Buy(int record) => send(new NetWriter(Msg.ShopDeal).U8(0).I32(record).ToArray());

    /// <summary>A sale by the item's GUID (kind 1).</summary>
    public void Sell(int itemGuid) => send(new NetWriter(Msg.ShopDeal).U8(1).I32(itemGuid).ToArray());

    /// <summary>The close (kind 2), sent as the pane goes.</summary>
    public void Close()
    {
        send(new NetWriter(Msg.ShopDeal).U8(2).ToArray());
        Said?.Invoke($"shop: {Open?.Name ?? "?"} closed");
        Open = null;
    }
}
