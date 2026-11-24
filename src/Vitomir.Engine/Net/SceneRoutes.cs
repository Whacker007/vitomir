// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The one question the conversation and the camera natives ask in a network game: "is the player this
// is for somebody else's machine?" In 1.1 each of the functions asks it itself — the conversation's
// start, the camera's cutscene, its shot, its release
// and the path (`PlayPath`) all walk the slot table for the GUID the
// script named on a host (net mode 2), and for a slot above 0 send the thing to that player instead
// of doing it here. This file is that walk, registered per place as the orders are (`NetOrders`).
using System.Runtime.CompilerServices;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Net;

/// <summary>What the host's routing did with a conversation a script asked for.</summary>
public enum RouteAnswer
{
    /// <summary>Not a network player's: run it here, as single player does.</summary>
    Here,
    /// <summary>Sent to the player's machine; the native answers true.</summary>
    Sent,
    /// <summary>Refused (the start's `return 0`); the native answers false.</summary>
    Refused,
}

/// <summary>The host's routing of a script's conversation and camera to the player it names.</summary>
public interface ISceneRoute
{
    /// <summary>`ExecuteConversation`'s network arms.</summary>
    RouteAnswer Converse(Conversations talk, Location place, string file, NcoConversation conversation,
                         ConversationFlag flags, int starter, int npc, Action<Conversation>? wire);
    /// <summary>`SetupCutscene(client, a, b)` for a network player (its network arm): true when it was theirs.</summary>
    bool SetupCutscene(int client, WorldThing first, WorldThing? second);
    /// <summary>`SetShot(client, …)` for a network player (message 0x2b).</summary>
    bool SetShot(int client, int shot, int angle, int distance, int side, int ms);
    /// <summary>`Release(client)` for a network player (message 0x2c).</summary>
    bool Release(int client);
    /// <summary>`PlayPath(client, script, name, speed)` for a network player (message 0x2d).</summary>
    bool PlayPath(int client, string name, float speed, Action? ended);
}

/// <summary>Which places are a host's, and the routing each one asks (per place, as <see cref="NetOrders"/>).</summary>
public static class SceneRoutes
{
    static readonly ConditionalWeakTable<Location, ISceneRoute> places = new();

    public static void Attach(Location place, ISceneRoute route) => places.AddOrUpdate(place, route);
    public static void Detach(Location place) => places.Remove(place);

    /// <summary>The routing of a host's place, or null anywhere else.</summary>
    public static ISceneRoute? For(Location? place) =>
        place is not null && places.TryGetValue(place, out var r) ? r : null;
}
