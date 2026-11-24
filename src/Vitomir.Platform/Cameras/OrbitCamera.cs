// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: a third-person camera to watch from, swung round the player with Right Alt held.
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Cameras;

/// <summary>
/// <b>the orbit camera's numbers</b> (<see cref="OrbitCamera"/>; SANDBOX, the player's settings <c>orbit*</c>): the double tap's window, the
/// pitch's limits, the zoom's range and step, how far the mouse swings it, and how fast it eases.
/// </summary>
public sealed record OrbitCameraSettings
{
    /// <summary>A second press of the key within this (s) of the first puts the camera back behind the player (a double tap).</summary>
    public float DoubleTapSeconds { get; init; } = 0.3f;

    /// <summary>The lowest the camera's look may be tilted (degrees, looking down: the camera over the player).</summary>
    public float LowestPitchDegrees { get; init; } = -75f;

    /// <summary>The highest (degrees, looking up: the camera low behind the player).</summary>
    public float HighestPitchDegrees { get; init; } = 35f;

    /// <summary>How near and how far the wheel brings the camera (m from its pivot; the default camera's boom is 2.5).</summary>
    public float NearestMetres { get; init; } = 1.0f;
    public float FarthestMetres { get; init; } = 6.0f;

    /// <summary>A notch of the wheel (m).</summary>
    public float ZoomStepMetres { get; init; } = 0.25f;

    /// <summary>The swing for the mouse's look (a share: 1 turns the camera as far as the same motion turns the player).</summary>
    public float Speed { get; init; } = 1.0f;

    /// <summary>How long the shoulder's offset takes to go (the camera's aim coming onto the player) and to come back, and the double tap's return (s).</summary>
    public float EaseSeconds { get; init; } = 0.25f;

    public static readonly OrbitCameraSettings Default = new();
}

/// <summary>
/// <b>the camera to watch from</b>, third person only, outside the simulation (presentation: nothing here is on a tape
/// or digested; the simulation's view, its aim, the reticle's answer and the move keys stay the driving camera's, behind the player).
/// <list type="bullet">
/// <item><b>Held</b> (the action <c>orbitCamera</c>, Right Alt by default) the mouse's look swings the camera round the player — around (yaw) and
/// tilting up and down (pitch, within <see cref="OrbitCameraSettings.LowestPitchDegrees"/> to <see cref="OrbitCameraSettings.HighestPitchDegrees"/>);
/// it never rolls (the horizon level); its aim is on the player (the shoulder's offset eased away as it first moves) — and the player does not turn.</item>
/// <item><b>Let go</b>, it stays where it was put, as a turn and a tilt from the driving camera's, so swung to his left side it stays on his left
/// as he turns; the mouse and the move keys drive the player as if the camera were behind him.</item>
/// <item><b>The wheel</b> zooms (<see cref="OrbitCameraSettings.NearestMetres"/> to <see cref="OrbitCameraSettings.FarthestMetres"/>), held or not.</item>
/// <item><b>A double tap</b> (two presses within <see cref="OrbitCameraSettings.DoubleTapSeconds"/>) eases it back to the default camera.</item>
/// </list>
/// Kept out of walls as the default camera is: the pivot's offset by <see cref="CameraSystem.Shoulder"/>, the boom by <see cref="CameraSystem.BoomClear"/>
/// (in at once, out eased). Free look is as it was and adds to it.
/// </summary>
public sealed class OrbitCamera
{
    /// <summary>The binding's action.</summary>
    public const string Action = "orbitCamera";

    OrbitCameraSettings settings;
    double lastPress = double.NegativeInfinity;
    float boomNow = -1f, frameSeconds = 1f / 60f;

    /// <summary>
    /// For a still or a test: the camera put swung by <paramref name="yaw"/> and tilted by <paramref name="pitch"/> (radians) from the driving camera, at
    /// <paramref name="boom"/> m (NaN: the default's), its aim on the player (no ease).
    /// </summary>
    public void Put(float yaw, float pitch, float boom = float.NaN)
    {
        (Yaw, Pitch, Boom, Shoulder, Returning) = (DetMath.WrapAngle(yaw), pitch, boom, yaw != 0f || pitch != 0f ? 0f : 1f, false);
        boomNow = -1f;
    }

    public OrbitCamera(OrbitCameraSettings? settings = null) => this.settings = settings ?? OrbitCameraSettings.Default;

    /// <summary>Its numbers (the Controls page's OK may change them).</summary>
    public OrbitCameraSettings Settings { get => settings; set => settings = value ?? OrbitCameraSettings.Default; }

