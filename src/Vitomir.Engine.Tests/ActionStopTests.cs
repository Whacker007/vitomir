// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **An action stopped from outside eases back into the motion** (the motion channel's stop,
/// read): the action channel is cleared when it holds something, and then — whether it did or
/// not — the fade starts from the pose last drawn at rate 5, the same fifth of a
/// second an action that runs out fades over. Every stop of an action in 1.1 is this one (the
/// scripts' <c>StopAction</c> and <c>StopActorAction</c>, the feeding's, the throw's, the revive's);
/// Vitomir's had snapped to the motion.
/// </summary>
public class ActionStopTests
{
    static WorldThing Christof(out Location place)
    {
        var files = Install.Open();
        place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        return place.Player!.Thing;
    }

    [Fact]
    public void A_stopped_action_fades_back_over_a_fifth_of_a_second()
    {
        var christof = Christof(out var place);
        var anim = christof.Animation!;
        Assert.NotNull(place.Motions);
        anim.PoseBones();
        Assert.True(anim.Play(anim.Mode!.Name, place.Motions!, onMotion: false) > 0);
        anim.Advance(1f);                                                  // the fade into it is over
        anim.PoseBones();
        Assert.Equal(0f, anim.Fade);
        Assert.NotNull(anim.Action);

        anim.StopAction();
        Assert.Null(anim.Action);
        Assert.Equal(1f, anim.Fade);
        Assert.Equal(5f, anim.FadeRate);
        anim.Advance(0.1f);
        Assert.Equal(0.5f, anim.Fade, 3);
    }

    [Fact]
    public void Stopping_an_empty_action_channel_still_fades()
    {
        // The stop tests the channel only to clear it; the fade follows either way.
        var christof = Christof(out _);
        var anim = christof.Animation!;
        anim.PoseBones();
        anim.Advance(1f);
        Assert.Null(anim.Action);
        Assert.Equal(0f, anim.Fade);
        anim.StopAction();
        Assert.Equal(1f, anim.Fade);
    }
}
