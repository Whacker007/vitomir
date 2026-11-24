// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Runtime.CompilerServices;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Animation;

/// <summary>
/// The numbers the <see cref="AnimationSystem"/> chooses by (ours; all tunable). The speeds bracket the
/// movement design's paces (3.9: walk 1.3, run 3.0 m/s).
/// </summary>
public sealed record AnimationSettings
{
    /// <summary>Below this ground speed (m/s) a character stands.</summary>
    public float IdleBelow { get; init; } = 0.2f;

    /// <summary>From this ground speed (m/s) a character runs rather than walks: midway between the walk and the run.</summary>
    public float RunFrom { get; init; } = 2.15f;

    /// <summary>The band (m/s) round each threshold that a change must cross, so a speed on a threshold does not flicker between two motions.</summary>
    public float Hysteresis { get; init; } = 0.1f;

    /// <summary>How long a change of motion fades: a fifth of a second, 1.1's own (five a second).</summary>
    public float FadeSeconds { get; init; } = 0.2f;

    /// <summary>The downward speed (m/s) at touch-down from which a character plays its hard landing, if it has one (about a 2.5 m fall).</summary>
    public float HardLanding { get; init; } = 7f;

    /// <summary>The fastest a motion plays, times its own rate: 1.1's clamp of 300 frames a second.</summary>
    public float MaxRate { get; init; } = 10f;

    /// <summary>
    /// The fastest a walk or run plays when its feet are matched to the ground (ours): a body faster than its
    /// motion was drawn for — a sprint on 1.1's run, drawn for about 2.9 m/s — lets its feet slide rather than
    /// pedal at nearly twice the cadence (as a walk showed). Till a sprint motion comes from the models
    /// repository (its people sprint on their own clip, <see cref="PersonSprintFrom"/>; 1.1's kinds as before).
    /// </summary>
    public float MostStrideRate { get; init; } = 1.35f;

    /// <summary>
    /// A walk or run plays backwards when the body moves more than about 105 degrees from its facing: its speed
    /// along the facing below this share of its speed over the ground, negated (ours).
    /// </summary>
    public float BackwardsShare { get; init; } = 0.25f;

    /// <summary>The action channel's fades.</summary>
    public ActionChannelSettings Actions { get; init; } = new();

    /// <summary>
    /// (<i>"park the kneeling crouch for now, and just stay with the 'slav squat' style on the balls of
    /// your feet"</i>): whether a person crouched and still settles into the models repository's kneel (<c>crouch_settle</c>,
    /// <c>crouch_kneel</c>, <c>crouch_rise</c>; <see cref="People.PeopleCrouch"/>). Off: the crouch held. <c>--crouch-kneel</c>, the
    /// setting <c>crouchKneel</c>; a tape carries it. Only the models repository's people have a kneel.
    /// </summary>
    public bool PersonKneel { get; init; }

    /// <summary>
    /// From this ground speed (m/s) a person running ahead plays the models repository's
    /// <c>sprint</c> (drawn for 5.5 m/s, stride-matched as the run) in place of the run's blend, which is capped at
    /// <see cref="MostStrideRate"/> and skates its feet at a sprint. 4.25: midway between the run (3.0) and the sprint (5.5), crossed past
    /// <see cref="Hysteresis"/>. Only ahead — within the body's sprint cone (<see cref="Bodies.BodyMotor.SprintCone"/>, 50°), the legs turned
    /// toward the way — sideways or back at that speed (Celerity's run) the run's blends as before. 0: the earlier build's run at every speed
    /// (<c>--sprint-clip-off</c>; the setting <c>sprintFrom</c>, <c>--sprint-from</c>; a tape carries it). Only the models repository's people have a
    /// sprint; a set without one (an older set) runs.
    /// </summary>
    public float PersonSprintFrom { get; init; } = 4.25f;
}

/// <summary>
/// Moves every character's animation on by one tick (at the end of <see cref="Phase.Move"/>, after the bodies have moved;
/// ours): from its <see cref="MotionInput"/> it chooses idle, walk or run by the ground speed (or the
/// jump, fall and landing roles in the air and at touch-down, when the kind has them), plays a walk or run
/// at the stride-matched rate (<see cref="Stride"/>: the feet keep pace with the ground, up to
/// <see cref="AnimationSettings.MostStrideRate"/>; backwards when the body moves backwards), fades a change
/// over <see cref="AnimationSettings.FadeSeconds"/>, and advances both motions of a fade. A character
/// without a <see cref="MotionInput"/> stands.
/// <para>
/// 1.1's rules kept: a motion already playing is not restarted when asked for again; a looping
/// motion wraps by whole durations and one that does not loop holds its end; a <c>nofadein</c> mode starts
/// without a fade; a <c>variable</c> mode's rate
/// is rolled once as it starts, 0.75× to 1.25× (from the world's <c>animation.variable</c>
/// stream). Ours: the stride matching; a walk changing to a run (or back) starts at the same point of its
/// cycle, so the feet carry on; the previous motion keeps playing through its fade; a character with no
/// motion for the air holds its pose there. the action channel moved on too (<see cref="ActionChannel.Advance"/>).
/// </para>
/// No allocation per tick.
/// </summary>
public sealed class AnimationSystem(CharacterAssets assets, AnimationSettings? settings = null) : ISystem
{
    /// <summary>The name it is registered under.</summary>
    public const string Name = "platform.animation";

    readonly AnimationSettings settings = settings ?? new AnimationSettings();
    People.PersonGroundSettings? stairRules;   // the stair clips' numbers (the world's), for their rate and step
    People.PersonStartSettings? startRules;    // the starts and stops (the world's), or null
    People.PersonGaitSettings? gaitRules;      // the player's gaits (the world's), or null
    bool gaited;                               // the one being stepped plays the player's gaits
    Bodies.Locomotion gaitHuman;               // the human's numbers the player's paces are by
    People.PersonAirSettings? airRules;        // the jumps, the fall and the landings (the world's), or null
    People.PersonOverSettings? overRules;      // over and onto things, off edges (the world's), or null
    People.PersonLedgeSettings? ledgeRules;    // the ledge (the world's), or null
    People.PersonPrelandingSettings? preRules; // the prelanding (the world's), or null
    People.PersonStealthSettings? duckRules;   // the duck's blends (the world's), or null
    Store<People.PersonStealth>? stealthStore; // who is in stealth (the sneak)
    bool sneaking;                             // the one being stepped is in stealth with the sneak on
    int intentsKnown, locosKnown, bodiesKnown;
    Rng? variable;
    int temposKnown;

    public AnimationSettings Settings => settings;

