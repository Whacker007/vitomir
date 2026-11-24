// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Net;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The Storyteller's bar, as 1.1 has it** — the differences a comparison with 1.1 found in `StoryPowers`,
/// and the seat's two leftovers:
/// **Revive** heals any character, living or dead, and refuses only what is no character;
/// **Delete** sets the ghost where the target stood and never deletes the control;
/// the actor pane's pair ask less than the bar's; **Advance** from the last step runs the
/// empty slot past the list; the options word's bit 0x80 (`-devmode`) takes the ground move out of the
/// seat onto the steered character; and the seat's stealth fade reaches the renderer.
/// </summary>
public class StoryBarTests
{
    static (GameServer Server, Location Place, StoryPowers Powers, Strings Words) Seat(GameFiles files)
    {
        var inn = ChronicleSet.Load(files)["MP_CurseDarkness"]!;
        var settings = GameSettings.For(inn, "Test", GameFlags.Moderated);
        var place = Location.Load(files, settings.Level, scene: settings.Scene, player: "MP_Serena", entrance: settings.Entrance);
        place.Awaken(new Ai.AiClasses(files), 1, new Rpg.EffectDefinitions(files), new Rpg.DisciplineDefinitions(files));
        var server = new GameServer(settings, new NetPeer(0), new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        server.Enter(place, place.Player?.Thing);
        var words = new Strings(files);
        return (server, place, new StoryPowers(server, place, words), words);
    }

    static WorldThing Someone(Location place, WorldThing? not = null) =>
        place.Things.First(t => t.Type == ThingType.Actor && t.Actor is { Dead: false } && t.Mind is not null && t.InWorld
                                && !ReferenceEquals(t, not) && !t.Template.Equals(StoryPowers.GhostTemplate, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void The_bars_Revive_heals_a_living_character_and_refuses_only_what_is_no_character()
    {
        using var files = Install.Open();
        var (server, place, st, words) = Seat(files);
        using var _ = server;
        var knight = Someone(place);
        var a = knight.Actor!;
        a.Health = a.MaxHealth / 4f;
        Assert.Equal("", st.Revive(knight));                                         // the revive on the living
        Assert.Equal(a.MaxHealth, a.Health);
        Assert.False(a.Dead);
        Assert.Equal(words.Find("UI_STSBREVIVENOSELECTION"), st.Revive(null));
        var prop = place.Things.First(t => t.Type == ThingType.Prop);
        Assert.Equal(words.Find("UI_STSBREVIVENOTDEAD"), st.Revive(prop));          // the line's one use

        // The actor pane's: no line at all, the same revive.
        a.Health = 1f;
        Assert.True(st.ReviveChosen(knight));
        Assert.Equal(a.MaxHealth, a.Health);
        Assert.False(st.ReviveChosen(null));
        Assert.False(st.ReviveChosen(prop));
    }

    [Fact]
    public void Delete_on_the_target_leaves_the_ghost_standing_where_it_was_and_steered()
    {
        using var files = Install.Open();
        var (server, place, st, _) = Seat(files);
        using var __ = server;
        var knight = Someone(place);
        var at = knight.Position;
        st.Target = knight;
        Assert.Equal("", st.Delete(knight));
        Assert.False(knight.InWorld);
        var ghost = st.Ghost!;
        Assert.True(ghost.InWorld);
        Assert.True(Vector3.Distance(at, ghost.Position) < 1f);                     // Where it stood
        Assert.Same(ghost, place.Steered);                                           // and steered
        Assert.Same(ghost, st.Target);
    }

    [Fact]
    public void Delete_never_deletes_the_control_the_ghost_or_a_possessed_body()
    {
        using var files = Install.Open();
        var (server, place, st, _) = Seat(files);
        using var __ = server;
        var knight = Someone(place);
        st.SetGhost(knight);
        var ghost = st.Ghost!;
        // The ghost is the target: set at itself it is the control, and stays.
        Assert.Same(ghost, st.Target);
        Assert.Equal("", st.Delete(ghost));
        Assert.True(ghost.InWorld);
        Assert.False(ghost.Removed);

        // A possessed body the pane names while the ghost is the target: it is the control, and stays.
        var other = Someone(place, knight);
        st.Possess(other);
        Assert.Same(other, place.Steered);
        Assert.Null(st.DeleteChosen(other));
        Assert.True(other.InWorld);

        // The same body as the bar's target: the ghost takes the control from it first, so it goes.
        st.Target = other;
        Assert.Equal("", st.Delete(other));
        Assert.False(other.InWorld);
        Assert.Same(ghost, place.Steered);
        Assert.Null(server.Possessed);
    }

    [Fact]
    public void The_actor_panes_Delete_asks_no_protected_flag_and_says_nothing_for_nothing()
    {
        using var files = Install.Open();
        var (server, place, st, words) = Seat(files);
        using var _ = server;
        var knight = Someone(place);
        knight.Flags |= StoryPowers.DeleteProtected;
        Assert.Equal(words.Find("UI_STSBDELETEPROTECTED"), st.Delete(knight));       // the bar's
        Assert.True(knight.InWorld);
        Assert.Null(st.DeleteChosen(null));
        Assert.Null(st.DeleteChosen(knight));                                         // the pane's
        Assert.False(knight.InWorld);
        Assert.Equal(words.Find("UI_STSBDELTHINGERROR"), st.DeleteChosen(place.Player!.Thing));
    }

    [Fact]
    public void Advance_from_the_last_step_runs_the_empty_slot_past_the_list_and_then_nothing()
    {
        var steps = new[] { new StoryPowers.Step(2, 1, "A"), new StoryPowers.Step(3, 1, "B") };
        int reached = -1;
        Assert.Equal(steps[0], StoryPowers.Advance(ref reached, steps.Length, i => steps[i]));
        Assert.Equal(steps[1], StoryPowers.Advance(ref reached, steps.Length, i => steps[i]));
        Assert.Equal(1, reached);
        // The signed test lets the last step go on to `count`: the zeroed slot, advance(0, 0).
        Assert.Equal(new StoryPowers.Step(0, 0, ""), StoryPowers.Advance(ref reached, steps.Length, i => steps[i]));
        Assert.Equal(2, reached);
        Assert.Null(StoryPowers.Advance(ref reached, steps.Length, i => steps[i]));
        Assert.Equal(2, reached);
        // A chronicle with no steps runs the empty slot once.
        int none = -1;
        Assert.Equal(StoryPowers.PastTheList, StoryPowers.Advance(ref none, 0, _ => throw new InvalidOperationException()));
        Assert.Null(StoryPowers.Advance(ref none, 0, _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void Devmode_takes_the_ground_move_out_of_the_seat_onto_the_steered_character()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var christof = place.Steered!;
        var from = christof.Position;
        var spot = StoryCarryTests.FloorNear(place, from, 150f);
        const uint dev = GameOptions.DevMode;
        // Without the bit, on a player's machine, and on a host that is not moderated: not taken.
        Assert.Null(StoryPowers.DevMoved(0, false, null, place, christof, spot, InputModifier.Alt));
        Assert.Null(StoryPowers.DevMoved(dev, true, null, place, christof, spot, InputModifier.Alt));
        Assert.Null(StoryPowers.DevMoved(dev, false, GameFlags.AutoStoryteller, place, christof, spot, InputModifier.Alt));
        Assert.Equal(from, christof.Position);
        // With Space on a moderated host: only turned. Single player: put there.
        Assert.StartsWith(christof.Template + " turns", StoryPowers.DevMoved(dev, false, GameFlags.Moderated, place, christof, spot, InputModifier.Space));
        Assert.Equal(from, christof.Position);
        Assert.Equal("no ground under the pointer", StoryPowers.DevMoved(dev, false, null, place, christof, null, InputModifier.Alt));
        Assert.StartsWith(christof.Template + " put at", StoryPowers.DevMoved(dev, false, null, place, christof, spot, InputModifier.Alt));
        Assert.True(Vector3.Distance(spot, christof.Position) < 1f);
        // The effect icon's cancel comes first.
        Assert.Equal("an effect cancelled", StoryPowers.DevMoved(dev, false, null, place, christof, from, InputModifier.Alt, () => true));
    }

    [Fact]
    public void In_the_seat_a_hidden_character_is_drawn_whole()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var christof = place.Steered!;
        christof.Actor!.Team = 1;
        christof.Actor.Perception = 0;
        var foe = place.Things.First(t => t.Type == ThingType.Actor && t.Actor is not null && !ReferenceEquals(t, christof));
        foe.Actor!.Team = 2;
        foe.Actor.Stealth = 6;
        bool seated = false;
        place.Session = new Session { Seated = () => seated };
        Assert.True(StealthFade.Unseen(foe, place));                                  // out of the seat: not drawn
        Assert.Equal(0f, StealthFade.Fade(foe, place));
        seated = true;
        Assert.False(StealthFade.Unseen(foe, place));
        Assert.Equal(1f, StealthFade.Fade(foe, place));
        // The steered one hidden still sees himself half there, seat or not (the fade asks that first).
        christof.Actor.Stealth = 6;
        Assert.Equal(StealthFade.Half, StealthFade.Fade(christof, place));
    }
}
