// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Animation;

/// <summary>
/// The motion a kind of character plays for one <see cref="MotionRole"/>: a clip of
/// <see cref="CharacterAssets"/> (−1 for none), the ground speed it was drawn for (metres a second, 0 when
/// it cannot be measured; <see cref="Stride"/>), where in it to start, whether its rate is rolled once
/// when it starts (1.1's <c>variable</c> flag), whether it starts without a fade (1.1's <c>nofadein</c>), and whether
/// it is a stand-in composed by the platform rather than a real motion (<see cref="Composed"/>).
/// </summary>
/// <param name="Composed">
/// The slot holds a composition (<see cref="ComposedClip"/>, or for <see cref="MotionRole.CrouchWalk"/> the walk that
/// the <see cref="PoseRig"/> lowers), not a motion made for it: a real motion put in the slot replaces it, and the rig
/// then leaves the motion's own hips and knees alone.
/// </param>
public readonly record struct RoleMotion(int Clip, float NaturalSpeed, float StartSeconds, bool Variable, bool NoFadeIn = false, bool Composed = false)
{
    public static RoleMotion None => new(-1, 0f, 0f, false);

    public bool Has => Clip >= 0;
}

/// <summary>
/// A kind of character: its model (an index into <see cref="CharacterAssets"/>), the motion it plays for
/// each role, and its head for first person. Townsfolk of one kind share one of these.
/// </summary>
public sealed class CharacterKind
{
    public required string Name { get; init; }

    /// <summary>The model, an index into <see cref="CharacterAssets.Model"/>.</summary>
    public required int Model { get; init; }

    /// <summary>The motion for each role, indexed by <see cref="MotionRole"/>.</summary>
    public required RoleMotion[] Motions { get; init; }

    /// <summary>What first person hides (<see cref="HeadCollapse"/>), or null when the kind has no known head.</summary>
    public HeadCollapse? Head { get; init; }

    /// <summary>The hips, legs and arms the <see cref="PoseRig"/> places by IK, or null for a kind posed as its motions alone.</summary>
    public LimbMap? Limbs { get; init; }

    /// <summary>
    /// The way the model faces in its own space, as a yaw (radians, counter-clockwise from +X seen from
    /// above): 1.1's characters face +Y (their walks carry a planted foot back along −Y), π/2.
    /// </summary>
    public float FacingYaw { get; init; } = Core.DetMath.HalfPi;

    /// <summary>How much bigger the model is drawn than it is made (a 1.1 template's <c>MODELSCALE</c>), 1 for none.</summary>
    public float Scale { get; init; } = 1f;

    /// <summary>What its action channel plays — attacks, the block, hurt and death — or null for a kind that does none.</summary>
    public ActionSet? Actions { get; init; }

    /// <summary>The things it holds, drawn and placed on its pose (a weapon, a shield), each once; empty for none.</summary>
    public IReadOnlyList<HeldModel> Held { get; init; } = [];

    /// <summary>
    /// What a kind on the models repository's bodies has beyond the rest (<see cref="People.PersonMotion"/>: the
    /// eight directions of its walk and run, its turns on the spot, its spine and root for the legs' turn, the twist bones'
    /// rule), or null — every 1.1 kind, posed exactly as before.
    /// </summary>
    public People.PersonMotion? Person { get; init; }

    /// <summary>
    /// The world matrix (row vectors) that stands this kind's model with its feet at <paramref name="feet"/>
    /// (metres, Z up), facing <paramref name="yaw"/>: a turn about Z by the yaw less <see cref="FacingYaw"/>,
    /// the scale, then the place. <see cref="Core.DetMath"/> only, so the simulation can use it for hit volumes.
    /// </summary>
    public System.Numerics.Matrix4x4 Placement(System.Numerics.Vector3 feet, float yaw)
    {
        var (s, c) = Core.DetMath.SinCos(yaw - FacingYaw);
        float k = Scale;
        return new System.Numerics.Matrix4x4(c * k, s * k, 0, 0, -s * k, c * k, 0, 0, 0, 0, k, 0, feet.X, feet.Y, feet.Z, 1);
    }

    /// <summary>The motion for a role, or <see cref="RoleMotion.None"/>.</summary>
    public RoleMotion Motion(MotionRole role) => (int)role < Motions.Length ? Motions[(int)role] : RoleMotion.None;
}

