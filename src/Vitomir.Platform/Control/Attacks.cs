// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Control;

/// <summary>A blow's strength: attack tapped a quick blow, held past the threshold a strong one.</summary>
public enum AttackStrength
{
    None = 0,
    Quick,
    Strong,
}

/// <summary>Why an attack was not made.</summary>
public enum AttackRefusal
{
    /// <summary>The masquerade mode is on: its safety catch stops hostile acts.</summary>
    Catch,
    /// <summary>A blow is already swinging and a press is already waiting for it to end (one press is buffered, no more).</summary>
    MidSwing,
    /// <summary>The block is held.</summary>
    Blocking,
    /// <summary>The body is in a move (a vault, a climb, a hang).</summary>
    Moving,
    /// <summary>The character has no attack in its motion set.</summary>
    NoAttack,
    /// <summary>The character is dead.</summary>
    Dead,
    /// <summary>The character has lost its weapon hand (a part severed: <see cref="Combat.MissingParts"/>), and with it the weapon.</summary>
    NoWeaponHand,
    /// <summary>The character is stunned a moment after its blade was held or parried at a guard (<see cref="Combatant.Stun"/>): the press is buffered, its blow begins when the stun (and the busy time) is over.</summary>
    Stunned,
}

/// <summary>
/// What a character wants of its action channel this tick: attack and block held (from the player's buttons, or a
/// script), the pitch it aims a blow at when nothing else aims it (radians, up positive: a script's; the player's comes from
/// the view), and one action to play at once (a hurt, a death: contact asks for the hurt). Plain data.
/// </summary>
public struct CombatIntent
{
    int attack, block, aims;

    public bool Attack { readonly get => attack != 0; set => attack = value ? 1 : 0; }
    public bool Block { readonly get => block != 0; set => block = value ? 1 : 0; }

    /// <summary>Whether <see cref="AimPitch"/> is given (else a blow is level).</summary>
    public bool Aims { readonly get => aims != 0; set => aims = value ? 1 : 0; }
    public float AimPitch;

    /// <summary>An action to play at once (<see cref="ActionRole.Hurt"/>, a death), taken and cleared by the <see cref="AttackSystem"/>.</summary>
    public ActionRole Play;

    // ---- ----
    /// <summary>
    /// The direction this fighter chooses for its next blow itself (the duelling brain, a script), read at the press;
    /// <see cref="AttackDirection.None"/> lets the mode choose (the player's <see cref="AttackDirectionInput"/>, automatic for others).
    /// Read only with directional attacks on (<see cref="DirectionalAttacks"/>).
    /// </summary>
    public AttackDirection Direction;
}

/// <summary>
/// A character's fighting state (digested): how long attack has been held in this press (−1 when up, or when the press
/// was refused), the blow waiting for the swing to end (the buffer), how long it is still busy (1.1's
/// <c>RELOADTIME</c>), which quick variant comes next, the blows begun so far and the one swinging (its number and where
/// its <c>Strike</c> mark is), whether the block is raised and since when, and the blow's point last tick (for its speed).
/// </summary>
public struct Combatant
{
    public float AttackHeld;
    public AttackStrength Pending;
    public float Busy;
    public int NextVariant;
    public int Swings, Swing;
    public float StrikeAt;
    public float BlockSeconds;
    public Vector3 PointLast;
    int wasAttack, buffered, blocking, pointKnown;

    /// <summary>Whether attack was held on the last tick.</summary>
    public bool WasAttack { readonly get => wasAttack != 0; set => wasAttack = value ? 1 : 0; }
    /// <summary>Whether a press has been taken during this swing (the one the buffer holds).</summary>
    public bool Buffered { readonly get => buffered != 0; set => buffered = value ? 1 : 0; }
    public bool Blocking { readonly get => blocking != 0; set => blocking = value ? 1 : 0; }
    public bool PointKnown { readonly get => pointKnown != 0; set => pointKnown = value ? 1 : 0; }

    public static Combatant Ready => new() { AttackHeld = -1f, StrikeAt = -1f };

    // ---- the guard (Combat/Guard.cs) ----

    /// <summary>The parry window's seconds left (0: none), opened by the block's press (<see cref="Combat.GuardSettings.ParryWindowSeconds"/>).</summary>
    public float ParryLeft;

    /// <summary>The seconds before a press may open another parry window (<see cref="Combat.GuardSettings.ParryRepressSeconds"/>).</summary>
    public float ParryWait;

    /// <summary>The seconds before an overpowered guard may be raised again (the rules' <see cref="Combat.GuardResolved.BrokenSeconds"/>).</summary>
    public float GuardBroken;

    /// <summary>The guard's pitch now (radians, up positive): the aim offset it bends the raised guard to, moving toward a threat's line.</summary>
    public float GuardPitch;

    /// <summary>
    /// The guard's target when it comes from input rather than from the threat (radians: yaw from the facing, pitch), when
    /// <see cref="GuardManual"/> — room for a manual direction (Mount &amp; Blade's mouse-direction block), not used yet.
    /// </summary>
    public float GuardManualYaw, GuardManualPitch;

    int wasBlock, shieldGuard, guardManual;

    /// <summary>Whether block was held on the last tick (its press opens the parry window).</summary>
    public bool WasBlock { readonly get => wasBlock != 0; set => wasBlock = value ? 1 : 0; }

    /// <summary>Whether the raised block is kept on the shield arm's layer while a swing plays (<see cref="CharacterAnimation.GuardWeight"/>).</summary>
    public bool ShieldGuard { readonly get => shieldGuard != 0; set => shieldGuard = value ? 1 : 0; }

    /// <summary>Whether the guard's target is <see cref="GuardManualYaw"/> and <see cref="GuardManualPitch"/> (a later option; false: the threat's line).</summary>
    public bool GuardManual { readonly get => guardManual != 0; set => guardManual = value ? 1 : 0; }

    // ---- the attacker's stun (Combat/FollowThrough.cs, Stun) ----

    /// <summary>
    /// The seconds left of this fighter's stun after its blade was held or parried at a guard (Bannerlord's "attacker stun";
    /// <see cref="Combat.BladeStopRule.StunHeldSeconds"/>): no attack begins while it runs (a press is refused, <see cref="AttackRefusal.Stunned"/>,
    /// and buffered); a guard and a dodge are allowed. 0: none.
    /// </summary>
    public float Stun;

    /// <summary>The swing whose blade was stopped at a guard (its owner may raise the guard once its rebound is over, <see cref="Combat.BladeStopRule.GuardAfterRebound"/>); 0 none.</summary>
    public int StunSwing;

    // ---- the blow's direction, held ready (Control/AttackDirections.cs) ----

    /// <summary>The direction of the blow begun last (the one on the channel while it plays); none with directions off.</summary>
    public AttackDirection Direction;

    /// <summary>The direction chosen at a press taken during a swing (the buffer), begun when the swing's busy time is over.</summary>
    public AttackDirection PendingDirection;

    /// <summary>The clip seconds the blow held ready holds at (its ready point), while <see cref="Readied"/>.</summary>
    public float ReadyAt;

    /// <summary>The blow's whole busy time from its start (<see cref="Combat.CombatTempo.BusyFor"/>), so the release can count it from the clip's place: the hold is not busy time spent.</summary>
    public float ReadyBusy;

    /// <summary>The seconds the blow has been held at its ready point.</summary>
    public float ReadyHeld;

    /// <summary>The mouse's recent movement (radians: x the view's yaw to the left, y its pitch up), a leaky sum kept for the player while the mode is the mouse's (<see cref="DirectionalAttacks.KeepLook"/>).</summary>
    public System.Numerics.Vector2 LookRecent;

