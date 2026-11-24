// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Runtime.CompilerServices;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **`CONV_XFLAG_STOPACTORS`: everybody in the place stops walking when the conversation begins.**
/// A conversation started with the flag (<see cref="ConversationFlag.StopActors"/>, `0x100`) sets bit
/// 4 of the current world's flags word (in the begin); every walker's update
/// tests that word against 5 and **stops** and returns when it is
/// set; and the world's thing tick clears the bit once every thing in it has been ticked (the tick's
/// tail, `&amp; 0xfffffffb`). So every walker ticked in the rest of that thing tick — or in the next
/// one, for a conversation begun outside it — stops, whoever it belongs to: the AI's, a script's,
/// the player's. A walker still in one of its waits returns before the test and
/// walks on.
///
/// Codex.java defines the flag and no shipped script passes it (it is unreachable
/// with the shipped files); a mod's script can. Kept per place (the flags word is the world's), in a
/// table of its own so the shared <see cref="Location"/> need not carry it. The word's bit 1, which
/// the same test reads, is not traced.
/// </summary>
public static class ConversationStop
{
    static readonly ConditionalWeakTable<Location, object> up = new();
    static readonly object Mark = new();

    /// <summary>The bit set.</summary>
    public static void Raise(Location place) => up.AddOrUpdate(place, Mark);

    /// <summary>Whether a walker ticked now stops.</summary>
    public static bool Up(Location place) => up.TryGetValue(place, out _);

    /// <summary>The bit cleared, at the end of the place's thing tick.</summary>
    public static void Lower(Location place) => up.Remove(place);
}
