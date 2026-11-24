// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The characters' shadows — the shadow a thing casts from one light,
// with the thing draw's per-thing loop and budget and the surface search behind it.
// Read. This half has no GPU:
// it works out the polygons (points, texture coordinates and shade) and the silhouette pictures;
// `Vitomir.Platform.Render.Classic.ShadowRenderer` draws them in mode 30, `shadow`.
//
//   var shadows = new Shadows();
//   shadows.Settings = ShadowConsole.Live();          // the maxed-out preset, and the console's
//   shadows.Frame(place, mesh, place.Drawn, eye);   // once a frame, after the things are posed
//   shadows.Polys / shadows.Bitmaps                 // what the renderer draws
using System.Numerics;
using Vitomir.Engine.Particles;
using Vitomir.Engine.Scene;
using Vitomir.Engine.Ui;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// The renderer's shadow settings as the shadow draw and the thing draw read them — the live globals
/// for the mode, the detail, the height,
/// the farthest distance, the full-shadow distance and
/// how many a thing casts, in the order the console's <c>shadow</c> command prints
/// them ("Current shadow detail %d %.2f %.2f %.2f %.2f" is mode, detail, height, full,
/// farthest). They come from the detail preset (<see cref="DetailPreset"/>: <c>shadowMode</c>,
/// <c>shadowDetail</c>, <c>shadowHeight</c>, <c>shadowMaxDist</c>, <c>shadowFullDist</c>,
/// <c>maxThingShadows</c>), whose shadow five the shadow index fills. Vitomir draws with
/// the maxed-out preset's (<see cref="Rendered"/>).
/// </summary>
/// <param name="Mode">
/// 0 off, 1 <i>Drop</i> (a round blob under every caster), 2 <i>Rough texture</i> (the stock
/// silhouette cast from a light), 3 <i>Full texture</i> (the caster's own silhouette, drawn every
/// frame), 4 <i>Full texture (HW)</i> and 5 <i>Full poly</i> — the console's own names for them.
/// 4 and 5 are named and never drawn: the shadow draw draws nothing for them.
/// </param>
/// <param name="Detail">The level of detail of the model the full silhouette is drawn from. **Not used by Vitomir**, which draws the silhouette from the whole model.</param>
/// <param name="Height">How far the surface search reaches, as a multiple of the caster's height.</param>
/// <param name="MaxDist">No shadow for a thing farther than this from the eye.</param>
/// <param name="FullDist">Beyond this a full-texture shadow is drawn as the rough one.</param>
/// <param name="PerThing">How many shadows a thing casts at most (the console's <c>numshadows</c>, at most 10).</param>
public readonly record struct ShadowSettings(int Mode, float Detail, float Height, float MaxDist, float FullDist, int PerThing)
{
    /// <summary>A preset's shadow settings.</summary>
    public static ShadowSettings From(DetailPreset p) =>
        new(p.ShadowMode, p.ShadowDetail, p.ShadowHeight, p.ShadowMaxDist, p.ShadowFullDist, p.MaxThingShadows);

    /// <summary>
    /// The settings Vitomir draws with: <see cref="DetailPresets.Maxed"/>'s — the shadow index 9 (full
    /// texture, detail 1.0, height 4, full to 3,000, farthest 6,000) and four shadows a thing —
    /// whatever the detail level, <c>[renderCustom]</c> or the Advanced screen's <i>shadow detail</i>
    /// and <i>shadows</i> bars say (the screens are inert in Vitomir). In 1.1
    /// the level's preset decides, and for a touched Custom the read-back puts the
    /// <i>shadows</i> bar in <c>maxThingShadows</c> and the <i>shadow detail</i> bar
    /// in the shadow index, then fills the shadow five from that index (the shadow helper,
    /// <see cref="DetailPresets.WithShadow"/>).
    /// </summary>
    public static ShadowSettings Rendered => From(DetailPresets.Maxed);
}

/// <summary>
/// What the console has set of the shadow settings: <c>shadow mode detail height full far</c>
/// and <c>numshadows n</c> write the live globals straight. **Ours:** 1.1 keeps
/// them until the render options are applied again; here they last until the game ends.
/// </summary>
public static class ShadowConsole
{
    /// <summary>The five <c>shadow</c> set, or null until it is used.</summary>
    public static (int Mode, float Detail, float Height, float FullDist, float MaxDist)? Detail { get; set; }
    /// <summary>What <c>numshadows</c> set (at most 10), or null until it is used.</summary>
    public static int? PerThing { get; set; }

    /// <summary>The settings in force: <see cref="ShadowSettings.Rendered"/>, with what the console set laid over them.</summary>
    public static ShadowSettings Live()
    {
        var s = ShadowSettings.Rendered;
        if (Detail is { } d) s = s with { Mode = d.Mode, Detail = d.Detail, Height = d.Height, FullDist = d.FullDist, MaxDist = d.MaxDist };
        if (PerThing is int n) s = s with { PerThing = n };
        return s;
    }
}

/// <summary>What the shadow draw draws for one thing and one light.</summary>
public enum ShadowKind
{
    /// <summary>Nothing.</summary>
    None = 0,
    /// <summary>The round blob under the thing, lit by nothing (<c>shadowblob</c>).</summary>
    Blob = 1,
    /// <summary>The stock silhouette laid along the light (<c>silshadow</c>).</summary>
    Rough = 2,
    /// <summary>The thing's own silhouette, drawn into a 128×128 picture this frame.</summary>
    Full = 3,
    /// <summary>A full shadow with the mode set to 4: named, never drawn.</summary>
    FullHardware = 4,
    /// <summary>A full shadow with the mode set to 5 or more: never drawn.</summary>
    FullPoly = 5,
}

