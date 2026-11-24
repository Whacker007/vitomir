// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Presentation;

/// <summary>Which way the first-person field of view is measured: up the picture, or across it.</summary>
public enum FieldOfViewAxis
{
    /// <summary>Vertical degrees (60 to 110, 80 by default): the picture's height; a wider window shows more at the sides.</summary>
    Vertical = 0,
    /// <summary>Horizontal degrees (70 to 140, 112 by default): the picture's width; the height is worked out from its shape (<see cref="FirstPersonView.VerticalOf"/>).</summary>
    Horizontal = 1,
}

/// <summary>
/// The first-person picture's own choices (ours): how much of the head's motion the
/// viewer sees, the field of view, and the near plane. Presentation only: none of them reaches the simulation or a
/// tape, so a recording replays the same whatever the viewer has chosen, and a replay is drawn with the viewer's own.
/// <para>
/// <b>Head motion</b>, 0 to 1 (the Controls page's 0 to 100 %, default 50 %): the picture stands at the steady eye
/// (<see cref="Cameras.ViewState.Eyes"/>: the head's eyes low-passed, no bounce or sway) moved this share of the way to
/// where the pose has the head (<see cref="Cameras.ViewState.Head"/>). At 0 the view glides with the body's lean and
/// crouch; at 1 it is the head's full motion. The look's direction is the player's at every setting (no roll). As
/// public guidance asks of a game that moves its camera with the body (Xbox Accessibility Guideline 117: let the
/// player reduce or turn off camera shake, bob and sway; The Elder Scrolls Online's head-bob slider and Halo
/// Infinite's are such sliders), it can be turned down to nothing.
/// </para>
/// <para>
/// <b>The reticle does not lie.</b> The simulation's reticle, use, talk, pointer and aim measure from the steady
/// eye along the look; the picture's reticle is its centre, a ray from the drawn eye along the same look. The two rays
/// are parallel and apart by the head motion's share of the bob — the head's eyes are never more than
/// <see cref="Cameras.CameraSettings.SteadyLeash"/> (6 cm) from the steady eye — so what the reticle covers is what the
/// simulation answers, to a few centimetres at any distance.
/// </para>
/// <para>
/// <b>The field of view</b> is the viewer's (vertical, degrees; 60 to 110, default 80). The simulation picks under a
/// freed pointer as if the picture were drawn with <see cref="Cameras.CameraSettings.FirstPersonPickField"/>; the
/// program puts the pointer's place into that field (<see cref="ToPick"/>), so the pick is the ray under the cursor.
/// The viewer may instead give the field <b>across</b> (<see cref="FieldOfViewAxis.Horizontal"/>; 70 to 140, default
/// 112, which is 80 vertical at 16:9): the vertical field drawn is then worked out from the picture's own shape each
/// frame (<see cref="VerticalOf"/>), so a wider window keeps the same field across; the pick goes through that drawn
/// vertical field as before.
/// </para>
/// </summary>
public static class FirstPersonView
{
    /// <summary>The head motion when the settings say nothing (50 %).</summary>
    public const float DefaultHeadMotion = 0.5f;

    /// <summary>The first-person field of view (vertical degrees): the default (the cameras' 80) and the slider's ends.</summary>
    public const float DefaultFieldOfView = 80f, LeastFieldOfView = 60f, MostFieldOfView = 110f;

    /// <summary>
    /// The field across (horizontal degrees): the default and the slider's ends. The default is the vertical default's
    /// 80° on a 16:9 picture (2·atan(tan 40° · 16/9) = 112.3°), so switching the axis at 16:9 keeps the picture; 106°
    /// (which was asked, as "80° vertical at 16:9") is 73.7° vertical there — it is 90° across at 4:3 carried to 16:9.
    /// </summary>
    public const float DefaultHorizontalField = 112f, LeastHorizontalField = 70f, MostHorizontalField = 140f;

    /// <summary>
    /// The vertical field a field across may come to (degrees), whatever the picture's shape: at most the vertical
    /// slider's own top, 110 — never wider than the simulation's pick field (<see cref="Cameras.CameraSettings.FirstPersonPickField"/>,
    /// 110), so every place on the picture is a place in the pick field (<see cref="PickScale"/> at most 1) — and at
    /// least 20. A field across that would pass it (140 on a picture narrower than 1.92:1, such as 16:9) is drawn a little narrower.
    /// </summary>
    public const float LeastDrawnField = 20f, MostDrawnField = MostFieldOfView;

