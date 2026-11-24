// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the swing's arc — where the next blow's blade will go ("a kind of
// faint very translucent kind of crescent arc visible that shows what the swords path will be, and it will always stay
// updated and accurate no matter how the player moves or looks").
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Presentation;

/// <summary>Which blow the arc shows.</summary>
public enum SwingArcBlow
{
    /// <summary>None: no arc (no weapon or fists to swing, dead, the catch on, a move, the weapon hand lost).</summary>
    None = 0,
    /// <summary>The blow being swung: the rest of its path through its active window.</summary>
    Current,
    /// <summary>The quick blow a release of attack now would strike (<c>Attacks.NextBlow</c>, from the direction chosen; with directions off the combo's next, <see cref="Combatant.NextVariant"/>).</summary>
    Quick,
    /// <summary>The strong blow: attack held past <see cref="CombatSettings.StrongAfter"/>, or a strong blow waiting for the swing to end.</summary>
    Strong,
}

/// <summary>The arc's numbers (ENGINE_SANDBOX, ours: looked at on Christof's broadsword).</summary>
public sealed record SwingArcSettings
{
    /// <summary>
    /// The part of the swing shown: the places of its active window whose tip is within this many degrees either side of the
    /// body's facing, seen from above about the feet (100°: where the blade can meet someone before the body; the whirl's
    /// and the forehand's back half, behind the shoulders, is left out).
    /// </summary>
    public float FrontDegrees { get; init; } = 100f;

    /// <summary>Where the ribbon starts along the blade, from the guard (0) to the tip (1): the half that strikes (0.5).</summary>
    public float RibbonFrom { get; init; } = 0.5f;

    /// <summary>The brightness at the ends of what is shown against the strike mark's (1): it fades from the mark to this.</summary>
    public float EndBrightness { get; init; } = 0.25f;

    /// <summary>The share of the opacity left where the ribbon is hidden by something nearer (a wall, a body, the own arm): drawn through, fainter.</summary>
    public float HiddenShare { get; init; } = 0.35f;

    /// <summary>
    /// The brightness kept past where the blade would first meet someone (a body or a shield, as they stand now): the blade stops
    /// there (it bites, or springs back) unless the rules let it cut on, so the rest is drawn at this share (0.3).
    /// </summary>
    public float PastMeetingShare { get; init; } = 0.3f;

    /// <summary>Whether the person met is posed again at the meeting's tick (the second pass; off: as they stand now).</summary>
    public bool PoseTheMet { get; init; } = true;

    /// <summary>The length (m) of the mark across the ribbon where the blade would first meet someone, along its path.</summary>
    public float MarkLength { get; init; } = 0.04f;

    /// <summary>The longest a prediction looks ahead (s): a press waits at most a swing's busy time (1.1's 1.1 s for the broadsword) and the window ends within the clip.</summary>
    public float MostSeconds { get; init; } = 3f;

    /// <summary>
    /// The old way, for comparison — the body's place carried on by its velocity over the blow's start-to-end
    /// time (the arc where the blade will be in the world). Off by default: the arc is in the <b>body's own frame</b>,
    /// the blow swept from where he stands now, drawn on him every picture (<i>"it's always right in front of
    /// Christof ... So even moving around, the arc will stays relative to him right in front"</i>). The setting
    /// <c>swingArcCarryOn</c>, <c>--arc-carry-on</c>.
    /// </summary>
    public bool CarryOn { get; init; }

    /// <summary>
    /// While a blow is being swung, its whole front path is drawn — the part the blade has already swept (in the
    /// body's frame, as now) as well as the rest — so the crescent stays whole in front of him through the blow instead of
    /// shrinking behind the blade (<i>"always show the sword path"</i>). Off: the rest of its path only.
    /// </summary>
    public bool WholeBlow { get; init; } = true;
}

/// <summary>One place of the predicted blade: the blade, the clip time it is at, and whether it is in the part shown (before the body).</summary>
public readonly record struct SwingArcPlace(BladePlacement Blade, float ClipTime, bool Front);

/// <summary>One quad of the arc's ribbon to draw: its four corners (mid-blade then tip, at two places) and their brightness (0 to 1).</summary>
public readonly record struct SwingArcQuad(Vector3 MidA, Vector3 TipA, Vector3 MidB, Vector3 TipB, float A, float B, bool Mark = false);

