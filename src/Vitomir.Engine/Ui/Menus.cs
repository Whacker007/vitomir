// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The interface library's generic controls as the front-end menus use them: a pane with buttons, a
// text list, text boxes and an edit field, each with the live state the engine keeps on the control
// itself. Read out of `vuiMenu`/`vuiControl` — the button's
// state word and its press/release rule, the text list's rows and scroll, the edit field's caret —
// and written for the main menu, the escape menu and the save and load screens.
// The shop's list (`BuySellPane`) predates this and keeps its own copy of the same rules.
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>A button's state word — which bitmap it draws, too.</summary>
public enum ButtonState
{
    /// <summary>The `bitmap=` art, or nothing when the button has none.</summary>
    Normal = 0,
    /// <summary>The pointer is on it: `rollover=`.</summary>
    Rollover = 1,
    /// <summary>Pressed and still held on it: `depressed=`. Only a release in this state acts.</summary>
    Pressed = 2,
    /// <summary>`greyed=`, and the callbacks are blocked (flag 0x80000).</summary>
    Greyed = 3,
}

/// <summary>
/// The text box family's line breaking, which the text list and the message
/// box share: an item is cut at every newline (`strtok`, so empty lines vanish), and with word wrap
/// on (`textboxflags` bit 2) each piece is broken to the box's width after the last character that
/// is followed by a space, tab, return or newline — or, with no such place, just before the
/// character that overflowed. The spaces at a break are dropped from the front of the next line.
/// </summary>
public static class TextBoxText
{
    /// <summary>The delimiters a break may follow (space, return, tab, newline).</summary>
    static bool Delimiter(char c) => c is ' ' or '\r' or '\t' or '\n';

    /// <summary>Every line an item becomes, top line first.</summary>
    public static List<string> Lines(string text, bool wrap, int width, Func<char, int> advance)
    {
        var lines = new List<string>();
        foreach (var piece in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!wrap) { lines.Add(piece); continue; }
            Wrap(piece, width, advance, lines);
        }
        return lines;
    }

    /// <summary>The break for one piece.</summary>
    public static void Wrap(string s, int width, Func<char, int> advance, List<string> into)
    {
        int start = 0, breakAt = 0, i = 0, run = 0;
        while (i < s.Length)
        {
            run += advance(s[i]);
            if (run > width)
            {
                if (breakAt == start) breakAt = i - 1;
                int count = Math.Max(0, breakAt - start) + 1;
                count = Math.Min(count, s.Length - start);
                into.Add(s.Substring(start, count));
                int next = start + count;
                while (next < s.Length && Delimiter(s[next])) next++;
                // A single character wider than the box would loop for ever in the original; here
                // it is let through on a line of its own.
                if (next <= start) next = start + 1;
                start = breakAt = i = next;
                run = 0;
                continue;
            }
            if (!Delimiter(s[i]) && i + 1 < s.Length && Delimiter(s[i + 1])) breakAt = i;
            i++;
        }
        // What is left always goes in — even nothing, when a break's spaces ran to the end.
        into.Add(s[Math.Min(start, s.Length)..]);
    }
}

/// <summary>
/// A text list (`textlistbox`) as the save and load screens use it: rows of one or
/// more lines laid down from the top of the box, the chosen row red, the hovered row yellow, a
/// scroll by whole pages and an eight-pixel slider that only says where the view is.
/// </summary>
public sealed class MenuList
{
    /// <summary>One row: its text, the number it carries, its lines, and where it sits in the box.</summary>
    /// <param name="Y">The row's bottom edge, box-local and counting up — negative below the box.</param>
    /// <param name="Colour">The row's own colour when it is neither chosen nor hovered — white, unless
    /// the adder gave one (the Storyteller's lists colour the players' rows and the reached scenes,
    /// both screens).</param>
    public sealed record Row(string Text, int Data, IReadOnlyList<string> Lines, int Y, int Height, uint Colour = RowColour);

    readonly List<Row> rows = [];
    /// <summary>The rows out of the choosing: their flags' bit 0, set by the choose.</summary>
    readonly HashSet<int> disabled = [];

    public NuiElement Element { get; }
    public NfdFont Font { get; }
    public int Width => Element.Size.Length > 1 ? Element.Size[0] : 0;
    public int Height => Element.Size.Length > 1 ? Element.Size[1] : 0;
    /// <summary>`itemspacing`, the gap between rows.</summary>
    public int Spacing { get; }
    public NuiPane.TextBoxFlags Flags { get; }

