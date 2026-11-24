// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The conversation on screen, as state rather than as pixels — what the original's conversation pane and
// subtitles would be drawing. `Vitomir.Render.ConversationPass` puts it up; everything here
// is testable without a GPU, which is the same split `StatusBar` uses and for the same reason.
//
// **It is two panes, not one**, and that is the thing to know first, because it is what the screen
// actually shows:
//
//   * `ui/subtitle.nui` — the **transcript**. 512x128 of two quads, one `textbox` called
//     `subtitleTextBox` filling it, **no background colour**, so the lines sit straight on the
//     scene. Lines are *appended* and accumulate for the whole conversation.
//   * `ui/conversationPane.nui` — the **replies**. 512x64 of two quads, one `textlistbox` called
//     `convResponse`, `color=255,0,0,0` — opaque black, which is the filled box a player sees when
//     there is something to say. Items are *set by index* rather than appended.
//
// Both are authored at `pos=64,32`, and `.nui` space has its origin at the **bottom** left
// (`UiAssets`), so both sit at the bottom of the screen with the reply box overlapping the lower
// half of the transcript. `vuiSubtitle` moves its own pane to y=340 (0x154) for the standalone,
// self-expiring subtitle and back to y=32 (0x20) for a conversation — through
// `vui::SetPos`.
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>One line of the transcript, with the colour the engine gave it.</summary>
public readonly record struct SpokenRow(string Text, uint Colour);

/// <summary>
/// The conversation panes: the running transcript, and the replies on offer.
/// </summary>
public sealed class ConversationPane
{
    /// <summary>The two pane files, and the one control that matters in each.</summary>
    public const string ReplyPane = "conversationPane", TranscriptPane = "subtitle";
    /// <inheritdoc cref="ReplyPane"/>
    public const string ReplyList = "convResponse", TranscriptBox = "subtitleTextBox";

    /// <summary>
    /// **Consecutive transcript lines alternate between these two**, and nothing else decides the
    /// colour: the transcript passes <c>(-(flip != 0) &amp; 0xffafafb0) - 1</c>, which is
    /// <c>0xffffffff</c> when the flip is clear and <c>0xffafafaf</c> when it is set, and then
    /// toggles the flip. It is per *call*, not per speaker — in a two-hander they coincide.
    /// </summary>
    public const uint Bright = 0xFFFFFFFF;
    /// <inheritdoc cref="Bright"/>
    public const uint Dim = 0xFFAFAFAF;

    /// <summary>
    /// What a reply under the mouse turns. **Provisional**: taken from a base-game screenshot, not
    /// from 1.1's own rules — how the `textlistbox` draw picks it is not yet known. `0xffffff00`
    /// is the ARGB for the yellow in the screenshot and is a colour the `Ui` module uses elsewhere,
    /// which is corroboration rather than proof. Reading 0x4d4xxx settles it.
    /// </summary>
    public const uint Highlight = 0xFFFFFF00;

    /// <summary>
    /// Where `vuiSubtitle` puts its pane: y=32 while a conversation is running, y=340 for the
    /// standalone subtitle. `.nui` space counts up from the bottom, so 32 is the bottom
    /// of the screen and 340 is near the top.
    /// </summary>
    public const int ConversationY = 0x20, StandaloneY = 0x154;

    public NuiPane Replies { get; }
    public NuiPane Transcript { get; }

    /// <summary>The reply list, and the box the transcript is written into.</summary>
    public UiControl List { get; }
    /// <inheritdoc cref="List"/>
    public UiControl Box { get; }

    public ConversationPane(NuiPane replies, NuiPane transcript)
    {
        Replies = replies;
        Transcript = transcript;
        List = Find(replies, ReplyList);
        Box = Find(transcript, TranscriptBox);
    }

