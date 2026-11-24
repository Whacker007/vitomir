// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Asking the router to let the players in: UPnP's Internet Gateway Device, which most home routers
// answer. Asked for ("I'm sold, let's include UPnP") so that hosting in 2026
// is "create game, friends join" as it was in 2000, with a manual forward of UDP 4994 as the
// fallback, and **a plain sentence when the router's own outside address is itself private or
// carrier-grade (100.64/10)** — the one case no mapping can fix (CGNAT, an accepted limitation).
// All of it is ours; 1.1 had WON for this. NAT-PMP and PCP beside it (NatPmp), all asked at once, the console told
// which worked, the outside address and port, and the manual forward and the overlay network when none did; and the
// way a machine gets its peer's address is one seam (IConnector), so hole punching or a relay can come later (an open door).
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace Vitomir.Engine.Net;

public static class PortMapping
{
    /// <summary>What came of asking: whether the port is mapped, the router's outside address, and a line for the host; by which protocol, and the outside port.</summary>
    public sealed record Result(bool Mapped, IPAddress? External, string Note, string Method = "UPnP", int Port = 0);

    const string Search = "urn:schemas-upnp-org:device:InternetGatewayDevice:1";
    static readonly string[] Services = ["urn:schemas-upnp-org:service:WANIPConnection:1",
                                         "urn:schemas-upnp-org:service:WANIPConnection:2",
                                         "urn:schemas-upnp-org:service:WANPPPConnection:1"];

    /// <summary>
    /// **Asks the router, every way it may answer**, on a background task, and reports through <paramref name="log"/>;
    /// never throws. UPnP and NAT-PMP/PCP are asked at once; each says what came of it in a line of its own, then one line
    /// says where players outside join (the protocol that mapped it, the outside address and port), or, when nothing
    /// mapped it, the manual forward and the overlay network (<see cref="NoneMapped"/>). A NAT-PMP or PCP lease is
    /// renewed at half its life while the program runs.
    /// </summary>
    public static Task MapInBackground(int port, Action<string> log) => Task.Run(async () =>
    {
        var timeout = TimeSpan.FromSeconds(3);
        var asked = await Task.WhenAll(Safely("UPnP", port, () => Map(port, timeout)), Safely("NAT-PMP/PCP", port, () => MapPmp(port, timeout)));
        foreach (var r in asked) log($"net: {r.Method}: {r.Note}");
        log("net: " + Summary(port, asked));
        if (asked.FirstOrDefault(r => r.Mapped && r.Method != "UPnP") is { } leased) _ = Renew(port, leased, log);
    });

    static async Task<Result> Safely(string method, int port, Func<Task<Result>> ask)
    {
        try { return await ask(); }
        catch (Exception e) { return new Result(false, null, $"not asked ({e.Message.Split('\n')[0]})", method, port); }
    }

    /// <summary>
    /// The line that says where players outside join: the first protocol that mapped the port, its outside address
    /// and port; or <see cref="NoneMapped"/>. A pure function of the answers, for the tests.
    /// </summary>
    public static string Summary(int port, IReadOnlyList<Result> asked)
    {
        if (asked.FirstOrDefault(r => r.Mapped) is not { } mapped) return NoneMapped(port, asked.Select(r => r.External).FirstOrDefault(a => a is not null));
        int outside = mapped.Port > 0 ? mapped.Port : port;
        string at = mapped.External is { } ip ? $"{ip}:{outside}" : $"this router's outside address, port {outside}";
        string line = $"the router mapped UDP {port} by {mapped.Method}: players outside join at {at}";
        if (mapped.External is { } e && Shared(e))
            line += $"; but {e} is not a public address (carrier-grade NAT): players outside cannot reach it — use an overlay network (Tailscale or ZeroTier)";
        return line;
    }

