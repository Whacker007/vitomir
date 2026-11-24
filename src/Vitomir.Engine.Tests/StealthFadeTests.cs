// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The stealth fade and the shadows** (from the thing draw): how
/// the steered character sees a hidden one — not at all when his perception is below its stealth,
/// 0.3 when equal, 0.5 one clear (and always 0.5 for himself or a friend), whole two clear — and
/// what that does to the shadow: an unseen character casts none, a half-seen one its full shadow,
/// because the sorted pass's test is the model's alpha, not the fade.
/// Also the mirror's rule for which things cast (the thing draw with bit 1).
/// </summary>
public class StealthFadeTests
{
    static WorldThing Body(int stealth = 0, int perception = 0, int team = 0, uint renderFlags = Shadows.FullShadow) => new()
    {
        Index = -1, Guid = 5, Scene = new NsdThing(), Template = "t", Type = ThingType.Actor,
        RenderFlags = renderFlags,
        Actor = new Actor { Stealth = stealth, Perception = perception, Team = team, Health = 100f, MaxHealth = 100f },
    };

    [Theory]
    [InlineData(0, 0, 1f)]          // no stealth: whole
    [InlineData(3, 5, 1f)]          // two clear: whole
    [InlineData(3, 4, 0.5f)]        // one clear: half
    [InlineData(3, 3, 0.3f)]        // just equal: faint
    [InlineData(3, 2, -1f)]         // below: not drawn (null)
    public void The_viewers_perception_against_the_stealth_sets_the_fade(int stealth, int perception, float expected)
    {
        var viewer = Body(perception: perception, team: 1);
        var hidden = Body(stealth: stealth, team: 2);
        var fade = StealthFade.Of(hidden, viewer);
        if (expected < 0f) Assert.Null(fade);
        else Assert.Equal(expected, fade!.Value, 1e-6f);
    }

    /// <summary>The viewer sees himself half there, and a friend too, whatever his perception; the Storyteller sees all.</summary>
    [Fact]
    public void Himself_and_a_friend_are_half_seen_and_the_storyteller_sees_all()
    {
        var me = Body(stealth: 6, team: 1);
        Assert.Equal(StealthFade.Half, StealthFade.Of(me, me));
        var friend = Body(stealth: 6, team: 1);
        Assert.Equal(StealthFade.Half, StealthFade.Of(friend, Body(team: 1)));
        var foe = Body(stealth: 6, team: 2);
        Assert.Null(StealthFade.Of(foe, Body(team: 1)));
        Assert.Equal(1f, StealthFade.Of(foe, Body(team: 1), storyteller: true));
    }

    /// <summary>
    /// The shadow, on Christof in the monastery: the fade does not keep a thing from casting (only a
    /// model alpha under 1 does), so hidden from a foe he still casts in the ordinary rule; only the
    /// unseen test (<see cref="Shadows.Unseen"/>, <see cref="StealthFade.Unseen"/>) stops it — and
    /// the steered character always sees himself (half). Then the mirror's rule (the thing draw with bit
    /// 1): REFLECTION ONLY — the first-person look's body — casts in the mirror and not in the world;
    /// NO REFLECTION in the world and not in the mirror; DON'T RENDER in neither.
    /// </summary>
    [Fact]
    public void An_unseen_character_casts_no_shadow_and_the_mirror_has_its_own_rule()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var christof = place.Player!.Thing;
        Assert.True(Shadows.Casts(christof));
        christof.Actor!.Stealth = 6;
        Assert.True(Shadows.Casts(christof));                                // the fade is not the alpha
        Assert.False(StealthFade.Unseen(christof, place));                   // he sees himself, half
        Assert.Equal(StealthFade.Half, StealthFade.Fade(christof, place));
        christof.Actor.Stealth = 0;

        uint flags = christof.RenderFlags;
        christof.RenderFlags = flags | 0x80;                                 // REFLECTION ONLY
        Assert.False(Shadows.Casts(christof));
        Assert.True(Shadows.Casts(christof, mirror: true));
        christof.RenderFlags = flags;
        christof.Flags |= WorldThing.Flag.NoReflection;
        Assert.True(Shadows.Casts(christof));
        Assert.False(Shadows.Casts(christof, mirror: true));
        christof.Flags &= ~WorldThing.Flag.NoReflection;
        christof.RenderFlags = flags | 0x2;                                  // DON'T RENDER
        Assert.False(Shadows.Casts(christof));
        Assert.False(Shadows.Casts(christof, mirror: true));
        christof.RenderFlags = flags;
    }

    /// <summary>
    /// A foe hidden from the steered character lays nothing: with the unseen test the shadow frame
    /// skips him, and a torch riding him goes with him (the thing draw skips the children too).
    /// </summary>
    [Fact]
    public void A_foe_the_player_cannot_see_is_not_drawn_nor_what_he_carries()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var foe = place.Drawn.First(t => t is { Actor: not null } && !ReferenceEquals(t, place.Player!.Thing)
                                         && !Rpg.Sight.Friendly(place.Steered!, t));
        Assert.False(StealthFade.Unseen(foe, place));
        foe.Actor!.Stealth = 2;
        Assert.True(StealthFade.Unseen(foe, place));
        Assert.Equal(0f, StealthFade.Fade(foe, place));
        var torch = place.Place("torchFlame", foe.Position, System.Numerics.Vector3.Zero, foe.Sector)!;
        torch.AttachedTo = foe;
        Assert.True(StealthFade.Unseen(torch, place));
        place.Steered!.Actor!.Perception = 3;
        Assert.Equal(StealthFade.Half, StealthFade.Fade(foe, place));
        Assert.False(StealthFade.Unseen(torch, place));
    }
}
