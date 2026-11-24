// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the swing's trail, a developer's aid for where the blade goes against where the crosshair points
// ("The crosshair in the middle of the screen is not where Christof is aiming his melee weapon").
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Presentation;

/// <summary>What a line of the swing's trail is: how the picture draws it.</summary>
public enum SwingTrailKind
{
    /// <summary>A piece of the blade's tip path through the active window.</summary>
    Tip,
    /// <summary>The blade itself (guard to tip) at one of the swept places, drawn lighter, every few.</summary>
    Blade,
    /// <summary>The reticle's world point as the blow began (a point: <see cref="SwingTrailLine.A"/> = <see cref="SwingTrailLine.B"/>).</summary>
    Reticle,
    /// <summary>The point of the tip's path nearest the reticle's ray (a point).</summary>
    Nearest,
    /// <summary>The miss: from that point to the ray's point nearest it.</summary>
    Miss,
    /// <summary>An arrow — its shaft, from its head back along its heading (<see cref="Combat.Arrow"/>), in flight or stuck; drawn in a world with a bow, whatever the trail's switch.</summary>
    Arrow,
    /// <summary>The bow's forecast path, the arc the drawn arrow would fly if loosed now (<see cref="Combat.BowForecast"/>; <c>showPath</c>).</summary>
    BowPath,
    /// <summary>Where the drawn arrow would land if loosed now (a point; <c>showImpact</c>).</summary>
    BowImpact,
    /// <summary>A shot's real path, kept a few seconds beside the forecast it was loosed on (<c>showPath</c>).</summary>
    BowFlown,
}

/// <summary>One line (or, A = B, one point) of the swing's trail to draw, and how much of it is left (1 new, 0 gone).</summary>
public readonly record struct SwingTrailLine(Vector3 A, Vector3 B, SwingTrailKind Kind, float Fade);

/// <summary>
/// The swing's trail (ours, <c>--show-swing</c>, the action <c>swingTrail</c>, no key by default): each swing's blade
/// as contact swept it — the <b>same</b> places (<see cref="Combat.ContactSystem"/> hands over the blades it swept each tick,
/// a fast swing's sub-steps too, cut where the blade stopped) — kept for <see cref="KeepSeconds"/> after its last, with,
/// for the viewer's own swings, the view's ray and the reticle's point as the blow began (what the player aimed at) and the
/// point of the tip's path nearest that ray. <b>Presentation only</b>: a service the picture (and a developer's tool) reads,
/// not digested; it records whether or not it is drawn, so turning the overlay on shows the last swings. Fixed storage, made
/// once (<see cref="MaxTrails"/> swings of <see cref="MaxPlaces"/> places each): nothing is allocated per tick.
/// </summary>
public sealed class SwingTrails
{
    /// <summary>How many swings are kept (the oldest is reused).</summary>
    public const int MaxTrails = 16;

    /// <summary>How many places one swing keeps (a 1.1 window is about 40 ticks; Celerity's sub-steps up to 8 a tick): the rest are dropped.</summary>
    public const int MaxPlaces = 512;

    /// <summary>How long a swing's trail is kept after its last place, s (ours: long enough to look at it); it fades over its last second.</summary>
    public float KeepSeconds { get; set; } = 4f;

    /// <summary>The blade is drawn at every this many places (and the last), so the swept surface reads without hiding the tip's path.</summary>
    public int BladeEvery { get; set; } = 3;

    /// <summary>One swing's trail: who, which swing, how many places, when, and the viewer's aim as the blow began.</summary>
    public struct Trail
    {
        public EntityId Of;
        public int Swing;
        /// <summary>Its slot (its places are kept there) and how many places it has.</summary>
        public int Slot, Count;
        public long FirstTick, LastTick;
        /// <summary>Whether the swing was the viewer's: the view's ray (from the camera, a unit direction) and, when the reticle was on something, its point.</summary>
        public bool HasRay, HasPoint;
        public Vector3 RayOrigin, RayDirection, Point;
        /// <summary>The tip path's closest approach to the ray (m; infinity until a piece is measured), the tip's point there and the ray's.</summary>
        public float Nearest;
        public Vector3 NearestTip, NearestOnRay;
    }

