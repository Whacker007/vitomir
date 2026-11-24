// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's clips: climbing walls by
// their holds, and faces with none out of the masquerade with the splayed grip (the motor's side BodyMotor.Wall.cs).
// Faster and flowing, with diagonals (the settings, the flow's store, a diagonal's body and limbs).
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>climbing walls</b> (<b>SANDBOX</b>, ours): whether they are climbed and their numbers. Off (<see cref="Off"/>: <c>--walls-off</c>, the setting
/// <c>wallsOff</c>) no wall is climbed (a tap at one jumps, as before) and the world has no <see cref="PersonWall"/> store. It rides on the hang's corners and
/// leaps (off with them) and needs a set with the clips (a newer set). <b>It changes the simulation</b> (the walls' capsule paths); a tape carries it
/// in its note <see cref="TapeNote"/>; a tape without it is the earlier build's.
/// </summary>
public sealed record PersonWallSettings
{
    /// <summary>Whether any of it is on (on by default; off: the earlier build's, <c>--walls-off</c>).</summary>
    public bool On { get; init; } = true;

    /// <summary>How far a hand or a foot is moved to a level's own hold (m; <c>wallHoldReach</c>, 0.05 to 0.4): a move whose end has a limb with none that near is refused.</summary>
    public float HoldReach { get; init; } = 0.25f;

    /// <summary>
    /// How much faster than their own the wall's moves play (<c>wallClimbRate</c>, 1 to 3; the clips' rate). The earlier build's 1.3: with the flow's overlap a held
    /// quick climb makes 2.8 hand moves a second, within a human speed climber's 2.5 to 2.9 (the project's research; 1.5, the brief's, made 3.2), and its moves
    /// come 1.4 times as often as before ("at least 50%" — a decision for him: 1.5 is the setting).
    /// </summary>
    public float Rate { get; init; } = 1.3f;

    /// <summary>
    /// How much faster than their own the getting-on (from the ground, with a jump, caught in the air), the getting-off at the foot, the let-go, the push-off,
    /// the clip over the top and the pull-up after it, and the corners play (<c>wallMoveRate</c>, 1 to 3; 1.5), as before <c>ladderMoveRate</c> for ladders.
    /// </summary>
    public float MoveRate { get; init; } = 1.5f;

    /// <summary>
    /// The flow (<c>wallFlowBlend</c>, s, 0.1 to 0.3; 0.15: the research's 0.10 to 0.15 into a move): a held move starts out of the getting-on once the limbs it moves first have their holds, the next
    /// out of the last as its last limb lands, over the top straight on into the pull-up — the capsule's difference and the animation's fade over this. 0 is the earlier build's.
    /// </summary>
    public float FlowBlend { get; init; } = 0.15f;

    /// <summary>Forward or back with a side held climbs diagonally (<c>wallDiagonals</c>; on).</summary>
    public bool Diagonals { get; init; } = true;

    /// <summary>How far a splayed hand's wrist may be bent back (degrees; <c>splayWristLimit</c>, 30 to 70; the models repository's "about 50"; 90 is almost a broken wrist).</summary>
    public float WristLimitDegrees { get; init; } = 50f;

    /// <summary>Whether any is on.</summary>
    public bool Any => On;

    /// <summary>Whether the earlier build's are on (the flow, the diagonals, the softened catches; the store <see cref="PersonWallFlow"/>): not on the earlier build's.</summary>
    public bool Flows => On && FlowBlend > 0f;

    /// <summary>None.</summary>
    public static PersonWallSettings Off => new() { On = false };

    /// <summary>The earlier build's climbing exactly (<c>--walls-no-flow</c>): the clips' own rates, no flow, no diagonals.</summary>
    public static PersonWallSettings NoFlow => new() { Rate = 1f, MoveRate = 1f, FlowBlend = 0f, Diagonals = false };

    /// <summary>The tape head's note.</summary>
    public const string TapeNote = "walls";

    /// <summary>The note: <c>off</c>, else the earlier build's <c>on=1 reach=0.25 rate=1 wrist=50</c> or the earlier build's <c>on=1 reach=0.25 rate=1.3 move=1.5 flow=0.15 diag=1 wrist=50</c>.</summary>
    public string Note() => !On ? "off" : !Flows
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"on=1 reach={HoldReach:0.####} rate={Rate:0.####} wrist={WristLimitDegrees:0.####}")
        : string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"on=1 reach={HoldReach:0.####} rate={Rate:0.####} move={MoveRate:0.####} flow={FlowBlend:0.####} diag={(Diagonals ? 1 : 0)} wrist={WristLimitDegrees:0.####}");

    /// <summary>
    /// These settings with a tape's note laid on; none (an older tape) or <c>off</c> is <see cref="Off"/>. The numbers go through the note's text; a note
    /// without <c>move</c>, <c>flow</c> and <c>diag</c> is the earlier build's: the moving clips at their own rate, no flow, no diagonals.
    /// </summary>
    public PersonWallSettings WithNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note) || !note.Contains('=')) return Off;
        var s = this with { On = true, MoveRate = 1f, FlowBlend = 0f, Diagonals = false };
        foreach (var part in note.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || !float.TryParse(part[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            s = part[..eq] switch
            {
                "on" => s with { On = v != 0f }, "reach" => s with { HoldReach = v }, "rate" => s with { Rate = v }, "wrist" => s with { WristLimitDegrees = v },
                "move" => s with { MoveRate = v }, "flow" => s with { FlowBlend = v }, "diag" => s with { Diagonals = v != 0f }, _ => s,
            };
        }
        return s.On ? s : Off;
    }

    /// <summary>The motor's numbers (the corners' tolerances the hang's, the earlier build's settings: <paramref name="hang"/>).</summary>
    public WallRules Rules(HangRules? hang) => new()
    {
        HoldReach = Math.Clamp(HoldReach, 0.05f, 0.4f), Rate = Math.Clamp(Rate, 1f, 3f),
        CornerSlack = hang?.CornerSlack ?? 0.15f, AngleTolerance = hang?.AngleTolerance ?? 15f * DetMath.DegToRad,
        MoveRate = Flows ? Math.Clamp(MoveRate, 1f, 3f) : 1f, FlowBlend = Flows ? Math.Clamp(FlowBlend, 0.1f, 0.3f) : 0f, Diagonals = Flows && Diagonals,
    };
}