/// <summary>
/// One piece of a shadow: the shadow's shape clipped to one level surface, lying 0.3 in front of it,
/// with its texture coordinates and a shade (0–255) at each point — what the shadow draw hands the renderer
/// entry with mode 30 and colours (shade, shade, shade, 255).
/// </summary>
public sealed class ShadowPoly
{
    public Vector3[] Points { get; init; } = [];
    public Vector2[] Uvs { get; init; } = [];
    public float[] Shade { get; init; } = [];
    public ShadowTexture Texture { get; init; }
    /// <summary>For <see cref="ShadowTexture.Bitmap"/>: which of <see cref="Shadows.Bitmaps"/>.</summary>
    public int Bitmap { get; init; }
    public int Sector { get; init; }
    public int Surface { get; init; }
}

/// <summary>A surface the shadow search found (a 24-byte result): the surface, the search point's foot on its plane, and how far in front of it the point is.</summary>
public readonly record struct ShadowHit(int Sector, int Surface, Vector3 Foot, float Distance);

/// <summary>
/// **The shadows of one place, worked out once a frame.** For every thing drawn (the thing draw, after its
/// model): with <c>maxThingShadows</c> not 0, the budget is reset  and
/// the thing's shadow is cast from its lights — the ones the thing-light picker chose for it
/// (<see cref="ModelLight.Pick"/>, sorted: every light that is not a sun, strongest first, then the
/// suns), one at a time, until <c>maxThingShadows</c> of them have answered "done" or the lights run
/// out; or, when the picker chose none, **once from a light made up for it**
/// (<see cref="Invented"/>). A light answers "done" (the shadow draw returns 1) for everything but a shadow
/// too faint to draw — so a thing out of range, or one whose mode draws nothing, uses its turn up.
/// </summary>
public sealed class Shadows
{
    // ---- the numbers, each where 1.1 uses it -------------------------------------------------

    /// <summary>The budget: the most shade a thing's shadows from the sun may add up to (150).</summary>
    public const float Budget = 150f;
    /// <summary>A shade is at most this (255).</summary>
    public const float Full = 255f;
    /// <summary>A shadow fainter than this is not drawn, and the light is passed over (2).</summary>
    public const float Faintest = 2f;
    /// <summary>The far end of a cast shadow is this share of its near end's shade (1/3).</summary>
    public const float FarShare = 0.33333334f;
    /// <summary>The sun is put this far back up its own beam from the thing (32).</summary>
    public const float SunBack = 32f;
    /// <summary>The search and the blob start this far above the thing's position (4).</summary>
    public const float Lift = 4f;
    /// <summary>A shadow lies this far in front of its surface (0.3).</summary>
    public const float Hover = 0.3f;
    /// <summary>The blob's radius, as a multiple of the thing's (1.25).</summary>
    public const float BlobScale = 1.25f;
    /// <summary>The rough shadow's width, as a multiple of the thing's radius (2.5).</summary>
    public const float RoughWidth = 2.5f;
    /// <summary>The rough shadow's rectangle starts this far below the thing's axis and ends this far short of its height (−8).</summary>
    public const float RoughDrop = 8f;
    /// <summary>The full shadow's width, as a multiple of the thing's radius (4).</summary>
    public const float FullWidth = 4f;
    /// <summary>The full shadow's height, as a multiple of the thing's (1.5).</summary>
    public const float FullTall = 1.5f;
    /// <summary>A corner closer to a plane than this is on it (0.00001).</summary>
    public const float OnPlane = 0.00001f;
    /// <summary>A corner farther from a plane than this, or projected farther, leaves the surface out (10, 000).</summary>
    public const float Farthest = 10000f;
    /// <summary>The most surfaces one search answers (<c>numHits &lt;= SHADOW_MAXSURFACES</c>, 0x800).</summary>
    public const int MaxSurfaces = 0x800;
    /// <summary>The full-shadow picture's size (128×128).</summary>
    public const int BitmapSize = 128;

    /// <summary>The made-up light's offset from the thing (−50 and +50).</summary>
    public static readonly Vector3 InventedOffset = new(-50f, 50f, 100f);
    /// <summary>The made-up light's range (350).</summary>
    public const float InventedRange = 350f;

    // ---- the frame's result --------------------------------------------------------------------

    /// <summary>The settings in force (<see cref="ShadowConsole.Live"/>).</summary>
    public ShadowSettings Settings { get; set; } = ShadowSettings.Rendered;

    /// <summary>
    /// Whether a sector was drawn this frame — the portal walk's answer (<see cref="PortalView.Sees"/>)
    /// — or null for every sector. 1.1 casts no shadow for a thing whose sector was not drawn
    /// (the sector's stamp against the frame's), lays none on a surface not drawn
    /// and floods into no sector not drawn. **Ours:** a surface counts as drawn
    /// when its sector was.
    /// </summary>
    public Func<int, bool>? SectorDrawn { get; set; }

    /// <summary>How many lights the picker keeps for a thing (<c>maxThingLights</c>).</summary>
    public int MaxLights { get; set; } = DetailPresets.Maxed.MaxThingLights;

    /// <summary>This frame's pieces of shadow.</summary>
    public List<ShadowPoly> Polys { get; } = [];

