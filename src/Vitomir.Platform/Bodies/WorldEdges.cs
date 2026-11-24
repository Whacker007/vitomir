// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// Where a body last stood on ground (ours), kept so that a body that
/// falls out of the world can be put back (<see cref="FallRecoverySystem"/>). While the body stands steadily
/// on ground its feet are sampled every <see cref="WorldEdges.SampleTicks"/> ticks; <see cref="Recent"/> is the
/// latest sample and <see cref="Safe"/> the one before it, so the place a body is put back at was stood on
/// a quarter of a second or more before it left the ground, back from the edge it slipped off.
/// </summary>
public struct GroundMemory
{
    /// <summary>The sample before the latest (valid when <see cref="Have"/> is 2).</summary>
    public Vector3 Safe;
    /// <summary>The latest sample, or where the body was first seen (valid when <see cref="Have"/> is 1 or 2).</summary>
    public Vector3 Recent;
    /// <summary>How many of the two places are known.</summary>
    public int Have;
    /// <summary>Ticks of standing left until the next sample.</summary>
    public int Countdown;

    /// <summary>A body first seen at <paramref name="feet"/>: that is where it goes back to until it has stood somewhere.</summary>
    public static GroundMemory First(Vector3 feet) => new() { Recent = feet, Safe = feet, Have = 1, Countdown = WorldEdges.SampleTicks };

    /// <summary>Where a body that falls out now is put back.</summary>
    public readonly Vector3 PutBack => Have >= 2 ? Safe : Recent;
}

/// <summary>
/// The controlled body's memory of the level's exit volumes (ours): where its feet were after last
/// tick's check (the face of a volume it came through is the one that place was beyond), the exit whose
/// volume it stood in when first seen (it may walk out of that one; 0 for none), and the exit it was last
/// stopped at (one <see cref="ExitReached"/> for each time it walks up to it; 0 for none). Exits are counted
/// from 1 here: an index into <see cref="LevelExits.All"/> plus one.
/// </summary>
public struct ExitGuard
{
    public Vector3 LastFeet;
    public int Inside;
    public int Touching;
}

/// <summary>A body fell below the level (<see cref="LevelSpace.Bounds"/>' floor less <see cref="WorldEdges.FallMargin"/>) and was put back where it last stood (ours).</summary>
public readonly record struct BodyRecovered(EntityId Entity, long Tick, Vector3 From, Vector3 To);

/// <summary>
/// The controlled body walked into an exit's volume and was stopped at its edge (ours): which exit (its
/// index in <see cref="LevelExits.All"/>, its number, where it leads and that place's level, whether the
/// package calls it shut) and where the feet were held. Published once each time the body walks up to it.
/// </summary>
public readonly record struct ExitReached(EntityId Entity, long Tick, int Exit, int Number, string To, string ToLevel, bool Closed, Vector3 Feet);

/// <summary>
/// The edges of the world (ours): a safety net under the level and the
/// exits' thresholds. Registered by <see cref="MovementServices.Register"/>, in the Move phase after the
/// bodies have moved: <c>Move/110 platform.exits</c> (<see cref="ExitThresholdSystem"/>) then
/// <c>Move/120 platform.fall-recovery</c> (<see cref="FallRecoverySystem"/>).
/// </summary>
public static class WorldEdges
{
    public const int ExitsOrder = 110, FallRecoveryOrder = 120;

    /// <summary>How far below the level's lowest bound a body falls before it is put back (m). Ours.</summary>
    public const float FallMargin = 20f;

    /// <summary>Ticks of standing between two samples of where a body stands (a quarter of a second). Ours.</summary>
    public const int SampleTicks = World.TicksPerSecond / 4;

    /// <summary>How long a body must have stood since landing before where it stands is sampled (s), so a graze of a ledge in the air is not "stood on". Ours.</summary>
    public const float SteadySeconds = 0.1f;

    /// <summary>How far the controlled body must step back from an exit's volume before walking up to it again reports it again (m). Ours.</summary>
    public const float ReleaseMetres = 0.25f;

