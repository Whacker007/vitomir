// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Where the player's things are kept (the user folder): VITOMIR_USERDATA, else vitomir.ini beside the launcher, else the usual place.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Vitomir.Platform.Setup;

/// <summary>One line of <c>vitomir.ini</c> that is not right: its number (from 1), its text and why.</summary>
public sealed record IniProblem(int Line, string Text, string Why)
{
    /// <summary>The problem in words: <c>vitomir.ini, line 3 ("colour = red"): …</c>.</summary>
    public string Words => Line > 0 ? $"{VitomirIni.FileName}, line {Line} (\"{Text.Trim()}\"): {Why}" : $"{VitomirIni.FileName}: {Why}";
}

/// <summary>
/// **<c>vitomir.ini</c>**, the file beside <c>vitomir-launcher.exe</c> that says where the user folder is: one section, <c>[folders]</c>,
/// with one setting, <c>user</c>. A line is empty, a comment (its first character other than a space <c>;</c> or <c>#</c>), a section
/// (<c>[folders]</c>) or a setting (<c>user = D:\Vitomir-data</c>); the value is the rest of the line, its spaces trimmed, or the words
/// between double quotes (<c>user = "D:\Vitomir-data\My data"</c>, which may be followed by a comment). An unquoted value takes no comment after it,
/// as Windows' own ini files: a folder's name may hold <c>;</c> and <c>#</c>. Anything else (an unknown section or setting, a setting
/// outside <c>[folders]</c>, <c>user</c> twice, a line that is none of these) is a problem with its line; nothing is guessed.
/// <see cref="UserFolderChoice"/> reads it; the launcher writes it (<see cref="WithUser"/>), and the package never ships one.
/// </summary>
public sealed record VitomirIni(string Path, bool Exists, string? User, int UserLine, IReadOnlyList<IniProblem> Problems, IReadOnlyList<string> Lines)
{
    /// <summary>The file's name.</summary>
    public const string FileName = "vitomir.ini";

    /// <summary>Its one section and its one setting.</summary>
    public const string Section = "folders", UserKey = "user";

    /// <summary>The file's text as the launcher writes a new one: a comment saying what it is, then the section and the setting.</summary>
    public const string Header =
        "; Vitomir's settings for this folder (read by vitomir-launcher.exe and the game).\n" +
        "; user = where your saves, settings, logs and imported game files are kept:\n" +
        ";   empty           the usual place, %LOCALAPPDATA%\\Vitomir\n" +
        ";   D:\\Vitomir-data  a folder of your own (environment variables such as %USERPROFILE% are expanded)\n" +
        ";   UserData        a folder inside this Vitomir folder (portable: the folder carries everything)\n";

