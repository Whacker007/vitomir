// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the scenario program's model — what a compiled scenario asks of each actor's control, its
// triggers, and the selectors and comparisons shared with the measures and the reader.
// Records shared with other code: add fields AT THE END ONLY, with defaults; never change or reorder.
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Control;

// ---------------------------------------------------------------------------------------------------------------------------
// Selectors and comparisons (section 4.5), shared: the director's triggers and a step's "on" read them in the world; the
// measures, assertions and stops (Vitomir.Platform.Host/Scenario/ScenarioMeasures.cs) read them in the host.
// Names as the results work proposed them (/tmp/the shared work/NOTE-results-001-selectors.txt).
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>
/// An event selector (section 4.5): an event of a kind (as <c>--describe events</c> names it: <c>ContactMade</c>), done by an actor
/// (<paramref name="By"/>: the attacker, the user, the dodger — an actor's NAME) to an actor (<paramref name="On"/>: the target, the
/// defender), whose fields match <paramref name="Where"/> (names and enumerated values without regard to case), optionally within
/// a window of the run (seconds, or between a <c>mark</c> step's text and another's: the first such marker of the run).
/// </summary>
public sealed record EventSelector(
    string Event,
    string? By = null,
    string? On = null,
    IReadOnlyList<WhereFilter>? Where = null,
    double? FromSeconds = null, double? ToSeconds = null,
    string? FromMarker = null, string? ToMarker = null);

/// <summary>One field filter of a selector: the event's field (or a derived field: <c>region</c>, <c>verdict</c>, <c>weaponClass</c>, <c>power</c>, <c>text</c>, <c>label</c>) and the values it may have, as invariant strings (<c>"body"</c>, <c>"true"</c>, <c>"3"</c>); any one matches.</summary>
public sealed record WhereFilter(string Field, IReadOnlyList<string> Values);

/// <summary>A comparator (section 4.5).</summary>
public enum CompareOp { Below, AtMost, Above, AtLeast, Equals, Between, IsTrue, IsFalse }

/// <summary>A comparison: the comparator and its threshold (<see cref="CompareOp.Between"/>: <paramref name="Value"/> to <paramref name="High"/>, both included; <see cref="CompareOp.IsTrue"/> / <see cref="CompareOp.IsFalse"/>: nonzero / zero).</summary>
public sealed record Comparison(CompareOp Op, double Value = 0, double High = 0);

/// <summary>An observable at the moment (section 4.5's closed list, by its dotted path: <c>Guard.health</c>, <c>Guard.distanceTo.Christof</c>, <c>time</c>) against a comparison.</summary>
public sealed record ValueCondition(string Observable, Comparison Compare);

/// <summary>How many events matching <paramref name="Of"/> have happened so far in the run (its window applies), against a comparison: <c>{ "count": {…}, "atLeast": 6 }</c>.</summary>
public sealed record CountCondition(EventSelector Of, Comparison Compare);

// ---------------------------------------------------------------------------------------------------------------------------
// The program (sections 4.4, 4.6, 5.3)
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>
/// A compiled scenario's program for one run (section 5.3): each actor's control and the triggers. Built by the reader
/// (the format work's compiler) beside the run's cast (<c>ArenaCast</c>), carried in the session's options and the tape's head as JSON,
/// so <c>--replay-check</c> rebuilds a run from its tape alone. Actors are named by their cast names throughout (unique; the
/// cast's order is theirs). The run's stop conditions are the runner's; a program ends a run only by an
/// <see cref="EndStep"/>, a trigger's <see cref="TriggerAction.End"/> or <see cref="ScenarioThen.End"/>.
/// </summary>
public sealed record ScenarioProgram
{
    /// <summary>Each actor that has a control block (an actor not listed is driven as its cast says, untouched by the director).</summary>
    public List<ActorControl> Actors { get; init; } = [];

    /// <summary>"When X, do Y" (section 4.6), in the file's order (their index is <see cref="TriggerFired.Trigger"/>).</summary>
    public List<ScenarioTrigger> Triggers { get; init; } = [];

