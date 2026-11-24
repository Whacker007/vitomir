// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json.Nodes;
using Vitomir.Platform.Control;

namespace Vitomir.Platform.Host.Scenario;

// The measures' and assertions' model and the results document's records — the types only.
// The format work's reader (ScenarioSpec) compiles a scenario file's
// "measures", "assertions", "stop" and "outputs" into these; the runner (ScenarioCommand, ScenarioTally) computes and writes
// them. The event selector, the comparison and the observable path are the program model's (ScenarioProgram.cs, the director work's:
// triggers use them in the world), shared here. Records shared with other code: add fields at the end only, with defaults.

// ---------------------------------------------------------------------------------------------------------------------------
// Value forms (section 4.5): what a measure, an assertion or a stop condition computes from one run.
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>
/// A value computed from one run (section 4.5's value forms). Each form gives, per run, one number (or none: censored), and
/// for a rate its numerator and denominator kept; a <see cref="DelayForm"/> also its samples.
/// </summary>
public abstract record ValueForm;

/// <summary>
/// <c>{ "count": selector }</c>: how many events matched the selector in its window; with <see cref="SumField"/>
/// (<c>"sum": "wounds"</c>, written inside the selector) that field summed over the matches instead.
/// </summary>
public sealed record CountForm(EventSelector Of, string? SumField = null) : ValueForm;

/// <summary>
/// <c>{ "rate": { "of": selector, "per": selector } }</c>: the matches of <see cref="Of"/> (or its <see cref="OfSumField"/>
/// summed) over the matches of <see cref="Per"/> (or its <see cref="PerSumField"/> summed), numerator and denominator kept;
/// aggregated over a point by summing both, with Wilson's 95 % interval when the numerator is a count of events.
/// With <see cref="PerSecond"/> (<c>"perSecond": true</c>) the denominator is the seconds of <see cref="Of"/>'s window
/// (from its start — <c>fromSeconds</c>, <c>fromMarker</c> or 0 — to its end — <c>toSeconds</c>, <c>toMarker</c> or the run's end).
/// </summary>
public sealed record RateForm(EventSelector Of, EventSelector? Per = null, bool PerSecond = false,
    string? OfSumField = null, string? PerSumField = null) : ValueForm;

/// <summary>
/// <c>{ "value": "&lt;observable&gt;" }</c>: an observable (<c>Guard.health</c>, <c>Christof.distanceTo.Guard</c>:
/// <c>--describe observables</c>) at the run's end, or at <see cref="AtSeconds"/> of the run (an assertion's or a measure's
/// <c>"atSeconds"</c>); a run that ended before that moment has none (censored).
/// </summary>
public sealed record ObservableForm(string Observable, double? AtSeconds = null) : ValueForm;

/// <summary>
/// <c>{ "value": "&lt;observable&gt;", "atEvent": selector, "nth": 1 }</c>: the observable at the moment the
/// <see cref="Nth"/> match of <see cref="AtEvent"/> happened (the end of that tick); none when there was no such match.
/// </summary>
public sealed record ObservableAtEventForm(string Observable, EventSelector AtEvent, int Nth = 1) : ValueForm;

/// <summary><c>{ "timeOf": selector, "nth": 1 }</c>: the seconds of the run at which the <see cref="Nth"/> match happened; none: censored.</summary>
public sealed record TimeOfForm(EventSelector Of, int Nth = 1) : ValueForm;

/// <summary>
/// <c>{ "delay": { "from": selector, "to": selector, "pairBy": "swing" } }</c>: the seconds from each match of
/// <see cref="From"/> to its <see cref="To"/> — the next <c>To</c> match after it with the same <see cref="PairBy"/> field
/// (matched without regard to case), or without <c>pairBy</c> the next <c>To</c> match after it. A <c>From</c> with no
/// <c>To</c> before the run ends is censored. Per run: the mean of its samples; per point: the run's samples pooled.
/// </summary>
public sealed record DelayForm(EventSelector From, EventSelector To, string? PairBy = null) : ValueForm;

/// <summary><c>{ "difference": [a, b] }</c>: a − b in each run (none when either is none).</summary>
public sealed record DifferenceForm(ValueForm Left, ValueForm Right) : ValueForm;

/// <summary><c>{ "ratio": [a, b] }</c>: a / b in each run (none when either is none or b is 0).</summary>
public sealed record RatioForm(ValueForm Numerator, ValueForm Denominator) : ValueForm;

/// <summary><c>{ "measure": "&lt;name&gt;" }</c>: a named measure of the scenario's, or a built-in one (<c>Christof.swings</c>: <see cref="BuiltInMeasures"/>).</summary>
public sealed record MeasureRefForm(string Measure) : ValueForm;

/// <summary>
/// <c>{ "check": "&lt;name&gt;" }</c>: one of the built-in checks (<see cref="BuiltInChecks"/>), true or false in each run; a
/// check counts its cases and its violations (both kept in the run's value: <see cref="RunValue.Numerator"/> the violations,
/// <see cref="RunValue.Denominator"/> the cases looked at).
/// </summary>
public sealed record CheckForm(string Check) : ValueForm;

// ---------------------------------------------------------------------------------------------------------------------------
// Measures (section 4.10)
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>What a measure's aggregate is: decides its statistics and interval (section 4.10).</summary>
public enum MeasureKind
{
    /// <summary>A count (or a sum) per run: mean, median, p10, p90, min, max over the runs; the mean's interval by bootstrap.</summary>
    Count,
    /// <summary>A rate: numerator and denominator summed over the runs; the pooled value with Wilson's 95 % interval (for a count of events over a count of events).</summary>
    Rate,
    /// <summary>A rate per second: matches over seconds, per run and pooled; the per-run mean with a bootstrap interval.</summary>
    RatePerSecond,
    /// <summary>A value (an observable, a difference, a ratio): the statistics over the runs that had one; the rest censored.</summary>
    Value,
    /// <summary>A time (seconds of the run): as a value, the runs without one censored.</summary>
    Time,
    /// <summary>A delay (seconds): the samples of every run pooled, the unanswered ones censored.</summary>
    Delay,
    /// <summary>A check: the runs it held in, as a proportion with Wilson's interval; its violations and cases summed.</summary>
    Check,
}

