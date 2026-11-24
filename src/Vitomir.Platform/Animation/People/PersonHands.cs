// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours (confirmed in play: "the hands clip into the walls and sometimes float over them"): the hands of the models
// stream's moves over and onto things put on the real top by the arms' IK while the clip has them down (CONVENTIONS, Notes, traverse.hands: "between
// those times the hand stays on that point of the world, and the engine's ledge IK may put it on the lip it finds").
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>the hands on the real top</b> (<see cref="PersonHandsRule"/>): whether they are put there (<b>SANDBOX</b>, ours). Off (<see cref="Off"/>:
/// <c>--hands-on-tops-off</c>) the hands are where the clips have them, over or under a real top off the clip's own height. Presentation: nothing simulated
/// moves (the capsule, the clip, its time) and nothing digested is kept; the drawn pose is (the hit volumes are posed on it), so a tape carries it in its
/// note <see cref="TapeNote"/>; a tape without it is the earlier build's.
/// </summary>
public sealed record PersonHandsSettings
{
    /// <summary>Whether a move's hands are put on the real top while its clip has them down (on by default; off: the earlier build's, <c>--hands-on-tops-off</c>).</summary>
    public bool OnTops { get; init; } = true;

    /// <summary>
    /// How long, in the clip's own seconds, the IK takes to come on before a hand's <c>contact_s</c> and to go after its <c>release_s</c> (0.1 s: the clips
    /// bring a hand down onto its point over about that, from 6 cm over it).
    /// </summary>
    public float EaseSeconds { get; init; } = 0.1f;

    /// <summary>Whether any is on.</summary>
    public bool Any => OnTops;

    /// <summary>None.</summary>
    public static PersonHandsSettings Off => new() { OnTops = false };

    /// <summary>The tape head's note.</summary>
    public const string TapeNote = "hands";

    /// <summary>The note: <c>off</c>, else <c>tops=1 ease=0.1</c>.</summary>
    public string Note() => !OnTops ? "off" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"tops=1 ease={EaseSeconds:0.####}");

    /// <summary>These settings with a tape's note laid on; none (an older tape) or <c>off</c> is <see cref="Off"/>. The numbers go through the note's text.</summary>
    public PersonHandsSettings WithNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note) || !note.Contains('=')) return Off;
        var s = this with { OnTops = true };
        foreach (var part in note.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || !float.TryParse(part[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            s = part[..eq] switch { "tops" => s with { OnTops = v != 0f }, "ease" => s with { EaseSeconds = v }, _ => s };
        }
        return s.OnTops ? s : Off;
    }
}

/// <summary>
/// <b>where a person's hands go beyond its clip</b> this tick (<see cref="PersonHandsRule.Of"/>): each wrist moved by an offset (the model's space, its
/// units) by a weight (0 none, 1 all of it). Made from the world's state when the pose is made; not kept.
/// </summary>
public readonly record struct PersonHands(Vector3 Left, float LeftWeight, Vector3 Right, float RightWeight, Vector3 Body = default)
{
    // Body: the drawn body moved with the hands (the model's space; a vault's: the arm straight under it, the body goes with the hand).

    /// <summary>Whether either hand is moved.</summary>
    public bool On => LeftWeight > 0f || RightWeight > 0f;
}

