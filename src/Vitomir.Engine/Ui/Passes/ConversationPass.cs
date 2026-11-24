// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The conversation, drawn. Every decision about *what* is on screen — which rows, what colour each
// is, which reply the mouse is on — belongs to `Vitomir.Engine.Ui.ConversationPane`, which is
// testable without a GPU. This file is the part that needs one.
//
// It draws two panes, because the original does: the transcript, which has
// no background of its own and sits straight on the scene, and — only while there is something to
// say — the reply list, which has an opaque black one.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>Draws the conversation panes over the finished frame while one is running.</summary>
public sealed class ConversationPass : IUiPass
{
    readonly UiCanvas ui;

    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    /// <summary>What to draw. Null when no conversation has been started.</summary>
    public ConversationPane? Pane { get; set; }

    /// <summary>The font both boxes use. Neither declares one, so it is the engine's default.</summary>
    public Vitomir.Formats.NfdFont Font { get; }

    /// <summary>The font a `.nui` gets when its control names none.</summary>
    public const string DefaultFont = "DefaultFont";

    /// <summary>Names the panes asked for that the install has not got, reported once.</summary>
    public IReadOnlyList<string> Missing => ui.Assets.Missing;

    public ConversationPass(UiCanvas ui, ConversationPane? pane = null)
    {
        this.ui = ui;
        Pane = pane;
        Font = ui.Assets.Font(pane?.Box.Element.Get("font") ?? DefaultFont);
    }

    /// <summary>Builds the pane from the player's own files.</summary>
    public static ConversationPane Load(UiAssets assets) =>
        new(assets.Pane(ConversationPane.ReplyPane), assets.Pane(ConversationPane.TranscriptPane));

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (Pane is not { } pane || width <= 0 || height <= 0) return;
        if (pane.Said.Count == 0 && !pane.Offering) return;
        ui.Begin(width, height);
        Transcript(pane);
        if (pane.Offering) Replies(pane);
    }

    /// <summary>
    /// The transcript: the pane's two quads, then the wrapped rows with the **newest at the
    /// bottom**, each in the colour `vuiSubtitle` alternated onto it.
    /// </summary>
    void Transcript(ConversationPane pane)
    {
        float px = pane.Transcript.Pos[0], py = pane.Transcript.Pos[1];
        foreach (var q in pane.Transcript.Quads)
            ui.Draw(q.Material, px + q.Pos[0], py + q.Pos[1], q.Size[0], q.Size[1]);

        var rows = pane.Rows(Font);
        int line = pane.LineHeight(Font);
        float left = px + pane.Box.X, bottom = py + pane.Box.Y;
        // `.nui` y counts up, so the last row sits on the box's floor and earlier ones stack above.
        for (int i = 0; i < rows.Count; i++)
            ui.Text(Font, left, bottom + (rows.Count - 1 - i) * line, rows[i].Text, Tint.From(rows[i].Colour));
    }

    /// <summary>
    /// The replies: the pane's two quads, the list's own opaque background, and one bare row each —
    /// no speaker in front, which is the difference between an offer and a line that was said. The
    /// rows are where the list's own layout puts them (<see cref="ConversationPane.Layout"/>: scrolled
    /// to the last reply, as each add does) and **cut at the box's edges**, so
    /// a fifth reply never hangs below the box.
    /// </summary>
    void Replies(ConversationPane pane)
    {
        float px = pane.Replies.Pos[0], py = pane.Replies.Pos[1];
        foreach (var q in pane.Replies.Quads)
            ui.Draw(q.Material, px + q.Pos[0], py + q.Pos[1], q.Size[0], q.Size[1]);

        var list = pane.List;
        float left = px + list.X, bottom = py + list.Y;
        if (Colour(list.Element.Get("color")) is { } fill) ui.Box(left, bottom, list.Width, list.Height, fill);

        var rows = pane.Layout(Font);
        for (int i = 0; i < rows.Rows.Count; i++)
        {
            if (!rows.Visible(i)) continue;
            var tint = i == pane.Hovered ? Tint.From(ConversationPane.Highlight) : Tint.White;
            foreach (var (text, lx, ly) in rows.LinesOf(i))
                ui.TextClipped(Font, left + lx, bottom + ly, text, tint, left, bottom, left + list.Width, bottom + list.Height);
        }
    }

    /// <summary>A `.nui` `color=a,r,g,b`, which is how a control declares a background.</summary>
    static Tint? Colour(string? value)
    {
        if (value is null) return null;
        var parts = Vitomir.Formats.NuiPane.Ints(value);
        if (parts.Length < 4) return null;
        return new Tint(parts[1] / 255f, parts[2] / 255f, parts[3] / 255f, parts[0] / 255f);
    }
}
