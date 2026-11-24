// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The campaign's part in a teleport: a place loaded without the party going there, and the journey
// of state 6 — the same journey as an exit's, begun and ended at a teleporter instead of an entrance.
using Microsoft.Extensions.Logging;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

public sealed partial class Campaign
{
    /// <summary>`LOCATION_FLAG_SHOWINMAP` (`Codex.java`), which every arrival sets.</summary>
    public const uint ShowInMap = 0x8;

    /// <summary>
    /// **The location load on its own**: a location made ready without the party going there — the one the
    /// party is in, one still loaded, one a load parked (read back from its record, as 1.1 reads its
    /// `.nsw`), or one built fresh. The place is kept loaded, as 1.1's is (it counts towards the six
    /// the original keeps). Null for a location with no level.
    ///
    /// 1.1's load puts the loading plaque up whenever it builds, a cast of
    /// Walk the Abyss included. **Vitomir does not, outside a journey**: the cast's load runs inside
    /// the simulation step that fires the script, and drawing a frame from there would step the
    /// world again from inside itself. The game holds still for the load instead (ours).
    /// </summary>
    public Location? Load(int index)
    {
        if (index == Chronicle.Current) return Here;
        if (Chronicle.At(index) is not { } destination || destination.LevelName.Length == 0) return null;
        if (resident.GetValueOrDefault(index) is { } kept) return kept;         // a changed chapter is swapped at the frame's end (ChapterSwap.cs)
        string? scene = destination.SceneName.Length > 0 ? destination.SceneName : null;
        Location place;
        if (Parked.Remove(index, out var record) && BuildSaved is not null)
        {
            place = Numbered(() => BuildSaved(record, record.Entrance), saved: true);
            ReadBack(place, scene);                                              // ChapterSwap.cs
        }
        else place = Numbered(() => Build(destination.LevelName, scene, -1), saved: false);   // the game's counter (CampaignGuids.cs)
        // Its own player thing stands aside until the party comes: a journey carries the party onto it.
        if (place.Player?.Thing is { } standIn) place.Withdraw(standIn);
        resident[index] = place;
        log.LogInformation("load: {Place} made ready without the party ({Level})", destination.Name, destination.LevelName);
        return place;
    }

    /// <summary>
    /// Before a teleport journey is taken (the teleport journey's first half): the end's place loaded and the end
    /// still there, or the request spent with nobody moving (<see cref="Teleports.Depart"/>). Any
    /// other journey goes ahead.
    /// </summary>
    bool Departing()
    {
        if (Chronicle.Pending is not { Kind: SequenceKind.Teleport } asked) return true;
        if (Here.Session?.Teleports.Depart(asked) is true) return true;
        Chronicle.Take();                  // the journey state zeroes the request before it takes the teleport
        return false;
    }

    /// <summary>
    /// The end of every arrival (the arrival's tail): the location's last-visited time is the
    /// clock now (which the six-place unload goes by) and it gains
    /// <c>LOCATION_FLAG_SHOWINMAP</c> (<c>|= 8</c>) — **arriving somewhere puts it on the map**.
    ///
    /// And after the party has been carried into a teleport's place (the arrival with entrance −2): stood
    /// round the end (<see cref="Teleports.Land"/>), and the place's arrival — where the camera and
    /// the leader are put — is where the leader now stands.
    /// </summary>
    void Landing(Location to, Session? session)
    {
        if (Chronicle.Leaving is { } going && Chronicle.At(going.To) is { } arrived) arrived.Flags |= ShowInMap;
        if (Chronicle.Pending is not { Kind: SequenceKind.Teleport } asked || session is null) return;
        if (session.Teleports.Land(to, asked) is not { } stand) return;
        to.Arrivals = [stand];
        to.StartPosition = stand.Position + new System.Numerics.Vector3(0, 0, Location.EyeHeight);
        to.StartYaw = MathF.Atan2(stand.Rotation[1], stand.Rotation[0]) * 180f / MathF.PI + 90f;
    }
}