    public IReadOnlyList<Row> Rows => rows;
    /// <summary>The chosen row, drawn red; -1 for none.</summary>
    public int Current { get; private set; } = -1;
    /// <summary>The row under the pointer, drawn yellow; -1 for none.</summary>
    public int Hovered { get; private set; } = -1;
    /// <summary>How far the rows are lifted: 0 shows the first row at the top.</summary>
    public int Scroll { get; private set; }

    /// <summary>The chosen row's red and the hovered row's yellow (the constructor's).</summary>
    public const uint CurrentColour = 0xFFFF0000u, HoverColour = 0xFFFFFF00u, RowColour = 0xFFFFFFFFu;

    public MenuList(NuiElement element, NfdFont font)
    {
        Element = element;
        Font = font;
        Spacing = element.Get("itemspacing") is { } s && int.TryParse(s, out int n) ? n : 0;
        Flags = NuiPane.TextBox(element);
    }

    /// <summary>A quarter of the font's height, which the scroll range leaves below the last row.</summary>
    int Quarter => Font.LineHeight / 4;

    /// <summary>How far the rows can be lifted: the last row brought up to a quarter-line above the bottom.</summary>
    public int MaxScroll => rows.Count == 0 || rows[^1].Y - Quarter >= 0 ? 0 : Quarter - rows[^1].Y;

    /// <summary>Everything gone: rows, scroll, the hovered and chosen rows.</summary>
    public void Clear()
    {
        rows.Clear();
        disabled.Clear();
        Scroll = 0;
        Current = Hovered = -1;
    }

    /// <summary>
    /// Whether a row is out of the choosing (its flags' bit 0): never hovered, so never
    /// pressed, and never chosen by its index. It is drawn in its own colour —
    /// the caller greys it (the Storyteller's reached steps, 0xff949494).
    /// </summary>
    public bool Disabled(int index) => disabled.Contains(index);

    /// <summary>
    /// A row by its index taken out of the choosing (the nth row; nothing
    /// with no such row): the Storyteller's steps already reached and the
    /// host's own row of the cast list.
    /// </summary>
    public void Disable(int index)
    {
        if (index >= 0 && index < rows.Count) disabled.Add(index);
    }

    /// <summary>A row by its index put back in the choosing (which nothing in 1.1 calls).</summary>
    public void Enable(int index) => disabled.Remove(index);

    /// <summary>
    /// Whether the whole list is only shown (bit 0x10): nothing is hovered
    /// (skips the list), so nothing is pressed. Setting it also forgets the hovered and the
    /// chosen row; clearing it leaves them. The lobby's three outputs — the room's talk, the
    /// room's status and the game's status — are made so once.
    /// </summary>
    public bool Inert
    {
        get => inert;
        set
        {
            inert = value;
            if (value) Current = Hovered = -1;
        }
    }
    bool inert;

    /// <summary>
    /// A row added at the bottom: its lines broken, its height their count in
    /// the font's lines, and its bottom `itemspacing` under the one before — the first against the
    /// top of the box. Adding scrolls to the end, as the engine's add does; the menus choose row 0
    /// straight afterwards, which scrolls back.
    /// </summary>
    public void Add(string text, int data, uint colour = RowColour)
    {
        // Streamer mode: an address, the user's folder or a secret in a row is hidden as it is laid out (the lobby's games, its
        // room's talk and status, every list of the menus).
        var lines = TextBoxText.Lines(Vitomir.Platform.Presentation.StreamerMode.Shown(text), Flags.HasFlag(NuiPane.TextBoxFlags.WordWrap), Width,
                                      c => Font[c].Advance);
        int h = lines.Count * Font.LineHeight;
        int y = rows.Count == 0 ? Height - h : rows[^1].Y - Spacing - h;
        rows.Add(new Row(text, data, lines, y, h, colour));
        Scroll = MaxScroll;
    }

    /// <summary>Scroll to a value, held to the range.</summary>
    public void ScrollTo(int value) => Scroll = Math.Clamp(value, 0, MaxScroll);

