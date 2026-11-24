// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A network game's places as 1.1's host files them: the chronicle's place write and the save.
using Vitomir.Engine.Save;

namespace Vitomir.Engine.World;

/// <summary>
/// **A host's places are remembered as the host last left them**. 1.1 keeps a place it is not
/// playing as a file, <c>SaveGames\__Current__\location_&lt;n&gt;.nsw</c>, written by the place write — for a
/// place with a world loaded — at these moments and no others: every journey of the host's own party
/// (the journey state with the mode not a client's, and on a host slot 0's journey
/// through the mover), a save (the current place fresh, mode 1, then
/// **every** file of the folder copied into the save's own), and a teleporter pair made or an end of it
/// taken away (both ends' places). A remote player's journey writes
/// nothing, nor does letting a place go; the next load of the place reads its file back,
/// or builds it fresh when it has none. So in a network game a place the players stay in
/// after the host has gone plays on, but what they change there is **not** in a save the host makes
/// elsewhere, nor in the place when it is let go and loaded again — and a place only players have been
/// to has no file, and comes back from a save or a let-go as its level first made it. The
/// host is the central and really the only brain of the game, so anything outside of it is discarded.
///
/// Built only for a host (<see cref="Host"/>): <see cref="Files"/> is the folder, each place's record as
/// its file's text (<see cref="Jsonc"/>), so what is read back is what was written and nothing that
/// happens after can reach it. The single-player game keeps its own way (<c>CampaignUnload.cs</c>): a
/// record taken as a place is let go, the same state 1.1's file holds there, because a place the party
/// is not in does not change.
/// </summary>
public sealed partial class Campaign
{
    /// <summary>
    /// A host's <c>__Current__</c> folder: each filed place's record, by location index, as the text of
    /// its file. Null in the single-player game, which does not keep them (<c>CampaignUnload.cs</c>).
    /// </summary>
    public Dictionary<int, string>? Files { get; private set; }

    /// <summary>
    /// This campaign is a network game's host's: eleven places kept and each
    /// remembered by its file (<see cref="Files"/>).
    /// </summary>
    public void Host()
    {
        KeepLoaded = Rules.GameRules.Current.Party.KeptPlaces[1];
        Files ??= [];
    }

    /// <summary>
    /// **The place write**: place <paramref name="index"/> written to its file as it stands — when it has a world
    /// loaded; nothing when this is not a host's campaign (see the summary above for when 1.1 calls it).
    /// The party is left out of it, as a save leaves it out of every place (<see cref="SaveCapture.OfPlace"/>).
    /// </summary>
    public void File(int index)
    {
        unentered.Remove(index);                                                // it has a file now (CampaignPreloads.cs)
        if (Files is null || index < 0) return;
        var place = index == Chronicle.Current ? Here : resident.GetValueOrDefault(index);
        if (place is null) return;
        var party = new Dictionary<WorldThing, string>(ReferenceEqualityComparer.Instance);
        foreach (var member in SaveCapture.PartyOf(place)) party[member] = ThingIds.Member(member);
        Files[index] = Jsonc.Write(AwayTimers.Taken(SaveCapture.OfPlace(place, party), place, Here));
    }

    /// <summary>A place's record read back from its file — a fresh copy each time — or null when it has none.</summary>
    public LocationSave? Filed(int index) =>
        Files is not null && Files.TryGetValue(index, out var text) ? Jsonc.Read<LocationSave>(text) : null;

    /// <summary>
    /// A loaded save's record of a place the party is not in: parked for the next visit to read back
    /// and, on a host, its file too — a load copies the save's files into <c>__Current__</c>.
    /// </summary>
    public void Remember(int index, LocationSave record)
    {
        Parked[index] = record;
        if (Files is not null) Files[index] = Jsonc.Write(record);
    }

    /// <summary>
    /// A host's save: the current place, <paramref name="current"/> in
    /// <paramref name="world"/> — already written fresh by the save — goes to its file too (the place write with
    /// mode 1), and every other place's file is copied in as it stands, whatever has happened in that
    /// place since (the loop after it). Does nothing for a campaign that is not a host's.
    /// </summary>
    public void CopyFiles(Dictionary<string, LocationSave> world, string current)
    {
        if (Files is null) return;
        if (Chronicle.Current >= 0 && world.TryGetValue(current, out var fresh)) Files[Chronicle.Current] = Jsonc.Write(fresh);
        foreach (var index in Files.Keys.Order())
            if (index != Chronicle.Current && Filed(index) is { } record) world[Chronicle.NameOf(index)] = record;
    }

    /// <summary>
    /// What letting a place go leaves for its next load (letting go writes nothing): on a host the place's
    /// file, or nothing — a place with no file is built again as its level first made it; in single
    /// player the record taken now (<c>CampaignUnload.cs</c>).
    /// </summary>
    LocationSave? LeftFor(int index, Location place) =>
        Files is null ? AwayTimers.Taken(SaveCapture.OfPlace(place), place, Here) : Filed(index);
}
