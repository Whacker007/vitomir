// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The discipline pane, as state rather than pixels: a strip of tabs, one per
// discipline group the character knows anything of, and five rows of the current tab's disciplines,
// each with its icon, its name, its five level sockets and — during advancement — a learn button.
// The **D** key and the status bar's discipline button open it; it is a right-hand pane, so it
// shares the right half of the screen with the inventory and closes it.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>One of the pane's five rows as the refresh leaves it.</summary>
/// <param name="Discipline">The instance the row's slot holds, or null for an empty row.</param>
/// <param name="Text">`slotNtext`: the discipline's name in the string table.</param>
/// <param name="Dots">`slotNdots`'s frame: level + 1, so 0 is five empty sockets and 5 is five lit.</param>
/// <param name="Learn">Whether `advDiscN`, the learn button, is up — every filled row during advancement.</param>
public readonly record struct DisciplineRow(Discipline? Discipline, string Text, int Dots, bool Learn);

/// <summary>`ui/disciplinePane.nui` and the pane object behind it.</summary>
public sealed class DisciplinePane(NuiPane pane)
{
    public const string PaneName = "disciplinePane";

    /// <summary>`MAX_VISIBLE_SLOTS`, `MAX_VISIBLE_TABS`.</summary>
    public const int VisibleRows = 5, VisibleTabs = 5;

    public NuiPane Pane { get; } = pane;

    /// <summary>The character shown (`pane[0]`): the steered one, or the one being advanced.</summary>
    public WorldThing? Subject { get; private set; }

    /// <summary>The discipline table, for the groups and their order.</summary>
    public DisciplineDefinitions? Powers { get; set; }

    /// <summary>`vuiAdvancement_bOpen`: the learn buttons are up, "cannot use yet" rows show, and a row cannot be selected or dragged.</summary>
    public bool Advancing { get; set; }

    /// <summary>The string tables. Null answers every key as itself.</summary>
    public Func<string, string?>? Text { get; set; }

    string Say(string key) => Text?.Invoke(key) ?? key;

    sealed class Tab(NdgGroup group)
    {
        public NdgGroup Group { get; } = group;
        public List<Discipline> Entries { get; } = [];
        /// <summary>The first visible entry's index — the row scroll, `tab[4]`.</summary>
        public int First { get; set; }
    }

    readonly List<Tab> tabs = [];

    /// <summary>The tab showing (`pane[5]`) and the first visible one on the strip (`pane[4]`).</summary>
    public int Current { get; private set; }
    /// <inheritdoc cref="Current"/>
    public int Offset { get; private set; }

    /// <summary>The groups on the tabs, in `game.ndg` order.</summary>
    public IEnumerable<NdgGroup> Groups => tabs.Select(t => t.Group);

    /// <summary>
    /// The rebuild (and the show hook's middle): the tabs — every `game.ndg`
    /// group, in file order, that the subject has an instance in — and each tab's rows — the group's
    /// own disciplines in `game.ndd` order that the subject has, whatever the order it learnt them
    /// in. Each tab starts scrolled to its top.
    ///
    /// **The new-discipline announcement rides on it**: every instance still flagged
    /// "unannounced" has the flag cleared — always — and when the pane is up and on top at that
    /// moment the group is answered, for a `NEWDISCIPLINE` and a line in the log. So a tome's new
    /// group is announced the next time the pane opens, and lost if a rebuild happens while it is
    /// shut.
    ///
    /// A player-type subject goes back to the tab and strip position it had (two
    /// bytes kept on the thing) when the pane is up; anybody else starts at the first tab.
    /// </summary>
    public List<NdgGroup> Rebuild(WorldThing? subject, bool upAndOnTop)
    {
        Subject = subject;
        tabs.Clear();
        Current = 0;
        Offset = 0;
        var announced = new List<NdgGroup>();
        if (subject?.Actor is not { } me || Powers?.Groups is not { } groups) return announced;
        // The tab strip: one tab for each group the character has anything in, in file order.
        foreach (var group in groups.All)
        {
            var tab = new Tab(group);
            foreach (var def in Powers.InGroup(group.Name))
                if (me.Disciplines.Of(def) is { } inst) tab.Entries.Add(inst);
            if (tab.Entries.Count > 0) tabs.Add(tab);
        }
        foreach (var d in me.Disciplines.All)
        {
            if ((d.Flags & Discipline.Unannounced) == 0) continue;
            d.Flags &= ~Discipline.Unannounced;
            if (upAndOnTop && Powers.Group(d.Definition.Group) is { } g) announced.Add(g);
        }
        if (upAndOnTop) Restore();
        return announced;
    }