/// <summary>
/// The swing's arc (ours): for the body the player controls, with a weapon or fists to swing,
/// <b>where the next blow's blade will go</b> — the blow being swung (the rest of its path), else the quick blow the combo
/// plays next, or the strong blow once attack has been held long enough to make one — worked out from the body's state now
/// by stepping a copy of it forward tick by tick exactly as the systems do (<see cref="AttackSystem"/>'s busy time and its
/// <see cref="ActionChannel.Start"/>, <see cref="AnimationSystem"/>'s locomotion and <see cref="ActionChannel.Advance"/>, the
/// <see cref="PoseRigSystem"/>'s aim offset easing toward <see cref="PoseRig.AimTarget"/>, the guard's turn coming back) and
/// posing it on a scratch <see cref="PoseEvaluator"/> at each tick of the blow's <see cref="ActiveWindow"/> (and a fast
/// swing's sub-steps), the blade placed as <see cref="ContactSystem"/> places it — so for a body that stands as it is, the arc
/// is the blade contact will sweep, place for place. An earlier build (<i>"it's always right in front of
/// Christof, and it's always drawn directly in front of him a normal range where his sword is going to be"</i>): the
/// prediction is in the <b>body's own frame</b> — the blow swept from where he stands now, no carrying on by his velocity
/// (<see cref="SwingArcSettings.CarryOn"/> keeps the earlier build's way) — and drawn on the body every picture (<see cref="Fill"/>), so
/// it moves with him exactly; moving, it differs from the blade's path in the world by the body's travel during the blow,
/// which is what was asked for. The <b>whole front path</b> is drawn: not cut where the blade would meet the level or a
/// person (faint past the first, the amber mark at a person), and a blow being swung keeps the part already swept
/// (<see cref="SwingArcSettings.WholeBlow"/>). <b>Presentation only</b>: it reads the world and changes
/// nothing; it is not a world service, not digested, and nothing in the simulation reads it. Fixed storage: nothing is
/// allocated once the scratch pose's buffers are made.
/// </summary>
public sealed class SwingArcs
{
    /// <summary>The most places kept (a 1.1 window is about 40 ticks; Celerity's sub-steps up to 8 a tick).</summary>
    public const int MaxPlaces = 512;

    readonly SwingArcPlace[] places = new SwingArcPlace[MaxPlaces];
    readonly Vector3[] feetOf = new Vector3[MaxPlaces];
    /// <summary>The prediction's core: a body's blade at a clip time from its simulation state alone (<see cref="SwingArcCore"/>).</summary>
    public SwingArcCore Core { get; } = new();
    int count;
    static readonly AnimationSettings Animation = new();
    static readonly GuardSettings NoGuard = new() { Enabled = false };
    static readonly AimOffsetSettings DefaultAim = new();
    static readonly ContactConfig DefaultContact = new();

    public SwingArcSettings Settings { get; set; } = new();

    /// <summary>The action channel's numbers the animation runs with (the platform's defaults: the session does not change them).</summary>
    public ActionChannelSettings Channel { get; set; } = Animation.Actions;

    /// <summary>Which blow was predicted last (<see cref="SwingArcBlow.None"/>: no arc).</summary>
    public SwingArcBlow Blow { get; private set; }

    /// <summary>The predicted blow's clip, its <c>Strike</c> mark (clip seconds) and its window.</summary>
    public int Clip { get; private set; } = -1;
    public float Strike { get; private set; }
    public ActiveWindow Window { get; private set; } = ActiveWindow.None;

    /// <summary>The tick the prediction was made at, and the body's feet and yaw then (the picture stands the arc on the drawn body by them).</summary>
    public long Tick { get; private set; } = -1;
    public EntityId Of { get; private set; }
    public Vector3 Feet { get; private set; }
    public float Yaw { get; private set; }

    /// <summary>How many ticks from the prediction's the blow's first place is (its first swept tick's last tick).</summary>
    public int Lead { get; private set; }

    /// <summary>
    /// Whether the predicted blade meets the level (a wall, the floor): the blow will stop there. the arc no longer ends
    /// there — it goes on past it at <see cref="SwingArcSettings.PastMeetingShare"/>, the whole front path shown — and
    /// <see cref="LevelAt"/> says where.
    /// </summary>
    public bool EndsAtLevel { get; private set; }

    /// <summary>Where the predicted blade first meets the level, as a place index and the share between it and the next (−1: nowhere).</summary>
    public float LevelAt { get; private set; } = -1f;

    /// <summary>
    /// How many of <see cref="Places"/> the blade has already swept, the blow being swung (<see cref="SwingArcSettings.WholeBlow"/>):
    /// they come first, from its window's start to now; the place after them is the blade now.
    /// </summary>
    public int Past { get; private set; }

    /// <summary>
    /// Where the predicted blade would first meet someone — a body's hit volume or a shield, each as it stands now — as a place
    /// index and the share between it and the next (−1: no one): <see cref="Meets"/>, the part (<see cref="MeetsRegion"/>, none for
    /// a shield) and the point. The blow will stop there unless the rules let it cut on; the picture dims the rest.
    /// </summary>
    public float MeetsAt { get; private set; } = -1f;
    public EntityId Meets { get; private set; }
    public BodyRegion MeetsRegion { get; private set; }
    public bool MeetsShield { get; private set; }
    public Vector3 MeetsPoint { get; private set; }

    static BladePlacement Mix(in BladePlacement a, in BladePlacement b, float s) =>
        new(Vector3.Lerp(a.Guard, b.Guard, s), Vector3.Lerp(a.Tip, b.Tip, s), a.Radius + (b.Radius - a.Radius) * s);

    /// <summary>The predicted places, in order: the blade at each tick contact sweeps (and a fast swing's sub-steps), from the tick before the window to its last.</summary>
    public ReadOnlySpan<SwingArcPlace> Places => places.AsSpan(0, count);

    /// <summary>Clears the prediction (no arc).</summary>
    public void Clear()
    {
        count = 0;
        Past = 0;
        Blow = SwingArcBlow.None;
        Clip = -1;
        Window = ActiveWindow.None;
        Tick = -1;
        Of = EntityId.None;
        (EndsAtLevel, LevelAt) = (false, -1f);
        (MeetsAt, Meets, MeetsRegion, MeetsShield, MeetsPoint) = (-1f, EntityId.None, BodyRegion.None, false, default);
    }

