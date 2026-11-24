// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// One place's decals: the wall marks explosions leave and the impact marks projectiles leave, as
// the original makes them, fades them (the light pass's second loop) and clears them,
// and as `rlDirect.dll` draws them (the surface-group draw's decal walk).
// Read.
//
// **The public API**, for the explosions, the projectiles and the renderer:
//
//   var decals = location.Decals;              // made on first use, one per place
//   decals.Stamp(sector, axis, at, angle, size, ms, material, now)   // lays a mark
//   decals.Step(seconds)                       // the fade, once a frame
//   decals.Live                                // what the renderer draws
using System.Numerics;
using Vitomir.Engine.Particles;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Explosions;

/// <summary>
/// One decal slot (0x534 bytes in the original's table): a piece of a mark clipped to one level
/// surface, lying 0.2 in front of it.
/// </summary>
public sealed class Decal
{
    /// <summary>The slot is taken.</summary>
    public bool InUse { get; internal set; }
    /// <summary>
    /// Whether it hangs on its surface's list (the surface, and the
    /// links onto its group's decal list), which is the only way the renderer finds it.
    /// A slot taken back while full is unlinked and may stay in use unlinked (see
    /// <see cref="Decals.Stamp"/>).
    /// </summary>
    public bool Linked { get; internal set; }
    /// <summary>The surface's sector.</summary>
    public int Sector { get; internal set; }
    /// <summary>The surface, in its sector.</summary>
    public int Surface { get; internal set; }
    /// <summary>The material (its use count raised).</summary>
    public string Material { get; internal set; } = "";
    /// <summary>The clipped polygon in the world (at most 64 points).</summary>
    public Vector3[] Points { get; internal set; } = [];
    /// <summary>Each point's texture coordinates.</summary>
    public Vector2[] Uvs { get; internal set; } = [];
    /// <summary>When it was made, on the engine's clock. Nothing reads it.</summary>
    public int Born { get; internal set; }
    /// <summary>How long it lives, in milliseconds.</summary>
    public int LifeMs { get; internal set; }
    /// <summary>What is left of its life, 1 down to 0.</summary>
    public float Fade { get; internal set; } = 1f;
    /// <summary>
    /// Its colour, red, green, blue and alpha as 0–255 floats — every
    /// point of the polygon is given it when it is drawn. 255 each when made; the fade lowers red,
    /// green and blue together over the last two seconds and never alpha.
    /// </summary>
    public Vector4 Color { get; internal set; } = new(255f);
}

/// <summary>
/// The decals of a place. The engine keeps one table for the game (room for 501) of which
/// the detail preset's <c>maxDecals</c> slots are used (set
/// by the renderer's detail preset); here a place has its own, which comes to the same thing — a level load clears
/// the engine's — of the maxed-out preset's 500 (<see cref="Rendered"/>).
/// </summary>
public sealed class Decals
{
    readonly Location? location;
    Decal[] slots;

    /// <summary>
    /// A mark's square is half its size either side of its centre (the size × 0.5),
    /// and the surfaces it may land on are those within its whole size.
    /// </summary>
    public const float Half = 0.5f;
    /// <summary>How far in front of its surface a decal is laid (0.2).</summary>
    public const float Lift = 0.2f;
    /// <summary>The most points a clipped decal has (the clip stops a pass at 64; the slot holds 64).</summary>
    public const int MaxPoints = 64;
    /// <summary>The fade's last stretch: over its final 2,000 ms a decal darkens to nothing.</summary>
    public const float FadeMs = 2000f;
    /// <summary>The whole-preset default (normal and high): 500 decals.</summary>
    public const int DefaultMax = 500;
    /// <summary>
    /// A projectile's impact mark lasts 20,000 ms  at twice its radius and a
    /// random turn out of 360° (<c>rand() × 1/32767 × 360</c>).
    /// </summary>
    public const int ImpactMs = 20000;

    /// <summary>
    /// How many slots are used: the detail preset's <c>maxDecals</c> — 0 at very
    /// low, 250 low, 500 normal and high. Nought turns decals off: the stamp makes none, and the
    /// renderer's constructor sets <c>detailFlags</c> 0x80, which stops the fade.
    /// </summary>
    public int Max => slots.Length;

    /// <summary>How many are in use — a counter only; see <see cref="Stamp"/>.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// The engine's <c>rand()</c> as the impact mark's turn draws it. **Ours: a stream of its own**, as
    /// the particles and the explosions have, so a bullet's mark never moves a fight's rolls in a test.
    /// </summary>
    public Ai.Rng Rand { get; } = Ai.Chance.Game?.Decals ?? new(1);   // a game's: the launch's, carried on

