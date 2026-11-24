// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Fixes page: Vitomir's own screen, reached from the Options screen, where the player turns the
// Fixes on and the quality-of-life switches off (the repairs, which are always applied, are in the notes).
// The in-game Fixes menu, built with the Options screen.
// Nothing here is 1.1's: it is drawn with 1.1's own art, fonts and controls so it sits
// among the Options screens as one of them.
using System.Text;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Features;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// The Fixes page (ours). Three groups in one list: the **gameplay Fixes** and the **visual Fixes**, each
/// faithful by default and off until ticked — the Fixes divided by the registry's own axis, the ones that
/// change the game first; and **quality of life**, on until unticked. **The repairs are not on it**:
/// they cannot be changed, and the notes describe them.
/// OK writes the choices into the
/// <c>[fixes]</c> and <c>[qol]</c> sections of the player's own <c>vitomir.ini</c> — the same
/// sections the ini and <c>--fix</c> / <c>--no-qol</c> already read — and puts them in force at once;
/// Cancel and Escape leave everything as it was.
///
/// **The rows are the feature registry's** (<see cref="FeatureRegistry"/>), and so are
/// the rules the boxes keep: a feature that <see cref="Feature.Refines">refines</see> another is drawn indented
/// under it and its box is locked while that one is off; unticking that one unticks it; and a tick the registry
/// refuses does not take — the box goes back and the hint box says why.
/// </summary>
/// <param name="features">The registry the page lists; the game's own unless a test says otherwise.</param>
public sealed partial class FixesPage(FrontEnd menus, FeatureRegistry? features = null)
{
    readonly FeatureRegistry registry = features ?? GameFeatures.Registry;
    Row[]? rows;

    /// <summary>This page's rows: its registry's (<see cref="Rows(FeatureRegistry)"/>), made once.</summary>
    public IReadOnlyList<Row> PageRows => rows ??= [.. Rows(registry)];

    /// <summary>The page's name as a pane (ours; there is no such file in the install).</summary>
    public const string File = "vitomirFixes";

    /// <summary>
    /// The page's layout, in the `.nui` language: the load screen's frame, whose list box is where
    /// the switches go, over the bottom row of the Options screens (OK and Cancel, at the positions
    /// every Options sub-screen puts them), and a check box and a label per row in `defaultFont10`,
    /// the font the Options screens' own lists use. Rows are laid down from the top of the box.
    /// </summary>
    public NuiPane Layout()
    {
        var nui = new StringBuilder();
        nui.AppendLine("name=vitomirfixes");
        nui.AppendLine("pos=0,0");
        foreach (var (mat, qx, qy, w, h) in Frame) nui.AppendLine($"quad mat={mat} pos={qx},{qy} size={w},{h}");
        // The load screen's title plate says "load game"; the console's own line art, which is
        // exactly the plate's width, blanks it for this page's title (in our lettering the plate's
        // words are taken off it instead, MenuLettering).
        nui.AppendLine($"quad mat=consoleline.tga pos={TitleX},{TitleY} size=128,16");
        nui.AppendLine($"quad mat=consoleline2.tga pos={TitleX + 128},{TitleY} size=128,16");
        nui.AppendLine($"element textbox name=fxtitle pos={TitleX},{TitleY} size=256,16 font=morpheus15 textboxflags=0x03 maxitems=1 tbtextcolor={Gold}");
        // The load screen's own scroll bar beside its list box (spLoad.nui: the slider first, so it is
        // hit before the track under it), which the frame's art already has the channel for.
        nui.AppendLine($"element scrollslider name={SliderName} pos={BarX},{BarY} size=8,8 bitmap=statusbarslider");
        nui.AppendLine($"element scrollbar name={BarName} pos={BarX},{BarY} size=8,{BarH} flags=0x02 vertical=1 slider={SliderName}");
        int index = 0;
        foreach (var row in PageRows)
        {
            int y = RowY(index++);
            if (row.Kind == RowKind.Heading)
                nui.AppendLine($"element textbox name={row.Id} pos={BoxX + 2},{y} size={BoxW - 4},{RowH} font=defaultfont10 textboxflags=0x02 maxitems=1 tbtextcolor={Gold}");
            else
            {
                // A row that refines the one above it stands in from it: box and label moved right together.
                int x = row.Under is null ? 0 : Indent;
                nui.AppendLine($"element checkbox name={row.Id} pos={BoxX + 4 + x},{y - CheckDrop} size=13,15 checkedbitmap=xbuttondep bitmap=xbutton");
                nui.AppendLine($"element textbox name={row.Id}label pos={BoxX + 22 + x},{y} size={BoxW - 26 - x},{RowH} font=defaultfont10 textboxflags=0x02 maxitems=1");
            }
        }
        nui.AppendLine($"element textbox name=fxhint pos={BoxX + 4},{HintY} size={BoxW - 8},{HintH} font=defaultfont10 textboxflags=0x04");
        nui.AppendLine("element button name=fxokbutton pos=332,12 size=91,41 flags=0x02 rollover=opsdisadvokbuttonover depressed=opsdisadvokbuttondep");
        nui.AppendLine("element button name=fxcancelbutton pos=220,12 size=91,41 flags=0x02 rollover=opsdisadvcancelbuttonover depressed=opsdisadvcancelbuttondep");
        return Spaced(NuiPane.Parse(nui.ToString(), File + ".nui"), "fxhint");
    }

