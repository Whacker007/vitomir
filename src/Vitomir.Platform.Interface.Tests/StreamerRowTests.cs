// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Interface.Tests;

/// <summary>
/// **Streamer mode on the platform's own game's options** (one platform setting, shown in each game's options): the
/// Controls page's Platform row shows it as it stands, Cancel leaves it, OK puts the toggle in force. Process-wide: run alone.
/// </summary>
[Collection(nameof(StreamerRowCollection))]
public sealed class StreamerRowTests : IDisposable
{
    readonly bool was = StreamerMode.On;
    readonly string? kept = StreamerMode.Kept;

    public void Dispose()
    {
        StreamerMode.On = was;
        StreamerMode.Kept = kept;
    }

    [Fact]
    public void The_controls_page_shows_streamer_mode_and_ok_puts_it_in_force_and_cancel_does_not()
    {
        StreamerMode.Kept = null;                    // kept nowhere: nothing written
        foreach (bool start in new[] { false, true })
        {
            StreamerMode.On = start;
            var page = Fixtures.LaidPage(2560, 1440);
            var s = page.Screen;
            Assert.Equal(start ? "On" : "Off", s.Data.Text("streamer"));
            void Toggle()
            {
                s.SetFocus(s.Find("streamer")!);
                s.Key(UiKey.Right);
                foreach (var a in s.TakeActions()) page.Handle(a);
            }
            Toggle();
            Assert.Equal(start ? "Off" : "On", s.Data.Text("streamer"));
            page.Handle(new UiAction("cancel", "", s.Data, null));
            Assert.Equal(start, StreamerMode.On);

            page = Fixtures.LaidPage(2560, 1440);
            s = page.Screen;
            Toggle();
            page.Handle(new UiAction("ok", "", s.Data, null));
            Assert.Equal(PageOutcome.Accepted, page.Outcome);
            Assert.Equal(!start, StreamerMode.On);
        }
    }
}

/// <summary>The streamer mode tests, which set a process-wide setting, alone.</summary>
[CollectionDefinition(nameof(StreamerRowCollection), DisableParallelization = true)]
public sealed class StreamerRowCollection;