    /// <summary>
    /// What a host is told when no router mapped its port: forward UDP <paramref name="port"/> to this machine by
    /// hand, or put the players on one overlay network and join by its address — with where the page is.
    /// </summary>
    public static string NoneMapped(int port, IPAddress? external = null) =>
        $"no router mapped UDP {port}{(external is null ? "" : $" (the router's outside address is {external})")}. To be joined from outside: " +
        $"forward UDP {port} on the router to this machine by hand, or have every player join one overlay network (Tailscale or ZeroTier) " +
        "and join by this machine's address there";

    /// <summary>
    /// **NAT-PMP and PCP** (<see cref="NatPmp"/>): the default gateway asked for a mapping in PCP first, and in NAT-PMP
    /// when it answers only that (or does not answer PCP); the outside address asked for in NAT-PMP when PCP did not give it.
    /// </summary>
    public static async Task<Result> MapPmp(int port, TimeSpan timeout)
    {
        var routers = NatPmp.Gateways();
        if (routers.Count == 0) return new Result(false, null, "no default gateway to ask", "NAT-PMP/PCP", port);
        var router = routers[0];
        var local = LocalAddressTowards(router.ToString());
        var nonce = new byte[12];
        System.Security.Cryptography.RandomNumberGenerator.Fill(nonce);
        int pcpResult = -1;
        var pcp = await NatPmp.Ask(router, NatPmp.PcpMap(local, port, nonce), b =>
        {
            var r = NatPmp.ReadPcpMap(b, nonce, out int code);
            if (r is null && code >= 0) { pcpResult = code; return ((IPAddress, int, uint)?)(IPAddress.None, -1, 0u); }
            return r;
        }, timeout);
        if (pcp is { Item2: > 0 } mapped)
            return new Result(true, mapped.Item1, $"the router at {router} mapped UDP {port} to outside port {mapped.Item2} for {mapped.Item3 / 60} minutes (PCP)", "PCP", mapped.Item2);
        var outside = await NatPmp.Ask(router, NatPmp.AddressRequest(), b => NatPmp.ReadAddress(b) is { } a ? (ValueTuple<IPAddress>?)new ValueTuple<IPAddress>(a) : null, timeout);
        IPAddress? external = outside?.Item1;
        int mapResult = -1;
        var map = await NatPmp.Ask(router, NatPmp.MapRequest(port), b =>
        {
            var r = NatPmp.ReadMap(b, out int code);
            if (r is null && code >= 0) { mapResult = code; return ((int, uint)?)(-1, 0u); }
            return r;
        }, timeout);
        if (map is { Item1: > 0 } m)
            return new Result(true, external, $"the router at {router} mapped UDP {port} to outside port {m.Item1} for {m.Item2 / 60} minutes (NAT-PMP)", "NAT-PMP", m.Item1);
        string why = mapResult > 0 ? $"refused the mapping (NAT-PMP result {mapResult})"
                   : pcpResult > 0 && pcpResult != 1 ? $"refused the mapping (PCP result {pcpResult})"
                   : "did not answer NAT-PMP or PCP";
        return new Result(false, external, $"the router at {router} {why}", "NAT-PMP/PCP", port);
    }

    /// <summary>A NAT-PMP or PCP lease renewed at half its life, for as long as the program runs.</summary>
    static async Task Renew(int port, Result leased, Action<string> log)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(NatPmp.Lease / 2));
            var again = await Safely(leased.Method, port, () => MapPmp(port, TimeSpan.FromSeconds(3)));
            if (!again.Mapped) log($"net: {leased.Method}: the lease on UDP {port} was not renewed ({again.Note})");
        }
    }

    /// <summary>
    /// SSDP's search for a gateway, its description fetched, its WAN connection service found, the
    /// outside address asked for and checked, and the UDP port mapped to this machine.
    /// </summary>
    public static async Task<Result> Map(int port, TimeSpan timeout)
    {
        var location = await Discover(timeout);
        if (location is null)
            return new Result(false, null, "no UPnP router answered", "UPnP", port);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = timeout };
        var description = await http.GetStringAsync(location);
        if (ControlUrl(description, location) is not { } control)
            return new Result(false, null, $"the router at {location.Host} offers no port mapping", "UPnP", port);
        var local = LocalAddressTowards(location.Host);
        string? outside = await Soap(http, control.Url, control.Service, "GetExternalIPAddress", "")
            is { } answer ? Field(answer, "NewExternalIPAddress") : null;
        var external = IPAddress.TryParse(outside, out var ip) ? ip : null;
        string args =
            $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{port}</NewExternalPort><NewProtocol>UDP</NewProtocol>" +
            $"<NewInternalPort>{port}</NewInternalPort><NewInternalClient>{local}</NewInternalClient><NewEnabled>1</NewEnabled>" +
            $"<NewPortMappingDescription>Vitomir</NewPortMappingDescription><NewLeaseDuration>0</NewLeaseDuration>";
        bool mapped = await Soap(http, control.Url, control.Service, "AddPortMapping", args) is not null;
        string note = mapped
            ? $"the router mapped UDP {port} to {local}" + (external is null ? "" : $"; players join at {external}:{port}")
            : $"the router refused to map UDP {port}";
        if (external is not null && Shared(external))
            note += $". The router's own outside address, {external}, is not a public one — this connection is behind a " +
                    "second layer of NAT (carrier-grade NAT, CGNAT), and players outside cannot reach it whatever is mapped";
        return new Result(mapped, external, note, "UPnP", port);
    }

    /// <summary>
    /// Whether an address is one the internet cannot reach: RFC 1918's private ranges, RFC 6598's
    /// carrier-grade 100.64/10, and the loopback and link-local ones.
    /// </summary>
    public static bool Shared(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || b[0] == 127
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            || (b[0] == 169 && b[1] == 254);
    }

    /// <summary>SSDP (239.255.255.250:1900): the first gateway's LOCATION header, or null.</summary>
    static async Task<Uri?> Discover(TimeSpan timeout)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        string search = "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\n" +
                        $"MX: 2\r\nST: {Search}\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(search);
        await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900));
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                var got = await udp.ReceiveAsync(cts.Token);
                if (Header(Encoding.ASCII.GetString(got.Buffer), "LOCATION") is { } at && Uri.TryCreate(at, UriKind.Absolute, out var uri))
                    return uri;
            }
        }
        catch (OperationCanceledException) { return null; }
    }

    /// <summary>A header of an HTTP-like message (SSDP's answer), case-insensitively.</summary>
    public static string? Header(string message, string name)
    {
        foreach (var line in message.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return line[(colon + 1)..].Trim();
        }
        return null;
    }

    /// <summary>
    /// The gateway description's WAN connection service: its type and its control URL, made
    /// absolute against the description's own address (or its URLBase). Null when there is none.
    /// </summary>
    public static (string Service, Uri Url)? ControlUrl(string descriptionXml, Uri location)
    {
        XDocument doc;
        try { doc = XDocument.Parse(descriptionXml); }
        catch (System.Xml.XmlException) { return null; }
        var root = doc.Root;
        if (root is null) return null;
        XNamespace ns = root.Name.Namespace;
        var baseUri = root.Element(ns + "URLBase")?.Value is { Length: > 0 } b && Uri.TryCreate(b, UriKind.Absolute, out var bu) ? bu : location;
        foreach (var service in root.Descendants(ns + "service"))
        {
            string type = service.Element(ns + "serviceType")?.Value ?? "";
            if (!Services.Contains(type)) continue;
            string control = service.Element(ns + "controlURL")?.Value ?? "";
            if (Uri.TryCreate(baseUri, control, out var url)) return (type, url);
        }
        return null;
    }

    /// <summary>One SOAP action; the answer's body, or null for a fault or no answer.</summary>
    static async Task<string?> Soap(HttpClient http, Uri control, string service, string action, string args)
    {
        string body = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
                      "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>" +
                      $"<u:{action} xmlns:u=\"{service}\">{args}</u:{action}></s:Body></s:Envelope>";
        using var request = new HttpRequestMessage(HttpMethod.Post, control)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/xml"),
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{service}#{action}\"");
        try
        {
            using var response = await http.SendAsync(request);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    static string? Field(string xml, string name)
    {
        int at = xml.IndexOf("<" + name + ">", StringComparison.Ordinal);
        if (at < 0) return null;
        at += name.Length + 2;
        int end = xml.IndexOf("</" + name + ">", at, StringComparison.Ordinal);
        return end < 0 ? null : xml[at..end];
    }

    /// <summary>This machine's address on the way to the router: the one a socket aimed at it picks.</summary>
    static IPAddress LocalAddressTowards(string host)
    {
        using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        s.Connect(host, 1900);
        return ((IPEndPoint)s.LocalEndPoint!).Address;
    }
}
