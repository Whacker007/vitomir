// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Six places loaded, and no more: the chronicle's location load, eviction and letting go.
using Microsoft.Extensions.Logging;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// **The original keeps six places loaded.** Every time a place is loaded — built for the first
/// time, or read back from its <c>location_&lt;n&gt;.nsw</c> — the chronicle's location load
/// marks it loaded (flag `0x100000`), stamps it with the game clock,
/// counts it, and, when the count passes
/// a limit (**6**; a network game's host keeps **11**), lets places go
/// until it does not: each time the loaded place with the **earliest stamp**, passing over
/// the one being entered, the one being left (the chronicle's current location, which moves only
/// after the load), any the chronicle marks <see cref="NscLocationFlags.NeverFree"/>, and
/// the one the camera is in. Letting go frees the world and clears the loaded bit — and
/// writes nothing, because the place was written when the party last left it: every journey writes
/// the place it leaves into <c>__Current__</c> before it builds the next.
/// The next visit finds no world and reads that file back.
///
/// So the stamp is **when the place was loaded, not when it was last visited**: a return to a
/// place still loaded neither stamps it again nor counts. Vitomir does the same with its own record
/// of a place (<see cref="Save.SaveCapture.OfPlace"/>), which is taken when the place is let go —
/// the same state as 1.1's file, because a place the party is not in does not change — and parked
/// (<see cref="Parked"/>), where a journey back finds it and builds the place from it
/// (<see cref="BuildSaved"/>), exactly as it does a place a loaded save remembers.
/// </summary>
public sealed partial class Campaign
{
    /// <summary>How many places stay loaded, and how many a network game's host keeps.</summary>
    public const int KeptPlaces = 6, KeptPlacesHosting = 11;

    /// <summary>How many places stay loaded: <see cref="KeptPlaces"/>, or <see cref="KeptPlacesHosting"/> for a host.</summary>
    public int KeepLoaded { get; set; } = Rules.GameRules.Current.Party.KeptPlaces[0];

    /// <summary>When each loaded place was loaded, as a count of loads — the order the load stamp gives.</summary>
    readonly Dictionary<int, long> loadedAt = [];
    long loads;

    /// <summary>The places let go since the campaign began, by location index — for the log and the tests.</summary>
    public IReadOnlyList<int> LetGo => letGo;
    readonly List<int> letGo = [];

    /// <summary>A place has been loaded: stamped, and — when that makes one too many — another let go.</summary>
    void Loaded(int index, int leaving = -1)
    {
        loadedAt[index] = ++loads;
        while (resident.Count > KeepLoaded)
        {
            // The earliest stamp among the places that may go, in location order.
            int pick = -1;
            foreach (var candidate in resident.Keys.Order())
            {
                if (candidate == index || candidate == leaving || candidate == Chronicle.Current) continue;
                if (Chronicle.At(candidate) is { } at && (at.Flags & (uint)NscLocationFlags.NeverFree) != 0) continue;
                if (ReferenceEquals(resident[candidate], Here)) continue;           // the camera's
                if (Occupied?.Invoke(resident[candidate]) == true) continue;       // a host's: a player is there (CampaignPlaces.cs)
                if (pick < 0 || loadedAt.GetValueOrDefault(candidate) < loadedAt.GetValueOrDefault(pick)) pick = candidate;
            }
            // Only where a place let go can be read back: a campaign with nothing to build a record
            // with (the headless host's) keeps every place instead, as Vitomir did earlier.
            if (pick < 0 || BuildSaved is null) return;
            LetGoOf(pick);
        }
    }

    /// <summary>
    /// The place is let go. Its record — what 1.1 wrote into <c>__Current__</c> when the
    /// party left it — is parked for the next visit to read back; its world and its scripts go.
    /// </summary>
    void LetGoOf(int index)
    {
        var place = resident[index];
        // Its clock stopped when the party left (the place is not simulated away from them); 1.1's
        // file is as of then, its timers on the game's clock (Save.AwayTimers).
        // A host's place is left as its file has it, or not at all (CampaignFiles.cs).
        // One loaded early and never written has no file to read back (CampaignPreloads.cs).
        if (!unentered.Remove(index) && LeftFor(index, place) is { } record) Parked[index] = record;
        resident.Remove(index);
        loadedAt.Remove(index);
        letGo.Add(index);
        log.LogInformation("travel: {Place} let go, the seventh place loaded; {Kept}", place.Name,
                           Parked.ContainsKey(index) ? "its record is kept for the next visit" : "it has no file, and comes back as its level made it");
        Discarded?.Invoke(place);
    }
}
