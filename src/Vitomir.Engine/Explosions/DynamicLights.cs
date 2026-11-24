// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// One place's dynamic lights: which things carry one, where each is, how bright it is this frame,
// and which walls it lays its light blob on (the light pass; the light's making, freeing
// and placing; the ramps).
//
// **The public API**, for the explosions, the natives and the renderer:
//
//   var lights = location.Lights;              // made on first use, one per place
//   lights.Of(thing)                           // the light a thing has, if any
//   lights.Allocate(thing, blob: true)         // AllocateDynamicLight
//   lights.Clear(thing)                        // ClearDynamicLight
//   lights.Blobs                               // this frame's light blobs, for the renderer
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Explosions;

/// <summary>
/// One light blob: a level surface a dynamic light reaches this frame, with the foot of the
/// perpendicular from the light to the surface's plane and how far the light is in front of it —
/// the entry the light pass links onto the surface's group for `rlDirect.dll` to draw.
/// </summary>
/// <param name="Light">The light, as it is this frame.</param>
/// <param name="Sector">The surface's sector.</param>
/// <param name="Surface">The surface, in its sector.</param>
/// <param name="Foot">Where the perpendicular from the light meets the plane.</param>
/// <param name="Distance">How far in front of the plane the light is (from −0.1 up).</param>
public readonly record struct LightBlob(DynamicLight Light, int Sector, int Surface, Vector3 Foot, float Distance);

/// <summary>
/// The dynamic lights of a place. The engine keeps each on its thing; here they are
/// kept beside the things, as the particles are, and a thing is looked at the first time the
/// system sees it: the scene's own things when the system is made, anything made later at the next
/// step — or at once, when <see cref="Notice(WorldThing)"/> is called as it enters.
/// </summary>
public sealed class DynamicLights
{
    readonly Location? location;
    readonly Dictionary<WorldThing, DynamicLight> byThing = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<WorldThing> seen = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The value ramps — one table for the place. **Ours**: the engine has one for the
    /// whole game; a place is all that is ever running.
    /// </summary>
    public ValueRamps Ramps { get; } = new();

    /// <summary>
    /// The blob pass runs for a light only above this intensity (50.0),
    /// and gathers the walls within its intensity **less this** (50.0).
    /// </summary>
    public const float BlobFloor = 50f;
    /// <summary>A surface counts while the light is no more than this far behind its plane.</summary>
    public const float BehindTolerance = -0.1f;
    /// <summary>At most this many blobs a frame (<c>0x7ff &lt; count</c> ends the pass).</summary>
    public const int MaxBlobs = 0x800;

    /// <summary>
    /// The console's lightblob switch (the pass runs only when the raster options' bit 4 is clear).
    /// Off by default, as in the shipped `masquerade.ini`.
    /// </summary>
    public bool BlobsOff { get; set; }

    /// <summary>This frame's blobs, after the last <see cref="Step"/>.</summary>
    public List<LightBlob> Blobs { get; } = [];

    /// <summary>
    /// The renderer's portal walk for this frame, or null (then every light is counted as seen). Set
    /// by the reflection pass as it walks, as the particles' is (the light pass runs in the world draw,
    /// after the walk).
    /// </summary>
    public PortalView? View { get; set; }

    /// <summary>
    /// Whether a blob on a surface of <paramref name="sector"/> shows in the main view: the group is
    /// drawn only for a sector the walk reached (over the visible list; the blobs are part of
    /// the group's draw). True with no walk.
    /// </summary>
    public bool SurfaceSeen(int sector) => View is not { } v || v.Sees(sector);

    /// <summary>
    /// The lit lights the light pass looks at, in its order: the walk's sectors in turn, and in each the
    /// things whose sector it is; with no walk, every one.
    /// </summary>
    IEnumerable<(WorldThing Thing, DynamicLight Light)> Seen()
    {
        if (View is not { } view)
        {
            foreach (var (thing, light) in byThing)
                if (thing.InWorld && (thing.Flags & WorldThing.Flag.DynaLight) != 0) yield return (thing, light);
            yield break;
        }
        var bySector = new Dictionary<int, List<(WorldThing, DynamicLight)>>();
        foreach (var (thing, light) in byThing)
        {
            if (!thing.InWorld || (thing.Flags & WorldThing.Flag.DynaLight) == 0) continue;
            int si = (int)thing.Sector;
            if (!view.Sees(si)) continue;
            if (!bySector.TryGetValue(si, out var list)) bySector[si] = list = [];
            list.Add((thing, light));
        }
        foreach (var (sector, _) in view.Visible)
            if (bySector.TryGetValue(sector, out var list))
                foreach (var entry in list) yield return entry;
    }