    /// <summary>
    /// Choose a row as the menus do on opening: it becomes current, and the view moves to
    /// put it at the top — which, for a row whose bottom is below the box, asks for a negative
    /// scroll and so lands on the top of the list instead.
    /// </summary>
    public void Select(int index)
    {
        if (index < 0 || index >= rows.Count || disabled.Contains(index)) return;
        Current = index;
        var row = rows[index];
        ScrollTo(row.Y > 0 ? Height - row.Height - row.Y : row.Y - row.Height);
    }

    /// <summary>A page up (−1) or down (+1): the wheel and a click on the track.</summary>
    public void Page(int direction) => ScrollTo(Scroll + direction * Height);

    /// <summary>Whether a row is on screen at the current scroll (the draw's own test).</summary>
    public bool Visible(int index)
    {
        var row = rows[index];
        return row.Y + Scroll < Height && row.Y + Scroll + row.Height >= 0;
    }

    /// <summary>
    /// The hover, with the pointer at box-local <paramref name="localY"/> (counting up),
    /// or null when the pointer is not on the list: the first visible row the pointer is strictly
    /// inside. Across is not tested.
    /// </summary>
    public void HoverAt(float? localY)
    {
        Hovered = -1;
        if (inert || localY is not { } y) return;
        for (int i = 0; i < rows.Count; i++)
        {
            if (!Visible(i)) continue;
            float bottom = rows[i].Y + Scroll;
            if (bottom < y && y < bottom + rows[i].Height) { if (!disabled.Contains(i)) Hovered = i; return; }
        }
    }

    /// <summary>A press: the hovered row becomes the chosen one. It does not scroll.</summary>
    public bool Press()
    {
        if (Hovered < 0) return false;
        Current = Hovered;
        return true;
    }

    /// <summary>What the chosen row carries, or -1 with nothing chosen.</summary>
    public int CurrentData => Current >= 0 && Current < rows.Count ? rows[Current].Data : -1;

    /// <summary>
    /// Where each line of a row goes, box-local: x from the left (centred when the box says so,
    /// `trunc((w − inset − lineWidth) × 0.5)`), y the line's baseline counting up — the top line
    /// highest, because the engine keeps a row's lines newest-first and draws index 0 at the row's
    /// bottom.
    /// </summary>
    public IEnumerable<(string Text, int X, int Y)> LinesOf(int index)
    {
        var row = rows[index];
        int n = row.Lines.Count;
        for (int k = 0; k < n; k++)
        {
            string line = row.Lines[k];
            int x = Flags.HasFlag(NuiPane.TextBoxFlags.Centre) ? (Width - Font.Measure(line)) / 2 : 0;
            yield return (line, x, row.Y + Scroll + (n - 1 - k) * Font.LineHeight);
        }
    }

    /// <summary>
    /// The slider's y in the pane: the range is `(last row − a quarter line) .. 0` and
    /// the value minus the scroll, so the slider sits at the **top** of its track for a list that
    /// overflows and is at its start, and at the **bottom** for one that fits.
    /// </summary>
    public int SliderY(NuiElement bar, NuiElement slider)
    {
        int barY = bar.Pos.Length > 1 ? bar.Pos[1] : 0;
        int travel = (bar.Size.Length > 1 ? bar.Size[1] : 0) - (slider.Size.Length > 1 ? slider.Size[1] : 0);
        int min = rows.Count == 0 ? 0 : rows[^1].Y - Quarter, max = 0;
        if (min >= max) return barY;
        int value = Math.Clamp(-Scroll, min, max);
        return (int)((value - min) / (float)(max - min) * travel + barY);
    }
}

/// <summary>What an edit field takes (the cases of its key handler).</summary>
public enum EditAccepts
{
    Any = 0,
    Digits = 1,
    Letters = 2,
    LettersAndDigits = 3,
}

/// <summary>
/// An edit field (`edit`): typed characters on the end, backspace off the end, and a
/// `|` after the text that blinks every half second while the field has the keyboard.
/// A line wider than the box is slid left so its end stays in view.
/// </summary>
public sealed class MenuEdit
{
    public NuiElement Element { get; }
    public NfdFont Font { get; }
    public string Text { get; private set; } = "";
    /// <summary>The most it takes: 511 unless the owner says.</summary>
    public int Max { get; private set; } = 511;
    /// <summary>The caret character (set to `|` by the constructor).</summary>
    public const char Caret = '|';
    /// <summary>Whether the caret is drawn this moment.</summary>
    public bool CaretOn { get; private set; }
    /// <summary>The slide left for a long line, 0 or negative.</summary>
    public int Offset { get; private set; }
    int nextBlink;

