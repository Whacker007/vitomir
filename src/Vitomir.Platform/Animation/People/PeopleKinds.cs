// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The contract's animation entry (the animation fills it; the session calls it).
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// A kind of character on the models repository's bodies: its model and motions added to the assets, every
/// <see cref="MotionRole"/> given a motion (the standard set's where it has one, the platform's compositions from its poses
/// for the rest), the eight directions and the turns on the spot, the limbs, head and hit regions by bone name.
/// </summary>
public static class PeopleKinds
{
    /// <summary>The standard set's clips the roles play (its mode names).</summary>
    public const string Stand = "stand", Walk = "walk", Run = "run";

    /// <summary>The seven ways other than ahead each gait has (<c>walk_</c> or <c>run_</c> and these).</summary>
    public static readonly IReadOnlyList<string> Ways = ["back", "left", "right", "fwd_left", "fwd_right", "back_left", "back_right"];

    /// <summary>Adds <paramref name="person"/> as a kind named <paramref name="name"/>, playing <paramref name="motions"/> (its sex's
    /// standard set), and returns the kind's index in <paramref name="assets"/>.</summary>
    /// <remarks>
    /// What the kind is:
    /// <list type="bullet">
    /// <item>The model, then <b>every clip</b> of the set in its order (later layers find them by name), then the platform's own:
    /// each gait's blends a degree round (<see cref="Gait"/>, 360 a gait), the turns played in place (<see cref="TurnSet"/>), the
    /// stand-ins (<see cref="PeopleCompositions"/>).</item>
    /// <item><b>Roles</b>: Idle <c>stand</c> (its 8 s loop; <c>idle</c> is a fidget, for later life layers); Walk and Run the
    /// gaits' blends straight ahead (<c>walk</c> drawn for its notes' 1.25 m/s, <c>run</c> for 3.0), any way by the blends (a run
    /// ahead at a sprint the set's <c>sprint</c>, drawn for 5.5, when it has one: <see cref="PersonMotion.Sprint"/>); the
    /// crouch and the crouched walk the models repository's crouch when the set has it (<see cref="PeopleCrouch"/>), else the
    /// crouch's stand-in and the walk lowered by the rig; every other slot its stand-in.</item>
    /// <item>Facing +Y (<see cref="CharacterKind.FacingYaw"/> π/2: the people are converted to 1.1's model space at load).</item>
    /// <item><b>Limbs</b> by name (<see cref="PersonRig"/>): the hips <c>pelvis</c> (not <c>root</c>), the legs <c>thigh</c>,
    /// <c>calf</c>, <c>foot</c>, <c>ball</c>, the arms <c>upperarm</c>, <c>lowerarm</c>, <c>hand</c>, <c>middle_01</c>; the
    /// shoulders' width and the ankles' height measured in the stand's first frame as 1.1's are
    /// (<see cref="LimbMap.Measured"/>).</item>
    /// <item><b>The head</b> for first person: <c>neck_01</c>, <c>head</c> and everything under it (the jaw, the eyes, and the
    /// hair's and beard's swing bones; the lashes, teeth, tongue, hair and beard are weighted to them) collapsed; <b>the eyes
    /// between <c>eye_l</c> and <c>eye_r</c></b> (their joints at rest: the eyeballs' middles).</item>
    /// <item>No action set (no combat on the new bodies: the action channel, the bow and the attack do nothing for
    /// it), nothing held; the twist bones' rule (<see cref="TwistRule"/>); the hit regions by name
    /// (<see cref="Combat.PeopleAnatomy"/>, found by <see cref="Combat.AnatomyTables.For"/>).</item>
    /// </list>
    /// Throws when the person's rig is not complete (its skeleton is not the models repository's). Call between ticks.
    /// <para><paramref name="style"/>, a townsperson's (<see cref="PersonStyle"/>): its walk one style's clip ahead (no eight
    /// ways), its stand broken by the fidget, its scale (the speeds its walk and run are drawn for by as much). Null: as before.</para>
    /// </remarks>
    public static int Register(CharacterAssets assets, PersonModel person, IReadOnlyList<INotedClip> motions, string name, PersonStyle? style = null)
    {
        var model = person.Model;
        var skeleton = model.Skeleton;
        var rig = person.Rig;
        if (!rig.Complete) throw new ArgumentException($"{name}: {person.Recipe.Key} is not on the models repository's skeleton (its rig is not complete)", nameof(person));
        int modelIndex = assets.AddModel(model);

        // Every clip of the set, in its order; the first of a name is the one the kind plays.
        var byName = new Dictionary<string, (INotedClip Clip, int Index)>(StringComparer.Ordinal);
        foreach (var clip in motions)
        {
            int index = assets.AddClip(clip);
            byName.TryAdd(clip.Name, (clip, index));
        }
        INotedClip? Named(string clip) => byName.TryGetValue(clip, out var c) ? c.Clip : null;
        var stand = Named(Stand) ?? motions.FirstOrDefault(c => c.Notes.Kind == "stand" && c.Loops)
                    ?? throw new ArgumentException($"{name}: the motions have no '{Stand}'", nameof(motions));

        var roles = new RoleMotion[Enum.GetValues<MotionRole>().Length];
        Array.Fill(roles, RoleMotion.None);
        roles[(int)MotionRole.Idle] = new RoleMotion(byName[stand.Name].Index, 0f, 0f, false);
        // A townsperson's stand broken by the set's fidget now and then.
        float scale = style is { Scale: > 0f } ? style.Scale : 1f;
        if (style is { FidgetAfter: > 0f } && Named("idle") is { } fidget)
            roles[(int)MotionRole.Idle] = new RoleMotion(assets.AddClip(new FidgetClip($"{name}:stand+idle", stand, fidget, style.FidgetAfter)), 0f, 0f, false);

        // The gaits: the eight ways' blends a degree round, or the clip ahead alone. A blend knows its two clips' places, whose
        // tables (the rig's ankles, the eyes' bob) stand for it.
        var poseClips = new List<int>();
        int IndexOf(INotedClip clip) => byName[clip.Name].Index;
        // A townsperson's walk of its own style, ahead only; its strides (and the speeds drawn for) by its scale.
        Gait? walk;
        RoleMotion walkRole;
        if (style?.Walk is { } styled && Named(styled) is { } styledWalk)
            (walk, walkRole) = (null, new RoleMotion(IndexOf(styledWalk), styledWalk.Notes.GroundSpeed, 0f, false));
        else walk = AddGait(assets, name, Walk, Named, IndexOf, poseClips, out walkRole);
        Gait? run = AddGait(assets, name, Run, Named, IndexOf, poseClips, out var runRole);
        roles[(int)MotionRole.Walk] = scale == 1f ? walkRole : walkRole with { NaturalSpeed = walkRole.NaturalSpeed * scale };
        roles[(int)MotionRole.Run] = scale == 1f ? runRole : runRole with { NaturalSpeed = runRole.NaturalSpeed * scale };

        // The models repository's sprint, when the set has it: played for a run ahead at a sprint; its tables made with the rest.
        int sprint = -1;
        float sprintSpeed = 0f;
        if (Named("sprint") is { } sprinting && sprinting.Loops)
        {
            sprint = IndexOf(sprinting);
            sprintSpeed = (sprinting.Notes.GroundSpeed > 0f ? sprinting.Notes.GroundSpeed : 5.5f) * scale;
            poseClips.Add(sprint);
        }
        // The player's own walk, run and sprint and their starts and stops; a townsperson's style has none.
        var playerGaits = style is null ? PlayerGaits.From(Named, IndexOf, scale) : null;
        if (playerGaits is not null) poseClips.AddRange(playerGaits.All);
        // The models repository's sneak, played for a walk ahead in stealth.
        int sneak = -1;
        float sneakSpeed = 0f;
        if (Named("walk_sneak") is { } sneaking && sneaking.Loops && sneaking.Notes.GroundSpeed > 0f)
        {
            sneak = IndexOf(sneaking);
            sneakSpeed = sneaking.Notes.GroundSpeed * scale;
            poseClips.Add(sneak);
        }

        // The models repository's slope clips, laid over the walk and the run by the ground's grade (PersonGround); their tables
        // made with the rest. And the hook for its stair clips, when a set has them.
        var slopes = PersonSlopes.From(Named, IndexOf);
        if (slopes is not null) foreach (int c in new[] { slopes.WalkUp, slopes.WalkDown, slopes.RunUp, slopes.RunDown }) if (c >= 0) poseClips.Add(c);
        var stairs = PersonStairs.From(Named, IndexOf);
        if (stairs is not null) foreach (int c in stairs.All) if (c >= 0) poseClips.Add(c);   // their tables (the feet) made with the rest
        // The starts and stops; a townsperson walking a style of its own has none for its walk (PeopleStarts keeps to the gaits).
        var starts = PersonStarts.From(Named, IndexOf);
        if (starts is not null) foreach (int c in starts.StartClips.Concat(starts.StopClips)) if (c >= 0) poseClips.Add(c);
        // The jumps, the fall and the landings; their tables (the feet) made with the rest. The roles keep their stand-ins
        // (for --air-clips-off and a set without them): PeopleAir plays these over them.
        var air = PersonAirClips.From(motions, Named, IndexOf);
        if (air is not null) poseClips.AddRange(air.All);
        // The moves over and onto things, off edges, the pedalling loops and the landings moving on; their tables made with the rest.
        var over = PersonOverClips.From(motions, Named, IndexOf, scale);
        if (over is not null) poseClips.AddRange(over.All);
        // The ledge: the hangs, the shimmies, the braces, the pull-ups, the drops and the grabs; their tables made with the rest.
        var ledge = PersonLedgeClips.From(motions, Named, IndexOf, scale);
        if (ledge is not null) poseClips.AddRange(ledge.All);
        // The ladders: the leaning and the upright sets, their landings and the ladder to a braced hang; their tables made with the rest.
        var ladder = PersonLadderClips.From(motions, Named, IndexOf, scale);
        if (ladder is not null) poseClips.AddRange(ladder.All);

        // The turns on the spot, played in place: each source once as made (its tables stand for all its degrees), then its degrees.
        TurnSet? turns = AddTurns(assets, name, skeleton, rig, "turn_", Named, poseClips);

        // The models repository's crouch, when the set has it: its clips by name, its walk's ways, its turns;
        // the crouch's role the crouch held, the crouched walk's its own blends (not lowered by the rig). Without it, the stand-ins.
        PersonCrouch? crouch = null;
        if (Named("crouch") is { } held)
        {
            int Index(string clip) => Named(clip) is { } c ? IndexOf(c) : -1;
            Gait? crouchWalk = AddGait(assets, name, "crouch_walk", Named, IndexOf, poseClips, out var crouchWalkRole);
            int heelsUp = PersonCrouch.HeelsUpClip.Length > 0 ? Index(PersonCrouch.HeelsUpClip) : -1;   // the heels-up crouch, once named
            crouch = new PersonCrouch
            {
                Hold = heelsUp >= 0 ? heelsUp : IndexOf(held), HeelsUp = heelsUp, Down = Index("crouch_down"), Up = Index("crouch_up"), Kneel = Index("crouch_kneel"), Settle = Index("crouch_settle"),
                Rise = Index("crouch_rise"), KneelDown = Index("crouch_kneel_down"), KneelUp = Index("crouch_kneel_up"), Walk = crouchWalk,
                Turns = AddTurns(assets, name, skeleton, rig, "crouch_turn_", Named, poseClips),
            };
            roles[(int)MotionRole.Crouch] = new RoleMotion(crouch.Hold, 0f, 0f, false);
            if (crouchWalkRole.Has) roles[(int)MotionRole.CrouchWalk] = crouchWalkRole;
        }

        // The stand-ins in the slots with no motion; the crouched walk is the walk, lowered by the rig.
        foreach (var (role, composed) in PeopleCompositions.For(name, skeleton, rig, c => Named(c), stand))
            if (!roles[(int)role].Has) roles[(int)role] = new RoleMotion(assets.AddClip(composed), 0f, 0f, false, Composed: true);
        if (!roles[(int)MotionRole.CrouchWalk].Has && roles[(int)MotionRole.Walk] is { Has: true } walking)
            roles[(int)MotionRole.CrouchWalk] = walking with { Variable = false, Composed = true };

        starts?.Measure(assets, skeleton, stand, roles[(int)MotionRole.Idle].Clip);
        if (playerGaits is not null) (playerGaits.FromStand, playerGaits.ToStand) = PersonStarts.InStand(assets, skeleton, stand, playerGaits.Starts, playerGaits.Stops);
        over?.MeasureHands(assets, skeleton, scale, rig);   // the low vaults' hands over their notes' points
        over?.Measure(assets, skeleton, scale);   // the run-offs' take-off feet // an earlier build: which starts and stops meet the stand's own pose
        var limbs = LimbMap.Measured(skeleton, new LimbMap(rig.Pelvis, rig.LeftLeg, rig.RightLeg, rig.LeftArm, rig.RightArm), stand) with { RaiseOnly = true };
        ledge?.Measure(assets, skeleton, scale, limbs, rig.Root);   // the turning clips' root; the braced hang's feet (the wall under the edge), the free hang's knuckles
        return assets.AddKind(new CharacterKind
        {
            Name = name, Model = modelIndex, Motions = roles, FacingYaw = DetMath.HalfPi, Scale = scale,   // a townsperson's height
            Head = Head(skeleton, rig), Limbs = limbs, Actions = null, Held = [],
            Person = new PersonMotion { Rig = rig, Walk = walk, Run = run, Sprint = sprint, SprintSpeed = sprintSpeed, PlayerGaits = playerGaits, Sneak = sneak, SneakSpeed = sneakSpeed, Slopes = slopes, Stairs = stairs, Starts = starts, Air = air, Over = over, Ledge = ledge, Ladder = ladder, Turns = turns, Crouch = crouch, Twist = TwistRule.For(skeleton), Bend = BendRule.For(skeleton), Skirt = SkirtRule.For(skeleton), Swing = SwingRule.For(skeleton, person.Swing), PoseClips = [.. poseClips.Distinct()] },
        });
    }

