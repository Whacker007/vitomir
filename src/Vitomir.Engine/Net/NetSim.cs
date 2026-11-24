// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// **The internet on one PC** (ours; his friends test from their homes, and every
// multiplayer test runs on his one PC). A network-conditions simulator inside the transport: every datagram this
// end sends and every one it reads is held back by the latency, scattered by the jitter, now and then lost, sent twice or
// overtaken, and squeezed through a bandwidth, both ways, from a seeded generator so a test sees the same internet each
// run. Behind `--developer` under the platform (`--net-sim latency=80,jitter=20,loss=2,reorder=1,dup=0.5,kbps=1000`), and
// `VITOMIR_NET_SIM` for a test run (every network test once more under it). Nothing of the game knows it is there.
using System.Globalization;
using System.Net;

namespace Vitomir.Engine.Net;

/// <summary>
/// **The network-conditions simulator** (ours). One per
/// transport (<see cref="NetPeer.Sim"/>), keeping its own real-time clock (a test's or a tape's clock is not the wire's):
/// <see cref="Out"/> takes a datagram this end sends and <see cref="In"/> one it read, and <see cref="Due"/> gives back each
/// when its time has come. <b>The numbers are what one end adds to a round trip</b>: <c>latency</c> is split half to each
/// way, so one end simulating 100 shows a round trip about 100 ms longer (both ends simulating, 200); <c>jitter</c> is the
/// spread of that round trip (each way a uniform ±a quarter of it); <c>loss</c>, <c>reorder</c> and <c>dup</c> are the
/// chance in a hundred of a datagram, each way, being lost, held back long enough to be overtaken, or delivered twice;
/// <c>kbps</c> each way's bandwidth (0: none), a datagram leaving only when the one before it has gone.
/// </summary>
public sealed class NetSim
{
    /// <summary>What the simulator does, read from <c>--net-sim</c>'s text (<see cref="Parse"/>).</summary>
    /// <param name="FrameMs">
    /// 0: the simulator keeps the wall clock's time (a game played by hand). More (<c>frame=33</c>, the tests' runs under
    /// <c>VITOMIR_NET_SIM</c>): its time is the transport's turns, each <c>Poll</c> that many milliseconds, so a test that turns its
    /// world faster than the wall clock (a frame a loop, no waiting) meets the latency in frames as a game would.
    /// </param>
    public sealed record Spec(int LatencyMs = 0, int JitterMs = 0, double Loss = 0, double Reorder = 0, double Dup = 0, int Kbps = 0, int Seed = 1, int FrameMs = 0)
    {
        /// <summary>The spec as <c>--net-sim</c> writes it, for the console's line.</summary>
        public override string ToString() => string.Create(CultureInfo.InvariantCulture,
            $"latency={LatencyMs},jitter={JitterMs},loss={Loss},reorder={Reorder},dup={Dup},kbps={Kbps},seed={Seed}{(FrameMs > 0 ? $",frame={FrameMs}" : "")}");
    }

    /// <summary>
    /// The spec every transport made from now on takes (<c>--net-sim</c>, set by the program's start; else
    /// <c>VITOMIR_NET_SIM</c>, read once): null for the plain wire.
    /// </summary>
    public static Spec? Default
    {
        get
        {
            if (!fromEnvironment)
            {
                fromEnvironment = true;
                if (Environment.GetEnvironmentVariable("VITOMIR_NET_SIM") is { Length: > 0 } text) configured ??= Parse(text, out _);
            }
            return configured;
        }
        set { configured = value; fromEnvironment = true; }
    }
    static Spec? configured;
    static bool fromEnvironment;
    static int made;

    /// <summary>
    /// <c>--net-sim</c>'s value on a command line, taken: the spec in force from now, and the console's line saying so;
    /// a value that does not read is said and nothing simulated. No switch: nothing changes (the environment's still holds).
    /// </summary>
    public static string? Configure(string? text)
    {
        if (text is null) return Default is { } env ? $"net-sim: {env} (VITOMIR_NET_SIM)" : null;
        if (Parse(text, out string? error) is not { } spec) return $"net-sim: {error}; the wire is not simulated";
        Default = spec;
        return $"net-sim: {spec}: every datagram sent and read is held back, scattered, lost, doubled or overtaken so";
    }