/// <summary>
/// <b>a person on a wall</b> — the simulation's state (digested): the motor's (<see cref="WallMove"/>). Only the people given one by the session have it
/// (the player, with the hang's corners and leaps), and a world has the store only with the walls on and a set that has the clips, so the earlier build's tapes digest as before.
/// </summary>
public struct PersonWall
{
    public WallMove Move;
}

/// <summary>
/// <b>a person's flow on a wall</b> — the simulation's state (digested): the motor's (<see cref="WallFlow"/>: a diagonal's second clip, the velocity carried
/// into a clip). Kept beside <see cref="PersonWall"/> only with the earlier build's on (<see cref="PersonWallSettings.Flows"/>), so the earlier build's tapes digest as before.
/// </summary>
public struct PersonWallFlow
{
    public WallFlow Move;
}

/// <summary>
/// <b>a person's limbs on a wall beyond its clip</b> this tick (<see cref="PersonWallRule.Of"/>): each limb's wrist or ankle moved by an offset (the model's
/// space, its units) by a weight — onto the level's own holds, onto a face with nothing to hold — and the splayed grip's fingers laid over each hand by
/// a weight (the grip's clip and its time). Made from the world's state when the pose is made; not kept.
/// </summary>
/// <remarks>A diagonal's second move (<paramref name="DiagClip"/> at <paramref name="DiagTime"/>, −1 none) laid over the body but its limbs by <paramref name="DiagWeight"/>.</remarks>
public readonly record struct PersonWallPose(Vector3 HandL, float HandLWeight, Vector3 HandR, float HandRWeight, Vector3 FootL, float FootLWeight, Vector3 FootR,
                                             float FootRWeight, int GripClip, float GripTime, float SplayL, float SplayR, int[] SplayBones, float WristLimit,
                                             int DiagClip = -1, float DiagTime = 0f, float DiagWeight = 0f)
{
    /// <summary>Whether anything is laid on.</summary>
    public bool On => HandLWeight > 0f || HandRWeight > 0f || FootLWeight > 0f || FootRWeight > 0f || SplayL > 0f || SplayR > 0f || DiagWeight > 0f;
}

