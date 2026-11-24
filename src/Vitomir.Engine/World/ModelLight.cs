// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// How a model is lit, every frame it is drawn — the whole path, read end to end:
//
//   the sector's things: each one's base light (ambient) into its model
//   one thing: its lights chosen, then drawn
//   the picker: the sector's lights, the lights things carry, the sun
//   each chosen light handed to the renderer at this frame's intensity
//   the hover glow raises the base
//   the renderer: the lights turned into each bone's space, the base packed
//   the renderer: every vertex, prelit or black, plus each light by type
//                 (point, spot, sun, box)
//   the renderer: the base added, the colour scaled down to fit, packed
//
// This is the software path, which is the game's unless it is started with `-tl` (render
// option bit 1): that switch hands the same lights to Direct3D instead (SetLight,
// LightEnable), with 1/d attenuation and colour / 255, and is not what 1.1 players saw.
using System.Numerics;
using Vitomir.Engine.Explosions;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// One light the picker chose for a thing, in the layout the renderer is handed (a level light's,
/// which a dynamic light's block matches from `+8`): its type, colour (0–255 a channel), where it
/// is, its direction, the **set** intensity it was scored by, how fast it falls off, its range and
/// cone, its style, and — after <see cref="ModelLightPicker.Finish"/> — this frame's intensity, the
/// set one times its style's brightness.
/// </summary>
public readonly record struct PickedLight(
    uint Type, Vector3 Color, Vector3 Position, Vector3 Direction, float Set, float Falloff, float Range,
    float CosPenumbra, int Style, object Source, float Score = 0f, float Now = 0f)
{
    /// <summary>A light of the level's (a sector's, or the level's sun or moon).</summary>
    public static PickedLight Of(NilLight light) =>
        new(light.Type, light.Color, light.Position, light.Direction, light.Intensity2, light.Falloff,
            light.Range, light.CosPenumbra, (int)light.Style, light);

    /// <summary>A light a thing carries (handed over as `light+8`).</summary>
    public static PickedLight Of(DynamicLight light) =>
        new(light.Type, light.Color, light.Position, light.Direction, light.Current, light.Falloff,
            light.Radius, light.CosPenumbra, light.Style, light);
}

/// <summary>
/// **The thing-light picker**, with the array it fills (pairs of light and
/// score, and a count). One of these is kept and reused for every thing drawn, as
/// the engine's static array is — and that matters, because two of the engine's slips write one
/// slot past the end of what it has filled, and what they leave there is read by the next thing.
/// </summary>
public sealed class ModelLightPicker
{
    /// <summary>The engine asserts a picker is never asked for more than eight (`maxLights &lt;= RENDEROPTIONS_MAX_T…`).</summary>
    public const int Most = 8;

    readonly PickedLight[] slots = new PickedLight[Most + 1];
    int count, min;

    /// <summary>How many lights were chosen.</summary>
    public int Count => count;

    /// <summary>The lights chosen, in the order the renderer is handed them after <see cref="Finish"/>.</summary>
    public ReadOnlySpan<PickedLight> Lights => slots.AsSpan(0, count);

    /// <summary>A new thing: the count and the index of the weakest both start at zero.</summary>
    public void Begin() { count = 0; min = 0; }

    /// <summary>
    /// One light scored at more than zero offered to the array (the insertion the first two loops
    /// share): while there is room it goes on the end, and it becomes the weakest if it is weaker
    /// than the weakest so far; when the array is full it replaces the weakest only if it is
    /// stronger, and the weakest is looked for again.
    /// </summary>
    /// <param name="late">
    /// The third loop's (lights carried in the **other** sectors being drawn): it counts the light
    /// **before** it compares, so the weakest's index can point one past the last light — and when
    /// the array next fills, the comparison is against whatever that slot last held. Kept.
    /// </param>
    public void Offer(PickedLight light, int max, bool late = false)
    {
        if (!(light.Score > 0f)) return;                                   // `_DAT_004eb488 < score`
        if (count < max)
        {
            slots[count] = light;
            if (!late)
            {
                if (light.Score < slots[min].Score) min = count;
                count++;
            }
            else
            {
                count++;
                if (light.Score < slots[min].Score) min = count;           // One past
            }
            return;
        }
        if (!(slots[min].Score < light.Score)) return;
        slots[min] = light;
        min = 0;
        for (int i = 1; i < count; i++)
            if (slots[i].Score < slots[min].Score) min = i;
    }

