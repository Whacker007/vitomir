// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The key page: Vitomir's own screen, reached from the Options screen's keybinds row (the Controls screen
// before), where every command's keys are moved. Nothing here is 1.1's; like the
// Fixes page it is drawn with 1.1's own art, fonts and controls so it sits among the Options screens as one of them.
using System.Text;
using System.Text.RegularExpressions;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// The key page (ours). **One list that scrolls** (<i>"let's make the list scrollable, so
/// there's no need for the 'more' option"</i>): a heading for each group of commands, then the commands, each a name
/// and three cells, one per chord. A click on a cell waits for the next key or button — with Shift, Ctrl or Alt if
/// they are held — and binds it there; **Escape while a cell waits unmaps it** (<i>"If a person clicks on a command
/// slot to remap it, have a tooltip at the very bottom underneath the normal key tooltip say 'press esc to unmap'"</i>;
/// Escape is the one key never bound), and a click on the waiting cell again leaves it as it was. A chord already bound
/// elsewhere asks whether to move it (OK) or keep it on both (Cancel). **defaults** puts back 1.1's keys. OK writes
/// <c>[keys]</c> in the player's own <c>vitomir.ini</c> and puts the keys in force at once; Cancel and Escape keep
/// nothing.
/// </summary>
public sealed class KeysPage(FrontEnd menus)
{
    /// <summary>The page's name as a pane (ours).</summary>
    public const string File = "vitomirKeys";

    /// <summary>The page's title on the plate (ours): in lower case, as 1.1's own screens' titles are.</summary>
    public const string Title = "key bindings";

    /// <summary>
    /// The groups: a heading and the commands in it, in order (ours). The camera's and driving's are two since the
    /// swivel lock; the quick save and load are with the characters and quick items, with the full-screen
    /// toggle; the free camera's are next, its moves and the way in and out, then its turns; the
    /// Storyteller's. Pages of fourteen until then; one list since.
    /// </summary>
    public static readonly (string Heading, KeyCommand[] Commands)[] Groups =
    [
        ("The Mouse", [KeyCommand.Act, KeyCommand.Cast, KeyCommand.Secondary, KeyCommand.Party, KeyCommand.Alt, KeyCommand.Hold,
                       KeyCommand.SecondaryClick, KeyCommand.PartyClick, KeyCommand.AltClick, KeyCommand.HoldClick,
                       KeyCommand.SkipLine, KeyCommand.Pause, KeyCommand.Screenshot, KeyCommand.Console]),
        ("The Camera", [KeyCommand.TurnLeft, KeyCommand.TurnRight, KeyCommand.TiltUp, KeyCommand.TiltDown,
                       KeyCommand.ZoomOut, KeyCommand.ZoomIn, KeyCommand.FirstPerson, KeyCommand.Swivel,
                       KeyCommand.DragSwivel, KeyCommand.SwivelLock,
                       KeyCommand.ShowObjects]),
        ("Driving", [KeyCommand.Forward, KeyCommand.Back, KeyCommand.StepLeft, KeyCommand.StepRight, KeyCommand.Run]),
        ("The Coterie and the Panes", [KeyCommand.Regroup, KeyCommand.Ungroup, KeyCommand.Group, KeyCommand.StanceAttack,
                       KeyCommand.StanceNeutral, KeyCommand.StanceDefense, KeyCommand.NextCharacter, KeyCommand.CharacterSheet,
                       KeyCommand.Inventory, KeyCommand.Quests, KeyCommand.Disciplines, KeyCommand.Map,
                       KeyCommand.ClosePanes, KeyCommand.StatusBar]),
        ("Characters, Items, Saves, Screen", [KeyCommand.Character1, KeyCommand.Character2, KeyCommand.Character3, KeyCommand.Character4,
                       KeyCommand.Quick1, KeyCommand.Quick2, KeyCommand.Quick3, KeyCommand.Quick4, KeyCommand.Quick5,
                       KeyCommand.QuickSave, KeyCommand.QuickLoad, KeyCommand.FullScreen]),
        ("Disciplines", [KeyCommand.Discipline1, KeyCommand.Discipline2, KeyCommand.Discipline3, KeyCommand.Discipline4,
                       KeyCommand.Discipline5, KeyCommand.Discipline6, KeyCommand.Feed, KeyCommand.Heal]),
        ("The Free Camera: Moving", [KeyCommand.FlyForward, KeyCommand.FlyBack, KeyCommand.FlyLeft, KeyCommand.FlyRight,
                       KeyCommand.FlyUp, KeyCommand.FlyDown, KeyCommand.FlyCamera]),
        ("The Free Camera: Turning", [KeyCommand.FlyTurnLeft, KeyCommand.FlyTurnRight, KeyCommand.FlyLookDown, KeyCommand.FlyLookUp,
                       KeyCommand.FlyDrag, KeyCommand.FlyRollLeft, KeyCommand.FlyRollRight, KeyCommand.FlyLevel, KeyCommand.FlyLevelView]),
        ("The Storyteller", [KeyCommand.StoryMove]),
    ];

