// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A campaign's places are numbered from the game's one counter (`ThingGuids`).
namespace Vitomir.Engine.World;

public sealed partial class Campaign
{
    /// <summary>
    /// A place built for this game without the party's journey — a network host readying one
    /// (<see cref="Visit"/>), a teleporter's far end (<see cref="Load"/>): numbered from the game's
    /// counter, its stand-in player thing on the leader's number (it spends none; the location load builds the
    /// world alone), and a remembered place on the numbers it was saved with.
    /// </summary>
    Location Numbered(Func<Location> build, bool saved)
    {
        var guids = Here.Guids;
        using var counting = ThingGuids.Use(guids);
        guids.Arriving = Here.Player?.Thing.Guid ?? 0;
        guids.Loading = saved;
        LoadScreen?.Invoke(true);                                               // CampaignLoadScreen.cs
        try { return build(); }
        finally { guids.Arriving = 0; guids.Loading = false; LoadScreen?.Invoke(false); }
    }
}