    public void Run(World world)
    {
        variable ??= world.Random.For("animation.variable");
        var animations = world.Store<CharacterAnimation>();
        var inputs = world.Store<MotionInput>();
        var tempos = PoseRigSystem.Optional<Combat.CombatTempo>(world, ref temposKnown);
        // A body slowed by its direction (DirectionPace: a backpedal at 0.7 of its pace) chooses its walk or
        // run by the pace it would have going ahead, so a backpedalling run (2.1 m/s) plays the run backwards, not a walk.
        var byWay = world.TryService<Bodies.DirectionPace>(out var dp) && dp.On ? dp : null;
        world.TryStore<People.PersonGround>(out var grounds);
        world.TryStore<People.PersonFlight>(out var flights);   // the stair clips' flights
        stairRules = world.TryService<People.PersonGroundSettings>(out var gs) ? gs : null;
        startRules = world.TryService<People.PersonStartSettings>(out var ps) && ps.Any ? ps : null;   // the starts and stops
        gaitRules = startRules is not null && world.TryService<People.PersonGaitSettings>(out var pg) && pg.Player ? pg : null;   // the player's gaits
        if (gaitRules is not null) gaitHuman = People.PlayerGaits.Human(world);
        world.TryStore<People.PersonStep>(out var stepStore);
        if (stepStore is not null) Wants(world, stepStore);
        // The jumps, the fall and the landings, for the people given them; the body's jump and fall read for them.
        world.TryStore<People.PersonAir>(out var airStore);
        airRules = airStore is not null && world.TryService<People.PersonAirSettings>(out var ar) && ar.Any ? ar : null;
        var airBodies = airRules is not null ? PoseRigSystem.Optional<Bodies.Body>(world, ref bodiesKnown) : null;
        var airLocos = airRules is not null ? PoseRigSystem.Optional<Bodies.Locomotion>(world, ref locosKnown) : null;
        // Over and onto things and off edges, for the people given them; the level read for the edges and the fall.
        world.TryStore<People.PersonOver>(out var overStore);
        overRules = airRules is not null && overStore is not null && world.TryService<People.PersonOverSettings>(out var or) && or.Any ? or : null;
        var overLevel = overRules is not null && world.TryService<Space.LevelSpace>(out var ls) ? ls.Collision : null;
        // The ledge, for the people given it (the player).
        world.TryStore<People.PersonLedge>(out var ledgeStore);
        ledgeRules = overRules is not null && ledgeStore is not null && world.TryService<People.PersonLedgeSettings>(out var lr) && lr.Any ? lr : null;
        // The commit point in the air (its catches entered at their matching phase), for the people given it (the player).
        var commitStore = ledgeRules is not null && world.TryStore<People.PersonCommit>(out var cs) ? cs : null;
        // The ladders on clips, for the people given them (the player), with tier 1's moves on.
        var ladderStore = ledgeRules is not null && world.TryService<People.PersonMovesSettings>(out var mv) && mv.On && mv.Ladders
                          && world.TryStore<People.PersonLadder>(out var ls2) ? ls2 : null;
        // The prelanding, for the people given the air's clips and the moves (the ground below foreseen by the level).
        preRules = overRules is not null && world.TryService<People.PersonPrelandingSettings>(out var pl) && pl.Any ? pl : null;
        duckRules = world.TryService<People.PersonStealthSettings>(out var dk) && dk.On ? dk : null;
        stealthStore = duckRules is { Sneak: true } && world.TryStore<People.PersonStealth>(out var sts) ? sts : null;
        var data = animations.Data;
        var entities = animations.Entities;
        for (int i = 0; i < data.Length; i++)
        {
            ref var input = ref inputs.Find(entities[i]);
            var motion = Unsafe.IsNullRef(ref input) ? MotionInput.Standing : input;
            // A block rises at the body's tempo (absent: 1.1's timing).
            float rise = tempos is not null && data[i].Action == ActionRole.Block ? Combat.CombatTempos.Of(tempos, entities[i]).RiseRate : 1f;
            // A fast blow (Celerity's tempo, above twice 1.1's rate) fades in faster by its rate over that, so its pose at a
            // clip time is a slower blow's and it meets what they meet (once the fade, in real time, left the arm low and the
            // blade met the chest); up to twice 1.1's rate, as before.
            if (tempos is not null && ActionRoles.IsAttack(data[i].Action))
            {
                float swing = Combat.CombatTempos.Of(tempos, entities[i]).SwingRate;
                if (swing > Combat.ContactSystem.SubStepTempo) rise = swing / Combat.ContactSystem.SubStepTempo;
            }
            float way = byWay is not null && motion.GroundSpeed > 1e-3f ? byWay.At(motion.ForwardSpeed / motion.GroundSpeed) : 1f;
            // A person's slope clip's share (People.PersonGround), for its stride.
            float slope = grounds is not null && grounds.Has(entities[i]) ? grounds.Get(entities[i]).Slope : 0f;
            var flight = flights is not null && flights.Has(entities[i]) ? flights.Get(entities[i]) : default;
            ref var personStep = ref stepStore is null ? ref Unsafe.NullRef<People.PersonStep>() : ref stepStore.Find(entities[i]);
            ref var personAir = ref airRules is null ? ref Unsafe.NullRef<People.PersonAir>() : ref airStore!.Find(entities[i]);
            var airBody = default(People.AirBody);
            if (!Unsafe.IsNullRef(ref personAir) && airBodies is not null && airBodies.Has(entities[i]))
            {
                ref readonly var body = ref airBodies.Get(entities[i]);
                float g = airLocos is not null && airLocos.Has(entities[i]) ? airLocos.Get(entities[i]).Gravity : 0f;
                airBody = new People.AirBody(body.Jumped != 0, body.JumpRise, g, body.LastFallHeight, body.HighestZ, body.AirSeconds);   // the air so far (the prelanding's cutoff)
            }
            ref var personOver = ref overRules is null ? ref Unsafe.NullRef<People.PersonOver>() : ref overStore!.Find(entities[i]);
            var off = default(People.OffWorld);
            if (!Unsafe.IsNullRef(ref personOver) && airBodies is not null && airBodies.Has(entities[i]) && airLocos is not null && airLocos.Has(entities[i]))
            {
                ref readonly var body = ref airBodies.Get(entities[i]);
                off = new People.OffWorld(overLevel, airLocos.Get(entities[i]), body.Feet, body.Velocity);
            }
            ref var personLedge = ref ledgeRules is null ? ref Unsafe.NullRef<People.PersonLedge>() : ref ledgeStore!.Find(entities[i]);
            ref var personCommit = ref commitStore is null || Unsafe.IsNullRef(ref personLedge) ? ref Unsafe.NullRef<People.PersonCommit>() : ref commitStore.Find(entities[i]);
            ref var personLadder = ref ladderStore is null || Unsafe.IsNullRef(ref personLedge) ? ref Unsafe.NullRef<People.PersonLadder>() : ref ladderStore.Find(entities[i]);
            sneaking = stealthStore is not null && stealthStore.Has(entities[i]) && stealthStore.Get(entities[i]).On != 0;
            Step(ref data[i], motion, World.TickSeconds, rise, way, slope, flight, ref personStep, ref personAir, airBody, ref personOver, off, ref personLedge, ref personCommit,
                 ref personLadder);
            sneaking = false;
        }
        if (world.TryService<CharacterPoses>(out var poses)) poses.Invalidate();
    }

