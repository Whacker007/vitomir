// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The launcher's one piece of its own: a line of text the player types, the Setup page's folder.
using System.Numerics;
using Vitomir.Platform.Interface;

namespace Vitomir.Launcher;

/// <summary>
/// **A text box** (registered as <c>textbox</c>): one line of the data's <see cref="Field"/> in the book face, in a dark cell like
/// a key cell, its end shown when it is longer than the box. A click or Enter fires <c>edit</c> (with the field's name): the launcher then sends it what is typed (<see cref="Launcher.Type"/>) until Enter (which fires
/// <see cref="Commit"/>), Escape, Tab or a click elsewhere. While it is being typed in (the data's <c>editing</c> is its field) a caret
/// stands after the words. Nothing here knows SDL: the launcher's window turns its text events into <see cref="Launcher.Type"/>.
/// </summary>
public sealed class TextBox : Worked
{
    /// <summary>The kind's name in a screen's file.</summary>
    public const string Kind = "textbox";

    /// <summary>The data's name it shows and the launcher writes.</summary>
    public string Field { get; set; } = "";

    /// <summary>What it shows, dimmed, while it is empty.</summary>
    public string Placeholder { get; set; } = "";

    public TextStyle Style { get; set; } = Theme.Styles["value"];

    /// <summary>The data's name that holds the field being typed in.</summary>
    public const string Editing = "editing";

    /// <summary>Registers the kind with the interface's loader (once; again is harmless).</summary>
    public static void Register() => PieceTypes.Register(Kind, (r, s) => new TextBox
    {
        Field = r.String("bind", ""), Commit = r.String("action"), Placeholder = r.String("placeholder", ""), Style = r.Style("style", "value"),
        ErrorWhen = r.String("error"), EnabledWhen = r.String("enabled"), Why = r.String("why"),
    }.Asking());

    /// <summary>A name in the data that is true while what was typed is refused (the Setup page's folder: <c>pathWhy</c>), or none.</summary>
    public string? ErrorWhen { get; set; }

    /// <summary>Whether what was typed is refused now (the launcher's look draws the box in its error state).</summary>
    public bool Error => ErrorWhen is not null && Scope.Flag(ErrorWhen);

    /// <summary>The action Enter fires while it is being typed in (the file's <c>action</c>: the Setup page's <c>usefolder</c>).</summary>
    public string? Commit { get; set; }

    /// <summary>Its own action: <c>edit</c>, its field's name the argument (the launcher then types into it).</summary>
    TextBox Asking()
    {
        Action = "edit";
        Argument = Field;
        return this;
    }

    /// <summary>Whether it is being typed in now.</summary>
    public bool Typing => Scope.Text(Editing) == Field && Field.Length > 0;

    protected override Vector2 MeasureContent(Vector2 room) => new(float.IsInfinity(room.X) ? 600f : room.X, Style.Em * 1.8f);

    public override void Draw(Painter p)
    {
        bool chosen = p.Focused(this) || Typing;
        p.Solid(Rect, Theme.Colours["cell"]);
        p.Edge(Rect, chosen ? 2f : 1f, Theme.Colours[Typing ? "chosenEdge" : chosen ? "buttonEdge" : "cellEdge"]);
        const float pad = 14f;
        var inner = new UiRect(Rect.X + pad, Rect.Y, MathF.Max(0f, Rect.Width - 2 * pad), Rect.Height);
        string text = Scope.Text(Field);
        p.PushClip(inner);
        if (text.Length == 0 && !Typing) p.Text(Placeholder, Style, inner, TextAlign.Start, Theme.Colours["muted"]);
        else
        {
            float width = Letters.Measure(Style.Lettering, text, Style.Em);
            float caret = Typing ? 6f : 0f;
            // Longer than the box: its end shown (where one types), the start cut off at the left.
            var box = width + caret > inner.Width ? inner with { X = inner.Right - width - caret, Width = width + caret } : inner;
            p.Text(text, Style, box, TextAlign.Start, Typing ? Theme.Colours["bright"] : null);
            if (Typing)
                p.Solid(new UiRect(box.X + width + 2f, Rect.Y + Rect.Height * 0.2f, 3f, Rect.Height * 0.6f), Theme.Colours["gold"]);
        }
        p.PopClip();
    }

    /// <summary>A click or Enter: the launcher is asked to type into it (<c>edit</c>, the field's name).</summary>
    public override void Activate(Screen screen) => Fire(screen);
}
