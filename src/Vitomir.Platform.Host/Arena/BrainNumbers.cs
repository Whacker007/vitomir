// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Combat;

namespace Vitomir.Platform.Host.Arena;

/// <summary>What kind of value a brain number is.</summary>
public enum BrainNumberKind
{
    /// <summary>A number between its least and its most.</summary>
    Number,
    /// <summary>true or false.</summary>
    Flag,
    /// <summary>A whole number between its least and its most.</summary>
    Count,
    /// <summary>The pace: <c>"none"</c>, or <c>[least, most]</c> seconds of pause after a run of blows.</summary>
    Pace,
}

/// <summary>
/// One of the duelling brain's named numbers as a spec gives it (the arena's 36 numbers made one table, shared by
/// the arena's spec, the scenario's <c>ai</c> and <c>--describe brain</c>, so their names, ranges and defaults cannot drift):
/// its name in a spec, its kind, its range, its unit, one sentence and how it is read from and written to
/// <see cref="DuelBrainSettings"/>. The values are SANDBOX or ENGINE_SANDBOX (the brain's own; <c>Combat/DuelBrain.cs</c>).
/// </summary>
public sealed record BrainNumber(string Name, BrainNumberKind Kind, float Least, float Most, string Unit, string Meaning,
    Func<DuelBrainSettings, float> Get, Func<DuelBrainSettings, float, DuelBrainSettings> Set);

/// <summary>The duelling brain's 36 named numbers (the arena's <c>brain</c>, the scenario's <c>ai</c>), in the arena's order.</summary>
public static class BrainNumbers
{
    static BrainNumber N(string name, float lo, float hi, string unit, string meaning, Func<DuelBrainSettings, float> get, Func<DuelBrainSettings, float, DuelBrainSettings> set)
        => new(name, BrainNumberKind.Number, lo, hi, unit, meaning, get, set);

    static BrainNumber F(string name, string meaning, Func<DuelBrainSettings, bool> get, Func<DuelBrainSettings, bool, DuelBrainSettings> set)
        => new(name, BrainNumberKind.Flag, 0f, 1f, "true/false", meaning, d => get(d) ? 1f : 0f, (d, v) => set(d, v != 0f));

