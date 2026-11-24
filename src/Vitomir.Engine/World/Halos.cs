// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A thing's halo: the glow a lantern, a lamp or a flashlight has around it (the world effects
// and the world draw's last loop, and the renderer's halo draw).
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>One thing's halo as the template gives it, and how far faded in it is.</summary>
public sealed class Halo(WorldThing thing, string material, float size, uint colour)
{
    public WorldThing Thing { get; } = thing;
    /// <summary>`HALOMATERIAL` (tag 96), by name.</summary>
    public string Material { get; } = material;
    /// <summary>`HALOSIZE` (tag 97): half the square's width — world units, or for a fluoro halo a share of the screen.</summary>
    public float Size { get; } = size;
    /// <summary>`HALOCOLOR` (tag 98), packed `0xAARRGGBB` as the template applier packs it.</summary>
    public uint Colour { get; } = colour;
    /// <summary>How far it has faded in, 0 to 255. Every halo starts out.</summary>
    public float Alpha { get; set; }
}

/// <summary>
/// **The halos of a place.** A thing whose render flags carry `HALO` (0x1) and whose template names a
/// `HALOMATERIAL` is drawn with a glowing square over it: the lanterns of Prague and London
/// (`lantern1_halo` … `oilLamp_halo`, 64 across), the free-standing `halo_*` effects, and the
/// modern-day flashlight (0.1, and `FLUORO HALO`). Read end to end:
/// <list type="bullet">
/// <item>**Each frame it is drawn** a ray is cast from the eye to the thing
/// (from the camera's sector, with every thing ignored). Clear, the halo fades
/// **in** at 500 a second to 255; blocked, it fades **out** at the same rate to
/// 0. So a lantern round a corner dims away in half a second rather than vanishing.</item>
/// <item>**At most 32** are drawn a frame (`count &lt; 0x20`), in the order the
/// things are drawn.</item>
/// <item>**The colour** is the template's scaled by the fade (from black, by
/// <c>alpha / 255</c>, alpha included).</item>
/// <item>**The square** (slot 0x1c) is laid out on the screen: centred on the
/// thing, `HALOSIZE` × the projection's scale across — divided by the depth for an ordinary halo, so
/// it is `HALOSIZE` world units, and **not** for a `FLUORO HALO` (0x10), which stays the same size on
/// screen however far away it is. Its depth is 0 — nearer than anything — so what hides a halo is the
/// fade and nothing else; it is drawn in its material's own pass, every shipped halo material's being
/// `glow` (mode 13: added, depth test off).</item>
/// <item>**Last in the world draw** (the world draw's final loop), after the particles.</item>
/// </list>
/// A thing with `DON'T RENDER` has none; `REFLECTION ONLY` leaves out its model but not its halo
/// (the thing draw reaches the halo past the model's draw). **Ours:** the fade lives here rather than on
/// the thing, and a still with no time to fade (the harness's `--headless`) is drawn settled.
///
/// **The twinkle** goes through the same square: every five seconds a half-second pulse, and while it
/// lasts each thing with `TWINKLE` (0x20) that rides nothing and can be seen glints — a grey
/// `lensflare_object` square up to 28 across at its peak (the pulse at the end of the effects' tick).
/// </summary>
public sealed class Halos
{
    public const uint HaloFlag = 0x1, FluoroFlag = 0x10, DontRender = 0x2;
    public const uint TagMaterial = 96, TagSize = 97, TagColour = 98;
    /// <summary>How fast a halo fades, in 0–255 a second.</summary>
    public const float FadeRate = 500f;
    /// <summary>Fully in.</summary>
    public const float Full = 255f;
    /// <summary>The most drawn in a frame.</summary>
    public const int Most = 32;

    readonly Dictionary<WorldThing, Halo?> byThing = new(ReferenceEqualityComparer.Instance);

    /// <summary>This frame's halos, at most <see cref="Most"/>, in draw order.</summary>
    public List<Halo> Drawn { get; } = [];

    // ---- the twinkle (and the pulse at the end of the effects' tick) ----

    /// <summary>`THING_RENDERFLAG_TWINKLE`: money, gems, rings and the rest of the 77 glint on the ground.</summary>
    public const uint TwinkleFlag = 0x20;
    /// <summary>The material every twinkle is drawn with (loaded by the world draw's set-up).</summary>
    public const string TwinkleMaterial = "lensflare_object";
    /// <summary>A pulse every five seconds, the first five seconds in, half a second long.</summary>
    public const int TwinkleEvery = 5000, TwinkleFor = 500;
    /// <summary>The pulse's size at its peak, world units centre to edge.</summary>
    public const float TwinkleSize = 28f;
    /// <summary>The pulse's phase: <c>ms × 0.002 × 180</c> degrees — half a turn of the sine in 500 ms.</summary>
    public const float TwinkleDegreesPerMs = 0.002f * 180f;
    /// <summary>The ray to a twinkling thing stops this short of it.</summary>
    public const float TwinkleShort = 4f;

    int twinkleNext = -1, twinkleFrom, twinkleUntil;
    bool twinkling;

    /// <summary>
    /// The world draw's set-up (the flare's load, then the next start at <c>now + 5000</c>): the
    /// first pulse five seconds on.
    /// </summary>
    public void Begin(int now)
    {
        twinkleNext = now + TwinkleEvery;
        twinkling = false;
        Twinkle = 0f;
    }

    /// <summary>The pulse's strength this frame, 0 to 1 — nothing between pulses.</summary>
    public float Twinkle { get; private set; }

