// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The free camera's own keys, as in 1.1; the driving's handling reworked to a
// design of its own (the camera's own axes, the drag look, level view).
using System.Numerics;

namespace Vitomir.Engine.World;

/// <summary>Which of the free camera's commands are held this frame: the host reads the key bindings into it (ours).</summary>
[Flags]
public enum FreeKeys
{
    None = 0,
    Forward = 1 << 0, Back = 1 << 1, Left = 1 << 2, Right = 1 << 3, Up = 1 << 4, Down = 1 << 5,
    TurnLeft = 1 << 6, TurnRight = 1 << 7, LookDown = 1 << 8, LookUp = 1 << 9,
    RollLeft = 1 << 10, RollRight = 1 << 11,
    /// <summary>Held; the level acts on the frame it goes down (<see cref="FreeCameraKeys"/>).</summary>
    Level = 1 << 12,
    /// <summary>1.1's Shift (either Shift, set at the top of the frame input); Vitomir's is the driving's run key.</summary>
    Fast = 1 << 13,
    /// <summary>Ours, <c>[qol] driving</c> only: the view level with the horizon, on the frame it goes down.</summary>
    LevelView = 1 << 14,
    /// <summary>Ours, <c>[qol] driving</c> only: the drag look held, so the mouse's movement turns and tilts the view.</summary>
    Drag = 1 << 15,
}

/// <summary>
/// **The free camera's keys** (camera mode 3): the camera flies where the
/// keys say, from where it was and the way it was looking (the mode's update only builds the
/// matrix from the eye and the angles as they stand).
///
/// **1.1's steps** (<see cref="Fly"/>, with <c>[qol] driving</c> off), as in 1.1: each key
/// held adds a fixed rate, and the sums are multiplied by the frame's length on the pausable clock
/// — no easing, no speed-up, nothing kept between frames.
/// <list type="bullet">
/// <item><b>W</b> and <b>S</b> forward and back, <b>D</b> and <b>A</b> right and left, <b>X</b> up and <b>C</b>
/// down, at <see cref="MoveRate"/> a second (<see cref="FastMoveRate"/> with Shift) — along <em>the camera's
/// own axes</em>: the move is pushed through the matrix built last frame (the move
/// on the left), so W flies the way the view points, pitch and roll included, and X is the camera's up,
/// not the world's.</item>
/// <item>The arrows turn at <see cref="TurnRate"/> a second (<see cref="FastTurnRate"/> with Shift):
/// <b>Left</b> and <b>Right</b> the yaw, and <b>Up</b> tips the view <em>down</em>, <b>Down</b> up — a plane's
/// stick — and the keypad's <b>/</b> and <b>*</b> roll it left and right. The three go to the camera
/// through the angle set, which wraps each into 0..360: nothing holds the pitch, so the camera can
/// loop right over.</item>
/// <item><b>Home</b>, on the frame it goes down (its stamp), levels the roll, before
/// the turn is added. It also writes 0 into the yaw's copy beside the angles, where
/// the roll's was meant; that copy is written again by the next frame's turn and nothing
/// draws from it, so it changes nothing to be seen.</item>
/// </list>
///
/// **Ours, with <c>[qol] driving</c> on** (<see cref="Drive"/>; *"let's have it
/// behave like the driving camera"*, and after flying it: *"I want to have the camera fly directly at
/// where it's pointing when W is used regardless of angle. S should go directly back … The up/down keys
/// can move the camera up and down relative to where it's facing."*): the keys fly it along <em>the
/// camera's own axes</em>, as 1.1's do — W and S along the view whatever the pitch, D and A along the
/// camera's right, X and C along its up, the roll included — but at driving's walk and run
/// (<see cref="Player.DefaultWalkSpeed"/>, <see cref="Player.DefaultRunSpeed"/>: a character with no speeds
/// of its own), the push made one length, so two keys are not faster than one, taken at once and let go at
/// once, as driving's speed is. The arrows turn at the driving camera's own key rate,
/// <see cref="DrivingTurnRate"/> (the game camera's 180° a second) with the same signs as
/// 1.1's, and the pitch is held to ±89° so the view never goes over the top. <b>The drag look</b>
/// (<see cref="FreeKeys.Drag"/>, the middle button): held, the mouse turns and tilts the view as the game
/// camera's swivel does under <c>[qol] camera</c> — half a degree a mickey, the sideways movement the yaw
/// and the up-and-down the pitch, the same way round (−0.5; <see cref="WorldCamera.Swivel"/>).
/// <b>Level view</b> (<see cref="FreeKeys.LevelView"/>, the keypad's 5), on the frame it goes down, puts the
/// view level with the horizon — the pitch and the roll 0, the heading kept. The roll keys and Home (the
/// roll alone) are 1.1's in both.
/// </summary>
public sealed class FreeCameraKeys
{
    /// <summary>1.1's move rate, units a second.</summary>
    public const float MoveRate = 100f;
    /// <summary>1.1's move rate with Shift.</summary>
    public const float FastMoveRate = 400f;
    /// <summary>1.1's turn rate, degrees a second.</summary>
    public const float TurnRate = 90f;
    /// <summary>1.1's turn rate with Shift.</summary>
    public const float FastTurnRate = 180f;
    /// <summary>The driving camera's key rate (ours for the free camera): the game camera's ±180 a second (<see cref="WorldCamera.TurnYaw"/>).</summary>
    public const float DrivingTurnRate = 180f;
    /// <summary>How far the driving free camera tips (ours): short of straight up or down.</summary>
    public const float DrivingPitchLimit = 89f;
    /// <summary>The drag look's degrees a mickey (ours for the free camera): the game camera's swivel's, −0.5.</summary>
    public const float DragRate = 0.5f;