/// <summary>
/// <b>the hands of a move over or onto a thing put on the real top</b>. The motor lays a clip's path on the real obstacle so the
/// hands meet its edge across, and so the feet stay on the ground, by stretching the path's rise by the height's difference from the clip's own top
/// (<see cref="BodyMotor.OverPoint"/>): at a height off the clip's own the capsule at the hands' contact is over or under where the clip has it against its
/// own top — an earlier build measured the hands +10 to −15 cm off at the bands' edges, into the top or floating over it. While the clip has a hand down
/// (<c>traverse.hands</c>: from <c>contact_s</c> to <c>release_s</c>, eased in over <see cref="PersonHandsSettings.EaseSeconds"/> before and out after),
/// its wrist is moved by the capsule's difference from where the clip's own path has it against the obstacle — the clip's forward and up less the drawn
/// capsule's, both measured from the face and the top (<see cref="OverMove.Origin"/>, <see cref="OverMove.Top"/>) — and the arm solved to it by the rig's
/// two-bone IK (<see cref="LimbIK.Solve"/>: the elbow in the clip's own plane, the hand keeping its turn), so the palm lies on the real top where the clip
/// lays it on its own: at the real height and the real edge. Presentation only: the capsule, the clip and its time are the motor's and the animation's
/// as before; the offset is made from them each time the pose is made and not kept.
/// </summary>
public static class PersonHandsRule
{
    /// <summary>The hands of <paramref name="entity"/> for its pose this tick (none when the world has no settings or they are off, or no move's clip with hands plays).</summary>
    public static PersonHands Of(World world, EntityId entity, CharacterAssets assets, in CharacterAnimation state)
    {
        // A catch at a lip above what the arc's capsule reaches — the arms reach the rest.
        if (Catching(world, entity, assets, state) is { On: true } reach) return reach;
        // A pull-up going on — the hands kept on the real lip while the capsule's difference from its path is taken out.
        if (Flowing(world, entity, assets, state) is { On: true } held) return held;
        if (!world.TryService<PersonHandsSettings>(out var settings) || !settings.OnTops || !world.TryStore<PersonOver>(out var overs) || !overs.Has(entity)
            || !world.TryStore<Body>(out var bodies) || !bodies.Has(entity))
            return default;
        var move = overs.Get(entity).Move;
        if (move.Clip <= 0 || assets.Kind(state.Kind) is not { Person.Over.Paths: { } paths } kind || paths.At(move.Clip - 1) is not { Hands.Length: > 0 } c
            || state.Clip != c.Clip)
            return default;
        ref readonly var body = ref bodies.Get(entity);
        var hands = For(c, move, body.Feet, body.Yaw, kind.FacingYaw, kind.Scale > 0f ? kind.Scale : 1f, state.Time, settings.EaseSeconds);
        // Meanwhile: a low vault's hand its clip holds high over its own top lowered onto it, the body not with it (tier 1's, off on the earlier build's).
        if (c.HandDrop.Length == c.Hands.Length && world.TryService<PersonMovesSettings>(out var moves) && moves.On) hands = Dropped(c, hands, state.Time, settings.EaseSeconds, kind.Scale > 0f ? kind.Scale : 1f);
        return hands;
    }

    /// <summary><paramref name="hands"/> with each hand's <see cref="OverClip.HandDrop"/> laid on by its weight while it is down (the model's space).</summary>
    static PersonHands Dropped(OverClip c, PersonHands hands, float t, float ease, float scale)
    {
        float left = 0f, right = 0f;
        for (int i = 0; i < c.Hands.Length; i++)
        {
            var h = c.Hands[i];
            float d = c.HandDrop[i] * Weight(t, h.ContactS, h.ReleaseS, ease) / scale;
            if (h.Left) left = MathF.Max(left, d); else right = MathF.Max(right, d);
        }
        if (left <= 0f && right <= 0f) return hands;
        return hands with { Left = hands.Left - new Vector3(0f, 0f, left), Right = hands.Right - new Vector3(0f, 0f, right) };
    }

    /// <summary>
    /// The hands of a move on clip <paramref name="c"/> (its state <paramref name="move"/>) with the capsule's feet at <paramref name="feet"/> and the body
    /// facing <paramref name="yaw"/> (the kind's <paramref name="facingYaw"/> and <paramref name="scale"/>), at <paramref name="t"/> s of the clip.
    /// </summary>
    public static PersonHands For(OverClip c, in OverMove move, Vector3 feet, float yaw, float facingYaw, float scale, float t, float ease)
    {
        float left = 0f, right = 0f;
        foreach (var h in c.Hands)
        {
            float w = Weight(t, h.ContactS, h.ReleaseS, ease);
            if (h.Left) left = MathF.Max(left, w); else right = MathF.Max(right, w);
        }
        if (left <= 0f && right <= 0f) return default;
        // The capsule's difference from the clip's own against the obstacle: forward from the face and up from the top.
        var (f, u) = c.At(t);
        float drawn = (feet.X - move.Origin.X) * move.Way.X + (feet.Y - move.Origin.Y) * move.Way.Y;
        float ahead = f - drawn, up = (u - c.TopM) - (feet.Z - move.Top);
        var world = new Vector3(move.Way.X * ahead, move.Way.Y * ahead, up);
        // Into the model's space (the placement's turn and scale undone: Placement).
        var (s, co) = DetMath.SinCos(yaw - facingYaw);
        var model = new Vector3(co * world.X + s * world.Y, -s * world.X + co * world.Y, world.Z) / scale;
        // A vault's hand is a straight arm under the body (the clip's own support): the drawn body goes with it, by the hand's weight, so nothing
        // pulls it about as the hand comes down (the hips followed the hand's own landing on the top, a 15 mm pop); the IK then has little to do.
        var body = c.Kind == TraversalKind.Vault ? model * MathF.Min(1f, MathF.Max(left, right)) : Vector3.Zero;
        return new PersonHands(model, left, model, right, body);
    }