    /// <summary>
    /// Whether <paramref name="who"/> could swing now (the arc's own test, shared with the crosshair's dimming): a
    /// living body with a melee weapon or its fists — a <see cref="Combatant"/> whose kind has an attack — not dying, its weapon
    /// hand not lost, not in a move (a vault, a hang), and (the player's) not under the masquerade's catch, which refuses every
    /// attack in a public place. Presentation only; nothing allocated.
    /// </summary>
    public static bool CanSwing(World world, EntityId who)
    {
        if (!world.TryService<CharacterAssets>(out var assets) || !world.Alive(who)) return false;
        if (!world.TryStore<CharacterAnimation>(out var anims) || !world.TryStore<Combatant>(out var fighters) || !world.TryStore<Body>(out var bodies)) return false;
        if (!anims.Has(who) || !fighters.Has(who) || !bodies.Has(who)) return false;
        ref var anim = ref anims.Get(who);
        if (assets.Kind(anim.Kind).Actions is not { HasAttack: true }) return false;
        if (ActionRoles.IsDeath(anim.Action) || HeldItems.WeaponHandGone(world, who)) return false;
        // The bow in hand (or a swap under way): no blade to swing, so no arc and the crosshair at full strength.
        if (world.TryStore<Bow>(out var bows) && bows.Has(who) && (bows.Get(who).InHand || world.TryStore<BowHand>(out var hands) && hands.Has(who) && hands.Get(who).SwapLeft > 0f)) return false;
        // A press would be refused: the masquerade's catch (the player's), a move. No arc then: no blow would come.
        if (world.TryStore<MasqueradeState>(out var masks) && world.TryStore<Controlled>(out var players) && players.Has(who)
            && masks.Has(who) && masks.Get(who).Mode == MasqueradeMode.Masquerade) return false;
        if (world.TryStore<Traversal>(out var moves) && moves.Has(who) && moves.Get(who).Kind != TraversalKind.None) return false;
        return true;
    }

