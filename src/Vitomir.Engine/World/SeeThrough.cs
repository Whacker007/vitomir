// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: what is drawn see-through for being in the camera's way, decided once a frame.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// **Seeing through what is in the way** (ours; presentation only, <see cref="CameraChoices.SeeThrough"/>). The base game
/// keeps the eye in front of a wall by pulling the camera in (<see cref="CameraSweep"/>). Two ways of seeing through
/// (<see cref="SeeThroughMode"/>):
/// <list type="bullet">
/// <item>**The window** (the players'): the camera still pulls in, and whatever is still between the eye and whom the camera
/// frames — a pillar, a beam, a roof, a crate, the corner of a wall the pull-in did not clear — is drawn clear in a soft
/// round window about each subject (<see cref="Holes"/>): clear in the middle (<see cref="WindowInner"/>), solid again at
/// its rim (<see cref="WindowOuter"/>), the part of the surface outside it solid. The pointer looks through a faded
/// surface exactly where the picture is more clear than not there (<see cref="LooksThrough"/>), by the same sum the
/// picture's shader does (<see cref="Clearness"/>).</item>
/// <item>**The x-ray** (the developer's): the camera is not pulled in, and every surface in the way is drawn nearly clear
/// whole, with a faint outline of its edges; the pointer looks through every faded surface.</item>
/// </list>
///
/// <para><b>One decision a frame, read by two.</b> <see cref="Frame"/> runs once a frame, after the camera has been aimed
/// (<see cref="WorldFrame.Frame"/>), and keeps a fade level for every level surface and every faded thing. The picture
/// draws from those levels (the snapshot carries them to the renderer) and the pointer passes over exactly what has a level
/// above nothing (<see cref="Pick.NearestShown"/>, <see cref="Pick.NearestThing(IEnumerable{WorldThing}, Vector3, Vector3, out bool, float, WorldThing?, Func{WorldThing, bool}?)"/>):
/// nothing works it out a second time.</para>
///
/// <para><b>What is in the way.</b> Lines are cast from the eye to three points on each subject (the feet, the chest and the
/// head: <see cref="Heights"/>; or, for a body its animation draws away from its own point or lying down, three points along
/// it where it is drawn, and nothing faded that holds one of them — the bed it lies on, the sheet over it: <see cref="PointsOf"/>),
/// <see cref="CastRadius"/> wide, stopping <see cref="SubjectMargin"/> short of the point; a
/// drawn level surface either side of which the line passes, through the surface grown by the radius
/// (<see cref="WorldSweep.Reaches"/>), is in the way, and so is a prop or a container whose body the line meets (a
/// character never is). Then the ceilings and roofs over a subject: a surface that is more floor than wall, lies wholly
/// above the subject's head and below the eye, within a column about the subject as wide as half the view there
/// (<see cref="ColumnLeast"/> to <see cref="ColumnMost"/>), and not beyond the subject as the eye sees it
/// (<see cref="ColumnBeyond"/>): a roof over the street behind is kept.</para>
///
/// <para><b>Who the subjects are.</b> The game camera's and a camera path's: the steered character. A cutscene's: its one or
/// two subjects. In either, the people of a conversation going on. The first-person look and the free camera have none, and
/// nothing is faded.</para>
///
/// <para>Each level eases towards 1 (in the way) or 0 over <see cref="EaseSeconds"/>, so nothing pops; a frame with no time
/// (a still) takes the levels where they are going at once.</para>
/// </summary>
public sealed class SeeThrough
{
    /// <summary>How clear a surface faded all the way is drawn: its opacity at a level of 1.</summary>
    public const float Clear = 0.1f;

    /// <summary>How long a surface takes to fade all the way, or come all the way back, in seconds.</summary>
    public const float EaseSeconds = 0.2f;

    /// <summary>How wide the lines from the eye to the subject are, in units.</summary>
    public const float CastRadius = 24f;

