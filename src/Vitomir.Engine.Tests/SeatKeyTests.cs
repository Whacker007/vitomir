// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Net;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The host's seat, from the keyboard and to the scripts** (a review of the docs found them): the two keys
/// of the key handler's host branch — **\** takes and leaves the Storyteller's seat,
/// **.** in the seat gives the host their own character back — and the scene index the scene pane greys
/// by, which is the host's and the scripts'. Before this
/// neither key did anything, and the pane kept an index of its own that a script never moved and a place change
/// put back to −1.
/// </summary>
public class SeatKeyTests
{
    [Fact]
    public void The_keys_are_the_backslash_and_the_period_and_only_a_host_reads_them()
    {
        Assert.Equal(SeatKey.Seat, SeatKeys.Of(backslash: true, period: false));
        Assert.Equal(SeatKey.Home, SeatKeys.Of(backslash: false, period: true));
        Assert.Equal(SeatKey.None, SeatKeys.Of(backslash: false, period: false));
        Assert.Equal(0, SeatKeys.Sound);
        Assert.True(SeatKeys.Read(host: true, hookReads: true));
        Assert.False(SeatKeys.Read(host: false, hookReads: true));                    // a host only
        Assert.False(SeatKeys.Read(host: true, hookReads: false));                    // the hook's gates (the key handler's start)
    }

    /// <summary>
    /// **.** in the seat: the body the Storyteller possessed is let go, the host's own character is steered again
    /// and made the target, and the ghost, in the world, is taken out of it. Out of the seat
    /// the key does nothing.
    /// </summary>
    [Fact]
    public void The_period_in_the_seat_gives_the_host_their_own_character_back()
    {
        using var files = Install.Open();
        var chronicle = ChronicleSet.Load(files)["MP_CurseDarkness"]!;
        var settings = GameSettings.For(chronicle, "Test", GameFlags.Moderated);
        var place = Location.Load(files, settings.Level, scene: settings.Scene, player: "MP_Serena", entrance: settings.Entrance);
        place.Awaken(new Ai.AiClasses(files), 1, new Rpg.EffectDefinitions(files), new Rpg.DisciplineDefinitions(files));
        using var server = new GameServer(settings, new NetPeer(0), new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        server.Enter(place, place.Player?.Thing);
        var own = server.Slots[0]!.Avatar!;
        var st = new StoryPowers(server, place, new Strings(files));

        st.SetGhost(own);                                                            // the ghost in the world, steered
        var ghost = st.Ghost!;
        Assert.True(ghost.InWorld);
        st.Create("Rat");
        var rat = st.Target!;
        st.Possess(rat);
        Assert.Same(rat, server.Possessed);
        Assert.Same(rat, place.Steered);

        Assert.False(SeatKeys.Home(st, inSeat: false, camera: null));               // out of the seat: nothing
        Assert.Same(rat, server.Possessed);

        Assert.True(SeatKeys.Home(st, inSeat: true, camera: null));
        Assert.Null(server.Possessed);
        Assert.Same(own, place.Steered);                                             // the control
        Assert.Same(own, st.Target);                                                 // the target
        Assert.False(ghost.InWorld);
        Assert.True(rat.InWorld);
    }

    /// <summary>
    /// The index is read once a chronicle (−1 and the count), moved by the scripts within the steps
    /// and by the pane's Advance as far as the slot past the list, and greys every row at or
    /// before it. The pane is made again on each of the host's place changes; the same chronicle's list
    /// read again keeps where the story is.
    /// </summary>
    [Fact]
    public void The_scene_index_is_the_hosts_moved_by_scripts_and_buttons_and_greys_the_steps_reached()
    {
        var index = new SceneIndex();
        index.Read("MP_CurseDarkness", 3);
        Assert.Equal(SceneIndex.None, index.Reached);
        Assert.False(index.Greyed(0));

        index.Set(1);
        Assert.Equal([true, true, false], Enumerable.Range(0, 3).Select(index.Greyed));
        index.Set(3);                                                                 // out of the list: nothing
        index.Set(-2);
        Assert.Equal(1, index.Reached);

        index.Read("MP_CurseDarkness", 3);                                            // the pane made again
        Assert.Equal(1, index.Reached);

        int at = index.Reached;                                                       // Advance, twice
        Assert.NotNull(StoryPowers.Advance(ref at, index.Count, i => new StoryPowers.Step(i, 0, "")));
        Assert.NotNull(StoryPowers.Advance(ref at, index.Count, i => new StoryPowers.Step(i, 0, "")));
        index.Reached = at;
        Assert.Equal(3, index.Reached);                                               // past the list
        Assert.True(index.Greyed(2));

        index.Read("MP_LegacyOfTyranny", 8);                                          // another chronicle's list
        Assert.Equal((8, SceneIndex.None), (index.Count, index.Reached));
    }
}