    /// <summary>
    /// A set of turns on the spot (<paramref name="prefix"/> <c>left_45</c>, <c>left_90</c>, <c>right_45</c>, <c>right_90</c>), played in
    /// place: each source once as made (its tables stand for all its degrees), then its degrees (<see cref="TurnSet.Clips"/>); null
    /// when the set lacks a side.
    /// </summary>
    static TurnSet? AddTurns(CharacterAssets assets, string kind, Skeleton skeleton, PersonRig rig, string prefix, Func<string, INotedClip?> named, List<int> poseClips)
    {
        int root = rig.Root;
        INotedClip? l45 = named(prefix + "left_45"), l90 = named(prefix + "left_90"), r45 = named(prefix + "right_45"), r90 = named(prefix + "right_90");
        if (TurnSet.Clips(kind, skeleton, root, l45, l90, r45, r90) is not { } turnClips) return null;
        var made = new Dictionary<INotedClip, int>(ReferenceEqualityComparer.Instance);
        foreach (var t in turnClips)
            if (!made.ContainsKey(t.Source))
            {
                int index = assets.AddClip(new TurnClip($"{kind}:{t.Source.Name}@made", t.Source, root, t.Yaws, t.Yaws.Length > 0 ? t.Yaws[^1] : 0f));
                made[t.Source] = index;
                poseClips.Add(index);
            }
        // Each source's own upper-body lead through it, taken out of the pose while the chain is on (TurnChain).
        var leads = new Dictionary<INotedClip, (float[] Chest, float[] Head)>(ReferenceEqualityComparer.Instance);
        int first = -1;
        foreach (var t in turnClips)
        {
            if (!leads.TryGetValue(t.Source, out var lead)) leads[t.Source] = lead = TurnChain.Leads(t, skeleton, rig);
            (t.ChestLeads, t.HeadLeads) = lead;
            t.PoseClip = made[t.Source];
            int index = assets.AddClip(t);
            if (first < 0) first = index;
        }
        return new TurnSet { First = first, Complete = l45 is not null && l90 is not null && r45 is not null && r90 is not null };
    }