    /// <summary>
    /// This frame's full-shadow pictures, <see cref="BitmapSize"/> square, a byte a texel (255 where
    /// the silhouette is, 0 elsewhere), the first <see cref="BitmapCount"/> in use. 1.1 has one
    /// picture and draws each caster's pieces as soon as it has drawn its silhouette into it; a
    /// frame drawn all at once needs one a caster (**ours**).
    /// </summary>
    public List<byte[]> Bitmaps { get; } = [];
    public int BitmapCount { get; private set; }

    /// <summary>How many shadows were cast (a light that drew something) last frame, for the diagnostics.</summary>
    public int Cast { get; private set; }

    /// <summary>The shade the current thing's shadows have laid down so far.</summary>
    public float Spent { get; private set; }

    readonly ModelLightPicker picker = new();
    readonly List<ShadowHit> hits = [];
    readonly Dictionary<int, AnimatedThing> animated = [];
    readonly MeshSilhouette silhouette = new();
    readonly Scratch scratch = new();
    readonly List<Picture> pending = [];
    bool deferring;
    bool mirrorFrame;

    /// <summary>A full shadow's picture still to be drawn this frame.</summary>
    readonly record struct Picture(IShadowModel Model, int Bitmap, Matrix4x4 Light, float Width, float Tall, float Radius);

    /// <summary>
    /// One frame: every drawn thing's shadows. <paramref name="drawn"/> is what the mesh's thing
    /// indices resolve against (<see cref="Location.Drawn"/>); <paramref name="eye"/> is the camera.
    /// Call after the animating things have been posed for this frame. **Ours:** a thing is picked
    /// lights as prelit when its model flags say <c>PRE LIT</c>, whether or not it has baked colours;
    /// and a thing of type 3, which the thing draw gives no picking of its own (its shadow reads the last
    /// thing's count), is picked for like any other.
    ///
    /// <paramref name="mirror"/>: the reflection pass's own frame. 1.1's mirror draws its things
    /// through the same thing draw with bit 1, and so casts their shadows again, onto what
    /// the mirror shows, measured from the reflected eye (the camera the mirror draw has put in
    /// place, which the shadow draw reads): the mirror's rule for which things
    /// (<see cref="Casts(WorldThing, bool)"/>) — a <c>REFLECTION ONLY</c> body casts in the mirror,
    /// a <c>NO REFLECTION</c> thing does not.
    /// </summary>
    public void Frame(Location place, SceneMesh? mesh, IReadOnlyList<WorldThing> drawn, Vector3 eye, bool mirror)
    {
        Polys.Clear();
        pending.Clear();
        mirrorFrame = mirror;
        BitmapCount = 0;
        Cast = 0;
        if (Settings.PerThing == 0 || Settings.Mode == 0 || place.Level is not { } level) return;
        animated.Clear();
        if (mesh is not null)
        {
            int count = mesh.Animated.Count;
            for (int i = 0; i < count; i++) animated[mesh.Animated[i].Thing] = mesh.Animated[i];
        }
        for (int i = 0; i < drawn.Count; i++)
        {
            var thing = drawn[i];
            if (thing is null || !Casts(thing, mirror)) continue;
            if (Unseen?.Invoke(thing) == true) continue;           // Not drawn at all
            // Nothing to cast here whatever its lights: the picking is skipped (it changes nothing).
            if (AtDistance(Kind(thing.RenderFlags, Settings.Mode), Vector3.Distance(thing.Position, eye), Settings) == ShadowKind.None) continue;
            if (SectorDrawn is { } drawnHere && !drawnHere((int)thing.Sector)) continue;
            ModelLight.Pick(picker, MaxLights, thing, level, place.Styles, place.Lights,
                            prelit: (thing.ModelFlags & ModelLight.PreLit) != 0);
            IShadowModel? model = mesh is null ? null : silhouette.For(place, mesh, i, thing, animated.GetValueOrDefault(i));
            deferring = true;
            ThingShadows(level, thing, picker.Lights, eye, model);
            deferring = false;
        }
        // The pictures, each on its own: 1.1 draws one and lays it before drawing the next; they do
        // not touch one another, so here they are drawn side by side once the frame's pieces are known.
        if (pending.Count > 1) Parallel.ForEach(pending, j => j.Model.Draw(Bitmaps[j.Bitmap], j.Light, j.Width, j.Tall, j.Radius));
        else foreach (var j in pending) j.Model.Draw(Bitmaps[j.Bitmap], j.Light, j.Width, j.Tall, j.Radius);
        pending.Clear();
    }

    /// <summary>The ordinary pass's frame (<see cref="Frame(Location, SceneMesh?, IReadOnlyList{WorldThing}, Vector3, bool)"/>).</summary>
    public void Frame(Location place, SceneMesh? mesh, IReadOnlyList<WorldThing> drawn, Vector3 eye) =>
        Frame(place, mesh, drawn, eye, mirror: false);

    /// <summary>
    /// The stealth test on the way to the shadow (<see cref="StealthFade"/>): true for a
    /// thing the viewer cannot make out, which the thing draw leaves undrawn — no model, no shadow. Null
    /// for none. A half-seen thing is drawn faded and casts its full shadow: the fade is the model's
    /// the fade, and the sorted pass's test is the alpha.
    /// </summary>
    public Func<WorldThing, bool>? Unseen { get; set; }

