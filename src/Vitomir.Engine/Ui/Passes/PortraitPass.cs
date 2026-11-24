// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The character screens' portrait window: the character being made or
// chosen, whole, standing in a little 3D window of its own on `mpPlayerCreate` and `mpPlayerSelect`,
// playing its mode 0x31 and turned round by two held keys. Read from the window's setup,
// the draw hook, the model's reset and the create screen's choice. A model insert
// in the draw list for the reason the party heads are one (`HeadsPass`): the
// engine breaks the 2D stream for it.
using System.Numerics;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Scene;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;
using Vitomir.Platform.Interface;

namespace Vitomir.Engine.Ui;

/// <summary>
/// One character's model in the character screens' portrait window: the window's own model, built, animated, posed and lit
/// here, taken into its snapshot scene and emitted as a **model insert**, which the old renderer's model inserts draw.
/// </summary>
public sealed class PortraitPass : IUiPass
{
    readonly UiCanvas ui;
    readonly GameFiles files;
    readonly SceneMesh mesh = new();
    // Made on first need, so a front end nobody opens the character screens in pays nothing.
    MaterialCache? materials;
    MotionSets? motions;
    TemplateSet? templates;
    /// <summary>The window's scene (the Classic snapshot's): filled here, the interface's pass; the insert's drawer reads only it.</summary>
    ScenePoses? poses;
    SceneSnapshot? snapshot;
    readonly ClassicScene scene = new();
    readonly Dictionary<string, WorldThing> built = new(StringComparer.OrdinalIgnoreCase);
    readonly List<WorldThing> drawn = [];
    WorldThing? showing;
    ulong lastTicks;

    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    /// <summary>The window's clock, in milliseconds (the program's: SDL's ticks); the model's animation runs on it.</summary>
    public Func<ulong> Ticks { get; set; } = () => 0;

    /// <summary>The two held keys that turn the model (the program's keyboard: left, right).</summary>
    public Func<(bool Left, bool Right)> Keys { get; set; } = () => (false, false);

    /// <summary>
    /// The window (the portrait's canvas): left 46, top 43, right 302, bottom 281 of the
    /// 640 × 480 interface space — `mpPlayerCreate.nui`'s own
    /// "character portrait/model window" comment, 256 × 238.
    /// </summary>
    public const int ViewX = 46, ViewY = 43, ViewW = 256, ViewH = 238;
    /// <summary>The camera: a 30° horizontal field of view, near 5, far 1,000 — the heads' own.</summary>
    public const float Fov = 30f;
    /// <summary>Where the camera stands and what it looks at: 280 in front, 100 up, at the chest.</summary>
    public static readonly Vector3 Eye = new(0f, 280f, 100f), LookAt = new(0f, 0f, 64f);
    /// <summary>How fast the held keys turn the model: 90° a second (times the frame's seconds).</summary>
    public const float TurnDegreesPerSecond = 90f;
    /// <summary>The mode the chosen model plays, at thirty frames a second.</summary>
    public const int Mode = 0x31;

    /// <summary>
    /// The window's two lights (two 0x48-byte records): a warm key
    /// up and to the left and a cool fill low to the right. The falloff is the heads' (ours, `HeadsPass`).
    /// </summary>
    public static readonly (Vector3 At, uint Colour, float Range)[] Lights =
    [
        (new(-50f, 50f, 100f), 0xFFFFE2AD, 500f),   // 255, 226, 173
        (new(50f, 50f, 0f), 0xFFBFE2FF, 300f),      // 191, 226, 255
    ];

    /// <summary>
    /// The light styles its renderer is made with, on first need: the spare the old renderer drew from the launch's lights
    /// stream as it was built (<see cref="LightStyles.DrawTheRenderersSpare"/>), which the pass no longer draws itself.
    /// Whoever draws the screen hands it over when <see cref="WantsSpare"/> asks, in the frame the renderer is made.
    /// </summary>
    public LightStyles? Styles { get; set; }

    /// <summary>Whether this frame's draw will make the renderer, and so wants <see cref="Styles"/> (the model's first showing).</summary>
    public bool WantsSpare => snapshot is null && Styles is null && Showing() is { Length: > 0 };

    /// <summary>Which template to show, or null for none — the character screens say (`MpMenus.PortraitTemplate`).</summary>
    public Func<string?> Showing { get; set; } = () => null;

    /// <summary>The model's turn about its own up axis, degrees; back to 0 whenever another model is chosen.</summary>
    public float Turn { get; set; }

