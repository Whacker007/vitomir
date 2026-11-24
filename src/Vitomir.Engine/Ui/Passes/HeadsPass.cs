// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The party portraits on the status bar, drawn — `vuiHeads::Draw`, the live 3D path.
//
// The engine draws each party member's own `.nod` head model, animated by its own `.nms` motion
// set, into a small perspective viewport in the middle of the bar. It is a genuine break in the
// UI's 2D stream: the heads' draw flushes the pending 2D batch on the way in and on the way out, which is
// why this is a model insert in the draw list and not a quad. `Engine.Ui.PartyHeads` decides
// *what* to draw and where; this says it.
using System.Numerics;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Scene;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Platform.Interface;

namespace Vitomir.Engine.Ui;

/// <summary>
/// The four party heads, over a finished frame: the heads' own small <see cref="SceneMesh"/> (<see cref="HeadModels"/>, which
/// makes and animates them before the draw) taken into their snapshot scene (<see cref="Scene"/>) and emitted as a
/// **model insert**: the viewport, and each head through its own offset. The old renderer's model inserts draw it
/// with a world renderer of its own (Vitomir.Render's <c>SceneInserts</c>), reading only the scene and the insert.
/// </summary>
public sealed class HeadsPass : IUiPass
{
    readonly UiCanvas ui;
    /// <summary>The heads' scene filled from the heads' models (the Classic snapshot's).</summary>
    readonly SceneSnapshot snapshot;
    readonly List<ModelDraw> draws = [];

    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    /// <summary>The heads' snapshot scene, which the model insert names (refilled each frame it is drawn).</summary>
    public ClassicScene Scene { get; } = new();

    /// <summary>
    /// The heads' models and their own per-frame pass (Engine.Ui.HeadModels): whoever drives the frame runs its
    /// <c>Frame</c> before the draw (which 1.1 runs before anything is drawn); this pass only draws what it left.
    /// </summary>
    public HeadModels Models { get; }

    /// <summary>Off unless the status bar is up: the heads live in the bar, not on the world.</summary>
    public bool Enabled { get; set; }

    /// <summary>Whether the head **models** draw (<see cref="HeadModels.ShowHeads"/>).</summary>
    public bool ShowHeads { get => Models.ShowHeads; set => Models.ShowHeads = value; }

    /// <summary>Whose heads to draw, in status-bar order (<see cref="HeadModels.Party"/>).</summary>
    public IReadOnlyList<WorldThing> Party { get => Models.Party; set => Models.Party = value; }

    /// <summary>Which of the canvases the heads go in (<see cref="HeadModels.Mode"/>).</summary>
    public int Mode { get => Models.Mode; set => Models.Mode = value; }

    /// <summary>One head alone in place of the party's (<see cref="HeadModels.Alone"/>).</summary>
    public WorldThing? Alone { get => Models.Alone; set => Models.Alone = value; }

    /// <summary>True while the large description box is covering the heads (<see cref="HeadModels.Hidden"/>).</summary>
    public bool Hidden { get => Models.Hidden; set => Models.Hidden = value; }

    /// <summary>The clock the heads are animated from, in seconds (<see cref="HeadModels.Seconds"/>).</summary>
    public float Seconds { get => Models.Seconds; set => Models.Seconds = value; }

    /// <summary>The heads' own clock, the interface's (<see cref="HeadModels.Clock"/>).</summary>
    public Func<float?>? Clock { get => Models.Clock; set => Models.Clock = value; }

    /// <param name="styles">The renderer's light styles: the spare its maker drew (<see cref="LightStyles.DrawTheRenderersSpare"/>).</param>
    public HeadsPass(UiCanvas ui, GameFiles files, MaterialCache materials, MotionSets motions, LightStyles styles)
    {
        this.ui = ui;
        Models = new HeadModels(files, materials, motions);
        snapshot = new SceneSnapshot(Models.Mesh, materials, Models.Poses, styles) { Identities = true };
    }

    /// <summary>The viewport's two lights (<see cref="HeadModels.HeadLights"/>).</summary>
    public static ChosenLight[] HeadLights => HeadModels.HeadLights;

