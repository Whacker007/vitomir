// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **Using a quick slot, and refilling it from the pack**.
///
/// The steered character's quick-slot use (the strip's click and the number
/// keys): nothing for the dead; the item in the slot is found (with
/// `0x200`) and its name copied; the item is used; and **when the use answered 1** the
/// slot is refilled. The use answers 1 only when the item has **left the slot** — the last
/// charge of an item destroyed at zero, or a weapon taken into the hand — and 0 when
/// charges remain or a spent item stays behind as dead weight.
///
/// The refill itself (`BackpackAutoLoad`) is <see cref="QuickRefill"/>; what counts as
/// the use answering 1 is <see cref="QuickRefill.Left"/>.
///
/// **The AI never refills.** Its item use (<c>Minds.UseItem</c>) calls the use straight; the only
/// other caller of the refill is the network handler for a client's use, and that one only
/// for the steered character. So a companion that drinks the last potion in a quick slot leaves the
/// slot empty whatever the option says.
/// </summary>
public static class QuickSlots
{
    /// <summary>What <see cref="Use"/> answers for a dead user, which does nothing (ours: the words).</summary>
    public const string DeadSays = "nothing: the dead use nothing";

    /// <summary>
    /// The steered character uses what is in a quick slot through <paramref name="use"/> (which is
    /// the item use and answers what to say), and the slot is refilled from the pack when the use
    /// emptied it and <paramref name="autoload"/> is on.
    /// </summary>
    public static string Use(WorldThing? user, Item item, bool autoload, Func<Item, string> use)
    {
        // **Nothing at all for the dead** (flag `0x10000000` returns before the slot
        // is even looked up) — not the use, not the refill. Earlier, Vitomir skipped only the
        // refill and still used the item (a comparison with 1.1's gap).
        if (user?.Actor is { Dead: true }) return DeadSays;
        if (user?.Actor is not { } me) return use(item);
        var (cell, template) = QuickRefill.Before(item);
        string said = use(item);
        if (QuickRefill.Left(item) && QuickRefill.From(me, cell, template, autoload) is { } next)
        {
            Audio.Noise.Foley(next.Thing, Audio.FoleyEvent.Wear);
            said += $"; another {next.Name} from the pack into quick slot {cell + 1}";
        }
        return said;
    }
}
