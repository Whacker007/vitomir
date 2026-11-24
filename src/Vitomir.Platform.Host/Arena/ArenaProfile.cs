// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Arena;

/// <summary>
/// What a bout's tick costs, system by system (<c>--arena-profile</c>, a developer's measurement): the first bout run
/// again with each of its world's systems wrapped in a stopwatch. The platform keeps its schedule to itself, so the wrapping
/// is done here by reflection, on this one world only; the systems run in the same order on the same state, so the bout is
/// the same. Also times the session's own work round each tick (its log's lines).
/// </summary>
internal static class ArenaProfile
{
    sealed class Timed(ISystem inner) : ISystem
    {
        public readonly Stopwatch Clock = new();
        public void Run(World world)
        {
            Clock.Start();
            inner.Run(world);
            Clock.Stop();
        }
    }

    internal static void Measure(ArenaRun run, ArenaSpec spec, Action<string> say)
    {
        using var session = Session.Create(run.OptionsFor(0, 0));
        var world = session.World;
        var schedule = typeof(World).GetProperty("Schedule", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(world);
        var entriesField = schedule?.GetType().GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic);
        var dirtyField = schedule?.GetType().GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic);
        if (schedule is null || entriesField?.GetValue(schedule) is not System.Collections.IList entries || dirtyField is null)
        {
            say("arena: profile: the world's schedule could not be reached (the platform changed); nothing measured");
            return;
        }
        var timed = new List<(string Name, Timed Clock)>();
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i]!;
            var t = entry.GetType();
            var system = (ISystem)t.GetProperty("System")!.GetValue(entry)!;
            string name = (string)t.GetProperty("Name")!.GetValue(entry)!;
            var clock = new Timed(system);
            // A record struct's with-copy: the same entry with the timed system.
            var copy = Activator.CreateInstance(t, t.GetProperty("Phase")!.GetValue(entry), t.GetProperty("Order")!.GetValue(entry), name, clock)!;
            entries[i] = copy;
            timed.Add(($"{t.GetProperty("Phase")!.GetValue(entry)}/{t.GetProperty("Order")!.GetValue(entry)} {name}", clock));
        }
        dirtyField.SetValue(schedule, true);

        long most = (long)MathF.Round(spec.Seconds * World.TicksPerSecond);
        var all = Stopwatch.StartNew();
        while (session.Tick < most)
        {
            session.Step(ActionFrame.Idle);
            if (world.TryService<EventQueue<Combat.ActorDied>>(out var died) && died.ThisTick.Count > 0) break;
        }
        all.Stop();
        double ticks = Math.Max(1, session.Tick);
        double inSystems = timed.Sum(t => t.Clock.Clock.Elapsed.TotalMilliseconds);
        say(string.Create(CultureInfo.InvariantCulture,
            $"arena: profile of bout 0 ({ticks:0} ticks, {spec.Fighters.Count} fighters): {1000 * all.Elapsed.TotalMilliseconds / ticks:0.0} µs a tick, {1000 * inSystems / ticks:0.0} µs in the systems, {1000 * (all.Elapsed.TotalMilliseconds - inSystems) / ticks:0.0} µs round them (the session's log, the queues)"));
        foreach (var (name, clock) in timed.OrderByDescending(t => t.Clock.Clock.Elapsed).Take(12))
            say(string.Create(CultureInfo.InvariantCulture, $"arena: profile:   {1000 * clock.Clock.Elapsed.TotalMilliseconds / ticks,7:0.0} µs  {100 * clock.Clock.Elapsed.TotalMilliseconds / Math.Max(1e-9, all.Elapsed.TotalMilliseconds),5:0.0} %  {name}"));
    }
}