    /// <summary>
    /// One gait's role: its blends a degree round added (<see cref="Gait.Blends"/>) when the set has two ways or more, the role the
    /// blend straight ahead, drawn for the clip ahead's ground speed; else the clip ahead alone (null gait).
    /// </summary>
    static Gait? AddGait(CharacterAssets assets, string kind, string gait, Func<string, INotedClip?> named, Func<INotedClip, int> indexOf, List<int> poseClips,
                         out RoleMotion role)
    {
        role = RoleMotion.None;
        var ahead = named(gait);
        var ways = new List<(INotedClip, float)>();
        if (ahead is not null) ways.Add((ahead, float.IsNaN(ahead.Notes.HeadingDeg) ? 0f : ahead.Notes.HeadingDeg));
        foreach (var way in Ways)
            if (named($"{gait}_{way}") is { } c && !float.IsNaN(c.Notes.HeadingDeg)) ways.Add((c, c.Notes.HeadingDeg));
        if (ways.Count == 0) return null;
        if (Gait.Blends(kind, gait, ways) is not { } made)
        {
            var only = ahead ?? ways[0].Item1;
            role = new RoleMotion(assets.AddClip(only), only.Notes.GroundSpeed, 0f, false);
            return null;
        }
        int first = -1;
        foreach (var clip in made.Clips)
        {
            clip.FromClip = indexOf(clip.From);
            clip.ToClip = indexOf(clip.To);
            int index = assets.AddClip(clip);
            if (first < 0) first = index;
        }
        foreach (var (clip, _) in ways) poseClips.Add(indexOf(clip));
        var speeds = made.Clips.Take(Gait.Bins).Select(b => b.NaturalSpeed).ToArray();
        role = new RoleMotion(first, speeds[0], 0f, false);
        return new Gait
        {
            Name = gait, First = first, Speeds = speeds, Headings = made.Headings, Alternates = made.Alternates,
            Backwards = [.. made.Clips.Select(c => c.Backwards)],
        };
    }

    /// <summary>
    /// The head for first person: <c>neck_01</c> and <c>head</c> with everything under it, collapsed about the neck's joint; the
    /// eyes between <c>eye_l</c> and <c>eye_r</c>. The joints at rest are taken in scalar arithmetic (the eye is the first-person
    /// camera's, and the camera feeds the aim).
    /// </summary>
    static HeadCollapse? Head(Skeleton skeleton, PersonRig rig)
    {
        if (HeadCollapse.For(skeleton, rig.Head) is not { } collapse) return null;
        Vector3 Joint(int bone) => Combat.HitVolumeFit.AffineInverseTranslation(skeleton.InverseBind[bone]);
        var eyes = (rig.EyeL >= 0 && rig.EyeR >= 0) ? (Joint(rig.EyeL) + Joint(rig.EyeR)) * 0.5f : collapse.Eyes;
        return new HeadCollapse
        {
            Bone = collapse.Bone, Neck = collapse.Neck, Bones = collapse.Bones,
            Pivot = Joint(collapse.Neck >= 0 ? collapse.Neck : collapse.Bone), Eyes = eyes,
        };
    }
}