    /// <summary>
    /// Whether the thing draw reaches the shadow for a thing: it is in the world and drawn (not
    /// <c>DON'T RENDER</c>, not <c>REFLECTION ONLY</c> in the ordinary pass), and it is not one of the
    /// things kept back for the sorted pass — <c>SORT RENDER</c> (0x40) or a model alpha under 1
    /// (alpha <c>&lt; 1.0</c>), a fading ghost's for one — which cast none. Whether it
    /// casts anything is then its <c>DROP SHADOW</c>/<c>FULL SHADOW</c> flags (<see cref="Kind"/>).
    /// In the mirror (<paramref name="mirror"/>, the thing draw with bit 1) the render flags' rule is the
    /// mirror's: <c>DON'T RENDER</c> still out, <c>REFLECTION ONLY</c> in, and a thing flagged
    /// <c>NO REFLECTION</c> (bit 0x10) out. The stealth test is
    /// <see cref="Unseen"/>.
    /// </summary>
    public static bool Casts(WorldThing thing, bool mirror = false) =>
        thing.InWorld
        && (mirror ? (thing.RenderFlags & 0x2u) == 0 && (thing.Flags & WorldThing.Flag.NoReflection) == 0 : !thing.ModelHidden)
        && (thing.RenderFlags & 0x40) == 0 && !(thing.ModelAlpha < 1f)
        && (thing.RenderFlags & (DropShadow | FullShadow)) != 0;

    /// <summary><c>THING_RENDERFLAG_DROPSHADOW</c> and <c>THING_RENDERFLAG_FULLSHADOW</c>.</summary>
    public const uint DropShadow = 0x4, FullShadow = 0x8;

    /// <summary>
    /// The thing draw's shadow loop for one thing: the budget reset, then its lights in the
    /// picker's order until <see cref="ShadowSettings.PerThing"/> of them are done — or the made-up
    /// light when there are none.
    /// </summary>
    public void ThingShadows(NilLevel level, WorldThing thing, ReadOnlySpan<PickedLight> lights, Vector3 eye, IShadowModel? model = null)
    {
        if (Settings.PerThing == 0) return;
        Spent = 0f;
        int most = Math.Min(Settings.PerThing, lights.Length);
        if (most == 0)
        {
            One(level, thing, Invented(thing.Position), eye, model);
            return;
        }
        int done = 0;
        for (int i = 0; i < lights.Length && done < most; i++)
            if (One(level, thing, lights[i], eye, model)) done++;
    }

    /// <summary>
    /// **The light the thing draw makes up** for a thing the picker found none for: a white point light of
    /// intensity 400 and range 350, falloff 1, at the thing offset by (−50, +50, +100). It is only
    /// ever handed to the shadow; it lights nothing.
    /// </summary>
    public static PickedLight Invented(Vector3 at) =>
        new(0, new Vector3(255f), at + InventedOffset, Vector3.Zero, 400f, 1f, InventedRange, 0f, 0, "invented");

    /// <summary>
    /// What the shadow draw draws for a thing with these render flags under a mode: a <c>FULL SHADOW</c>
    /// thing asks for 5 and a <c>DROP SHADOW</c> one for 2; then mode 1 makes every one a blob,
    /// mode 2 every one rough, mode 3 turns a full one's 5 into 3 and mode 4 into 4; any other mode
    /// leaves it as asked. Mode 0 or neither flag: nothing.
    /// </summary>
    public static ShadowKind Kind(uint renderFlags, int mode)
    {
        if (mode == 0 || (renderFlags & (DropShadow | FullShadow)) == 0) return ShadowKind.None;
        var asked = (renderFlags & FullShadow) != 0 ? ShadowKind.FullPoly : ShadowKind.Rough;
        return mode switch
        {
            1 => ShadowKind.Blob,
            2 => ShadowKind.Rough,
            3 => asked == ShadowKind.FullPoly ? ShadowKind.Full : asked,
            4 => asked == ShadowKind.FullPoly ? ShadowKind.FullHardware : asked,
            _ => asked,
        };
    }

    /// <summary>
    /// The distance rule (the shadow draw's head): nothing beyond <see cref="ShadowSettings.MaxDist"/> of the
    /// eye, and a full shadow (3, 4 or 5) beyond <see cref="ShadowSettings.FullDist"/> is drawn rough.
    /// </summary>
    public static ShadowKind AtDistance(ShadowKind kind, float distance, ShadowSettings s)
    {
        if (distance > s.MaxDist) return ShadowKind.None;
        if (distance > s.FullDist && kind is ShadowKind.Full or ShadowKind.FullHardware or ShadowKind.FullPoly)
            return ShadowKind.Rough;
        return kind;
    }

    /// <summary>
    /// How tall a thing counts as: its height for a cylinder, else its radius field (read
    /// whatever the body — <c>collide == 2 ? height: radius</c>).
    /// </summary>
    public static float Tall(WorldThing thing) => thing.Collide == CollideShape.Cylinder ? thing.Height : thing.Radius;

    /// <summary>A thing's radius as the engine answers it: 0 for a thing with no body.</summary>
    public static float Wide(WorldThing thing) => thing.Collide != CollideShape.None ? thing.Radius : 0f;

    /// <summary>
    /// Where the shadow is cast from: for the sun (type 2) a point <see cref="SunBack"/> back up its
    /// beam from the thing; for any other light its own position, raised to at least the top of the
    /// thing (<see cref="Tall"/>) so the shadow never points up the wall behind a light at the feet.
    /// </summary>
    public static Vector3 From(WorldThing thing, in PickedLight light)
    {
        if (light.Type == 2) return thing.Position - light.Direction * SunBack;
        var at = light.Position;
        float top = thing.Position.Z + Tall(thing);
        if (at.Z < top) at.Z = top;
        return at;
    }