/// <summary>A named measure (section 4.10): computed per run and aggregated per point.</summary>
/// <param name="Name">Its name: letters, digits and <c>_</c>; unique among the scenario's measures and not a built-in's.</param>
/// <param name="Form">What it computes.</param>
/// <param name="Unit">Its unit as the results show it (<c>count</c>, <c>s</c>, <c>share</c>, <c>per s</c>, <c>Health</c>, …); the runner fills it when null.</param>
/// <param name="Pointer">The JSON pointer of its place in the scenario file (<c>/measures/3</c>), for messages.</param>
public sealed record MeasureSpec(string Name, ValueForm Form, string? Unit = null, string? Pointer = null);

/// <summary>A built-in measure (every run, every actor; section 4.10): <c>&lt;actor&gt;.&lt;Name&gt;</c>, and the run's own as <c>run.&lt;Name&gt;</c>.</summary>
/// <param name="Name">The measure's name after the actor's (<c>swings</c>).</param>
/// <param name="Unit">Its unit (<c>count</c>, <c>s</c>, <c>Health</c>, <c>Blood</c>).</param>
/// <param name="Column">The <c>runs.csv</c> column it is (<c>&lt;actor&gt;.&lt;column&gt;</c>; the run's own without the actor), so a reader can find it there.</param>
/// <param name="Sentence">One sentence: what it counts.</param>
public sealed record BuiltInMeasure(string Name, string Unit, string Column, string Sentence);

/// <summary>The built-in measures' names (section 4.10), from <c>bouts.csv</c>'s columns; per actor unless <see cref="RunScope"/>.</summary>
public static class BuiltInMeasures
{
    /// <summary>The prefix of the run's own built-in measures (<c>run.seconds</c>).</summary>
    public const string RunScope = "run";

    /// <summary>Per actor, as <c>&lt;actor&gt;.&lt;name&gt;</c>.</summary>
    public static readonly IReadOnlyList<BuiltInMeasure> PerActor =
    [
        new("swings", "count", "swings", "Blows begun (AttackBegun)."),
        new("strikes", "count", "strikes", "Blows that reached their Strike moment (StrikeMoment)."),
        new("contactsBody", "count", "contacts_body", "Contacts its blows made on a body (ContactMade, Met Body)."),
        new("contactsShield", "count", "contacts_shield", "Contacts its blows made on a shield."),
        new("contactsWeapon", "count", "contacts_weapon", "Contacts its blows made on a blade."),
        new("contactsLevel", "count", "contacts_level", "Contacts its blows made on the level."),
        new("hits", "count", "hits", "Contacts the rules resolved as harm (HarmResolved, as the attacker)."),
        new("woundsDealt", "count", "wounds_dealt", "Wounds its blows dealt (every blow's, flurry blows included)."),
        new("woundsTaken", "count", "wounds_taken", "Wounds it took."),
        new("healthStart", "Health", "health_start", "Its Health at the start (the module's scale, 1-1000)."),
        new("healthEnd", "Health", "health_end", "Its Health at the end."),
        new("bloodSpent", "Blood", "blood_spent", "The Blood its power uses paid."),
        new("powerUses", "count", "uses", "Its powers used (renewals included)."),
        new("renewals", "count", "renewals", "Its power uses that renewed a live window."),
        new("powersRefused", "count", "refused", "Its power presses refused by the module (PowerRefused)."),
        new("liveSeconds", "s", "live_seconds", "The seconds any of its powers was live (runs.csv's live_seconds is per power when it has several)."),
        new("dodges", "count", "dodges_begun", "Dodges begun (DodgeBegun)."),
        new("guardHeld", "count", "guard_held", "Blows met at its guard that the guard held (GuardResolved, as the defender)."),
        new("guardParried", "count", "guard_parried", "Blows met at its guard that it parried."),
        new("guardOverpowered", "count", "guard_overpowered", "Blows met at its guard that overpowered it."),
        new("staggers", "count", "staggered", "Staggers it took (Staggered)."),
        new("stopped", "0/1", "stopped", "1 when a harness stop ended its part (ZERO_HEALTH_STOP, SEVERED_STOP, DISARMED), else 0."),
    ];

    /// <summary>The run's own, as <c>run.&lt;name&gt;</c>.</summary>
    public static readonly IReadOnlyList<BuiltInMeasure> PerRun =
    [
        new("seconds", "s", "seconds", "The run's length."),
        new("ticks", "ticks", "ticks", "The run's ticks (120 a second)."),
    ];
}

// ---------------------------------------------------------------------------------------------------------------------------
// Built-in checks (section 4.11): the rules package's acceptance negatives, assertable
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>A built-in check's name and its sentence (<c>--describe</c>).</summary>
public sealed record BuiltInCheck(string Name, string Sentence, bool NeedsBow = false);

/// <summary>The built-in checks (section 4.5): each true in a run when nothing in it violates the rule.</summary>
public static class BuiltInChecks
{
    public const string NoHarmWithoutContact = "noHarmWithoutContact";
    public const string BlockedBodyNoWound = "blockedBodyNoWound";
    public const string NoDuplicateHarm = "noDuplicateHarm";
    public const string ProtectedUnharmed = "protectedUnharmed";
    public const string ArrowsAccounted = "arrowsAccounted";

    public static readonly IReadOnlyList<BuiltInCheck> All =
    [
        new(NoHarmWithoutContact, "Every HarmResolved answers a ContactMade of the same contact id (no harm without a contact)."),
        new(BlockedBodyNoWound, "A swing whose blow was stopped at a shield or held or parried at a guard deals no body wound with that contact."),
        new(NoDuplicateHarm, "No contact id is harmed twice."),
        new(ProtectedUnharmed, "No protected person (a ProtectedProtest) took a wound."),
        new(ArrowsAccounted, "Every arrow loosed (ArrowLoosed) met something or came to rest (ArrowSpent) by the run's end.", NeedsBow: true),
    ];
}

