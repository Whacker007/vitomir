// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Streamer mode: one setting of the platform's for every game and module, which hides
// from the screen what a stream should not show. Ours: 1.1 has nothing like it.
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vitomir.Platform.Presentation;

/// <summary>
/// **Streamer mode** (in this day and age of streaming, the game needs
/// streamer-friendly settings). One setting of the platform's, the same for
/// every game and module, **off by default**, kept in the platform's user data (<see cref="FileName"/>, one file for all games:
/// a player who streams streams every game), shown in each game's own options (the Classic game's: our own Fixes page, since
/// 1.1's Options has no room) and given for one run by the players' switch <c>--streamer</c> (<see cref="Switch"/>, declared
/// in each game's manifest, no <c>--developer</c>; not kept). **No hot key**: one pressed by mistake on a stream would show what
/// it hides.
///
/// When it is on, wherever text is put on the screen (every text box, list and edit of a game's menus, its message and chat
/// boxes, the developer overlay, a console window, the log window: it is streamable) it goes through <see cref="Shown"/>, which
/// hides **every network address and its port** (IPv4, IPv6, an IPv4 in IPv6, each with its port when it has one) as
/// <see cref="Hidden"/>, **the user's own folder** in any path (<c>C:\Users\&lt;name&gt;</c> as <c>%USERPROFILE%</c>), and every
/// text a game has said is secret (<see cref="Hide"/>: a host's name as it was typed into the join screen's address box, a join code a
/// later way in brings). An address box draws a dot for each character typed (<see cref="Dots"/>). A port by itself (the
/// console's "on UDP 4994") is not hidden: without the address it names nothing.
///
/// **Not masked**: the log files and the log bundle (they are the player's own to send; the players' page says so), and
/// a tape. Passwords are starred always, whether this is on or off (1.1's own password boxes), and a <c>--password</c>'s value
/// is never written in a log (<c>SecretSwitches</c>).
/// </summary>
public static partial class StreamerMode
{
    /// <summary>The players' switch that turns it on for one run (not kept).</summary>
    public const string Switch = "--streamer";

    /// <summary>The setting's file in the platform's user data (the program's own user data folder for <c>vitomir-tools</c>).</summary>
    public const string FileName = "streamer.json";

    /// <summary>What stands in for an address (and its port) on the screen.</summary>
    public const string Hidden = "(hidden)";

    /// <summary>What stands in for the user's own folder in a path on the screen.</summary>
    public const string ProfileWord = "%USERPROFILE%";

    /// <summary>What an address box draws for each character typed while it is on.</summary>
    public const char Dot = '.';

    static volatile bool on;
    static int version;

    /// <summary>Whether it is on now (the setting kept, or <c>--streamer</c> for this run). Changing it says so in <see cref="Version"/>.</summary>
    public static bool On
    {
        get => on;
        set
        {
            if (on == value) return;
            on = value;
            shown.Clear();
            Interlocked.Increment(ref version);
        }
    }

    /// <summary>Counts each change of <see cref="On"/> and of the secrets, so a box laid out once lays itself out again.</summary>
    public static int Version => Volatile.Read(ref version);

    /// <summary>
    /// The user's own folder, which a path on the screen names as <see cref="ProfileWord"/> (the system's, unless a test gives
    /// its own; null or empty: none).
    /// </summary>
    public static string? Profile
    {
        get => profile;
        set { profile = value; shown.Clear(); Interlocked.Increment(ref version); }
    }
    static string? profile = SystemProfile();