/// <summary>
/// <b>the limbs on the wall's holds</b> ("the holds a clip was made on give only its spacing: the engine moves each hand and foot to a level's own
/// holds by its IK") and <b>the splayed grip</b>. While a wall clip has a limb on a hold (its <c>on_s</c> to its <c>off_s</c>, eased in and out over
/// <see cref="Ease"/>), the limb is moved from where the drawn body has the clip's own hold (the clip's hold laid from its wall position, with the capsule's
/// difference from the clip's path) to the real one — the level's own nearest within <see cref="WallRules.HoldReach"/> where its area gives them; on the
/// lattice the clip's own at the true wall position (only the clip over the top and the getting-off, laid onto a real edge or ground off their own, leave
/// anything to do) — the arms and legs solved by the rig's two-bone IK. On a face with nothing to hold the hands are moved toward it by the notes' difference
/// of the grips (<see cref="PersonLedgeClips.SplayNotes"/>), the splayed grip's finger and thumb bones laid over each hand while it is on the face, and the wrist
/// kept bent back no more than <see cref="PersonWallSettings.WristLimitDegrees"/>. Presentation only: nothing simulated moves; the drawn pose (and the hit volumes
/// posed on it) does, so a tape carries the settings in its note.
/// </summary>
public static class PersonWallRule
{
    /// <summary>How long (s of the clip) a limb's move onto its hold comes on before its <c>on_s</c> and goes after its <c>off_s</c>.</summary>
    public const float Ease = 0.1f;

