// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Vitomir.Formats.L2;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Navigation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Control;

/// <summary>
/// <c>moveTo</c> round the level. Each tenth of a second the step looks along
/// the straight line from the body to its goal (a sphere of <see cref="LineRadius"/> swept at <see cref="LineHeight"/> over the feet,
/// against the level's solid surfaces). While it is clear the step walks straight, exactly as earlier (so a moveTo on open ground
/// gives the same run, digest for digest). When it meets the level the step asks the navigation mesh for a route: a route of more than one
/// corner is walked corner to corner (a <see cref="MoveToRouted"/> says so); a straight route (the mesh walks over what the line met: a
/// kerb, a stair) is walked straight as before; no route ends the step refused <c>NoRoute</c> with a <see cref="MoveToFailed"/>. A route
/// walked with no progress for <see cref="RouteSteer.StallSeconds"/> is asked again, and a second such stall ends the step
/// <c>Blocked</c>; a step that times out says so too (<see cref="MoveToFailure.TimedOut"/>), with the path the body actually walked. Its
/// numbers are ENGINE_SANDBOX (ours).
/// </summary>
public sealed partial class ScenarioDirector
{
    /// <summary>The height over the feet the straight line is looked along, and the sphere's radius (m; ENGINE_SANDBOX): above a kerb, below a lintel.</summary>
    public const float LineHeight = 0.9f, LineRadius = 0.2f;
    /// <summary>How often the straight line is looked along (ticks: a tenth of a second).</summary>
    public const int LineEveryTicks = 12;
    /// <summary>How often the walked path is sampled (ticks), and the most points it keeps (thinned by half when full).</summary>
    const int WalkEveryTicks = 30, WalkMost = 64;

    /// <summary>A moveTo's route and its record of the walk (per track; the route digested only while one is walked).</summary>
    internal sealed class RouteRun
    {
        public readonly Vector3[] Corners = new Vector3[RouteWalker.Capacity];
        public readonly Vector2[] Walked = new Vector2[WalkMost];
        public int Count, Next, Asked, Stalls, WalkedCount, WalkStride = 1, StraightStalls;
        public long LastLook = -1, StraightUntil = -1, LastSample;
        public Vector3 AskedGoal, BlockedAt, LastFeet, StuckAt;
        public float Nearest, Stalled, WalkedMetres, Closest, StuckTime;
        public bool Started;

        public void Reset()
        {
            Count = Next = Asked = Stalls = WalkedCount = StraightStalls = 0;
            StuckTime = 0f;
            WalkStride = 1;
            LastLook = StraightUntil = -1;
            LastSample = 0;
            Nearest = float.MaxValue; Stalled = 0f; WalkedMetres = 0f; Closest = float.MaxValue;
            Started = false;
        }

        /// <summary>A path's next leg: the route and the stall start afresh; the walked path and the asks go on.</summary>
        public void NextLeg()
        {
            Count = Next = Stalls = StraightStalls = 0;
            StuckTime = 0f; Stalled = 0f;
            LastLook = StraightUntil = -1;
            Nearest = float.MaxValue; Closest = float.MaxValue;
            StuckAt = LastFeet;
        }

        /// <summary>Only a route walked is digested (a moveTo walking straight adds nothing: the earlier build's digests stand).</summary>
        public void Digest(ref Fnv1a64 h)
        {
            if (StraightStalls > 0) h.Add(StraightStalls);
            if (Count == 0) return;
            h.Add(Count); h.Add(Next); h.Add(Asked); h.Add(Stalls); h.Add(Stalled); h.Add(Nearest);
            for (int i = 0; i < Count; i++) { h.Add(Corners[i].X); h.Add(Corners[i].Y); h.Add(Corners[i].Z); }
        }
    }

    StepResult MoveToRouted(ActorRun a, Track t, MoveToStep mt, bool first)
    {
        ref var b = ref bodies.Get(a.Entity);
        ref var m = ref MoveOf(a, ControlChannels.Move | ControlChannels.Face);
        if (Unsafe.IsNullRef(ref m) || !Where(mt.To, b.Feet, out var to, out _)) return StepResult.Refused(StepRefusal.NoTarget);
        var r = t.Route;
        if (first || !r.Started) StartWalk(r, b.Feet);
        if (Leg(a, t, mt.Label, ref m, in b, to, mt.Gait, mt.WithinMetres, brake: true, out bool there) is { } failed) return failed;
        if (mt.FaceWhileMoving is { } face && Feet(Entity(face), out var ff)) FaceToward(a, ref m, b.Feet, ff);
        else FaceWay(a, ref m);
        return there ? StepResult.Done : StepResult.Running;
    }

