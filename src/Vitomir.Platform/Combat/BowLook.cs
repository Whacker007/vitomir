// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the player's swap between the melee weapon and the bow, and the bow's look — 1.1's own bow clips for Christof
// (Christof.nms: stand_bow ChrisStandBow, walk_bow ChrisDWalkLongbow, run_bow ChrisRunBows, bow chrisAttackBow, hurt_bow
// ChrisDamageBow) and 1.1's bow model in his hands, read from the install at run time (OneOneLibrary, with 1.1's longBow template:
// darkAges.not, WEAPON_TYPE CARRY BOW, WEAPON_MELEETYPE ATTACK_BOW, WEAPON_MODEL bow). The shot clip is played by the bow's state.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Combat;

/// <summary>
/// The player's swap between the melee weapon and the bow under way (digested, a world with a bow only): the seconds
/// left of it and which way it goes. Written by the player's controls (<see cref="Control.PlayerBow"/>), which set
/// <see cref="BowIntent.Ranged"/> when it ends.
/// </summary>
public struct BowHand
{
    /// <summary>Seconds left of a swap under way (0: none).</summary>
    public float SwapLeft;
    int toRanged;

    /// <summary>Whether the swap under way takes the bow (else the melee weapon).</summary>
    public bool ToRanged { readonly get => toRanged != 0; set => toRanged = value ? 1 : 0; }

    int waitRelease;

    /// <summary>Aim let go while drawing: no draw until attack is let go.</summary>
    public bool WaitRelease { readonly get => waitRelease != 0; set => waitRelease = value ? 1 : 0; }
}

/// <summary>
/// The kinds a body with a bow is drawn and posed as (digested): its melee kind (its weapon in hand) and its bow kind
/// (1.1's bow clips and model, <see cref="CharacterAssets"/> indices; −1 none). <see cref="BowLookSystem"/> puts the body's
/// <see cref="CharacterAnimation.Kind"/> to the one in hand. Given by the session with the bow, unless the switch
/// <c>bow-look-off</c> keeps the earlier build's archer (his melee stance, his sword in hand).
/// </summary>
public struct BowLook
{
    public int MeleeKind, RangedKind;
}

/// <summary>
/// Where the bow's shot clip (1.1's <c>chrisAttackBow</c>: 39 frames at 30 a second, its <c>Fire</c> mark at frame 19) has the bow
/// raised, drawn and loosed (found from the clip's own hands, not hand-set): <see cref="DrawStart"/>, where the draw hand
/// begins to leave the bow hand (the raise before it); <see cref="FullDraw"/>, the hands furthest apart up to the mark; <see cref="Release"/>,
/// the mark (the loose); the hands' separation by frame up to it, so a draw share is shown as the clip's own draw at that share.
/// </summary>
public sealed class BowShot
{
    public required int Clip { get; init; }
    public required float Rate { get; init; }
    public required float DrawStart { get; init; }
    public required float FullDraw { get; init; }
    public required float Release { get; init; }
    public required float Duration { get; init; }
    /// <summary>The hands' separation (m) at each frame from 0 to the release's.</summary>
    public required float[] Separation { get; init; }

    /// <summary>
    /// How far the clip's drawn arrow (from the draw hand to the bow hand at full draw) points to the left of the body's facing, across
    /// the ground (radians; right negative): 1.1's archer stands side-on and his arrow does not point straight along his facing. The
    /// player's body (and a script's archer) is turned by it while the bow is up, so the arrow drawn points where the arrow will fly.
    /// </summary>
    public float AimTurn { get; init; }

    /// <summary>A share of the draw rises past <see cref="DrawStart"/> by this share of the separation's rise (ENGINE_SANDBOX, ours).</summary>
    public const float StartRise = 0.1f;

