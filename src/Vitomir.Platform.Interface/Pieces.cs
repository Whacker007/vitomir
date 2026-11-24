// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;

namespace Vitomir.Platform.Interface;

/// <summary>Words: one line, or broken into lines at the piece's width when <see cref="Wrap"/>; names from the data in braces.</summary>
public sealed class TextPiece : Piece
{
    public string Text { get; set; } = "";
    public TextStyle Style { get; set; } = Theme.Styles[Theme.DefaultStyle];
    /// <summary>The style's name in the file (a look may dress a style its own way: the launcher's title is a picture).</summary>
    public string StyleName { get; set; } = Theme.DefaultStyle;
    public TextAlign Align { get; set; } = TextAlign.Start;
    public bool Wrap { get; set; }

    /// <summary>The words as shown now (the data's names filled in).</summary>
    public string Words => Scope.Fill(Text);

    float LineHeight => Style.Em * Style.LineHeight;

    protected override Vector2 MeasureContent(Vector2 room)
    {
        var text = Scope.Fill(Text);
        if (Wrap && !float.IsInfinity(room.X))
        {
            var lines = Letters.Wrap(Style.Lettering, text, Style.Em, room.X);
            float w = 0;
            foreach (var l in lines) w = MathF.Max(w, Letters.Measure(Style.Lettering, l, Style.Em));
            return new Vector2(w, lines.Count * LineHeight);
        }
        return new Vector2(Letters.Measure(Style.Lettering, text, Style.Em), LineHeight);
    }

    public override void Draw(Painter p)
    {
        var text = Scope.Fill(Text);
        if (!Wrap)
        {
            p.Text(text, Style, Rect, Align);
            return;
        }
        var lines = Letters.Wrap(Style.Lettering, text, Style.Em, Rect.Width);
        for (int i = 0; i < lines.Count; i++)
            p.Text(lines[i], Style, new UiRect(Rect.X, Rect.Y + i * LineHeight, Rect.Width, LineHeight), Align);
    }
}

/// <summary>Something the player works: it fires its <see cref="Action"/> (with <see cref="Argument"/>, names filled in) for the host.</summary>
public abstract class Worked : Piece
{
    public string? Action { get; set; }
    public string Argument { get; set; } = "";

    /// <summary>A name in the data that must be true for the piece to be worked (none: always); disabled, it is shown but passed over.</summary>
    public string? EnabledWhen { get; set; }

    /// <summary>Whether it can be worked now.</summary>
    public bool Enabled => EnabledWhen is null || Scope.Flag(EnabledWhen);

    /// <summary>Why it cannot be worked while it is disabled (the file's <c>why</c>, names filled in), for the screen to say beside it.</summary>
    public string? Why { get; set; }

    /// <summary>The reason it is disabled now, in words; empty while it is enabled or none is given.</summary>
    public string WhyNot => Enabled || Why is null ? "" : Scope.Fill(Why);

    public override bool Interactive => Enabled;

    protected void Fire(Screen screen)
    {
        if (!string.IsNullOrEmpty(Action)) screen.Fire(new UiAction(Action, Scope.Fill(Argument), Scope, this));
    }
}

/// <summary>A button: a plate with a name on it; the chosen one lit red (2a). It fires its action when clicked or on Enter.</summary>
public sealed class Button : Worked
{
    public string Text { get; set; } = "";
    public TextStyle Style { get; set; } = Theme.Styles["button"];
    /// <summary>The style's name in the file (<c>button</c>, <c>buttonSmall</c>, <c>tab</c>): a look dresses each kind its own way.</summary>
    public string StyleName { get; set; } = "button";
    /// <summary>A name in the data that is true while the button stands for what is chosen now (a page's tab), or none.</summary>
    public string? SelectedWhen { get; set; }

    /// <summary>Whether the button stands for what is chosen now.</summary>
    public bool Selected => SelectedWhen is not null && Scope.Flag(SelectedWhen);

    /// <summary>
    /// When it is the screen's **default button** (Enter works it from a checkbox or a radio as from itself): a data flag's name, or
    /// <see cref="Always"/>; null: never. A button that may be the default but is not now is drawn as a secondary one by a look.
    /// </summary>
    public string? DefaultWhen { get; set; }

