// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the blow comes from the side the player chooses ("what side of the enemy
// my cursor is on will dictate where the weapon swings from"), Mount & Blade's three ways of choosing it, and Bannerlord's
// press-to-ready, release-to-strike.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Control;

/// <summary>
/// Where a blow comes from: from the attacker's <b>left</b> (the blade starts on his left and sweeps
/// right: Christof's backhand), from his <b>right</b> (the forehand), <b>overhead</b> (an overhand swing down) or a <b>thrust</b> (a
/// stab). <see cref="None"/>: not chosen (1.1's alternation, or the mode's choice still to come).
/// </summary>
public enum AttackDirection
{
    None = 0,
    Left,
    Right,
    Overhead,
    Thrust,
}

/// <summary>How the player's blow's direction is chosen ("all of the above as options, like in the M&amp; B series").</summary>
public enum AttackDirectionMode
{
    /// <summary>
    /// By where the crosshair is on the foe in front (the default: the side of the enemy the cursor is on
    /// decides where the weapon swings from): left or right of his centre line, above his shoulders, below his waist.
    /// </summary>
    Automatic = 0,
    /// <summary>By the mouse's movement just before and at the press (Bannerlord's): left, right, up (overhead), down (thrust).</summary>
    Mouse,
    /// <summary>By the movement key held at the press (Warband's "movement direction"): A left, D right, W overhead, S thrust; none, automatic.</summary>
    Keys,
}

/// <summary>Which input chose a blow's direction (for the log and the tests).</summary>
public enum DirectionSource
{
    /// <summary>The fighter's own intent (the duelling brain, a script).</summary>
    Intent = 0,
    /// <summary>Automatic, the crosshair (or the body's look) on a foe in front.</summary>
    Foe,
    /// <summary>Automatic with no foe in front: the view's yaw and pitch against the body's facing.</summary>
    NoFoe,
    /// <summary>The mouse's movement.</summary>
    Mouse,
    /// <summary>The movement keys.</summary>
    Keys,
}

/// <summary>
/// A direction chosen: the direction, the side it leans to (<see cref="AttackDirection.Left"/> or
/// <see cref="AttackDirection.Right"/>: where a direction the body has no blow for falls back to, <see cref="DirectionalAttacks.Resolve"/>;
/// <see cref="AttackDirection.None"/> when the input gave no side), and what chose it.
/// </summary>
public readonly record struct DirectionChoice(AttackDirection Direction, AttackDirection Lean, DirectionSource Source);

/// <summary>
/// The player's way of choosing a blow's direction (the setting <c>attackDirection</c>, <c>--attack-direction</c>): a
/// service the program puts in the world, carried in a recording's head (<see cref="TapeNote"/>). Absent: automatic.
/// </summary>
public sealed class AttackDirectionInput
{
    public AttackDirectionMode Mode { get; set; } = AttackDirectionMode.Automatic;

    /// <summary>The tape head's note (<c>automatic</c>, <c>mouse</c>, <c>keys</c>).</summary>
    public const string TapeNote = "attackDirection";

    /// <summary>The setting's and the note's names, in the modes' order.</summary>
    public static readonly IReadOnlyList<string> Names = ["automatic", "mouse", "keys"];

    public string Note => Names[(int)Mode];

    /// <summary>A mode by its name (any case); false for another word.</summary>
    public static bool TryParse(string? text, out AttackDirectionMode mode)
    {
        mode = AttackDirectionMode.Automatic;
        if (text is null) return false;
        for (int i = 0; i < Names.Count; i++)
            if (string.Equals(text.Trim(), Names[i], StringComparison.OrdinalIgnoreCase)) { mode = (AttackDirectionMode)i; return true; }
        return false;
    }

    /// <summary>The mode a tape's head records (automatic without the note).</summary>
    public static AttackDirectionInput FromNote(string? note) => new() { Mode = TryParse(note, out var m) ? m : AttackDirectionMode.Automatic };

    /// <summary>
    /// The mode a setting (<paramref name="setting"/>, the Controls page's <c>attackDirection</c>) asks for when it is
    /// not <paramref name="now"/> (the session's: the run's start's, <c>--attack-direction</c>'s included, or the last change); null
    /// when they agree or the setting names no mode. The program puts it on the next live tick's frame (<see cref="ActionFrame.AttackMode"/>).
    /// </summary>
    public static AttackDirectionMode? ChangeTo(AttackDirectionMode now, string? setting) =>
        TryParse(setting, out var asked) && asked != now ? asked : null;
}

