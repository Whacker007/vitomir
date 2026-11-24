// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The host's two keys in the game camera's key hook (its host-only branch), read: `\` takes and leaves the Storyteller's seat, `.` in the seat gives the
// host their own character back. Neither was built before; a comparison with 1.1 had read `\` as the chat box's.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>The two physical keys the host's branch of the key hook reads (DirectInput's scan codes, as SDL's are).</summary>
public enum SeatKey
{
    None,
    /// <summary>The \ key (`DIK_BACKSLASH` 0x2B): the seat taken, or left.</summary>
    Seat,
    /// <summary>The . key (`DIK_PERIOD` 0x34) — not the keypad's: back to the host's own character.</summary>
    Home,
}

/// <summary>
/// **The host's keys**. The branch is read **on a host only**
/// (net mode 2) and under the whole hook's gates (the game camera with no dialog, vault, advancement
/// screen, shop or chat line up — <see cref="ChatKeys.Read"/>). Each key is a press (the key's state stamped with
/// this frame) and plays the interface sound 0 first, whatever follows:
/// <list type="bullet">
/// <item>**\**: when the status bar's current menu is the Storyteller's (the bar's current menu is its Storyteller menu) the seat is
/// left (the multiplayer bar back, the control made the target), otherwise it is taken
/// (only on the network bar, with no vault up and in a game made with Storyteller moderation, game flag
/// 0x400; without it nothing happens). So it is the same as the multiplayer bar's Storyteller button and the
/// Storyteller bar's character button, from the keyboard.</item>
/// <item>**.**: only **in the seat** and only when the host has a character of their own
/// (`*`, slot 0's): <see cref="Home"/>.</item>
/// </list>
/// </summary>
public static class SeatKeys
{
    /// <summary>The interface sound each key plays.</summary>
    public const int Sound = 0;

    /// <summary>Which of the host's keys a press is, from its physical key.</summary>
    public static SeatKey Of(bool backslash, bool period) =>
        backslash ? SeatKey.Seat : period ? SeatKey.Home : SeatKey.None;

    /// <summary>Whether the branch is read at all: a host's (net mode 2), under the hook's gates (<see cref="ChatKeys.Read"/>).</summary>
    public static bool Read(bool host, bool hookReads) => host && hookReads;

    /// <summary>
    /// **.** in the seat, with <paramref name="inSeat"/> the seat's flag:
    /// the host's own character (<c>*</c>) becomes **the control** (the white
    /// shell flashed over it for 0x5000 ms, and its panes) — a
    /// body the Storyteller possessed, here or carried to another place, is let go — then **the target**
    /// (set directly), and **the camera** goes to it; the ghost, when it is in
    /// the world and is not the host's character, is taken out of it (tested before and
    /// after the target changes, which comes to that). The Storyteller's panes are redone by the caller.
    /// Resetting the cursor is not needed: in the seat the multiplayer bar and its
    /// discipline pane are down, so no discipline is being dragged. False, doing nothing, out of the seat or with
    /// no character of the host's (a host who does not play).
    /// </summary>
    public static bool Home(StoryPowers powers, bool inSeat, WorldCamera? camera)
    {
        if (!inSeat || powers.Server.Slots.Count == 0 || powers.Server.Slots[0]?.Avatar is not { } own) return false;
        if (powers.Server.Possessed is { } held && !ReferenceEquals(held, own))
        {
            StoryHand.LetGo(held);                                                   // one control for the game
            powers.Server.Possessed = null;
        }
        own.Place?.SetSteered(own);                                                  // the control
        powers.Clicked(own, own, camera);                                            // the target, the ghost, the camera
        return true;
    }
}
