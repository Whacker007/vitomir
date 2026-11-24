// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Audio;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Video;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **"All coterie members are dead!" comes down as often as 1.1 takes it down**:
/// the world covered or uncovered — state 3 to 4 and back,
/// which is what the shop, the advancement screen, the map, the Options from the escape
/// menu and two side panes together do — a film starting, and the console's
/// revive; and not the escape menu, which is no change of state, so the words stay behind it.
/// </summary>
public class EndTextHoldTests
{
    static Caption[] Words() =>
    [
        new("All coterie members are dead!", Captions.EndFont, 20, 430, 0, Captions.EndMs, Captions.EndFadeInMs, 0, Lines: 2),
        new("Press Esc to load saved game.", Captions.EndFont, 20, 430, 0, Captions.EndMs, Captions.EndFadeInMs, 0),
    ];

    [Fact]
    public void The_world_covered_takes_the_end_text_down_and_a_drawn_world_keeps_it()
    {
        var captions = new Captions();
        var hold = new EndTextHold(captions);
        hold.World(true);
        hold.Put(Words());
        for (int frame = 0; frame < 10; frame++) hold.World(true);        // the escape menu up: still state 3
        Assert.True(hold.Up);
        Assert.Equal(2, captions.Up.Count);

        hold.World(false);                                               // the Options from it: state 4
        Assert.False(hold.Up);
        Assert.Empty(captions.Up);
        hold.World(true);                                                // and back: gone for good
        Assert.False(hold.Up);
    }

    /// <summary>
    /// Put up while the world is covered — the shop or the map open as the last of the party falls —
    /// the words go when it is uncovered (the uncover lets them go on its way back to state 3).
    /// </summary>
    [Fact]
    public void The_world_uncovered_takes_it_down_too()
    {
        var captions = new Captions();
        var hold = new EndTextHold(captions);
        hold.World(false);
        hold.Put(Words());
        hold.World(false);
        Assert.True(hold.Up);
        hold.World(true);
        Assert.False(hold.Up);
    }

    /// <summary>Letting it go takes only its own panes; a floating text put up after it stays (only its own pane is taken down).</summary>
    [Fact]
    public void Letting_it_go_leaves_other_floating_text_up()
    {
        var captions = new Captions();
        var hold = new EndTextHold(captions);
        captions.Add(new Caption("an earlier title", Captions.TitleFont, 0, 400, 0, 8000, 2000, 2000));
        hold.Put(Words());                                               // every floating text first
        Assert.Equal(2, captions.Up.Count);
        var later = new Caption("Monastery 1", Captions.TitleFont, 0, 400, 0, 8000, 2000, 2000);
        captions.Add(later);
        hold.LetGo();
        Assert.Same(later, Assert.Single(captions.Up));
        hold.LetGo();                                                    // nothing up: nothing happens
        Assert.Same(later, Assert.Single(captions.Up));
    }

    sealed class StillFilm : IFilm
    {
        public FilmInfo Info => new(640, 346, 25, 2.0, false);
        public bool Advance(int ms) => ms < 2000;
        public Sound? Sound() => null;
        public void Dispose() { }
    }

    /// <summary>A film taking the screen lets the end text go; one that will not open does not.</summary>
    [Fact]
    public void A_film_starting_takes_the_end_text_down()
    {
        var captions = new Captions();
        var hold = new EndTextHold(captions);
        var film = new StoryFilm(name => name == "embrace.bik" ? new StillFilm() : null) { Opened = hold.LetGo };
        hold.Put(Words());
        Assert.False(film.Start("nothing.bik"));
        Assert.True(hold.Up);
        Assert.True(film.Start("embrace.bik"));
        Assert.False(hold.Up);
        Assert.Empty(captions.Up);
    }
}
