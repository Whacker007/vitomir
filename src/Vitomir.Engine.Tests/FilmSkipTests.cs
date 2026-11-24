// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Video;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The two film skips (ours, quality-of-life switches, off by default): `skipopening` leaves the logo and the trailer out
/// before the title screen, and `skipstoryfilms` ends each of the story's films as it is asked for — in single player only, a
/// network game's films being as they always were. Neither makes a game modded, and each is the player's own machine's.
/// </summary>
public class FilmSkipTests
{
    [Fact]
    public void Both_are_off_until_the_player_turns_them_on_and_play_every_film_then()
    {
        var qol = new Qol();
        Assert.False(Films.OpeningSkipped(qol));
        Assert.False(Films.StorySkipped(qol, networked: false));
        Assert.False(Films.StorySkipped(qol, networked: true));
    }

    [Fact]
    public void On_the_opening_films_are_left_out_and_a_story_film_too_but_never_in_a_network_game()
    {
        var qol = new Qol { SkipOpening = true, SkipStoryFilms = true };
        Assert.True(Films.OpeningSkipped(qol));
        Assert.True(Films.StorySkipped(qol, networked: false));
        Assert.False(Films.StorySkipped(qol, networked: true));
        // Each alone: the one does not take the other with it.
        Assert.False(Films.StorySkipped(new Qol { SkipOpening = true }, networked: false));
        Assert.False(Films.OpeningSkipped(new Qol { SkipStoryFilms = true }));
    }

    [Fact]
    public void They_are_read_from_the_settings_file_s_qol_section_and_leave_a_game_faithful()
    {
        string user = Path.Combine(Path.GetTempPath(), $"vitomir-filmskip-{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllText(user, "[qol]\nskipopening = 1\nskipstoryfilms = on\n");
            var complaints = new List<string>();
            var qol = Qol.Parse([], Path.GetTempPath(), complaints.Add, user);
            Assert.Empty(complaints);
            Assert.True(qol.SkipOpening);
            Assert.True(qol.SkipStoryFilms);
            Assert.Equal("faithful", Features.GameFeatures.Registry.Profile(new Fixes(), qol));
            foreach (var name in new[] { "skipopening", "skipstoryfilms" })
                Assert.Equal(Features.FeatureWhose.Machine, Features.GameFeatures.Registry.Find("qol", name)!.Whose);
        }
        finally { File.Delete(user); }
    }
}