    /// <summary>
    /// The shade a cast shadow is drawn with (0 to 255), from the light it is cast from and how far
    /// away that is (<paramref name="distance"/>, from <see cref="From"/> to the thing): from the sun,
    /// what is left of the <see cref="Budget"/>; from any other light
    /// <c>255 × (1 − (distance − radius) / range)</c>, the fraction clamped to 0..1 — so full at the
    /// light and gone at its range. A shade under <see cref="Faintest"/> is not drawn (and the light is
    /// passed over: the shadow draw answers 0).
    /// </summary>
    public static float Shade(WorldThing thing, in PickedLight light, float distance, float spent)
    {
        if (light.Type == 2)
        {
            float left = Budget - spent;
            if (left < 0f) return -1f;
            return left > Full ? Full : left;
        }
        float r = thing.Collide != CollideShape.None ? thing.Radius : 0f;
        float t = (distance - r) / light.Range;
        if (!(t >= 0f)) t = 0f;
        else if (!(t <= 1f)) t = 1f;
        return (1f - t) * Full;
    }

    /// <summary>
    /// **The shadow draw**: the shadow a thing casts from one light. True when it is done with — drawn, or
    /// nothing to draw — and false only when the shadow was too faint, which sends the thing draw on to the
    /// thing's next light. The budget grows by the shade of every cast shadow that found a surface.
    /// </summary>
    public bool One(NilLevel level, WorldThing thing, in PickedLight light, Vector3 eye, IShadowModel? model = null)
    {
        var kind = Kind(thing.RenderFlags, Settings.Mode);
        if (kind == ShadowKind.None) return true;
        kind = AtDistance(kind, Vector3.Distance(thing.Position, eye), Settings);
        if (kind == ShadowKind.None) return true;
        // Nothing for a thing whose sector was not drawn this frame (its stamp against the frame's).
        if (thing.Sector < 0 || thing.Sector >= level.Sectors.Length) return true;
        if (SectorDrawn is { } drawn && !drawn((int)thing.Sector)) return true;
        var from = From(thing, light);
        if (kind == ShadowKind.Blob) { Blob(level, thing); return true; }
        if (kind is ShadowKind.FullHardware or ShadowKind.FullPoly) return true;
        if (kind == ShadowKind.Full && model is null) return true;          // No model, no shadow

        var dir = thing.Position - from;
        float distance = dir.Length();
        if (distance != 0f) dir /= distance;
        float shade = Shade(thing, light, distance, Spent);
        if (shade < Faintest) return false;
        float near = shade * FarShare;
        var centre = thing.Position + new Vector3(0f, 0f, Lift);
        float tall = Tall(thing);
        hits.Clear();
        FindSurfaces(level, (int)thing.Sector, centre, dir, Settings.Height * tall, hits, SectorDrawn, mirrorFrame ? NilGroup.FlagReflect : 0u);
        if (hits.Count == 0) return true;

        var frame = BeamEmitter.Basis(dir);
        frame.Translation = from;
        Span<Vector3> corners = stackalloc Vector3[4];
        Span<Vector2> uvs = stackalloc Vector2[4];
        Span<float> shades = stackalloc float[4];
        float r = Wide(thing);
        var texture = ShadowTexture.Silhouette;
        int bitmap = 0;
        if (kind == ShadowKind.Rough)
        {
            // A rectangle across the beam at the light: 2.5 radii wide, from 8 below the
            // thing's axis to 8 short of its height; its near (feet) end at the full shade.
            float w = r * RoughWidth, half = w * 0.5f, top = tall - RoughDrop;
            corners[0] = new(-half, 0f, top); corners[1] = new(-half, 0f, -RoughDrop);
            corners[2] = new(half, 0f, -RoughDrop); corners[3] = new(half, 0f, top);
            uvs[0] = new(0f, 1f); uvs[1] = new(0f, 0f); uvs[2] = new(1f, 0f); uvs[3] = new(1f, 1f);
            shades[0] = near; shades[1] = shade; shades[2] = shade; shades[3] = near;
        }
        else
        {
            // The picture's own rectangle: four radii wide, one and a half heights tall
            // from a radius below the axis, and a width back up the beam.
            float w = r * FullWidth, half = w * 0.5f, t = tall * FullTall;
            corners[0] = new(-half, -w, t - r); corners[1] = new(-half, -w, -r);
            corners[2] = new(half, -w, -r); corners[3] = new(half, -w, t - r);
            uvs[0] = new(0f, 0f); uvs[1] = new(0f, 1f); uvs[2] = new(1f, 1f); uvs[3] = new(1f, 0f);
            shades[0] = near; shades[1] = shade; shades[2] = shade; shades[3] = near;
            texture = ShadowTexture.Bitmap;
            bitmap = NextBitmap();
            if (deferring) pending.Add(new Picture(model!, bitmap, frame, w, t, r));
            else model!.Draw(Bitmaps[bitmap], frame, w, t, r);
        }
        for (int i = 0; i < 4; i++) corners[i] = Vector3.Transform(corners[i], frame);
        foreach (var hit in hits)
            if (Lay(level, hit, dir, corners, uvs, shades, texture, bitmap, scratch) is { } poly) Polys.Add(poly);
        Spent += shade;
        Cast++;
        return true;
    }

    int NextBitmap()
    {
        if (BitmapCount == Bitmaps.Count) Bitmaps.Add(new byte[BitmapSize * BitmapSize]);
        return BitmapCount++;
    }

