// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A place's frame as the module says it (the module never draws): the game half of the frame renderer; its drawing half is
// the host's
// renderer's (Vitomir.Platform.Render.Classic.ClassicFrameRenderer).
using System.Numerics;
using Vitomir.Engine.Scene;
using Vitomir.Engine.World;

namespace Vitomir.Classic;

/// <summary>
/// **A place's frame, the module's side**: the place, its simulation, the frame's game side (<see cref="Game"/>,
/// Engine.World.WorldFrame: the step, the walk, the mirrors, the sky's and the ring's clocks, the poses), the poses and the
/// snapshot's producer (<see cref="Snapshot"/>), and what goes over the frame (<see cref="After"/>: the interface's passes, the
/// screen's fade and flash, the developer overlay). It says what the frame is — the Classic snapshot (<see cref="ClassicFrame"/>)
/// and the frame's <see cref="ClassicOver"/> — and the host's renderer draws it (<see cref="World"/>, asked of the host's
/// <see cref="IClassicRenderer"/>). It holds no GPU object and makes no GPU call.
/// <para>Until then this and the drawing were one class of the old glue (<c>Vitomir.Render.FrameRenderer</c>); the members kept
/// their names, so the controller reads as before.</para>
/// </summary>
public sealed class PlaceFrame : IDisposable
{
    readonly Location location;
    readonly ClassicOver over = new();
    FreeCamera? drawnCamera;

    /// <summary>The host's renderer's world for this place (made with the place's frame, as the old glue made its own).</summary>
    public IClassicWorld World { get; }
    /// <summary>
    /// The clock this frame draws. Every frame shows where the simulation has got to; the
    /// drawing advances nothing itself (<see cref="Game"/> does, before the draw).
    /// </summary>
    public Simulation Sim { get; }
    /// <summary>
    /// **The frame's game side** (Engine.World.WorldFrame): the step, the walk, the mirrors, the sky's and the ring's
    /// clocks, the poses. <c>Game.Frame</c> runs before every <see cref="Render"/>, <c>Game.Advance</c> for a still's
    /// later moment; the snapshot reads what they leave.
    /// </summary>
    public WorldFrame Game { get; }
    /// <summary>The place's animating things posed and lit for the picture (Engine.Scene.ScenePoses; <see cref="Game"/> poses them).</summary>
    public ScenePoses Poses { get; }
    /// <summary>**The frame's Classic snapshot** (Engine.World.WorldSnapshot): filled by <see cref="Render"/> after the game side.</summary>
    public WorldSnapshot Snapshot { get; }
    /// <summary>
    /// The scene's scripts. Defaults to none; the game installs the real script layer, which is
    /// what switches light styles, sets material frames and runs the scene's own timers.
    /// </summary>
    public ISceneScripts Scripts { get => Sim.Scripts; set => Sim.Scripts = value; }
    /// <summary>Time since the location was entered: what sliding textures and light styles run on.</summary>
    public float Seconds => Sim.Seconds;
    /// <summary>A viewer aid: draw the sky over the whole frame instead of inside its window (the host's world's).</summary>
    public bool SkyEverywhere { get => World.SkyEverywhere; set => World.SkyEverywhere = value; }
    /// <summary>The scene's fog, applied to level and things (the sky turns it off): the snapshot's.</summary>
    public FogSettings Fog { get => Snapshot.Scene.Fog; set => Snapshot.Scene.Fog = value; }
    /// <summary>Whose fade and flash the frame's snapshot carries (<see cref="ClassicFrame.Screen"/>): the game camera's.</summary>
    public ScreenEffects? Effects { get => Snapshot.Effects; set => Snapshot.Effects = value; }
    /// <summary>The hover glow's state, when something is feeding one (Engine.Ui.HoverGlow): it tints and lights the things it names.</summary>
    public Engine.Ui.HoverGlow? Glow { get => Poses.Glow; set => Poses.Glow = value; }

    /// <summary>
    /// What goes over the finished frame, after the brightness curve, in order: the interface's draw list, the screen's fade and
    /// flash and, last, the developer overlay (<see cref="FrameLayers"/>). Emitted into the frame's <see cref="ClassicOver"/>
    /// after its snapshot and before the host draws it.
    /// </summary>
    public IFrameLayer? After { get; set; }

    /// <summary>
    /// The levels generator's cell world under the things, for a cell level (switched off); null for every 1.1 level,
    /// and under the platform always (<see cref="CellWorlds"/>).
    /// </summary>
    public ICellWorld? Below { get; }

    public PlaceFrame(IClassicRenderer renderer, Location location, Simulation? sim = null)
    {
        this.location = location;
        Sim = sim ?? new Simulation(location);
        // The place's own styles; the renderer makes no spare of its own: whoever builds it draws the one it used to
        // make first (LightStyles.DrawTheRenderersSpare).
        Poses = new ScenePoses(location.Mesh) { Things = location.Drawn, Place = location };
        Game = new WorldFrame(location, Sim) { Poses = Poses };
        Snapshot = new WorldSnapshot(location, Game, Poses);
        // The host's renderer's world, with the place's sky as it is now (a viewer's --sky-only and --sky-hide are set on it next).
        World = renderer.World(Snapshot.Lightmaps, Snapshot.Sky);
        // A cooked level's world and its characters (ours, which only vitomir-tools draws).
        Below = CellWorlds.Make?.Invoke(location, Sim, World, () => drawnCamera ?? location.Camera());
        if (Below is not null) World.Below = Below.Below;
    }

