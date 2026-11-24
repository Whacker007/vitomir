// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Shops. The pane that shows one is `Ui.BuySellPane`; the script side is the two
// natives `AddBuySell` and `ExecuteBuySell`.
//
// A shop is not a place and not a character. It is a **record** the owner's script builds from a
// `.nbs` file every time the player talks to the shopkeeper, keyed by the shopkeeper's GUID, kept
// in one global list for the rest of the session, and written into every save. The engine's own
// words for the pieces: the store (0x6c bytes), its stock lines (0x18 bytes each), and on every line
// one treasure record — the item to be, which is what the shelf shows and what a purchase makes.
using System.Text;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>One line of a `.nbs` file, as the store keeps it (0x18 bytes).</summary>
public sealed class StockLine
{
    /// <summary>An infinite supply — the file's 0.</summary>
    public const int Infinite = -1;

    /// <summary>How many are left. Set to <see cref="Copies"/> each time the line is stocked.</summary>
    public int Remaining { get; set; }
    /// <summary>How many the file says, or <see cref="Infinite"/>.</summary>
    public int Copies { get; init; }
    /// <summary>
    /// The item on the shelf, or null for an empty line — sold out, or a roll that gave
    /// nothing. **One per line**: every copy of a line is made from the same record, so the three
    /// copies of an `R 201 3` line are the same rolled scroll.
    /// </summary>
    public TreasureRecord? Treasure { get; set; }
    /// <summary>`D` (a template by name) rather than `R` (a treasure class) — 1 or 2.</summary>
    public bool Direct { get; init; }
    /// <summary>A <c>D</c> line's template.</summary>
    public string Template { get; init; } = "";
    /// <summary>An <c>R</c> line's class.</summary>
    public int Class { get; init; }

    public override string ToString() =>
        $"{(Direct ? "D " + Template : "R " + Class)} {(Copies == Infinite ? "inf" : $"{Remaining}/{Copies}")}"
        + (Treasure is { } t ? $" -> {t.Template}" : " (empty)");
}

/// <summary>The store record (0x6c bytes).</summary>
public sealed class Store
{
    /// <summary>The shopkeeper's GUID, which is what finds the store again.</summary>
    public int OwnerGuid { get; init; }
    /// <summary>The `.nbs` it was built from. **Not saved**.</summary>
    public string File { get; init; } = "";
    /// <summary>`DESCRIPTIONID` and its text, which is what the pane's title says.</summary>
    public string DescriptionId { get; init; } = "";
    public string Name { get; set; } = "";
    /// <summary>
    /// `AddBuySell`'s flags until the pane opens, then **overwritten by
    /// `ExecuteBuySell`'s**. Only <see cref="Shops.WantFeedback"/> is ever tested.
    /// </summary>
    public uint Flags { get; set; }
    /// <summary>What the player pays against value and gets back; 1.25 and 0.8 in every shipped script.</summary>
    public float BuyRate { get; set; }
    public float SellRate { get; set; }
    /// <summary>Which `ITEM_SELLFLAGS` the shop will take.</summary>
    public int SellMask { get; set; }
    /// <summary>The generator's state when this store was stocked. Never read in single player.</summary>
    public uint Seed { get; set; }
    /// <summary>
    /// Who is trading — the character being steered when the pane opened, and after any
    /// switch — and null while the pane is shut, which is also the "already trading" test.
    /// </summary>
    public WorldThing? Trader { get; set; }
    /// <summary>The script that opened it, which is told when it closes.</summary>
    public object? Script { get; set; }
    /// <summary>The lines, in file order.</summary>
    public List<StockLine> Lines { get; } = [];

    public override string ToString() => $"{File} for {OwnerGuid} ({Lines.Count} lines)";
}

/// <summary>What a purchase came to, for the pane and the journal.</summary>
public enum BuyResult
{
    Bought, NoTrader, NoLine, CannotAfford, PackFull, Failed,
    /// <summary>On a player's machine: the checks passed and the purchase has gone to the host (the buy's network-client branch).</summary>
    Asked,
}

/// <summary>
/// Where a player's machine sends what its shop pane does: the purchase by the
/// shelf's record number (message 0x43 kind 0), the sale by the item's GUID (kind
/// 1) and the close (kind 2). The host makes them (`Net.HostShops`).
/// </summary>
public interface IShopWire
{
    void Buy(int record);
    void Sell(int itemGuid);
    void Close();
}

