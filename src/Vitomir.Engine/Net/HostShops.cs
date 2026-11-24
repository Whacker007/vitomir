// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The host's half of a shop opened for a player (the shop's network arms and
// messages 0x42 and 0x43). A script's `ExecuteBuySell` naming a player sends them the whole shop;
// their machine opens the pane, and its purchases, sales and close come back
// to be made here on their character, each answered to their machine.
// The shops the shipped chronicles open this way are Sorvena's (`CTD_SorvenaShop`), Hank's
// (`LOT_GUNH_Hank`) and Emelia's (`LOT_NEWM_Emelia`). Who gets what is 1.1's; the bytes are ours.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>What the host's arm of `ExecuteBuySell` did.</summary>
public enum ShopRoute
{
    /// <summary>The shopper is nobody's, or the host's own: the single-player way, on this machine's pane.</summary>
    Here,
    /// <summary>Sent to the shopper's machine; the native answers true (1.1 returns 1).</summary>
    Sent,
    /// <summary>No store for the keeper, or it is already trading with somebody: the native answers false.</summary>
    Refused,
}

public sealed class HostShops
{
    /// <summary>The most stock lines the shop message sends (`if (0x80 < uVar7) uVar7 = 0x80`).</summary>
    public const int LineLimit = 128;

    readonly GameServer server;

    /// <summary>Each player's open shop — 1.1 keeps it in the player's slot, set by `ExecuteBuySell` and cleared by the close.</summary>
    readonly Dictionary<NetPlayer, Store> open = [];

    /// <summary>The purse each player's machine was last told (ours).</summary>
    readonly Dictionary<NetPlayer, int> told = [];

    public HostShops(GameServer server)
    {
        this.server = server;
        // Ours: a player who leaves with a shop open closes it, as their Done would, so the keeper's
        // script hears `buysellended` and the store can trade again. 1.1's leaving is unread here.
        server.Departing += p => { if (open.ContainsKey(p)) Close(p); told.Remove(p); };
    }

    /// <summary>The store <paramref name="p"/> has open, or null.</summary>
    public Store? OpenFor(NetPlayer p) => open.GetValueOrDefault(p);

    Shops? Shops => server.Place?.Session?.Shops;

    /// <summary>
    /// `ExecuteBuySell(keeper, shopper, xflags)` on the host (net mode 2). No
    /// store for the keeper, or one somebody is already trading at, refuses. Then the slot
    /// table is walked for the shopper: a player's (slot 1 and above) is sent the shop and
    /// becomes its trader, with the script and the flags kept for the close — and the host opens no
    /// pane of its own. The host's own character, or nobody's, goes the single-player way.
    /// </summary>
    public ShopRoute Execute(WorldThing keeper, int shopperGuid, uint xflags, object? script)
    {
        if (Shops is not { } shops || shops.All.FirstOrDefault(s => s.OwnerGuid == keeper.Guid) is not { } store)
            return ShopRoute.Refused;
        if (store.Trader is not null) return ShopRoute.Refused;
        var p = shopperGuid == 0 ? null
            : server.Players.FirstOrDefault(x => !x.Host && x.Avatar?.Guid == shopperGuid && x.Avatar.InWorld);
        if (p?.Avatar is not { } body) return ShopRoute.Here;

        // The message is made before the store takes the call's flags: it carries the script's
        // `AddBuySell` flags, then `ExecuteBuySell` writes `xflags` over them.
        TellPurse(p, force: true);
        server.Reliable(p, Message(store, keeper.Guid));
        store.Script = script;
        store.Flags = xflags;
        store.Trader = body;
        open[p] = store;
        server.Note($"net: {p.Name} shops at {store.File} ({store.Lines.Count} lines)");
        return ShopRoute.Sent;
    }

    /// <summary>
    /// Message 0x42: the file and the description key (32 bytes each), the keeper's GUID,
    /// the seed, the flags, the two rates, the sell mask, and each of at most 128 lines'
    /// count as a signed byte.
    /// </summary>
    internal static byte[] Message(Store store, int keeper)
    {
        var w = new NetWriter(Msg.Shop).Str(Cap(store.File)).Str(Cap(store.DescriptionId)).I32(keeper)
            .U32(store.Seed).U32(store.Flags).F32(store.BuyRate).F32(store.SellRate).I32(store.SellMask);
        int n = Math.Min(store.Lines.Count, LineLimit);
        w.U8((byte)n);
        for (int i = 0; i < n; i++) w.U8((byte)store.Lines[i].Remaining);        // `(char)`
        return w.ToArray();
    }

    static string Cap(string s) => s.Length > 31 ? s[..31] : s;

    /// <summary>
    /// A player's message 0x43: taken only from a player with a shop open. Kind 0 buys the
    /// record through the ordinary purchase (which makes it) and names the
    /// thing bought to their machine; kind 1 sells the item through the ordinary sale
    /// (the script's `buysellquery` asked here) and names it back; kind 2 ends
    /// the store with <c>bAborted = false</c> and clears their slot's shop.
    /// </summary>
    internal void Deal(NetPlayer p, NetReader r)
    {
        int kind = r.U8();
        if (!open.TryGetValue(p, out var store) || server.Place is not { } place || Shops is not { } shops) return;
        switch (kind)
        {
            case 0:
            {
                int record = r.I32();
                var result = shops.Buy(place, store, record, out var bought);
                server.Note($"net: {p.Name} buys {record} at {store.File} — {result}");
                if (result != BuyResult.Bought || bought is null) return;
                TellPurse(p, force: true);
                server.Reliable(p, new NetWriter(Msg.ShopDeal).U8(0).I32(record).I32(bought.Guid).ToArray());
                // Ours: the pack goes at once rather than at the next sheet.
                if (p.Avatar is { } a) server.Reliable(p, GameServer.Sheet(a, server.NowMs));
                return;
            }
            case 1:
            {
                int guid = r.I32();
                if (place.Thing(guid) is not { } item || !shops.Sell(place, store, item))
                {
                    server.Note($"net: {p.Name} cannot sell {guid} at {store.File}");
                    return;
                }
                server.Note($"net: {p.Name} sells {item.Template} at {store.File}");
                TellPurse(p, force: true);
                server.Reliable(p, new NetWriter(Msg.ShopDeal).U8(1).I32(guid).ToArray());
                if (p.Avatar is { } a) server.Reliable(p, GameServer.Sheet(a, server.NowMs));
                return;
            }
            case 2:
                Close(p);
                return;
        }
    }

    void Close(NetPlayer p)
    {
        if (!open.Remove(p, out var store)) return;
        Shops?.End(store, aborted: false);
        server.Note($"net: {p.Name} closes {store.File}");
    }

    /// <summary>Every player's purse sent when it has changed since they were last told (ours; with each sheet).</summary>
    internal void Purses()
    {
        foreach (var p in server.Players.Where(p => !p.Host && p.Ready)) TellPurse(p, force: false);
    }

    void TellPurse(NetPlayer p, bool force)
    {
        if (p.Avatar is not { } body) return;
        int cash = Rpg.Shops.CashOf(body);
        if (!force && told.TryGetValue(p, out int was) && was == cash) return;
        told[p] = cash;
        server.Reliable(p, new NetWriter(Msg.Purse).I32(body.Guid).I32(cash).ToArray());
    }
}