    /// <summary>
    /// The level's sun or moon, offered last. With room it goes on the end and **the
    /// weakest is not looked at**; with the array full the engine writes the light **and its score**
    /// into the slot past the end (both at `count × 8`) and does not count it — so
    /// the sun is lost, every light kept keeps its score and its place, and the slot past the end
    /// holds the sun for the next thing's late comparison. (Earlier, Vitomir wrote the score over
    /// the weakest's, a misreading, which could move that light in the order the shadows are cast
    /// in.)
    /// </summary>
    public void OfferGlobal(PickedLight light, int max)
    {
        if (!(light.Score > 0f)) return;
        if (count < max) { slots[count++] = light; return; }
        slots[count] = light;                                              // Past the end
    }

    /// <summary>
    /// The array sorted (the C library's `qsort`) — every light that is not
    /// a sun first, strongest first, then the suns, strongest first — and each light's intensity for
    /// this frame worked out as it is handed over: the set intensity, times its style's
    /// brightness when the style is not 0 — nothing at all while that style is switched off.
    /// </summary>
    public void Finish(LightStyles styles)
    {
        // `qsort` is not stable; ties keep their order here, which the engine's may not.
        slots.AsSpan(0, count).Sort(ByCompare);
        for (int i = 0; i < count; i++)
        {
            var l = slots[i];
            slots[i] = l with { Now = l.Style == 0 ? l.Set : styles.Value(l.Style) * l.Set };
        }
    }

    /// <summary>Suns after everything else; within each, the higher score first.</summary>
    /// <summary><see cref="Compare"/> as the sort wants it, made once (one was made for every pick).</summary>
    static readonly Comparison<PickedLight> ByCompare = Compare;

    public static int Compare(PickedLight a, PickedLight b)
    {
        bool sa = a.Type == 2, sb = b.Type == 2;
        if (sa != sb) return sa ? 1 : -1;
        return b.Score.CompareTo(a.Score);
    }
}

/// <summary>
/// The rules of lighting a model — the picker's scores, the base light and the per-vertex arithmetic
/// of the renderer — as pure functions. <see cref="Scene.ThingLighting"/> runs them every frame.
/// </summary>
public static class ModelLight
{
    /// <summary>
    /// How many lights the picker keeps for a thing: the detail option's `maxThingLights`
    /// (the preset's byte), which the normal and high presets set to 4, low to 2 and very low to
    /// 1 (<see cref="Ui.DetailPresets"/>). Vitomir draws with eight, the lights bar's top
    /// (<see cref="Ui.DetailPresets.Maxed"/>); this is 1.1's normal and high.
    /// </summary>
    public const int DefaultMaxLights = 4;

    /// <summary>
    /// The renderer's one scale: a light of intensity 392 at the vertex, square to it, gives full
    /// white (1/392 — the light blob's own). The base light is the level's ambient over
    /// the same 392.
    /// </summary>
    public const float Scale = 1f / 392f;

    /// <summary>The floor an item's, a weapon's or an armour's base light is raised to, a channel (100).</summary>
    public const float ItemFloor = 100f;

    /// <summary>The floor the hover glow raises a base to before scaling it by its intensity (150).</summary>
    public const float GlowFloor = 150f;

    /// <summary>
    /// The sector flag that lets the level's sun or moon reach a thing in it (`sector flags &amp;
    /// 0x10000`). The shipped levels set it on the outdoor sectors; a thing indoors is never lit by
    /// the sun, whatever the window.
    /// </summary>
    public const uint SunlitSector = 0x10000;

    /// <summary>The model flags that matter here: `PRE LIT` 1, `FULLY LIT` 2, `CONSTANT LIT` 4.</summary>
    public const uint PreLit = 1, FullyLit = 2, ConstantLit = 4;

    /// <summary>
    /// The engine's square root, both in the game and in the renderer: the
    /// integer trick `0x5f3759df − (bits / 2)` and one Newton step, `1.5 y − 0.5 x y³`. A distance is
    /// `InverseRoot(d²) × d²`, which is within two parts in a thousand of the true one.
    /// </summary>
    public static float InverseRoot(float x)
    {
        float y = BitConverter.Int32BitsToSingle(0x5f3759df - (BitConverter.SingleToInt32Bits(x) >> 1));
        return 1.5f * y - x * 0.5f * y * y * y;
    }