    /// <summary>Every slot, in table order.</summary>
    public IReadOnlyList<Decal> Slots => slots;

    /// <summary>The decals the renderer finds: those in use and on their surface's list.</summary>
    public IEnumerable<Decal> Live => slots.Where(d => d.InUse && d.Linked);

    /// <summary>A place's decals, as many slots as the maxed-out preset has (500) unless a test says otherwise.</summary>
    public Decals(Location location, int max = -1)
    {
        this.location = location;
        slots = Make(max >= 0 ? max : Rendered);
    }

    /// <summary>Decals with no place, for the tests that hand a level.</summary>
    public Decals(int max = DefaultMax) => slots = Make(max);

    static Decal[] Make(int max) => Enumerable.Range(0, Math.Max(0, max)).Select(_ => new Decal()).ToArray();

    /// <summary>
    /// <c>maxDecals</c> as Vitomir draws: <see cref="Ui.DetailPresets.Maxed"/>'s 500, whatever the
    /// detail level, <c>[renderCustom]</c> or the Advanced screen's decals bar say
    /// (the screens are inert in Vitomir). In 1.1 the level's preset decides — for a touched Custom
    /// the bar's stop × 50. The table is presentation only: no thing, not saved,
    /// the impact mark's turn drawn before the count is looked at  and the
    /// stamp's answer thrown away by both callers.
    /// </summary>
    public static int Rendered => Math.Clamp(Ui.DetailPresets.Maxed.MaxDecals, 0, 501);

    /// <summary>
    /// The clear: every slot in use is unlinked and freed.
    /// </summary>
    public void Clear()
    {
        foreach (var d in slots)
            if (d.InUse) { d.InUse = false; d.Linked = false; Count--; }
    }

    /// <summary>
    /// The fade (the light pass's second loop, once a frame, while <c>detailFlags</c> 0x80 is clear):
    /// each decal in use loses <c>seconds / (life × 0.001)</c> of its life; while any is
    /// left, and less than 2,000 ms of it (<c>life × fade &lt; 2000</c>), its red, green and blue are
    /// <c>life × fade × 0.0005 × 255</c> — so it darkens to black over its last
    /// two seconds; once none is left it is unlinked and freed. A decal made with no life
    /// divides by nought and goes on the first frame.
    /// </summary>
    public void Step(float seconds)
    {
        foreach (var d in slots)
        {
            if (!d.InUse) continue;
            float fade = d.Fade - seconds / ((float)(uint)d.LifeMs * 0.001f);
            d.Fade = fade;
            if (fade > 0f)
            {
                float left = (float)(uint)d.LifeMs * fade;
                if (left < FadeMs)
                {
                    float v = left * 0.0005f * 255f;
                    d.Color = new Vector4(v, v, v, d.Color.W);
                }
            }
            else
            {
                d.InUse = false;
                d.Linked = false;
                Count--;
            }
        }
    }

    /// <summary>
    /// A WALLMARK event: with the template's `WALLMARK` material, a size
    /// and a time, and a sector, a mark of that size for that long along the explosion's own Z axis
    /// from its position, unturned.
    /// </summary>
    public bool WallMark(WorldThing blast, string material, float size, int ms, int now)
    {
        if (material.Length == 0 || size == 0f || ms == 0 || blast.Sector < 0) return false;
        var axis = new Vector3(blast.Rotation.M31, blast.Rotation.M32, blast.Rotation.M33);
        return Stamp((int)blast.Sector, axis, blast.Position, 0f, size, ms, material, now);
    }

    /// <summary>
    /// A projectile's impact mark (before either branch of <c>OnSurfaceContact</c>): its
    /// `WALLMARK` material (tag 244 on a type-4 thing), along the struck surface's
    /// plane normal, at the contact, turned by <c>rand() × 1/32767 × 360</c>° and twice its radius
    /// across (the radius when it has a shape, else 0) for 20,000 ms.
    /// </summary>
    public bool Impact(WorldThing shot, int sector, Vector3 normal, Vector3 at, int now)
    {
        string material = ImpactMaterial(shot);
        if (material.Length == 0) return false;
        float radius = shot.Collide != CollideShape.None ? shot.Radius : 0f;
        float angle = Rand.Next() * Ai.Rng.Scale * 360f;
        return Stamp(sector, normal, at, angle, radius + radius, ImpactMs, material, now);
    }

