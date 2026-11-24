// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The scripts' part of each thing's own tick: the world's walk
// (Vitomir.Engine.World.Simulation, World/ThingTick.cs) asks for a thing's track move and its
// proximity test from inside that thing's tick, and for the deletion of one destroyed before the
// walk reached it.
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>Whether the scripts have anything to run this frame (a live script or a carried one's).</summary>
    bool stepping;

    /// <summary>
    /// The scripts' part of the frame before the world's walk: the scripts' clock moves on and the
    /// frame is counted (the proximity test staggers by it); the things destroyed since the last
    /// walk are deleted (1.1 deletes each at its own place in the next walk, and
    /// Vitomir's destroy has already taken it out of the list, so it is done as the walk begins —
    /// ours); and the walks of no length arrive (<c>Repairs.ClosedRound</c>, ours).
    /// </summary>
    public void BeginThings(float seconds)
    {
        stepping = live.Count > 0 || loose.Count > 0;
        if (!stepping) return;
        now += seconds;
        frames++;
        DeleteDoomed();                                                             // CodexScenes.Destroyed.cs
        ArrivePending();
    }

    /// <summary>
    /// One thing's track move (a <c>MOVETYPE 2</c> thing's), filed again by
    /// where it now is; a thing that reaches its keyframe tells its scripts (<see cref="Arrived"/>)
    /// before the arrival's noise (<c>TrackMoves</c>) — which is how RotateAlways keeps
    /// turning: its <c>arrived</c> sets a timer, and the timer starts the next turn.
    /// </summary>
    public void MoveThing(WorldThing thing, float seconds)
    {
        if (!stepping || location is null || thing.Motion is null) return;
        TrackMoves.Step(location, thing, seconds, frame => Arrived(thing, frame));
    }

    /// <summary>
    /// **`THING_FLAG_PROXIMITYTRIGGER`** (0x400) — "Thing triggers a codex message when some thing
    /// comes close to it". Each thing's tick runs the proximity test for a thing in a world
    /// with the flag: on the frames where its GUID plus the frame count is a multiple of 16
    /// (<c>&amp; 0xf</c>), it asks who is touching it (<see cref="Proximity.Touching"/>: the
    /// characters within its `PROXRADIUS`, not itself, nearest first) and sends each one
    /// `touched(thingGuid, otherGuid, captureId)`. It stops as soon as a script has
    /// taken the flag off. The frame count is Vitomir's simulation steps (ours).
    /// </summary>
    public void TouchThing(WorldThing thing)
    {
        if (!stepping || location is null) return;
        if ((thing.Flags & WorldThing.Flag.ProximityTrigger) == 0 || !thing.InWorld || thing.Removed) return;
        if (((thing.Guid + frames) & (Vitomir.Engine.Rules.GameRules.Current.Time.TriggerEveryFrames - 1)) != 0) return;
        foreach (var toucher in Proximity.Touching(location, thing))
        {
            Tell3("touched", thing, toucher);
            if ((thing.Flags & WorldThing.Flag.ProximityTrigger) == 0 || thing.Removed) break;
        }
    }

    /// <summary>
    /// A destroyed thing the walk has reached (its deleting destructor): its
    /// deletion as the scripts see it, this frame — a thing destroyed by a thing the walk met
    /// before it.
    /// </summary>
    public void DeleteThing(WorldThing thing)
    {
        if (doomed.Remove(thing)) Deleted(thing);
    }

    /// <summary>
    /// **The things' own scripts in the order the scene's messages reach them**: the world's list
    /// from its head (<see cref="Location.ListOrder"/>, newest first), each thing's own script
    /// — the walk of `beginscene`, `endscene`, `joined`, `exitstatechanged` and `weatherchanged`,
    /// each of which reads a thing's next before it calls. A thing with more than one
    /// script of its own has them in the order they were made. The live scripts whose thing is not
    /// in the world's list — a scene-wide one, one whose thing was picked up — come after, in the
    /// order they were made (ours: the list walk does not reach them). A copy, so a script made or
    /// let go during the calls does not disturb the walk; one made during it is not told, as in 1.1,
    /// where it goes in at the head.
    /// </summary>
    Live[] InListOrder()
    {
        var all = live.ToArray();
        if (location is null || all.Length == 0) return all;
        var byThing = all.Where(l => l.Thing > 0).ToLookup(l => l.Thing);
        var told = new List<Live>(all.Length);
        foreach (var thing in location.ListOrder) told.AddRange(byThing[thing.Guid]);
        var reached = new HashSet<Live>(told, ReferenceEqualityComparer.Instance);
        foreach (var l in all) if (!reached.Contains(l)) told.Add(l);
        return [.. told];
    }
}