    /// <summary>A distance as the engine measures one: <c>InverseRoot(d²) × d²</c>.</summary>
    public static float Distance(Vector3 a, Vector3 b)
    {
        float d2 = Vector3.DistanceSquared(a, b);
        return InverseRoot(d2) * d2;
    }

    /// <summary>
    /// The thing's radius as the picker counts it: its `RADIUS` when it has a collide
    /// type, and nothing when it has none.
    /// </summary>
    public static float Radius(WorldThing thing) => thing.Collide != CollideShape.None ? thing.Radius : 0f;

    /// <summary>
    /// How strongly one light reaches a thing (the same switch in all four of its passes).
    /// A light whose styled intensity is 0 scores nothing. Then, by type:
    /// <list type="bullet">
    /// <item>0 point: <c>I − (d − r) × falloff</c>;</item>
    /// <item>1 spot: 0 beyond its range or outside its cone (<c>cos &lt; cosPenumbra</c>), else the point
    /// score times the cosine;</item>
    /// <item>2 sun: the **set** intensity, unstyled;</item>
    /// <item>3 box: 0 beyond its range, else <c>I / ((d − r) × falloff × (d − r))</c>.</item>
    /// </list>
    /// <c>I</c> is the set intensity times the style's brightness when the style is not 0; <c>r</c>
    /// the thing's radius (<see cref="Radius(WorldThing)"/>).
    /// </summary>
    public static float Score(in PickedLight light, Vector3 at, float radius, LightStyles styles)
    {
        float intensity = light.Style == 0 ? light.Set : styles.Value(light.Style) * light.Set;
        if (intensity == 0f) return 0f;
        switch (light.Type)
        {
            case 0:
                return intensity - (Distance(at, light.Position) - radius) * light.Falloff;
            case 1:
            {
                var v = at - light.Position;
                float d2 = v.LengthSquared(), inv = InverseRoot(d2);
                float d = inv * d2;
                if (light.Range < d - radius) return 0f;
                float cone = Vector3.Dot(v * inv, light.Direction);
                if (cone < light.CosPenumbra) return 0f;
                return (intensity - (d - radius) * light.Falloff) * cone;
            }
            case 2:
                return light.Set;
            case 3:
            {
                float d = Distance(at, light.Position) - radius;
                if (light.Range < d) return 0f;
                return intensity / (d * light.Falloff * d);
            }
            default:
                return 0f;                                                  // no fifth type is shipped
        }
    }

    /// <summary>
    /// **The picker whole**: the lights a thing is drawn with this frame.
    /// <list type="number">
    /// <item>The lights of **its own sector** — unless the model is `PRE LIT` and has its baked colours
    /// (render option 0x1000 would force them on; it is a debug switch).</item>
    /// <item>The lights carried by the things **in its own sector** (`THING_FLAG_DYNALIGHT`, and this
    /// frame's intensity over 0), scored like a point light from their set intensity — a torch, a
    /// fireball, an explosion, a muzzle flash, and the thing itself if it carries one.</item>
    /// <item>The lights carried by the things in **every other sector being drawn** — the late
    /// insertion (<see cref="ModelLightPicker.Offer"/>).</item>
    /// <item>The level's **sun or moon**, in a sector flagged <see cref="SunlitSector"/> and again not
    /// for a prelit model.</item>
    /// </list>
    /// Then sorted, and each given this frame's intensity. "Being drawn" is the renderer's portal
    /// walk (<see cref="DynamicLights.View"/>): a torch in a room the walk did not reach
    /// lights nobody in view; with no walk every sector counts. **Ours:** the lights in the other
    /// sectors are offered in the order the place keeps them, not sector by sector.
    /// </summary>
    public static void Pick(ModelLightPicker into, int max, WorldThing thing, NilLevel level, LightStyles styles,
                            DynamicLights? carried, bool prelit)
    {
        max = Math.Clamp(max, 0, ModelLightPicker.Most);
        into.Begin();
        var at = thing.Position;
        float radius = Radius(thing);
        var sector = thing.Sector >= 0 && thing.Sector < level.Sectors.Length ? level.Sectors[thing.Sector] : null;
        if (!prelit && sector is not null)
            foreach (var l in sector.Lights)
            {
                var p = PickedLight.Of(l);
                into.Offer(p with { Score = Score(p, at, radius, styles) }, max);
            }
        if (carried is not null)
        {
            for (int pass = 0; pass < 2; pass++)
                foreach (var (who, light) in carried.All)
                {
                    if (!who.InWorld || (who.Flags & WorldThing.Flag.DynaLight) == 0 || !(light.Intensity > 0f)) continue;
                    bool own = who.Sector == thing.Sector;
                    if (own != (pass == 0)) continue;
                    if (!own && carried.View is { } walk && !walk.Sees((int)who.Sector)) continue;   // not drawn this frame
                    var p = PickedLight.Of(light);
                    into.Offer(p with { Score = Score(p, at, radius, styles) }, max, late: pass == 1);
                }
        }
        if (!prelit && sector is not null && (sector.Flags & SunlitSector) != 0 && level.GlobalLight is { } sun)
        {
            var p = PickedLight.Of(sun);
            into.OfferGlobal(p with { Score = Score(p, at, radius, styles) }, max);
        }
        into.Finish(styles);
    }

