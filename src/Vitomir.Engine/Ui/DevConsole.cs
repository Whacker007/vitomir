// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The developer console, read: its set-up and command table,
// the line it runs and its key hook. The commands themselves are
// `ConsoleCommands` (every one of the 94), given to it by the host with whatever of the
// game they reach.
namespace Vitomir.Engine.Ui;

/// <summary>
/// The console: `ui\console.nui` — a 256 x 16 line at the top of the screen, a dialog — opened in
/// 1.1 by the key left of 1 (DIK_GRAVE) and **only** when the game was
/// started with `-console` (bit 0x40 of the options word). **Vitomir opens it in every game**
/// (always available, with no launch switch) — one of the conveniences that are always on
/// with no switch.
///
/// What 1.1 does, and this does:
///
/// - **Enter** runs the line: it goes to the top of a 32-line history (80 characters a
///   line) unless it is the line already there; the console **closes**; the first word, lower-cased,
///   names the command and the rest are its arguments. A name that is not a command answers
///   "Unrecognized command" in the message log (single player; in multiplayer the line is chat).
/// - **Escape** or the grave key again clears the line and closes it; **[** steps back through the
///   history and **]** forward, each wrapping round; **Tab** completes a command name from what is
///   typed.
/// - What a command says goes to the status bar's message box.
/// </summary>
public sealed class DevConsole(FrontEnd menus)
{
    /// <summary>The pane (`ui\console.nui`) and its field (`consoleEdit`).</summary>
    public const string File = "console", Edit = "consoleEdit";
    /// <summary>The history: 32 lines of up to 80 characters (`0x1f < count`, `strncpy(…, 0x50)`).</summary>
    public const int HistoryLines = 32, LineLength = 80;
    /// <summary>What an unknown command answers.</summary>
    public const string Unrecognized = "Unrecognized command";

    /// <summary>
    /// Every command 1.1 registers, in its order — the cheat table and the command table
    /// together (`aura`, `ping`, `rename` and `humanity` are in both). <see cref="ConsoleCommands"/>
    /// gives every one a handler in <see cref="Commands"/>; a name left without one would answer that
    /// Vitomir has no such command yet.
    /// </summary>
    public static readonly string[] OriginalCommands =
    [
        "commands", "framerate", "soundcache", "addthing", "playmusic", "stopmusic", "musicvolume", "playambient",
        "stopambient", "ambientvolume", "adddiscipline", "castdiscipline", "maxfps", "timescale", "pause", "resume",
        "shadow", "vertexlight", "thinglights", "fog", "zclip", "modellod", "lightblobs", "numshadows", "sky", "gamma",
        "prelights", "cash", "echo", "whereis", "jump", "scenelist", "revive", "god", "ai", "aitrace", "freecasting",
        "addalldisciplines", "particles", "heads", "loadpath", "savepath", "say", "statustest", "changescene",
        "advance", "changechronicle", "getcls", "stakeme", "speed", "freeammo", "combatmessages", "idkfa", "frenzyme",
        "killme", "damageme", "diseaseme", "poisonme", "scaleme", "embraceme", "xp", "advancement", "aura", "dropcash",
        "vault", "camtest", "setsoak", "shapeshift", "getchronflag", "setchronflag", "clearchronflag", "netstat",
        "read", "shell", "emit", "roomtype", "autoswitchtest", "totals", "enemycount", "throwgib", "setdetect",
        "sethide", "freeze", "hands", "stcmd", "stcmd2", "team", "maxopenedlocs", "noitemstatchecks", "endgame",
        "pack", "ping", "rename", "humanity",
    ];