    /// <summary>One line of the list: a group's heading (<see cref="Command"/> null) or a command.</summary>
    public readonly record struct Row(string Heading, KeyCommand? Command);

    /// <summary>The list, top to bottom: each group's heading and its commands.</summary>
    public static readonly Row[] Rows =
        [.. Groups.SelectMany(g => new[] { new Row(g.Heading, null) }.Concat(g.Commands.Select(c => new Row("", c))))];

    /// <summary>The list box the rows fill: the Fixes page's (the load screen's, 193,106 246 x 312).</summary>
    public const int BoxX = FixesPage.BoxX, BoxY = FixesPage.BoxY, BoxW = FixesPage.BoxW, BoxH = FixesPage.BoxH;
    /// <summary>One row's height, and where the first row's foot sits.</summary>
    public const int RowH = 17, TopY = BoxY + BoxH - RowH - 4;
    /// <summary>The room over a group's heading (the Fixes page's).</summary>
    public const int HeadingGap = FixesPage.HeadingGap;
    /// <summary>The name column's width, and the three cells' left edges and width.</summary>
    public const int NameW = 94, CellX = BoxX + 98, CellW = 48;
    /// <summary>The box for the hovered command's description, at the foot of the list box, and the line under it that says what a waiting cell takes.</summary>
    public const int HintY = BoxY + 4, HintH = 44, PromptH = 12;
    /// <summary>The lowest a row's foot may sit and still be on show: the top of the hint box.</summary>
    public const int Floor = HintY + HintH;

    /// <summary>Our buttons along the bottom: **defaults** beside 1.1's Cancel and OK.</summary>
    public const string DefaultsButton = "kbdefaultsbutton", OkButton = "kbokbutton", CancelButton = "kbcancelbutton";
    /// <summary>The scroll bar and its slider: the load screen's own, where the Fixes page has them.</summary>
    public const string BarName = "kbscrollbar", SliderName = "kbscrollslider";

    /// <summary>A cell's control name.</summary>
    public static string Cell(int row, int col) => $"kbcell{row}_{col}";
    /// <summary>A row's name box (a command's name, or a heading).</summary>
    public static string Label(int row) => $"kbname{row}";

    /// <summary>Where a row's foot sits with the list at its top: <see cref="RowH"/> a row, <see cref="HeadingGap"/> more over each heading but the first.</summary>
    public static int RowY(int index)
    {
        int y = TopY - index * RowH;
        for (int i = 1; i <= index && i < Rows.Length; i++)
            if (Rows[i].Command is null) y -= HeadingGap;
        return y;
    }

    /// <summary>How far the rows are raised with the list scrolled to <paramref name="scroll"/>.</summary>
    public static int LiftAt(int scroll) => TopY - RowY(Math.Clamp(scroll, 0, Rows.Length - 1));