    public PortraitPass(UiCanvas ui, GameFiles files)
    {
        this.ui = ui;
        this.files = files;
        var lights = Lights.Select(l => HeadsPass.Light(l.At, l.Colour, l.Range)).ToArray();
        // No level and no lightmaps: the window is lit by its own two lights and nothing else.
        mesh.LightSource = _ => (lights, Vector3.Zero);
    }

    /// <summary>
    /// The projection and view: the heads' formula (`PartyHeads.Camera`) for the window's
    /// extents at 30°, looking from <see cref="Eye"/> at <see cref="LookAt"/> with +Z up.
    /// </summary>
    public static Matrix4x4 ViewProjection(float width, float height)
    {
        var view = Matrix4x4.CreateLookAt(Eye, LookAt, Vector3.UnitZ);
        var (fovY, aspect) = PartyHeads.Camera(width, height, Fov);
        return view * Matrix4x4.CreatePerspectiveFieldOfView(fovY, aspect, 5f, 1000f);
    }

    /// <summary>The model for a template, built once, on its mode 0x31.</summary>
    WorldThing? Model(string template)
    {
        if (built.TryGetValue(template, out var had)) return had;
        var took = System.Diagnostics.Stopwatch.StartNew();
        try { return Build(template); }
        finally { Log?.Invoke($"portrait: {template} made in {took.ElapsedMilliseconds} ms{(snapshotMade ? "" : ", the first (the window's renderer made with it)")}"); snapshotMade = true; }
    }

    /// <summary>Where a model's making is said, with how long it took (ours: a stall in the character screens is then named).</summary>
    public Action<string>? Log { get; set; }
    bool snapshotMade;

    WorldThing? Build(string template)
    {
        templates ??= new TemplateSet(files);
        motions ??= new MotionSets(files);
        materials ??= new MaterialCache(files);
        poses ??= new ScenePoses(mesh);
        snapshot ??= new SceneSnapshot(mesh, materials, poses, Styles
                                       ?? throw new InvalidOperationException("the portrait's renderer needs its light styles (PortraitPass.WantsSpare)"))
                     { Identities = true };
        if (!templates.Has(template)) return null;
        var thing = Location.Build(NsdThing.Placed(template, Vector3.Zero, Vector3.Zero, 0), templates, drawn.Count, drawn.Count + 1);
        built[template] = thing;
        if (!mesh.AddThing(thing, drawn.Count, files, materials, motions)) return thing;
        drawn.Add(thing);
        poses.Things = drawn;
        // The old pass filled and synced its scene here too; this frame's draw fills it next, before anything is drawn, so
        // the one fill carries what this one did (the list's model insert takes one fill a frame).
        thing.Animation?.Play(Mode, motions, onMotion: true);
        return thing;
    }

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        ulong now = Ticks();
        float seconds = lastTicks == 0 ? 0f : Math.Min((now - lastTicks) / 1000f, 0.1f);
        lastTicks = now;
        if (Showing() is not { Length: > 0 } template || Model(template) is not { } thing || snapshot is null) { showing = null; return; }
        if (!ReferenceEquals(thing, showing)) { showing = thing; Turn = 0f; thing.Animation?.Play(Mode, motions!, onMotion: true); }

        // Two held keys turn it. **Ours**: the arrow keys — the engine's key words
        // are the ones its free camera turns by, not read as keys.
        var (left, right) = Keys();
        if (left) Turn += TurnDegreesPerSecond * seconds;
        if (right) Turn -= TurnDegreesPerSecond * seconds;
        // The window's own model, not the game's (the interface's): animated, posed and lit here.
        thing.Animation?.Advance(seconds);
        poses!.Pose();
        snapshot.Fill(scene, 0f);

        // Stretched as 1.1's, fitted (ours) or the 16:9 stretch (ours): the map is the sheet's, whichever.
        // The character sheet's own window, so through the map of the panes above the bar (UiLayout.Panes).
        var sheet = UiLayout.Panes(width, height);
        float sx = sheet.ScaleX, sy = sheet.ScaleY;
        int vx = (int)(sheet.X + ViewX * sx), vy = (int)(sheet.Y + ViewY * sy);
        int vw = Math.Max(1, (int)(ViewW * sx)), vh = Math.Max(1, (int)(ViewH * sy));
        ui.BeginPanes(width, height);
        ReadOnlySpan<ModelDraw> draw = [new(snapshot.IdentityOf(thing), Matrix4x4.CreateRotationZ(Turn * MathF.PI / 180f) * ViewProjection(vw, vh))];
        ui.Model(scene, new PixelRect(vx, vy, vw, vh), draw);
    }
}
