// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a frame does to the game around its picture (out of the old renderer).
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.Scene;
using Vitomir.Engine.Ui;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// The world's picture laid out on a target (ours; until then <c>FrameRenderer.Render</c>'s own lines, FrameRenderer.cs:232-266):
/// the size the world is drawn at and its stretch (<see cref="WorldFrame.WorldSize"/>), the rows it is drawn into from the
/// top and how far down it carries on under a fitted status bar, the columns a pane leaves it, its projection and view.
/// </summary>
public readonly record struct FrameLayout(int TargetWidth, int TargetHeight, int Width, int Height, float Stretch,
                                          int Rows, int Drawn, ViewColumns Columns, int X0, int Cols,
                                          Matrix4x4 Projection, Matrix4x4 ViewProjection);

/// <summary>
/// **What one frame does to the game around its picture** (ours). Earlier, the old
/// renderer did all of this inside <c>FrameRenderer.Render</c>;  moved it here, so the renderer only
/// reads, and <c>vitomir-tools</c> and the Classic module (which never draws) call this one copy at the same point of the
/// frame. <see cref="Frame"/>, in the old renderer's order: the rows asked for, the rain's box on the camera, the
/// particles' first-draw rule, the interface's clock, <b>the world's step</b>, the drawn camera aimed, <b>the frame's portal
/// walk</b> handed to the particles and the lights, and the mirrors in view, the sky's own animation while it is on
/// screen, the animating things posed and lit (<see cref="Poses"/>, when there is a picture), the click ring's clock, and
/// the light blobs gathered.
/// <para>The renderer reads what is left here: <see cref="Walked"/> and <see cref="Reaches"/>, <see cref="Mirror"/> and
/// <see cref="Mirrored"/>, <see cref="SkyOn"/>, <see cref="RingShown"/>, <see cref="InterfaceSeconds"/>, and lays its
/// picture out with the same <see cref="Lay"/>.</para>
/// </summary>
public sealed class WorldFrame(Location location, Simulation sim)
{
    readonly NilLevel level = location.Level;
    readonly bool cellWorld = CellLevel.Of(location.Level) is not null;   // a cooked level's own world pass (ours)
    bool drawnOnce, walked;
    int lastStart = -1;
    (Vector3 Min, Vector3 Max)[]? boxes;

    public Location Location => location;
    public Simulation Sim => sim;

    /// <summary>
    /// Whether <see cref="Frame"/> steps <see cref="Sim"/> (it does, by default). False for a view of a place something
    /// else steps — the host's other places, which <c>Net.HostPlaces</c> steps (ours).
    /// </summary>
    public bool Steps { get; set; } = true;

    /// <summary>
    /// Called after the world has stepped and before the walk, with the camera about to be drawn from: where the harness
    /// points it at whatever the game's own camera (<see cref="Simulation.Camera"/>) has just worked out. Null leaves the
    /// camera as given.
    /// </summary>
    public Action<FreeCamera>? Aim { get; set; }

    /// <summary>
    /// How much of the target's height the world is drawn into, from the top, as a share of the whole (1 all of it;
    /// <see cref="StatusBar.WorldShare"/> what the status bar leaves; <see cref="Viewport"/> says what it does).
    /// </summary>
    public float ViewHeight { get; set; } = 1f;

    /// <summary>
    /// Asked at the start of every <see cref="Frame"/> for **the sheet's share** the world has (1, or
    /// <see cref="StatusBar.WorldShare"/> with the status bar up), so a viewer whose status bar toggles in play does not
    /// have to keep the two in step; <see cref="ViewHeight"/> is that share laid on the frame's picture
    /// (<see cref="Asked"/>). Null leaves <see cref="ViewHeight"/> alone.
    /// </summary>
    public Func<float>? WorldView { get; set; }

    /// <summary>
    /// What <see cref="WorldView"/> asks for now as a share of a picture <paramref name="width"/> by
    /// <paramref name="height"/> (<see cref="UiLayout.WorldShare(float, float, float)"/>: the same number wherever the
    /// sheet is as tall as the picture; more of the picture where the sheet is shorter and sits at its bottom).
    /// <see cref="ViewHeight"/> with no <see cref="WorldView"/>.
    /// </summary>
    public float Asked(int width, int height) => WorldView is null ? ViewHeight : UiLayout.WorldShare(WorldView(), width, height);

