// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The effect icons in the top right corner, drawn. Where each one goes and how opaque it is belongs
// to `Vitomir.Engine.Ui.EffectIcons`; this puts the bitmaps on screen.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>Draws the steered character's effect icons.</summary>
public sealed class EffectIconsPass(UiCanvas ui) : IUiPass
{
    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    /// <summary>Whose icons: the character being steered, or nobody.</summary>
    public Func<WorldThing?>? Steered { get; set; }

    /// <summary>Whether the status bar is up, which puts the icons lower (<see cref="EffectIcons.TopWithBar"/>).</summary>
    public Func<bool>? BarShown { get; set; }

    /// <summary>
    /// Whether something has the screen that hides them: the discipline pane, the inventory pane,
    /// a shop or a conversation (the draw's gates).
    /// </summary>
    public Func<bool>? Hidden { get; set; }

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (width <= 0 || height <= 0 || Hidden?.Invoke() == true) return;
        var icons = EffectIcons.Layout(Steered?.Invoke(), BarShown?.Invoke() ?? false);
        if (icons.Count == 0) return;
        ui.Begin(width, height);
        // Mode 0xb with the blink in the colour's alpha (`round(fade) << 24 | 0xffffff`).
        foreach (var icon in icons)
            ui.Sub(icon.Bitmap, icon.X, icon.Y, EffectIcons.Width, EffectIcons.Height,
                   0f, 0f, EffectIcons.UvWidth, 1f, new Tint(1f, 1f, 1f, icon.Alpha));
    }
}