    /// <summary>A player-type subject's remembered tab and strip position, held to what there is.</summary>
    void Restore()
    {
        if (Subject is not { Type: ThingType.Player, Actor: { } me }) return;
        SetCurrent(me.DisciplineTab);
        SetOffset(me.DisciplineTabOffset);
    }

    /// <summary>The tab showing, held to the tabs there are.</summary>
    public void SetCurrent(int n) => Current = tabs.Count == 0 ? 0 : Math.Clamp(n, 0, tabs.Count - 1);

    /// <summary>
    /// The strip's first visible tab. Five or fewer tabs, 0; a position that would leave
    /// fewer than five on show, the last five; otherwise what was asked — and a negative ask is
    /// ignored rather than held to 0.
    /// </summary>
    public void SetOffset(int n)
    {
        if (tabs.Count <= VisibleTabs) Offset = 0;
        else if (tabs.Count - n < VisibleTabs) Offset = tabs.Count - VisibleTabs;
        else if (n >= 0) Offset = n;
    }

    /// <summary>`discTabScroll2`: more tabs to the right.</summary>
    public bool CanTabRight => Offset + VisibleTabs < tabs.Count;
    /// <summary>`discTabScroll1`: more tabs to the left.</summary>
    public bool CanTabLeft => Offset != 0;

    /// <summary>
    /// The strip: up to five tabs from the offset, each with its group and its icon,
    /// the current one checked; the group name over the strip is the current tab's.
    /// </summary>
    public IEnumerable<(int Visible, NdgGroup Group, bool Checked)> Tabs()
    {
        for (int i = Offset, v = 0; i < tabs.Count && v < VisibleTabs; i++, v++)
            yield return (v, tabs[i].Group, i == Current);
    }

    /// <summary>`discGroupName`: the current tab's group, in the string table's words.</summary>
    public string GroupName => tabs.Count > Current ? Say(tabs[Current].Group.Name) : "";

    /// <summary>
    /// The rows: from the tab's first visible entry, top down, five at most — skipping
    /// a "cannot use yet" (level −1) entry unless advancing.
    /// </summary>
    public DisciplineRow[] Rows()
    {
        var rows = Enumerable.Repeat(new DisciplineRow(null, "", 0, false), VisibleRows).ToArray();
        if (tabs.Count <= Current) return rows;
        var tab = tabs[Current];
        int row = 0;
        for (int i = tab.First; i < tab.Entries.Count && row < VisibleRows; i++)
        {
            var d = tab.Entries[i];
            if (!Advancing && d.Level == -1) continue;
            rows[row++] = new DisciplineRow(d, Say(d.Definition.Name), d.Level + 1, Advancing);
        }
        return rows;
    }

    /// <summary>
    /// How many entries the row scroll counts: all of them, one fewer when not advancing and the
    /// **last** is at −1. Only the last is looked at, which is exact for the shipped unlock chain
    /// (the one −1 in a group is its newest).
    /// </summary>
    int Counted(Tab tab) =>
        tab.Entries.Count - (!Advancing && tab.Entries.Count > 0 && tab.Entries[^1].Level == -1 ? 1 : 0);

    /// <summary>`discScroll2`, down: more rows below the fifth.</summary>
    public bool CanScrollDown => tabs.Count > Current && tabs[Current] is { Entries.Count: > 0 } t && Counted(t) > t.First + VisibleRows;
    /// <summary>`discScroll1`, up: the first visible row is not the first.</summary>
    public bool CanScrollUp => tabs.Count > Current && tabs[Current] is { Entries.Count: > 0 } t && t.First != 0;