    /// <summary>
    /// Reads <c>latency=80,jitter=20,loss=2,reorder=1,dup=0.5,kbps=1000[,seed=7]</c> — any of them, in any order (the rest
    /// 0; the seed 1). Null, with the reason, for a word it does not know or a number out of range.
    /// </summary>
    public static Spec? Parse(string text, out string? error)
    {
        error = null;
        var spec = new Spec();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            string key = eq > 0 ? part[..eq].Trim().ToLowerInvariant() : part.ToLowerInvariant();
            string value = eq > 0 ? part[(eq + 1)..].Trim() : "";
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0 || double.IsNaN(v))
            {
                error = $"--net-sim's {part} wants a number of 0 or more";
                return null;
            }
            switch (key)
            {
                case "latency" when v <= 10_000: spec = spec with { LatencyMs = (int)v }; break;
                case "jitter" when v <= 10_000: spec = spec with { JitterMs = (int)v }; break;
                case "loss" when v <= 100: spec = spec with { Loss = v }; break;
                case "reorder" when v <= 100: spec = spec with { Reorder = v }; break;
                case "dup" when v <= 100: spec = spec with { Dup = v }; break;
                case "kbps" when v <= 10_000_000: spec = spec with { Kbps = (int)v }; break;
                case "seed" when v <= int.MaxValue: spec = spec with { Seed = (int)v }; break;
                case "frame" when v <= 1000: spec = spec with { FrameMs = (int)v }; break;
                default:
                    error = $"--net-sim does not know {part} (latency, jitter, loss, reorder, dup, kbps, seed and frame, each =number)";
                    return null;
            }
        }
        return spec;
    }

    /// <summary>A simulator for one transport, from the spec in force (<see cref="Default"/>), or null when there is none.</summary>
    public static NetSim? ForNewPeer() => Default is { } spec ? new NetSim(spec, spec.Seed + Interlocked.Increment(ref made) - 1) : null;

    readonly Random random;
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    readonly PriorityQueue<Item, (long Due, long Order)> outgoing = new(), incoming = new();
    long order, outFree, inFree;

    public NetSim(Spec spec, int seed)
    {
        Settings = spec;
        random = new Random(seed);
    }

    /// <summary>What it does.</summary>
    public Spec Settings { get; }

    /// <summary>The datagrams it has lost, sent twice and held back to be overtaken, both ways (the trace's counts).</summary>
    public long Lost { get; private set; }
    /// <inheritdoc cref="Lost"/>
    public long Doubled { get; private set; }
    /// <inheritdoc cref="Lost"/>
    public long Overtaken { get; private set; }

    /// <summary>Its own clock, in milliseconds: the wall clock's, or its turns' (<see cref="Spec.FrameMs"/>).</summary>
    public long Now => Settings.FrameMs > 0 ? Interlocked.Read(ref turned) : clock.ElapsedMilliseconds;

    long turned;

    /// <summary>A turn of the transport (each <c>Poll</c>): with <see cref="Spec.FrameMs"/>, its clock moves on that much.</summary>
    public void Turn()
    {
        if (Settings.FrameMs > 0) Interlocked.Add(ref turned, Settings.FrameMs);
    }

    /// <summary>How many are held, both ways (the most it holds is <see cref="MaxHeld"/>, past which it loses them).</summary>
    public int Held => outgoing.Count + incoming.Count;

    /// <summary>The most datagrams held at once each way: past it one is lost, as a router's full queue loses it.</summary>
    public const int MaxHeld = 4096;

    readonly record struct Item(byte[] Data, IPEndPoint Peer);

    /// <summary>A datagram this end sends to <paramref name="to"/>, held (or lost) for its way out.</summary>
    public void Out(ReadOnlySpan<byte> data, IPEndPoint to) => Take(outgoing, ref outFree, data, to);

    /// <summary>A datagram this end read from <paramref name="from"/>, held (or lost) for its way in.</summary>
    public void In(ReadOnlySpan<byte> data, IPEndPoint from) => Take(incoming, ref inFree, data, from);

    void Take(PriorityQueue<Item, (long, long)> queue, ref long free, ReadOnlySpan<byte> data, IPEndPoint peer)
    {
        long now = Now;
        if (queue.Count >= MaxHeld || Chance(Settings.Loss)) { Lost++; return; }
        // The bandwidth: a datagram leaves when the one before it has gone (its bits at kbps), and then travels.
        long leaves = now;
        if (Settings.Kbps > 0)
        {
            leaves = Math.Max(now, free) + Math.Max(1, data.Length * 8L / Settings.Kbps);
            free = leaves;
        }
        long due = leaves + Way();
        if (Chance(Settings.Reorder)) { due += Settings.LatencyMs / 2 + 30; Overtaken++; }
        var held = new Item(data.ToArray(), peer);
        queue.Enqueue(held, (due, order++));
        if (Chance(Settings.Dup)) { queue.Enqueue(held, (due + 1 + random.Next(10), order++)); Doubled++; }
    }

    /// <summary>One way's delay: half the latency, and a uniform ±a quarter of the jitter.</summary>
    int Way()
    {
        double spread = Settings.JitterMs / 4.0;
        return Math.Max(0, (int)Math.Round(Settings.LatencyMs / 2.0 + (random.NextDouble() * 2 - 1) * spread));
    }

    bool Chance(double percent) => percent > 0 && random.NextDouble() * 100 < percent;

    /// <summary>The next datagram whose time has come on its way out, or false.</summary>
    public bool DueOut(out byte[] data, out IPEndPoint to) => Due(outgoing, out data, out to);

    /// <summary>The next datagram whose time has come on its way in, or false.</summary>
    public bool DueIn(out byte[] data, out IPEndPoint from) => Due(incoming, out data, out from);

    /// <summary>The next datagram on its way out whatever its time (a transport closing sends what is held at once).</summary>
    public bool DueOutNow(out byte[] data, out IPEndPoint to) => Due(outgoing, out data, out to, now: long.MaxValue);

    bool Due(PriorityQueue<Item, (long Due, long Order)> queue, out byte[] data, out IPEndPoint peer, long? now = null)
    {
        if (queue.TryPeek(out var held, out var when) && when.Due <= (now ?? Now))
        {
            queue.Dequeue();
            (data, peer) = (held.Data, held.Peer);
            return true;
        }
        (data, peer) = ([], null!);
        return false;
    }
}