    /// <summary>When it is the screen's **cancel button** (Escape works it, before the screen's <c>back</c>): as <see cref="DefaultWhen"/>.</summary>
    public string? CancelWhen { get; set; }

    /// <summary>
    /// A button of secondary weight (the file's <c>quiet</c>): its words at its style's size, a look drawing it as its quieter kind
    /// (the launcher's Quit). The theme's own look does not read it.
    /// </summary>
    public bool Quiet { get; set; }

    /// <summary>
    /// An icon drawn in place of the words (the file's <c>icon</c>): <c>up</c> or <c>down</c>, an arrow head (a row's Move up and Move down);
    /// the words stay the button's name (its action's, a reason's, a test's). Null: the words are drawn.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>The direction of <see cref="Icon"/>'s arrow: −1 up, +1 down, 0 none.</summary>
    public int IconDirection => Icon switch { "up" => -1, "down" => +1, _ => 0 };

    /// <summary>A condition that always holds (a file's <c>true</c>).</summary>
    public const string Always = "*";

    public bool IsDefault => Holds(DefaultWhen);
    public bool IsCancel => Holds(CancelWhen);

    bool Holds(string? when) => when == Always || when is not null && Scope.Flag(when);
    public float PadAcross { get; set; } = 36f;

    protected override Vector2 MeasureContent(Vector2 room) =>
        IconDirection != 0 ? new(Style.Em * 1.75f, Style.Em * 1.75f)
            : new(Letters.Measure(Style.Lettering, Scope.Fill(Text), Style.Em) + 2 * PadAcross, Style.Em * 1.75f);

    public override void Draw(Painter p)
    {
        bool chosen = p.Focused(this), pressed = p.Pressed(this);
        p.Solid(Rect, Theme.Colours[pressed ? "pressed" : chosen ? "chosen" : "button"]);
        p.Edge(Rect, chosen ? 2f : 1.5f, Theme.Colours[chosen ? "chosenEdge" : "buttonEdge"]);
        if (chosen)
        {
            // Two small arrow heads at the ends pointing in at the words, as a menu's chosen word is marked.
            float mid = Rect.Y + Rect.Height * 0.5f;
            p.Arrow(Rect.X + 30f, mid, 10f, +1, Theme.Colours["chosenEdge"]);
            p.Arrow(Rect.Right - 30f, mid, 10f, -1, Theme.Colours["chosenEdge"]);
        }
        if (IconDirection != 0) { p.ArrowUpDown(this, IconDirection, Theme.Colours[Enabled ? (chosen ? "bright" : "parchment") : "muted"]); return; }
        p.Text(Scope.Fill(Text), Style, Rect, TextAlign.Centre, chosen ? Theme.Colours["bright"] : null, chosen ? Theme.Chosen : null);
    }

    public override void Activate(Screen screen) => Fire(screen);
}

/// <summary>
/// A key cell: the name of what works an action (a key, a mouse button, a wheel turn) in the book face, in a dark
/// box. Its look follows the data's <see cref="State"/>: <c>waiting</c> for the next key, <c>lost</c> (its key was
/// just taken for another action), or nothing. A click fires its action, which the host answers by waiting.
/// </summary>
public sealed class KeyCell : Worked
{
    public string Text { get; set; } = "";
    /// <summary>The data's name for the cell's state.</summary>
    public string? State { get; set; }
    public TextStyle Style { get; set; } = Theme.Styles["value"];
    /// <summary>What a cell shows while it waits, and when it is empty.</summary>
    public string WaitingText { get; set; } = "Press a Key";
    public string EmptyText { get; set; } = "—";

    public string StateNow => State is null ? "" : Scope.Text(State);

    protected override Vector2 MeasureContent(Vector2 room) =>
        new(MathF.Max(160f, Letters.Measure(Style.Lettering, Scope.Fill(Text), Style.Em) + 48f), Style.Em * 1.7f);