    /// <summary>Every number, in the order the arena reads them (the pace and its run last).</summary>
    public static readonly IReadOnlyList<BrainNumber> All =
    [
        N("strongShare", 0f, 1f, "share", "the share of its blows made strong (attack held past the strong threshold), the rest quick", d => d.StrongShare, (d, v) => d with { StrongShare = v }),
        N("highShare", 0f, 1f, "share", "the share of its blows aimed high (taken in proportion with levelShare and lowShare)", d => d.HighShare, (d, v) => d with { HighShare = v }),
        N("levelShare", 0f, 1f, "share", "the share of its blows aimed level", d => d.LevelShare, (d, v) => d with { LevelShare = v }),
        N("lowShare", 0f, 1f, "share", "the share of its blows aimed low", d => d.LowShare, (d, v) => d with { LowShare = v }),
        N("highDegrees", -90f, 90f, "degrees", "the pitch of a high blow, up positive (the aim offset clamps to 25 degrees up)", d => d.HighDegrees, (d, v) => d with { HighDegrees = v }),
        N("lowDegrees", -90f, 90f, "degrees", "the pitch of a low blow (the aim offset clamps to 40 degrees down)", d => d.LowDegrees, (d, v) => d with { LowDegrees = v }),
        N("blockChance", 0f, 1f, "chance", "the chance of raising its block when its foe begins a blow within its reach (the guard by skill sets it from Dexterity and Melee unless guard-skill-off)", d => d.BlockChance, (d, v) => d with { BlockChance = v }),
        N("reactSeconds", 0f, 10f, "s", "how long after the foe's blow begins it asks for the block (its reaction; the guard by skill sets it unless guard-skill-off)", d => d.ReactSeconds, (d, v) => d with { ReactSeconds = v }),
        N("blockSeconds", 0f, 60f, "s", "how long it holds a raised block", d => d.BlockSeconds, (d, v) => d with { BlockSeconds = v }),
        N("backOffChance", 0f, 1f, "chance", "the chance of backing off a step after its own blow ends", d => d.BackOffChance, (d, v) => d with { BackOffChance = v }),
        N("backOffSeconds", 0f, 10f, "s", "how long a back-off walks away from its foe", d => d.BackOffSeconds, (d, v) => d with { BackOffSeconds = v }),
        N("strikeShare", 0.1f, 3f, "share of reach", "it begins a blow when its foe is within this share of its reach, centre to centre", d => d.StrikeShare, (d, v) => d with { StrikeShare = v }),
        N("closeShare", 0.1f, 3f, "share of reach", "it closes to this share of its reach and stops there", d => d.CloseShare, (d, v) => d with { CloseShare = v }),
        N("tooCloseShare", 0f, 3f, "share of reach", "nearer than this share of its reach it steps back to it at a walk", d => d.TooCloseShare, (d, v) => d with { TooCloseShare = v }),
        N("walkWithin", 0f, 100f, "m", "it closes at a run beyond this distance from its foe and walks within it", d => d.WalkWithin, (d, v) => d with { WalkWithin = v }),
        N("powerShare", 0f, 100f, "share of reach", "it uses a power while its foe is within this share of its reach (engaged)", d => d.PowerShare, (d, v) => d with { PowerShare = v }),
        N("renewSeconds", 0f, 3600f, "s", "it presses a live power again (paying again) when its time left falls under this, while engaged", d => d.RenewSeconds, (d, v) => d with { RenewSeconds = v }),
        N("powerWaitSeconds", 0f, 60f, "s", "after pressing a slot it presses none for this long", d => d.PowerWaitSeconds, (d, v) => d with { PowerWaitSeconds = v }),
        F("paceWithPowers", "while a power of its own is live it holds its blocks and back-offs for its usual times divided by its body's tempo; false: real time always", d => d.PaceWithPowers, (d, v) => d with { PaceWithPowers = v }),
        N("healBelow", 0f, 1f, "share of Health", "it presses a healing slot while its Health is below this share of its maximum (0: never)", d => d.HealBelow, (d, v) => d with { HealBelow = v }),
        N("dodgeChance", 0f, 1f, "chance", "the chance of dodging, instead of blocking, when its foe begins a blow within its reach (0: never, and no draw)", d => d.DodgeChance, (d, v) => d with { DodgeChance = v }),
        N("dodgeBackShare", 0f, 1f, "share", "the share of its dodges made straight back (in proportion with dodgeSideShare)", d => d.DodgeBackShare, (d, v) => d with { DodgeBackShare = v }),
        N("dodgeSideShare", 0f, 1f, "share", "the share of its dodges made to a side (left or right evenly)", d => d.DodgeSideShare, (d, v) => d with { DodgeSideShare = v }),
        N("dodgeWaitSeconds", 0f, 10f, "s", "how long after its reaction a dodge may still wait for its own blow to stop playing before it is given up", d => d.DodgeWaitSeconds, (d, v) => d with { DodgeWaitSeconds = v }),
        F("dodgeHoldOff", "after a dodge it does not close in again while the blow it dodged still plays", d => d.DodgeHoldOff, (d, v) => d with { DodgeHoldOff = v }),
        F("dodgeAtStrike", "a chosen dodge is pressed timed to the blow's strike rather than as soon as its reaction has passed", d => d.DodgeAtStrike, (d, v) => d with { DodgeAtStrike = v }),
        N("parryChance", 0f, 1f, "chance", "the chance of parrying, instead of blocking, when its foe begins a blow within its reach (0: never; the guard by skill sets it unless guard-skill-off)", d => d.ParryChance, (d, v) => d with { ParryChance = v }),
        N("parryLeadSeconds", 0f, 2f, "s", "how long before the foe's blow is due to strike its parry's press comes", d => d.ParryLeadSeconds, (d, v) => d with { ParryLeadSeconds = v }),
        N("leftShare", 0f, 100f, "weight", "the weight of its blows from the left (in proportion with the other three directions it has blows for)", d => d.LeftShare, (d, v) => d with { LeftShare = v }),
        N("rightShare", 0f, 100f, "weight", "the weight of its blows from the right", d => d.RightShare, (d, v) => d with { RightShare = v }),
        N("overheadShare", 0f, 100f, "weight", "the weight of its overhead blows", d => d.OverheadShare, (d, v) => d with { OverheadShare = v }),
        N("thrustShare", 0f, 100f, "weight", "the weight of its thrusts", d => d.ThrustShare, (d, v) => d with { ThrustShare = v }),
        N("awayFromGuardShare", 0f, 1f, "chance", "the chance, when its foe's guard is held to one side, of striking from the other side", d => d.AwayFromGuardShare, (d, v) => d with { AwayFromGuardShare = v }),
        N("heldParryGiveUpSeconds", 0f, 30f, "s", "how long it waits on a blow its foe holds ready before it gives the parry up and is free to act", d => d.HeldParryGiveUpSeconds, (d, v) => d with { HeldParryGiveUpSeconds = v }),
        new("pace", BrainNumberKind.Pace, 0f, 60f, "s", "the pause after a run of blows: \"none\", or [least, most] seconds drawn (0 to 60, rising); not given: its kind of person's pace (data/combat/guard.json's brain_pace)",
            d => d.PaceMostSeconds, (d, v) => d),
        new("paceRun", BrainNumberKind.Count, 0f, 10f, "blows", "the longest run of blows before a pause (1 to this, drawn) with a pace given; 0: its kind's (else 2)",
            d => d.PaceRunMost, (d, v) => d with { PaceRunMost = (int)v }),
    ];

    /// <summary>The names, in order (what the arena's <c>brain</c> and the scenario's <c>ai</c> take beside <c>kind</c>).</summary>
    public static readonly string[] Names = [.. All.Select(n => n.Name)];

    /// <summary>The number by its name (exactly), or null.</summary>
    public static BrainNumber? Find(string name)
    {
        foreach (var n in All) if (n.Name == name) return n;
        return null;
    }
}