    /// <summary>How far short of a subject's point a line stops, so that what the subject stands on or against is kept.</summary>
    public const float SubjectMargin = 16f;

    /// <summary>The points of a subject the lines are cast to, above its feet: the feet, the chest and the head.</summary>
    public static readonly float[] Heights = [24f, 72f, 128f];

    /// <summary>The column over a subject in which ceilings and roofs are faded: at least this wide, and at most that.</summary>
    public const float ColumnLeast = 128f, ColumnMost = 384f;

    /// <summary>How far beyond a subject, seen from the eye, a ceiling may lie and still be faded: the roofs behind are kept.</summary>
    public const float ColumnBeyond = 64f;

    /// <summary>How far above the top of a subject's head a ceiling must start to count as over it.</summary>
    public const float HeadClearance = 16f;

    /// <summary>
    /// How far a subject's drawn body may lie from its own point, across, and still be framed by <see cref="Heights"/> above
    /// it; and the least height of a body drawn upright. A body carried further by its animation, or lower — a sleeper on a
    /// bed beside which the thing's point stands — is framed where it is drawn (<see cref="Body"/>, <see cref="PointsOf"/>).
    /// </summary>
    public const float BodyAside = 48f, BodyUpright = 64f;

    /// <summary>How far a thing's body is grown when asked whether a drawn body's point is within it (<see cref="Holds"/>).</summary>
    public const float HoldSlack = 8f;

    /// <summary>
    /// A subject's body as last drawn (the world box of its posed vertices, <c>ScenePoses.BodyOf</c>), or null when not known:
    /// set by the frame. Unset, every subject is framed by <see cref="Heights"/> above its point, as before.
    /// </summary>
    public Func<WorldThing, (Vector3 Min, Vector3 Max)?>? Body { get; set; }

    /// <summary>How flat a surface must be to count as a ceiling or a roof (the upright part of its normal).</summary>
    public const float Flat = 0.3f;

    /// <summary>The window's clear middle, in world units about a subject's chest, as seen from the eye.</summary>
    public const float WindowInner = 192f;

    /// <summary>The window's rim, where a faded surface is solid again, in world units about a subject's chest.</summary>
    public const float WindowOuter = 352f;

    /// <summary>How clear a faded surface must be at a point for the pointer to look through it there: more clear than not.</summary>
    public const float LookThrough = 0.5f;

    /// <summary>The most windows a frame has (the shader's own limit): the first subjects'.</summary>
    public const int MostHoles = 4;

    /// <summary>
    /// One soft window (ours, <see cref="SeeThroughMode.On"/>): the line from the eye to a subject's chest
    /// (<see cref="Axis"/>, a unit), the cosines of the angles from it at which the window is clear (<see cref="CosInner"/>)
    /// and solid again (<see cref="CosOuter"/>), and how far along the line it reaches (<see cref="Far"/>: up to the subject,
    /// so nothing beyond it is cleared).
    /// </summary>
    public readonly record struct Hole(Vector3 Axis, float CosInner, float CosOuter, float Far);

    /// <summary>One level surface as the decision tests it: where it is filed, its box, and whether it is drawn at all.</summary>
    readonly record struct Entry(int Sector, int Surface, Vector3 Min, Vector3 Max, bool Drawn);

    NilLevel? level;
    Entry[] table = [];
    int[] sectorBase = [];
    float[] levels = [];
    bool[] wanted = [];
    readonly List<int> active = [];
    readonly List<int> scratch = [];
    readonly Dictionary<WorldThing, float> things = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<WorldThing> thingsWanted = new(ReferenceEqualityComparer.Instance);
    readonly List<Vector3> points = [];
    readonly List<WorldThing> subjects = [];
    readonly List<Hole> holes = [];
    readonly List<Vector3> held = [];

    /// <summary>How the frame sees through (the camera's, <see cref="WorldCamera.SeeThrough"/>), as the last decision found it.</summary>
    public SeeThroughMode Mode { get; private set; }

