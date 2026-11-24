// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The destroy tells whoever is aiming at the thing, which a comparison with 1.1
/// of the game library found missing. A thing somebody's action is
/// aimed at carries <c>THING_FLAG_TARGETTED</c> (0x2000000); when it is destroyed, every actor and
/// player of its world whose action target is it has the action cancelled, and the bit is cleared
/// (the same loop as the pick-up's). Vitomir untargeted on a
/// pick-up only, so somebody walking to or swinging at a thing another had just broken or a script
/// had just removed kept at it.
/// </summary>
public class DestroyUntargetsTests
{
    static Location Open(GameFiles files) =>
        Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");

    /// <summary>A template every install has, so a test never invents one.</summary>
    const string Rat = "petRat";

    [Fact]
    public void Destroying_a_targetted_thing_stops_whoever_was_aiming_at_it()
    {
        using var files = Install.Open();
        var place = Open(files);
        var christof = place.Player!.Thing;
        var rat = place.Spawn(Rat, christof)!;
        christof.Actor!.Action = ActionMode.Swing;
        christof.Actor.Target = rat;
        rat.Flags |= WorldThing.Flag.Targetted;

        Assert.True(place.Remove(rat));

        Assert.Equal(ActionMode.None, christof.Actor.Action);
        // The cancel (called with (1, 0) from the untargeting) clears the action's target only for a move (case 1)
        // and the default arm; a swing (5) or an attack (2) keeps it, so in 1.1 the swinger is left
        // holding the GUID of a thing that is gone. Vitomir keeps the reference the same way.
        Assert.Same(rat, christof.Actor.Target);
        Assert.False(rat.Has(WorldThing.Flag.Targetted));
    }

    /// <summary>
    /// The bit is the whole gate: a thing nobody marked as targetted tells nobody, even
    /// one somebody's target still points at — as in 1.1, where every order that aims at a thing
    /// sets the bit.
    /// </summary>
    [Fact]
    public void Without_the_targetted_bit_nobody_is_told()
    {
        using var files = Install.Open();
        var place = Open(files);
        var christof = place.Player!.Thing;
        var rat = place.Spawn(Rat, christof)!;
        christof.Actor!.Action = ActionMode.Swing;
        christof.Actor.Target = rat;

        Assert.True(place.Remove(rat));

        Assert.Equal(ActionMode.Swing, christof.Actor.Action);
        Assert.Same(rat, christof.Actor.Target);
    }
}
