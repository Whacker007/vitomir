// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository: squeezing through gaps, stealth with cover, and the walk
// into the crouch walk and back on a phase-matched blend (the motor's side BodyMotor.Side.cs).
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>squeezing, stealth with cover, and ducking</b> (<b>SANDBOX</b>, ours): whether they are on and their numbers. Off (<see cref="Off"/>:
/// <c>--stealth-off</c>, the setting <c>stealthOff</c>) a gap stops the body as before, the stealth key does nothing, and the walk goes into the crouch walk
/// on the fifth of a second's fade; the world has no <see cref="PersonStealth"/> store. It rides on the walls (off with them) and needs a set with the
/// clips (a newer set). <b>It changes the simulation</b> (the squeeze's and the cover's capsule paths, stealth's pace, the drawn stance and the duck's
/// fades, which pose the hit volumes); a tape carries it in its note <see cref="TapeNote"/>; a tape without it is the earlier build's (or before).
/// </summary>
public sealed record PersonStealthSettings
{
    /// <summary>Whether any of it is on (on by default; off: the earlier build's, <c>--stealth-off</c>).</summary>
    public bool On { get; init; } = true;

    /// <summary>Gaps narrower than the body squeezed through.</summary>
    public bool Squeeze { get; init; } = true;

    /// <summary>In stealth, the back to a wall behind in cover.</summary>
    public bool Cover { get; init; } = true;

    /// <summary>
    /// Stealth's walk 30–50% of the walk's speed, a sneak 0.4–0.7 m/s: stealth's pace (m/s; <c>stealthPace</c>, 0.3 to 0.8): what a body in stealth
    /// goes at, walking or at the run (0.6: 46% of the human's 1.3 walk, on the models repository's <c>walk_sneak</c>, drawn for 0.7). Sprint held: the walk's own
    /// speed (a hurried sneak, still lowered).
    /// </summary>
    public float Pace { get; init; } = 0.6f;

    /// <summary>Crouched in stealth the crouch clips played slower: the crouched walk's pace in stealth (m/s; <c>stealthCrouchPace</c>, 0.3 to 1; 0.6).</summary>
    public float CrouchPace { get; init; } = 0.6f;

    /// <summary>How far stealth lowers the hips (m; <c>stealthLower</c>, 0 to 0.2): by a blend toward the crouch's pose at that share of the crouch's own drop. 0.11: the cover's.</summary>
    public float Lower { get; init; } = 0.11f;

    /// <summary>Walking ahead in stealth on the models repository's sneak (<c>walk_sneak</c>; <c>stealthSneak</c>); off: the walk's own blends, slowed and lowered.</summary>
    public bool Sneak { get; init; } = true;

    /// <summary>How far the back may be off a wall behind to take cover against it (m; <c>coverReach</c>, 0.1 to 0.5).</summary>
    public float CoverReach { get; init; } = 0.3f;

    /// <summary>Ducking: the walk (or the run) into the crouch walk, phase-matched, over this long (s; <c>duckIn</c>, 0.1 to 1); and back out (<c>duckOut</c>).</summary>
    public float DuckIn { get; init; } = 0.4f;
    public float DuckOut { get; init; } = 0.6f;

    /// <summary>Squeezes quick: the quarter turns into and out of a squeeze played this much faster (<c>squeezeTurnRate</c>, 1 to 2; 1.5: 0.71 s).</summary>
    public float TurnRate { get; init; } = 1.5f;

    /// <summary>No locked-in clip: a side-step's cycle let go of runs to its nearer end this much faster (<c>sideStopRate</c>, 1 to 3; 2).</summary>
    public float StopRate { get; init; } = 2f;

    /// <summary>Horizon's 3 frames at 30 Hz: the keys' way along a squeeze or a cover kept this long after they let go (s; <c>sideHold</c>, 0 to 0.25; 0.1).</summary>
    public float Hold { get; init; } = 0.1f;

    /// <summary>Whether any is on.</summary>
    public bool Any => On;

    /// <summary>None.</summary>
    public static PersonStealthSettings Off => new() { On = false };

    /// <summary>The tape head's note.</summary>
    public const string TapeNote = "stealth";

    /// <summary>The note: <c>off</c>, else <c>on=1 squeeze=1 cover=1 pace=0.6 crouch=0.6 lower=0.11 sneak=1 reach=0.3 in=0.4 out=0.6 turn=1.5 stop=2 hold=0.1</c>.</summary>
    public string Note() => !On ? "off" : string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"on=1 squeeze={(Squeeze ? 1 : 0)} cover={(Cover ? 1 : 0)} pace={Pace:0.####} crouch={CrouchPace:0.####} lower={Lower:0.####} sneak={(Sneak ? 1 : 0)} reach={CoverReach:0.####} in={DuckIn:0.####} out={DuckOut:0.####} turn={TurnRate:0.####} stop={StopRate:0.####} hold={Hold:0.####}");

    /// <summary>These settings with a tape's note laid on; none (an older tape) or <c>off</c> is <see cref="Off"/>. The numbers go through the note's text.</summary>
    public PersonStealthSettings WithNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note) || !note.Contains('=')) return Off;
        var s = this with { On = true };
        foreach (var part in note.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || !float.TryParse(part[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            s = part[..eq] switch
            {
                "on" => s with { On = v != 0f }, "squeeze" => s with { Squeeze = v != 0f }, "cover" => s with { Cover = v != 0f }, "pace" => s with { Pace = v },
                "crouch" => s with { CrouchPace = v }, "lower" => s with { Lower = v }, "sneak" => s with { Sneak = v != 0f }, "reach" => s with { CoverReach = v },
                "in" => s with { DuckIn = v }, "out" => s with { DuckOut = v }, "turn" => s with { TurnRate = v }, "stop" => s with { StopRate = v }, "hold" => s with { Hold = v },
                _ => s,
            };
        }
        return s.On ? s : Off;
    }

    /// <summary>The motor's numbers.</summary>
    public SideRules Rules() => new()
    {
        Squeeze = Squeeze, Cover = Cover, CoverReach = Math.Clamp(CoverReach, 0.1f, 0.5f), TurnRate = Math.Clamp(TurnRate, 1f, 2f), StopRate = Math.Clamp(StopRate, 1f, 3f),
        Hold = Math.Clamp(Hold, 0f, 0.25f),
    };

    /// <summary>The duck's fades in force (s), clamped.</summary>
    public float In => Math.Clamp(DuckIn, 0.1f, 1f);
    public float Out => Math.Clamp(DuckOut, 0.1f, 1f);

    /// <summary>
    /// A body's numbers in stealth (<paramref name="loco"/>'s, capped): the walk and the run at <see cref="Pace"/>, the sprint at the walk's own speed, the crouched
    /// walk at <see cref="CrouchPace"/> (so its clips play slower: their rate is the body's speed over theirs).
    /// </summary>
    public Locomotion Stealthy(in Locomotion loco)
    {
        var l = loco;
        float p = Math.Clamp(Pace, 0.3f, 0.8f);
        l.SprintSpeed = MathF.Min(l.SprintSpeed, MathF.Max(p, loco.WalkSpeed));
        l.WalkSpeed = MathF.Min(l.WalkSpeed, p);
        l.RunSpeed = MathF.Min(l.RunSpeed, p);
        l.CrouchSpeed = MathF.Min(l.CrouchSpeed, Math.Clamp(CrouchPace, 0.3f, 1f));
        return l;
    }

    /// <summary>
    /// Whether a person's drawn pose has its root's turn taken out for a squeeze's quarter turn (a service of the world's): the turn shown, or fading into
    /// the side-steps after it, or the turn out fading into the stand — the body's facing carries that turn (BodyMotor.Side.cs). Presentation of the motor's state.
    /// </summary>
    public static bool StillRoot(World world, EntityId entity, CharacterAssets assets, in CharacterAnimation state)
    {
        if (!world.TryService<PersonStealthSettings>(out var s) || !s.On || state.Clip < 0 || !world.TryStore<PersonStealth>(out var stealth) || !stealth.Has(entity)) return false;
        if (assets.Kind(state.Kind).Person?.Ledge?.Paths is not { } paths || !world.TryStore<PersonLedge>(out var ledges) || !ledges.Has(entity)) return false;
        var side = stealth.Get(entity).Move;
        var l = ledges.Get(entity).Move;
        bool fading = state.PreviousClip >= 0 && state.Blend < 1f;
        if (l.Clip > 0 && paths.At(l.Clip - 1) is { OnSide: true } c)
        {
            if (c.Role == LedgeRole.SideTurn && c.Clip == state.Clip) return true;
            if (fading && side.Kind == 1)
                foreach (var t in paths.Clips) if (t.Role == LedgeRole.SideTurn && t.Clip == state.PreviousClip) return true;
            return false;
        }
        return fading && side.Turned == state.PreviousClip + 1;
    }
}