/// <summary>
/// The models, clips and kinds of character the simulation's characters refer to by index (a service,
/// not digested: references, not state; design principle 3 and the brief's rule that components hold no
/// references). Filled between ticks — at load, or when a loader thread hands over — and never emptied
/// while a world runs, so an index once given stays good. Not safe for adding from several threads at
/// once.
/// </summary>
public sealed class CharacterAssets
{
    readonly List<SkinnedModel> models = [];
    readonly List<IAnimationClip> clips = [];
    readonly List<CharacterKind> kinds = [];

    public int ModelCount => models.Count;
    public int ClipCount => clips.Count;
    public int KindCount => kinds.Count;

    /// <summary>The most bones any model added has.</summary>
    public int MaxBones { get; private set; }

    /// <summary>
    /// An action played on a moving body keeps its upper body turned as the action's own hips turn it
    /// (<see cref="ActionHips"/>); off (false) is every batch before: the upper body over the walk's hips. The session sets it with the
    /// enhanced module's switch <c>blows-on-moving-hips</c> (the session sets it on unless that switch is given).
    /// </summary>
    public bool CarryActionHips { get; set; }

    /// <summary>
    /// A person's skirt hung from the thighs as before — each half rigid with its own thigh, the hem opening between them
    /// (<see cref="People.SkirtRule"/>) — rather than carried by both thighs. Only swing bones move (no hit volume, nothing simulated). The
    /// session sets it from the setting <c>skirtSplit</c> and <c>--skirt-split</c>; false by default.
    /// </summary>
    public bool PersonSkirtSplit { get; set; }

    /// <summary>
    /// How the people's swing bones are simulated (<see cref="People.PersonSwingSettings"/>; on by default). Read when a world is built
    /// (<see cref="AnimationRegistration.Animation"/>): off (<see cref="People.PersonSwingSettings.Off"/>, <c>--swing-off</c>), a world has
    /// no simulator, as before. Presentation only.
    /// </summary>
    public People.PersonSwingSettings PersonSwing { get; set; } = People.PersonSwingSettings.Default;

    /// <summary>
    /// A person's turn as a chain drawn with the earlier build's waist (<see cref="People.TurnChain"/>: the spine turned evenly and slid on the
    /// hips so its top stays put) rather than the earlier build's (the turn spread mostly over the chest, the spine left on the pelvis). The session
    /// sets it from <see cref="LegTurnSettings.WaistEven"/> (<c>--turn-waist-even</c>, a tape's note); false by default.
    /// </summary>
    public bool PersonWaistEven { get; set; }

    public int AddModel(SkinnedModel model)
    {
        models.Add(model);
        MaxBones = Math.Max(MaxBones, model.Skeleton.Count);
        return models.Count - 1;
    }

    public int AddClip(IAnimationClip clip)
    {
        clips.Add(clip);
        return clips.Count - 1;
    }

    public int AddKind(CharacterKind kind)
    {
        if ((uint)kind.Model >= (uint)models.Count) throw new ArgumentException($"{kind.Name}: no model {kind.Model}");
        foreach (var m in kind.Motions)
            if (m.Clip >= clips.Count) throw new ArgumentException($"{kind.Name}: no clip {m.Clip}");
        if (kind.Actions is { } actions)
            foreach (var m in actions.Quick.Append(actions.Strong).Append(actions.Block).Append(actions.Hurt).Append(actions.DeathQuick).Append(actions.DeathSlow))
                if (m.Clip >= clips.Count) throw new ArgumentException($"{kind.Name}: no clip {m.Clip} for an action");
        kinds.Add(kind);
        return kinds.Count - 1;
    }

    public SkinnedModel Model(int index) => models[index];
    public IAnimationClip Clip(int index) => clips[index];
    public CharacterKind Kind(int index) => kinds[index];

    /// <summary>The model a kind of character is drawn with.</summary>
    public SkinnedModel ModelOf(int kind) => models[kinds[kind].Model];

    /// <summary>The first kind of that name (case-insensitive), or −1.</summary>
    public int FindKind(string name)
    {
        for (int i = 0; i < kinds.Count; i++)
            if (string.Equals(kinds[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