    /// <summary>Whether a row is in the box with the list scrolled to <paramref name="scroll"/>.</summary>
    public static bool Fits(int index, int scroll) => index >= scroll && index < Rows.Length && RowY(index) + LiftAt(scroll) >= Floor;

    /// <summary>How many rows are on show with the list scrolled to <paramref name="scroll"/>.</summary>
    public static int ShownAt(int scroll)
    {
        int n = 0;
        while (Fits(scroll + n, scroll)) n++;
        return n;
    }

    /// <summary>The furthest the list scrolls: the first row from which the last is on show.</summary>
    public static int ScrollEnd
    {
        get
        {
            int s = 0;
            while (s < Rows.Length - 1 && !Fits(Rows.Length - 1, s)) s++;
            return s;
        }
    }

    /// <summary>The page's layout, in the `.nui` language: the Fixes page's frame and list box, a name and three cells a command.</summary>
    public NuiPane Layout()
    {
        var nui = new StringBuilder();
        nui.AppendLine("name=vitomirkeys");
        nui.AppendLine("pos=0,0");
        foreach (var (mat, qx, qy, w, h) in FixesPage.FrameTiles) nui.AppendLine($"quad mat={mat} pos={qx},{qy} size={w},{h}");
        nui.AppendLine($"quad mat=consoleline.tga pos={FixesPage.TitleX},{FixesPage.TitleY} size=128,16");
        nui.AppendLine($"quad mat=consoleline2.tga pos={FixesPage.TitleX + 128},{FixesPage.TitleY} size=128,16");
        nui.AppendLine($"element textbox name=kbtitle pos={FixesPage.TitleX},{FixesPage.TitleY} size=256,16 font=morpheus15 textboxflags=0x03 maxitems=1 tbtextcolor={FixesPage.Gold}");
        nui.AppendLine($"element scrollslider name={SliderName} pos={FixesPage.BarX},{FixesPage.BarY} size=8,8 bitmap=statusbarslider");
        nui.AppendLine($"element scrollbar name={BarName} pos={FixesPage.BarX},{FixesPage.BarY} size=8,{FixesPage.BarH} flags=0x02 vertical=1 slider={SliderName}");
        for (int row = 0; row < Rows.Length; row++)
        {
            int y = RowY(row);
            if (Rows[row].Command is null)
            {
                nui.AppendLine($"element textbox name={Label(row)} pos={BoxX + 2},{y} size={BoxW - 4},{RowH} font=defaultfont10 textboxflags=0x02 maxitems=1 tbtextcolor={FixesPage.Gold}");
                continue;
            }
            nui.AppendLine($"element textbox name={Label(row)} pos={BoxX + 4},{y} size={NameW},{RowH} font=defaultfont10 textboxflags=0x02 maxitems=1");
            for (int col = 0; col < KeyBindings.MaxBindings; col++)
                nui.AppendLine($"element button name={Cell(row, col)} pos={CellX + col * (CellW + 2)},{y} size={CellW},{RowH} font=defaultfont10");
        }
        nui.AppendLine($"element textbox name=kbhint pos={BoxX + 4},{HintY + PromptH} size={BoxW - 8},{HintH - PromptH} font=defaultfont10 textboxflags=0x04");
        nui.AppendLine($"element textbox name=kbprompt pos={BoxX + 4},{HintY} size={BoxW - 8},{PromptH} font=defaultfont10 textboxflags=0x02 maxitems=1 tbtextcolor={FixesPage.Gold} {MenuText.FaceKey}={MenuText.SentenceWord}");
        nui.AppendLine($"element button name={OkButton} pos=332,12 size=91,41 flags=0x02 rollover=opsdisadvokbuttonover depressed=opsdisadvokbuttondep");
        nui.AppendLine($"element button name={CancelButton} pos=220,12 size=91,41 flags=0x02 rollover=opsdisadvcancelbuttonover depressed=opsdisadvcancelbuttondep");
        nui.AppendLine($"element button name={DefaultsButton} pos=129,20 size=91,34 font=morpheus20");
        return FixesPage.Spaced(NuiPane.Parse(nui.ToString(), File + ".nui"), "kbhint");
    }