    /// <summary>The picture the last <see cref="Frame"/> was for (0 by 0 before the first).</summary>
    public (int Width, int Height) Target { get; private set; }

    /// <summary>The rows the world is drawn into: the top <see cref="ViewHeight"/> of them.</summary>
    public int WorldRows(int height) => Rows(height, ViewHeight);

    static int Rows(int height, float viewHeight) => Math.Clamp((int)MathF.Round(height * viewHeight), 1, height);

    /// <summary>
    /// **Whether a pixel of the picture shows the world** (ours): the columns and
    /// the rows the world is drawn into this frame, and nothing else. **The one test of whose a pointer event is once no
    /// pane has taken it**: the hover, the left button's order, the right button's cast, a held button's walk and the
    /// harness's aim all ask here, so none of them can take a place on the picture for the world's that another does
    /// not. Off it the picture shows no world, and a pointer there is nobody's: no pick, no order, no ring.
    /// <para>What it is, as <see cref="Lay"/> draws it: the columns a pane leaves the world (<see cref="Pick.View"/>,
    /// None of them in state 4); down them, the top <see cref="ViewHeight"/> of the rows (1.1's: the rows
    /// above the status bar's line, all of them with no bar); and, where the world is carried on down under a bar that
    /// lies over it (<see cref="UiLayout.Overlays(float, float)"/>: <see cref="FrameLayout.Drawn"/>), the rows below those **beside
    /// the sheet**, since inside the sheet's own columns the bar's rows are the bar's and its black
    /// (<c>StatusBarPass</c>). A cell level's world is not carried on, so there the rows stay 1.1's.</para>
    /// <para>Where the sheet is the picture (1.1's stretch, and the 16:9 stretch on a 16:9 picture) this is
    /// <see cref="Pick.OnWorld(float, int, int)"/> across the pane's columns, which is what the hover asked earlier;
    /// and there every pixel it refuses is under a quad of the status bar or of an open pane, which 1.1 gives to that
    /// pane.</para>
    /// </summary>
    public bool Shows(float x, float y, int width, int height) =>
        Shows(x, y, width, height, ViewHeight, cellWorld, Pick.View, UiLayout.Mode, UiLayout.Seat);

    /// <summary>The same with everything it reads named (a test's, which puts none in force).</summary>
    public static bool Shows(float x, float y, int width, int height, float viewHeight, bool cellWorld,
                             ViewColumns columns, UiLayoutMode mode, SheetSeat seat)
    {
        if (!columns.Drawn || width <= 0 || height <= 0 || y < 0f || y >= height) return false;
        var (x0, cols) = columns.Pixels(width);
        if (x < x0 || x >= x0 + cols) return false;
        if (y < Rows(height, viewHeight)) return true;
        // Below 1.1's rows: only where the world is carried on to the picture's bottom edge, and then beside the sheet.
        if (cellWorld || viewHeight >= 1f || !UiLayout.Overlays(width, height, mode)) return false;
        var map = UiLayout.On(width, height, mode, seat);
        return x < map.X || x >= map.X + UiAssets.Width * map.ScaleX;
    }

    /// <summary>
    /// Whether reflections are drawn: <c>detailFlags</c> bit 4 clear — in 1.1 normal and high, and the Advanced screen's
    /// box; in Vitomir always, the maxed-out preset's. The mirror search runs only when on.
    /// </summary>
    public bool Reflections { get; set; } = DetailPresets.Reflections;

    /// <summary>Whether the animating things the view cannot show are passed over (the diagnostics turn it off).</summary>
    public bool PoseOnlySeen { get; set; } = Environment.GetEnvironmentVariable("VIT_POSE_ALL") is null;

    /// <summary>
    /// The world's animating things, posed and lit by <see cref="Frame"/> for the picture; null where nothing draws (the
    /// Classic module's headless loop: the posed vertices are the picture's, never the game's).
    /// </summary>
    public ScenePoses? Poses { get; set; }