    /// <summary>
    /// One character's step: choose its motion from how it moves, then advance it by <paramref name="seconds"/>;
    /// <paramref name="actionFadeIn"/> scales the action channel's fade-in (the block's rise rate; 1 as before);
    /// <paramref name="wayFactor"/> is the direction's share of the pace (<see cref="Bodies.DirectionPace"/>), the walk or
    /// the run chosen by the speed over it (1 as before). <paramref name="slope"/>: a person's slope clip's share
    /// (<see cref="People.PersonGround.Slope"/>), its stride laid over the gait's (0 as before).
    /// </summary>
    public void Step(ref CharacterAnimation a, in MotionInput input, float seconds, float actionFadeIn = 1f, float wayFactor = 1f, float slope = 0f) =>
        Step(ref a, input, seconds, actionFadeIn, wayFactor, slope, default);

    /// <summary>
    /// As the overload before, for a person on a flight of stairs (<paramref name="flight"/>, <see cref="People.PersonFlight"/>): the walk's or the
    /// run's stair clip laid over it by the flight's share, the stride's rate by the flight's tread (up to <see cref="People.PersonGroundSettings.StairMostRate"/>)
    /// and its time nudged so the clip's steps fall on the flight's treads (<see cref="People.PersonStairs.Lock"/>); the sprint not played on a flight
    /// (the run's stair clip is).
    /// </summary>
    public void Step(ref CharacterAnimation a, in MotionInput input, float seconds, float actionFadeIn, float wayFactor, float slope, in People.PersonFlight flight) =>
        Step(ref a, input, seconds, actionFadeIn, wayFactor, slope, flight, ref Unsafe.NullRef<People.PersonStep>());

    /// <summary>
    /// As the overload before, for a person given starts and stops (<paramref name="step"/>, <see cref="People.PersonStep"/>, a null reference for
    /// anyone else): its start and stop clips chosen and played by <see cref="People.PeopleStarts"/> before the gaits.
    /// </summary>
    public void Step(ref CharacterAnimation a, in MotionInput input, float seconds, float actionFadeIn, float wayFactor, float slope, in People.PersonFlight flight,
                     ref People.PersonStep step) =>
        Step(ref a, input, seconds, actionFadeIn, wayFactor, slope, flight, ref step, ref Unsafe.NullRef<People.PersonAir>(), default);

    /// <summary>
    /// As the overload before, for a person given the air's clips (<paramref name="air"/>, <see cref="People.PersonAir"/>, a null reference for anyone
    /// else; <paramref name="body"/>, what its body's jump and fall are): its jumps, fall and landings chosen and played by <see cref="People.PeopleAir"/>
    /// before the starts and the gaits.
    /// </summary>
    public void Step(ref CharacterAnimation a, in MotionInput input, float seconds, float actionFadeIn, float wayFactor, float slope, in People.PersonFlight flight,
                     ref People.PersonStep step, ref People.PersonAir air, in People.AirBody body) =>
        Step(ref a, input, seconds, actionFadeIn, wayFactor, slope, flight, ref step, ref air, body, ref Unsafe.NullRef<People.PersonOver>(), default);

    /// <summary>
    /// As the overload before, for a person given the moves over and onto things and off edges (<paramref name="over"/>, <see cref="People.PersonOver"/>,
    /// a null reference for anyone else; <paramref name="off"/>, the level and its body): a clip-carried move's clip by <see cref="People.PeopleOver"/>, and
    /// off an edge and the fitted air by <see cref="People.PeopleOff"/> ahead of the older air.
    /// </summary>
    public void Step(ref CharacterAnimation a, in MotionInput input, float seconds, float actionFadeIn, float wayFactor, float slope, in People.PersonFlight flight,
                     ref People.PersonStep step, ref People.PersonAir air, in People.AirBody body, ref People.PersonOver over, in People.OffWorld off) =>
        Step(ref a, input, seconds, actionFadeIn, wayFactor, slope, flight, ref step, ref air, body, ref over, off, ref Unsafe.NullRef<People.PersonLedge>());

    /// <summary>
    /// As the overload before, for a person given the ledge's clips (<paramref name="ledge"/>, <see cref="People.PersonLedge"/>, a null reference for anyone
    /// else): the clip the motor hangs, shimmies, pulls up, drops or grabs by (<see cref="People.PeopleLedge"/>), before the moves over and onto things.
    /// </summary>
    public void Step(ref CharacterAnimation a, in MotionInput input, float seconds, float actionFadeIn, float wayFactor, float slope, in People.PersonFlight flight,
                     ref People.PersonStep step, ref People.PersonAir air, in People.AirBody body, ref People.PersonOver over, in People.OffWorld off,
                     ref People.PersonLedge ledge) =>
        Step(ref a, input, seconds, actionFadeIn, wayFactor, slope, flight, ref step, ref air, body, ref over, off, ref ledge, ref Unsafe.NullRef<People.PersonCommit>());

    /// <summary>As the overload before, for a person with the commit point in the air (<paramref name="commit"/>, a null reference for anyone else): a catch's clip entered at its matching phase.</summary>
    public void Step(ref CharacterAnimation a, in MotionInput input, float seconds, float actionFadeIn, float wayFactor, float slope, in People.PersonFlight flight,
                     ref People.PersonStep step, ref People.PersonAir air, in People.AirBody body, ref People.PersonOver over, in People.OffWorld off,
                     ref People.PersonLedge ledge, ref People.PersonCommit commit) =>
        Step(ref a, input, seconds, actionFadeIn, wayFactor, slope, flight, ref step, ref air, body, ref over, off, ref ledge, ref commit, ref Unsafe.NullRef<People.PersonLadder>());