    /// <summary>The clip seconds that show the draw at <paramref name="share"/> (0 the draw's start, 1 full): the first time the hands are that share of the way from their separation at <see cref="DrawStart"/> to the furthest.</summary>
    public float TimeAt(float share)
    {
        share = Math.Clamp(share, 0f, 1f);
        int start = (int)MathF.Round(DrawStart * NadFrames), full = (int)MathF.Round(FullDraw * NadFrames);
        if (full <= start) return FullDraw;
        float s0 = Separation[start], s1 = Separation[full];
        float want = s0 + share * (s1 - s0);
        for (int f = start + 1; f <= full; f++)
            if (Separation[f] >= want)
            {
                float a = Separation[f - 1], b = Separation[f];
                float t = b > a ? Math.Clamp((want - a) / (b - a), 0f, 1f) : 1f;
                return (f - 1 + t) / NadFrames;
            }
        return FullDraw;
    }

    const float NadFrames = 30f;

    /// <summary>
    /// The shot of a bow kind (its first quick action: 1.1's <c>bow</c> mode), its draw and release found from the hands of
    /// <paramref name="limbs"/> on <paramref name="skeleton"/>; null when the kind has no shot, no arms or no release mark.
    /// </summary>
    public static BowShot? Of(CharacterAssets assets, int kind)
    {
        var k = assets.Kind(kind);
        if (k.Actions is not { Quick.Length: > 0 } set || k.Limbs is not { LeftArm.Has: true, RightArm.Has: true } limbs) return null;
        var motion = set.Quick[0];
        if (motion.StrikeSeconds <= 0f) return null;
        var clip = assets.Clip(motion.Clip);
        var skeleton = assets.ModelOf(kind).Skeleton;
        // The shot composed with the ready pose (BowReadyClip): its own timeline, the source's release mark moved with it.
        if (clip is BowReadyClip ready)
        {
            var of = Find(ready.Source, skeleton, limbs, motion.StrikeSeconds, k.Placement(Vector3.Zero, 0f));
            if (of is null) return null;
            float release = motion.StrikeSeconds + ready.Shift;
            int n = (int)MathF.Floor(release * NadFrames + 1e-3f);
            var sep = new float[n + 1];
            var l = new BoneTransform[skeleton.Count];
            var m = new Matrix4x4[skeleton.Count];
            for (int f = 0; f <= n; f++)
            {
                ready.Sample(MathF.Min(f / NadFrames, ready.Duration), skeleton, l);
                PoseEvaluator.ToModel(skeleton, l, m);
                sep[f] = Geometry.Length(m[limbs.LeftArm.End].Translation - m[limbs.RightArm.End].Translation);
            }
            return new BowShot
            {
                Clip = motion.Clip, Rate = motion.Rate, DrawStart = ready.ReadyAt, FullDraw = ready.FullDraw, Release = release, Duration = ready.Duration,
                Separation = sep, AimTurn = of.AimTurn, Ready = ready,
            };
        }
        var shot = Find(clip, skeleton, limbs, motion.StrikeSeconds, k.Placement(Vector3.Zero, 0f));
        return shot is null ? null : new BowShot
        {
            Clip = motion.Clip, Rate = motion.Rate, DrawStart = shot.DrawStart, FullDraw = shot.FullDraw, Release = shot.Release,
            Duration = clip.Duration, Separation = shot.Separation, AimTurn = shot.AimTurn,
        };
    }

    /// <summary>The shot composed with the ready pose, when it is (else the earlier build's: 1.1's clip itself).</summary>
    public BowReadyClip? Ready { get; init; }