    int readied;

    /// <summary>Whether the blow on the channel is held ready (pressed, attack still held: Bannerlord's ready) — it strikes on release.</summary>
    public bool Readied { readonly get => readied != 0; set => readied = value ? 1 : 0; }
}

/// <summary>The fighting's numbers (SANDBOX).</summary>
public sealed record CombatSettings
{
    /// <summary>Attack held this long (s) makes a strong blow on release; let go sooner, a quick one (SANDBOX 0.3 s).</summary>
    public float StrongAfter { get; init; } = 0.3f;
}

/// <summary>An attack begun: who, quick or strong, the role and clip played (<see cref="CharacterAssets.Clip"/>), the aim pitch wanted (radians, clamped to the body's reach) and the blow's number.</summary>
public readonly record struct AttackBegun(EntityId Entity, long Tick, int Swing, AttackStrength Strength, ActionRole Role, int Clip, float AimPitch,
    AttackDirection Direction = AttackDirection.None);   // at the end: the blow's direction (none: 1.1's)

/// <summary>
/// A blow held ready let go (attack released): who, the blow's number, its direction and the seconds it was held at its ready
/// point (0: a tap, released before it got there, or a buffered blow begun after its press was let go). Its strike follows
/// (<see cref="StrikeMoment"/>). Every directional blow has one.
/// </summary>
public readonly record struct AttackReleased(EntityId Entity, long Tick, int Swing, AttackDirection Direction, float HeldSeconds);

/// <summary>
/// A blow held ready (or winding up to its ready point) switched to another direction while attack was held — who,
/// when, the swing (the same blow), from and to, and whether it had reached its ready point. Its release then strikes the new one
/// (<see cref="AttackReleased.Direction"/> names it).
/// </summary>
public readonly record struct AttackSwitched(EntityId Entity, long Tick, int Swing, AttackDirection From, AttackDirection To, bool AtReady);

/// <summary>A blow's <c>Strike</c> moment: who, the blow's number, the blow's point (the blade's tip, world m) and its speed there (m/s).</summary>
public readonly record struct StrikeMoment(EntityId Entity, long Tick, int Swing, ActionRole Role, Vector3 Tip, float TipSpeed);

/// <summary>The block raised.</summary>
public readonly record struct BlockRaised(EntityId Entity, long Tick);

/// <summary>The block lowered, after it was held so many seconds.</summary>
public readonly record struct BlockLowered(EntityId Entity, long Tick, float HeldSeconds);

/// <summary>An attack refused, and why; once a press.</summary>
public readonly record struct AttackRefused(EntityId Entity, long Tick, AttackRefusal Why);

/// <summary>
/// The attacks and the block (<see cref="Phase.Act"/>). For each
/// <see cref="Combatant"/> with an animation:
/// <list type="bullet">
/// <item><b>Attack tapped</b> is a quick blow, made as it is let go; <b>held</b> past <see cref="CombatSettings.StrongAfter"/>
/// it is a strong one, made as it is let go. A blow plays its role on the action channel (<see cref="ActionChannel"/>)
/// and keeps the character busy for its <c>RELOADTIME</c>; the quick blow's variants are taken in turn.</item>
/// <item><b>A press during a swing is buffered once</b>: it is timed as ever and its blow begins as the swing's busy time
/// ends; a second press in the same swing is refused (<see cref="AttackRefusal.MidSwing"/>).</item>
/// <item><b>The masquerade's catch</b>: while the player's body's masquerade mode is on, no attack is made (the catch is the
/// player's safety; what the townsfolk and guards do is their own) — the press is refused
/// once (<see cref="AttackRefusal.Catch"/>) and nothing comes of it. The catch lets go by itself in a free place (the mode's
/// own rule), and when someone strikes the player (contact, <see cref="MasqueradeCatch.LetGo"/>).</item>
/// <item><b>Block held</b> raises the block (when no swing is busy) and holds it at its raised frame; let go, it lowers. A
/// press of attack while blocking is refused.</item>
/// <item>A <see cref="CombatIntent.Play"/> (a hurt, a death) plays at once, lowering the block and dropping a buffered blow.</item>
/// <item><b>The aim</b>: each tick the body's <see cref="PoseRig.AimTarget"/> is set — for the player, the view's pitch (in
/// third person too, plus <see cref="ThirdPersonAimSettings.LevelOffsetDegrees"/>; the reticle's point from the chest,
/// <see cref="AimOffset.Toward"/>, with <see cref="ThirdPersonAimSettings.FromReticle"/>); for others the intent's.</item>
/// </list>
/// Everything is the tick's input and digested state, so a replay makes the same blows. No allocation per tick.
/// </summary>
public sealed class AttackSystem(CombatSettings? settings = null) : ISystem
{
    readonly CombatSettings settings = settings ?? new CombatSettings();
    int masqueradeKnown, movesKnown, rigsKnown, camerasKnown, playersKnown, temposKnown;

    public CombatSettings Settings => settings;