/// <summary>
/// Every store in the session and the one open.
/// </summary>
public sealed class Shops
{
    /// <summary>Where the `.nbs` files live: resource kind 0x13, registered as `Stores`.</summary>
    public const string Folder = "stores/";

    /// <summary>`BUYSELL_XFLAG_WANTFEEDBACK`: tell the script when the pane closes, and ask it before a sale.</summary>
    public const uint WantFeedback = 0x4;

    /// <summary>
    /// The price's two Manipulation terms, as 1.1 keeps them —
    /// doubles, applied in single precision.
    /// </summary>
    public const double Percent = 0.01, BuySlope = -0.6, BuyBase = 1.3, SellSlope = 0.6, SellBase = 0.7;

    readonly List<Store> stores = [];

    /// <summary>Every record a load brings back, newest first, and no shop open.</summary>
    internal void Restore(IEnumerable<Store> list)
    {
        stores.Clear();
        stores.AddRange(list);
        Active = null;
    }

    /// <summary>Newest first — each new store is put on the front of the list.</summary>
    public IReadOnlyList<Store> All => stores;

    /// <summary>The store whose pane is open, or null.</summary>
    public Store? Active { get; private set; }

    /// <summary>The wall clock the restock seeds from; a test fixes it.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    /// <summary>A string by id, for the store's name. Null answers the id.</summary>
    public Func<string, string?>? Text { get; set; }

    /// <summary>
    /// Close every modal thing before the pane goes up: the look camera, the
    /// advancement screen, the vault, the subtitles, the map, the escape menu, the save menu, the
    /// console. The front end's to do; this only asks.
    /// </summary>
    public Action? CloseModals { get; set; }
    /// <summary>The pane opens. False when it could not.</summary>
    public Func<Store, bool>? Opened { get; set; }
    /// <summary>The pane closes.</summary>
    public Action<Store>? Closed { get; set; }
    /// <summary>The shelf and the pack want redrawing.</summary>
    public Action<Store>? Changed { get; set; }
    /// <summary>`buysellended(boolean bAborted)` on the script, when it asked for feedback.</summary>
    public Action<Store, bool>? Ended { get; set; }
    /// <summary>`buysellquery(false, itemGuid)` before a sale; no shipped script has one, so the answer is yes.</summary>
    public Func<Store, WorldThing, bool>? Query { get; set; }
    /// <summary>
    /// On a player's machine in a network game, the host: a purchase, a sale and the close go there
    /// rather than being made here. Null everywhere else.
    /// </summary>
    public IShopWire? Wire { get; set; }

    // --- building one --------------------------------------------------------------------

    /// <summary>
    /// `AddBuySell` (the native). True when there is a store for the owner
    /// afterwards.
    ///
    /// **The dedupe looks at one record only**: the first in the list with this owner's GUID. If it
    /// was built from the same file (compared without case) its rates, mask and flags are refreshed
    /// and that is all — the stock is untouched. Anything else — a first visit, a different file,
    /// or a record loaded from a save, which carries no file name — builds a **new** store, stocks
    /// it from scratch and puts it in front. That is how Unorna's two lists coexist, and why every
    /// first visit after a load re-rolls the shelf.
    /// </summary>
    /// <param name="seed">
    /// The host's seed, when a player's machine builds the shop the host has sent it (the store is made
    /// with its eighth argument 1 and the seed as its ninth): the generator is set to it
    /// rather than to the clock, so the shelf rolls as the host's did, and the store's own seed is not
    /// written. Null for a script's `AddBuySell`.
    /// </param>
    public bool Add(Location place, string file, WorldThing owner, string descriptionId, uint flags,
                    float buyRate, float sellRate, int sellMask, uint? seed = null)
    {
        // Both rates above zero, NaN refused.
        if (!(buyRate > 0f) || !(sellRate > 0f)) return false;
        file = Cut(file);
        descriptionId = Cut(descriptionId);
        if (stores.FirstOrDefault(s => s.OwnerGuid == owner.Guid) is { } first
            && first.File.Equals(file, StringComparison.OrdinalIgnoreCase))
        {
            first.BuyRate = buyRate;
            first.SellRate = sellRate;
            first.SellMask = sellMask;
            first.Flags = flags;
            return true;
        }
        if (place.Files is not { } files || !files.TryRead(Folder + file, out var data)) return false;
        var store = new Store
        {
            OwnerGuid = owner.Guid, File = file, DescriptionId = descriptionId,
            Name = Text?.Invoke(descriptionId) ?? descriptionId,
            Flags = flags, BuyRate = buyRate, SellRate = sellRate, SellMask = sellMask,
        };
        foreach (var line in Parse(data, name => place.Templates?.Has(name) == true))
            store.Lines.Add(line);
        // The one generator, reseeded from the clock — which also moves every other
        // roll that shares it.
        if (place.Treasure is { } table)
        {
            if (seed is { } given) table.Random.State = given;
            else
            {
                table.Random.State = Ai.Chance.TreasureClock(Clock());   // the clock, or a repeat run's seed
                store.Seed = table.Random.State;
            }
            Fill(store, table);
        }
        stores.Insert(0, store);
        Diagnostics.Journal.Current?.Write(place.Milliseconds, Diagnostics.JournalKind.Shop,
            ("event", "stocked"), ("file", file), ("owner", Diagnostics.Watch.Name(owner)),
            ("lines", store.Lines.Count), ("on shelf", store.Lines.Count(l => l.Treasure is not null)),
            ("stores", stores.Count));
        return true;
    }