    /// <summary>
    /// The swing round the player from the driving camera's yaw (radians: the camera's look turned to the left positive, as the mouse turns the player, so
    /// the camera itself goes round to his right) and the tilt added to its pitch (radians, up positive: the camera goes below).
    /// </summary>
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }

    /// <summary>The boom's length asked for (m), or NaN for the default camera's.</summary>
    public float Boom { get; private set; } = float.NaN;

    /// <summary>How much of the shoulder's offset is kept (1: the default camera's; 0: the aim on the player).</summary>
    public float Shoulder { get; private set; } = 1f;

    /// <summary>Whether it is easing back to the default after a double tap.</summary>
    public bool Returning { get; private set; }

    /// <summary>Whether the key is held now.</summary>
    public bool Held { get; private set; }

    /// <summary>Whether the camera is the default one (nothing swung, tilted, zoomed or eased).</summary>
    public bool IsDefault => Yaw == 0f && Pitch == 0f && float.IsNaN(Boom) && Shoulder >= 1f && !Returning;

    /// <summary>Back to the default at once (a view change, a new session).</summary>
    public void Reset()
    {
        (Yaw, Pitch, Boom, Shoulder, Returning) = (0f, 0f, float.NaN, 1f, false);
        boomNow = -1f;
    }

    /// <summary>
    /// One frame of input (<paramref name="now"/>: a clock in seconds; <paramref name="seconds"/>: the frame's length): whether the key is held and went
    /// down this frame, the look gathered while it was held (the mouse's, as the player's turn takes it: x to the left, y up, radians), the wheel's
    /// notches (up positive: nearer), against the driving camera's pitch (radians) for the tilt's limits. <paramref name="allowed"/> false (first person,
    /// the pointer freed): nothing but the ease.
    /// </summary>
    public void Step(bool allowed, bool held, bool pressed, Vector2 look, float wheel, float basePitch, double now, float seconds)
    {
        var s = settings;
        frameSeconds = MathF.Max(0f, seconds);
        Held = allowed && held;
        if (allowed && pressed)
        {
            if (now - lastPress <= s.DoubleTapSeconds) { Returning = true; lastPress = double.NegativeInfinity; }
            else lastPress = now;
        }
        if (allowed && Held && look != Vector2.Zero && !Returning)
        {
            Yaw = DetMath.WrapAngle(Yaw + look.X * s.Speed);
            var (lo, hi) = Limits(basePitch);
            Pitch = Math.Clamp(basePitch + Pitch + look.Y * s.Speed, lo, hi) - basePitch;
        }
        if (allowed && wheel != 0f && !Returning)
        {
            float from = float.IsNaN(Boom) ? CameraBoomDefault : Boom;
            Boom = Math.Clamp(from - wheel * s.ZoomStepMetres, s.NearestMetres, s.FarthestMetres);
        }
        // The shoulder's offset goes as soon as the camera is swung or tilted (its aim on the player), and comes back with the default.
        float k = s.EaseSeconds > 0f ? 1f - DetMath.Exp(-seconds * 3f / s.EaseSeconds) : 1f;
        bool swung = Yaw != 0f || Pitch != 0f;
        if (Returning)
        {
            Yaw *= 1f - k; Pitch *= 1f - k;
            if (!float.IsNaN(Boom)) Boom += (CameraBoomDefault - Boom) * k;
            Shoulder += (1f - Shoulder) * k;
            if (MathF.Abs(Yaw) < 0.002f && MathF.Abs(Pitch) < 0.002f && (float.IsNaN(Boom) || MathF.Abs(Boom - CameraBoomDefault) < 0.005f) && Shoulder > 0.995f)
                Reset();
        }
        else Shoulder = swung ? Shoulder * (1f - k) : Shoulder + (1f - Shoulder) * k;
        if (Shoulder < 1e-3f) Shoulder = 0f;
        if (Shoulder > 1f - 1e-4f) Shoulder = 1f;
    }

    /// <summary>The tilt's limits (radians) about a driving pitch: the settings', widened to take the driving pitch itself (never tilted by them alone).</summary>
    (float Low, float High) Limits(float basePitch) =>
        (MathF.Min(basePitch, settings.LowestPitchDegrees * DetMath.DegToRad), MathF.Max(basePitch, settings.HighestPitchDegrees * DetMath.DegToRad));

    /// <summary>The default camera's boom (m) the wheel starts from (<see cref="CameraSettings.Boom"/>; set by the session from its camera's numbers).</summary>
    public float CameraBoomDefault { get; set; } = new CameraSettings().Boom;

    /// <summary>
    /// The view from the camera, for a player whose head (the pivot's root: the driving camera's) is at <paramref name="head"/>, the driving camera
    /// looking along <paramref name="yaw"/> and <paramref name="pitch"/> (radians) over its shoulder <paramref name="offset"/> (the right of that yaw and
    /// up, m) from <paramref name="boom"/> m: swung and tilted, the shoulder's offset by <see cref="Shoulder"/>, zoomed, and kept out of the level as the
    /// default camera is (the boom out after a wall eased over the frames <see cref="Step"/> was given).
    /// </summary>
    public ViewPoint View(CollisionWorld level, CameraSettings camera, Vector3 head, float yaw, float pitch, Vector3 offset, float boom, float fieldOfView)
    {
        float seconds = frameSeconds;
        float y = DetMath.WrapAngle(yaw + Yaw);
        var (lo, hi) = Limits(pitch);
        float p = Math.Clamp(pitch + Pitch, lo, hi);
        // The shoulder's offset turned with the swing (it stays over the same shoulder of the camera), by the share kept.
        var (sd, cd) = DetMath.SinCos(Yaw);
        var turned = new Vector3(offset.X * cd - offset.Y * sd, offset.X * sd + offset.Y * cd, offset.Z) * Shoulder;
        var walls = CameraSystem.Shoulder(level, head, turned, camera);
        var rise = new Vector3(0f, 0f, turned.Z);
        var pivot = head + rise * walls.HardRise + (turned - rise) * walls.HardSide;
        float want = float.IsNaN(Boom) ? boom : Boom;
        var dir = -DetMath.Direction(y, p);
        float clear = CameraSystem.BoomClear(level, pivot, dir * want, camera.ProbeRadius, camera.CornerGive);
        // In at once (never in a wall), out eased.
        float k = camera.BoomOutSeconds > 0f ? 1f - DetMath.Exp(-seconds / camera.BoomOutSeconds) : 1f;
        boomNow = boomNow < 0f || clear < boomNow ? clear : boomNow + (clear - boomNow) * k;
        return new ViewPoint(pivot + dir * boomNow, y, p, fieldOfView);
    }
}