    /// <summary>
    /// The draw's start, full draw and turn found from the hands of a shot clip (factored out so the ready pose can be
    /// composed from them before the kind is made): <paramref name="placement"/> the kind's placement at the origin, facing 0.
    /// </summary>
    public static BowShot? Find(IAnimationClip clip, Skeleton skeleton, LimbMap limbs, float strikeSeconds, Matrix4x4 placement)
    {
        var motion = (StrikeSeconds: strikeSeconds, Clip: 0, Rate: 1f);
        int frames = (int)MathF.Floor(motion.StrikeSeconds * NadFrames + 1e-3f);
        var sep = new float[frames + 1];
        var local = new BoneTransform[skeleton.Count];
        var model = new Matrix4x4[skeleton.Count];
        int lh = limbs.LeftArm.End, rh = limbs.RightArm.End;
        for (int f = 0; f <= frames; f++)
        {
            clip.Sample(MathF.Min(f / NadFrames, clip.Duration), skeleton, local);
            PoseEvaluator.ToModel(skeleton, local, model);
            sep[f] = Geometry.Length(model[lh].Translation - model[rh].Translation);
        }
        int far = 0;
        for (int f = 1; f <= frames; f++) if (sep[f] > sep[far]) far = f;
        clip.Sample(MathF.Min(far / NadFrames, clip.Duration), skeleton, local);
        PoseEvaluator.ToModel(skeleton, local, model);
        var way = Vector3.TransformNormal(model[lh].Translation - model[rh].Translation, placement);
        float turn = way.X * way.X + way.Y * way.Y > 1e-6f ? DetMath.Atan2(way.Y, way.X) : 0f;
        float rise = sep[far] - sep[0];
        int start = 0;
        for (int f = 1; f <= far; f++) if (sep[f] - sep[0] < StartRise * rise) start = f; else break;
        return new BowShot
        {
            Clip = motion.Clip, Rate = motion.Rate, DrawStart = start / NadFrames, FullDraw = far / NadFrames,
            Release = motion.StrikeSeconds, Duration = clip.Duration, Separation = sep, AimTurn = turn,
        };
    }
}

/// <summary>
/// The bow's look (<see cref="Phase.Act"/>, <see cref="BowServices.BowLookOrder"/>, after the bow): for each body with a
/// <see cref="Bow"/> and a <see cref="BowLook"/>,
/// <list type="bullet">
/// <item>the kind it is drawn and posed as is its bow kind while the bow is in hand, else its melee kind (the locomotion's next step
/// starts the kind's own stand, walk or run, cross-faded: 1.1's <c>stand_bow</c>, <c>walk_bow</c>, <c>run_bow</c>);</item>
/// <item>the shot clip on the action channel (<see cref="ActionRole.Shoot"/>, over the upper body while it moves) is posed by the bow:
/// raising, its start to <see cref="BowShot.DrawStart"/> by the raise's progress; nocked, held there; drawing and held, the clip's own
/// draw at the draw's share (<see cref="BowShot.TimeAt"/>); at the loose it plays on from its release mark at 1.1's rate, so the arrow
/// leaves as the clip's string is let go; a draw let down plays it backwards to its start.</item>
/// </list>
/// Deterministic (the shot's times are a function of the kind's clip); allocates nothing per tick once each kind's shot is found.
/// </summary>
public sealed class BowLookSystem : ISystem
{