    /// <summary>
    /// <b>the arms' reach in a catch</b> at a lip above what the arc's capsule reaches with the grab clip's own contact pose: the motor keeps
    /// the capsule under the clip's place by <see cref="AirCommit.Arms"/> at the hands' contact (<see cref="BodyMotor.CatchPoint"/>: the arc blended to it from
    /// the commit, taken out over <see cref="BodyMotor.CatchSettle"/> after), and the wrists are raised by as much — the arms solved to it by the rig's IK, and
    /// what an arm cannot reach taken by the drawn body (<see cref="Apply"/>) — so the knuckles meet the edge. Presentation only, made each time the pose is made.
    /// </summary>
    static PersonHands Catching(World world, EntityId entity, CharacterAssets assets, in CharacterAnimation state)
    {
        if (!world.TryStore<PersonCommit>(out var commits) || !commits.Has(entity) || !world.TryStore<PersonLedge>(out var ledges) || !ledges.Has(entity)) return default;
        var k = commits.Get(entity).Move;
        if (k.Catch != 1 || k.Arms <= 0f || k.Contact <= 0f) return default;
        var l = ledges.Get(entity).Move;
        if (l.Clip <= 0 || assets.Kind(state.Kind) is not { Person.Ledge.Paths: { } paths } kind || paths.At(l.Clip - 1) is not { } c || state.Clip != c.Clip) return default;
        float up = k.Arms * Share(l.Age, k.Contact);
        if (up <= 0f) return default;
        var model = new Vector3(0f, 0f, up / (kind.Scale > 0f ? kind.Scale : 1f));
        return new PersonHands(model, 1f, model, 1f);
    }

    /// <summary>
    /// <b>the hands of a pull-up going on</b> (<see cref="LedgeMove.Fade"/> 4: entered from a grab's settle or a brace past its start): the motor takes the
    /// capsule's difference from the pull-up's path out over <see cref="BodyMotor.FlowBlend"/>, so the drawn body is off the clip's place by what is left of
    /// it — and the hands, which the clip has on the edge, by as much (by the pull-up's share of the pose while it fades in; the clip it fades from, held,
    /// by how far the capsule has gone since). The wrists are moved back by it (the arms solved by the rig's IK) so the knuckles stay on the real lip. Presentation only,
    /// made each time the pose is made.
    /// </summary>
    static PersonHands Flowing(World world, EntityId entity, CharacterAssets assets, in CharacterAnimation state)
    {
        if (!world.TryStore<PersonLedge>(out var ledges) || !ledges.Has(entity) || !world.TryStore<Body>(out var bodies) || !bodies.Has(entity)) return default;
        var l = ledges.Get(entity).Move;
        if (l.Fade != 4 || l.Clip <= 0 || assets.Kind(state.Kind) is not { Person.Ledge.Paths: { } paths } kind || paths.At(l.Clip - 1) is not { Role: LedgeRole.PullUp } c
            || state.Clip != c.Clip)
            return default;
        static float Smooth(float x) { x = Math.Clamp(x, 0f, 1f); return x * x * (3f - 2f * x); }
        float wa = l.AcrossTo > l.AcrossFrom + 1e-5f ? Smooth((l.Age - l.AcrossFrom) / (l.AcrossTo - l.AcrossFrom)) : l.Age >= l.AcrossTo ? 1f : 0f;
        float wu = l.UpTo > l.UpFrom + 1e-5f ? Smooth((l.Age - l.UpFrom) / (l.UpTo - l.UpFrom)) : l.Age >= l.UpTo ? 1f : 0f;
        // The pull-up's share of the pose is off the edge by what is left of the difference; the clip it fades from (held where it was) had its hands on it
        // where the capsule was when the pull-up began, so is off by how far the capsule has gone since.
        float shown = state.PreviousClip >= 0 ? Math.Clamp(state.Blend, 0f, 1f) : 1f;
        var start = BodyMotor.LedgeRaw(l, c, BodyMotor.PullUpEntry(c)) + l.Delta;
        var gone = bodies.Get(entity).Feet - start;
        var left = new Vector3(-l.Delta.X * (1f - wa), -l.Delta.Y * (1f - wa), -l.Delta.Z * (1f - wu)) * shown - gone * (1f - shown);
        if (left.LengthSquared() < 1e-8f) return default;
        var (sn, co) = DetMath.SinCos(bodies.Get(entity).Yaw - kind.FacingYaw);
        var model = new Vector3(co * left.X + sn * left.Y, -sn * left.X + co * left.Y, left.Z) / (kind.Scale > 0f ? kind.Scale : 1f);
        return new PersonHands(model, 1f, model, 1f);
    }

    /// <summary>The arms' share of a catch's reach <paramref name="age"/> s after the commit (from nought, all of it at the contact, nought again after the settle).</summary>
    public static float Share(float age, float contact)
    {
        static float Smooth(float x) { x = Math.Clamp(x, 0f, 1f); return x * x * (3f - 2f * x); }
        return age < contact ? Smooth(age / contact) : 1f - Smooth((age - contact) / BodyMotor.CatchSettle);
    }

