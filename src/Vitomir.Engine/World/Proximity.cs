// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The proximity trigger's question: who is near enough to have touched it.
using System.Numerics;

namespace Vitomir.Engine.World;

/// <summary>
/// **Who a `THING_FLAG_PROXIMITYTRIGGER` thing has been touched by** — the query the proximity test asks, once
/// in sixteen frames, for every thing with the flag (the thing tick calls it; the scripts
/// hear `touched` for each answer, `Scripting/CodexScenes.Callbacks.cs`).
///
/// **The reach is the template's `PROXRADIUS` (tag 234), not the body's radius.**
/// The test hands the thing's trigger mask to the query, and the template setter fills it from
/// the tag, a negative value taken as 0 — "the radius at which an object will trigger a touched
/// message" (`noteditortags.txt`). The shipped triggers are not small: the placed `proxTrigger`s
/// reach 200 with a body of 20, the spider's egg sac 250 with 33, the satchel charge (and the thrown
/// one) 128 with 6. Earlier, Vitomir asked with the body's radius, so a satchel charge went off
/// only under someone's feet and an egg sac burst only when walked into (a comparison with 1.1).
///
/// **Only characters touch, and never the trigger itself.** The query is reset and then
/// given two things the first reading missed: the type mask
/// <c>0xffffff7e</c>, which leaves only types 0 and 7, the actors
/// and the players — the clear shot's mask (<see cref="Rpg.ClearShot.TypeMask"/>) — and the trigger
/// as the thing left out. The dead are not passed over (flag 4 is not
/// set). Earlier, Vitomir sent `touched` for every thing in reach, the trigger first of all, so a
/// satchel charge (whose script explodes at any toucher) went off as soon as it was armed.
///
/// It asks from the thing's own sector: a trigger whose body is a cylinder (collide
/// 2) as a cylinder of that reach and its own height, anything else as a
/// sphere of that reach. The flood and each flooded sector's things follow,
/// through the filter (the type mask, the trigger, `DISABLECOLLIDE`) and the shape pairs
/// of <see cref="Bodies.Hit"/>. The answers are handed out **nearest first**, the first found among
/// equals.
/// </summary>
public static class Proximity
{
    /// <summary>How often a trigger looks: on the frames where its GUID plus the frame count is a multiple of sixteen (<c>&amp; 0xf</c>).</summary>
    public const int Every = 16;

    /// <summary>The things touching <paramref name="trigger"/> now, nearest first.</summary>
    public static List<WorldThing> Touching(Location place, WorldThing trigger)
    {
        if (place.Props is not { } bodies) return [];
        var at = trigger.Position;
        float reach = trigger.ProxRadius;
        // Collide type 2 asks as a cylinder, anything else as a sphere.
        var shape = trigger.Collide == CollideShape.Cylinder ? CollideShape.Cylinder : CollideShape.Sphere;
        var sectors = bodies.SectorsNear(at, reach, (int)trigger.Sector);
        var flooded = new HashSet<int>(sectors);
        var met = new List<(WorldThing Thing, float Distance, int Order)>();
        foreach (var t in bodies.In(sectors))
        {
            if (!flooded.Contains((int)t.Sector) || t.Removed) continue;           // ours: a thing on its way out
            if (t.Type is not (ThingType.Actor or ThingType.Player)) continue;     // the mask 0xffffff7e
            if (ReferenceEquals(t, trigger)) continue;                             // left out
            if ((t.Flags & WorldThing.Flag.DisableCollide) != 0) continue;
            if (!Bodies.Hit(shape, at, reach, trigger.Height, t, out float d)) continue;
            met.Add((t, d, met.Count));
        }
        // The query's results, asked until it answers nothing: the nearest not yet handed out, the first among equals.
        return [.. met.OrderBy(m => m.Distance).ThenBy(m => m.Order).Select(m => m.Thing)];
    }
}
