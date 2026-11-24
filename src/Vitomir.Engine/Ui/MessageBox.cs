// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The status bar's message box as the player sees it: the lines of the message log laid out in the
// box, how far they are scrolled, and the scroll bar beside them — `sbTextbox`, `sbScrollBar` and
// `sbScrollSlider`, three of the interface library's generic controls (the text box, the bar
// and the slider). The log itself — what has been said — is
// `MessageLog`; this is the view of it. The bar and its slider are the library's, shared with the
// read dialog's (`ScrollBar`, moved onto it with no change in behaviour).
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The message box and its scroll bar** — the text
/// box's layout and scroll, the bar's range, value and slider
/// and the slider's drag (and the box's hook).
///
/// Everything is in the box's own space, which **counts up** like the rest of the interface: an
/// item's <c>Y</c> is its bottom edge, 0 is the box's bottom and the box's height its top. The first
/// line said sits against the top and each later one below it; once they run past the bottom the
/// box is lifted by <see cref="Scroll"/> so that the newest is a quarter of a line above the bottom
/// edge. **Every new line lifts it back to the newest**, however far up the player had scrolled —
/// the add does that unconditionally — and the box keeps the last hundred.
/// </summary>
public sealed class MessageBox
{
    /// <summary>One thing said: its lines as the box broke them, and where it sits before scrolling.</summary>
    /// <param name="Y">Its bottom edge, box-local and counting up — negative once below the box.</param>
    /// <param name="Height">Its lines times the font's height.</param>
    public sealed record Item(Message Message, IReadOnlyList<string> Lines, int Y, int Height);

    /// <summary>
    /// The box's own colour, which a line posted without one is drawn in: the
    /// constructor sets it to 240 grey and `sbTextbox` has no `tbtextcolor` to change it,
    /// and the plain add passes it on. Not quite white.
    /// </summary>
    public const uint OwnColour = 0xFFF0F0F0u;

    readonly MessageLog log;
    readonly List<Item> items = [];
    int seenVersion = -1;
    long seenAdded;
    int seenStreamer;
    /// <summary>The bar and its slider, when the pane has both (the library's, <see cref="Ui.ScrollBar"/>).</summary>
    readonly ScrollBar? scroll;

    public NfdFont Font { get; }
    /// <summary>`sbTextbox`.</summary>
    public UiControl Box { get; }
    /// <summary>`sbScrollBar`, when the pane has it and its slider.</summary>
    public UiControl? Bar => scroll?.Bar;
    /// <summary>`sbScrollSlider`, which the bar moves.</summary>
    public UiControl? Slider => scroll?.Slider;
    /// <summary>`textboxflags` — word wrap, for this box.</summary>
    public NuiPane.TextBoxFlags Flags { get; }
    /// <summary>`itemspacing`, the gap between items: none on the status bar.</summary>
    public int Spacing { get; }

    public MessageBox(MessageLog log, NfdFont font, UiControl box, UiControl? bar, UiControl? slider)
    {
        this.log = log;
        Font = font;
        Box = box;
        scroll = bar is not null && slider is not null ? new ScrollBar(bar, slider) : null;
        Flags = NuiPane.TextBox(box.Element);
        Spacing = box.Element.Get("itemspacing") is { } s && int.TryParse(s, out int n) ? n : 0;
        Sync();
    }

    /// <summary>What is in the box, oldest first.</summary>
    public IReadOnlyList<Item> Items => items;

    /// <summary>How far the items are lifted: 0 puts the oldest at the top.</summary>
    public int Scroll { get; private set; }

    /// <summary>A page, which the wheel and a click on the bar each move: the box's own height.</summary>
    public int Page => Box.Height;

    int Quarter => Font.LineHeight / 4;

    /// <summary>
    /// How far the items can be lifted: until the newest is a quarter of a line above the
    /// bottom edge, and not at all while it is higher than that.
    /// </summary>
    public int MaxScroll => items.Count == 0 || items[^1].Y - Quarter >= 0 ? 0 : Quarter - items[^1].Y;

    // ---- the log --------------------------------------------------------------------------------

    /// <summary>
    /// Catches up with the log. A line added lays the items out again — the oldest gone
    /// first when there were a hundred — sets the bar's range to
    /// <c>(newest − a quarter line) .. 0</c>, and lifts the box to the newest and the slider with it;
    /// the slider is left where it is only while it is held. Emptying the box drops the
    /// scroll and leaves the bar alone.
    /// </summary>
    public void Sync()
    {
        // Streamer mode turned on or off lays the box out again, its lines hidden or shown, without scrolling it.
        int streamer = Vitomir.Platform.Presentation.StreamerMode.Version;
        if (streamer != seenStreamer && log.Version == seenVersion)
        {
            seenStreamer = streamer;
            Layout();
            return;
        }
        seenStreamer = streamer;
        if (log.Version == seenVersion) return;
        bool added = log.Added != seenAdded;
        seenVersion = log.Version;
        seenAdded = log.Added;
        Layout();
        if (items.Count == 0 || !added)
        {
            Scroll = 0;
            return;
        }
        int last = items[^1].Y;
        SetRange(last - Quarter, 0);
        Scroll = last - Quarter < 0 ? Quarter - last : 0;
        if (!Dragging) SetValue(-Scroll);
    }

