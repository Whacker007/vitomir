// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Play page checklist's mark: a tick, a cross, an exclamation mark or three dots, drawn of strokes (the delivered fonts
// have no tick or cross).
using System.Numerics;
using Vitomir.Platform.Interface;

namespace Vitomir.Launcher;

/// <summary>
/// **A readiness item's mark** (registered as <c>mark</c>): the data's <see cref="Field"/> names it, <c>ready</c> a tick, <c>failed</c>
/// a cross, <c>warning</c> an exclamation mark, <c>waiting</c> three dots, in a square of <see cref="Size"/> units; and a row's chevron,
/// <c>closed</c> (▸, its details closed) or <c>open</c> (▾). Drawn of short
/// strokes, each a run of small squares, in the colour the look gives it (<see cref="CourtLook"/>: the kit's text, warning red, gold,
/// muted); the item's words beside it say the same, so the mark is never the only sign.
/// </summary>
public sealed class ReadyMark : Piece
{
    /// <summary>The kind's name in a screen's file.</summary>
    public const string Kind = "mark";

    /// <summary>The square's side, in units.</summary>
    public const float Size = 32f;

    /// <summary>The data's name that holds the mark (<c>ready</c>, <c>failed</c>, <c>warning</c>, <c>waiting</c>).</summary>
    public string Field { get; set; } = "mark";

    /// <summary>The mark shown now.</summary>
    public string Mark => Scope.Text(Field);

    /// <summary>Registers the kind with the interface's loader (once; again is harmless).</summary>
    public static void Register() => PieceTypes.Register(Kind, (r, s) => new ReadyMark { Field = r.String("bind", "mark") });

    protected override Vector2 MeasureContent(Vector2 room) => new(Size, Size);

    public override void Draw(Painter p) => Draw(p, Theme.Colours[Mark switch { "failed" => "chosenEdge", "waiting" => "muted", "warning" => "gold", _ => "parchment" }]);

    /// <summary>The mark in <paramref name="colour"/>, its strokes 3.4 units wide, in the middle of the piece.</summary>
    public void Draw(Painter p, Rgba colour)
    {
        float s = MathF.Min(Size, MathF.Min(Rect.Width, Rect.Height)), x = Rect.X + (Rect.Width - s) / 2f, y = Rect.Y + (Rect.Height - s) / 2f;
        Vector2 At(float u, float v) => new(x + u * s, y + v * s);
        const float w = 3.4f;
        switch (Mark)
        {
            case "ready":
                Stroke(p, At(0.16f, 0.54f), At(0.40f, 0.78f), w, colour);
                Stroke(p, At(0.40f, 0.78f), At(0.86f, 0.24f), w, colour);
                break;
            case "failed":
                Stroke(p, At(0.22f, 0.22f), At(0.78f, 0.78f), w, colour);
                Stroke(p, At(0.78f, 0.22f), At(0.22f, 0.78f), w, colour);
                break;
            case "warning":
                Stroke(p, At(0.5f, 0.14f), At(0.5f, 0.62f), w + 0.6f, colour);
                p.Solid(new UiRect(At(0.5f, 0.82f).X - (w + 0.6f) / 2f, At(0.5f, 0.76f).Y, w + 0.6f, w + 0.6f), colour);
                break;
            case "closed" or "open":
                {
                    // A row's chevron (its details closed, or open under it): a small filled triangle, pointing right or down, built of strips.
                    float side = s * 0.36f, cx = x + s / 2f, cy = y + s / 2f;
                    const int strips = 12;
                    for (int i = 0; i < strips; i++)
                    {
                        float t = (i + 0.5f) / strips, half = side * 0.5f * (1f - t), step = side / strips;
                        if (Mark == "closed") p.Solid(new UiRect(cx - side * 0.4f + i * step, cy - half, step + 0.5f, 2f * half), colour);
                        else p.Solid(new UiRect(cx - half, cy - side * 0.4f + i * step, 2f * half, step + 0.5f), colour);
                    }
                    break;
                }
            default:
                for (int i = 0; i < 3; i++) p.Solid(new UiRect(At(0.2f + 0.3f * i, 0.5f).X - w / 2f, At(0.5f, 0.5f).Y - w / 2f, w, w), colour);
                break;
        }
    }

    /// <summary>A stroke from <paramref name="a"/> to <paramref name="b"/>, <paramref name="width"/> units wide: squares stamped along it a fifth of its width apart.</summary>
    static void Stroke(Painter p, Vector2 a, Vector2 b, float width, Rgba colour)
    {
        float length = Vector2.Distance(a, b);
        int steps = Math.Max(1, (int)MathF.Ceiling(length / (width * 0.2f)));
        for (int i = 0; i <= steps; i++)
        {
            var c = Vector2.Lerp(a, b, i / (float)steps);
            p.Solid(new UiRect(c.X - width / 2f, c.Y - width / 2f, width, width), colour);
        }
    }
}
