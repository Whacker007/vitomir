// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Whether a run starts with the status bar up (ours: 1.1 always has it, Vitomir's developer runs chose).
namespace Vitomir.Engine.Ui;

/// <summary>
/// Whether the status bar is up when a run starts (ours). 1.1 has no switch for it: a game always
/// starts with the bar, and only a cutscene or the advancement screen takes it down. Vitomir's
/// developer runs started without it unless asked (`--hud`) or started as a game (`--play`, a new
/// game), so `--load` and `--level` runs came up bare — which play-testing showed
/// So: **every windowed run with somebody to play starts
/// with it**; `--no-hud` starts one without it. A headless still keeps it off unless `--hud` (or
/// a game the menus start, which had it before), because the stills compared across batches were
/// all taken without it. F4 toggles it in play whatever it started as.
/// </summary>
public static class HudStart
{
    /// <param name="hud">`--hud` was given.</param>
    /// <param name="noHud">`--no-hud` was given; it wins over everything.</param>
    /// <param name="playing">A game the menus start (`--play`, `--new-game`).</param>
    /// <param name="headless">The run draws one still and quits (`--headless`).</param>
    /// <param name="hasPlayer">There is a character to steer; a free camera over a level has no bar.</param>
    public static bool On(bool hud, bool noHud, bool playing, bool headless, bool hasPlayer) =>
        !noHud && (hud || playing || !headless && hasPlayer);
}
