// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The walker's own stop — as the kill calls it, built
//

namespace Vitomir.Engine.World;

/// <summary>
/// **The walker's stop**. For a character or a player-type thing (type 0 or 7) that is
/// walking (the walker's "moving" bit, physics bit 1): its `stopmove` noise
/// and the `scuff_&lt;floor&gt;` of a foot planted (sound 3, then <c>0x2e</c> plus the
/// floor's material), the walker's state bits cleared (<c>&amp; 0xffffdfd0</c> — the moving bit
/// with them, which is the route stopped), the locomotion chosen again (`stand`; here the
/// walker's own next step, which chooses it for a walker that has stopped), and
/// for the party's selected member the walk ring taken away. A walker that is not
/// walking is left alone.
///
/// Vitomir says the noises and takes the ring away as edges, frame to frame
/// (the walker's footfalls in each thing's tick, <c>World/ThingTick.cs</c>, and the ring's watch), for every other way a walk ends; this is
/// the one caller that stops a walker from outside and must not wait a frame, because the death's
/// own animation starts at the same moment. So it says the noises itself and marks the thing
/// as no longer walking, which keeps the edge from saying them twice; the ring is left to its watch
/// (the ring belongs to the simulation, which a thing cannot reach), which sees the stop on the next
/// frame — unless the member steered next is walking by then, when the ring stays until that one
/// stops (ours, a frame's difference in all but that case). The network half (the host telling its clients
/// where the walker stopped) is not sent — ours; the snapshot carries the position.
/// </summary>
public static class WalkerStop
{
    /// <summary>
    /// The kill's call: a thing the walker drives (movetype 1) is stopped through the
    /// walker. Answers whether it was walking.
    /// </summary>
    public static bool OnDeath(WorldThing thing)
    {
        if (thing.MoveType != WorldThing.MoveWalker) return false;
        if (thing.Type is not (ThingType.Actor or ThingType.Player)) return false;  // Type 0 or 7
        if (thing.Locomotion is not { Walking: true } walker) return false;
        Audio.Noise.Foley(thing, Audio.FoleyEvent.StopMove);
        if (Audio.KeyframeSounds.FloorOrNone(thing) is var floor and >= 0)
            Audio.Noise.Foley(thing, Audio.FoleyEvent.Scuff + floor);                // 0x2e + material
        thing.Walking = false;              // said here: the frame's edge must not say it again
        walker.Stop();                      // & 0xffffdfd0: the moving bit and the walk's state
        // The locomotion chosen again, is the walker's own next step here (a walker not
        // walking sets its speed to nothing and chooses — `stand`), under the death's animation.
        return true;
    }
}