    /// <summary>Registers the components <see cref="GroundMemory"/> and <see cref="ExitGuard"/>, the events <see cref="ExitReached"/> and <see cref="BodyRecovered"/>, and the two systems.</summary>
    public static WorldBuilder Register(WorldBuilder b)
    {
        b.Component<GroundMemory>().Component<ExitGuard>();
        b.Event<ExitReached>().Event<BodyRecovered>();
        b.System(Phase.Move, ExitsOrder, "platform.exits", new ExitThresholdSystem());
        b.System(Phase.Move, FallRecoveryOrder, "platform.fall-recovery", new FallRecoverySystem());
        return b;
    }
}

/// <summary>
/// The safety net (ours): a body whose feet fall below the level's lowest bound less
/// <see cref="WorldEdges.FallMargin"/> — through a hole in a level, out of a doorway into nothing — is put
/// back at the last place it stood (<see cref="GroundMemory.PutBack"/>), settling onto the ground there as a
/// body just placed does (<see cref="Body.At"/>), its velocity gone and its jump and fall forgotten, and
/// <see cref="BodyRecovered"/> says so. Every body is kept, the player's and the townsfolk's. A body is
/// given its memory the first tick it is seen, where it stands then.
/// </summary>
public sealed class FallRecoverySystem : ISystem
{
    public void Run(World world)
    {
        float floor = world.Service<LevelSpace>().Bounds.Min.Z - WorldEdges.FallMargin;
        var bodies = world.Store<Body>();
        var memories = world.Store<GroundMemory>();
        var recovered = world.Events<BodyRecovered>();
        var data = bodies.Data;
        var ents = bodies.Entities;
        for (int i = 0; i < data.Length; i++)
        {
            var e = ents[i];
            ref var body = ref data[i];
            if (!memories.Has(e)) memories.Set(e, GroundMemory.First(body.Feet));
            ref var m = ref memories.Get(e);
            if (body.Feet.Z < floor)
            {
                var from = body.Feet;
                var to = m.PutBack;
                body = Body.At(to, body.Yaw);
                m = GroundMemory.First(to);
                recovered.Publish(new BodyRecovered(e, world.Tick, from, to));
                continue;
            }
            if (body.State == BodyState.Ground && body.GroundSeconds >= WorldEdges.SteadySeconds && --m.Countdown <= 0)
            {
                m.Safe = m.Recent;
                m.Recent = body.Feet;
                m.Have = 2;
                m.Countdown = WorldEdges.SampleTicks;
            }
        }
    }
}

/// <summary>
/// The exits' thresholds (ours): the controlled body is not let into an exit's walk-in volume
/// (<see cref="LevelExit.Volume"/>). Until taking an exit is built (in the movement design, a
/// later batch) the volume stands as a wall for the player: the body is held at the edge it walked up to —
/// put back to the face it came through, grown by its radius, and its speed into the volume taken away, so it
/// stops as against a wall and slides along the face, never a jolt — and <see cref="ExitReached"/> says which
/// exit, once each time it walks up to it. A body that stands in a volume when first seen (a start inside
/// one) may walk out of it. The townsfolk ignore exits. Nothing is allocated in a tick.
/// </summary>
public sealed class ExitThresholdSystem : ISystem
{
    public void Run(World world)
    {
        var exits = world.Service<LevelSpace>().Exits;
        if (exits.WalkIn == 0) return;
        var tags = world.Store<Controlled>();
        var bodies = world.Store<Body>();
        var locos = world.Store<Locomotion>();
        var guards = world.Store<ExitGuard>();
        var reached = world.Events<ExitReached>();
        var ents = tags.Entities;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            if (!bodies.Has(e)) continue;
            ref var body = ref bodies.Get(e);
            float radius = 0.3f, height = 1.8f;
            if (locos.Has(e)) { ref readonly var l = ref locos.Get(e); radius = l.Radius; height = body.CapsuleHeight(l); }
            bool first = !guards.Has(e);
            if (first) guards.Set(e, new ExitGuard { LastFeet = body.Feet });
            ref var g = ref guards.Get(e);
            var all = exits.All;
            for (int k = 0; k < all.Length; k++)
            {
                if (all[k].Volume is not { } box) continue;
                int id = k + 1;
                bool overlaps = ExitThreshold.Overlaps(box, body.Feet, radius, height);
                if (first && overlaps) { g.Inside = id; continue; }
                if (g.Inside == id) { if (!overlaps) g.Inside = 0; continue; }
                if (overlaps)
                {
                    ExitThreshold.Hold(box, radius, g.LastFeet, ref body.Feet, ref body.Velocity);
                    if (g.Touching != id)
                    {
                        g.Touching = id;
                        var x = all[k];
                        reached.Publish(new ExitReached(e, world.Tick, k, x.Number, x.To, x.ToLevel, x.Closed, body.Feet));
                    }
                }
                else if (g.Touching == id && ExitThreshold.Clearance(box, body.Feet, radius) > WorldEdges.ReleaseMetres) g.Touching = 0;
            }
            g.LastFeet = body.Feet;
        }
    }
}