    /// <summary>As the overload before, for a person given the ladders' clips (<paramref name="ladder"/>, a null reference for anyone else): the clip the motor climbs by, before the ledge's.</summary>
    public void Step(ref CharacterAnimation a, in MotionInput input, float seconds, float actionFadeIn, float wayFactor, float slope, in People.PersonFlight flight,
                     ref People.PersonStep step, ref People.PersonAir air, in People.AirBody body, ref People.PersonOver over, in People.OffWorld off,
                     ref People.PersonLedge ledge, ref People.PersonCommit commit, ref People.PersonLadder ladder)
    {
        var kind = assets.Kind(a.Kind);
        // The player's own gaits, when the world has them on and the body is the player's (its start and stop on the spring).
        gaited = !Unsafe.IsNullRef(ref step) && People.PlayerGaits.Plays(gaitRules, kind.Person, step);
        if (gaited) step.Curve = 3;
        var want = Choose(in a, in input, kind, out bool hold, wayFactor);
        var (role, motion) = Resolve(kind, want);
        // A person (the models repository's bodies). Standing, its turn on the spot is the legs' turn's to play
        // (People.PeopleTurn) and is left as it is; walking or running, the gait's blend for the way it goes (People.Gait).
        var person = kind.Person;
        // A start or a stop (the models repository), when the person has one in hand; else (or leaving one) the gaits as before.
        bool stepping = false, steps = person?.Starts is not null && !Unsafe.IsNullRef(ref step) && step.Curve != 0 && startRules is not null;
        // A jump, the fall or a landing (the models repository), when the person has them in hand: before the starts and the gaits.
        bool airborne = person?.Air is not null && !Unsafe.IsNullRef(ref air) && airRules is not null;
        // A move over or onto a thing carried by its clip, off an edge and the air fitted to the fall: ahead of the older air.
        bool overs = airborne && person!.Over is not null && !Unsafe.IsNullRef(ref over) && overRules is not null;
        // The ladder's clip the motor climbs by (the models repository's clips): before the ledge's.
        if (overs && person!.Ladder is not null && !Unsafe.IsNullRef(ref ladder) && People.PeopleLadder.Choose(assets, person!, ref a, ref air, ref ladder, input))
        {
            if (steps) People.PeopleStarts.Clear(ref step);
            goto advance;
        }
        // The ledge's clip the motor carries the capsule by (the models repository's clips): before the moves over and onto things.
        if (overs && person!.Ledge is not null && !Unsafe.IsNullRef(ref ledge) && ledgeRules is not null
            && People.PeopleLedge.Choose(assets, person!, ref a, ref air, ref ledge, ref commit, input))
        {
            if (steps) People.PeopleStarts.Clear(ref step);
            goto advance;
        }
        if (overs && (People.PeopleOver.Choose(assets, person!, ref a, ref air, ref over, input, overRules!, settings)
                      || People.PeopleOff.Choose(assets, person!, ref a, ref air, ref over, input, body, overRules!, settings, seconds, off)))
        {
            if (steps) People.PeopleStarts.Clear(ref step);
            goto advance;
        }
        if (airborne && People.PeopleAir.Choose(assets, person!, ref a, ref air, input, body, airRules!, settings, seconds, kind.Motion(MotionRole.Idle).Clip,
                                                overs ? preRules : null, off))   // the prelanding, with the ground below
        {
            if (overs) People.PeopleOff.After(assets, person!, ref a, air);   // the player's standing high landing its own
            if (steps) People.PeopleStarts.Clear(ref step);
            goto advance;
        }
        if (steps && !hold)
            stepping = gaited ? People.PeopleStarts.ChoosePlayer(assets, person!, person!.PlayerGaits!, ref a, ref step, input, want, startRules!, gaitRules!, seconds)
                              : People.PeopleStarts.Choose(assets, person!, ref a, ref step, input, want, startRules!, settings.RunFrom, seconds, out _);
        if (stepping) goto advance;
        bool turning = person is not null && a.Role == MotionRole.Turn && !hold
                       && ((want == MotionRole.Idle && person.Turns?.Holds(a.Clip) == true) || (want == MotionRole.Crouch && person.Crouch?.Turns?.Holds(a.Clip) == true));
        // The models repository's crouch, when the set has it: which of its clips plays (People.PeopleCrouch), by the clock.
        bool byClock = false;
        if (!turning && person?.Crouch is { } crouch && People.PeopleCrouch.Choose(crouch, in a, want, assets, kind, out var crouchRole, out int crouchClip, settings.PersonKneel))
            (role, motion, byClock) = (crouchRole, motion with { Clip = crouchClip, StartSeconds = 0f, NaturalSpeed = 0f, Composed = false }, true);
        var gait = byClock ? null : person?.GaitOf(role);
        if (turning) { role = MotionRole.Turn; motion = motion with { Clip = a.Clip }; }
        else if (gait is not null) motion = motion with { Clip = gait.Clip(input.ForwardSpeed, input.SideSpeed, a.Clip) };
        // A person running ahead at a sprint plays the models repository's sprint, drawn for its own speed.
        bool climbing = flight.Share > 0.5f && person?.Stairs is not null;   // on a flight the run's stair clip, not the sprint
        bool sneaks = sneaking && !turning && !climbing && role is MotionRole.Walk or MotionRole.Run && gait is not null && person!.Sneak >= 0
                      && input.GroundSpeed <= 1.2f * person.SneakSpeed && input.ForwardSpeed >= Bodies.BodyMotor.SprintCone * input.GroundSpeed;
        // The player's own walk, run and sprint, ahead (within the sprint's cone, the legs turned toward the way), drawn for their own speeds.
        if (gaited && !sneaks && !turning && !climbing && role is MotionRole.Walk or MotionRole.Run && gait is not null
            && input.ForwardSpeed >= Bodies.BodyMotor.SprintCone * input.GroundSpeed && person!.PlayerGaits is { } own)
        {
            bool sprintable = own.Loops[2] >= 0 && settings.PersonSprintFrom > 0f;
            int g = role == MotionRole.Walk ? 0 : sprintable && Sprints(in a, in input, person, gaitRules!.SprintFrom) ? 2 : 1;
            // A gait asked is taken on the key (the speed following on the spring), so the body answers it at once; nothing asked, by the speed.
            if (step.Wanted >= startRules!.Asked)
            {
                g = own.GaitFor(step.Wanted, gaitRules!.RunFrom, gaitRules.SprintFrom);
                if (g == 2 && !sprintable) g = 1;
                role = g == 0 ? MotionRole.Walk : MotionRole.Run;
            }
            motion = motion with { Clip = own.Loops[g], NaturalSpeed = own.Speeds[g] };
        }
        else if (!turning && !climbing && role == MotionRole.Run && gait is not null && person!.Sprint >= 0 && Sprints(in a, in input, person))
            motion = motion with { Clip = person.Sprint, NaturalSpeed = person.SprintSpeed };
        // In stealth, walking ahead (within the sprint's cone, no faster than a fifth over its own speed) on the models repository's sneak.
        else if (sneaks)
            motion = motion with { Clip = person!.Sneak, NaturalSpeed = person.SneakSpeed };
        if (motion.Clip != a.Clip)
        {
            if (gait is not null && gait.Alike(a.Clip, motion.Clip))
            {
                // From one of the gait's blends to the next (a degree round) stepping the same way: no fade, the cycle carried
                // on at its share. From stepping ahead to backpedalling (or back) it fades as any change (Start).
                float from = assets.Clip(a.Clip).Duration;
                a.Time = from > 0f ? a.Time / from * assets.Clip(motion.Clip).Duration : 0f;
                a.Clip = motion.Clip;
                a.Role = role;
            }
            else Start(ref a, kind, role, motion);
        }
        else a.Role = role;

        if (turning) a.Rate = 0f;   // the legs' turn moves it on (PeopleTurn)
        else if (byClock) a.Rate = a.RateScale;   // a crouch's clip at its own rate
        else if (MotionSlots.IsTraversal(role) && a.Clip >= 0 && input.Traversal != Bodies.TraversalKind.None)
        {
            // A move's slot plays by the move's progress, whatever its motion's length.
            a.Rate = 0f;
            a.Time = Math.Clamp(input.TraversalProgress, 0f, 1f) * assets.Clip(a.Clip).Duration;
        }
        else if (role == MotionRole.Dodge && a.Clip >= 0)
        {
            // The dodge's motion by the dodge's progress (its lowest pose as the burst ends).
            a.Rate = 0f;
            a.Time = Math.Clamp(input.DodgeProgress, 0f, 1f) * assets.Clip(a.Clip).Duration;
        }
        else if (hold) a.Rate = 0f;
        else if (role is MotionRole.Walk or MotionRole.Run or MotionRole.CrouchWalk)
        {
            // A person's gait blend is drawn for its own speed (its stride at that angle), and goes back by its own clips.
            // A person drawn bigger or smaller than its base (a townsperson's height) strides by as much (the role's speed is already so).
            float natural = gait is not null && assets.Clip(a.Clip) is People.GaitBlendClip blend ? Natural(blend, person!, a.Clip, slope) * kind.Scale : motion.NaturalSpeed;
            // On a flight, the stair clip's stride on the flight's treads, and its rate allowed up to the stairs' most.
            float most = settings.MostStrideRate;
            // The player slowing into a gait asked (a sprint into the run, the run into the walk): that gait's quicker steps while the speed comes down to it,
            // its feet kept (the rate the speed needs), not its stride's cap.
            if (gaited && person!.PlayerGaits!.IsLoop(a.Clip) && step.Wanted >= (startRules?.Asked ?? 0.2f) && step.Wanted < input.GroundSpeed - 0.05f) most = settings.MaxRate;
            if (flight.Share > 0f && person?.Stairs is { } stairs && assets.Clip(a.Clip) is People.GaitBlendClip climb)
            {
                var (_, slot, w) = stairs.For(person, a.Clip, flight.Way, flight.Share);
                if (w > 0f)
                {
                    natural = stairs.NaturalSpeed(climb, natural / kind.Scale, slot, w, flight.Tread, kind.Scale) * kind.Scale;
                    most += (MathF.Max(most, (stairRules ?? StairDefaults).StairMostRate) - most) * w;
                }
            }
            a.Rate = Math.Clamp(Stride.RateFor(input.GroundSpeed, natural), 0f, MathF.Min(settings.MaxRate, most)) * a.RateScale;
            if (gait is null && input.ForwardSpeed < -settings.BackwardsShare * input.GroundSpeed) a.Rate = -a.Rate;
        }
        else a.Rate = a.RateScale;

    advance:
        float before = a.Time;
        a.Time = Advance(a.Clip, a.Time + seconds * a.Rate);
        if (steps && !stepping && step.Mode == People.StepMode.Waiting) People.PeopleStarts.Crossed(assets, person!, ref a, ref step, before, seconds);   // a stop's phase reached
        if (flight.Found != 0 && a.Rate > 0f && person?.Stairs is { } treads) Lock(ref a, person, treads, flight, seconds);   // the steps on the treads (from a flight's sight, so they are in step as its clip comes in)
        if (a.PreviousClip >= 0)
        {
            a.PreviousTime = Advance(a.PreviousClip, a.PreviousTime + seconds * a.PreviousRate);
            a.Blend = settings.FadeSeconds > 0f ? a.Blend + seconds / settings.FadeSeconds : 1f;
            // A start from, or a stop into, the stand's own pose fades only so long (People.PersonStarts.StandFade).
            if (steps && step.QuickFade > 0f && step.QuickFade != settings.FadeSeconds) a.Blend += seconds / step.QuickFade - seconds / settings.FadeSeconds;
            // A fade of the air's clips (into a jump, the fall, a landing) takes its own time.
            if (airborne && air.Fade > 0f && settings.FadeSeconds > 0f) a.Blend += seconds / air.Fade - seconds / settings.FadeSeconds;
            // The walk (or the run) into the crouch walk and back, phase-matched: its own times, the clip it leaves kept at the same point of its cycle.
            if (duckRules is not null && person is not null && settings.FadeSeconds > 0f && People.PersonStealthRule.Ducking(person, a, out bool into))
            {
                a.Blend += seconds / (into ? duckRules.In : duckRules.Out) - seconds / settings.FadeSeconds;
                float dc = assets.Clip(a.Clip).Duration, dp = assets.Clip(a.PreviousClip).Duration;
                if (dc > 0f && dp > 0f) a.PreviousTime = Advance(a.PreviousClip, a.Time / dc * dp);
            }
            if (a.Blend >= 1f) { a.Blend = 1f; a.PreviousClip = -1; a.PreviousTime = 0f; a.PreviousRate = 0f; }
        }
        if (airborne && air.Fade > 0f && a.PreviousClip < 0) air.Fade = 0f;
        if (steps && step.QuickFade > 0f && (a.PreviousClip < 0 || (step.Mode == People.StepMode.None && a.Role != MotionRole.Idle))) step.QuickFade = 0f;
        a.WasGrounded = input.Grounded;
        a.LastVerticalSpeed = input.VerticalSpeed;

        // The action channel, over the whole body while it stands and over the upper body while it moves.
        ActionChannel.Advance(ref a, assets, settings.Actions, a.Role is MotionRole.Idle or MotionRole.Crouch or MotionRole.Land, seconds, actionFadeIn);
    }

