// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Tests;

/// <summary>
/// **Streamer mode, the platform's setting** (<c>Presentation/StreamerMode.cs</c>): what it hides
/// (every address with its port, IPv4, IPv6 and an IPv4 in IPv6; the user's folder in a path, either slash, any case; a secret a
/// game said, a host's name as typed among them) and what it leaves (a port by itself, a clock, a version, a position); off, every
/// text as it is; the setting's file, off by default, and <c>--streamer</c> for one run. The setting is process-wide, so the
/// tests that turn it run alone (<see cref="StreamerModeCollection"/>).
/// </summary>
[Collection(nameof(StreamerModeCollection))]
public sealed class StreamerModeTests : IDisposable
{
    readonly bool was = StreamerMode.On;
    readonly string? profile = StreamerMode.Profile;
    readonly string? kept = StreamerMode.Kept;

    public void Dispose()
    {
        StreamerMode.On = was;
        StreamerMode.Profile = profile;
        StreamerMode.Kept = kept;
        StreamerMode.Forget();
    }

    const string H = StreamerMode.Hidden;

    [Theory]
    [InlineData("players join at 203.0.113.7:4994", "players join at " + H)]
    [InlineData("from 192.168.1.20, 40 ms", "from " + H + ", 40 ms")]
    [InlineData("the host at [2001:db8::5]:4994 answers", "the host at " + H + " answers")]
    [InlineData("from [::1]", "from " + H)]
    [InlineData("from ::ffff:10.0.0.2:5000.", "from " + H + ".")]
    [InlineData("fe80::1c2b:3a4d:5e6f:7a8b%12 bound", H + "%12 bound")]
    [InlineData("2001:0db8:85a3:0000:0000:8a2e:0370:7334 is long", H + " is long")]
    [InlineData("UPnP: outside address 198.51.100.2, port 4994 mapped", "UPnP: outside address " + H + ", port 4994 mapped")]
    // What is no address stays.
    [InlineData("hosting on UDP 4994", "hosting on UDP 4994")]
    [InlineData("at 12:30:45 the clock", "at 12:30:45 the clock")]
    [InlineData("Vitomir Platform 1.0, platform API 1.5.0", "Vitomir Platform 1.0, platform API 1.5.0")]
    [InlineData("thing 1073741829 at 1204,-388,96, health 40", "thing 1073741829 at 1204,-388,96, health 40")]
    [InlineData("version 1.2.3.4.5 of a thing", "version 1.2.3.4.5 of a thing")]
    [InlineData("Classic::Run and std::string", "Classic::Run and std::string")]
    public void Every_address_and_its_port_is_hidden_and_nothing_else(string text, string shown) =>
        Assert.Equal(shown, StreamerMode.Mask(text, null));

    [Fact]
    public void The_user_s_folder_in_a_path_is_shown_as_userprofile_with_either_slash_in_any_case()
    {
        const string profile = @"C:\Users\Anna";
        Assert.Equal(@"log: %USERPROFILE%\AppData\Local\Vitomir\Logs\vitomir.log",
                     StreamerMode.Mask(@"log: C:\Users\Anna\AppData\Local\Vitomir\Logs\vitomir.log", profile));
        Assert.Equal("models: %USERPROFILE%/Vitomir-models", StreamerMode.Mask("models: c:/users/anna/Vitomir-models", profile));
        Assert.Equal(@"the game in D:\Games\VTMR", StreamerMode.Mask(@"the game in D:\Games\VTMR", profile));
        Assert.Equal("/home/anna is none of it", StreamerMode.Mask("/home/anna is none of it", profile));
        Assert.Equal("%USERPROFILE%/x", StreamerMode.Mask("/home/anna/x", "/home/anna/"));
    }

    [Fact]
    public void A_secret_a_game_says_is_hidden_with_or_without_its_port_and_a_short_one_or_a_port_is_not_taken()
    {
        StreamerMode.Hide("friend.example.org:4994");
        StreamerMode.Hide("ab");
        StreamerMode.Hide("4994");
        StreamerMode.Hide(null);
        Assert.Equal(["friend.example.org", "friend.example.org:4994"], StreamerMode.Secrets.Order(StringComparer.Ordinal));
        Assert.Equal($"joining {H} as Anna; {H} answers", StreamerMode.Mask("joining Friend.Example.org:4994 as Anna; friend.example.org answers", null, StreamerMode.Secrets));
        Assert.Equal("a 4994 ab", StreamerMode.Mask("a 4994 ab", null, StreamerMode.Secrets));
    }

    [Fact]
    public void Off_every_text_is_as_it_is_and_on_it_is_hidden_and_a_join_code_and_an_address_box_follow()
    {
        StreamerMode.Profile = @"C:\Users\Anna";
        const string line = @"net: Anna from 10.1.2.3:4994; log C:\Users\Anna\x.log";
        StreamerMode.On = false;
        Assert.Equal(line, StreamerMode.Shown(line));
        Assert.Equal("K7Q-29X", StreamerMode.Code("K7Q-29X"));
        Assert.Equal("10.1.2.3", StreamerMode.Dots("10.1.2.3"));
        int before = StreamerMode.Version;
        StreamerMode.On = true;
        Assert.NotEqual(before, StreamerMode.Version);
        Assert.Equal($"net: Anna from {H}; log %USERPROFILE%\\x.log", StreamerMode.Shown(line));
        Assert.Equal(H, StreamerMode.Code("K7Q-29X"));
        Assert.Equal("........", StreamerMode.Dots("10.1.2.3"));
        Assert.Equal("", StreamerMode.Shown(""));
        Assert.Null(StreamerMode.ShownOrNull(null));
    }

    [Fact]
    public void The_setting_is_off_by_default_kept_in_its_file_and_streamer_turns_it_on_for_one_run()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"vitomir-streamer-{Guid.NewGuid():N}");
        try
        {
            // No file: off. --streamer: on for this run, nothing written.
            Assert.Equal("streamer: off", StreamerMode.Start(folder, []));
            Assert.False(StreamerMode.On);
            Assert.Equal(StreamerMode.PathIn(folder), StreamerMode.Kept);
            Assert.Equal("streamer: on (--streamer, this run)", StreamerMode.Start(folder, ["--game", "classic", StreamerMode.Switch]));
            Assert.True(StreamerMode.On);
            Assert.False(File.Exists(StreamerMode.PathIn(folder)));

            // Set from a game's options: in force and kept; the next start reads it.
            StreamerMode.Set(true);
            Assert.True(StreamerMode.Read(StreamerMode.PathIn(folder)));
            Assert.Equal("streamer: on (kept)", StreamerMode.Start(folder, []));
            StreamerMode.Set(false);
            Assert.Equal("streamer: off", StreamerMode.Start(folder, []));

            // A file that does not read is off, and said.
            File.WriteAllText(StreamerMode.PathIn(folder), "{ not json");
            var said = new List<string>();
            Assert.False(StreamerMode.Read(StreamerMode.PathIn(folder), said.Add));
            Assert.Single(said);
            Assert.StartsWith("streamer: ", said[0]);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}

/// <summary>The streamer mode tests, which set a process-wide setting, alone.</summary>
[CollectionDefinition(nameof(StreamerModeCollection), DisableParallelization = true)]
public sealed class StreamerModeCollection;