    readonly Dictionary<string, string> impactMaterials = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A projectile's impact material: its template's `WALLMARK` (244), which the
    /// template reader gives a type-4 thing (tag 0xf4; a type-10 thing's goes to
    /// another field). Looked up from the template here, once a template, rather than kept on every
    /// thing — **ours**. Every bullet names `BulletMark1`, the flame thrower `ExplosionMark1`, the
    /// severed heads `DCLBloodSpatter3` and the flaming gib `bulletMark1`.
    /// </summary>
    public string ImpactMaterial(WorldThing shot)
    {
        if (shot.Type != ThingType.Projectile || location?.Templates is not { } templates) return "";
        string key = shot.Scene.Template ?? "";
        if (impactMaterials.TryGetValue(key, out var have)) return have;
        string name = templates.Resolve(shot.Scene).TryGetValue(ExplosionSystem.TagWallMark, out var tag) ? tag.AsString() : "";
        impactMaterials[key] = name;
        return name;
    }

    /// <summary>
    /// Stamps a mark on the level round <paramref name="at"/>. Answers false when decals are
    /// off (no slots) or no surface is near, true otherwise — even when every piece was clipped away.
    /// <list type="number">
    /// <item>The surfaces within <paramref name="size"/> of the point, from its sector through the
    /// portal flood — the sphere query the light blobs use (<see cref="DynamicLights.Gather"/>'s
    /// test), taken nearest first;</item>
    /// <item>the mark is a square, half the size either side, in the X/Z plane of a frame whose Y is
    /// <paramref name="axis"/>, turned about that Y by <paramref name="angle"/> degrees
    /// (Euler <c>(0, angle, 0)</c>), its corners (h, −h), (−h, −h), (−h, h), (h, h) at texture
    /// coordinates (0,0), (1,0), (1,1), (0,1);</item>
    /// <item>for each surface: its normal taken into that frame (turned as a point — the frame has
    /// no translation, so it is the same) and made the Y of a second frame (the same frame builder again),
    /// composed with the first — a frame whose Y is the surface's normal; the square laid
    /// in that frame's X/Z plane at the foot of the perpendicular from the point to the plane, moved
    /// by how far the point is from the foot across the <i>first</i> frame's X and Z; clipped to the
    /// surface's polygon on X and Z; every point put 0.2 in front of the plane
    /// (<see cref="Lift"/>) and taken back into the world;</item>
    /// <item>kept in the first free slot, or — with every slot taken — <b>in slot 0</b>, whatever it
    /// holds and however new (the old one is unlinked first). A piece clipped to fewer than
    /// three points takes nothing — but slot 0, taken back when full, then stays in use unlinked,
    /// drawn by nobody, until its own time runs out; and slot 0 re-used is counted again.</item>
    /// </list>
    /// </summary>
    public bool Stamp(int sector, Vector3 axis, Vector3 at, float angle, float size, int ms, string material, int now)
    {
        if (slots.Length == 0 || location?.Level is not { } level) return false;
        // The material is loaded when the template is read, long before a mark is made;
        // asking for it here puts it in the cache before the renderer's next catch-up.
        location.Materials.Named(material);
        IReadOnlyList<int> sectors;
        int start = location.Solid is { } solid ? solid.SectorAt(at, sector) : sector;
        if (location.Props is { } bodies && start >= 0) sectors = bodies.SectorsNear(at, size, start);
        else sectors = Enumerable.Range(0, level.Sectors.Length).ToArray();
        return Stamp(level, sectors, axis, at, angle, size, ms, material, now);
    }

