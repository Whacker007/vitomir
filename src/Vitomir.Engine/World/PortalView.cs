// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Which sectors a camera sees, through the portals: the sector walk (one routine starts one,
// one adds a sector to it, one recurses) with the portal's two tests.
// Read for the reflection pass.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>A rectangle on the screen, in normalised device coordinates (x right, y up, −1 to 1).</summary>
public readonly record struct ScreenRect(float X0, float Y0, float X1, float Y1)
{
    /// <summary>The whole view.</summary>
    public static readonly ScreenRect Full = new(-1f, -1f, 1f, 1f);
    /// <summary>
    /// Nothing — inverted, so that a union with it is the other rectangle. The engine starts the
    /// mirror's rectangle this way (the world draw loads the viewport's corners crossed).
    /// </summary>
    public static readonly ScreenRect None = new(1f, 1f, -1f, -1f);
    public bool IsEmpty => !(X0 < X1 && Y0 < Y1);
    /// <summary>The smallest rectangle holding both.</summary>
    public ScreenRect Union(ScreenRect o) => new(MathF.Min(X0, o.X0), MathF.Min(Y0, o.Y0), MathF.Max(X1, o.X1), MathF.Max(Y1, o.Y1));
    /// <summary>Where both are.</summary>
    public ScreenRect Intersect(ScreenRect o) => new(MathF.Max(X0, o.X0), MathF.Max(Y0, o.Y0), MathF.Min(X1, o.X1), MathF.Min(Y1, o.Y1));
}

/// <summary>
/// What the portal tests take from the engine's camera beyond its eye and its view-projection:
/// the way it looks, the cosine of half its horizontal field of
/// view and its far clip — and, ours, the sine of half the
/// view's corner-to-corner angle, which is what the facing test compares against in Vitomir
/// (<see cref="PortalView"/>, *the facing test on a wide screen*).
/// </summary>
public readonly record struct PortalCamera(Vector3 Forward, float CosHalfFov, float Far, float SinHalfCorner)
{
    /// <summary>
    /// The facing test's bound (the second half): a portal is passed over when the camera looks
    /// along its normal — away from it — by more than this. **1.1 compares against the cosine of half the
    /// horizontal field of view**, which is only safe while that half is no wider than 90° less half the
    /// corner-to-corner angle: at 1.1's 4:3 (40° across, 46.4° to the corner) it is, and never drops a
    /// portal on the screen. Vitomir widens the view across on a wider window (48.2° at 16:9), and 1.1's
    /// bound then drops a sector seen at the screen's edge for the frames a turning camera spends
    /// between 37.9° and 48.2° off the portal's normal — a stretch of street gone to the sky for a
    /// frame or two: the flashing seen in play. Every point of a portal lies
    /// at least 90° off its normal, so a look within <c>90° − corner</c> of the normal cannot see it:
    /// <c>cos θ &gt; sin(corner)</c> is the test that drops only what is off the screen. At 4:3 it drops
    /// exactly what 1.1's drops that the quad test would not keep anyway — the walk is the same list.
    /// </summary>
    public float FacingBound => SinHalfCorner;

    /// <summary>
    /// The same from a view and a projection built the way Vitomir builds them (right-handed
    /// <c>CreateLookAt</c> and <c>CreatePerspectiveFieldOfView</c>): the look is the view's third
    /// column reversed, the half field of view across is <c>atan(1 / M11)</c>, and the far clip is
    /// <c>M43 / (1 + M33)</c>.
    /// </summary>
    public static PortalCamera From(Matrix4x4 view, Matrix4x4 projection)
    {
        var forward = Vector3.Normalize(-new Vector3(view.M13, view.M23, view.M33));
        float m = projection.M11;
        float cos = m / MathF.Sqrt(1f + m * m);
        float far = MathF.Abs(1f + projection.M33) < 1e-12f ? float.PositiveInfinity : projection.M43 / (1f + projection.M33);
        // The corner: tan² across plus tan² up and down (M22 is 1 / tan of half the view up and down).
        float across = 1f / m, up = 1f / projection.M22;
        float corner = MathF.Sqrt(across * across + up * up);
        return new PortalCamera(forward, cos, far, corner / MathF.Sqrt(1f + corner * corner));
    }
}

