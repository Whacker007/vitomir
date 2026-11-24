// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Two rollovers of the interface's own that were not written: an item icon's
// and a party row's, read.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// What the two description boxes say over an item icon or a party row: lines for the small box, lines
/// for the large one, whether the large one comes at once (the status bar's mode 2) or 1.5 s later
/// (mode 3), and what the hover is on, for that wait — the shape <c>RolloverPass.Lines</c> takes.
/// </summary>
public static class IconRollover
{
    /// <summary>
    /// **An item icon**, which is installed on every item's icon — the pack's grids, the
    /// vault, the shop's sell side and the quick-use belt alike: the item's own rollover
    /// (the name in the small box — red when the steered character cannot use it — and the detail
    /// block in the large one) in **mode 3**, the large box 1.5 s later, when the icon is inside the
    /// status bar's own pane, and **mode 2**, both at once,
    /// everywhere else. So a belt slot makes you wait and a grid does not.
    /// </summary>
    public static (IReadOnlyList<RolloverLine> Small, IReadOnlyList<RolloverLine> Large, bool LargeNow, object Key)? Item(
        Rpg.Item? item, bool onStatusBar, WorldThing? user, Strings? text, NedFile? effects)
    {
        if (item?.Thing is not { } thing || Rollover.Name(thing, text, user) is not { } name) return null;
        return ([name], Rollover.Detail(thing, text, effects), !onStatusBar, item);
    }

    /// <summary>
    /// **A party row** (the row hotspot's rollover): **mode 1**, the small box alone, with
    /// the member's name and then one line — `UI_STATUSIND_ROLLOVERHUMAN` (`Health: %h
    /// Mana: %b Faith: %f`) for a human (`ACTOR_TYPE` 2), `UI_STATUSIND_ROLLOVERKINDRED`
    /// (`Health: %h Blood: %b Frenzy: %f`) for anybody else — each number the float truncated
    /// toward zero: health, then mana and faith or blood and frenzy.
    /// Both lines in the small box's own colour.
    /// </summary>
    public static (IReadOnlyList<RolloverLine> Small, IReadOnlyList<RolloverLine> Large, bool LargeNow, object Key)? PartyRow(
        WorldThing? member, Strings? text)
    {
        if (member?.Actor is not { } who) return null;
        bool human = who.Type == ActorType.Human;
        int h = (int)who.Health;
        int b = (int)who.Stat[(int)(human ? ActorStat.Mana : ActorStat.Blood)];
        int f = (int)who.Stat[(int)(human ? ActorStat.Faith : ActorStat.Frenzy)];
        string key = human ? "UI_STATUSIND_ROLLOVERHUMAN" : "UI_STATUSIND_ROLLOVERKINDRED";
        string line = text is null ? $"Health: {h} {(human ? "Mana" : "Blood")}: {b} {(human ? "Faith" : "Frenzy")}: {f}"
                                   : Strings.Format(text.Find, key, $"%h{h}%b{b}%f{f}");
        return ([new RolloverLine(Rollover.NameOf(member, text), Rollover.White), new RolloverLine(line, Rollover.White)],
                [], true, ("row", member));
    }

    /// <summary>
    /// Whichever of the two the pointer is on: an icon in any open grid of <paramref name="hands"/>,
    /// a quick-use slot or a party row of <paramref name="bar"/> — or null.
    /// </summary>
    public static (IReadOnlyList<RolloverLine> Small, IReadOnlyList<RolloverLine> Large, bool LargeNow, object Key)? Under(
        InventoryHands? hands, StatusBar? bar, int x, int y, WorldThing? user, Strings? text, NedFile? effects)
    {
        if (hands is not null)
            foreach (var pane in hands.Panes)
                if (pane.At(x, y) is { } spot)
                    return spot.Item is { } item ? Item(item, onStatusBar: false, user, text, effects) : null;
        if (bar is null) return null;
        if (bar.QuickAt(x, y) is { } quick) return Item(quick, onStatusBar: true, user, text, effects);
        // The network bar's portrait: the steered character, a player's only.
        if (bar.At(x, y) is { } head && head.Name.Equals(NetRollovers.HeadHotspot, StringComparison.OrdinalIgnoreCase))
            return NetRollovers.Head(user, text);
        if (bar.At(x, y) is { } hit && hit.Name.StartsWith("statushotspot", StringComparison.OrdinalIgnoreCase)
            && hit.Row >= 0 && hit.Row < bar.PartyThings.Count)
            return PartyRow(bar.PartyThings[hit.Row], text);
        return null;
    }

    /// <summary>
    /// **When a rollover is written: on a change of hover, not every frame**. Each frame the
    /// UI picks the control under the pointer afresh (cleared and set again each frame),
    /// and the hover runs that control's rollover (its own virtual, which
    /// for a party row writes the row's lines) **only when it is not last frame's control**
    /// — so the lines are built as the pointer arrives and then stay, however it moves inside the
    /// control. The only other writer is the hover's refresh, which re-runs the hover for
    /// the events that call it: the thing under the pointer *in the world* (none
    /// while the pointer is on the interface) taking damage, an item being identified (`SetItemFlags`
    /// with 2), and the character sheet's, the discipline pane's and the pack's own actions.
    /// So a party row's `Health: … Blood: … Frenzy: …` line says what
    /// the member had when the pointer came onto the row: **feeding with the pointer resting on the
    /// portrait, the blood in it does not climb**; it is read again when the pointer leaves the row and
    /// comes back. Vitomir rebuilt the lines on every movement of the mouse, so the line followed the
    /// feed in jumps as the hand on the mouse happened to twitch — in play it would
    /// skip a second and update slowly, or even more than a second.
    ///
    /// Answers <paramref name="next"/> when the hover is on something else (its key differs), and
    /// <paramref name="now"/> — the lines as first written — when it is on the same thing. A caller
    /// standing for the refresh assigns the fresh lines outright instead.
    /// </summary>
    public static (IReadOnlyList<RolloverLine> Small, IReadOnlyList<RolloverLine> Large, bool LargeNow, object Key)? OnChange(
        (IReadOnlyList<RolloverLine> Small, IReadOnlyList<RolloverLine> Large, bool LargeNow, object Key)? now,
        (IReadOnlyList<RolloverLine> Small, IReadOnlyList<RolloverLine> Large, bool LargeNow, object Key)? next) =>
        now is { } was && next is { } fresh && Equals(was.Key, fresh.Key) ? now : next;
}