    /// <summary>
    /// The first-person picture's near plane (m): a few centimetres, so a hand or a sleeve close to the eyes on a
    /// ladder or a ledge is drawn rather than cut, and nothing is seen of the inside of the body. (Third person keeps
    /// the renderer's own.)
    /// </summary>
    public const float NearPlane = 0.03f;

    /// <summary>The drawn eye: the steady eye moved <paramref name="headMotion"/> (0 to 1) of the way to the head's eyes.</summary>
    public static Vector3 Eye(Vector3 steady, Vector3 head, float headMotion) => steady + (head - steady) * Math.Clamp(headMotion, 0f, 1f);

    /// <summary>A field of view the settings hold (degrees), kept within the slider's ends; the default for nothing sensible.</summary>
    public static float Field(float degrees) => float.IsFinite(degrees) && degrees > 0f ? Math.Clamp(degrees, LeastFieldOfView, MostFieldOfView) : DefaultFieldOfView;

    /// <summary>A field across the settings hold (degrees), kept within the slider's ends; the default for nothing sensible.</summary>
    public static float HorizontalField(float degrees) =>
        float.IsFinite(degrees) && degrees > 0f ? Math.Clamp(degrees, LeastHorizontalField, MostHorizontalField) : DefaultHorizontalField;

    /// <summary>
    /// The vertical field (degrees) a picture of <paramref name="aspect"/> (its width over its height) has when its field
    /// across is <paramref name="horizontal"/> degrees: <c>v = 2·atan(tan(h/2) / aspect)</c>, kept within
    /// <see cref="LeastDrawnField"/> to <see cref="MostDrawnField"/> (an unknown shape is read as 16:9).
    /// </summary>
    public static float VerticalOf(float horizontal, float aspect)
    {
        float a = float.IsFinite(aspect) && aspect > 0.05f ? aspect : 16f / 9f;
        float half = DetMath.Tan(HorizontalField(horizontal) * DetMath.DegToRad / 2f) / a;
        return Math.Clamp(2f * DetMath.Atan2(half, 1f) * DetMath.RadToDeg, LeastDrawnField, MostDrawnField);
    }

    /// <summary>
    /// The vertical field drawn (degrees) for the viewer's choice: <paramref name="degrees"/> up the picture
    /// (<see cref="Field"/>), or across it on a picture of <paramref name="aspect"/> (<see cref="VerticalOf"/>).
    /// </summary>
    public static float Drawn(FieldOfViewAxis axis, float degrees, float aspect) =>
        axis == FieldOfViewAxis.Horizontal ? VerticalOf(degrees, aspect) : Field(degrees);

    /// <summary>
    /// How far from the picture's centre a place drawn with <paramref name="drawnField"/> is in a picture of
    /// <paramref name="pickField"/> (both vertical degrees) seen from the same eye: the ratio of their half-fields' tangents.
    /// </summary>
    public static float PickScale(float drawnField, float pickField) =>
        DetMath.Tan(drawnField * DetMath.DegToRad / 2f) / DetMath.Tan(MathF.Max(pickField, 1f) * DetMath.DegToRad / 2f);

    /// <summary>
    /// A place on the picture (0 to 1 across and down) put into the pick field (<see cref="PickScale"/>'s
    /// <paramref name="scale"/>): the same ray. A place off the picture stays off it.
    /// </summary>
    public static Vector2 ToPick(Vector2 place, float scale) =>
        place.X is >= 0f and <= 1f && place.Y is >= 0f and <= 1f ? new Vector2(0.5f, 0.5f) + (place - new Vector2(0.5f, 0.5f)) * scale : place;

    /// <summary>A place in the pick field back on the picture (the inverse of <see cref="ToPick"/>).</summary>
    public static Vector2 FromPick(Vector2 place, float scale) =>
        scale > 1e-6f ? new Vector2(0.5f, 0.5f) + (place - new Vector2(0.5f, 0.5f)) / scale : place;
}