// ---------------------------------------------------------------------------------------------------------------------------
// Assertions (section 4.11)
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>How an assertion is checked (section 4.11).</summary>
public enum AssertionScope
{
    /// <summary>In every run; passes at a point when at least <see cref="AssertionSpec.PassShare"/> of its runs pass.</summary>
    EachRun,
    /// <summary>Passes at a point when any run passes.</summary>
    AnyRun,
    /// <summary>The point's <see cref="AssertionSpec.Statistic"/> with its 95 % interval: pass when the whole interval is on the passing side, fail when the whole is on the failing side, else inconclusive.</summary>
    Aggregate,
    /// <summary>Across the sweep's values of <see cref="AssertionSpec.Over"/>: the rank correlation of the value with the point's statistic, with a bootstrap interval over the paired runs.</summary>
    Trend,
}

/// <summary>Which of a point's numbers an aggregate or a trend compares (section 4.11).</summary>
public enum Statistic { Mean, Median, P10, P90, Rate }

/// <summary>A trend's expected direction (section 4.11).</summary>
public enum TrendDirection { Rising, Falling, Flat }

/// <summary>
/// A limit on the points an assertion is checked at: the sweep path and the values it applies to (<c>"points": { path: [values] }</c>),
/// as invariant text the way the compiled point gives its values (<c>"3"</c>, <c>"true"</c>, <c>"BroadSword"</c>; a number matches a
/// number of the same value: <c>"1"</c> matches <c>"1.0"</c>).
/// </summary>
public sealed record PointFilter(string Path, IReadOnlyList<string> Values);

/// <summary>
/// An assertion (section 4.11): a value form, a comparison and a scope. A trend has no comparison (its
/// <see cref="Direction"/> says what passes).
/// </summary>
/// <param name="Name">Its name (free text; unique among the scenario's assertions).</param>
/// <param name="Value">What it checks (a named measure is a <see cref="MeasureRefForm"/>).</param>
/// <param name="Compare">The comparator; null for a trend.</param>
/// <param name="Scope">How it is checked.</param>
/// <param name="PassShare">For <see cref="AssertionScope.EachRun"/>: the share of runs that must pass (default all).</param>
/// <param name="Statistic">For an aggregate or a trend: which number; null: the measure kind's own (a rate's rate, a check's share, otherwise the mean).</param>
/// <param name="Over">For a trend: the sweep path its values are ordered by.</param>
/// <param name="Direction">For a trend: rising, falling or flat.</param>
/// <param name="Points">Limits to some points (null: every point).</param>
/// <param name="Pointer">The JSON pointer of its place in the scenario file (<c>/assertions/2</c>).</param>
/// <param name="FlatWithin">For a flat trend: the band the whole correlation interval must lie in, ± (default 0.2).</param>
public sealed record AssertionSpec(string Name, ValueForm Value, Comparison? Compare, AssertionScope Scope,
    double PassShare = 1.0, Statistic? Statistic = null, string? Over = null, TrendDirection? Direction = null,
    IReadOnlyList<PointFilter>? Points = null, string? Pointer = null, double FlatWithin = 0.2);

/// <summary>An assertion's verdict (section 4.11).</summary>
public enum Verdict
{
    Pass,
    Fail,
    Inconclusive,
    /// <summary>Not checked at this point (its <c>points</c> exclude it, or a trend's per-point line).</summary>
    NotApplicable,
}

// ---------------------------------------------------------------------------------------------------------------------------
// Stop (section 4.8)
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>What a harness stop does to the run (section 4.8).</summary>
public enum HarnessStops
{
    /// <summary>The first stop ends the run (two actors); with sides, one side left ends it. The arena's rule.</summary>
    First,
    /// <summary>Harness stops do not end the run (it ends by time, a condition or the script).</summary>
    Never,
}

/// <summary>One of the stop's conditions: a value form and a comparison, checked after each tick.</summary>
public sealed record StopCondition(ValueForm Value, Comparison Compare, string? Pointer = null);

/// <summary>When a run ends (section 4.8).</summary>
/// <param name="AfterSeconds">The most a run lasts (the arena's <c>seconds</c>).</param>
/// <param name="OnHarnessStops">What a harness stop does.</param>
/// <param name="When">Conditions: the first that holds ends the run with <c>CONDITION:&lt;index&gt;</c>.</param>
public sealed record ScenarioStop(double AfterSeconds = 30, HarnessStops OnHarnessStops = HarnessStops.First,
    IReadOnlyList<StopCondition>? When = null, string OnObjectives = "none");   // none, first, all

/// <summary>
/// The names of a run's end (section 4.8). None is a death: no torpor or Final Death exists. With sides (one against several), a run
/// ends when one side is left, named by the kind of the last stop (each actor's own stop is in actors.csv's <c>stopped</c>).
/// </summary>
public static class StopNames
{
    public const string ZeroHealth = "ZERO_HEALTH_STOP";
    public const string Severed = "SEVERED_STOP";
    public const string Disarmed = "DISARMED";
    /// <summary>The time ran out (<c>stop.afterSeconds</c>): a censored run for any time-to-event measure. (The output contract calls it TIMED_OUT.)</summary>
    public const string Time = "TIMED_OUT";
    /// <summary><c>CONDITION:&lt;index&gt;</c>: the stop's <c>when</c> condition at that index held.</summary>
    public const string ConditionPrefix = "CONDITION:";
    /// <summary><c>SCENARIO_END:&lt;reason&gt;</c>: a script's <c>end</c> step, a <c>"then": "end"</c>, or a trigger's <c>end</c>.</summary>
    public const string ScenarioEndPrefix = "SCENARIO_END:";
    /// <summary>The run threw (exit code 5); its exception is in the run's record.</summary>
    public const string Threw = "THREW";
    /// <summary><c>OBJECTIVES:first</c> or <c>OBJECTIVES:all</c> — the objectives ended the run (<c>stop.onObjectives</c>); the run's <c>ending</c> names which.</summary>
    public const string ObjectivesPrefix = "OBJECTIVES:";
}

// ---------------------------------------------------------------------------------------------------------------------------
// Outputs (section 4.12)
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>Which runs of each point keep a stream (events, ticks).</summary>
public enum TraceRuns { First, All, None }

/// <summary>The per-tick state trace (<c>ticks.jsonl</c>), when asked.</summary>
public sealed record TicksOutput(int EveryTicks = 12, TraceRuns Runs = TraceRuns.First);

