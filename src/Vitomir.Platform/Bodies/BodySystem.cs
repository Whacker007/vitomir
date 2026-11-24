// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// The Move phase's bodies (ours): <see cref="BodyMotor.Step"/> over every entity with a <see cref="Body"/>,
/// a <see cref="Locomotion"/> and a <see cref="MoveIntent"/>, in the body store's order. Each body meets
/// the others where they stand now — those before it in the order where they have just moved to — so the
/// result depends only on the order, which is fixed; each capsule is the body's as it is now (crouched,
/// tucked in a move, or standing). A body with a <see cref="Traversal"/> makes the moves (the motor writes
/// it every tick); one without is the plain body. The intent's jump press is used up. What the motor
/// reports becomes the events <see cref="JumpAttempted"/>, <see cref="JumpTookOff"/>, <see cref="JumpApex"/>,
/// <see cref="Landed"/> (with its <see cref="LandingClass"/>), <see cref="TraversalStarted"/>,
/// <see cref="TraversalEnded"/>, <see cref="LedgeGrabbed"/>, <see cref="LedgeDropped"/>, and
/// <see cref="EdgeStopped"/> (once a stop: <see cref="Body.EdgeHold"/>) and <see cref="ClimbedDown"/>, and
/// <see cref="NoStandLanded"/> once a landing (<see cref="Body.NoStandHold"/>) and, for the body the player controls,
/// <see cref="PlayerStopped"/> once a stop (<see cref="Body.StopHold"/>). The capsules' buffer grows when the bodies do and
/// is otherwise kept, so a tick allocates nothing.
/// </summary>
public sealed class BodySystem : ISystem
{
    /// <summary>How long after the guard last held a body a guard met again is the same stop (s).</summary>
    public const float EdgeStopSeconds = 0.5f;

    /// <summary>
    /// How long after a body was last on a no-stand top a meeting with one coming down is the same landing (s): his
    /// walk logged one landing three ticks running.
    /// </summary>
    public const float NoStandSeconds = 0.25f;

    /// <summary>How long after the player was last stopped a stop met again is the same one (s).</summary>
    public const float StopSeconds = 0.5f;

    BodyCapsule[] capsules = new BodyCapsule[64];
    int[] slot = new int[64];
    World? lookedIn;
    Store<Controlled>? controlled;
    // The pace scale (BodyMotor.PaceScale.cs), when the world has one, and the masquerade's modes.
    Store<Powers.PaceScale>? paces;
    Store<MasqueradeState>? masks;
    Store<Dodging>? dodges;   // a body dodging is moved by the dodge's step (BodyMotor.Dodge.cs)
    Store<Animation.CharacterAnimation>? actions;   // the action channel, for the fighting pace
    Store<RushUnpaced>? unpaced;   // bodies that keep their own pace through their blows (rush-unpaced)
    Store<StartStopSpring>? springs;   // bodies that start and stop on the spring
    Store<Animation.People.PersonStep>? steps;   // people in a start or a stop (the models repository)
    Store<Animation.People.PersonAir>? airs;     // people whose jump waits for its clip's take-off (the models repository)
    Animation.People.PersonAirSettings? airRules;
    Store<Animation.People.PersonOver>? overs;   // people whose moves over and onto things are carried by the models repository's clips
    Animation.CharacterAssets? overAssets;
    bool overAuto;                               // the run's own vault on the clips (PersonOverSettings.Auto)
    Store<Animation.People.PersonLedge>? ledges; // people (the player) whose ledge is carried by the models repository's clips
    Animation.CharacterAssets? ledgeAssets;
    Store<Animation.People.PersonCommit>? commits;   // people (the player) with the commit point in the air
    AirCommitRules? commitRules;
    Store<Animation.People.PersonLadder>? climbs;   // people (the player) whose ladders are climbed on the models repository's clips
    MovesRules? movesRules;                          // tier 1's moves (the braced grabs, the low vaults, the ladders)
    Store<Animation.People.PersonHang>? hangs;       // people (the player) with the hang's corners and leaps
    Store<Animation.People.PersonWall>? walls;       // people (the player) who climb walls
    Store<Animation.People.PersonWallFlow>? flows;   // their flow
    Store<Animation.People.PersonStealth>? sides;    // people (the player) who squeeze, take cover and go in stealth
    Animation.People.PersonStealthSettings? stealthSettings;
    Animation.People.PersonGaitSettings? gaitRules;   // the player's own gaits (their paces, their stops by their clips)
    Animation.CharacterAssets? gaitAssets;
    Locomotion gaitHuman, gaited;
    SideRules? sideRules;
    Locomotion stealthy;
    StartStopSprings? springConfig;
    Modules.MovementProfiles? springProfiles;
    StartStopSpring noSpring;
    Traversal noMove;
    Locomotion paced, fighting;