    /// <summary>How much a hand down from <paramref name="contact"/> to <paramref name="release"/> s is moved at <paramref name="t"/>: eased in over <paramref name="ease"/> before, out over it after.</summary>
    public static float Weight(float t, float contact, float release, float ease)
    {
        if (t >= contact && t <= release) return 1f;
        float e = MathF.Max(ease, 1e-3f);
        float x = t < contact ? 1f - (contact - t) / e : 1f - (t - release) / e;
        if (x <= 0f) return 0f;
        return x * x * (3f - 2f * x);
    }

    /// <summary>
    /// Lays <paramref name="hands"/> on a pose (<paramref name="local"/>, changed; <paramref name="model"/> scratch): each wrist moved by its offset by its
    /// weight, its arm solved by the two-bone IK (the elbow bending in the plane the pose has it in; the hand keeping its turn in the model's space).
    /// </summary>
    public static void Apply(Skeleton skeleton, LimbMap limbs, in PersonHands hands, Span<BoneTransform> local, Span<Matrix4x4> model)
    {
        if (!hands.On) return;
        if (hands.Body != Vector3.Zero)
        {
            var root0 = local[limbs.Root];
            local[limbs.Root] = root0 with { Translation = root0.Translation + hands.Body };
        }
        PoseEvaluator.ToModel(skeleton, local, model);
        bool left = hands.LeftWeight > 0f && limbs.LeftArm.Has, right = hands.RightWeight > 0f && limbs.RightArm.Has;
        // (the body's move already carried the wrists by Body)
        var targetLeft = left ? model[limbs.LeftArm.End].Translation + hands.Left * MathF.Min(1f, hands.LeftWeight) - hands.Body : default;
        var targetRight = right ? model[limbs.RightArm.End].Translation + hands.Right * MathF.Min(1f, hands.RightWeight) - hands.Body : default;
        // An arm that cannot reach (a vault's straight arm under the body, a hanging arm over it): the drawn body moved by what it lacks, as the rig's
        // hanging hands pull the hips (PoseEvaluator.Place), so the hand is still on the top; the capsule is where it was. The lack is taken softly
        // (Soft: never a kink as the arm comes to its reach, a little bend kept in it) and by the hand's weight, so the body never jumps.
        for (int pass = 0; pass < 2; pass++)
        {
            var (shift, more) = left ? Short(model, limbs.LeftArm, targetLeft, hands.LeftWeight) : (Vector3.Zero, 0f);
            if (right && Short(model, limbs.RightArm, targetRight, hands.RightWeight) is var r && r.More > more) (shift, more) = r;
            if (more <= 1e-5f) break;
            var root = local[limbs.Root];
            local[limbs.Root] = root with { Translation = root.Translation + shift };
            PoseEvaluator.ToModel(skeleton, local, model);
        }
        if (left) LimbIK.Solve(skeleton, local, model, limbs.LeftArm, targetLeft, 1f, new Vector3(-0.5f, -0.5f, -0.7f));
        if (right) LimbIK.Solve(skeleton, local, model, limbs.RightArm, targetRight, 1f, new Vector3(0.5f, -0.5f, -0.7f));
    }

    /// <summary>How soft the arms' reach is taken (m): <see cref="Short"/>'s pull is <c>lack² / (lack + Softness)</c>, none without a lack.</summary>
    public const float Softness = 0.01f;

    /// <summary>
    /// How far the body must move for <paramref name="arm"/> to reach its wrist's <paramref name="target"/> (m, and which way): its lack — how far the target is
    /// beyond both 0.98 of the arm's reach and where the pose already has the wrist (an arm the clip holds straight is not pulled for that) — taken softly
    /// (nothing at no lack, its slope nought there, so the body never jerks as the arm comes to its reach; within <see cref="Softness"/> of all of it), by
    /// <paramref name="weight"/>.
    /// </summary>
    static (Vector3 Shift, float More) Short(ReadOnlySpan<Matrix4x4> model, in Limb arm, Vector3 target, float weight)
    {
        var shoulder = model[arm.Upper].Translation;
        var d = target - shoulder;
        float l = LimbIK.Length(d), lack = l - MathF.Max(LimbIK.Reach(model, arm) * 0.98f, LimbIK.Length(model[arm.End].Translation - shoulder));
        if (l <= 0f || lack <= 0f) return (Vector3.Zero, 0f);
        float more = MathF.Min(1f, weight) * lack * lack / (lack + Softness);
        return (d / l * more, more);
    }
}