/// <summary>
/// A portal walk: the sectors a camera sees and, for each, the rectangle of the screen it is seen
/// through — the engine's list (0x30 bytes an entry: the sector and its rectangle; at most 255,
/// the walk asserts <c>numSectors &lt; SECTOR_MAX_VISIBLE</c>).
/// <list type="bullet">
/// <item><b>The start</b>: the list emptied, the sector the camera is in put first with
/// the whole view (or, for a mirror, the mirror's rectangle);</item>
/// <item><b>each sector's portals</b>: a portal that is not closed, whose plane the eye
/// is in front of (<c>(eye − corner) · normal ≥ 0</c>), and whose quad, taken to the
/// screen, clipped to the near plane (<c>w &gt; 0.01</c>) and to this sector's rectangle,
/// leaves anything, opens the sector beyond with that rectangle; a sector already in
/// the list is given the union of the two and walked again only when that made it bigger.</item>
/// </list>
/// <b>The camera's own tests</b> (with a <see cref="PortalCamera"/>): a portal is also passed over when
/// the camera looks along its normal by less than half the field of view (the camera's
/// look dotted with the normal, against <c>cos(fov / 2)</c>, which
/// the projection works out with its sine table) — the portal lies wholly behind the eye — or
/// when all four of its corners lie past the far clip (<c>w &gt; far</c>, the
/// scene's <c>RENDERINFO_ZCLIP</c> or 10,000, set by the scene's start). The engine keeps each portal's
/// answer for the walk (the walk's stamp, bit 2 the verdict); it is the same
/// answer every time it is asked, so Vitomir asks again.
/// <b>Closed portals:</b> a portal whose flag bit 0 is set is not looked through. Nothing
/// in 1.1 sets it but the script natives <c>Sector.DisablePortals</c> (<c>EnablePortals</c>
/// clears it), and no shipped script uses <c>CodexSector</c> at all — a door does
/// <b>not</b> close its portal (read again: the only other code that walks a sector's
/// portals is the collision search and the level load, and neither writes the bit). So in the
/// shipped game every portal is open, as here.
/// </summary>
public sealed class PortalView
{
    /// <summary>The most sectors a walk takes (<c>0xfe &lt; count</c> asserts).</summary>
    public const int MaxSectors = 255;
    /// <summary>A corner at or behind this <c>w</c> is behind the eye (0.01).</summary>
    public const float NearW = 0.01f;

    readonly List<(int Sector, ScreenRect Rect)> visible = [];
    int[] slot = [];
    int steps;

    /// <summary>
    /// **Ours**: how many sectors one walk may open or re-open before it stops. A sector reached
    /// again with a larger rectangle is walked again (as 1.1 does); this bounds a ring of portals
    /// whose rectangles keep growing by a hair.
    /// </summary>
    public const int MaxSteps = 4096;
    NilLevel? level;
    Matrix4x4 viewProj;
    Vector3 eye;
    PortalCamera? camera;

    /// <summary>How many sectors the last walk opened or re-opened (ours; at most a little over <see cref="MaxSteps"/>).</summary>
    public int Steps => steps;

    /// <summary>The sectors of the last walk, in the order they were found, with their rectangles.</summary>
    public IReadOnlyList<(int Sector, ScreenRect Rect)> Visible => visible;

    /// <summary>Whether a sector was reached by the last walk.</summary>
    public bool Sees(int sector) => sector >= 0 && sector < slot.Length && slot[sector] >= 0;