    /// <summary>
    /// **Mode 1, the blob**: no light at all. A circle 1.25 radii across, 4 above
    /// the thing's position, laid on every level surface that sphere touches (the sphere's surface query, as the light
    /// blobs find theirs, <see cref="Explosions.DynamicLights.Gather"/>): a square the width of the
    /// circle the sphere cuts in the plane, clipped to the polygon, at full white, with
    /// <c>shadowblob</c>, only on a surface whose plane the sphere's walk down its sector's tree
    /// marks (<see cref="SphereTree"/>, none on a Detail surface). The budget is not touched. **Ours in method:** the flag 0x40 the query is
    /// given is not read; the surfaces are found as <see cref="Explosions.DynamicLights.Gather"/> finds them.
    /// </summary>
    public void Blob(NilLevel level, WorldThing thing)
    {
        float r = Wide(thing) * BlobScale;
        if (!(r > 0f)) return;
        var c = thing.Position + new Vector3(0f, 0f, Lift);
        foreach (int si in Flood(level, (int)thing.Sector, c, r))
        {
            if (SectorDrawn is { } drawn && !drawn(si)) continue;
            var s = level.Sectors[si];
            // Only the planes the sphere's walk down the sector's tree marks (SphereTree.cs).
            if (SphereTree.Marked(s, c, r) is not { } marked) continue;
            for (int fi = 0; fi < s.Surfaces.Length; fi++)
            {
                var surface = s.Surfaces[fi];
                if (surface.NumVerts < 3 || !marked.Contains(surface.Plane)
                    || (s.Groups[surface.Group].SurfaceFlags & NilGroup.FlagNodraw) != 0) continue;
                var plane = s.SurfacePlanes[surface.Plane];
                float d = Vector3.Dot(plane.Normal, c) - plane.Dist;
                if (d > r || d < Explosions.DynamicLights.BehindTolerance) continue;
                var foot = c - d * plane.Normal;
                float half = MathF.Sqrt(r * r - d * d);
                if (!Explosions.DynamicLights.Touches(s, surface, plane, foot, half)) continue;
                var onto = BeamEmitter.Basis(plane.Normal);
                var back = Matrix4x4.Transpose(onto);
                var f = Vector3.Transform(foot, back);
                var pts = new Vector3[Explosions.Decals.MaxPoints + 1];
                var uvs = new Vector2[Explosions.Decals.MaxPoints + 1];
                pts[0] = new(f.X - half, 0f, f.Z + half); pts[1] = new(f.X + half, 0f, f.Z + half);
                pts[2] = new(f.X + half, 0f, f.Z - half); pts[3] = new(f.X - half, 0f, f.Z - half);
                uvs[0] = new(0f, 1f); uvs[1] = new(0f, 0f); uvs[2] = new(1f, 0f); uvs[3] = new(1f, 1f);
                var poly = Polygon(s, surface, back);
                int n = Explosions.Decals.Clip(pts, uvs, 4, poly);
                if (n < 3) continue;
                float y = poly[0].Y + Hover;
                var world = new Vector3[n];
                var shade = new float[n];
                for (int i = 0; i < n; i++) { world[i] = Vector3.Transform(new Vector3(pts[i].X, y, pts[i].Z), onto); shade[i] = Full; }
                Polys.Add(new ShadowPoly { Points = world, Uvs = uvs[..n], Shade = shade, Texture = ShadowTexture.Blob, Sector = si, Surface = fi });
            }
        }
    }

    /// <summary>A surface's polygon in a frame (its points through <paramref name="into"/>).</summary>
    static Vector3[] Polygon(NilSector s, NilSurface surface, Matrix4x4 into)
    {
        var poly = new Vector3[surface.NumVerts];
        for (int i = 0; i < poly.Length; i++)
            poly[i] = Vector3.Transform(s.Vertices[s.SectorVertices[surface.FirstVertex + i].Vertex], into);
        return poly;
    }

    /// <summary>The sectors a sphere's portal flood reaches (as <see cref="Bodies.SectorsNear"/> tests a portal).</summary>
    static List<int> Flood(NilLevel level, int start, Vector3 at, float radius)
    {
        var found = new List<int> { start };
        var seen = new HashSet<int> { start };
        for (int i = 0; i < found.Count; i++)
            foreach (var portal in level.Sectors[found[i]].Portals)
            {
                int next = (int)portal.NeighbourSector;
                if (next < 0 || next >= level.Sectors.Length || portal.Quad.Length == 0 || seen.Contains(next)) continue;
                float d = Vector3.Dot(at - portal.Quad[0], portal.Normal);
                if (d < 0f || d >= radius) continue;
                seen.Add(next);
                found.Add(next);
            }
        return found;
    }