    public MenuEdit(NuiElement element, NfdFont font)
    {
        Element = element;
        Font = font;
    }

    int Width => Element.Size.Length > 1 ? Element.Size[0] : 0;

    /// <summary>The limit, cutting what is there to it (at most 511).</summary>
    public void Limit(int max)
    {
        Max = Math.Clamp(max, 0, 0x1ff);
        if (Text.Length > Max) Text = Text[..Max];
        Relayout();
    }

    /// <summary>The text set outright, cut to one fewer than the limit (<see cref="EditMask.SetCut"/>).</summary>
    public void Set(string text)
    {
        Text = EditMask.SetCut(text, Max);
        Relayout();
    }

    /// <summary>
    /// Which characters the field takes: set by the screen, 0 from the constructor.
    /// The pack's drop-cash box asks for <see cref="EditAccepts.Digits"/>.
    /// </summary>
    public EditAccepts Accepts { get; set; }

    /// <summary>
    /// One typed character (the key handler's default case): a character the field's
    /// <see cref="Accepts"/> refuses goes nowhere, control characters are refused, and a full field
    /// takes nothing. Backspace takes the last character off.
    /// </summary>
    public void Type(char c)
    {
        if (c == '\b')
        {
            if (Text.Length > 0) Text = Text[..^1];
            Relayout();
            return;
        }
        if (!Takes(Accepts, c)) return;
        if (char.IsControl(c) || c > 0xFF || Text.Length >= Max) return;
        Text += c;
        Relayout();
    }

    /// <summary>
    /// The default case's character test by mode: 1 is the C runtime's
    /// `isdigit` (class bit 4), 2 its `isalpha` (0x103) and 3 its `isalnum`
    /// (0x107), all in the "C" locale, so only ASCII answers; 0 takes anything.
    /// </summary>
    public static bool Takes(EditAccepts mode, char c) => mode switch
    {
        EditAccepts.Digits => c is >= '0' and <= '9',
        EditAccepts.Letters => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z',
        EditAccepts.LettersAndDigits => c is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z',
        _ => true,
    };

    /// <summary>
    /// Ctrl+V (the case for character 0x16): the clipboard's text (`CF_TEXT`) up to its first
    /// line break, looking at no more characters than the field's limit; each one that is not a
    /// control character (the C runtime's `iscntrl`, so a `\r` is dropped)
    /// goes on the end while there is room. **The field's <see cref="Accepts"/> is not asked** —
    /// the paste arm never tests it, so letters paste into the digits-only drop-cash box.
    /// </summary>
    public void Paste(string clip)
    {
        Text = Pasted(Text, clip, Max);
        Relayout();
    }

    /// <summary>The arm's rule on its own: <paramref name="text"/> with <paramref name="clip"/> pasted on the end, for a field that takes <paramref name="max"/>.</summary>
    public static string Pasted(string text, string clip, int max)
    {
        int looked = 0;
        foreach (char c in clip)
        {
            if (c is '\0' or '\n' || looked >= max) break;
            looked++;
            if (c < 0x20 || c == 0x7f || c > 0xFF) continue;           // above 0xFF: not in CF_TEXT (ours)
            if (text.Length < max) text += c;
        }
        return text;
    }

    /// <summary>The field loses the keyboard: the caret goes off.</summary>
    public void Unfocus() => CaretOn = false;

    /// <summary>The caret's blink, while the field has the keyboard: every 500 ms.</summary>
    public void Blink(int nowMs, bool focused)
    {
        if (!focused) return;
        if (nextBlink < nowMs)
        {
            CaretOn = !CaretOn;
            nextBlink = nowMs + 500;
        }
    }

    /// <summary>A password field (style bit 0): drawn as stars (<see cref="EditMask.Shown"/>).</summary>
    public bool Password { get; set; }

    /// <summary>
    /// An address field (ours): while streamer mode is on (<see cref="Vitomir.Platform.Presentation.StreamerMode"/>) it draws
    /// a dot for each character typed, as a password field draws a star; the text itself is what is typed. The join screen's address box's.
    /// </summary>
    public bool Address { get; set; }

