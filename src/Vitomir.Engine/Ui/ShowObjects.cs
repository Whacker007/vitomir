// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The show-objects key (ours): held, every object on screen that the
// pointer could land on glows as it would under the pointer. 1.1 has nothing like it — its draw lights
// exactly one thing, the hovered one (the thing draw passes flag 2 only for it).
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The show-objects key** (ours): while it is held, every object on screen that the player could
/// interact with glows in the colour it would take under the pointer — items lying on the ground,
/// chests and barrels, doors and exits, levers and switches, usable props, books: a kind of debug or cheat key that,
/// while it is held down, highlights all of the objects visible
/// on the screen with the proper glow colour, and no living things: no enemies,
/// characters, party members, summoned things, ghosts, pets, golems and the like.
///
/// **Nothing in 1.1 does this.** The hover glow (<see cref="HoverGlow"/>) is set on one thing only:
/// the thing draw passes flag 2 to the model's lighting when the thing is the hovered
/// one, and for nobody else; no key and no console command reaches it. What *is* 1.1's is every part
/// of the answer this reuses:
/// <list type="bullet">
/// <item>**which things** — what the pointer's ray can meet at all (a thing with a body,
/// <see cref="Pick.Clickable"/>), and then the pointer's own gate, <see cref="Highlight.Offers"/>:
/// never a thing being removed or one the author marked `THING_FLAG_NOHIGHLIGHT`, a secret thing only to
/// a perceptive character, only what the player can notice, and only what the
/// verb (<see cref="Rpg.Cursor.Verb"/>) offers something on — so an opened chest, which offers
/// nothing, does not glow, and the raising doors of the first monastery (`NO HIGHLIGHT`) do not
/// either;</item>
/// <item>**which colour** — the hover's table (<see cref="Highlight.Colour"/>), shown as the
/// reference build shows it or, with the Fix <c>glowcolours</c>, as 1.1 asks
/// (<see cref="HoverGlow.Shown"/>);</item>
/// <item>**how it glows** — the same fade, tint and lift as the hover (the model's update, the renderer's
/// Each shown thing carries the hover bit while the key is held.</item>
/// </list>
///
/// **What is ours** is the key, and two rules on top of the gate:
/// <list type="number">
/// <item>**Nothing alive or acting** (<see cref="IsCharacter"/>): not a thing whose type is a
/// character (<see cref="ThingType.Actor"/>, <see cref="ThingType.Player"/>) and not a thing with an
/// actor record — which is every enemy, townsperson and party member, and every summoned thing,
/// ghost, golem, animal and shape, dead or alive — and not anything riding one (a torch or a weapon
/// in a hand, whose glow would be its holder's anyway). The pointer itself still lights them as 1.1
/// does; only the key leaves them out. **One kind is lit after all** (<see cref="TakenLikeAnItem"/>):
/// a living animal you pick up — in 1.1's data the feeder rats (`Rat_small`, `Rat_small_wander`: animal,
/// pick-up and neutral), whose click makes a vitae item in the inventory (`FeederRat.clicked`). The
/// hostile rats, the pet rat, the cats and the horses have no pick-up flag and stay unlit.</item>
/// <item>**Only what is on screen** (<see cref="OnScreen"/>): the thing's sector reached by the frame's
/// portal walk (<see cref="PortalView"/> — the renderer's own, so what the key lights is
/// what the frame draws) and its bounds inside the view. A thing behind a wall in a sector the walk
/// did reach passes, and its tint is hidden by the wall as its model is (the tint pass is depth
/// tested).</item>
/// </list>
///
/// It is presentation only: it reads the world and writes nothing into it — no flag, no roll, no
/// thing's state — so a replay runs the same with the key held or not.
/// </summary>
public static class ShowObjects
{
    /// <summary>
    /// The least radius a thing's bounds are taken to have for the view test (ours, in world units):
    /// an item on the floor may have no collision radius at all, and its model still has a size.
    /// </summary>
    public const float MinRadius = 32f;

    /// <summary>
    /// Whether <paramref name="thing"/> is alive or acting (ours): a character by its type, or anything
    /// with an actor record — the summoned, the ghosts, the golems, the animals, a shape. The key never
    /// lights one of these.
    /// </summary>
    public static bool IsCharacter(WorldThing thing) =>
        thing.Type is ThingType.Actor or ThingType.Player || thing.Actor is not null;

    /// <summary>
    /// Whether <paramref name="thing"/> is a living animal the player picks up (ours): an actor, not dead, with both
    /// <c>THING_AF_ANIMAL</c> and <c>THING_AF_PICKUP</c> — the feeder rats, which the key lights as the item they become.
    /// </summary>
    public static bool TakenLikeAnItem(WorldThing thing) =>
        thing.Type == ThingType.Actor && thing.Actor is { } a && !a.Has(Actor.Flag.Dead)
        && a.Has(Actor.Flag.Animal) && a.Has(Actor.Flag.Pickup);

    /// <summary>What <paramref name="thing"/> rides, all the way down, or itself.</summary>
    public static WorldThing Root(WorldThing thing)
    {
        var t = thing;
        for (int hops = 0; hops < 16 && t.AttachedTo is { } under && !ReferenceEquals(under, t); hops++) t = under;
        return t;
    }