    /// <summary>
    /// The room between the lines of our pages' hint boxes (ours): a unit, so a letter's tail clears the line below
    /// (<i>"See text clipping here"</i>) and the box's six lines still fit its 66 (6 x 10 + 5).
    /// </summary>
    public const int HintSpacing = 1;

    /// <summary>A hint box given <see cref="HintSpacing"/> between its lines (a key of ours, <c>spacing</c>, which no file of 1.1's has).</summary>
    internal static NuiPane Spaced(NuiPane nui, string hint)
    {
        int i = nui.Elements.FindIndex(e => e.Name.Equals(hint, StringComparison.OrdinalIgnoreCase));
        if (i >= 0)
            nui.Elements[i] = nui.Elements[i] with { Attrs = new Dictionary<string, string>(nui.Elements[i].Attrs) { ["spacing"] = HintSpacing.ToString() } };
        return nui;
    }

    /// <summary>
    /// The art (ours to choose, 1.1's to draw): the load screen's twelve tiles for the frame and its
    /// list box, with the Options sub-screens' bottom-left tiles for the OK and Cancel words.
    /// </summary>
    static readonly (string Mat, int X, int Y, int W, int H)[] Frame =
    [
        ("spload01.tga", 0, 224, 256, 256), ("spload02.tga", 256, 224, 256, 256), ("spload03.tga", 512, 224, 128, 256),
        ("spload04.tga", 0, 96, 256, 128), ("spload05.tga", 256, 96, 256, 128), ("spload06.tga", 512, 96, 128, 128),
        ("opsgameplay07.tga", 0, 32, 256, 64), ("opsgameplay08.tga", 256, 32, 256, 64), ("spload09.tga", 512, 32, 128, 64),
        ("opsgameplay10.tga", 0, 0, 256, 32), ("opsgameplay11.tga", 256, 0, 256, 32), ("spload12.tga", 512, 0, 128, 32),
    ];

    /// <summary>The frame's tiles, which the key page (<see cref="KeysPage"/>) is drawn on too.</summary>
    public static IReadOnlyList<(string Mat, int X, int Y, int W, int H)> FrameTiles => Frame;