    /// <summary>The commands Vitomir can run, by name: each takes the arguments and answers what to print (or null).</summary>
    public Dictionary<string, Func<string[], string?>> Commands { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A line for the message log.</summary>
    public event Action<string>? Posted;

    /// <summary>Puts a line in the message box as a command's own print does.</summary>
    public void Say(string line) => Post(line);

    /// <summary>The last line the console put out — a command's answer or its own complaint — which Ctrl+C copies from an empty line (ours, <see cref="TypedLine"/>).</summary>
    public string? LastOutput { get; private set; }

    void Post(string line)
    {
        LastOutput = line;
        Posted?.Invoke(line);
    }

    readonly List<string> history = [];
    int recalled = -1;

    public IReadOnlyList<string> History => history;
    public MenuPane Pane => menus.Pane(File);
    public bool IsOpen => Pane.Shown;

    /// <summary>The console up, the field empty and with the keyboard.</summary>
    public void Open()
    {
        var pane = Pane;
        if (pane.Shown) return;
        pane.Edits[Edit].Set("");
        recalled = -1;
        menus.Show(pane);
        pane.Focus = Edit;
    }

    /// <summary>Put away, the line cleared.</summary>
    public void Close()
    {
        Pane.Edits[Edit].Set("");
        menus.Hide(Pane);
    }

    /// <summary>Whether a pane is the console.</summary>
    public bool Owns(MenuPane pane) => ReferenceEquals(pane, menus.Pane(File));

    /// <summary>Enter runs the line; Escape closes.</summary>
    public void Key(MenuKey key)
    {
        if (key == MenuKey.Enter) Run(Pane.Edits[Edit].Text);
        else if (key == MenuKey.Escape) Close();
    }

    /// <summary>
    /// A typed character, before the field sees it: the grave key closes, [ and ] walk the
    /// history, Tab completes. Answers whether it was the console's.
    /// </summary>
    public bool Char(char c)
    {
        var edit = Pane.Edits[Edit];
        switch (c)
        {
            case '`': Close(); return true;
            case '[' when history.Count > 0:
                recalled = recalled + 1 >= history.Count ? 0 : recalled + 1;
                edit.Set(history[recalled]);
                return true;
            case ']' when history.Count > 0:
                recalled = recalled - 1 < 0 ? history.Count - 1 : recalled - 1;
                edit.Set(history[recalled]);
                return true;
            case '[' or ']': return true;
            case '\t':
                string typed = edit.Text.Trim().ToLowerInvariant();
                if (typed.Length > 0 && OriginalCommands.Concat(Commands.Keys).FirstOrDefault(n =>
                        n.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) is { } name)
                    edit.Set(name);
                return true;
        }
        return false;
    }

    /// <summary>
    /// A line run: into the history, the console closed, the command found and called.
    /// Answers whether a command ran.
    /// </summary>
    public bool Run(string line)
    {
        string kept = line.Length > LineLength - 1 ? line[..(LineLength - 1)] : line;
        if (history.Count == 0 || history[0] != kept)
        {
            history.Insert(0, kept);
            if (history.Count > HistoryLines) history.RemoveAt(history.Count - 1);
        }
        recalled = -1;
        Close();
        // The command word runs to the first of `, \t\n\r` and is lower-cased;
        // the rest of the line after that one character, up to a line end, is the
        // command's argument — kept whole in Rest for the few that take text as it is, and split at
        // spaces and tabs for the ones that `sscanf` it.
        int start = 0;
        while (start < line.Length && WordEnd.Contains(line[start])) start++;
        int end = start;
        while (end < line.Length && !WordEnd.Contains(line[end])) end++;
        if (end == start) return false;
        string name = line[start..end].ToLowerInvariant();
        Rest = end < line.Length ? line[(end + 1)..].Split('\n', '\r')[0] : "";
        if (Commands.TryGetValue(name, out var run))
        {
            if (run(Rest.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)) is { Length: > 0 } said) Post(said);
            return true;
        }
        if (Array.IndexOf(OriginalCommands, name) >= 0)
        {
            Post($"{name}: 1.1 has this command and Vitomir does not yet");
            return false;
        }
        Post(Unrecognized);
        return false;
    }

    /// <summary>What ends the command word.</summary>
    const string WordEnd = ", \t\n\r";

    /// <summary>The line after the command word, as typed, for the command being run (<c>strtok(NULL, "\n\r")</c>).</summary>
    public string Rest { get; private set; } = "";

    /// <summary>`commands`: every name in the table, one line each.</summary>
    public IEnumerable<string> Names() => Commands.Keys.OrderBy(n => Array.IndexOf(OriginalCommands, n.ToLowerInvariant()));
}