/// <summary>How a held blow's ready point is found (<see cref="DirectionSettings.Ready"/>).</summary>
public enum ReadyRule
{
    /// <summary>Just before the clip's active window (contact's, <see cref="Combat.ActiveWindows"/>): its start less <see cref="DirectionSettings.ReadyBeforeWindowSeconds"/>.</summary>
    Window = 0,
    /// <summary>The wind-up's turn: the moment the striking point moves slowest in the last part of the wind-up before the window (the blade cocked).</summary>
    Turn,
}

/// <summary>
/// The choosing's and the holding's numbers (ENGINE_SANDBOX, ours: <c>Data/combat/directions.json</c>'s <c>choosing</c> and
/// <c>ready</c> fill them; these are the platform's defaults, the same numbers).
/// </summary>
public sealed record DirectionSettings
{
    // ---- automatic ----
    /// <summary>A foe counts for automatic within this distance, feet to feet across the ground (m): a sword's reach and a step.</summary>
    public float FoeReachMetres { get; init; } = 3f;
    /// <summary>... and within this angle either side of the body's facing (degrees).</summary>
    public float FoeConeDegrees { get; init; } = 60f;
    /// <summary>The crosshair's line passing the foe's centre line above this share of his height (his capsule's: crouched, lower) is overhead (0.80: his shoulders, 1.44 m on a 1.8 m man).</summary>
    public float OverheadAboveShare { get; init; } = 0.80f;
    /// <summary>... below this share is a thrust (0.52: his waist, 0.94 m on a 1.8 m man).</summary>
    public float ThrustBelowShare { get; init; } = 0.52f;
    /// <summary>With no foe in front: the view pitched up at least this (degrees) is overhead.</summary>
    public float NoFoeOverheadDegrees { get; init; } = 20f;
    /// <summary>... pitched down at least this far (degrees, negative) is a thrust.</summary>
    public float NoFoeThrustDegrees { get; init; } = -25f;
    /// <summary>... else the view turned from the body's facing at least this far (degrees) chooses that side.</summary>
    public float NoFoeSideDegrees { get; init; } = 10f;
    /// <summary>The side when nothing gives one (no foe, the view on the facing): the forehand, 1.1's first quick blow.</summary>
    public AttackDirection DefaultSide { get; init; } = AttackDirection.Right;
    /// <summary>A body that is not the player looks from this share of its height (its eyes) along its facing and its aim's pitch.</summary>
    public float EyeShare { get; init; } = 0.92f;

    // ---- mouse ----
    /// <summary>The mouse's movement is summed over about this long before the press (s; a leaky sum, its time constant).</summary>
    public float MouseWindowSeconds { get; init; } = 0.15f;
    /// <summary>Less movement than this (degrees of view turned, summed) is no flick: automatic chooses.</summary>
    public float MouseDeadZoneDegrees { get; init; } = 1f;
    /// <summary>The up-down movement counted this many times against the side-to-side (Bannerlord's "vertical movement scale").</summary>
    public float MouseVerticalScale { get; init; } = 1f;
    /// <summary>Bannerlord's "inverted attack direction": the mouse moved left strikes from the right.</summary>
    public bool MouseInvert { get; init; }

    // ---- keys ----
    /// <summary>A movement key counts held at this much of the move (the keys give 1; a stick's half-way).</summary>
    public float KeysThreshold { get; init; } = 0.5f;

    // ---- ready and release ----
    /// <summary>How the ready point is found.</summary>
    public ReadyRule Ready { get; init; } = ReadyRule.Turn;
    /// <summary>The ready point this many clip seconds before the active window's start (<see cref="ReadyRule.Window"/>, and the latest the turn may be).</summary>
    public float ReadyBeforeWindowSeconds { get; init; } = 0.02f;
    /// <summary>The turn is looked for over this share of the wind-up before the window (its last part).</summary>
    public float TurnSearchShare { get; init; } = 0.5f;
    /// <summary>The ready point this many clip seconds before the <c>Strike</c> mark when the clip has no active window.</summary>
    public float ReadyBeforeStrikeSeconds { get; init; } = 0.2f;
    /// <summary>The longest a blow is held ready (s) before it strikes by itself; 0: no limit ("holding waits").</summary>
    public float HoldMostSeconds { get; init; }

    // ---- switching a held blow's direction ----
    /// <summary>
    /// Whether a blow held ready (or winding up to it) takes another direction when the player's mode chooses one while attack is
    /// held (<see cref="DirectionalAttacks.ChooseHeld"/>; Mount &amp; Blade's "hold attack, and then switch movements
    /// to change the attack direction").
    /// </summary>
    public bool SwitchWhileHeld { get; init; } = true;
    /// <summary>The cross-fade from the held blow into the new one (s; ENGINE_SANDBOX, ours): a little longer than the channel's 0.1 s, so the blade is seen to swing over.</summary>
    public float SwitchFadeSeconds { get; init; } = 0.25f;
}