    /// <summary>The viewer's aim as contact sees it: none for anyone not viewed.</summary>
    public readonly record struct Aim(bool HasRay, Vector3 RayOrigin, Vector3 RayDirection, bool HasPoint, Vector3 Point)
    {
        public static Aim None => default;
    }

    readonly BladePlacement[] places = new BladePlacement[MaxTrails * MaxPlaces];
    readonly Trail[] trails = new Trail[MaxTrails];
    int next, count;

    /// <summary>How many swings are kept now (at most <see cref="MaxTrails"/>).</summary>
    public int Count => count;

    /// <summary>
    /// A swing begun (contact opens its record on the tick the blow begins): its trail made, empty, with the viewer's aim then —
    /// the view's ray and the reticle's point, what the player aimed at as he struck (in first person the eye rides the head,
    /// which the swing carries about; the trail keeps the aim of the blow's start).
    /// </summary>
    public void Begin(EntityId who, int swing, long tick, in Aim aim)
    {
        int t = next;
        next = (next + 1) % MaxTrails;
        if (count < MaxTrails) count++;
        trails[t] = new Trail { Of = who, Swing = swing, Slot = t, FirstTick = tick, LastTick = tick, Nearest = float.PositiveInfinity,
            HasRay = aim.HasRay, RayOrigin = aim.RayOrigin, RayDirection = aim.RayDirection, HasPoint = aim.HasPoint, Point = aim.Point };
    }

    /// <summary>
    /// The blades contact swept for <paramref name="who"/>'s swing <paramref name="swing"/> on <paramref name="tick"/>:
    /// <paramref name="blades"/> are the places from last tick's (0) to this tick's (its last), a fast swing's sub-steps
    /// between, evenly in the tick; <paramref name="until"/> (0 to 1, of the tick) where the blade stopped (1: it did not), the
    /// places after it left out and the blade's place there added. Last tick's place is kept too when the swing was not swept
    /// last tick (its first tick swept, or after its blade was held). A swing not begun (<see cref="Begin"/>) is begun now.
    /// </summary>
    public void Record(EntityId who, int swing, long tick, ReadOnlySpan<BladePlacement> blades, float until)
    {
        int n = blades.Length - 1;
        if (n < 1) return;
        int t = Find(who, swing);
        if (t < 0)
        {
            Begin(who, swing, tick, Aim.None);
            t = (next + MaxTrails - 1) % MaxTrails;
        }
        ref var tr = ref trails[t];
        if (tr.Count == 0 || tr.LastTick < tick - 1) Add(ref tr, blades[0]);
        tr.LastTick = tick;
        until = Math.Clamp(until, 0f, 1f);
        for (int k = 1; k <= n; k++)
        {
            float at = k / (float)n;
            if (at <= until) { Add(ref tr, blades[k]); continue; }
            float from = (k - 1) / (float)n;
            float share = (until - from) / (at - from);
            if (share > 1e-4f) Add(ref tr, Mix(blades[k - 1], blades[k], share));
            break;
        }
    }

    /// <summary>The newest trail kept of <paramref name="who"/>'s swing <paramref name="swing"/>, or −1.</summary>
    int Find(EntityId who, int swing)
    {
        int best = -1;
        for (int i = 0; i < count; i++)
        {
            ref var t = ref trails[i];
            if (t.Of != who || t.Swing != swing) continue;
            if (best < 0 || t.FirstTick > trails[best].FirstTick) best = i;
        }
        return best;
    }

    void Add(ref Trail t, BladePlacement b)
    {
        if (t.Count >= MaxPlaces) return;
        if (t.Count > 0 && t.HasRay)
        {
            var last = places[t.Slot * MaxPlaces + t.Count - 1].Tip;
            float d = SegmentToRay(last, b.Tip, t.RayOrigin, t.RayDirection, out var onTip, out var onRay);
            if (d < t.Nearest) (t.Nearest, t.NearestTip, t.NearestOnRay) = (d, onTip, onRay);
        }
        places[t.Slot * MaxPlaces + t.Count] = b;
        t.Count++;
    }

    static BladePlacement Mix(in BladePlacement a, in BladePlacement b, float s) =>
        new(Vector3.Lerp(a.Guard, b.Guard, s), Vector3.Lerp(a.Tip, b.Tip, s), a.Radius + (b.Radius - a.Radius) * s);