    /// <summary>The load screen's list box (`spLoadList`, 193,106 246 x 312), which the rows fill.</summary>
    public const int BoxX = 193, BoxY = 106, BoxW = 246, BoxH = 312;
    /// <summary>
    /// One row's height, and where the first row's bottom sits. 16, when the two window
    /// switches made sixteen rows; 15, when `shapetorch` made seventeen; 13 and 12,
    /// each time so that every row still sat above the hint box; 15 again, when the
    /// page began to scroll. **16 now**, with the check box level with its row (<see cref="CheckDrop"/>
    /// 0): the box's middle on the label's (<i>"The text isn't lined up with the
    /// checkbox"</i>), a unit between one row's box and the next (the Controls screen's own spacing, 16 for
    /// boxes of 15), so no box runs into another (<i>"See text clipping here and here"</i>).
    /// </summary>
    public const int RowH = 16, TopY = BoxY + BoxH - RowH - 4;
    /// <summary>How far a row's check box sits below the row's foot: none (to keep it out of the heading below).</summary>
    public const int CheckDrop = 0;
    /// <summary>
    /// The room over a group's heading, between it and the last row of the group before (ours, so the groups
    /// stand apart). Not over the first heading, nor over a heading at the top
    /// of the box when the list is scrolled to it.
    /// </summary>
    public const int HeadingGap = 7;
    /// <summary>
    /// How far a row that refines another stands in from the left, box and label together (ours, a slight indent):
    /// most of a check box's width, so its box sits under the
    /// start of the label above it.
    /// </summary>
    public const int Indent = 12;
    /// <summary>The lowest a row's foot may sit and still be on show: the top of the hint box.</summary>
    public const int Floor = HintY + HintH;

    /// <summary>Where a row's foot sits with the list at its top: <see cref="RowH"/> a row, and <see cref="HeadingGap"/> more over each heading but the first.</summary>
    public int RowY(int index)
    {
        int y = TopY - index * RowH;
        for (int i = 1; i <= index && i < PageRows.Count; i++)
            if (PageRows[i].Kind == RowKind.Heading) y -= HeadingGap;
        return y;
    }

    /// <summary>How far the rows are raised with the list scrolled to <paramref name="scroll"/>: the first on show at the top.</summary>
    public int LiftAt(int scroll) => TopY - RowY(Math.Clamp(scroll, 0, Math.Max(0, PageRows.Count - 1)));

    /// <summary>Whether a row is in the box with the list scrolled to <paramref name="scroll"/>: at or after the first, its box above the hint box.</summary>
    public bool Fits(int index, int scroll) => index >= scroll && index < PageRows.Count && RowY(index) + LiftAt(scroll) - CheckDrop >= Floor;

    /// <summary>How many rows are on show with the list scrolled to <paramref name="scroll"/>.</summary>
    public int ShownAt(int scroll)
    {
        int n = 0;
        while (Fits(scroll + n, scroll)) n++;
        return n;
    }

    /// <summary>Where the lowest row on show sits at the list's end (<see cref="ScrollEnd"/>), pinned in <c>FixesScrollTests</c>.</summary>
    public int LastRowY => RowY(PageRows.Count - 1) + LiftAt(ScrollEnd);
    /// <summary>The box the hovered switch's description goes in, at the foot of the list box.</summary>
    public const int HintY = BoxY + 4, HintH = 66;
    /// <summary>
    /// The scroll bar (ours to place, 1.1's to draw): the load screen's own, `spLoadScrollBar` at
    /// 441,105, 8 x 314, just right of the list box, with its `StatusBarSlider` knob (`spLoad.nui`).
    /// </summary>
    public const int BarX = 441, BarY = 105, BarH = 314;
    /// <summary>The scroll bar's and its slider's names on the page.</summary>
    public const string BarName = "fxscrollbar", SliderName = "fxscrollslider";
    /// <summary>Where the page's title sits: on the load screen's title plate (the console line's 256 x 16 over it).</summary>
    public const int TitleX = 192, TitleY = 452;
    /// <summary>
    /// The gold of the Options screens' own lettering, as a `tbtextcolor` — written blue first,
    /// so this is red 224, green 184, blue 96.
    /// </summary>
    public const string Gold = "96,184,224";

    public enum RowKind { Heading, Fix, Qol, Repair, Platform }

    /// <summary>One line of the page: a group's heading, or a switch with its name and what it does.</summary>
    public readonly record struct Row(RowKind Kind, string Id, string Name, string What)
    {
        /// <summary>The switch's declaration; null for a heading.</summary>
        public Feature? Feature { get; init; }
        /// <summary>The <see cref="Id"/> of the row this one is drawn indented under — the feature it refines — or null.</summary>
        public string? Under { get; init; }
    }