    /// <summary>`strncpy(…, 0x20)` and a NUL at 31: every name the native takes is cut to 31 characters.</summary>
    static string Cut(string s) => s.Length > 31 ? s[..31] : s;

    /// <summary>
    /// The `.nbs` parse, line by line: a line starting `;`, `#` or `//` is a
    /// comment; the rest is lower-cased; **a line starting with any white space is skipped**, blank
    /// ones included (`strspn` of <c>" \r\t\n"</c>); then <c>%s %s %u</c> must give all three. A
    /// type whose first letter is <c>d</c> is a template, which must exist; **any other letter** is a
    /// class number, which must not be above 256. Copies 0 means infinite.
    /// </summary>
    public static IEnumerable<StockLine> Parse(byte[] data, Func<string, bool> templateExists)
    {
        foreach (var raw in Encoding.Latin1.GetString(data).Split('\n'))
        {
            var line = raw.Length > 510 ? raw[..510] : raw;      // fgets(buf, 0x1ff)
            if (line.Length == 0) continue;
            if (line[0] is ';' or '#' || line.StartsWith("//", StringComparison.Ordinal)) continue;
            if (line[0] is ' ' or '\r' or '\t' or '\n') continue;
            var f = line.ToLowerInvariant().Split((char[])[' ', '\t', '\r', '\n', '\v', '\f'],
                                                  StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 3 || !TryUnsigned(f[2], out uint count)) continue;
            int copies = count == 0 ? StockLine.Infinite : (int)count;
            if (f[0][0] == 'd')
            {
                if (!templateExists(f[1])) continue;              // An unknown template is dropped
                yield return new StockLine { Direct = true, Template = f[1], Copies = copies };
            }
            else
            {
                int cls = Atoi(f[1]);
                if ((uint)cls > 0x100) continue;
                yield return new StockLine { Direct = false, Class = cls, Copies = copies };
            }
        }
    }

    /// <summary>
    /// Stocking, and the restock the one native that asks for it runs: every line with
    /// nothing on it gets its record — the template's, or one roll of its class — marked
    /// <see cref="ItemFlags.Identified"/>, numbered with the line's index, and the line's count put
    /// back to its copies. A line that still has stock is not touched, however much of it has sold.
    /// </summary>
    public static void Fill(Store store, Treasure table)
    {
        for (int i = 0; i < store.Lines.Count; i++)
        {
            var line = store.Lines[i];
            if (line.Treasure is not null) continue;
            var t = line.Direct ? table.Build(line.Template) : table.Roll(line.Class);
            line.Treasure = t;
            if (t is not null)
            {
                t.Item.Flags |= ItemFlags.Identified;
                t.Id = i;                                           // the slot, written over the record's number
            }
            if (line.Copies != StockLine.Infinite) line.Remaining = line.Copies;
        }
    }

    /// <summary>`RefillAllBuySells`: <see cref="Fill"/> on every store. No shipped script calls it.</summary>
    public void Refill(Treasure table)
    {
        foreach (var s in stores) Fill(s, table);
    }

    // --- opening and closing --------------------------------------------------------------------------

