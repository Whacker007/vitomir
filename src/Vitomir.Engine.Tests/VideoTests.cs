// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Vfs;
using Vitomir.Engine.Video;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The films. Vitomir ships no Bink decoder and asks the player's own FFmpeg to
/// do it, so **most of what matters here is what happens when there is no FFmpeg**: nothing,
/// quietly, and the game carries on.
/// </summary>
public class VideoTests
{
    static string? Install => GameFiles.Locate();

    // --- finding a film ---------------------------------------------------------------------

    [Fact]
    public void A_film_is_found_in_the_video_folder_whatever_its_case()
    {
        var root = Directory.CreateTempSubdirectory("vitomir-video");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, Films.Folder));
            File.WriteAllText(Path.Combine(root.FullName, Films.Folder, "NYC.BIK"), "not really a film");

            Assert.NotNull(Films.Find(root.FullName, "NYC.BIK"));
            // The install mixes `Sunset.bik` with `NYC.BIK`, and on Linux that difference is real.
            Assert.NotNull(Films.Find(root.FullName, "nyc.bik"));
            // A bare name gets the extension the game's films all have.
            Assert.NotNull(Films.Find(root.FullName, "NYC"));
            Assert.Null(Films.Find(root.FullName, "embrace.bik"));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void A_films_name_cannot_climb_out_of_the_install()
    {
        var root = Directory.CreateTempSubdirectory("vitomir-video");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "secret.bik"), "x");
            var below = Directory.CreateDirectory(Path.Combine(root.FullName, "game"));
            // A script names the film, so a name is not to be trusted with a path.
            Assert.Null(Films.Find(below.FullName, @"..\secret.bik"));
            Assert.Null(Films.Find(below.FullName, "../secret.bik"));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void A_missing_film_is_not_an_error()
    {
        Assert.Null(Films.Find("", "logo.bik"));
        Assert.Null(Films.Find(Path.Combine(Path.GetTempPath(), "no-such-install-" + Guid.NewGuid()), "logo.bik"));
    }

    // --- finding ffmpeg ---------------------------------------------------------------------

    /// <summary>
    /// **The whole arrangement rests on this**: with nothing to find, the engine answers that it
    /// has none — it does not guess, and it does not throw: if the player
    /// does not have it, the engine fails closed and skips the video. A bad
    /// `--ffmpeg` is not the same thing: that falls through to the PATH, so one mistyped flag does
    /// not cost a player the films they could have had.
    ///
    /// The machine running this may well have one where an installer left it (a development machine, from
    /// winget), and an empty PATH does not hide that: so the installers' folders are emptied too, and
    /// <see cref="With_an_empty_PATH_an_ffmpeg_in_an_installers_folder_is_still_found"/> pins that they
    /// are looked in.
    /// </summary>
    [Fact]
    public void A_machine_with_no_ffmpeg_gets_no_ffmpeg()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        string? told = Environment.GetEnvironmentVariable(FFmpegTool.Variable);
        try
        {
            // Nowhere to look and nothing to look for: the answer is none, not a guess and not a throw.
            Environment.SetEnvironmentVariable("PATH", "");
            Environment.SetEnvironmentVariable(FFmpegTool.Variable, null);
            var nowhere = Path.Combine(Path.GetTempPath(), "not-ffmpeg-" + Guid.NewGuid());
            Assert.Null(FFmpegTool.Find(nowhere, installerFolders: []));
            Assert.Null(FFmpegTool.Find(Path.GetTempPath(), installerFolders: []));   // a real folder with no ffmpeg in it
            Assert.Null(FFmpegTool.Find(null, installerFolders: []));
            Assert.Null(FFmpegTool.Find(null, installerFolders: [nowhere, Path.GetTempPath()]));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", path);
            Environment.SetEnvironmentVariable(FFmpegTool.Variable, told);
        }
    }

    /// <summary>
    /// The last place looked: where the Windows installers leave it. A player who installed FFmpeg with
    /// winget and whose PATH has not caught up (a terminal opened before the install) still gets the
    /// films. Skipped on a machine with no FFmpeg, which has nothing to stand in an installer's folder.
    /// </summary>
    [Fact]
    public void With_an_empty_PATH_an_ffmpeg_in_an_installers_folder_is_still_found()
    {
        var real = FFmpegTool.Find();
        Assert.SkipWhen(real is null, "no ffmpeg on this machine");
        string folder = Path.GetDirectoryName(real!.FFmpeg)!;
        string? path = Environment.GetEnvironmentVariable("PATH");
        string? told = Environment.GetEnvironmentVariable(FFmpegTool.Variable);
        try
        {
            Environment.SetEnvironmentVariable("PATH", "");
            Environment.SetEnvironmentVariable(FFmpegTool.Variable, null);
            var found = FFmpegTool.Find(null, installerFolders: [Path.Combine(Path.GetTempPath(), "not-ffmpeg-" + Guid.NewGuid()), folder]);
            Assert.NotNull(found);
            Assert.Equal(real.FFmpeg, found!.FFmpeg);
            Assert.Equal(real.Version, found.Version);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", path);
            Environment.SetEnvironmentVariable(FFmpegTool.Variable, told);
        }
    }

    /// <summary>And with no FFmpeg at all, opening a film answers null rather than throwing.</summary>
    [Fact]
    public void With_no_ffmpeg_nothing_is_played()
    {
        var pretend = new FFmpegTool(Path.Combine(Path.GetTempPath(), "nowhere-" + Guid.NewGuid()), null, "none");
        Assert.Null(VideoClip.Open(pretend, Path.Combine(Path.GetTempPath(), "nothing-" + Guid.NewGuid() + ".bik")));
    }

    // --- actually decoding one --------------------------------------------------------------

    /// <summary>
    /// End to end, when the machine has both an FFmpeg and the game's films: the file describes
    /// itself sensibly, the first frames come out at the size it claimed, and the sound is the
    /// length the picture is. Skipped on a machine without either, which is most of them.
    /// </summary>
    [Fact]
    public void A_bink_film_decodes_to_pictures_and_sound()
    {
        Assert.SkipWhen(Install is null, "no game install");
        var tool = FFmpegTool.Find();
        Assert.SkipWhen(tool is null, "no ffmpeg on this machine");
        var path = Films.Find(Install!, "Sunset.bik");
        Assert.SkipWhen(path is null, "this install has no Video folder");

        using var clip = VideoClip.Open(tool!, path!);
        Assert.NotNull(clip);
        Assert.True(clip!.Info.Sensible, $"{clip.Info}");
        Assert.True(clip.Info.Seconds > 1, "a film of no length");

        // The frame due a second in, and every frame before it, decoded.
        Assert.True(clip.Advance(1000));
        Assert.Equal(clip.Info.Width * clip.Info.Height * 4, clip.Pixels.Length);
        Assert.True(clip.FrameIndex >= (int)clip.Info.Fps - 1, $"only got to frame {clip.FrameIndex}");
        // Not a blank picture: a film that decoded to nothing would be all zeroes.
        bool anything = false;
        foreach (var b in clip.Pixels) if (b != 0) { anything = true; break; }
        Assert.True(anything, "the frame was entirely black");

        if (clip.Sound() is not { } track) return;
        using var pcm = track.Open!();
        long samples = 0;
        var buffer = new short[8192];
        while (pcm.Read(buffer) is var got && got > 0) samples += got;
        double seconds = samples / (double)pcm.Channels / pcm.Rate;
        Assert.True(Math.Abs(seconds - clip.Info.Seconds) < 1.0,
                    $"the sound ran {seconds:0.0} s against a picture of {clip.Info.Seconds:0.0} s");
    }

    /// <summary>Running past the end stops, rather than blocking or throwing.</summary>
    [Fact]
    public void A_film_that_has_run_out_says_so()
    {
        Assert.SkipWhen(Install is null, "no game install");
        var tool = FFmpegTool.Find();
        Assert.SkipWhen(tool is null, "no ffmpeg on this machine");
        var path = Films.Find(Install!, "Sunset.bik");
        Assert.SkipWhen(path is null, "this install has no Video folder");

        using var clip = VideoClip.Open(tool!, path!)!;
        Assert.False(clip.Advance((int)(clip.Info.Seconds * 1000) + 5000));
        Assert.True(clip.Ended);
        Assert.False(clip.Advance(0));      // and stays ended
    }
}