    /// <summary>
    /// Predicts <paramref name="who"/>'s next blow from the world as it stands (after a tick): false, and no arc, when it has
    /// none to make. See the class's remarks.
    /// </summary>
    public bool Predict(World world, EntityId who)
    {
        Clear();
        if (!CanSwing(world, who)) return false;
        var assets = world.Service<CharacterAssets>();
        ref var anim = ref world.Store<CharacterAnimation>().Get(who);
        ref var c = ref world.Store<Combatant>().Get(who);
        ref var body = ref world.Store<Body>().Get(who);
        var kind = assets.Kind(anim.Kind);
        var set = kind.Actions!;

        var config = world.TryService<ContactConfig>(out var cc) ? cc : DefaultContact;
        if (!world.TryService<ActiveWindows>(out var windows)) return false;
        var guard = world.TryService<GuardSettings>(out var gs) ? gs : NoGuard;
        var aimSettings = world.TryService<AimOffsetSettings>(out var aimS) ? aimS : DefaultAim;
        var tempo = world.TryStore<CombatTempo>(out var tempos) ? CombatTempos.Of(tempos, who) : CombatTempo.Normal;
        bool hasRig = world.TryStore<PoseRig>(out var rigs) && rigs.Has(who);
        var rig = hasRig ? rigs!.Get(who) : default;

        // Which blow: the one swinging while its window is still ahead of it, else the next.
        var s = anim;
        bool swinging = ActionRoles.IsAttack(anim.Action) && !anim.ActionEnding && !FollowThrough.Held(world, who, c.Swing);
        ActiveWindow window = ActiveWindow.None;
        if (swinging)
        {
            window = windows.For(assets, anim.Kind, anim.ActionClip, c.StrikeAt, config.Window);
            if (!window.Has || anim.ActionTime > window.To) swinging = false;
        }
        ActionMotion motion = ActionMotion.None;
        ActionRole role = ActionRole.None;
        if (swinging)
        {
            Blow = SwingArcBlow.Current;
            (Clip, Strike) = (anim.ActionClip, c.StrikeAt);
            // A blow held ready at its wind-up (the direction work's ready and release) is shown as a release now would
            // strike it: the copy is let go (an attack never holds earlier, so nothing else changes).
            if (s.ActionHolds) s.ActionHolds = false;
        }
        else
        {
            // The blow a release would strike now (the direction work's Attacks.NextBlow: the direction chosen by the setting;
            // The earlier build's choice — the quick combo, the strong blow after StrongAfter — with directions off).
            (motion, role, Blow) = Attacks.NextBlow(world, who, out var next)
                ? (next.Motion, next.Role, next.Strength == AttackStrength.Strong ? SwingArcBlow.Strong : SwingArcBlow.Quick)
                : (ActionMotion.None, ActionRole.None, SwingArcBlow.None);
            if (!motion.Has) { Clear(); return false; }
            window = windows.For(assets, anim.Kind, motion.Clip, motion.StrikeSeconds, config.Window);
            (Clip, Strike) = (motion.Clip, motion.StrikeSeconds);
        }
        if (!window.Has) { Clear(); return false; }
        Window = window;
        (Tick, Of, Feet, Yaw) = (world.Tick, who, body.Feet, body.Yaw);

        // Step a copy forward tick by tick as the systems would, the body standing where it is (its own frame) or, the old
        // way (CarryOn), carried on by its velocity.
        float dt = World.TickSeconds;
        bool standing = anim.Role is MotionRole.Idle or MotionRole.Crouch or MotionRole.Land;
        float fadeIn = tempo.SwingRate > ContactSystem.SubStepTempo ? tempo.SwingRate / ContactSystem.SubStepTempo : 1f;
        int subSteps = tempo.SwingRate <= ContactSystem.SubStepTempo ? 1 : Math.Min(ContactSystem.MaxSubSteps, (int)MathF.Ceiling(tempo.SwingRate / ContactSystem.SubStepTempo));
        // The old way: the body's place carried on, its velocity now, braked toward a stop (Locomotion.BrakingDeceleration,
        // as the motor brakes) when it stands on the ground with no move wished (the player has let go of the move keys).
        var velocity = Settings.CarryOn ? new Vector2(body.Velocity.X, body.Velocity.Y) : Vector2.Zero;
        float braking = 0f;
        if (Settings.CarryOn && body.State == BodyState.Ground && world.TryStore<MoveIntent>(out var intents) && intents.Has(who) && intents.Get(who).Direction.LengthSquared() < 1e-6f)
            braking = world.TryStore<Bodies.Locomotion>(out var locos) && locos.Has(who) ? locos.Get(who).BrakingDeceleration : Bodies.Locomotion.Sandbox.BrakingDeceleration;
        // A blow being swung keeps the part its blade has already swept (WholeBlow): its window from its start to now, posed
        // at those clip times with the body as it is now, in its own frame. The blade now follows them.
        if (swinging && Settings.WholeBlow)
        {
            var here = kind.Placement(Feet, Yaw);
            float step = MathF.Max(1e-4f, (anim.ActionRate > 0f ? anim.ActionRate : 1f) * dt);
            float from = MathF.Max(0f, window.From - step);
            int pastMost = Math.Min(MaxPlaces / 2, (int)MathF.Ceiling((anim.ActionTime - from) / step));
            for (int p = 0; p < pastMost; p++)
            {
                float t = from + p * step;
                if (t >= anim.ActionTime - step * 0.5f) break;
                feetOf[count] = Feet;
                if (!AddAt(assets, kind, config, anim, hasRig, rig, here, t)) break;
            }
            Past = count;
        }
        var feetAt = Feet;
        var feetBefore = Feet;
        float busy = c.Busy;
        bool begun = swinging;
        float twistStep = guard.GuardDegreesPerSecond * DetMath.DegToRad * dt;
        float sideStep = guard.GuardDegreesPerSecond / MathF.Max(1f, guard.GuardSideDegrees) * dt;
        float heightStep = guard.GuardDegreesPerSecond / MathF.Max(1f, guard.GuardHeightSpanDegrees) * dt;
        int most = (int)MathF.Ceiling(Settings.MostSeconds * World.TicksPerSecond);
        var previous = s;
        var previousRig = rig;
        bool had = false;
        for (int k = 1; k <= most; k++)
        {
            (previous, previousRig) = (s, rig);
            // The body moved on a tick (the motor: the speed changed, then the feet moved by it).
            if (braking > 0f) velocity = Brake(velocity, braking * dt);
            feetBefore = feetAt;
            feetAt += new Vector3(velocity.X, velocity.Y, 0f) * dt;
            // Act: the busy time runs down and the blow begins as it ends (a release now, or the buffered press); the guard's turn
            // comes back while it is not raised (AttackSystem.Direct).
            if (!begun)
            {
                busy = MathF.Max(0f, busy - dt);
                if (busy <= 0f)
                {
                    ActionChannel.Start(ref s, motion, role, rateScale: tempo.SwingRate);
                    begun = true;
                }
            }
            if (guard.Enabled)
            {
                if (s.GuardTwist != 0f) s.GuardTwist = GuardAim.Towards(s.GuardTwist, 0f, twistStep);
                if (s.GuardSide != 0f) s.GuardSide = GuardAim.Towards(s.GuardSide, 0f, sideStep);
                if (s.GuardHeight != 0f) s.GuardHeight = GuardAim.Towards(s.GuardHeight, 0f, heightStep);
            }
            // Move: the locomotion and the action channel advanced (AnimationSystem.Step, the motion kept), then the aim's bend eased.
            SwingArcCore.StepTick(assets, Channel, aimSettings, set.Spine.Length > 0, standing, fadeIn, ref s, ref rig, dt);
            if (!begun) continue;
            // Contact: swept while the clip's time this tick overlaps the window, from last tick's place to this tick's.
            if (!window.Overlaps(s.ActionLast, s.ActionTime))
            {
                if (had || s.ActionTime > window.To || s.ActionEnding) break;
                continue;
            }
            var place = kind.Placement(feetAt, Yaw);
            if (!had)
            {
                var placeBefore = kind.Placement(feetBefore, Yaw);
                feetOf[count] = feetBefore;
                if (!Add(assets, kind, config, previous, hasRig, previousRig, placeBefore, previous.ActionTime)) { Clear(); return false; }
                Lead = k - 1;
                had = true;
            }
            for (int sIx = 1; sIx < subSteps; sIx++)
            {
                var sub = s;
                sub.ActionTime = s.ActionLast + (s.ActionTime - s.ActionLast) * sIx / subSteps;
                if (count < MaxPlaces) feetOf[count] = feetAt;
                Add(assets, kind, config, sub, hasRig, rig, place, sub.ActionTime);
            }
            if (count < MaxPlaces) feetOf[count] = feetAt;
            if (!Add(assets, kind, config, s, hasRig, rig, place, s.ActionTime)) break;
        }
        if (count - Past < 2) { Clear(); return false; }

        // The level stops a blade (contact's rule: the tip and the middle as moving points). the arc no longer ends there
        // (the whole front path is shown): where it would first meet it is kept (LevelAt), and the rest is drawn faint, as past
        // a person. The part already swept is not searched (a blade that met the level would have stopped).
        EndsAtLevel = false;
        LevelAt = -1f;
        if (world.TryService<Space.LevelSpace>(out var level))
            for (int i = Past + 1; i < count; i++)
            {
                var (b0, b1) = (places[i - 1].Blade, places[i].Blade);
                bool tipHit = ContactMath.Moving(b0.Tip, b1.Tip, 0f, level.Collision, out var lt, out _);
                bool midHit = ContactMath.Moving((b0.Guard + b0.Tip) * 0.5f, (b1.Guard + b1.Tip) * 0.5f, 0f, level.Collision, out var lm, out _);
                if (!tipHit && !midHit) continue;
                float at = tipHit && (!midHit || lt.Time <= lm.Time) ? lt.Time : lm.Time;
                LevelAt = i - 1 + at;
                EndsAtLevel = true;
                break;
            }

        // Who it would meet first: the people near, their shields and hit volumes standing as they are now (contact's sweep against
        // each, the broad capsule first); the rules decide what the meeting does, so the arc goes on past it, dimmed.
        FirstMeeting(world, who, config);

        // The part shown: the places whose tip is before the body (within FrontDegrees of its facing, about the feet).
        float limit = Settings.FrontDegrees * DetMath.DegToRad;
        for (int i = 0; i < count; i++)
        {
            ref var p = ref places[i];
            var d = p.Blade.Tip - feetOf[i];
            bool front = d.X * d.X + d.Y * d.Y > 1e-6f && MathF.Abs(DetMath.WrapAngle(DetMath.Atan2(d.Y, d.X) - Yaw)) <= limit;
            p = p with { Front = front };
        }
        // One crescent: the run of shown places that holds the strike mark (or comes nearest it; the longest at a tie). A blade
        // that comes round in front again after its window's main stroke (the forehand's end, high on the left) is left out: a
        // patch apart from the stroke read as something else (seen in the pictures).
        int bestFrom = -1, bestTo = -1;
        float bestGap = float.PositiveInfinity;
        for (int i = 0; i < count; )
        {
            if (!places[i].Front) { i++; continue; }
            int j = i;
            while (j + 1 < count && places[j + 1].Front) j++;
            float gap = Strike < places[i].ClipTime ? places[i].ClipTime - Strike : Strike > places[j].ClipTime ? Strike - places[j].ClipTime : 0f;
            if (gap < bestGap || (gap == bestGap && j - i > bestTo - bestFrom)) (bestGap, bestFrom, bestTo) = (gap, i, j);
            i = j + 1;
        }
        for (int i = 0; i < count; i++)
            if (places[i].Front && (i < bestFrom || i > bestTo)) places[i] = places[i] with { Front = false };
        return true;
    }