    /// <summary>
    /// `ExecuteBuySell` (the native): open the owner's store. The shopper the
    /// script names is **not used** in single player — the trader is whoever is being steered.
    /// False when there is no store, when it is already open, or when another is.
    /// </summary>
    public bool Execute(Location place, WorldThing owner, uint xflags, object? script)
    {
        if (stores.FirstOrDefault(s => s.OwnerGuid == owner.Guid) is not { } store) return false;
        if (store.Trader is not null || Active is not null) return false;
        CloseModals?.Invoke();
        Active = store;
        store.Flags = xflags;
        store.Script = script;
        store.Trader = place.Steered;
        Diagnostics.Journal.Current?.Write(place.Milliseconds, Diagnostics.JournalKind.Shop,
            ("event", "opened"), ("file", store.File),
            ("trader", store.Trader is null ? null : Diagnostics.Watch.Name(store.Trader)));
        // The pane's show failing leaves the store open with nobody able to close it; in single player the
        // pane's own "already open" flag mirrors `Active`, so it cannot fail.
        return Opened?.Invoke(store) ?? true;
    }

    /// <summary>
    /// The end — the Done button, Escape, and the modal close. The script is told
    /// **before** the pane goes, and always with <c>bAborted = true</c>: the other end
    /// (which passes false) has no callers.
    /// </summary>
    public void End(bool aborted = true)
    {
        if (Active is not { } store) return;
        End(store, aborted);
    }

    /// <summary>
    /// One store's end (the body both ends share): the script told when it asked for
    /// feedback, the trader let go, and — when it is this machine's open pane — the pane closed and,
    /// on a player's machine, the close sent to the host. The host ends a player's store
    /// this way when their close arrives, with <c>bAborted = false</c> (kind 2).
    /// </summary>
    public void End(Store store, bool aborted)
    {
        if (store.Script is not null && (store.Flags & WantFeedback) != 0) Ended?.Invoke(store, aborted);
        store.Trader = null;
        if (!ReferenceEquals(Active, store)) return;
        Closed?.Invoke(store);
        Wire?.Close();
        Active = null;
        Diagnostics.Journal.Current?.Write(0, Diagnostics.JournalKind.Shop, ("event", "closed"), ("file", store.File));
    }

    /// <summary>
    /// A different party member takes over while the pane is open (from the UI refresh
    /// They become the trader and the shelf is priced again with their Manipulation.
    /// </summary>
    public void SwitchTrader(WorldThing? steered)
    {
        if (Active is not { } store || steered is null) return;
        store.Trader = steered;
        Changed?.Invoke(store);
    }

    // --- money ----------------------------------------------------------------------------------------

    /// <summary>
    /// What <paramref name="who"/> has to spend: the party's purse for a party member,
    /// the character's own cash for anyone else — the mirror of <see cref="Session.AddCash"/>. **In a
    /// network game every character spends its own**: the party's purse is read
    /// only outside a network game.
    /// </summary>
    public static int CashOf(WorldThing who)
    {
        if (who.Actor is not { } actor) return 0;
        if (who.Type == ThingType.Player && actor.Has(Actor.Flag.Party) && who.Place?.Session is { } session
            && Net.NetOrders.Mode(who.Place) == Net.NetMode.Single)
            return session.Gold;
        return actor.Cash;
    }

    /// <summary>Single precision, as 1.1 computes: every result rounded to a float as it is made.</summary>
    static float P(double x) => (float)x;

    /// <summary>Truncates toward zero.</summary>
    static int Truncate(float x) => (int)x;

    /// <summary>`max(1, v)` as the engine does it — unsigned, so only a 0 becomes 1.</summary>
    static int AtLeastOne(int v) => Rules.GameRules.Current.Items.PriceFloor is var floor && (uint)v < (uint)floor ? floor : v;

    /// <summary>
    /// What one effect adds to a price: its definition's permanent cost times the
    /// record's value, or — for an effect with a duration — its duration cost times value, time
    /// and the item's amount. The value is the record's **own** value, which is 0 for an effect
    /// given by level: only an effect given by value moves the price.
    /// </summary>
    public static int EffectCost(ItemEffect e, int amount, NedFile? effects)
    {
        if (!Rules.GameRules.Current.Items.EffectCostFormula || effects?[e.Name] is not { } def) return 0;
        float value = e.Value ?? 0f;
        if (e.TimeMs != 0)
            return Truncate(P(P(P((double)def.DurationCost * value) * (double)e.TimeMs) * (double)amount));
        return Truncate(P((double)def.PermanentCost * value));
    }

