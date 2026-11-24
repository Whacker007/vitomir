// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A thing's dynamic light: a 0x60-byte block the thing points at, made
// with the light and freed with it. Its middle is laid out exactly like one of the level's own lights — the renderer and
// the thing-light picker are handed `light+8` and read it as a sector light record — which is why
// the fields below sit where they do.
using System.Numerics;

namespace Vitomir.Engine.Explosions;

/// <summary>
/// One thing's dynamic light. Offsets are from the start of the block; "record" offsets are from
/// `+8`, where the level-light layout begins.
/// </summary>
public sealed class DynamicLight
{
    /// <summary>`DYNA_FLAGS` (tag 125). The SDK lists no flags ("NO FLAGS YET") and nothing reads it.</summary>
    public uint Flags { get; set; }
    /// <summary>
    /// The light's material, by name — the blob drawn over the walls it reaches. Empty is
    /// "none of its own": the blob pass then uses the engine's `lightblob`.
    /// </summary>
    public string Material { get; set; } = "";
    /// <summary>The light type. Nothing ever writes it, so every dynamic light is a point light (0).</summary>
    public uint Type { get; set; }
    /// <summary>Its light style (`DYNA_STYLE`, 126), one of the 32 the lightmaps use; 0 is steady.</summary>
    public int Style { get; set; }
    /// <summary>Its colour, 0–255 a channel, as floats.</summary>
    public Vector3 Color { get; set; }
    /// <summary>
    /// **This frame's** intensity — <see cref="Current"/> times its style's
    /// brightness, written by the dynamic-light pass every frame the thing is seen. It is
    /// what `GetDynamicLightIntensity` reads and `SetDynamicLightIntensity` writes, so a script's set
    /// lasts until the next frame overwrites it.
    /// </summary>
    public float Intensity { get; set; }
    /// <summary>The intensity it is set to — what the template and the explosion scripts write and ramp.</summary>
    public float Current { get; set; }
    /// <summary>A spot light's direction. Never written.</summary>
    public Vector3 Direction { get; set; }
    /// <summary>Where it is, put there by the thing tick.</summary>
    public Vector3 Position { get; set; }
    /// <summary>A spot light's cone. Never written.</summary>
    public float CosPenumbra { get; set; }
    /// <summary>Its range (`DYNA_RADIUS`, or the intensity when that is not given).</summary>
    public float Radius { get; set; }
    /// <summary>How fast it fades with distance when it lights a thing — intensity over radius, or 1.</summary>
    public float Falloff { get; set; }
    /// <summary>The bone it rides (`DYNA_BONE`, 124), −1 for none. No shipped template sets one.</summary>
    public int Bone { get; set; } = -1;
    /// <summary>Its offset from the bone (`DYNA_OFFSET`, 135). No shipped template sets one.</summary>
    public Vector3 Offset { get; set; }

    /// <summary>What a fresh light is: intensity, current and radius 200, white, no bone, falloff 0.</summary>
    public const float DefaultIntensity = 200f, DefaultColor = 255f;
    /// <summary>The material `AllocateDynamicLight` gives a light it makes (made with its argument set).</summary>
    public const string Blob = "lightblob";
    /// <summary>A `DYNA_RADIUS` at or below this is ignored and the radius left at the intensity.</summary>
    public const float SmallestRadius = 5f;

    /// <summary>The template's dynamic-light tags, in the editor's order (`noteditortags.txt` SORT).</summary>
    public const uint TagIntensity = 121, TagMaterial = 122, TagColor = 123, TagBone = 124, TagOffset = 135,
                      TagFlags = 125, TagStyle = 126, TagRadius = 127;

    /// <summary>A light as the original makes one, with the blob material when <paramref name="blob"/> (the native's call) and none otherwise (the template's).</summary>
    public static DynamicLight Allocate(bool blob) => new()
    {
        Intensity = DefaultIntensity, Current = DefaultIntensity, Radius = DefaultIntensity,
        Color = new Vector3(DefaultColor), Bone = -1, Material = blob ? Blob : "",
    };

    /// <inheritdoc/>
    public override string ToString() => $"light {Current} ({Intensity}) r{Radius} rgb({Color.X},{Color.Y},{Color.Z}) style {Style}";
}