/// <summary>
/// <b>a person's stealth, squeeze and cover</b> — the simulation's state (digested): the motor's (<see cref="SideMove"/>), whether stealth is on (the key's
/// toggle) and how far the stance has gone down into it (0 to 1). Only the people given one by the session have it (the player, with the walls), and a world has the
/// store only with these on and a set that has the clips, so the earlier build's tapes digest as before.
/// </summary>
public struct PersonStealth
{
    public SideMove Move;
    /// <summary>1 while stealth is on (the <c>stealth</c> key toggles it).</summary>
    public int On;
    /// <summary>How far down into stealth's stance the body is (0 standing as ever, 1 lowered by <see cref="PersonStealthSettings.Lower"/>).</summary>
    public float Stance;
    /// <summary>
    /// The stance's way there — the earlier build's inertialized join (<see cref="Inertial"/>) on the one number: where it is going (0 or 1), its difference from there and
    /// its speed when it set off (a change of mind half way starts from where it is, at its speed), and the seconds since; its speed now (a second).
    /// </summary>
    public byte StanceTo;
    public float StanceFrom, StanceSpeed, StanceAge, StanceRate;
}

/// <summary>A person's stealth stance this tick: the crouch's clip whose pose is laid over the shown one, its time, and how much (made when the pose is made; not kept).</summary>
public readonly record struct PersonStancePose(int Clip, float Time, float Weight)
{
    public bool On => Weight > 0f && Clip >= 0;
}