    /// <summary>
    /// At the end: the one actor driven through <b>the player's own controls</b> (<c>PLAYER_INPUT_REPLAY</c>), or null for
    /// none: that cast fighter is made the controlled body (no brain, no director), and each tick's <see cref="ActionFrame"/> is
    /// <see cref="PlayerInput.Frame"/> — the move, the look turned, the buttons — fed in as the window or a tape feeds it, so the
    /// camera, the crosshair, automatic directions, held blows, the player's guard, dodge and slots run as in play.
    /// </summary>
    public PlayerInput? Input { get; init; }

    /// <summary>
    /// At the end: a scenario of kind <c>contact</c>'s <b>fixed contacts</b> (<c>FIXED_CONTACT</c>), each placed at
    /// its time and resolved by the rules as a swept contact (<see cref="Combat.FixedContactSystem"/>); null (written as nothing) for
    /// every other scenario, so their programs' text is unchanged.
    /// </summary>
    public List<Combat.FixedContactSpec>? Contacts { get; init; }

    /// <summary>The frame for the session's next tick (<paramref name="tick"/>: the world's tick before it steps): <see cref="Input"/>'s, else idle.</summary>
    public ActionFrame FrameAt(long tick) => Input is { } input ? input.Frame(tick) : ActionFrame.Idle;

    /// <summary>
    /// Who drives <paramref name="actor"/> in this program, in the execution modes' words (the reader's agree):
    /// <c>PLAYER_INPUT_REPLAY</c> (<see cref="Input"/>); <c>HYBRID</c> (a duelling or blocking AI with steps, trigger steps or mutes);
    /// <c>CONTROLLED_SCRIPT</c> (steps with no AI or the standing dummy, or nothing at all and no AI: the director stands it);
    /// <c>NATIVE_AI</c> (an AI or the dummy alone, or no control block: the cast's brain).
    /// </summary>
    public string AuthorityOf(string actor)
    {
        if (Input?.Actor == actor) return "PLAYER_INPUT_REPLAY";
        var c = Actors.Find(a => a.Actor == actor);
        if (c is null) return "NATIVE_AI";
        bool steps = c.Tracks.Exists(t => t.Count > 0) || Triggers.Exists(t => t.Do.Exists(d => d.Actor == actor && d.Step is not null));
        bool ai = c.Ai is ScenarioAi.Duel or ScenarioAi.Block or ScenarioAi.Archer;   // Archer
        if (steps) return ai ? "HYBRID" : "CONTROLLED_SCRIPT";
        if (ai) return c.Mute != ControlChannels.None ? "HYBRID" : "NATIVE_AI";
        return c.Ai == ScenarioAi.Stand ? "NATIVE_AI" : "CONTROLLED_SCRIPT";
    }

    static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowOutOfOrderMetadataProperties = true,
    };

    /// <summary>The program as compact JSON (the session's <c>Scenario</c> option and the tape's head; the same program writes the same text).</summary>
    public string Write() => JsonSerializer.Serialize(this, Json);

    /// <summary>A program from its JSON; <see cref="ScenarioProgramException"/> when it is not one.</summary>
    public static ScenarioProgram Read(string text)
    {
        try { return JsonSerializer.Deserialize<ScenarioProgram>(text, Json) ?? throw new JsonException("empty"); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            throw new ScenarioProgramException($"scenario: the program is not readable ({e.Message})");
        }
    }
}

/// <summary>An actor's AI as the program knows it (its numbers are the cast's: <c>CastFighter.Numbers</c>).</summary>
public enum ScenarioAi
{
    /// <summary>No AI: the actor does what its steps say and nothing else (between steps it stands).</summary>
    None = 0,
    /// <summary>The duelling brain (<c>DuelBrain</c>, Think 95) with the cast's numbers.</summary>
    Duel,
    /// <summary>The arena's dummy that stands and keeps its facing.</summary>
    Stand,
    /// <summary>The arena's dummy that holds its block.</summary>
    Block,
    /// <summary>The archer brain (<c>ArcherBrain</c>, Think 94) with the cast's numbers: it shoots its foe, and draws its melee weapon (the duelling brain) up close.</summary>
    Archer,
}