    /// <summary>The ini at <paramref name="path"/>: read when it is there, else none (<see cref="Exists"/> false, no problem).</summary>
    public static VitomirIni Read(string path)
    {
        if (!File.Exists(path)) return new(path, false, null, 0, [], []);
        try { return Parse(path, File.ReadAllText(path)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(path, true, null, 0, [new(0, "", $"it cannot be read ({e.Message})")], []);
        }
    }

    /// <summary>**The ini's text parsed**: its <c>user</c> setting (null when it has none) and its line, and every line that is not right.</summary>
    public static VitomirIni Parse(string path, string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0) lines = lines[..^1];
        if (lines.Length > 0 && lines[0].StartsWith('\uFEFF')) lines[0] = lines[0][1..];
        var problems = new List<IniProblem>();
        string? section = null, user = null;
        int userLine = 0;
        bool unknownSection = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i], t = line.Trim();
            int n = i + 1;
            if (t.Length == 0 || t[0] is ';' or '#') continue;
            if (t[0] == '[')
            {
                if (!t.EndsWith(']')) { problems.Add(new(n, line, "a section's name ends with ]")); unknownSection = true; continue; }
                section = t[1..^1].Trim();
                unknownSection = !section.Equals(Section, StringComparison.OrdinalIgnoreCase);
                if (unknownSection) problems.Add(new(n, line, $"[{section}] is not a section of {FileName}: it has only [{Section}]"));
                continue;
            }
            int eq = t.IndexOf('=');
            if (eq <= 0) { problems.Add(new(n, line, $"it is not a setting (user = <folder>), a comment (starting with ; or #) or a section ([{Section}])")); continue; }
            string key = t[..eq].Trim(), raw = t[(eq + 1)..].Trim();
            if (unknownSection) continue;                                          // its section said already
            if (section is null) { problems.Add(new(n, line, $"{key} is outside a section: it belongs under [{Section}]")); continue; }
            if (!key.Equals(UserKey, StringComparison.OrdinalIgnoreCase)) { problems.Add(new(n, line, $"{key} is not a setting of [{Section}]: it has only {UserKey}")); continue; }
            if (user is not null) { problems.Add(new(n, line, $"{UserKey} is set twice (lines {userLine} and {n})")); continue; }
            if (Unquote(raw, out string? why) is not { } value) { problems.Add(new(n, line, why!)); continue; }
            user = value;
            userLine = n;
        }
        return new(path, true, user, userLine, problems, lines);
    }

    /// <summary>A value: the words between double quotes (a comment may follow them), else the trimmed text as it is; null with why when a quote does not close or words follow it.</summary>
    static string? Unquote(string raw, out string? why)
    {
        why = null;
        if (!raw.StartsWith('"')) return raw;
        int close = raw.IndexOf('"', 1);
        if (close < 0) { why = "its quote does not close"; return null; }
        string after = raw[(close + 1)..].Trim();
        if (after.Length > 0 && after[0] is not (';' or '#')) { why = $"words after the closing quote ({after}): put a comment after ; or #"; return null; }
        return raw[1..close];
    }

    /// <summary>
    /// **The ini's text with <c>user</c> set to <paramref name="value"/>** (empty: the usual place): the line of the setting replaced in
    /// <paramref name="existing"/> (its comments and the rest kept), or added under <c>[folders]</c> (added too when there is none); a new file's
    /// text is <see cref="Header"/> and the section. A value with spaces at its ends, or a quote, <c>;</c> or <c>#</c> first, is quoted.
    /// </summary>
    public static string WithUser(string? existing, string value)
    {
        string setting = $"{UserKey} = {Quote(value)}";
        if (string.IsNullOrEmpty(existing)) return Header + $"\n[{Section}]\n{setting}\n";
        var parsed = Parse("", existing);
        var lines = parsed.Lines.ToList();
        if (parsed.UserLine > 0) lines[parsed.UserLine - 1] = setting;
        else
        {
            int at = lines.FindIndex(l => l.Trim().Equals($"[{Section}]", StringComparison.OrdinalIgnoreCase));
            if (at < 0) { lines.Add(""); lines.Add($"[{Section}]"); lines.Add(setting); }
            else lines.Insert(at + 1, setting);
        }
        return string.Join("\n", lines) + "\n";
    }

    static string Quote(string value) =>
        value.Length > 0 && (value != value.Trim() || value[0] is '"' or ';' or '#') ? "\"" + value + "\"" : value;
}

/// <summary>How the user folder was chosen.</summary>
public enum UserFolderSource
{
    /// <summary><c>VITOMIR_USERDATA</c> names it (a developer's, the tests'): it beats the ini.</summary>
    Variable,
    /// <summary><c>vitomir.ini</c>'s <c>user</c> names it (a folder of the player's own, or one inside the Vitomir folder: portable).</summary>
    Ini,
    /// <summary>The usual place, <c>%LOCALAPPDATA%\Vitomir</c> (no ini, or its <c>user</c> empty).</summary>
    Usual,
}

/// <summary>
/// **What the user folder's choice is made from**: <c>VITOMIR_USERDATA</c>'s value, the program's own folder (the ini is looked for at
/// the top of the Vitomir folder: <see cref="TopFolder"/>) and the root of the usual place (<c>%LOCALAPPDATA%</c>). A test gives its own.
/// </summary>
public sealed record UserFolderPlace(string? Variable, string ProgramFolder, string UsualRoot)
{
    /// <summary>The variable that names the user folder outright (a developer's and the tests'); it beats the ini.</summary>
    public const string VariableName = "VITOMIR_USERDATA";

    /// <summary>The usual place's folder under <c>%LOCALAPPDATA%</c>.</summary>
    public const string UsualName = "Vitomir";

    /// <summary>The portable user folder's name inside the Vitomir folder.</summary>
    public const string PortableName = "UserData";

    /// <summary>This program's: the variable, <see cref="AppContext.BaseDirectory"/>, and <c>%LOCALAPPDATA%</c> (<c>~/.local/share</c> where there is none).</summary>
    public static UserFolderPlace ThisProgram()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return new(Environment.GetEnvironmentVariable(VariableName), AppContext.BaseDirectory, root);
    }

    /// <summary>
    /// **The top of the Vitomir folder**: the program's folder, or the folder above it when it is named <c>bin</c> (the package's
    /// programs are in <c>Vitomir\bin\</c> and <c>vitomir-launcher.exe</c> is at <c>Vitomir\</c>; a developer's build folder is its own top).
    /// </summary>
    public string TopFolder => TopOf(ProgramFolder);

    /// <summary>The top folder of a program's folder (<see cref="TopFolder"/>).</summary>
    public static string TopOf(string programFolder)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(programFolder));
        return Path.GetFileName(full).Equals("bin", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(full) is { Length: > 0 } up ? up : full;
    }

    /// <summary>Where the ini is: <c>vitomir.ini</c> at the top folder.</summary>
    public string IniPath => Path.Combine(TopFolder, VitomirIni.FileName);

    /// <summary>The usual place: <c>%LOCALAPPDATA%\Vitomir</c>.</summary>
    public string Usual => Path.Combine(UsualRoot, UsualName);

    /// <summary>The portable user folder: <c>UserData</c> inside the Vitomir folder.</summary>
    public string Portable => Path.Combine(TopFolder, PortableName);

    /// <summary>The program's own <c>bin</c> folder when the top folder is above it (a move there is refused), else null.</summary>
    public string? Bin
    {
        get
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ProgramFolder));
            return TopFolder.Equals(full, StringComparison.OrdinalIgnoreCase) ? null : full;
        }
    }
}