    public void Run(World world)
    {
        if (!world.TryStore<BowLook>(out var looks) || looks.Count == 0 || !world.TryService<CharacterAssets>(out var assets)) return;
        var bows = world.Store<Bow>();
        var anims = world.Store<CharacterAnimation>();
        var loosed = world.Events<ArrowLoosed>().ThisTick;
        var cancelled = world.Events<DrawCancelled>().ThisTick;
        var config = world.Service<BowConfig>();
        var ents = looks.Entities;
        var data = looks.Data;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            ref var bow = ref bows.Find(e);
            ref var a = ref anims.Find(e);
            if (Unsafe.IsNullRef(ref bow) || Unsafe.IsNullRef(ref a)) continue;
            var look = data[i];
            int want = bow.InHand ? look.RangedKind : look.MeleeKind;
            if (want < 0 || want >= assets.KindCount) continue;
            if (ActionRoles.IsDeath(a.Action)) continue;
            if (a.Kind != want)
            {
                a.Kind = want;
                if (a.Action is ActionRole.Shoot or ActionRole.Block || ActionRoles.IsAttack(a.Action)) ActionChannel.Clear(ref a);
            }
            if (!bow.InHand || want != look.RangedKind) continue;
            var shot = Shot(assets, want);
            if (shot is null) continue;

            bool loosedNow = false, letDown = false;
            foreach (var l in loosed) if (l.Archer == e) loosedNow = true;
            foreach (var c in cancelled) if (c.Archer == e && c.Cause != DrawCancelCause.Hurt) letDown = true;
            if (loosedNow)
            {
                // The loose: on from the clip's release mark at 1.1's rate (its string let go as the arrow leaves).
                Ensure(ref a, shot);
                a.ActionTime = a.ActionLast = shot.Release;
                a.ActionRate = shot.Rate;
                continue;
            }
            if (a.Action == ActionRole.Hurt && !a.ActionEnding) continue;   // a flinch plays over it (the draw is let down by the bow)
            if (shot.Ready is not null) { Composed(ref a, in bow, shot, config); continue; }   // the ready pose
            float time;
            switch (bow.State)
            {
                case BowState.Raising:
                {
                    var d = (uint)bow.Weapon < (uint)config.Bows.Count ? config.Bows[bow.Weapon] : null;
                    float need = d is null ? 0.25f : bow.OnString ? d.RaiseSeconds : d.NockSeconds;
                    time = shot.DrawStart * Math.Clamp(need > 0f ? bow.Seconds / need : 1f, 0f, 1f);
                    break;
                }
                case BowState.Nocked: time = shot.DrawStart; break;
                case BowState.Drawing:
                case BowState.Held: time = shot.TimeAt(bow.Share); break;
                default:
                    // Low at the waist: a draw let down plays back to the clip's start; a loose plays on to its end; else nothing.
                    if (letDown && a.Action == ActionRole.Shoot && !a.ActionEnding) a.ActionRate = -shot.Rate;
                    continue;
            }
            Ensure(ref a, shot);
            if (letDown) { a.ActionRate = -shot.Rate; continue; }
            a.ActionRate = 0f;
            a.ActionLast = a.ActionTime;
            a.ActionTime = time;
        }
    }

    /// <summary>How long the bow takes to come down from the ready pose (or a draw) to the waist, played backwards (s; ENGINE_SANDBOX, ours).</summary>
    public const float LowerSeconds = 0.25f;

    /// <summary>How long the loose's follow-through (1.1's clip from its release mark) plays before the bow goes back to the ready pose while the next arrow is nocked (s; ENGINE_SANDBOX, ours).</summary>
    public const float FollowThroughSeconds = 0.25f;

    /// <summary>How long a draw let down takes to ease back to the ready pose (s; ENGINE_SANDBOX, ours).</summary>
    public const float LetDownSeconds = 0.2f;

    /// <summary>
    /// The shot composed with the ready pose (<see cref="BowReadyClip"/>) posed by the bow's state: raising, from the waist
    /// to the ready pose by the raise's progress (after a loose, the follow-through plays on while the next arrow is nocked); nocked, the
    /// ready pose (a draw let down eases back to it over <see cref="LetDownSeconds"/>; after a loose's follow-through, cross-faded to it);
    /// drawing and held, the draw at its share; low (aim let go), played backwards to the waist over <see cref="LowerSeconds"/> and ended.
    /// </summary>
    static void Composed(ref CharacterAnimation a, in Bow bow, BowShot shot, BowConfig config)
    {
        const float dt = World.TickSeconds;
        bool playing = a.Action == ActionRole.Shoot && !a.ActionEnding && a.ActionClip == shot.Clip;
        bool afterLoose = playing && a.ActionTime > shot.FullDraw + 1e-4f;
        switch (bow.State)
        {
            case BowState.Raising:
            {
                if (afterLoose)
                {
                    // The loose's follow-through (the string hand flying back) for FollowThroughSeconds, then back to the ready pose,
                    // cross-faded, while the next arrow is nocked: in aim mode the bow stays up.
                    if (a.ActionTime < shot.Release + FollowThroughSeconds * shot.Rate) { a.ActionRate = shot.Rate; return; }
                    ActionChannel.Start(ref a, new ActionMotion(shot.Clip, shot.Rate, shot.Release, 0f, -1f, false, "bow"), ActionRole.Shoot);
                    Hold(ref a, shot.DrawStart);
                    return;
                }
                Ensure(ref a, shot);
                if (playing && a.ActionTime >= shot.DrawStart - 1e-4f) { Hold(ref a, MathF.Max(shot.DrawStart, a.ActionTime - (shot.FullDraw - shot.DrawStart) * dt / LetDownSeconds)); return; }   // up already: nocked there
                var d = (uint)bow.Weapon < (uint)config.Bows.Count ? config.Bows[bow.Weapon] : null;
                float need = d is null ? 0.25f : bow.OnString ? d.RaiseSeconds : d.NockSeconds;
                float time = shot.DrawStart * Math.Clamp(need > 0f ? bow.Seconds / need : 1f, 0f, 1f);
                if (a.ActionTime > time && a.ActionTime <= shot.DrawStart + 1e-4f) time = a.ActionTime;   // raised again before it was down: from where it is
                Hold(ref a, time);
                return;
            }
            case BowState.Nocked:
            {
                if (afterLoose)
                {
                    ActionChannel.Start(ref a, new ActionMotion(shot.Clip, shot.Rate, shot.Release, 0f, -1f, false, "bow"), ActionRole.Shoot);   // cross-faded to the ready pose
                    Hold(ref a, shot.DrawStart);
                    return;
                }
                Ensure(ref a, shot);
                float step = (shot.FullDraw - shot.DrawStart) * dt / LetDownSeconds;
                float time = a.ActionTime > shot.DrawStart ? MathF.Max(shot.DrawStart, a.ActionTime - step) : shot.DrawStart;
                Hold(ref a, time);
                return;
            }
            case BowState.Drawing:
            case BowState.Held:
                Ensure(ref a, shot);
                Hold(ref a, shot.TimeAt(bow.Share));
                return;
            default:
                // Low at the waist: played backwards to the start (then it ends); a loose's follow-through plays on to its end.
                if (playing && !afterLoose && a.ActionRate >= 0f) a.ActionRate = -MathF.Max(a.ActionTime, shot.DrawStart) / LowerSeconds;
                return;
        }
    }

    static void Hold(ref CharacterAnimation a, float time)
    {
        a.ActionRate = 0f;
        a.ActionLast = a.ActionTime;
        a.ActionTime = time;
    }

    BowShot? Shot(CharacterAssets assets, int kind) => shots.For(assets, kind);

    readonly BowShots shots = new();

    /// <summary>The shot on the channel (started from its beginning when something else is there, or it has begun to fade).</summary>
    static void Ensure(ref CharacterAnimation a, BowShot shot)
    {
        if (a.Action == ActionRole.Shoot && !a.ActionEnding && a.ActionClip == shot.Clip) return;
        ActionChannel.Start(ref a, new ActionMotion(shot.Clip, shot.Rate, shot.Release, 0f, -1f, false, "bow"), ActionRole.Shoot);
        a.ActionRate = 0f;
    }
}

