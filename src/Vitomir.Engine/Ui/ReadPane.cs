// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `ui/ReadTextBox.nui` with a document in it — the letter, journal or book a `BOOK` item opens.
//
// The pane is a **dialog** (`dialog=1`, the only shipped pane that says so), six quads from x=149
// to x=486, a title box, and a body with its scroll bar:
//
// ```
// element scrollslider name=readScrollSlider pos=475,181 size=8,8 bitmap=StatusBarSlider.tga
// element scrollbar    name=readScrollBar    pos=475,181 size=8,268 vertical=1 slider=readScrollSlider
// element textbox name=readTextBox  pos=161,182 size=312,266 textboxflags=0x04 font=defaultFont10
//                                   vscrollbar=readScrollBar itemspacing=8
// element textbox name=mpReadTextTitle pos=196,453 size=252,13 textboxflags=0x01 font=defaultFont10
// ```
//
// The body is the ui library's text box, and the pane fills it as any other: each paragraph added in
// its colour, then the scroll put back at the top. So it is laid out
// as the library lays out a box with `textboxflags` bit 3 clear: **a paragraph is an
// item**, broken into lines by the library's word wrap (bit 2), the first
// against the top and each later one `itemspacing` (8) below the one before, the lines of an item a
// font height apart. The scroll is in pixels, from 0 (the top) to the newest item a
// quarter line above the box's bottom; the wheel over the body turns it a page,
// the box's height; `readScrollBar` shows it and moves it (<see cref="ScrollBar"/>).
// Built; until then the wrap and the line scroll were ours, and the mouse could not scroll.
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>One laid-out line of a document, with the colour of the paragraph it came from.</summary>
public readonly record struct ReadLine(string Text, uint Colour, int Paragraph);

/// <summary>
/// One paragraph as the body lays it out (an item of the text box): its lines, top first, and where
/// it sits before scrolling.
/// </summary>
/// <param name="Y">Its bottom edge, box-local and counting up — negative once below the box.</param>
/// <param name="Height">Its lines times the font's height.</param>
public sealed record ReadItem(int Paragraph, uint Colour, IReadOnlyList<string> Lines, int Y, int Height);

/// <summary>The read-text dialog: a document in the pane's body box, and the box's scroll bar.</summary>
public sealed class ReadPane
{
    public const string PaneName = "ReadTextBox";
    public const string BodyBox = "readTextBox", TitleBox = "mpReadTextTitle";
    public const string ScrollBarName = "readScrollBar";

    /// <summary>The gap between paragraphs when the box declares none.</summary>
    public const int DefaultSpacing = 8;

    public NuiPane Pane { get; }

    /// <summary>`readScrollBar` and its slider, or null for a pane without them.</summary>
    public ScrollBar? Bar { get; }

    /// <summary>What is open, or null for a closed dialog.</summary>
    public NtxEntry? Document { get; set; }

    /// <summary>How far the body is lifted, in pixels: 0 shows the document's top.</summary>
    public int Scroll { get; private set; }

    /// <summary>A character's advance in the body's font (<see cref="Letter"/>); six pixels until it is lettered.</summary>
    public Func<char, int> Advance { get; set; } = _ => 6;

    /// <summary>The font's height (its first field, <c>**</c>): a line, and a quarter of it the scroll's end.</summary>
    public int LineHeight { get; set; } = 12;

    /// <summary>
    /// **The pane laid out with the font it is drawn in** (ours; earlier, the read pass's constructor did it, a comparison with 1.1
    /// section 1.3), so the wrap a test sees is the wrap the screen gets and a page is the page a click turns: each
    /// character's advance in the body's own font (the box's <c>font</c>, else <c>defaultFont10</c>), and that font's
    /// height a line. CPU metrics from the install's <c>.nfd</c>; nothing is drawn.
    /// </summary>
    public void Letter(UiAssets assets)
    {
        var body = assets.Font(FontOf(this, BodyBox));
        Advance = c => body[c].Advance;
        LineHeight = body.LineHeight;
    }

    /// <summary>The font a box of the pane names, or <c>defaultFont10</c> (the read pass draws with the same).</summary>
    public static string FontOf(ReadPane? pane, string box) =>
        pane?.Control(box)?.Get("font") is { Length: > 0 } named ? named : "defaultFont10";

    public ReadPane(NuiPane pane)
    {
        Pane = pane;
        Bar = ScrollBar.Of(pane, Control(BodyBox)?.Get("vscrollbar") is { Length: > 0 } named ? named : ScrollBarName);
    }