    /// <summary>Every row of the game's own registry, top to bottom: a new feature is on the page the day it is declared.</summary>
    public static IEnumerable<Row> Rows() => Rows(GameFeatures.Registry);

    /// <summary>
    /// Every row of a registry, top to bottom: the four groups in turn, each in the registry's order — but a
    /// feature that refines another of its group follows it at once, wherever it was declared, so that it can be
    /// drawn indented under it.
    ///
    /// **The Fixes are two groups, by the registry's own axis** (<i>"I'd like the Fixes group
    /// at the top separated into 2 subgroups, gameplay and visual. Purely visual ones that don't affect gameplay
    /// put into the proper category, and won't affect the faithful label."</i>): first the Fixes whose
    /// <see cref="Feature.Axis"/> is rules, then the ones whose axis is presentation. Nobody writes either list:
    /// a Fix is in the group its declaration computes. The quality-of-life switches and the repairs are a group
    /// each, as they were.
    /// </summary>
    public static IEnumerable<Row> Rows(FeatureRegistry registry)
    {
        (RowKind Row, FeatureKind Kind, FeatureAxis? Axis, string Prefix)[] groups =
        [
            (RowKind.Fix, FeatureKind.Fix, FeatureAxis.Rules, "fxfix_"),
            (RowKind.Fix, FeatureKind.Fix, FeatureAxis.Presentation, "fxfix_"),
            (RowKind.Qol, FeatureKind.Qol, null, "fxqol_"),
        ];
        for (int g = 0; g < groups.Length; g++)
        {
            var (kind, of, axis, prefix) = groups[g];
            yield return new Row(RowKind.Heading, "fxhead" + g, "", "");
            var group = registry.Of(of).Where(f => axis is null || f.Axis == axis).ToList();
            bool Under(Feature f, Feature? parent) =>
                f.Refines is { } id && registry.Find(id) is { } refined && group.Contains(refined) ? ReferenceEquals(refined, parent) : parent is null;
            IEnumerable<Row> Below(Feature? parent)
            {
                foreach (var f in group)
                {
                    if (!Under(f, parent)) continue;
                    yield return new Row(kind, prefix + f.Name, f.Name, f.What) { Feature = f, Under = parent is null ? null : prefix + parent.Name };
                    foreach (var row in Below(f)) yield return row;
                }
            }
            foreach (var row in Below(null)) yield return row;
        }
        // The platform's own settings, the same in every game and kept by the platform, not in [fixes] or [qol]: streamer
        // mode. Not a feature of the registry: it changes nothing a game does, only what the screen shows.
        yield return new Row(RowKind.Heading, "fxhead" + groups.Length, "", "");
        yield return new Row(RowKind.Platform, StreamerRow, "streamer", StreamerWhat);
    }

    /// <summary>The streamer mode row's id (<see cref="Vitomir.Platform.Presentation.StreamerMode"/>).</summary>
    public const string StreamerRow = "fxplat_streamer";

    /// <summary>The streamer mode row's label.</summary>
    public const string StreamerTitle = "Streamer mode";

    /// <summary>The streamer mode row's hint: four lines of the hint box at most.</summary>
    public const string StreamerWhat =
        "Hides network addresses and ports, your user folder in paths and any join code wherever they show on screen, the log window too. Your log files keep them. The same in every game.";

    /// <summary>
    /// The three headings (ours), in the groups' order: the Fixes that change the game, the Fixes that change only
    /// what is seen or heard — which leave a game faithful — and the quality-of-life switches. Each is
    /// one line of the row's width in <c>defaultFont10</c> (242 units; the longest two are 219), which
    /// <c>FixesPageTests</c> measures: a heading that wrapped would lose its second line.
    /// </summary>
    public static readonly string[] Headings =
    [
        "Gameplay Fixes - 1.1's faults, off unless ticked",
        "Visual Fixes - plays the same, off unless ticked",
        "Quality of Life - on unless unticked",
        "Platform - the same in every game",
    ];

