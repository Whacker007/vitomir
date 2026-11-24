// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Engine/World/FreeCamera.cs at b4185ef, reworked for the platform.
using System.Numerics;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// The eye the modern renderer draws from, in the renderer's own units (72 to the metre,
/// <see cref="ModernMath.UnitsPerMetre"/>), +Z up. Yaw 0 looks along +X, 90° along +Y; pitch up is
/// positive. The old engine's free camera, trimmed to what the renderer reads: the platform hands the
/// renderer a <see cref="Presentation.ViewPoint"/> in metres with a vertical field of view, and
/// <see cref="PlatformRenderer"/> turns it into one of these (<see cref="From"/>).
/// </summary>
internal sealed class FreeCamera
{
    public Vector3 Position;
    /// <summary>Degrees about +Z.</summary>
    public float Yaw;
    /// <summary>Degrees, positive looks up.</summary>
    public float Pitch;

    /// <summary>Vertical field of view in radians. The old program's game camera is 80° across at 4:3 (Hor+), which is <see cref="OldProgramVerticalFov"/>.</summary>
    public float VerticalFov = OldProgramVerticalFov;

    /// <summary>
    /// The near plane (renderer units): the renderer's own <see cref="DefaultNear"/> (10 units, 14 cm), or the
    /// view's own (first person's few centimetres, so nothing of the viewer's body is cut open). The depth is reversed
    /// and infinite, so a nearer plane costs no precision where it matters.
    /// </summary>
    public float Near = DefaultNear;

    /// <summary>The renderer's near plane (units), as it was before a view could choose its own.</summary>
    public const float DefaultNear = 10f;

    /// <summary>The old program's free camera's view: 80° across at 4:3, kept vertical for every window shape (about 64.4°).</summary>
    public static readonly float OldProgramVerticalFov = 2f * MathF.Atan(MathF.Tan(80f * MathF.PI / 360f) * 3f / 4f);

    public Vector3 Forward
    {
        get
        {
            float y = Yaw * MathF.PI / 180f, p = Pitch * MathF.PI / 180f;
            return new Vector3(MathF.Cos(p) * MathF.Cos(y), MathF.Cos(p) * MathF.Sin(y), MathF.Sin(p));
        }
    }

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitZ));

    /// <summary>The eye's up for the look-at: +Z (the platform clamps the pitch short of straight up and down).</summary>
    public Vector3 Up => Vector3.UnitZ;

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Position + Forward, Up);

    /// <summary>The view with the eye at the origin: for things drawn centred on the camera, like the sky.</summary>
    public Matrix4x4 RotationView => Matrix4x4.CreateLookAt(Vector3.Zero, Forward, Up);

    /// <summary>The renderer's camera for a platform view point (metres, radians, the field of view in degrees).</summary>
    public static FreeCamera From(Presentation.ViewPoint view) => new()
    {
        Position = view.Eye * ModernMath.UnitsPerMetre,
        Near = view.NearPlane > 0f ? view.NearPlane * ModernMath.UnitsPerMetre : DefaultNear,   // first person's few centimetres
        Yaw = view.Yaw * 180f / MathF.PI,
        Pitch = Math.Clamp(view.Pitch * 180f / MathF.PI, -89f, 89f),
        VerticalFov = (view.FieldOfViewDegrees > 0 ? view.FieldOfViewDegrees : 64.37f) * MathF.PI / 180f,
    };
}