    /// <summary>
    /// Scroll the rows to entry <paramref name="n"/>, held to the last countable one — and
    /// **only taken when five entries still fit below it**, so a scroll that would leave fewer is
    /// ignored.
    /// </summary>
    public void ScrollRows(int n)
    {
        if (tabs.Count <= Current || tabs[Current] is not { Entries.Count: > 0 } tab) return;
        int max = Counted(tab) - 1;
        n = Math.Clamp(n, 0, Math.Max(0, max));
        if (n + VisibleRows <= tab.Entries.Count) tab.First = n;
    }

    /// <summary>The row scroll of the tab showing.</summary>
    public int FirstRow => tabs.Count > Current ? tabs[Current].First : 0;

    // ---- the clicks --------------------------------------------------------------------------

    /// <summary>
    /// A tab, or the icon in it (on the button's press): it becomes the current tab —
    /// remembered on a player-type subject — and a click on an **end** tab also moves the strip one
    /// step that way.
    /// </summary>
    public void ClickTab(int visible)
    {
        if (visible < 0 || visible >= VisibleTabs || Offset + visible >= tabs.Count) return;
        SetCurrent(Offset + visible);
        var me = Subject is { Type: ThingType.Player } p ? p.Actor : null;
        if (me is not null) me.DisciplineTab = (byte)Current;
        if (visible == 0) SetOffset(Offset - 1);
        else if (visible == VisibleTabs - 1) SetOffset(Offset + 1);
        if (me is not null && visible is 0 or VisibleTabs - 1) me.DisciplineTabOffset = (byte)Offset;
    }

    /// <summary>The strip's arrows, remembered on a player-type subject.</summary>
    public void ScrollTabs(int by)
    {
        SetOffset(Offset + by);
        if (Subject is { Type: ThingType.Player, Actor: { } me }) me.DisciplineTabOffset = (byte)Offset;
    }

    // ---- where the pointer is ----------------------------------------------------------------

    /// <summary>What a point in the pane's space is on.</summary>
    public enum Part { None, Slot, Text, Dots, Learn, Tab, TabLeft, TabRight, RowUp, RowDown }

    /// <summary>The control under a point, and its index (row or visible tab), or <see cref="Part.None"/>.</summary>
    public (Part Part, int Index) At(float x, float y)
    {
        var rows = Rows();
        for (int i = 0; i < VisibleRows; i++)
        {
            if (Advancing && rows[i].Learn && Hit($"advDisc{i}", x, y)) return (Part.Learn, i);
            if (Hit($"slot{i}", x, y)) return (Part.Slot, i);
            if (Hit($"slot{i}text", x, y)) return (Part.Text, i);
            if (Hit($"slot{i}dots", x, y)) return (Part.Dots, i);
        }
        foreach (var (v, _, _) in Tabs())
            if (Hit($"discTab{v}", x, y) || Hit($"discTabIcon{v}", x, y)) return (Part.Tab, v);
        if (Hit("discTabScroll1", x, y)) return (Part.TabLeft, 0);
        if (Hit("discTabScroll2", x, y)) return (Part.TabRight, 0);
        if (Hit("discScroll1", x, y)) return (Part.RowUp, 0);
        if (Hit("discScroll2", x, y)) return (Part.RowDown, 0);
        return (Part.None, -1);
    }

    bool Hit(string name, float x, float y) =>
        Element(name) is { Size.Length: > 1, Pos.Length: > 1 } e
        && x >= Px + e.Pos[0] && x <= Px + e.Pos[0] + e.Size[0]
        && y >= Py + e.Pos[1] && y <= Py + e.Pos[1] + e.Size[1];

    float Px => Pane.Pos.Length > 1 ? Pane.Pos[0] : 0;
    float Py => Pane.Pos.Length > 1 ? Pane.Pos[1] : 0;

    /// <summary>Whether a point is on the pane's artwork at all — which is what swallows a click.</summary>
    public bool Covers(float x, float y) =>
        Pane.Quads.Any(q => x >= Px + q.Pos[0] && x <= Px + q.Pos[0] + q.Size[0]
                            && y >= Py + q.Pos[1] && y <= Py + q.Pos[1] + q.Size[1]);

    public NuiElement? Element(string name) =>
        Pane.Elements.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // ---- what the description boxes say ------------------------------------------------------