    public void Run(World world)
    {
        if (!world.TryService<CharacterAssets>(out var assets)) return;
        var fighters = world.Store<Combatant>();
        var intents = world.Store<CombatIntent>();
        var anims = world.Store<CharacterAnimation>();
        var bodies = world.Store<Body>();
        var masks = PoseRigSystem.Optional<MasqueradeState>(world, ref masqueradeKnown);
        var moves = PoseRigSystem.Optional<Traversal>(world, ref movesKnown);
        var rigs = PoseRigSystem.Optional<PoseRig>(world, ref rigsKnown);
        var cameras = PoseRigSystem.Optional<CameraRig>(world, ref camerasKnown);
        var players = PoseRigSystem.Optional<Controlled>(world, ref playersKnown);
        var tempos = PoseRigSystem.Optional<Combat.CombatTempo>(world, ref temposKnown);   // absent, 1.1's timing
        world.TryService<ViewState>(out var view);
        world.TryService<ReticleState>(out var reticle);
        var guard = world.TryService<Combat.GuardSettings>(out var gs) ? gs : DefaultGuard;
        bool on = guard.Enabled;
        var rigSettings = world.TryService<AimOffsetSettings>(out var aimSettings) ? aimSettings : DefaultAim;
        var thirdAim = world.TryService<ThirdPersonAimSettings>(out var tpa) ? tpa : DefaultThirdAim;
        // Directional attacks (null: off, 1.1's tap-and-hold), the player's mode and the tick's input (the mouse's look).
        var dirs = DirectionalAttacks.On(world, out var da) ? da : null;
        bool mouseMode = dirs is not null && world.TryService<AttackDirectionInput>(out var adi) && adi.Mode == AttackDirectionMode.Mouse;
        var tickInput = mouseMode && world.TryService<TickInput>(out var ti) ? ti : null;
        float dt = World.TickSeconds;
        long tick = world.Tick;
        var data = fighters.Data;
        var ents = fighters.Entities;
        for (int i = 0; i < data.Length; i++)
        {
            var e = ents[i];
            ref var c = ref data[i];
            ref var anim = ref anims.Find(e);
            if (Unsafe.IsNullRef(ref anim)) continue;
            ref var intentRef = ref intents.Find(e);
            var intent = Unsafe.IsNullRef(ref intentRef) ? default : intentRef;
            var set = assets.Kind(anim.Kind).Actions;
            var tempo = Combat.CombatTempos.Of(tempos, e);
            // A blow held ready that the channel no longer holds (a feint, a dodge, a hurt, a stop took it) is
            // no longer ready; while one is held at its ready point the busy time does not run (the hold is not the swing's time).
            if (dirs is not null && c.Readied && (!ActionRoles.IsAttack(anim.Action) || anim.ActionEnding || !anim.ActionHolds)) c.Readied = false;
            bool atReady = dirs is not null && c.Readied && ActionChannel.AtReady(anim);
            if (atReady) c.ReadyHeld += dt;
            else c.Busy = MathF.Max(0f, c.Busy - dt);
            if (c.Stun > 0f) c.Stun = MathF.Max(0f, c.Stun - dt);   // the attacker's stun runs down
            if (on)
            {
                // A guard lowered by someone else (a dodge) lets the shield arm's layer go too.
                if (c.ShieldGuard && !c.Blocking) { anim.GuardTarget = 0f; c.ShieldGuard = false; }
                // The parry's window and wait, and a broken guard's time, run down.
                c.ParryLeft = MathF.Max(0f, c.ParryLeft - dt);
                c.ParryWait = MathF.Max(0f, c.ParryWait - dt);
                c.GuardBroken = MathF.Max(0f, c.GuardBroken - dt);
            }

            // The aim wanted: the player's view, else the intent's. third person by the view's pitch too (plus the
            // level offset), so the swing's arc moves smoothly with the look; the old way, from the chest to the reticle's point,
            // behind ThirdPersonAimSettings.FromReticle.
            float aim = intent.Aims ? intent.AimPitch : 0f;
            if (cameras is not null && view is not null && view.Of == e)
            {
                ref var cam = ref cameras.Find(e);
                if (!Unsafe.IsNullRef(ref cam))
                {
                    aim = cam.Pitch;
                    ref var body = ref bodies.Find(e);
                    if (cam.View == ViewKind.Third && !thirdAim.FromReticle) aim = cam.Pitch + thirdAim.LevelOffsetDegrees * DetMath.DegToRad;
                    else if (cam.View == ViewKind.Third && reticle is { Answer.Kind: not Presentation.ReticleTarget.Nothing } r && !Unsafe.IsNullRef(ref body))
                        aim = AimOffset.Toward(body.Feet, r.Answer.Point, rigSettings);
                }
            }
            if (rigs is not null)
            {
                ref var rig = ref rigs.Find(e);
                if (Unsafe.IsNullRef(ref rig)) world.Set(e, new PoseRig { AimTarget = aim });
                else rig.AimTarget = aim;
            }

            bool dead = ActionRoles.IsDeath(anim.Action);
            // An action played at once: a hurt, a death.
            if (!Unsafe.IsNullRef(ref intentRef) && intentRef.Play != ActionRole.None)
            {
                var play = intentRef.Play;
                intentRef.Play = ActionRole.None;
                if (set is not null && set.For(play) is { Has: true } m && !dead)
                {
                    if (c.Blocking) Lower(world, e, ref c, ref anim, tick, set);
                    ActionChannel.Start(ref anim, m, play);
                    c.Pending = AttackStrength.None;
                    c.AttackHeld = -1f;
                    c.Readied = false;   // a blow held ready is taken by the hurt (always false with directions off)
                    atReady = false;
                    c.Busy = MathF.Max(c.Busy, m.BusySeconds > 0f ? m.BusySeconds : assets.Clip(m.Clip).Duration);
                    dead = ActionRoles.IsDeath(play);
                }
            }

            // Directional attacks — the direction chosen at the press, the blow held ready, struck on release.
            if (dirs is not null)
            {
                if (tickInput is not null && players is not null && players.Has(e))
                {
                    ref var camLook = ref cameras is null ? ref Unsafe.NullRef<CameraRig>() : ref cameras.Find(e);
                    bool free = !Unsafe.IsNullRef(ref camLook) && camLook.PointerFree;
                    c.LookRecent = DirectionalAttacks.KeepLook(c.LookRecent, free ? System.Numerics.Vector2.Zero : tickInput.Current.Look, dirs.Settings);
                }
                Directional(world, assets, dirs, e, ref c, ref anim, set, in intent, dead, atReady, masks, players, moves, aim, rigSettings, tick, tempo, on);
            }
            else
            {
            // Attack: tapped quick, held strong, a press in a swing buffered once; the catch refuses.
            bool down = intent.Attack, pressed = down && !c.WasAttack, released = !down && c.WasAttack;
            c.WasAttack = down;
            bool stunned = c.Stun > 0f;   // the attacker's stun holds the next blow as the busy time does
            bool busy = c.Busy > 0f || stunned;
            if (pressed)
            {
                AttackRefusal? why = null;
                if (dead) why = AttackRefusal.Dead;
                else if (set is null || !set.HasAttack) why = AttackRefusal.NoAttack;
                else if (HeldItems.WeaponHandGone(world, e)) why = AttackRefusal.NoWeaponHand;
                else if (masks is not null && players is not null && players.Has(e) && masks.Has(e) && masks.Get(e).Mode == MasqueradeMode.Masquerade) why = AttackRefusal.Catch;
                else if (c.Blocking && !on) why = AttackRefusal.Blocking;   // with the guard on, block and swing together
                else if (moves is not null && moves.Has(e) && moves.Get(e).Kind != TraversalKind.None) why = AttackRefusal.Moving;
                else if (busy && (c.Buffered || c.Pending != AttackStrength.None)) why = AttackRefusal.MidSwing;
                if (why is { } refused)
                {
                    c.AttackHeld = -1f;
                    world.Events<AttackRefused>().Publish(new AttackRefused(e, tick, refused));
                }
                else
                {
                    c.AttackHeld = 0f;
                    if (busy) c.Buffered = true;
                    // Pressed in the stun — said (refused for now) and buffered, as 1.1's buffer takes a press in a
                    // swing: its blow begins as the stun and the busy time are over, so pressing early is not punished.
                    if (stunned) world.Events<AttackRefused>().Publish(new AttackRefused(e, tick, AttackRefusal.Stunned));
                }
            }
            else if (down && c.AttackHeld >= 0f) c.AttackHeld += dt;
            if (released && c.AttackHeld >= 0f)
            {
                var strength = c.AttackHeld >= settings.StrongAfter ? AttackStrength.Strong : AttackStrength.Quick;
                c.AttackHeld = -1f;
                if (c.Busy > 0f || c.Stun > 0f) c.Pending = strength;   // or stunned
                else Begin(world, assets, e, ref c, ref anim, set!, strength, aim, rigSettings, tick, tempo, on);
            }
            else if (c.Busy <= 0f && c.Stun <= 0f && c.Pending != AttackStrength.None && !dead && set is { HasAttack: true })   // and not stunned
            {
                var strength = c.Pending;
                c.Pending = AttackStrength.None;
                Begin(world, assets, e, ref c, ref anim, set, strength, aim, rigSettings, tick, tempo, on);
            }
            }   // end of 1.1's tap-and-hold (directional attacks off)

            // Block: raised while held (once no swing is busy), lowered when let go. the press opens a parry
            // window; with a shield the block is kept (or raised) on the shield arm's layer while a swing plays, and put back on
            // the channel when the swing is over; an overpowered guard is not raised again until its broken time has run.
            bool blockPressed = intent.Block && !c.WasBlock;
            c.WasBlock = intent.Block;
            if (c.Blocking) c.BlockSeconds += dt;
            bool canRaise = !dead && set is { Block.Has: true } && (!on || c.GuardBroken <= 0f);
            // A blade stopped at a guard frees its owner's guard once its rebound is over (Bannerlord's stunned
            // attacker may block), though the busy time runs on and holds the next blow.
            // A shield holder's shield comes up on its arm's layer mid-swing anyway: this is a lone weapon's guard.
            bool freed = c.StunSwing != 0 && c.StunSwing == c.Swing && c.Busy > 0f && GuardFreed(world, e, c.Swing) && Combat.GuardLayer.ArmOf(assets, anim.Kind) is null;
            if (intent.Block && !c.Blocking && canRaise && (c.Busy <= 0f || freed) && c.Pending == AttackStrength.None)
            {
                ActionChannel.Start(ref anim, set!.Block, ActionRole.Block, hold: true, rateScale: tempo.RiseRate);
                c.Blocking = true;
                c.BlockSeconds = 0f;
                world.Events<BlockRaised>().Publish(new BlockRaised(e, tick));
                if (on && blockPressed && c.ParryWait <= 0f) OpenParry(world, assets, e, ref c, guard, rigSettings, aim, tick);
            }
            else if (on && intent.Block && !c.Blocking && canRaise && ActionRoles.IsAttack(anim.Action) && Combat.GuardLayer.ArmOf(assets, anim.Kind) is not null)
            {
                // A shield raised on its arm's layer while the weapon arm swings (no parry: the press came mid-swing).
                ShieldUp(ref c, ref anim, set!, 0f);
                c.Blocking = true;
                c.BlockSeconds = 0f;
                world.Events<BlockRaised>().Publish(new BlockRaised(e, tick));
            }
            else if (on && c.Blocking && c.ShieldGuard && intent.Block && !dead && c.Busy <= 0f && c.Pending == AttackStrength.None)
            {
                // The swing over with the shield kept up: the block back on the channel at its raised frame, the layer let go.
                ActionChannel.Start(ref anim, set!.Block, ActionRole.Block, hold: true, rateScale: tempo.RiseRate);
                anim.ActionTime = MathF.Max(0f, set.Block.HoldSeconds);
                anim.ActionLast = anim.ActionTime;
                anim.GuardTarget = 0f;
                c.ShieldGuard = false;
            }
            else if (c.Blocking && (!intent.Block || dead || (anim.Action != ActionRole.Block && !c.ShieldGuard) || (on && c.GuardBroken > 0f)))
                Lower(world, e, ref c, ref anim, tick, set);

            // The guard turns toward the nearest threat's line within its arc, faster in a parry window.
            if (on) Direct(world, assets, e, ref c, ref anim, rigs, guard, rigSettings, aim);
        }
    }

