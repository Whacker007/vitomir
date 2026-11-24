// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Engine.World;

/// <summary>
/// **The steered character's route is walked by the world, like everybody else's.** In the engine
/// a walker is part of the thing, and the thing tick runs its update for every
/// character with a route — the one the player steers included. The player's own input only
/// *orders* a walk (the click's move order); it never steps one.
///
/// Earlier, Vitomir walked the steered character's route from the viewer's frame loop instead,
/// and <see cref="Ai.Minds.Follow"/> leaves the steered character out for that reason. So anything
/// that stepped the world without the viewer — a headless `--settle`, a test, a replay — left a
/// route the steered character had been given standing still. That is how `OLDT_7_4`'s own
/// `SendActorToPos(220)`, the run into Ecaterina's region that sets off the Embrace,
/// left Christof standing for twenty seconds while the camera path played round him.
///
/// Driving with the keys (ours) is still the viewer's: it cancels the route
/// (<see cref="Player.Move"/>), so there is none here to walk. A character standing still is
/// not stepped here either — the viewer's idle step (<c>Move</c> with no push) is what puts him
/// back on his stand while nothing is ordered, as before.
/// </summary>
public static class SteeredWalk
{
    /// <summary>
    /// One tick of the steered character's route, when there is one. A
    /// character whose route has run out is left alone: <see cref="Player.Follow"/> with nowhere
    /// to go is the stand step, which would fight the viewer's keyboard drive.
    /// </summary>
    public static void Step(Location place, float seconds)
    {
        if (place.SteeredWalker is { Walking: true } steered) steered.Follow(seconds);
    }
}
