// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Host;

// Ours: the bow's two developer aids, drawn with the swing trail's lines. Presentation only.

public sealed partial class Session
{
    /// <summary>The point of impact shown (the action <c>showImpact</c>, the setting <c>showBowImpact</c>, <c>--show-impact</c>).</summary>
    public bool ShowBowImpact { get; set; }

    /// <summary>The arrow's path shown, and each shot's real path for <see cref="FlownKeptSeconds"/> beside it (the action <c>showPath</c>, the setting <c>showBowPath</c>, <c>--show-path</c>).</summary>
    public bool ShowBowPath { get; set; }

    /// <summary>How long a shot's real path and the forecast it was loosed on stay drawn after it stops (s; ENGINE_SANDBOX, ours: presentation only).</summary>
    public const double FlownKeptSeconds = 4;

    /// <summary>The forecast drawn now (the player's bow, flown each picture).</summary>
    public BowForecast BowForecast { get; } = new();

    sealed class Flown
    {
        public ulong Arrow;
        public double StoppedAt = double.NaN;
        public readonly List<Vector3> Real = [];
        public Vector3[] Forecast = [];
    }

    readonly List<Flown> flown = [];
    int lastShots = -1;
    Vector3[] lastForecast = [];

    /// <summary>
    /// The aids into the picture's lines: the forecast of the player's drawn (or raised) bow — its path as green lines,
    /// its impact as a red star — and, for each shot loosed while the path was shown, its real path (amber) beside the forecast it was
    /// loosed on, kept <see cref="FlownKeptSeconds"/> after it stops.
    /// </summary>
    void DrawBowAids(FrameView view)
    {
        if (!ShowBowImpact && !ShowBowPath) { flown.Clear(); lastShots = -1; return; }
        if (!world.Alive(player) || !world.TryStore<Bow>(out var bows) || !bows.Has(player)) return;
        var bow = bows.Get(player);
        double now = world.Seconds;
        // A new shot: its forecast kept (as last drawn) and its arrow followed.
        if (lastShots >= 0 && bow.Shots > lastShots && ShowBowPath && world.TryStore<Arrow>(out var arrows))
        {
            var ents = arrows.Entities;
            var data = arrows.Data;
            for (int i = 0; i < ents.Length; i++)
                if (data[i].Archer == player && data[i].Shot == bow.Shots)
                    flown.Add(new Flown { Arrow = ents[i].Packed, Forecast = [.. lastForecast] });
        }
        lastShots = bow.Shots;
        if (BowForecast.Fly(world, player))
        {
            var path = BowForecast.Path;
            if (ShowBowPath)
            {
                for (int i = 1; i < path.Length; i++) view.SwingTrail.Add(new SwingTrailLine(path[i - 1], path[i], SwingTrailKind.BowPath, 1f));
                if (lastForecast.Length != path.Length) lastForecast = new Vector3[path.Length];
                path.CopyTo(lastForecast);
            }
            if (ShowBowImpact && BowForecast.Meets) view.SwingTrail.Add(new SwingTrailLine(BowForecast.Impact, BowForecast.Impact, SwingTrailKind.BowImpact, 1f));
        }
        if (!ShowBowPath) { flown.Clear(); return; }
        for (int f = flown.Count - 1; f >= 0; f--)
        {
            var k = flown[f];
            var e = EntityId.FromPacked(k.Arrow);
            if (world.Alive(e) && world.TryStore<Arrow>(out var store) && store.Has(e))
            {
                var a = store.Get(e);
                if (k.Real.Count == 0 || Vector3.DistanceSquared(k.Real[^1], a.Position) > 1e-6f) k.Real.Add(a.Position);
                if (!a.Flying && double.IsNaN(k.StoppedAt)) k.StoppedAt = now;
            }
            else if (double.IsNaN(k.StoppedAt)) k.StoppedAt = now;
            if (!double.IsNaN(k.StoppedAt) && now - k.StoppedAt > FlownKeptSeconds) { flown.RemoveAt(f); continue; }
            float fade = double.IsNaN(k.StoppedAt) ? 1f : (float)Math.Clamp(1.0 - (now - k.StoppedAt) / FlownKeptSeconds, 0.0, 1.0);
            for (int i = 1; i < k.Real.Count; i++) view.SwingTrail.Add(new SwingTrailLine(k.Real[i - 1], k.Real[i], SwingTrailKind.BowFlown, fade));
            for (int i = 1; i < k.Forecast.Length; i++) view.SwingTrail.Add(new SwingTrailLine(k.Forecast[i - 1], k.Forecast[i], SwingTrailKind.BowPath, 0.5f * fade));
        }
    }
}