    /// <summary>The text as drawn, without the caret: stars for a password, dots for an address while streamer mode is on, else itself.</summary>
    string Drawn => Address && !Password ? Vitomir.Platform.Presentation.StreamerMode.Dots(Text) : EditMask.Shown(Text, Password);

    /// <summary>What is drawn: the text (stars for a password, dots for an address on a stream), and the caret after it while it is on (`%s%c`).</summary>
    public string Shown => CaretOn ? Drawn + Caret : Drawn;

    void Relayout()
    {
        int caret = Font[Caret].Advance, text = Font.Measure(Drawn);
        Offset = text + caret > Width ? Width - text - caret : 0;
    }
}

/// <summary>
/// An image array's pictures and which one it shows (`imagearray`: one routine sets one, another
/// picks one) — the map's markers.
/// </summary>
public readonly record struct MenuImages(IReadOnlyList<string> Images, int Current)
{
    public string? Shown => Current >= 0 && Current < Images.Count ? Images[Current] : null;
}

/// <summary>
/// A scroll bar that stands for a number rather than scrolling a list — the Options screens'
/// gamma, volume and detail bars (`scrollbar vertical=0 slider=…`). Read out of the scroll bar's
/// own code for the Options screen:
///
/// - **A continuous bar** (no `quanta=`) holds a value between a minimum and a maximum its owner
///   sets. The slider stands at `(value − min) / (max − min)` of
///   the travel, the travel being the bar's width less the slider's (the link in the bar's parse:
///   the bar's width less the slider's). Dragging it moves it with the pointer, held to the track, and the
///   value is read back as `min + trunc(offset / travel × (max − min))` — truncated — with the
///   owner's callback run only when that number changed.
/// - **A quantized bar** (`quanta=n`) has n + 1 stops, 0 to n, and the slider stands at
///   `travel / n × stop`. A drag moves it **one stop at a time**, toward whichever
///   neighbouring stop the pointer is over (the drag's first branch), and calls nobody: the owner
///   reads the stop when it wants it.
/// - Setting a value holds it to the range and never calls the owner back.
/// </summary>
public sealed class MenuBar
{
    public NuiElement Element { get; }
    public NuiElement Slider { get; }
    /// <summary>The range; 0..100 until the owner says (the Options screens always do).</summary>
    public int Min { get; private set; }
    public int Max { get; private set; } = 100;
    /// <summary>`quanta=`: the number of steps, or 0 for a continuous bar.</summary>
    public int Quanta { get; }
    /// <summary>The value, or the stop on a quantized bar.</summary>
    public int Value { get; private set; }
    /// <summary>
    /// Where a dragged slider stands, from the bar's left edge — **and where it stays once let go**.
    /// The drag moves the slider itself and reads the value from where it stands; nothing
    /// puts it back until the owner sets a value, which the Options screens do
    /// only as a screen opens. Until then Vitomir drew it from the value again on the release —
    /// the value truncated to its step, so on a bar of few steps it jumped back to the left: up to 8 of
    /// the brightness bar's pixels (a step is 125 / 15), 20 on a 1600 x 900 window.
    /// </summary>
    float? dragAt;

    public MenuBar(NuiElement bar, NuiElement slider)
    {
        Element = bar;
        Slider = slider;
        Quanta = bar.Get("quanta") is { } q && int.TryParse(q, out int n) ? Math.Max(0, n) : 0;
    }

    /// <summary>How far the slider travels: the bar's width less its own (set when the two are linked).</summary>
    public int Travel => Math.Max(0, (Element.Size.Length > 0 ? Element.Size[0] : 0) - (Slider.Size.Length > 0 ? Slider.Size[0] : 0));

    /// <summary>The range, and the slider put where the value now says.</summary>
    public void Range(int min, int max)
    {
        Min = min;
        Max = max;
        dragAt = null;
    }

    /// <summary>
    /// A value set by its owner: a continuous bar's held to its range, a quantized bar's
    /// to its stops 0..n.
    /// </summary>
    public void Set(int value)
    {
        Value = Quanta == 0 ? Math.Clamp(value, Math.Min(Min, Max), Math.Max(Min, Max)) : Math.Clamp(value, 0, Quanta);
        dragAt = null;
    }

    /// <summary>Where the slider stands, from the bar's left edge.</summary>
    public int SliderX
    {
        get
        {
            if (dragAt is { } at) return (int)at;
            if (Quanta > 0) return StopX(Value);
            if (Min == Max) return 0;
            return (int)((Value - Min) / (float)(Max - Min) * Travel);
        }
    }

