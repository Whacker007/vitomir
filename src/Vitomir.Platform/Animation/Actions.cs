// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation;

/// <summary>
/// What a character's action channel plays: 1.1's attacks, its shield block, its hurts
/// and deaths, played over the locomotion. <see cref="None"/> is the empty channel (a component's default).
/// </summary>
public enum ActionRole
{
    /// <summary>Nothing: the locomotion alone.</summary>
    None = 0,
    /// <summary>A quick blow: 1.1's attack for the carried weapon's class, its variants taken in turn. every directional blow plays as this role (its direction on <see cref="Control.Combatant.Direction"/>).</summary>
    AttackQuick,
    /// <summary>A strong blow: the class's <c>special</c> variant (Christof's sword: the whirl).</summary>
    AttackStrong,
    /// <summary>The block, held at its raised frame while the block is held.</summary>
    Block,
    /// <summary>Hurt: 1.1's <c>hurt_</c> for the carry class (the next batch triggers it).</summary>
    Hurt,
    /// <summary>A quick death (1.1's <c>deathquick</c>): held at its end.</summary>
    DeathQuick,
    /// <summary>A slow death (1.1's <c>deathslow</c>): held at its end.</summary>
    DeathSlow,
    /// <summary>
    /// At the end: the bow's shot (1.1's <c>bow</c> mode, Christof's <c>chrisAttackBow</c>), posed by the bow's state
    /// (<see cref="Combat.BowLookSystem"/>): not an attack (no blade is swept, no arc is drawn).
    /// </summary>
    Shoot,
}

/// <summary>The roles' groupings.</summary>
public static class ActionRoles
{
    public static bool IsAttack(ActionRole role) => role is ActionRole.AttackQuick or ActionRole.AttackStrong;

    public static bool IsDeath(ActionRole role) => role is ActionRole.DeathQuick or ActionRole.DeathSlow;
}

/// <summary>
/// One motion the action channel plays: a clip of <see cref="CharacterAssets"/>, the rate it plays at (1.1 stretches
/// an attack so the whole clip takes the weapon's <c>WEAPON_STRIKETIME</c>), where in it the blow lands (its <c>Strike</c>
/// mark, clip seconds; −1 for none), how long the character is busy after starting it (the weapon's
/// <c>WEAPON_RELOADTIME</c>, real seconds), the clip seconds a held action holds at (the block's raised frame; −1 for none),
/// whether it is a stand-in composed by the platform (<see cref="ComposedClip"/>; SANDBOX until a real motion comes), and
/// 1.1's mode it came from (for the log and the docs).
/// </summary>
public readonly record struct ActionMotion(int Clip, float Rate, float StrikeSeconds, float BusySeconds, float HoldSeconds, bool Composed, string Mode)
{
    public static ActionMotion None => new(-1, 1f, -1f, 0f, -1f, false, "");

    public bool Has => Clip >= 0;
}

/// <summary>
/// What a kind of character's action channel can play, filled from 1.1's motion set by 1.1's own rules for the
/// weapon it carries (<see cref="Assets.OneOne.OneOneActions"/>): the quick blow's variants (taken in turn), the strong
/// blow, the block, the hurt and the two deaths; and the bones the channel moves over a moving body (the upper body,
/// from the spine up) and the spine bones the aim offset bends.
/// </summary>
public sealed class ActionSet
{
    public required ActionMotion[] Quick { get; init; }
    public ActionMotion Strong { get; init; } = ActionMotion.None;
    public ActionMotion Block { get; init; } = ActionMotion.None;
    public ActionMotion Hurt { get; init; } = ActionMotion.None;
    public ActionMotion DeathQuick { get; init; } = ActionMotion.None;
    public ActionMotion DeathSlow { get; init; } = ActionMotion.None;

    /// <summary>For each bone, whether it is the upper body (the spine's first bone and every bone under it): the bones an action moves while the legs walk.</summary>
    public required bool[] UpperBody { get; init; }

    /// <summary>The spine bones the aim offset bends, hips to chest (skeleton 0: 14, 15, 16); empty for a kind with no known spine.</summary>
    public required int[] Spine { get; init; }

    /// <summary>The bone the weapon hand's tip is (the blow's point when nothing is held), or −1.</summary>
    public int WeaponHand { get; init; } = -1;

