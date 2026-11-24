// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Navigation;

/// <summary>A route's corners, held in the component itself (plain data, digested with it).</summary>
[InlineArray(RouteWalker.Capacity)]
public struct RouteCorners
{
    Vector3 first;
}

/// <summary>
/// A body that walks routes on its own (<i>navigation plans, the body walks</i>):
/// for now a <b>wanderer</b>, which picks a random reachable place, asks a route there and walks it corner
/// to corner at walking pace, asking again when it makes no progress. The corners live in the component
/// (up to <see cref="Capacity"/>; a longer route is walked to its last held corner and asked again from
/// there).
/// </summary>
public struct RouteWalker
{
    public const int Capacity = 24;

    public RouteCorners Corners;
    public int Count, Next;
    public Vector3 Destination;
    /// <summary>1 while a route is being walked.</summary>
    public int Walking;
    /// <summary>The nearest the body has come to the corner it is walking to, and how long since it came nearer (s).</summary>
    public float Nearest, Stalled;
    /// <summary>Seconds to wait before choosing again (after arriving, or after no route was found).</summary>
    public float Wait;
    /// <summary>How far round itself a wanderer looks for a place to go (m).</summary>
    public float WanderRadius;
    /// <summary>Routes asked for, and of them asked again for want of progress (for diagnostics).</summary>
    public int Asked, Reasked;

    /// <summary>
    /// An errand in place of the routine (perception's reactions, <see cref="Perception.ReactionSystem"/>): 0 none,
    /// <see cref="StandErrand"/> stand facing <see cref="Face"/>, <see cref="WalkErrand"/> or <see cref="RunErrand"/> go to
    /// <see cref="Goal"/> by a route at a walk or a run and stand there facing <see cref="Face"/>. <see cref="Arrived"/> is 1
    /// once the errand's walk has arrived (or found no route); <see cref="AskedGoal"/> is the goal its route was asked for.
    /// </summary>
    public int Errand;
    public Vector3 Goal;
    public float Face;
    public int Arrived;
    public Vector3 AskedGoal;
    /// <summary>A walker posted (a witness): its routine is to stand at <see cref="Post"/> facing <see cref="PostYaw"/>, walking back there after an errand, not to wander.</summary>
    public int Posted;
    public Vector3 Post;
    public float PostYaw;

    /// <summary>The errands (<see cref="Errand"/>).</summary>
    public const int StandErrand = 1, WalkErrand = 2, RunErrand = 3;

    /// <summary>
    /// An errand that leaves the body to someone else — the duelling brain of a person who fights
    /// (<see cref="Perception.Reaction.Fight"/>): the routes stand aside and write nothing to its <see cref="MoveIntent"/>.
    /// </summary>
    public const int AsideErrand = 4;

    public static RouteWalker Wanderer(float radius = 25f) => new() { WanderRadius = radius, Nearest = float.MaxValue };

    /// <summary>A walker posted at <paramref name="post"/> facing <paramref name="yaw"/>: it stands there until an errand takes it away, and goes back after.</summary>
    public static RouteWalker AtPost(Vector3 post, float yaw) => new() { Nearest = float.MaxValue, Posted = 1, Post = post, PostYaw = yaw };
}

/// <summary>Steering a body along corners: the shared arithmetic of <see cref="RouteSystem"/> and <see cref="SnagCheck"/>.</summary>
public static class RouteSteer
{
    /// <summary>A corner counts as reached within this across (m; less than the mesh's margin from walls, so turning early never steers into a wall's end); the last one, within <see cref="ArriveRadius"/>.</summary>
    public const float CornerRadius = 0.2f, ArriveRadius = 0.5f;

    /// <summary>No progress for this long and the route is asked again (s).</summary>
    public const float StallSeconds = 2f;

    /// <summary>Progress is coming this much nearer the corner (m).</summary>
    public const float Progress = 0.05f;