/// <summary>The arithmetic of an exit's threshold (ours): a body's capsule against a turned box, its footprint taken as the box grown by the body's radius.</summary>
public static class ExitThreshold
{
    /// <summary>How far outside the face a held body's feet are put (m), so the next tick starts outside.</summary>
    public const float Skin = 0.001f;

    /// <summary>Whether a body of <paramref name="radius"/> and <paramref name="height"/> with its feet at <paramref name="feet"/> overlaps the box.</summary>
    public static bool Overlaps(in TurnedBox box, Vector3 feet, float radius, float height)
    {
        var l = box.ToLocal(feet);
        return l.X > -radius && l.X < box.Size.X + radius && l.Y > -radius && l.Y < box.Size.Y + radius
               && l.Z > -height && l.Z < box.Size.Z;
    }

    /// <summary>How far outside the box grown by <paramref name="radius"/> the feet are across the level's plane (m; 0 or less inside).</summary>
    public static float Clearance(in TurnedBox box, Vector3 feet, float radius)
    {
        var l = box.ToLocal(feet);
        float ox = MathF.Max(-radius - l.X, l.X - box.Size.X - radius);
        float oy = MathF.Max(-radius - l.Y, l.Y - box.Size.Y - radius);
        return MathF.Max(ox, oy);
    }

    /// <summary>
    /// Puts feet that went into the box (grown by <paramref name="radius"/>) back just outside the face they
    /// came through — the nearest of the faces <paramref name="previous"/> was beyond, or the nearest face of
    /// all when it was beyond none — leaving their height and their place along the face, and takes away the
    /// part of <paramref name="velocity"/> that goes into the box.
    /// </summary>
    public static void Hold(in TurnedBox box, float radius, Vector3 previous, ref Vector3 feet, ref Vector3 velocity)
    {
        var l = box.ToLocal(feet);
        var p = box.ToLocal(previous);
        float x0 = -radius, x1 = box.Size.X + radius, y0 = -radius, y1 = box.Size.Y + radius;
        bool bx0 = p.X <= x0, bx1 = p.X >= x1, by0 = p.Y <= y0, by1 = p.Y >= y1;
        bool any = bx0 || bx1 || by0 || by1;
        int face = -1;
        float best = float.MaxValue;
        if ((!any || bx0) && l.X - x0 < best) { best = l.X - x0; face = 0; }
        if ((!any || bx1) && x1 - l.X < best) { best = x1 - l.X; face = 1; }
        if ((!any || by0) && l.Y - y0 < best) { best = l.Y - y0; face = 2; }
        if ((!any || by1) && y1 - l.Y < best) { face = 3; }
        Vector2 outward;
        switch (face)
        {
            case 0: l.X = x0 - Skin; outward = -box.AxisX; break;
            case 1: l.X = x1 + Skin; outward = box.AxisX; break;
            case 2: l.Y = y0 - Skin; outward = -box.AxisY; break;
            default: l.Y = y1 + Skin; outward = box.AxisY; break;
        }
        var held = box.ToLevel(l);
        feet = new Vector3(held.X, held.Y, feet.Z);
        float into = velocity.X * outward.X + velocity.Y * outward.Y;
        if (into < 0f)
        {
            velocity.X -= into * outward.X;
            velocity.Y -= into * outward.Y;
        }
    }
}