    /// <summary>
    /// Draws the click-to-move ring, or null for a viewer that has none. Its state lives on the
    /// simulation (<see cref="Simulation.WalkTarget"/>) because the walker clears it; its clock runs in
    /// <see cref="Game"/> while there is a ring to draw.
    /// </summary>
    public IClassicRing? Marker { get => World.Ring; set { World.Ring = value; Game.Ring = value is not null; } }

    /// <summary>A ring for this frame's world (its material loaded now; <see cref="WorldSnapshot.RingMaterial"/>).</summary>
    public IClassicRing NewMarker() => World.NewRing(Snapshot.RingMaterial());

    /// <summary>
    /// How much of the window's height the world is drawn into, measured from the top, as a share of the whole
    /// (<see cref="WorldFrame.ViewHeight"/>: the walk and the picture share it). 1 is all of it;
    /// <see cref="Engine.Ui.StatusBar.WorldShare"/> is what the status bar leaves.
    /// </summary>
    public float ViewHeight { get => Game.ViewHeight; set => Game.ViewHeight = value; }

    /// <summary>Asked for the sheet's share at the start of every frame; <see cref="ViewHeight"/> is it laid on the picture (<see cref="WorldFrame.WorldView"/>).</summary>
    public Func<float>? WorldView { get => Game.WorldView; set => Game.WorldView = value; }

    /// <summary>The rows the world is actually drawn into: the top <see cref="ViewHeight"/> of them.</summary>
    public int WorldRows(int height) => Game.WorldRows(height);

    /// <summary>The world's projection when only the top <paramref name="rows"/> are drawn into (<see cref="WorldFrame.Viewport"/>, which says why).</summary>
    public static Matrix4x4 WorldViewport(FreeCamera camera, int width, int height, int rows, float far, float magnify = 1f, float stretch = 1f, int drawn = 0) =>
        WorldFrame.Viewport(camera, width, height, rows, far, magnify, stretch, drawn);

    /// <summary>
    /// The size the world is drawn at when it is not the target's (ours, <see cref="WorldFrame.WorldSize"/>, which the
    /// walk lays its picture out by too). A picture taken to a file (F12) shows what the screen shows.
    /// </summary>
    public static (int Width, int Height)? WorldSize { get => WorldFrame.WorldSize; set => WorldFrame.WorldSize = value; }

    /// <summary>How long the last frame's game step took (slow frames, <see cref="WorldFrame.LastStepMs"/>).</summary>
    public double LastStepMs => Game.LastStepMs;

    /// <summary>
    /// Where the last frame's drawing went, in milliseconds of this thread's own time (for the slow-frame line): the host's
    /// world's, with the interface's emitting (the module's) added to its interface.
    /// </summary>
    public (double Animated, double Shadows, double Mirrors, double World, double Interface) LastDrawMs
    {
        get
        {
            var d = World.LastDrawMs;
            return (d.Animated, d.Shadows, d.Mirrors, d.World, d.Interface + lastEmitMs);
        }
    }

    double lastEmitMs;

    /// <summary>How long the last frame's snapshot took to fill (the copy and what the producer works out), in milliseconds.</summary>
    public double LastSnapshotMs => Snapshot.LastFillMs;

    /// <summary>The last frame's view and projection together (row vectors), or null before the first; the show-objects key's view test.</summary>
    public Matrix4x4? ViewProjection { get; private set; }

    /// <summary>Whether the animating things the view cannot show are passed over (<see cref="WorldFrame.PoseOnlySeen"/>).</summary>
    public bool PoseOnlySeen { get => Game.PoseOnlySeen; set => Game.PoseOnlySeen = value; }

    /// <summary>
    /// **The frame said** for a target <paramref name="width"/> × <paramref name="height"/> from <paramref name="camera"/>: what
    /// <see cref="Game"/>'s <c>Frame</c> left, taken into the frame's snapshot, and what goes over it emitted. Writes nothing of the
    /// game's.
    /// </summary>
    ClassicFrame Say(FreeCamera camera, int width, int height)
    {
        drawnCamera = camera;
        var frame = Snapshot.Fill(camera, width, height, cellWorld: Below is not null,
                                  castShadows: Below is not { DrawsThings: true },   // a cell level's cast the sun's own (ours)
                                  ring: Marker is not null);
        ViewProjection = frame.Layout.ViewProjection;      // the show-objects key's view test (ours, Engine.Ui.ShowObjects)
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        over.Clear();
        After?.Emit(over, frame.Layout.TargetWidth, frame.Layout.TargetHeight, frame, camera, Sim);
        lastEmitMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        return frame;
    }

    /// <summary>One frame of <paramref name="camera"/>'s view said and drawn by the host's renderer into the host's picture.</summary>
    public void Render(in GpuPicture picture, FreeCamera camera)
    {
        var frame = Say(camera, picture.Width, picture.Height);
        World.Draw(picture, frame, over);
    }

    /// <summary>
    /// One frame with no window at all, its pixels handed back (RGBA, top row first): what the headless viewer and the
    /// rendering tests use, after the frame's <c>Game.Frame</c> as for any other picture.
    /// </summary>
    public byte[] RenderToImage(int width, int height, FreeCamera camera)
    {
        var frame = Say(camera, width, height);
        return World.Still(width, height, frame, over);
    }

    /// <summary>The host's world let go (and with it a cell level's world under it).</summary>
    public void Dispose() => World.Dispose();
}
