// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Platform.Interface;

/// <summary>
/// A choice the player makes by a mark beside its label: a <see cref="Checkbox"/> (on or off, on its own) or a
/// <see cref="Radio"/> (one of a group). The mark stands at the piece's left, <see cref="MarkSize"/> units square, centred on
/// the label's first line; the label is its <see cref="Text"/>, or what it holds (one piece: a column of lines), or both, the
/// text first. **The whole piece is worked**, the label as much as the mark: a click anywhere on it, or Space (Enter too, when
/// the screen has no default button) while it has the focus.
/// </summary>
public abstract class Choice : Worked
{
    /// <summary>The mark's side, units (the kit's 32-pixel pictures drawn a unit a pixel: the same size beside the layout's words as
    /// the kit's own 16 beside its 15.5-unit words).</summary>
    public const float DefaultMark = 32f;

    /// <summary>The room between the mark and the label, units.</summary>
    public const float Gap = 14f;

    public string Text { get; set; } = "";
    public TextStyle Style { get; set; } = Theme.Styles["label"];
    public bool Wrap { get; set; }
    public Edges Padding { get; set; }
    public float MarkSize { get; set; } = DefaultMark;

    /// <summary>The mark's width, units: <see cref="MarkSize"/> (square), a <see cref="Switch"/>'s twice that.</summary>
    public virtual float MarkWidth => MarkSize;

    /// <summary>Whether it is on (a checkbox ticked, a radio the group's selected one).</summary>
    public abstract bool Checked { get; }

    /// <summary>Where the mark is, from the last layout.</summary>
    public UiRect MarkRect { get; private set; }

    /// <summary>Where the label's own words are, from the last layout (empty without <see cref="Text"/>).</summary>
    public UiRect LabelRect { get; private set; }

    float LineHeight => Style.Em * Style.LineHeight;

    /// <summary>The label's first line's height: its words', else the first words of what it holds, else the mark's.</summary>
    float FirstLine()
    {
        if (Scope.Fill(Text).Length > 0) return LineHeight;
        foreach (var c in Children)
            foreach (var p in c.Walk())
                if (p is TextPiece t && p.Shown) return t.Style.Em * t.Style.LineHeight;
        return MarkSize;
    }

    (float Width, float Height) Words(float room)
    {
        string text = Scope.Fill(Text);
        if (text.Length == 0) return (0f, 0f);
        if (Wrap && !float.IsInfinity(room))
        {
            var lines = Letters.Wrap(Style.Lettering, text, Style.Em, room);
            float w = 0f;
            foreach (var l in lines) w = MathF.Max(w, Letters.Measure(Style.Lettering, l, Style.Em));
            return (w, lines.Count * LineHeight);
        }
        return (Letters.Measure(Style.Lettering, text, Style.Em), LineHeight);
    }

    protected override Vector2 MeasureContent(Vector2 room)
    {
        float across = MarkWidth + Gap;
        float labelRoom = room.X - Padding.Horizontal - across;
        var (tw, th) = Words(labelRoom);
        var child = Vector2.Zero;
        foreach (var c in Children) if (c.Shown) child = Vector2.Max(child, c.Measure(new Vector2(labelRoom, float.PositiveInfinity)));
        float drop = MathF.Max(0f, (MarkSize - FirstLine()) * 0.5f);
        float h = drop + th + (th > 0f && child.Y > 0f ? 4f : 0f) + child.Y;
        return new Vector2(across + MathF.Max(tw, child.X) + Padding.Horizontal, MathF.Max(MarkSize, h) + Padding.Vertical);
    }

    public override void Arrange(UiRect rect)
    {
        Rect = rect;
        var inner = rect.Inset(Padding);
        float first = FirstLine();
        // The label drops when its first line is shorter than the mark, so the mark never stands above the piece.
        float drop = MathF.Max(0f, (MarkSize - first) * 0.5f);
        MarkRect = new UiRect(inner.X, inner.Y + drop + (first - MarkSize) * 0.5f, MarkWidth, MarkSize);
        float x = inner.X + MarkWidth + Gap, width = MathF.Max(0f, inner.Right - x);
        var (_, th) = Words(width);
        LabelRect = new UiRect(x, inner.Y + drop, width, th);
        float y = inner.Y + drop + th + (th > 0f ? 4f : 0f);
        foreach (var c in Children)
            if (c.Shown)
            {
                var size = c.Measure(new Vector2(width, float.PositiveInfinity));
                c.Arrange(new UiRect(x, y, c.Width.Kind == SizeKind.Fill ? width : MathF.Min(size.X, width), size.Y));
            }
    }