    /// <summary>
    /// The intent of walking from <paramref name="feet"/> to <paramref name="corner"/>: there at walking pace, facing it,
    /// keeping its footing (<see cref="EdgeGuard.Footing"/>): never over a drop higher than its step, or a
    /// little more where the route goes down there (<see cref="BodyMotor.StepsDown"/>), stepping down onto a step it takes
    /// rather than flying over it, and never up onto an edge beside its way the route does not climb.
    /// </summary>
    public static MoveIntent Toward(Vector3 feet, Vector3 corner, Gait gait = Gait.Walk)
    {
        var to = new Vector2(corner.X - feet.X, corner.Y - feet.Y);
        float l = to.Length();
        if (l < 1e-4f) return new MoveIntent { Gait = gait, Guard = EdgeGuard.Footing, GuardCorner = corner };
        var way = to / l;
        return new MoveIntent { Direction = way, Gait = gait, Facing = DetMath.Atan2(way.Y, way.X), Faces = true, Guard = EdgeGuard.Footing, GuardCorner = corner };
    }

    /// <summary>The distance across from the feet to a corner.</summary>
    public static float Across(Vector3 feet, Vector3 corner) => new Vector2(corner.X - feet.X, corner.Y - feet.Y).Length();
}

/// <summary>
/// The Think phase's routes (ours): every <see cref="RouteWalker"/> with a <see cref="Body"/> and a
/// <see cref="MoveIntent"/>. One with no route picks a random place reachable from where it stands
/// (the world's stream <c>"routes"</c>, so it is the same on every run), asks a route and walks it corner to
/// corner at walking pace; it asks again from where it is when it makes no progress for
/// <see cref="RouteSteer.StallSeconds"/>, and chooses afresh after a second's rest on arriving or when no
/// route is found. Walkers are visited in the store's order; asking a route allocates nothing of ours.
/// A walker on an errand (<see cref="RouteWalker.Errand"/>: perception's reactions) stands or goes where it is
/// sent instead, at a walk or a run with the same footing; a posted one (a witness) stands at its post and walks back to
/// it after an errand. One whose errand is to stand aside (<see cref="RouteWalker.AsideErrand"/>: it fights, and the
/// duelling brain drives it) is left alone.
/// </summary>
public sealed class RouteSystem : ISystem
{
    readonly Vector3[] buffer = new Vector3[RouteWalker.Capacity];

    public void Run(World world)
    {
        var nav = world.Service<LevelSpace>().Navigation;
        var walkers = world.Store<RouteWalker>();
        var bodies = world.Store<Body>();
        var intents = world.Store<MoveIntent>();
        var rng = world.Random.For("routes");
        var data = walkers.Data;
        var ents = walkers.Entities;
        const float dt = World.TickSeconds;
        for (int i = 0; i < data.Length; i++)
        {
            var e = ents[i];
            if (!bodies.Has(e) || !intents.Has(e)) continue;
            ref var w = ref data[i];
            if (w.Errand == RouteWalker.AsideErrand) { w.Walking = 0; continue; }      // another drives it (a fight)
            ref readonly var body = ref bodies.Get(e);
            ref var intent = ref intents.Get(e);
            intent = new MoveIntent { Gait = Gait.Walk, Facing = body.Yaw, Faces = true };
            // An errand of perception's reactions, or a posted walker's routine, in place of wandering.
            if (w.Errand != 0) { Errand(nav, ref w, body, ref intent, w.Errand, w.Goal, w.Face); continue; }
            if (w.Posted != 0)
            {
                bool there = RouteSteer.Across(body.Feet, w.Post) <= RouteSteer.ArriveRadius && MathF.Abs(body.Feet.Z - w.Post.Z) < 1f;
                Errand(nav, ref w, body, ref intent, there || w.Arrived != 0 && Vector3.DistanceSquared(w.AskedGoal, w.Post) < 1e-4f ? RouteWalker.StandErrand : RouteWalker.WalkErrand, w.Post, w.PostYaw);
                continue;
            }
            if (nav is null) continue;
            if (w.Walking == 0)
            {
                if (w.Wait > 0f) { w.Wait -= dt; continue; }
                if (body.State != BodyState.Ground) continue;
                if (!nav.RandomReachable(body.Feet, w.WanderRadius, rng, out var goal) || !Ask(nav, ref w, body.Feet, goal))
                {
                    w.Wait = 0.5f;
                    continue;
                }
            }
            var corner = w.Corners[w.Next];
            float across = RouteSteer.Across(body.Feet, corner);
            bool last = w.Next == w.Count - 1;
            if (across < (last ? RouteSteer.ArriveRadius : RouteSteer.CornerRadius))
            {
                if (!last) { w.Next++; w.Nearest = float.MaxValue; w.Stalled = 0f; corner = w.Corners[w.Next]; }
                else if (RouteSteer.Across(corner, w.Destination) > RouteSteer.ArriveRadius)
                {
                    // A long route was held in part: ask on from here.
                    if (!Ask(nav, ref w, body.Feet, w.Destination)) { w.Walking = 0; w.Wait = 0.5f; continue; }
                    corner = w.Corners[w.Next];
                }
                else { w.Walking = 0; w.Wait = 1f; continue; }
            }
            across = RouteSteer.Across(body.Feet, corner);
            if (across < w.Nearest - RouteSteer.Progress) { w.Nearest = across; w.Stalled = 0f; }
            else if ((w.Stalled += dt) >= RouteSteer.StallSeconds)
            {
                w.Reasked++;
                if (!Ask(nav, ref w, body.Feet, w.Destination)) { w.Walking = 0; w.Wait = 0.5f; continue; }
                corner = w.Corners[w.Next];
            }
            intent = RouteSteer.Toward(body.Feet, corner);
        }
    }