    /// <summary>
    /// A new walk from <paramref name="start"/>, seen through <paramref name="rect"/>, by a
    /// camera at <paramref name="eyeAt"/> whose view and projection together are
    /// <paramref name="viewProjection"/> (row vectors: clip = point × matrix). With
    /// <paramref name="cam"/> the camera's own tests are made too (the facing test's second half and
    /// the far test); without it only the eye's side of each portal and its quad on the screen count.
    /// </summary>
    public void Walk(NilLevel nil, int start, Vector3 eyeAt, Matrix4x4 viewProjection, ScreenRect rect, PortalCamera? cam = null)
    {
        level = nil;
        eye = eyeAt;
        viewProj = viewProjection;
        camera = cam;
        visible.Clear();
        steps = 0;
        if (slot.Length != nil.Sectors.Length) slot = new int[nil.Sectors.Length];
        Array.Fill(slot, -1);
        if (start < 0 || start >= nil.Sectors.Length) return;
        Add(start, rect);
    }

    /// <summary>
    /// **The sector a walk starts from, settled** (ours): the sector search (<see cref="Collision.SectorAt"/>)
    /// counts a point up to <see cref="Collision.Slack"/> past a sector's plane as still inside it, and
    /// tries the last frame's sector first, so an eye that has just crossed into the next sector can be
    /// answered with the one it left. The walk's own test on the eye's side of a portal has no slack,
    /// so from there it would pass over the portal into the sector the eye is really in, and
    /// that sector and everything seen through it would go undrawn for the frame. An eye behind one of
    /// its start sector's portals, inside the sector beyond, starts from that one instead — the one it
    /// is deepest inside, where two portals share the plane it has crossed.
    /// </summary>
    public static int SettledStart(NilLevel nil, Collision solid, int start, Vector3 eye)
    {
        for (int hops = 0; hops < 4 && start >= 0 && start < nil.Sectors.Length; hops++)
        {
            int beyond = -1;
            float deepest = float.NegativeInfinity;
            foreach (var portal in nil.Sectors[start].Portals)
            {
                if ((portal.Flags & 1) != 0 || portal.Quad.Length < 3) continue;
                if (portal.NeighbourSector >= (uint)nil.Sectors.Length) continue;
                if (Vector3.Dot(eye - portal.Quad[0], portal.Normal) >= 0f) continue;
                int next = (int)portal.NeighbourSector;
                if (!solid.Contains(next, eye)) continue;
                float depth = solid.Depth(next, eye);
                if (depth > deepest) { deepest = depth; beyond = next; }
            }
            if (beyond < 0) break;
            start = beyond;
        }
        return start;
    }

    /// <summary>Another sector put in the same walk (a second mirror), with its rectangle, and walked from.</summary>
    public void Add(int sector, ScreenRect rect)
    {
        if (level is null || sector < 0 || sector >= level.Sectors.Length) return;
        if (slot[sector] < 0)
        {
            if (visible.Count >= MaxSectors) return;
            slot[sector] = visible.Count;
            visible.Add((sector, rect));
        }
        else visible[slot[sector]] = (sector, rect);
        Recurse(sector);
    }

