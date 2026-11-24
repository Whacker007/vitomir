// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `ExecuteBuySell`'s network arm on a host (when the game is a host): a shop a script opens
// for a player goes to that player's machine (`Net.HostShops`); for the host's own character, or
// nobody's, the single-player native runs as it is.
using Vitomir.Engine.Net;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    void HostShops(GameServer server) =>
        Wrap("Codex.ExecuteBuySell", (a, local) =>
        {
            if (location is not { Session: { } session } place || ScriptOf(CodexHost.Int(a, 0)) is not { } script
                || place.Thing(CodexHost.Int(a, 1)) is not { } owner)
                return local();
            Hook(session.Shops);
            return server.Shops.Execute(owner, CodexHost.Int(a, 2), (uint)CodexHost.Int(a, 3), script.Script) switch
            {
                ShopRoute.Here => local(),
                ShopRoute.Sent => CodexHost.Box(true),
                _ => CodexHost.Box(false),
            };
        });
}