    /// <summary>The role a character's movement asks for; <paramref name="hold"/> when it should keep its pose (in the air with nothing to play there).</summary>
    MotionRole Choose(in CharacterAnimation a, in MotionInput input, CharacterKind kind, out bool hold, float wayFactor = 1f)
    {
        hold = false;
        float speed = input.GroundSpeed;
        if (input.Traversal != Bodies.TraversalKind.None) return MotionSlots.Of(input.Traversal);
        if (input.Dodging && input.Grounded && input.DodgeAside >= MotionSlots.DodgeMotionAside - 1e-3f && kind.Motion(MotionRole.Dodge).Has)
            return MotionRole.Dodge;   // 1.1's dodge (MotionSlots.DodgeMotionAside: every way)
        if (!input.Grounded)
        {
            if (kind.Motion(MotionRole.Jump).Has && ((a.WasGrounded && input.VerticalSpeed > 0f) || (a.Role == MotionRole.Jump && !Finished(in a))))
                return MotionRole.Jump;
            if (kind.Motion(MotionRole.Fall).Has) return MotionRole.Fall;
            if (a.Role == MotionRole.Jump) return MotionRole.Jump;      // its last frame, held
            hold = true;
            return a.Role;
        }
        bool hardLanding = !a.WasGrounded && -a.LastVerticalSpeed >= settings.HardLanding;
        if (kind.Motion(MotionRole.Land).Has && speed < settings.IdleBelow
            && (hardLanding || (a.Role == MotionRole.Land && !Finished(in a))))
            return MotionRole.Land;
        if (input.Crouched)
        {
            float band = settings.Hysteresis * 0.5f;
            return speed < settings.IdleBelow + (a.Role == MotionRole.CrouchWalk ? -band : band) ? MotionRole.Crouch : MotionRole.CrouchWalk;
        }
        // Walk or run by the pace ahead (the speed over the direction's factor); idle by the speed itself.
        if (wayFactor > 0f && wayFactor != 1f && speed >= settings.IdleBelow + settings.Hysteresis) speed /= wayFactor;
        return Locomotion(a.Role, speed, gaited ? gaitRules!.RunFrom : settings.RunFrom);   // the player's own gaits their own
    }

