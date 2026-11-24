// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The replay tape's input lines: the neutral events written and read, and the old `ev` lines read.
using System.Globalization;
using Vitomir.Platform.Modules;

namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// **A tape's input events** (ours). Since format 2 a recording writes each
/// event the game's loop polled as an <c>in</c> line, the platform's neutral event (<see cref="LoopInput"/>) and nothing of
/// SDL's: <c>in key-down code=4 key=0x61 mod=0x1 t=…</c>, a field only when it is not zero. **The old <c>ev</c> lines**
/// (format 1, SDL's own events, field for field) are read too, as the same neutral events the live host
/// makes of SDL's (<c>SdlInput.From</c>, the platform renderer's): a key's scan code, key code and modifiers, a button's
/// number and place, a move's place, movement and buttons held, the wheel's turn and the pointer's place, text, the quit,
/// and the window's events the game reads by SDL's numbers for them; anything else (a window shown, its pixels' size, a
/// device added, an event kept as its bytes) is none, as the live host makes none of it. So the baseline tapes recorded
/// Earlier replay unchanged.
/// </summary>
public static class TapeInput
{
    /// <summary>The line a neutral event is written as (format 2).</summary>
    public const string Line = "in";

    /// <summary>The line an SDL event was written as (format 1), still read.</summary>
    public const string OldLine = "ev";

    /// <summary>Each kind's word on the tape; a kind is never renamed there.</summary>
    static readonly (LoopInputKind Kind, string Word)[] Words =
    [
        (LoopInputKind.KeyDown, "key-down"), (LoopInputKind.KeyUp, "key-up"), (LoopInputKind.Text, "text"),
        (LoopInputKind.PointerMove, "move"), (LoopInputKind.ButtonDown, "button-down"), (LoopInputKind.ButtonUp, "button-up"),
        (LoopInputKind.Wheel, "wheel"), (LoopInputKind.FocusGained, "focus-gained"), (LoopInputKind.FocusLost, "focus-lost"),
        (LoopInputKind.Resized, "resized"), (LoopInputKind.Quit, "quit"), (LoopInputKind.PointerEnter, "pointer-enter"),
        (LoopInputKind.PointerLeave, "pointer-leave"), (LoopInputKind.Moved, "moved"), (LoopInputKind.Minimized, "minimized"),
        (LoopInputKind.Maximized, "maximized"), (LoopInputKind.Restored, "restored"), (LoopInputKind.DisplayChanged, "display-changed"),
        (LoopInputKind.CloseRequested, "close-requested"),
    ];

    /// <summary>The fields of an event's <c>in</c> line: its kind's word, then each field that is not zero (a float by its bits).</summary>
    public static string[] Fields(in LoopInput e)
    {
        var kind = e.Kind;
        var f = new List<string> { Array.Find(Words, w => w.Kind == kind).Word ?? throw new ArgumentException($"no word for {kind}") };
        if (e.Code != 0) f.Add("code=" + e.Code.ToString(CultureInfo.InvariantCulture));
        if (e.Key != 0) f.Add($"key=0x{(uint)e.Key:x}");
        if (e.Mods != 0) f.Add($"mod=0x{(ushort)e.Mods:x}");
        if (e.Repeat) f.Add("rep=1");
        Float(f, "x", e.X);
        Float(f, "y", e.Y);
        Float(f, "dx", e.DX);
        Float(f, "dy", e.DY);
        if (e.Buttons != 0) f.Add("buttons=" + e.Buttons.ToString(CultureInfo.InvariantCulture));
        if (e.Kind == LoopInputKind.Text) f.Add("text=" + ReplayTape.Escape(e.Text));
        if (e.TimeNs != 0) f.Add("t=" + e.TimeNs.ToString(CultureInfo.InvariantCulture));
        return [.. f];
    }

    static void Float(List<string> f, string name, float v)
    {
        if (BitConverter.SingleToInt32Bits(v) != 0) f.Add(name + "=" + ReplayTape.Float(v));
    }

