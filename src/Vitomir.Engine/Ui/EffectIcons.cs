// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The icons of what is on the character you are steering — Celerity's, Fortitude's, a poison's —
// in the top right corner of the screen. One pass draws them, another finds the
// one under the pointer, and a right click on one that may be cancelled takes it off.
// All three walk the same list the same way; this is that walk.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// The effect icons. The engine places them in **screen** space — 640 × 480 with
/// y counting down, handed straight to the 2-D draw with no pane in between — so the numbers below
/// are that space's; what this hands out is the interface's usual y-up space, as every pane uses.
/// </summary>
public static class EffectIcons
{
    /// <summary>
    /// Where the rows sit, y down: the right-hand icon's left edge (609) and the first row's top —
    /// 444, in the bottom corner, or **305 while the status bar is up** (`*` is the
    /// bar's own pane), which lifts them clear of it. The second row is 36 **higher**
    /// (4 + 32): disciplines on the bottom row, everything else above.
    /// </summary>
    public const float Right = 609f, Top = 444f, TopWithBar = 305f, RowGap = 4f;

    /// <summary>The screen's height in the interface's space, for turning y down into y up.</summary>
    const float Screen = 480f;

    /// <summary>An icon is 27 wide and 32 tall, and the next one is 31 to the left.</summary>
    public const float Width = 27f, Height = 32f, Step = 31f;

    /// <summary>How much of the 32-pixel bitmap's width the 27-pixel icon shows.</summary>
    public const float UvWidth = 27f / 32f;

    /// <summary>One icon: whose it is, the bitmap, its lower-left corner (y up), and how opaque.</summary>
    public readonly record struct Icon(Effect Effect, string Bitmap, float X, float Y, float Alpha);

    /// <summary>
    /// The icons as the draw lays them: every live effect whose definition has an icon, in the
    /// character's own list order, on the row its definition names — 0 for disciplines, 1 for the
    /// rest — each row filling leftwards from the right edge. The alpha is the blink
    /// (<see cref="Effect.Fade"/>) out of 255: 200 when steady, so a steady icon is not quite solid.
    /// </summary>
    public static List<Icon> Layout(WorldThing? steered, bool barShown)
    {
        var icons = new List<Icon>();
        Walk(steered, barShown, drawn: true, (e, x, y) =>
        {
            icons.Add(new Icon(e, e.Definition.Icon, x, y, Math.Clamp(MathF.Round(e.Fade) / 255f, 0f, 1f)));
            return false;
        });
        return icons;
    }

    /// <summary>
    /// The effect whose icon is under the pointer. Its walk **does not skip a removed
    /// instance** — the draw does — so for the frame between an effect ending and its reaping, the
    /// icons to its left answer a click one place further right than they are drawn.
    /// </summary>
    public static Effect? At(WorldThing? steered, bool barShown, float ux, float uy)
    {
        Effect? found = null;
        Walk(steered, barShown, drawn: false, (e, x, y) =>
        {
            // The hit test is inclusive on all four edges.
            if (ux < x || ux > x + Width || uy < y || uy > y + Height) return false;
            found = e;
            return true;
        });
        return found;
    }

    /// <summary>
    /// The right click's first question: the icon under the pointer belongs to an effect
    /// the player may cancel (`effects.ned` flag 0x200) — and then it is taken off, with every
    /// consequence of a removal.
    /// </summary>
    public static bool Cancel(WorldThing? steered, bool barShown, float ux, float uy)
    {
        if (steered is null || At(steered, barShown, ux, uy) is not { } e) return false;
        if (!e.Definition.Has(EffectFlags.Cancellable)) return false;
        if (Net.ClientSeen.Cancel(steered, e)) return true;     // a player's machine asks the host
        EffectRuntime.Remove(steered, e, notify: true);
        return true;
    }

    static void Walk(WorldThing? steered, bool barShown, bool drawn, Func<Effect, float, float, bool> each)
    {
        if (steered is null) return;
        float top = barShown ? TopWithBar : Top;
        Span<float> y = [top, top - RowGap - Height];
        Span<float> x = [Right, Right];
        foreach (var e in steered.Effects.All)
        {
            if (e.Definition.Icon.Length == 0) continue;
            if (drawn && e.Spent) continue;
            int row = e.Definition.Row == 0 ? 0 : 1;
            if (each(e, x[row], Screen - y[row] - Height)) return;
            x[row] -= Step;
        }
    }
}
