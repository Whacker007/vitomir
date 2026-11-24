// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Perception;

/// <summary>
/// The light perception sees by, given over the time's (ours; a scenario's <c>light</c>): a level 0 to 1 over the
/// whole run, or steps — each a second from the run's start and a level, held until the next — in place of the time's night 0.3 or
/// day 1.0 (<see cref="PerceptionConfig.Light"/>, where no area says its own; an area with its own light keeps it). Registered only
/// when given (<see cref="Register"/>): the system sets <see cref="PerceptionConfig.LightLevel"/> before each tick's perception,
/// by the tick alone, so a replay sees the same. A world without it is unchanged. The text form (<see cref="TryParse"/>):
/// <c>0.45</c>, or <c>0:0.3;5:1</c>.
/// </summary>
public sealed class LightSchedule : ISystem
{
    readonly (long Tick, float Level)[] steps;

    LightSchedule((long, float)[] steps) { this.steps = steps; }

    /// <summary>The steps (the tick each begins at, its level), in time order; a single level is one step at tick 0.</summary>
    public IReadOnlyList<(long Tick, float Level)> Steps => steps;

    /// <summary>Just before perception (<see cref="PerceptionServices.PerceiveOrder"/>).</summary>
    public const int Order = PerceptionServices.PerceiveOrder - 1;

    /// <summary>The level at <paramref name="tick"/>: the last step begun by then (the first before any).</summary>
    public float LevelAt(long tick)
    {
        float level = steps[0].Level;
        foreach (var (t, l) in steps) { if (t > tick) break; level = l; }
        return level;
    }

    public void Run(World world) => world.Service<PerceptionConfig>().LightLevel = LevelAt(world.Tick);

    /// <summary>
    /// Reads <c>0.45</c> or <c>0:0.3;5:1</c> (seconds : level, the seconds rising from 0); false with the problem when it is not
    /// that (a level outside 0 to 1, a time not rising).
    /// </summary>
    public static bool TryParse(string text, out LightSchedule schedule, out string problem)
    {
        schedule = null!;
        problem = "";
        var list = new List<(long, float)>();
        var ic = CultureInfo.InvariantCulture;
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = part.IndexOf(':');
            string at = colon < 0 ? "0" : part[..colon], lv = colon < 0 ? part : part[(colon + 1)..];
            if (!double.TryParse(at, NumberStyles.Float, ic, out double seconds) || seconds < 0) { problem = $"light: \"{part}\": the time is not a second from the start"; return false; }
            if (!float.TryParse(lv, NumberStyles.Float, ic, out float level) || !(level >= 0f && level <= 1f)) { problem = $"light: \"{part}\": the level is not 0 to 1"; return false; }
            long tick = (long)Math.Round(seconds * World.TicksPerSecond);
            if (list.Count > 0 && tick <= list[^1].Item1) { problem = $"light: \"{part}\": the steps' times must rise"; return false; }
            list.Add((tick, level));
        }
        if (list.Count == 0) { problem = "light: no level given"; return false; }
        schedule = new LightSchedule([.. list]);
        return true;
    }

    /// <summary>The schedule registered (its system just before perception) and its first level set; nothing for null.</summary>
    public static WorldBuilder Register(WorldBuilder b, LightSchedule? schedule)
    {
        if (schedule is null) return b;
        b.Service(schedule);
        b.System(Phase.Perceive, Order, "platform.light-schedule", schedule);
        return b;
    }
}