    /// <summary>The weapon carried (1.1's template name), or empty for bare hands.</summary>
    public string Weapon { get; init; } = "";

    /// <summary>1.1's carry class (<c>WEAPON_TYPE1</c>: 0 bare hands, 1 one-handed, …).</summary>
    public int Carry { get; init; }

    public bool HasAttack => Quick.Length > 0;

    /// <summary>
    /// The shield arm's bones (true for each), for the guard's layer that keeps the shield's block while the
    /// weapon arm swings (<see cref="Combat.GuardLayer"/>); null for a kind that holds no shield. Found once, at first need
    /// (<see cref="Combat.GuardLayer.ArmOf"/>), and kept here.
    /// </summary>
    public bool[]? GuardArm { get; set; }

    /// <summary>Whether <see cref="GuardArm"/> was looked for.</summary>
    public bool GuardArmKnown { get; set; }

    /// <summary>
    /// The blows stitched by the platform from 1.1's own clips for the directions 1.1 has no motion for (the
    /// overhead and the thrust, <see cref="StitchedAttacks"/>; <see cref="StitchedMotions.Stitched"/> finds one by direction);
    /// empty for a kind that has none. Nothing plays them until the direction table names them.
    /// </summary>
    public StitchedMotion[] Stitched { get; set; } = [];

    /// <summary>The motion for a role (the quick blow's variant <paramref name="variant"/>, taken round), or <see cref="ActionMotion.None"/>.</summary>
    public ActionMotion For(ActionRole role, int variant = 0) => role switch
    {
        ActionRole.AttackQuick => Quick.Length == 0 ? ActionMotion.None : Quick[(int)((uint)variant % (uint)Quick.Length)],
        ActionRole.AttackStrong => Strong.Has ? Strong : Quick.Length == 0 ? ActionMotion.None : Quick[0],
        ActionRole.Block => Block,
        ActionRole.Hurt => Hurt,
        ActionRole.DeathQuick => DeathQuick.Has ? DeathQuick : DeathSlow,
        ActionRole.DeathSlow => DeathSlow.Has ? DeathSlow : DeathQuick,
        _ => ActionMotion.None,
    };
}

/// <summary>The numbers the action channel blends by (ours, tunable).</summary>
public sealed record ActionChannelSettings
{
    /// <summary>How long an action takes to come in over the locomotion (s).</summary>
    public float FadeInSeconds { get; init; } = 0.1f;

    /// <summary>How long an action takes to give the body back to the locomotion when it ends (s): 1.1's fade, a fifth of a second.</summary>
    public float FadeOutSeconds { get; init; } = 0.2f;

    /// <summary>How long one action takes to cross-fade into the next on the channel (s).</summary>
    public float CrossSeconds { get; init; } = 0.1f;

    /// <summary>How long the legs take to change between taking the action (standing) and keeping the locomotion (moving) (s).</summary>
    public float LegsSeconds { get; init; } = 0.2f;
}

/// <summary>
/// The action channel's arithmetic (ours): starting an action on a <see cref="CharacterAnimation"/>, letting go
/// of a held one, and moving it on by a tick. 1.1's channel replaces the whole body and holds the locomotion's frame;
/// ours plays over the locomotion — on the upper body while the body moves, on the whole body while it stands (so a
/// standing swing is 1.1's swing, the blade where 1.1 had it) — and fades in and out.
/// </summary>
public static class ActionChannel
{
    /// <summary>
    /// Starts <paramref name="motion"/> as <paramref name="role"/> on the channel, from its start, held at its hold frame
    /// while <paramref name="hold"/>; an action already playing is cross-faded from.
    /// </summary>
    public static void Start(ref CharacterAnimation a, in ActionMotion motion, ActionRole role, bool hold = false, float rateScale = 1f)
    {
        if (!motion.Has) return;
        if (a.Action != ActionRole.None && a.ActionWeight > 0f)
        {
            a.ActionFrom = a.Action;
            a.ActionFromClip = a.ActionClip;
            a.ActionFromTime = a.ActionTime;
            a.ActionFromRate = a.ActionRate;
            a.ActionBlend = 0f;
        }
        else
        {
            a.ActionFrom = ActionRole.None;
            a.ActionBlend = 1f;
            a.ActionWeight = 0f;
        }
        a.Action = role;
        a.ActionClip = motion.Clip;
        a.ActionTime = 0f;
        a.ActionLast = 0f;
        a.ActionRate = motion.Rate * rateScale;
        a.ActionHoldAt = motion.HoldSeconds;
        a.ActionHolds = hold && motion.HoldSeconds >= 0f;
        a.ActionEnding = false;
        a.ActionFadeOut = 0f;
        a.ActionCrossSeconds = 0f;   // the channel's own cross-fade
        if (ActionRoles.IsDeath(role)) a.ActionLegs = 1f;
    }

