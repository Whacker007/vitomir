// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The ui library's scroll bar and its slider — `element scrollbar` with `slider=`, `vertical=1` and no
// `quanta` — as a text box's `vscrollbar` drives it: the read dialog's `readScrollBar` and the
// status bar's and the chat's message boxes (`MessageBox`, which had its own copy until then).
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// A vertical scroll bar and its slider, in the pane's own space (y counting up, a control's
/// <c>Y</c> its bottom edge). The bar keeps a range and a value;
/// the slider sits along the track where the value says and, held, says
/// where the value is. The text box it belongs to owns the scroll: it sets the range
/// and the value, and it scrolls to minus the value when the slider moves (its hook).
/// </summary>
public sealed class ScrollBar
{
    /// <summary>The bar: the track, which a press pages along.</summary>
    public UiControl Bar { get; }
    /// <summary>The slider, the bar's child (the bar's parse re-parents it), so it is hit first.</summary>
    public UiControl Slider { get; }

    public ScrollBar(UiControl bar, UiControl slider)
    {
        Bar = bar;
        Slider = slider;
        Slider.X = bar.X;
    }

    /// <summary>The pane's `scrollbar` and the `scrollslider` its `slider=` names, or null when either is missing.</summary>
    public static ScrollBar? Of(NuiPane pane, string bar)
    {
        var track = pane.Elements.FirstOrDefault(e => e.Name.Equals(bar, StringComparison.OrdinalIgnoreCase));
        if (track?.Get("slider") is not { Length: > 0 } sliderName) return null;
        var slider = pane.Elements.FirstOrDefault(e => e.Name.Equals(sliderName, StringComparison.OrdinalIgnoreCase));
        return slider is null ? null : new ScrollBar(new UiControl(track), new UiControl(slider));
    }

    /// <summary>The range's two ends, as the box last set them.</summary>
    public int Min { get; private set; }
    /// <inheritdoc cref="Min"/>
    public int Max { get; private set; }
    /// <summary>The value: minus the box's scroll.</summary>
    public int Value { get; private set; }

    /// <summary>How far the slider can travel: the bar's height less its own.</summary>
    public int Travel => Bar.Height - Slider.Height;

    /// <summary>Whether the slider is held (the interface's captured control).</summary>
    public bool Held { get; private set; }

    float lastPointerY, carry;

    /// <summary>The range set, and the slider put where the value now says.</summary>
    public void SetRange(int min, int max)
    {
        Min = min;
        Max = max;
        Place();
    }

    /// <summary>
    /// The value set (a bar without `quanta`): **anything outside the range becomes the
    /// range's minimum**, not its nearer end — which, for a box that fits, is every value.
    /// </summary>
    public void SetValue(int value)
    {
        Value = Min <= value && value <= Max ? value : Min;
        Place();
    }

    /// <summary>
    /// The slider's bottom from the value: at the top of the track for the range's top
    /// end, at the bottom for its other, and at the bottom for an empty range. Moving a slider that
    /// is held lets go of it.
    /// </summary>
    void Place()
    {
        int y = Min == Max ? Bar.Y : (int)((Value - Min) / (float)(Max - Min) * Travel + Bar.Y);
        if (Slider.Y == y) return;
        Held = false;
        Slider.Y = y;
    }

    /// <summary>
    /// A press on the track at pane <paramref name="y"/>, not on the slider: the box a
    /// page towards its start when the press is above the slider's bottom, else towards its end.
    /// Answers the sign of the scroll: −1 or +1.
    /// </summary>
    public int PageAt(float y) => y > Slider.Y ? -1 : +1;

    /// <summary>A press on the slider, when the box has somewhere to scroll: it is held.</summary>
    public void Take(float y)
    {
        Held = true;
        lastPointerY = y;
        carry = 0f;
    }

    /// <summary>
    /// The held slider, with the pointer now at pane <paramref name="y"/>: it moves by
    /// what the pointer moved, kept on its track, and the value is read back from where it is.
    /// The original moves it by the mouse's own counts each frame; this moves it by the
    /// pointer's travel in the pane, so it stays under the pointer at any window size (ours, as the
    /// message box's). Answers whether the value changed — the box then scrolls to it.
    /// </summary>
    public bool DragTo(float y)
    {
        if (!Held) return false;
        carry += y - lastPointerY;
        lastPointerY = y;
        int step = (int)carry;
        carry -= step;
        Slider.Y = Math.Clamp(Slider.Y + step, Bar.Y, Bar.Y + Travel);
        return FromSlider();
    }

    /// <summary>The value from where the slider is (the fraction of the travel times the range, truncated).</summary>
    bool FromSlider()
    {
        if (Travel == 0) return false;
        int value = (int)((Slider.Y - Bar.Y) / (float)Travel * (Max - Min) + Min);
        if (value == Value) return false;
        Value = value;
        return true;
    }

    /// <summary>The button let go: the slider stays where it was dropped.</summary>
    public void Release() => Held = false;
}