    /// <summary>
    /// A shelf item's value: its base cost, plus what its effects add unless it is
    /// flagged precomputed or unique cost, never below 1 once the effects are counted.
    /// </summary>
    public static int Value(TreasureRecord t, NedFile? effects)
    {
        var item = t.Item;
        int v = item.BaseCost;
        if ((item.Flags & (ItemFlags.PrecomputedCost | ItemFlags.UniqueCost)) != 0) return v;
        foreach (var e in item.Effects) v += EffectCost(e, item.Amount, effects);
        return v > 1 ? v : 1;
    }

    /// <summary>
    /// An owned item's value: the same, except that **an unidentified item's effects
    /// add nothing** — its value is its base cost.
    /// </summary>
    public static int Value(Item item, NedFile? effects)
    {
        int v = item.BaseCost;
        if ((item.Flags & (ItemFlags.PrecomputedCost | ItemFlags.UniqueCost)) != 0
            || (item.Flags & ItemFlags.Identified) == 0 && Rules.GameRules.Current.Items.UnidentifiedEffectsWorthless) return v;
        foreach (var e in item.Effects) v += EffectCost(e, item.Amount, effects);
        return v > 1 ? v : 1;
    }

    /// <summary>The trader's Manipulation, the one stat the price reads.</summary>
    static float Manipulation(WorldThing? trader) =>
        trader?.Actor is { } a ? a.Stat[(int)ActorStat.Manipulation] : 0f;

    /// <summary>
    /// What the player pays: value, times the store's buy rate unless the item is
    /// <see cref="ItemFlags.UniqueCost"/>, times <c>1.3 − 0.006 × Manipulation</c>, each step
    /// truncated, and never below 1. No trader, no price.
    /// </summary>
    public static int BuyPrice(Store store, TreasureRecord t, NedFile? effects)
    {
        if (store.Trader is null) return 0;
        int v = Value(t, effects);
        var rules = Rules.GameRules.Current.Items;
        if ((t.Item.Flags & ItemFlags.UniqueCost) == 0 || !rules.UniqueCostSkipsBuyRate) v = Truncate(P((double)v * store.BuyRate));
        float m = Manipulation(store.Trader);
        var buy = rules.BuyPriceFactor;            // base, slope, percent
        float factor = P(P(P((double)m * buy[2]) * buy[1]) + buy[0]);
        return AtLeastOne(Truncate(P((double)factor * v)));
    }

    /// <summary>
    /// What the player gets (and inlined in the sale): value times the store's sell rate,
    /// **a quarter of that when the item is not identified**, times <c>0.7 + 0.006 ×
    /// Manipulation</c>, each step truncated, never below 1. No unique-cost exemption on this side.
    /// </summary>
    public static int SellPrice(Store store, Item item, NedFile? effects)
    {
        if (store.Trader is null) return 0;
        int v = Truncate(P((double)Value(item, effects) * store.SellRate));
        var rules = Rules.GameRules.Current.Items;
        if ((item.Flags & ItemFlags.Identified) == 0) v = rules.UnidentifiedSellShare == 4 ? v >> 2 : v / rules.UnidentifiedSellShare;
        float m = Manipulation(store.Trader);
        var sell = rules.SellPriceFactor;          // base, slope, percent
        float factor = P(P(P((double)m * sell[2]) * sell[1]) + sell[0]);
        return AtLeastOne(Truncate(P((double)factor * v)));
    }

    // --- buying ------------------------------------------------------------------

    /// <summary>
    /// Buys the shelf item numbered <paramref name="slot"/>. The checks and their noises are the
    /// engine's, in its order:
    /// <list type="number">
    /// <item>too dear — the trader says <c>vox_cannotafford</c> if free to speak, and the interface
    /// plays **`AFTERPURCHASE`** (DA or MD by the chronicle);</item>
    /// <item>otherwise the interface plays **`CANTBUY`** — on the way to a *successful* purchase.
    /// The two look swapped in 1.1 and are reproduced as they are;</item>
    /// <item>no room in the pack (10 x 4, first fit) — <c>vox_inventoryfull</c>, and nothing is
    /// taken;</item>
    /// <item>then the item is made, marked identified, put in the pack, the line counted down and
    /// emptied at zero, and the price taken from the purse.</item>
    /// </list>
    /// Nothing merges with what the trader already has, and nothing the player sells ever joins the
    /// shelf.
    /// </summary>
    public BuyResult Buy(Location place, Store store, int slot) => Buy(place, store, slot, out _);

