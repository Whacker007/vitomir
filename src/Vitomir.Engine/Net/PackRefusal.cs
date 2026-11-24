// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Which of a player's pack moves the host refuses, as in 1.1.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **A player's pack move refused only for the possessed body**. 1.1's host tests, for a take (message
/// 0x13), a take-out (0x14) and a swap (0x15), that the mover is not
/// the host's steered body (the control — on a host in the seat, the Storyteller's
/// possessed one), and refuses silently when it is; a use (0x17) is not so tested. **None of them asks
/// about the Storyteller's pause** — so a player may rearrange the pack while the game is paused, as a cast
/// (<see cref="NetStoryCast"/>) is taken then too; only an order is refused for the pause
/// (`UI_STSBPAUSE`). Earlier, Vitomir's host refused every pack move during the pause and never tested the
/// possessed body. <b>Ours</b>: a move here is the pane's gesture (<see cref="PackGesture"/>), which may make a
/// take, a take-out or a swap, so every gesture but a use is tested.
/// </summary>
public static class PackRefusal
{
    /// <summary>Whether <paramref name="mover"/>'s <paramref name="kind"/> of move is refused, <paramref name="possessed"/> being the body the Storyteller has (or null).</summary>
    public static bool Refused(PackKind kind, WorldThing mover, WorldThing? possessed) =>
        kind != PackKind.Use && NetStoryCast.Refused(mover, possessed);
}