    /// <summary>
    /// The event an input line holds: an <c>in</c> line's, or an old <c>ev</c> line's as the live host would have made
    /// it; null for a line of neither kind, or an <c>ev</c> line of an event nothing past the host reads.
    /// </summary>
    public static LoopInput? Read(TapeLine line) => line.Kind switch
    {
        Line => ReadNew(line),
        OldLine => ReadOld(line),
        _ => null,
    };

    static LoopInput? ReadNew(TapeLine line)
    {
        string word = line.Fields.FirstOrDefault() ?? "";
        int at = Array.FindIndex(Words, w => w.Word == word);
        if (at < 0) throw new InvalidDataException($"an input line of no kind this build knows: \"{line}\"");
        var kind = Words[at].Kind;
        return new LoopInput(kind)
        {
            Code = (int)line.Long("code"), Key = (int)(uint)line.Long("key"), Mods = (LoopMods)(ushort)line.Long("mod"),
            Repeat = line.Long("rep") != 0, X = line.Float("x"), Y = line.Float("y"), DX = line.Float("dx"), DY = line.Float("dy"),
            Buttons = (uint)line.Long("buttons"), Text = kind == LoopInputKind.Text ? ReplayTape.Unescape(line["text"] ?? "%") : "",
            TimeNs = line.Long("t"),
        };
    }

    // ---- format 1: SDL's events as first written (InputTape.Describe then) --------------------------------------

    /// <summary>SDL 3's numbers for the window's events the game reads (<c>SDL_EVENT_WINDOW_*</c>), as format 1 wrote them.</summary>
    static readonly (uint Sdl, LoopInputKind Kind)[] OldWindow =
    [
        (0x205, LoopInputKind.Moved), (0x206, LoopInputKind.Resized), (0x209, LoopInputKind.Minimized),
        (0x20A, LoopInputKind.Maximized), (0x20B, LoopInputKind.Restored), (0x20C, LoopInputKind.PointerEnter),
        (0x20D, LoopInputKind.PointerLeave), (0x20E, LoopInputKind.FocusGained), (0x20F, LoopInputKind.FocusLost),
        (0x210, LoopInputKind.CloseRequested), (0x213, LoopInputKind.DisplayChanged),
    ];

    static LoopInput? ReadOld(TapeLine ev)
    {
        var f = ev.Fields;
        string kind = f.Length > 0 ? f[0] : "";
        bool down = f.Length > 1 && f[1] == "down";
        long t = ev.Long("t");
        switch (kind)
        {
            case "key":
                return new LoopInput(down ? LoopInputKind.KeyDown : LoopInputKind.KeyUp)
                {
                    Code = (int)ev.Long("sc"), Key = (int)(uint)ev.Long("key"), Mods = (LoopMods)(ushort)ev.Long("mod"),
                    Repeat = ev.Long("rep") != 0, TimeNs = t,
                };
            case "button":
                return new LoopInput(down ? LoopInputKind.ButtonDown : LoopInputKind.ButtonUp)
                {
                    Code = (byte)ev.Long("b"), X = ev.Float("x"), Y = ev.Float("y"), TimeNs = t,
                };
            case "motion":
                return new LoopInput(LoopInputKind.PointerMove)
                {
                    X = ev.Float("x"), Y = ev.Float("y"), DX = ev.Float("dx"), DY = ev.Float("dy"), Buttons = (uint)ev.Long("state"), TimeNs = t,
                };
            case "wheel":
                return new LoopInput(LoopInputKind.Wheel) { X = ev.Float("mx"), Y = ev.Float("my"), DX = ev.Float("x"), DY = ev.Float("y"), TimeNs = t };
            case "text":
                return new LoopInput(LoopInputKind.Text) { Text = ReplayTape.Unescape(f.Length > 1 ? f[1] : "%"), TimeNs = t };
            case "quit":
                return new LoopInput(LoopInputKind.Quit) { TimeNs = t };
            case "window":
            {
                uint type = (uint)ev.Long("type");
                int at = Array.FindIndex(OldWindow, w => w.Sdl == type);
                if (at < 0) return null;
                return new LoopInput(OldWindow[at].Kind) { X = (int)ev.Long("d1"), Y = (int)ev.Long("d2"), TimeNs = t };
            }
        }
        // `raw`, an event kept as its bytes: none the game reads (format 1 wrote every kind it reads by name).
        return null;
    }
}
