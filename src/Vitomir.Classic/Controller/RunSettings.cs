// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The flags that only set a run up, which a run started from the menus inherits.

namespace Vitomir;

/// <summary>
/// The flags that only set a run up — where the game is, the window, the fixes, the log — which a run started from the
/// menus inherits from the one that started it. A run given nothing else is the front end.
/// </summary>
public static class RunSettings
{
    static readonly string[] settingValued = ["--game", "--data", "--recooked", "--player-module", "--size", "--gpu", "--fix", "--qol", "--no-qol", "--log", "--log-file",
                              "--log-folder", "--log-keep", "--config", "--gamma", "--record-sound", "--frames",
                              "--frame-times", "--replay-size", "--ui-layout", "--menu-lettering", "--camera-up", "--camera-steps", "--camera-out",       // a row a frame, for every game of the session (FrameTimes)
                              // The seed a recording writes into its launch (InputTape.Launch) is a setting, not a start:
                              // without it here a recorded play went straight to the haven instead of the title screen.
                              // The streams are started once, on the first run, so carrying it changes nothing.
                              "--seed", "--ai-seed", "--l2", "--borderless",
                              // The films' program, and the network simulator a game joined or hosted from the menus takes.
                              "--ffmpeg", "--net-sim",
                              // A frame dump films whatever the run shows, the menus too: a setting, so a replay of a play begun at
                              // the title screen still begins there (as a start it went straight to the haven and left its tape at
                              // frame 0, filming a recording made from the menus).
                              "--frame-dump", "--frame-dump-fps", "--frame-dump-log"];
    // `--journal` among them (bare, not with a path), so a play-test started from the menus writes a
    // journal for every game it plays.
    static readonly string[] settingBare = ["--debug", "--no-log-file", "--no-log-console", "--mute", "--gpu-debug", "--rebuild-scripts", "--script-sources",
                            "--strict-scripts", "--no-cursor", "--journal", "--save-strict-json", "--no-prefetch",
                            "--offer-imports",
                            "--pointer-log",                                                    // whose each pointer event is, for every game of the session
                            "--window", "--fullscreen", "--no-vsync",                           // so `vitomir --window` is the front end in a window
                            // Each only sets a run up, so each alone is the front end (`--streamer` alone went straight
                            // into a new game at the haven on a Windows PC, as `--skip-intro`, `--no-video` and `--devmode`
                            // alone had since they were added; Vitomir.Modules.Classic.Tests' FrontEndSwitchesTests keeps every
                            // switch the manifest declares sorted as a setting or a start).
                            "--streamer", "--skip-intro", "--no-video", "--devmode", "--log-window", "--no-predict", "--no-upnp"];

    /// <summary>The settings among the given arguments: these, and those the host program's additions name (<see cref="RunAddition"/>).</summary>
    public static string[] Settings(string[] given)
    {
        var kept = new List<string>();
        for (int i = 0; i < given.Length; i++)
        {
            // A setting given without its value (`--borderless` with no size, then `--record`) is kept alone: it never takes
            // the next switch for its value. It did, and the next run's `--borderless --player-module --player-module <folder>`
            // read `--player-module` as a module's folder (a play-test's recording); and the switch it took was lost
            // to the front end's question, so the run went into a game at the haven instead of the title screen.
            if (Valued(given[i]) && (i + 1 >= given.Length || given[i + 1].StartsWith("--", StringComparison.Ordinal))) kept.Add(given[i]);
            else if (Valued(given[i])) { kept.Add(given[i]); kept.Add(given[++i]); }
            else if (Bare(given[i]))
            {
                kept.Add(given[i]);
                // `--journal <file>` is the same setting with its file named. Read as a start it turned a
                // replay of a play begun at the menus into a game with no menus, which left its tape at
                // frame 0 (found adding a journal to a replay of a play-test's recording).
                if (given[i] == "--journal" && i + 1 < given.Length && !given[i + 1].StartsWith("--")) kept.Add(given[++i]);
            }
        }
        return [.. kept];
    }

    static bool Valued(string flag) => Array.IndexOf(settingValued, flag) >= 0 || RunAdditions.Names(flag, a => a.ValuedSettings);

    static bool Bare(string flag) => Array.IndexOf(settingBare, flag) >= 0 || RunAdditions.Names(flag, a => a.BareSettings);

    /// <summary>Whether the arguments ask for the front end: nothing but settings, or <c>--menu</c> or <c>--credits</c>.</summary>
    public static bool FrontEndWanted(string[] given)
    {
        // `--menu-steps` (a harness's input on the menus, ours) is the front end's own: it starts nothing.
        if (Array.IndexOf(given, "--menu-steps") is var ms and >= 0) given = [.. given.Where((_, i) => i != ms && i != ms + 1)];
        return Array.IndexOf(given, "--menu") >= 0 || Array.IndexOf(given, "--credits") >= 0
            || (given.Length > 0 && Settings(given).Length == given.Length) || given.Length == 0;
    }
}
