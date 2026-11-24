// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Host.Tests;

/// <summary>The runs that tests and tools make keep their errors in the log and the exit code. A box on the
/// screen waits for a click, so a test that feeds the program a bad tape on purpose would sit until someone clicks OK
/// (four were once seen: an edit had turned the replay check and dump into a trailing comment).</summary>
public sealed class ErrorBoxTests
{
    [Theory]
    [InlineData("--replay-dump", "a.tape")]
    [InlineData("--replay-check", "a.tape")]
    [InlineData("--headless", "x.png")]
    [InlineData("--headless-screen", "pause")]
    [InlineData("--snagcheck", "a.json")]
    [InlineData("--record-drive", "a.tape")]
    [InlineData("--arena", "a.json")]
    [InlineData("--aim-survey", "a.csv")]
    [InlineData("--arc-survey", "a.csv")]
    [InlineData("--camera-survey", "a.csv")]
    [InlineData("--scenario", "a.json")]
    [InlineData("--trace-event", "a.jsonl")]
    public void A_tool_or_test_run_shows_no_box(string option, string value) =>
        Assert.True(Entry.ErrorsStayInTheLog(new Cli([option, value])));

    [Fact]
    public void A_module_s_headless_loop_and_describe_show_no_box()
    {
        Assert.True(Entry.ErrorsStayInTheLog(new Cli(["--game", "classic", "--headless", "--level", "P1_HAVN"])));
        Assert.True(Entry.ErrorsStayInTheLog(new Cli(["--describe"])));
    }

    [Fact]
    public void Play_shows_its_errors_in_a_box()
    {
        Assert.False(Entry.ErrorsStayInTheLog(new Cli([])));
        Assert.False(Entry.ErrorsStayInTheLog(new Cli(["--level", "east-gate", "--sandbox"])));
    }
}
