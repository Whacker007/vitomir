// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the diagnostic overlay says. This is the *content*; `Vitomir.Render.Overlay` is the only
// thing that knows it ends up as triangles, and the engine keeps no reference to it.
//
// It exists because an AI bug was once chased for a long time that turned out to
// be the player having died with nothing on screen to say so. A level where nothing is moving
// looks identical whether the AI has stalled, everybody is out of ammo, the player is dead, or
// nothing has noticed anybody yet - and those want completely different fixes. This tells them
// apart at a glance.
//
// It is a developer tool and deliberately not the HUD: it shows internal state in internal words.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Diagnostics;

/// <summary>How a character reads at a glance. The overlay picks a colour from it.</summary>
public enum WatchTint
{
    /// <summary>Alive, minding its own business.</summary>
    Idle,
    /// <summary>The player, or somebody in the party.</summary>
    Friend,
    /// <summary>Hostile and hunting: it holds a target.</summary>
    Foe,
    /// <summary>Hostile but has not found anybody.</summary>
    Looking,
    /// <summary>Out of it — dead, or the AI cannot run.</summary>
    Gone,
}

/// <summary>One character's floating label.</summary>
/// <param name="At">Where to put it: the top of the body, in world space.</param>
/// <param name="Health">0..1, for the bar. Negative when the thing has no health to show.</param>
public readonly record struct WatchLabel(WorldThing Thing, Vector3 At, IReadOnlyList<string> Lines,
                                         float Health, WatchTint Tint);

/// <summary>A frame's worth of overlay: the corner panel, and a label for each character in range.</summary>
public sealed record WatchReport(IReadOnlyList<string> Panel, IReadOnlyList<WatchLabel> Labels);

/// <summary>Reads the live world into something the overlay can draw.</summary>
public static class Watch
{
    /// <summary>How far from the player a character still gets a label.</summary>
    public const float LabelRange = 2500f;

    /// <summary>At most this many labels, nearest first, so a crowded scene stays readable.</summary>
    public const int LabelLimit = 20;

    /// <summary>Where a label floats when the thing's template gives no height.</summary>
    public const float DefaultHeight = 72f;

    public static WatchReport Look(Location place, Simulation sim,
                                   float range = LabelRange, int limit = LabelLimit) =>
        new(Panel(place, sim), Labels(place, sim, range, limit));

    // --- the corner panel ------------------------------------------------------------------

    static List<string> Panel(Location place, Simulation sim)
    {
        var lines = new List<string>
        {
            $"{place.Name}{(place.SceneFile.Length > 0 ? " / " + place.SceneFile : "")}   t={sim.Seconds:0.0}s",
        };

        if (place.Player?.Thing is { } me)
        {
            lines.Add("");
            foreach (var line in Character(me, place.Player, detailed: true)) lines.Add(line);
        }

        var minds = place.Minds;
        if (minds is null) { lines.Add(""); lines.Add("ai: off"); return lines; }

        // The party, which is the other half a player is usually wondering about.
        var party = minds.All
            .Where(m => m.Thing.Actor is { } a && a.Has(Actor.Flag.Party) && !ReferenceEquals(m.Thing, place.Player?.Thing))
            .ToList();
        if (party.Count > 0)
        {
            lines.Add("");
            lines.Add($"party ({party.Count})");
            foreach (var m in party) foreach (var line in Character(m.Thing, m.Walker, detailed: false)) lines.Add("  " + line);
        }

        // And the room, so "nothing is moving" can be told from "nothing is there".
        int alive = 0, dead = 0, targeted = 0, moving = 0;
        foreach (var m in minds.All)
        {
            if (m.Thing.Actor is not { } a) continue;
            if (a.Dead) dead++; else alive++;
            if (m.Target is not null) targeted++;
            if (m.Walker.Speed > 0f) moving++;
        }
        lines.Add("");
        lines.Add($"minds {alive} live, {dead} dead   {targeted} aimed, {moving} moving   blows {sim.Blows.Count}");
        var byMode = minds.All.Where(m => m.Thing.Actor is { Dead: false })
                          .GroupBy(m => m.Mode).OrderByDescending(g => g.Count()).Take(6)
                          .Select(g => $"{g.Key}:{g.Count()}");
        lines.Add("  " + string.Join("  ", byMode));
        return lines;
    }

