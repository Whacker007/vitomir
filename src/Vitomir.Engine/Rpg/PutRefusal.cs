// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The put-down's arrival with an item that is no longer the carrier's,
// read in a comparison with 1.1 and built.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **The put-down's refusal**. The walked put-down (<see cref="Actions.Put"/>,
/// ACTION_DROP) asks at the point, not before, whether the thing it is carrying over is still an
/// item (type 1, 5 or 6) **owned or held by the carrier**. When
/// it is not — a script took it, it was used up, another pane moved it — the arrival shows the
/// **refusal cursor** with its noise (the cursor object's own: `cursorNegative` for a
/// quarter second and sound 5, <see cref="Ui.CursorState.Refuse"/>) and cancels the order,
/// unforced. Vitomir stopped in silence, and stopped at once rather than at the point.
/// </summary>
public static class PutRefusal
{
    /// <summary>Whether <paramref name="target"/> is an item this carrier owns or holds.</summary>
    public static bool Mine(WorldThing carrier, WorldThing? target) =>
        target is { Item: not null } && ReferenceEquals(target.Owner ?? target.Holder, carrier);

    /// <summary>
    /// The refusal cursor — the game's one pointer, whoever was carrying — when there is
    /// a screen to show it on.
    /// </summary>
    public static void Show(WorldThing carrier, int nowMs) => carrier.Place?.Session?.Cursor?.Refuse(nowMs);
}
