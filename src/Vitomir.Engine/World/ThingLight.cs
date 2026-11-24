// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Lighting a thing that has no baked lighting of its own — the stand-in.
//
// **The world's things are lit every frame by `ModelLight` and `Scene.ThingLighting`**, the
// engine's own model path read end to end. What is left here is the light a
// model is *built* with — the colours a thing has before its first frame is drawn, and the whole of
// the light of the status bar's heads, which have two fixed lights of their own and no level.
//
// Most things in the game have none: of the roughly 13,100 modelled things across the 112 levels
// only 2,641 carry vertex colours, so the rest are lit at run time from the lights in the sector
// they stand in. The engine's own light picker — it takes a thing and a
// maximum (asserted <= 8, RENDEROPTIONS_MAX_T…), scores every light in the sector, and keeps the
// strongest, evicting the weakest when it is full. The scoring below is that function's, per light
// type; what is not the original's, and is marked where it happens, is how the chosen lights become
// a vertex colour.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>One light chosen for a thing, and how strongly it reaches it.</summary>
public readonly record struct ChosenLight(NilLight Light, float Strength);

public static class ThingLight
{
    /// <summary>The engine asserts a thing is never lit by more than eight lights.</summary>
    public const int MaxLights = 8;

    /// <summary>
    /// The light the engine invents when the picker chose none: white, intensity 400, range 350,
    /// falloff 1, placed at the thing and offset (−50, +50, +100). **It is the
    /// shadow's** — the thing draw hands it to the shadow draw after the model is drawn, and it never lights the
    /// model; this stand-in used it for the build-time light until then read the model path.
    /// </summary>
    public static NilLight Fallback(Vector3 at) => new(
        Flags: 0, InSector: true, Type: NilLight.Point, Style: 0,
        Color: new Vector3(255, 255, 255), Intensity: 350f, Intensity2: 400f,
        Direction: Vector3.Zero, Position: at + FallbackOffset,
        CosUmbra: 0, CosPenumbra: 0, Range: 350f, Falloff: 1f, RawHead: []);

    public static readonly Vector3 FallbackOffset = new(-50, 50, 100);

    /// <summary>
    /// How strongly one light reaches a point, as the engine scores it. The light's
    /// style multiplies its intensity first, so a light on an animated style dims for things
    /// exactly as it dims the lightmap it drives.
    /// </summary>
    /// <param name="radius">The thing's own radius: the falloff starts at its surface, not its centre.</param>
    public static float Strength(NilLight light, Vector3 at, float radius, float styleValue)
    {
        float intensity = light.Style == 0 ? light.Intensity2 : light.Intensity2 * styleValue;
        if (intensity == 0) return 0;
        var away = at - light.Position;

        switch (light.Type)
        {
            case NilLight.Point:
                return intensity - (away.Length() - radius) * light.Falloff;

            case NilLight.Spot:
            {
                float distance = away.Length();
                if (light.Range < distance - radius) return 0;
                float cone = distance > 0 ? Vector3.Dot(away / distance, light.Direction) : 1f;
                if (cone < light.CosPenumbra) return 0;
                return (intensity - (distance - radius) * light.Falloff) * cone;
            }

            // Type 2 is the sun or moon: everywhere at once, at full strength.
            case 2: return intensity;

            case NilLight.Box:
            {
                float distance = away.Length() - radius;          // the engine measures to the box
                if (light.Range < distance) return 0;
                float d = distance * light.Falloff * distance;
                return d > 0 ? intensity / d : intensity;
            }
            default: return 0;
        }
    }

    /// <summary>
    /// The lights that reach a point, strongest first, at most <paramref name="max"/> of them.
    /// The engine keeps them in an unordered fixed array and evicts the weakest; sorting here is
    /// the same set in a more useful order.
    /// </summary>
    public static ChosenLight[] Choose(NilSector sector, Vector3 at, float radius, LightStyles styles,
                                       int max = MaxLights)
    {
        var chosen = new List<ChosenLight>(max + 1);
        foreach (var light in sector.Lights)
        {
            float strength = Strength(light, at, radius, styles.Value((int)light.Style));
            if (strength <= 0) continue;                          // the engine's own test is > 0
            chosen.Add(new ChosenLight(light, strength));
        }
        if (chosen.Count <= max) return [.. chosen.OrderByDescending(c => c.Strength)];
        return [.. chosen.OrderByDescending(c => c.Strength).Take(max)];
    }

    /// <summary>
    /// Intensity is in the same 0-255 units as colour. The evidence is the ambient term: a level
    /// stores it as <c>AmbientColor × AmbientIntensity</c>, and Prague by day comes out
    /// (51000, 46000, 34000) — that is (255, 230, 170) × 200. Its sun is intensity 250 and East
    /// London's moon is 50, on the same scale, so dividing a light's strength by 255 puts
    /// everything in one place without a constant to invent.
    /// </summary>
    public const float Scale = 255f;

    /// <summary>A level or sector's ambient, which is colour × intensity, as a 0-1 colour.</summary>
    public static Vector3 Ambient(Vector3 stored) =>
        Vector3.Clamp(stored / (Scale * Scale), Vector3.Zero, Vector3.One);

    /// <summary>
    /// The colour a vertex takes from those lights.
    ///
    /// <b>The attenuation and the scale are the original's</b>: its score is
    /// <c>intensity − distance × falloff</c>, the file stores <c>range = intensity / falloff</c> so
    /// the score reaches zero exactly at the light's edge, and intensity shares colour's 0-255
    /// range (see <see cref="Scale"/>).
    ///
    /// <b>The rest is ours</b> until the engine's model path is read: that the light falls off with
    /// the cosine of the surface angle, that the lights add, and that the ambient is the floor.
    /// </summary>
    public static Vector3 Shade(ChosenLight[] lights, Vector3 position, Vector3 normal, Vector3 ambient)
    {
        var colour = ambient;
        foreach (var (light, strength) in lights)
        {
            float reach = Math.Min(strength / Scale, 1f);
            if (reach <= 0) continue;
            colour += light.Color / Scale * (reach * Facing(light, position, normal));
        }
        return Vector3.Clamp(colour, Vector3.Zero, Vector3.One);
    }

    /// <summary>How square a surface is to a light. Ours, not the original's — see <see cref="Shade"/>.</summary>
    static float Facing(NilLight light, Vector3 position, Vector3 normal)
    {
        if (normal == Vector3.Zero) return 1f;
        if (light.Type == 2)
        {
            // The sun or moon: its direction points from the light into the scene, so a face turned
            // towards it has a negative dot product.
            if (light.Direction == Vector3.Zero) return 1f;
            return Math.Max(0f, -Vector3.Dot(normal, Vector3.Normalize(light.Direction)));
        }
        var toLight = light.Position - position;
        float d = toLight.Length();
        return d > 0.0001f ? Math.Max(0f, Vector3.Dot(normal, toLight / d)) : 1f;
    }

    /// <summary>The colour packed the way a scene vertex carries it (R, G, B, A bytes).</summary>
    public static uint Pack(Vector3 colour) =>
        (uint)(colour.X * 255f + 0.5f) | ((uint)(colour.Y * 255f + 0.5f) << 8) |
        ((uint)(colour.Z * 255f + 0.5f) << 16) | 0xff000000u;
}