    /// <summary>
    /// An errand: stand facing <paramref name="face"/>; or go to <paramref name="goal"/> by a route — asked again
    /// when the goal moves a quarter metre or the walk makes no progress for <see cref="RouteSteer.StallSeconds"/> — at a
    /// walk or a run with the route-followers' footing (no moves, never over a drop), and once there (or with no route
    /// to it) stand facing <paramref name="face"/>, <see cref="RouteWalker.Arrived"/> set.
    /// </summary>
    void Errand(LevelNavigation? nav, ref RouteWalker w, in Body body, ref MoveIntent intent, int errand, Vector3 goal, float face)
    {
        var stand = new MoveIntent { Gait = Gait.Walk, Facing = face, Faces = true };
        if (errand == RouteWalker.StandErrand) { w.Walking = 0; intent = stand; return; }
        var gait = errand == RouteWalker.RunErrand ? Gait.Run : Gait.Walk;
        if (Vector3.DistanceSquared(goal, w.AskedGoal) > 0.25f * 0.25f || (w.Walking == 0 && w.Arrived == 0))
        {
            w.AskedGoal = goal;
            w.Arrived = 0;
            if (body.State != BodyState.Ground) { w.Walking = 0; intent = stand; return; }
            if (nav is null || !Ask(nav, ref w, body.Feet, goal)) { w.Walking = 0; w.Arrived = 1; }
        }
        if (w.Arrived != 0 || w.Walking == 0) { intent = stand; return; }
        var corner = w.Corners[w.Next];
        bool last = w.Next == w.Count - 1;
        if (RouteSteer.Across(body.Feet, corner) < (last ? RouteSteer.ArriveRadius : RouteSteer.CornerRadius))
        {
            if (!last) { w.Next++; w.Nearest = float.MaxValue; w.Stalled = 0f; corner = w.Corners[w.Next]; }
            else if (RouteSteer.Across(corner, w.Destination) > RouteSteer.ArriveRadius && nav is not null && Ask(nav, ref w, body.Feet, w.Destination))
                corner = w.Corners[w.Next];
            else { w.Walking = 0; w.Arrived = 1; intent = stand; return; }
        }
        float across = RouteSteer.Across(body.Feet, corner);
        if (across < w.Nearest - RouteSteer.Progress) { w.Nearest = across; w.Stalled = 0f; }
        else if ((w.Stalled += World.TickSeconds) >= RouteSteer.StallSeconds)
        {
            w.Reasked++;
            if (nav is null || !Ask(nav, ref w, body.Feet, w.Destination)) { w.Walking = 0; w.Arrived = 1; intent = stand; return; }
            corner = w.Corners[w.Next];
        }
        intent = RouteSteer.Toward(body.Feet, corner, gait);
    }

    bool Ask(LevelNavigation nav, ref RouteWalker w, Vector3 from, Vector3 to)
    {
        w.Asked++;
        int n = nav.Route(from, to, buffer);
        if (n <= 0) return false;
        for (int k = 0; k < n; k++) w.Corners[k] = buffer[k];
        // A whole route ends at the goal's nearest point on the mesh, which becomes the destination.
        w.Count = n; w.Next = 0; w.Destination = n < buffer.Length ? buffer[n - 1] : to; w.Walking = 1;
        w.Nearest = float.MaxValue; w.Stalled = 0f;
        return true;
    }
}