/// <summary>
/// **The user folder as chosen**: <c>VITOMIR_USERDATA</c> (a developer's, the tests') beats <c>vitomir.ini</c>'s <c>[folders] user</c>,
/// which beats the usual place. The ini's value: empty (or no ini) the usual place; an absolute path that folder; a relative one a folder
/// under the Vitomir folder (portable). Environment variables in it (<c>%USERPROFILE%</c>) are expanded. A bad ini is
/// <see cref="Problem"/>, never a silent fallback: <see cref="Folder"/> is then the usual place's path, which nothing may use
/// (the readiness check refuses the start, <see cref="Readiness"/>).
/// </summary>
public sealed record UserFolderChoice(string Folder, UserFolderSource Source, UserFolderPlace Place, VitomirIni Ini, IReadOnlyList<IniProblem> Problems)
{
    /// <summary>The first problem of the ini (or of what it names), in words; null when there is none.</summary>
    public string? Problem => Problems.Count == 0 ? null : Problems[0].Words;

    /// <summary>
    /// Whether the ini names it by a relative path (under the Vitomir folder): such a folder that is not there yet is made on first use
    /// (<see cref="Readiness.UserFolder"/>), where a missing absolute one was moved or its drive unplugged.
    /// </summary>
    public bool Relative => Source == UserFolderSource.Ini && !string.IsNullOrWhiteSpace(Ini.User)
                            && !Path.IsPathFullyQualified(Environment.ExpandEnvironmentVariables(Ini.User.Trim().Trim('"')));

    /// <summary>Whether the folder is inside the Vitomir folder (portable).</summary>
    public bool Portable => Source == UserFolderSource.Ini && UserFolderMarks.Under(Folder, Place.TopFolder);

    /// <summary>
    /// **Whether a user folder has been chosen**: named by the variable or the ini (its <c>user</c> empty is the usual place, chosen), or
    /// the usual place is there with a user folder's mark in it (<see cref="UserFolderMarks.HasMark"/>). False on a first start with no
    /// ini and the usual place missing or empty, and whenever the usual place holds an older Vitomir's folder (<see cref="OlderVitomir"/>):
    /// the launcher asks where (<see cref="UserFolderSetup"/>).
    /// </summary>
    public bool Chosen => Source != UserFolderSource.Usual
                          || !OlderVitomir && (Ini.Exists || !UserFolderMarks.EmptyOrMissing(Folder));

    /// <summary>
    /// Whether the usual place (<c>%LOCALAPPDATA%\Vitomir</c>) is there with something in it but no user folder's mark: the old
    /// program's own folder had that name, and it is never taken for a user folder (nor deleted: the player renames or deletes it, or
    /// chooses another place).
    /// </summary>
    public bool OlderVitomir => Source == UserFolderSource.Usual && Problems.Count == 0
                                && !UserFolderMarks.EmptyOrMissing(Folder) && !UserFolderMarks.HasMark(Folder);

    /// <summary>What the first start's choices say while <see cref="OlderVitomir"/> holds.</summary>
    public const string OlderVitomirWords = "A folder named Vitomir from an older Vitomir is there: rename or delete it, or choose another place";

    /// <summary>How it was chosen, in words.</summary>
    public string How => Source switch
    {
        UserFolderSource.Variable => $"named by {UserFolderPlace.VariableName} (a developer's)",
        UserFolderSource.Ini => $"named by {VitomirIni.FileName} (line {Ini.UserLine}{(Portable ? ": portable, inside the Vitomir folder" : "")})",
        _ => Ini.Exists ? $"the usual place ({VitomirIni.FileName} names no other)" : "the usual place",
    };

    /// <summary>This program's choice (<see cref="UserFolderPlace.ThisProgram"/>), read again each time: the ini may have been written since.</summary>
    public static UserFolderChoice ThisProgram() => Resolve(UserFolderPlace.ThisProgram());