    public NuiElement? Control(string name) =>
        Pane.Elements.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public string Title => Document?.Title ?? "";

    /// <summary>`itemspacing` on the body box, or <see cref="DefaultSpacing"/>.</summary>
    public int Spacing =>
        Control(BodyBox)?.Get("itemspacing") is { } v && int.TryParse(v, out var n) ? n : DefaultSpacing;

    int Width => Control(BodyBox) is { Size.Length: > 1 } box ? box.Size[0] : 0;

    /// <summary>The body's height, which is a page to the wheel and to the track.</summary>
    public int Page => Control(BodyBox) is { Size.Length: > 1 } box ? box.Size[1] : 0;

    /// <summary>A quarter of the font's height: how far above the bottom the scroll leaves the last line.</summary>
    int Quarter => LineHeight / 4;

    List<ReadItem>? laid;
    (NtxEntry?, int, int, Func<char, int>) laidFor;

    /// <summary>
    /// The document laid out (bit 3 clear): each paragraph broken to the body's width by
    /// the library's wrap (after the last character a space follows, else just before the
    /// one that overflowed), the first against the top and each one `itemspacing` under the one before.
    /// </summary>
    public IReadOnlyList<ReadItem> Items()
    {
        var key = (Document, Width, LineHeight, Advance);
        if (laid is not null && laidFor == key) return laid;
        laid = [];
        laidFor = key;
        if (Document is not { } doc || Width <= 0) return laid;
        bool wrap = Control(BodyBox) is not { } box || NuiPane.TextBox(box).HasFlag(NuiPane.TextBoxFlags.WordWrap);
        int y = Page;
        for (int p = 0; p < doc.Paragraphs.Count; p++)
        {
            var lines = TextBoxText.Lines(doc.Paragraphs[p].Text, wrap, Width, Advance);
            int h = lines.Count * LineHeight;
            y -= h;
            laid.Add(new ReadItem(p, doc.Paragraphs[p].Colour, lines, y, h));
            y -= Spacing;
        }
        return laid;
    }

    /// <summary>Every line of the document, top first, with its paragraph's colour.</summary>
    public List<ReadLine> Lines() =>
        [.. Items().SelectMany(i => i.Lines.Select(l => new ReadLine(l, i.Colour, i.Paragraph)))];

    /// <summary>
    /// How far the body can be lifted: until the last paragraph's bottom is a
    /// quarter of a line above the box's bottom, and not at all while it is higher than that.
    /// </summary>
    public int MaxScroll => Items() is { Count: > 0 } items && items[^1].Y - Quarter < 0 ? Quarter - items[^1].Y : 0;

    /// <summary>Whether everything is in the box — the last paragraph's bottom not below it — so the slider cannot be taken.</summary>
    public bool Fits => Items() is not { Count: > 0 } items || items[^1].Y >= 0;

    /// <summary>
    /// Every line drawn, box-local: y the line's bottom counting up, an item's first line highest
    /// An item is drawn when its bottom is below the box's top and its top not below the
    /// box's bottom, and whole — the box's own surface cuts what is outside it).
    /// </summary>
    public IEnumerable<(string Text, int Y, uint Colour)> Drawn()
    {
        foreach (var item in Items())
        {
            int bottom = item.Y + Scroll;
            if (bottom >= Page || bottom + item.Height < 0) continue;
            int n = item.Lines.Count;
            for (int k = 0; k < n; k++)
                yield return (item.Lines[k], bottom + (n - 1 - k) * LineHeight, item.Colour);
        }
    }

    // ---- scrolling ------------------------------------------------------------------------------

    /// <summary>
    /// The body scrolled by <paramref name="by"/> pixels, held to 0 .. <see cref="MaxScroll"/>;
    /// the slider follows unless it is held. An empty body does not move. The arrow keys (a line)
    /// and Page keys (a page) come here too — they are ours; 1.1's dialog has no keys but its close.
    /// </summary>
    public void ScrollBy(int by)
    {
        if (Items().Count == 0) return;
        Scroll = Math.Clamp(Scroll + by, 0, MaxScroll);
        if (Bar is { Held: false } bar) bar.SetValue(-Scroll);
    }

    /// <summary>
    /// The wheel with the pointer on the body (the box reads the frame's turn only for the control
    /// under the pointer): a page towards the top for a turn away from you, towards the end for one
    /// towards you — one page however far it turned.
    /// </summary>
    public void Wheel(int turn)
    {
        if (turn != 0) ScrollBy(turn > 0 ? -Page : Page);
    }