    static readonly People.PersonGroundSettings StairDefaults = new();

    /// <summary>
    /// Each person with a start or stop in hand (<see cref="People.PersonStep"/>) told what it is asked this tick: the intent's share of its gait's
    /// pace (none crouched), the way asked and the way its body went.
    /// </summary>
    void Wants(World world, Store<People.PersonStep> steps)
    {
        var intents = PoseRigSystem.Optional<Bodies.MoveIntent>(world, ref intentsKnown);
        var locos = PoseRigSystem.Optional<Bodies.Locomotion>(world, ref locosKnown);
        var bodies = PoseRigSystem.Optional<Bodies.Body>(world, ref bodiesKnown);
        var data = steps.Data;
        var ents = steps.Entities;
        for (int i = 0; i < data.Length; i++)
        {
            ref var st = ref data[i];
            float wanted = 0f;
            if (intents is not null && intents.Has(ents[i]))
            {
                ref var intent = ref intents.Get(ents[i]);
                var d = intent.Direction;
                float l = d.Length();
                if (l > 1f) { d /= l; l = 1f; }
                float pace = 0f;
                if (locos is not null && locos.Has(ents[i]))
                {
                    // The player's own gaits at their own paces.
                    var loco = locos.Get(ents[i]);
                    if (gaitRules is not null && world.TryStore<CharacterAnimation>(out var anims) && anims.Has(ents[i])
                        && People.PlayerGaits.Plays(gaitRules, assets.Kind(anims.Get(ents[i]).Kind).Person, st))
                        loco = assets.Kind(anims.Get(ents[i]).Kind).Person!.PlayerGaits!.Paced(loco, gaitHuman);
                    pace = loco.SpeedOf(intent.Gait);
                }
                wanted = intent.Crouch ? 0f : l * pace;
                if (l > 0.01f) st.WantWay = d / l;
            }
            float was = st.Wanted;
            st.Wanted = wanted;
            if (bodies is not null && bodies.Has(ents[i]))
            {
                var v = bodies.Get(ents[i]).Velocity;
                var h = new System.Numerics.Vector2(v.X, v.Y);
                if (h.LengthSquared() > 0.0025f) st.Going = System.Numerics.Vector2.Normalize(h);
                // The speed it went at while asked to go: last tick's when this tick asks nothing (the motor has already slowed it a tick).
                if (wanted > 0f) st.Cruise = h.Length();
                else if (was <= 0f) st.Cruise = h.Length();
            }
        }
    }

    /// <summary>
    /// A person's stride on a flight nudged into step with its treads: the share of the cycle its stair clip is off (<see cref="People.PersonStairs.Lock"/>),
    /// taken out over <see cref="People.PersonGroundSettings.LockSeconds"/>, at most <see cref="People.PersonGroundSettings.LockMost"/> of its rate a tick.
    /// </summary>
    void Lock(ref CharacterAnimation a, People.PersonMotion person, People.PersonStairs stairs, in People.PersonFlight flight, float seconds)
    {
        var (_, slot, w) = stairs.For(person, a.Clip, flight.Way, 1f);
        if (w <= 0f) return;
        var rules = stairRules ?? StairDefaults;
        float d = assets.Clip(a.Clip).Duration;
        if (d <= 0f) return;
        float p = a.Time / d;
        p -= MathF.Floor(p);
        float off = stairs.Lock(slot, p, flight.Phase) * d;
        float take = off * MathF.Min(1f, seconds / MathF.Max(1e-3f, rules.LockSeconds)), most = rules.LockMost * a.Rate * seconds;
        a.Time = Advance(a.Clip, a.Time + Math.Clamp(take, -most, most));
    }

    /// <summary>A gait blend's natural speed with the person's slope clip laid over it by <paramref name="slope"/> (its own without).</summary>
    float Natural(People.GaitBlendClip blend, People.PersonMotion person, int clip, float slope)
    {
        if (slope == 0f || person.Slopes is not { } slopes) return blend.NaturalSpeed;
        var (on, w) = slopes.For(person, clip, slope);
        return on >= 0 && assets.Clip(on) is Assets.INotedClip noted ? People.PersonSlopes.NaturalSpeed(blend, noted, w) : blend.NaturalSpeed;
    }

    /// <summary>
    /// Whether a person running plays its sprint: at <see cref="AnimationSettings.PersonSprintFrom"/> or faster (crossed past the
    /// band), going ahead within the body's sprint cone.
    /// </summary>
    bool Sprints(in CharacterAnimation a, in MotionInput input, People.PersonMotion person, float from = -1f)
    {
        if (from < 0f) from = settings.PersonSprintFrom;   // the player's own gaits give their own
        if (from <= 0f) return false;
        float speed = input.GroundSpeed, h = settings.Hysteresis * 0.5f;
        if (speed < from + (a.Clip == person.Sprint ? -h : h)) return false;
        return input.ForwardSpeed >= Bodies.BodyMotor.SprintCone * speed;
    }

    /// <summary>Idle, walk or run by the ground speed, each threshold crossed only past its band.</summary>
    MotionRole Locomotion(MotionRole current, float speed, float runFrom)
    {
        float h = settings.Hysteresis * 0.5f;
        bool moving = current is MotionRole.Walk or MotionRole.Run;
        if (speed < settings.IdleBelow + (moving ? -h : h)) return MotionRole.Idle;
        bool running = current == MotionRole.Run;
        return speed >= runFrom + (running ? -h : h) ? MotionRole.Run : MotionRole.Walk;
    }

    /// <summary>
    /// The motion a kind plays for a role, falling back from a walk to a run (and back) and then to the idle when it has
    /// not got one; a crouched walk without a motion to the walk, a crouch to the idle. A move's slot without a
    /// motion keeps its role (so the rig still places the limbs) over the idle's motion.
    /// </summary>
    static (MotionRole Role, RoleMotion Motion) Resolve(CharacterKind kind, MotionRole role)
    {
        var m = kind.Motion(role);
        if (m.Has) return (role, m);
        if (role == MotionRole.CrouchWalk) role = MotionRole.Walk;
        if (role == MotionRole.Crouch) return (MotionRole.Idle, kind.Motion(MotionRole.Idle));
        if (MotionSlots.IsTraversal(role)) return (role, kind.Motion(MotionRole.Idle));
        m = kind.Motion(role);
        if (m.Has) return (role, m);
        if (role == MotionRole.Walk && kind.Motion(MotionRole.Run).Has) return (MotionRole.Run, kind.Motion(MotionRole.Run));
        if (role == MotionRole.Run && kind.Motion(MotionRole.Walk).Has) return (MotionRole.Walk, kind.Motion(MotionRole.Walk));
        return (MotionRole.Idle, kind.Motion(MotionRole.Idle));
    }

