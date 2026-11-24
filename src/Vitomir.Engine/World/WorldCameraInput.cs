// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the player's own hands do to the camera: the first-person look, the swivel, and turning it
// with the pointer at the edge of the screen (the frame input, the status bar,
// and the camera's modes).
using System.Numerics;

namespace Vitomir.Engine.World;

public sealed partial class WorldCamera
{
    /// <summary>
    /// This frame's mouse movement in mickeys (across, and
    /// down), which the first-person look turns by. The harness sets both every frame, before the
    /// tick, and zero when the mouse has not moved. The engine's are whole mickeys; a modern mouse
    /// reports fractions, and they are kept rather than rounded away.
    /// </summary>
    public float MouseDx, MouseDy;

    /// <summary>
    /// Into the first-person look, which the engine calls every frame the look key is
    /// down. Refused for a dead character. The character's body stops being drawn — the engine's
    /// <c>REFLECTION ONLY</c> render bit, which leaves the model and everything attached to it out
    /// of the view — and the pointer goes away.
    /// </summary>
    public bool EnterLook()
    {
        if (Mode == CameraMode.Look || Subject is not { } thing) return false;
        if (thing.Type is ThingType.Actor or ThingType.Player && thing.Actor is { Dead: true }) return false;
        thing.RenderFlags |= RenderFirstPerson;
        CursorShown = false;
        Mode = CameraMode.Look;
        return true;
    }

    /// <summary>
    /// Back to the game camera, from anything but the game camera itself: the engine
    /// calls it when the look key comes up, or when the character dies while it is down. Follow
    /// the steered character again, pitch back to −30°, the fov back to 80, the distance back to
    /// its near end — at once, not eased — and the screen and the pointer back.
    ///
    /// <b>It clears the character's <c>THING_RENDERFLAG_DONTRENDER</c> as well as the first-person
    /// bit</b> (the mask is <c>0xffffff7d</c>), so a character a script had made invisible comes
    /// back into view the first time the player looks round with Z. Copied.
    /// </summary>
    public bool BackToGame(WorldThing? steered)
    {
        if (Mode == CameraMode.Game || steered is null) return false;
        steered.RenderFlags &= ~(RenderFirstPerson | RenderDontRender);
        SetSubject(steered);
        Angles.X = Wrap(StartPitch);
        Fov = FovTarget = DefaultFov;
        Distance = DistanceTarget = NearDistance;
        CursorShown = true;
        Mode = CameraMode.Game;
        return true;
    }

    /// <summary>
    /// **Whether Space skips the line being spoken** in this camera mode (the frame input, where each mode
    /// reads its own keys): the game camera does (inside its key handling), a camera path does
    /// (the check sits after the `-devmode` block, which jumps *to* it, not past it), and
    /// a cutscene does. The first-person look and the free camera read no Space. Until then
    /// Vitomir had the path mode the other way round — a misreading of that jump — so the Knight of
    /// St John's lines outside the convent could not be hurried, which they can in 1.1.
    ///
    /// </summary>
    public bool SpaceSkipsLine => Mode is CameraMode.Game or CameraMode.Path or CameraMode.Cutscene;

    /// <summary><c>THING_RENDERFLAG_DONTRENDER</c>: the thing is not drawn at all (the thing draw returns at once).</summary>
    public const uint RenderDontRender = 0x2;

    /// <summary>
    /// <c>THING_RENDERFLAG_REFLECTION ONLY</c>, which is the bit the look sets: the model and
    /// everything attached to it are left out of the view, and nothing else about the thing is.
    /// </summary>
    public const uint RenderFirstPerson = 0x80;

    /// <summary>
    /// The swivel: while <b>X</b>, <b>numpad 5</b> or the fifth mouse button is held
    /// the pointer stays where it is and the mouse turns the game camera instead — half a degree
    /// per mickey, the pitch held to the game camera's own limits (the upper one ours, <see cref="PitchTop"/>) and the yaw free.
    /// </summary>
    public void Swivel(float dx, float dy)
    {
        float pitch = dy * -0.5f + Angles.X;
        if (pitch < PitchLow) pitch = PitchLow;
        else if (PitchTop < pitch) pitch = PitchTop;
        Angles.X = Wrap(pitch);
        Angles.Z = Wrap(Wrap(dx * -0.5f) + Angles.Z);
    }

    /// <summary>
    /// The screen's four edge strips, in the interface's 640×480: the bottom and top
    /// two pixels turn the pitch, the right four and left two the yaw. Each is x, y, width, height,
    /// and a point on the far edge counts.
    /// </summary>
    public static readonly (int X, int Y, int W, int H)[] PitchEdges = [(0, 478, 640, 2), (0, 0, 640, 2)];

    /// <inheritdoc cref="PitchEdges"/>
    public static readonly (int X, int Y, int W, int H)[] YawEdges = [(638, 0, 4, 480), (0, 0, 2, 480)];

    /// <summary>
    /// Turning the camera with the pointer against the edge of the screen. At the top or
    /// bottom, moving the mouse up and down adds to the pitch rate — only with the Control menu's
    /// <i>Enable Mouse Pitch</i> (<c>MousePitch</c>) on; at either side, moving it across adds to
    /// the yaw rate, always. The rate goes up by one degree a second per mickey.
    ///
    /// Answers whether the pointer is in a strip, which is what decides whether the turn rates are
    /// kept into the next frame: the engine only zeroes them when the
    /// pointer is <em>not</em> at an edge, so a push against the edge <b>keeps turning</b> the
    /// camera until the pointer comes away from it.
    /// </summary>
    public bool EdgeTurn(int uiX, int uiY, float dx, float dy, bool mousePitch)
    {
        bool at = false;
        if (In(PitchEdges, uiX, uiY))
        {
            if (mousePitch && dy != 0) TurnPitch(-dy);
            at = true;
        }
        if (In(YawEdges, uiX, uiY))
        {
            if (dx != 0) TurnYaw(-dx);
            at = true;
        }
        return at;
    }

    static bool In((int X, int Y, int W, int H)[] strips, int x, int y)
    {
        foreach (var (sx, sy, w, h) in strips)
            if (sx <= x && x <= sx + w && sy <= y && y <= sy + h) return true;
        return false;
    }
}