    /// <summary>
    /// The walk on from <paramref name="sector"/> — each portal on the eye's side, facing the
    /// camera and on the screen within the sector's rectangle takes the walk into the sector beyond, first
    /// time or with a rectangle that grew, depth first, in the portals' order.
    /// <para>**Ours: a stack of our own instead of the machine's.** 1.1 recurses (the walk calls itself for
    /// each sector it opens or re-opens), and so did Vitomir until then, when a play-test
    /// crashed in the third monastery level: there a ring of portals re-opens its sectors with rectangles
    /// that grow by a hair each time round, the walk ran to its <see cref="MaxSteps"/> as nested calls, and
    /// on Windows (a 1 MB main-thread stack) that many frames of ours overflowed it. This loop walks the
    /// sectors in exactly the order the recursion did — each entry keeps its sector, where it is in the
    /// sector's portals and the rectangle it started from, as a call's locals did — so what is found, and
    /// in what order, is unchanged (<c>PortalWalkTests</c>).</para>
    /// </summary>
    void Recurse(int sector)
    {
        if (++steps > MaxSteps) return;
        var stack = frames;
        stack.Clear();
        stack.Add(new Frame(sector, 0, visible[slot[sector]].Rect));
        while (stack.Count > 0)
        {
            int top = stack.Count - 1;
            var (at, from, parent) = stack[top];
            var portals = level!.Sectors[at].Portals;
            bool deeper = false;
            for (int i = from; i < portals.Length; i++)
            {
                var portal = portals[i];
                if ((portal.Flags & 1) != 0 || portal.Quad.Length < 3) continue;
                int next = (int)portal.NeighbourSector;
                if (next < 0 || next >= level.Sectors.Length) continue;
                if (Vector3.Dot(eye - portal.Quad[0], portal.Normal) < 0f) continue;
                if (camera is { } c && Vector3.Dot(c.Forward, portal.Normal) > c.FacingBound) continue;  // the facing test (see FacingBound)
                if (!Project(portal.Quad, viewProj, parent, out var rect, NearW, camera?.Far ?? float.PositiveInfinity)) continue;
                if (slot[next] >= 0)
                {
                    var had = visible[slot[next]].Rect;
                    var grown = had.Union(rect);
                    if (grown == had) continue;
                    visible[slot[next]] = (next, grown);
                }
                else
                {
                    if (visible.Count >= MaxSectors) continue;
                    slot[next] = visible.Count;
                    visible.Add((next, rect));
                }
                // The recursive call: past the cap it returns at once, and this sector's own loop goes on.
                if (++steps > MaxSteps) continue;
                stack[top] = new Frame(at, i + 1, parent);
                stack.Add(new Frame(next, 0, visible[slot[next]].Rect));
                deeper = true;
                break;
            }
            if (!deeper) stack.RemoveAt(top);
        }
    }

    /// <summary>One sector of the walk under way: which, the next of its portals to try, and the rectangle it started with (ours).</summary>
    readonly record struct Frame(int Sector, int Next, ScreenRect Parent);
    readonly List<Frame> frames = [];

