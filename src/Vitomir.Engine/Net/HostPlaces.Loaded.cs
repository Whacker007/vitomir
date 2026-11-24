// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A host ticks every world it has loaded.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **A network host ticks every place it has loaded**. The world tick's first arm, taken when
/// on a host (net mode 2), walks every chronicle location and ticks the world of each that has
/// one; a single player's game ticks only the current location's.
/// The script timers are the same: on a host each one that falls due fires,
/// whatever place its thing is in (the timer tests for a host first), where a single player's
/// throws away one whose thing is not in the party's place.
///
/// So a place is ticked on a host whether or not anybody is in it: a place every player has left, the
/// place the whole game journeyed away from, and — after a chronicle change, whose start loads its
/// early places on a host as in single player (state 7) — a haven nobody has been
/// to yet. Its minds think, its things move and its scripts' timers fire. Earlier, Vitomir's host
/// stepped only the places a player or the camera was in.
///
/// Here the campaign's loaded places (<see cref="Loaded"/>) that are not yet among the host's are
/// adopted as they are found, each with a simulation of its own started on the host's clock (1.1 has
/// one clock), and <see cref="Step"/> steps them all; <see cref="Wake"/> has the script
/// layer run a place's scripts it had put aside (a place left, or loaded early).
/// </summary>
public sealed partial class HostPlaces
{
    /// <summary>The campaign's loaded places other than the host's own, by location index (the run's: <c>Campaign.Resident</c>).</summary>
    public Func<IEnumerable<KeyValuePair<int, Location>>>? Loaded { get; set; }

    /// <summary>
    /// Has the script layer run a loaded place's scripts, if it had put them aside — a place the party
    /// left, or one loaded early (<c>CodexScenes.KeepRunning</c>) — and answers whether they run. Null
    /// for a host with no scripts: every place is stepped.
    /// </summary>
    public Func<Location, bool>? Wake { get; set; }

    /// <summary>
    /// Whether a place of the host's nobody is in may be stepped: its scripts, if there are any, run for
    /// it (a place whose scripts were never made is left still, ours — stepping it would have the host's
    /// own place's scripts answer for it).
    /// </summary>
    bool Awake(HostPlace h) => Wake?.Invoke(h.Place) ?? true;

    /// <summary>
    /// The campaign's loaded places that are not the host's yet, adopted — their orders, conversations
    /// and awards the host's (<see cref="Adopt"/>) — each with a simulation of its own (<see cref="Started"/>).
    /// </summary>
    void AdoptLoaded()
    {
        if (Loaded is null || Home is null) return;
        foreach (var (index, place) in Loaded().ToArray())
        {
            if (Of(place) is not null) continue;
            Adopt(place, index).Sim = Started(place);
        }
    }

    /// <summary>
    /// A simulation for one of the host's other places, started on the host's clock (the run's
    /// <see cref="Simulate"/>, or a bare one). 1.1 has one clock for every world; a
    /// simulation started at nought put its place's clock back to nought on its first step, and a
    /// deadline a thing there carried — an effect's end, a recast — ran from the wrong time.
    /// </summary>
    Simulation Started(Location place)
    {
        var sim = Simulate?.Invoke(place) ?? new Simulation(place);
        if (Home?.Place is { } home && !ReferenceEquals(home, place)) sim.Resume(home.Milliseconds / 1000f);
        return sim;
    }
}