    /// <summary>The page's title (ours): in lower case, as 1.1's own screens' titles are.</summary>
    public const string Title = "vitomir fixes";

    /// <summary>
    /// The switches being edited, held to what the registry allows (<see cref="FeatureTicks"/>): the boxes are a
    /// picture of this.
    /// </summary>
    public FeatureTicks Ticks => ticks ??= new FeatureTicks(registry);
    FeatureTicks? ticks;

    /// <summary>The Fixes as the boxes stand: what OK writes and hands on. A copy — the page's own state is <see cref="Ticks"/>.</summary>
    public World.Fixes Editing
    {
        get
        {
            var fixes = new World.Fixes();
            foreach (var f in registry.Of(FeatureKind.Fix)) fixes[f.Name] = Ticks.On(f);
            return fixes;
        }
    }

    /// <summary>The quality-of-life switches as the boxes stand.</summary>
    public Qol EditingQol
    {
        get
        {
            var qol = new Qol();
            foreach (var f in registry.Of(FeatureKind.Qol)) qol[f.Name] = Ticks.On(f);
            return qol;
        }
    }

    /// <summary>Where OK writes, the player's own <c>vitomir.ini</c> unless a test says otherwise.</summary>
    public string? SavePath { get; set; }

    /// <summary>The page's pane, built on first use from <see cref="Layout"/>.</summary>
    public MenuPane Pane => menus.Pane(File, Layout);

    /// <summary>
    /// Opens the page over the Options screen on a layer of its own, as every Options sub-screen
    /// opens: every box put from what is in force now, the repairs ticked and greyed.
    /// </summary>
    public void Open()
    {
        var pane = Pane;
        // In someone else's game the host's switches are in force over the player's own; the page
        // edits the player's own (Net.HostSwitches), so what it writes back is theirs, not the host's.
        Ticks.Put(registry.On(Net.HostSwitches.Own?.Fixes ?? World.Fixes.Current, Net.HostSwitches.Own?.Qol ?? Qol.Current));
        EditingStreamer = Vitomir.Platform.Presentation.StreamerMode.On;
        pane.Texts["fxtitle"] = Title;
        pane.Texts["fxhint"] = Standing();
        said = null;
        Bar?.Release();
        ScrollTo(0);
        int h = 0;
        foreach (var row in PageRows)
            if (row.Kind == RowKind.Heading) pane.Texts[row.Id] = Headings[h++];
        PutBoxes(pane);
        menus.PushLayer();
        menus.Show(pane);
    }

    /// <summary>
    /// Every box and label as the switches stand: a repair ticked and locked; in someone else's game a switch
    /// the host governs showing the host's setting, locked (the player's own stays in <see cref="Ticks"/>,
    /// untouched, and is theirs again when they leave, <see cref="Net.HostSwitches.Leave"/>); a switch that
    /// refines another locked while that one is off; every other box the player's own to press.
    /// </summary>
    void PutBoxes(MenuPane pane)
    {
        foreach (var row in PageRows)
        {
            if (row.Kind == RowKind.Platform)
            {
                pane.Check(row.Id, EditingStreamer);
                pane.Texts[row.Id + "label"] = StreamerTitle;
                pane.Locked.Remove(row.Id);
                continue;
            }
            if (row.Feature is not { } feature) continue;
            bool locked = true;
            if (row.Kind == RowKind.Repair)
            {
                pane.Check(row.Id, true);
                pane.Texts[row.Id + "label"] = feature.Title + " (always on)";
            }
            else if (HostValue(row, Host()) is { } hosts)
            {
                pane.Check(row.Id, hosts);
                pane.Texts[row.Id + "label"] = feature.Title + HostsLabel;
            }
            else
            {
                pane.Check(row.Id, Ticks.On(feature));
                pane.Texts[row.Id + "label"] = feature.Title;
                locked = Ticks.LockedUnder(feature) is not null;
            }
            if (locked) pane.Locked.Add(row.Id); else pane.Locked.Remove(row.Id);
        }
    }