    /// <summary>
    /// The portal's screen test (and the surface's, with a near <c>w</c> of 0.1): a polygon taken to the
    /// screen — into clip space by the view-projection, clipped to <c>w &gt; 0.01</c>, divided through, and clipped to
    /// <paramref name="within"/> — and the rectangle round what is left. False when nothing is, or
    /// when every corner lies past <paramref name="far"/> (the far test, <c>w &gt; far</c>
    /// for all four corners of a portal).
    /// </summary>
    public static bool Project(IReadOnlyList<Vector3> polygon, Matrix4x4 viewProjection, ScreenRect within, out ScreenRect rect,
                               float near = NearW, float far = float.PositiveInfinity)
    {
        // The same arithmetic in room on the stack: this is asked for every portal and every
        // mirror surface of every frame, and the lists it used to make were most of what the view
        // allocated. A polygon too big for the room goes the old way.
        rect = ScreenRect.None;
        int n = polygon.Count;
        if (n > StackSides) return ProjectOnTheHeap(polygon, viewProjection, within, out rect, near, far);
        Span<Vector4> clip = stackalloc Vector4[StackSides];
        int beyond = 0;
        for (int i = 0; i < n; i++)
        {
            var v = Vector4.Transform(new Vector4(polygon[i], 1f), viewProjection);
            if (far < v.W) beyond++;
            clip[i] = v;
        }
        if (beyond == n) return false;
        Span<Vector2> flat = stackalloc Vector2[StackRoom], other = stackalloc Vector2[StackRoom];
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            var a = clip[i];
            var b = clip[(i + 1) % n];
            float da = a.W - near, db = b.W - near;
            if (da > 0f) flat[count++] = new Vector2(a.X / a.W, a.Y / a.W);
            if ((da > 0f) != (db > 0f)) { var c = a + (b - a) * (da / (da - db)); flat[count++] = new Vector2(c.X / c.W, c.Y / c.W); }
        }
        if (count < 3) return false;
        for (int side = 0; side < 4; side++)
        {
            if (count == 0) break;
            if (count * 2 > StackRoom) return ProjectOnTheHeap(polygon, viewProjection, within, out rect, near, far);
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                var a = flat[i];
                var b = flat[(i + 1) % count];
                float da = Inside(a, side, within), db = Inside(b, side, within);
                if (da >= 0f) other[made++] = a;
                if ((da >= 0f) != (db >= 0f)) other[made++] = Vector2.Lerp(a, b, da / (da - db));
            }
            var swap = flat; flat = other; other = swap;
            count = made;
        }
        if (count < 3) return false;
        float x0 = flat[0].X, x1 = flat[0].X, y0 = flat[0].Y, y1 = flat[0].Y;
        for (int i = 1; i < count; i++)
        {
            x0 = MathF.Min(x0, flat[i].X); x1 = MathF.Max(x1, flat[i].X);
            y0 = MathF.Min(y0, flat[i].Y); y1 = MathF.Max(y1, flat[i].Y);
        }
        // Held to the rectangle: the clip's interpolation can land a hair outside it.
        rect = new ScreenRect(x0, y0, x1, y1).Intersect(within);
        return !rect.IsEmpty;
    }

    /// <summary>The most corners <see cref="Project"/> takes on the stack, and the room it clips them in.</summary>
    const int StackSides = 16, StackRoom = 64;

    static float Inside(Vector2 p, int side, ScreenRect within) => side switch
    {
        0 => p.X - within.X0,
        1 => within.X1 - p.X,
        2 => p.Y - within.Y0,
        _ => within.Y1 - p.Y,
    };

    /// <summary><see cref="Project"/> as it was written first, with lists: for a polygon too big for the stack's room.</summary>
    static bool ProjectOnTheHeap(IReadOnlyList<Vector3> polygon, Matrix4x4 viewProjection, ScreenRect within, out ScreenRect rect,
                               float near = NearW, float far = float.PositiveInfinity)
    {
        rect = ScreenRect.None;
        var clip = new List<Vector4>(polygon.Count + 2);
        int beyond = 0;
        foreach (var p in polygon)
        {
            var v = Vector4.Transform(new Vector4(p, 1f), viewProjection);
            if (far < v.W) beyond++;
            clip.Add(v);
        }
        if (beyond == polygon.Count) return false;
        // The near plane, in clip space.
        var front = new List<Vector4>(clip.Count + 2);
        for (int i = 0; i < clip.Count; i++)
        {
            var a = clip[i];
            var b = clip[(i + 1) % clip.Count];
            float da = a.W - near, db = b.W - near;
            if (da > 0f) front.Add(a);
            if ((da > 0f) != (db > 0f)) front.Add(a + (b - a) * (da / (da - db)));
        }
        if (front.Count < 3) return false;
        var flat = front.Select(v => new Vector2(v.X / v.W, v.Y / v.W)).ToList();
        // To the rectangle, one side at a time.
        flat = Side(flat, p => p.X - within.X0, (a, b, t) => Vector2.Lerp(a, b, t));
        flat = Side(flat, p => within.X1 - p.X, (a, b, t) => Vector2.Lerp(a, b, t));
        flat = Side(flat, p => p.Y - within.Y0, (a, b, t) => Vector2.Lerp(a, b, t));
        flat = Side(flat, p => within.Y1 - p.Y, (a, b, t) => Vector2.Lerp(a, b, t));
        if (flat.Count < 3) return false;
        float x0 = flat.Min(p => p.X), x1 = flat.Max(p => p.X), y0 = flat.Min(p => p.Y), y1 = flat.Max(p => p.Y);
        // Held to the rectangle: the clip's interpolation can land a hair outside it.
        rect = new ScreenRect(x0, y0, x1, y1).Intersect(within);
        return !rect.IsEmpty;
    }

    static List<Vector2> Side(List<Vector2> poly, Func<Vector2, float> inside, Func<Vector2, Vector2, float, Vector2> lerp)
    {
        if (poly.Count == 0) return poly;
        var o = new List<Vector2>(poly.Count + 2);
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            float da = inside(a), db = inside(b);
            if (da >= 0f) o.Add(a);
            if ((da >= 0f) != (db >= 0f)) o.Add(lerp(a, b, da / (da - db)));
        }
        return o;
    }
}