/// <summary>
/// <b>stealth's stance</b> ("the stance lowered a little, the hips toward the cover's 11 cm"): the pose shown blended toward the crouch's own by the share
/// that lowers the hips by <see cref="PersonStealthSettings.Lower"/> (the crouch's drop measured from the stand's first frame to the held crouch's: 0.41 m for the
/// man, so 27 %) times the stance's progress — a walk or a run toward the crouch walk of its way at the same point of its cycle, the stand, a turn and a start or
/// stop toward the held crouch. Laid on before the legs' IK, which keeps the feet on the ground. Presentation of the simulation's state (the hit volumes pose on
/// it), so a tape carries its numbers.
/// </summary>
public static class PersonStealthRule
{
    sealed class Drop { public float Metres = float.NaN, Sneak = float.NaN; }

    static readonly ConditionalWeakTable<PersonMotion, Drop> drops = new();

    [ThreadStatic] static BoneTransform[]? crouched;

    /// <summary>The pelvis's height in <paramref name="clip"/>'s pose (m in the model, unscaled): the mean of its frames a twentieth of a second apart.</summary>
    static float Hips(CharacterAssets assets, Skeleton skeleton, int clip, int pelvis)
    {
        int n = skeleton.Count;
        var local = new BoneTransform[n];
        var model = new Matrix4x4[n];
        var c = assets.Clip(clip);
        float sum = 0f;
        int k = 0;
        for (float t = 0f; k == 0 || t < c.Duration - 1e-4f; t += 0.05f, k++)
        {
            c.Sample(t, skeleton, local);
            PoseEvaluator.ToModel(skeleton, local, model);
            sum += model[pelvis].Translation.Z;
        }
        return sum / k;
    }

    /// <summary>The crouch's drop of the hips from the stand (m, at the kind's scale), measured once from their poses (the mean of each clip's frames).</summary>
    public static float DropOf(CharacterAssets assets, int kindIndex, PersonMotion person) => Drops(assets, kindIndex, person).Metres;

    /// <summary>How much lower than the walk's the sneak carries the hips (m, at the kind's scale; 0 without a sneak), measured once likewise.</summary>
    public static float SneakDropOf(CharacterAssets assets, int kindIndex, PersonMotion person) => Drops(assets, kindIndex, person).Sneak;