    static readonly Combat.GuardSettings DefaultGuard = new();
    readonly Combat.GuardAim guardAim = new();

    // ---- the guard freed after a blocked blow's rebound ----
    static readonly Combat.ContactConfig DefaultContact = new();

    /// <summary>Whether <paramref name="e"/>'s blocked <paramref name="swing"/> lets the guard up now: the rule allows it and its blade is no longer springing back.</summary>
    static bool GuardFreed(World world, EntityId e, int swing)
    {
        var rule = (world.TryService<Combat.ContactConfig>(out var cc) ? cc : DefaultContact).Stop;
        if (!rule.GuardAfterRebound || !world.TryStore<Combat.BladeHold>(out var holds)) return rule.GuardAfterRebound;
        ref var h = ref holds.Find(e);
        return Unsafe.IsNullRef(ref h) || h.Swing != swing || h.State != Combat.BladeHoldState.Rebound;
    }
    // ---- end

    /// <summary>
    /// The guard's direction (Mount &amp; Blade's automatic block): while the block is raised on the channel,
    /// the guard's target is the nearest threat's line within its arc (<see cref="Combat.GuardAim"/>; or, later, a manual
    /// direction) — its yaw the chest's turn (<see cref="CharacterAnimation.GuardTwist"/>, clamped to the twist's most), its pitch
    /// the aim offset's bend (clamped to the guard's reach) — and the guard moves toward it at the guard's speed, the parry's in
    /// a parry window; with no threat it rests at the facing and the look's pitch (the first block). Not raised (or swinging): the
    /// turn comes back to the facing at the guard's speed and the pitch follows the look.
    /// </summary>
    void Direct(World world, CharacterAssets assets, EntityId e, ref Combatant c, ref CharacterAnimation anim, Store<PoseRig>? rigs, Combat.GuardSettings g,
        AimOffsetSettings aimSettings, float aim)
    {
        bool guarding = c.Blocking && anim.Action == ActionRole.Block;
        float degrees = c.ParryLeft > 0f ? g.ParryDegreesPerSecond : g.GuardDegreesPerSecond;
        float step = degrees * DetMath.DegToRad * World.TickSeconds;
        // The stance's side and height move as their angles (GuardSideDegrees, the height's span) would at the same speed.
        float sideStep = degrees / MathF.Max(1f, g.GuardSideDegrees) * World.TickSeconds;
        float heightStep = degrees / MathF.Max(1f, g.GuardHeightSpanDegrees) * World.TickSeconds;
        if (!guarding)
        {
            if (anim.GuardTwist != 0f) anim.GuardTwist = Combat.GuardAim.Towards(anim.GuardTwist, 0f, step);
            if (anim.GuardSide != 0f) anim.GuardSide = Combat.GuardAim.Towards(anim.GuardSide, 0f, sideStep);
            if (anim.GuardHeight != 0f) anim.GuardHeight = Combat.GuardAim.Towards(anim.GuardHeight, 0f, heightStep);
            c.GuardPitch = aim;
            return;
        }
        float yaw = 0f, pitch = aim, sideWant = 0f, heightWant = 0f;
        if (c.GuardManual) (yaw, pitch) = (c.GuardManualYaw, c.GuardManualPitch);
        else if (guardAim.Threat(world, assets, e, g, aimSettings, out var ty, out var tp, out var side, out _))
        {
            float most = g.GuardMostTwistDegrees * DetMath.DegToRad;
            yaw = Math.Clamp(ty, -most, most);
            // A shield is the guard: it turns with the chest toward the foe and the rest is the block's own (measured: moving the
            // weapon hand toward the blow met it with the hand first, and bending to a high line raised the hand into it — 5 or 6
            // of 10 blows at the guard against 8 with the block held still).
            if (Combat.GuardLayer.ArmOf(assets, anim.Kind) is null)
            {
                pitch = Math.Clamp(tp, -g.GuardMostDownDegrees * DetMath.DegToRad, g.GuardMostUpDegrees * DetMath.DegToRad);
                sideWant = Math.Clamp(side * DetMath.RadToDeg / MathF.Max(1f, g.GuardSideDegrees), -1f, 1f);
                heightWant = Math.Clamp((tp * DetMath.RadToDeg - g.GuardLevelDegrees) / MathF.Max(1f, g.GuardHeightSpanDegrees), -1f, 1f);
            }
        }
        anim.GuardTwist = Combat.GuardAim.Towards(anim.GuardTwist, yaw, step);
        anim.GuardSide = Combat.GuardAim.Towards(anim.GuardSide, sideWant, sideStep);
        anim.GuardHeight = Combat.GuardAim.Towards(anim.GuardHeight, heightWant, heightStep);
        c.GuardPitch = Combat.GuardAim.Towards(c.GuardPitch, pitch, step);
        if (rigs is not null)
        {
            ref var rig = ref rigs.Find(e);
            if (!Unsafe.IsNullRef(ref rig)) rig.AimTarget = c.GuardPitch;
        }
    }