/// <summary>The channels of a body's control (section 4.4): which the director holds, which the AI drives, which are muted.</summary>
[Flags]
public enum ControlChannels
{
    None = 0,
    /// <summary>The way and the gait (<see cref="MoveIntent.Direction"/>, <see cref="MoveIntent.Gait"/>).</summary>
    Move = 1,
    /// <summary>The facing (<see cref="MoveIntent.Facing"/>, <see cref="MoveIntent.Faces"/>).</summary>
    Face = 2,
    /// <summary>Attack held, the blow's direction and aim (<see cref="CombatIntent.Attack"/>, <see cref="CombatIntent.Direction"/>, <see cref="CombatIntent.AimPitch"/>); the bow's states.</summary>
    Attack = 4,
    /// <summary>The block (<see cref="CombatIntent.Block"/>).</summary>
    Block = 8,
    /// <summary>A dodge (<see cref="DodgeIntent"/>).</summary>
    Dodge = 16,
    /// <summary>A quick slot pressed (<see cref="Powers.PowerIntent"/>).</summary>
    Power = 32,
    All = Move | Face | Attack | Block | Dodge | Power,
}

/// <summary>What follows an actor's steps when every track has ended (section 4.4's <c>then</c>).</summary>
public enum ScenarioThen
{
    /// <summary>Every channel to the AI (the default when an AI is given; with <see cref="ScenarioAi.None"/> it stands).</summary>
    Ai = 0,
    /// <summary>The director keeps every channel and asks nothing: the body stands (the default without an AI).</summary>
    Stand,
    /// <summary>The run ends (<c>SCENARIO_END:script</c>).</summary>
    End,
}

/// <summary>Who drives one actor (section 4.4's <c>control</c>).</summary>
public sealed record ActorControl
{
    /// <summary>The actor's cast name.</summary>
    public required string Actor { get; init; }
    /// <summary>The actor's AI.</summary>
    public ScenarioAi Ai { get; init; } = ScenarioAi.None;
    /// <summary>Channels the AI may not drive (the director clears them each tick after the brain).</summary>
    public ControlChannels Mute { get; init; } = ControlChannels.None;
    /// <summary>Tracks of steps run side by side (a <c>script</c> is one track); each in order.</summary>
    public List<List<ScenarioStep>> Tracks { get; init; } = [];
    /// <summary>What follows when every track has ended.</summary>
    public ScenarioThen Then { get; init; } = ScenarioThen.Ai;
    /// <summary>The actor its steps aim at, face and keep away from when they name nobody (the cast's foe's name), or null for none.</summary>
    public string? Foe { get; init; }
}

/// <summary>"When X, do Y" (section 4.6).</summary>
public sealed record ScenarioTrigger
{
    public string? Label { get; init; }
    /// <summary>The condition.</summary>
    public required TriggerWhen When { get; init; }
    /// <summary>What it does, in order, all on the tick it fires (one tick after the event it reads: the director reads last tick's events).</summary>
    public List<TriggerAction> Do { get; init; } = [];
    /// <summary>Whether it may fire again (false: once a run).</summary>
    public bool Repeat { get; init; }
    /// <summary>With <see cref="Repeat"/>: the least time between two firings.</summary>
    public float CooldownSeconds { get; init; }
    /// <summary>The window of the run in which it may fire: not before this second (null: from the start).</summary>
    public float? AfterSeconds { get; init; }
    /// <summary>Not after this second (null: to the end).</summary>
    public float? BeforeSeconds { get; init; }
}

/// <summary>A trigger's condition: exactly one of an event (fires on each match), a value with a comparison, or a count with a comparison. A value or count condition fires on the tick it becomes true (and, with <see cref="ScenarioTrigger.Repeat"/>, again each time it becomes true after being false).</summary>
public sealed record TriggerWhen
{
    public EventSelector? Event { get; init; }
    public ValueCondition? Value { get; init; }
    public CountCondition? Count { get; init; }
}