    static string? SystemProfile()
    {
        try { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
        catch (PlatformNotSupportedException) { return null; }
    }

    static readonly ConcurrentDictionary<string, byte> secrets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A text a game says is secret, hidden wherever it shows while this is on: the host's name as typed in the join box (an
    /// address that is a name, <c>friend.example.org:4994</c>, which no pattern knows), a join code a later way in brings.
    /// Shorter than three characters, or nothing but a port, it is not taken (it would hide ordinary words).
    /// </summary>
    public static void Hide(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return;
        secret = secret.Trim();
        if (secret.Length < 3 || secret.All(char.IsAsciiDigit)) return;
        if (secrets.TryAdd(secret, 0))
        {
            shown.Clear();
            Interlocked.Increment(ref version);
        }
        // A name with its port is hidden with or without it.
        int colon = secret.LastIndexOf(':');
        if (colon > 2 && secret.IndexOf(':') == colon && secret[(colon + 1)..].All(char.IsAsciiDigit)) Hide(secret[..colon]);
    }

    /// <summary>The secrets said (<see cref="Hide"/>), for a test.</summary>
    public static IReadOnlyCollection<string> Secrets => [.. secrets.Keys];

    /// <summary>Forgets the secrets (a test's).</summary>
    public static void Forget()
    {
        secrets.Clear();
        shown.Clear();
        Interlocked.Increment(ref version);
    }

    /// <summary>A join code, as the screen shows it: hidden while this is on (none exists yet; for later ways in).</summary>
    public static string Code(string code) => On ? Hidden : code;

    /// <summary>An address box's text as it is drawn: a dot a character while this is on, else as it is.</summary>
    public static string Dots(string text) => On ? new string(Dot, text.Length) : text;

    // The screen asks the same lines every frame: what was worked out is kept (cleared at every change, and when it grows).
    static readonly ConcurrentDictionary<string, string> shown = new(StringComparer.Ordinal);
    const int ShownKept = 4096;

    /// <summary>**A text as the screen shows it**: as it is while this is off; with every address, the user's folder and every secret hidden while it is on.</summary>
    public static string Shown(string text)
    {
        if (!on || string.IsNullOrEmpty(text)) return text;
        if (shown.TryGetValue(text, out var done)) return done;
        done = Mask(text, profile, secrets.Keys);
        if (shown.Count >= ShownKept) shown.Clear();
        shown[text] = done;
        return done;
    }

    /// <summary>Null kept null (<see cref="Shown(string)"/>).</summary>
    public static string? ShownOrNull(string? text) => text is null ? null : Shown(text);

    /// <summary>
    /// **The masking itself**, whatever the setting (a pure function, for the tests): every address with its port as
    /// <see cref="Hidden"/>, <paramref name="profileFolder"/> (either slash, any case) as <see cref="ProfileWord"/>, and each
    /// of <paramref name="secretTexts"/> (any case) as <see cref="Hidden"/>.
    /// </summary>
    public static string Mask(string text, string? profileFolder, IEnumerable<string>? secretTexts = null)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (!string.IsNullOrEmpty(profileFolder) && profileFolder.Length > 1)
        {
            string folder = profileFolder.TrimEnd('\\', '/');
            if (folder.Length > 1)
            {
                text = ReplaceAny(text, folder);
                if (folder.Contains('\\')) text = ReplaceAny(text, folder.Replace('\\', '/'));
                else if (folder.Contains('/')) text = ReplaceAny(text, folder.Replace('/', '\\'));
            }
        }
        foreach (var secret in (secretTexts ?? []).OrderByDescending(s => s.Length))
            if (secret.Length > 0) text = Replace(text, secret, Hidden);
        if (!MayHoldAddress(text)) return text;
        text = Bracketed().Replace(text, Hidden);
        text = Six().Replace(text, m => m.Value.Count(c => c == ':') >= 2 ? Hidden : m.Value);
        text = Four().Replace(text, Hidden);
        return text;

        static string ReplaceAny(string text, string folder) => Replace(text, folder, ProfileWord);
    }