    /// <summary>
    /// Whether the key lights <paramref name="thing"/> for <paramref name="player"/>, wherever it is:
    /// in the world and drawn, not alive or acting nor riding something that is (<see cref="IsCharacter"/>) unless it is an
    /// animal picked up like an item (<see cref="TakenLikeAnItem"/>),
    /// with a body the pointer's ray can meet (<see cref="Pick.Clickable"/>: a thing that collides with
    /// nothing is never under the pointer — a candle's flame, a sound's marker), one of the pointer's things (<see cref="Highlight.Offers"/>) and one a click would do
    /// something on (the verb, not the discipline's or the scroll's reach, which is about
    /// people).
    /// </summary>
    public static bool Takes(WorldThing player, WorldThing thing)
    {
        if (!thing.InWorld || thing.ModelHidden) return false;
        if (!TakenLikeAnItem(thing) && (IsCharacter(thing) || IsCharacter(Root(thing)))) return false;
        if (!Pick.Clickable(thing, out _, out _)) return false;
        if (Rpg.Cursor.Verb(player, thing) == CursorVerb.None) return false;
        return Highlight.Offers(player, thing);
    }

    /// <summary>The colour <paramref name="thing"/> would take under the pointer (the hover's table, <see cref="Highlight.Colour"/>).</summary>
    public static uint Colour(WorldThing player, WorldThing thing) =>
        Highlight.Colour(Rpg.Cursor.Verb(player, thing), thing.Type);

    /// <summary>
    /// Whether <paramref name="thing"/> is on screen: its sector (or the one of what it rides) reached by
    /// <paramref name="walk"/> — no walk, or a thing in no sector, counts as reached, as the particles
    /// count it — and a sphere round it inside the view <paramref name="viewProjection"/> (row vectors,
    /// clip depth 0 to 1, as <see cref="FreeCamera.Projection(float)"/> builds it).
    /// </summary>
    public static bool OnScreen(WorldThing thing, Matrix4x4 viewProjection, PortalView? walk)
    {
        var root = Root(thing);
        if (walk is not null && root.Sector >= 0 && !walk.Sees((int)root.Sector)) return false;
        var (centre, radius) = Bounds(thing);
        return InView(viewProjection, centre, radius);
    }

    /// <summary>A sphere round a thing (ours): its middle, and the largest of its radius, half its height, half its box and <see cref="MinRadius"/>.</summary>
    public static (Vector3 Centre, float Radius) Bounds(WorldThing thing)
    {
        float boxHalf = (thing.BoxMax - thing.BoxMin).Length() * 0.5f * MathF.Max(thing.Scale, 0f);
        float radius = MathF.Max(MathF.Max(thing.Radius, thing.Height * 0.5f), MathF.Max(boxHalf, MinRadius));
        var centre = thing.Position + new Vector3(0f, 0f, thing.Height * 0.5f);
        return (centre, radius);
    }

    /// <summary>
    /// Whether a sphere is at least partly inside the view: the six planes of
    /// <paramref name="viewProjection"/> (row vectors, clip = point × matrix, depth 0 to 1).
    /// </summary>
    public static bool InView(Matrix4x4 viewProjection, Vector3 centre, float radius)
    {
        var m = viewProjection;
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        Span<Vector4> planes = [c4 + c1, c4 - c1, c4 + c2, c4 - c2, c3, c4 - c3];
        foreach (var p in planes)
        {
            float length = new Vector3(p.X, p.Y, p.Z).Length();
            if (length <= 0f) continue;
            if ((Vector3.Dot(new Vector3(p.X, p.Y, p.Z), centre) + p.W) / length < -radius) return false;
        }
        return true;
    }

    /// <summary>
    /// Every thing of <paramref name="things"/> the key lights this frame, with its colour: those
    /// <see cref="Takes"/> answers yes for and <see cref="OnScreen"/> too.
    /// </summary>
    public static Dictionary<WorldThing, uint> Find(IEnumerable<WorldThing> things, WorldThing player,
                                                    Matrix4x4 viewProjection, PortalView? walk)
    {
        var found = new Dictionary<WorldThing, uint>(ReferenceEqualityComparer.Instance);
        foreach (var thing in things)
            if (Takes(player, thing) && OnScreen(thing, viewProjection, walk))
                found[thing] = Colour(player, thing);
        return found;
    }

    /// <summary>
    /// A portal walk of <paramref name="place"/> from <paramref name="eye"/>, as the renderer makes one
    /// each frame (<c>ReflectionPass.Find</c>: the portal walk from the settled sector, with the camera's own
    /// tests) — for a still or a test, which have no frame's walk to hand. Null when the eye is in no sector.
    /// </summary>
    public static PortalView? Walk(Location place, Vector3 eye, Matrix4x4 view, Matrix4x4 projection)
    {
        if (place.Solid is not { } solid) return null;
        int start = PortalView.SettledStart(place.Level, solid, solid.SectorAt(eye), eye);
        if (start < 0) return null;
        var walk = new PortalView();
        walk.Walk(place.Level, start, eye, view * projection, ScreenRect.Full, PortalCamera.From(view, projection));
        return walk;
    }
}