    /// <summary>The page's pane, built on first use from <see cref="Layout"/>.</summary>
    public MenuPane Pane => menus.Pane(File, Layout);

    /// <summary>The keys being edited.</summary>
    public KeyBindings Editing { get; private set; } = new();
    /// <summary>The first row on show (ours, the list scrolls a row at a time).</summary>
    public int Scroll { get; private set; }
    /// <summary>The cell last chosen: its command and which of the three it is.</summary>
    public (KeyCommand Command, int Col)? Chosen { get; private set; }
    /// <summary>Whether the page is waiting for a key for <see cref="Chosen"/>: the host hands it the next one (<see cref="Capture"/>).</summary>
    public bool Capturing { get; private set; }
    /// <summary>Where OK writes, the player's own <c>vitomir.ini</c> unless a test says otherwise.</summary>
    public string? SavePath { get; set; }
    /// <summary>The new keys, for the host to put in force (the page does not write <see cref="KeyBindings.Current"/> itself).</summary>
    public event Action<KeyBindings>? Changed;

    /// <summary>What a cell says while it waits for a key (ours).</summary>
    public const string Waiting = "?";
    /// <summary>What an empty cell says, so there is something to click (ours).</summary>
    public const string Empty = "-";
    /// <summary>The line at the foot of the hint box while a cell waits (ours).</summary>
    public const string Prompt = "Press a key to bind it, or Escape to unmap it.";
    /// <summary>What the hint box adds while a cell waits: how to leave it alone.</summary>
    public const string LeaveIt = "Click the cell again to leave it as it was.";

    /// <summary>Opens the page on a layer of its own, with the keys in force now and the list at its top.</summary>
    public void Open()
    {
        Editing = KeyBindings.Current.Clone();
        Chosen = null;
        Capturing = false;
        var pane = Pane;
        pane.Texts["kbtitle"] = Title;
        pane.Texts[DefaultsButton] = "defaults";
        pane.Texts["kbhint"] = "";
        pane.Texts["kbprompt"] = "";
        for (int row = 0; row < Rows.Length; row++)
            pane.Texts[Label(row)] = Rows[row].Command is { } c ? KeyBindings.Of(c).Name : Rows[row].Heading;
        Bar?.Release();
        ScrollTo(0);
        Fill();
        menus.PushLayer();
        menus.Show(pane);
    }

    /// <summary>Every cell from <see cref="Editing"/>.</summary>
    void Fill()
    {
        var pane = Pane;
        for (int row = 0; row < Rows.Length; row++)
        {
            if (Rows[row].Command is not { } command) continue;
            var chords = Editing[command];
            for (int col = 0; col < KeyBindings.MaxBindings; col++)
                pane.Texts[Cell(row, col)] = Capturing && Chosen == (command, col) ? Waiting
                                           : col < chords.Count ? chords[col].Short : Empty;
        }
        pane.Texts["kbprompt"] = Capturing ? Prompt : "";
    }

    /// <summary>A row's controls: its name box, and a command's three cells.</summary>
    static IEnumerable<string> Controls(int row) =>
        Rows[row].Command is null ? [Label(row)] : new[] { Label(row) }.Concat(Enumerable.Range(0, KeyBindings.MaxBindings).Select(c => Cell(row, c)));

    // ---- the scroll (as the Fixes page's, FixesPageScroll) ------------------------------------------------------

    ScrollBar? bar;

    /// <summary>The page's scroll bar and slider (the library's), made from the pane the first time.</summary>
    public ScrollBar? Bar => bar ??= ScrollBar.Of(Pane.Nui, BarName);

    /// <summary>The list scrolled to <paramref name="rows"/>: the rows in the box raised into it and shown, the rest off the pane.</summary>
    public void ScrollTo(int rows)
    {
        Scroll = Math.Clamp(rows, 0, ScrollEnd);
        if (Bar is { } b)
        {
            b.SetRange(-ScrollEnd, 0);
            b.SetValue(-Scroll);
        }
        Lay();
    }