    static Drop Drops(CharacterAssets assets, int kindIndex, PersonMotion person)
    {
        var d = drops.GetOrCreateValue(person);
        if (!float.IsNaN(d.Metres)) return d;
        var kind = assets.Kind(kindIndex);
        float scale = kind.Scale > 0f ? kind.Scale : 1f;
        int stand = kind.Motion(MotionRole.Idle).Clip, hold = person.Crouch?.Hold ?? -1, pelvis = person.Rig.Pelvis, walk = person.Walk?.First ?? kind.Motion(MotionRole.Walk).Clip;
        var skeleton = assets.ModelOf(kindIndex).Skeleton;
        float m = 0f, sneak = 0f;
        if (stand >= 0 && hold >= 0 && pelvis >= 0) m = (Hips(assets, skeleton, stand, pelvis) - Hips(assets, skeleton, hold, pelvis)) * scale;
        if (person.Sneak >= 0 && walk >= 0 && pelvis >= 0) sneak = MathF.Max(0f, (Hips(assets, skeleton, walk, pelvis) - Hips(assets, skeleton, person.Sneak, pelvis)) * scale);
        d.Sneak = sneak;
        d.Metres = m;
        return d;
    }

    /// <summary>The stance of <paramref name="entity"/> for its pose this tick (none out of stealth, crouched, in a move, or with nothing to lay).</summary>
    public static PersonStancePose Of(World world, EntityId entity, CharacterAssets assets, in CharacterAnimation state)
    {
        if (!world.TryService<PersonStealthSettings>(out var settings) || !settings.On || settings.Lower <= 0f || !world.TryStore<PersonStealth>(out var stealth)
            || !stealth.Has(entity)) return default;
        float stance = stealth.Get(entity).Stance;
        if (stance <= 0f || state.Clip < 0) return default;
        var kind = assets.Kind(state.Kind);
        if (kind.Person is not { Crouch: { } crouch } person || crouch.Hold < 0) return default;
        // In a move only while a squeeze or a cover begins (the stance going up as its clip comes in, so the hips do not jump), toward the held crouch.
        bool side = MotionSlots.IsTraversal(state.Role) && world.TryStore<PersonLedge>(out var ledges) && ledges.Has(entity) && ledges.Get(entity).Move.Clip is > 0 and var lc
                    && person.Ledge?.Paths.At(lc - 1) is { OnSide: true };
        if (crouch.Holds(state.Clip) || (MotionSlots.IsTraversal(state.Role) && !side)
            || (!side && state.Role is not (MotionRole.Idle or MotionRole.Turn or MotionRole.Walk or MotionRole.Run))) return default;
        float drop = DropOf(assets, state.Kind, person);
        if (drop < 0.05f) return default;
        float lower = Math.Clamp(settings.Lower, 0f, 0.2f);
        // The sneak lowers the hips by itself a little (2.3 cm for the man): the rest by the blend, toward the crouch walk ahead at the same point of its cycle.
        bool sneak = person.Sneak >= 0 && state.Clip == person.Sneak;
        if (sneak) lower = MathF.Max(0f, lower - SneakDropOf(assets, state.Kind, person));
        float w = Math.Clamp(lower / drop, 0f, 1f) * Math.Clamp(stance, 0f, 1f);
        float d = assets.Clip(state.Clip).Duration;
        if (sneak && crouch.Walk is { } ahead && d > 0f)
            return new PersonStancePose(ahead.First, state.Time / d * assets.Clip(ahead.First).Duration, w);
        // A walk's or a run's way: the crouch walk's of the same degree, at the same point of its cycle.
        var gait = person.Walk?.Holds(state.Clip) == true ? person.Walk : person.Run?.Holds(state.Clip) == true ? person.Run : null;
        if (gait is not null && crouch.Walk is { } low && d > 0f)
        {
            int i = state.Clip - gait.First;
            if (i >= Gait.Bins) i = Array.IndexOf(gait.Alternates, i - Gait.Bins) is var at && at >= 0 ? at : 0;
            int clip = low.First + Math.Clamp(i, 0, Gait.Bins - 1);
            float phase = state.Time / d;
            return new PersonStancePose(clip, phase * assets.Clip(clip).Duration, w);
        }
        float hd = assets.Clip(crouch.Hold).Duration;
        return new PersonStancePose(crouch.Hold, hd > 0f ? state.Time % hd : 0f, w);
    }