    /// <summary>Whether the click ring is drawn, so its clock runs (a viewer with a ring renderer).</summary>
    public bool Ring { get; set; }

    /// <summary>How long the last frame's step took (slow frames).</summary>
    public double LastStepMs { get; private set; }

    /// <summary>The main view's portal walk (from the eye's sector), which also says which emitters were seen.</summary>
    public PortalView View { get; } = new();

    /// <summary>This frame's portal walk, or null when the eye was in no sector and nothing was walked.</summary>
    public PortalView? Walked => walked ? View : null;

    /// <summary>
    /// Whether the picture leaves nothing out for the walk's sake (ours): under the developer's x-ray
    /// (<see cref="SeeThroughMode.XRay"/>, <see cref="WorldCamera.SeeThrough"/>) the eye may stand behind a wall, or outside the level, from where the portals
    /// do not reach whom it frames; so every sector is drawn and every animating thing posed, as with an eye in no sector.
    /// The walk itself still runs (the particles, the light blobs and the mirrors read it).
    /// </summary>
    public bool SeesAll => sim.Camera?.SeeThrough == SeeThroughMode.XRay;

    /// <summary>After a walk: the mirrors in view and the mirrored view, or null (left as it was by a frame the world is not drawn in).</summary>
    public MirrorView? Mirror { get; private set; }

    /// <summary>Whether this frame's picture shows a mirror (the world drawn and <see cref="Mirror"/> found).</summary>
    public bool Mirrored { get; private set; }

    /// <summary>Whether this frame's picture shows the sky, and its window in the world's columns (y down).</summary>
    public bool SkyOn { get; private set; }

    /// <summary>The sky window <see cref="Frame"/> found, in the world's own columns (before the first column is added).</summary>
    public (int X, int Y, int W, int H) SkyWindow { get; private set; }

    /// <summary>Whether the click ring shows this frame (its clock's answer).</summary>
    public bool RingShown { get; private set; }

    /// <summary>The interface's seconds this frame ran on (the ring, the halos; <see cref="DrawClocks"/>).</summary>
    public float InterfaceSeconds { get; private set; }

    /// <summary>
    /// The size the world is drawn at when it is not the target's (ours, borderless full screen at a size of its own —
    /// <c>Engine.Ui.FullScreen.WorldSizeOn</c>): the world goes into an off-screen picture this size and is stretched over the
    /// whole target; the interface is drawn after, at the target's own size. Shapes stay as they are whatever the two sizes'
    /// proportions: the projection is the target's (<see cref="Viewport"/>'s stretch). Null, or a size not smaller than the
    /// target: the target's size, as ever.
    /// </summary>
    public static (int Width, int Height)? WorldSize { get; set; }

    /// <summary>
    /// Runs the clock forward without a picture, so a still can be asked for a later moment. The sky is stepped alongside it
    /// because there is no frame here to say whether it would have been visible, and the original only animates it when it is.
    /// </summary>
    public void Advance(float seconds, float step = 1f / 60f)
    {
        for (float t = 0; t < seconds; t += step)
        {
            float slice = MathF.Min(step, seconds - t);
            sim.Step(slice);
            location.Sky?.Player?.Advance(slice);
        }
    }