    /// <summary>The limbs of <paramref name="entity"/> for its pose this tick (none without the walls, off a wall clip, or with nothing to do).</summary>
    public static PersonWallPose Of(World world, EntityId entity, CharacterAssets assets, in CharacterAnimation state)
    {
        if (!world.TryService<PersonWallSettings>(out var settings) || !settings.On || !world.TryStore<PersonWall>(out var walls) || !walls.Has(entity)
            || !world.TryStore<PersonLedge>(out var ledges) || !ledges.Has(entity) || !world.TryStore<Body>(out var bodies) || !bodies.Has(entity)) return default;
        var l = ledges.Get(entity).Move;
        if (l.Clip <= 0 || assets.Kind(state.Kind) is not { Person.Ledge: { } clips } kind || clips.Paths.At(l.Clip - 1) is not { OnWall: true } c
            || c.Role == LedgeRole.WallGrip || state.Clip != c.Clip) return default;
        var w = walls.Get(entity).Move;
        var f = world.TryStore<PersonWallFlow>(out var flows) && flows.Has(entity) ? flows.Get(entity).Move : default;
        var level = world.Service<Space.LevelSpace>().Collision;
        float reach = Math.Clamp(settings.HoldReach, 0.05f, 0.4f);
        float scale = kind.Scale > 0f ? kind.Scale : 1f;
        float t = state.Time;
        // The capsule's difference from the clip's path (the start's and the end's being taken out): the drawn body's holds are off the clip's own by it.
        var path = BodyMotor.LedgeRaw(l, c, t);
        var capsule = BodyMotor.WallPoint(l, w, f, clips.Paths, c, t, l.Age);   // with a diagonal's second path and a carried velocity
        // While a wall clip fades out (the flow's, or any), the drawn body is the two clips' blend: its holds are off the blend of their paths (the one faded
        // from laid to end where this one starts), so the limbs are not moved twice (this clip's alone).
        if (settings.Flows && state.PreviousClip >= 0 && state.Blend is >= 0f and < 1f && PrevOnWall(clips.Paths, state.PreviousClip) is { } prev)
        {
            var (pf, pu, ps) = prev.At(state.PreviousTime);
            // Laid to end where this one starts (a flow at its end); a move taken over at its start (a diagonal from the straight move begun a moment before)
            // laid from where it began, the same wall position.
            bool fromStart = prev.Role == LedgeRole.WallMove && state.PreviousTime < 0.5f * prev.Duration;
            var (ef, eu, es) = prev.At(fromStart ? 0f : prev.Duration);
            var lv = new Vector2(-l.In.Y, l.In.X);
            var prevRaw = l.Place + new Vector3(l.In * (pf - ef) + lv * (ps - es), pu - eu);
            path = Vector3.Lerp(prevRaw, path, state.Blend);
        }
        var drawn = capsule - path;
        var truePlace = l.Place - w.Shift;
        Span<Vector3> off = stackalloc Vector3[4];
        Span<float> weight = stackalloc float[4];
        Span<float> splay = stackalloc float[2];
        bool sheer = w.Sheer != 0 && clips.SplayBones.Length > 0 && !float.IsNaN(clips.SplayNotes);
        var diag = f.Diag > 0 && c.Role == LedgeRole.WallMove ? clips.Paths.At(f.Diag - 1) : null;
        if (diag is not null) DiagLimbs(l, w, c, diag, t, drawn, truePlace, level, reach, sheer, clips.SplayNotes, off, weight, splay);
        else foreach (var h in c.Holds)
        {
            float k = PersonHandsRule.Weight(t, h.On, h.Off, Ease);
            if (k <= 0f) continue;
            if (sheer && h.Hand) splay[h.Limb] = MathF.Max(splay[h.Limb], k);
            if (k < weight[h.Limb]) continue;
            bool corner = c.Role == LedgeRole.WallCorner;
            var own = corner ? CornerHold(l, c, h) : OnFace(l.Place, l.In, c.WallFace, h.Left, h.Up);
            var at = own + drawn;
            Vector3 real;
            if (w.Given != 0 && level.Climb.HoldNear(corner ? own : OnFace(truePlace, l.In, c.WallFace, h.Left, h.Up), reach, out var given)) real = given;
            else if (corner) continue;   // the lattice round a corner: the clip's own holds (laid on the real corner)
            else real = OnFace(truePlace, l.In, c.WallFace, h.Left, h.Up);
            var d = real - at;
            if (sheer && h.Hand) d += new Vector3(l.In * clips.SplayNotes, 0f);
            off[h.Limb] = d;
            weight[h.Limb] = k;
        }
        // A refused move's intent — the leading hand (the side's; going up the right, down the left, as the quick moves lead) reaches toward the wish.
        bool leaning = f.Lean != Vector2.Zero;
        if (leaning)
        {
            var lv = new Vector2(-l.In.Y, l.In.X);
            var d = new Vector3(lv * f.Lean.X, f.Lean.Y);
            float len = f.Lean.Length();
            int hand = f.Lean.X > 0.3f * len ? 0 : f.Lean.X < -0.3f * len ? 1 : f.Lean.Y > 0f ? 1 : 0;
            if (weight[hand] <= 0f) { off[hand] = d; weight[hand] = 1f; }
            else off[hand] += d / MathF.Min(1f, weight[hand]);
        }
        ref readonly var body = ref bodies.Get(entity);
        var (s, co) = DetMath.SinCos(body.Yaw - kind.FacingYaw);
        Vector3 Model(Vector3 v) => new Vector3(co * v.X + s * v.Y, -s * v.X + co * v.Y, v.Z) / scale;
        int grip = clips.Paths.Wall(LedgeRole.WallGrip)?.Clip ?? -1;
        float gripTime = grip >= 0 && assets.Clip(grip).Duration > 0f ? t % assets.Clip(grip).Duration : 0f;
        var pose = new PersonWallPose(Model(off[0]), weight[0], Model(off[1]), weight[1], Model(off[2]), weight[2], Model(off[3]), weight[3], grip, gripTime,
                                      sheer && grip >= 0 ? splay[0] : 0f, sheer && grip >= 0 ? splay[1] : 0f, clips.SplayBones,
                                      Math.Clamp(settings.WristLimitDegrees, 30f, 70f) * DetMath.DegToRad,
                                      diag?.Clip ?? -1, diag is null ? 0f : BodyMotor.DiagTime(c, diag, t), diag is null ? 0f : BodyMotor.DiagShare);
        // Nothing to move by less than half a millimetre (the lattice's own holds): the clip as it is.
        bool any = pose.SplayL > 0f || pose.SplayR > 0f || diag is not null || leaning;
        for (int i = 0; i < 4 && !any; i++) any = weight[i] > 0f && off[i].LengthSquared() > 2.5e-7f;
        return any ? pose : default;
    }

