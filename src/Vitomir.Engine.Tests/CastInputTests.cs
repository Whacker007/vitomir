// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The player's hand on the disciplines: the right button's refusals, and the
/// keys. A character whose mind has the controls takes no orders from the
/// mouse while it is the one being steered.
/// </summary>
public class CastInputTests
{
    static (Location Place, WorldThing Steered, Minds Minds, DisciplineDefinitions Powers) Open(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var powers = new DisciplineDefinitions(files);
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), powers);
        return (place, place.Steered!, minds, powers);
    }

    [Theory]
    [InlineData(AiMode.Frenzied)]
    [InlineData(AiMode.Mesmerized)]
    [InlineData(AiMode.Dazed)]
    [InlineData(AiMode.Afraid)]
    public void A_steered_character_whose_mind_has_the_controls_refuses_the_mouse(AiMode mode)
    {
        using var files = Install.Open();
        var (place, steered, _, powers) = Open(files);
        var celerity = steered.Actor!.Disciplines.Add(powers["Celerity"]!, 0);
        steered.Actor.Disciplines.Current = celerity;
        steered.Mind!.Enter(mode);

        Assert.True(CastInput.MindHasControls(steered));
        Assert.True(CastInput.Refused(steered));
        Assert.Equal(1, CastInput.OnGround(steered, steered.Position, InputModifier.None));
        var other = place.Things.First(t => t.Actor is not null && !ReferenceEquals(t, steered));
        Assert.Equal(1, CastInput.OnThing(steered, other, InputModifier.None));
    }

    [Fact]
    public void A_pet_or_a_possessed_body_is_the_minds_too_and_only_the_steered_one_is_refused()
    {
        using var files = Install.Open();
        var (place, steered, minds, _) = Open(files);
        steered.Mind!.Flags |= Mind.FlagPossessed;
        Assert.True(CastInput.Refused(steered));
        steered.Mind.Flags &= ~Mind.FlagPossessed;
        Assert.False(CastInput.Refused(steered));

        var other = minds.All.First(m => !ReferenceEquals(m.Thing, steered) && m.Thing.Actor is not null);
        other.Flags |= Mind.FlagPet;
        Assert.True(CastInput.MindHasControls(other.Thing));
        Assert.False(CastInput.Refused(other.Thing));             // not the one being steered
    }

    [Fact]
    public void With_nothing_selected_the_right_button_does_nothing()
    {
        using var files = Install.Open();
        var (_, steered, _, _) = Open(files);
        steered.Actor!.Disciplines.Current = null;
        Assert.Equal(0, CastInput.OnGround(steered, steered.Position, InputModifier.None));
    }

    /// <summary>1 to 6 are the quick slots; F is Feed; H is Blood Healing, or Heal for a human.</summary>
    [Fact]
    public void The_keys_select_quick_slots_feed_and_healing()
    {
        using var files = Install.Open();
        var (_, steered, _, powers) = Open(files);
        var me = steered.Actor!;
        var celerity = me.Disciplines.Add(powers["Celerity"]!, 0, quickSlot: 2);
        var feed = me.Disciplines.Add(powers["Feed"]!, 0);
        var heal = me.Disciplines.Add(powers["Heal"]!, 0);

        Assert.True(CastInput.Key(steered, '3'));
        Assert.Same(celerity, me.Disciplines.Current);
        Assert.False(CastInput.Key(steered, '4'));                // an empty slot
        Assert.False(CastInput.Key(steered, '3'));                // already the one
        Assert.True(CastInput.Key(steered, 'f'));
        Assert.Same(feed, me.Disciplines.Current);
        Assert.True(CastInput.Key(steered, 'H'));
        Assert.Same(heal, me.Disciplines.Current);

        var blood = me.Disciplines.Add(powers["BloodHealing"]!, 0);
        Assert.True(CastInput.Key(steered, 'h'));
        Assert.Same(blood, me.Disciplines.Current);
        Assert.False(CastInput.Key(steered, 'x'));
    }
}
