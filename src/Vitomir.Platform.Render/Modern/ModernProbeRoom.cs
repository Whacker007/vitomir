// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernProbeRoom.cs at b4185ef, reworked for the platform.
using System.Numerics;
using Vitomir.Formats.L2;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// **Ours: a room in the light probes' grid**. The
/// probes stand 1.5 m apart and are read with a trilinear filter, and a house wall is 0.4 m thick: a floor at a
/// wall's foot indoors would take most of its light from the probe in the street on the wall's other side, and a
/// room would be lit as the street is. So each interior of the level (<see cref="L2Area"/>, kind
/// <see cref="L2Area.Interior"/>) is a room: a surface inside its box reads the probes at a point held a whole
/// spacing inside the box's faces (<see cref="Read"/>), where all eight probes round the point stand in the
/// room's own air. Over a wall's thickness outside the box (<see cref="Rise"/>: the doorway's reveals, the
/// threshold) the point slides from the street's to the room's. The shader does the same
/// (<c>ProbeRoomPoint</c> in <c>Shaders/ModernProbes.hlsli</c>) from <see cref="Most"/> rooms in the frame's
/// constants; this is its model and its test.
/// </summary>
internal readonly record struct ModernProbeRoom(Vector3 Origin, float RotZ, Vector3 Size)
{
    /// <summary>The most rooms a frame carries (ours); the first of a level's interiors.</summary>
    public const int Most = 4;
    /// <summary>The distance outside a room's box over which a surface's read point slides into the room, metres (ours: a wall's thickness).</summary>
    public const float Rise = 0.4f;

    /// <summary>A level's interiors as rooms, the first <see cref="Most"/> of them.</summary>
    public static List<ModernProbeRoom> From(IReadOnlyList<L2Area>? areas) =>
        [.. (areas ?? []).Where(a => a.Kind == L2Area.Interior && a.Box.Sound).Take(Most)
            .Select(a => new ModernProbeRoom(new Vector3(a.Box.Origin[0], a.Box.Origin[1], a.Box.Origin[2]), a.Box.RotZ,
                                             new Vector3(a.Box.Size[0], a.Box.Size[1], a.Box.Size[2])))];

    Vector3 ToRoom(Vector3 p)
    {
        float c = MathF.Cos(RotZ), s = MathF.Sin(RotZ);
        var d = p - Origin;
        return new Vector3(c * d.X + s * d.Y, -s * d.X + c * d.Y, d.Z);
    }

    /// <summary>
    /// Where the probes are read for a surface's point <paramref name="surface"/> whose read point (the surface's
    /// lifted off along its normal) is <paramref name="lifted"/>, the probes <paramref name="spacing"/> apart;
    /// all metres. A surface further outside the box than <see cref="Rise"/> keeps <paramref name="lifted"/>.
    /// </summary>
    public Vector3 Read(Vector3 surface, Vector3 lifted, float spacing)
    {
        var q = ToRoom(surface);
        var inside = Vector3.Min(q, Size - q);
        float depth = MathF.Min(inside.X, MathF.Min(inside.Y, inside.Z));
        float t = Math.Clamp(depth / Rise + 1f, 0f, 1f);
        if (t <= 0) return lifted;
        float c = MathF.Cos(RotZ), s = MathF.Sin(RotZ);
        float k = spacing * (MathF.Abs(c) + MathF.Abs(s));       // a turned room: a spacing along each of the grid's axes
        var shrink = Vector3.Min(new Vector3(k, k, spacing), Size * 0.5f);
        var r = Vector3.Clamp(ToRoom(lifted), shrink, Size - shrink);
        var held = Origin + new Vector3(c * r.X - s * r.Y, s * r.X + c * r.Y, r.Z);
        return Vector3.Lerp(lifted, held, t);
    }
}