    static UiControl Find(NuiPane pane, string name) =>
        new(pane.Elements.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{pane.Name} has no control named {name}"));

    /// <summary>Everything said so far, oldest first. Cleared when a conversation starts or ends.</summary>
    public IReadOnlyList<SpokenRow> Said => said;
    readonly List<SpokenRow> said = [];
    bool dim;

    /// <summary>
    /// Another line, already formatted as <c>&lt;speaker&gt; words</c> by
    /// <see cref="Rpg.Conversation.Subtitle"/>. A chosen reply comes through here too, which is why
    /// a reply is bare in the list and has a name in front of it once it has been said.
    /// </summary>
    public void Say(string line)
    {
        said.Add(new SpokenRow(line, dim ? Dim : Bright));
        dim = !dim;
    }

    /// <summary>
    /// Start again — the flip goes back to <see cref="Bright"/>, which the pane does whenever it
    /// puts the pane up.
    /// </summary>
    public void Reset()
    {
        said.Clear();
        dim = false;
        Offer([]);
    }

    /// <summary>The replies on offer, bare — the list draws them with no speaker in front.</summary>
    public IReadOnlyList<string> Offered => offered;
    readonly List<string> offered = [];

    /// <summary>Which reply the mouse is over, or −1.</summary>
    public int Hovered { get; set; } = -1;

    /// <summary>Whether the reply box is up at all.</summary>
    public bool Offering => offered.Count > 0;

    /// <summary>
    /// Mirror what is on offer. **Idempotent**, so the frame loop can call it every tick without
    /// losing which row the mouse is on, or where the list is scrolled to: both are only dropped when
    /// the list actually changes.
    /// </summary>
    public void Offer(IEnumerable<string> replies)
    {
        var next = replies as IList<string> ?? [.. replies];
        if (next.Count == offered.Count && next.SequenceEqual(offered)) return;
        offered.Clear();
        offered.AddRange(next);
        Hovered = -1;
        laidOut = null;
    }

    /// <summary>
    /// **The replies laid out as 1.1's `convResponse` lays them out**: it is the ui library's
    /// `textlistbox` (<see cref="MenuList"/>), and each reply is *added* to it (through the list's add),
    /// not placed. So a reply is word-wrapped to the box
    /// (`textboxflags` 0x04), the first row sits against the box's top and each later one a font line
    /// plus `itemspacing` under the one before — and **`itemspacing=0x04` is 0**: 1.1 reads that key
    /// with <c>sscanf("%d")</c>, which stops at the <c>x</c>
    /// (<c>textboxflags</c> is read with <c>"%i"</c>, so its 0x04 is 4). With
    /// `defaultFont`'s 10-pixel line in the 60-pixel box the rows sit at 50, 40, 30, 20 and 10 from the
    /// bottom: **five replies fit**. A sixth or more: **every add scrolls the list to its end** — the last
    /// reply brought up to a quarter line above the box's bottom — and the draw cuts at the
    /// box's edges, so the first rows go off the top; the wheel over the box
    /// pages back (<see cref="Page"/>). Built once per offer, so the scroll lasts while the
    /// same replies are up.
    /// </summary>
    public MenuList Layout(NfdFont font)
    {
        if (laidOut is { } kept && ReferenceEquals(kept.Font, font)) return kept;
        var list = new MenuList(List.Element, font);
        for (int i = 0; i < offered.Count; i++) list.Add(offered[i], i);
        return laidOut = list;
    }
    MenuList? laidOut;

    /// <summary>How far apart the list's one-line rows are: the font's line, plus the pane's `itemspacing`.</summary>
    public int RowHeight(NfdFont font) => font.LineHeight + Spacing(List);

    /// <inheritdoc cref="RowHeight"/>
    public int LineHeight(NfdFont font) => font.LineHeight + Spacing(Box);

    /// <summary>
    /// A control's `itemspacing`, read as 1.1 reads it — <c>sscanf("%d")</c>, leading decimal
    /// digits — so the reply list's <c>0x04</c> is 0 and the transcript's <c>3</c> is 3.
    /// </summary>
    static int Spacing(UiControl control) =>
        control.Element.Get("itemspacing") is { } v && NuiPane.Ints(v) is [var n, ..] ? n : 0;

    /// <summary>Whether a point in `.nui` space (origin bottom left) is on the reply list.</summary>
    public bool Over((int X, int Y) at)
    {
        int left = Replies.Pos[0] + List.X, bottom = Replies.Pos[1] + List.Y;
        return Offering && at.X >= left && at.X < left + List.Width && at.Y >= bottom && at.Y < bottom + List.Height;
    }

    /// <summary>
    /// Which reply is at a point in `.nui` space (origin bottom left), or −1: the list's own hover
    /// at the point's height in the box, so a reply scrolled out of the box is never
    /// under the mouse and one cut by the box's edge only where it shows.
    /// </summary>
    public int RowAt(NfdFont font, int x, int y)
    {
        if (!Over((x, y))) return -1;
        var list = Layout(font);
        list.HoverAt(y - (Replies.Pos[1] + List.Y));
        return list.Hovered;
    }

    /// <summary>
    /// The wheel over the list: up (<paramref name="direction"/> −1) or down (+1) by the
    /// box's height, held to the list's range; the hover taken again at <paramref name="at"/>.
    /// </summary>
    public void Page(NfdFont font, int direction, (int X, int Y) at)
    {
        if (direction == 0 || !Offering) return;
        Layout(font).Page(direction);
        Hovered = RowAt(font, at.X, at.Y);
    }

    /// <summary>
    /// The transcript as it would be drawn: every line word-wrapped to the box, keeping its own
    /// colour, then clipped to the rows that fit with the **newest at the bottom**.
    /// </summary>
    public List<SpokenRow> Rows(NfdFont font)
    {
        var rows = new List<SpokenRow>();
        foreach (var line in said)
            foreach (var part in Wrap(font, line.Text, Box.Width))
                rows.Add(new SpokenRow(part, line.Colour));
        int fit = Math.Max(1, Box.Height / Math.Max(1, LineHeight(font)));
        if (rows.Count > fit) rows.RemoveRange(0, rows.Count - fit);
        return rows;
    }

    /// <summary>
    /// Word wrap to a pixel width, measured with the pane's own font. A single word longer than the
    /// box is left over-long rather than broken, which is what a text box does with one.
    /// </summary>
    public static List<string> Wrap(NfdFont font, string text, int width)
    {
        var lines = new List<string>();
        if (width <= 0) { lines.Add(text); return lines; }
        var words = text.Split(' ');
        var line = new System.Text.StringBuilder();
        foreach (var word in words)
        {
            if (line.Length == 0) { line.Append(word); continue; }
            if (font.Measure($"{line} {word}") <= width) { line.Append(' ').Append(word); continue; }
            lines.Add(line.ToString());
            line.Clear().Append(word);
        }
        if (line.Length > 0) lines.Add(line.ToString());
        if (lines.Count == 0) lines.Add("");
        return lines;
    }
}