    /// <summary>
    /// The closest approach (m) of the segment <paramref name="a"/>–<paramref name="b"/> to the ray from <paramref name="o"/>
    /// along the unit <paramref name="d"/> (its points at distances 0 and on), with the two points.
    /// </summary>
    public static float SegmentToRay(Vector3 a, Vector3 b, Vector3 o, Vector3 d, out Vector3 onSegment, out Vector3 onRay)
    {
        var u = b - a;
        var w = a - o;
        float uu = Vector3.Dot(u, u), ud = Vector3.Dot(u, d), uw = Vector3.Dot(u, w), dw = Vector3.Dot(d, w);
        float den = uu - ud * ud;   // d is a unit
        float s = uu < 1e-12f ? 0f : den > 1e-9f ? Math.Clamp((ud * dw - uw) / den, 0f, 1f) : 0f;
        float t = MathF.Max(0f, Vector3.Dot(a + u * s - o, d));
        // With t clamped to the ray's start, the segment's point nearest that is taken again.
        if (uu > 1e-12f) s = Math.Clamp(Vector3.Dot(o + d * t - a, u) / uu, 0f, 1f);
        onSegment = a + u * s;
        t = MathF.Max(0f, Vector3.Dot(onSegment - o, d));
        onRay = o + d * t;
        return Vector3.Distance(onSegment, onRay);
    }

    /// <summary>The swing begun most recently by <paramref name="who"/> (its trail), if any (for a developer's tool and the tests).</summary>
    public bool Latest(EntityId who, out Trail trail)
    {
        trail = default;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            ref var t = ref trails[i];
            if (t.Of != who || (found && t.FirstTick < trail.FirstTick)) continue;
            (trail, found) = (t, true);
        }
        return found;
    }

    /// <summary><paramref name="trail"/>'s places, in order, into <paramref name="into"/> (cleared first; for a tool: it may grow the list).</summary>
    public void Places(in Trail trail, List<BladePlacement> into)
    {
        into.Clear();
        for (int i = 0; i < trail.Count; i++) into.Add(places[trail.Slot * MaxPlaces + i]);
    }

    /// <summary>
    /// The lines to draw at <paramref name="seconds"/> (the picture's time, the world's seconds between ticks) into
    /// <paramref name="into"/> (cleared first): for each swing kept that was swept, its tip path, its blade every
    /// <see cref="BladeEvery"/> places, and for the viewer's the reticle's point, the nearest point of the tip's path and the
    /// miss; each fading over the last second of <see cref="KeepSeconds"/>. A list grown once is not grown again.
    /// </summary>
    public void Fill(double seconds, List<SwingTrailLine> into)
    {
        into.Clear();
        int every = Math.Max(1, BladeEvery);
        for (int i = 0; i < count; i++)
        {
            ref var t = ref trails[i];
            if (t.Count == 0) continue;
            float age = (float)(seconds - t.LastTick / (double)World.TicksPerSecond);
            if (age > KeepSeconds) continue;
            float fade = Math.Clamp(KeepSeconds - MathF.Max(0f, age), 0f, 1f);
            int at = t.Slot * MaxPlaces;
            var previous = places[at];
            for (int k = 0; k < t.Count; k++)
            {
                var b = places[at + k];
                if (k > 0) into.Add(new SwingTrailLine(previous.Tip, b.Tip, SwingTrailKind.Tip, fade));
                if (k % every == 0 || k == t.Count - 1) into.Add(new SwingTrailLine(b.Guard, b.Tip, SwingTrailKind.Blade, fade));
                previous = b;
            }
            if (t.HasPoint) into.Add(new SwingTrailLine(t.Point, t.Point, SwingTrailKind.Reticle, fade));
            if (t.HasRay && float.IsFinite(t.Nearest))
            {
                into.Add(new SwingTrailLine(t.NearestTip, t.NearestTip, SwingTrailKind.Nearest, fade));
                if (t.Nearest > 1e-3f) into.Add(new SwingTrailLine(t.NearestTip, t.NearestOnRay, SwingTrailKind.Miss, fade));
            }
        }
    }
}