/// <summary>One thing a trigger does: start <see cref="Step"/> on <see cref="Actor"/> at once (holding its channels over any step already holding them, which resumes after), or end the run with <see cref="End"/>'s reason.</summary>
public sealed record TriggerAction
{
    public string? Actor { get; init; }
    public ScenarioStep? Step { get; init; }
    /// <summary>The reason the run ends (<c>SCENARIO_END:&lt;reason&gt;</c>), or null.</summary>
    public string? End { get; init; }
}

// ---------------------------------------------------------------------------------------------------------------------------
// Steps (section 4.4's step table). Every step: one kind, the timing fields, its own fields. Serialized with the kind as the
// "step" discriminator (written first), the kinds named as the scenario file names them.
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>A step's kind (its name in the file, and in the director's events).</summary>
public enum ScenarioStepKind
{
    Wait, Stand, Face, MoveTo, KeepDistance, Retreat, Strafe, Circle, Path,
    Attack, Block, Parry, Cancel, Dodge, UsePower,
    Equip, Aim, Draw, Loose, CancelDraw,
    Mark, Repeat, HandOver, End,
}

/// <summary>
/// One step of a track (section 4.4). The timing fields: <see cref="AtSeconds"/> (absolute; late if the step before has not ended
/// by then: <see cref="StepLate"/>), <see cref="AfterSeconds"/> (after the step before ends), <see cref="On"/> then
/// <see cref="DelaySeconds"/> (when an event matching it happens), <see cref="ForSeconds"/> (the most it runs: a goal step not met
/// by then ends <see cref="StepOutcome.TimedOut"/>), <see cref="Label"/>. Times are seconds, rounded to the nearest tick.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "step")]
[JsonDerivedType(typeof(WaitStep), "wait")]
[JsonDerivedType(typeof(StandStep), "stand")]
[JsonDerivedType(typeof(FaceStep), "face")]
[JsonDerivedType(typeof(MoveToStep), "moveTo")]
[JsonDerivedType(typeof(KeepDistanceStep), "keepDistance")]
[JsonDerivedType(typeof(RetreatStep), "retreat")]
[JsonDerivedType(typeof(StrafeStep), "strafe")]
[JsonDerivedType(typeof(CircleStep), "circle")]
[JsonDerivedType(typeof(PathStep), "path")]
[JsonDerivedType(typeof(AttackStep), "attack")]
[JsonDerivedType(typeof(BlockStep), "block")]
[JsonDerivedType(typeof(ParryStep), "parry")]
[JsonDerivedType(typeof(CancelStep), "cancel")]
[JsonDerivedType(typeof(DodgeStep), "dodge")]
[JsonDerivedType(typeof(UsePowerStep), "usePower")]
[JsonDerivedType(typeof(EquipStep), "equip")]
[JsonDerivedType(typeof(AimStep), "aim")]
[JsonDerivedType(typeof(DrawStep), "draw")]
[JsonDerivedType(typeof(LooseStep), "loose")]
[JsonDerivedType(typeof(CancelDrawStep), "cancelDraw")]
[JsonDerivedType(typeof(MarkStep), "mark")]
[JsonDerivedType(typeof(RepeatStep), "repeat")]
[JsonDerivedType(typeof(HandOverStep), "handOver")]
[JsonDerivedType(typeof(EndStep), "end")]
public abstract record ScenarioStep
{
    /// <summary>The step's kind.</summary>
    [JsonIgnore] public abstract ScenarioStepKind Kind { get; }
    /// <summary>A name for the events and the assertions, or null.</summary>
    public string? Label { get; init; }
    /// <summary>Starts at this second of the run (absolute), or null.</summary>
    public float? AtSeconds { get; init; }
    /// <summary>Starts this long after the step before it ends, or null (at once).</summary>
    public float? AfterSeconds { get; init; }
    /// <summary>Starts when an event matching this happens (read one tick later), then <see cref="DelaySeconds"/>; or null.</summary>
    public EventSelector? On { get; init; }
    /// <summary>With <see cref="On"/>: the delay after the event, or null (none).</summary>
    public float? DelaySeconds { get; init; }
    /// <summary>The most it runs, or null (the step's own end; a goal step without it ends when its goal is met).</summary>
    public float? ForSeconds { get; init; }
}

