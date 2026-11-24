// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.InteropServices;

namespace Vitomir.Platform.Assets;

/// <summary>
/// A skinned vertex, in the model's own space in metres, Z up: up to four bones with weights that sum to
/// one (a 1.1 vertex uses two: its bone and that bone's parent; the glTF bodies use four).
/// The layout is the GPU's vertex layout too (48 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct SkinVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 Uv;
    public uint Bones;      // four bone indices, one a byte, the first in the lowest byte
    public Vector4 Weights;  // not normalised beyond what the source gives; the sum is one in practice

    public readonly int Bone(int i) => (int)((Bones >> (8 * i)) & 0xff);

    public static uint PackBones(int b0, int b1 = 0, int b2 = 0, int b3 = 0) =>
        (uint)(b0 & 0xff) | (uint)(b1 & 0xff) << 8 | (uint)(b2 & 0xff) << 16 | (uint)(b3 & 0xff) << 24;
}

/// <summary>One bone: its name, its parent (−1 for a root), and its rest pose relative to the parent.</summary>
public sealed record BoneInfo(string Name, int Parent, BoneTransform Rest);

/// <summary>A bone's turn and offset relative to its parent. Matrices follow System.Numerics' row-vector convention: v × M.</summary>
public readonly record struct BoneTransform(Quaternion Rotation, Vector3 Translation)
{
    public static BoneTransform Identity => new(Quaternion.Identity, Vector3.Zero);

    /// <summary>The bone's local matrix: rotate, then translate (row vectors).</summary>
    public Matrix4x4 ToMatrix()
    {
        var m = Matrix4x4.CreateFromQuaternion(Rotation);   // exact arithmetic on the quaternion: no trigonometry
        m.Translation = Translation;
        return m;
    }
}

/// <summary>
/// A skeleton: bones with parents before children, and each bone's inverse bind matrix (model space to
/// the bone's own space in the rest pose), so a vertex is skinned as v × InverseBind[b] × Pose[b].
/// </summary>
public sealed class Skeleton
{
    public required BoneInfo[] Bones { get; init; }
    public required Matrix4x4[] InverseBind { get; init; }

    public int Count => Bones.Length;

