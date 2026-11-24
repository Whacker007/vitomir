// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A player's machine told where the other players go:
// 1.1's host sends its message 0x3f to every client whenever its mover moves a player's
// character, and a machine that is not that player's reads it in its
// other arm.
using Vitomir.Engine.Rpg;

namespace Vitomir.Engine.Net;

public sealed partial class GameClient
{
    readonly Dictionary<int, (int Location, string Name)> whereabouts = [];

    /// <summary>
    /// Where each other player has last been told to have gone — the chronicle's location index and
    /// name, by slot (**ours** as a record: 1.1 keeps nothing of it, and only takes the
    /// character out of this machine's world). Empty for a player nobody has seen travel.
    /// </summary>
    public IReadOnlyDictionary<int, (int Location, string Name)> Whereabouts => whereabouts;

    /// <summary>
    /// `Msg.Went` (1.1's 0x3f for another slot): the character of the player in that slot is
    /// stopped — whatever it was doing cancelled, its model's action channel stopped
    /// and its walk — and taken out of this machine's world, wherever it
    /// was going; this player's record of it names no character until the host says where it is again.
    /// A message for this machine's own slot is ignored (its own journey is `Msg.Travel`).
    /// </summary>
    void Went(NetReader r)
    {
        int slot = r.U8();
        int index = r.I32();
        r.I32();                                                    // the entrance, which nothing here reads
        string name = r.Str();
        if (slot == Slot) return;
        whereabouts[slot] = (index, name);
        string who = players.TryGetValue(slot, out var p) ? p.Name : $"slot {slot}";
        if (p is { Avatar: not 0 } && mirror?.Find(p.Avatar) is { } body && !ReferenceEquals(body, local))
        {
            if (body.Actor is not null) Actions.Cancel(body, force: true, release: true);
            body.Animation?.StopAction();
            Actions.StopWalking(body);
            mirror.Removed(p.Avatar);
        }
        if (p is { Avatar: not 0 }) players[slot] = p with { Avatar = 0 };
        Log?.Invoke($"net: {who} goes to {name}");
    }
}