    /// <summary>
    /// The layout (with `textboxflags` bit 3 clear): each item broken into lines,
    /// the first against the top and each one `itemspacing` under the one before.
    /// </summary>
    void Layout()
    {
        items.Clear();
        int y = Box.Height;
        foreach (var m in log.Lines)
        {
            // Streamer mode: an address, the user's folder or a secret in a line of the message box (the ~ console's lines
            // among them) or the chat box is hidden as it is laid out.
            var lines = TextBoxText.Lines(Vitomir.Platform.Presentation.StreamerMode.Shown(m.Text), Flags.HasFlag(NuiPane.TextBoxFlags.WordWrap), Box.Width,
                                          c => Font[c].Advance);
            int h = lines.Count * Font.LineHeight;
            y -= h;
            items.Add(new Item(m, lines, y, h));
            y -= Spacing;
        }
    }

    // ---- what is drawn --------------------------------------------------------------------------

    /// <summary>Whether an item is drawn at all: its bottom below the top edge and its top not below the bottom.</summary>
    public bool Visible(int index)
    {
        var item = items[index];
        int bottom = item.Y + Scroll;
        return bottom < Box.Height && bottom + item.Height >= 0;
    }

    /// <summary>
    /// Every line drawn, box-local: y the line's bottom counting up — an item's first line highest,
    /// because the box keeps an item's lines last-first and draws the first of those at the item's
    /// bottom — and the colour, the box's own for a line posted without one.
    /// An item partly outside the box is drawn whole; the box's own surface cuts it.
    /// </summary>
    public IEnumerable<(string Text, int Y, uint Colour)> Drawn()
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (!Visible(i)) continue;
            var item = items[i];
            uint colour = item.Message.Colour == MessageLog.Own ? OwnColour : item.Message.Colour;
            int n = item.Lines.Count;
            for (int k = 0; k < n; k++)
                yield return (item.Lines[k], item.Y + Scroll + (n - 1 - k) * Font.LineHeight, colour);
        }
    }

    // ---- scrolling ------------------------------------------------------------------------------

    /// <summary>
    /// The box's scroll by <paramref name="by"/>, held to 0 .. <see cref="MaxScroll"/>;
    /// the slider follows unless it is held. An empty box does not move.
    /// </summary>
    public void ScrollBy(int by)
    {
        Sync();
        if (items.Count == 0) return;
        Scroll = Math.Clamp(Scroll + by, 0, MaxScroll);
        if (!Dragging) SetValue(-Scroll);
    }

    /// <summary>
    /// The wheel with the pointer on the box (which reads the frame's wheel turn): a page
    /// towards the oldest for a turn away from you, towards the newest for one towards you — one page
    /// however far it turned.
    /// </summary>
    public string Wheel(int turn)
    {
        if (turn == 0) return "no turn";
        ScrollBy(turn > 0 ? -Page : Page);
        return Where();
    }

    /// <summary>
    /// A press on the scroll bar at pane <paramref name="y"/>. The slider is the bar's child
    /// (the bar's parse re-parents it), so it is hit before the bar: a press on it takes hold of it — only
    /// when the newest item's bottom is below the box — and a press anywhere else on the
    /// bar pages: above the slider's bottom towards the oldest, level with it or below
    /// towards the newest.
    /// </summary>
    public string Press(int x, int y)
    {
        Sync();
        if (Bar is null || Slider is null) return "the message box has no scroll bar";
        if (Slider.Contains(x, y))
        {
            if (items.Count == 0 || items[^1].Y >= 0) return "the slider (nothing to scroll)";
            scroll!.Take(y);
            return "took hold of the slider";
        }
        ScrollBy(scroll!.PageAt(y) * Page);
        return Where();
    }

    /// <summary>Whether the slider is being held (the interface's captured control).</summary>
    public bool Dragging => scroll?.Held ?? false;

    /// <summary>
    /// The held slider, with the pointer now at pane <paramref name="y"/>: it moves by
    /// what the pointer moved, kept on its track, and when the bar's value changed the box
    /// is told: its scroll becomes minus the value, held to its range. The original moves
    /// the slider by the mouse's own counts each frame; this moves it by the pointer's travel in the
    /// pane, so it stays under the pointer at any window size.
    /// </summary>
    public void DragTo(float y)
    {
        if (scroll is null || !scroll.DragTo(y) || items.Count == 0) return;
        Scroll = Math.Clamp(-scroll.Value, 0, MaxScroll);
    }

    /// <summary>The button let go: the slider stays where it was dropped.</summary>
    public void Release() => scroll?.Release();

    /// <summary>Where the view is, for the console: how far above the newest, in pixels of the box.</summary>
    string Where() => items.Count == 0 ? "the message box is empty"
        : Scroll == MaxScroll ? $"message box at the newest ({items.Count} messages)"
        : $"message box {MaxScroll - Scroll} px up from the newest ({items.Count} messages)";

    // ---- the scroll bar ------------------------------------------------------------------------

    /// <summary>The bar's range: the newest item's bottom less a quarter line, to 0.</summary>
    public int Min => scroll?.Min ?? 0;
    /// <inheritdoc cref="Min"/>
    public int Max => scroll?.Max ?? 0;
    /// <summary>The bar's value: minus the box's scroll.</summary>
    public int Value => scroll?.Value ?? 0;

    /// <summary>How far the slider can travel: the bar's height less its own.</summary>
    public int Travel => scroll?.Travel ?? 0;

    /// <summary>
    /// The range set, and the slider put where the value now says: at the bottom of the
    /// track for the newest, at the top for the oldest, and at the bottom for a box that fits.
    /// Moving a slider that is held lets go of it — which is how a line arriving mid-drag
    /// ends the drag.
    /// </summary>
    void SetRange(int min, int max) => scroll?.SetRange(min, max);

    /// <summary>
    /// The value set (a bar without `quanta`): **anything outside the range becomes the
    /// range's minimum**, not its nearer end — which, for a box that fits, is every value.
    /// </summary>
    void SetValue(int value) => scroll?.SetValue(value);
}
