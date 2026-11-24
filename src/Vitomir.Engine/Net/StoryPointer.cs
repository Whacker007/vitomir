// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the pointer offers in the Storyteller's seat: 1.1's pick filter, whose seat arm was first built only
// for the carried body's place (`StoryHand.Point`) — the host's own place asked the steered character's rule.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **The pointer's offer** (asked of each thing the pick meets). **In the Storyteller's
/// seat** it answers "anything but the current target"  and asks
/// nothing else — no `NO HIGHLIGHT`, no verb, no discipline — so a table, a lamp, a mug or the ghost itself
/// can be pointed at and clicked to be the target (the seat arm takes any thing but a region). Out of
/// the seat it is the steered character's own rule (<see cref="Ui.Highlight.Offers"/>: not the one steered,
/// a thing with a verb or a discipline's target). The same rule in the host's own place (Program's hover
/// pick) and in a carried body's (<see cref="StoryHand.Point"/>).
/// </summary>
public static class StoryPointer
{
    /// <summary>
    /// The thing the pick passes over and the offer, for the pointer of <paramref name="steered"/>'s machine:
    /// in the <paramref name="seat"/>, the <paramref name="target"/> and anything but it; out of it, the one
    /// steered and <see cref="Ui.Highlight.Offers"/>.
    /// </summary>
    public static (WorldThing? PassedOver, Func<WorldThing, bool> Offers) For(bool seat, WorldThing? target, WorldThing steered) =>
        seat ? (target, t => !ReferenceEquals(t, target))
             : (steered, t => Ui.Highlight.Offers(steered, t));
}
