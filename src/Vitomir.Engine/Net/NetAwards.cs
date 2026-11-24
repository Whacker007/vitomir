// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a network game changes about experience and Humanity. Experience a single-player party would
// share (and `AwardPartyExperience`) is split on the host between the players
// instead; a kill costs Humanity only when the game was made with Track Humanity
// (game flag 0x200). Registered per place, as `NetOrders` is, so a test can run a
// host and players side by side.
using System.Runtime.CompilerServices;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

public static class NetAwards
{
    static readonly ConditionalWeakTable<Location, GameServer> places = new();

    public static void Attach(Location place, GameServer server) => places.AddOrUpdate(place, server);
    public static void Detach(Location place) => places.Remove(place);

    /// <summary>
    /// A party award on a host's place, split between the players; null anywhere else, where
    /// the single-player party award stands. Every player in the game (slot state 4) with a character
    /// counts — and when the award is tied to a thing (<paramref name="near"/>: the kill's victim, or the
    /// quest's thing), only those in its place and not dead. The amount is divided by how many
    /// that is, as an unsigned whole number; a share that comes to nothing gives nobody anything. Each
    /// share goes through the one award. Answers who got it.
    /// </summary>
    public static List<WorldThing>? Split(Location place, int amount, WorldThing? near, Action<WorldThing, int>? rankGained)
    {
        if (!places.TryGetValue(place, out var server)) return null;
        var takers = new List<WorldThing>();
        foreach (var p in server.Players)
        {
            if (!p.Ready || p.Avatar is not { Actor: { } a } body || !body.InWorld) continue;
            if (near is not null && (!ReferenceEquals(body.Place, near.Place) || a.Has(Actor.Flag.Dead))) continue;
            takers.Add(body);
        }
        if (takers.Count == 0) return takers;
        // The rules file may give each the whole award, as a single-player party award does (ours).
        uint share = Rules.GameRules.Current.Advancement.PartyAwardSplitNetwork ? unchecked((uint)amount) / (uint)takers.Count : unchecked((uint)amount);
        if (share == 0) return [];
        int gift = (int)Math.Min(share, int.MaxValue);
        foreach (var body in takers) Experience.Award(body, gift, rankGained);
        server.Note($"net: {amount} experience split, {gift} each to {string.Join(", ", takers.Select(t => t.Actor!.Name))}");
        return takers;
    }

    /// <summary>
    /// Whether a kill in this place costs Humanity (the kill's gate): always in single player, never on
    /// a player's machine (which hurts nobody), and on a host only in a game made with Track Humanity.
    /// </summary>
    public static bool TracksHumanity(Location? place) =>
        place is null || NetOrders.Mode(place) switch
        {
            NetMode.Client => false,
            NetMode.Server => places.TryGetValue(place, out var server) && server.Flags.HasFlag(GameFlags.TrackHumanity),
            _ => true,
        };
}