    /// <summary>A parry window opened by the block's press, and the threat it turns to (for the event).</summary>
    void OpenParry(World world, CharacterAssets assets, EntityId e, ref Combatant c, Combat.GuardSettings g, AimOffsetSettings aimSettings, float aim, long tick)
    {
        c.ParryLeft = g.ParryWindowSeconds;
        c.ParryWait = g.ParryRepressSeconds;
        bool seen = guardAim.Threat(world, assets, e, g, aimSettings, out var yaw, out var pitch, out var threat);
        world.Events<Combat.ParryBegun>().Publish(new Combat.ParryBegun(e, tick, seen ? yaw : 0f, seen ? pitch : aim, threat));
    }

    /// <summary>The shield's block kept on (or raised to) the shield arm's layer from <paramref name="weight"/>, at its raised frame.</summary>
    static void ShieldUp(ref Combatant c, ref CharacterAnimation anim, ActionSet set, float weight)
    {
        anim.GuardClip = set.Block.Clip;
        anim.GuardTime = MathF.Max(0f, set.Block.HoldSeconds);
        anim.GuardWeight = Math.Clamp(weight, 0f, 1f);
        anim.GuardTarget = 1f;
        c.ShieldGuard = true;
    }

    static readonly AimOffsetSettings DefaultAim = new();
    static readonly ThirdPersonAimSettings DefaultThirdAim = new();   // the view's pitch

    void Begin(World world, CharacterAssets assets, EntityId e, ref Combatant c, ref CharacterAnimation anim, ActionSet set, AttackStrength strength, float aim,
        AimOffsetSettings aimSettings, long tick, in Combat.CombatTempo tempo, bool guardOn)
    {
        var role = strength == AttackStrength.Strong ? ActionRole.AttackStrong : ActionRole.AttackQuick;
        var motion = role == ActionRole.AttackQuick ? set.For(role, c.NextVariant++) : set.For(role);
        if (!motion.Has) return;
        if (guardOn && c.Blocking)
        {
            // Block never cancels a swing, and a swing lowers no shield — the shield arm keeps the block on its layer;
            // a lone weapon (or bare hands) lowers its guard and swings, and raises it again when the swing is over if block is held.
            if (set.Block.Has && Combat.GuardLayer.ArmOf(assets, anim.Kind) is not null)
            {
                if (!c.ShieldGuard) ShieldUp(ref c, ref anim, set, anim.Action == ActionRole.Block ? anim.ActionWeight : 1f);
            }
            else Lower(world, e, ref c, ref anim, tick, set);
        }
        c.ParryLeft = 0f;
        // The body's tempo — the motion at its rate times the swing rate, the busy time after the strike by the recovery rate.
        ActionChannel.Start(ref anim, motion, role, rateScale: tempo.SwingRate);
        c.Busy = tempo.BusyFor(motion);
        c.Swings++;
        c.Swing = c.Swings;
        c.StrikeAt = motion.StrikeSeconds;
        c.Buffered = false;
        c.PointKnown = false;
        world.Events<AttackBegun>().Publish(new AttackBegun(e, tick, c.Swing, strength, role, motion.Clip, AimOffset.Clamp(aim, aimSettings)));
    }

    // ---- directional attacks, ready and release ----

    /// <summary>
    /// Bannerlord's: attack's <b>press</b> chooses the blow's direction
    /// (<see cref="DirectionalAttacks.Choose"/>: the fighter's own, else the player's mode, else automatic) and begins it <b>held
    /// ready</b> — its wind-up played to its ready point and held there while attack is held (<see cref="ActionChannel.StartReady"/>;
    /// the hold is not busy time); the <b>release</b> strikes (<see cref="ActionChannel.Strike"/>) — at once, or, for a tap released
    /// during the wind-up, as the wind-up goes on: a whole blow either way. No strong blow: holding waits, it adds nothing. A press
    /// during a swing is buffered once with its direction chosen then; its blow begins when the busy time (and a stun) is over —
    /// held ready if attack is still held, else whole. The refusals are 1.1's (the catch, a move, dead, no attack, no weapon hand;
    /// the block with the guard off; a second press in a swing). A held blow is cancelled by the cancel key (a feint), a dodge,
    /// a hurt as a swing in its wind-up is (<see cref="SwingCancel"/>).
    /// </summary>
    void Directional(World world, CharacterAssets assets, DirectionalAttacks dirs, EntityId e, ref Combatant c, ref CharacterAnimation anim, ActionSet? set,
        in CombatIntent intent, bool dead, bool atReady, Store<MasqueradeState>? masks, Store<Controlled>? players, Store<Traversal>? moves, float aim,
        AimOffsetSettings aimSettings, long tick, in Combat.CombatTempo tempo, bool guardOn)
    {
        bool down = intent.Attack, pressed = down && !c.WasAttack, released = !down && c.WasAttack;
        c.WasAttack = down;
        bool stunned = c.Stun > 0f;
        bool busy = c.Busy > 0f || stunned;
        if (pressed)
        {
            AttackRefusal? why = null;
            if (dead) why = AttackRefusal.Dead;
            else if (set is null || !set.HasAttack) why = AttackRefusal.NoAttack;
            else if (HeldItems.WeaponHandGone(world, e)) why = AttackRefusal.NoWeaponHand;
            else if (masks is not null && players is not null && players.Has(e) && masks.Has(e) && masks.Get(e).Mode == MasqueradeMode.Masquerade) why = AttackRefusal.Catch;
            else if (c.Blocking && !guardOn) why = AttackRefusal.Blocking;
            else if (moves is not null && moves.Has(e) && moves.Get(e).Kind != TraversalKind.None) why = AttackRefusal.Moving;
            else if (busy && (c.Buffered || c.Pending != AttackStrength.None)) why = AttackRefusal.MidSwing;
            var d = AttackDirection.None;
            var motion = ActionMotion.None;
            if (why is null)
            {
                var choice = dirs.Choose(world, e, in c, in intent);
                d = dirs.ResolveFor(world, assets, e, anim.Kind, choice, intent, out motion);
                if (d == AttackDirection.None || !motion.Has) why = AttackRefusal.NoAttack;
            }
            if (why is { } refused)
            {
                c.AttackHeld = -1f;
                world.Events<AttackRefused>().Publish(new AttackRefused(e, tick, refused));
            }
            else
            {
                c.AttackHeld = 0f;
                if (busy)
                {
                    c.Buffered = true;
                    c.PendingDirection = d;
                    // Pressed in the stun — said and buffered.
                    if (stunned) world.Events<AttackRefused>().Publish(new AttackRefused(e, tick, AttackRefusal.Stunned));
                }
                else BeginDirectional(world, assets, e, ref c, ref anim, set!, d, motion, held: true, aim, aimSettings, tick, tempo, guardOn);
            }
        }
        else if (down && c.AttackHeld >= 0f) c.AttackHeld += Dt;

        // ---- a held blow takes the direction chosen now (the keys held, a flick, the crosshair), from where it is.
        if (!pressed && down && c.Readied && c.AttackHeld >= 0f && dirs.Settings.SwitchWhileHeld && !dead && ActionRoles.IsAttack(anim.Action) && !anim.ActionEnding
            && dirs.ChooseHeld(world, e, in c, in intent) is { } now)
        {
            var to = dirs.ResolveFor(world, assets, e, anim.Kind, now, intent, out var toMotion);
            if (to != AttackDirection.None && to != c.Direction && toMotion.Has && toMotion.HoldSeconds >= 0f) SwitchHeld(world, e, ref c, ref anim, to, in toMotion, dirs.Settings.SwitchFadeSeconds, tick, tempo);
        }
        // ---- end

        // Held its most (data; none by default): it strikes by itself.
        float most = dirs.Settings.HoldMostSeconds;
        if (atReady && c.Readied && most > 0f && c.ReadyHeld >= most)
        {
            StrikeReadied(world, e, ref c, ref anim, tick);
            c.AttackHeld = -1f;
        }
        if (released && c.AttackHeld >= 0f)
        {
            c.AttackHeld = -1f;
            if (c.Readied) StrikeReadied(world, e, ref c, ref anim, tick);
            else if (c.Buffered) c.Pending = AttackStrength.Quick;   // let go before its turn: a whole blow when it comes
        }
        if (c.Busy <= 0f && c.Stun <= 0f && c.Buffered && c.PendingDirection != AttackDirection.None && !dead && set is { HasAttack: true })
        {
            // The buffered press's turn: held ready if attack is still held since that press, else a whole blow.
            bool held = c.Pending == AttackStrength.None && down && c.AttackHeld >= 0f;
            var d = c.PendingDirection;
            var motion = dirs.Motion(world, assets, anim.Kind, d);
            c.PendingDirection = AttackDirection.None;
            c.Pending = AttackStrength.None;
            c.Buffered = false;
            if (motion.Has) BeginDirectional(world, assets, e, ref c, ref anim, set, d, motion, held, aim, aimSettings, tick, tempo, guardOn);
        }
        else if (!c.Buffered) c.PendingDirection = AttackDirection.None;   // a cancel dropped the buffered press
    }