/// <summary>A place in the level's frame, metres (+Z up; <see cref="Z"/> null: on the ground there).</summary>
public sealed record ScenarioPlace(float X, float Y, float? Z = null);

/// <summary>Left or right (a strafe's way, a circle's turn, an aimed region's side).</summary>
public enum StepSide { Left, Right }

/// <summary>
/// What a step faces or goes to: exactly one of an actor (by name), a point, or a facing in degrees (counter-clockwise from +X);
/// with an actor, <see cref="DistanceMetres"/> is how far from it (a <c>moveTo</c>'s <c>{ "actor", "distanceMetres" }</c>: the
/// place that far from it on the line from it to the mover, feet to feet).
/// </summary>
public sealed record ScenarioTarget
{
    public string? Actor { get; init; }
    public ScenarioPlace? Point { get; init; }
    public float? Degrees { get; init; }
    public float DistanceMetres { get; init; }
}

/// <summary>A body region as <c>aimAt</c> names it (section 4.4): the side is apart (<see cref="ScenarioAim.Side"/>).</summary>
public enum AimRegion { Head, Neck, Chest, Belly, Hips, UpperArm, Forearm, Hand, Thigh, Shin, Foot }

/// <summary>
/// Where a blow (or the bow) is aimed: exactly one of a region of an actor (<see cref="Region"/>, optionally <see cref="Side"/>;
/// <see cref="Actor"/> null: the step's target), a point (<see cref="Point"/>, metres), or angles (<see cref="PitchDegrees"/>, up
/// positive; <see cref="YawDegrees"/> from the body's facing, counter-clockwise). An aim is a request: the director solves the
/// yaw and pitch toward the region's posed hit volume at the press and while held, and the outputs give the region asked and met.
/// </summary>
public sealed record ScenarioAim
{
    public string? Actor { get; init; }
    public AimRegion? Region { get; init; }
    public StepSide? Side { get; init; }
    public ScenarioPlace? Point { get; init; }
    public float? PitchDegrees { get; init; }
    public float? YawDegrees { get; init; }
}

/// <summary>Does nothing for <see cref="Seconds"/>.</summary>
public sealed record WaitStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Wait;
    public float Seconds { get; init; }
}

/// <summary>Holds the way still (channel move) for <see cref="Seconds"/>.</summary>
public sealed record StandStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Stand;
    public float Seconds { get; init; }
}

/// <summary>Faces an actor, a point or a bearing (channel face); ends when faced (within 2°), or at <see cref="ScenarioStep.ForSeconds"/>.</summary>
public sealed record FaceStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Face;
    public required ScenarioTarget Toward { get; init; }
}

/// <summary>Goes to a point, or to a distance from an actor (channels move, face); ends on arrival (within <see cref="WithinMetres"/>).</summary>
public sealed record MoveToStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.MoveTo;
    public required ScenarioTarget To { get; init; }
    public Gait Gait { get; init; } = Gait.Walk;
    /// <summary>Faces this actor while it moves (by name), or null: the way it goes.</summary>
    public string? FaceWhileMoving { get; init; }
    public float WithinMetres { get; init; } = 0.2f;
}

/// <summary>Holds a distance from an actor, toward or away, facing it (channels move, face), for <see cref="ScenarioStep.ForSeconds"/> (null: until stopped or handed over).</summary>
public sealed record KeepDistanceStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.KeepDistance;
    /// <summary>The actor (by name; null: the foe).</summary>
    public string? From { get; init; }
    public float Metres { get; init; }
    public float ToleranceMetres { get; init; } = 0.2f;
    public Gait Gait { get; init; } = Gait.Walk;
}