    void Lay()
    {
        var pane = Pane;
        int lift = LiftAt(Scroll);
        for (int row = 0; row < Rows.Length; row++)
        {
            bool on = Fits(row, Scroll);
            foreach (var id in Controls(row))
            {
                pane.Lift[id] = lift;
                if (on) pane.Hidden.Remove(id); else pane.Hidden.Add(id);
            }
        }
        if (Bar is { } b && pane.Element(SliderName) is { } slider)
            pane.Lift[SliderName] = b.Slider.Y - slider.Pos[1];
    }

    /// <summary>Scrolls just far enough that a command's row is in the box.</summary>
    public void Reveal(KeyCommand command)
    {
        int index = RowOf(command);
        if (index < 0) return;
        if (index < Scroll) ScrollTo(index);
        else if (!Fits(index, Scroll))
        {
            int s = Scroll;
            while (s < index && !Fits(index, s)) s++;
            ScrollTo(s);
        }
    }

    /// <summary>The left button down on the page's scroll bar: the track pages, the slider is caught. Answers whether it was the bar's.</summary>
    public bool Press(NuiElement control, float y)
    {
        if (Bar is not { } b) return false;
        if (control.Name.Equals(SliderName, StringComparison.OrdinalIgnoreCase))
        {
            if (ScrollEnd > 0) b.Take(y);
            return true;
        }
        if (!control.Name.Equals(BarName, StringComparison.OrdinalIgnoreCase)) return false;
        ScrollTo(Scroll + (b.PageAt(y) < 0 ? -ShownAt(Scroll) : ShownAt(Scroll)));
        return true;
    }

    /// <summary>Each frame: a held slider follows the pointer and the list follows it.</summary>
    public void Drag(float y)
    {
        if (Bar is not { Held: true } b || !b.DragTo(y)) return;
        Scroll = Math.Clamp(-b.Value, 0, ScrollEnd);
        Lay();
    }

    /// <summary>The button up: a held slider let go.</summary>
    public void Release() => Bar?.Release();

    /// <summary>The wheel on the page: a box's worth a turn.</summary>
    public void Wheel(int turn)
    {
        if (turn != 0) ScrollTo(Scroll + (turn > 0 ? -ShownAt(Scroll) : ShownAt(Scroll)));
    }

    // ---- the cells ------------------------------------------------------------------------------------------------

    /// <summary>The command a cell stands for, or null.</summary>
    public (KeyCommand Command, int Col)? CellAt(string control)
    {
        if (!control.StartsWith("kbcell", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = control[6..].Split('_');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int row) || !int.TryParse(parts[1], out int col)) return null;
        return row >= 0 && row < Rows.Length && Rows[row].Command is { } c ? (c, col) : null;
    }

    /// <summary>The row of the list a command is on.</summary>
    public static int RowOf(KeyCommand command) => Array.FindIndex(Rows, r => r.Command == command);

    /// <summary>A button released over itself on the page.</summary>
    public void Clicked(string button)
    {
        switch (button.ToLowerInvariant())
        {
            case OkButton: Ok(); return;
            case CancelButton: Cancel(); return;
            case DefaultsButton:
                Capturing = false;
                Chosen = null;
                Editing = new KeyBindings();
                Fill();
                Pane.Texts["kbhint"] = "Every key back to 1.1's own.";
                return;
        }
        if (CellAt(button) is { } cell)
        {
            if (Capturing && Chosen == cell)
            {
                CancelCapture();                         // the waiting cell again: left as it was
                return;
            }
            Chosen = cell;
            Capturing = true;
            Fill();
            Pane.Texts["kbhint"] = $"{Described(KeyBindings.Of(cell.Command).What)} {LeaveIt}";
        }
    }

