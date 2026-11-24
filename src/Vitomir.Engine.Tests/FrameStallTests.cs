// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>A front-end turn that came two seconds or more after the last is said in the log, with what was up (ours).</summary>
public class FrameStallTests
{
    [Fact]
    public void A_gap_under_two_seconds_says_nothing_and_one_of_two_or_more_names_what_was_up()
    {
        bool asked = false;
        Assert.Null(FrameStall.Line(1_999_000_000UL, () => { asked = true; return "up: x"; }));
        Assert.False(asked);                                         // what was up is only worked out for a stall
        Assert.Equal("front end: a frame took 2.0 s (up: mpPlayerSelect.nui)", FrameStall.Line(2_000_000_000UL, () => "up: mpPlayerSelect.nui"));
        Assert.Equal("front end: a frame took 26.3 s", FrameStall.Line(26_300_000_000UL, () => ""));
    }
}
