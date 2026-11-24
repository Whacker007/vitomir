// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// A threshold of a cell level crossed (ours): the party went into or came out of a room that
/// stands for a 1.1 place. In 1.1 this is a level change, and the next round's scripts hear it as one.
/// </summary>
/// <param name="Area">The room.</param>
/// <param name="Entering">True going in, false coming out.</param>
/// <param name="From">The chronicle place left: the level's own going in, the room's coming out; "" when no chronicle runs.</param>
/// <param name="To">The chronicle place entered.</param>
/// <param name="Exit">The exit of <paramref name="From"/> that 1.1 takes for this, or −1 when the chronicle has none.</param>
/// <param name="Entrance">The entrance of <paramref name="To"/> 1.1 arrives at, or −1.</param>
public readonly record struct CellCrossing(L2Area Area, bool Entering, string From, string To, int Exit, int Entrance);

// **Ours: what a cell level does while it is played** — its exits, the party's area, the area's
// sound. The game binds a level to the chronicle when the party arrives in
// it (Bind) and steps it once a frame (Step, from Simulation.Step).
public sealed partial class CellLevel
{
    CellAreas? areas;
    CellExits? exits;
    Location? bound;
    bool wired;

    /// <summary>The level's areas and which the party is in.</summary>
    public CellAreas Areas => areas ??= new CellAreas(Manifest.Areas, Manifest.Units.EngineUnitsPerMetre);

    /// <summary>The level's exits (<see cref="CellExits.Take"/> is the seam a clicked exit sign calls).</summary>
    public CellExits Exits => exits ??= new CellExits(Manifest);

    /// <summary>The sound of the level's areas.</summary>
    public CellAreaSound Sound { get; } = new();

    /// <summary>The party crossed a room's threshold: what stands for 1.1's level change, for the scripts to hear.</summary>
    public event Action<CellCrossing>? Crossed;

    /// <summary>What the level reports as it is played (the game writes it to its log), prefixed <c>area:</c> or <c>exit:</c>.</summary>
    public Action<string>? Say { get; set; } = Console.WriteLine;

    /// <summary>
    /// The party has arrived somewhere (the start of a game, a load, a journey): a cell level is bound to the
    /// chronicle running, and a 1.1 level only has the chronicle's threshold question taken away. One call
    /// for every arrival, whatever the place is.
    /// </summary>
    public static void Arrived(Location place, Chronicle? chronicle)
    {
        if (chronicle is not null) chronicle.Threshold = null;
        place.Audio?.AbandonCrossfade();
        Of(place.Level)?.Bind(place, chronicle);
    }

    /// <summary>
    /// A place has been thrown away — let go as the seventh loaded, rebuilt, or its chronicle
    /// ended (<c>Campaign.Discarded</c>): a cell level's stream stops reading cells. A place let go comes back
    /// from its record as a new level. Nothing for a 1.1 level.
    /// </summary>
    public static void Gone(Location place) => Of(place.Level)?.Stream.Dispose();

    /// <summary>
    /// Binds the level to the place it is and the chronicle running (null for a level played on its own):
    /// its exits answer to the chronicle, the chronicle asks it about rooms in the map
    /// (<see cref="Chronicle.Threshold"/>), and where the party was is forgotten.
    /// </summary>
    public void Bind(Location place, Chronicle? chronicle)
    {
        bound = place;
        Exits.Chronicle = chronicle;
        Exits.Reset();
        Areas.Reset();
        Sound.Reset();
        if (chronicle is not null) chronicle.Threshold = n => Exits.Cross(n, place);
        if (wired) return;
        wired = true;
        Areas.Left += a => Cross(a, entering: false);
        Areas.Entered += a => Cross(a, entering: true);
        Exits.Took += (n, taken, exit) => Say?.Invoke(taken switch
        {
            CellExits.Taken.Leaving => $"exit: {n} taken: leaving {Here()} for {Going()}",
            CellExits.Taken.WalkingIn => $"exit: {n} taken: {exit?.Area?.Name} is a room of this map; the party walks in through its door " +
                                         $"(1.1 loads {exit?.ToLevel} here)",
            _ => $"exit: {n} refused: " + (Exits.Chronicle is null ? "no chronicle is running here (give --place <Place>=<package>)"
                                           : $"{Here()} has no such exit, or it is shut, or it leads nowhere"),
        });
    }

    string Here() => Exits.Chronicle is { } c ? c.NameOf(c.Current) : Name;
    string Going() => Exits.Chronicle is { Leaving: { } g } c ? $"{c.NameOf(g.To)} ({c.At(g.To)?.LevelName}), entrance {g.Entrance}" : "nowhere";

    /// <summary>The walkers' shown height over the level's steps (ours; <see cref="CellStepEase"/>), stepped by the world's tick after the things have moved.</summary>
    public CellStepEase Ease { get; } = new();

    /// <summary>
    /// One frame of the level being played, the leader of <paramref name="place"/> where it now is: the
    /// stream's cells are taken in, the party's area worked out, a gate's volume walked into takes its exit,
    /// and a crossfade moves on.
    /// </summary>
    public void Step(Location place, float seconds)
    {
        Stream.Update(place.Player?.Position ?? place.StartPosition);
        if (place.Player is { } leader)
        {
            Areas.Update(leader.Position);
            if (Exits.Chronicle is not null && Exits.Walked(leader.Position) is { } gate) Exits.Take(gate);
        }
        place.Audio?.StepCrossfade(seconds);
    }

    void Cross(L2Area area, bool entering)
    {
        var chronicle = Exits.Chronicle;
        var place = bound;
        string own = chronicle is not null ? chronicle.NameOf(chronicle.Current) : "";
        var room = CellAreaSound.PlaceOf(area, chronicle);
        string roomName = room?.Name ?? area.Place ?? "";
        // 1.1's numbers for the same crossing: going in, the exit of the level's place and the entrance it
        // arrives at; coming out, the way back the chronicle's loader wrote for that line.
        int exit = -1, entrance = -1;
        if (chronicle?.At(chronicle.Current) is { } here && area.Exit is { } n && n >= 0 && n < here.ExitCount && here.Exits[n].To >= 0)
            (exit, entrance) = entering ? (n, here.Exits[n].Entrance) : (here.Exits[n].Entrance, n);
        string sound = place is null ? "" : entering ? Sound.Enter(area, place, chronicle) : Sound.Leave(area, place);
        var crossing = entering ? new CellCrossing(area, true, own, roomName, exit, entrance)
                                : new CellCrossing(area, false, roomName, own, exit, entrance);
        Say?.Invoke($"area: the party is {(entering ? "in" : "out of")} {area.Name}" +
                    (roomName.Length == 0 ? ""
                     : chronicle is null ? $" (stands for {roomName}; no chronicle is running)"
                     : $" ({crossing.From} -> {crossing.To}" + (exit >= 0 ? $", 1.1's exit {exit} to entrance {entrance}" : "") + ")") +
                    (sound.Length > 0 ? "; " + sound : ""));
        Crossed?.Invoke(crossing);
    }
}