/// <summary>Backs away from an actor, facing it, until <see cref="Metres"/> from it (channels move, face).</summary>
public sealed record RetreatStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Retreat;
    /// <summary>The actor (by name; null: the foe).</summary>
    public string? From { get; init; }
    public float Metres { get; init; }
    public Gait Gait { get; init; } = Gait.Walk;
}

/// <summary>Goes sideways, facing an actor (channels move, face), for <see cref="ScenarioStep.ForSeconds"/>.</summary>
public sealed record StrafeStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Strafe;
    public StepSide Side { get; init; }
    public Gait Gait { get; init; } = Gait.Walk;
    /// <summary>The actor it faces (by name; null: the foe).</summary>
    public string? Facing { get; init; }
}

/// <summary>Goes round an actor at a radius, facing it (channels move, face), for <see cref="ScenarioStep.ForSeconds"/>.</summary>
public sealed record CircleStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Circle;
    /// <summary>The actor (by name; null: the foe).</summary>
    public string? Around { get; init; }
    public float RadiusMetres { get; init; }
    /// <summary>Left: counter-clockwise seen from above; right: clockwise.</summary>
    public StepSide Direction { get; init; }
    public Gait Gait { get; init; } = Gait.Walk;
}

/// <summary>Goes through waypoints in order (channels move, face), again from the first when <see cref="Loop"/>.</summary>
public sealed record PathStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Path;
    public List<ScenarioPlace> Points { get; init; } = [];
    public Gait Gait { get; init; } = Gait.Walk;
    public bool Loop { get; init; }
    public float WithinMetres { get; init; } = 0.2f;
}

/// <summary>
/// A blow (channel attack): from <see cref="Direction"/> (<see cref="AttackDirection.None"/>: the mode chooses), quick or strong,
/// held <see cref="HoldSeconds"/> then released (null: a tap, or for a strong blow the strong threshold and a tick), pressed when
/// the body may swing again (<see cref="WaitReady"/>, at most <see cref="ReadySeconds"/>) or at once, aimed by
/// <see cref="AimAt"/> (null: level), at <see cref="Target"/> (null: the foe). Ends when the blow is released (its swing goes on).
/// </summary>
public sealed record AttackStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Attack;
    public AttackDirection Direction { get; init; } = AttackDirection.None;
    public AttackStrength Strength { get; init; } = AttackStrength.Quick;
    public float? HoldSeconds { get; init; }
    public bool WaitReady { get; init; } = true;
    public float ReadySeconds { get; init; } = 2f;
    public ScenarioAim? AimAt { get; init; }
    public string? Target { get; init; }
}

/// <summary>Holds the block (channel block) for <see cref="ScenarioStep.ForSeconds"/>, or until an event matching <see cref="Until"/>.</summary>
public sealed record BlockStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Block;
    public EventSelector? Until { get; init; }
}

/// <summary>Waits for <see cref="Against"/>'s next blow and presses block <see cref="LeadSeconds"/> before its strike is due (channel block).</summary>
public sealed record ParryStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Parry;
    /// <summary>The attacker (by name; null: the foe).</summary>
    public string? Against { get; init; }
    public float LeadSeconds { get; init; } = 0.25f;
}

/// <summary>Lets a held blow down (a feint; channel attack).</summary>
public sealed record CancelStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Cancel;
}

/// <summary>A dodge's way, from the body's facing.</summary>
public enum DodgeWay { Back, Left, Right, Forward }

/// <summary>A dodge (channels dodge, move): <see cref="DodgeIntent"/> pressed once.</summary>
public sealed record DodgeStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Dodge;
    public DodgeWay Way { get; init; } = DodgeWay.Back;
}