    /// <summary>
    /// The frame's game side, before its picture is drawn (FrameRenderer.Render's writes until then, in its order):
    /// <paramref name="seconds"/> the world's step (0 in a pause), <paramref name="interfaceSeconds"/> the interface's
    /// (null: the game's), for a target <paramref name="width"/> by <paramref name="height"/> drawn from
    /// <paramref name="camera"/>.
    /// </summary>
    public void Frame(FreeCamera camera, int width, int height, float seconds, float? interfaceSeconds = null)
    {
        Target = (width, height);
        ViewHeight = Asked(width, height);
        location.Particles.Eye = camera.Position;   // the rain's box follows the camera
        if (!drawnOnce) { drawnOnce = true; location.Particles.BeforeFirstDraw(); }   // no walk yet: nothing drawn
        float ui = interfaceSeconds ?? seconds;      // the interface's clock (DrawClocks)
        InterfaceSeconds = ui;
        sim.InterfaceSeconds = ui;
        long stepStart = System.Diagnostics.Stopwatch.GetTimestamp();
        if (Steps) sim.Step(seconds);
        LastStepMs = System.Diagnostics.Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds;
        Aim?.Invoke(camera);
        var lay = Lay(camera, width, height, ViewHeight, cellWorld);
        // The portal walk and the mirrors in view (the world draw's first half), only for a world drawn.
        Mirrored = lay.Columns.Drawn && Find(camera.Position, camera.View, lay.Projection);
        // The sky only exists where the level's Sky surfaces are on screen, and only then does its own animation advance.
        (int X, int Y, int W, int H) window = default;
        SkyOn = lay.Columns.Drawn && location.Sky is not null && location.SkyWindow!.ScreenRect(lay.ViewProjection, lay.Cols, lay.Drawn, out window);
        SkyWindow = window;
        if (SkyOn) location.Sky!.Player?.Advance(seconds);
        if (Poses is { } poses)
        {
            // Only what the view could show is posed; a mirror's picture reaches further, so then all of it.
            poses.Reached = !Mirrored && PoseOnlySeen && Walked is not null && !SeesAll ? Reaches : null;
            poses.Pose();
        }
        // What is drawn see-through for being in the camera's way (ours, SeeThrough): decided once, from the eye drawn from,
        // for the picture and the pointer both — after the poses, so a subject's body is framed where this frame draws it.
        location.SeeThrough.Body = Poses is { } posed ? posed.BodyOf : null;
        location.SeeThrough.Frame(location, sim.Camera, camera.Position, ui);
        // The click ring, at whatever size and colour its clock has reached.
        RingShown = Ring && sim.WalkTarget.Advance(ui);
        // The dynamic lights' blobs on the walk's surfaces.
        if (location.Lights?.View is not null) location.Lights.GatherBlobs();
    }

    /// <summary>The main portal walk and the mirror search (<see cref="Mirrors.Find"/>): false when no mirror is in view.</summary>
    bool Find(Vector3 eye, Matrix4x4 view, Matrix4x4 projection)
    {
        Mirror = null;
        int start = location.Solid is { } solid ? PortalView.SettledStart(level, solid, solid.SectorAt(eye, lastStart), eye) : -1;
        lastStart = start;
        walked = start >= 0;
        View.Walk(level, start, eye, view * projection, ScreenRect.Full, PortalCamera.From(view, projection));   // with the camera's tests
        location.Particles.View = walked ? View : null;
        location.Lights.View = walked ? View : null;         // the light blobs' lights and surfaces
        if (!Reflections || !walked) return false;
        Mirror = Mirrors.Find(level, View, eye, view, projection);
        return Mirror is not null;
    }

    /// <summary>
    /// Whether the main view could show a thing: one in no sector, in a sector the walk reached, or near enough to one to
    /// reach into it — a ball of <paramref name="radius"/> about <paramref name="centre"/> against the sector's box. The engine
    /// draws a walked sector's own things and the things on its list of those overlapping it (the sector draw's two loops); **ours**
    /// keeps no such list, and the box is the generous stand-in for it. With no walk, everything.
    /// </summary>
    public bool Reaches(long sector, Vector3 centre, float radius)
    {
        if (!walked || sector < 0 || View.Sees((int)sector)) return true;
        boxes ??= [.. level.Sectors.Select(Box)];
        float within = radius * radius;
        foreach (var (si, _) in View.Visible)
        {
            var (min, max) = boxes[si];
            var nearest = Vector3.Clamp(centre, min, max);
            if (Vector3.DistanceSquared(nearest, centre) <= within) return true;
        }
        return false;
    }