/// <summary>What a scenario writes (section 4.12).</summary>
/// <param name="Events">The event stream: the first <see cref="TraceRunCount"/> runs of each point (default), all, or none.</param>
/// <param name="TraceRunCount">How many runs a point keeps streams for with <see cref="TraceRuns.First"/> (the file's <c>traceRuns</c>; default 2).</param>
/// <param name="Ticks">The per-tick trace; null: off.</param>
/// <param name="Csv">The arena's tables (<c>runs.csv</c>, <c>contacts.csv</c>, <c>powers.csv</c>).</param>
/// <param name="SummaryPage">The arena's <c>summary.html</c>.</param>
/// <param name="Tapes">Tapes kept per point (null: as <see cref="TraceRunCount"/>).</param>
/// <param name="Policy">
/// Which runs keep their full event stream and tape (the output contract's retention levels): null for the one
/// <see cref="Events"/> implies (first: <see cref="TracePolicy.Sampled"/>, all: <see cref="TracePolicy.All"/>, none:
/// <see cref="TracePolicy.None"/>). Every run's seed, outcome and measures are kept whatever the policy.
/// </param>
/// <param name="TraceBudgetRuns">The most runs (over all points) whose streams are written, failures first claimed in run order; 0: no limit.</param>
/// <param name="WideBouts">The arena's wide <c>bouts.csv</c> (275 columns), an optional export beside the normalized tables (default on).</param>
public sealed record ScenarioOutputs(TraceRuns Events = TraceRuns.First, int TraceRunCount = 2, TicksOutput? Ticks = null,
    bool Csv = true, bool SummaryPage = true, int? Tapes = null,
    TracePolicy? Policy = null, int TraceBudgetRuns = 0, bool WideBouts = true,
    DecisionsOutput Decisions = DecisionsOutput.Traced);   // which runs carry the brains' decisions (BrainDecided)

/// <summary>
/// Which runs' streams carry the duelling brains' decisions (<c>BrainDecided</c>, linked to what
/// they caused). <see cref="Traced"/> (the default): the runs whose streams are kept from the start (the sampled runs, <c>ALL</c>,
/// <c>--only-run</c>); <see cref="All"/>: every run that may keep a stream (a failure's too); <see cref="None"/>: none. Never an untraced
/// run's outputs, never a digest: a run with decisions is the same run.
/// </summary>
public enum DecisionsOutput { Traced, All, None }

/// <summary>
/// Which runs keep their full event stream (<c>outputs.tracePolicy</c>; the retention levels). <see cref="Sampled"/> is the
/// default: the first <see cref="ScenarioOutputs.TraceRunCount"/> runs of each point and every run that failed an assertion, threw, or
/// broke a built-in check (an anomaly). <see cref="Failures"/> keeps only the latter; <see cref="All"/> every run; <see cref="None"/>
/// none. The per-run rows (seed, outcome, measures) are written for every run under every policy.
/// </summary>
public enum TracePolicy { None, Sampled, Failures, All }

// ---------------------------------------------------------------------------------------------------------------------------
// The measures part of a compiled scenario (what the format work's reader hands the runner beside the program and the cast)
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>
/// The measures part of a compiled scenario: the named measures, the assertions, the stop and the outputs (one for the whole
/// scenario: a point changes values, not what is measured).
/// </summary>
public sealed record ScenarioMeasuresModel(IReadOnlyList<MeasureSpec> Measures, IReadOnlyList<AssertionSpec> Assertions,
    ScenarioStop Stop, ScenarioOutputs Outputs);

// ---------------------------------------------------------------------------------------------------------------------------
// Exit codes (section 5.1)
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>The runner's exit codes (section 5.1). For a folder, the worst of its scenarios' by <see cref="ScenarioExitCodes.Worst"/>.</summary>
public enum ScenarioExit
{
    /// <summary>Ran; every assertion passed (or none were given).</summary>
    Passed = 0,
    /// <summary>The scenario was refused (its errors printed).</summary>
    Refused = 1,
    /// <summary>The modules refused (as the arena's).</summary>
    ModulesRefused = 2,
    /// <summary>Ran; at least one assertion failed.</summary>
    AssertionFailed = 3,
    /// <summary>Ran; none failed, at least one inconclusive.</summary>
    Inconclusive = 4,
    /// <summary>A run threw (its seed and the exception in results.json; the other runs went on).</summary>
    RunThrew = 5,
    /// <summary>Cancelled (Ctrl-C or a <c>CANCEL</c> file in the output folder): the runs that ran are in results.json, state CANCELLED.</summary>
    Cancelled = 6,
}

// ---------------------------------------------------------------------------------------------------------------------------
// Per run (what ScenarioTally gives)
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>
/// One measure's value in one run. <see cref="Value"/> null: none (censored: no such event, the run ended first). A rate
/// keeps <see cref="Numerator"/> and <see cref="Denominator"/>; a delay its <see cref="Samples"/> and how many
/// <see cref="Unanswered"/>; a check its violations (<see cref="Numerator"/>) and cases (<see cref="Denominator"/>).
/// </summary>
/// <param name="FollowedSeconds">For a time: how long the run was watched (its length when the time is censored), for the censored estimators.</param>
public sealed record RunValue(double? Value, double? Numerator = null, double? Denominator = null,
    IReadOnlyList<double>? Samples = null, int Unanswered = 0, double? FollowedSeconds = null);

/// <summary>A step that fell short in a run (the director's <c>StepRefused</c>, <c>StepTimedOut</c>, <c>StepLate</c>).</summary>
/// <param name="Actor">The actor's name.</param>
/// <param name="Event">The director's event kind.</param>
/// <param name="Step">The step's kind (<c>moveTo</c>), and its label when it has one.</param>
/// <param name="Seconds">When (seconds of the run).</param>
/// <param name="Reason">Why (<c>NoSlot</c>, …), when the event gives one.</param>
public sealed record StepShortfall(string Actor, string Event, string Step, string? Label, double Seconds, string? Reason = null);