    /// <summary>One character as the panel lists it: two lines in detail, one otherwise.</summary>
    static IEnumerable<string> Character(WorldThing thing, Player? walker, bool detailed)
    {
        var a = thing.Actor;
        string name = Name(thing);
        if (a is null) { yield return name; yield break; }

        string state = a.Dead ? "DEAD" : $"hp {a.Health:0}/{a.MaxHealth:0}";
        string blood = a.Stat[(int)ActorStat.BloodPool] > 0f
            ? $"  blood {a.Stat[(int)ActorStat.Blood]:0}/{a.Stat[(int)ActorStat.BloodPool]:0}" : "";
        var mind = thing.Mind;
        string mode = mind is null ? "no mind" : mind.Mode.ToString().ToLowerInvariant();
        string target = mind?.Target is { } t ? $" -> {Name(t)}" : "";

        yield return detailed
            ? $"{name}   {state}{blood}"
            : $"{name}  {state}  {mode}{target}";
        if (!detailed) yield break;

        string action = a.Action == ActionMode.None ? "" : $"  {a.Action.ToString().ToLowerInvariant()}";
        string pace = walker is null ? "" : walker.Speed > 0f ? $"  {(walker.Running ? "running" : "walking")} {walker.Speed:0}" : "  still";
        yield return $"  {mode}{target}{action}{pace}  node {walker?.Node ?? -1}  stance {a.Stance.ToString().ToLowerInvariant()}";
    }

    // --- the floating labels ---------------------------------------------------------------

    static List<WatchLabel> Labels(Location place, Simulation sim, float range, int limit)
    {
        var labels = new List<WatchLabel>();
        if (place.Minds is not { } minds) return labels;
        var from = place.Player?.Thing.Position ?? Vector3.Zero;

        foreach (var mind in minds.All
                     .Where(m => ReferenceEquals(m.Thing, place.Player?.Thing)
                                 || Vector3.Distance(m.Thing.Position, from) <= range)
                     .OrderBy(m => Vector3.Distance(m.Thing.Position, from))
                     .Take(limit))
        {
            var thing = mind.Thing;
            var a = thing.Actor;
            float top = thing.Height > 0f ? thing.Height : DefaultHeight;
            var lines = new List<string> { Name(thing) };

            if (a is not null)
            {
                lines.Add(a.Dead ? "dead" : $"hp {a.Health:0}");
                string mode = mind.Mode.ToString().ToLowerInvariant();
                string target = mind.Target is { } t ? $" -> {Name(t)}" : "";
                string action = a.Action == ActionMode.None ? "" : $" {a.Action.ToString().ToLowerInvariant()}";
                if (!a.Dead) lines.Add(mode + target + action);
            }

            labels.Add(new WatchLabel(thing, thing.Position + new Vector3(0f, 0f, top), lines,
                                      a is { MaxHealth: > 0f } ? Math.Clamp(a.Health / a.MaxHealth, 0f, 1f) : -1f,
                                      TintFor(thing, mind, place)));
        }
        return labels;
    }

    static WatchTint TintFor(WorldThing thing, Mind mind, Location place)
    {
        var a = thing.Actor;
        if (a is null || a.Dead) return WatchTint.Gone;
        if (ReferenceEquals(thing, place.Player?.Thing) || a.Has(Actor.Flag.Party)) return WatchTint.Friend;
        if (a.Has(Actor.Flag.AiPaused)) return WatchTint.Gone;
        // Friendly to the player, or one of the two flags every search steps over: not a threat.
        if (place.Player?.Thing is { } me && Sight.Friendly(thing, me)) return WatchTint.Idle;
        if ((a.Flags & Minds.LeaveAloneFlags) != 0) return WatchTint.Idle;
        return mind.Target is null ? WatchTint.Looking : WatchTint.Foe;
    }

    /// <summary>What to call a thing: the name a script would know it by, else its template.</summary>
    public static string Name(WorldThing thing) =>
        thing.Actor?.Name is { Length: > 0 } named ? named
        : thing.Actor?.CastId is { Length: > 0 } cast ? cast
        : thing.Template;
}