    /// <summary>Every light, by thing.</summary>
    public IReadOnlyDictionary<WorldThing, DynamicLight> All => byThing;

    /// <summary>A system for a place: every thing already there is looked at.</summary>
    public DynamicLights(Location location)
    {
        this.location = location;
        Notice();
    }

    /// <summary>A system with no place — what the ramp and blob tests use.</summary>
    public DynamicLights() { }

    /// <summary>The light a thing has been given so far, or null — <see cref="Of"/> without making a template's (a look at a thing's light that changes nothing; ours).</summary>
    public DynamicLight? Peek(WorldThing thing) => byThing.GetValueOrDefault(thing);

    /// <summary>The light a thing has, or null.</summary>
    public DynamicLight? Of(WorldThing thing)
    {
        if (location is not null && seen.Add(thing)) FromTemplate(thing);
        return byThing.GetValueOrDefault(thing);
    }

    /// <summary>
    /// A thing that already has a light keeps it and the answer is false; otherwise a new
    /// one (see <see cref="DynamicLight.Allocate"/>) and the thing's `THING_FLAG_DYNALIGHT` (0x80)
    /// set — a light is on the moment it is made.
    /// </summary>
    public bool Allocate(WorldThing thing, bool blob)
    {
        if (Of(thing) is not null) return false;
        byThing[thing] = DynamicLight.Allocate(blob);
        thing.Flags |= WorldThing.Flag.DynaLight;
        return true;
    }

    /// <summary>The light goes (its material released) and `THING_FLAG_DYNALIGHT` is cleared.</summary>
    public bool Clear(WorldThing thing)
    {
        if (Of(thing) is null) return false;
        byThing.Remove(thing);
        thing.Flags &= ~WorldThing.Flag.DynaLight;
        return true;
    }

    /// <summary>
    /// A thing entering the world: its template's `DYNA_*` tags are applied now rather than at the
    /// next step. The explosions call this for a new explosion, whose script's first event may be
    /// on the next frame.
    /// </summary>
    public void Notice(WorldThing thing)
    {
        if (seen.Add(thing)) FromTemplate(thing);
    }

    void Notice()
    {
        if (location is null) return;
        foreach (var thing in location.Things)
            if (seen.Add(thing)) FromTemplate(thing);
        List<WorldThing>? gone = null;
        foreach (var thing in byThing.Keys)
            if (thing.Removed) (gone ??= []).Add(thing);
        if (gone is not null)
            foreach (var thing in gone) { byThing.Remove(thing); Ramps.Forget(thing); }
    }

    void FromTemplate(WorldThing thing)
    {
        if (location?.Templates is not { } templates) return;
        Apply(thing, templates.Resolve(thing.Scene));
    }