    /// <summary>
    /// Bannerlord's press-to-ready: starts a blow <b>held ready</b> — its clip plays its
    /// wind-up to <paramref name="readyAt"/> (clip seconds, just before its active window: <see cref="Control.DirectionalAttacks.ReadyPoint"/>)
    /// and holds there (the hold the block uses, <see cref="CharacterAnimation.ActionHolds"/>) until <see cref="Strike"/> lets it go.
    /// With no ready point (−1) the blow plays through at once.
    /// </summary>
    public static void StartReady(ref CharacterAnimation a, in ActionMotion motion, ActionRole role, float readyAt, float rateScale = 1f)
    {
        Start(ref a, motion, role, hold: false, rateScale);
        if (!motion.Has || readyAt < 0f) return;
        a.ActionHoldAt = readyAt;
        a.ActionHolds = true;
    }

    /// <summary>
    /// (hold attack, then change the movement to change the attack direction, as Mount &amp; Blade has it): the blow
    /// on the channel, held ready or still winding up to its ready point, becomes <paramref name="motion"/> — cross-faded over
    /// <paramref name="crossSeconds"/> from where it is into the new clip at <paramref name="readyAt"/> (its ready point) when the old
    /// one had reached its own, else at the same share of the new wind-up as the old had played — held there as ever.
    /// </summary>
    public static void SwitchReady(ref CharacterAnimation a, in ActionMotion motion, float readyAt, float crossSeconds, float rateScale = 1f)
    {
        if (!motion.Has || readyAt < 0f) return;
        bool reached = AtReady(a);
        float share = reached || a.ActionHoldAt <= 0f ? 1f : Math.Clamp(a.ActionTime / a.ActionHoldAt, 0f, 1f);
        StartReady(ref a, motion, a.Action, readyAt, rateScale);
        a.ActionFromRate = 0f;   // the old blow's pose held where it was while it fades out (not played on into its strike)
        a.ActionTime = a.ActionLast = readyAt * share;
        a.ActionCrossSeconds = MathF.Max(0f, crossSeconds);
    }

    /// <summary>A blow held ready let go (attack released): it plays on from where it is, through its strike (a tap's, still winding up, plays through without a stop).</summary>
    public static void Strike(ref CharacterAnimation a) => a.ActionHolds = false;

    /// <summary>Whether the channel's blow is held at its ready point now (its time at the hold and the hold on): it does not move this tick.</summary>
    public static bool AtReady(in CharacterAnimation a) =>
        ActionRoles.IsAttack(a.Action) && a.ActionHolds && a.ActionHoldAt >= 0f && !a.ActionEnding && a.ActionTime >= a.ActionHoldAt;

    /// <summary>
    /// The action ended where it stands and faded out over <paramref name="seconds"/> (not the channel's fade-out): a
    /// stopped blade's pose blended back into the stance (<see cref="Combat.BringBackKind.Blend"/>). Held where it is (its rate 0).
    /// </summary>
    public static void FadeFromHere(ref CharacterAnimation a, float seconds)
    {
        if (a.Action == ActionRole.None) return;
        a.ActionRate = 0f;
        a.ActionEnding = true;
        a.ActionFadeOut = MathF.Max(seconds, World.TickSeconds);
    }

    /// <summary>
    /// A held action let go: it plays on from where it is (1.1's shield block lowers itself) and then fades, or with
    /// <paramref name="fadeNow"/> fades from where it is (a composed guard, one held pose).
    /// </summary>
    public static void Release(ref CharacterAnimation a, bool fadeNow = false)
    {
        a.ActionHolds = false;
        if (fadeNow) a.ActionEnding = true;
    }

