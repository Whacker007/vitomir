// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Navigation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Combat;

/// <summary>
/// The duelling brain closing on a foe <b>round a wall</b>. Measured first (the
/// brain, the proving ground's free yard, a guard 12 m from Christof with the 4.5 m wall between them): the brain runs straight at
/// its foe and stands pressed against the wall for the rest of the run. Now, while the brain closes, it looks along the straight line
/// to its foe each tenth of a second (<see cref="ScenarioDirector.LineMeets"/>: a sphere swept at knee-to-waist height against the
/// level's solid surfaces); <b>only when that line meets the level</b> does it ask the navigation mesh for a route and run it corner to
/// corner, facing the way, until the line is clear again. A clear line changes nothing — the brain's way, its draws and its state are
/// The earlier build's, so an open arena is master's cell for cell — and no route (or a straight one: the mesh walks over what the line met, a kerb
/// or a stair) leaves it closing straight as before. The switch <c>brain-straight</c> (<see cref="Enabled"/> false) is the earlier build's
/// brain exactly. A service registered by the enhanced module beside the brains; its state is not digested (it is made from the
/// bodies' places, which are). ENGINE_SANDBOX numbers (ours): the looking period, the corner radius and the stall, as the routes'.
/// 1.1's manoeuvre (<c>approach: oneone</c>) does not route: it is 1.1's own way of coming on.
/// </summary>
public sealed class DuelRoutes
{
    /// <summary>False with the switch <c>brain-straight</c>: the brain closes straight, as before.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Routes asked, and the closing ticks walked on a route (for diagnostics and tests).</summary>
    public int Asked { get; private set; }
    public long RoutedTicks { get; private set; }

    sealed class State
    {
        public readonly Vector3[] Corners = new Vector3[RouteWalker.Capacity];
        public int Count, Next;
        public long LastLook = -1, StraightUntil = -1;
        public Vector3 AskedGoal;
        public float Nearest = float.MaxValue, Stalled;
    }

    readonly Dictionary<EntityId, State> states = [];

    /// <summary>
    /// The way for <paramref name="who"/> closing from <paramref name="feet"/> on a foe at <paramref name="foe"/>: true with
    /// <paramref name="way"/> (unit, level) when a route round the level is being run; false to close straight as ever.
    /// </summary>
    public bool Way(World world, EntityId who, Vector3 feet, Vector3 foe, out Vector2 way)
    {
        way = default;
        if (!Enabled || !world.TryService<LevelSpace>(out var space)) return false;
        if (!states.TryGetValue(who, out var st)) states[who] = st = new State();
        long tick = world.Tick;
        if (st.LastLook < 0 || tick - st.LastLook >= ScenarioDirector.LineEveryTicks || tick - st.LastLook < 0)
        {
            st.LastLook = tick;
            if (!ScenarioDirector.LineMeets(space.Collision, feet, foe, out _)) st.Count = 0;
            else if (st.Count == 0 && tick >= st.StraightUntil || st.Count > 0 && Vector3.DistanceSquared(st.AskedGoal, foe) > 0.5f * 0.5f)
                Ask(space.Navigation, st, feet, foe, tick);
        }
        if (st.Count == 0) return false;
        while (st.Next < st.Count - 1 && RouteSteer.Across(feet, st.Corners[st.Next]) < RouteSteer.CornerRadius) { st.Next++; st.Nearest = float.MaxValue; st.Stalled = 0f; }
        var corner = st.Corners[st.Next];
        float across = RouteSteer.Across(feet, corner);
        if (across < st.Nearest - RouteSteer.Progress) { st.Nearest = across; st.Stalled = 0f; }
        else if ((st.Stalled += World.TickSeconds) >= RouteSteer.StallSeconds) { Ask(space.Navigation, st, feet, foe, tick); if (st.Count == 0) return false; corner = st.Corners[st.Next]; }
        var to = new Vector2(corner.X - feet.X, corner.Y - feet.Y);
        float l = to.Length();
        if (l < 1e-4f) return false;
        way = to / l;
        RoutedTicks++;
        return true;
    }

    void Ask(LevelNavigation? nav, State st, Vector3 feet, Vector3 foe, long tick)
    {
        st.AskedGoal = foe;
        st.Nearest = float.MaxValue; st.Stalled = 0f; st.Next = 0;
        int n = nav is null ? -1 : nav.Route(feet, foe, st.Corners);
        Asked++;
        // No route, or a straight one: close straight as before, and look again in a second.
        if (n <= 1) { st.Count = 0; st.StraightUntil = tick + World.TicksPerSecond; return; }
        st.Count = n;
    }

    /// <summary>Forget a fighter's route (its closing over).</summary>
    public void Clear(EntityId who) { if (states.TryGetValue(who, out var st)) st.Count = 0; }
}