    static string Replace(string text, string what, string with)
    {
        int at = text.IndexOf(what, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        int from = 0;
        while (at >= 0)
        {
            sb.Append(text, from, at - from).Append(with);
            from = at + what.Length;
            at = text.IndexOf(what, from, StringComparison.OrdinalIgnoreCase);
        }
        return sb.Append(text, from, text.Length - from).ToString();
    }

    /// <summary>A quick look before the patterns: an address has a dot between digits or a colon.</summary>
    static bool MayHoldAddress(string text)
    {
        for (int i = 1; i + 1 < text.Length; i++)
            if ((text[i] == '.' && char.IsAsciiDigit(text[i - 1]) && char.IsAsciiDigit(text[i + 1])) || text[i] == ':') return true;
        return false;
    }

    // An IPv6 address in brackets, with its port: [::1]:4994, [fe80::1%3].
    [GeneratedRegex(@"\[[0-9A-Fa-f:.%]*:[0-9A-Fa-f:.%]*\](?::\d{1,5})?", RegexOptions.CultureInvariant)]
    private static partial Regex Bracketed();

    // A bare IPv6 address: one with "::" (an IPv4 in IPv6 among them, ::ffff:10.0.0.2, with its port when one follows the
    // IPv4), or four colons and more between hex groups. Not a clock (12:30:45: no "::", three groups) nor a word.
    [GeneratedRegex(@"(?<![\w:.])(?:(?:[0-9A-Fa-f]{1,4}:){0,6}[0-9A-Fa-f]{0,4}::(?:[0-9A-Fa-f]{1,4}:){0,6}(?:\d{1,3}(?:\.\d{1,3}){3}(?::\d{1,5})?|[0-9A-Fa-f]{1,4})?|(?:[0-9A-Fa-f]{1,4}:){4,7}[0-9A-Fa-f]{1,4})(?![\w:])", RegexOptions.CultureInvariant)]
    private static partial Regex Six();

    // An IPv4 address with its port: 192.168.1.20:4994. Four numbers of up to three digits, not part of a longer run of them.
    [GeneratedRegex(@"(?<![\d.])(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?:\.(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}(?::\d{1,5})?(?![\d.]*\d)", RegexOptions.CultureInvariant)]
    private static partial Regex Four();

    // ---- the setting kept --------------------------------------------------------------------------------------------------

    /// <summary>The setting's file in a user data folder.</summary>
    public static string PathIn(string userData) => System.IO.Path.Combine(userData, FileName);

    /// <summary>The file the setting is kept in, set by the program at its start (null: kept nowhere, a test's).</summary>
    public static string? Kept { get; set; }

    /// <summary>
    /// The setting as its file has it: <c>{ "streamer": true }</c>; false when the file is not there, does not read, or says
    /// anything else (it is off by default). <paramref name="say"/> is told of a file that does not read.
    /// </summary>
    public static bool Read(string path, Action<string>? say = null)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("streamer", out var v) && v.ValueKind == JsonValueKind.True;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            say?.Invoke($"streamer: {path} could not be read ({e.Message}); streamer mode is off");
            return false;
        }
    }

    /// <summary>The setting written to its file (through a file beside it, so a run that stops half way never leaves half a file).</summary>
    public static void Write(string path, bool value)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, $"{{\n  \"streamer\": {(value ? "true" : "false")}\n}}\n");
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// **At a program's start**: the file kept in <paramref name="userData"/> read (<see cref="Kept"/> set to it), and
    /// <c>--streamer</c> among <paramref name="args"/> turns it on for this run whatever the file says. Answers the one line the
    /// program writes to its log.
    /// </summary>
    public static string Start(string userData, IReadOnlyList<string> args, Action<string>? say = null)
    {
        Kept = PathIn(userData);
        bool kept = Read(Kept, say);
        bool given = args.Contains(Switch);
        On = kept || given;
        return $"streamer: {(On ? "on" : "off")}{(given ? " (--streamer, this run)" : kept ? " (kept)" : "")}";
    }

    /// <summary>The setting changed from a game's options: in force at once and kept in its file (when one is).</summary>
    public static void Set(bool value)
    {
        On = value;
        if (Kept is { } path)
            try { Write(path, value); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