    /// <summary>The label's own words drawn in <paramref name="colour"/> (null: its style's), wrapped as laid out.</summary>
    public void DrawLabel(Painter p, Rgba? colour)
    {
        string text = Scope.Fill(Text);
        if (text.Length == 0) return;
        if (!Wrap) { p.Text(text, Style, LabelRect with { Height = LineHeight }, TextAlign.Start, colour); return; }
        var lines = Letters.Wrap(Style.Lettering, text, Style.Em, LabelRect.Width);
        for (int i = 0; i < lines.Count; i++)
            p.Text(lines[i], Style, new UiRect(LabelRect.X, LabelRect.Y + i * LineHeight, LabelRect.Width, LineHeight), TextAlign.Start, colour);
    }

    /// <summary>The theme's plain look: a dark cell with an edge, filled when on; dimmed words when disabled.</summary>
    public override void Draw(Painter p)
    {
        bool focused = p.Focused(this);
        var mark = MarkRect;
        p.Solid(mark, Theme.Colours["cell"]);
        p.Edge(mark, focused ? 2f : 1f, Theme.Colours[focused ? "chosenEdge" : "cellEdge"]);
        if (this is Switch)
        {
            // The knob: left when off, right when on (the state never rests on colour alone).
            var knob = new UiRect(Checked ? mark.Right - mark.Height : mark.X, mark.Y, mark.Height, mark.Height).Inset(MarkSize * 0.2f);
            p.Solid(knob, Theme.Colours[!Enabled ? "muted" : Checked ? "gold" : "cellEdge"]);
        }
        else if (Checked) p.Solid(mark.Inset(MarkSize * (this is Radio ? 0.3f : 0.22f)), Theme.Colours[Enabled ? "gold" : "muted"]);
        Rgba? words = Enabled ? null : Theme.Colours["muted"];
        DrawLabel(p, words);
        var was = p.WordsColour;
        p.WordsColour = words;
        DrawChildren(p);
        p.WordsColour = was;
    }
}

/// <summary>
/// **A checkbox**: one setting on or off, on its own (a save to import or not). Its state is the data's flag <see cref="Field"/>; a
/// click or Space turns it over, writes it back (where the name is found: a list's item, or the screen's data) and fires its action.
/// </summary>
public sealed class Checkbox : Choice
{
    public string Field { get; set; } = "";

    public override bool Checked => Scope.Flag(Field);

    public override void Activate(Screen screen)
    {
        Scope.SetWhere(Field, !Checked);
        Fire(screen);
    }
}

/// <summary>
/// **A radio button**: one of a few that exclude one another (the copy of the game the setup works on). The group's choice is the
/// data's <see cref="Field"/>; this one is on while it holds <see cref="Value"/> (names filled in: <c>"{n}"</c> in a list's row).
/// A click or Space selects it: the field written (where the name is found) and its action fired, only when that changed it.
/// The arrow keys move to the group's next or previous one and select it, round the group's ends; Tab stops at a group once, at
/// its selected one (<see cref="Screen"/>). The group is <see cref="Group"/>, else the field's name.
/// </summary>
public sealed class Radio : Choice
{
    public string Field { get; set; } = "";
    public string Value { get; set; } = "";
    public string? GroupName { get; set; }

    /// <summary>The group's name: radios of one name exclude one another.</summary>
    public string Group => GroupName ?? Field;

    /// <summary>The value this one stands for now.</summary>
    public string ValueNow => Scope.Fill(Value);

    public override bool Checked => Scope.Text(Field) == ValueNow;

    public override void Activate(Screen screen) => Select(screen);

    /// <summary>Selects this one: the field set to its value and its action fired; nothing when it is selected already.</summary>
    public void Select(Screen screen)
    {
        if (Checked) return;
        Scope.SetWhere(Field, ValueNow);
        Fire(screen);
    }

    /// <summary>Left and Right (Up and Down too, by the screen) step through the group.</summary>
    public override bool Nudge(Screen screen, int direction) => screen.StepRadio(this, direction);
}

/// <summary>
/// **A toggle switch**: a setting that acts at once (a shortcut made or removed), where a <see cref="Checkbox"/> is a choice
/// kept for later. On or off is the data's flag <see cref="Field"/>; a click or Space fires its action and turns the flag over —
/// the action may set the flag again to what really happened (the shortcut's state found on disk), which the next frame shows.
/// The mark is twice as wide as it is high (the kit's <c>switch_*</c>: 64 by 32 units).
/// </summary>
public sealed class Switch : Choice
{
    public string Field { get; set; } = "";

    public override float MarkWidth => MarkSize * 2f;

    public override bool Checked => Scope.Flag(Field);

    public override void Activate(Screen screen)
    {
        Scope.SetWhere(Field, !Checked);
        Fire(screen);
    }
}