    static (Vector3, Vector3) Box(NilSector sector)
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var v in sector.Vertices) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
        return (min, max);
    }

    /// <summary>
    /// The world's picture on a target <paramref name="width"/> by <paramref name="height"/> drawn from
    /// <paramref name="camera"/> with <paramref name="viewHeight"/> of the height (the one copy the walk and the renderer
    /// share; FrameRenderer.Render's lines until then). <paramref name="cellWorld"/>: a cell level's own world pass works its
    /// picture out from the rows it is given, so there the rows stay 1.1's.
    /// </summary>
    public static FrameLayout Lay(FreeCamera camera, int width, int height, float viewHeight, bool cellWorld)
    {
        // The target's size is the interface's; the world's may be smaller (WorldSize).
        int targetWidth = width, targetHeight = height;
        float stretch = 1f;
        if (WorldSize is { } world && world.Width >= 1 && world.Height >= 1
            && world.Width <= width && world.Height <= height && world != (width, height))
        {
            stretch = width / (float)height / (world.Width / (float)world.Height);
            (width, height) = world;
        }
        int rows = Rows(height, viewHeight);
        // Where the sheet does not span the picture across — fitted on a picture wider than 4:3, or the 16:9 stretch on
        // one wider than 16:9 — the status bar no longer reaches the picture's sides: the world is drawn on down to the
        // bottom edge and the bar lies over it, so what shows beside the bar is world and not black (ours; UiLayout.Overlays). **The view does not move**: its centre stays the centre of 1.1's rows and the same things
        // are in the same place; there is only more of it below (Viewport's `drawn`).
        int drawn = !cellWorld && viewHeight < 1f && UiLayout.Overlays(targetWidth, targetHeight) ? height : rows;
        // The columns a pane leaves the world (`Pick.View`): all of them, one half, or none.
        var columns = Pick.View;
        var (x0, cols) = columns.Pixels(width);
        var projection = Viewport(camera, cols, height, rows, camera.Far, columns.Magnify, stretch, drawn);
        return new FrameLayout(targetWidth, targetHeight, width, height, stretch, rows, drawn, columns, x0, cols,
                               projection, camera.View * projection);
    }

    /// <summary>
    /// The world's projection for a window of <paramref name="width"/> by <paramref name="height"/> when only the top
    /// <paramref name="rows"/> of it are drawn into.
    ///
    /// **The scale does not change with the viewport; only how much fits does.** That is the engine's convention and it is
    /// the one thing the head camera's own arithmetic states outright: the projection computes its pixels-per-unit as
    /// <c>halfWidth / tan(fov/2)</c>, from the **width alone**, and divides it by the half-height only to turn it into a
    /// matrix term. So a shorter viewport shows *less* at the same size rather than the same amount smaller.
    ///
    /// This tree's own camera pins the scale to the height instead (a fixed vertical field of view, horizontal widening with
    /// the aspect — `FreeCamera.VerticalFov`), which is the same picture whenever the viewport is the whole window and a
    /// different one when it is not. Narrowing the vertical field of view by the viewport's share of the height is what makes
    /// the two agree: the vertical pixels-per-unit becomes <c>yScale x H/rows x rows/2</c>, which is the full window's, and
    /// the aspect keeps the horizontal one equal to it.
    /// </summary>
    /// <param name="drawn">Ours: the rows actually drawn into when they are more than <paramref name="rows"/> — the
    /// world carried on down under a status bar that lies over it (<c>UiLayout.Overlays</c>). The view is the one
    /// <paramref name="rows"/> gives, to the pixel, in those rows; the frustum is only taken further down, so its centre is no
    /// longer the centre of what is drawn. 0, or the same as <paramref name="rows"/>: 1.1's.</param>
    public static Matrix4x4 Viewport(FreeCamera camera, int width, int height, int rows, float far, float magnify = 1f, float stretch = 1f, int drawn = 0)
    {
        float fovY = 2f * MathF.Atan(MathF.Tan(camera.VerticalFov * 0.5f) * rows / height / magnify);
        if (drawn <= rows) return Matrix4x4.CreatePerspectiveFieldOfView(fovY, width / (float)rows * stretch, camera.Near, far);
        float top = camera.Near * MathF.Tan(fovY * 0.5f), right = top * (width / (float)rows * stretch);
        return Matrix4x4.CreatePerspectiveOffCenter(-right, right, -top * (2f * drawn / rows - 1f), top, camera.Near, far);
    }
}