    /// <summary>
    /// **The surfaces a shadow falls on — the shadow's surface search.** From the thing's sector, every level
    /// surface drawn this frame (the surface's stamp against the frame's — here a surface
    /// not <c>nodraw</c>, which is never drawn, in a sector <paramref name="drawn"/> says was; every
    /// sector when it is null) that
    /// <list type="bullet">
    /// <item>faces against the beam (its normal · <paramref name="dir"/> ≤ 0),</item>
    /// <item>has <paramref name="centre"/> in front of its plane by no more than <paramref name="radius"/>,</item>
    /// <item>and has the centre's foot on the plane inside the polygon or no more than the radius
    /// outside each of its edges — measured on the two axes the plane's normal leaves when its
    /// largest one is dropped (so on a slope the allowance is the flattened one), the polygon wound
    /// counter-clockwise seen from the front (by the engine's axis table);</item>
    /// </list>
    /// then on through each portal of the sector into a neighbour not yet searched and drawn this
    /// frame, whose portal plane the centre is in front of by less than the radius — depth first, in
    /// portal order. At most <see cref="MaxSurfaces"/>. Answers how many it found.
    /// <paramref name="skipGroups"/>: group flags whose surfaces are not drawn in this pass besides
    /// <c>nodraw</c> — the mirror's frame leaves out the Reflect groups, which the renderer does not
    /// draw in the mirror (**ours in reading**: that an undrawn Reflect surface is not stamped
    /// drawn is inferred, not read in the renderer).
    /// </summary>
    public static int FindSurfaces(NilLevel level, int sector, Vector3 centre, Vector3 dir, float radius, List<ShadowHit> into,
                                   Func<int, bool>? drawn = null, uint skipGroups = 0)
    {
        if (!(radius > 0f) || sector < 0 || sector >= level.Sectors.Length) return 0;
        int before = into.Count;
        var seen = new HashSet<int>();
        Search(level, sector, centre, dir, radius, into, seen, drawn, skipGroups | NilGroup.FlagNodraw);
        return into.Count - before;
    }

    static void Search(NilLevel level, int si, Vector3 c, Vector3 dir, float radius, List<ShadowHit> into, HashSet<int> seen,
                       Func<int, bool>? drawn, uint skipGroups)
    {
        seen.Add(si);
        var s = level.Sectors[si];
        int count = drawn is null || drawn(si) ? s.Surfaces.Length : 0;
        for (int fi = 0; fi < count; fi++)
        {
            if (into.Count >= MaxSurfaces) return;
            var surface = s.Surfaces[fi];
            if (surface.NumVerts < 3 || (s.Groups[surface.Group].SurfaceFlags & skipGroups) != 0) continue;
            var plane = s.SurfacePlanes[surface.Plane];
            var n = plane.Normal;
            if (Vector3.Dot(n, dir) > 0f) continue;
            float d = Vector3.Dot(c, n) - plane.Dist;
            if (!(d <= radius) || !(d >= 0f)) continue;
            var foot = c - d * n;
            if (!Within(s, surface, n, foot, radius)) continue;
            into.Add(new ShadowHit(si, fi, foot, d));
        }
        foreach (var portal in s.Portals)
        {
            int next = (int)portal.NeighbourSector;
            if (next < 0 || next >= level.Sectors.Length || seen.Contains(next) || portal.Quad.Length == 0) continue;
            if (drawn is not null && !drawn(next)) continue;
            float d = Vector3.Dot(c - portal.Quad[0], portal.Normal);
            if (!(d >= 0f) || !(d < radius)) continue;
            Search(level, next, c, dir, radius, into, seen, drawn, skipGroups);
            if (into.Count >= MaxSurfaces) return;
        }
    }

    /// <summary>
    /// The search's edge test: the axes (a, b) are the two the normal's largest component leaves, taken
    /// from the axis table (x: z, y; y: x, z; z: y, x — swapped when that component is not
    /// positive); an edge from v to w has the point on its inside when
    /// <c>e.b (p.a − v.a) − (p.b − v.b) e.a ≥ 0</c>, and otherwise, with (e.a, e.b) made a unit, when that
    /// is at least −radius.
    /// </summary>
    public static bool Within(NilSector s, NilSurface surface, Vector3 n, Vector3 p, float radius)
    {
        float ax = MathF.Abs(n.X), ay = MathF.Abs(n.Y), az = MathF.Abs(n.Z);
        int drop = ax <= ay ? (ay <= az ? 2 : 1) : (ax <= az ? 2 : 0);
        (int a, int b) = drop switch { 0 => (2, 1), 1 => (0, 2), _ => (1, 0) };
        if (!(Get(n, drop) > 0f)) (a, b) = (b, a);
        int count = surface.NumVerts;
        for (int i = 0; i < count; i++)
        {
            var v = s.Vertices[s.SectorVertices[surface.FirstVertex + i].Vertex];
            var w = s.Vertices[s.SectorVertices[surface.FirstVertex + (i + 1) % count].Vertex];
            float ea = Get(w, a) - Get(v, a), eb = Get(w, b) - Get(v, b);
            float pa = Get(p, a) - Get(v, a), pb = Get(p, b) - Get(v, b);
            float cross = eb * pa - pb * ea;
            if (cross >= 0f) continue;
            float len = MathF.Sqrt(ea * ea + eb * eb);
            if (len != 0f) { ea /= len; eb /= len; }
            if (eb * pa - pb * ea < -radius) return false;
        }
        return true;
    }

