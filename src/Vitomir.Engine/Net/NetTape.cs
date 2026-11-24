// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The replay harness's network half: ours.
using System.Net;
using System.Net.Sockets;

namespace Vitomir.Engine.Net;

/// <summary>
/// **What a network game reads from outside, where the replay harness can reach it** (ours;
/// <c>--record</c>, <c>--replay</c> and <c>--replay-check</c>). A host's world depends on its players' datagrams, which arrive by
/// the wall clock, and the transport keeps time by its own (<c>Environment.TickCount64</c>: resends, heartbeats,
/// timeouts) and makes its connection tokens from an unseeded generator. With a tape in (<see cref="Tape"/>,
/// installed by the program's <c>InputTape</c>) each of the three goes through it: a recording writes each
/// datagram read, each clock reading and the tokens' seed; a replay reads none of them from the socket or the
/// clock but from the tape — so a host, or a player, replays alone, without the other machine. With no tape
/// (a test, a run not recorded) each is the machine's own, as before.
/// </summary>
public static class NetTape
{
    /// <summary>The tape in force, or null.</summary>
    public static INetTape? Tape { get; set; }

    /// <summary>The transport's clock reading, in milliseconds: <paramref name="live"/>, or the tape's.</summary>
    public static long Clock(long live) => Tape?.Clock(live) ?? live;

    /// <summary>The seed of a transport's own generator (its connection tokens), when it was given none: the tape's, or a fresh one.</summary>
    public static Random Generator() => Tape is { } t ? new Random(t.Seed(Random.Shared.Next(1, int.MaxValue))) : new Random();

    /// <summary>
    /// Whether a join's password is the wrong one: <paramref name="live"/>, or the tape's. A tape never holds the game's
    /// password (its <c>--password</c> is written as <c>****</c>), so a replay cannot make the check itself and takes the
    /// recording's answer; asked only of a game with a password, so no other tape has the line.
    /// </summary>
    public static bool WrongPassword(bool live) => Tape?.WrongPassword(live) ?? live;

    /// <summary>
    /// A join's build and data check (<see cref="NetIdentity"/>): what differs, or null. A recording writes the host's answer
    /// and a replay takes it: the build that replays a tape is another build than the one that recorded it, and its install may be
    /// another store's, so the check itself cannot be made again.
    /// </summary>
    public static string? Differs(string? live) => Tape is { } tape ? tape.Differs(live) : live;
}

/// <summary>The replay harness's side of <see cref="NetTape"/> (the program's <c>InputTape</c>).</summary>
public interface INetTape
{
    /// <summary>A clock reading: the live one written, or the recorded one.</summary>
    long Clock(long live);

    /// <summary>A generator's seed: the live one written, or the recorded one.</summary>
    int Seed(int live);

    /// <summary>
    /// The next datagram for the transport, or false when none is waiting: read and written, or the recorded one (the
    /// socket's own drained and dropped). <paramref name="read"/> is the transport's own read of one datagram **as it is
    /// opened** (a sealed datagram's plain packet, so a tape holds the messages and never a key; false when nothing
    /// waits), and <paramref name="taped"/> what of it the tape writes (the password's proof masked).
    /// </summary>
    bool Receive(Socket socket, byte[] buffer, ReceiveRead read, Func<byte[], int, byte[]> taped, out int length, out EndPoint from);

    /// <summary>A join's password check in a game with a password: the live answer written, or the recorded one.</summary>
    bool WrongPassword(bool live);

    /// <summary>A join's build and data check: the live answer written (null: the same), or the recorded one.</summary>
    string? Differs(string? live) => live;

    /// <summary>
    /// Whether this tape is being replayed: the transport's datagrams are the tape's, not the socket's, which it drops. A recording
    /// answers false: it reads the socket as a game does and writes what it read.
    /// </summary>
    bool Replaying => false;
}

/// <summary>The transport's own read of one datagram (false when none waits).</summary>
public delegate bool ReceiveRead(byte[] buffer, out int length, out EndPoint from);
