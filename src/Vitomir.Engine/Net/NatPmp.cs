// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// **NAT-PMP and PCP beside UPnP** (ours; "UPnP/NAT-PMP on the host"). Apple's NAT Port
// Mapping Protocol (RFC 6886) and its successor, the Port Control Protocol (RFC 6887), both a few datagrams to the
// router's UDP 5351: many routers that do not answer UPnP answer one of these (Apple's, and most that run miniupnpd).
// The packets are built and read by pure functions here (tested on every machine); the asking is PortMapping's.
using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Vitomir.Engine.Net;

public static class NatPmp
{
    /// <summary>The router's port for both protocols.</summary>
    public const int Port = 5351;
    /// <summary>The lease asked for: two hours, renewed at half (RFC 6886's recommendation).</summary>
    public const uint Lease = 7200;

    // ---- NAT-PMP (RFC 6886) ----

    /// <summary>"What is your outside address?": version 0, opcode 0.</summary>
    public static byte[] AddressRequest() => [0, 0];

    /// <summary>"Map this UDP port": version 0, opcode 1, the internal port, the external one wished for, the lease.</summary>
    public static byte[] MapRequest(int port, uint lease = Lease)
    {
        var p = new byte[12];
        p[1] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(4), (ushort)port);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(6), (ushort)port);
        BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(8), lease);
        return p;
    }

    /// <summary>The outside address in an answer to <see cref="AddressRequest"/>, or null (not one, or a result not 0).</summary>
    public static IPAddress? ReadAddress(ReadOnlySpan<byte> answer)
    {
        if (answer.Length < 12 || answer[0] != 0 || answer[1] != 128 || BinaryPrimitives.ReadUInt16BigEndian(answer[2..]) != 0) return null;
        return new IPAddress(answer.Slice(8, 4));
    }

    /// <summary>The external port and lease in an answer to <see cref="MapRequest"/>, or null (not one, or a result not 0 — the code then in <paramref name="result"/>).</summary>
    public static (int External, uint Lease)? ReadMap(ReadOnlySpan<byte> answer, out int result)
    {
        result = -1;
        if (answer.Length < 16 || answer[0] != 0 || answer[1] != 129) return null;
        result = BinaryPrimitives.ReadUInt16BigEndian(answer[2..]);
        if (result != 0) return null;
        return (BinaryPrimitives.ReadUInt16BigEndian(answer[10..]), BinaryPrimitives.ReadUInt32BigEndian(answer[12..]));
    }

    // ---- PCP (RFC 6887) ----

    /// <summary>
    /// PCP's MAP request (version 2, opcode 1): the lease, this machine's address (IPv4 as ::ffff:a.b.c.d), then the MAP
    /// opcode's body — a 12-byte nonce, UDP (17), the internal port, the external port wished for, and no external address
    /// wished for (all zero, ::ffff:0.0.0.0 for IPv4).
    /// </summary>
    public static byte[] PcpMap(IPAddress local, int port, ReadOnlySpan<byte> nonce, uint lease = Lease)
    {
        var p = new byte[60];
        p[0] = 2;
        p[1] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(4), lease);
        Mapped(local).CopyTo(p, 8);
        nonce[..12].CopyTo(p.AsSpan(24));
        p[36] = 17;
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(40), (ushort)port);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(42), (ushort)port);
        p[54] = 0xff;
        p[55] = 0xff;
        return p;
    }

    /// <summary>
    /// PCP's answer to <see cref="PcpMap"/>: the external address and port and the lease, or null — not an answer, another
    /// nonce, or a result not 0 (in <paramref name="result"/>; 1, UNSUPP_VERSION, is a NAT-PMP router's word for "ask me
    /// in NAT-PMP").
    /// </summary>
    public static (IPAddress External, int Port, uint Lease)? ReadPcpMap(ReadOnlySpan<byte> answer, ReadOnlySpan<byte> nonce, out int result)
    {
        result = -1;
        if (answer.Length < 2) return null;
        // A NAT-PMP router answers a version it does not know with its own version 0 and result 1 (RFC 6887 §9).
        if (answer[0] == 0 && answer.Length >= 4) { result = BinaryPrimitives.ReadUInt16BigEndian(answer[2..]); return null; }
        if (answer.Length < 60 || answer[0] != 2 || answer[1] != 0x81) return null;
        result = answer[3];
        if (result != 0 || !answer.Slice(24, 12).SequenceEqual(nonce[..12])) return null;
        uint lease = BinaryPrimitives.ReadUInt32BigEndian(answer[4..]);
        int external = BinaryPrimitives.ReadUInt16BigEndian(answer[42..]);
        var address = new IPAddress(answer.Slice(44, 16));
        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address, external, lease);
    }

    static byte[] Mapped(IPAddress a) =>
        (a.AddressFamily == AddressFamily.InterNetwork ? a.MapToIPv6() : a).GetAddressBytes();

    // ---- asking ----

    /// <summary>The machine's default IPv4 gateways (the routers to ask), the first interface's first.</summary>
    public static IReadOnlyList<IPAddress> Gateways()
    {
        var found = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var g in nic.GetIPProperties().GatewayAddresses)
                    if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any) && !found.Contains(g.Address))
                        found.Add(g.Address);
            }
        }
        catch (NetworkInformationException) { }
        catch (PlatformNotSupportedException) { }
        return found;
    }

    /// <summary>
    /// One question to <paramref name="router"/>'s 5351, sent again at 250 ms, 500 ms, 1 s (RFC 6886's backoff, cut short):
    /// the first answer <paramref name="take"/> takes, or null when none came within <paramref name="timeout"/>.
    /// </summary>
    public static async Task<T?> Ask<T>(IPAddress router, byte[] question, Func<byte[], T?> take, TimeSpan timeout) where T : struct
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var to = new IPEndPoint(router, Port);
        using var cts = new CancellationTokenSource(timeout);
        int wait = 250;
        try
        {
            while (!cts.IsCancellationRequested)
            {
                await udp.SendAsync(question, question.Length, to);
                using var round = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                round.CancelAfter(wait);
                try
                {
                    while (true)
                    {
                        var got = await udp.ReceiveAsync(round.Token);
                        if (!got.RemoteEndPoint.Address.Equals(router)) continue;
                        if (take(got.Buffer) is { } answer) return answer;
                    }
                }
                catch (OperationCanceledException) { }
                wait = Math.Min(wait * 2, 1000);
            }
        }
        catch (SocketException) { }
        return null;
    }
}