    EntityId[] near = new EntityId[16];
    HitVolume[] future = new HitVolume[32];
    Matrix4x4[] futureModel = [];

    /// <summary>
    /// Where the predicted blade would first meet someone (<see cref="MeetsAt"/>): each person near, their shield and their hit
    /// volumes swept by the blade from place to place as contact sweeps them (the broad capsule first). Two passes: the people as
    /// they stand now, carried on by their velocity; then each one met posed again at the tick of that meeting — their own motion
    /// played on to it (their locomotion and action channel stepped as the systems step them), so an arm swaying in an idle or a
    /// walking step is where it will be — and the sweep made again against that pose. The rules decide what a meeting does.
    /// </summary>
    void FirstMeeting(World world, EntityId who, ContactConfig config)
    {
        if (!world.TryService<HitVolumes>(out var volumes) || !world.TryService<CharacterAssets>(out var assets)) return;
        int n = 0;
        float reach = config.ReachAtMost + 1.5f;
        var bodies = world.Store<Body>();
        var ents = bodies.Entities;
        var anims = world.Store<CharacterAnimation>();
        for (int i = 0; i < ents.Length; i++)
        {
            var t = ents[i];
            if (t == who || !anims.Has(t) || ActionRoles.IsDeath(anims.Get(t).Action)) continue;
            var d = bodies.Data[i].Feet - Feet;
            if (d.X * d.X + d.Y * d.Y > reach * reach) continue;
            if (n == near.Length) Array.Resize(ref near, n * 2);
            near[n++] = t;
        }
        if (n == 0) return;
        // Pass 1: as they stand now, carried on by their velocity.
        float at = Meet(world, volumes, assets, n, -1, default);
        if (at < 0f) return;
        // Pass 2: the one met first, posed at that tick, and the sweep again (it, and the others as they were).
        var first = Meets;
        int tick = Lead + (int)MathF.Ceiling(at - Past);
        if (Settings.PoseTheMet && Posed(world, volumes, assets, first, tick, out var count2)) at = Meet(world, volumes, assets, n, count2, first);
        MeetsAt = at;
    }

