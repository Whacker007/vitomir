// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Stealth from what a character is doing** (read whole): feeding, or a swing
/// still playing, costs five; a cast still playing costs three; walking costs one; nothing costs
/// nothing — first match wins, and the busy time rather than the action alone is what counts for a
/// swing or a cast. Earlier, only the walking was built.
/// </summary>
public class StealthTests
{
    static WorldThing Hidden(int stealth, ActionMode doing = ActionMode.None, int busyUntil = 0) => new()
    {
        Index = -1, Guid = 5, Scene = new NsdThing(), Template = "t", Type = ThingType.Actor,
        Actor = new Actor { Stealth = stealth, Action = doing, BusyUntil = busyUntil, Health = 100f, MaxHealth = 100f },
    };

    [Theory]
    [InlineData(ActionMode.None, 0, false, 6)]
    [InlineData(ActionMode.None, 0, true, 5)]              // walking: one easier
    [InlineData(ActionMode.Feeding, 0, false, 1)]          // the neck: five, busy or not
    [InlineData(ActionMode.WristFeeding, 0, true, 1)]      // the wrist, and it beats the walking
    [InlineData(ActionMode.Swing, 500, false, 1)]          // a swing still playing: five
    [InlineData(ActionMode.Swing, 0, false, 6)]            // a swing's recovery run out: nothing
    [InlineData(ActionMode.Swing, 0, true, 5)]             //   … and walking is the next rung
    [InlineData(ActionMode.Cast, 500, false, 3)]           // a cast still playing: three
    [InlineData(ActionMode.Cast, 0, false, 6)]             // the lingering ACTION 13 of a finished cast
    [InlineData(ActionMode.Attack, 0, true, 5)]            // closing in is only walking
    public void Stealth_comes_down_for_what_a_character_is_doing(ActionMode doing, int busyUntil, bool moving, int expected) =>
        Assert.Equal(expected, Sight.StealthOf(Hidden(6, doing, busyUntil), moving));

    /// <summary>The five and the three are held at zero; the one never needs to be.</summary>
    [Fact]
    public void The_cost_is_held_at_zero()
    {
        Assert.Equal(0, Sight.StealthOf(Hidden(3, ActionMode.Feeding), moving: false));
        Assert.Equal(0, Sight.StealthOf(Hidden(2, ActionMode.Cast, busyUntil: 500), moving: false));
        Assert.Equal(0, Sight.StealthOf(Hidden(1), moving: true));
        Assert.Equal(0, Sight.StealthOf(Hidden(0, ActionMode.Feeding), moving: true));
    }

    /// <summary>
    /// What it means for being noticed: a looker with perception 2 cannot make out a
    /// character hidden at 6 standing still, and can the moment it bites somebody.
    /// </summary>
    [Fact]
    public void A_hidden_vampire_that_feeds_is_noticed()
    {
        var looker = Hidden(0);
        looker.Actor!.Perception = 2;
        var vampire = Hidden(6);
        Assert.False(Sight.Notices(looker, vampire, targetMoving: false));
        vampire.Actor!.Action = ActionMode.Feeding;
        Assert.True(Sight.Notices(looker, vampire, targetMoving: false));
    }
}