    /// <summary><see cref="Buy(Location, Store, int)"/>, answering the thing bought — which the host names to the buyer's machine.</summary>
    public BuyResult Buy(Location place, Store store, int slot, out WorldThing? bought)
    {
        bought = null;
        var line = store.Lines.FirstOrDefault(l => l.Treasure is { } t && t.Id == slot);
        if (store.Trader is not { Actor: { } actor } trader) return BuyResult.NoTrader;
        if (line?.Treasure is not { } treasure) return BuyResult.NoLine;
        var effects = place.Effects?.File;
        bool modern = place.Session?.ModernDay == true;
        int price = BuyPrice(store, treasure, effects);
        if ((uint)price > (uint)CashOf(trader))
        {
            if (Audio.Noise.CanSpeak(trader)) Audio.Noise.Foley(trader, Audio.FoleyEvent.VoxCannotAfford);
            Audio.Noise.Ui(place, modern ? Audio.FoleyEvent.AfterPurchaseMd : Audio.FoleyEvent.AfterPurchaseDa);
            Log(place, "refused", treasure, price, "cannot afford");
            return BuyResult.CannotAfford;
        }
        Audio.Noise.Ui(place, Audio.FoleyEvent.CantBuy);
        if (treasure.Item.Icon.Length > 0 && !Fits(actor, treasure.Item))
        {
            Audio.Noise.Foley(trader, Audio.FoleyEvent.VoxInventoryFull);
            Log(place, "refused", treasure, price, "pack full");
            return BuyResult.PackFull;
        }
        // A player's machine asks the host and makes nothing itself until it answers.
        if (Wire is { } wire)
        {
            wire.Buy(slot);
            return BuyResult.Asked;
        }

        // The purchase proper.
        if (Treasure.Make(place, treasure) is not { Item: { } item } thing) return BuyResult.Failed;
        item.Flags |= ItemFlags.Identified;
        if (!Stow.Place(actor, item, Slot.Inventory))
        {
            Audio.Noise.Foley(trader, Audio.FoleyEvent.VoxInventoryFull);
            place.Remove(thing, drop: false);
            Log(place, "refused", treasure, price, "pack full");
            return BuyResult.PackFull;
        }
        actor.Inventory.Add(item);
        thing.Owner = trader;
        if (line.Copies != StockLine.Infinite && --line.Remaining < 1) line.Treasure = null;
        place.Session?.AddCash(trader, -price);
        Log(place, "bought", treasure, price, null);
        if (ReferenceEquals(Active, store)) Changed?.Invoke(store);
        bought = thing;
        return BuyResult.Bought;
    }

    /// <summary>
    /// The host's word that a purchase went through, on the buyer's machine (kind 0 into
    /// The shelf's line counted down and emptied at zero, as the host's was. **Ours**: the
    /// item itself comes in the host's next sheet and the purse as the host has it, rather than being
    /// made and paid for here.
    /// </summary>
    public void Bought(Store store, int slot)
    {
        if (store.Lines.FirstOrDefault(l => l.Treasure is { } t && t.Id == slot) is not { } line) return;
        if (line.Copies != StockLine.Infinite && --line.Remaining < 1) line.Treasure = null;
        if (ReferenceEquals(Active, store)) Changed?.Invoke(store);
    }

    /// <summary>
    /// The host's word that a sale went through, on the seller's machine (kind 1 into
    /// `AFTERSELL` again (the click played it once) and the pane redrawn. The item goes in
    /// the host's next sheet.
    /// </summary>
    public void SoldThere(Location place, Store store)
    {
        Audio.Noise.Ui(place, place.Session?.ModernDay == true ? Audio.FoleyEvent.AfterSellMd : Audio.FoleyEvent.AfterSellDa);
        if (ReferenceEquals(Active, store)) Changed?.Invoke(store);
    }

