// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A network game's host loads places the party is not going to: a player travels alone,
// and the place stays loaded while a player is in it.
namespace Vitomir.Engine.World;

public sealed partial class Campaign
{
    /// <summary>
    /// **A place loaded for somebody else**: a network game's host loads the
    /// location a player is going to alone, and the party — the host's own — stays where it is. A
    /// place still loaded is answered as it is (the chronicle's load does nothing for a location with a
    /// world; a changed chapter is swapped in it at the end of the frame); a place a load
    /// or an earlier letting-go parked is read back (its chapter swapped if it has changed
    /// since); anything else is built fresh at the
    /// entrance. A place built here is stamped and counted as any load is, and may let another go
    /// (<see cref="Loaded"/>). Null when the location names no level.
    /// </summary>
    public Location? Visit(int index, int entrance)
    {
        if (Chronicle.At(index) is not { } at || at.LevelName.Length == 0) return null;
        if (LoadedAt(index) is { } kept) return kept;                          // a changed chapter is swapped at the frame's end (ChapterSwap.cs)
        string? scene = at.SceneName.Length > 0 ? at.SceneName : null;
        Location built;
        if (Parked.Remove(index, out var record) && BuildSaved is not null)
        {
            built = Numbered(() => BuildSaved(Save.AwayTimers.Since(record, Here.Milliseconds), entrance), saved: true);
            ReadBack(built, scene);                                              // ChapterSwap.cs
        }
        else built = Numbered(() => Build(at.LevelName, scene, entrance), saved: false);   // the game's counter (CampaignGuids.cs)
        // Nobody of the party is in it: the location load builds the world alone, and only the party's own
        // journey stands the party in a place. A build puts this machine's
        // character at the entrance; it is taken out again — the host's own character had stood on the
        // start a carried body or an arriving player is given — and is put back when the host comes
        // (Travel's `Reenter`).
        if (built.Player?.Thing is { InWorld: true } stray) { Rpg.Utility.Leave(stray); built.Withdraw(stray); }
        resident[index] = built;
        Loaded(index);
        return built;
    }

    /// <summary>
    /// Whether somebody is in a loaded place — on a network game's host, a player's character
    /// (the eviction walks the slots in state 4 and passes over the location each one's character is in).
    /// Such a place is never let go. Null (single player): nobody but the party, which is <see cref="Here"/>.
    /// </summary>
    public Func<Location, bool>? Occupied { get; set; }
}
