// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Engine.World;

/// <summary>
/// A fly-through camera in the game's world space (+Z up). Yaw 0 looks along +X, 90° along +Y.
/// Widescreen is Hor+: the vertical field of view is fixed and wider windows see more at the sides.
/// </summary>
public sealed class FreeCamera
{
    public Vector3 Position;
    /// <summary>Degrees about +Z.</summary>
    public float Yaw;
    /// <summary>Degrees, positive looks up; clamped to ±89 by <see cref="Turn"/> (1.1's free camera keys, which
    /// wrap it, can take it past straight up: <see cref="FreeCameraKeys"/>).</summary>
    public float Pitch;
    /// <summary>
    /// Degrees about the view, 1.1's camera roll: positive leans the camera's up to its
    /// right. Only the free camera's roll keys turn it; everything else leaves it 0.
    /// </summary>
    public float Roll;
    /// <summary>
    /// Horizontal field of view at 4:3, in degrees. The original's game camera uses 80 (clamped to
    /// 0.5..120 when set) and keeps it horizontal for any window shape, which makes it Vert-.
    /// </summary>
    public float HorizontalFov43 = 80f;
    /// <summary>
    /// Clip planes. The game camera's near plane is 10; its far plane comes from the scene
    /// (RENDERINFO_ZCLIP), 10,000 when the scene leaves it at 0.
    /// </summary>
    public float Near = 10f, Far = 10000f;

    public Vector3 Forward
    {
        get
        {
            float y = Yaw * MathF.PI / 180f, p = Pitch * MathF.PI / 180f;
            return new Vector3(MathF.Cos(p) * MathF.Cos(y), MathF.Cos(p) * MathF.Sin(y), MathF.Sin(p));
        }
    }

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitZ));

    /// <summary>
    /// The camera's own axes as 1.1 builds them from its angles (<see cref="WorldCamera.FromAngles"/>):
    /// rows right, forward (<see cref="Forward"/>), up, and the position as the translation. 1.1's yaw is
    /// this <see cref="Yaw"/> less 90 (its yaw 0 looks along +Y).
    /// </summary>
    public Matrix4x4 Axes => WorldCamera.FromAngles(new Vector3(Pitch, Roll, Yaw - 90f), Position);

    /// <summary>
    /// Which way is up for the eye: +Z, unless the camera is rolled or pitched past ±89° — which only
    /// the free camera's keys do — when it is the third row of <see cref="Axes"/>.
    /// </summary>
    public Vector3 Up
    {
        get
        {
            if (Roll == 0f && MathF.Abs(Pitch) <= 89f) return Vector3.UnitZ;
            var m = Axes;
            return new Vector3(m.M31, m.M32, m.M33);
        }
    }

    /// <summary>
    /// Swings the camera round to look at a point from <paramref name="distance"/> away, keeping
    /// whichever way it is already pointing. This is what makes it a third-person camera: the
    /// player turns it with the mouse and it stays behind whoever it is following.
    /// </summary>
    public void Orbit(Vector3 target, float distance) => Position = target - Forward * distance;

    /// <summary>
    /// A fixed chase viewpoint, for tests that need to look at a character from behind and click
    /// through the picture. **Not the game's camera**: that is <see cref="WorldCamera"/>, from the
    /// game's own numbers (−30°, 128 up, 300..512 back), and play is drawn
    /// from it. These were the harness's stand-in at first.
    /// </summary>
    public const float ChaseDistance = 420f, ChaseHeight = 64f, ChasePitch = -14f;

    /// <summary>Vertical field of view in radians: the 4:3 horizontal one converted, then kept for every aspect (Hor+).</summary>
    public float VerticalFov => 2f * MathF.Atan(MathF.Tan(HorizontalFov43 * MathF.PI / 360f) * 3f / 4f);

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Position + Forward, Up);

    public Matrix4x4 Projection(float aspect) => Projection(aspect, Far);

    public Matrix4x4 Projection(float aspect, float far) => Matrix4x4.CreatePerspectiveFieldOfView(VerticalFov, aspect, Near, far);

    /// <summary>The view with the eye at the origin: for things drawn centred on the camera, like the sky.</summary>
    public Matrix4x4 RotationView => Matrix4x4.CreateLookAt(Vector3.Zero, Forward, Up);

    public Matrix4x4 ViewProjection(float aspect) => View * Projection(aspect);

    /// <summary>Moves by local axes (forward, right, up) scaled by <paramref name="distance"/>.</summary>
    public void Move(float forward, float right, float up, float distance)
    {
        Position += (Forward * forward + Right * right + Vector3.UnitZ * up) * distance;
    }

    public void Turn(float dYaw, float dPitch)
    {
        Yaw = (Yaw + dYaw) % 360f;
        Pitch = Math.Clamp(Pitch + dPitch, -89f, 89f);
    }
}
