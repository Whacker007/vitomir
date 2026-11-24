// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Perception;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Presentation;

/// <summary>
/// One observer as the developer's awareness overlay draws it (ours, <c>--show-awareness</c>, the action
/// <c>awareness</c>): the point over its head where its mark goes (none, ?, ! or a red !, by its rung), its feet, the way
/// it faces, its cone's half-angle and how far it sees a running body in the open where it stands (m, the cone drawn
/// faintly on the ground to there), and whether it keeps the peace.
/// </summary>
public readonly record struct AwarenessMark(Vector3 OverHead, Vector3 Feet, float Yaw, float HalfAngle, float Sight, Awareness Level, bool Keeper);

/// <summary>
/// A piece of the outline of an area of the level that perception reads (ours, the awareness overlay): from, to (on
/// the ground, a few centimetres over it), and whether the area conceals (a <c>conceal</c> key) or has its own light (a
/// <c>light</c> key).
/// </summary>
public readonly record struct AwarenessEdge(Vector3 From, Vector3 To, bool Conceal);

/// <summary>
/// The awareness overlay's marks (ours): read from the world's observers between ticks, changing nothing — its
/// digests and a recording are the same with it on or off. and the outlines of the level's light and conceal areas.
/// </summary>
public static class AwarenessView
{
    /// <summary>The longest piece an area's outline is drawn in (m), so it lies on the ground it crosses; and the most pieces a side is cut into.</summary>
    const float EdgePiece = 1f;
    const int MostPieces = 32;

    /// <summary>Fills <paramref name="view"/>'s awareness marks and area outlines (<see cref="FrameView.Awareness"/>, <see cref="FrameView.AwarenessAreas"/>).</summary>
    public static void For(World world, FrameView view)
    {
        For(world, view.Awareness);
        Areas(world, view.AwarenessAreas);
    }

    /// <summary>
    /// Fills <paramref name="into"/> with the outline of every light and conceal area of <paramref name="world"/>'s level
    /// (cleared first): each side of the area's box cut into pieces of at most a metre, each end laid on the ground under
    /// it (the level's first surface down from the box's top), or on the box's floor where there is none.
    /// </summary>
    public static void Areas(World world, List<AwarenessEdge> into)
    {
        into.Clear();
        if (!world.TryService<PerceptionState>(out var state)) return;
        var areas = state.Areas;
        var level = world.TryService<LevelSpace>(out var space) ? space.Collision : null;
        for (int i = 0; i < areas.Lights + areas.Conceals; i++)
        {
            bool conceal = i >= areas.Lights;
            var box = conceal ? areas.Conceal(i - areas.Lights).Box : areas.Light(i).Box;
            ReadOnlySpan<Vector2> corners = [new(0, 0), new(box.Size.X, 0), new(box.Size.X, box.Size.Y), new(0, box.Size.Y)];
            for (int c = 0; c < 4; c++)
            {
                var a = corners[c];
                var b = corners[(c + 1) % 4];
                int pieces = Math.Clamp((int)MathF.Ceiling(Vector2.Distance(a, b) / EdgePiece), 1, MostPieces);
                var last = Ground(level, box, a);
                for (int k = 1; k <= pieces; k++)
                {
                    var next = Ground(level, box, Vector2.Lerp(a, b, k / (float)pieces));
                    into.Add(new AwarenessEdge(last, next, conceal));
                    last = next;
                }
            }
        }
    }

    /// <summary>The ground under the point <paramref name="local"/> of <paramref name="box"/>'s floor, a few centimetres over it.</summary>
    static Vector3 Ground(CollisionWorld? level, in TurnedBox box, Vector2 local)
    {
        var top = box.ToLevel(new Vector3(local, box.Size.Z));
        if (level?.Ray(top, -Vector3.UnitZ, box.Size.Z) is { } hit) return hit.Point + new Vector3(0, 0, 0.04f);
        return box.ToLevel(new Vector3(local, 0.04f));
    }

    /// <summary>Fills <paramref name="into"/> with a mark for every observer of <paramref name="world"/> (cleared first; nothing when it has no perception).</summary>
    public static void For(World world, List<AwarenessMark> into)
    {
        into.Clear();
        if (!world.TryService<PerceptionConfig>(out var config) || config.Kinds.Count == 0) return;
        var areas = world.TryService<PerceptionState>(out var state) ? state.Areas : LevelAreas.None;
        var bodies = world.Store<Body>();
        var locos = world.Store<Locomotion>();
        var observers = world.Store<Observer>();
        var ents = observers.Entities;
        var data = observers.Data;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            ref readonly var o = ref data[i];
            if (!bodies.Has(e) || !locos.Has(e) || o.Kind < 0 || o.Kind >= config.Kinds.Count) continue;
            ref readonly var body = ref bodies.Get(e);
            float height = body.CapsuleHeight(locos.Get(e));
            var kind = config.Kinds[o.Kind].Numbers;
            float sight = config.Sight(o.Kind, areas.LightAt(body.Feet, config.Light)) * config.Numbers.MotionRun;
            into.Add(new AwarenessMark(body.Feet + new Vector3(0, 0, height + 0.2f), body.Feet, body.Yaw,
                MathF.Min(180f, kind.ConeDegrees) * DetMath.DegToRad, sight, o.Level, config.RoleOf(o.Kind) == ObserverRole.Guard));
        }
    }
}
