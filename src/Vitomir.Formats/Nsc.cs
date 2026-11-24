// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The chronicle file: the campaign's locations and the exits between them.
//
// A .nsc is plain text under Chronicles\ — loose files, in no archive. The game parses it so: a line whose first character is '#' or ';' is a comment, otherwise the first word is a
// directive (split on " \r\t\n") and its arguments follow (split on " \r\t\n," — so `OldTown,3` is
// two tokens, and a stray `Haven, 0` parses the same as `Haven 0`). Anything else is ignored.
// Seven directives exist; the SDK documents all of them (NodSDK\Docs\nsc.htm).
namespace Vitomir.Formats;

/// <summary>One way out of a location, by its number in the level.</summary>
public sealed class NscExit
{
    /// <summary>The location this exit leads to, or −1 when there is none.</summary>
    public int To { get; set; } = -1;
    /// <summary>Which entrance of that location you arrive at.</summary>
    public int Entrance { get; set; }
    /// <summary>Whether the exit is shut. Scripts open and close it as the story moves.</summary>
    public bool Closed { get; set; }
}

/// <summary>A place in the chronicle: a level, the scene it starts in, and where it sits on the map.</summary>
public sealed class NscLocation
{
    public string Name { get; init; } = "";
    /// <summary>The level file, as written (<c>P1_CNVT.nil</c>).</summary>
    public string Level { get; set; } = "";
    /// <summary>
    /// The scene the location starts in, or <c>null</c> — literally, for a location that exists only
    /// to be locked. `CodexSequence.ChangeScene` rewrites it while the game runs, which is
    /// how a chapter moves a place on.
    /// </summary>
    public string Scene { get; set; } = "";
    public string MapFile { get; init; } = "";
    public string MapMarker { get; init; } = "";
    /// <summary>Display and behaviour bits; see <see cref="NscLocationFlags"/>. Scripts change these at run time.</summary>
    public uint Flags { get; set; }
    /// <summary>One past the highest exit number any line used, which is what the original counts.</summary>
    public int ExitCount { get; set; }
    public NscExit[] Exits { get; } = [.. Enumerable.Range(0, MaxExits).Select(_ => new NscExit())];

    /// <summary>The level's base name, without the extension (<c>P1_CNVT</c>).</summary>
    public string LevelName => Strip(Level);
    /// <summary>The starting scene's base name, or "" when the location has none.</summary>
    public string SceneName =>
        Scene.Equals("null", StringComparison.OrdinalIgnoreCase) ? "" : Strip(Scene);

    static string Strip(string s)
    {
        int dot = s.LastIndexOf('.');
        return dot < 0 ? s : s[..dot];
    }

    public const int MaxExits = 32;
}

/// <summary>What a location's flags mean (NodSDK\Docs\nsc.htm).</summary>
[Flags]
public enum NscLocationFlags : uint
{
    None = 0,
    /// <summary>Objects are not saved in this location.</summary>
    NoSaveObjects = 0x1,
    /// <summary>Normally loaded only briefly, so the origin world is kept.</summary>
    KeepOriginWorld = 0x2,
    /// <summary>Should not be freed.</summary>
    NeverFree = 0x4,
    /// <summary>Shown on the map.</summary>
    ShowOnMap = 0x8,
    /// <summary>A haven, marked as one on the map.</summary>
    Haven = 0x10,
    /// <summary>Teleports cannot be created here.</summary>
    NoTeleports = 0x20,
    /// <summary>Clearing the buffer is required.</summary>
    ClearBuffer = 0x40,
}

/// <summary>What a chronicle's own flags mean (NodSDK\Docs\nsc.htm).</summary>
[Flags]
public enum NscChronicleFlags : uint
{
    None = 0,
    /// <summary>Modern day, which changes the UI.</summary>
    ModernDay = 0x1,
    Multiplayer = 0x2,
    /// <summary>Meant to be played with a Storyteller.</summary>
    Storyteller = 0x4,
    /// <summary>Only chained from another chronicle; never offered in a selection UI.</summary>
    Chained = 0x8,
    /// <summary>Undocumented, and set only by Prague.nsc — the one chronicle the campaign starts in.</summary>
    CampaignStart = 0x10000,
}

/// <summary>Flags on an <c>exit</c> line.</summary>
[Flags]
public enum NscExitFlags : uint
{
    None = 0,
    Closed = 0x1,
    /// <summary>Load time only: suppresses the backward link the loader would otherwise create.</summary>
    OneWay = 0x100,
}