    /// <summary>The wall clip of the set that is the animation clip <paramref name="clip"/> (not the grip), or null.</summary>
    static LedgeClip? PrevOnWall(LedgePaths paths, int clip)
    {
        foreach (var x in paths.Clips) if (x.Clip == clip) return x.OnWall && x.Role != LedgeRole.WallGrip ? x : null;
        return null;
    }

    /// <summary>
    /// A diagonal's limbs — the side's move carries them (its holds and their times); each is moved from where the drawn body has the side's own hold to the
    /// real one: its start hold held to its <c>off_s</c>, its end hold (the side's, a step up or down: the second move's step) from its <c>on_s</c>, and between
    /// them carried from the one's difference to the other's over its own flight (a smoothstep), so nothing jumps; the weight whole throughout.
    /// </summary>
    static void DiagLimbs(in LedgeMove l, in WallMove w, LedgeClip c, LedgeClip diag, float t, Vector3 drawn, Vector3 truePlace, Space.CollisionWorld level, float reach,
                          bool sheer, float splayNotes, Span<Vector3> off, Span<float> weight, Span<float> splay)
    {
        for (int limb = 0; limb < 4; limb++)
        {
            WallHold? from = null, to = null;
            foreach (var h in c.Holds)
                if (h.Limb == limb) { if (h.On < 0f) from = h; else to ??= h; }
            if (from is not { } a || to is not { } b) continue;
            var da = Correction(l, w, c, a, 0f, truePlace, level, reach, sheer, splayNotes);
            var db = Correction(l, w, c, b, diag.StepUp, truePlace, level, reach, sheer, splayNotes);
            float span = b.On - a.Off;
            float k = span > 1e-4f ? Math.Clamp((t - a.Off) / span, 0f, 1f) : t >= b.On ? 1f : 0f;
            k = k * k * (3f - 2f * k);
            off[limb] = da + (db - da) * k - drawn;
            weight[limb] = 1f;
            if (sheer && a.Hand) splay[limb] = MathF.Max(PersonHandsRule.Weight(t, a.On, a.Off, Ease), PersonHandsRule.Weight(t, b.On, b.Off, Ease));
        }
    }

    /// <summary>A hold's real place less the clip's own (the lattice's at the true wall position, <paramref name="lift"/> higher; or the level's own nearest), on a sheer face the grip's.</summary>
    static Vector3 Correction(in LedgeMove l, in WallMove w, LedgeClip c, in WallHold h, float lift, Vector3 truePlace, Space.CollisionWorld level, float reach, bool sheer, float splayNotes)
    {
        var own = OnFace(l.Place, l.In, c.WallFace, h.Left, h.Up);
        var lattice = OnFace(truePlace, l.In, c.WallFace, h.Left, h.Up + lift);
        var real = w.Given != 0 && level.Climb.HoldNear(lattice, reach, out var given) ? given : lattice;
        var d = real - own;
        if (sheer && h.Hand) d += new Vector3(l.In * splayNotes, 0f);
        return d;
    }

