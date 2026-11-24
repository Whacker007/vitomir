// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Cameras;

/// <summary>What the reticle is on this tick (a service; the program puts it into the frame, use and talk read it).</summary>
public sealed class ReticleState
{
    public ReticleAnswer Answer { get; set; } = ReticleAnswer.Nothing;
    /// <summary>How far the reticle's ray reaches (m).</summary>
    public float Reach { get; init; } = 100f;
}

/// <summary>
/// The Post phase's reticle, after the camera (ours): a ray from the view along
/// its forward, up to <see cref="ReticleState.Reach"/>, against the level and every other body's capsule;
/// the nearest is the answer (a person — every body is one for now — the level, or nothing), with the point
/// and its distance <b>from the body's eyes</b> (what use and talk measure). In third person the line from
/// the eyes to that point is then checked against the same: if something nearer stands between (the
/// shoulder's blind side), the answer is <see cref="ReticleAnswer.Blocked"/>, so what you see is what you get.
/// <para>
/// On a person, the answer names the <b>part</b> under the reticle (<see cref="ReticleAnswer.Part"/>, "head",
/// "left forearm"): the same ray against that person's hit volumes on their pose this tick (<see cref="HitVolumes"/>),
/// up to the level behind them; none when it passes their capsule but no drawn part (between the legs, by the
/// head), or the world has no volumes. Which person is under it is still decided by the bodies' capsules.
/// </para>
/// </summary>
public sealed class ReticleSystem : ISystem
{
    public void Run(World world)
    {
        var state = world.Service<ReticleState>();
        var view = world.Service<ViewState>();
        state.Answer = view.HasView ? Cast(world, view, view.View.Eye, view.View.Forward, state.Reach) : ReticleAnswer.Nothing;
    }

    /// <summary>
    /// What a ray from <paramref name="origin"/> (the camera) along <paramref name="dir"/> (a unit) meets first within
    /// <paramref name="reach"/> — the level or another body than the view's own — with the point, its distance from the
    /// body's eyes, and in third person whether the way from the eyes to it is blocked. The reticle's ray (along the
    /// view) and the freed pointer's (through its place on the picture, <see cref="PointerPick"/>) are both this.
    /// </summary>
    public static ReticleAnswer Cast(World world, ViewState view, Vector3 origin, Vector3 dir, float reach)
    {
        var level = world.Service<LevelSpace>().Collision;
        var bodies = world.Store<Body>();
        var locos = world.Store<Locomotion>();
        var eye = origin;
        float best = reach;
        var kind = ReticleTarget.Nothing;
        ulong entity = 0;
        if (level.Ray(eye, dir, best) is { } hit) { best = hit.Distance; kind = ReticleTarget.Level; }
        float behind = best;
        if (Nearest(bodies, locos, view.Of, eye, dir, best) is { } on) { best = on.Distance; kind = ReticleTarget.Person; entity = on.Entity; }
        if (kind == ReticleTarget.Nothing) return ReticleAnswer.Nothing;
        var point = eye + dir * best;
        var eyes = view.Eyes;
        float distance = Vector3.Distance(eyes, point);
        bool blocked = false;
        if (view.Kind == ViewKind.Third && distance > 0.05f)
        {
            var way = (point - eyes) / distance;
            float clear = distance - 0.05f;
            if (level.Ray(eyes, way, clear) is not null) blocked = true;
            else if (Nearest(bodies, locos, view.Of, eyes, way, clear) is { } between && between.Entity != entity) blocked = true;
        }
        string? part = null;
        if (kind == ReticleTarget.Person && world.TryService<HitVolumes>(out var volumes)
            && volumes.Ray(world, EntityId.FromPacked(entity), eye, dir, behind, out var on2))
            part = BodyRegions.Name(on2.Region);
        return new ReticleAnswer(kind, entity, point, distance, null, part, blocked);
    }

    /// <summary>The nearest body's capsule (not <paramref name="skip"/>'s) the ray meets within <paramref name="length"/>.</summary>
    static (float Distance, ulong Entity)? Nearest(Store<Body> bodies, Store<Locomotion> locos, EntityId skip, Vector3 o, Vector3 d, float length)
    {
        var data = bodies.Data;
        var ents = bodies.Entities;
        (float, ulong)? best = null;
        for (int i = 0; i < data.Length; i++)
        {
            if (ents[i] == skip || !locos.Has(ents[i])) continue;
            ref readonly var l = ref locos.Get(ents[i]);
            var cap = Capsule.OfBody(data[i].Feet, l.Radius, data[i].CapsuleHeight(l));
            if (SweepMath.RayCapsule(o, d, length, cap.Lower, cap.Segment, cap.Radius) is { } t)
            {
                length = t;
                best = (t, ents[i].Packed);
            }
        }
        return best;
    }
}