    /// <summary>A quantized bar's stop, as a distance from its left edge: -1 off the ends.</summary>
    int StopX(int stop) => stop < 0 || stop > Quanta || Quanta == 0 ? -1 : (int)(Travel / (float)Quanta * stop);

    /// <summary>
    /// One frame of a drag, the pointer at <paramref name="pointerX"/> from the bar's left
    /// edge and <paramref name="grab"/> where on the slider it was caught. Answers whether the value
    /// changed — which, for a continuous bar, is when its owner hears of it.
    /// </summary>
    public bool Drag(float pointerX, float grab)
    {
        int was = Value;
        if (Quanta > 0)
        {
            int w = Slider.Size.Length > 0 ? Slider.Size[0] : 0;
            int less = StopX(Value - 1), more = StopX(Value + 1);
            if (less >= 0 && pointerX >= less && pointerX <= less + w) Value--;
            else if (more >= 0 && pointerX >= more && pointerX <= more + w) Value++;
            return Value != was;
        }
        float x = Math.Clamp(pointerX - grab, 0, Travel);
        dragAt = x;
        if (Travel > 0) Value = Min + (int)(x / Travel * (Max - Min));
        return Value != was;
    }
}

/// <summary>
/// One pane of the interface with its controls' live state: which buttons are lit, pressed or
/// greyed, what the text boxes say, the lists and the edit field. The pane's `.nui` is the layout;
/// this is everything the engine keeps on the controls themselves.
/// </summary>
public sealed class MenuPane
{
    public NuiPane Nui { get; }
    /// <summary>The file it came from, without the folder (`MainMenu.nui`).</summary>
    public string File { get; }

    public MenuPane(NuiPane nui, string file, Func<string, NfdFont> font)
    {
        Nui = nui;
        File = file;
        foreach (var e in nui.Elements)
        {
            switch (e.Type)
            {
                case "button":
                    Buttons[e.Name] = ButtonState.Normal;
                    break;
                case "checkbox":
                    Checks[e.Name] = false;
                    break;
                case "scrollbar" when e.Get("vertical") == "0" && e.Get("slider") is { } s
                                      && nui.Elements.FirstOrDefault(k => k.Name.Equals(s, StringComparison.OrdinalIgnoreCase)) is { } knob:
                    Bars[e.Name] = new MenuBar(e, knob);
                    break;
                case "textlistbox":
                    Lists[e.Name] = new MenuList(e, font(FontOf(e)));
                    break;
                case "edit":
                    Edits[e.Name] = new MenuEdit(e, font(FontOf(e)));
                    break;
            }
            // An edit field puts itself in the focus ring when it starts, and so does any
            // control with a nonzero `key` (`NuiKeys`); showing the pane gives the
            // ring's first the keyboard.
            if (NuiKeys.InRing(e)) Focus ??= e.Name;
        }
    }

    /// <summary>Whether the pane wears our lettering (<see cref="MenuLettering"/>): it has words of ours on it.</summary>
    public bool Lettered => Worded.Count > 0 || Nui.Elements.Any(e => e.Type == MenuLettering.WordsType);

    HashSet<string>? worded;