    const float Dt = World.TickSeconds;

    /// <summary>A directional blow begun — held ready at its ready point (<paramref name="held"/>), else whole; the busy time, the swing's number, the event.</summary>
    void BeginDirectional(World world, CharacterAssets assets, EntityId e, ref Combatant c, ref CharacterAnimation anim, ActionSet set, AttackDirection d,
        in ActionMotion motion, bool held, float aim, AimOffsetSettings aimSettings, long tick, in Combat.CombatTempo tempo, bool guardOn)
    {
        if (!motion.Has) return;
        if (guardOn && c.Blocking)
        {
            // As Begin: a shield keeps its block on its arm's layer; a lone weapon lowers its guard and swings.
            if (set.Block.Has && Combat.GuardLayer.ArmOf(assets, anim.Kind) is not null)
            {
                if (!c.ShieldGuard) ShieldUp(ref c, ref anim, set, anim.Action == ActionRole.Block ? anim.ActionWeight : 1f);
            }
            else Lower(world, e, ref c, ref anim, tick, set);
        }
        c.ParryLeft = 0f;
        float ready = held ? motion.HoldSeconds : -1f;
        ActionChannel.StartReady(ref anim, motion, ActionRole.AttackQuick, ready, rateScale: tempo.SwingRate);
        c.Busy = tempo.BusyFor(motion);
        c.ReadyBusy = c.Busy;
        c.Swings++;
        c.Swing = c.Swings;
        c.StrikeAt = motion.StrikeSeconds;
        c.Buffered = false;
        c.PointKnown = false;
        c.Direction = d;
        c.Readied = held && ready >= 0f;
        c.ReadyAt = ready;
        c.ReadyHeld = 0f;
        world.Events<AttackBegun>().Publish(new AttackBegun(e, tick, c.Swing, AttackStrength.Quick, ActionRole.AttackQuick, motion.Clip, AimOffset.Clamp(aim, aimSettings), d));
        if (!c.Readied) world.Events<AttackReleased>().Publish(new AttackReleased(e, tick, c.Swing, d, 0f));
    }

    /// <summary>
    /// The blow held ready becomes <paramref name="to"/>'s — the channel cross-faded over <paramref name="fade"/> s into
    /// the new clip at its ready point (or at the same share of its wind-up, before the old reached its own: <see cref="ActionChannel.SwitchReady"/>);
    /// the same swing, its busy time the new blow's whole less the new clip's time played (the hold still not counted), its strike mark
    /// and direction the new one's. <see cref="AttackSwitched"/> says so.
    /// </summary>
    static void SwitchHeld(World world, EntityId e, ref Combatant c, ref CharacterAnimation anim, AttackDirection to, in ActionMotion motion, float fade, long tick,
        in Combat.CombatTempo tempo)
    {
        bool reached = ActionChannel.AtReady(anim);
        var from = c.Direction;
        ActionChannel.SwitchReady(ref anim, motion, motion.HoldSeconds, fade, rateScale: tempo.SwingRate);
        c.ReadyBusy = tempo.BusyFor(motion);
        c.Busy = anim.ActionRate > 0f ? MathF.Max(0f, c.ReadyBusy - anim.ActionTime / anim.ActionRate) : c.ReadyBusy;
        c.StrikeAt = motion.StrikeSeconds;
        c.ReadyAt = motion.HoldSeconds;
        c.Direction = to;
        c.PointKnown = false;
        world.Events<AttackSwitched>().Publish(new AttackSwitched(e, tick, c.Swing, from, to, reached));
    }

    /// <summary>
    /// The blow held ready strikes — the clip let go from where it is; the busy time counted from the clip's place (the whole
    /// blow's less the clip time played, at its rate), so the time held is added to the swing and nothing else changes.
    /// </summary>
    static void StrikeReadied(World world, EntityId e, ref Combatant c, ref CharacterAnimation anim, long tick)
    {
        ActionChannel.Strike(ref anim);
        c.Readied = false;
        if (anim.ActionRate > 0f) c.Busy = MathF.Max(0f, c.ReadyBusy - anim.ActionTime / anim.ActionRate);
        world.Events<AttackReleased>().Publish(new AttackReleased(e, tick, c.Swing, c.Direction, c.ReadyHeld));
    }
    // ---- end

    static void Lower(World world, EntityId e, ref Combatant c, ref CharacterAnimation anim, long tick, ActionSet? set)
    {
        if (anim.Action == ActionRole.Block) ActionChannel.Release(ref anim, fadeNow: set?.Block.Composed ?? true);
        c.Blocking = false;
        if (c.ShieldGuard) { anim.GuardTarget = 0f; c.ShieldGuard = false; }   // the shield arm's layer let go
        c.ParryLeft = 0f;
        world.Events<BlockLowered>().Publish(new BlockLowered(e, tick, c.BlockSeconds));
    }
}

/// <summary>
/// The <c>Strike</c> moment of each blow (<see cref="Phase.Contact"/>, where the pose is this tick's on this tick's
/// body): each tick a blow swings, its point (the blade's tip, <see cref="HeldItems.Point"/>) is found and kept; on the tick
/// the action's time crosses the clip's <c>Strike</c> mark a <see cref="StrikeMoment"/> is published with the point and its
/// speed over the last tick. What the blade meets in its active window is contact's (<see cref="Combat.ContactSystem"/>).
/// </summary>
public sealed class StrikeSystem : ISystem
{
    public void Run(World world)
    {
        var fighters = world.Store<Combatant>();
        var anims = world.Store<CharacterAnimation>();
        var data = fighters.Data;
        var ents = fighters.Entities;
        long tick = world.Tick;
        for (int i = 0; i < data.Length; i++)
        {
            var e = ents[i];
            ref var c = ref data[i];
            ref var anim = ref anims.Find(e);
            if (Unsafe.IsNullRef(ref anim) || !ActionRoles.IsAttack(anim.Action) || anim.ActionEnding) { c.PointKnown = false; continue; }
            if (!HeldItems.Point(world, e, out var point)) continue;
            // A blade that went on after a bite put its clip back before the mark crosses it again: told once.
            if (ActionChannel.Crossed(anim, c.StrikeAt) && !Combat.FollowThrough.StruckBefore(world, e, c.Swing))
            {
                float speed = c.PointKnown ? LimbIK.Length(point - c.PointLast) / World.TickSeconds : 0f;
                world.Events<StrikeMoment>().Publish(new StrikeMoment(e, tick, c.Swing, anim.Action, point, speed));
            }
            c.PointLast = point;
            c.PointKnown = true;
        }
    }
}