    /// <summary><see cref="Stamp(int, Vector3, Vector3, float, float, int, string, int)"/> over some sectors of a level.</summary>
    public bool Stamp(NilLevel level, IEnumerable<int> sectors, Vector3 axis, Vector3 at, float angle, float size, int ms,
                      string material, int now)
    {
        if (slots.Length == 0) return false;
        var near = Near(level, sectors, at, size);
        if (near.Count == 0) return false;

        // The square, turned about its own Y.
        float h = size * Half;
        var turn = RingBase.Turn(new Vector3(0f, angle, 0f), Vector3.Zero);
        Span<Vector3> corners = stackalloc Vector3[4];
        corners[0] = Vector3.Transform(new Vector3(h, 0f, -h), turn);
        corners[1] = Vector3.Transform(new Vector3(-h, 0f, -h), turn);
        corners[2] = Vector3.Transform(new Vector3(-h, 0f, h), turn);
        corners[3] = Vector3.Transform(new Vector3(h, 0f, h), turn);
        // The mark's frame: Y along the axis. Its inverse is its transpose (no translation).
        var frame = BeamEmitter.Basis(axis);
        var inverse = Matrix4x4.Transpose(frame);
        var centre = Vector3.Transform(at, inverse);

        foreach (var (si, fi, foot) in near)
        {
            var s = level.Sectors[si];
            var surface = s.Surfaces[fi];
            var normal = s.SurfacePlanes[surface.Plane].Normal;
            // The surface's frame (built on the normal in the mark's frame) and its inverse.
            var local = Vector3.Transform(normal, inverse);
            var onto = BeamEmitter.Basis(local) * frame;
            var back = Rigid(onto);
            var footMark = Vector3.Transform(foot, inverse);
            float dx = centre.X - footMark.X, dz = centre.Z - footMark.Z;

            var slot = Free();
            var f = Vector3.Transform(foot, back);
            var pts = new Vector3[MaxPoints + 1];
            var uvs = new Vector2[MaxPoints + 1];
            for (int i = 0; i < 4; i++)
                pts[i] = new Vector3(f.X + dx + corners[i].X, f.Y + corners[i].Y, f.Z + dz + corners[i].Z);
            uvs[0] = new Vector2(0f, 0f); uvs[1] = new Vector2(1f, 0f); uvs[2] = new Vector2(1f, 1f); uvs[3] = new Vector2(0f, 1f);
            var poly = new Vector3[surface.NumVerts];
            for (int i = 0; i < poly.Length; i++)
                poly[i] = Vector3.Transform(s.Vertices[s.SectorVertices[surface.FirstVertex + i].Vertex], back);
            int n = Clip(pts, uvs, 4, poly);
            if (n < 3) continue;
            float y = poly[0].Y + Lift;
            var world = new Vector3[n];
            for (int i = 0; i < n; i++) world[i] = Vector3.Transform(new Vector3(pts[i].X, y, pts[i].Z), onto);
            slot.Points = world;
            slot.Uvs = uvs[..n];
            slot.Sector = si;
            slot.Surface = fi;
            slot.LifeMs = ms;
            slot.Material = material;
            slot.Fade = 1f;
            slot.Color = new Vector4(255f);
            slot.Born = now;
            slot.InUse = true;
            slot.Linked = true;
            Count++;
        }
        return true;
    }

    /// <summary>
    /// The slot a piece goes in: the first not in use, else slot 0, unlinked from its
    /// surface but left in use.
    /// </summary>
    Decal Free()
    {
        foreach (var d in slots)
            if (!d.InUse) return d;
        slots[0].Linked = false;
        return slots[0];
    }

    /// <summary>
    /// The surfaces a mark may land on, nearest first, with the foot of the perpendicular from the
    /// point: the test the sphere query applies (<see cref="DynamicLights"/>'s blob gather) — the point in
    /// front of the plane by more than −0.1 and within <paramref name="reach"/> of it, the circle the
    /// sphere cuts from the plane reaching the polygon, `Nodraw` groups left out, and only a surface
    /// whose plane the sphere's walk down its sector's tree marks (<see cref="SphereTree"/>,
    /// No scorch on a Detail surface) — then walked by
    /// distance (the least first, the earlier on a tie). At most 256 (the query's table).
    /// </summary>
    public static List<(int Sector, int Surface, Vector3 Foot)> Near(NilLevel level, IEnumerable<int> sectors, Vector3 at, float reach)
    {
        var found = new List<(int Sector, int Surface, Vector3 Foot, float Distance)>();
        foreach (int si in sectors)
        {
            if (si < 0 || si >= level.Sectors.Length) continue;
            var sector = level.Sectors[si];
            // Only the planes the sphere's walk down the sector's tree marks (World/SphereTree.cs).
            if (World.SphereTree.Marked(sector, at, reach) is not { } marked) continue;
            for (int fi = 0; fi < sector.Surfaces.Length; fi++)
            {
                var surface = sector.Surfaces[fi];
                if (surface.NumVerts < 3 || !marked.Contains(surface.Plane)) continue;
                if ((sector.Groups[surface.Group].SurfaceFlags & NilGroup.FlagNodraw) != 0) continue;
                var plane = sector.SurfacePlanes[surface.Plane];
                float d = Vector3.Dot(plane.Normal, at) - plane.Dist;
                if (d > reach || d < DynamicLights.BehindTolerance) continue;
                var foot = at - d * plane.Normal;
                float circle = MathF.Sqrt(MathF.Max(0f, reach * reach - d * d));
                if (!DynamicLights.Touches(sector, surface, plane, foot, circle)) continue;
                if (found.Count >= MaxQuery) break;
                found.Add((si, fi, foot, d));
            }
        }
        // Stable: equal distances keep the query's order, as the original's strict "less than" does.
        return found.Select((f, i) => (f, i)).OrderBy(x => x.f.Distance).ThenBy(x => x.i)
                    .Select(x => (x.f.Sector, x.f.Surface, x.f.Foot)).ToList();
    }

