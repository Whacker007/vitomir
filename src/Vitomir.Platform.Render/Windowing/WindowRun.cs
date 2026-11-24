// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the window code is told of the run it serves: whether it is a replay, where the window
// keeps what it keeps, and the game's log. Earlier, the window code asked the tape (`InputTape`), the engine's settings
// path (`Diag.UserSettingsPath`) and its log (`Diag.For`) itself; now the game says so in its ask (`WindowAsk`), so the
// window code needs nothing of the game's to be any program's.
using Vitomir.Platform.Render.Classic;
using Vitomir.Platform.Windowing;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// **The run the window serves, as its ask said** (<see cref="WindowAsk"/>): set when the window is asked for
/// (<see cref="From"/>), the same for every run of the process — a replay is a replay from its start to its end.
/// </summary>
static class WindowRun
{
    /// <summary>Whether the run replays a tape: its window is a plain one at its tape's size, never kept, never full screen.</summary>
    public static bool Replaying { get; private set; }

    /// <summary>Where the window keeps what it keeps (<c>[window]</c>): the game's settings.</summary>
    public static IWindowSettings Settings { get; private set; } = NoWindowSettings.Instance;

    static Action<string>? log;

    /// <summary>The run's facts taken from its ask.</summary>
    public static void From(in WindowAsk ask)
    {
        Replaying = ask.Replaying;
        Settings = ask.Settings ?? NoWindowSettings.Instance;
        log = ask.Log;
    }

    /// <summary>
    /// What the window does, said where it can be read: the console, and — made Vitomir a windowed program with
    /// no console of its own — the game's log (the game's <see cref="WindowAsk.Log"/>).
    /// </summary>
    public static void Say(string line)
    {
        Console.WriteLine(line);
        log?.Invoke(line);
    }
}
