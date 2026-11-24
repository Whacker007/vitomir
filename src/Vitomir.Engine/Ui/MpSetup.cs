// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the multiplayer screens keep between runs besides the last character and the most players (ours): the game a
// Host Game makes at once — its name, chronicle and options as the host last set them in Game Setup — and the address
// a player last joined by. 1.1 keeps none of it (its create screen is filled afresh each time, and its Internet was WON's).
using System.Globalization;
using System.Net;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Net;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The game Host Game makes, and the address Join Game asks with** (ours), kept under <see cref="LastPlayer.Section"/> of the
/// player's own <c>vitomir.ini</c> as the last character is (<see cref="LastPlayer"/>): <c>gamename</c>, <c>chronicle</c> and
/// <c>gameoptions</c> written by Game Setup's OK, <c>lastaddress</c> by a join by address. **The password is never kept.** With
/// nothing kept, a game is 1.1's create screen's defaults: <c>UI_NET_DEFAULTGAME</c> for the name, the first multiplayer
/// chronicle, no password, Auto Storyteller alone ticked, and the most players kept by <see cref="MostPlayersOption"/>.
/// </summary>
public static class MpSetup
{
    public const string NameKey = "gamename", ChronicleKey = "chronicle", OptionsKey = "gameoptions", AddressKey = "lastaddress";

    /// <summary>The create screen's four boxes, the only game flags kept.</summary>
    public const GameFlags Boxes = GameFlags.CoOp | GameFlags.TrackHumanity | GameFlags.Moderated | GameFlags.AutoStoryteller;

    /// <summary>The name a game gets when none is kept: 1.1's <c>UI_NET_DEFAULTGAME</c> ("%P's game") with <paramref name="player"/>.</summary>
    public static string DefaultName(Func<string, string?> find, string player) => Strings.Format(find, "UI_NET_DEFAULTGAME", "%P" + player);

    /// <summary>
    /// The game Host Game makes: the kept name, chronicle (when it is still one of <paramref name="offered"/>) and options, else
    /// the defaults; the most players kept, else eight. <paramref name="name"/> is the default name.
    /// </summary>
    public static GameSettings Kept(string? settingsPath, IReadOnlyList<Chronicle> offered, string name)
    {
        var ini = settingsPath is null ? null : IniFile.Read(settingsPath);
        string? Get(string key) => ini?[LastPlayer.Section + "." + key] is { Length: > 0 } v ? v : null;
        var chronicle = offered.FirstOrDefault(c => c.Name.Equals(Get(ChronicleKey), StringComparison.OrdinalIgnoreCase)) ?? offered.FirstOrDefault();
        var flags = uint.TryParse(Get(OptionsKey), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint kept)
            ? (GameFlags)kept & Boxes : GameFlags.AutoStoryteller;
        if ((flags & (GameFlags.Moderated | GameFlags.AutoStoryteller)) == 0) flags |= GameFlags.AutoStoryteller;   // one Storyteller box, as the screen keeps
        int most = MostPlayersOption.Read(settingsPath);
        return chronicle is null
            ? new GameSettings { Name = Get(NameKey) ?? name, Flags = flags, MaxPlayers = most }
            : GameSettings.For(chronicle, Get(NameKey) ?? name, flags, "", most);
    }

    /// <summary>Keeps the game's name, chronicle and options (never its password) and the most players, for the next Host Game.</summary>
    public static void Keep(string settingsPath, GameSettings settings)
    {
        SettingsFile.Set(settingsPath, LastPlayer.Section, NameKey, settings.Name);
        SettingsFile.Set(settingsPath, LastPlayer.Section, ChronicleKey, settings.Chronicle);
        SettingsFile.Set(settingsPath, LastPlayer.Section, OptionsKey, ((uint)(settings.Flags & Boxes)).ToString("x", CultureInfo.InvariantCulture));
        MostPlayersOption.Write(settingsPath, settings.MaxPlayers);
    }

    /// <summary>The address last joined by, or empty: what the address box shows (never the loopback by itself).</summary>
    public static string LastAddress(string? settingsPath) =>
        settingsPath is null ? "" : IniFile.Read(settingsPath)[LastPlayer.Section + "." + AddressKey] ?? "";

    /// <summary>Keeps the address as typed.</summary>
    public static void KeepAddress(string settingsPath, string address) => SettingsFile.Set(settingsPath, LastPlayer.Section, AddressKey, address);

    /// <summary>The box's words for a join nobody answered (within the transport's five seconds, <see cref="NetPeer.ConnectTimeoutMs"/>).</summary>
    public static string NoAnswer(IPEndPoint host) => $"No game answered at {host}.";

    /// <summary>A room's line for a player with no character yet.</summary>
    public const string ChooseLine = "Choose your character with Player Setup.";

    /// <summary>A room's line for a game whose chronicle this install has not (the player counts as not ready).</summary>
    public static string MissingChronicle(string chronicle) =>
        $"This install does not have the game's chronicle ({chronicle}): you cannot play it until the host chooses another.";

    /// <summary>The players' list's words for a slot whose player has not chosen a character yet.</summary>
    public const string Choosing = "choosing a character";

    /// <summary>The players' list's words after a player whose install lacks the chronicle.</summary>
    public const string NoChronicle = "(no chronicle)";
}