    /// <summary>The eye the frame's windows are seen from.</summary>
    public Vector3 Eye { get; private set; }

    /// <summary>The frame's soft windows (<see cref="SeeThroughMode.On"/>): none under the x-ray, whose fade is whole.</summary>
    public IReadOnlyList<Hole> Holes => holes;

    /// <summary>
    /// How clear the windows make a point (0 to 1), before a surface's own level: the most of any window's, each the
    /// smoothstep from its rim's cosine to its middle's of the cosine between the line from the eye to the point and the
    /// window's line — and nothing beyond the window's reach. **The shader does the same sum** (<c>Level.hlsl</c>'s
    /// <c>Clearness</c>), so the picture and the pointer agree. Under the x-ray every point is clear.
    /// </summary>
    public float Clearness(Vector3 point)
    {
        if (Mode == SeeThroughMode.XRay) return 1f;
        return ClearnessOf(Eye, holes, point);
    }

    /// <summary><see cref="Clearness"/>'s sum, with everything it reads named (a test's, and the renderer's reference).</summary>
    public static float ClearnessOf(Vector3 eye, IReadOnlyList<Hole> windows, Vector3 point)
    {
        var d = point - eye;
        float length = d.Length();
        if (length < 1e-3f) return 1f;
        var v = d / length;
        float best = 0f;
        foreach (var h in windows)
        {
            if (h.Far <= 0f || Vector3.Dot(d, h.Axis) >= h.Far) continue;
            best = MathF.Max(best, SmoothStep(h.CosOuter, h.CosInner, Vector3.Dot(v, h.Axis)));
        }
        return best;
    }