/// <summary>Presses the quick slot that shows <see cref="Power"/> (a power's id), or <see cref="Slot"/> (1 to 8) when given (channel power).</summary>
public sealed record UsePowerStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.UsePower;
    public string? Power { get; init; }
    /// <summary>The slot, 1 to 8, or 0: the slot that shows <see cref="Power"/>.</summary>
    public int Slot { get; init; }
}

/// <summary>What <see cref="EquipStep"/> takes in hand.</summary>
public enum EquipWhat { Ranged, Melee }

/// <summary>The bow's: takes the ranged or the melee weapon in hand (channel attack). Written by the bow work (<c>ScenarioDirector.Bow.cs</c>).</summary>
public sealed record EquipStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Equip;
    public EquipWhat What { get; init; } = EquipWhat.Ranged;
}

/// <summary>The bow's: raised, an arrow nocked, the aim held on <see cref="At"/> (channels attack, face). Written by the bow work.</summary>
public sealed record AimStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Aim;
    public required ScenarioAim At { get; init; }
}

/// <summary>The bow's: attack held until the draw reaches <see cref="ToShare"/> of full (channel attack). Written by the bow work.</summary>
public sealed record DrawStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Draw;
    public float ToShare { get; init; } = 1f;
}

/// <summary>The bow's: attack released, the arrow loosed at the speed the draw reached (channel attack). Written by the bow work.</summary>
public sealed record LooseStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Loose;
}

/// <summary>The bow's: lets the string down (channel attack). Written by the bow work.</summary>
public sealed record CancelDrawStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.CancelDraw;
}

/// <summary>Publishes a <see cref="Marker"/> with <see cref="Text"/> (for windows, stops, assertions and readers).</summary>
public sealed record MarkStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Mark;
    public required string Text { get; init; }
}

/// <summary>Its <see cref="Steps"/> again, <see cref="Count"/> times, or until the run stops (<see cref="UntilStop"/>).</summary>
public sealed record RepeatStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.Repeat;
    public int Count { get; init; } = 1;
    public bool UntilStop { get; init; }
    public List<ScenarioStep> Steps { get; init; } = [];
}

/// <summary>Every channel to the AI from here (this actor's tracks end).</summary>
public sealed record HandOverStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.HandOver;
}

/// <summary>Ends the run (<c>SCENARIO_END:&lt;reason&gt;</c>).</summary>
public sealed record EndStep : ScenarioStep
{
    public override ScenarioStepKind Kind => ScenarioStepKind.End;
    public string Reason { get; init; } = "script";
}

// ---------------------------------------------------------------------------------------------------------------------------
// What the director's events say (the events themselves: Combat/CombatEvents.cs, at the end)
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>How a step ended.</summary>
public enum StepOutcome
{
    /// <summary>It did what it says (arrived, released, waited, pressed).</summary>
    Done = 0,
    /// <summary>Its goal was not met in its time (<see cref="StepTimedOut"/> says why).</summary>
    TimedOut,
    /// <summary>It could not be asked at all (<see cref="StepRefused"/> says why); nothing was written.</summary>
    Refused,
    /// <summary>A trigger's step took its channels; it resumes after (published when it is cut short for good: a hand-over, the run's end).</summary>
    Interrupted,
    /// <summary>The run ended while it ran.</summary>
    Ended,
    /// <summary>A hand-over took its channels.</summary>
    HandedOver,
}

/// <summary>Why a step could not be asked.</summary>
public enum StepRefusal
{
    /// <summary>No quick slot shows the power.</summary>
    NoSlot = 0,
    /// <summary>The actor it names is not in the world.</summary>
    NoTarget,
    /// <summary>The actor is stopped (its death hold).</summary>
    Stopped,
    /// <summary>The rules refused the press (<c>waitReady</c> false: an <see cref="AttackRefused"/> on its tick).</summary>
    RulesRefused,
    /// <summary>This build does not do it yet (the bow's steps before the bow work merges).</summary>
    NotBuilt,
    // ---- the bow work, at the end ----
    /// <summary>A bow step for an actor that carries no bow (its gear gives no <c>ranged</c> weapon).</summary>
    NoBow,
    /// <summary>The quiver is empty and no arrow is on the string: nothing to nock, draw or loose.</summary>
    NoArrows,
    /// <summary>A <c>loose</c> with nothing drawn (the bow not drawn, or let down).</summary>
    NotDrawn,
    // ---- the goals work, at the end ----
    /// <summary>A <c>moveTo</c> whose straight line meets the level and whose navigation mesh has no route to its goal (<see cref="Combat.MoveToFailed"/> says where it stood and why).</summary>
    NoRoute,
}

