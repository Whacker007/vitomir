// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The chronicle's load of a place tells a network game's players.
namespace Vitomir.Engine.World;

public sealed partial class Campaign
{
    /// <summary>
    /// **A place loaded while players wait** (on a host — network mode 2): a location not in memory
    /// has the load screen put up on every player's machine before it is built or read back and taken down after (also
    /// when the build fails) —
    /// whichever caller loads it: the party's journey, a player going alone, a teleporter's
    /// far end, a preload. A place still loaded is not built, and says nothing. The network host's
    /// (`Net.GameServer.LoadScreen`, set by the harness); null in single player and on a player's machine, which
    /// loads no place for anybody (the load asserts `!netGame.IsClient()`).
    /// </summary>
    public Action<bool>? LoadScreen { get; set; }
}