/// <summary>
/// One row of the direction → clip table (<c>Data/combat/directions.json</c>'s <c>blows</c>): which kinds of character and
/// which carry class it applies to (null: any), and each direction's blow — <c>quick:N</c> (the kind's own Nth quick blow, 1.1's
/// mode's entries in order), <c>strong</c> (its strong blow), a clip's name (one of the kind's own blows, or a clip a
/// <see cref="DirectionalAttacks.Resolvers"/> resolver knows: a stitched one), <c>lend:&lt;clip&gt;</c> (another kind's blow on the same
/// skeleton, at this kind's weapon's times), several of these between <c>|</c> (the first that resolves), empty to take the
/// direction away; null to leave it as earlier rows gave it. Rows apply in order, a later one over an earlier.
/// </summary>
public sealed record DirectionRow(string? Kind, int? Carry, string? Left, string? Right, string? Overhead, string? Thrust)
{
    public string? Of(AttackDirection d) => d switch
    {
        AttackDirection.Left => Left,
        AttackDirection.Right => Right,
        AttackDirection.Overhead => Overhead,
        AttackDirection.Thrust => Thrust,
        _ => null,
    };
}

/// <summary>
/// The blow that a release of attack would strike now (<see cref="Attacks.NextBlow"/>; the swing's arc shows it): its direction
/// (<see cref="AttackDirection.None"/> with directions off: 1.1's quick variant or strong blow), its role and motion, its strength,
/// whether it is the blow <b>held ready</b> on the channel now (then it is the current blow, its clip held at
/// <see cref="ReadyAt"/>: a copy stepped forward must be let go to strike, <c>ActionHolds = false</c>), and what chose it.
/// </summary>
public readonly record struct NextBlowInfo(AttackDirection Direction, ActionRole Role, ActionMotion Motion, AttackStrength Strength, bool Held, float ReadyAt, DirectionSource Source);

/// <summary>
/// Directional attacks (a world service the enhanced module registers from <c>Data/combat/directions.json</c>): whether they
/// are on (<see cref="Enabled"/>; off — the switch <c>attack-spin</c>, or a world without the service — is 1.1's tap-and-hold
/// exactly: quick blows in turn, the whirl when held), the table's rows, the numbers, and each kind's blows by direction worked
/// out at first need (a memo: the same answer every time; not digested). Nothing allocated per tick once a kind is known.
/// </summary>
public sealed class DirectionalAttacks
{
    /// <summary>On: the blow's direction chosen at the press, held ready while attack is held, struck on release; no spin.</summary>
    public bool Enabled { get; set; } = true;

    public DirectionSettings Settings { get; set; } = new();

    /// <summary>The platform's own table: every kind's two quick blows, the second from the left, the first from the right (1.1's forehand first).</summary>
    public static readonly IReadOnlyList<DirectionRow> DefaultRows = [new DirectionRow(null, null, "quick:1", "quick:0", null, null)];

    public IReadOnlyList<DirectionRow> Rows { get; set; } = DefaultRows;

    /// <summary>
    /// Resolvers for a clip name the kind's own blows do not have (the stitched overhead and thrust): given the assets, the
    /// kind and the name, the motion (its <see cref="ActionMotion.HoldSeconds"/> the ready point when it knows one, else −1), or
    /// <see cref="ActionMotion.None"/>. Asked in order.
    /// </summary>
    public List<Func<CharacterAssets, int, string, ActionMotion>> Resolvers { get; } = [];

    /// <summary>What the table could not find (said once per kind): <c>"Christof: overhead 'x' not found (falls back to the side)"</c>.</summary>
    public List<string> Problems { get; } = [];

    ActionMotion[][] byKind = [];

    /// <summary>The motion for <paramref name="direction"/> on <paramref name="kind"/>, with its ready point in <see cref="ActionMotion.HoldSeconds"/>; none when the kind has no blow for it.</summary>
    public ActionMotion Motion(World world, CharacterAssets assets, int kind, AttackDirection direction)
    {
        if (direction == AttackDirection.None || kind < 0) return ActionMotion.None;
        var all = Of(world, assets, kind);
        return all[(int)direction];
    }