    /// <summary>
    /// **The base light**: the brighter of the level's ambient and the sector's, a channel
    /// at a time, over 392 — and for an item, a weapon or an armour lying about, each channel at 100
    /// or below raised to 100, so a dropped sword can be seen in a black room. Things riding
    /// something are not given one here: the draw of the sector's list passes them over, and their
    /// model keeps whatever base it last had (<see cref="Scene.ThingLighting"/>).
    /// </summary>
    public static Vector3 Base(NilLevel level, NilSector? sector, ThingType type)
    {
        var ambient = sector is null ? level.Ambient : Vector3.Max(level.Ambient, sector.Ambient);
        ambient *= Scale;
        if (type is ThingType.Item or ThingType.Weapon or ThingType.Armor)
            ambient = new Vector3(ambient.X <= ItemFloor ? ItemFloor : ambient.X,
                                  ambient.Y <= ItemFloor ? ItemFloor : ambient.Y,
                                  ambient.Z <= ItemFloor ? ItemFloor : ambient.Z);
        return ambient;
    }

    /// <summary>
    /// The hover glow's raise: while the model's glow intensity is not zero, its base is
    /// each channel's greater of itself and 150, times the intensity — written back into the model,
    /// so it is what the base is from then on until something writes it again.
    /// </summary>
    public static Vector3 Glow(Vector3 based, float intensity) =>
        intensity == 0f ? based : Vector3.Max(based, new Vector3(GlowFloor)) * intensity;