    /// <summary>Whether the level key was down last frame: it acts on the frame it goes down, as its stamp does.</summary>
    bool levelWas = true;
    /// <summary>Whether the level-view key was down last frame (ours: it acts on the frame it goes down, as Home does).</summary>
    bool levelViewWas = true;

    /// <summary>
    /// One frame of the free camera's keys: 1.1's steps, or, with <paramref name="driving"/>, driving's
    /// handling. <paramref name="seconds"/> is the frame on the game's pausable clock (0 while paused);
    /// <paramref name="mouse"/> the mouse's movement this frame in mickeys, which only the driving's drag
    /// look reads.
    /// </summary>
    public void Step(FreeCamera cam, FreeKeys held, float seconds, bool driving, Vector2 mouse = default)
    {
        bool level = (held & FreeKeys.Level) != 0 && !levelWas;
        levelWas = (held & FreeKeys.Level) != 0;
        bool levelView = (held & FreeKeys.LevelView) != 0 && !levelViewWas;
        levelViewWas = (held & FreeKeys.LevelView) != 0;
        if (driving) Drive(cam, held, seconds, level, levelView, mouse);
        else Fly(cam, held, seconds, level);
    }

    /// <summary>
    /// The camera is not up: a level key held now is an old press when it comes back up, not a new one
    /// (1.1's stamp is this frame's or nothing).
    /// </summary>
    public void Rest() => levelWas = levelViewWas = true;

    static float Axis(FreeKeys held, FreeKeys plus, FreeKeys minus) =>
        ((held & plus) != 0 ? 1f : 0f) - ((held & minus) != 0 ? 1f : 0f);

    /// <summary>1.1's.</summary>
    static void Fly(FreeCamera cam, FreeKeys held, float seconds, bool level)
    {
        bool fast = (held & FreeKeys.Fast) != 0;
        float move = fast ? FastMoveRate : MoveRate, turn = fast ? FastTurnRate : TurnRate;
        // The matrix is last frame's: the level below does not rebuild it (the mode's update does, next frame).
        var m = cam.Axes;
        if (level) cam.Roll = 0f;
        var v = new Vector3(Axis(held, FreeKeys.Right, FreeKeys.Left),
                            Axis(held, FreeKeys.Forward, FreeKeys.Back),
                            Axis(held, FreeKeys.Up, FreeKeys.Down)) * move;
        float pitch = Axis(held, FreeKeys.LookUp, FreeKeys.LookDown) * turn;
        float roll = Axis(held, FreeKeys.RollRight, FreeKeys.RollLeft) * turn;
        float yaw = Axis(held, FreeKeys.TurnLeft, FreeKeys.TurnRight) * turn;
        if (v == Vector3.Zero && pitch == 0f && roll == 0f && yaw == 0f) return;
        v *= seconds;
        cam.Position += new Vector3(m.M11, m.M12, m.M13) * v.X + new Vector3(m.M21, m.M22, m.M23) * v.Y
                      + new Vector3(m.M31, m.M32, m.M33) * v.Z;
        cam.Pitch = Signed(cam.Pitch + pitch * seconds);
        cam.Roll = Signed(cam.Roll + roll * seconds);
        cam.Yaw = (cam.Yaw + yaw * seconds) % 360f;
    }

    /// <summary>
    /// Ours: driving's handling (see the class). The levels act first, then the move goes along the
    /// camera's axes as they stand, then the turns — the order 1.1's own step keeps.
    /// </summary>
    static void Drive(FreeCamera cam, FreeKeys held, float seconds, bool level, bool levelView, Vector2 mouse)
    {
        if (level) cam.Roll = 0f;
        if (levelView) cam.Pitch = cam.Roll = 0f;
        var m = cam.Axes;
        var push = new Vector3(m.M21, m.M22, m.M23) * Axis(held, FreeKeys.Forward, FreeKeys.Back)
                 + new Vector3(m.M11, m.M12, m.M13) * Axis(held, FreeKeys.Right, FreeKeys.Left)
                 + new Vector3(m.M31, m.M32, m.M33) * Axis(held, FreeKeys.Up, FreeKeys.Down);
        if (push.LengthSquared() > 1e-6f)
        {
            float speed = (held & FreeKeys.Fast) != 0 ? Player.DefaultRunSpeed : Player.DefaultWalkSpeed;
            cam.Position += Vector3.Normalize(push) * speed * seconds;
        }
        float turn = DrivingTurnRate * seconds;
        float yaw = Axis(held, FreeKeys.TurnLeft, FreeKeys.TurnRight) * turn;
        float pitch = Axis(held, FreeKeys.LookUp, FreeKeys.LookDown) * turn;
        // The drag look goes by the mouse's movement, not the clock, as the game camera's swivel does.
        if ((held & FreeKeys.Drag) != 0)
        {
            yaw -= mouse.X * DragRate;
            pitch -= mouse.Y * DragRate;
        }
        cam.Yaw = (cam.Yaw + yaw) % 360f;
        cam.Pitch = Math.Clamp(cam.Pitch + pitch, -DrivingPitchLimit, DrivingPitchLimit);
        cam.Roll = Signed(cam.Roll + Axis(held, FreeKeys.RollRight, FreeKeys.RollLeft) * turn);
    }

    /// <summary>An angle into −180..180: the same direction as 1.1's 0..360, kept signed for <see cref="FreeCamera"/>.</summary>
    static float Signed(float degrees)
    {
        float d = degrees % 360f;
        return d > 180f ? d - 360f : d <= -180f ? d + 360f : d;
    }
}