    public void Run(World world)
    {
        var level = world.Service<LevelSpace>().Collision;
        var bodies = world.Store<Body>();
        var locos = world.Store<Locomotion>();
        var intents = world.Store<MoveIntent>();
        var moves = world.Store<Traversal>();
        var data = bodies.Data;
        var ents = bodies.Entities;
        if (capsules.Length < data.Length)
        {
            int size = Math.Max(data.Length, capsules.Length * 2);
            capsules = new BodyCapsule[size];
            slot = new int[size];
        }
        int n = 0;
        for (int i = 0; i < data.Length; i++)
        {
            slot[i] = -1;
            if (!locos.Has(ents[i])) continue;
            ref readonly var l = ref locos.Get(ents[i]);
            slot[i] = n;
            capsules[n++] = new BodyCapsule(ents[i].Packed, data[i].Feet, l.Radius, data[i].CapsuleHeight(l));
        }
        var attempted = world.Events<JumpAttempted>();
        var tookOff = world.Events<JumpTookOff>();
        var apex = world.Events<JumpApex>();
        var landed = world.Events<Landed>();
        var started = world.Events<TraversalStarted>();
        var ended = world.Events<TraversalEnded>();
        var grabbed = world.Events<LedgeGrabbed>();
        var dropped = world.Events<LedgeDropped>();
        var stopped = world.Events<EdgeStopped>();
        var climbed = world.Events<ClimbedDown>();
        var noStand = world.Events<NoStandLanded>();
        var halted = world.Events<PlayerStopped>();
        if (!ReferenceEquals(lookedIn, world))
        {
            // The player's body: the store of the control's tag, when the world has one (looked for once).
            lookedIn = world;
            controlled = null;
            paces = null;
            masks = null;
            dodges = null;
            actions = null;
            unpaced = null;
            springs = null;
            steps = null;
            airs = null;
            airRules = world.TryService<Animation.People.PersonAirSettings>(out var ar) && ar.Squash ? ar : null;
            overs = null;
            overAssets = world.TryService<Animation.People.PersonOverSettings>(out var ov) && ov.Any && world.TryService<Animation.CharacterAssets>(out var oa) ? oa : null;
            overAuto = overAssets is not null && ov!.Auto;
            ledges = null;
            ledgeAssets = world.TryService<Animation.People.PersonLedgeSettings>(out var lg) && lg.Any && world.TryService<Animation.CharacterAssets>(out var la) ? la : null;
            commits = null;
            commitRules = ledgeAssets is not null && world.TryService<Animation.People.PersonCommitSettings>(out var cm) && cm.On ? cm.Rules() : null;
            // Tier 1's moves, riding on the commit point (the braced twins are its catches'); the ladders' store only with their clips on.
            movesRules = commitRules is not null && world.TryService<Animation.People.PersonMovesSettings>(out var mv) && mv.On ? mv.Rules() : null;
            if (commitRules is not null && movesRules is { BracedGrabs: true }) commitRules = commitRules.WithTwins(true);
            climbs = null;
            // The hang's corners and leaps, riding on tier 1's moves.
            hangs = null;
            if (movesRules is not null && world.TryService<Animation.People.PersonHangSettings>(out var hg) && hg.On) movesRules = movesRules.WithHang(hg.Rules());
            // The walls, riding on the hang's.
            walls = null;
            flows = null;
            if (movesRules?.Hang is not null && world.TryService<Animation.People.PersonWallSettings>(out var wl) && wl.On) movesRules = movesRules.WithWall(wl.Rules(movesRules.Hang));
            // Squeezing, cover and stealth, riding on the walls.
            sides = null;
            stealthSettings = movesRules?.Wall is not null && world.TryService<Animation.People.PersonStealthSettings>(out var st) && st.On ? st : null;
            sideRules = stealthSettings?.Rules();
            gaitAssets = world.TryService<Animation.People.PersonGaitSettings>(out var pg) && pg.Player && world.TryService<Animation.CharacterAssets>(out var ga) ? ga : null;
            gaitRules = gaitAssets is not null ? pg : null;
            if (gaitRules is not null) gaitHuman = Animation.People.PlayerGaits.Human(world);
            springConfig = world.TryService<StartStopSprings>(out var sc) ? sc : null;
            springProfiles = world.TryService<Modules.MovementProfiles>(out var sp) ? sp : null;
            var stores = world.Stores;
            for (int k = 0; k < stores.Count; k++)
            {
                if (stores[k] is Store<Controlled> c) controlled = c;
                if (stores[k] is Store<Powers.PaceScale> ps) paces = ps;
                if (stores[k] is Store<MasqueradeState> ms) masks = ms;
                if (stores[k] is Store<Dodging> dg) dodges = dg;
                if (stores[k] is Store<Animation.CharacterAnimation> an) actions = an;
                if (stores[k] is Store<RushUnpaced> ru) unpaced = ru;   // rushing creatures keep their pace
                if (stores[k] is Store<StartStopSpring> ss && springConfig is not null) springs = ss;   // the spring
                if (stores[k] is Store<Animation.People.PersonStep> pst) steps = pst;                     // the starts and stops
                if (stores[k] is Store<Animation.People.PersonAir> pa && airRules is not null) airs = pa;  // the jump's squash
                if (stores[k] is Store<Animation.People.PersonOver> po && overAssets is not null) overs = po;   // the clip-carried moves
                if (stores[k] is Store<Animation.People.PersonLedge> pl && ledgeAssets is not null) ledges = pl;   // the ledge's clips
                if (stores[k] is Store<Animation.People.PersonCommit> pc && commitRules is not null) commits = pc;   // the commit point
                if (stores[k] is Store<Animation.People.PersonLadder> pd && movesRules is { Ladders: true }) climbs = pd;   // the ladders' clips
                if (stores[k] is Store<Animation.People.PersonHang> ph && movesRules?.Hang is not null) hangs = ph;   // the hang's corners and leaps
                if (stores[k] is Store<Animation.People.PersonWall> pw && movesRules?.Wall is not null) walls = pw;   // the walls
                if (stores[k] is Store<Animation.People.PersonWallFlow> pf && movesRules?.Wall is { Flows: true }) flows = pf;   // their flow
                if (stores[k] is Store<Animation.People.PersonStealth> pz && stealthSettings is not null) sides = pz;   // squeezing, cover, stealth
            }
        }
        // The human's numbers a pace scale multiplies (the world's human, else the platform's own).
        Locomotion human = paces is null ? default
            : world.TryService<Modules.MovementProfiles>(out var profiles) && profiles.HumanIndex >= 0
                ? profiles.Numbers(profiles.HumanIndex, MasqueradeMode.Unbound) : Locomotion.Sandbox;
        var surfaces = level.Surfaces;
        var materials = level.Materials;
        var byWay = world.TryService<DirectionPace>(out var dp) && dp.On ? dp : null;   // speed by direction
        bool landingSlide = springConfig?.LandingSlide ?? false;                // The earlier build's landings on the spring
        // The fighting pace — a body whose blow is swinging or held ready moves at most its cap (FightingPace.cs).
        var inFight = actions is not null && world.TryService<FightingPace>(out var fp) && fp.On ? fp : null;
        var drawnBows = inFight is not null && world.TryStore<Combat.Bow>(out var bowStore) ? bowStore : null;   // a drawn bow is a held blow
        for (int i = 0; i < data.Length; i++)
        {
            if (slot[i] < 0 || !intents.Has(ents[i])) continue;
            var e = ents[i];
            ref var body = ref data[i];
            ref readonly var loco = ref locos.Get(e);
            ref var intent = ref intents.Get(e);
            var others = capsules.AsSpan(0, n);
            var velocityBefore = body.Velocity;
            // A pace scale on a body not in the masquerade's mode (BodyMotor.PaceScale.cs); the charge keeps its walk.
            float factor = BodyMotor.PaceFactor(paces, masks, e);
            if (factor > 1f) paced = BodyMotor.Paced(loco, human, factor);
            ref readonly var moving = ref factor > 1f ? ref paced : ref loco;
            float chargeWalk = factor > 1f ? loco.WalkSpeed : 0f;
            // The player's own gaits at their clips' paces (by its numbers over the human's), before stealth's and the fight's caps.
            ref var stepOf = ref steps is null ? ref System.Runtime.CompilerServices.Unsafe.NullRef<Animation.People.PersonStep>() : ref steps.Find(e);
            if (gaitRules is not null && !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref stepOf) && actions is not null && actions.Has(e)
                && gaitAssets!.Kind(actions.Get(e).Kind).Person is { PlayerGaits: { } own } gaitPerson && Animation.People.PlayerGaits.Plays(gaitRules, gaitPerson, stepOf))
            {
                gaited = own.Paced(moving, gaitHuman);
                moving = ref gaited;
            }
            // A body in stealth at stealth's pace (the run and the sprint at it, the walk at 0.6 of it).
            ref var stealthOf = ref sides is null ? ref System.Runtime.CompilerServices.Unsafe.NullRef<Animation.People.PersonStealth>() : ref sides.Find(e);
            bool stealth = !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref stealthOf) && stealthOf.On != 0;
            if (stealth)
            {
                stealthy = stealthSettings!.Stealthy(moving);
                moving = ref stealthy;
            }
            MotorOutcome o;
            // A dodge under way moves the body by its own step (its start, burst and recovery).
            ref var dodge = ref dodges is null ? ref System.Runtime.CompilerServices.Unsafe.NullRef<Dodging>() : ref dodges.Find(e);
            bool dodging = !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref dodge) && dodge.Active;
            // A body on the spring — its half-life in force now (its profile's in its mode, a pace scale's); else the ramp.
            ref var spring = ref springs is null ? ref System.Runtime.CompilerServices.Unsafe.NullRef<StartStopSpring>() : ref springs.Find(e);
            bool sprung = !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref spring);
            if (sprung) spring.HalfLife = springConfig!.HalfLifeOf(masks, springProfiles, e, factor);
            else { noSpring = default; spring = ref noSpring; }
            // In a blow, on the ground, the gait asked becomes a walk at no more than the fighting pace (the charge keeps
            // its walk); a dodge is its own step, and a move under way (a vault, a climb, a ladder) and the air are the motor's.
            bool capped = false;
            if (inFight is not null && !dodging && body.State == BodyState.Ground && (!moves.Has(e) || moves.Get(e).Kind == TraversalKind.None)
                && !FightingPace.Unpaced(unpaced, e))   // a rushing creature keeps its own pace (rush-unpaced)
            {
                ref var act = ref actions!.Find(e);
                if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref act) && (FightingPace.InBlow(act) || FightingPace.BowDrawn(drawnBows, e)))
                {
                    capped = true;
                    if (chargeWalk <= 0f) chargeWalk = moving.WalkSpeed;
                    fighting = inFight.Capped(moving, intent.Gait, factor);
                    moving = ref fighting;
                }
            }
            // A person in a start or a stop (Animation.People.PeopleStarts) moves as its clip says: the player's capsule held at its speed while
            // its stop waits for the clip's phase (the spring's goal nought only when the stop begins); anyone else's moved by the clip's own speed.
            var given = intent;
            if (steps is not null && !dodging && body.State == BodyState.Ground)
            {
                ref var step = ref steps.Find(e);
                if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref step) && (step.Holds || step.Moves) && step.Way.LengthSquared() > 1e-6f)
                {
                    var g = step.Speed <= moving.WalkSpeed + 1e-3f ? Gait.Walk : step.Speed <= moving.RunSpeed + 1e-3f ? Gait.Run : Gait.Sprint;
                    float pace = moving.SpeedOf(g);
                    given.Gait = g;
                    given.Direction = step.Way * (pace > 1e-3f ? MathF.Min(1f, step.Speed / pace) : 0f);
                    if (step.Moves) { body.Velocity.X = step.Way.X * step.Speed; body.Velocity.Y = step.Way.Y * step.Speed; }   // the ramp then has nothing to take up
                    if (step.Moves && step.Curve == 3) spring.Acceleration = default;   // the player's own stop by its clip's speed; the spring rests
                }
            }
            // A person given the clip-carried moves (the models repository): its state and its clips; a press at a move over or onto a
            // thing is that move's (BodyMotor.OverPress: running at a vault, held for the run's footfall), not the squash's.
            ref var carried = ref System.Runtime.CompilerServices.Unsafe.NullRef<OverMove>();
            OverPaths? paths = null;
            bool overTook = false, autoVault = false;
            if (overs is not null)
            {
                ref var person = ref overs.Find(e);
                if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref person) && actions is not null && actions.Has(e))
                {
                    paths = overAssets!.Kind(actions.Get(e).Kind).Person?.Over?.Paths;
                    if (paths is not null)
                    {
                        carried = ref person.Move;
                        // The run's and the sprint's own vault on the clips, the player's body only (townsfolk don't vault).
                        autoVault = overAuto && controlled is not null && controlled.Has(e);
                        if (!dodging) overTook = BodyMotor.OverPress(ref carried, ref given, body, moving, level, paths, auto: autoVault,
                                                                     low: autoVault && movesRules is { LowVaults: true });   // the low vaults
                    }
                }
            }
            // A person given the ledge's clips (the models repository: the player): its state and its clips; a press below a lip, or running at one,
            // is a grab's (BodyMotor.LedgePress: running, held for the run's footfall), not the squash's.
            ref var hanging = ref System.Runtime.CompilerServices.Unsafe.NullRef<LedgeMove>();
            ref var committing = ref System.Runtime.CompilerServices.Unsafe.NullRef<AirCommit>();
            LedgePaths? ledgePaths = null;
            ref var climbing = ref System.Runtime.CompilerServices.Unsafe.NullRef<LadderClipMove>();
            LadderPaths? ladderPaths = null;
            ref var hangMove = ref System.Runtime.CompilerServices.Unsafe.NullRef<HangMove>();
            ref var wallMove = ref System.Runtime.CompilerServices.Unsafe.NullRef<WallMove>();
            ref var wallFlow = ref System.Runtime.CompilerServices.Unsafe.NullRef<WallFlow>();
            ref var sideMove = ref System.Runtime.CompilerServices.Unsafe.NullRef<SideMove>();
            bool sheer = false;
            if (ledges is not null)
            {
                ref var person = ref ledges.Find(e);
                if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref person) && actions is not null && actions.Has(e))
                {
                    ledgePaths = ledgeAssets!.Kind(actions.Get(e).Kind).Person?.Ledge?.Paths;
                    if (ledgePaths is not null)
                    {
                        hanging = ref person.Move;
                        if (!dodging && !overTook) overTook = BodyMotor.LedgePress(ref hanging, ref given, body, moving, level, ledgePaths);
                        if (commits is not null)
                        {
                            ref var c = ref commits.Find(e);
                            if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref c)) committing = ref c.Move;   // the commit point in the air
                        }
                        if (climbs is not null)
                        {
                            ref var d = ref climbs.Find(e);
                            ladderPaths = ledgeAssets!.Kind(actions.Get(e).Kind).Person?.Ladder?.Paths;
                            if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref d) && ladderPaths is not null) climbing = ref d.Move;   // the ladders' clips
                            else ladderPaths = null;
                        }
                        if (hangs is not null)
                        {
                            ref var hm = ref hangs.Find(e);
                            if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref hm)) hangMove = ref hm.Move;   // the hang's corners and leaps
                        }
                        if (walls is not null && !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref hangMove) && ledgePaths.HasWalls)
                        {
                            ref var wm = ref walls.Find(e);
                            if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref wm))
                            {
                                wallMove = ref wm.Move;   // the walls
                                if (flows is not null)
                                {
                                    ref var wf = ref flows.Find(e);
                                    if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref wf)) wallFlow = ref wf.Move;   // the flow
                                }
                                if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref stealthOf) && (ledgePaths.HasSqueeze || ledgePaths.HasCover)) sideMove = ref stealthOf.Move;
                                // A face with nothing to hold only out of the masquerade, or in a free place (the settled place: MasqueradeSystem's).
                                ref var mask = ref masks is null ? ref System.Runtime.CompilerServices.Unsafe.NullRef<MasqueradeState>() : ref masks.Find(e);
                                sheer = !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref mask) && (mask.Mode == MasqueradeMode.Unbound || mask.Place == Space.PlaceKind.Free);
                                // A press at a wall gets onto it (its own dip, not the jump's squash).
                                if (!dodging && !overTook && movesRules?.Wall is { } wr)
                                    overTook = BodyMotor.WallPress(given, body, moving, level, hanging, ledgePaths, wr, sheer);
                            }
                        }
                    }
                }
            }
            // A person's press of jump on the ground held back while its clip's squash runs (the player's jump leaves after 0.10 s), let
            // go as the clip's feet leave (Animation.People.PeopleAir.Hold); a body that charges keeps its own.
            if (airs is not null && !overTook)
            {
                ref var air = ref airs.Find(e);
                if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref air))
                    Animation.People.PeopleAir.Hold(ref air, ref given, body.State == BodyState.Ground && !dodging, moving.CanCharge);
            }
            if (dodging)
            {
                noMove = default;
                ref var move = ref moves.Has(e) ? ref moves.Get(e) : ref noMove;
                o = BodyMotor.DodgeStep(ref body, ref move, ref dodge, moving, intent, level, others, e.Packed, world.Tick, out bool over);
                spring.Acceleration = default;   // the dodge is its own step; the spring rests
                if (over) world.Events<DodgeEnded>().Publish(new DodgeEnded(e, world.Tick, body.Feet, dodge.Moved, dodge.Distance, Cut: false));
            }
            else if (moves.Has(e))
            {
                ref var move = ref moves.Get(e);
                // A body facing one way and moving another on the ground goes at its pace times DirectionPace's factor.
                var asked = given;
                if (byWay is not null && intent.Faces && body.State == BodyState.Ground && move.Kind == TraversalKind.None)
                    asked.Direction *= byWay.Factor(given.Direction, intent.Facing);
                if (capped) asked.Gait = Gait.Walk;
                bool tiered = !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref committing);   // tier 1's moves ride on the commit point (the player)
                o = BodyMotor.Step(ref body, ref move, ref spring, moving, asked, level, others, e.Packed, world.Tick, ref carried, paths, ref hanging, ledgePaths,
                                   ref committing, System.Runtime.CompilerServices.Unsafe.IsNullRef(ref committing) ? null : commitRules,   // the commit point
                                   ref climbing, ladderPaths, tiered ? movesRules : null,          // the ladders' clips, tier 1's moves
                                   ref hangMove,                                                    // the hang's corners and leaps
                                   ref wallMove, sheer,                                             // the walls
                                   ref wallFlow,                                                    // their flow
                                   ref sideMove, System.Runtime.CompilerServices.Unsafe.IsNullRef(ref sideMove) ? null : sideRules, stealth,   // squeezing, cover
                                   chargeWalk: chargeWalk, landingSlide: landingSlide,   // the clip-carried moves; the ledge's
                                   autoVault: autoVault);                                         // the run's own vault (the sprint vault leaves the clips' things to it)
                if (o.Committed != AirOutcome.None)
                    world.Events<AirCommitted>().Publish(new AirCommitted(e, world.Tick, o.Committed, o.CommitSeconds, o.CommitLip, o.CommitWay, o.CommitAuto, body.Feet, o.CommitWhy));
                if (o.MoveEnded != TraversalKind.None) ended.Publish(new TraversalEnded(e, world.Tick, o.MoveEnded, body.Feet, o.MoveCompleted));
                if (o.MoveStarted != TraversalKind.None) started.Publish(new TraversalStarted(e, world.Tick, move.Kind, move.From, move.To, move.Lip, move.Height));
                if (o.Grabbed) grabbed.Publish(new LedgeGrabbed(e, world.Tick, o.GripLip, o.GripFeet, o.GripHeight));
                if (o.Dropped) dropped.Publish(new LedgeDropped(e, world.Tick, o.GripLip, o.GripFeet));
            }
            else
            {
                var asked = given;    // as above, for a body without moves
                if (byWay is not null && intent.Faces && body.State == BodyState.Ground) asked.Direction *= byWay.Factor(given.Direction, intent.Facing);
                if (capped) asked.Gait = Gait.Walk;
                o = BodyMotor.Step(ref body, ref spring, moving, asked, level, others, e.Packed, world.Tick, chargeWalk, landingSlide);
            }
            JumpEvents.Publish(world, e, body, o);   // the jump tapped or charged, a charge's steps, a move refused
            intent.Jump = false;
            LadderEvents.Publish(world, e, o);       // on and off ladders, the shimmy
            if (o.HangStarted != HangMoveKind.None || o.HangRefused != HangRefusal.None) HangEvents.Publish(world, e, o);   // the hang's corners and leaps
            if (o.WallStarted != WallAct.None || o.WallRefused != WallRefusal.None) WallEvents.Publish(world, e, o);       // the walls
            if (o.SideStarted != SideAct.None || o.SideRefused != SideRefusal.None) SideEvents.Publish(world, e, o);       // squeezing, cover
            // Stealth's stance, down while in stealth standing or walking (not crouched, not in a move), up out of it, at the duck's times.
            if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref stealthOf))
            {
                bool low = stealth && body.State == BodyState.Ground && body.Crouched == 0 && (!moves.Has(e) || moves.Get(e).Kind == TraversalKind.None);
                Animation.People.PersonStealthRule.Ease(ref stealthOf, low, stealthSettings!);
            }
            intent.Use = false;
            capsules[slot[i]] = new BodyCapsule(e.Packed, body.Feet, loco.Radius, body.CapsuleHeight(loco));
            if (o.JumpAttempted) attempted.Publish(new JumpAttempted(e, world.Tick, body.Feet, body.Yaw, o.AttemptOnGround));
            if (o.TookOff) tookOff.Publish(new JumpTookOff(e, world.Tick, o.TakeOffFeet, o.TakeOffVelocity, o.TookOffCoyote, o.TookOffBuffered));
            if (o.Apex) apex.Publish(new JumpApex(e, world.Tick, o.ApexFeet, o.ApexHeight));
            // A stop at an edge, told once a stop (a guard again within EdgeStopSeconds is the same one).
            if (o.Guarded)
            {
                if (body.EdgeHold <= 0f)
                    stopped.Publish(new EdgeStopped(e, world.Tick, body.Feet, o.GuardedEdge, o.GuardedDrop, intent.Guard, BodyMotor.GuardLimit(intent, loco)));
                body.EdgeHold = EdgeStopSeconds;
            }
            else body.EdgeHold = MathF.Max(0f, body.EdgeHold - World.TickSeconds);
            if (o.ClimbedDown) climbed.Publish(new ClimbedDown(e, world.Tick, o.ClimbFrom, o.ClimbLip, body.Feet, o.ClimbDrop));
            // For the log; once a landing (the motor meets the top coming down on a tick or more of it).
            if (o.NoStandMet && body.NoStandHold <= 0f) noStand.Publish(new NoStandLanded(e, world.Tick, body.Feet, o.NoStandWay));
            body.NoStandHold = o.OnNoStand || o.NoStandMet ? NoStandSeconds : MathF.Max(0f, body.NoStandHold - World.TickSeconds);
            // What stopped the player, once a stop (BodyMotor.Stops.cs). not on the tick a move or a ladder
            // ends — the body stands there from rest while the player still pushes, which is no stop (a walk in play
            // logged one after each pull-up, climb down and step off a ladder).
            if (controlled is { } cs && cs.Has(e) && !dodging && !o.Guarded && o.MoveStarted == TraversalKind.None
                && o.MoveEnded == TraversalKind.None && !o.ClimbedDown && o.LadderOff == LadderLeave.None
                && (!moves.Has(e) || moves.Get(e).Kind == TraversalKind.None)
                && (sprung ? BodyMotor.StoppedAgainst(body, velocityBefore, intent, o.SpringAsked) : BodyMotor.StoppedAgainst(body, velocityBefore, intent)))   // the spring's first ticks are no stop
            {
                if (body.StopHold <= 0f)
                {
                    var way = intent.Direction;
                    var cause = BodyMotor.ReadStop(level, body.Feet, way, loco, out float height, out float depth, out var take, out var by);
                    halted.Publish(new PlayerStopped(e, world.Tick, body.Feet, Vector2.Normalize(way), intent.Gait, cause, height, depth, take, by));
                }
                body.StopHold = StopSeconds;
            }
            else body.StopHold = MathF.Max(0f, body.StopHold - World.TickSeconds);
            if (o.Landed)
            {
                int t = o.LandTriangle;
                bool known = t >= 0 && t < surfaces.Length;
                landed.Publish(new Landed(e, world.Tick, body.Feet, o.ImpactSpeed, o.FallHeight, o.AirSeconds, t,
                                          known ? materials[t] : (ushort)0, known ? surfaces[t] : 0, Landings.Of(o.FallHeight, loco)));
            }
        }
    }
}