/// <summary>One run's record in <c>results.json</c>.</summary>
/// <param name="Run">Its number at the point (0 up).</param>
/// <param name="Seed">Its seed: the scenario's seed plus <see cref="Run"/> (paired across points).</param>
/// <param name="Stop">What ended it (<see cref="StopNames"/>).</param>
/// <param name="Seconds">Its length (s).</param>
/// <param name="Ticks">Its ticks.</param>
/// <param name="Digest">The world's digest at its end (hex).</param>
/// <param name="Measures">Every measure's value (built-in and named) by name; null where none.</param>
/// <param name="Shortfalls">Steps that fell short.</param>
/// <param name="Error">The exception's type and message when it threw.</param>
/// <param name="Events">The event stream's file (relative to the output folder), when kept.</param>
/// <param name="Tape">The tape's file, when kept.</param>
/// <param name="Winner">The actor whose foe's stop ended it, when one did.</param>
/// <param name="RunId">Its id, <c>p000-r003</c> (the join key of every table: <c>runs.csv</c>, <c>actors.csv</c>, <c>actions.csv</c>).</param>
/// <param name="Valid">False when the run could not honour the scenario (a step refused as not built, or naming nobody); its numbers are kept but counted apart.</param>
/// <param name="Anomalies">What made it a failure for the trace policy (an assertion failed in it, a check broken, it threw).</param>
public sealed record RunRecord(int Run, ulong Seed, string Stop, double Seconds, long Ticks, string Digest,
    IReadOnlyDictionary<string, double?> Measures, IReadOnlyList<StepShortfall>? Shortfalls = null, string? Error = null,
    string? Events = null, string? Tape = null, string? Winner = null,
    string RunId = "", bool Valid = true, IReadOnlyList<string>? Anomalies = null,
    IReadOnlyList<ObjectiveOutcome>? Objectives = null, string? Ending = null);   // each objective's result, and what ended the run in words

// ---------------------------------------------------------------------------------------------------------------------------
// Per point (aggregates and verdicts)
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>
/// One measure's aggregate over a point's runs (section 4.10). <see cref="N"/>: the runs (or, for a delay, the samples) with a
/// value; <see cref="Censored"/>: those without. A rate: <see cref="Numerator"/>, <see cref="Denominator"/>,
/// <see cref="Value"/> (their quotient) and Wilson's <see cref="Interval95"/>. A count, value, time or delay: mean, median,
/// p10, p90, min, max and the mean's bootstrap <see cref="Interval95"/> (a fixed seed). A check: the share of runs it held
/// in as <see cref="Value"/> with Wilson's interval, the violations and cases summed.
/// </summary>
/// <param name="KaplanMeierMedian">For a time: the Kaplan–Meier median over every run, a run without the event censored at its end (null when
/// fewer than half the runs had it), and its bootstrap interval.</param>
/// <param name="RestrictedMeanSeconds">For a time: the Kaplan–Meier restricted mean up to <paramref name="RestrictedToSeconds"/> (the shortest run's length).</param>
public sealed record MeasureAggregate(string Kind, string? Unit, int N, int Censored = 0,
    double? Mean = null, double? Median = null, double? P10 = null, double? P90 = null, double? Min = null, double? Max = null,
    double? Numerator = null, double? Denominator = null, double? Value = null, double[]? Interval95 = null,
    string? IntervalMethod = null, double? KaplanMeierMedian = null, double[]? KaplanMeierInterval95 = null,
    double? RestrictedMeanSeconds = null, double? RestrictedToSeconds = null);

/// <summary>An assertion's result at one point (section 4.11).</summary>
/// <param name="Name">The assertion's name.</param>
/// <param name="Scope">eachRun, anyRun, aggregate.</param>
/// <param name="Result">pass, fail, inconclusive, notApplicable.</param>
/// <param name="Measured">The number compared (eachRun/anyRun: the share of runs that passed; aggregate: the statistic).</param>
/// <param name="Interval95">The measured number's 95 % interval (aggregate; eachRun: Wilson's over the runs' pass share).</param>
/// <param name="Statistic">The statistic compared (aggregate).</param>
/// <param name="Threshold">The comparator as written (<c>atMost 40</c>, <c>between [1, 3]</c>).</param>
/// <param name="N">The runs (or samples) the number came from.</param>
/// <param name="RunsPassed">eachRun/anyRun: runs that passed.</param>
/// <param name="Runs">eachRun/anyRun: runs checked (those with a value; the rest counted as failing).</param>
/// <param name="PassShare">eachRun: the share required.</param>
/// <param name="FirstFailingSeed">The first failing run's seed (so it can be replayed alone).</param>
/// <param name="Note">Why inconclusive or not applicable, in a sentence.</param>
public sealed record AssertionPointResult(string Name, string Scope, string Result, double? Measured = null,
    double[]? Interval95 = null, string? Statistic = null, string? Threshold = null, int N = 0, int? RunsPassed = null,
    int? Runs = null, double? PassShare = null, ulong? FirstFailingSeed = null, string? Note = null,
    IReadOnlyList<string>? Evidence = null, string? FirstFailingRun = null);

/// <summary>A trend's result across the points (section 4.11): the rank correlation and its bootstrap interval.</summary>
/// <param name="Over">The sweep path.</param>
/// <param name="Direction">rising, falling, flat.</param>
/// <param name="Correlation">Spearman's rank correlation of the sweep value with the point's statistic.</param>
/// <param name="Interval95">Its 95 % interval, by a bootstrap over the paired runs (a fixed seed).</param>
/// <param name="ByValue">The point's statistic at each sweep value, in the values' order.</param>
public sealed record TrendResult(string Over, string Direction, string Statistic, double? Correlation, double[]? Interval95,
    IReadOnlyList<TrendPoint> ByValue, double? FlatWithin = null);

/// <summary>One sweep value of a trend: the value, the point(s) at it, their statistic and n.</summary>
public sealed record TrendPoint(JsonNode? Value, IReadOnlyList<int> Points, double? Statistic, int N);