    /// <summary>
    /// One vertex's light before the base (the renderer's vertex light and the four it calls), in 0–255
    /// units: <paramref name="start"/> — black, or the model's baked colour when it is prelit — plus
    /// each light, where <c>L</c> runs from the vertex to the light and <c>n</c> is the vertex's
    /// normal:
    /// <list type="bullet">
    /// <item>point: when <c>L·n &gt; 0</c> and <c>f = I − |L| × falloff &gt; 0</c>, the colour
    /// times <c>(L·n / |L|) × f / 392</c>;</item>
    /// <item>spot: the same, only inside the cone — <c>(L/|L|)·dir ≤ −cosPenumbra</c> —
    /// and with no fall-off towards its edge;</item>
    /// <item>sun: when <c>−dir·n &gt; 0</c>, the colour times <c>−dir·n × I / 392</c>;</item>
    /// <item>box: when <c>L·n &gt; 0</c> and the light faces away from the vertex
    /// (<c>c = (L/|L|)·dir ≤ 0</c>), the colour times
    /// <c>falloff × (L·n/|L|) × I × −c / (|L|² × 392)</c>.</item>
    /// </list>
    /// <c>I</c> is this frame's intensity (<see cref="PickedLight.Now"/>). The thing's radius plays
    /// no part here; nor does the light's range, except for a box — a point light is cut off where
    /// its own fall-off reaches zero.
    /// </summary>
    public static Vector3 Vertex(ReadOnlySpan<PickedLight> lights, Vector3 p, Vector3 n, Vector3 start)
    {
        var acc = start;
        foreach (ref readonly var l in lights)
        {
            switch (l.Type)
            {
                case 0:
                {
                    var to = l.Position - p;
                    float ndl = Vector3.Dot(to, n);
                    if (!(ndl > 0f)) break;
                    float d2 = to.LengthSquared(), inv = InverseRoot(d2);
                    float f = l.Now - inv * d2 * l.Falloff;
                    if (!(f > 0f)) break;
                    acc += l.Color * (inv * ndl * f * Scale);
                    break;
                }
                case 1:
                {
                    var to = l.Position - p;
                    float ndl = Vector3.Dot(to, n);
                    if (!(ndl > 0f)) break;
                    float d2 = to.LengthSquared(), inv = InverseRoot(d2);
                    if (!(Vector3.Dot(to * inv, l.Direction) <= -l.CosPenumbra)) break;
                    float f = l.Now - inv * d2 * l.Falloff;
                    if (!(f > 0f)) break;
                    acc += l.Color * (f * inv * ndl * Scale);
                    break;
                }
                case 2:
                {
                    float ndl = -Vector3.Dot(l.Direction, n);
                    if (!(ndl > 0f)) break;
                    acc += l.Color * (ndl * l.Now * Scale);
                    break;
                }
                case 3:
                {
                    var to = l.Position - p;
                    float ndl = Vector3.Dot(to, n);
                    if (!(ndl > 0f)) break;
                    float inv = InverseRoot(to.LengthSquared());
                    float c = Vector3.Dot(to * inv, l.Direction);
                    if (!(c <= 0f)) break;
                    float w = l.Falloff * inv * ndl * inv * inv * l.Now * c * -Scale;   // the renderer's constant, −1/392
                    if (!(w > 0f)) break;
                    acc += l.Color * w;
                    break;
                }
            }
        }
        return acc;
    }

    /// <summary>
    /// **The pack**: the base added, and — where the brightest channel is past 255 — all three
    /// scaled so that it is exactly 255, which keeps the hue rather than clipping it; then each turned
    /// to a byte by the renderer's float trick (1.5 × 2²³ added, which rounds half to even). Packed
    /// the way a scene vertex carries its colour (R, G, B, A bytes), alpha full.
    /// </summary>
    public static uint Pack(Vector3 light, Vector3 based)
    {
        var c = light + based;
        float top = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
        if (top > 255f) c *= 255f / top;
        return Byte(c.X) | (Byte(c.Y) << 8) | (Byte(c.Z) << 16) | 0xff000000u;
    }

    /// <summary>A channel as the renderer makes a byte of it: round half to even, the low eight bits.</summary>
    static uint Byte(float x) => (uint)(int)MathF.Round(x) & 0xff;                  // MathF.Round rounds half to even

    /// <summary>A baked vertex colour (B, G, R, A in the file) as the renderer reads it into its sum: 0–255 floats.</summary>
    public static Vector3 Baked(uint bgra) =>
        new((bgra >> 16) & 0xff, (bgra >> 8) & 0xff, bgra & 0xff);
}

/// <summary>A diagnostic: what lights a thing this frame, one line each (the harness's <c>--thing-lights</c>).</summary>
public static class ModelLightReport
{
    public static IEnumerable<string> Describe(Location place, WorldThing thing, int max = ModelLight.DefaultMaxLights)
    {
        var picker = new ModelLightPicker();
        ModelLight.Pick(picker, max, thing, place.Level, place.Styles, place.Lights, prelit: false);
        var sector = thing.Sector >= 0 && thing.Sector < place.Level.Sectors.Length ? place.Level.Sectors[thing.Sector] : null;
        var based = ModelLight.Base(place.Level, sector, thing.Type);
        yield return $"{thing.Template} at {thing.Position} in sector {thing.Sector} (flags 0x{sector?.Flags ?? 0:x}, " +
                     $"{sector?.Lights.Length ?? 0} lights): base {based}, radius {ModelLight.Radius(thing)}, {picker.Count} chosen of {max}";
        foreach (var l in picker.Lights.ToArray())
            yield return $"  {(l.Source is Explosions.DynamicLight ? "carried" : "level")} type {l.Type} at {l.Position} " +
                         $"colour {l.Color} set {l.Set:0.#} now {l.Now:0.#} style {l.Style} falloff {l.Falloff:0.###} score {l.Score:0.#}";
    }
}
