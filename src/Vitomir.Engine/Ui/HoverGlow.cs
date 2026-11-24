// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The glow a thing takes on while the pointer is resting on it.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>
/// The hover glow: what the pointer is resting on brightens and takes on a colour that says what a
/// click would do.
///
/// It is **two effects with one fade**, and they end differently. Read end to end, through
/// The original and its renderer DLL (whose one export fills the table
/// the game draws through):
///
/// <list type="bullet">
/// <item><b>The colour</b> is chosen by the interface's hover when the hover *changes* and written into
/// the hovered thing's model instance (<see cref="Highlight.Colour"/>). It is never
/// written back; a thing keeps the colour it was last given.</item>
/// <item><b>The fade</b> is the model instance's own update: an intensity moves
/// towards 1 while the instance carries bit `0x20` and towards 0 while it does not, at
/// <see cref="FadeRate"/> a second, clamped to 0..1. The draw sets that bit from flag 2,
/// and the draw passes flag 2 for exactly one thing — the hovered one.</item>
/// <item><b>The tint</b> is an extra pass the renderer adds only while the bit is set:
/// mode 28, `full_addconstant` — the constant alone, **added** on top,
/// with no texture colour in it at all — and the constant is the colour times the intensity. So the
/// tint comes up over a third of a second and is gone **the frame the pointer leaves**. A
/// video of the reference build shows exactly that: 18 frames at 60 fps to come up, one to go.</item>
/// <item><b>The lift</b> is the other half, and it does not stop with the pointer. While the intensity
/// is not zero, the model's light set-up raises its base light to each channel's greater
/// of itself and <see cref="LiftLevel"/>, times the intensity, and the renderer adds that base to
/// every vertex's light before it normalises. So a glowing thing is also lit,
/// and it keeps being lit, dimming, for the third of a second the fade takes to reach zero after the
/// tint has already gone. The per-frame model lighting applies it (<see cref="World.ModelLight.Glow"/>,
/// <see cref="Scene.ThingLighting"/>), from <see cref="Intensity"/>.</item>
/// </list>
///
/// **<see cref="Base"/> is this class's own record of the raise over a black base**, and the residue it
/// keeps — <see cref="LiftLevel"/> times the last non-zero intensity — is real only for a thing riding
/// another: the sector's draw rewrites every other thing's base from the ambient each frame
/// before the glow is applied (corrected).
///
/// **The colours on screen are not the ones in the table.** The renderer hands the colour to Direct3D
/// as the texture factor in the ordinary `0xAARRGGBB` order, so a door (`0xFF505000`) asks for a dim
/// yellow and an enemy (`0xFF600000`) for a dark red. The reference build shows a door **cyan** and an
/// enemy **blue** — measured off a video, the door gains (0, 80, 80) on every pixel:
/// the two outer bytes exchanged, red for blue. The executable is not where that happens; the GOG
/// DirectX 1-7 wrapper the reference build draws through is the only thing between the two. What the
/// player sees is the baseline, so Vitomir shows cyan and blue, and the Fix
/// <see cref="Fixes.GlowColours"/> puts back the colours 1.1 asks for.
///
/// In 1.1 the door that leads deeper into the dungeon, the lever and the barrels
/// all highlight when the pointer is over them, and here nothing
/// did — characters included. The colour table had existed and been tested for weeks, and nothing
/// had ever drawn it.
/// </summary>
public sealed class HoverGlow
{
    /// <summary>How fast the intensity rises and falls, in full intensities a second (`_DAT_004eb7ec`).</summary>
    public const float FadeRate = 3f;

    /// <summary>The brightest a glow gets (`_DAT_004eb3f4`).</summary>
    public const float Full = 1f;

    /// <summary>
    /// The base light a glowing model is given at full intensity, in the renderer's 0-255 light units
    /// (`_DAT_004eb76c`, 150.0) — a little over half of full white, added to what its lights give it.
    /// </summary>
    public const float LiftLevel = 150f;

    sealed class State
    {
        public uint Colour;
        public float Intensity;
        public float Base;
    }

    readonly Dictionary<WorldThing, State> glowing = new(ReferenceEqualityComparer.Instance);

    /// <summary>The thing the pointer is resting on: in 1.1 the only one that brightens, and the only one tinted.</summary>
    public WorldThing? Hovered { get; private set; }

    readonly Dictionary<WorldThing, uint> shown = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// **Ours** (<see cref="ShowObjects"/>): the things the show-objects key lights this frame, each with
    /// the colour it would take under the pointer. Each carries the hover bit as the hovered thing does —
    /// the same fade, tint and lift — until a frame leaves it out; null or empty lets them all go.
    /// </summary>
    public void Show(IReadOnlyDictionary<WorldThing, uint>? things)
    {
        shown.Clear();
        if (things is null) return;
        foreach (var (thing, colour) in things)
        {
            shown[thing] = colour;
            if (!glowing.ContainsKey(thing)) glowing[thing] = new State();
        }
    }

    /// <summary>Whether the show-objects key is lighting <paramref name="thing"/> (ours, <see cref="Show"/>).</summary>
    public bool IsShown(WorldThing thing) => shown.ContainsKey(thing);