    public override void Draw(Painter p)
    {
        var state = StateNow;
        bool chosen = p.Focused(this);
        var (fill, edge) = state switch
        {
            "waiting" => ("waiting", "waitingEdge"),
            "lost" => ("lost", "lostEdge"),
            _ => (p.Pressed(this) ? "pressed" : chosen ? "chosen" : "cell", chosen ? "chosenEdge" : "cellEdge"),
        };
        p.Solid(Rect, Theme.Colours[fill]);
        p.Edge(Rect, state.Length > 0 || chosen ? 2f : 1f, Theme.Colours[edge]);
        var text = Scope.Fill(Text);
        if (state == "waiting") p.Text(WaitingText, Style, Rect, TextAlign.Centre, Theme.Colours["bright"], Theme.Chosen);
        else if (text.Length == 0) p.Text(EmptyText, Style, Rect, TextAlign.Centre, Theme.Colours["muted"]);
        else p.Text(text, Style, Rect, TextAlign.Centre, state == "lost" ? Theme.Colours["bright"] : chosen ? Theme.Colours["bright"] : null);
    }

    public override void Activate(Screen screen) => Fire(screen);
}

/// <summary>
/// A slider: a number of the data's (<see cref="Field"/>) between <see cref="Min"/> and <see cref="Max"/>, in steps of
/// <see cref="Step"/>, set by pressing or dragging along its track, or by Left and Right while it has the focus.
/// Each change is written to the data and fires the action.
/// </summary>
public sealed class Slider : Worked
{
    public string Field { get; set; } = "";
    public float Min { get; set; }
    public float Max { get; set; } = 1f;
    public float Step { get; set; }
    const float Knob = 16f;

    public override bool ClicksOnRelease => false;

    float Value => Scope.Number(Field);
    float Share => Max > Min ? Math.Clamp((Value - Min) / (Max - Min), 0f, 1f) : 0f;

    protected override Vector2 MeasureContent(Vector2 room) => new(360f, 44f);

    UiRect Track => new(Rect.X + Knob * 0.5f, Rect.Y + Rect.Height * 0.5f - 4f, MathF.Max(0f, Rect.Width - Knob), 8f);

    public override void Draw(Painter p)
    {
        bool chosen = p.Focused(this);
        var track = Track;
        p.Solid(track, Theme.Colours["track"]);
        p.Solid(track with { Width = track.Width * Share }, Theme.Colours["trackFill"]);
        float x = track.X + track.Width * Share;
        var knob = new UiRect(x - Knob * 0.5f, Rect.Y + 4f, Knob, Rect.Height - 8f);
        if (chosen) p.Solid(knob.Inset(-4f), Theme.Colours["chosenEdge"]);
        p.Solid(knob, Theme.Colours["knob"]);
        p.Edge(knob, 1f, new Rgba(0.1f, 0.06f, 0.04f, 1f));
    }

    public override bool Nudge(Screen screen, int direction)
    {
        float step = Step > 0f ? Step : (Max - Min) / 20f;
        Set(screen, Value + direction * step);
        return true;
    }

    public override void PointerDown(Screen screen, float x) => PointerDrag(screen, x);

    public override void PointerDrag(Screen screen, float x)
    {
        var track = Track;
        float share = track.Width > 0f ? Math.Clamp((x - track.X) / track.Width, 0f, 1f) : 0f;
        Set(screen, Min + share * (Max - Min));
    }

    void Set(Screen screen, float v)
    {
        if (Step > 0f) v = Min + MathF.Round((v - Min) / Step) * Step;
        v = Math.Clamp(v, Min, Max);
        // Steps are kept to the step's own decimals, so 0.0022 stays 0.0022 and not 0.00219999.
        v = (float)Math.Round(v, 6);
        if (v == Value) return;
        Scope.Set(Field, v);
        Fire(screen);
    }
}

/// <summary>
/// A toggle: one of a few words (<see cref="Options"/>; Off and On by default) kept in the data's <see cref="Field"/>,
/// shown between two small arrows; a click or Enter or Right goes to the next, Left to the one before. Each change
/// fires the action.
/// </summary>
public sealed class Toggle : Worked
{
    public string Field { get; set; } = "";
    public string[] Options { get; set; } = ["Off", "On"];
    public TextStyle Style { get; set; } = Theme.Styles["value"];

    /// <summary>
    /// Its options side by side, each a part of its width (the launcher's Leave / Import): a click picks the one under the
    /// pointer instead of going to the next. Left and Right still step through them.
    /// </summary>
    public bool Segments { get; set; }

    /// <summary>The option under the pointer at <paramref name="x"/> (units) when its options stand side by side.</summary>
    public int SegmentAt(float x) =>
        Options.Length == 0 || Rect.Width <= 0f ? -1 : Math.Clamp((int)((x - Rect.X) / (Rect.Width / Options.Length)), 0, Options.Length - 1);