    /// <summary>Whether the pack has a place for it, asked without moving anything.</summary>
    static bool Fits(Actor actor, Item item)
    {
        var (location, cell) = (item.Location, item.Cell);
        bool fits = Stow.Place(actor, item, Slot.Inventory);
        (item.Location, item.Cell) = (location, cell);
        return fits;
    }

    // --- selling ---------------------------------------------------------------------------

    /// <summary>
    /// Whether the shop will look at <paramref name="item"/> at all — the pane's own test when an
    /// icon is clicked: not <see cref="ItemFlags.NoSell"/>, and one of its sell flags
    /// in the store's mask. **The pane does not exempt a zero mask; the sale does** — so a shop with
    /// a mask of 0 refuses every click.
    /// </summary>
    public static bool Takes(Store store, Item item) =>
        (item.Flags & ItemFlags.NoSell) == 0 && (item.SellFlags & store.SellMask) != 0;

    /// <summary>
    /// Sells <paramref name="itemThing"/>, which must be the trader's own. What happens is the
    /// engine's: the item comes off (a worn one is taken off first), the price goes into the purse,
    /// and the item is **destroyed** — not put on the shelf. Then `AFTERSELL`, DA or MD.
    /// </summary>
    public bool Sell(Location place, Store store, WorldThing itemThing)
    {
        if (store.Trader is not { Actor: { } actor } trader || itemThing.Item is not { } item) return false;
        if (itemThing.Type is not (ThingType.Item or ThingType.Weapon or ThingType.Armor)) return false;
        if (!ReferenceEquals(itemThing.Owner ?? itemThing.Holder, trader)) return false;
        if ((item.Flags & ItemFlags.NoSell) != 0) return false;
        if (store.SellMask != 0 && (item.SellFlags & store.SellMask) == 0) return false;
        // A player's machine sends the sale and plays `AFTERSELL` at once; the host asks the script
        // and makes it (the sale's network-client branch).
        if (Wire is { } wire)
        {
            wire.Sell(itemThing.Guid);
            Audio.Noise.Ui(place, place.Session?.ModernDay == true ? Audio.FoleyEvent.AfterSellMd : Audio.FoleyEvent.AfterSellDa);
            return true;
        }
        if ((store.Flags & WantFeedback) != 0 && store.Script is not null
            && Query is { } ask && !ask(store, itemThing)) return false;

        // Out of the trader's keeping through the release with its sound (Ground.Release) — a
        // weapon swap in force ended, nothing out of a changed shape, a worn item taken off first,
        // whose `unworn` may keep it on. A refusal ends the sale there: nothing
        // paid, no sound, answered true. (A comparison with 1.1; Vitomir took it off whatever `unworn` said,
        // and sold out of a changed shape.)
        if (!Ground.Release(itemThing, item, trader, place.ItemScripts)) return true;
        item.Location = Slot.None;

        int price = SellPrice(store, item, place.Effects?.File);
        place.Session?.AddCash(trader, price);
        item.Destroyed = true;
        place.Remove(itemThing, drop: false);
        Audio.Noise.Ui(place, place.Session?.ModernDay == true ? Audio.FoleyEvent.AfterSellMd : Audio.FoleyEvent.AfterSellDa);
        Diagnostics.Journal.Current?.Write(place.Milliseconds, Diagnostics.JournalKind.Shop,
            ("event", "sold"), ("item", itemThing.Template), ("price", price), ("file", store.File));
        return true;
    }

    static void Log(Location place, string what, TreasureRecord t, int price, string? why) =>
        Diagnostics.Journal.Current?.Write(place.Milliseconds, Diagnostics.JournalKind.Shop,
            ("event", what), ("item", t.Template), ("price", price), ("why", why));

    static int Atoi(string s)
    {
        int i = 0, sign = 1;
        long v = 0;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) { if (s[i] == '-') sign = -1; i++; }
        while (i < s.Length && char.IsAsciiDigit(s[i])) { v = v * 10 + (s[i] - '0'); if (v > int.MaxValue) break; i++; }
        return (int)(sign * v);
    }

    /// <summary><c>%u</c>: leading digits, at least one.</summary>
    static bool TryUnsigned(string s, out uint value)
    {
        value = 0;
        int i = 0;
        if (i < s.Length && s[i] == '+') i++;
        int start = i;
        ulong v = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i])) { v = v * 10 + (uint)(s[i] - '0'); i++; }
        if (i == start) return false;
        value = (uint)v;
        return true;
    }
}