/// <summary>A parsed chronicle file. Mutable where the game mutates it: location flags and closed exits.</summary>
public sealed class NscChronicle
{
    /// <summary>The chronicle's name, which is the file name without its extension.</summary>
    public string Name { get; init; } = "";
    public uint Flags { get; set; }
    public string Metafile { get; set; } = "";
    public List<NscLocation> Locations { get; } = [];
    /// <summary>The chronicle script classes, at most eight. Every shipped chronicle names one.</summary>
    public List<string> Scripts { get; } = [];
    /// <summary>Where Walk the Abyss takes the player, and by which entrance. −1 when unset.</summary>
    public int PortalLocation { get; private set; } = -1;
    public int PortalEntrance { get; private set; }
    /// <summary>Where a multiplayer character revives, and where joining clients start. −1 when unset.</summary>
    public int ReviveLocation { get; private set; } = -1;
    public int ReviveEntrance { get; private set; }
    /// <summary>Lines the parser did not understand, kept so a viewer can report them.</summary>
    public List<string> Ignored { get; } = [];

    public NscChronicleFlags Kind => (NscChronicleFlags)Flags;

    /// <summary>The first location listed, which is where the chronicle starts (SDK).</summary>
    public NscLocation? Start => Locations.Count > 0 ? Locations[0] : null;

