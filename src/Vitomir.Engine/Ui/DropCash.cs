// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Dropping money from the pack: the coin button, its input box and the purse.
namespace Vitomir.Engine.Ui;

using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

/// <summary>
/// Dropping money from the pack (and its rpg side).
/// The coin button under the pack's money box (`invGoldButton`) asks "How much cash would you like
/// to drop?" in the input box, and a whole number above 0 that the purse can cover goes on the
/// ground at the steered character's feet as a money bag — the same `GoldBag` the `dropcash`
/// console command makes — and comes out of the purse. Anything else does nothing, and
/// says nothing: 1.1 has no message for a number too large.
/// </summary>
public static class DropCash
{
    /// <summary>The input box's title (looked up by this key).</summary>
    public const string Prompt = "UI_INV_DROPCASH";

    /// <summary>The coin button's name in `inventoryPane.nui`.</summary>
    public const string Button = "invGoldButton";

    /// <summary>
    /// Whether a point of the pack is on the coin button — the test both the press and the release
    /// make. The `.nui` reader lowercases every line before it splits it (as the engine's does), so
    /// the name <see cref="InventoryPane.ButtonAt"/> hands back is `invgoldbutton`, and the test has
    /// to ignore case: an exact comparison with <see cref="Button"/> never matched, and the button
    /// was deaf in play.
    /// </summary>
    public static bool At(InventoryPane pane, int x, int y) =>
        string.Equals(pane.ButtonAt(x, y), Button, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The coin button's callback: with a steered character who has no money, nothing;
    /// otherwise the input box — titled <see cref="Prompt"/>, with OK and Cancel — whose field takes
    /// digits only (the C runtime's `isdigit`). 1.1 asks even with nobody steered,
    /// and then its OK finds nobody to drop from. The field keeps the edit's own limit, 511,
    /// because the box never sets one. Returns whether the box went up.
    /// </summary>
    public static bool Ask(FrontEnd menus, WorldThing? steered, Func<string, string?> text, Action<string>? told = null)
    {
        if (steered is not null && Shops.CashOf(steered) == 0)
        {
            told?.Invoke("the purse is empty, no box");
            return false;
        }
        return menus.Input(text(Prompt) ?? Prompt, "", Limit,
                           (result, typed) =>
                           {
                               bool moved = Answered(steered, result, typed);   // first: `told?.` would skip it
                               told?.Invoke(moved ? $"dropped {Scan(typed)} gold, {Shops.CashOf(steered!)} left"
                                                  : $"dropped nothing ({(result == 1 ? $"OK, \"{typed}\"" : "cancelled")})");
                           },
                           ok: true, cancel: true, accepts: EditAccepts.Digits);
    }

    /// <summary>The field's limit: the edit's own default (511), never changed here.</summary>
    public const int Limit = 511;

    /// <summary>
    /// The box's callback: OK (result 1) and a number `sscanf("%d")` reads that is
    /// above 0 drops that much from whoever is steered. Returns whether money moved.
    /// </summary>
    public static bool Answered(WorldThing? steered, int result, string typed)
    {
        if (result != 1 || Scan(typed) is not > 0 || steered is null) return false;
        return Drop(steered, Scan(typed)!.Value);
    }

    /// <summary>
    /// An amount the purse covers (<see cref="Shops.CashOf"/>) becomes a bag at
    /// <paramref name="who"/>'s feet, and only when the bag is made is it taken from the purse
    /// (the cash add with the amount negated — <see cref="Session.AddCash"/>). A player's machine in a
    /// network game sends the host the amount instead (`Net.NetArms`) and takes it from its
    /// own purse at once, bag or no bag; the host drops it through this same function.
    /// </summary>
    public static bool Drop(WorldThing who, int amount)
    {
        if (amount <= 0 || (uint)amount > (uint)Shops.CashOf(who)) return false;
        if (Net.NetArms.DropCash(who, amount)) return true;
        if (Bag(who, amount) is null) return false;
        who.Place?.Session?.AddCash(who, -amount);
        return true;
    }

    /// <summary>
    /// A `GoldBag` (looked up by that name) made, holding <paramref name="amount"/>,
    /// and put down **beside** <paramref name="at"/> through the placement a death's loot
    /// takes (the placement with distance 0 and flags 0xb, <see cref="Location.PlaceNear"/>) —
    /// not on the dropper's feet, which is where Vitomir's spawn put it until a comparison with 1.1
    /// library. The bag, or null when there is no place or no such template. **Which bag is the
    /// chronicle's**: 1.1 picks it at random from the templates its metafile's <c>CASH</c> line names
    /// (<see cref="ChronicleCash"/>) — a `GoldBag` or `goldCoins` in Prague, `cashUK` in
    /// London — and makes a `GoldBag` only when no chronicle is loaded or it named none. Until the
    /// A comparison with 1.1 this page said nothing filled that table; the metafile's reader does.
    /// </summary>
    public static WorldThing? Bag(WorldThing at, int amount)
    {
        if (at.Place is not { } place || place.Make(ChronicleCash.For(place)) is not { Item: { } item } bag) return null;
        item.Amount = amount;
        place.PlaceNear(bag, at, 0f, Rpg.Loot.PlaceFlags);
        return bag;
    }

    /// <summary>The money bag's template when the chronicle names none (looked up by name).</summary>
    public const string GoldBag = ChronicleCash.Fallback;

    /// <summary>
    /// `sscanf(text, "%d", &amp;n)` as the box's accept uses it: leading white space skipped, an optional
    /// sign, then at least one digit; the digits read are the number (wrapping past 32 bits as the
    /// C runtime's does) and anything after them is ignored. Null when nothing was read.
    /// </summary>
    public static int? Scan(string text)
    {
        int i = 0;
        while (i < text.Length && text[i] is ' ' or '\t' or '\n' or '\r' or '\v' or '\f') i++;
        bool minus = false;
        if (i < text.Length && text[i] is '+' or '-') minus = text[i++] == '-';
        int start = i;
        uint n = 0;
        while (i < text.Length && text[i] is >= '0' and <= '9') n = unchecked(n * 10 + (uint)(text[i++] - '0'));
        if (i == start) return null;
        return unchecked(minus ? -(int)n : (int)n);
    }
}
