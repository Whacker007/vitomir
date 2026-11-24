// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Presentation;

/// <summary>
/// Where the picture is seen from: the eye in metres, its yaw and pitch (radians; yaw counter-clockwise
/// from +X seen from above, pitch up positive), the vertical field of view in degrees, and the near plane in metres
/// (0 for the renderer's own: first person draws with <see cref="FirstPersonView.NearPlane"/>).
/// </summary>
public readonly record struct ViewPoint(Vector3 Eye, float Yaw, float Pitch, float FieldOfViewDegrees, float NearPlane = 0f)
{
    public Vector3 Forward => Core.DetMath.Direction(Yaw, Pitch);
}

/// <summary>
/// One character to draw: its model, where it stands (a world matrix, row vectors) and its skin matrices
/// (per bone: inverse bind × the posed bone, model space), already posed and with anything hidden from
/// this view (the own head and neck in first person) collapsed.
/// </summary>
public sealed class DrawnCharacter
{
    public required SkinnedModel Model { get; init; }
    public required Matrix4x4 World { get; set; }
    public required Matrix4x4[] Skin { get; init; }
    /// <summary>
    /// The skin the character's shadow is cast with when the picture hides part of it (the viewer's own body in
    /// first person, its head and neck hidden, its shadow whole), or null when the shadow is cast with <see cref="Skin"/>.
    /// </summary>
    public Matrix4x4[]? ShadowSkin { get; set; }
    /// <summary>The entity, for picking and diagnostics.</summary>
    public ulong Entity { get; init; }
}

/// <summary>What the reticle is on: nothing, the level, or a thing; the point and distance; and, once hit volumes exist, the part.</summary>
public readonly record struct ReticleAnswer(ReticleTarget Kind, ulong Entity, Vector3 Point, float Distance, string? Name, string? Part, bool Blocked)
{
    public static ReticleAnswer Nothing => new(ReticleTarget.Nothing, 0, default, 0, null, null, false);
}

public enum ReticleTarget { Nothing, Level, Person, Thing }

/// <summary>
/// Everything the renderer draws for one frame, made by the program from the simulation's last two
/// snapshots (design principle 6): the view, the characters, the reticle, the time of day, and a line of
/// diagnostics. The level itself is given to the renderer once, when it is loaded.
/// </summary>
public sealed class FrameView
{
    public ViewPoint View { get; set; }
    public List<DrawnCharacter> Characters { get; } = [];
    public ReticleAnswer Reticle { get; set; } = ReticleAnswer.Nothing;
    /// <summary>Whether the reticle is drawn (direct mode with the pointer captured).</summary>
    public bool ShowReticle { get; set; } = true;
    /// <summary>What a use on the reticle would do: the reticle shows talk by the person's name under it.</summary>
    public Cameras.PointerVerb ReticleVerb { get; set; }
    /// <summary>
    /// The freed pointer's cursor (null while the pointer is captured or a menu has it): drawn in place of the
    /// system's, 1.1's art by <see cref="Cursors"/>, <see cref="Cursors.Units"/> interface units square.
    /// </summary>
    public PointerCursor? Cursor { get; set; }
    /// <summary>The player's interface scale (75 % to 150 %), for the cursor's size.</summary>
    public float InterfaceScale { get; set; } = 1f;
    /// <summary>Hours since midnight for the sky and the sun, 0 to 24 (the level's own when null).</summary>
    public float? TimeOfDay { get; set; }
    /// <summary>Simulation seconds at this frame (for animated materials, clouds, water).</summary>
    public double Seconds { get; set; }
    /// <summary>A line drawn in a corner (build, frame time, sandbox warning), or null.</summary>
    public string? StatusLine { get; set; }
    /// <summary>A banner that must be seen (the rules sandbox), or null.</summary>
    public string? Banner { get; set; }
    /// <summary>
    /// The reach highlight's marks (<see cref="ReachHighlight"/>; empty when it is off): lines along the lips round
    /// the player where a move would start, drawn in the world, depth-tested, in the proving ground's colours.
    /// </summary>
    public List<ReachMark> Reach { get; } = [];

    /// <summary>
    /// The awareness overlay's marks (<see cref="AwarenessView"/>; empty when it is off): a mark over each observer's
    /// head by its awareness and its cone faintly on the ground. Presentation only.
    /// </summary>
    public List<AwarenessMark> Awareness { get; } = [];

    /// <summary>
    /// The awareness overlay's area outlines (<see cref="AwarenessView.Areas"/>; empty when it is off): each light and
    /// conceal area of the level outlined on the ground. Presentation only.
    /// </summary>
    public List<AwarenessEdge> AwarenessAreas { get; } = [];

    /// <summary>
    /// The hit volumes' overlay (<see cref="HitVolumeView"/>; empty when it is off): every character's capsules on
    /// its pose where the picture draws it, drawn as wire outlines over the body, coloured by region. Presentation only.
    /// </summary>
    public List<DrawnVolume> HitVolumes { get; } = [];

    /// <summary>
    /// The flurry's cue (<see cref="Combat.BlowCues"/>): a short flash at the struck point for each
    /// extra blow a Celerity swing carried, staggered so k blows read as k; drawn over everything. Presentation only.
    /// </summary>
    public List<Combat.BlowCueMark> Cues { get; } = [];

    /// <summary>
    /// The swing's trail (<see cref="SwingTrails"/>; empty when it is off): each recent swing's tip path and, lighter, its
    /// blade as contact swept it, the reticle's point and the tip's nearest pass to the reticle's ray; drawn over everything.
    /// Presentation only.
    /// </summary>
    public List<SwingTrailLine> SwingTrail { get; } = [];

    /// <summary>
    /// The swing's arc (<see cref="SwingArcs"/>; empty when it is off or there is no blow to show): the next blow's
    /// blade from its middle to its tip, quad by quad, where it can meet someone before the body, each corner's brightness (0 to 1)
    /// highest at the strike mark. Drawn translucent, its opacity <see cref="SwingArcOpacity"/>. Presentation only.
    /// </summary>
    public List<SwingArcQuad> SwingArc { get; } = [];

    /// <summary>The swing's arc's opacity (the setting <c>swingArcOpacity</c>, 0 to 1): what its brightest part is drawn at.</summary>
    public float SwingArcOpacity { get; set; }

    /// <summary>
    /// <i>"we might dim it substantially while the weapon is drawn"</i>: the reticle's opacity,
    /// 0 to 1 — the setting <c>meleeReticleOpacity</c> while a melee weapon or the fists are out (but while a power asks for its
    /// target), 1 otherwise (<see cref="ReticleLook"/>). The dot, the cross and their rim alike.
    /// </summary>
    public float ReticleOpacity { get; set; } = 1f;

    /// <summary>
    /// The combat log's window (<see cref="Presentation.CombatLog"/>; null when it is off): the last lines the
    /// session said about the fight, drawn in a corner by its settings, each by its strength at <see cref="Seconds"/>.
    /// Presentation only.
    /// </summary>
    public CombatLog? CombatLog { get; set; }
}