    /// <summary>This frame's twinkles, at most <see cref="Most"/>: where each glints.</summary>
    public List<Vector3> Twinkles { get; } = [];

    /// <summary>
    /// The pulse (the effects' tick's tail, once a frame): idle until the clock passes the next start, then a
    /// half sine over 500 ms — <c>sin((now − start) × 0.36°)</c> — and the next start 5 s after this one.
    /// </summary>
    public void Pulse(int now)
    {
        if (twinkleNext < 0) Begin(now);
        if (!twinkling)
        {
            if (twinkleNext <= now)
            {
                twinkleFrom = twinkleNext;
                twinkleUntil = twinkleNext + TwinkleFor;
                twinkleNext += TwinkleEvery;
                twinkling = true;
                Twinkle = 0f;
            }
        }
        else if (twinkleUntil < now) twinkling = false;
        else Twinkle = MathF.Sin((now - twinkleFrom) * TwinkleDegreesPerMs * (MathF.PI / 180f));
        if (!twinkling) Twinkle = 0f;
    }

    /// <summary>A thing's halo, read from its template the first time it is asked for; null for none.</summary>
    public Halo? Of(WorldThing thing, TemplateSet? templates)
    {
        if (byThing.TryGetValue(thing, out var have)) return have;
        Halo? halo = null;
        if (templates is not null && thing.Template.Length > 0 && templates.Has(thing.Template))
            halo = From(thing, templates.Resolve(thing.Template));
        return byThing[thing] = halo;
    }

    /// <summary>The template applier's three cases. No material, no halo.</summary>
    public static Halo? From(WorldThing thing, IReadOnlyDictionary<uint, NodTag> tags)
    {
        if (!tags.TryGetValue(TagMaterial, out var material) || material.AsString().Length == 0) return null;
        float size = tags.TryGetValue(TagSize, out var s) ? s.AsFloat() : 0f;
        uint colour = 0xffffffffu;
        if (tags.TryGetValue(TagColour, out var c))
        {
            var rgb = c.AsVector3();
            colour = 0xff000000u | (Byte(rgb.X) << 16) | (Byte(rgb.Y) << 8) | Byte(rgb.Z);
        }
        return new Halo(thing, material.AsString(), size, colour);
    }

    static uint Byte(float x) => (uint)(int)MathF.Round(Math.Clamp(x, 0f, 255f));

    /// <summary>
    /// One frame of the halos seen from <paramref name="eye"/> in <paramref name="eyeSector"/>: each
    /// halo thing's fade stepped by whether the eye can see it, and the first <see cref="Most"/>
    /// collected. <paramref name="seconds"/> of 0 settles every fade at once (ours, for a still).
    /// </summary>
    public void Step(Location place, Vector3 eye, int eyeSector, float seconds)
    {
        Drawn.Clear();
        Twinkles.Clear();
        Pulse(place.Milliseconds);
        foreach (var thing in place.Things)
        {
            if (!thing.InWorld || (thing.RenderFlags & DontRender) != 0) continue;
            // A thing the steered character cannot make out is not drawn at all — the thing draw
            // returns before its halo and its glint, and its halo's fade stands still.
            if ((thing.RenderFlags & (TwinkleFlag | HaloFlag)) != 0 && StealthFade.Unseen(thing, place)) continue;
            // A twinkling thing riding nothing, while a pulse is on, seen short of it.
            if ((thing.RenderFlags & TwinkleFlag) != 0 && thing.AttachedTo is null && twinkling && Twinkles.Count < Most
                && Visible(place, eye, eyeSector, thing.Position, TwinkleShort))
                Twinkles.Add(thing.Position);
            if ((thing.RenderFlags & HaloFlag) == 0) continue;
            if (Of(thing, place.Templates) is not { } halo) continue;
            bool seen = Visible(place, eye, eyeSector, thing.Position);
            if (seconds <= 0f) halo.Alpha = seen ? Full : 0f;
            else if (seen) { if (halo.Alpha < Full) halo.Alpha = Math.Min(Full, halo.Alpha + seconds * FadeRate); }
            else if (halo.Alpha > 0f) halo.Alpha = Math.Max(0f, halo.Alpha - seconds * FadeRate);
            if (Drawn.Count < Most) Drawn.Add(halo);
        }
    }

    /// <summary>The eye's ray to the thing, against the level alone (−1: every thing ignored).</summary>
    static bool Visible(Location place, Vector3 eye, int eyeSector, Vector3 at, float shortOf = 0f)
    {
        if (place.Solid is not { } solid) return true;
        var to = at - eye;
        float length = to.Length();
        if (length <= shortOf) return true;
        return solid.Clear(eye, eyeSector, to / length, length - shortOf);
    }

    /// <summary>A twinkle's colour: grey, every channel alpha included, the pulse × 255.</summary>
    public uint TwinkleColour()
    {
        uint v = Byte(Twinkle * Full);
        return (v << 24) | (v << 16) | (v << 8) | v;
    }

    /// <summary>
    /// The colour drawn: each channel of <see cref="Halo.Colour"/>, alpha included, scaled
    /// from black by <c>fade / 255</c> and rounded the renderer's way. Packed `0xAARRGGBB`.
    /// </summary>
    public static uint Shown(Halo halo)
    {
        float t = halo.Alpha * (1f / 255f);
        uint c = halo.Colour;
        uint a = Byte(((c >> 24) & 0xff) * t), r = Byte(((c >> 16) & 0xff) * t);
        uint g = Byte(((c >> 8) & 0xff) * t), b = Byte((c & 0xff) * t);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }
}