    /// <summary>
    /// The direction a choice plays on <paramref name="kind"/> (its blow): the one chosen when the kind has a blow for it; else its
    /// lean's side (an overhead or a thrust the kind lacks falls back to the side of the centre line the crosshair, the flick or the
    /// keys leaned to, <paramref name="leanIfNone"/> when they gave none); else the other side; else none.
    /// </summary>
    public AttackDirection Resolve(World world, CharacterAssets assets, int kind, in DirectionChoice choice, AttackDirection leanIfNone, out ActionMotion motion)
    {
        var all = Of(world, assets, kind);
        motion = ActionMotion.None;
        var d = choice.Direction;
        if (d != AttackDirection.None && all[(int)d].Has) { motion = all[(int)d]; return d; }
        var lean = choice.Lean is AttackDirection.Left or AttackDirection.Right ? choice.Lean
                 : leanIfNone is AttackDirection.Left or AttackDirection.Right ? leanIfNone : Settings.DefaultSide;
        if (all[(int)lean].Has) { motion = all[(int)lean]; return lean; }
        var other = lean == AttackDirection.Left ? AttackDirection.Right : AttackDirection.Left;
        if (all[(int)other].Has) { motion = all[(int)other]; return other; }
        for (int i = (int)AttackDirection.Overhead; i <= (int)AttackDirection.Thrust; i++)
            if (all[i].Has) { motion = all[i]; return (AttackDirection)i; }
        return AttackDirection.None;
    }

    /// <summary>
    /// <see cref="Resolve"/> for <paramref name="e"/>'s choice: when the direction chosen has no blow on its kind and the choice gave no
    /// side, the side is automatic's (the crosshair's or the look's side of the foe's centre line), worked out only then.
    /// </summary>
    public AttackDirection ResolveFor(World world, CharacterAssets assets, EntityId e, int kind, in DirectionChoice choice, in CombatIntent intent, out ActionMotion motion)
    {
        var lean = AttackDirection.None;
        if (choice.Lean == AttackDirection.None && (choice.Direction == AttackDirection.None || !Has(world, assets, kind, choice.Direction)))
            lean = Automatic(world, e, world.TryStore<Controlled>(out var players) && players.Has(e), intent).Lean;
        return Resolve(world, assets, kind, choice, lean, out motion);
    }

    /// <summary>Whether <paramref name="kind"/> has its own blow for <paramref name="direction"/>.</summary>
    public bool Has(World world, CharacterAssets assets, int kind, AttackDirection direction) => Motion(world, assets, kind, direction).Has;

    ActionMotion[] Of(World world, CharacterAssets assets, int kind)
    {
        if (kind >= byKind.Length) Array.Resize(ref byKind, Math.Max(kind + 1, Math.Max(8, byKind.Length * 2)));
        return byKind[kind] ??= Build(world, assets, kind);
    }

    ActionMotion[] Build(World world, CharacterAssets assets, int kind)
    {
        var all = new ActionMotion[5];
        Array.Fill(all, ActionMotion.None);
        var k = assets.Kind(kind);
        if (k.Actions is not { HasAttack: true } set) return all;
        var refs = new string?[5];
        foreach (var row in Rows)
        {
            if (row.Kind is { } name && !string.Equals(name, k.Name, StringComparison.OrdinalIgnoreCase)) continue;
            if (row.Carry is { } carry && carry != set.Carry) continue;
            for (int d = 1; d <= 4; d++) if (row.Of((AttackDirection)d) is { } r) refs[d] = r;
        }
        for (int d = 1; d <= 4; d++)
        {
            if (string.IsNullOrWhiteSpace(refs[d])) continue;
            var m = Find(assets, kind, set, refs[d]!);
            if (!m.Has)
            {
                lock (Problems) Problems.Add($"{k.Name}: {Name((AttackDirection)d)} '{refs[d]}' not found (falls back to the side)");
                continue;
            }
            all[d] = m with { HoldSeconds = ReadyPoint(world, assets, kind, m) };
        }
        return all;
    }

