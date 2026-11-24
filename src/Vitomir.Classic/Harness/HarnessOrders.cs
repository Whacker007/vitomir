// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Two harness knobs for looking at a discipline's work and a thing's use without a hand on the mouse:
// `--cast` and `--use`. Ours: nothing in 1.1 resembles them.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir;

/// <summary>
/// <c>--cast &lt;discipline&gt;</c> orders the steered character to cast a discipline it knows, at
/// once, the way a right click on the ground does with Space held (<see cref="Cast.Order"/>) — the
/// order, the animation, the `Cast` key and the shipped script's `cast`, all of it the game's;
/// <c>--cast &lt;discipline&gt;@&lt;template&gt;</c> casts it at the nearest living thing of that
/// template, as a right click on it does (<c>--cast Feed@cappadocian</c>).
/// <c>--use &lt;template&gt;[@seconds]</c> orders it, that far into <c>--settle</c>, to use the
/// nearest thing of that template — the order a click on the thing gives (<see cref="Actions.Use"/>),
/// walk and all. Together: <c>--disciplines WalkTheAbyss --cast WalkTheAbyss --use havenPortal@4
/// --settle 12</c> opens a portal to the haven, walks to it, and goes through.
/// </summary>
static class HarnessOrders
{
    /// <summary>
    /// <c>--cast a,b,…</c>: several casts, one after another — the first at once, each of the
    /// rest the moment the caster stops being held by the one before (<see cref="Actor.BusyUntil"/>),
    /// which is how Cloak the Gathering and Cloak of Shadows are stacked in 1.1
    /// (<c>ObfuscateStackingTests</c>). The later ones are taken during <c>--settle</c>. Ours.
    /// </summary>
    public sealed class Casts(List<string> names)
    {
        int next;

        public static Casts? Parse(string? arg) =>
            arg is null || arg.StartsWith("--") ? null
            : new Casts([.. arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]);

        /// <summary>The first cast, before the settle starts.</summary>
        public void Begin(Location place)
        {
            if (next < names.Count) Cast(place, names[next++]);
        }

        /// <summary>The next cast, once the caster is free.</summary>
        public void Tick(Location place, int nowMs)
        {
            if (next >= names.Count) return;
            var who = place.Steered ?? place.Player?.Thing;
            if (who?.Actor is { } actor && actor.BusyUntil > nowMs) return;
            Cast(place, names[next++]);
        }
    }

    public static void Cast(Location place, string name)
    {
        var who = place.Steered ?? place.Player?.Thing;
        if (who?.Actor is not { } actor) { Console.WriteLine("cast: nobody to cast"); return; }
        // `name@template`: at the nearest living thing of that template, as a right click on it
        // with no modifier — walking into range first, as the cast order does.
        string? at = null;
        if (name.IndexOf('@') is int split and > 0) { at = name[(split + 1)..]; name = name[..split]; }
        if (actor.Disciplines[name] is not { } power) { Console.WriteLine($"cast: {who.Template} does not know {name}"); return; }
        WorldThing? target = null;
        if (at is not null)
        {
            target = place.Things.Where(t => !t.Removed && t != who && t.Template.Equals(at, StringComparison.OrdinalIgnoreCase)
                                             && t.Actor?.Has(Actor.Flag.Dead) != true)
                                 .OrderBy(t => System.Numerics.Vector3.Distance(t.Position, who.Position)).FirstOrDefault();
            if (target is null) { Console.WriteLine($"cast: no living {at} in {place.Name}"); return; }
        }
        var answer = Engine.Rpg.Cast.Order(who, target, null, power, target is null ? InputModifier.Space : InputModifier.None);
        Console.WriteLine($"cast: {who.Template} casts {power.Definition.Name} level {power.Level}"
                          + (target is null ? "" : $" at {target.Template} (guid {target.Guid})") + $": {answer}");
    }

    /// <summary><c>--use</c>'s orders, each waiting for its moment: <c>template@seconds</c>, several separated by commas.</summary>
    public sealed class Use(List<(string Template, float At)> orders)
    {
        int next;

        public static Use? Parse(string? arg)
        {
            if (arg is null || arg.StartsWith("--")) return null;
            var orders = new List<(string, float)>();
            foreach (var one in arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = one.Split('@');
                float at = parts.Length > 1 && float.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : 0f;
                orders.Add((parts[0], at));
            }
            return new Use(orders);
        }

        public void Tick(Location place, float seconds, int nowMs)
        {
            if (next >= orders.Count || seconds < orders[next].At) return;
            var template = orders[next++].Template;
            var who = place.Steered ?? place.Player?.Thing;
            if (who is null) { Console.WriteLine("use: nobody to use anything"); return; }
            var thing = place.Things.Where(t => !t.Removed && t.Template.Equals(template, StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(t => System.Numerics.Vector3.Distance(t.Position, who.Position)).FirstOrDefault();
            if (thing is null) { Console.WriteLine($"use: no {template} in {place.Name}"); return; }
            var answer = Actions.Use(who, thing, nowMs);
            Console.WriteLine($"use: {who.Template} uses {thing.Template} (guid {thing.Guid}) at {seconds:0.0}s: {answer}");
        }
    }
}