    bool Bit(WorldThing thing) => ReferenceEquals(thing, Hovered) || shown.ContainsKey(thing);

    /// <summary>
    /// The pointer has come to rest on <paramref name="thing"/>, or on nothing. The colour is chosen
    /// only when the hover *changes*, as the original does — pointing at the same thing frame after
    /// frame does not re-pick it. A thing that was glowing a moment ago keeps its intensity and
    /// rises again from there.
    /// </summary>
    public void Point(WorldThing? thing, WorldThing? player)
    {
        if (ReferenceEquals(thing, Hovered)) return;
        Hovered = thing;
        if (thing is null || player is null) return;
        var verb = Cursor.Verb(player, thing);
        if (!glowing.TryGetValue(thing, out var s)) glowing[thing] = s = new State();
        s.Colour = Highlight.Colour(verb, thing.Type);
    }

    /// <summary>
    /// One frame: the model update's fade, then the draw's lift. A thing that has left the world is
    /// forgotten, because it is no longer drawn at all.
    /// </summary>
    public void Step(float seconds)
    {
        if (glowing.Count == 0) return;
        List<WorldThing>? gone = null;
        foreach (var (thing, s) in glowing)
        {
            if (!thing.InWorld) { (gone ??= []).Add(thing); continue; }
            // Down while the bit is clear, up while it is set.
            s.Intensity = Bit(thing)
                ? MathF.Min(Full, s.Intensity + seconds * FadeRate)
                : MathF.Max(0f, s.Intensity - seconds * FadeRate);
            // Only while the intensity is not zero, so the last value stays behind.
            if (s.Intensity != 0f) s.Base = s.Intensity * MathF.Max(LiftLevel, s.Base);
            if (s.Intensity == 0f && s.Base == 0f && !Bit(thing)) (gone ??= []).Add(thing);
        }
        if (gone is not null) foreach (var t in gone) glowing.Remove(t);
    }

    /// <summary>How brightly <paramref name="thing"/> is glowing, 0 to 1.</summary>
    public float Intensity(WorldThing thing) => glowing.TryGetValue(thing, out var s) ? s.Intensity : 0f;

    /// <summary>The colour <paramref name="thing"/> was given, as the table writes it, or 0 when it has none.</summary>
    public uint Colour(WorldThing thing) => glowing.TryGetValue(thing, out var s) ? s.Colour : 0u;

    /// <summary>The base light <paramref name="thing"/> has been given, in 0-255 light units.</summary>
    public float Base(WorldThing thing) => glowing.TryGetValue(thing, out var s) ? s.Base : 0f;

    /// <summary>
    /// The tint to add on top of each tinted thing, as 0..1 RGB already scaled by its intensity.
    /// The renderer's pass is gated on the hover bit, not on the intensity, so in 1.1 this is at most
    /// one thing, the hovered one; the show-objects key (ours, <see cref="Show"/>) adds the things it
    /// lights, each in its own colour — the hovered thing keeps the colour the hover gave it.
    /// </summary>
    public IEnumerable<(WorldThing Thing, Vector3 Add)> Lit()
    {
        if (Hovered is not null && glowing.TryGetValue(Hovered, out var s) && s.Intensity > 0f)
            yield return (Hovered, Shown(s.Colour, Fixes.Current.GlowColours) * s.Intensity);
        foreach (var (thing, colour) in shown)
            if (!ReferenceEquals(thing, Hovered) && glowing.TryGetValue(thing, out var t) && t.Intensity > 0f)
                yield return (thing, Shown(colour, Fixes.Current.GlowColours) * t.Intensity);
    }

    /// <summary>Every thing whose light is raised, and by how much, as 0..1 of full white.</summary>
    public IEnumerable<(WorldThing Thing, float Lift)> Lifts()
    {
        foreach (var (thing, s) in glowing)
            if (s.Base > 0f) yield return (thing, s.Base / 255f);
    }

    /// <summary>
    /// A table colour as it reaches the screen, 0..1 RGB. The reference build shows the outer two
    /// bytes exchanged (see the class notes); <paramref name="authored"/> reads it the way the
    /// executable meant it.
    /// </summary>
    public static Vector3 Shown(uint colour, bool authored)
    {
        float hi = (colour >> 16) & 0xFF, mid = (colour >> 8) & 0xFF, lo = colour & 0xFF;
        return (authored ? new Vector3(hi, mid, lo) : new Vector3(lo, mid, hi)) / 255f;
    }

    /// <summary>
    /// A vertex's light with the base added, as the renderer does it: the
    /// sum, and when any channel passes full white, all three scaled down by that channel so the
    /// brightest is exactly full. The hue is kept; nothing is clipped.
    /// </summary>
    public static Vector3 Raise(Vector3 light, float lift)
    {
        var sum = light + new Vector3(lift);
        float top = MathF.Max(sum.X, MathF.Max(sum.Y, sum.Z));
        return top > 1f ? sum / top : sum;
    }

    /// <summary>Forgets every glow, which is what a change of place needs.</summary>
    public void Clear() { glowing.Clear(); shown.Clear(); Hovered = null; }
}
