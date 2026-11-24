// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Space in the game camera stops the character you steer.
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;

namespace Vitomir.Engine.World;

/// <summary>
/// **Space stops the character you steer.** The game camera's keys test Space on the
/// frame it goes down twice: first the click and, in a conversation, the
/// line skipped; then — in a conversation or not — the click again and the stop, which
/// cancels the steered character's order **unforced** (a character mid-swing,
/// busy, dead or held keeps what it is doing) and stops its walk whatever the cancel did.
/// On a network client the stop first sends the host order 0 for it and then does the
/// same on its own machine — it does not return after the send, as the other orders do.
///
/// Only the game camera reads it: the look, the free camera, a camera path and a cutscene have key
/// handlers of their own, and none of them calls the stop. Space held with a click is the
/// click's own hold modifier (bit 8 of the click's mask), which the press before it does
/// not change: a character asked to hold an attack is stopped first, then given the attack.
/// The two clicks go to the interface's one flat channel in the same frame, so one is heard —
/// 1.1 plays one `ui_buttonpress` on channel 0 for every line Space skips;
/// Vitomir plays it once, here.
/// </summary>
public static class StopKey
{
    /// <summary>
    /// Space went down in the game camera. Answers the character that was stopped, or
    /// null when nobody is steered (1.1 asserts there is always one).
    /// </summary>
    public static WorldThing? Press(Location place)
    {
        Audio.Noise.Ui(place, Audio.FoleyEvent.ButtonDepress);
        if (place.Steered is not { } who) return null;
        // A client sends the host order 0 for the controlled thing — and carries on.
        NetOrders.Forward(who, OrderKind.Stop);
        Actions.Cancel(who);
        Actions.StopWalking(who);
        return who;
    }
}