    /// <summary>
    /// A <c>path</c> round the level: each leg to its next waypoint is walked as a moveTo's (straight while the line is clear, the mesh's
    /// route when it meets the level, a failure said), braking only for the last; the earlier build's walk on open ground.
    /// </summary>
    StepResult PathRouted(ActorRun a, Track t, PathStep p, bool first)
    {
        ref var b = ref bodies.Get(a.Entity);
        ref var m = ref MoveOf(a, ControlChannels.Move | ControlChannels.Face);
        if (Unsafe.IsNullRef(ref m) || p.Points.Count == 0) return StepResult.Refused(StepRefusal.NoTarget);
        var r = t.Route;
        if (first || !r.Started) StartWalk(r, b.Feet);
        FaceWay(a, ref m);
        for (int guard = 0; guard <= p.Points.Count; guard++)
        {
            int i = t.Run.Index;
            var point = p.Points[i];
            var to = new Vector3(point.X, point.Y, point.Z ?? b.Feet.Z);
            bool last = i == p.Points.Count - 1 && !p.Loop;
            if (Leg(a, t, p.Label, ref m, in b, to, p.Gait, p.WithinMetres, brake: last, out bool there) is { } failed) return failed;
            if (!there) return StepResult.Running;
            if (last) return StepResult.Done;
            t.Run.Index = (i + 1) % p.Points.Count;
            r.NextLeg();
        }
        return StepResult.Running;
    }

    void StartWalk(RouteRun r, Vector3 feet) { r.Reset(); r.Started = true; r.LastFeet = feet; r.StuckAt = feet; r.LastSample = tick; AddWalked(r, feet); }

