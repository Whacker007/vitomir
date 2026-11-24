// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// When a rollover's lines are written: on a change of hover (1.1), or every frame (Fix `liverollover`).
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **When the rollover boxes are written** — 1.1's rule, and the Fix <c>liverollover</c> that
/// keeps them current instead.
///
/// 1.1 writes a rollover only when the hover changes. Each frame the interface's hover picks what is
/// under the pointer and runs a control's rollover when it is not last frame's control,
/// and a world thing's when it is not last frame's thing;
/// the only other writer is the hover's refresh, which re-runs whichever is up for a few
/// events: the hovered thing hurt (the damage calls it when the damaged actor is the hovered thing),
/// an item identified, and the character sheet's, the discipline pane's and the
/// pack's own actions. So a party portrait's `Health: … Blood: … Frenzy: …` says what the member had
/// when the pointer came onto it — feeding with the pointer resting there, the blood does not climb.
/// 1.1 does the same: it updates only when the pointer moves
/// off and back over, which is a rendering bug.
///
/// With <see cref="Fixes.LiveRollover"/> on, every rollover is rebuilt **every frame** while the
/// pointer rests — cheap, a few strings — so each shows what is true now: the party portraits, the
/// item icons in the pack and the vault, the discipline pane's rows and the quick slots, the raise
/// buttons' prices, the shop's shelf, and the world hover (a character's name tinted by its health,
/// an item's name). **A candidate to become permanent** (a Fix for now, flagged
/// as a candidate to be always on). Ours.
/// </summary>
public static class LiveRollover
{
    /// <summary>
    /// This frame's lines for a control's rollover: 1.1's — the lines as first written while the
    /// hover's key is the same (<see cref="IconRollover.OnChange"/>) — or, with the fix, the
    /// <paramref name="fresh"/> ones, carrying the old key object over so the large box's 1.5 s wait
    /// (which watches the key by reference) is not re-armed by the rebuild.
    /// </summary>
    public static (IReadOnlyList<RolloverLine> Small, IReadOnlyList<RolloverLine> Large, bool LargeNow, object Key)? Keep(
        (IReadOnlyList<RolloverLine> Small, IReadOnlyList<RolloverLine> Large, bool LargeNow, object Key)? now,
        (IReadOnlyList<RolloverLine> Small, IReadOnlyList<RolloverLine> Large, bool LargeNow, object Key)? fresh,
        Fixes? fixes = null)
    {
        if (!(fixes ?? Fixes.Current).LiveRollover) return IconRollover.OnChange(now, fresh);
        return now is { } was && fresh is { } next && Equals(was.Key, next.Key) ? next with { Key = was.Key } : fresh;
    }
}

/// <summary>
/// **The world hover's boxes**, written as the hover writes them: when the thing under the pointer
/// changes, and again when that thing is hurt — which this sees as
/// a fall in its health, ours — or an item under it is identified. A
/// character's name is tinted by its health, so in 1.1 a hovered character that heals keeps the
/// colour it had when the pointer came onto it. With <c>liverollover</c> (<see cref="Fixes.LiveRollover"/>)
/// the lines are written every frame.
/// </summary>
public sealed class WorldHover
{
    WorldThing? thing;
    float health;
    bool identified;
    RolloverLine? name, owner;
    IReadOnlyList<RolloverLine> detail = [];

    /// <summary>The small box's second line for the thing last written: a prop's owner in a network game (<see cref="Rollover.Owner"/>).</summary>
    public RolloverLine? Owner => owner;

    /// <summary>The small box's line and the large box's lines for <paramref name="hovered"/>, this frame.</summary>
    public (RolloverLine? Name, IReadOnlyList<RolloverLine> Detail) For(WorldThing? hovered, Strings? text,
                                                                      WorldThing? user, NedFile? effects, bool live)
    {
        float nowHealth = hovered?.Actor?.Health ?? 0f;
        bool nowIdentified = hovered?.Item is { } item && (item.Flags & Rpg.ItemFlags.Identified) != 0;
        if (live || !ReferenceEquals(hovered, thing) || nowHealth < health || nowIdentified != identified)
        {
            thing = hovered;
            name = Rollover.Name(hovered, text, user);
            owner = Rollover.Owner(hovered, text);
            detail = Rollover.Detail(hovered, text, effects);
        }
        (health, identified) = (nowHealth, nowIdentified);
        return (name, detail);
    }
}