    /// <summary>Clears the channel at once.</summary>
    public static void Clear(ref CharacterAnimation a)
    {
        a.Action = ActionRole.None;
        a.ActionFrom = ActionRole.None;
        a.ActionWeight = 0f;
        a.ActionHolds = false;
        a.ActionEnding = false;
        a.ActionFadeOut = 0f;
    }

    /// <summary>
    /// The channel moved on by <paramref name="seconds"/>: its time by its rate (held at the hold frame while held, at
    /// its end when done), its weight towards 1 (or down when it has ended; a death never ends), the cross-fade from the
    /// action before, and the legs towards taking it when <paramref name="standing"/>. <paramref name="fadeInScale"/> runs
    /// the fade-in and the cross-fade into the action that much faster (the block's rise rate, <see cref="Combat.CombatTempo.Rise"/>);
    /// at 1 it is the first channel exactly.
    /// </summary>
    public static void Advance(ref CharacterAnimation a, CharacterAssets assets, ActionChannelSettings s, bool standing, float seconds, float fadeInScale = 1f)
    {
        Combat.GuardLayer.Advance(ref a, seconds, s.FadeInSeconds / MathF.Max(fadeInScale, 1e-3f));   // the shield arm's layer
        if (a.Action == ActionRole.None) { a.ActionLegs = standing ? 1f : 0f; return; }
        float duration = assets.Clip(a.ActionClip).Duration;
        a.ActionLast = a.ActionTime;
        float t = a.ActionTime + seconds * a.ActionRate;
        if (a.ActionHolds && a.ActionHoldAt >= 0f && t > a.ActionHoldAt) t = MathF.Max(a.ActionTime, a.ActionHoldAt);
        // An action played backwards (a stopped blade's bring-back, a rebound) stops at its start.
        a.ActionTime = MathF.Max(0f, MathF.Min(t, duration));
        if (a.ActionFrom != ActionRole.None)
        {
            a.ActionFromTime = Math.Clamp(a.ActionFromTime + seconds * a.ActionFromRate, 0f, assets.Clip(a.ActionFromClip).Duration);
            float cross = a.ActionCrossSeconds > 0f ? a.ActionCrossSeconds : s.CrossSeconds;   // a held blow's switch has its own
            a.ActionBlend += cross > 0f ? seconds * fadeInScale / cross : 1f;
            if (a.ActionBlend >= 1f) { a.ActionBlend = 1f; a.ActionFrom = ActionRole.None; }
        }
        bool death = ActionRoles.IsDeath(a.Action);
        if (!death && !a.ActionHolds && a.ActionTime >= duration) a.ActionEnding = true;
        // Played back to its start (a bring-back by the clip reversed, Combat.BringBackKind.Reverse), it ends there.
        if (!death && a.ActionRate < 0f && a.ActionTime <= 0f) a.ActionEnding = true;
        if (a.ActionEnding)
        {
            float fadeOut = a.ActionFadeOut > 0f ? a.ActionFadeOut : s.FadeOutSeconds;   // a bring-back's own length
            a.ActionWeight -= fadeOut > 0f ? seconds / fadeOut : 1f;
            if (a.ActionWeight <= 0f) { Clear(ref a); a.ActionLegs = standing ? 1f : 0f; return; }
        }
        else a.ActionWeight = MathF.Min(1f, a.ActionWeight + (s.FadeInSeconds > 0f ? seconds * fadeInScale / s.FadeInSeconds : 1f));
        float legs = death || standing ? 1f : 0f;
        float step = s.LegsSeconds > 0f ? seconds / s.LegsSeconds : 1f;
        a.ActionLegs = a.ActionLegs < legs ? MathF.Min(legs, a.ActionLegs + step) : MathF.Max(legs, a.ActionLegs - step);
    }

    /// <summary>Whether the channel's action crossed <paramref name="seconds"/> of its clip on the last advance (a blow's <c>Strike</c> mark).</summary>
    public static bool Crossed(in CharacterAnimation a, float seconds) =>
        a.Action != ActionRole.None && seconds >= 0f && a.ActionLast < seconds && a.ActionTime >= seconds;

    /// <summary>Whether an action that is not over yet plays (it has not begun to fade out).</summary>
    public static bool Playing(in CharacterAnimation a) => a.Action != ActionRole.None && !a.ActionEnding;
}
