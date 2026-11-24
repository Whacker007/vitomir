// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A host's places tied to its campaign and scripts (`Engine.Net.HostPlaces`): players in different places. The headless
// host's run and the windowed host's both use it.
using Vitomir.Engine;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir;

public static class NetPlaces
{
    /// <summary>
    /// A host's places are its campaign's (the chronicle's locations it keeps loaded, eleven at most,
    /// as in 1.1): a player going somewhere alone has the place loaded through the campaign
    /// (<see cref="Campaign.Visit"/>) and its scripts opened beside the host's own
    /// (`CodexScenes.Open`); a place a player is in is never let go, and one let go is
    /// forgotten; each other place a player is in is stepped with its own simulation, sharing the
    /// host's scripts and conversations.
    /// </summary>
    public static void Wire(GameServer server, Campaign campaign, Session session, Scripting.CodexScenes? scenes, Func<Conversations?> talk)
    {
        var places = server.Places;
        places.Chronicle = campaign.Chronicle;
        campaign.Occupied = places.Kept;                            // a player's place, or the camera's
        campaign.LoadScreen = server.LoadScreen;                    // the players' load screen while a place loads
        campaign.Discarded += places.Forget;
        places.Loaded = () => campaign.Resident;                   // every loaded place is ticked on a host (HostPlaces.Loaded.cs)
        places.Load = (index, entrance) =>
        {
            if (campaign.Visit(index, entrance) is not { } there) return null;
            there.Session ??= session;
            scenes?.Open(there);
            return there;
        };
        // The teleporters' network parts (TeleportsNetwork.cs), and a campaign to load the portal's place with.
        session.Teleports.Campaign ??= campaign;
        session.Teleports.Network = places;
        places.Simulate = there => new Simulation(there) { Scripts = scenes ?? (ISceneScripts)NoScripts.Instance, Talk = talk() };
    }

    /// <summary>
    /// A headless player's still survives the journey (ours, for looking at a player's window after the
    /// host has moved it): `--headless` and `--settle` go on to the run that loads the new place.
    /// </summary>
    public static string[] StillArgs(string[] args)
    {
        var kept = new List<string>();
        foreach (var name in new[] { "--headless", "--settle" })
            if (Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length) { kept.Add(name); kept.Add(args[i + 1]); }
        return [.. kept];
    }

    /// <summary>
    /// `/jump &lt;slot or name&gt; &lt;location&gt; [entrance]` on a host's console or chat line (ours): that player's
    /// character to a location of the chronicle, alone — the Storyteller's Jump to Location,
    /// with an entrance. Slot 0 on a playing host is the host's own journey,
    /// alone (at entrance 0 as the Storyteller's jump). Answers the line to print.
    /// </summary>
    public static string Jump(GameServer server, string[] words)
    {
        // The player by slot or by name.
        int slot = words.Length < 3 ? -1 : int.TryParse(words[1], out var n0) ? n0
            : server.Players.FirstOrDefault(p => p.Name.Equals(words[1], StringComparison.OrdinalIgnoreCase))?.Slot ?? -1;
        if (slot < 0 || server.Slots.ElementAtOrDefault(slot)?.Avatar is not { } who)
            return "  jump: no such player";
        if (server.Places.Chronicle is not { } story) return "  jump: the game has no chronicle";
        int to = int.TryParse(words[2], out var n) ? n : story.Find(words[2]);
        int entrance = words.Length > 3 && int.TryParse(words[3], out var e) ? e : 0;
        if (story.At(to) is not { } at) return $"  jump: no location {words[2]} (the chronicle has {string.Join(", ", Enumerable.Range(0, story.Count).Select(story.NameOf))})";
        if (slot == 0 && server.Places.JumpTo(who, to)) return $"  jump: {server.Slots[0]!.Name} goes to {at.Name} alone, entrance 0";
        return slot != 0 && server.Places.Move(who, to, entrance)
            ? $"  jump: {server.Slots[slot]!.Name} goes to {at.Name}, entrance {entrance}"
            : $"  jump: {server.Slots[slot]!.Name} cannot go to {at.Name}";
    }
}