/// <summary>An assertion's result over the whole scenario: the worst of its points' (fail, then inconclusive, then pass).</summary>
/// <param name="ByPoint">Each point's result, in the points' order (notApplicable where excluded).</param>
/// <param name="Trend">A trend's correlation.</param>
/// <param name="Kind">DETERMINISTIC (eachRun, anyRun: exact from the runs' receipts) or STATISTICAL (aggregate, trend: by a 95 % interval).</param>
/// <param name="Evidence">Where to look: the failing runs' event streams and event ids (<c>events/p000-r003.events.jsonl#41</c>), when kept.</param>
public sealed record AssertionResult(string Name, string Scope, string Result, IReadOnlyList<string> ByPoint,
    TrendResult? Trend = null, string? Pointer = null, string Kind = "DETERMINISTIC", IReadOnlyList<string>? Evidence = null);

/// <summary>One point's record in <c>results.json</c> (section 5.5).</summary>
/// <param name="Index">The point's index (0 up).</param>
/// <param name="Name">A name made of its values (<c>Christof.sheet.values.melee 3</c>).</param>
/// <param name="Values">The swept paths' values at this point.</param>
/// <param name="RunCount">Runs run.</param>
/// <param name="Seeds">The first and last seed.</param>
/// <param name="Ends">Runs by what ended them (<see cref="StopNames"/>), in the names' order.</param>
/// <param name="Control">Each actor's control as words (<c>script, then AI</c>; <c>AI (muted: attack, move)</c>).</param>
/// <param name="Controlled">True when any actor's AI was muted or scripted over: the runs measure the thing controlled, not play.</param>
/// <param name="Measures">Each measure's aggregate by name (built-in and named).</param>
/// <param name="Assertions">Each assertion's result here (trends are whole-scenario only).</param>
/// <param name="Shortfalls">Steps that fell short, counted by actor, step and event (<c>Christof moveTo "close in" StepTimedOut</c>).</param>
/// <param name="Runs">Each run's record — null in results.json (a compact summary): every run's row is in <c>runs.csv</c> (and <c>actors.csv</c>, <c>actions.csv</c>).</param>
/// <param name="Threw">Runs that threw.</param>
/// <param name="Reloaded">True when this point loaded the content again (its gear or data differ).</param>
public sealed record PointResult(int Index, string Name, IReadOnlyDictionary<string, JsonNode?> Values, int RunCount,
    ulong[] Seeds, IReadOnlyDictionary<string, int> Ends, IReadOnlyDictionary<string, string> Control, bool Controlled,
    IReadOnlyDictionary<string, MeasureAggregate> Measures, IReadOnlyList<AssertionPointResult> Assertions,
    IReadOnlyDictionary<string, int> Shortfalls, IReadOnlyList<RunRecord>? Runs, int Threw = 0, bool Reloaded = false,
    string ExecutionMode = "NATIVE_AI", RunCounts? RunCounts = null, JsonArray? Setup = null, IReadOnlyList<string>? ActorModes = null,
    IReadOnlyDictionary<string, string>? ConfigHashes = null,   // each module's configuration hash at this point, when its data was overridden there (null otherwise: provenance's)
    IReadOnlyList<ObjectiveAggregate>? Objectives = null);   // each objective over the point's runs (null: the scenario gives none)

// ---------------------------------------------------------------------------------------------------------------------------
// The results document (VITOMIR_SCENARIO_RESULTS_1, section 5.5)
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>The scenario file's identity.</summary>
public sealed record ScenarioIdentity(string File, string Sha256, string Format, string? Title, string? Notes = null);

/// <summary>A module's identity in the provenance.</summary>
public sealed record ModuleProvenance(string ConfigHash, string? Profile, bool Research, string? Version = null);

/// <summary>The level a scenario ran on.</summary>
public sealed record LevelProvenance(string? Ground, string Path, string? Sha256);

/// <summary>Where and how the results were made (section 5.5): enough to make them again.</summary>
/// <param name="Build">The program's version (<c>Vitomir Platform an earlier build</c>).</param>
/// <param name="Commit">The git commit when known (from the build or the tree), else null.</param>
/// <param name="DirtyTree">True when the tree had changes not committed, null when unknown.</param>
/// <param name="Machine">The machine's name.</param>
/// <param name="Game">The game played (<c>enhanced</c>).</param>
/// <param name="Modules">Each module's configuration hash and profile.</param>
/// <param name="Switches">The switches on (the command line's and the scenario's).</param>
/// <param name="Level">The level.</param>
/// <param name="Evidence">NATIVE: the engine's own systems produced every number (no synthetic data).</param>
/// <param name="CommandLine">The command line.</param>
/// <param name="StartedAt">When it started (ISO 8601 with offset).</param>
/// <param name="WallSeconds">How long it took.</param>
/// <param name="ResultsDigest">The hash of every run's end digest in order: two machines compare a scenario with one string.</param>
/// <param name="ConfigurationHash">The hash of the resolved scenario and the modules' configuration hashes together.</param>
/// <param name="Seed">The seed base; run r has seed + r at every point.</param>
/// <param name="Runs">Runs per point.</param>
/// <param name="CommandLineOverrides">The command line's overrides of the file (<c>--runs 20</c>, <c>--seed 7</c>, <c>--only 2</c>).</param>
/// <param name="Jobs">Workers.</param>
/// <param name="Sandbox">True with <c>--sandbox</c>.</param>
/// <param name="Branch">The git branch when known.</param>
/// <param name="WorkingTreeDiffSha256">The SHA-256 of <c>git diff HEAD</c> when the tree is dirty (so an experiment's patch is fingerprinted), null when clean or unknown.</param>
/// <param name="ScenarioSha256">The scenario file's SHA-256.</param>
/// <param name="ResolvedSha256">The resolved scenario's SHA-256 (every default explicit: resolved.json).</param>
/// <param name="OverridesSha256">The SHA-256 of what the run changed from the game's own (switches, tempo, every point's swept values, the command line's overrides).</param>
/// <param name="ProfileSha256">The modules' configuration hashes together (their data and profile).</param>
/// <param name="RngPolicyId">How seeds are given: run r of every point has seed + r; the director draws nothing; each system its own stream.</param>
/// <param name="SourceFrom">Where <paramref name="Commit"/> came from: <c>clone</c> (the program's own clone, read live), <c>package</c>
/// (the packaging script's <c>build-info.json</c>) or <c>build</c> (written into the program when it was built).</param>
/// <param name="CloneCommit">When the program's clone has moved to another commit since it was built: that commit (the run's code is the build's).</param>
public sealed record Provenance(string Build, string? Commit, bool? DirtyTree, string Machine, string Game,
    IReadOnlyDictionary<string, ModuleProvenance> Modules, IReadOnlyList<string> Switches, LevelProvenance? Level,
    string Evidence, string CommandLine, string StartedAt, double WallSeconds, string ResultsDigest,
    string ConfigurationHash, ulong Seed, int Runs, IReadOnlyList<string> CommandLineOverrides, int Jobs, bool Sandbox,
    string? Branch = null, string? WorkingTreeDiffSha256 = null, string ScenarioSha256 = "", string ResolvedSha256 = "",
    string OverridesSha256 = "", string ProfileSha256 = "", string RngPolicyId = "vitomir-seed-plus-run/1",
    string? SourceFrom = null, string? CloneCommit = null);