    /// <summary>
    /// The template's light (cases 0x79–0x7f and 0x87). **Ours: the order.** The engine
    /// applies a template's tags in the order the file lists them, which in every shipped template
    /// is the editor's (intensity, material, colour, bone, offset, flags, style, radius); this
    /// applies them in that order from the resolved set. It matters for one pair — the radius
    /// divides by the intensity — and no shipped template puts them the other way round.
    /// </summary>
    public void Apply(WorldThing thing, IReadOnlyDictionary<uint, NodTag> tags)
    {
        if (!tags.TryGetValue(DynamicLight.TagIntensity, out var intensityTag)) return;
        // 0x79: allocate (no blob material), then intensity, current and radius all the tag's, and a falloff of 1.
        if (!byThing.TryGetValue(thing, out var light))
        {
            light = DynamicLight.Allocate(blob: false);
            byThing[thing] = light;
            thing.Flags |= WorldThing.Flag.DynaLight;
        }
        float v = intensityTag.AsFloat();
        light.Intensity = light.Current = light.Radius = v;
        light.Falloff = 1f;
        if (tags.TryGetValue(DynamicLight.TagMaterial, out var material)) light.Material = material.AsString();   // 0x7a
        if (tags.TryGetValue(DynamicLight.TagColor, out var color)) light.Color = color.AsVector3();              // 0x7b
        // 0x7c: a bone the thing's model has; a negative one, or no model, leaves −1.
        if (tags.TryGetValue(DynamicLight.TagBone, out var bone) && bone.AsInt() is var b and >= 0
            && thing.Animation is { } anim && b < anim.BoneCount)
            light.Bone = b;
        // 0x87: only a thing with a model takes an offset.
        if (tags.TryGetValue(DynamicLight.TagOffset, out var offset) && (thing.Model.Length > 0 || thing.Scene.Model is not null))
            light.Offset = offset.AsVector3();
        if (tags.TryGetValue(DynamicLight.TagFlags, out var flags)) light.Flags = flags.AsUInt();                  // 0x7d
        if (tags.TryGetValue(DynamicLight.TagStyle, out var style)) light.Style = style.AsInt();                  // 0x7e
        // 0x7f: over 5 it is the range and the falloff is intensity / range; otherwise the range is the intensity.
        if (tags.TryGetValue(DynamicLight.TagRadius, out var radius))
        {
            float r = radius.AsFloat();
            if (r > DynamicLight.SmallestRadius) { light.Radius = r; light.Falloff = light.Intensity / r; }
            else light.Radius = light.Intensity;
        }
    }

    /// <summary>
    /// One frame, in the engine's order: new things looked at and gone ones let go; each lit
    /// light put where its thing is (in every thing's tick); the ramps stepped
    /// (after the things); then the dynamic-light pass of the world draw — this frame's
    /// intensity for every light that is on, and the blobs of those above <see cref="BlobFloor"/>.
    ///
    /// The blobs are gathered again by the renderer after its portal walk (<see cref="View"/>,
    /// <see cref="GatherBlobs"/>); the gather here is for a simulation with no renderer.
    /// </summary>
    public void Step(int now, int frameMs, LightStyles? styles)
    {
        PlaceAll();
        StepRamps(now, frameMs);
        Light(styles);
    }

    /// <summary>
    /// The things' part of <see cref="Step"/>: new things looked at and gone ones let go, and each
    /// lit light put where its thing is (in every thing's tick).
    /// </summary>
    public void PlaceAll()
    {
        Notice();
        foreach (var (thing, light) in byThing)
            if (thing.InWorld && (thing.Flags & WorldThing.Flag.DynaLight) != 0) Place(thing, light);
    }

    /// <summary>The ramps, once a frame after the things and the camera.</summary>
    public void StepRamps(int now, int frameMs) => Ramps.Step(now, frameMs);

    /// <summary>
    /// The world draw's dynamic-light pass: this frame's intensity for every light that
    /// is on — its style's value, stepped at the top of the same pass — and the blobs.
    /// </summary>
    public void Light(LightStyles? styles)
    {
        foreach (var (thing, light) in byThing)
            if (thing.InWorld && (thing.Flags & WorldThing.Flag.DynaLight) != 0)
                light.Intensity = light.Style == 0 ? light.Current : (styles?.Value(light.Style) ?? 1f) * light.Current;
        GatherBlobs();
    }

    /// <summary>
    /// the placing. With a bone: the offset turned by the bone (its rotation, not its
    /// translation) and added to the thing's position. Without: **the thing's position alone** —
    /// the engine turns the offset by the thing's matrix and then throws the answer away, so
    /// `DYNA_OFFSET` does nothing on a thing with no bone.
    /// </summary>
    public static void Place(WorldThing thing, DynamicLight light)
    {
        if (light.Bone != -1 && thing.Animation is { } anim && light.Bone < anim.BoneCount)
        {
            // Ours in part: the engine reads the model's bone matrix at model+300 (+100 + bone ×
            // 0x110), in its own space; the posed bone is the nearest thing here.
            var m = anim.BoneMatrix(light.Bone);
            light.Position = thing.Position + Vector3.TransformNormal(light.Offset, m);
            return;
        }
        light.Position = thing.Position;
    }