    /// <summary>The buttons that have a word of ours on them (<see cref="MenuLettering"/>): drawn by the word, not by a caption.</summary>
    public HashSet<string> Worded => worded ??= new HashSet<string>(
        Nui.Elements.Where(e => e.Type == MenuLettering.WordsType && e.Get("button") is not null).Select(e => e.Get("button")!),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>A control's font: its own `font=`, else `defaultFont` (the post-parse).</summary>
    public static string FontOf(NuiElement e) => e.Get("font") is { Length: > 0 } f ? f : "defaultFont";

    public float X => Nui.Pos.Length > 1 ? Nui.Pos[0] : 0;
    public float Y => Nui.Pos.Length > 1 ? Nui.Pos[1] : 0;

    bool? wholeSheet;

    /// <summary>
    /// Whether the pane's own art is **the whole 640 x 480 sheet** (ours): its quads reach all four edges of the
    /// sheet and have at least its area — the twelve tiles of a full-screen backdrop (the Options screen and its pages,
    /// the save and load screens, the maps, the main menu, the multiplayer screens), and never the escape menu's ankh, a
    /// message box or a plaque. Such a pane hides everything behind it, so while one is drawn the sheet is the whole
    /// screen (<see cref="FrontEnd.WholeSheet"/>, <see cref="UiLayout.InGame"/>).
    /// </summary>
    public bool WholeSheet => wholeSheet ??= Nui.Quads.Count > 0
        && Nui.Quads.Min(q => X + q.Pos[0]) <= 0f && Nui.Quads.Min(q => Y + q.Pos[1]) <= 0f
        && Nui.Quads.Max(q => X + q.Pos[0] + q.Size[0]) >= UiAssets.Width && Nui.Quads.Max(q => Y + q.Pos[1] + q.Size[1]) >= UiAssets.Height
        && Nui.Quads.Sum(q => (long)q.Size[0] * q.Size[1]) >= (long)UiAssets.Width * UiAssets.Height;
    bool? againstTop;

    /// <summary>
    /// Whether the pane's art **runs up to the sheet's top edge** without being the whole sheet (ours): the
    /// escape menus' ankh (<c>escapeMenu</c>, <c>escapeMenuMP</c>: its two upper quads end at 480, the horns
    /// cut there in the art itself) and the console's strip. 1.1's screen ended there, so the cut was its edge.
    /// </summary>
    public bool AgainstTop => againstTop ??= Nui.Quads.Count > 0 && !WholeSheet
        && Nui.Quads.Max(q => Y + q.Pos[1] + q.Size[1]) >= UiAssets.Height;
    /// <summary>`dialog=1`: modal, and on the on-top list (it sets 0x102).</summary>
    public bool Dialog => Nui.Dialog;
    /// <summary>Whether showing it puts it on the on-top list rather than the top layer (flag 0x100).</summary>
    public bool OnTopList => Nui.OnTop || Nui.Dialog;

    /// <summary>Up (flag 1).</summary>
    public bool Shown { get; internal set; }
    /// <summary>Shown and not drawn yet (flag 0x800): its key hook does not run until it has been.</summary>
    public bool JustShown { get; internal set; }

    public Dictionary<string, ButtonState> Buttons { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, MenuList> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, MenuEdit> Edits { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Each checkbox's state, which is also which of its two bitmaps it draws.</summary>
    public Dictionary<string, bool> Checks { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The scroll bars that stand for a value (<see cref="MenuBar"/>), by the bar's name.</summary>
    public Dictionary<string, MenuBar> Bars { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>What each image array shows (the map's markers).</summary>
    public Dictionary<string, MenuImages> Images { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Checkboxes that do not move when pressed (ours: the Fixes page's repairs, which have no switch).</summary>
    public HashSet<string> Locked { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>What each text box says, one item per entry (the message box's body is wrapped when drawn).</summary>
    public Dictionary<string, string> Texts { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Controls taken off the pane (the message box's unwanted OK or Cancel).</summary>
    public HashSet<string> Hidden { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// How far up a control is drawn and hit from where its `.nui` puts it (ours: the Fixes page's rows
    /// and its scroll bar's slider, which the page moves as it scrolls).
    /// </summary>
    public Dictionary<string, int> Lift { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A control's bottom-left corner in the 640 x 480 space: the pane's place, the `.nui`'s, and its <see cref="Lift"/>.</summary>
    public (float X, float Y) At(NuiElement e) =>
        (X + e.Pos[0], Y + e.Pos[1] + (Lift.Count > 0 ? Lift.GetValueOrDefault(e.Name) : 0));
    /// <summary>The control with the keyboard.</summary>
    public string? Focus { get; set; }

    /// <summary>
    /// Tab in an edit field (the case for character 9): the keyboard moves round the pane's
    /// **focus ring** — the edit fields, in the order the file declares them, each of which put
    /// itself in the ring when it was made (a circle kept on the pane, linked
    /// both ways), and any control with a nonzero `key` (<see cref="NuiKeys"/>; no
    /// shipped pane has one). Tab takes the next that is not hidden; with a shift key
    /// down the walk goes **backwards — but it starts from the next field**, so it only
    /// goes back past a hidden one; with the next one shown, Shift+Tab goes forward like Tab (1.1's,
    /// kept). The field left loses its caret. A pane with one field keeps it. Answers
    /// whether the keyboard moved.
    /// </summary>
    public bool Tab(bool shift)
    {
        var ring = NuiKeys.Ring(Nui).Select(e => e.Name).ToList();
        int at = Focus is null ? -1 : ring.FindIndex(n => n.Equals(Focus, StringComparison.OrdinalIgnoreCase));
        // Tab is the edit field's own character; a `key` control has only the base handler, so the keyboard stays on it.
        if (at < 0 || !Edits.ContainsKey(ring[at])) return false;
        int count = ring.Count, next = (at + 1) % count;
        if (next == at) return false;
        while (Hidden.Contains(ring[next]))
        {
            next = shift ? (next - 1 + count) % count : (next + 1) % count;
            if (next == at) return false;
        }
        Edits[ring[at]].Unfocus();
        Focus = ring[next];
        return true;
    }

    public NuiElement? Element(string name) =>
        Nui.Elements.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a point in the 640 x 480 space is on the pane's own art.</summary>
    public bool Covers(float x, float y)
    {
        foreach (var q in Nui.Quads)
            if (x >= X + q.Pos[0] && x < X + q.Pos[0] + q.Size[0] && y >= Y + q.Pos[1] && y < Y + q.Pos[1] + q.Size[1])
                return true;
        return false;
    }

    /// <summary>
    /// The control under a point, or null: the first one declared whose rectangle holds it, among
    /// the ones not hidden. (The panes this is used for have no overlapping controls but a slider
    /// on its own track, and the slider is declared first.)
    /// </summary>
    public NuiElement? ControlAt(float x, float y)
    {
        foreach (var e in Nui.Elements)
        {
            if (Hidden.Contains(e.Name) || e.Pos.Length < 2 || e.Size.Length < 2) continue;
            var (left, bottom) = At(e);
            // A value bar's slider is where its value has moved it (the bar moves the control itself).
            if (e.Type == "scrollslider" && Bars.Values.FirstOrDefault(b => ReferenceEquals(b.Slider, e)) is { } bar)
                (left, bottom) = (X + bar.Element.Pos[0] + bar.SliderX, Y + bar.Element.Pos[1]);
            if (x >= left && x < left + e.Size[0] && y >= bottom && y < bottom + e.Size[1]) return e;
        }
        return null;
    }

    /// <summary>
    /// The per-frame button update: lit while the pointer is on it, held while it stays
    /// pressed and the button is down, dark otherwise — so dragging off a pressed button and back
    /// leaves it lit rather than pressed, and letting go there does nothing.
    /// </summary>
    public void UpdateButtons(string? pointerOn, bool leftHeld)
    {
        foreach (var name in Buttons.Keys.ToList())
        {
            var state = Buttons[name];
            if (state == ButtonState.Greyed) continue;
            bool on = pointerOn is not null && name.Equals(pointerOn, StringComparison.OrdinalIgnoreCase);
            Buttons[name] = !on ? ButtonState.Normal
                          : state == ButtonState.Pressed && leftHeld ? ButtonState.Pressed : ButtonState.Rollover;
        }
    }

    /// <summary>A checkbox's `radiogroup=`, 0 for none.</summary>
    public int GroupOf(string name) =>
        Element(name)?.Get("radiogroup") is { } g && int.TryParse(g, out int n) ? n : 0;

    /// <summary>
    /// A checkbox set: checking one in a radio group clears every other checkbox of the
    /// same group on the pane first, so exactly one of a group can be down.
    /// </summary>
    public void Check(string name, bool on)
    {
        if (!Checks.ContainsKey(name)) return;
        int group = GroupOf(name);
        if (on && group != 0)
            foreach (var other in Checks.Keys.ToList())
                if (!other.Equals(name, StringComparison.OrdinalIgnoreCase) && GroupOf(other) == group) Checks[other] = false;
        Checks[name] = on;
    }

    /// <summary>
    /// A checkbox clicked: a plain one toggles; one in a radio group is only ever
    /// checked, so clicking the one already down leaves it down.
    /// </summary>
    public void ClickCheck(string name)
    {
        if (!Checks.TryGetValue(name, out bool on)) return;
        Check(name, GroupOf(name) != 0 || !on);
    }

    /// <summary>Grey a button, or give it back.</summary>
    public void Grey(string name, bool greyed)
    {
        if (!Buttons.ContainsKey(name)) return;
        Buttons[name] = greyed ? ButtonState.Greyed : ButtonState.Normal;
    }
}