    ActionMotion Find(CharacterAssets assets, int kind, ActionSet set, string reference)
    {
        // Alternatives, the first that resolves: "quick:1|lend:chrisAttack2Sword".
        if (reference.Contains('|'))
        {
            foreach (var alt in reference.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Find(assets, kind, set, alt) is { Has: true } found) return found;
            return ActionMotion.None;
        }
        var r = reference.Trim();
        if (r.StartsWith("lend:", StringComparison.OrdinalIgnoreCase)) return Lend(assets, kind, set, r[5..].Trim());
        if (r.StartsWith("quick:", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(r.AsSpan(6), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n)
                   && (uint)n < (uint)set.Quick.Length ? set.Quick[n] with { HoldSeconds = -1f } : ActionMotion.None;
        if (string.Equals(r, "strong", StringComparison.OrdinalIgnoreCase)) return set.Strong.Has ? set.Strong with { HoldSeconds = -1f } : ActionMotion.None;
        foreach (var q in set.Quick)
            if (q.Has && string.Equals(assets.Clip(q.Clip).Name, r, StringComparison.OrdinalIgnoreCase)) return q with { HoldSeconds = -1f };
        if (set.Strong.Has && string.Equals(assets.Clip(set.Strong.Clip).Name, r, StringComparison.OrdinalIgnoreCase)) return set.Strong with { HoldSeconds = -1f };
        // The stitched blows by their registered name (StitchedAttacks.ClipName: stitched:overhead,
        // stitched:thrust), from the kind's own action set, held ready at their own ready point.
        if (r.StartsWith("stitched:", StringComparison.OrdinalIgnoreCase))
        {
            var direction = r["stitched:".Length..].Trim();
            var stitched = set.Stitched(direction);
            return stitched.Has ? stitched with { HoldSeconds = set.StitchedReady(direction) } : ActionMotion.None;
        }
        foreach (var resolve in Resolvers)
            if (resolve(assets, kind, r) is { Has: true } m) return m;
        return ActionMotion.None;
    }

    /// <summary>
    /// <c>lend:&lt;clip&gt;</c>: another kind's blow of that name lent to <paramref name="kind"/> when both stand on the same skeleton
    /// (the same bones by name and parent, in order: 1.1's people on skeleton 0 share Christof's clips, as before lent his dodge), played
    /// at the borrower's own weapon's times — its whole clip in the time its first quick blow takes (its <c>STRIKETIME</c>), busy its
    /// <c>RELOADTIME</c> — its <c>Strike</c> mark the lender's. The first kind found, in the assets' order; none when no kind has it.
    /// </summary>
    static ActionMotion Lend(CharacterAssets assets, int kind, ActionSet set, string clip)
    {
        if (set.Quick.Length == 0 || !set.Quick[0].Has || set.Quick[0].Rate <= 0f) return ActionMotion.None;
        var mine = assets.ModelOf(kind).Skeleton;
        float strikeTime = assets.Clip(set.Quick[0].Clip).Duration / set.Quick[0].Rate;
        for (int k = 0; k < assets.KindCount; k++)
        {
            if (k == kind || assets.Kind(k).Actions is not { } other) continue;
            ActionMotion lent = ActionMotion.None;
            foreach (var q in other.Quick) if (q.Has && string.Equals(assets.Clip(q.Clip).Name, clip, StringComparison.OrdinalIgnoreCase)) { lent = q; break; }
            if (!lent.Has || !SameSkeleton(mine, assets.ModelOf(k).Skeleton)) continue;
            return lent with { Rate = assets.Clip(lent.Clip).Duration / MathF.Max(strikeTime, 1e-3f), BusySeconds = set.Quick[0].BusySeconds, HoldSeconds = -1f };
        }
        return ActionMotion.None;
    }

    static bool SameSkeleton(Skeleton a, Skeleton b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a.Bones[i].Parent != b.Bones[i].Parent || !string.Equals(a.Bones[i].Name, b.Bones[i].Name, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>
    /// A blow's ready point (clip seconds; the class's remarks): the motion's own when it gives one; else, with the clip's active
    /// window (contact's), by <see cref="DirectionSettings.Ready"/> — <see cref="ReadyRule.Window"/> its start less a little, or
    /// <see cref="ReadyRule.Turn"/> the slowest moment of the striking point over the last part of the wind-up (the blade cocked) —
    /// and with no window, the <c>Strike</c> mark less <see cref="DirectionSettings.ReadyBeforeStrikeSeconds"/>; never below 0.
    /// </summary>
    public float ReadyPoint(World world, CharacterAssets assets, int kind, in ActionMotion m)
    {
        if (m.HoldSeconds >= 0f) return m.HoldSeconds;
        var s = Settings;
        float from = -1f;
        if (world.TryService<Combat.ActiveWindows>(out var windows) && world.TryService<Combat.ContactConfig>(out var config))
        {
            var w = windows.For(assets, kind, m.Clip, m.StrikeSeconds, config.Window);
            if (w.Has) from = w.From;
        }
        if (from < 0f) return MathF.Max(0f, m.StrikeSeconds - s.ReadyBeforeStrikeSeconds);
        float latest = MathF.Max(0f, from - s.ReadyBeforeWindowSeconds);
        if (s.Ready == ReadyRule.Window) return latest;
        return Turn(assets, kind, m.Clip, latest * (1f - Math.Clamp(s.TurnSearchShare, 0f, 1f)), latest);
    }

    /// <summary>The clip time in [<paramref name="from"/>, <paramref name="to"/>] where the striking point (the first blade's tip, else the weapon hand) moves slowest, sampled at 240 a second on the kind's skeleton alone.</summary>
    public static float Turn(CharacterAssets assets, int kind, int clip, float from, float to)
    {
        var c = assets.Kind(kind);
        var skeleton = assets.ModelOf(kind).Skeleton;
        int bone = -1;
        Vector3 local = Vector3.Zero;
        foreach (var held in c.Held)
        {
            foreach (var t in held.Things)
                if (t.Kind == HeldKind.Blade) { bone = t.Bone; local = t.Tip; break; }
            if (bone >= 0) break;
        }
        if (bone < 0 && c.Actions is { WeaponHand: >= 0 } set) bone = set.WeaponHand;
        if ((uint)bone >= (uint)skeleton.Count || to <= from) return to;
        var motion = assets.Clip(clip);
        const float step = 1f / 240f;
        var pose = new BoneTransform[skeleton.Count];
        var model = new Matrix4x4[skeleton.Count];
        Vector3 At(float t)
        {
            PoseEvaluator.Rest(skeleton, pose);
            motion.Sample(Math.Clamp(t, 0f, motion.Duration), skeleton, pose);
            PoseEvaluator.ToModel(skeleton, pose, model);
            return PoseMath.Transform(local, model[bone]);
        }
        float best = to, slowest = float.MaxValue;
        var last = At(MathF.Max(0f, from - step));
        for (float t = from; t <= to + 1e-6f; t += step)
        {
            var p = At(t);
            float speed = Combat.Geometry.Length(p - last) / step;
            if (speed < slowest) { slowest = speed; best = t; }
            last = p;
        }
        return best;
    }

    /// <summary>A direction's name (<c>left</c>, <c>right</c>, <c>overhead</c>, <c>thrust</c>; empty for none).</summary>
    public static string Name(AttackDirection d) => d switch
    {
        AttackDirection.Left => "left",
        AttackDirection.Right => "right",
        AttackDirection.Overhead => "overhead",
        AttackDirection.Thrust => "thrust",
        _ => "",
    };

    /// <summary>Whether the world plays directional attacks (the service present and on).</summary>
    public static bool On(World world, out DirectionalAttacks dirs)
    {
        if (world.TryService<DirectionalAttacks>(out var d) && d.Enabled) { dirs = d; return true; }
        dirs = null!;
        return false;
    }

    // ---- choosing ----

    /// <summary>
    /// The direction <paramref name="e"/>'s blow would take if attack were pressed now: the fighter's own intent's when it
    /// gives one (the duelling brain, a script); else for the player by the mode (<see cref="AttackDirectionInput"/>; the mouse and
    /// the keys fall to automatic when they give nothing), for anyone else automatic. Reads the world; changes nothing.
    /// </summary>
    public DirectionChoice Choose(World world, EntityId e, in Combatant c, in CombatIntent intent)
    {
        if (intent.Direction != AttackDirection.None)
            return new DirectionChoice(intent.Direction, intent.Direction is AttackDirection.Left or AttackDirection.Right ? intent.Direction : AttackDirection.None, DirectionSource.Intent);
        bool player = world.TryStore<Controlled>(out var players) && players.Has(e);
        if (player)
        {
            var mode = world.TryService<AttackDirectionInput>(out var input) ? input.Mode : AttackDirectionMode.Automatic;
            if (mode == AttackDirectionMode.Mouse && Mouse(c.LookRecent, Settings) is { } m) return m;
            if (mode == AttackDirectionMode.Keys && world.TryService<TickInput>(out var tick) && Keys(tick.Current.Move, Settings) is { } k) return k;
        }
        return Automatic(world, e, player, intent);
    }

    /// <summary>
    /// The direction a blow held ready is switched to now, or none (it keeps its own): the fighter's own intent's
    /// when it gives one; the player's by the mode — <b>keys</b> the move key held now, <b>mouse</b> a flick now (the look's recent
    /// movement over the same window and dead zone as at the press), <b>automatic</b> where the crosshair sits on the foe now — and
    /// neither the keys nor the mouse fall to automatic: no key held and no flick keep the blow; anyone else's automatic. From the
    /// tick's state and input only.
    /// </summary>
    public DirectionChoice? ChooseHeld(World world, EntityId e, in Combatant c, in CombatIntent intent)
    {
        if (intent.Direction != AttackDirection.None)
            return new DirectionChoice(intent.Direction, intent.Direction is AttackDirection.Left or AttackDirection.Right ? intent.Direction : AttackDirection.None, DirectionSource.Intent);
        bool player = world.TryStore<Controlled>(out var players) && players.Has(e);
        if (player)
        {
            var mode = world.TryService<AttackDirectionInput>(out var input) ? input.Mode : AttackDirectionMode.Automatic;
            if (mode == AttackDirectionMode.Mouse) return Mouse(c.LookRecent, Settings);
            if (mode == AttackDirectionMode.Keys) return world.TryService<TickInput>(out var tick) ? Keys(tick.Current.Move, Settings) : null;
        }
        return Automatic(world, e, player, intent);
    }

    /// <summary>
    /// The mouse's choice: the look's recent movement (<see cref="Combatant.LookRecent"/>: radians, x the yaw to the left, y the
    /// pitch up), its up-down part scaled; under the dead zone, none (automatic chooses). The larger part decides: to the left
    /// (the view turned left) from the left, to the right from the right, up overhead, down a thrust (inverted with
    /// <see cref="DirectionSettings.MouseInvert"/>). Its lean is the side part's sign.
    /// </summary>
    public static DirectionChoice? Mouse(Vector2 look, DirectionSettings s)
    {
        float x = look.X, y = look.Y * s.MouseVerticalScale;
        if (s.MouseInvert) { x = -x; y = -y; }
        float dead = s.MouseDeadZoneDegrees * DetMath.DegToRad;
        if (x * x + y * y < dead * dead) return null;
        var lean = x > 0f ? AttackDirection.Left : x < 0f ? AttackDirection.Right : AttackDirection.None;
        if (MathF.Abs(x) >= MathF.Abs(y)) return new DirectionChoice(lean, lean, DirectionSource.Mouse);
        return new DirectionChoice(y > 0f ? AttackDirection.Overhead : AttackDirection.Thrust, MathF.Abs(x) > dead * 0.25f ? lean : AttackDirection.None, DirectionSource.Mouse);
    }

    /// <summary>The keys' choice: the move held (x right, y forward): A or D first (a side), else W overhead or S a thrust; none held, none (automatic chooses).</summary>
    public static DirectionChoice? Keys(Vector2 move, DirectionSettings s)
    {
        float t = s.KeysThreshold;
        if (move.X <= -t) return new DirectionChoice(AttackDirection.Left, AttackDirection.Left, DirectionSource.Keys);
        if (move.X >= t) return new DirectionChoice(AttackDirection.Right, AttackDirection.Right, DirectionSource.Keys);
        if (move.Y >= t) return new DirectionChoice(AttackDirection.Overhead, AttackDirection.None, DirectionSource.Keys);
        if (move.Y <= -t) return new DirectionChoice(AttackDirection.Thrust, AttackDirection.None, DirectionSource.Keys);
        return null;
    }

    /// <summary>
    /// Automatic: the crosshair's line — the player's view, from the camera along its forward; anyone else's
    /// look, from its eyes along its facing and its aim's pitch — against <b>the nearest person in front</b> (within
    /// <see cref="DirectionSettings.FoeReachMetres"/> feet to feet and <see cref="DirectionSettings.FoeConeDegrees"/> of the body's
    /// facing; alive). Where the line passes nearest his centre line (the vertical through his feet), seen from above: on the left of
    /// it from the left, on the right (or on it) from the right; and the line's height there — above
    /// <see cref="DirectionSettings.OverheadAboveShare"/> of his height overhead, below <see cref="DirectionSettings.ThrustBelowShare"/> a
    /// thrust. With no one in front: the view pitched up past <see cref="DirectionSettings.NoFoeOverheadDegrees"/> overhead, down past
    /// <see cref="DirectionSettings.NoFoeThrustDegrees"/> a thrust, else the view turned from the facing past
    /// <see cref="DirectionSettings.NoFoeSideDegrees"/> that side, else <see cref="DirectionSettings.DefaultSide"/>.
    /// </summary>
    public DirectionChoice Automatic(World world, EntityId e, bool player, in CombatIntent intent) =>
        Automatic(world, e, player, intent, out _, out _, out _);

    /// <summary><see cref="Automatic(World, EntityId, bool, in CombatIntent)"/>, with the foe found, the line's place across his centre line (m, right positive) and its height there as a share of his.</summary>
    public DirectionChoice Automatic(World world, EntityId e, bool player, in CombatIntent intent, out EntityId foe, out float across, out float heightShare)
    {
        var s = Settings;
        foe = EntityId.None;
        across = 0f;
        heightShare = 0f;
        var bodies = world.Store<Body>();
        ref var body = ref bodies.Find(e);
        if (Unsafe.IsNullRef(ref body)) return new DirectionChoice(s.DefaultSide, s.DefaultSide, DirectionSource.NoFoe);
        var locos = world.TryStore<Locomotion>(out var ls) ? ls : null;
        Vector3 origin;
        float yaw, pitch;
        if (player && world.TryService<ViewState>(out var view) && view.HasView && view.Of == e)
        {
            origin = view.View.Eye;
            (yaw, pitch) = (view.View.Yaw, view.View.Pitch);
        }
        else
        {
            float height = locos is not null && locos.Has(e) ? body.CapsuleHeight(locos.Get(e)) : 1.8f;
            origin = body.Feet + new Vector3(0f, 0f, height * s.EyeShare);
            (yaw, pitch) = (body.Yaw, intent.Aims ? intent.AimPitch : 0f);
        }
        var dir = DetMath.Direction(yaw, pitch);

        // The nearest person in front.
        var anims = world.TryStore<CharacterAnimation>(out var an) ? an : null;
        var dead = world.TryStore<Combat.Dead>(out var dd) ? dd : null;
        float best = s.FoeReachMetres, cone = s.FoeConeDegrees * DetMath.DegToRad;
        var data = bodies.Data;
        var ents = bodies.Entities;
        int found = -1;
        for (int i = 0; i < data.Length; i++)
        {
            var o = ents[i];
            if (o == e || anims is null) continue;
            ref var oa = ref anims.Find(o);
            if (Unsafe.IsNullRef(ref oa) || ActionRoles.IsDeath(oa.Action) || (dead is not null && dead.Has(o))) continue;
            float dx = data[i].Feet.X - body.Feet.X, dy = data[i].Feet.Y - body.Feet.Y;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            if (d > best || d < 1e-4f) continue;
            if (MathF.Abs(DetMath.WrapAngle(DetMath.Atan2(dy, dx) - body.Yaw)) > cone) continue;
            best = d;
            found = i;
        }
        float flat = MathF.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        if (found >= 0 && flat > 1e-3f)
        {
            var f = data[found].Feet;
            var dh = new Vector2(dir.X / flat, dir.Y / flat);
            var rel = new Vector2(f.X - origin.X, f.Y - origin.Y);
            float along = rel.X * dh.X + rel.Y * dh.Y;
            if (along > 1e-3f)
            {
                foe = ents[found];
                var right = new Vector2(dh.Y, -dh.X);
                across = -(rel.X * right.X + rel.Y * right.Y);   // the line's place from his centre line, to the right positive
                float z = origin.Z + dir.Z * (along / flat);
                float height = locos is not null && locos.Has(foe) ? data[found].CapsuleHeight(locos.Get(foe)) : 1.8f;
                heightShare = (z - f.Z) / MathF.Max(0.1f, height);
                var lean = across < 0f ? AttackDirection.Left : AttackDirection.Right;
                var d = heightShare >= s.OverheadAboveShare ? AttackDirection.Overhead : heightShare <= s.ThrustBelowShare ? AttackDirection.Thrust : lean;
                return new DirectionChoice(d, lean, DirectionSource.Foe);
            }
        }
        // No one in front: the view against the body.
        float turned = DetMath.WrapAngle(yaw - body.Yaw);
        float sideAt = s.NoFoeSideDegrees * DetMath.DegToRad;
        var side = turned >= sideAt ? AttackDirection.Left : turned <= -sideAt ? AttackDirection.Right : s.DefaultSide;
        if (pitch >= s.NoFoeOverheadDegrees * DetMath.DegToRad) return new DirectionChoice(AttackDirection.Overhead, side, DirectionSource.NoFoe);
        if (pitch <= s.NoFoeThrustDegrees * DetMath.DegToRad) return new DirectionChoice(AttackDirection.Thrust, side, DirectionSource.NoFoe);
        return new DirectionChoice(side, side, DirectionSource.NoFoe);
    }

    /// <summary>The mouse's recent movement kept a tick: <c>sum ← sum × (1 − dt/τ) + look</c>, τ <see cref="DirectionSettings.MouseWindowSeconds"/>.</summary>
    public static Vector2 KeepLook(Vector2 sum, Vector2 look, DirectionSettings s)
    {
        float keep = s.MouseWindowSeconds > World.TickSeconds ? 1f - World.TickSeconds / s.MouseWindowSeconds : 0f;
        return sum * keep + look;
    }
}
