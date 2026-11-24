// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The camera's screen effects: fades, flashes and the tinted
// filter (set by the camera, and drawn by it).

namespace Vitomir.Engine.World;

/// <summary>
/// Eight effect slots on the camera (0x2c bytes each) and the three numbers they
/// come to each frame. Scripts start them through <c>CodexCamera.AddFade</c>, <c>AddFlash</c> and
/// <c>AddFilter</c> and stop them all with <c>ClearAllEffects</c>.
///
/// <list type="bullet">
/// <item>A <b>fade</b> runs a level from one value to another: 255 is the picture as it is, 0 is
/// black. The screen shows the <em>lowest</em> of the running fades, and — this is the part to
/// know — <b>when the last fade finishes the level stays where it ended</b>. A script that fades to
/// black and does nothing else leaves the screen black until another fade brings it back.</item>
/// <item>A <b>flash</b> runs an intensity (0..255) and a colour; the strongest running flash
/// wins, and it too holds its last value once none is running.</item>
/// <item>A <b>filter</b> is a tint on a timer, on while its time lasts.</item>
/// </list>
///
/// A fade or flash asked to <b>bounce</b> runs out in half the time and then comes back the same
/// way in the other half.
///
/// They are drawn over everything, the interface included (the frame draws them after the UI): a
/// fade is a white quad in <c>materials/General/fade.nam</c>'s <c>subblend</c> with every channel
/// <c>255 − level</c>, and a flash is <c>whiteFlash.nam</c>'s <c>addblend</c> with the colour scaled
/// by <c>intensity / 255</c>.
/// </summary>
public sealed class ScreenEffects
{
    /// <summary>What a slot holds (<c>slot+0</c>).</summary>
    public enum Kind { Fade = 0, Flash = 1, Filter = 2 }

    sealed class Slot
    {
        public Kind Kind;
        public int Id;              // 0 is a free slot
        public int End;             // on the game clock
        public int Duration;        // a filter keeps its colour here instead
        public float Initial;
        public float Current;
        public float Target;
        public float Rate;          // per millisecond
        public uint Color;          // a fade keeps its bounce flag here instead
        public bool Bounce;         // its own word for a flash, the colour's for a fade
    }

    /// <summary>How many effects can run at once.</summary>
    public const int Slots = 8;

    readonly Slot[] slots = [.. Enumerable.Range(0, Slots).Select(_ => new Slot())];
    int nextId = 1;

    /// <summary>
    /// The fade level the screen is at (<c>_DAT_00502384</c>): 255 for no fade, 0 for black. Held
    /// between fades.
    /// </summary>
    public float FadeLevel { get; private set; } = 255f;

    /// <summary>The flash's intensity (<c>_DAT_0061584c</c>, 0..255) and colour (0xRRGGBB).</summary>
    public float FlashIntensity { get; private set; }
    /// <inheritdoc cref="FlashIntensity"/>
    public uint FlashColor { get; private set; }

    /// <summary>Whether the tint is on and its colour.</summary>
    public bool FilterOn { get; private set; }
    /// <inheritdoc cref="FilterOn"/>
    public uint FilterColor { get; private set; }

    /// <summary>
    /// A fade: the level from <paramref name="initial"/> to <paramref name="target"/>
    /// over <paramref name="ms"/>. Answers the slot's id, or 0 when all eight are busy.
    /// </summary>
    public int AddFade(float initial, float target, int ms, bool bounce, int nowMs)
    {
        var s = Free();
        if (s is null) return 0;
        int run = bounce ? (int)((uint)ms >> 1) : ms;
        s.Kind = Kind.Fade;
        s.Id = nextId++;
        s.End = nowMs + run;
        s.Initial = s.Current = initial;
        s.Target = target;
        s.Rate = ms == 0 ? 0f : (target - initial) / run;
        s.Duration = run;
        s.Bounce = bounce;
        return s.Id;
    }

    /// <summary>A flash: as a fade, with a colour.</summary>
    public int AddFlash(float initial, float target, int ms, uint color, bool bounce, int nowMs)
    {
        var s = Free();
        if (s is null) return 0;
        int run = bounce ? (int)((uint)ms >> 1) : ms;
        s.Kind = Kind.Flash;
        s.Id = nextId++;
        s.End = nowMs + run;
        s.Initial = s.Current = initial;
        s.Target = target;
        s.Rate = ms == 0 ? 0f : (target - initial) / run;
        s.Duration = run;
        s.Color = color;
        s.Bounce = bounce;
        return s.Id;
    }

    /// <summary>A filter: the tint, on for <paramref name="ms"/>.</summary>
    public int AddFilter(int ms, uint color, int nowMs)
    {
        var s = Free();
        if (s is null) return 0;
        s.Kind = Kind.Filter;
        s.Id = nextId++;
        s.End = nowMs + ms;
        s.Color = color;
        return s.Id;
    }

    /// <summary>
    /// Everything off: the slots freed, the screen unfaded, no flash, no tint. The only
    /// thing that puts a held fade level back to 255 without a fade of its own.
    /// </summary>
    public void ClearAll()
    {
        foreach (var s in slots) s.Id = 0;
        FadeLevel = 255f;
        FlashIntensity = 0f;
        FilterOn = false;
    }

    /// <summary>
    /// One frame. A running effect moves by its rate times the frame; one whose time is
    /// up takes its target, frees its slot and, if it bounces, starts its own way back.
    /// </summary>
    public void Step(int nowMs, int frameMs)
    {
        bool faded = false;
        float lowest = 255f, strongest = 0f;
        uint strongestColor = 0;
        int flashes = 0;
        bool filter = false;
        uint filterColor = 0;
        foreach (var s in slots)
        {
            if (s.Id == 0) continue;
            switch (s.Kind)
            {
                case Kind.Fade:
                    if (nowMs < s.End)
                    {
                        s.Current = frameMs * s.Rate + s.Current;
                        if (s.Current < lowest) lowest = s.Current;
                    }
                    else
                    {
                        if (s.Target < lowest) lowest = s.Target;
                        s.Id = 0;
                        if (s.Bounce) AddFade(s.Target, s.Initial, s.Duration, false, nowMs);
                    }
                    faded = true;
                    break;
                case Kind.Flash:
                    if (nowMs < s.End)
                    {
                        s.Current = frameMs * s.Rate + s.Current;
                        if (strongest < s.Current) { strongestColor = s.Color; strongest = s.Current; }
                    }
                    else
                    {
                        if (strongest < s.Target) { strongest = s.Target; strongestColor = s.Color; }
                        s.Id = 0;
                        if (s.Bounce) AddFlash(s.Target, s.Initial, s.Duration, s.Color, false, nowMs);
                    }
                    flashes++;
                    break;
                case Kind.Filter:
                    if (nowMs < s.End) { filterColor = s.Color; filter = true; }
                    else s.Id = 0;
                    break;
            }
        }
        if (faded) FadeLevel = lowest;
        if (flashes != 0) { FlashIntensity = strongest; FlashColor = strongestColor; }
        FilterOn = filter;
        FilterColor = filterColor;
    }

    /// <summary>How many slots are in use.</summary>
    public int Running => slots.Count(s => s.Id != 0);

    Slot? Free() => slots.FirstOrDefault(s => s.Id == 0);
}