    void Start(ref CharacterAnimation a, CharacterKind kind, MotionRole role, RoleMotion motion)
    {
        float time = motion.StartSeconds;
        bool cycle = (a.Role is MotionRole.Walk or MotionRole.Run or MotionRole.CrouchWalk) && (role is MotionRole.Walk or MotionRole.Run or MotionRole.CrouchWalk);
        if (cycle && a.Clip >= 0 && motion.Clip >= 0)
        {
            var from = assets.Clip(a.Clip);
            var to = assets.Clip(motion.Clip);
            if (from.Loops && to.Loops && from.Duration > 0f) time = a.Time / from.Duration * to.Duration;
        }
        if (a.Clip >= 0 && settings.FadeSeconds > 0f && !motion.NoFadeIn)
        {
            a.PreviousClip = a.Clip;
            a.PreviousTime = a.Time;
            a.PreviousRate = a.Rate;
            a.Blend = 0f;
        }
        else
        {
            a.PreviousClip = -1;
            a.Blend = 1f;
        }
        a.Role = role;
        a.Clip = motion.Clip;
        a.Time = time;
        a.RateScale = motion.Variable && variable is not null ? 1f + (variable.NextFloat() * 0.5f - 0.25f) : 1f;
    }

    bool Finished(in CharacterAnimation a) =>
        a.Clip < 0 || assets.Clip(a.Clip) is { Loops: false } c && a.Time >= c.Duration;

    /// <summary>A time moved on in a clip: wrapped by whole durations when it loops, held at its end when it does not.</summary>
    float Advance(int clip, float time)
    {
        if (clip < 0) return 0f;
        var c = assets.Clip(clip);
        float d = c.Duration;
        if (d <= 0f) return 0f;
        if (!c.Loops) return time > d ? d : time < 0f ? 0f : time;
        if (time >= d || time < 0f)       // both ways round: a walk played backwards wraps from its start to its end
        {
            time -= MathF.Floor(time / d) * d;
            if (time >= d) time -= d;
            if (time < 0f) time = 0f;
        }
        return time;
    }
}

/// <summary>Registering the platform's animation in a world.</summary>
public static class AnimationRegistration
{
    /// <summary>
    /// <b>Posing before contact</b>: the animation and the rig run at the end of
    /// <see cref="Phase.Move"/>, after the bodies have moved (<c>Move/100</c>), reached exits (110) and been put back from
    /// a fall (120), so a system in <see cref="Phase.Contact"/> that asks <see cref="CharacterPoses"/> gets this tick's
    /// pose on this tick's body: the blade a contact sweeps and the body it is placed on are one tick's pair. Until then
    /// they ran in <see cref="Phase.Post"/>, which left contact a tick behind. Each still runs once a tick, so the cost is
    /// where it was; nothing in Contact, Resolve or Perceive changes what they read, and Post's camera reads the same pose.
    /// </summary>
    public const Phase PosePhase = Phase.Move;

    /// <summary>The animation's place in <see cref="PosePhase"/> (the body's motion runs before it, the rig after).</summary>
    public const int AnimationOrder = 200;