/// <summary>
/// The attacks' questions for the presentation: <see cref="NextBlow"/>, the blow a release of attack would strike
/// now — the swing's arc shows it (the view work's <c>Presentation/SwingArc.cs</c>).
/// </summary>
public static class Attacks
{
    static readonly CombatSettings DefaultCombat = new();

    /// <summary>
    /// The blow a release of attack would strike now for <paramref name="e"/>: false when none could be made (no weapon or fists,
    /// dead; the arc's other refusals — the catch, a move, the weapon hand lost — are the caller's). With directional attacks on
    /// (<see cref="DirectionalAttacks"/>): <b>the blow held ready</b> while one is (<see cref="NextBlowInfo.Held"/>: the blow on the channel,
    /// its clip held at its ready point; to step a copy of it forward, let the copy go — <c>ActionHolds = false</c>); else a press
    /// buffered during a swing (its direction chosen at its press); else the direction a press now would choose
    /// (<see cref="DirectionalAttacks.Choose"/>: the player's mode, the crosshair on the foe, the mouse's recent movement, the keys held)
    /// on the body's blow for it. Off (1.1's): the strong blow while attack is held past <see cref="CombatSettings.StrongAfter"/> or a
    /// strong blow waits, else the quick blow the combo plays next. Reads the world, changes nothing, allocates nothing once the
    /// kind's blows are known.
    /// </summary>
    public static bool NextBlow(World world, EntityId e, out NextBlowInfo blow)
    {
        blow = default;
        if (!world.TryService<CharacterAssets>(out var assets) || !world.Alive(e)) return false;
        ref var anim = ref world.Store<CharacterAnimation>().Find(e);
        ref var c = ref world.Store<Combatant>().Find(e);
        if (Unsafe.IsNullRef(ref anim) || Unsafe.IsNullRef(ref c)) return false;
        if (assets.Kind(anim.Kind).Actions is not { HasAttack: true } set || ActionRoles.IsDeath(anim.Action)) return false;
        if (!DirectionalAttacks.On(world, out var dirs))
        {
            var combat = world.TryService<CombatSettings>(out var cs) ? cs : DefaultCombat;
            var strength = c.Pending != AttackStrength.None ? c.Pending
                : c.AttackHeld >= 0f && c.AttackHeld >= combat.StrongAfter ? AttackStrength.Strong : AttackStrength.Quick;
            var role = strength == AttackStrength.Strong ? ActionRole.AttackStrong : ActionRole.AttackQuick;
            var m = role == ActionRole.AttackQuick ? set.For(role, c.NextVariant) : set.For(role);
            blow = new NextBlowInfo(AttackDirection.None, role, m, strength, false, -1f, DirectionSource.Intent);
            return m.Has;
        }
        if (c.Readied && ActionRoles.IsAttack(anim.Action) && !anim.ActionEnding)
        {
            var held = dirs.Motion(world, assets, anim.Kind, c.Direction);
            blow = new NextBlowInfo(c.Direction, ActionRole.AttackQuick, held, AttackStrength.Quick, true, c.ReadyAt, DirectionSource.Intent);
            return held.Has;
        }
        if (c.Buffered && c.PendingDirection != AttackDirection.None)
        {
            var waiting = dirs.Motion(world, assets, anim.Kind, c.PendingDirection);
            blow = new NextBlowInfo(c.PendingDirection, ActionRole.AttackQuick, waiting, AttackStrength.Quick, false, waiting.HoldSeconds, DirectionSource.Intent);
            return waiting.Has;
        }
        ref var intentRef = ref world.Store<CombatIntent>().Find(e);
        var intent = Unsafe.IsNullRef(ref intentRef) ? default : intentRef;
        var choice = dirs.Choose(world, e, in c, in intent);
        var d = dirs.ResolveFor(world, assets, e, anim.Kind, choice, intent, out var motion);
        blow = new NextBlowInfo(d, ActionRole.AttackQuick, motion, AttackStrength.Quick, false, motion.HoldSeconds, choice.Source);
        return d != AttackDirection.None && motion.Has;
    }

    /// <summary>Whether <paramref name="e"/> holds a blow ready (pressed, attack still held; directional attacks on) — for a guard or a brain that reads its foe.</summary>
    public static bool HeldReady(World world, EntityId e) =>
        world.TryStore<Combatant>(out var fighters) && fighters.Has(e) && fighters.Get(e).Readied;
}

/// <summary>What a script asks of a character: a quick or strong blow, the block, a hurt, a death.</summary>
public enum ScriptedAct
{
    Attack,
    Strong,
    Block,
    Hurt,
    Death,
    DeathSlow,
    // At the end: a blow from a direction (a tap with the intent's direction; directional attacks on).
    Left,
    Right,
    Overhead,
    Thrust,
}

/// <summary>One line of an action script: who (a name the host resolves), what, from which tick to which (the block's span; a blow's press), and the aim (radians) or none.</summary>
public readonly record struct ScriptedAction(string Who, ScriptedAct Act, long From, long To, float? Aim);

/// <summary>
/// Townsfolk and guards made to attack or block on a schedule (for tests and the coming arena): the host's
/// <c>--npc-actions &lt;who&gt;:&lt;action&gt;@&lt;s&gt;[+&lt;held s&gt;][^&lt;aim°&gt;],...</c> — actions <c>attack</c> (a tap),
/// <c>strong</c> (held past the threshold), <c>block</c> (held, by default a second), <c>hurt</c>, <c>death</c>,
/// <c>deathslow</c>; <c>^20</c> aims the blow 20° up, <c>^-25</c> down. The schedule is the session's options (carried in a
/// tape's head), so a replay drives the same.
/// </summary>
public sealed class ActionScript
{
    readonly List<(EntityId Who, ScriptedAction Line)> lines = [];

    public IReadOnlyList<(EntityId Who, ScriptedAction Line)> Lines => lines;

    public void Add(EntityId who, in ScriptedAction line) => lines.Add((who, line));

    /// <summary>The tape head's note for the schedule (the text as given).</summary>
    public const string TapeNote = "npcActions";