    /// <summary>Lays <paramref name="pose"/> on a person's local pose: every bone toward the crouch clip's by its weight (the turns slerped, the translations lerped).</summary>
    public static void Lay(CharacterAssets assets, Skeleton skeleton, in PersonStancePose pose, Span<BoneTransform> local)
    {
        if (!pose.On) return;
        int n = skeleton.Count;
        if (crouched is null || crouched.Length < n) crouched = new BoneTransform[n];
        var c = crouched.AsSpan(0, n);
        assets.Clip(pose.Clip).Sample(pose.Time, skeleton, c);
        float w = MathF.Min(1f, pose.Weight);
        for (int b = 0; b < n && b < local.Length; b++)
            local[b] = new BoneTransform(DetMath.Slerp(local[b].Rotation, c[b].Rotation, w), PoseMath.Lerp(local[b].Translation, c[b].Translation, w));
    }

    /// <summary>
    /// <b>the stance a tick on</b> toward down (<paramref name="low"/>: in stealth standing or walking, not crouched, not in a move) or up, over the duck's times
    /// (<see cref="PersonStealthSettings.In"/> down, <see cref="PersonStealthSettings.Out"/> up) on the earlier build's inertialized join: the difference from where it is going
    /// and its speed as it set off decayed to nought (Holden's cubic), so the hips set off at once and arrive at rest, and a change of mind keeps their speed.
    /// </summary>
    public static void Ease(ref PersonStealth p, bool low, PersonStealthSettings settings)
    {
        const float dt = World.TickSeconds;
        byte to = low ? (byte)1 : (byte)0;
        if (to != p.StanceTo)
        {
            p.StanceTo = to;
            p.StanceFrom = p.Stance - to;
            p.StanceSpeed = p.StanceRate;
            p.StanceAge = 0f;
        }
        if (p.StanceFrom == 0f && p.StanceSpeed == 0f) { p.Stance = to; p.StanceRate = 0f; return; }
        p.StanceAge += dt;
        float blend = to == 1 ? settings.In : settings.Out;
        float was = p.Stance;
        p.Stance = Math.Clamp(to + Inertial.Offset(new Vector3(p.StanceFrom, 0f, 0f), new Vector3(p.StanceSpeed, 0f, 0f), p.StanceAge, blend).X, 0f, 1f);
        p.StanceRate = (p.Stance - was) / dt;
        if (p.StanceAge >= blend) { p.Stance = to; p.StanceFrom = 0f; p.StanceSpeed = 0f; p.StanceRate = 0f; }
    }

    /// <summary>
    /// The duck's fade: whether the fade under way is from a walk or a run into the crouch walk (<paramref name="into"/>) or back out of it, so it takes
    /// <see cref="PersonStealthSettings.In"/> or <see cref="PersonStealthSettings.Out"/> and keeps the clip it leaves at the same point of its cycle.
    /// </summary>
    public static bool Ducking(PersonMotion person, in CharacterAnimation a, out bool into)
    {
        into = false;
        if (a.PreviousClip < 0 || a.Clip < 0 || person.Crouch?.Walk is not { } low) return false;
        bool Upright(int clip) => person.Walk?.Holds(clip) == true || person.Run?.Holds(clip) == true || (clip >= 0 && clip == person.Sneak);   // the sneak too
        if (Upright(a.PreviousClip) && low.Holds(a.Clip)) { into = true; return true; }
        return low.Holds(a.PreviousClip) && Upright(a.Clip);
    }
}

/// <summary>
/// <b>the stealth key</b> (the Input phase, after the masquerade): a press of <see cref="Buttons.Stealth"/> toggles stealth for the body the player
/// controls with a <see cref="PersonStealth"/>, told (<see cref="StealthChanged"/>). Deterministic: the press is on the tape.
/// </summary>
public sealed class StealthSystem : ISystem
{
    /// <summary>Its place in the Input phase: after the masquerade's mode.</summary>
    public const int Order = MasqueradeServices.ModeOrder + 2;

    public void Run(World world)
    {
        if (!world.TryService<TickInput>(out var input) || (input.Pressed & Buttons.Stealth) == 0) return;
        if (!world.TryStore<PersonStealth>(out var stealth) || !world.TryStore<Controlled>(out var tags)) return;
        var bodies = world.TryStore<Body>(out var bs) ? bs : null;
        var changed = world.Events<StealthChanged>();
        var data = stealth.Data;
        var ents = stealth.Entities;
        for (int i = 0; i < data.Length; i++)
        {
            if (!tags.Has(ents[i])) continue;
            data[i].On = data[i].On != 0 ? 0 : 1;
            changed.Publish(new StealthChanged(ents[i], world.Tick, data[i].On != 0, bodies is not null && bodies.Has(ents[i]) ? bodies.Get(ents[i]).Feet : default));
        }
    }
}
