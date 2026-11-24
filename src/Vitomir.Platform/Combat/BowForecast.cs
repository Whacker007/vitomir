// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the developer's aids for the bow — where the drawn arrow would land and the arc it flies.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Combat;

/// <summary>
/// The shot a drawn bow would loose now, flown ahead (<i>"debug toggles that can show either
/// or both the calculated point of impact, and the actual path the projectile will take, like how you can do it in some of the sniper
/// games"</i>): from the release point along the bow's aim (<see cref="Bow.AimYaw"/>, <see cref="Bow.AimPitch"/>) at the speed of the draw
/// reached (full draw while the bow is up undrawn), the body's own velocity added — what <see cref="BowSystem"/>'s loose launches — and
/// stepped by <see cref="Ballistics.Step"/> a tick at a time, as <see cref="ProjectileSystem"/> flies it, each tick's segment tested
/// against the level and every other body's hit volumes (not shields or held blades: the aid shows where the arrow goes, not what may
/// step into it), until it meets one, has flown the bow's <see cref="BowData.FlightSecondsAtMost"/> or fallen its
/// <see cref="BowData.FallenBelowMetres"/>. The same steps as the shot, so the aid and the shot cannot disagree. Presentation and tests
/// only: it reads the world and changes nothing; its points go into a buffer made once.
/// </summary>
public sealed class BowForecast
{
    /// <summary>The most points a path keeps (one a tick: 8 s).</summary>
    public const int MaxPoints = 960;

    readonly Vector3[] points = new Vector3[MaxPoints];

    /// <summary>The path's points, a tick apart, from the release point to the last (the impact when <see cref="Meets"/>).</summary>
    public ReadOnlySpan<Vector3> Path => points.AsSpan(0, Count);

    /// <summary>How many points <see cref="Path"/> has.</summary>
    public int Count { get; private set; }

    /// <summary>Whether the flight meets something (the level or a body) before it is given up.</summary>
    public bool Meets { get; private set; }

    /// <summary>The body it meets (none for the level or nothing).</summary>
    public EntityId Met { get; private set; }

    /// <summary>Where the flight ends: the impact when it meets something, else its last point.</summary>
    public Vector3 Impact { get; private set; }

    /// <summary>The share of full draw the forecast is flown at and its speed (m/s).</summary>
    public float Share { get; private set; }

    /// <summary>
    /// Flies <paramref name="archer"/>'s shot as it would be loosed now: false (and an empty path) when it has no bow in hand and up.
    /// </summary>
    public bool Fly(World world, EntityId archer)
    {
        Count = 0;
        Meets = false;
        Met = EntityId.None;
        if (!world.TryStore<Bow>(out var bows) || !world.TryService<BowConfig>(out var config)) return false;
        ref var bow = ref bows.Find(archer);
        if (Unsafe.IsNullRef(ref bow) || !bow.InHand || bow.State < BowState.Raising || (uint)bow.Weapon >= (uint)config.Bows.Count) return false;
        ref var body = ref world.Store<Body>().Find(archer);
        if (Unsafe.IsNullRef(ref body)) return false;
        var d = config.Bows[bow.Weapon];
        Share = bow.Drawn ? MathF.Max(bow.Share, d.LeastShareToLoose) : 1f;
        var from = BowServices.ReleasePoint(world, archer, in body, bow.AimYaw, d);
        var velocity = DetMath.Direction(bow.AimYaw, bow.AimPitch) * d.SpeedAt(Share) + body.Velocity;
        return Fly(world, archer, from, velocity, d);
    }

    /// <summary>Flies a shot from <paramref name="from"/> at <paramref name="velocity"/> with <paramref name="d"/>'s arrow, never meeting <paramref name="archer"/>.</summary>
    public bool Fly(World world, EntityId archer, Vector3 from, Vector3 velocity, BowData d)
    {
        Count = 0;
        Meets = false;
        Met = EntityId.None;
        world.TryService<LevelSpace>(out var level);
        world.TryService<HitVolumes>(out var volumes);
        var bodies = world.Store<Body>();
        var ents = bodies.Entities;
        var p = from;
        var v = velocity;
        points[Count++] = p;
        int steps = Math.Min(MaxPoints - 1, (int)MathF.Ceiling(d.FlightSecondsAtMost * World.TicksPerSecond));
        for (int i = 0; i < steps; i++)
        {
            var p0 = p;
            Ballistics.Step(ref p, ref v, d.ArrowDragPerMetre, World.TickSeconds);
            var way = p - p0;
            float length = MathF.Sqrt(way.LengthSquared());
            if (length > 1e-6f)
            {
                var dir = way / length;
                float best = length;
                bool hit = false;
                var met = EntityId.None;
                if (level is not null && level.Collision.Ray(p0, dir, best) is { } h) { best = h.Distance; hit = true; }
                if (volumes is not null)
                    for (int k = 0; k < ents.Length; k++)
                    {
                        if (ents[k] == archer) continue;
                        if (volumes.Ray(world, ents[k], p0, dir, best, out var on) && on.Hit.Time < best) { best = on.Hit.Time; hit = true; met = ents[k]; }
                    }
                if (hit)
                {
                    Impact = p0 + dir * best;
                    points[Count++] = Impact;
                    Meets = true;
                    Met = met;
                    return true;
                }
            }
            points[Count++] = p;
            if (from.Z - p.Z > d.FallenBelowMetres) break;
        }
        Impact = p;
        return true;
    }
}