    static float Get(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    /// <summary>
    /// A cast shadow's rectangle laid on one surface:
    /// each corner carried along the beam onto the plane — a corner already on it stays, one behind
    /// it or more than <see cref="Farthest"/> in front leaves the surface out, and so does a beam that
    /// does not run into the plane — then, in the plane's own frame (built on its normal), clipped
    /// to the polygon on X and Z carrying texture coordinates and shade, every point put
    /// <see cref="Hover"/> in front of the plane and taken back into the world. Null when fewer than
    /// three points are left.
    /// </summary>
    public static ShadowPoly? Lay(NilLevel level, ShadowHit hit, Vector3 dir, ReadOnlySpan<Vector3> corners,
                                 ReadOnlySpan<Vector2> uvs, ReadOnlySpan<float> shades, ShadowTexture texture, int bitmap,
                                 Scratch? scratch = null)
    {
        scratch ??= new Scratch();
        var s = level.Sectors[hit.Sector];
        var surface = s.Surfaces[hit.Surface];
        var plane = s.SurfacePlanes[surface.Plane];
        var n = plane.Normal;
        var onto = BeamEmitter.Basis(n);
        var back = Matrix4x4.Transpose(onto);
        var pts = scratch.Points; var tex = scratch.Uvs; var shade = scratch.Shade;
        float into = -Vector3.Dot(dir, n);
        for (int i = 0; i < 4; i++)
        {
            float d = Vector3.Dot(corners[i], n) - plane.Dist;
            if (MathF.Abs(d) <= OnPlane) d = 0f;
            else if (d < 0f || d > Farthest) return null;
            if (!(into > 0f)) return null;
            float t;
            if (d <= 0f) t = 0f;
            else
            {
                if (into * Farthest < d) return null;
                t = d / into;
                if (t < 0f) t = 0f;
            }
            pts[i] = Vector3.Transform(corners[i] + t * dir, back);
            tex[i] = uvs[i];
            shade[i] = shades[i];
        }
        // The surface's polygon in the plane's frame, in the scratch's own room (a frame tried
        // hundreds of surfaces and made an array for each, most of them only to be turned away below).
        int sides = surface.NumVerts;
        if (scratch.Poly.Length < sides) scratch.Poly = new Vector3[sides];
        var poly = scratch.Poly.AsSpan(0, sides);
        for (int i = 0; i < sides; i++)
            poly[i] = Vector3.Transform(s.Vertices[s.SectorVertices[surface.FirstVertex + i].Vertex], back);
        // A rectangle whose box misses the polygon's on X or Z clips to nothing: said at once (ours,
        // the same answer the engine would give).
        float px0 = MathF.Min(MathF.Min(pts[0].X, pts[1].X), MathF.Min(pts[2].X, pts[3].X));
        float px1 = MathF.Max(MathF.Max(pts[0].X, pts[1].X), MathF.Max(pts[2].X, pts[3].X));
        float pz0 = MathF.Min(MathF.Min(pts[0].Z, pts[1].Z), MathF.Min(pts[2].Z, pts[3].Z));
        float pz1 = MathF.Max(MathF.Max(pts[0].Z, pts[1].Z), MathF.Max(pts[2].Z, pts[3].Z));
        float qx0 = float.MaxValue, qx1 = float.MinValue, qz0 = float.MaxValue, qz1 = float.MinValue;
        foreach (var q in poly) { qx0 = MathF.Min(qx0, q.X); qx1 = MathF.Max(qx1, q.X); qz0 = MathF.Min(qz0, q.Z); qz1 = MathF.Max(qz1, q.Z); }
        if (px1 < qx0 || qx1 < px0 || pz1 < qz0 || qz1 < pz0) return null;
        int count = Clip(pts, tex, shade, 4, poly, scratch);
        if (count < 3) return null;
        float y = poly[0].Y + Hover;
        var world = new Vector3[count];
        for (int i = 0; i < count; i++) world[i] = Vector3.Transform(new Vector3(pts[i].X, y, pts[i].Z), onto);
        return new ShadowPoly
        {
            Points = world, Uvs = tex[..count], Shade = shade[..count], Texture = texture, Bitmap = bitmap,
            Sector = hit.Sector, Surface = hit.Surface,
        };
    }

    /// <summary>Working room for <see cref="Lay"/> and <see cref="Clip"/>, kept between calls so a frame does not allocate for every surface it tries.</summary>
    public sealed class Scratch
    {
        const int N = Explosions.Decals.MaxPoints + 1;
        internal readonly Vector3[] Points = new Vector3[N], OutPoints = new Vector3[N];
        internal readonly Vector2[] Uvs = new Vector2[N], OutUvs = new Vector2[N];
        internal readonly float[] Shade = new float[N], OutShade = new float[N];
        /// <summary>The surface's own polygon, grown to the largest one met.</summary>
        internal Vector3[] Poly = new Vector3[32];
    }

    /// <summary>
    /// <see cref="Explosions.Decals.Clip"/> with a shade carried at each point
    /// (its edge step keeps a point's one float and interpolates it where the outline
    /// crosses an edge, as it does the texture coordinates).
    /// </summary>
    public static int Clip(Vector3[] pts, Vector2[] uvs, float[] shade, int n, ReadOnlySpan<Vector3> poly, Scratch? scratch = null)
    {
        int max = Explosions.Decals.MaxPoints;
        int m = poly.Length;
        var inP = pts; var inT = uvs; var inS = shade;
        scratch ??= new Scratch();
        var outP = scratch.OutPoints; var outT = scratch.OutUvs; var outS = scratch.OutShade;
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
                    if (count == max) break;
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
                        outS[count] = inS[k];
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
                    outS[count] = (inS[next] - inS[k]) * t + inS[k];
                    count++;
                }
                if (count == 0) state = 2;
            }
            if (state == 2 || count < 3) return 0;
            if (state != 0) { (inP, outP) = (outP, inP); (inT, outT) = (outT, inT); (inS, outS) = (outS, inS); }
            n = count;
        }
        if (!ReferenceEquals(inP, pts))
        {
            Array.Copy(inP, pts, n);
            Array.Copy(inT, uvs, n);
            Array.Copy(inS, shade, n);
        }
        return n;
    }
}
