// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The multiplayer character remembered between runs — 1.1's `lastplayer` ("Both and keep
// escape"). 1.1 keeps it in its own ini through GetPrivateProfileString and
// WritePrivateProfileString; Vitomir keeps it in the player's own `vitomir.ini`, never in 1.1's.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Net;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The last multiplayer character**. The select screen, opened with
/// nobody in use, reads the name kept as `lastplayer` (the default empty) and loads that character;
/// the list is then drawn again and the loaded one's row chosen by its name without regard to
/// case (`__strcmpi`). When nothing loads — no name kept, or no such character any more — the
/// first row is chosen instead. OK and Enter (the key hook,
/// either Enter key) both write the character in use back as `lastplayer` before going on to the
/// lobby.
/// <para>Vitomir keeps it under <see cref="Section"/> in the player's own settings file
/// (`Diag.UserSettingsPath`, ours), which the caller names: the multiplayer screens remember nobody
/// until they are given one (<see cref="MpMenus.SettingsPath"/>), so a test never reads or writes the
/// player's real file.</para>
/// </summary>
public static class LastPlayer
{
    /// <summary>The section it is kept under in `vitomir.ini` (ours; 1.1's ini is not Vitomir's to write).</summary>
    public const string Section = "multiplayer";
    /// <summary>1.1's own key.</summary>
    public const string Key = "lastplayer";

    /// <summary>The name kept, or empty when none is (the ini read's empty default).</summary>
    public static string Read(string settingsPath) => IniFile.Read(settingsPath)[Section + "." + Key] ?? "";

    /// <summary>Keeps <paramref name="name"/> as the last character, the file's other lines left as they are.</summary>
    public static void Write(string settingsPath, string name) => SettingsFile.Set(settingsPath, Section, Key, name);

    /// <summary>
    /// The row chosen when the select screen opens with nobody in use (its last step): the row of
    /// the character named <paramref name="remembered"/>, its name compared without regard to case
    /// (the file of that name is loaded, and the list chooses the loaded one's row by `__strcmpi`
    /// — so the answer does not depend on whether the file system minds case); the first row when
    /// nobody is remembered or the one remembered is not listed; −1 when the list is empty.
    /// </summary>
    public static int Row(IReadOnlyList<NetCharacter> listed, string remembered)
    {
        if (listed.Count == 0) return -1;
        if (remembered.Length > 0)
            for (int i = 0; i < listed.Count; i++)
                if (string.Equals(listed[i].Name, remembered, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }
}