    int pressedAt = -1;

    public override void PointerDown(Screen screen, float x) => pressedAt = Segments ? SegmentAt(x) : -1;

    protected override Vector2 MeasureContent(Vector2 room)
    {
        float w = 0;
        foreach (var o in Options) w = MathF.Max(w, Letters.Measure(Style.Lettering, o, Style.Em));
        return new Vector2(w + 120f, Style.Em * 1.7f);
    }

    public override void Draw(Painter p)
    {
        bool chosen = p.Focused(this);
        p.Solid(Rect, Theme.Colours[p.Pressed(this) ? "pressed" : chosen ? "chosen" : "cell"]);
        p.Edge(Rect, chosen ? 2f : 1f, Theme.Colours[chosen ? "chosenEdge" : "cellEdge"]);
        var arrows = Theme.Colours[chosen ? "chosenEdge" : "muted"];
        float mid = Rect.Y + Rect.Height * 0.5f;
        p.Arrow(Rect.X + 20f, mid, 12f, -1, arrows);
        p.Arrow(Rect.Right - 20f, mid, 12f, +1, arrows);
        p.Text(Scope.Text(Field), Style, Rect, TextAlign.Centre, chosen ? Theme.Colours["bright"] : null);
    }

    public override void Activate(Screen screen)
    {
        // A click on one of its side-by-side options picks that one; Enter (or a click on a plain toggle) goes to the next.
        if (pressedAt >= 0 && pressedAt < Options.Length)
        {
            int at = pressedAt;
            pressedAt = -1;
            if (Scope.Text(Field) == Options[at]) return;
            Scope.Set(Field, Options[at]);
            Fire(screen);
            return;
        }
        Nudge(screen, +1);
    }

    public override bool Nudge(Screen screen, int direction)
    {
        if (Options.Length == 0) return true;
        int at = Array.IndexOf(Options, Scope.Text(Field));
        int next = at < 0 ? 0 : ((at + direction) % Options.Length + Options.Length) % Options.Length;
        Scope.Set(Field, Options[next]);
        Fire(screen);
        return true;
    }
}

/// <summary>
/// A meter (the HUD's bars): a track filled from its left by the share the data's number <see cref="Field"/> names
/// (0 to 1, kept within), in <see cref="Fill"/>'s colour over <see cref="Track"/>'s, with a thin <see cref="Edge"/> round
/// it. Each colour may be named from the data (<c>"{slotTone}"</c>). It shows; it is not worked.
/// </summary>
public sealed class Meter : Piece
{
    public string Field { get; set; } = "";
    public string Fill { get; set; } = "trackFill";
    public string Track { get; set; } = "track";
    public string? Edge { get; set; }

    /// <summary>The share filled now (0 to 1).</summary>
    public float Share => Math.Clamp(Scope.Number(Field), 0f, 1f);

    protected override Vector2 MeasureContent(Vector2 room) => new(float.IsInfinity(room.X) ? 100f : room.X, 12f);

    public override void Draw(Painter p)
    {
        p.Solid(Rect, Screen.Colour(Scope.Fill(Track)));
        float share = Share;
        if (share > 0f) p.Solid(new UiRect(Rect.X, Rect.Y, Rect.Width * share, Rect.Height), Screen.Colour(Scope.Fill(Fill)));
        if (Edge is not null) p.Edge(Rect, 1f, Screen.Colour(Scope.Fill(Edge)));
    }
}

/// <summary>A piece the file named but no one has registered (or nothing at all): it takes no room and draws nothing.</summary>
public sealed class Nothing : Piece
{
    public override void Draw(Painter p) { }
}

/// <summary>What a piece asks of the host: an action's name, its argument (names filled in), the data it was in, and the piece.</summary>
public readonly record struct UiAction(string Name, string Argument, ScreenData Scope, Piece? Source)
{
    public override string ToString() => Argument.Length == 0 ? Name : $"{Name}({Argument})";
}

/// <summary>The keys a screen answers (the host turns its own key events into these). Space works the focused piece; Enter works the default button from a checkbox or a radio.</summary>
public enum UiKey : byte { Up, Down, Left, Right, Tab, BackTab, Enter, Escape, Space, AltUp, AltDown }

internal static class Invariant
{
    public static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
