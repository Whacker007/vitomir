// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `--console-run` in a windowed run: the lines run through the developer console once the place is up.
using Vitomir.Engine.Ui;

namespace Vitomir;

/// <summary>
/// <c>--console-run "line;line"</c> in a **windowed** run (ours). Earlier, only a headless
/// still ran the lines — its one frame is drawn after them — and a windowed run read the flag only
/// to let the grave key open the console, so a <c>--load SP3 --console-run "sethide 3"</c> did
/// nothing that could be seen. Here they run through the same <see cref="DevConsole.Run"/> a typed line
/// takes, once, on frame <see cref="Frame"/> — or the first frame after it that no film holds the
/// screen, since a film stops the game and a line run under it would be spent before the player
/// sees the place.
///
/// **Why that frame.** By then the place has been simulated and drawn and its scripts have heard
/// `beginscene`, and — the reason it is not the harness's tenth — every particle emitter registered
/// before the first portal walk gave the renderer a view has dropped out of the 128-slot live list
/// (<see cref="Engine.Particles.ParticleSystem.UnseenFrames"/>). Where Christof stands in
/// `SP3.vsave` (the monastery) the list is full until then — 65 candle flames, 19 medium flames and 8
/// others he can see, and 34 `flametest` and 2 weapon trails registered on the first frame and never
/// seen again — so `damageme 10 4` run on frame 10 or 62 made ten or eleven `syssfx_firedamage`
/// flames and not one of them got a slot: the burn was there and nothing showed it.
/// </summary>
sealed class ConsoleRun(string[] lines)
{
    /// <summary>The lines of the argument, or null when there is none.</summary>
    public static ConsoleRun? From(string? arg)
    {
        if (arg is null || arg.StartsWith("--", StringComparison.Ordinal)) return null;
        var lines = arg.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? null : new ConsoleRun(lines);
    }

    /// <summary>
    /// The frame the lines run on, once the place is up (ours). 70, past the particle list's
    /// 60-frame drop-out, because the first frame had filled the list with every flame in the place;
    /// the first frame registers only what its walk reaches (<c>ParticleSystem.AwaitingWalk</c>),
    /// and a `damageme` fire in Monastery 1 finds its slots at frame 10.
    /// </summary>
    public const int Frame = 10;

    bool done;

    /// <summary>Runs the lines, once, when their frame has come.</summary>
    public void Pump(int frame, bool filmPlaying, DevConsole? console)
    {
        if (done || frame < Frame || filmPlaying || console is null) return;
        done = true;
        foreach (var line in lines)
        {
            Console.WriteLine($"  console-run: {line}");
            console.Run(line);
        }
    }
}