    /// <summary>
    /// The sweep of the predicted places against the near people (their shields and volumes), as they stand now and carried on by
    /// their velocity — or, for <paramref name="posedOf"/>, its volumes posed as they will be at the first pass's meeting
    /// (<see cref="future"/>, <paramref name="posedCount"/> of them), carried on the same. Sets <see cref="Meets"/> and the rest; answers the place index and share of the
    /// first meeting, or −1.
    /// </summary>
    float Meet(World world, HitVolumes volumes, CharacterAssets assets, int n, int posedCount, EntityId posedOf)
    {
        (Meets, MeetsRegion, MeetsShield, MeetsPoint) = (EntityId.None, BodyRegion.None, false, default);
        var bodies = world.Store<Body>();
        float dt = World.TickSeconds;
        // Each person's shield, broad capsule and volumes taken once (the posed one's from its future pose).
        if (people.Length < n) Array.Resize(ref people, n);
        int volumeCount = 0;
        for (int k = 0; k < n; k++)
        {
            var t = near[k];
            ref var tb = ref bodies.Get(t);
            ref var who = ref people[k];
            who.Velocity = new Vector3(tb.Velocity.X, tb.Velocity.Y, 0f);
            who.HasShield = HeldItems.Shield(world, t, out who.Shield);
            who.HasBroad = volumes.Broad(world, t, out who.Broad);
            var posed = t == posedOf && posedCount > 0 ? future.AsSpan(0, posedCount) : volumes.Posed(world, t);
            uint missing = volumes.Missing(world, t, out var collapse);
            who.From = volumeCount;
            for (int vI = 0; vI < posed.Length; vI++)
            {
                if (missing != 0 && collapse is not null && collapse.VolumeGone(missing, posed[vI].Region, posed[vI].Bone)) continue;
                if (volumeCount == nearVolumes.Length) Array.Resize(ref nearVolumes, volumeCount * 2);
                nearVolumes[volumeCount++] = posed[vI];
            }
            who.Count = volumeCount - who.From;
        }
        // From the blade now (the part already swept is not searched) to where it would meet the level (a person beyond a
        // wall is not met: the blade stops at the wall).
        int last = LevelAt >= 0f ? Math.Min(count - 1, (int)MathF.Floor(LevelAt) + 1) : count - 1;
        for (int i = Past + 1; i <= last; i++)
        {
            var (b0, b1) = (places[i - 1].Blade, places[i].Blade);
            float best = 2f;
            int tickHere = Lead + (i - Past);
            // The blade's sweep's box (its four ends and its radius): a volume whose own box misses it is not swept.
            var lo = Vector3.Min(Vector3.Min(b0.Guard, b0.Tip), Vector3.Min(b1.Guard, b1.Tip)) - new Vector3(b1.Radius);
            var hi = Vector3.Max(Vector3.Max(b0.Guard, b0.Tip), Vector3.Max(b1.Guard, b1.Tip)) + new Vector3(b1.Radius);
            for (int k = 0; k < n; k++)
            {
                var t = near[k];
                ref var who = ref people[k];
                // Their place carried on by their velocity, by the ticks from now to this place's.
                var shift = who.Velocity * (tickHere * dt);
                var shift0 = shift - who.Velocity * dt;
                if (who.HasShield)
                {
                    var sb = who.Shield;
                    var box0 = new OrientedBox(sb.Centre + shift0, sb.AxisX, sb.AxisY, sb.AxisZ, sb.HalfSize);
                    var box1 = new OrientedBox(sb.Centre + shift, sb.AxisX, sb.AxisY, sb.AxisZ, sb.HalfSize);
                    if (ContactMath.Sweep(b0.Guard, b0.Tip, b1.Guard, b1.Tip, b1.Radius, box0, box1, out var sh) && sh.Time < best)
                        (best, Meets, MeetsRegion, MeetsShield, MeetsPoint) = (sh.Time, t, BodyRegion.None, true, sh.Point);
                }
                if (!who.HasBroad) continue;
                var broad1 = new OrientedCapsule(who.Broad.A + shift, who.Broad.B + shift, who.Broad.Radius);
                var broad0 = new OrientedCapsule(who.Broad.A + shift0, who.Broad.B + shift0, who.Broad.Radius);
                if (!ContactMath.Sweep(b0.Guard, b0.Tip, b1.Guard, b1.Tip, b1.Radius, broad0, broad1, out _)) continue;
                bool still = who.Velocity == Vector3.Zero;
                for (int vI = who.From; vI < who.From + who.Count; vI++)
                {
                    var c = nearVolumes[vI].Capsule;
                    var cLo = Vector3.Min(Vector3.Min(c.A + shift0, c.B + shift0), Vector3.Min(c.A + shift, c.B + shift)) - new Vector3(c.Radius);
                    var cHi = Vector3.Max(Vector3.Max(c.A + shift0, c.B + shift0), Vector3.Max(c.A + shift, c.B + shift)) + new Vector3(c.Radius);
                    if (cLo.X > hi.X || cLo.Y > hi.Y || cLo.Z > hi.Z || cHi.X < lo.X || cHi.Y < lo.Y || cHi.Z < lo.Z) continue;
                    if (still)
                    {
                        if (ContactMath.Sweep(b0.Guard, b0.Tip, b1.Guard, b1.Tip, b1.Radius, c, c, out var hs) && hs.Time < best)
                            (best, Meets, MeetsRegion, MeetsShield, MeetsPoint) = (hs.Time, t, nearVolumes[vI].Region, false, hs.Point);
                        continue;
                    }
                    var c0 = new OrientedCapsule(c.A + shift0, c.B + shift0, c.Radius);
                    var c1 = new OrientedCapsule(c.A + shift, c.B + shift, c.Radius);
                    if (ContactMath.Sweep(b0.Guard, b0.Tip, b1.Guard, b1.Tip, b1.Radius, c0, c1, out var h) && h.Time < best)
                        (best, Meets, MeetsRegion, MeetsShield, MeetsPoint) = (h.Time, t, nearVolumes[vI].Region, false, h.Point);
                }
            }
            if (best <= 1f)
            {
                if (LevelAt >= 0f && i - 1 + best > LevelAt) break;
                return i - 1 + best;
            }
        }
        (Meets, MeetsRegion, MeetsShield, MeetsPoint) = (EntityId.None, BodyRegion.None, false, default);
        return -1f;
    }