    /// <summary>Whether pane point (<paramref name="x"/>, <paramref name="y"/>) is on the body, edges included — where the wheel works.</summary>
    public bool OverBody(int x, int y) =>
        Control(BodyBox) is { Pos.Length: > 1, Size.Length: > 1 } box
        && x >= box.Pos[0] && x <= box.Pos[0] + box.Size[0] && y >= box.Pos[1] && y <= box.Pos[1] + box.Size[1];

    /// <summary>
    /// A press at pane point (<paramref name="x"/>, <paramref name="y"/>) on the scroll bar. The
    /// slider is hit first: a press on it takes hold of it unless everything fits; a press
    /// anywhere else on the track pages. Answers what happened, or null when the press was
    /// not on the bar — the dialog swallows it all the same.
    /// </summary>
    public string? Press(int x, int y)
    {
        if (Bar is not { } bar || Document is null) return null;
        if (bar.Slider.Contains(x, y))
        {
            if (Fits) return "the slider (nothing to scroll)";
            bar.Take(y);
            return "took hold of the slider";
        }
        if (!bar.Bar.Contains(x, y)) return null;
        ScrollBy(bar.PageAt(y) * Page);
        return Where();
    }

    /// <summary>The slider or the track under pane point (<paramref name="x"/>, <paramref name="y"/>) — the slider first, being the bar's child — or null.</summary>
    public UiControl? TrackAt(int x, int y) =>
        Bar is not { } bar || Document is null ? null
        : bar.Slider.Contains(x, y) ? bar.Slider : bar.Bar.Contains(x, y) ? bar.Bar : null;

    /// <summary>Whether the slider is held.</summary>
    public bool Dragging => Bar?.Held == true;

    /// <summary>The held slider follows the pointer to pane <paramref name="y"/>, and the body to it (minus the value, held to the range).</summary>
    public void DragTo(float y)
    {
        if (Bar is { } bar && bar.DragTo(y)) Scroll = Math.Clamp(-bar.Value, 0, MaxScroll);
    }

    /// <summary>The button let go: the slider stays where it was dropped.</summary>
    public void Release() => Bar?.Release();

    /// <summary>Where the view is, for the console.</summary>
    public string Where() => Items().Count == 0 ? "the page is empty"
        : $"read at {Scroll} of {MaxScroll} px";

    /// <summary>
    /// Opens a document at the top. Null closes the dialog. The paragraphs go in as the library adds
    /// items (the bar's range the last paragraph's bottom less a quarter line, to 0), then
    /// the scroll goes back to the top and the bar with it — so the slider starts at
    /// the top of its track, or at the bottom for a page that fits (<see cref="ScrollBar.SetValue"/>).
    /// </summary>
    public void Open(NtxEntry? document)
    {
        if (Document is not null) Close();
        Document = document;
        Scroll = 0;
        Bar?.Release();
        if (Bar is { } bar && Items() is { Count: > 0 } items)
        {
            bar.SetRange(items[^1].Y - Quarter, 0);
            bar.SetValue(0);
        }
        if (document is null || Audio is not { } audio) return;
        // Reading ducks the music (single player), and the document's own
        // voice track plays flat at 0.8  — a stream, so `.mp3` first.
        audio.Duck(true);
        voice = audio.Bank.Load(document.Voice, stream: true);
        if (voice is not null) voiceChannel = audio.Mixer.Play2D(voice, VoiceVolume, 0.5f, 0);
    }

    /// <summary>The voice track's volume.</summary>
    public const float VoiceVolume = 0.8f;

    /// <summary>The game's sound, for the voice track and the ducking. Null plays nothing.</summary>
    public Audio.GameAudio? Audio { get; set; }

    Audio.Sound? voice;
    int voiceChannel;

    /// <summary>Whether the document's voice is still going.</summary>
    public bool Speaking => Audio?.Mixer.Get(voiceChannel) is not null;

    /// <summary>
    /// Told when the dialog closes (the OK button's release → the text code's close), which is
    /// where a script that opened it hears `textended`.
    /// </summary>
    public Action? Closed { get; set; }

    /// <summary>Closing it (the original has an unreferenced copy without the pause and the pane):
    /// the voice stops and is let go, and the music comes back up.</summary>
    void Close()
    {
        Closed?.Invoke();
        if (Audio is not { } audio) return;
        if (voice is not null)
        {
            audio.Mixer.Stop(voiceChannel);
            audio.Bank.Release(voice);
            voice = null;
        }
        audio.Duck(false);
    }
}
