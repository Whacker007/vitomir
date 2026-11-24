// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Two rollovers of a network game's own panes, read (a comparison with 1.1's gaps 1 and 2): the network
// status bar's portrait and
// the Storyteller's object list.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>What the small description box says over the network status bar's portrait and the Storyteller's object list.</summary>
public static class NetRollovers
{
    /// <summary>The network status bar's portrait hotspot (`statusBarMP.nui`: `sbHeadHotspot`, 80 × 88 at 9,32).</summary>
    public const string HeadHotspot = "sbHeadHotspot";

    /// <summary>
    /// **The network status bar's portrait** (installed on `sbHeadHotspot`): the steered
    /// character (a player's own character, or the host's steered one), **only when it is
    /// a player's character** (type 7), in **mode 1** (the small box alone): its name (in a
    /// network game a player's character's is its player's name, given at the join) and
    /// the party row's line (<see cref="IconRollover.PartyRow"/>: `UI_STATUSIND_ROLLOVERHUMAN` for a human,
    /// `UI_STATUSIND_ROLLOVERKINDRED` otherwise, each number truncated). Anybody else steered — the Storyteller's
    /// possessed townsman, say — gets no box (1.1 writes nothing). Earlier, Vitomir's network bar
    /// described nothing there.
    /// </summary>
    public static (IReadOnlyList<RolloverLine> Small, IReadOnlyList<RolloverLine> Large, bool LargeNow, object Key)? Head(
        WorldThing? steered, Strings? text)
    {
        if (steered is not { Type: ThingType.Player } || IconRollover.PartyRow(steered, text) is not { } row) return null;
        // The name in a network game: a player's character by its player's name (set at the join),
        // when it has one, before its description.
        var small = row.Small.ToList();
        if (steered.Actor is { Name.Length: > 0 } who) small[0] = small[0] with { Text = who.Name };
        return (small, row.Large, row.LargeNow, ("head", steered));
    }

    /// <summary>
    /// What a row of the Storyteller's object list names (the entry made for each template on the chronicle's
    /// load): the template's description (template key 82, looked up in the string table — a key it does not
    /// have is shown as itself) when it has a non-empty one, else the template's own name.
    /// </summary>
    public static string TemplateLine(string template, TemplateSet? templates, Func<string, string?> find)
    {
        if (templates is not null && templates.Has(template)
            && templates.Resolve(template).TryGetValue(Tags.DescriptionId, out var d) && d.AsString() is { Length: > 0 } key
            && (find(key) ?? key) is { Length: > 0 } said)
            return said;
        return template;
    }

    /// <summary>
    /// **The Storyteller's object list's hover** (the list's own rollover, run as the pointer comes onto it;
    /// and the list's hover hook, run as the hovered row changes): with a row hovered (the
    /// row's entry), **mode 1** — the small box alone — with <see cref="TemplateLine"/> of its template; with none, the
    /// boxes taken down (here null). <paramref name="rows"/> are the list's templates, a row's data its
    /// index. Earlier, Vitomir's object pane described nothing on hover.
    /// </summary>
    public static (IReadOnlyList<RolloverLine> Small, IReadOnlyList<RolloverLine> Large, bool LargeNow, object Key)? Object(
        MenuList list, IReadOnlyList<string> rows, TemplateSet? templates, Func<string, string?> find)
    {
        int at = list.Hovered >= 0 && list.Hovered < list.Rows.Count ? list.Rows[list.Hovered].Data : -1;
        if (at < 0 || at >= rows.Count) return null;
        return ([new RolloverLine(TemplateLine(rows[at], templates, find), Rollover.White)], [], true, ("stobject", rows[at]));
    }
}
