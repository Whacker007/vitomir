// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// **A reliable message of any size** (ours; the design intent:
// "That's an arbitrary limit, and we can probably send the item list a better and different way").
//
// Earlier, a reliable message had to fit one datagram (NetPeer.MaxMessage, 1,168 bytes), so the join carried a
// kept character of about twenty-five items at most, the sheet twenty-four, and the vault's contents went sixteen
// a message. 1.1 has no such ceiling because it never sends a list in one message: its join sends every item of
// the character by itself, each under DirectPlay's own limit (255 bytes, its
// send's assert). Vitomir's wire is its own, and the better way is one place for all of it, not a special
// case per message: the transport cuts a long reliable message into pieces, each a reliable message of its own —
// numbered, acknowledged and resent like any other — and the far end, which delivers reliable messages once and in
// order, joins them again before anything above the transport sees it. So the join, the sheet, the vault's
// contents and whatever else grows all go whole, up to MaxReliableMessage, and a message over that is refused
// where it is sent (an ArgumentException, as an oversize unreliable one is) and, should one arrive in pieces
// anyway, where it is received (the connection closed with TooBig, and the far end told).
//
// On the wire a piece is an ordinary reliable record whose length has its top bit set (the messages are at most
// MaxMessage long, so the bit was never used): more of the same message follows. The last piece is a plain record.
// A peer reads such a record's length as past the datagram's end and drops the rest of that datagram — it never
// sees a piece as a message — so the transport's own version (NetPeer.Version) stays 1 and a mismatched join still
// meets the protocol's refusal whenever its Hello fits one datagram.
//
// A long message must not go out in one burst either: a thousand datagrams at once overflow a socket's receive
// buffer (64 KB on a stock Windows) and would be resent, a thousand at a time, for ever. So the reliable channel
// keeps at most Window bytes sent and not yet acknowledged; the next message goes out when acknowledgements make
// room (resends are not held back, and one message always goes when nothing is in flight). An acknowledgement names
// the 32 messages past the first one missing, so under loss a piece past those is sent again though it arrived — the
// far end drops the copy; a window of 32 pieces would avoid that and halve what a round trip carries.
namespace Vitomir.Engine.Net;

public sealed partial class Connection
{
    /// <summary>
    /// The reliable bytes sent on this connection and not yet acknowledged (ours) — at most
    /// <see cref="NetPeer.Window"/>, or one message when that one alone is bigger.
    /// </summary>
    public int InFlight => NetPeer.InFlight(this);

    // The pieces of the message being received, in order, and how many bytes they come to.
    internal List<byte[]>? Pieces;
    internal int PiecesLength;
}

public sealed partial class NetPeer
{
    /// <summary>
    /// The largest reliable message (ours): 1 MiB, some nine hundred pieces. A joining character with every item
    /// the wire can name (65,535) would pass it; one with a thousand items is about 45 KB.
    /// </summary>
    public const int MaxReliableMessage = 1 << 20;

    /// <summary>
    /// The most reliable bytes sent and not yet acknowledged on a connection (ours): 64 KB, fifty-odd datagrams,
    /// under a stock Windows socket's receive buffer. A bigger message goes out over several round trips.
    /// </summary>
    public const int Window = 64 * 1024;

    /// <summary>
    /// Why a connection was closed when the far end sent a reliable message past <see cref="MaxReliableMessage"/>
    /// (ours; the reason both ends' <see cref="NetEventKind.Disconnected"/> carries).
    /// </summary>
    public const string TooBig = "too big";

    /// <summary>The bit of a reliable record's length that marks it a piece, more of its message following (ours).</summary>
    const int PieceBit = 0x8000;

    /// <summary>Whether a reliable message of <paramref name="length"/> bytes can be sent (ours).</summary>
    public static bool Fits(int length) => length <= MaxReliableMessage;

    /// <summary>
    /// Queues a reliable message (ours): as it is when it fits one datagram, else in pieces of
    /// <see cref="MaxMessage"/> bytes, each numbered in turn, all but the last marked. Over
    /// <see cref="MaxReliableMessage"/> it is refused, nothing queued.
    /// </summary>
    void QueueReliable(Connection c, byte[] message)
    {
        if (!Fits(message.Length))
            throw new ArgumentException($"a {message.Length}-byte message is over the {MaxReliableMessage}-byte limit of a reliable one");
        if (message.Length <= MaxMessage)
        {
            c.Pending[c.SendNext++] = new Connection.Queued(message);
            return;
        }
        for (int at = 0; at < message.Length; at += MaxMessage)
        {
            int n = Math.Min(MaxMessage, message.Length - at);
            c.Pending[c.SendNext++] = new Connection.Queued(message.AsSpan(at, n).ToArray(), more: at + n < message.Length);
        }
    }

    /// <summary>
    /// A reliable record's turn has come (ours): a whole message is reported, a piece is kept until the last of
    /// its message arrives and the whole is reported then. Pieces past <see cref="MaxReliableMessage"/> close the
    /// connection, the far end told why; false then, and nothing more of the datagram is read.
    /// </summary>
    bool Deliver(Connection c, byte[] data, bool more)
    {
        if (c.Pieces is null && !more)
        {
            events.Add(new NetEvent(NetEventKind.Message, c, data));
            return true;
        }
        if (c.PiecesLength + data.Length > MaxReliableMessage)
        {
            c.Pieces = null;
            c.PiecesLength = 0;
            Refuse(c, TooBig);
            return false;
        }
        (c.Pieces ??= []).Add(data);
        c.PiecesLength += data.Length;
        if (more) return true;
        var whole = new byte[c.PiecesLength];
        int at = 0;
        foreach (var piece in c.Pieces) { piece.CopyTo(whole, at); at += piece.Length; }
        c.Pieces = null;
        c.PiecesLength = 0;
        events.Add(new NetEvent(NetEventKind.Message, c, whole));
        return true;
    }

    /// <summary>
    /// Closes a connection and tells the far end why (ours: the refusal packet the host's handshake has) — sealed once
    /// the keys are made, plain only to a knock.
    /// </summary>
    void Refuse(Connection c, string reason)
    {
        var why = System.Text.Encoding.UTF8.GetBytes(reason);
        var packet = new byte[7 + why.Length];
        Header(packet, Packet.Refuse);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(3), c.Token);
        why.CopyTo(packet, 7);
        for (int i = 0; i < 3; i++)
            if (c.Seal.Ready) Sealed(c, packet); else Raw(c.Remote, packet);
        Close(c, reason, report: true);
    }

    /// <summary>The reliable bytes sent on a connection and not yet acknowledged (ours).</summary>
    internal static int InFlight(Connection c)
    {
        int n = 0;
        foreach (var o in c.Pending.Values)
        {
            if (o.Sends == 0) break;                    // sent in order: the unsent are the tail
            n += o.Data.Length;
        }
        return n;
    }

    /// <summary>Whether a first send of <paramref name="length"/> bytes fits the window, counted in when it does (ours).</summary>
    static bool Room(ref int flying, int length)
    {
        if (flying > 0 && flying + length > Window) return false;
        flying += length;
        return true;
    }
}
