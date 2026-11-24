// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Audio;

namespace Vitomir.Platform.Tests;

/// <summary>
/// **The sound queued ahead of the speakers lasts until the next frame** (ours; the slow-frame
/// check): a tenth of a second at any ordinary frame rate, as before, and one and a half of the gap
/// between frames when that is longer — so four frames a second do not break the sound up — but never
/// more than a second and a half.
/// </summary>
public class OutputQueueTests
{
    [Fact]
    public void The_queue_is_a_tenth_of_a_second_at_sixty_frames_and_outlasts_a_slow_frame()
    {
        const int Rate = 22_050;                    // 1.1's mixer (the engine's Miles.OutputRate)
        int tenth = Rate / 10;
        Assert.Equal(tenth, OutputQueue.Frames(tenth, 1.0 / 60, Rate));
        Assert.Equal(tenth, OutputQueue.Frames(tenth, 0.05, Rate));
        Assert.Equal((int)Math.Ceiling(0.375 * Rate), OutputQueue.Frames(tenth, 0.25, Rate));   // four frames a second
        Assert.Equal((int)Math.Ceiling(1.5 * Rate), OutputQueue.Frames(tenth, 1.0, Rate));
        Assert.Equal((int)Math.Ceiling(1.5 * Rate), OutputQueue.Frames(tenth, 30.0, Rate));     // a stall: capped
        Assert.Equal(tenth, OutputQueue.Frames(tenth, -1.0, Rate));
    }
}