    /// <summary>
    /// A command's description as the hint box shows it: without the notes in brackets that are for the code's
    /// readers — a name in the code (<c>(ours, KeyBindings.KeypadZoom)</c>).
    /// </summary>
    public static string Described(string what)
    {
        string shown = Regex.Replace(what, @"\s*\([^()]*(?:0x[0-9a-fA-F]|\bours\b|[A-Z][A-Za-z]*\.[A-Z])[^()]*\)", "");
        return Regex.Replace(shown, @"\s+([.,;:])", "$1").Trim();
    }

    /// <summary>The description of the row under the pointer, for the hint box (called each frame by the front end).</summary>
    public void Hover(MenuPane pane, string? control)
    {
        if (Capturing || control is null) return;
        int? row = null;
        if (control.StartsWith("kbname", StringComparison.OrdinalIgnoreCase) && int.TryParse(control[6..], out int r)) row = r;
        else if (CellAt(control) is { } cell) row = RowOf(cell.Command);
        if (row is { } at && at >= 0 && at < Rows.Length && Rows[at].Command is { } c)
            pane.Texts["kbhint"] = Described(KeyBindings.Of(c).What);
    }

    /// <summary>Stops waiting with nothing bound: the cell back as it was.</summary>
    public void CancelCapture()
    {
        Capturing = false;
        Fill();
        Pane.Texts["kbhint"] = "";
    }

    /// <summary>
    /// **Escape while a cell waits: the cell unmapped** (ours) — its chord taken off the command, the ones after
    /// it moving up, as the **clear** button did.
    /// </summary>
    public void Unmap()
    {
        if (!Capturing || Chosen is not var (cmd, col)) return;
        Capturing = false;
        var list = Editing[cmd].ToList();
        if (col < list.Count) list.RemoveAt(col);
        Editing.Set(cmd, list);
        Fill();
        Pane.Texts["kbhint"] = $"{KeyBindings.Of(cmd).Name}: {Describe(Editing[cmd])}";
    }

    /// <summary>
    /// The key or button the host read while the page was waiting. Escape unmaps the cell (<see cref="Unmap"/>). A
    /// chord another command has asks first: OK takes it from there, Cancel keeps it on both.
    /// </summary>
    public void Capture(KeyChord chord)
    {
        if (!Capturing || Chosen is not var (cmd, col)) return;
        if (chord.Input == KeyNames.Escape) { Unmap(); return; }
        Capturing = false;
        var others = Editing.BoundTo(chord).Where(c => c != cmd).ToList();
        if (others.Count == 0)
        {
            Bind(cmd, col, chord, []);
            return;
        }
        string names = string.Join(" and ", others.Select(c => KeyBindings.Of(c).Name));
        Fill();
        menus.Message(Title, $"{chord} is {names}'s as well. OK moves it here; Cancel keeps it on both.", ok: true, cancel: true,
            result => Bind(cmd, col, chord, result == 1 ? others : []));
    }

    void Bind(KeyCommand cmd, int col, KeyChord chord, List<KeyCommand> takeFrom)
    {
        foreach (var other in takeFrom) Editing.Set(other, Editing[other].Where(b => b != chord));
        var list = Editing[cmd].Where(b => b != chord).ToList();
        if (col < list.Count) list[col] = chord; else list.Add(chord);
        Editing.Set(cmd, list);
        Fill();
        Pane.Texts["kbhint"] = $"{KeyBindings.Of(cmd).Name}: {Describe(Editing[cmd])}";
    }

    static string Describe(IReadOnlyList<KeyChord> chords) => chords.Count == 0 ? "no key" : string.Join(", ", chords);

    /// <summary>OK (ours): every command written to <c>[keys]</c>, put in force at once, and the page put away.</summary>
    public void Ok()
    {
        try { Editing.Save(SavePath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Changed?.Invoke(Editing);
        Close();
    }

    /// <summary>Cancel, or Escape when not waiting for a key: nothing kept.</summary>
    public void Cancel() => Close();

    void Close()
    {
        Capturing = false;
        Bar?.Release();
        if (menus.IsUp(Pane)) menus.PopLayer();
    }
}