    /// <summary>
    /// One leg toward <paramref name="to"/>: straight as before while the line is clear, the mesh's route when it meets the level; null while
    /// it goes (<paramref name="there"/> once within <paramref name="within"/>), else the step's end (refused NoRoute, timed out blocked).
    /// </summary>
    StepResult? Leg(ActorRun a, Track t, string? label, ref MoveIntent m, in Body b, Vector3 to, Gait gait, float within, bool brake, out bool there)
    {
        var r = t.Route;
        Record(r, b.Feet, to);

        // Look along the straight line now and then (and whenever no route is held and the goal moved off the one asked).
        if (r.LastLook < 0 || tick - r.LastLook >= LineEveryTicks)
        {
            r.LastLook = tick;
            var space = w.TryService<LevelSpace>(out var sp) ? sp : null;
            bool blocked = space is not null && LineMeets(space.Collision, b.Feet, to, out r.BlockedAt);
            if (!blocked) { if (r.Count > 0 && r.StraightStalls == 0) r.Count = 0; }   // the line clear again: straight (not when the straight walk stuck: the route was taken for that)
            else if (r.Count == 0 && tick >= r.StraightUntil || r.Count > 0 && Vector3.DistanceSquared(r.AskedGoal, to) > 0.25f * 0.25f)
            {
                var nav = space!.Navigation;
                int n = nav is null ? -1 : nav.Route(b.Feet, to, r.Corners);
                if (n < 0)
                {
                    PublishMoveFailed(a, t, label, MoveToFailure.NoRoute, to, b.Feet, NoRouteWhy(nav, b.Feet, to));
                    { there = false; return StepResult.Refused(StepRefusal.NoRoute); }
                }
                r.AskedGoal = to;
                if (n <= 1) { r.Count = 0; r.StraightUntil = tick + World.TicksPerSecond; }   // the mesh walks it straight: as before, looked at again in a second
                else
                {
                    r.Count = n; r.Next = 0; r.Asked++; r.Nearest = float.MaxValue; r.Stalled = 0f;
                    PublishRouted(a, t, label, b.Feet, to, r);
                }
            }
        }

        if (r.Count == 0)
        {
            there = Go(a, ref m, in b, to, gait, within, brake);   // straight: the earlier build's walk
            // Asked to move and not moving (the body's own feet, so a goal that walks away is no stall): ask the mesh for a way round
            // after one stall, and say it is blocked after a second (a doorway narrower than the body, another body in the way).
            if (m.Direction == Vector2.Zero || RouteSteer.Across(b.Feet, r.StuckAt) > 0.1f) { r.StuckAt = b.Feet; r.StuckTime = 0f; }
            else if ((r.StuckTime += World.TickSeconds) >= RouteSteer.StallSeconds)
            {
                r.StuckTime = 0f;
                r.StraightStalls++;
                var nav = w.TryService<LevelSpace>(out var sp2) ? sp2.Navigation : null;
                int n = r.StraightStalls == 1 && nav is not null ? nav.Route(b.Feet, to, r.Corners) : -1;
                if (n > 1) { r.Count = n; r.Next = 0; r.Asked++; r.AskedGoal = to; r.Nearest = float.MaxValue; r.Stalled = 0f; r.BlockedAt = b.Feet; PublishRouted(a, t, label, b.Feet, to, r); }
                else if (r.StraightStalls >= 2)
                {
                    m.Direction = Vector2.Zero;
                    PublishMoveFailed(a, t, label, MoveToFailure.Blocked, to, b.Feet,
                        F($"the body did not move for {2 * RouteSteer.StallSeconds:0.#} s walking straight at ({b.Feet.X:0.##}, {b.Feet.Y:0.##}), {RouteSteer.Across(b.Feet, to):0.##} m from the goal, and the navigation mesh has no other way: something it does not know stops the body (a doorway narrower than it, another body)"));
                    { there = false; return StepResult.TimedOut("blocked"); }
                }
            }
        }
        else
        {
            // Corner to corner; the last corner is the goal itself (braking for it).
            while (r.Next < r.Count - 1 && RouteSteer.Across(b.Feet, r.Corners[r.Next]) < RouteSteer.CornerRadius)
            {
                r.Next++; r.Nearest = float.MaxValue; r.Stalled = 0f;
            }
            bool last = r.Next >= r.Count - 1;
            var corner = last ? to : r.Corners[r.Next];
            there = Go(a, ref m, in b, corner, gait, last ? within : RouteSteer.CornerRadius * 0.5f, brake: last && brake) && last;
            float across = RouteSteer.Across(b.Feet, corner);
            if (across < r.Nearest - RouteSteer.Progress) { r.Nearest = across; r.Stalled = 0f; r.Stalls = 0; }
            else if ((r.Stalled += World.TickSeconds) >= RouteSteer.StallSeconds)
            {
                r.Stalls++;
                if (r.Stalls >= 2)
                {
                    m.Direction = Vector2.Zero;
                    PublishMoveFailed(a, t, label, MoveToFailure.Blocked, to, b.Feet,
                        F($"no progress along its route for {2 * RouteSteer.StallSeconds:0.#} s (asked again between) at ({b.Feet.X:0.##}, {b.Feet.Y:0.##}): something the navigation mesh does not know stops the body (a doorway narrower than it, another body)"));
                    { there = false; return StepResult.TimedOut("blocked"); }
                }
                var nav = w.Service<LevelSpace>().Navigation;
                int n = nav is null ? -1 : nav.Route(b.Feet, to, r.Corners);
                if (n > 1) { r.Count = n; r.Next = 0; r.Asked++; r.AskedGoal = to; PublishRouted(a, t, label, b.Feet, to, r); }
                // Progress from here on is coming nearer the corner it now walks to (not the ask itself), so a body held in place stalls again.
                r.Nearest = RouteSteer.Across(b.Feet, r.Next < r.Count - 1 ? r.Corners[r.Next] : to); r.Stalled = 0f;
            }
        }
        return null;
    }

    /// <summary>Whether the straight line from <paramref name="feet"/> to <paramref name="goal"/>, at <see cref="LineHeight"/> over each, meets the level's solid surfaces; where.</summary>
    public static bool LineMeets(CollisionWorld level, Vector3 feet, Vector3 goal, out Vector3 at)
    {
        var from = feet + new Vector3(0, 0, LineHeight);
        var delta = goal + new Vector3(0, 0, LineHeight) - from;
        float length = delta.Length();
        at = default;
        if (length < 1e-3f) return false;
        var hit = level.SphereSweep(from, delta / length, length, LineRadius, L2Surface.Solid);
        if (hit is not { } h) return false;
        at = h.Point;
        return true;
    }

    static string NoRouteWhy(LevelNavigation? nav, Vector3 feet, Vector3 to)
    {
        if (nav is null) return "the straight line meets the level and the level has no navigation mesh to route by";
        bool start = nav.Nearest(feet, out _) != 0, goal = nav.Nearest(to, out _) != 0;
        return !start ? F($"the straight line meets the level and the body stands off the navigation mesh at ({feet.X:0.##}, {feet.Y:0.##})")
            : !goal ? F($"the straight line meets the level and the goal ({to.X:0.##}, {to.Y:0.##}) is off the navigation mesh (inside or on top of something, or beyond the floor)")
            : F($"the straight line meets the level and the navigation mesh has no route from ({feet.X:0.##}, {feet.Y:0.##}) to ({to.X:0.##}, {to.Y:0.##}): they are not joined");
    }