    /// <summary>
    /// The blob half of the light pass: for each lit light above <see cref="BlobFloor"/>, the level
    /// surfaces within its intensity less 50 (from the sector its position is in, through
    /// the same portal flood as everything else): a surface counts when the light is in
    /// front of its plane (by more than −0.1) and within that radius of it, and the circle where
    /// the sphere cuts the plane reaches the polygon. `Nodraw` groups are left out (the
    /// query does not carry 0x80). At most 2,048 a frame. And before any of that, only a surface
    /// whose plane the sphere's walk down its sector's collision tree marks
    /// (<see cref="World.SphereTree"/>) — so no blob lands on a Detail surface, whose plane is
    /// not in the tree.
    ///
    /// **Which lights**: the pass walks the portal walk's list (<see cref="View"/>) in
    /// its order, and in each sector the things in that sector's own list (the sector's live
    /// list) that carry a light (flag <c>0x80</c>) — so a torch in a room the walk
    /// did not reach lays no blob, even on a wall of a room that is in view, and the table's 2,048
    /// fill in the walk's order. With no walk (a simulation with no renderer, or an eye in no
    /// sector) every lit light in the place is taken, in the order they were noticed. Which blobs
    /// the view then shows is the renderer's: a surface's blobs are drawn with its group, so only
    /// on a surface of a sector the walk reached (<see cref="SurfaceSeen"/>).
    /// </summary>
    public void GatherBlobs()
    {
        Blobs.Clear();
        if (BlobsOff || location?.Level is not { } level) return;
        foreach (var (thing, light) in Seen())
        {
            if (!(light.Intensity > BlobFloor)) continue;
            float reach = light.Intensity - BlobFloor;
            IReadOnlyList<int> sectors;
            int start = location.Solid is { } solid ? solid.SectorAt(light.Position, (int)thing.Sector) : (int)thing.Sector;
            if (location.Props is { } bodies && start >= 0) sectors = bodies.SectorsNear(light.Position, reach, start);
            else sectors = Enumerable.Range(0, level.Sectors.Length).ToArray();
            if (!Gather(level, light, reach, sectors, Blobs)) return;
        }
    }

    /// <summary>The sphere query's surface test over some sectors, into <paramref name="into"/>; false once the table is full.</summary>
    public static bool Gather(NilLevel level, DynamicLight light, float reach, IEnumerable<int> sectors, List<LightBlob> into)
    {
        var c = light.Position;
        foreach (int si in sectors)
        {
            if (si < 0 || si >= level.Sectors.Length) continue;
            var sector = level.Sectors[si];
            // Only the planes the sphere's walk down the sector's tree marks (World/SphereTree.cs).
            if (World.SphereTree.Marked(sector, c, reach) is not { } marked) continue;
            for (int fi = 0; fi < sector.Surfaces.Length; fi++)
            {
                var surface = sector.Surfaces[fi];
                if (surface.NumVerts < 3 || !marked.Contains(surface.Plane)) continue;
                if ((sector.Groups[surface.Group].SurfaceFlags & NilGroup.FlagNodraw) != 0) continue;
                var plane = sector.SurfacePlanes[surface.Plane];
                float d = Vector3.Dot(plane.Normal, c) - plane.Dist;
                if (d > reach || d < BehindTolerance) continue;
                var foot = c - d * plane.Normal;
                float circle = MathF.Sqrt(reach * reach - d * d);
                if (!Touches(sector, surface, plane, foot, circle)) continue;
                if (into.Count >= MaxBlobs) return false;
                into.Add(new LightBlob(light, si, fi, foot, d));
            }
        }
        return true;
    }