    /// <summary>
    /// The host's setting of a row's switch while this machine is a player in someone else's network
    /// game and the switch is the host's to govern (<see cref="Net.HostSwitches.Of"/>) and the host
    /// sent it (<paramref name="sent"/>, <see cref="Net.HostSwitches.FromHost"/>); null otherwise — at
    /// home, in the player's own game, and for a switch that is each machine's own.
    /// </summary>
    public static bool? HostValue(Row row, IReadOnlyList<Net.HostSwitches.Setting>? sent)
    {
        if (sent is null || row.Kind is not (RowKind.Fix or RowKind.Qol)) return null;
        string register = row.Kind == RowKind.Fix ? "fixes" : "qol";
        if (row.Feature is { } feature ? feature.Whose != FeatureWhose.Host : Net.HostSwitches.Of(register, row.Name) != Net.HostSwitches.Governor.Host) return null;
        foreach (var s in sent)
            if (s.Register.Equals(register, StringComparison.OrdinalIgnoreCase) && s.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase))
                return s.On;
        return null;
    }

    /// <summary>
    /// What the host sent, while this machine is a player in someone else's game — <see
    /// cref="Net.HostSwitches.FromHost"/> unless a test says otherwise (it is process-wide).
    /// </summary>
    public Func<IReadOnlyList<Net.HostSwitches.Setting>?> Host { get; set; } = () => Net.HostSwitches.FromHost;

    /// <summary>What a host-governed row's label adds (ours).</summary>
    public const string HostsLabel = " (the host's)";

    /// <summary>
    /// The line that says why (ours): in the hint box when the page opens in someone else's game, and
    /// after a host-governed switch's own description when the pointer is on it.
    /// </summary>
    public const string HostsGame =
        "In this network game the host's setting is in force for the greyed switches; yours comes back when you leave.";

    /// <summary>
    /// **What the switches as ticked make a game** (ours; from the registry's <see cref="FeatureRegistry.Profile(IEnumerable{Feature})"/>
    /// and <see cref="FeatureRegistry.RulesFeatures"/>): the line the hint box holds while the pointer is on no
    /// row. Either a game with these switches is <see cref="FeatureRegistry.Faithful"/> to 1.1's rules, or it is
    /// <see cref="FeatureRegistry.Modded"/>, and by which switches, by name — the gameplay Fixes that are ticked,
    /// and nothing else: a game played as it comes is faithful, since no quality-of-life switch counts
    /// (<see cref="Feature.Counts"/>). It follows the boxes as they are ticked. In someone else's network game the rules are the host's, not these
    /// boxes', and the line is <see cref="HostsGame"/> as it was.
    /// </summary>
    public string Standing() => Host() is not null ? HostsGame : Standing(registry, Ticks.AllOn);

    /// <summary><see cref="Standing()"/> of any set of features on.</summary>
    public static string Standing(FeatureRegistry registry, IEnumerable<Feature> on)
    {
        var rules = registry.RulesFeatures(on);
        if (rules.Count == 0) return FaithfulGame;
        string names = rules.Count == 1 ? rules[0].Title
            : string.Join(", ", rules.Take(rules.Count - 1).Select(f => f.Title)) + " and " + rules[^1].Title;
        return $"A game with these switches is {FeatureRegistry.Modded}: {names} {(rules.Count == 1 ? "changes" : "change")} 1.1's rules.";
    }

    /// <summary>What <see cref="Standing()"/> says when no switch that changes the rules is ticked (ours).</summary>
    public const string FaithfulGame = "A game with these switches is " + FeatureRegistry.Faithful + " to 1.1's rules.";

    /// <summary>
    /// A box pressed: the switch it stands for follows it — if the registry allows it. A tick it refuses does
    /// not take: the box goes back and the hint box says why (<see cref="FeatureRegistry.Blocks"/>'s sentence).
    /// A switch unticked takes with it every switch that needed it, and the hint box says which. A repair's box,
    /// the host's, and one locked under a switch that is off never move.
    /// </summary>
    public void Checked(MenuPane pane, string box)
    {
        var row = PageRows.FirstOrDefault(r => r.Id.Equals(box, StringComparison.OrdinalIgnoreCase));
        if (row.Kind == RowKind.Platform)
        {
            EditingStreamer = pane.Checks.GetValueOrDefault(box);
            said = null;
            PutBoxes(pane);
            return;
        }
        if (pane.Locked.Contains(box) || row.Feature is not { } feature) return;   // a repair, the host's, or locked: the player's own stays
        var pressed = Ticks.Tick(feature, pane.Checks.GetValueOrDefault(box));
        said = null;
        if (pressed.Refused is { } why) said = (row.Id, Refused(why));
        else if (pressed.AlsoOff.Count > 0) said = (row.Id, string.Join(" ", pressed.AlsoOff.Select(AlsoOff)));
        if (said is { } says) pane.Texts["fxhint"] = says.Text;
        PutBoxes(pane);
    }

    /// <summary>What the hint box says of a tick that did not take (ours).</summary>
    public static string Refused(Conflict why) => $"Not switched on: {why.Shown}.";

    /// <summary>What the hint box says of a switch that went off with the one it needed (ours).</summary>
    public static string AlsoOff(Conflict why) => $"{why.Feature.Title} is switched off too: {why.Shown}.";

    /// <summary>What a press just said, and of which row: it stays in the hint box while the pointer rests there.</summary>
    (string Id, string Text)? said;

    /// <summary>
    /// The second sentence of a row's hint, from its declaration (ours): what kind of thing the switch changes,
    /// and whose it is in a network game (<see cref="Feature.Whose"/>). A Fix or a repair that changes the rules
    /// says <c>Rules</c>. A quality-of-life switch that the host carries out
    /// (<c>quickequip</c>, <c>driving</c>, <c>dropcells</c>) says <c>How you play</c>: it changes the way to an
    /// outcome and not the outcome, and leaves a game faithful. Anything else changes only
    /// this machine's picture, sound or controls — and a switch that changes only the picture or the sound may
    /// still be the host's, when the host is the machine that makes it (<c>soakedvoid</c>). Short, because the hint
    /// box holds four lines and the longest one-line descriptions fill three (<c>FixesPageTests</c> holds every
    /// row's hint to the box).
    /// </summary>
    public static string Reach(Feature feature) =>
        (feature.Axis == FeatureAxis.Rules ? feature.Kind == FeatureKind.Qol ? "How you play" : "Rules"
            : feature.Whose == FeatureWhose.Host ? "Picture or sound" : "Picture, sound or controls")
        + (feature.Whose == FeatureWhose.Host ? ": the host's in a network game." : ": each machine's own.");

    /// <summary>
    /// What a switch's hint says it does, without its closing stop: on and off, plainly (<see cref="FeatureHints"/>), where it
    /// has them, else its one line (<paramref name="what"/>, the declaration's by default).
    /// </summary>
    public static string Says(Feature feature, string? what = null)
    {
        string said = FeatureHints.Of(feature) is { } plain ? $"On: {Opening(plain.On)} Off: {Opening(plain.Off)}" : Opening(what ?? feature.What);
        return said.EndsWith('.') ? said[..^1] : said;
    }

    /// <summary>
    /// A row's hint (ours): its one line, then <see cref="Reach"/> — or, for a switch locked under the one it
    /// refines, what it is waiting for; and in someone else's game, for a switch the host governs,
    /// <see cref="HostsGame"/> in <see cref="Reach"/>'s place (it says whose the switch is, and why it is grey).
    /// </summary>
    public string Hint(Row row)
    {
        if (row.Kind == RowKind.Platform) return row.What;
        if (row.Feature is not { } feature) return "";
        string what = Says(feature, row.What);
        if (HostValue(row, Host()) is not null) return $"{what}. {HostsGame}";
        if (row.Kind != RowKind.Repair && Ticks.LockedUnder(feature) is { } needs)
            return $"{what}. {Reach(feature)} Locked until {needs.Title} is on: it refines it.";
        return $"{what}. {Reach(feature)}";
    }

    /// <summary>
    /// A description as the first sentence of a hint: its first letter a capital. The declarations keep their
    /// phrases in lower case, since the console's listing prints them after a name.
    /// </summary>
    public static string Opening(string what) =>
        what.Length > 0 && char.IsLower(what[0]) ? char.ToUpperInvariant(what[0]) + what[1..] : what;

    /// <summary>
    /// The description of the row under the pointer, for the hint box (called each frame by the host of the
    /// page) — or, while the pointer rests on a box just pressed, what the press said; and **while the pointer
    /// is on no row** (nothing, a heading, a button, the bar), what the switches as ticked make a game
    /// (<see cref="Standing()"/>).
    /// </summary>
    public void Hover(MenuPane pane, string? control)
    {
        string id = control is null ? "" : control.EndsWith("label", StringComparison.OrdinalIgnoreCase) ? control[..^5] : control;
        var row = PageRows.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (control is null || row.Kind == RowKind.Heading || row.Feature is null && row.Kind != RowKind.Platform)
        {
            said = null;
            pane.Texts["fxhint"] = Standing();
            return;
        }
        if (said is { } says && says.Id.Equals(row.Id, StringComparison.OrdinalIgnoreCase)) pane.Texts["fxhint"] = says.Text;
        else
        {
            said = null;
            pane.Texts["fxhint"] = Hint(row);
        }
    }

    public void Clicked(string button)
    {
        if (button == "fxokbutton") Ok();
        else if (button == "fxcancelbutton") Cancel();
    }

    /// <summary>
    /// OK (ours): every switch written to <c>[fixes]</c> and <c>[qol]</c> in the player's own
    /// <c>vitomir.ini</c> — each one, on or off, so the file says plainly what is in force — and put
    /// in force at once, then the page put away. A switch given on the command line is overridden for
    /// the rest of the session — the host keeps the choice for every run after this one
    /// (<see cref="MenuChoices"/>) — and the command line wins again when the program next starts.
    /// </summary>
    public void Ok()
    {
        string path = SavePath ?? Diag.UserSettingsPath;
        var (fixes, qol) = (Editing, EditingQol);
        try
        {
            SettingsFile.Set(path, "fixes", fixes.All.Select(f => new KeyValuePair<string, string>(f.Name, f.On ? "on" : "off")));
            SettingsFile.Set(path, "qol", qol.All.Select(q => new KeyValuePair<string, string>(q.Name, q.On ? "on" : "off")));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        // Streamer mode: the platform's, in force at once and kept in the platform's own file, not the game's ini.
        if (EditingStreamer != Vitomir.Platform.Presentation.StreamerMode.On) StreamerChanged?.Invoke(EditingStreamer);
        Changed?.Invoke(fixes, qol);
        Close();
    }

    /// <summary>
    /// Streamer mode as its box stands: put from <see cref="Vitomir.Platform.Presentation.StreamerMode.On"/> when the page
    /// opens, and what OK hands on (<see cref="StreamerChanged"/>).
    /// </summary>
    public bool EditingStreamer { get; set; }

    /// <summary>
    /// Streamer mode changed by OK: the host puts it in force and keeps it
    /// (<see cref="Vitomir.Platform.Presentation.StreamerMode.Set"/>). The page does not set it itself, so a test can open it
    /// without touching the process-wide setting.
    /// </summary>
    public event Action<bool>? StreamerChanged;

    /// <summary>
    /// The new switches, for the host to install. The page does not write <see cref="World.Fixes.Current"/>
    /// itself, so that a test can open it without touching the process-wide registers.
    /// </summary>
    public event Action<World.Fixes, Qol>? Changed;

    /// <summary>Cancel, or Escape: nothing kept.</summary>
    public void Cancel() => Close();

    void Close()
    {
        Bar?.Release();
        if (menus.IsUp(Pane)) menus.PopLayer();
    }
}