/// <summary>The scenario's counts and its exit code.</summary>
public sealed record ResultsSummary(int Points, int Runs, int Passed, int Failed, int Inconclusive, int Threw, int ExitCode,
    string Verdict);

/// <summary>The files written beside <c>results.json</c> (relative paths; null where not written).</summary>
public sealed record ResultsFiles(string? Runs, string? Contacts, string? Powers, string? Events, string? Ticks,
    string? Tapes, string? SummaryPage, string Scenario, string Resolved, string? Log);

/// <summary>
/// <c>results.json</c> (section 5.5): one per scenario run, small, self-describing, versioned. A machine reads
/// <see cref="Summary"/> and <see cref="Assertions"/> first, then the points' aggregates, then opens the files.
/// </summary>
/// <param name="State">COMPLETED (every run asked for ran), CANCELLED (stopped by Ctrl-C or a CANCEL file: what ran is here), FAILED (it could not run).</param>
/// <param name="RunCounts">The runs asked for, completed, valid, censored (ended by time), invalid, failed in execution, cancelled.</param>
/// <param name="Comparisons">Each point against point 0 on the same seeds (paired): matched seeds, the outcomes that changed.</param>
/// <param name="Metrics">The scenario's named measures at each point, one entry each: estimate (null when not observed), unit, independent n (runs), the denominator, the 95 % interval and its method.</param>
/// <param name="NotCovered">The stages and receipts this build does not report yet (declared, not silently missing).</param>
public sealed record ScenarioResults(string Schema, ScenarioIdentity Scenario, Provenance Provenance,
    IReadOnlyList<PointResult> Points, IReadOnlyList<AssertionResult> Assertions, ResultsSummary Summary, ResultsFiles Files,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> CsvColumns, IReadOnlyDictionary<string, string> MeasureUnits,
    string State = "COMPLETED", RunCounts? RunCounts = null, IReadOnlyList<PairedComparison>? Comparisons = null,
    IReadOnlyList<MetricResult>? Metrics = null, IReadOnlyList<string>? NotCovered = null,
    IReadOnlyList<RunReceipts>? Receipts = null,   // the kept runs' receipts (ScenarioReceipts.cs)
    IReadOnlyList<SwapComparison>? Swaps = null)   // each point against its swapped twin on the same seeds (null: no swap)
{
    /// <summary>The results document's schema name (section 5.5).</summary>
    public const string SchemaName = "VITOMIR_SCENARIO_RESULTS_1";
}

/// <summary>Runs by what became of them (the output contract: completed, invalid, cancelled and censored kept apart).</summary>
/// <param name="Requested">Points × runs asked for.</param>
/// <param name="Completed">Ran to an end (any stop).</param>
/// <param name="Valid">Completed and honoured the scenario.</param>
/// <param name="Censored">Completed by time (TIMED_OUT): censored for any time-to-event measure.</param>
/// <param name="Invalid">Completed but could not honour the scenario (a step not built, a name not in the world).</param>
/// <param name="FailedExecution">Threw.</param>
/// <param name="Cancelled">Never ran (cancelled).</param>
public sealed record RunCounts(int Requested, int Completed, int Valid, int Censored, int Invalid, int FailedExecution, int Cancelled);

/// <summary>A paired comparison: a point against the control point on the same seeds.</summary>
/// <param name="MatchedSeedCount">Seeds completed at both points.</param>
/// <param name="OutcomesChanged">Of them, the runs whose outcome (the stop and who won) differs.</param>
/// <param name="DigestsDiffer">Of them, the runs whose end digest differs (the world went another way).</param>
/// <param name="Differences">Each named measure's per-seed difference (treatment − control) over the seeds where both runs had a value: the mean, a paired bootstrap interval, how many pairs differed.</param>
public sealed record PairedComparison(string ComparisonId, int ControlPoint, int TreatmentPoint, bool Paired, int MatchedSeedCount,
    int OutcomesChanged, int DigestsDiffer, string? Control = null, string? Treatment = null, IReadOnlyList<PairedDifference>? Differences = null);

/// <summary>A named measure's paired difference between two points on the same seeds (a matched contrast).</summary>
/// <param name="Pairs">Seeds where both runs had a value.</param>
/// <param name="MeanDifference">The mean of treatment − control over them (null with none).</param>
/// <param name="Interval95">Its 95 % interval by a bootstrap over the pairs (a fixed seed).</param>
/// <param name="PairsChanged">Pairs whose values differ.</param>
public sealed record PairedDifference(string MetricId, int Pairs, double? MeanDifference, Interval95? Interval95, int PairsChanged);

/// <summary>
/// A point run as written against its twin with the swap made (two actors' places and facings exchanged, or the sides'
/// ground turned round), seed for seed (paired: run r of both has seed + r): how many seeds matched, how many changed their outcome (the
/// stop, who won, an objective's result) and their end digest, each objective's result counted both ways and the pairs where it flipped,
/// and each named measure's paired difference (swapped − as written).
/// </summary>
public sealed record SwapComparison(string ComparisonId, int Point, int SwappedPoint, string Swap, int MatchedSeedCount, int OutcomesChanged,
    int DigestsDiffer, IReadOnlyList<SwapObjective>? Objectives, IReadOnlyList<PairedDifference>? Differences);

/// <summary>One objective under a swap: met as written and swapped, and the seeds whose result flipped (each way).</summary>
public sealed record SwapObjective(string Name, int MetAsWritten, int MetSwapped, int FlippedToMet, int FlippedFromMet, int OtherChanges);

