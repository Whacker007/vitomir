// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The world's share of the screen across, while a pane is up: the interface's view split, read.
// The share down — the status bar's — is `Ui.StatusBar.WorldShare`.
namespace Vitomir.Engine.World;

/// <summary>
/// Which columns of the window the world is drawn into, as shares of its width, and how much larger
/// than the whole-screen picture it is drawn there. <see cref="Whole"/> is the whole width at its
/// own scale; a <see cref="Right"/> not past <see cref="Left"/> is nothing drawn at all.
/// </summary>
public readonly record struct ViewColumns(float Left, float Right, float Magnify)
{
    public static readonly ViewColumns Whole = new(0f, 1f, 1f);

    /// <summary>Whether the world is drawn at all (the split sets state 4 when it is not).</summary>
    public bool Drawn => Right > Left;

    /// <summary>The first column and how many, for a target <paramref name="width"/> wide.</summary>
    public (int X, int Width) Pixels(int width)
    {
        int x0 = (int)MathF.Round(width * Left), x1 = (int)MathF.Round(width * Right);
        return (x0, Math.Max(1, x1 - x0));
    }
}

/// <summary>
/// **The view split**, which every pane's show and hide hook calls: with the status bar up
/// (its pane shown), the world's height is the bar's share of the
/// screen (0.7583) and then, for each pane on the list, a **left** pane (by the pane's side flag,
/// its `visGroup`, 1) moves the world's left edge to the middle and a **right** pane (2) its right
/// edge, and each **halves the lens** (the lens times 0.5) that the renderer is given.
/// With a pane on each side the two edges meet, and the world is not drawn at
/// all: the game's state 4. Without the bar nothing is halved.
///
/// Half the lens over half the width is not the same scale: the picture in the half is **larger**,
/// by <c>tan(lens/2) / (2 tan(lens/4))</c> — 15% at the game's 80° — and Vitomir draws it so
/// (<see cref="Magnify"/>).
/// </summary>
public static class WorldView
{
    /// <summary>The share of the width a pane leaves, and the lens's halving (0.5).</summary>
    public const float Half = 0.5f;

    public static ViewColumns Columns(bool barUp, bool leftPane, bool rightPane, float lensDegrees)
    {
        if (!barUp || (!leftPane && !rightPane)) return ViewColumns.Whole;
        if (leftPane && rightPane) return new ViewColumns(Half, Half, 1f);
        return new ViewColumns(leftPane ? Half : 0f, rightPane ? Half : 1f, Magnify(lensDegrees));
    }

    /// <summary>How much larger the world is drawn over half the width with half the horizontal lens.</summary>
    public static float Magnify(float lensDegrees)
    {
        float half = lensDegrees * MathF.PI / 360f;
        return MathF.Tan(half) / (2f * MathF.Tan(half * 0.5f));
    }
}
