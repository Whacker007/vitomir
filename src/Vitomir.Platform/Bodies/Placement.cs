// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// A place to put a body before the world's first tick (ours; the program's <c>--place x,y,z[,yaw[,view]]</c>,
/// for pictures): its feet (metres), the way it faces and the way the view looks
/// (degrees, counter-clockwise from +X; the facing kept and the view along it when not given). It is part of how a
/// session starts, like its start anchor, so a recording carries its text in the tape's head (the note
/// <see cref="TapeNote"/>) and a replay puts the body there again before the first frame.
/// </summary>
public readonly record struct Placement(Vector3 Feet, float? YawDegrees, float? ViewDegrees)
{
    /// <summary>The tape head's note that carries the placement's text.</summary>
    public const string TapeNote = "place";

    /// <summary>
    /// Reads <c>x,y,z[,yaw[,view]]</c> (a point for the decimals). False with <paramref name="error"/> saying why
    /// for anything else: fewer than three numbers or more than five, or one that is not a finite number.
    /// </summary>
    public static bool TryParse(string? text, out Placement placement, out string error)
    {
        placement = default; error = "";
        var parts = (text ?? "").Split(',');
        var v = new float[parts.Length];
        bool numbers = true;
        for (int i = 0; i < parts.Length; i++)
            numbers &= float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) && float.IsFinite(v[i]);
        if (parts.Length is < 3 or > 5 || !numbers)
        {
            error = $"--place wants x,y,z[,yaw[,view]] (metres, degrees), for example --place 12.5,-3,0.2,90; not '{text}'";
            return false;
        }
        placement = new Placement(new Vector3(v[0], v[1], v[2]), parts.Length > 3 ? v[3] : null, parts.Length > 4 ? v[4] : null);
        return true;
    }

    /// <summary>
    /// Puts body <paramref name="e"/> here, standing still and making no move (it settles on its first ticks), and
    /// turns its camera rig's view. Answers how far the view is turned from the facing (radians), which a headless
    /// still's <c>--forward</c> goes by. Between ticks only.
    /// </summary>
    public float Apply(World world, EntityId e)
    {
        ref var body = ref world.Get<Body>(e);
        float yaw = YawDegrees is { } y ? y * DetMath.DegToRad : body.Yaw;
        float view = ViewDegrees is { } w ? w * DetMath.DegToRad : yaw;
        body = Body.At(Feet, yaw);
        if (world.Has<Traversal>(e)) world.Get<Traversal>(e) = default;
        if (world.Has<CameraRig>(e)) world.Get<CameraRig>(e).Yaw = view;
        return DetMath.WrapAngle(yaw - view);
    }

    /// <summary>The placement as a line says it: <c>(12.5, -3, 0.2) facing 90°</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"({Feet.X:0.###}, {Feet.Y:0.###}, {Feet.Z:0.###}){(YawDegrees is { } y ? $" facing {y:0.#}°" : "")}{(ViewDegrees is { } w ? $", the view {w:0.#}°" : "")}");
}