    struct Person
    {
        public Vector3 Velocity;
        public bool HasShield, HasBroad;
        public BoxPlacement Shield;
        public OrientedCapsule Broad;
        public int From, Count;
    }

    Person[] people = new Person[16];
    HitVolume[] nearVolumes = new HitVolume[64];

    /// <summary>
    /// <paramref name="t"/>'s volumes posed <paramref name="ticks"/> from now into <see cref="future"/>: its animation stepped on as
    /// the systems step it (the locomotion and the action channel, its motion and rig kept), posed by the evaluator, stood where it
    /// stands now (<see cref="Meet"/> carries it on by its velocity).
    /// </summary>
    bool Posed(World world, HitVolumes volumes, CharacterAssets assets, EntityId t, int ticks, out int count2)
    {
        count2 = 0;
        if (volumes.Fitted(world, t) is not { } fitted) return false;
        var s = world.Get<CharacterAnimation>(t);
        var kind = assets.Kind(s.Kind);
        bool standing = s.Role is MotionRole.Idle or MotionRole.Crouch or MotionRole.Land;
        float dt = World.TickSeconds;
        for (int k = 0; k < ticks; k++)
        {
            SwingArcCore.Locomotion(assets, ref s, dt);
            ActionChannel.Advance(ref s, assets, Channel, standing, dt);   // (its rig and aim as they are)
        }
        int bones = assets.ModelOf(s.Kind).Skeleton.Count;
        if (futureModel.Length < bones) futureModel = new Matrix4x4[bones];
        // Posed by the core (its clips' samples kept; the legs as the motion has them: their IK, the costliest step, moves only the
        // legs a little on level ground — the meeting's part agreed with the blade's in every survey blow).
        bool hasRig = world.TryStore<PoseRig>(out var rigs) && rigs.Has(t);
        Core.PoseAll(assets, s, hasRig, hasRig ? rigs!.Get(t) : default, futureModel.AsSpan(0, bones));
        if (future.Length < fitted.Volumes.Length) future = new HitVolume[fitted.Volumes.Length];
        ref var b = ref world.Store<Body>().Get(t);
        HitVolumes.Pose(fitted, futureModel.AsSpan(0, bones), kind.Placement(b.Feet, b.Yaw), kind.Scale, future);
        count2 = fitted.Volumes.Length;
        return true;
    }

    /// <summary>The blade of a body in <paramref name="state"/> posed with <paramref name="rig"/> and placed at <paramref name="place"/>, kept (<see cref="SwingArcCore.BladeAt"/>).</summary>
    bool Add(CharacterAssets assets, CharacterKind kind, ContactConfig config, in CharacterAnimation state, bool hasRig, in PoseRig rig, in Matrix4x4 place, float clipTime)
    {
        if (count >= MaxPlaces || !Core.BladeAt(assets, config.HandRadius, state, hasRig, rig, place, out var blade)) return false;
        places[count++] = new SwingArcPlace(blade, clipTime, false);
        return true;
    }

    /// <summary>The blade of a body in <paramref name="state"/> with its action at <paramref name="clipTime"/> (the core's second form), kept.</summary>
    bool AddAt(CharacterAssets assets, CharacterKind kind, ContactConfig config, in CharacterAnimation state, bool hasRig, in PoseRig rig, in Matrix4x4 place, float clipTime)
    {
        if (count >= MaxPlaces || !Core.BladeAt(assets, config.HandRadius, state, hasRig, rig, place, clipTime, out var blade)) return false;
        places[count++] = new SwingArcPlace(blade, clipTime, false);
        return true;
    }

    /// <summary>A ground speed braked toward a stop by <paramref name="step"/> m/s (the motor's MoveToward to nothing).</summary>
    static Vector2 Brake(Vector2 v, float step)
    {
        float l = v.Length();
        return l <= step || l < 1e-6f ? Vector2.Zero : v * ((l - step) / l);
    }