    /// <summary>The sphere query's table: 256 entries (0x30 to 0x1830 in steps of 0x18).</summary>
    public const int MaxQuery = 256;

    /// <summary>A rigid frame's inverse: the rotation transposed and the translation taken back through it.</summary>
    public static Matrix4x4 Rigid(Matrix4x4 m)
    {
        var t = Matrix4x4.Transpose(new Matrix4x4(m.M11, m.M12, m.M13, 0f, m.M21, m.M22, m.M23, 0f, m.M31, m.M32, m.M33, 0f, 0f, 0f, 0f, 1f));
        var p = m.Translation;
        t.M41 = -(m.M11 * p.X + m.M12 * p.Y + m.M13 * p.Z);
        t.M42 = -(m.M21 * p.X + m.M22 * p.Y + m.M23 * p.Z);
        t.M43 = -(m.M31 * p.X + m.M32 * p.Y + m.M33 * p.Z);
        return t;
    }

    /// <summary>
    /// The mark's polygon (points and texture coordinates, <paramref name="n"/> of them)
    /// clipped to a surface's polygon on the X/Z plane, one edge at a time from the last
    /// (<c>poly[m−2] → poly[m−1]</c>) down to the first (<c>poly[m−1] → poly[0]</c>). A point is kept
    /// when <c>(p.x − b.x)(a.z − b.z) − (p.z − b.z)(a.x − b.x) ≥ 0</c> for the edge from <c>b</c> to
    /// <c>a</c>; where the outline crosses the edge a point is put on it, its coordinates
    /// interpolated, and only its X and Z are written. A pass stops at 64 points. Answers the
    /// points left, the result back in <paramref name="pts"/>/<paramref name="uvs"/> — or 0 as soon as a
    /// pass leaves fewer than three, or none (and 0 at once for a polygon of none).
    /// </summary>
    public static int Clip(Vector3[] pts, Vector2[] uvs, int n, Vector3[] poly)
    {
        int m = poly.Length;
        var inP = pts; var inT = uvs;
        var outP = new Vector3[MaxPoints + 1]; var outT = new Vector2[MaxPoints + 1];
        for (int i = m - 1; i >= 0; i--)
        {
            var a = poly[i];
            var b = poly[(i != 0 ? i : m) - 1];
            float ez = a.Z - b.Z, ex = a.X - b.X;
            int state = 0, count = 0;
            if (n == 0) state = 2;
            else
            {
                for (int k = 0; k < n; k++)
                {
                    if (count == MaxPoints) break;
                    int next = k + 1 == n ? 0 : k + 1;
                    var p = inP[k];
                    var q = inP[next];
                    float sideQ = (q.X - b.X) * ez + (q.Z - b.Z) * -ex;
                    float sideP = (p.X - b.X) * ez + (p.Z - b.Z) * -ex;
                    bool cross;
                    if (sideP < 0f) cross = sideQ >= 0f;
                    else
                    {
                        outP[count] = new Vector3(p.X, outP[count].Y, p.Z);
                        outT[count] = inT[k];
                        count++;
                        cross = sideQ < 0f;
                    }
                    if (!cross) continue;
                    float denom = (q.Z - p.Z) * (b.X - a.X) - (q.X - p.X) * (b.Z - a.Z);
                    if (denom == 0f) continue;
                    state = 1;
                    float t = ((p.X - b.X) * (b.Z - a.Z) - (p.Z - b.Z) * (b.X - a.X)) / denom;
                    outP[count] = new Vector3((q.X - p.X) * t + p.X, outP[count].Y, (q.Z - p.Z) * t + p.Z);
                    outT[count] = (inT[next] - inT[k]) * t + inT[k];
                    count++;
                }
                if (count == 0) state = 2;
            }
            if (state == 2 || count < 3) return 0;
            if (state != 0) { (inP, outP) = (outP, inP); (inT, outT) = (outT, inT); }
            n = count;
        }
        if (!ReferenceEquals(inP, pts))
        {
            Array.Copy(inP, pts, n);
            Array.Copy(inT, uvs, n);
        }
        return n;
    }
}