    /// <summary>Reads a schedule; false with the reason when a line is not understood.</summary>
    public static bool TryParse(string text, CombatSettings settings, out List<ScriptedAction> lines, out string error)
    {
        lines = [];
        error = "";
        foreach (var raw in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = raw.LastIndexOf(':'), at = raw.IndexOf('@');
            if (colon <= 0 || at < colon) { error = $"--npc-actions: '{raw}' is not who:action@seconds"; return false; }
            string who = raw[..colon], what = raw[(colon + 1)..at], when = raw[(at + 1)..];
            float? aim = null;
            int caret = when.IndexOf('^');
            if (caret >= 0)
            {
                if (!float.TryParse(when[(caret + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float deg))
                { error = $"--npc-actions: '{raw}': the aim after ^ is degrees"; return false; }
                aim = deg * DetMath.DegToRad;
                when = when[..caret];
            }
            float held = -1f;
            int plus = when.IndexOf('+');
            if (plus >= 0)
            {
                if (!float.TryParse(when[(plus + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out held))
                { error = $"--npc-actions: '{raw}': the time held after + is seconds"; return false; }
                when = when[..plus];
            }
            if (!float.TryParse(when, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds) || seconds < 0f)
            { error = $"--npc-actions: '{raw}': the time after @ is seconds"; return false; }
            if (!Enum.TryParse<ScriptedAct>(what, ignoreCase: true, out var act))
            { error = $"--npc-actions: '{raw}': the action is attack, strong, block, hurt, death, deathslow, left, right, overhead or thrust"; return false; }
            long from = (long)MathF.Round(seconds * World.TicksPerSecond);
            float hold = held >= 0f ? held : act switch { ScriptedAct.Strong => settings.StrongAfter + 0.1f, ScriptedAct.Block => 1f, _ => World.TickSeconds };
            long to = from + Math.Max(1, (long)MathF.Round(hold * World.TicksPerSecond));
            lines.Add(new ScriptedAction(who, act, from, to, aim));
        }
        return true;
    }
}

/// <summary>
/// The action script's turn (<see cref="Phase.Think"/>): for each scripted character, attack held through a blow's
/// press, block held through its span, a hurt or a death asked for on its tick, the aim of the line in force. Characters with
/// no line keep their intent.
/// </summary>
public sealed class ScriptedActionSystem : ISystem
{
    public void Run(World world)
    {
        if (!world.TryService<ActionScript>(out var script) || script.Lines.Count == 0) return;
        var intents = world.Store<CombatIntent>();
        long tick = world.Tick;
        var lines = script.Lines;
        // Clear the held buttons of every scripted character first, then set what this tick's lines hold.
        for (int i = 0; i < lines.Count; i++)
        {
            ref var intent = ref intents.Find(lines[i].Who);
            if (Unsafe.IsNullRef(ref intent)) continue;
            intent.Attack = false;
            intent.Block = false;
            intent.Direction = AttackDirection.None;
        }
        for (int i = 0; i < lines.Count; i++)
        {
            var (who, line) = lines[i];
            ref var intent = ref intents.Find(who);
            if (Unsafe.IsNullRef(ref intent) || tick < line.From) continue;
            bool within = tick < line.To;
            switch (line.Act)
            {
                case ScriptedAct.Attack or ScriptedAct.Strong when within: intent.Attack = true; break;
                case ScriptedAct.Left or ScriptedAct.Right or ScriptedAct.Overhead or ScriptedAct.Thrust when within:
                    intent.Attack = true;
                    intent.Direction = line.Act switch
                    {
                        ScriptedAct.Left => AttackDirection.Left,
                        ScriptedAct.Right => AttackDirection.Right,
                        ScriptedAct.Overhead => AttackDirection.Overhead,
                        _ => AttackDirection.Thrust,
                    };
                    break;
                case ScriptedAct.Block when within: intent.Block = true; break;
                case ScriptedAct.Hurt when tick == line.From: intent.Play = ActionRole.Hurt; break;
                case ScriptedAct.Death when tick == line.From: intent.Play = ActionRole.DeathQuick; break;
                case ScriptedAct.DeathSlow when tick == line.From: intent.Play = ActionRole.DeathSlow; break;
            }
            if (line.Aim is { } a && (within || line.Act is ScriptedAct.Attack or ScriptedAct.Strong or ScriptedAct.Left or ScriptedAct.Right or ScriptedAct.Overhead or ScriptedAct.Thrust)) { intent.AimPitch = a; intent.Aims = true; }
        }
    }
}

/// <summary>The fighting registered in one call, and the helper that arms a character.</summary>
public static class CombatServices
{
    /// <summary>The systems' places: the script in Think, the attacks in Act, the strike in Contact.</summary>
    public const int ScriptOrder = 90, AttackOrder = 100, StrikeOrder = 50;

    /// <summary>
    /// Registers the <see cref="Combatant"/> and <see cref="CombatIntent"/> components, the events (<see cref="AttackBegun"/>,
    /// <see cref="StrikeMoment"/>, <see cref="BlockRaised"/>, <see cref="BlockLowered"/>, <see cref="AttackRefused"/>; an earlier build:
    /// <see cref="SwingCancelled"/>, <see cref="Combat.ParryBegun"/>, <see cref="Combat.GuardBroken"/>, <see cref="Combat.GuardResolved"/>), the
    /// <see cref="CombatSettings"/>, <see cref="ActionScript"/> and <see cref="Combat.GuardSettings"/> services, and the systems <c>Think/90 platform.action-script</c>,
    /// <c>Act/100 platform.attacks</c> and <c>Contact/50 platform.strike</c>. After the animation.
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b, CombatSettings? settings = null)
    {
        var s = settings ?? new CombatSettings();
        b.Component<Combatant>().Component<CombatIntent>();
        b.Event<AttackBegun>().Event<StrikeMoment>().Event<BlockRaised>().Event<BlockLowered>().Event<AttackRefused>();
        b.Event<AttackReleased>();   // a blow held ready let go
        b.Event<AttackSwitched>();   // a held blow switched to another direction
        b.Service(s).Service(new ActionScript());
        // The guard's numbers (the module fills them), the cancel, the parry and the guard's verdict.
        if (!b.Peek.TryService<Combat.GuardSettings>(out _)) b.Service(new Combat.GuardSettings());
        b.Event<SwingCancelled>().Event<Combat.ParryBegun>().Event<Combat.GuardBroken>();
        if (!b.Peek.TryService<EventQueue<Combat.GuardResolved>>(out _)) b.Event<Combat.GuardResolved>();
        b.System(Phase.Think, ScriptOrder, "platform.action-script", new ScriptedActionSystem());
        b.System(Phase.Act, AttackOrder, "platform.attacks", new AttackSystem(s));
        b.System(Phase.Contact, StrikeOrder, "platform.strike", new StrikeSystem());
        Animation.GuardSolverServices.Register(b);   // the guard's arm solver and the give (Animation/GuardSolver.cs)
        return b;
    }

    /// <summary>
    /// A swing stopped by what it met (a shield, a parrying blade, the level): <b>cut short</b> — its clip put back to
    /// the moment it met (<paramref name="at"/>, the share of this tick's advance at the contact), held there (its rate 0)
    /// and faded back to the locomotion over the channel's fade-out (0.2 s): the attacker's short recoil, the blade drawn
    /// where it stopped rather than through what stopped it. Its strike mark is not reached and no more is swept of it; the
    /// busy time (<c>RELOADTIME</c>) runs on as it was (so the owner swings again no sooner). with blades stopping where
    /// they stop (<see cref="Combat.BladeStopRule"/>) a stop is a bite or a rebound and a bring-back
    /// (<see cref="Combat.FollowThrough"/>); this is the stop with the rule off, or with <see cref="Combat.BladeStopRule.StopFreesOwnerAtOnce"/>,
    /// and a person losing the weapon hand mid-swing.
    /// </summary>
    public static void CutShort(ref CharacterAnimation anim, float at = 1f)
    {
        if (!ActionRoles.IsAttack(anim.Action)) return;
        anim.ActionTime = anim.ActionLast + (anim.ActionTime - anim.ActionLast) * Math.Clamp(at, 0f, 1f);
        anim.ActionRate = 0f;
        anim.ActionEnding = true;
    }

    /// <summary>Makes a character able to fight: its <see cref="Combatant"/> and <see cref="CombatIntent"/>.</summary>
    public static void Arm(World world, EntityId e)
    {
        world.Set(e, Combatant.Ready);
        world.Set(e, new CombatIntent());
    }
}
