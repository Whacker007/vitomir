// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Formats;
using Xunit;

namespace Vitomir.Formats.Tests;

/// <summary>Reading one key of INI text the way the engine's reader takes it (ours; the writer's cases are the engine's settings tests).</summary>
public class IniTextTests
{
    [Fact]
    public void A_value_is_its_last_occurrence_in_its_own_section_matched_case_insensitively_comments_skipped()
    {
        const string text = "[qol]\r\n; skipopening = on\r\nSkipOpening = 0\r\n[fixes]\r\nskipopening = on\r\n[QOL]\r\nskipopening = yes\r\n";
        Assert.Equal("yes", IniText.Value(text, "qol", "skipopening"));
        Assert.Null(IniText.Value(text, "qol", "skipstoryfilms"));
        Assert.Null(IniText.Value("", "qol", "skipopening"));
        Assert.True(IniText.On("yes"));
        Assert.True(IniText.On(" 1 "));
        Assert.False(IniText.On("0"));
        Assert.False(IniText.On(null));
    }
}
