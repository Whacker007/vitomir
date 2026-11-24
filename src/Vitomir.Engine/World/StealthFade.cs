// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// How well the player sees a hidden character: the stealth fade, asked by the thing draw
// before each thing with a model is drawn. Read.
using Vitomir.Engine.Rpg;

namespace Vitomir.Engine.World;

/// <summary>
/// **The stealth fade** (from the thing draw, in the world and in the mirror
/// alike): how a thing looks to the viewer — the character the player is steering.
/// The thing's stealth right now (<see
/// cref="Sight.StealthOf"/>) against the viewer's perception:
/// <list type="bullet">
/// <item>no stealth: drawn as it is (1.0);</item>
/// <item>the viewer itself, or a character (type 0 or 7) the viewer counts as friendly (the side test,
/// asked of the viewer): <b>half</b> (0.5) — Christof under Obfuscate sees himself half there, and so
/// does he a hidden companion;</item>
/// <item>the Storyteller's seat: 1.0;</item>
/// <item>perception less than the stealth: <b>not drawn at all</b> (the fade answers 0 and the thing draw
/// returns — no model, no shadow, nothing riding it, no halo, no glint);</item>
/// <item>perception equal to it: <b>0.3</b> (<c>0x3e99999a</c>); one more: 0.5; two or more: 1.0.</item>
/// </list>
/// The answer goes into the model's <b>fade</b> (held to 0..1 and passed to
/// the four sub-models, so the sword fades with the hand), which the renderer multiplies into the
/// model's alpha as it draws (<c>fade × alpha</c>, drawn blended in
/// mode 4 under 1). <b>The shadow does not look at it</b>: the thing draw keeps back for the sorted pass (which
/// casts none) only a thing whose alpha is under 1, so a half-seen character casts its full
/// shadow, and an unseen one none — and, by the same test, is drawn in its place among the things,
/// blended with its depth written, not sorted in with the translucent ones
/// (<c>WorldRenderer.PassAlphaOf</c>).
/// </summary>
public static class StealthFade
{
    /// <summary>A thing half seen: the viewer itself, a friend, or one point clear (<c>0x3f000000</c>).</summary>
    public const float Half = 0.5f;
    /// <summary>A thing barely seen: perception just equal to its stealth (<c>0x3e99999a</c>).</summary>
    public const float Faint = 0.3f;

    /// <summary>
    /// The fade <paramref name="viewer"/> sees <paramref name="thing"/> with, or null when he cannot
    /// see it at all. <b>Ours:</b> with no viewer (the engine asserts one) everything is drawn.
    /// </summary>
    public static float? Of(WorldThing thing, WorldThing? viewer, bool storyteller = false)
    {
        int stealth = Sight.StealthOf(thing, moving: false);
        if (stealth == 0 || viewer is null) return 1f;
        if (ReferenceEquals(viewer, thing)) return Half;
        if (thing.Type is ThingType.Actor or ThingType.Player && Sight.Friendly(viewer, thing)) return Half;
        if (storyteller) return 1f;
        int clear = (viewer.Actor?.Perception ?? 0) - stealth;
        if (clear < 0) return null;
        return clear switch { 0 => Faint, 1 => Half, _ => 1f };
    }

    /// <summary>
    /// Whether the thing draw draws nothing of <paramref name="thing"/> in <paramref name="place"/>: the
    /// steered character cannot see it — or cannot see what it rides (a torch in a hidden hand goes
    /// with the hand: the draw of a thing's children is skipped with it).
    /// </summary>
    public static bool Unseen(WorldThing thing, Location place)
    {
        var viewer = place.Steered;
        bool seat = Seated(place);
        for (var t = thing; t is not null; t = t.AttachedTo)
            if (t.Actor is not null && Of(t, viewer, seat) is null) return true;
        return false;
    }

    /// <summary>The fade the steered character sees a thing with: 0 when not drawn at all.</summary>
    public static float Fade(WorldThing thing, Location place)
    {
        if (Unseen(thing, place)) return 0f;
        return thing.Actor is null ? 1f : Of(thing, place.Steered, Seated(place)) ?? 0f;
    }

    /// <summary>
    /// Whether the Storyteller's seat is taken (the host's `Session.Seated`) — then every hidden
    /// character is drawn whole (1.0), past the viewer's own half and a friend's (as in 1.1, and the
    /// renderer was not told the seat).
    /// </summary>
    public static bool Seated(Location place) => place.Session?.Seated?.Invoke() == true;

    /// <summary>The shadows' test for a place (<see cref="Shadows.Unseen"/>).</summary>
    public static Func<WorldThing, bool> Hides(Location place) => t => Unseen(t, place);
}