    static float Towards(float value, float target, float step) =>
        value < target ? MathF.Min(target, value + step) : MathF.Max(target, value - step);

    /// <summary>
    /// The ribbon to draw: for each run of shown places, a quad between each two from the blade's
    /// <see cref="SwingArcSettings.RibbonFrom"/> point to its tip, its brightness highest at the strike mark and falling to
    /// <see cref="SwingArcSettings.EndBrightness"/> at the run's ends (and to nothing at its first and last place), stood on
    /// the drawn body: moved by <paramref name="drawnFeet"/> less the predicted feet and turned about them by
    /// <paramref name="drawnYaw"/> less the predicted yaw (the picture draws the body between ticks). <paramref name="into"/> is
    /// cleared first; a list grown once is not grown again.
    /// </summary>
    public void Fill(Vector3 drawnFeet, float drawnYaw, List<SwingArcQuad> into)
    {
        into.Clear();
        if (count < 2 || Blow == SwingArcBlow.None) return;
        var (sin, cos) = DetMath.SinCos(DetMath.WrapAngle(drawnYaw - Yaw));
        float from = Math.Clamp(Settings.RibbonFrom, 0f, 1f);
        float least = Math.Clamp(Settings.EndBrightness, 0f, 1f);
        // The run's extent either side of the strike mark, for the brightness's fall.
        float early = float.PositiveInfinity, late = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
            if (places[i].Front) { early = MathF.Min(early, places[i].ClipTime); late = MathF.Max(late, places[i].ClipTime); }
        if (!float.IsFinite(early)) return;
        float before = MathF.Max(1e-4f, Strike - early), after = MathF.Max(1e-4f, late - Strike);
        float past = Math.Clamp(Settings.PastMeetingShare, 0f, 1f);
        // Faint past the first thing the blade would meet — a person (the amber mark) or the level, whichever is first.
        float cut = MeetsAt >= 0f && (LevelAt < 0f || MeetsAt <= LevelAt) ? MeetsAt : LevelAt;
        int meet = cut >= 0f ? (int)MathF.Floor(cut) : int.MaxValue;
        int marked = MeetsAt >= 0f ? (int)MathF.Floor(MeetsAt) : int.MaxValue;
        for (int i = 1; i < count; i++)
        {
            ref readonly var a = ref places[i - 1];
            ref readonly var b = ref places[i];
            if (!a.Front || !b.Front) continue;
            float ba = Brightness(i - 1) * (i - 1 > meet ? past : 1f), bb = Brightness(i) * (i - 1 >= meet ? past : 1f);
            if (ba <= 0f && bb <= 0f) continue;
            if (i - 1 == meet)
            {
                // The quad the meeting is in: bright to the meeting, dim after it.
                float at = cut - meet;
                var m = Mix(a.Blade, b.Blade, at);
                float bm = ba + (Brightness(i) - ba) * at;
                into.Add(new SwingArcQuad(Stand(Vector3.Lerp(a.Blade.Guard, a.Blade.Tip, from)), Stand(a.Blade.Tip),
                                          Stand(Vector3.Lerp(m.Guard, m.Tip, from)), Stand(m.Tip), ba, bm));
                into.Add(new SwingArcQuad(Stand(Vector3.Lerp(m.Guard, m.Tip, from)), Stand(m.Tip),
                                          Stand(Vector3.Lerp(b.Blade.Guard, b.Blade.Tip, from)), Stand(b.Blade.Tip), bm * past, bb));
                continue;
            }
            into.Add(new SwingArcQuad(Stand(Vector3.Lerp(a.Blade.Guard, a.Blade.Tip, from)), Stand(a.Blade.Tip),
                                      Stand(Vector3.Lerp(b.Blade.Guard, b.Blade.Tip, from)), Stand(b.Blade.Tip), ba, bb));
        }
        // The mark where the blade would first meet someone: a short strip across the ribbon, the full length of the shown blade,
        // drawn whole (not fading inward), whether or not that place is in the part shown.
        if (MeetsAt >= 0f && marked + 1 < count)
        {
            var a = places[marked].Blade;
            var b = places[marked + 1].Blade;
            float at = MeetsAt - marked;
            var m = Mix(a, b, at);
            var along = b.Tip - a.Tip;
            float l = along.Length();
            if (l > 1e-5f)
            {
                var h = along / l * (Math.Max(0.005f, Settings.MarkLength) * 0.5f);
                var mid = Vector3.Lerp(m.Guard, m.Tip, from);
                into.Add(new SwingArcQuad(Stand(mid - h), Stand(m.Tip - h), Stand(mid + h), Stand(m.Tip + h), 1f, 1f, Mark: true));
            }
        }

        float Brightness(int i)
        {
            // Nothing at a run's first and last place, so the ribbon fades into its ends.
            if (i == 0 || i == count - 1 || !places[i - 1].Front || !places[i + 1].Front) return 0f;
            float t = places[i].ClipTime;
            float u = t < Strike ? (Strike - t) / before : (t - Strike) / after;
            u = Math.Clamp(u, 0f, 1f);
            return least + (1f - least) * (1f - u * u);
        }

        Vector3 Stand(Vector3 p)
        {
            var d = p - Feet;
            return drawnFeet + new Vector3(cos * d.X - sin * d.Y, sin * d.X + cos * d.Y, d.Z);
        }
    }
}