/// <summary>
/// Each bow kind's shot found once (<see cref="BowShot.Of"/>; a function of the kind's clip, so deterministic), and the
/// turn an archer drawn with it is given while the bow is up (<see cref="BowShot.AimTurn"/>).
/// </summary>
public sealed class BowShots
{
    readonly Dictionary<int, BowShot?> shots = [];

    public BowShot? For(CharacterAssets assets, int kind)
    {
        if (!shots.TryGetValue(kind, out var shot)) shots[kind] = shot = BowShot.Of(assets, kind);
        return shot;
    }

    static readonly BowShots Shared = new();

    /// <summary>
    /// The turn (radians, left positive) <paramref name="e"/> is given while its bow is up: its bow kind's <see cref="BowShot.AimTurn"/>
    /// when it is drawn as it (a <see cref="BowLook"/>), else 0 (the earlier build's archer, <c>bow-look-off</c>).
    /// </summary>
    public static float TurnOf(World world, EntityId e)
    {
        if (!world.TryStore<BowLook>(out var looks) || !looks.Has(e) || !world.TryStore<CharacterAnimation>(out var anims) || !anims.Has(e)
            || !world.TryService<CharacterAssets>(out var assets)) return 0f;
        int kind = looks.Get(e).RangedKind;
        if (kind < 0 || kind >= assets.KindCount) return 0f;
        return Shared.For(assets, kind)?.AimTurn ?? 0f;
    }
}