    /// <summary>HLSL's <c>smoothstep</c>: 0 at or below <paramref name="from"/>, 1 at or above <paramref name="to"/>, eased between.</summary>
    public static float SmoothStep(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Whether the pointer looks through a level surface at <paramref name="point"/> on it: the surface is faded this frame and,
    /// at its level, at least <see cref="LookThrough"/> clear there (<see cref="Clearness"/>) — where the picture shows what
    /// is behind it more than it shows it.
    /// </summary>
    public bool LooksThrough(int sector, int surface, Vector3 point)
    {
        float level = LevelOf(sector, surface);
        if (level <= 0f) return false;
        return level * Clearness(point) >= LookThrough;
    }

    /// <summary>Whether anything is drawn faded this frame.</summary>
    public bool Any => active.Count > 0 || things.Count > 0;

    /// <summary>Whether any level surface is drawn faded this frame (the pointer then looks through them, <see cref="Pick.NearestShown"/>).</summary>
    public bool AnySurfaces => active.Count > 0;

    /// <summary>A level surface's fade level this frame: 0 solid, 1 as clear as it goes.</summary>
    public float LevelOf(int sector, int surface)
    {
        if ((uint)sector >= (uint)sectorBase.Length) return 0f;
        int i = sectorBase[sector] + surface;
        return (uint)i < (uint)levels.Length && surface >= 0 ? levels[i] : 0f;
    }

    /// <summary>Whether a level surface is drawn faded this frame (its level above nothing): what the pointer passes over.</summary>
    public bool Faded(int sector, int surface) => LevelOf(sector, surface) > 0f;

    /// <summary>Every level surface drawn faded this frame, with its level.</summary>
    public IEnumerable<(int Sector, int Surface, float Level)> Surfaces()
    {
        foreach (int i in active) yield return (table[i].Sector, table[i].Surface, levels[i]);
    }

    /// <summary>How opaque a surface or a thing at a fade level is drawn: 1 at 0, <see cref="Clear"/> at 1.</summary>
    public static float Opacity(float level) => 1f - Math.Clamp(level, 0f, 1f) * (1f - Clear);

    /// <summary>Everything back to solid at once.</summary>
    public void Reset()
    {
        foreach (int i in active) levels[i] = 0f;
        active.Clear();
        foreach (var t in things.Keys) t.SeeThroughLevel = 0f;
        things.Clear();
        holes.Clear();
    }

    /// <summary>
    /// The frame's decision: the subjects <paramref name="camera"/> frames in <paramref name="place"/>, seen from
    /// <paramref name="eye"/>, eased by <paramref name="seconds"/> (the interface's: it runs in a pause too). With the camera's
    /// see-through off, or on a cell level (whose world is drawn by its own pass), nothing is faded.
    /// </summary>
    public void Frame(Location place, WorldCamera? camera, Vector3 eye, float seconds)
    {
        if (camera is null || camera.SeeThrough == SeeThroughMode.Off || Levels.Cells.CellLevel.Of(place.Level) is not null)
        {
            if (Any) Reset();
            holes.Clear();
            Mode = SeeThroughMode.Off;
            return;
        }
        SubjectsOf(place, camera, subjects);
        float column = ColumnLeast;
        if (subjects.Count > 0)
        {
            float half = MathF.Tan(Math.Clamp(camera.Fov, 1f, 170f) * 0.5f * MathF.PI / 180f);
            column = Math.Clamp(Vector3.Distance(eye, subjects[0].Position) * half, ColumnLeast, ColumnMost);
        }
        Decide(place.Level, place.Things, eye, subjects, column, seconds, camera.SeeThrough);
    }

    /// <summary>
    /// The frame's windows (<see cref="SeeThroughMode.On"/>; none otherwise): one about each of the first
    /// <see cref="MostHoles"/> subjects' chests, <see cref="WindowInner"/> clear and <see cref="WindowOuter"/> to its rim as seen
    /// from <paramref name="eye"/>, reaching to <see cref="SubjectMargin"/> short of the chest.
    /// </summary>
    public static void Windows(Vector3 eye, IReadOnlyList<WorldThing> who, SeeThroughMode mode, List<Hole> into,
                               Func<WorldThing, (Vector3 Min, Vector3 Max)?>? body = null)
    {
        into.Clear();
        if (mode != SeeThroughMode.On) return;
        foreach (var s in who)
        {
            if (into.Count >= MostHoles) break;
            // A body drawn away from its point has its window about where it is drawn (PointsOf's middle).
            var chest = body?.Invoke(s) is { } drawn && Away(s, drawn) is { } middle ? middle
                      : s.Position + new Vector3(0, 0, Heights[1] + s.ShownLift);
            var line = chest - eye;
            float length = line.Length();
            if (length <= SubjectMargin) continue;
            float inner = MathF.Atan(WindowInner / length), outer = MathF.Atan(WindowOuter / length);
            into.Add(new Hole(line / length, MathF.Cos(inner), MathF.Cos(outer), length - SubjectMargin));
        }
    }

    /// <summary>
    /// A subject framed where its body is drawn (ours): when <paramref name="body"/> lies more than <see cref="BodyAside"/>
    /// across from the subject's own point, or is lower than <see cref="BodyUpright"/>, three points along its longer
    /// level side at its middle height — near one end, the middle, near the other — go into <paramref name="into"/>, and true;
    /// otherwise nothing, and false (<see cref="Heights"/> above the point frame it, as ever). 1.1's opening has Christof's
    /// point beside the convent's bed while his waking draws him lying on it: framed from the point, the bed and the sheet
    /// over him were faded.
    /// </summary>
    public static bool PointsOf(WorldThing subject, (Vector3 Min, Vector3 Max) body, List<Vector3> into)
    {
        if (Away(subject, body) is not { } middle) return false;
        var size = body.Max - body.Min;
        var along = size.X >= size.Y ? new Vector3(size.X * 0.5f, 0, 0) : new Vector3(0, size.Y * 0.5f, 0);
        float inset = MathF.Max(0f, along.Length() - SubjectMargin) / MathF.Max(along.Length(), 1e-3f);
        into.Add(middle - along * inset);
        into.Add(middle);
        into.Add(middle + along * inset);
        return true;
    }

    // The middle of a body drawn away from the subject's point (PointsOf), or null for one drawn over it, upright.
    static Vector3? Away(WorldThing subject, (Vector3 Min, Vector3 Max) body)
    {
        var (min, max) = body;
        if (min.X > max.X) return null;
        var middle = (min + max) * 0.5f;
        float aside = new Vector2(middle.X - subject.Position.X, middle.Y - subject.Position.Y).Length();
        return aside <= BodyAside && max.Z - min.Z >= BodyUpright ? null : middle;
    }

    // Whether a thing holds one of the points of a body drawn away from its own point: what it lies in or under is not faded.
    bool HoldsAny(WorldThing t)
    {
        foreach (var p in held) if (Holds(t, p)) return true;
        return false;
    }

    /// <summary>
    /// Whether <paramref name="point"/> is within a thing's body, grown by <see cref="HoldSlack"/>: its box (turned as the
    /// thing is), its upright cylinder or its ball. A thing with no body holds nothing.
    /// </summary>
    public static bool Holds(WorldThing t, Vector3 point)
    {
        switch (t.Collide)
        {
            case CollideShape.Box:
            {
                var r = t.Rotation;
                var d = point - t.Position;
                Span<Vector3> axes = [new(r.M11, r.M12, r.M13), new(r.M21, r.M22, r.M23), new(r.M31, r.M32, r.M33)];
                float[] lo = [t.BoxMin.X, t.BoxMin.Y, t.BoxMin.Z], hi = [t.BoxMax.X, t.BoxMax.Y, t.BoxMax.Z];
                for (int i = 0; i < 3; i++)
                {
                    float x = Vector3.Dot(d, axes[i]);
                    if (x < lo[i] - HoldSlack || x > hi[i] + HoldSlack) return false;
                }
                return true;
            }
            case CollideShape.Cylinder:
                return new Vector2(point.X - t.Position.X, point.Y - t.Position.Y).Length() <= t.Radius + HoldSlack
                       && point.Z >= t.Position.Z - HoldSlack && point.Z <= t.Position.Z + t.Height + HoldSlack;
            case CollideShape.Sphere:
                return Vector3.Distance(point, t.Position) <= t.Radius + HoldSlack;
            default:
                return false;
        }
    }

    /// <summary>Whom the camera frames (<see cref="SeeThrough"/>'s *Who the subjects are*), in <paramref name="into"/>.</summary>
    public static void SubjectsOf(Location place, WorldCamera camera, List<WorldThing> into)
    {
        into.Clear();
        void Add(WorldThing? t)
        {
            if (t is null || !t.InWorld || !ReferenceEquals(t.Place, place)) return;
            foreach (var have in into) if (ReferenceEquals(have, t)) return;
            into.Add(t);
        }
        switch (camera.Mode)
        {
            case CameraMode.Game or CameraMode.Path: Add(camera.Subject); break;
            case CameraMode.Cutscene: Add(camera.CutsceneA); Add(camera.CutsceneB); break;
            default: return;
        }
        if (place.Talk?.Current is { } talk) foreach (var who in talk.Actors) Add(who);
    }

    /// <summary>
    /// The decision itself, with everything it reads named (a test's): the level surfaces and the props of
    /// <paramref name="world"/> and <paramref name="candidates"/> between <paramref name="eye"/> and
    /// <paramref name="who"/>, and the ceilings over them within <paramref name="column"/>, eased by
    /// <paramref name="seconds"/> (none: at once); seen through by <paramref name="mode"/>, whose windows it lays
    /// (<see cref="Windows"/>).
    /// </summary>
    public void Decide(NilLevel world, IEnumerable<WorldThing> candidates, Vector3 eye, IReadOnlyList<WorldThing> who,
                       float column, float seconds, SeeThroughMode mode = SeeThroughMode.XRay)
    {
        Table(world);
        Mode = mode;
        Eye = eye;
        Array.Clear(wanted);
        thingsWanted.Clear();
        points.Clear();
        held.Clear();
        foreach (var s in who)
        {
            // A body its animation draws away from its point is framed where it is drawn, and what holds it kept (PointsOf).
            if (Body?.Invoke(s) is { } body && PointsOf(s, body, points))
            {
                for (int k = points.Count - 3; k < points.Count; k++) held.Add(points[k]);
                continue;
            }
            foreach (float h in Heights) points.Add(s.Position + new Vector3(0, 0, h + s.ShownLift));
        }
        Windows(eye, who, mode, holes, Body);
        if (points.Count > 0)
        {
            var lo = eye; var hi = eye;
            foreach (var p in points) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            lo -= new Vector3(CastRadius); hi += new Vector3(CastRadius);
            for (int i = 0; i < table.Length; i++)
            {
                ref readonly var e = ref table[i];
                if (!e.Drawn) continue;
                if (Overlaps(e.Min, e.Max, lo, hi) && InTheWay(world, e, eye)) wanted[i] = true;
                else if (Over(world, e, eye, who, column)) wanted[i] = true;
            }
            foreach (var t in candidates)
                if (Fades(t, who) && !HoldsAny(t) && Meets(t, eye)) thingsWanted.Add(t);
        }
        Ease(seconds);
    }

    // The surfaces a line from the eye to one of the points passes through, either side.
    bool InTheWay(NilLevel world, in Entry e, Vector3 eye)
    {
        var sector = world.Sectors[e.Sector];
        var surface = sector.Surfaces[e.Surface];
        var plane = sector.SurfacePlanes[surface.Plane];
        foreach (var p in points)
        {
            float length = Vector3.Distance(eye, p);
            if (length <= SubjectMargin) continue;
            float de = Vector3.Dot(plane.Normal, eye) - plane.Dist, dp = Vector3.Dot(plane.Normal, p) - plane.Dist;
            if (de > 0f == dp > 0f || de == dp) continue;
            float t = de / (de - dp);
            if (t * length > length - SubjectMargin) continue;
            if (WorldSweep.Reaches(sector, surface, plane, eye + (p - eye) * t, CastRadius)) return true;
        }
        return false;
    }

    // A ceiling or a roof over one of the subjects: flat enough, wholly above the head and below the eye, within the column.
    static bool Over(NilLevel world, in Entry e, Vector3 eye, IReadOnlyList<WorldThing> who, float column)
    {
        if (e.Min.Z >= eye.Z) return false;
        var sector = world.Sectors[e.Sector];
        var normal = sector.SurfacePlanes[sector.Surfaces[e.Surface].Plane].Normal;
        if (MathF.Abs(normal.Z) < Flat) return false;
        foreach (var s in who)
        {
            float top = s.Position.Z + s.ShownLift + MathF.Max(s.Height, Heights[^1]) + HeadClearance;
            if (e.Min.Z <= top) continue;
            float dx = MathF.Max(0f, MathF.Max(e.Min.X - s.Position.X, s.Position.X - e.Max.X));
            float dy = MathF.Max(0f, MathF.Max(e.Min.Y - s.Position.Y, s.Position.Y - e.Max.Y));
            if (dx * dx + dy * dy > column * column) continue;
            // Not a roof beyond the subject: its box's nearest corner to the eye, along the level line from the eye to the
            // subject, no further than the subject and a little.
            var level = new Vector2(s.Position.X - eye.X, s.Position.Y - eye.Y);
            float reach = level.Length();
            if (reach < 1f) return true;
            level /= reach;
            float nearest = MathF.Min(level.X * (e.Min.X - eye.X), level.X * (e.Max.X - eye.X))
                          + MathF.Min(level.Y * (e.Min.Y - eye.Y), level.Y * (e.Max.Y - eye.Y));
            if (nearest <= reach + ColumnBeyond) return true;
        }
        return false;
    }

    // Only props and containers are faded: never a character, nor what a subject is.
    static bool Fades(WorldThing t, IReadOnlyList<WorldThing> who)
    {
        if (t.Type is not (ThingType.Prop or ThingType.Container) || t.IsActor || !t.InWorld || t.ModelHidden) return false;
        foreach (var s in who) if (ReferenceEquals(s, t)) return false;
        return true;
    }

    // A thing's body met by a line from the eye to one of the points, short of it.
    bool Meets(WorldThing t, Vector3 eye)
    {
        foreach (var p in points)
        {
            var line = p - eye;
            float length = line.Length();
            if (length <= SubjectMargin) continue;
            if (Pick.PointerMeets(t, eye, line / length, length - SubjectMargin) is { } d && d <= length - SubjectMargin) return true;
        }
        return false;
    }

    // Every level towards where it is going; the things' too, each written onto its thing.
    void Ease(float seconds)
    {
        float step = seconds > 0f ? seconds / EaseSeconds : float.PositiveInfinity;
        scratch.Clear();
        foreach (int i in active) if (!wanted[i]) scratch.Add(i);
        for (int i = 0; i < wanted.Length; i++)
        {
            if (!wanted[i]) continue;
            if (levels[i] <= 0f) active.Add(i);
            levels[i] = MathF.Min(1f, levels[i] + step);
        }
        foreach (int i in scratch)
        {
            levels[i] = MathF.Max(0f, levels[i] - step);
            if (levels[i] <= 0f) { levels[i] = 0f; active.Remove(i); }
        }
        foreach (var t in thingsWanted)
        {
            things.TryGetValue(t, out float was);
            things[t] = t.SeeThroughLevel = MathF.Min(1f, was + step);
        }
        foreach (var t in things.Keys.ToList())
        {
            if (thingsWanted.Contains(t)) continue;
            float now = MathF.Max(0f, things[t] - step);
            t.SeeThroughLevel = now;
            if (now <= 0f || !t.InWorld) { t.SeeThroughLevel = 0f; things.Remove(t); }
            else things[t] = now;
        }
    }

    static bool Overlaps(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax) =>
        aMin.X <= bMax.X && aMax.X >= bMin.X && aMin.Y <= bMax.Y && aMax.Y >= bMin.Y && aMin.Z <= bMax.Z && aMax.Z >= bMin.Z;

    // The level's surfaces, boxed once: a surface of a Sky or Nodraw group is never drawn and is never faded.
    void Table(NilLevel world)
    {
        if (ReferenceEquals(world, level)) return;
        Reset();
        level = world;
        var entries = new List<Entry>();
        sectorBase = new int[world.Sectors.Length];
        for (int si = 0; si < world.Sectors.Length; si++)
        {
            var sector = world.Sectors[si];
            sectorBase[si] = entries.Count;
            for (int fi = 0; fi < sector.Surfaces.Length; fi++)
            {
                var surface = sector.Surfaces[fi];
                var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
                for (int k = 0; k < surface.NumVerts; k++)
                {
                    var v = sector.Vertices[sector.SectorVertices[surface.FirstVertex + k].Vertex];
                    min = Vector3.Min(min, v); max = Vector3.Max(max, v);
                }
                bool drawn = surface.NumVerts >= 3 && surface.Group >= 0 && surface.Group < sector.Groups.Length
                             && (sector.Groups[surface.Group].SurfaceFlags & (NilGroup.FlagSky | NilGroup.FlagNodraw)) == 0
                             && sector.Groups[surface.Group].Material >= 0;
                entries.Add(new Entry(si, fi, min, max, drawn));
            }
        }
        table = [.. entries];
        levels = new float[table.Length];
        wanted = new bool[table.Length];
    }
}