    /// <summary>A light at a point, of a colour and a range, as a viewport of its own lights them (<see cref="HeadModels.Light"/>).</summary>
    public static ChosenLight Light(Vector3 at, uint colour, float range) => HeadModels.Light(at, colour, range);

    /// <summary>How many heads have been made — one per member and head model, however many journeys.</summary>
    public int HeadsMade => Models.HeadsMade;

    /// <summary>What a party member's portrait head is playing, or null.</summary>
    public ThingAnimation? Playing(WorldThing who) => Models.Playing(who);

    /// <summary>
    /// The head camera (the engine's camera and its projection),
    /// standing at <see cref="PartyHeads.Eye"/> and turned a half-turn about Z so it looks back
    /// along −Y. <paramref name="width"/> and <paramref name="height"/> are the viewport
    /// rectangle's extents in pixels; the projection is built from them by
    /// <see cref="PartyHeads.Scales"/>, which is the engine's formula.
    /// </summary>
    public static Matrix4x4 ViewProjection(float width = PartyHeads.ViewW, float height = PartyHeads.ViewH, float fov = PartyHeads.Fov)
    {
        // The game's forward axis is **+Y** (it is what a projectile's `PHYSICS_VELOCITY 0, s, 0`
        // means), so a camera at Euler (0,0,180) looks along **−Y** — from y = +17 back at a head
        // standing on the origin. Up is +Z, which a half-turn about Z leaves alone; this tree's own
        // cameras use the same up (`FreeCamera.View`).
        var view = Matrix4x4.CreateLookAt(PartyHeads.Eye, PartyHeads.Eye - Vector3.UnitY, Vector3.UnitZ);
        var (fovY, aspect) = PartyHeads.Camera(width, height, fov);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(fovY, aspect, PartyHeads.Near, PartyHeads.Far);
        return view * projection;
    }

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (!Enabled || Hidden) return;

        // The viewport, in the interface's own 640x480 space scaled to the window — the same
        // letterbox factors the panes use, which is what keeps the heads inside `descLargeSP`'s
        // rectangle at every resolution. 1.1 re-applies it for the current mode at the current
        // screen size the same way.
        // Stretched as 1.1's, fitted (ours) or the 16:9 stretch (ours): the map is the sheet's, whichever.
        var sheet = UiLayout.On(width, height);
        float sx = sheet.ScaleX, sy = sheet.ScaleY;
        var canvas = PartyHeads.Canvas(Mode);
        int vx = (int)(sheet.X + canvas.X * sx), vy = (int)(sheet.Y + canvas.Y * sy);
        int vw = Math.Max(1, (int)(canvas.W * sx)), vh = Math.Max(1, (int)(canvas.H * sy));

        if (!ShowHeads) return;

        // The heads HeadModels.Frame put up this frame, made, animated, posed and lit (before the draw).
        var party = Models.Shown;
        if (party.Count == 0) return;
        snapshot.Fill(Scene, 0f);
        ui.Begin(width, height);
        draws.Clear();
        // An empty mesh draws nothing; its fill is still taken in (every fill is read).
        if (Models.Mesh.IndexCount != 0)
        {
            // The projection is built from the viewport rectangle itself, the way the engine builds it:
            // the 30° is horizontal, the aspect argument the engine passes its camera is never read,
            // and the vertical field of view falls out of the box being 3.6 times wider than it is
            // tall. `PartyHeads.Scales` carries the numbers. The rectangle scales with the window
            // exactly as the canvas scales it — by width/640 and height/480 independently — so at a
            // non-4:3 size the heads grow with the width and the box grows with the height, and less of
            // each head fits. That is the engine's own behaviour, not a compromise here.
            var viewProj = ViewProjection(vw, vh, canvas.Fov);
            foreach (var head in party)
            {
                if (Models.HeadOf(head) is not { } thing) continue;
                // Each head is drawn at the identity with its own offset along x (the light pass's
                // `m[9] += offsetX`), which is the whole of its placement.
                draws.Add(new ModelDraw(snapshot.IdentityOf(thing), Matrix4x4.CreateTranslation(head.OffsetX, 0f, 0f) * viewProj));
            }
        }
        ui.Model(Scene, new PixelRect(vx, vy, vw, vh), System.Runtime.InteropServices.CollectionsMarshal.AsSpan(draws));
    }
}