    /// <summary>**The choice made from <paramref name="place"/>**: the variable, else the ini, else the usual place (above).</summary>
    public static UserFolderChoice Resolve(UserFolderPlace place)
    {
        var ini = VitomirIni.Read(place.IniPath);
        if (!string.IsNullOrWhiteSpace(place.Variable))
            return new(Path.GetFullPath(place.Variable), UserFolderSource.Variable, place, ini, []);
        if (ini.Problems.Count > 0) return new(place.Usual, UserFolderSource.Usual, place, ini, ini.Problems);
        if (string.IsNullOrWhiteSpace(ini.User)) return new(place.Usual, UserFolderSource.Usual, place, ini, []);
        string text = ini.Lines.Count >= ini.UserLine && ini.UserLine > 0 ? ini.Lines[ini.UserLine - 1] : ini.User;
        IniProblem Bad(string why) => new(ini.UserLine, text, why);
        string expanded = Environment.ExpandEnvironmentVariables(ini.User);
        if (Regex.Match(expanded, @"%[^%\s]+%") is { Success: true } unset)
            return new(place.Usual, UserFolderSource.Usual, place, ini, [Bad($"{unset.Value} is not set on this machine")]);
        string folder;
        try
        {
            if (Path.IsPathFullyQualified(expanded)) folder = Path.GetFullPath(expanded);
            else if (Path.IsPathRooted(expanded))
                return new(place.Usual, UserFolderSource.Usual, place, ini, [Bad("it is not a whole path: name its drive (D:\\Vitomir-data), or a folder inside the Vitomir folder (UserData)")]);
            else folder = Path.GetFullPath(Path.Combine(place.TopFolder, expanded));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(place.Usual, UserFolderSource.Usual, place, ini, [Bad($"it is not a folder's path ({e.Message})")]);
        }
        if (File.Exists(folder)) return new(place.Usual, UserFolderSource.Usual, place, ini, [Bad($"{folder} is a file, not a folder")]);
        return new(Path.TrimEndingDirectorySeparator(folder), UserFolderSource.Ini, place, ini, []);
    }

    /// <summary>The value the ini would hold for <paramref name="folder"/>: relative when it is inside the Vitomir folder (portable), else whole.</summary>
    public static string IniValue(UserFolderPlace place, string folder)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return UserFolderMarks.Under(full, place.TopFolder) && !full.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(place.TopFolder)), StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(place.TopFolder, full) : full;
    }
}

/// <summary>
/// **What a user folder holds that says it is one**: the mark the launcher writes in one it sets up (<see cref="MarkName"/>), or any of
/// the things the platform keeps there (its imported game files, its modules' folders, its games' settings, the player's modules, the
/// launcher's settings, its settings, its logs).
/// </summary>
public static class UserFolderMarks
{
    /// <summary>The mark of a user folder set up by the launcher (<c>vitomir-user-folder.json</c>: what made it and when).</summary>
    public const string MarkName = "vitomir-user-folder.json";

    /// <summary>The things the platform keeps at a user folder's top (any one says it is a user folder of Vitomir's).</summary>
    public static readonly string[] Known = [MarkName, "Imported", "Modules", "Games", "PlayerModules", "Launcher", "Logs", "Recordings", "Screenshots", "settings.json", "streamer.json"];

    /// <summary>Whether <paramref name="folder"/> is not there or holds nothing.</summary>
    public static bool EmptyOrMissing(string folder)
    {
        try { return !Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Whether <paramref name="folder"/> holds a user folder's mark: the launcher's (<see cref="MarkName"/>) or the imported game files'
    /// marker (<see cref="ImportedData.MarkerName"/>).
    /// </summary>
    public static bool HasMark(string folder)
    {
        try { return File.Exists(Path.Combine(folder, MarkName)) || File.Exists(Path.Combine(ImportedData.Folder(folder), ImportedData.MarkerName)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    /// <summary>Whether <paramref name="folder"/> looks like a user folder of Vitomir's (its mark, or one of <see cref="Known"/>).</summary>
    public static bool LooksLikeOne(string folder)
    {
        try { return Directory.Exists(folder) && Known.Any(k => File.Exists(Path.Combine(folder, k)) || Directory.Exists(Path.Combine(folder, k))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Writes the mark in <paramref name="folder"/> (made when it is not there): by what and when, in UTC.</summary>
    public static void Mark(string folder, string madeBy)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, MarkName);
        if (File.Exists(path)) return;
        File.WriteAllText(path, "{\n  \"format\": \"vitomir-user-folder/1\",\n  \"madeBy\": " + System.Text.Json.JsonSerializer.Serialize(madeBy)
            + ",\n  \"madeUtc\": \"" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\"\n}\n", new UTF8Encoding(false));
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or inside it (whole paths, any capitals).</summary>
    public static bool Under(string path, string folder)
    {
        string p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return p.Equals(f, StringComparison.OrdinalIgnoreCase) || p.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