    /// <summary>The walk's record: the path's samples, its length, the nearest the body came to the goal.</summary>
    void Record(RouteRun r, Vector3 feet, Vector3 to)
    {
        r.WalkedMetres += new Vector2(feet.X - r.LastFeet.X, feet.Y - r.LastFeet.Y).Length();
        r.LastFeet = feet;
        r.Closest = MathF.Min(r.Closest, RouteSteer.Across(feet, to));
        if (tick - r.LastSample >= WalkEveryTicks * r.WalkStride) { r.LastSample = tick; AddWalked(r, feet); }
    }

    static void AddWalked(RouteRun r, Vector3 feet)
    {
        if (r.WalkedCount == WalkMost)
        {
            for (int i = 0; i < WalkMost / 2; i++) r.Walked[i] = r.Walked[i * 2];
            r.WalkedCount = WalkMost / 2;
            r.WalkStride *= 2;
        }
        r.Walked[r.WalkedCount++] = new Vector2(feet.X, feet.Y);
    }

    /// <summary>Publishes a moveTo's failure with the path walked (called by the step, and by the director's time-out for a moveTo).</summary>
    void PublishMoveFailed(ActorRun a, Track t, string? label, MoveToFailure why, Vector3 to, Vector3 at, string reason)
    {
        var r = t.Route;
        var walked = new StringBuilder();
        for (int i = 0; i < r.WalkedCount; i++) Point(walked, r.Walked[i].X, r.Walked[i].Y);
        if (r.WalkedCount == 0 || r.Walked[r.WalkedCount - 1] != new Vector2(at.X, at.Y)) Point(walked, at.X, at.Y);
        var route = new StringBuilder();
        for (int i = 0; i < r.Count; i++) Point(route, r.Corners[i].X, r.Corners[i].Y);
        var instr = t.Code[t.Pc];
        float left = RouteSteer.Across(at, to);
        w.Events<MoveToFailed>().Publish(new MoveToFailed(a.Entity, tick, t.Number, instr.Number, label, t.Run.Instance, why, to, at,
            MathF.Min(r.Closest, left), left, r.WalkedMetres, walked.ToString(), route.ToString(), reason));
    }

    /// <summary>The time-out of a moveTo (from the director's <c>forSeconds</c> check): its <see cref="MoveToFailure.TimedOut"/>.</summary>
    void MoveToTimedOut(ActorRun a, Track t, ScenarioStep s)
    {
        ref var b = ref bodies.Find(a.Entity);
        if (Unsafe.IsNullRef(ref b)) return;
        Vector3 to;
        if (s is MoveToStep mt) { if (!Where(mt.To, b.Feet, out to, out _)) return; }
        else if (s is PathStep { Points.Count: > 0 } p) { var q = p.Points[Math.Clamp(t.Run.Index, 0, p.Points.Count - 1)]; to = new Vector3(q.X, q.Y, q.Z ?? b.Feet.Z); }
        else return;
        var r = t.Route;
        if (!r.Started) StartWalk(r, b.Feet);
        Record(r, b.Feet, to);
        PublishMoveFailed(a, t, s.Label, MoveToFailure.TimedOut, to, b.Feet,
            F($"forSeconds ({s.ForSeconds:0.##} s) passed {RouteSteer.Across(b.Feet, to):0.##} m from {(s is PathStep ? $"its waypoint {t.Run.Index}" : "the goal")}{(r.Count > 0 ? ", walking its route" : ", walking straight")}"));
    }

    void PublishRouted(ActorRun a, Track t, string? label, Vector3 feet, Vector3 to, RouteRun r)
    {
        var path = new StringBuilder();
        float metres = 0f;
        var prev = feet;
        for (int i = 0; i < r.Count; i++)
        {
            Point(path, r.Corners[i].X, r.Corners[i].Y);
            metres += new Vector2(r.Corners[i].X - prev.X, r.Corners[i].Y - prev.Y).Length();
            prev = r.Corners[i];
        }
        var instr = t.Code[t.Pc];
        w.Events<MoveToRouted>().Publish(new MoveToRouted(a.Entity, tick, t.Number, instr.Number, label, t.Run.Instance, feet, to, r.BlockedAt,
            r.Count, metres, RouteSteer.Across(feet, to), path.ToString(), r.Asked));
    }

    static void Point(StringBuilder sb, float x, float y)
    {
        if (sb.Length > 0) sb.Append(';');
        sb.Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(',').Append(y.ToString("0.##", CultureInfo.InvariantCulture));
    }

    static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
