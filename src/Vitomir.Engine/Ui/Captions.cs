// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Words that float over the picture for a few seconds and fade — the engine's own little text
// objects, which is how a chapter announces itself.
namespace Vitomir.Engine.Ui;

/// <summary>
/// One floating line: a font, where its pane stands in the 640x480 space, and a life of
/// <paramref name="TotalMs"/> that fades in over the first <paramref name="FadeInMs"/> and out over
/// the last <paramref name="FadeOutMs"/> (which scales the pane's alpha by 255).
///
/// <paramref name="X"/>, <paramref name="Y"/> is the pane's **bottom left**, like every other pane
/// (the space counts up from the bottom). The pane is <c>(<paramref name="Lines"/> + 1)</c> of the
/// font's line height tall (<c>(lines + 1) * font height</c>) and its text is set from
/// the top, so the words sit <see cref="PenY"/> above where the pane is stood — not on
/// <paramref name="Y"/> itself.
///
/// The pane is as wide as the text it was made with (it is measured and capped at
/// 640) and is drawn from textures cut to that width, so a line added to it later that is wider is
/// cut off at the pane's right edge: <paramref name="Width"/>, when it is not 0.
/// </summary>
public sealed record Caption(string Text, string Font, float X, float Y, int StartMs, int TotalMs, int FadeInMs, int FadeOutMs,
                             int Lines = 1, float Rise = 0f, float Width = 0f, bool Ours = false)
{
    /// <summary>
    /// Where the pen is at <paramref name="nowMs"/> for a caption that rises: the floating text's
    /// own step adds <c>dt × 0.001 × velocity</c> to its y every frame, which is
    /// <see cref="Rise"/> pixels a second — the credits' 20 (`0x41a00000`).
    /// </summary>
    public float PenYAt(int lineHeight, int nowMs) => PenY(lineHeight) + Rise * Math.Max(0, nowMs - StartMs) / 1000f;

    /// <summary>
    /// Where the pen goes for the one line of text: the top line of a pane
    /// <c>Lines + 1</c> line heights tall, whose bottom is at <see cref="Y"/>.
    /// </summary>
    public float PenY(int lineHeight) => Y + (Lines + 1) * lineHeight - lineHeight;

    /// <summary>How opaque it is at <paramref name="nowMs"/>, 0 to 1.</summary>
    public float Alpha(int nowMs)
    {
        int elapsed = nowMs - StartMs, left = StartMs + TotalMs - nowMs;
        if (FadeInMs != 0 && elapsed < FadeInMs) return Math.Clamp((float)elapsed / FadeInMs, 0f, 1f);
        if (FadeOutMs != 0 && left < FadeOutMs) return Math.Clamp((float)left / FadeOutMs, 0f, 1f);
        return 1f;
    }

    /// <summary>Whether its time is up — the frame after which it is gone.</summary>
    public bool Over(int nowMs) => nowMs > StartMs + TotalMs;
}

/// <summary>The captions up now.</summary>
public sealed class Captions
{
    readonly List<Caption> up = [];
    public IReadOnlyList<Caption> Up => up;

    public void Add(Caption caption) => up.Add(caption);

    /// <summary>Takes one down before its time (which the journeys use on the end text).</summary>
    public bool Remove(Caption caption) => up.Remove(caption);

    /// <summary>Takes down any whose time is up.</summary>
    public void Prune(int nowMs) => up.RemoveAll(c => c.Over(nowMs));

    /// <summary>Takes every one down.</summary>
    public void Clear() => up.Clear();

    /// <summary>The end text's font: the engine's default (given none), `DefaultFont15`.</summary>
    public const string EndFont = "DefaultFont15";
    /// <summary>How long the end text stays up: two hours (7,200,000 ms), which is to say until something takes it down.</summary>
    public const int EndMs = 7_200_000;
    /// <summary>The end text fades in over half a second (<c>0x1f4</c>) and never out.</summary>
    public const int EndFadeInMs = 500;

    /// <summary>
    /// **The words over the world when the game is lost** (single player): one
    /// floating pane stood at (20, 430) — <c>0x14</c>, <c>0x1ae</c>, near the top left — three lines
    /// of the default font tall, holding the headline and then "Press Esc to load saved game." added
    /// under it. The pane's text box is made without word wrap (its flags are left at 0)
    /// and keeps two items; it is as wide as the headline, so when the second line is the wider
    /// ("Christof lost all Humanity!", 182 pixels, over the 212 of the second) its end is cut off.
    /// Nothing goes up for an empty headline (it is refused).
    /// </summary>
    public static Caption[] EndText(string headline, string pressEsc, int headlineWidth, int nowMs)
    {
        if (string.IsNullOrEmpty(headline)) return [];
        float width = Math.Min(headlineWidth, 640);
        return
        [
            new Caption(headline, EndFont, 20, 430, nowMs, EndMs, EndFadeInMs, 0, Lines: 2, Width: width),
            new Caption(pressEsc, EndFont, 20, 430, nowMs, EndMs, EndFadeInMs, 0, Lines: 1, Width: width),
        ];
    }

    /// <summary>The font the chapter title is in: the engine's font 5, `morpheus20`.</summary>
    public const string TitleFont = "morpheus20";

    /// <summary>
    /// **The chapter title** (as a level starts): a place entered with a chapter scene
    /// laid over it announces the chapter — the string table's <c>&lt;scene&gt;S</c>, centred across
    /// the screen in a pane stood at y = 400, for eight seconds with two of fade at each end. The pane
    /// is two lines tall (the call passes 1 for <see cref="Caption.Lines"/>) and the line is written
    /// at its top, so the words' baseline is at 420 — 60 below the top of the screen, which is
    /// 112 pixels down a 900-pixel window. A 1.1 screenshot of "Monastery 1" has the letters
    /// from row 85 to row 116 with the baseline at 110. Once Vitomir put the baseline on
    /// 400, a line lower than the original. The monastery says
    /// "Monastery 1"; the convent's first night, "That evening within the Convent...". A line that
    /// begins <c>&lt;&gt;</c> is the table's way of saying "nothing", and nothing goes up; nor does
    /// anything while a book is open (the read dialog's text box).
    /// </summary>
    public static Caption? ChapterTitle(string? chapterScene, Func<string, string> text, Func<string, string, int> measure,
                                        int nowMs)
    {
        if (string.IsNullOrEmpty(chapterScene)) return null;
        string scene = chapterScene;
        int dot = scene.LastIndexOf('.');
        if (dot >= 0) scene = scene[..dot];
        string line = text(scene + "S");
        if (line.StartsWith("<>", StringComparison.Ordinal)) return null;
        // `(640 − width) / 2`, a signed halving (`cdq; sub; sar`), so it rounds toward zero.
        int x = (640 - measure(TitleFont, line)) / 2;
        return new Caption(line, TitleFont, x, 400, nowMs, 8000, 2000, 2000, Lines: 1);
    }
}