    /// <summary>
    /// Adds the animation to a world being built: the <see cref="CharacterAnimation"/>, <see cref="MotionInput"/> and
    /// <see cref="PoseRig"/> components, the <see cref="CharacterAssets"/> and <see cref="CharacterPoses"/>
    /// services, the <see cref="AnimationSystem"/> in <see cref="PosePhase"/> at <see cref="AnimationOrder"/> plus
    /// <paramref name="order"/>, and the <see cref="PoseRigSystem"/> after it. the legs' turn between them
    /// (<see cref="LegTurnSystem"/>, its <see cref="LegTurnSettings"/> a service: <paramref name="legs"/>, or on by default). a world
    /// with people, the <see cref="People.PersonGroundSystem"/> before the animation (<paramref name="ground"/>, or on by default).
    /// </summary>
    public static WorldBuilder Animation(this WorldBuilder builder, CharacterAssets assets, AnimationSettings? settings = null, int order = 0,
                                         PoseRigSettings? rig = null, LegTurnSettings? legs = null, People.PersonFeetSettings? feet = null,
                                         People.PersonGroundSettings? ground = null, People.PersonStartSettings? starts = null,
                                         People.PersonAirSettings? air = null, People.PersonOverSettings? over = null, People.PersonLedgeSettings? ledge = null,
                                         People.PersonPrelandingSettings? prelanding = null, People.PersonCommitSettings? commit = null,
                                         People.PersonMovesSettings? moves = null, People.PersonHangSettings? hang = null,
                                         People.PersonWallSettings? wall = null, People.PersonStealthSettings? stealth = null,
                                         People.PersonGaitSettings? gaits = null, People.PersonPresentSettings? present = null)
    {
        var rigSettings = rig ?? new PoseRigSettings();
        var animation = settings ?? new AnimationSettings();
        // The legs' turn (LegTurn), its backwards rule the animation's own so the clip and the legs agree.
        var legTurn = legs ?? new LegTurnSettings { BackwardsShare = animation.BackwardsShare };
        // The people's turn as a chain keeps its state in a store of its own, only in a world with people and the chain
        // on (People.PersonTurn): 1.1's worlds (the arena) and the earlier build's turn have the stores and digests they had.
        bool people = false;
        for (int k = 0; k < assets.KindCount && !people; k++) people = assets.Kind(k).Person is not null;
        if (people && legTurn.On && legTurn.Chain) builder.Component<People.PersonTurn>();
        // The people's planted feet, heel lift and bend rule (People.PersonFeet; on by default), likewise only with people
        // and any of them on: 1.1's worlds and the earlier build's feet (--feet-off --bend-rule-off) have the stores and digests they had.
        var feetSettings = feet ?? new People.PersonFeetSettings();
        if (people && feetSettings.Any)
            builder.Component<People.PersonFeet>()
                   .System(PosePhase, AnimationOrder + order + People.PersonFeetSystem.Order, People.PersonFeetSystem.Name, new People.PersonFeetSystem(assets, feetSettings));
        // The people on stairs and slopes (People.PersonGround; on by default), likewise only with people and either rule on: 1.1's
        // worlds and the earlier build's people (--stair-line-off --slope-clips-off) have the stores and digests they had.
        var groundSettings = ground ?? new People.PersonGroundSettings();
        // The stair clips' flights, only with people who have them and the clips on: the people and the earlier build's digest as before.
        bool stairClips = false;
        for (int k = 0; k < assets.KindCount && !stairClips; k++) stairClips = assets.Kind(k).Person?.Stairs is not null;
        if (people && groundSettings.Any && groundSettings.Clips && stairClips) builder.Component<People.PersonFlight>();
        // The starts and stops, only when given (the session's) and with people who have them; who plays them is the session's to say
        // (a People.PersonStep on the body): the people, the earlier build's and every world built without them digest as before.
        bool startClips = false;
        for (int k = 0; k < assets.KindCount && !startClips; k++) startClips = assets.Kind(k).Person?.Starts is not null;
        if (people && starts is { Any: true } && startClips) builder.Component<People.PersonStep>().Service(starts);
        // The player's own gaits, riding on the starts and stops, only when given (the session's) and with people who have them (a newer set): a
        // service (who plays them: the body whose start and stop is on the spring, the player). The earlier build's, the sets before and every world built without it as before.
        bool playerGaits = false;
        for (int k = 0; k < assets.KindCount && !playerGaits; k++) playerGaits = assets.Kind(k).Person?.PlayerGaits is not null;
        if (people && starts is { Any: true } && startClips && gaits is { Player: true } && playerGaits)
        {
            builder.Service(gaits);
            // The player's lean, look and lead, riding on its gaits: presentation, a store not digested and its system after the feet.
            if (present is { Any: true })
                builder.Component<People.PersonPresent>(digested: false).Service(present)
                       .System(PosePhase, AnimationOrder + order + People.PersonPresentSystem.Order, People.PersonPresentSystem.Name, new People.PersonPresentSystem(assets, present));
        }
        // The jumps, the fall and the landings, only when given (the session's) and with people who have them; who plays them is the
        // session's to say (a People.PersonAir on the body): the people, the earlier build's and every world built without them digest as before.
        bool airClips = false;
        for (int k = 0; k < assets.KindCount && !airClips; k++) airClips = assets.Kind(k).Person?.Air is not null;
        if (people && air is { Any: true } && airClips) builder.Component<People.PersonAir>().Service(air);
        // Over and onto things and off edges, only when given (the session's), with the air's clips on and with people who have them (a newer
        // set); who plays them is the session's to say (a People.PersonOver on the body): the people, the earlier build's and every world built without them
        // digest as before.
        bool overClips = false;
        for (int k = 0; k < assets.KindCount && !overClips; k++) overClips = assets.Kind(k).Person?.Over is not null;
        if (people && air is { Any: true } && airClips && over is { Any: true } && overClips) builder.Component<People.PersonOver>().Service(over);
        // The ledge, only when given (the session's), with the moves over and onto things and the air's clips on and people who have it (a newer
        // set); who hangs is the session's to say (a People.PersonLedge on the body: the player): the people, the earlier build's and every world built
        // without it digest as before.
        bool ledgeClips = false;
        for (int k = 0; k < assets.KindCount && !ledgeClips; k++) ledgeClips = assets.Kind(k).Person?.Ledge is not null;
        if (people && air is { Any: true } && airClips && over is { Any: true } && overClips && ledge is { Any: true } && ledgeClips)
        {
            builder.Component<People.PersonLedge>().Service(ledge);
            // The commit point in the air, only with the ledge's clips (its catches are theirs) and when given: who has it is the session's to say (a
            // People.PersonCommit on the body: the player); the earlier build's, the people and every world built without it digest as before.
            if (commit is { Any: true })
            {
                builder.Component<People.PersonCommit>().Service(commit);
                // Tier 1's moves, riding on the commit point, only when given (the session's): a service; the ladders' store only with their clips on and
                // people who have them (a newer set). The earlier build's, the people and every world built without them digest as before.
                if (moves is { Any: true })
                {
                    builder.Service(moves);
                    bool ladderClips = false;
                    for (int k = 0; k < assets.KindCount && !ladderClips; k++) ladderClips = assets.Kind(k).Person?.Ladder is not null;
                    if (moves.Ladders && ladderClips) builder.Component<People.PersonLadder>();
                    // The hang's corners and leaps, riding on tier 1's moves, only when given and with people who have them (a newer set): a service and
                    // a store. The earlier build's, the people and every world built without them digest as before.
                    bool hangClips = false;
                    for (int k = 0; k < assets.KindCount && !hangClips; k++) hangClips = assets.Kind(k).Person?.Ledge?.Paths.HasHangMoves == true;
                    if (hang is { Any: true } && hangClips) builder.Component<People.PersonHang>().Service(hang);
                    // The walls, riding on the hang's corners and leaps, only when given and with people who have them (a newer set): a service and a
                    // store. The earlier build's, the sets before and every world built without them digest as before.
                    bool wallClips = false;
                    for (int k = 0; k < assets.KindCount && !wallClips; k++) wallClips = assets.Kind(k).Person?.Ledge?.Paths.HasWalls == true;
                    if (hang is { Any: true } && hangClips && wall is { Any: true } && wallClips)
                    {
                        builder.Component<People.PersonWall>().Service(wall);
                        // The flow's state beside it, only with the earlier build's on (the earlier build's digest as before).
                        if (wall.Flows) builder.Component<People.PersonWallFlow>();
                    }
                    // Squeezing, stealth with cover and the duck's blends, riding on the walls, only when given and with people who have the clips (a newer
                    // set): a service, a store and the stealth key's system. The earlier build's, the sets before and every world built without them digest as before.
                    bool sideClips = false;
                    for (int k = 0; k < assets.KindCount && !sideClips; k++) sideClips = assets.Kind(k).Person?.Ledge?.Paths is { HasSqueeze: true } or { HasCover: true };
                    if (hang is { Any: true } && hangClips && wall is { Any: true } && wallClips && stealth is { Any: true } && sideClips)
                        builder.Component<People.PersonStealth>().Service(stealth).System(Phase.Input, People.StealthSystem.Order, "platform.stealth", new People.StealthSystem());
                }
            }
        }
        // The prelanding, only when given (the session's) with the moves and the air's clips on: a service and no store (its state is the air's, a
        // mode only it sets), so a world whose people have no prelandings (an older set) or with it off digests as before.
        if (people && air is { Any: true } && airClips && over is { Any: true } && overClips && prelanding is { Any: true }) builder.Service(prelanding);
        // The people's swing bones simulated (People.PersonSwingSystem; on by default) in a world with people of the models repository's: a
        // service and a system at the end of the tick, neither digested, so every digest is as it was; off (--swing-off), not there at all.
        if (people && assets.PersonSwing.On)
        {
            var swings = new People.PersonSwings(assets.PersonSwing);
            builder.Service(swings).System(Phase.Post, People.PersonSwingSystem.Order, People.PersonSwingSystem.Name, new People.PersonSwingSystem(assets, swings));
        }
        if (people && groundSettings.Any)
            builder.Component<People.PersonGround>()
                   .Service(groundSettings)
                   .System(PosePhase, AnimationOrder + order + People.PersonGroundSystem.Order, People.PersonGroundSystem.Name, new People.PersonGroundSystem(assets, groundSettings));
        return builder.Component<CharacterAnimation>()
               .Component<MotionInput>()
               .Component<PoseRig>()
               .Service(assets)
               .Service(new CharacterPoses(assets))
               .Service(rigSettings.Aim)       // the aim offset's reach, for whoever aims a body
               .Service(legTurn)
               .System(PosePhase, AnimationOrder + order, AnimationSystem.Name, new AnimationSystem(assets, animation))
               .System(PosePhase, AnimationOrder + order + LegTurnSystem.Order, LegTurnSystem.Name, new LegTurnSystem(assets, legTurn))
               .System(PosePhase, AnimationOrder + order + PoseRigSystem.Order, PoseRigSystem.Name, new PoseRigSystem(assets, rigSettings));
    }
}
