// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Whether Ctrl+P takes the game camera into the free camera.
using Vitomir.Engine.Net;
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.World;

/// <summary>
/// **Ctrl+P's way into the free camera** — the camera key's test, with the QoL <c>freecamera</c> standing beside the
/// developer switch. 1.1 reads its developer keys only with Ctrl held (set by the frame input from the
/// key-state array's LCtrl and RCtrl) and the options word's **bit 0x80** (<c>-devmode</c> on the
/// command line), in a game that is **not a player's machine** (single player or
/// a host) and, on a host (mode 2), only one made with **Storyteller moderation** (game flag <c>0x400</c>);
/// then P (its entry of the array on this frame) sets the camera's builder to the free camera's,
/// clears the steered character's first-person bit and puts the camera in mode 3. Vitomir opens it with
/// <c>[qol] freecamera</c> (<i>"if it's on, then the player can press ctrl p and use that
/// mode. Off, that free camera mode doesn't work."</i>) **or** <c>--devmode</c> (<see cref="GameOptions.DevMode"/>,
/// 1.1's own switch) — and the rest of 1.1's test stands: never on a player's machine, and on a host only in a
/// moderated game. <c>--debug</c> no longer opens it. Ctrl+P **out** of the free camera is the free camera's own
/// and needs none of this.
/// </summary>
public static class FreeCameraGate
{
    /// <summary>
    /// Whether Ctrl+P goes into the free camera: <paramref name="qol"/>'s <c>freecamera</c> or the options word's
    /// <see cref="GameOptions.DevMode"/> (bit <c>0x80</c>), not a player's machine
    /// (<paramref name="client"/>), and on a host (<paramref name="host"/>, its game flags;
    /// null in single player) only a moderated game (0x400).
    /// </summary>
    public static bool Opens(Qol qol, uint options, bool client, GameFlags? host) =>
        (qol.FreeCameraKey || (options & GameOptions.DevMode) != 0)
        && !client
        && (host is not { } flags || (flags & GameFlags.Moderated) != 0);
}