    /// <summary>
    /// The discipline rollover: the name in the small box — white, or **red** when the
    /// instance is not a scroll's and its owner does not meet the stat requirements — and four lines
    /// in the large one: the long description (`&lt;NAME&gt;_L`), an empty line, the level line, and the
    /// requirements.
    /// </summary>
    public static (List<RolloverLine> Small, List<RolloverLine> Large) Describe(Discipline d, Func<string, string?>? text)
    {
        string Say(string key) => text?.Invoke(key) ?? key;
        var def = d.Definition;
        var owner = d.Owner;
        bool ok = (d.Flags & Discipline.Temporary) != 0 || owner is null || Cast.MeetsRequirements(owner, def);
        var small = new List<RolloverLine> { new(Say(def.Name), ok ? Rollover.White : Red) };
        string targets = Targets(def, Say);
        string level = d.Level >= 0
            ? $"{Say("RPG_DISCLEVEL")} {d.Level + 1}     {Say(owner?.Actor?.Type == ActorType.Human ? "RPG_DISCCOSTMANA" : "RPG_DISCCOSTBLOOD")} {def.CostAt(d.Level)}     {Say("RPG_DISCCASTON")} {targets}"
            : $"{Say("RPG_DISCNOLEVEL")}     {Say("RPG_DISCCASTON")} {targets}";
        var large = new List<RolloverLine>
        {
            new(Say(def.Name + "_L"), Rollover.White),
            new("", Rollover.White),
            new(level, Rollover.White),
            new(Requirements(def, text), Rollover.White),
        };
        return (small, large);
    }

    /// <summary>The rollover's red (`0xffff0000`).</summary>
    public const uint Red = 0xFFFF0000u;

    /// <summary>"Cast on": each target kind the row allows, in the engine's order, two spaces apart — none after Head.</summary>
    static string Targets(NddDiscipline def, Func<string, string> say)
    {
        var parts = new List<string>();
        (DisciplineFlags Flag, string Key)[] order =
        [
            (DisciplineFlags.TargetNone, "RPG_DISCTARGET_NONE"), (DisciplineFlags.TargetArea, "RPG_DISCTARGET_AREA"),
            (DisciplineFlags.TargetInventory, "RPG_DISCTARGET_ITEM"), (DisciplineFlags.TargetEnemy, "RPG_DISCTARGET_ENEMY"),
            (DisciplineFlags.TargetFriendly, "RPG_DISCTARGET_FRIENDLY"), (DisciplineFlags.TargetDead, "RPG_DISCTARGET_DEAD"),
            (DisciplineFlags.TargetHead, "RPG_DISCTARGET_HEAD"),
        ];
        var sb = new System.Text.StringBuilder();
        foreach (var (flag, key) in order)
        {
            if (!def.Has(flag)) continue;
            sb.Append(say(key));
            if (flag != DisciplineFlags.TargetHead) sb.Append("  ");
        }
        return sb.ToString();
    }

    /// <summary>
    /// The requirements line: "Min %V %S" for a minimum, "Max %V %S" for a maximum (the
    /// value's negation), `%V` truncated, `%S` the stat's own `RPG_RESTRICT_` name — each one
    /// **followed** by the two spaces, the last included: the description's builder appends them after
    /// every check, where the item's restrictions put them only between rows. 1.1's
    /// Dread Gaze reads `Min 35 Charisma  `. A discipline with no
    /// checks has an empty line.
    /// </summary>
    static string Requirements(NddDiscipline def, Func<string, string?>? text)
    {
        var line = new System.Text.StringBuilder();
        foreach (var (stat, value) in def.Checks)
        {
            string name = $"RPG_RESTRICT_{((ActorStat)stat).ToString().ToUpperInvariant()}";
            string shown = text?.Invoke(name) ?? name;
            string format = $"%V{(int)(value > 0f ? value : -value)}%S{shown}";
            line.Append(Strings.Format(k => text?.Invoke(k), value > 0f ? "RPG_RESTRICT_MINSTAT" : "RPG_RESTRICT_MAXSTAT", format))
                .Append("  ");
        }
        return line.ToString();
    }
}