    /// <summary>A location by name, case-insensitively, or −1.</summary>
    public int Find(string name)
    {
        for (int i = 0; i < Locations.Count; i++)
            if (string.Equals(Locations[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    public NscLocation? At(int index) => index >= 0 && index < Locations.Count ? Locations[index] : null;
    public NscLocation? At(string name) => At(Find(name));

    public const int MaxLocations = 128;
    public const int MaxScripts = 8;

    public static NscChronicle Read(ReadOnlySpan<byte> data, string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var text = System.Text.Encoding.Latin1.GetString(data);
        string metafile = "";
        var locations = new List<NscLocation>();
        var scripts = new List<string>();
        var ignored = new List<string>();
        uint flags = 0;

        // Two passes are not how the original reads it, but they are equivalent: the original
        // resolves an exit's location names against the locations it has read so far, and every
        // shipped file lists all of its locations before any exit. Reading locations first and
        // then everything else gives the same result and forgives a file that does not.
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            var t = new Strtok(line);
            if (t.Next(Keyword) is not { } word) continue;
            if (!word.Equals("location", StringComparison.OrdinalIgnoreCase)) continue;
            if (locations.Count >= MaxLocations) continue;             // the original drops the rest silently
            if (t.Next(Arg) is not { } locName) continue;
            if (t.Next(Arg) is not { } level) continue;
            if (t.Next(Arg) is not { } scene) continue;
            if (t.Next(Arg) is not { } nui) continue;
            if (t.Next(Arg) is not { } marker) continue;
            locations.Add(new NscLocation
            {
                Name = Clip(locName, 0x80), Level = Clip(level, 0x20), Scene = Clip(scene, 0x20),
                MapFile = Clip(nui, 0x20), MapMarker = Clip(marker, 0x20),
                // The flags argument is optional: the original counts the location before it reads
                // one, so a line that stops after the map marker is still a location, with flags 0.
                Flags = t.Next(Arg) is { } f ? Hex(f) : 0,
            });
        }

        var chronicle = new NscChronicle { Name = name };
        chronicle.Locations.AddRange(locations);

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            var t = new Strtok(line);
            if (t.Next(Keyword) is not { } word) continue;

            switch (word.ToLowerInvariant())
            {
                case "location": break;                                // done above
                case "exit": Exit(chronicle, t, ignored, line); break;
                case "script":
                    if (t.Next(Arg) is { } cls && scripts.Count < MaxScripts) scripts.Add(cls);
                    break;
                case "metafile":
                    if (t.Next(Arg) is { } mf) metafile = Clip(mf, 0x20);
                    break;
                case "portal":
                    if (t.Next(Arg) is { } pl && chronicle.Find(pl) is var pi && pi >= 0)
                    {
                        chronicle.PortalLocation = pi;
                        chronicle.PortalEntrance = t.Next(Arg) is { } pe ? Dec(pe) : 0;
                    }
                    break;
                case "reviveloc":
                    if (t.Next(Arg) is { } rl && chronicle.Find(rl) is var ri && ri >= 0)
                    {
                        chronicle.ReviveLocation = ri;
                        chronicle.ReviveEntrance = t.Next(Arg) is { } re ? Dec(re) : 0;
                    }
                    break;
                case "flags":
                    if (t.Next(Arg) is { } cf) flags = Hex(cf);
                    break;
                default: ignored.Add(line.Trim()); break;
            }
        }

        chronicle.Flags = flags;
        chronicle.Metafile = metafile;
        chronicle.Scripts.AddRange(scripts);
        chronicle.Ignored.AddRange(ignored);
        // The original's last pass: an exit pointing past the end of the location list is voided.
        foreach (var loc in chronicle.Locations)
            for (int i = 0; i < loc.ExitCount && i < NscLocation.MaxExits; i++)
                if (loc.Exits[i].To >= chronicle.Locations.Count) loc.Exits[i].To = -1;

        return chronicle;
    }

    /// <summary>
    /// One <c>exit</c> line. The loader writes the forward link and, unless the line says ONEWAY,
    /// the backward one too — which is why a chronicle lists each connection once. A location's
    /// exit count is one past the highest number either direction used.
    /// </summary>
    static void Exit(NscChronicle c, Strtok t, List<string> ignored, string line)
    {
        if (t.Next(Arg) is not { } fromName) return;
        int from = c.Find(fromName);
        if (from < 0) { ignored.Add(line.Trim()); return; }
        if (t.Next(Arg) is not { } fromNumText) return;
        int fromExit = Dec(fromNumText);
        if (fromExit < 0 || fromExit >= NscLocation.MaxExits) { ignored.Add(line.Trim()); return; }
        if (t.Next(Arg) is not { } toName) return;
        int to = c.Find(toName);
        if (to < 0) { ignored.Add(line.Trim()); return; }
        if (t.Next(Arg) is not { } toNumText) return;
        int entrance = Dec(toNumText);
        // The original does not bound-check this one and would write past the record. We refuse it:
        // a memory bug is a code bug, and no shipped chronicle comes near the limit.
        if (entrance < 0 || entrance >= NscLocation.MaxExits) { ignored.Add(line.Trim()); return; }

        uint flags = t.Next(Arg) is { } f ? Hex(f) : 0;
        var a = c.Locations[from];
        var b = c.Locations[to];

        a.Exits[fromExit].To = to;
        a.Exits[fromExit].Entrance = entrance;
        a.ExitCount = Math.Max(a.ExitCount, fromExit + 1);
        b.ExitCount = Math.Max(b.ExitCount, entrance + 1);
        if ((flags & (uint)NscExitFlags.Closed) != 0) a.Exits[fromExit].Closed = true;

        if ((flags & (uint)NscExitFlags.OneWay) == 0)
        {
            b.Exits[entrance].To = from;
            b.Exits[entrance].Entrance = fromExit;
            if ((flags & (uint)NscExitFlags.Closed) != 0) b.Exits[entrance].Closed = true;
        }
    }

    /// <summary>The delimiters the original's two strtok calls use: no comma for the directive, comma for its arguments.</summary>
    const string Keyword = " \r\t\n";
    const string Arg = " \r\t\n,";

    static string Clip(string s, int max) => s.Length <= max - 1 ? s : s[..(max - 1)];

    /// <summary>C's <c>%x</c>: optional 0x, then hex digits, and 0 when there are none.</summary>
    internal static uint Hex(string s)
    {
        int i = 0;
        if (s.Length > 1 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X')) i = 2;
        uint v = 0; bool any = false;
        for (; i < s.Length; i++)
        {
            int d = Uri.IsHexDigit(s[i]) ? Uri.FromHex(s[i]) : -1;
            if (d < 0) break;
            v = v * 16 + (uint)d; any = true;
        }
        return any ? v : 0;
    }

    /// <summary>C's <c>atoi</c>: leading sign, decimal digits, and 0 when there are none.</summary>
    internal static int Dec(string s)
    {
        int i = 0, sign = 1;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) { sign = s[i] == '-' ? -1 : 1; i++; }
        long v = 0;
        for (; i < s.Length && char.IsAsciiDigit(s[i]); i++) v = Math.Min(v * 10 + (s[i] - '0'), int.MaxValue);
        return (int)(sign * v);
    }

    /// <summary>C's <c>strtok</c> over one line, so the file splits exactly as the original splits it.</summary>
    struct Strtok(string s)
    {
        int i = 0;

        public string? Next(string delimiters)
        {
            while (i < s.Length && delimiters.Contains(s[i], StringComparison.Ordinal)) i++;
            if (i >= s.Length) return null;
            int start = i;
            while (i < s.Length && !delimiters.Contains(s[i], StringComparison.Ordinal)) i++;
            return s[start..i];
        }
    }
}
