// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The host's side of the vault's moves and of the load screen on the players' machines: 1.1's messages
// 0x16 and 0x48 (from the chronicle's load).
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

public sealed partial class GameServer
{
    /// <summary>
    /// A player's vault move (1.1's 0x16 at the host, <see cref="VaultMoves.Host"/>) — **ours**: only
    /// for the player's own character. Made, it is told: to **every player** when the mover is a
    /// player's character (type 7, reliably), to the players in the mover's place otherwise
    /// (reliably); refused, nobody hears. No pause test: 1.1 has none.
    /// </summary>
    void VaultMoved(NetPlayer from, VaultMove move)
    {
        if (PlaceOf(from) is not { } place || from.Avatar is not { } me || move.Mover != me.Guid) return;
        bool made = false;
        Places.Within(place, () => made = VaultMoves.Host(place, move));
        Log?.Invoke($"net: {from.Name}'s vault, in {move.In}, out {move.Out} — {(made ? "made" : "refused")}");
        if (!made) return;
        var w = new NetWriter(Msg.VaultMove);
        move.Write(w);
        if (me.Type == ThingType.Player) Everyone(Channel.Reliable, w.ToArray());
        else InPlace(place, Channel.Reliable, w.ToArray());
    }

    /// <summary>
    /// **Ours**: what <paramref name="p"/>'s character's vault holds, sent to her machine before the vault opens there
    /// (<see cref="SendVault"/>) — each item's GUID, template, cell and count, all in one message (the transport
    /// sends a long one in pieces; the earlier wire's went sixteen a message), replacing what her machine had. 1.1's player's
    /// machine knows every item its character has (each made and moved by its own message); Vitomir's sends what a
    /// character carries in its sheet, and the vault, which holds ninety, rides apart from it.
    /// </summary>
    void SendVaultItems(NetPlayer p)
    {
        if (p.Avatar is not { Actor: { } a } me) return;
        var items = a.Inventory.Items.Where(i => i.Thing is not null && i.Location == Rpg.Slot.Vault).Take(ushort.MaxValue).ToList();
        var w = new NetWriter(Msg.VaultItems).I32(me.Guid).U16((ushort)items.Count);
        foreach (var i in items) w.I32(i.Thing!.Guid).Str(i.Thing.Template).I16((short)i.Cell.X).I16((short)i.Cell.Y).I32(i.Amount);
        Send(p, Channel.Reliable, w.ToArray());
    }

    /// <summary>
    /// **The load screen on the players' machines** (<see cref="NetPane"/>): on before
    /// the host loads a place not in memory, off when it has or has failed, to every
    /// player reliably. **Ours**: the "on" is sent at once (the transport's queue flushed) — 1.1's DirectPlay sends
    /// as it is asked, and Vitomir's host would otherwise send both after the load, the screen never seen.
    /// </summary>
    public void LoadScreen(bool on)
    {
        Everyone(Channel.Reliable, new NetWriter(Msg.Pause).Bool(on).U8(NetPane.Load).ToArray());
        if (on) peer.Flush();
    }
}
