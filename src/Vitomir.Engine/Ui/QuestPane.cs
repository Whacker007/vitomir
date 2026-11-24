// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The quest pane, as state rather than pixels: which eight lines it shows, which of
// them carry an icon, and what the description boxes say when the pointer rests on one.
using Vitomir.Engine.Rpg;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>One of the pane's eight rows.</summary>
/// <param name="Text">What `questText<i>n</i>` says; empty for an unused row.</param>
/// <param name="Icon">The texture `questIcon<i>n</i>` shows, or null for none.</param>
/// <param name="Quest">The quest the row is, when it is a quest's own line.</param>
/// <param name="Item">The item the row is, when it is one of a quest's parts.</param>
public readonly record struct QuestRow(string Text, string? Icon, Quest? Quest, QuestItem? Item);

/// <summary>
/// `ui/questPane.nui` — a **left**-hand pane (<c>visGroup=1</c>, shared with the character sheet
/// and the vault, so opening one closes the others) of eight rows, each a text box and an icon.
///
/// <para>The refresh fills the rows from the top: each quest in slot order on a line of
/// its own, then each of its items that is not hidden, and <b>it stops at eight</b>. There is no
/// scrolling — the pane has no bar and the function has no offset — so a ninth line of the log is
/// never shown. An icon is drawn only once the quest (or item) is done and its experience given
/// (flag 4), and it is always <b>the quest's</b> icon: <c>QuestCheck.tga</c> for most, the object
/// itself for a few (the Reliquary of St. John, Lucretia's heart).</para>
/// </summary>
public sealed class QuestPane(NuiPane pane)
{
    public const string PaneName = "questPane";

    /// <summary>How many rows the pane declares (<c>MAX_QUEST_ITEMS</c>).</summary>
    public const int RowCount = 8;

    /// <summary>The colour a completed quest's name is written in in the description box.</summary>
    public const uint DoneColour = 0xFF317FF4u;

    public NuiPane Pane { get; } = pane;

    /// <summary>The log it shows.</summary>
    public QuestLog? Log { get; set; }

    /// <summary>The eight rows, as the refresh would leave them.</summary>
    public QuestRow[] Rows()
    {
        var rows = Enumerable.Repeat(new QuestRow("", null, null, null), RowCount).ToArray();
        if (Log is null) return rows;
        int at = 0;
        foreach (var quest in Log.All)
        {
            rows[at++] = new QuestRow(quest.Short, Shown(quest.Flags, quest), quest, null);
            if (at == RowCount) return rows;
            foreach (var item in quest.Items)
            {
                if (item.Hidden) continue;
                rows[at++] = new QuestRow(item.Short, Shown(item.Flags, quest), null, item);
                if (at == RowCount) return rows;
            }
        }
        return rows;
    }

    static string? Shown(uint flags, Quest quest) =>
        (flags & QuestLog.Rewarded) != 0 && quest.IconLoaded && quest.Icon.Length > 0 ? quest.Icon : null;

    /// <summary>The row whose text box or icon is under a point in the pane's 640x480 space, or −1.</summary>
    public int RowAt(float x, float y)
    {
        float px = Pane.Pos.Length > 1 ? Pane.Pos[0] : 0, py = Pane.Pos.Length > 1 ? Pane.Pos[1] : 0;
        for (int row = 0; row < RowCount; row++)
            foreach (var name in new[] { $"questtext{row}", $"questicon{row}" })
                if (Element(name) is { Size.Length: > 1, Pos.Length: > 1 } e
                    && x >= px + e.Pos[0] && x <= px + e.Pos[0] + e.Size[0]
                    && y >= py + e.Pos[1] && y <= py + e.Pos[1] + e.Size[1])
                    return row;
        return -1;
    }

    /// <summary>Whether a point is on the pane's artwork at all — which is what swallows a click.</summary>
    public bool Covers(float x, float y)
    {
        float px = Pane.Pos.Length > 1 ? Pane.Pos[0] : 0, py = Pane.Pos.Length > 1 ? Pane.Pos[1] : 0;
        return Pane.Quads.Any(q => x >= px + q.Pos[0] && x <= px + q.Pos[0] + q.Size[0]
                                   && y >= py + q.Pos[1] && y <= py + q.Pos[1] + q.Size[1]);
    }

    public NuiElement? Element(string name) =>
        Pane.Elements.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What the description boxes say while the pointer rests on a row: both boxes at
    /// once, with no wait (the status bar's mode 2) — the name in the small one, in blue
    /// once it is done, and the description in the large one. An empty row
    /// takes both boxes down.
    /// </summary>
    public (RolloverLine Small, RolloverLine Large)? Describe(int row)
    {
        if (row < 0 || row >= RowCount) return null;
        var r = Rows()[row];
        if (r.Quest is { } q)
            return (new RolloverLine(q.Short, q.Complete ? DoneColour : Rollover.White), new RolloverLine(q.Long, Rollover.White));
        if (r.Item is { } i)
            return (new RolloverLine(i.Short, i.Complete ? DoneColour : Rollover.White), new RolloverLine(i.Long, Rollover.White));
        return null;
    }
}