    static float Window(float t, float from, float to)
    {
        if (to <= from + 1e-5f) return t >= to && to > 0f ? 1f : 0f;
        float x = Math.Clamp((t - from) / (to - from), 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    /// <summary>The point on the face's plane of a wall position: <paramref name="left"/> to its left and <paramref name="up"/> over its capsule's bottom.</summary>
    static Vector3 OnFace(Vector3 place, Vector2 inward, float faceFwd, float left, float up)
    {
        var lv = new Vector2(-inward.Y, inward.X);
        return new Vector3(place.X + inward.X * faceFwd + lv.X * left, place.Y + inward.Y * faceFwd + lv.Y * left, place.Z + up);
    }

    /// <summary>A corner's hold (<c>along_m</c> from its corner, on the first face or the other, the way of the move) laid from its wall position.</summary>
    static Vector3 CornerHold(in LedgeMove l, LedgeClip c, in WallHold h)
    {
        var lv = new Vector2(-l.In.Y, l.In.X) * c.Way;
        var corner = new Vector3(l.Place.X + l.In.X * c.WallFace + lv.X * c.CornerAlong, l.Place.Y + l.In.Y * c.WallFace + lv.Y * c.CornerAlong, l.Place.Z + h.Up);
        if (!h.Other) return corner + new Vector3(lv * h.Along, 0f);
        var d2 = c.Outside ? l.In : -l.In;   // along the other face away from the corner
        return corner + new Vector3(d2 * h.Along, 0f);
    }

    [ThreadStatic] static BoneTransform[]? gripPose;

    /// <summary>
    /// Lays <paramref name="pose"/> on a person's pose (<paramref name="local"/>, changed; <paramref name="model"/> scratch): each limb solved to its offset by the
    /// two-bone IK, the splayed grip's fingers over each hand by its weight, a splayed wrist kept within its limit.
    /// </summary>
    public static void Apply(CharacterAssets assets, Skeleton skeleton, LimbMap limbs, in PersonWallPose pose, Span<BoneTransform> local, Span<Matrix4x4> model)
    {
        if (!pose.On) return;
        PoseEvaluator.ToModel(skeleton, local, model);
        var knee = LegTurn.Knee(0f);
        // The targets from the one pose, then each limb (none moves another's end).
        Vector3 hl = default, hr = default, fl = default, fr = default;
        if (pose.HandLWeight > 0f && limbs.LeftArm.Has) hl = model[limbs.LeftArm.End].Translation + pose.HandL * MathF.Min(1f, pose.HandLWeight);
        if (pose.HandRWeight > 0f && limbs.RightArm.Has) hr = model[limbs.RightArm.End].Translation + pose.HandR * MathF.Min(1f, pose.HandRWeight);
        if (pose.FootLWeight > 0f && limbs.LeftLeg.Has) fl = model[limbs.LeftLeg.End].Translation + pose.FootL * MathF.Min(1f, pose.FootLWeight);
        if (pose.FootRWeight > 0f && limbs.RightLeg.Has) fr = model[limbs.RightLeg.End].Translation + pose.FootR * MathF.Min(1f, pose.FootRWeight);
        // A diagonal's second move laid over the body but its limbs (the targets are the side's limbs' own, moved), then the limbs to their targets.
        if (pose.DiagWeight > 0f && pose.DiagClip >= 0)
        {
            LayBody(assets, skeleton, limbs, pose.DiagClip, pose.DiagTime, MathF.Min(1f, pose.DiagWeight), local);
            PoseEvaluator.ToModel(skeleton, local, model);
            // A hand its arm cannot reach (the side's arm reaching a step up or down too: the woman's, briefly) brings the hips toward it by what is short (the
            // feet are left: the clips' legs are often near straight, and a lean for them pops; the woman's leading foot going down and left is short, measured).
            for (int pass = 0; pass < 2; pass++)
            {
                var (sl, ml) = pose.HandLWeight > 0f && limbs.LeftArm.Has ? Short(model, limbs.LeftArm, hl) : (Vector3.Zero, 0f);
                var (sr, mr) = pose.HandRWeight > 0f && limbs.RightArm.Has ? Short(model, limbs.RightArm, hr) : (Vector3.Zero, 0f);
                var shift = sl + sr;   // both (the one that is short more does not take over at once)
                if (MathF.Max(ml, mr) <= 1e-4f) break;
                float len = shift.Length();
                shift *= DiagLean / (DiagLean + len);   // a soft limit: never past DiagLean, no corner on the way
                var r = local[limbs.Root];
                local[limbs.Root] = r with { Translation = r.Translation + shift };
                PoseEvaluator.ToModel(skeleton, local, model);
            }
        }
        if (pose.HandLWeight > 0f && limbs.LeftArm.Has) LimbIK.Solve(skeleton, local, model, limbs.LeftArm, hl, 1f, new Vector3(-0.5f, -0.5f, -0.7f));
        if (pose.HandRWeight > 0f && limbs.RightArm.Has) LimbIK.Solve(skeleton, local, model, limbs.RightArm, hr, 1f, new Vector3(0.5f, -0.5f, -0.7f));
        if (pose.FootLWeight > 0f && limbs.LeftLeg.Has) LimbIK.Solve(skeleton, local, model, limbs.LeftLeg, fl, 1f, knee);
        if (pose.FootRWeight > 0f && limbs.RightLeg.Has) LimbIK.Solve(skeleton, local, model, limbs.RightLeg, fr, 1f, knee);
        if ((pose.SplayL > 0f || pose.SplayR > 0f) && pose.GripClip >= 0)
        {
            int n = skeleton.Count;
            if (gripPose is null || gripPose.Length < n) gripPose = new BoneTransform[n];
            var g = gripPose.AsSpan(0, n);
            assets.Clip(pose.GripClip).Sample(pose.GripTime, skeleton, g);
            foreach (int b in pose.SplayBones)
            {
                if (b < 0 || b >= n) continue;
                float k = MathF.Min(1f, Side(skeleton, b, limbs) == 0 ? pose.SplayL : pose.SplayR);
                if (k > 0f) local[b] = local[b] with { Rotation = DetMath.Slerp(local[b].Rotation, g[b].Rotation, k) };
            }
            // A splayed hand's wrist bent back no more than the limit: the hand turned back toward the forearm's line by the excess.
            if (pose.SplayL > 0f) KeepWrist(skeleton, local, model, limbs.LeftArm, pose.WristLimit);
            if (pose.SplayR > 0f) KeepWrist(skeleton, local, model, limbs.RightArm, pose.WristLimit);
        }
    }

    [ThreadStatic] static BoneTransform[]? diagPose, diagFirst;

    /// <summary>How far a diagonal's hips may be brought toward a hand its arm cannot reach (the model's units: the people's are metres).</summary>
    const float DiagLean = 0.08f;

    /// <summary>Over how far (the model's units) about the arm's reach the lean comes on (a smooth hinge, so the hips start moving gently).</summary>
    const float DiagLeanIn = 0.06f;

    /// <summary>How far a wrist's target is beyond the arm's reach (97% of it) from the shoulder, eased in over <see cref="DiagLeanIn"/> either side, and the way.</summary>
    static (Vector3 Shift, float Short) Short(ReadOnlySpan<Matrix4x4> model, in Limb arm, Vector3 target)
    {
        var d = target - model[arm.Upper].Translation;
        float l = d.Length(), x = l - LimbIK.Reach(model, arm) * 0.97f;
        float more = x <= -DiagLeanIn ? 0f : x >= DiagLeanIn ? x : (x + DiagLeanIn) * (x + DiagLeanIn) / (4f * DiagLeanIn);
        return more > 0f && l > 0f ? (d / l * more, more) : (Vector3.Zero, 0f);
    }

    /// <summary>
    /// <paramref name="clip"/> at <paramref name="time"/> laid over every bone but the arms' (from the clavicles) and the legs' (from the thighs): their turns by
    /// <paramref name="k"/>, and their places (the hips', the only bones a clip moves) by what it moves them from its first frame, whole — so the two moves' hips
    /// go with the two paths summed (each clip's hips are made to go with its own path; half of each would carry half of each path's corners).
    /// </summary>
    static void LayBody(CharacterAssets assets, Skeleton skeleton, LimbMap limbs, int clip, float time, float k, Span<BoneTransform> local)
    {
        int n = skeleton.Count;
        if (diagPose is null || diagPose.Length < n) { diagPose = new BoneTransform[n]; diagFirst = new BoneTransform[n]; }
        var g = diagPose.AsSpan(0, n);
        var g0 = diagFirst!.AsSpan(0, n);
        assets.Clip(clip).Sample(time, skeleton, g);
        assets.Clip(clip).Sample(0f, skeleton, g0);
        int cl = limbs.LeftArm.Has ? skeleton.Bones[limbs.LeftArm.Upper].Parent : -1, cr = limbs.RightArm.Has ? skeleton.Bones[limbs.RightArm.Upper].Parent : -1;
        int ll = limbs.LeftLeg.Has ? limbs.LeftLeg.Upper : -1, lr = limbs.RightLeg.Has ? limbs.RightLeg.Upper : -1;
        for (int b = 0; b < n; b++)
        {
            bool limb = false;
            for (int x = b; x >= 0 && !limb; x = skeleton.Bones[x].Parent) limb = x == cl || x == cr || x == ll || x == lr;
            if (limb) continue;
            local[b] = new BoneTransform(DetMath.Slerp(local[b].Rotation, g[b].Rotation, k), local[b].Translation + (g[b].Translation - g0[b].Translation));
        }
    }

    /// <summary>Which hand a bone hangs from (0 the left arm's, 1 the right's).</summary>
    static int Side(Skeleton skeleton, int bone, LimbMap limbs)
    {
        for (int b = bone; b >= 0; b = skeleton.Bones[b].Parent)
        {
            if (b == limbs.LeftArm.End) return 0;
            if (b == limbs.RightArm.End) return 1;
        }
        return 0;
    }

    /// <summary>The wrist's bend in a pose (radians): between the forearm's line (elbow to wrist) and the hand's (wrist to knuckles).</summary>
    public static float WristBend(ReadOnlySpan<Matrix4x4> model, in Limb arm)
    {
        if (!arm.Has || arm.Tip < 0) return 0f;
        var f = model[arm.End].Translation - model[arm.Lower].Translation;
        var h = model[arm.Tip].Translation - model[arm.End].Translation;
        float fl = f.Length(), hl = h.Length();
        if (fl < 1e-6f || hl < 1e-6f) return 0f;
        return DetMath.Acos(Math.Clamp(Vector3.Dot(f, h) / (fl * hl), -1f, 1f));
    }

    static void KeepWrist(Skeleton skeleton, Span<BoneTransform> local, Span<Matrix4x4> model, in Limb arm, float limit)
    {
        if (!arm.Has || arm.Tip < 0) return;
        PoseEvaluator.ToModel(skeleton, local, model);
        float bend = WristBend(model, arm);
        if (bend <= limit) return;
        var f = Vector3.Normalize(model[arm.End].Translation - model[arm.Lower].Translation);
        var h = Vector3.Normalize(model[arm.Tip].Translation - model[arm.End].Translation);
        var back = DetMath.Slerp(Quaternion.Identity, LimbIK.FromTo(h, f), (bend - limit) / bend);
        int parent = skeleton.Bones[arm.End].Parent;
        var qParent = LimbIK.ModelRotation(skeleton, local, parent);
        var qEnd = PoseMath.Multiply(qParent, local[arm.End].Rotation);
        var qEnd2 = LimbIK.Normalize(PoseMath.Multiply(back, qEnd));
        local[arm.End] = local[arm.End] with { Rotation = LimbIK.Normalize(PoseMath.Multiply(LimbIK.Conjugate(qParent), qEnd2)) };
    }
}