    /// <summary>
    /// Whether a circle in a surface's plane reaches the polygon: the polygon is
    /// flattened onto the plane's two dominant axes and the circle's centre is inside it or within
    /// the radius of one of its edges. **Ours in method**: 1.1 walks the edges on those axes
    /// too, but its edge arithmetic is not read line by line.
    /// </summary>
    public static bool Touches(NilSector sector, NilSurface surface, NilPlane plane, Vector3 centre, float radius)
    {
        if (Pick.Inside(sector, surface, plane, centre)) return true;
        var n = Vector3.Abs(plane.Normal);
        int drop = n.X >= n.Y && n.X >= n.Z ? 0 : n.Y >= n.Z ? 1 : 2;
        static Vector2 Flat(Vector3 p, int drop) => drop switch { 0 => new(p.Y, p.Z), 1 => new(p.X, p.Z), _ => new(p.X, p.Y) };
        var q = Flat(centre, drop);
        float r2 = radius * radius;
        for (int i = 0; i < surface.NumVerts; i++)
        {
            var a = Flat(sector.Vertices[sector.SectorVertices[surface.FirstVertex + i].Vertex], drop);
            var b = Flat(sector.Vertices[sector.SectorVertices[surface.FirstVertex + (i + 1) % surface.NumVerts].Vertex], drop);
            var ab = b - a;
            float t = ab.LengthSquared() > 0f ? Math.Clamp(Vector2.Dot(q - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
            if (Vector2.DistanceSquared(q, a + t * ab) <= r2) return true;
        }
        return false;
    }

    // ---- what rlDirect.dll does with a blob -----------------------------------------

    /// <summary>The blob texture spans the circle: u and v are <c>(offset in the plane + r) × 0.5 / r</c>.</summary>
    public const float BlobHalf = 0.5f;
    /// <summary>The blob's brightness per unit of intensity left at the wall: 1 / 392.</summary>
    public const float BlobScale = 0.0025510204f;

    /// <summary>
    /// The radius of the circle the blob is drawn over: where a sphere of the light's **full**
    /// intensity (not less 50) cuts the plane, <c>sqrt(I² − d²)</c>.
    /// </summary>
    public static float BlobRadius(LightBlob blob) =>
        MathF.Sqrt(blob.Light.Intensity * blob.Light.Intensity - blob.Distance * blob.Distance);

    /// <summary>
    /// The blob's vertex colour: each channel <c>colour × (I − d) / 392</c>, turned into
    /// a byte by the float trick the renderer uses everywhere (adding 1.5 × 2²³ and taking the low
    /// bits) and packed <b>without a clamp</b> — red keeps its low eight bits, green and blue are
    /// ORed in shifted, so a channel past 255 spills into the one above it. A 600-intensity flash
    /// at a wall a few units away is past 255 and comes out in the wrong colour; that is 1.1's.
    /// Answered as <c>0xAARRGGBB</c>, alpha 255.
    /// </summary>
    public static uint BlobColor(LightBlob blob)
    {
        float k = (blob.Light.Intensity - blob.Distance) * BlobScale;
        var c = blob.Light.Color;
        uint r = Round(k * c.X), g = Round(k * c.Y), b = Round(k * c.Z);
        return 0xff000000u | ((r & 0xff) << 16) | (g << 8) | b;
    }

    /// <summary>The renderer's float-to-integer: the nearest whole number, halves to even (as 1.1 rounds), kept to 22 bits.</summary>
    static uint Round(float x) => (uint)(long)Math.Round((double)x, MidpointRounding.ToEven) & 0x3fffff;

    /// <summary>
    /// A blob vertex's texture coordinates: the vertex's position in the plane's own
    /// frame, less the foot's, plus the circle's radius, times 0.5 / radius — so the blob's texture
    /// spans the circle and its centre sits on the foot. The plane's frame is the one the renderer
    /// builds looking along the reversed normal; <b>ours</b>: which of its in-plane axes is u and
    /// which v is not read, and the blob texture is round, so it does not show.
    /// </summary>
    public static Vector2 BlobUv(LightBlob blob, Vector3 normal, Vector3 vertex)
    {
        float r = BlobRadius(blob);
        if (!(r > 0f)) return new Vector2(0.5f, 0.5f);
        var (u, v) = PlaneAxes(normal);
        var rel = vertex - blob.Foot;
        float s = BlobHalf / r;
        return new Vector2((Vector3.Dot(rel, u) + r) * s, (Vector3.Dot(rel, v) + r) * s);
    }

    /// <summary>Two unit axes in a plane, at right angles to each other and to its normal.</summary>
    public static (Vector3 U, Vector3 V) PlaneAxes(Vector3 normal)
    {
        var n = Vector3.Normalize(-normal);
        var up = MathF.Abs(n.Z) < 0.99f ? Vector3.UnitZ : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(up, n));
        var v = Vector3.Cross(n, u);
        return (u, v);
    }
}
