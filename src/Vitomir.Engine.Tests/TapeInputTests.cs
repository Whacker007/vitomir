// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;
using Vitomir.Platform.Modules;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The tape's input lines (<see cref="TapeInput"/>): a neutral event written as an <c>in</c>
/// line reads back the same, field for field and float for float; an old <c>ev</c> line (format 1, SDL's events as the first format
/// wrote them) reads as the neutral event the live host makes of that SDL event; and a reader takes both formats. The
/// SDL side of the same promise (the host's conversion, against every <c>ev</c> line of the baseline tapes) is
/// <c>Vitomir.Render.Tests.NeutralInputTests</c>, which has SDL.
/// </summary>
public class TapeInputTests
{
    static LoopInput? Read(string line) => TapeInput.Read(ReplayTape.Parse(line)!.Value);

    static LoopInput Back(in LoopInput e) => TapeInput.Read(new TapeLine(TapeInput.Line, TapeInput.Fields(e)))!.Value;

    [Fact]
    public void Every_kind_reads_back_as_it_was_written()
    {
        foreach (var kind in Enum.GetValues<LoopInputKind>())
        {
            var e = new LoopInput(kind)
            {
                Code = 93, Key = LoopKey.F5, Mods = LoopMods.LeftShift | LoopMods.Num, Repeat = true, X = 123.25f, Y = -0.5f,
                DX = 1e-7f, DY = -3f, Buttons = 5, Text = kind == LoopInputKind.Text ? "a b=c%é" : "", TimeNs = 9_876_543_210,
            };
            Assert.Equal(e, Back(e));
        }
        // Nothing but the kind: every field left out reads as nothing.
        Assert.Equal(new LoopInput(LoopInputKind.Quit), Back(new LoopInput(LoopInputKind.Quit)));
        Assert.Equal(["quit"], TapeInput.Fields(new LoopInput(LoopInputKind.Quit)));
        // A negative zero is not zero to the bit, and is kept.
        var negative = Back(new LoopInput(LoopInputKind.Wheel) { DY = -0f });
        Assert.Equal(BitConverter.SingleToInt32Bits(-0f), BitConverter.SingleToInt32Bits(negative.DY));
        // Text that is empty is still text.
        Assert.Equal("", Back(new LoopInput(LoopInputKind.Text)).Text);
    }

    [Fact]
    public void A_line_reads_as_its_words_say()
    {
        var key = Read("in key-down code=4 key=0x61 mod=0x41 rep=1 t=17")!.Value;
        Assert.Equal(new LoopInput(LoopInputKind.KeyDown) { Code = 4, Key = 'a', Mods = LoopMods.LeftShift | LoopMods.LeftCtrl, Repeat = true, TimeNs = 17 }, key);
        Assert.Throws<InvalidDataException>(() => Read("in key-sideways code=4"));
        Assert.Null(Read("none"));
    }

    /// <summary>Each kind of line the first format's tapes hold, as its <c>InputTape.Describe</c> wrote it, and the neutral event it is.</summary>
    [Fact]
    public void An_old_ev_line_reads_as_the_event_the_host_makes_of_it()
    {
        Assert.Equal(new LoopInput(LoopInputKind.KeyDown) { Code = 41, Key = 0x1b, Mods = LoopMods.Num, TimeNs = 12345 },
            Read("ev key down sc=41 key=0x1b mod=0x1000 rep=0 raw=9 which=3 win=2 t=12345"));
        Assert.Equal(new LoopInput(LoopInputKind.KeyUp) { Code = 72, Key = LoopKey.Pause, Repeat = true, TimeNs = 5 },
            Read("ev key up sc=72 key=0x40000048 mod=0x0 rep=1 raw=0 which=0 win=2 t=5"));
        Assert.Equal(new LoopInput(LoopInputKind.ButtonDown) { Code = 3, X = 320.5f, Y = 240f, TimeNs = 7 },
            Read("ev button down b=3 x=320.5 y=240 clicks=2 which=1 win=2 t=7"));
        Assert.Equal(new LoopInput(LoopInputKind.ButtonUp) { Code = 1, X = 1f, Y = 2f },
            Read("ev button up b=1 x=1 y=2 clicks=1 which=0 win=0 t=0"));
        Assert.Equal(new LoopInput(LoopInputKind.PointerMove) { X = 10f, Y = 20f, DX = -1.5f, DY = 2f, Buttons = 4, TimeNs = 9 },
            Read("ev motion x=10 y=20 dx=-1.5 dy=2 state=4 which=1 win=2 t=9"));
        Assert.Equal(new LoopInput(LoopInputKind.Wheel) { X = 100f, Y = 200f, DX = 0f, DY = -1f, TimeNs = 3 },
            Read("ev wheel x=0 y=-1 dir=0 mx=100 my=200 ix=0 iy=-1 which=1 win=2 t=3"));
        Assert.Equal(new LoopInput(LoopInputKind.Text) { Text = "hello world", TimeNs = 4 }, Read("ev text hello%20world win=2 t=4"));
        Assert.Equal(new LoopInput(LoopInputKind.Text) { Text = "", TimeNs = 4 }, Read("ev text % win=2 t=4"));
        Assert.Equal(new LoopInput(LoopInputKind.Quit) { TimeNs = 8 }, Read("ev quit t=8"));
        Assert.Equal(new LoopInput(LoopInputKind.FocusGained) { TimeNs = 1 }, Read("ev window type=0x20e d1=0 d2=0 win=2 t=1"));
        Assert.Equal(new LoopInput(LoopInputKind.Resized) { X = 1280f, Y = 720f, TimeNs = 1 }, Read("ev window type=0x206 d1=1280 d2=720 win=2 t=1"));
        Assert.Equal(new LoopInput(LoopInputKind.PointerEnter), Read("ev window type=0x20c d1=0 d2=0 win=2 t=0"));
        // What nothing past the host reads is no event: a window shown, its pixels' size, a device added (its bytes).
        Assert.Null(Read("ev window type=0x202 d1=0 d2=0 win=2 t=1"));
        Assert.Null(Read("ev window type=0x207 d1=640 d2=480 win=2 t=1"));
        Assert.Null(Read("ev raw 001100000000000012345678"));
    }

    [Fact]
    public void A_reader_takes_both_formats_and_says_which()
    {
        Assert.Equal(2, new TapeReader(new StringReader(ReplayTape.Magic + "\nframe 1\nnone\n")).Format);
        Assert.Equal(1, new TapeReader(new StringReader(ReplayTape.FirstMagic + "\nframe 1\nnone\n")).Format);
        Assert.Throws<InvalidDataException>(() => new TapeReader(new StringReader("vitomir-tape 3\n")));
    }
}