// ---------------------------------------------------------------------------------------------------------------------------
// The player's input, synthesised (PLAYER_INPUT_REPLAY). Records shared: add fields at the end only.
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>
/// The input of the one actor driven through the player's controls: segments of input over the run, each tick's frame the
/// sum of the segments in force (<see cref="Frame"/>). Pure data and a pure function of the tick, so the frames are the same on every
/// run; the tape records them as it records a player's, and a replay feeds them back.
/// </summary>
public sealed record PlayerInput
{
    /// <summary>The cast fighter (by name) made the controlled body.</summary>
    public required string Actor { get; init; }
    /// <summary>The segments, in any order; several may be in force at once.</summary>
    public List<InputSegment> Segments { get; init; } = [];

    /// <summary>
    /// The frame of tick <paramref name="tick"/>: the buttons held by every segment in force and pressed by any beginning now; the
    /// move of the last segment in force that gives one (in the list's order); the look turned, every segment's share of its turn
    /// for this tick added up (radians: yaw to the left, pitch up); the attack-direction mode of a segment beginning now. Allocates nothing.
    /// </summary>
    public ActionFrame Frame(long tick)
    {
        var held = Buttons.None;
        var move = Vector2.Zero;
        var look = Vector2.Zero;
        AttackDirectionMode? mode = null;
        foreach (var s in Segments)
        {
            long start = (long)MathF.Round(s.AtSeconds * World.TicksPerSecond);
            long length = Math.Max(1, (long)MathF.Round((s.ForSeconds ?? 0f) * World.TicksPerSecond));
            if (tick < start || tick >= start + length) continue;
            held |= s.Hold;
            if (tick == start) { held |= s.Press; if (s.AttackMode is { } m) mode = m; }
            if (s.MoveRight is not null || s.MoveForward is not null) move = new Vector2(s.MoveRight ?? 0f, s.MoveForward ?? 0f);
            look += new Vector2(s.TurnLeftDegrees, s.TurnUpDegrees) * (DetMath.DegToRad / length);
        }
        return new ActionFrame(move, look, held, new Vector2(0.5f, 0.5f), 0f, mode);
    }
}

/// <summary>
/// One segment of a player's input: from <see cref="AtSeconds"/> for <see cref="ForSeconds"/> (null: one tick), the move
/// keys' way (<see cref="MoveRight"/>, <see cref="MoveForward"/>, each −1 to 1, null: none), the look turned over the segment
/// (degrees, spread evenly over its ticks: <see cref="TurnLeftDegrees"/> yaw to the left, <see cref="TurnUpDegrees"/> pitch up),
/// the buttons held through it (<see cref="Hold"/>: attack held, block held, sprint, crouch …) and pressed on its first tick only
/// (<see cref="Press"/>: a dodge, a quick slot, switching the view; a double tap is two segments), and the player's way of choosing a
/// blow's direction set on its first tick (<see cref="AttackMode"/>, as the Controls page sets it).
/// </summary>
public sealed record InputSegment
{
    public float AtSeconds { get; init; }
    public float? ForSeconds { get; init; }
    public float? MoveRight { get; init; }
    public float? MoveForward { get; init; }
    public float TurnLeftDegrees { get; init; }
    public float TurnUpDegrees { get; init; }
    public Buttons Hold { get; init; }
    public Buttons Press { get; init; }
    public AttackDirectionMode? AttackMode { get; init; }
    public string? Label { get; init; }
}