    public int Find(string name) => Array.FindIndex(Bones, b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A run of a model's triangles drawn with one texture.</summary>
public sealed record ModelPart(int FirstIndex, int IndexCount, string Texture, bool AlphaTest)
{
    /// <summary>Drawn with both faces (a 1.1 material that says <c>twosided</c>: capes, hems).</summary>
    public bool TwoSided { get; init; }

    /// <summary>
    /// The part's full material (the glTF bodies, outfits and hair), or null for a 1.1 part, which is
    /// drawn as before from <see cref="Texture"/> alone. When set, <see cref="Texture"/> is the base colour's name too.
    /// </summary>
    public PartMaterial? Material { get; init; }
}

/// <summary>
/// A glTF metallic-roughness material as the platform draws a character's part (the models repository's integration
/// spec, section 2). Texture names are keys of the session's textures (<see cref="TextureImage.Name"/>); null for none.
/// The base colour is sRGB; the normal map (tangent space, glTF's +Y green) and the ORM map (occlusion R, roughness G,
/// metal B) are linear. The colour is factor × texture × the vertex colour when <see cref="VertexColor"/>.
/// </summary>
/// <param name="AlphaCutoff">0 for opaque; above 0 a cut-out at that alpha (glTF MASK, hair at 0.35). glTF BLEND (the
/// eyelashes) is drawn as a cut-out at 0.5 until the renderer blends characters.</param>
/// <param name="Hidden">Not drawn at all (the cornea, whose transmission the renderer does not do: the spec allows skipping it).</param>
public sealed record PartMaterial(string Name, string? BaseColor, string? Normal, string? Orm,
                                  System.Numerics.Vector4 BaseColorFactor, float Roughness, float Metallic,
                                  float AlphaCutoff, bool DoubleSided, bool VertexColor, bool Hidden = false)
{
    /// <summary>
    /// An addition: the normal map's scale (glTF's <c>normalTexture.scale</c>: the hair's and the beard's are
    /// 0.6), 1 when the file gives none.
    /// </summary>
    public float NormalScale { get; init; } = 1f;
}

/// <summary>
/// A skinned model ready for the simulation and the renderer: vertices in metres, triangles (three
/// indices each), parts by texture, the skeleton, and the box round the rest pose. Made from 1.1's
/// <c>.nod</c> models now, from the glTF bodies later.
/// </summary>
public sealed class SkinnedModel
{
    public required string Name { get; init; }
    public required SkinVertex[] Vertices { get; init; }
    public required uint[] Indices { get; init; }
    public required ModelPart[] Parts { get; init; }
    public required Skeleton Skeleton { get; init; }
    public Vector3 BoundsMin { get; init; }
    public Vector3 BoundsMax { get; init; }

    /// <summary>Each vertex's tangent (xyz, w the bitangent's sign, glTF's convention), or null (1.1's models: none).</summary>
    public Vector4[]? Tangents { get; init; }

    /// <summary>Each vertex's colour, linear RGBA (glTF COLOR_0: an outfit's wear and dye, a hair card's shade), or null.</summary>
    public Vector4[]? Colors { get; init; }
}

/// <summary>An image ready for the GPU: RGBA, eight bits a channel, rows top to bottom.</summary>
public sealed record TextureImage(string Name, int Width, int Height, byte[] Rgba);

/// <summary>
/// A motion clip: what each bone's local transform is at a time. Sampling is pure arithmetic in a fixed
/// order (no C-runtime trigonometry; <see cref="Core.DetMath"/>), so the simulation and the renderer pose
/// a body identically from the same clip and time.
/// </summary>
public interface IAnimationClip
{
    string Name { get; }

    /// <summary>Length in seconds.</summary>
    float Duration { get; }

    bool Loops { get; }

    /// <summary>Each bone's local transform at <paramref name="seconds"/> (wrapped or held by <see cref="Loops"/>); bones the clip does not move keep their rest.</summary>
    void Sample(float seconds, Skeleton skeleton, Span<BoneTransform> local);
}

/// <summary>A clip that carries the models repository's notes (<c>extras.vitomir</c> on each glTF animation).</summary>
public interface INotedClip : IAnimationClip
{
    ClipNotes Notes { get; }
}

/// <summary>A footfall in a clip's notes: which foot (the notes' own name: <c>Lwalk</c>, <c>Rrun</c>, <c>Lturn</c>, <c>Lguard</c> …) at what second.</summary>
public readonly record struct ClipFootfall(string Foot, float At)
{
    public bool Left => Foot.Length > 0 && (Foot[0] == 'L' || Foot[0] == 'l');
}

/// <summary>
/// The notes the character models carry on each motion, the fields the platform reads; <see cref="Json"/> keeps the whole object for the rest (a blow's
/// <c>fight.marks</c>, <c>contacts</c>, <c>takes</c> …). A missing number is 0 (or NaN for <see cref="HeadingDeg"/> and
/// <see cref="TurnDeg"/>), a missing flag false, a missing text "".
/// </summary>
public sealed record ClipNotes
{
    public string Kind { get; init; } = "";
    public string Style { get; init; } = "";
    public string Carry { get; init; } = "";
    public bool Loops { get; init; }
    public float Duration { get; init; }
    /// <summary>The ground speed it was made for, metres a second (<c>ground_speed_m_s</c>); 0 for none.</summary>
    public float GroundSpeed { get; init; }
    /// <summary>The way it goes from where it faces, degrees, 0 ahead, 90 its own left, −90 its right, 180 back (<c>heading_deg</c>); NaN for none.</summary>
    public float HeadingDeg { get; init; } = float.NaN;
    public bool PlayedBackwards { get; init; }
    public int Strides { get; init; }
    public float StrideM { get; init; }
    public float StepsPerMinute { get; init; }
    public IReadOnlyList<ClipFootfall> Footfalls { get; init; } = [];
    /// <summary>A turn on the spot: degrees, positive to the body's left (<c>turn_deg</c>); NaN for none.</summary>
    public float TurnDeg { get; init; } = float.NaN;
    /// <summary>The root bone carries the turn (<c>root_turns</c>).</summary>
    public bool RootTurns { get; init; }
    /// <summary>Celerity quickens it (<c>quickens</c>).</summary>
    public bool Quickens { get; init; }
    /// <summary>The motion it must follow (<c>after</c>), "" for none.</summary>
    public string After { get; init; } = "";
    /// <summary>The hand closed round a held thing (<c>held</c>: <c>socket_hand_r</c> …), "" for none.</summary>
    public string Held { get; init; } = "";
    /// <summary>
    /// the ground the motion was made on (<c>ground</c>), or null for flat ground. Such a clip is
    /// made in its capsule's frame: the capsule going forward at <see cref="GroundSpeed"/> and up or down the ground's mean line (on stairs
    /// the line through the nosings).
    /// </summary>
    public ClipGround? Ground { get; init; }
    /// <summary>The models repository: a start's or a stop's note (<c>start_stop</c>), or null for any other motion.</summary>
    public ClipStartStop? StartStop { get; init; }
    /// <summary>Group F1: a jump's, the fall's or a landing's <c>airborne</c> note, or null for any other motion.</summary>
    public ClipAirborne? Airborne { get; init; }
    /// <summary>A landing's <c>landing</c> note, or null for any other motion.</summary>
    public ClipLanding? Landing { get; init; }
    /// <summary>Group F2: a move over or onto a thing's <c>traverse</c> note (the capsule's path), or null for any other motion.</summary>
    public ClipTraverse? Traverse { get; init; }
    /// <summary>The whole <c>extras.vitomir</c> object as written, for fields not read above.</summary>
    public string Json { get; init; } = "{}";
}

/// <summary>
/// A start's or a stop's note: <c>way</c> (<c>start</c>, <c>stop</c>), <c>foot</c> (<c>l</c>, <c>r</c>: the
/// foot that steps first, or closes last), <c>curve</c> (<c>spring</c>: the player's, on the engine's critically damped spring; <c>natural</c>: the
/// human curve, everyone else's), <c>half_life_s</c>, <c>distance_m</c>, a start's <c>exit_phase</c> or a stop's <c>enter_phase</c> (where in the
/// loop it leaves or begins, a share of the stride: <see cref="Phase"/>), <c>speed_m_s</c> (the capsule's speed every frame, 30 a second) and the
/// gait it belongs to (the notes' <c>gait</c>: <c>walk</c>, <c>run</c>); an earlier build a start's <c>prep_s</c> (<see cref="Prep"/>: the body easing
/// forward over the standing foot for so long before the first foot lifts; its speed is in <see cref="Speeds"/> like the rest, so the engine plays it
/// as any frame).
/// </summary>
public sealed record ClipStartStop(string Way, string Foot, string Curve, float HalfLife, float Distance, float Phase, float[] Speeds, string Gait, float Prep = 0f)
{
    /// <summary>
    /// The run's and the sprint's stops brake on a sliding foot — each slide's foot, its window (s from the clip's start) and how far it goes
    /// (<c>start_stop.slides</c>: the engine leaves that foot off its ground lock then); empty for every other clip.
    /// </summary>
    public ClipSlide[] Slides { get; init; } = [];

    /// <summary>When the braking foot lands and when the feet have closed (s; NaN for none).</summary>
    public float BrakeLands { get; init; } = float.NaN;
    public float FeetClosed { get; init; } = float.NaN;

    /// <summary>Whether <paramref name="left"/>'s foot is in one of its slides at <paramref name="seconds"/>.</summary>
    public bool Sliding(bool left, float seconds)
    {
        foreach (var s in Slides) if (s.Left == left && seconds >= s.From && seconds <= s.To) return true;
        return false;
    }

    public bool Start => Way == "start";
    public bool Stop => Way == "stop";
    public bool Spring => Curve == "spring";
    public bool Left => Foot == "l";

    /// <summary>The capsule's speed at <paramref name="seconds"/> into the clip (m/s), between its frames linearly; its last after its end.</summary>
    public float SpeedAt(float seconds)
    {
        if (Speeds.Length == 0) return 0f;
        float f = MathF.Max(0f, seconds) * 30f;
        int i = (int)f;
        if (i >= Speeds.Length - 1) return Speeds[^1];
        return Speeds[i] + (Speeds[i + 1] - Speeds[i]) * (f - i);
    }

    /// <summary>The first time (s) the clip's speed reaches <paramref name="speed"/> (a start's rising, a stop's falling); its end when it never does.</summary>
    public float TimeAt(float speed)
    {
        for (int i = 0; i + 1 < Speeds.Length; i++)
        {
            float a = Speeds[i], b = Speeds[i + 1];
            if ((Start && a <= speed && b >= speed) || (Stop && a >= speed && b <= speed))
                return (i + (MathF.Abs(b - a) > 1e-6f ? (speed - a) / (b - a) : 0f)) / 30f;
        }
        return Math.Max(0, Speeds.Length - 1) / 30f;
    }
}

/// <summary>One foot's slide in a stop (<see cref="ClipStartStop.Slides"/>): the foot, from and to (s), how far (m).</summary>
public readonly record struct ClipSlide(bool Left, float From, float To, float Metres);

/// <summary>
/// A motion's <c>ground</c> note: <c>kind</c> (<c>slope</c> or <c>stairs</c>), how fast the
/// capsule rises (<c>climb_m_s</c>, m/s, negative going down), a slope's grade (<c>grade_deg</c>, uphill positive), the stairs' way
/// (<c>up</c>), riser and tread (m), treads a step and how far past a nosing the capsule is at the clip's start (<c>nosing_offset_m</c>).
/// </summary>
public sealed record ClipGround(string Kind, float GradeDeg, float ClimbMs, bool Up, float RiserM, float TreadM, int TreadsAStep, float NosingOffsetM)
{
    public bool Slope => Kind == "slope";
    public bool Stairs => Kind == "stairs";
}

/// <summary>
/// A jump's, the fall's or a landing's <c>airborne</c> note (the models repository). The clips
/// are the body relative to its capsule, whose bottom is the ground it stood on or meets; the engine flies the capsule. A jump has
/// <c>takeoff_s</c> (<see cref="TakeoffS"/>: the feet leave; NaN for none), rising at <c>v_up_m_s</c> to <c>rise_m</c> at <c>apex_s</c>, its
/// <c>pelvis_at_takeoff_m</c> and <c>then</c> (<c>fall</c>: blend into the fall loop at its end; <c>hold</c>: hold its last frame until the ground is
/// met); a running jump also <c>level_landing_s</c> (it ends at the moment of landing on level ground), <c>land</c> (the running landing that
/// begins as it ends), <c>enter</c> (<c>run</c>) and <c>enter_foot</c> (the foot it takes off from, planted at its first frame). The fall has
/// <c>then</c> <c>land</c>; a landing <c>after</c> (what it follows).
/// </summary>
public sealed record ClipAirborne(float TakeoffS, float ApexS, float RiseM, float VUpMs, string Then, float PelvisAtTakeoffM, float LevelLandingS,
                                  string Land, string Enter, string EnterFoot, string After)
{
    /// <summary>
    /// A clip that leaves the ground with a stride (<c>jump_run_&lt;f&gt;</c>, <c>run_off_&lt;f&gt;</c>, <c>walk_off_&lt;f&gt;</c>): <c>air_s</c>, the part
    /// of it in the air, from the take-off to the pose ready to land on the other foot (s; NaN for none). Its <see cref="Then"/> names the pedalling
    /// loop that carries the stride on (<c>fall_run</c>, <c>fall_walk</c>).
    /// </summary>
    public float AirFrom { get; init; } = float.NaN;
    public float AirTo { get; init; } = float.NaN;
    /// <summary>A pedalling loop's <c>loop</c> (<c>pedal</c>), its <c>half_s</c> (half its length) and <c>lands</c> (the phase, a share of the
    /// loop, at which it is ready to land on the right and on the left foot; NaN for none).</summary>
    public string Loop { get; init; } = "";
    public float HalfS { get; init; } = float.NaN;
    public float LandsR { get; init; } = float.NaN;
    public float LandsL { get; init; } = float.NaN;
    /// <summary>A walk-off's or a run-off's <c>enter_phase</c> (the gait's frame it starts from, a share of the stride; NaN for none).</summary>
    public float EnterPhase { get; init; } = float.NaN;
    /// <summary><c>after</c> as a list (a landing's: what it follows; the running and walking landings name several).</summary>
    public IReadOnlyList<string> AfterAll { get; init; } = [];
    /// <summary>A prelanding's <c>prelanding</c> (the last moment of a fall before a standing landing), or null for any other clip.</summary>
    public ClipPrelanding? Prelanding { get; init; }
    /// <summary>Whether it leaves with a stride carried on in the air (<see cref="AirFrom"/> to <see cref="AirTo"/> and a pedalling loop after it).</summary>
    public bool Strides => !float.IsNaN(AirFrom) && AirTo > AirFrom;
    /// <summary>Whether it is a pedalling loop (<c>fall_run</c>, <c>fall_walk</c>).</summary>
    public bool Pedals => Loop == "pedal" && HalfS > 0f;
    /// <summary>A pedalling loop's phase ready to land on a foot (1 left, 2 right).</summary>
    public float Lands(int foot) => foot == 1 ? LandsL : LandsR;

    /// <summary>Whether it is a jump (it has a take-off and an apex after it).</summary>
    public bool Jump => !float.IsNaN(TakeoffS) && ApexS > TakeoffS;
    /// <summary>Whether it holds its last frame until the ground is met (<c>then</c> <c>hold</c>), else blends into the fall loop (<c>fall</c>).</summary>
    public bool Holds => Then == "hold";
    /// <summary>Whether it is a running jump (<c>enter</c> <c>run</c>, off one foot).</summary>
    public bool Running => Enter == "run" && EnterFoot is "l" or "r";
}

/// <summary>
/// A prelanding's <c>airborne.prelanding</c> note (the models repository): <c>lead_s</c> (<see cref="LeadS"/>: it is played from its first frame
/// when the time to contact is this long; with less time left, faster to fit, at <c>lead_s</c> over the time to contact, never begun part way in),
/// <c>from</c> (the loop it is blended in from at any phase: <c>fall</c>), <c>then</c> (the standing landing whose first frame is its last: <c>land_soft</c>,
/// <c>land_high</c>, <c>land_high_player</c>) and <c>start</c> (the rule in words).
/// </summary>
public sealed record ClipPrelanding(float LeadS, string From, string Then, string Start);

/// <summary>
/// A landing's <c>landing</c> note: <c>drop_m</c> (<see cref="DropLow"/> to <see cref="DropHigh"/>: the drops it is for, m),
/// <c>contact_s</c> (its first frame the moment of contact, the toes touching), <c>heels_down_s</c>, a standing landing's <c>deepest_s</c> (NaN for
/// none), a running one's <c>onto</c> (the foot it lands on), <c>exit_into</c> (<c>run</c>) and <c>exit_phase</c> (where in the run its last frame
/// falls, a share of the stride).
/// </summary>
public sealed record ClipLanding(float DropLow, float DropHigh, float ContactS, float HeelsDownS, float DeepestS, string Onto, string ExitInto, float ExitPhase)
{
    /// <summary>Who it is for (<c>who</c>: <c>player</c> for the player's standing high landing), "" for everyone.</summary>
    public string Who { get; init; } = "";
    /// <summary>A walking landing's <c>ground_speed_m_s</c> (the walk's), 0 for none.</summary>
    public float GroundSpeed { get; init; }
    /// <summary>Whether it lands walking on (<c>exit_into</c> <c>walk</c>).</summary>
    public bool Walking => ExitInto == "walk";

    /// <summary>Whether it lands running on (onto one foot, into the run).</summary>
    public bool Running => ExitInto == "run" || Onto is "l" or "r";
    /// <summary>Whether <paramref name="drop"/> (m) is within its band.</summary>
    public bool For(float drop) => drop >= DropLow - 1e-4f && drop <= DropHigh + 1e-4f;
}

/// <summary>
/// A move over or onto a thing (the models repository,; <c>traverse</c>): the clip moves its capsule.
/// <see cref="Path"/> is a row a frame (<c>[t, forward_m, up_m]</c>: the capsule's displacement from where it stood at the first frame; it follows the
/// pelvis, never below where it started) and the clip is the body relative to that moving capsule. <c>obstacle</c>: <see cref="FaceM"/> (how far ahead
/// of the capsule's centre the near face stood at the first frame), <see cref="TopM"/>, <see cref="DepthM"/> (NaN: <c>deep</c>), <see cref="Thin"/> (a
/// vault's), <see cref="Low"/> to <see cref="High"/> (<c>height_range_m</c>: the heights it serves). <see cref="Hands"/> (each hand put on the top: when,
/// where). <c>warp</c>: <see cref="WarpFrom"/> to <see cref="WarpTo"/> (the window in which the path is stretched so the hands meet the real edge; NaN
/// for none). A vault's <c>enter</c>, <c>enter_foot</c>, <c>enter_phase</c>, <c>speed_m_s</c> (<c>[in, through, out]</c>), <c>left_lands_s</c>,
/// <c>exit_into</c>, <c>exit_phase</c>; a climb's or a mantle's <c>ends</c> (<c>stand on the top</c>).
/// </summary>
public sealed record ClipTraverse(float[] Times, float[] Forward, float[] Up, float FaceM, float TopM, float DepthM, bool Thin, float Low, float High,
                                  IReadOnlyList<ClipHand> Hands, float WarpFrom, float WarpTo)
{
    public string Enter { get; init; } = "";
    public string EnterFoot { get; init; } = "";
    public float EnterPhase { get; init; } = float.NaN;
    /// <summary>The capsule's speed going in, through and out (m/s; empty for none).</summary>
    public IReadOnlyList<float> Speeds { get; init; } = [];
    public float LeftLandsS { get; init; } = float.NaN;
    public string ExitInto { get; init; } = "";
    public float ExitPhase { get; init; } = float.NaN;
    public string Ends { get; init; } = "";
    /// <summary>
    /// Group F3: a shimmy's fourth number a row, the capsule's sideways move from where it stood (m, positive to the
    /// body's left); empty for a path of three.
    /// </summary>
    public float[] Side { get; init; } = [];
    /// <summary>A ledge clip's <c>hang</c> (where the capsule hangs from the edge: the place its path is relative to), or null.</summary>
    public ClipHang? Hang { get; init; }
    /// <summary>A grab's <c>grab</c> (how it catches the edge), or null.</summary>
    public ClipGrab? Grab { get; init; }
    /// <summary>A shimmy's <c>shimmy</c> (its way, its speed and its step a cycle), or null.</summary>
    public ClipShimmy? Shimmy { get; init; }
    /// <summary>The hang a looping clip starts and ends its cycle in (<c>loops_from</c>: a shimmy's <c>hang_free</c> or <c>hang_braced</c>), "" for none.</summary>
    public string LoopsFrom { get; init; } = "";
    /// <summary>A low deep top's vault's <c>obstacle.depth_range_m</c> (the depths it serves, m), NaN for none.</summary>
    public float DepthLow { get; init; } = float.NaN;
    public float DepthHigh { get; init; } = float.NaN;
    /// <summary>A ladder clip's <c>traverse.ladder</c> (its ladder's numbers), or null.</summary>
    public ClipLadder? Ladder { get; init; }
    /// <summary>A ladder clip's <c>traverse.top</c> (the floor at the top), or null.</summary>
    public ClipLadderTop? Top { get; init; }
    /// <summary>A ladder clip's <c>traverse.landing</c> (a landing beside the ladder at a middle level), or null.</summary>
    public ClipLadderLanding? Landing { get; init; }
    /// <summary>A ladder clip's <c>traverse.hang</c> read as a ladder's (the braced ledge beside the ladder), or null.</summary>
    public ClipLadderHang? LadderHang { get; init; }
    /// <summary>A ladder climb's <c>climb</c> (its rungs a cycle and its way), or null.</summary>
    public ClipLadderClimb? Climb { get; init; }
    /// <summary>The clip a getting-off starts from (<c>starts</c>: the set's idle), "" for none.</summary>
    public string Starts { get; init; } = "";
    /// <summary>A hang leap's <c>traverse.leap</c> (from a braced hang to the next ledge), or null.</summary>
    public ClipLeap? Leap { get; init; }
    /// <summary>A ledge corner's <c>traverse.corner</c> (hand over hand round a corner of the lip), or null.</summary>
    public ClipCorner? Corner { get; init; }
    /// <summary>A clip on a wall climbed by its holds' <c>traverse.wall</c> (the wall position, the holds), or null.</summary>
    public ClipWall? Wall { get; init; }
    /// <summary>A squeeze's or a cover's <c>traverse.side_step</c> (side-stepping along a wall), or null.</summary>
    public ClipSideStep? SideStep { get; init; }
    /// <summary>Whether it ends standing on the top (a climb, a mantle; a hop up, whose path ends on it).</summary>
    public bool EndsOnTop => Ends.Contains("top", StringComparison.Ordinal) || (!Thin && Up.Length > 0 && Up[^1] > 0.5f * TopM);
    /// <summary>The first hand's contact (s), NaN for none.</summary>
    public float ContactS => Hands.Count > 0 ? Hands.Min(h => h.ContactS) : float.NaN;
}

/// <summary>
/// A ladder clip's <c>traverse.ladder</c> (the models repository): the rungs' spacing along the rails (<c>rung_m</c>), the lean off
/// upright (<c>tilt_deg</c>), rung 1's height over the ground at the foot (<c>first_rung_m</c>), rung 1's front ahead of and its top above the capsule's bottom at
/// the ladder position (<c>rung_fwd_m</c>, <c>lower_rung_up_m</c>), the hands' spacing and the clip's kind (<c>idle</c>, <c>climb</c>, <c>on bottom</c> ...).
/// </summary>
public sealed record ClipLadder(float RungM, float TiltDeg, float FirstRungM, float RungFwdM, float LowerRungUpM, float HandsApartM, string Kind);

/// <summary>
/// A ladder clip's <c>traverse.top</c>: the rung level with the floor (counted from rung 1 at the ladder position), the floor's height over
/// the capsule's bottom there, its edge and where the capsule stands on it (forward, m), whether the rails go on above it (<c>rails</c>).
/// </summary>
public sealed record ClipLadderTop(int Rung, float FloorUpM, float LipFwdM, float StandFwdM, bool Rails);

/// <summary>A ladder clip's <c>traverse.landing</c>: the side (<c>l</c>, <c>r</c>), the rung level with it, its floor's height and where the capsule stands on it (left and forward, m).</summary>
public sealed record ClipLadderLanding(string To, int Rung, float FloorUpM, float StandLeftM, float StandFwdM);

/// <summary>
/// A ladder clip's <c>traverse.hang</c> (from a ladder into a braced hang on a ledge beside it, and back): the side, the hang's kind, the rung
/// whose top the ledge's is level with, the hang's middle on the edge to the left of, the edge ahead of and its top over the ladder position's capsule bottom
/// (m), the hands' places along the edge (the near one first).
/// </summary>
public sealed record ClipLadderHang(string To, string Kind, int Rung, float EdgeLeftM, float EdgeFwdM, float EdgeUpM, float[] HandsLeftM);

/// <summary>A ladder climb's <c>climb</c>: the rungs a cycle, a step's time, its way (<c>up</c>, <c>down</c>) and the loop it starts and ends in.</summary>
public sealed record ClipLadderClimb(int Rungs, float StepS, string Way, string LoopsFrom);

/// <summary>A hand put on the top in a move over or onto a thing (<c>traverse.hands</c>): which, from when to when (s), where (<c>at_m</c>: forward and up
/// from where the capsule stood, the palm's point; <c>left_m</c>: from the body's middle, positive to its left).</summary>
public sealed record ClipHand(string Hand, float ContactS, float ReleaseS, float AtForward, float AtUp, float Left);

/// <summary>
/// A ledge clip's <c>traverse.hang</c> (the models repository): in the hang the capsule stands <see cref="EdgeFwd"/> back from the
/// ledge's edge and <see cref="EdgeUp"/> below its top (m; <c>edge_fwd_m</c>, <c>edge_up_m</c>), and the clip's <c>traverse.path</c> is relative to that place;
/// <see cref="Kind"/> (<c>free</c>: the legs down; <c>braced</c>: the feet on the wall; a brace's <c>free to braced</c>, <c>braced to free</c>); the hands
/// (<c>hands</c>: <see cref="HandsAt"/>, the point of the hand meant — <c>knuckles</c> —, <see cref="HandFwd"/> and <see cref="HandUp"/> from the edge, m, and
/// <see cref="HandsApart"/>).
/// </summary>
public sealed record ClipHang(float EdgeFwd, float EdgeUp, string Kind, string HandsAt, float HandFwd, float HandUp, float HandsApart)
{
    /// <summary>Whether the hang it is in, or ends in, is braced (<c>braced</c>, <c>free to braced</c>).</summary>
    public bool EndsBraced => Kind == "braced" || Kind.EndsWith("to braced", StringComparison.Ordinal);
}

/// <summary>
/// A grab's <c>traverse.grab</c>: <see cref="Start"/> (<c>stand</c>, <c>run</c>, <c>fall</c>), <c>takeoff_s</c>, <c>v_up_m_s</c>,
/// <c>contact_s</c> (the hands on the edge), <c>speed_m_s</c> (a running one's), <c>height_range_m</c> (<see cref="Low"/> to <see cref="High"/>: the edge
/// heights the jump reaches, m above the ground it starts from), a running one's <c>enter</c>, <c>enter_foot</c>, <c>enter_phase</c>; a falling one's
/// <c>fall_speed_m_s</c>, <c>starts_over_hang_m</c> and <c>starts_off_hang_m</c> (begin it when the falling capsule is that far above, and that far forward
/// of, its hang place). A number not given is NaN.
/// </summary>
public sealed record ClipGrab(string Start, float TakeoffS, float VUpMs, float ContactS, float SpeedMs, float Low, float High)
{
    public string Enter { get; init; } = "";
    public string EnterFoot { get; init; } = "";
    public float EnterPhase { get; init; } = float.NaN;
    public float FallSpeedMs { get; init; } = float.NaN;
    public float StartsOverHang { get; init; } = float.NaN;
    public float StartsOffHang { get; init; } = float.NaN;
}

/// <summary>
/// A hang leap's <c>traverse.leap</c> (the models repository's clips): where the next hang is from this one (<c>left_m</c>, <c>up_m</c>; the leap back's
/// <c>back_m</c>, its face that far behind this one's and facing it), the feet leaving the wall (<c>push_s</c>), the leading hand on the next edge (<c>contact_s</c>),
/// the leap back's <c>turn</c> (<c>left</c>), and the hangs it starts and ends on (<c>start</c>, <c>then</c>: <c>hang_braced</c>).
/// </summary>
public sealed record ClipLeap(float LeftM, float UpM, float BackM, float PushS, float ContactS, string Turn, string Start, string Then);

/// <summary>
/// A ledge corner's <c>traverse.corner</c>: <c>kind</c> (<c>outside</c>: the face turns away from the climber; <c>inside</c>: the other face comes
/// toward him), <c>way</c> (<c>left</c>, <c>right</c>), <c>corner_along_m</c> (the corner from the hang's middle along the face, the way of the move),
/// <c>ends_along_m</c> (the end's hang that far from the corner along the other face), <c>start</c> and <c>then</c> (<c>hang_braced</c>, <c>hang_free</c>).
/// </summary>
public sealed record ClipCorner(string Kind, string Way, float CornerAlongM, float EndsAlongM, string Start, string Then)
{
    public bool Outside => Kind == "outside";
    public bool Left => Way == "left";
}

/// <summary>
/// A hold of a wall clip (<c>traverse.wall.holds</c>): the limb (<c>hand_l</c>, <c>hand_r</c>, <c>foot_l</c>, <c>foot_r</c>), when it is
/// taken and let go (<c>on_s</c>, <c>off_s</c>: −1 held from before the clip, 99 past it), the hold's top middle to the left of and above the capsule's bottom
/// at the wall position (<c>left_m</c>, <c>up_m</c>); a corner's from the corner along the face the way of the move instead (<c>along_m</c>) and on which face
/// (<c>face</c>: <c>first</c>, <c>other</c>). A number not given is NaN.
/// </summary>
public sealed record ClipWallHold(string Limb, float OnS, float OffS, float LeftM, float UpM, float AlongM, string Face);

/// <summary>
/// A wall clip's <c>traverse.wall</c> (the models repository's clips): the face ahead of the capsule's bottom at the wall position (<c>face_fwd_m</c>),
/// the holds' depth (<c>hold_depth_m</c>, <c>foothold_depth_m</c>), the clip's <c>kind</c> (<c>idle</c>, <c>move</c>, <c>long</c>, <c>on</c>, <c>off</c>,
/// <c>to hang</c>, <c>corner</c>, <c>grip</c>), the knuckles from a hold's front top edge (<c>hands.fwd_m</c>, <c>up_m</c>), every hold, where the move ends
/// (<c>step_m</c>: <c>left</c>, <c>up</c>), the ground it was made over (<c>ground_up_m</c>); <c>wall_on_jump</c>'s <c>jump</c> (<c>takeoff_s</c>,
/// <c>contact_s</c>, <c>above_ground_on_m</c>), <c>wall_to_hang_braced</c>'s <c>top</c> (<c>edge_fwd_m</c>, <c>edge_up_m</c>), a long move's
/// <c>long.airborne_s</c>, the grip's <c>surface</c>. A number not given is NaN.
/// </summary>
public sealed record ClipWall(float FaceFwdM, float HoldDepthM, float FootholdDepthM, string Kind, float HandFwdM, float HandUpM, IReadOnlyList<ClipWallHold> Holds,
                              float StepLeftM, float StepUpM, float GroundUpM)
{
    public float JumpTakeoffS { get; init; } = float.NaN;
    public float JumpContactS { get; init; } = float.NaN;
    public float AboveGroundOnM { get; init; } = float.NaN;
    public float TopEdgeFwdM { get; init; } = float.NaN;
    public float TopEdgeUpM { get; init; } = float.NaN;
    public string TopThen { get; init; } = "";
    public float LongFromS { get; init; } = float.NaN;
    public float LongToS { get; init; } = float.NaN;
    public string Surface { get; init; } = "";
}

/// <summary>
/// A squeeze's or a cover's <c>traverse.side_step</c> (the models repository): its <c>kind</c> (<c>squeeze</c>, <c>cover</c>), the walls (<c>wall</c>:
/// <c>ahead and behind</c>, <c>behind</c>), the loops' step and its time (<c>step_m</c>, <c>cycle_s</c>), and from the capsule's middle the skin's reach ahead and
/// behind (<c>body_front_m</c>, <c>body_back_m</c>), the gap it needs (<c>gap_min_m</c>) or the wall behind (<c>wall_back_m</c>); <c>cover_in</c>'s
/// <c>stand_off_wall_m</c> (it starts standing that far off where the cover's capsule will be) and <c>then</c>. A number not given is NaN.
/// </summary>
public sealed record ClipSideStep(string Kind, string Wall, float StepM, float CycleS, float BodyFrontM, float BodyBackM, float GapMinM, float WallBackM,
                                  float StandOffWallM, string Then)
{
    public bool Squeeze => Kind == "squeeze";
    public bool Cover => Kind == "cover";
}

/// <summary>A shimmy's <c>traverse.shimmy</c>: <c>to</c> (<c>left</c>, <c>right</c>), <c>speed_m_s</c> and <c>step_m</c> (the sideways move a cycle).</summary>
public sealed record ClipShimmy(string To, float SpeedMs, float StepM)
{
    public bool Left => To == "left";
}