/// <summary>A 95 % interval and how it was made (<c>wilson</c> for one trial a run; a bootstrap over runs otherwise: the run is the unit of independence).</summary>
public sealed record Interval95(double? Low, double? High, string Method);

/// <summary>One named measure at one point (a metric): the estimate (null when nothing was observed: never a made-up 0).</summary>
/// <param name="NIndependent">The runs with a value (the unit of independence is the run, not the contact).</param>
/// <param name="DenominatorCount">A rate's denominator summed over the runs (its events or seconds), null for other kinds.</param>
public sealed record MetricResult(string MetricId, int Point, string Kind, string Unit, double? Estimate, string Statistic, int NIndependent,
    double? Numerator, double? DenominatorCount, int Censored, Interval95? Interval95, int Runs);

/// <summary>One scenario's line in <c>batch.json</c> (a folder run).</summary>
public sealed record BatchEntry(string File, string Out, int ExitCode, string Verdict, int Passed, int Failed, int Inconclusive,
    int Threw, string? Error = null);

/// <summary><c>batch.json</c>: a folder's scenarios in name order and the worst exit code.</summary>
public sealed record BatchResults(string Schema, string StartedAt, double WallSeconds, IReadOnlyList<BatchEntry> Scenarios,
    int ExitCode)
{
    public const string SchemaName = "VITOMIR_SCENARIO_BATCH_1";
}

// ---------------------------------------------------------------------------------------------------------------------------
// The event stream (VITOMIR_EVENT_TRACE_2, section 5.5) and the tick trace: the schema's records
// ---------------------------------------------------------------------------------------------------------------------------

/// <summary>One field of an event kind (<c>--describe events</c>): its name as the stream writes it, its JSON type, unit and whether it names an actor.</summary>
/// <param name="Values">An enumeration's names, when it is one.</param>
/// <param name="Derived">True for a field the scenario stream adds (<c>region</c>, <c>verdict</c>, <c>power</c>, …).</param>
public sealed record EventFieldDoc(string Name, string Type, string? Unit, bool Actor, IReadOnlyList<string>? Values = null,
    bool Derived = false, string? Sentence = null);

/// <summary>One event kind of the stream (<c>--describe events</c>): its name, where it comes from, its fields, the field that is its <c>by</c> and its <c>on</c>.</summary>
public sealed record EventKindDoc(string Kind, string Source, string Sentence, IReadOnlyList<EventFieldDoc> Fields,
    string? ByField, string? OnField);

/// <summary>One actor's state on a line of <c>ticks.jsonl</c> (section 5.5).</summary>
public sealed record TickActor(string Name, float[] Feet, float FacingDegrees, float[] Velocity, float Speed, string? Gait,
    string? Action, string? Phase, string? Direction, bool Held, int? Health, int? Blood, string? LivePowers,
    float? DistanceToFoe, string? Step, IReadOnlyDictionary<string, string>? Channels,
    TickPlayer? Player = null);   // the player-input actor's receipts (null for every other actor)

/// <summary>
/// The receipts of the actor driven through the player's own controls, on each traced tick.
/// <paramref name="Input"/> the frame fed in that tick (the move keys −1 to 1, the look turned this tick in degrees, the buttons held and
/// pressed); <paramref name="Camera"/> the view as the camera left it after the tick (its kind: <c>firstPerson</c>, <c>rightShoulder</c>,
/// <c>leftShoulder</c>; the eye, its yaw and pitch, the field of view, the third-person boom); <paramref name="Aim"/> the crosshair's ray (from
/// the eye along the view) and what it meets first (the reticle's own answer: the level, an actor and its part, or nothing; its point and
/// distance from the body's eyes; blocked when, in third person, something nearer stands between the eyes and it); <paramref name="Requested"/>
/// the movement the controls asked of the body this tick (the way, level, unit; the gait and its speed); <paramref name="Actual"/> what the
/// body did (its displacement since the last traced line and its speed over it, and this tick's velocity along the requested way);
/// <paramref name="BlockedBy"/> the world in the way when the body made under half the requested speed along it (derived: a sphere swept
/// from the waist along the requested way, 0.6 m; the level's point and its normal), else null; <paramref name="NotRecorded"/> what is not here.
/// </summary>
public sealed record TickPlayer(TickInput Input, TickCamera? Camera, TickAim? Aim, TickMove Requested, TickMove Actual, TickBlock? BlockedBy,
    IReadOnlyList<string> NotRecorded);

/// <summary>The frame fed in: the move keys, the look turned this tick (degrees, left and up positive), the buttons held and pressed on it.</summary>
public sealed record TickInput(float MoveRight, float MoveForward, float TurnLeftDegrees, float TurnUpDegrees, IReadOnlyList<string> Held, IReadOnlyList<string> Pressed);

/// <summary>The camera: its kind, the eye, yaw and pitch (degrees), field of view, the third person's boom (m) and orbit (free look, degrees).</summary>
public sealed record TickCamera(string View, float[] Eye, float YawDegrees, float PitchDegrees, float FieldOfViewDegrees, float? BoomMetres, float? OrbitDegrees);

/// <summary>The crosshair's ray and what it meets first: <c>level</c>, <c>actor</c> (its name and part), or <c>nothing</c> within its reach.</summary>
public sealed record TickAim(float[] Origin, float[] Direction, string Meets, string? Actor, string? Part, float[]? Point, float? DistanceMetres, bool Blocked);

/// <summary>A movement: the way (level, unit; [0, 0] none), the gait, the speed (m/s); the displacement since the last line (m) and over how long (s).</summary>
public sealed record TickMove(float[] Way, string? Gait, float SpeedMetresPerSecond, float[]? Displacement = null, float? OverSeconds = null, float? AlongWayMetresPerSecond = null);

/// <summary>What stopped the body: <c>level</c>, the point met and the surface's normal; how it was found.</summary>
public sealed record TickBlock(string What, float[] Point, float[] Normal, string How);

/// <summary>One line of <c>ticks.jsonl</c>.</summary>
public sealed record TickLine(long Tick, double Seconds, IReadOnlyList<TickActor> Actors);
