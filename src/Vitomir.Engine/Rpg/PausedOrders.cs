// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// "This character is currently busy, can't issue new orders now." — the one line an order writes
// to the message box itself.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **An order refused while the game is paused says so**. Three orders open with the same
/// test — the attack on a thing, the attack on a point and the cast:
/// busy (<c>now &lt), dead (flag <c>0x10000000</c>) or held
/// (second flag word <c>0x80</c>) answers 2, and before it does, **when the one refused is the
/// character being steered and the game pause is up **, it
/// puts <c>RPG_CANTDOACTION</c> — "This character is currently busy, can't issue new orders now." —
/// in the message box in the box's own grey. With the clock stopped a busy character
/// stays busy, so this is what a click on the world gets between the Pause key and the next.
/// Unpaused, the same refusal is silent. The move, use, take, drop and feed orders say nothing.
/// The pause is the host's (<see cref="Session.Paused"/>: the front end's Pause key and the menus that
/// stop the clock; ours, since the engine keeps no pause of its own).
/// </summary>
public static class PausedOrders
{
    /// <summary>The string-table line.</summary>
    public const string Line = "RPG_CANTDOACTION";

    /// <summary>
    /// A refusal for being busy, dead or held (the answer 2): the line, when <paramref name="who"/>
    /// is the one being steered and the game is paused.
    /// </summary>
    public static void Busy(WorldThing who)
    {
        if (who.Place is not { Session: { } session } place || session.Paused?.Invoke() != true) return;
        if (!ReferenceEquals(place.Steered, who)) return;
        session.Messages.Post(Line);
    }
}
