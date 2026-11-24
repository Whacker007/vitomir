// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the commit point in the air.
using System.Numerics;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// The commit point in the air committed (for the log): to what, the seconds to its contact foreseen, the lip (a catch's, one fallen past, a top
/// cleared), the way the body went at it (+1 rising, −1 falling), whether a catch was the jump's own (else use held), and where the feet were.
/// </summary>
public readonly record struct AirCommitted(EntityId Entity, long Tick, AirOutcome Outcome, float Seconds, Vector3 Lip, int Way, bool Auto, Vector3 Feet,
                                           CatchRefused Why = CatchRefused.None);

/// <summary>Why a lip within reach was not caught (the log's): use not held, no room to hang, the catch's path blocked, the lip ending under the hands, a face not to be climbed, the lip gone before the catch began.</summary>
public enum CatchRefused
{
    None = 0,
    UseNotHeld = 1,
    NoRoom = 2,
    PathBlocked = 3,
    LipEnds = 4,
    NoClimb = 5,
    LipGone = 6,
}

/// <summary>What a body in the air has committed to (<see cref="AirCommit.Outcome"/>).</summary>
public enum AirOutcome
{
    /// <summary>Nothing yet: the contact foreseen is further than the commit's lead.</summary>
    None = 0,
    /// <summary>The hands catch a lip: a grab's clip carries the capsule from the commit to the hands' contact and into the hang.</summary>
    Catch = 1,
    /// <summary>The feet clear the top the arc meets: the body lands on it (or flies over a thin one), nothing caught.</summary>
    Top = 2,
    /// <summary>The hands would reach a lip but nothing asks for the catch: the body falls past it; the next commit is looked for.</summary>
    Past = 3,
    /// <summary>The ground below comes first: the landing.</summary>
    Ground = 4,
}

/// <summary>
/// <b>the commit point's numbers</b> for the motor (the people's <c>PersonCommitSettings</c>, handed here as plain numbers), null for a body without it.
/// </summary>
public sealed class AirCommitRules
{
    /// <summary>How long before the contact foreseen the body commits to one outcome (s; "about 0.3 s").</summary>
    public float Lead { get; init; } = 0.3f;

    /// <summary>Whether a jump taken at a wall catches its lip by itself (the lip's way in within <see cref="AutoCone"/> of the jump's way).</summary>
    public bool AutoCatch { get; init; } = true;

    /// <summary>The cone about the jump's way (degrees each side) in which a lip met is the one the jump was taken at.</summary>
    public float AutoCone { get; init; } = 60f;

    /// <summary>
    /// The rules' version — 2: a jump's descent catches lips under its take-off, a lip met at the wall past the arc's top within the hands' reach,
    /// a lip beside the way looked for along the wish, the catch decided whole at the commit (the lip under the hands' contact, its room and its path) and
    /// kept; 1: the earlier build's (a tape's note without <c>v</c>, <c>--commit-rules-1</c>).
    /// </summary>
    public int Version { get; init; } = 2;

    /// <summary>
    /// Of a lip above what the arc's capsule reaches with the clip's own contact pose, how much the arms take up at the contact (m), by the hands'
    /// IK; the capsule is lifted over its arc by the rest, and the arms' share taken out after the contact over <see cref="BodyMotor.CatchSettle"/>.
    /// </summary>
    public float ArmReach { get; init; } = 0.12f;

    /// <summary>
    /// Braced by default: a grab that ends in the free hang where there is a wall under the edge goes into <c>hang_brace_on</c> this long before its end
    /// (s), blended over <see cref="BodyMotor.BraceBlend"/>, so there is no free hang between them; 0: at its end, as before.
    /// </summary>
    public float BraceOverlap { get; init; } = 0.45f;

    /// <summary>
    /// <b>going on without a stop</b> — a grab whose player is holding on (forward held toward the wall, jump held, or jump
    /// pressed during the grab) flows into the pull-up <see cref="BodyMotor.FlowAfter"/> after the hands' contact, entered past its own still start
    /// (<see cref="BodyMotor.PullUpEntry"/>) at <see cref="PullUpRate"/>; a press in the hang or its brace pulls up at once too. Off: the earlier build's (the grab's
    /// settle, the brace and the hang waited out, the pull-up from its first frame at its own rate).
    /// </summary>
    public bool Climb { get; init; } = true;

    /// <summary>The rate the pull-up plays at when the player goes on from the grab (<c>pullUpRate</c>, 1.3); from a hang with a fresh press, its own.</summary>
    public float PullUpRate { get; init; } = 1.3f;

    /// <summary>
    /// Tier 1's <see cref="MovesRules.BracedGrabs"/>: a standing or a falling grab where there is a wall under the
    /// edge plays its braced twin (<c>grab_jump_up_braced</c>, <c>grab_fall_braced</c>: ending on <c>hang_braced</c>'s first frame), so no brace follows it;
    /// <see cref="BraceOverlap"/> stays for a set without the twins. Off: the earlier build's.
    /// </summary>
    public bool BracedTwins { get; init; }

    /// <summary>These numbers with <see cref="BracedTwins"/> as given.</summary>
    public AirCommitRules WithTwins(bool twins) => new()
    {
        Lead = Lead, AutoCatch = AutoCatch, AutoCone = AutoCone, Version = Version, ArmReach = ArmReach, BraceOverlap = BraceOverlap, Climb = Climb,
        PullUpRate = PullUpRate, BracedTwins = twins,
    };
}

/// <summary>
/// <b>the commit point in the air</b> — the motor's state for it (part of the people's digested <c>PersonCommit</c>): what the body has committed to
/// and when the contact is foreseen, the lip it falls past (not caught again on the same way), and a catch's own flight from the commit to the hands'
/// contact (the arc it was on, the time to the contact, the arms' share of a lip above the arc).
/// </summary>
public struct AirCommit
{
    /// <summary>What the body has committed to (<see cref="AirOutcome"/>), 0 for nothing yet.</summary>
    public int Outcome;
    /// <summary>The ticks left to the contact foreseen at the commit (counted down; a commit whose contact does not come lapses).</summary>
    public int Ticks;
    /// <summary>A lip fallen past: its top (world Z), its lip point and the way the body went past it (+1 rising, −1 falling; 0 none).</summary>
    public float PastTop;
    public Vector3 PastLip;
    public int PastWay;
    /// <summary>
    /// 1 while a catch's clip carries the capsule from its start through the hands' contact and its settle; 2 while a catch committed to waits for its start
    /// (<see cref="BodyMotor.CatchAfter"/> into the flight, so the fade into the jump's or the fall's clip is not cut by it).
    /// </summary>
    public int Catch;
    /// <summary>The arc at the commit: the feet, the velocity, gravity (the catch's flight blends from it to the clip's path).</summary>
    public Vector3 From;
    public Vector3 Speed;
    public float Gravity;
    /// <summary>The seconds from the commit to the hands' contact, and the clip's time at the commit (s of it).</summary>
    public float Contact;
    public float Entry;
    /// <summary>The height the arms take up at the contact (m): the capsule under the clip's own place by it then, taken out after.</summary>
    public float Arms;
    /// <summary>How far from the face under the edge the arc's capsule is kept (m: its radius and a skin).</summary>
    public float Keep;
    /// <summary>
    /// Where the capsule is at the hands' contact (the clip's place there laid on the real edge, under it by <see cref="Arms"/>) and its velocity then (the clip's,
    /// at its own rate). while a catch waits for its start (<see cref="Catch"/> 2) they hold its plan, decided at the commit: <see cref="Goal"/> the lip's
    /// point under the hands, <see cref="GoalSpeed"/> the face's outward normal, <see cref="Keep"/> the top, <see cref="Arms"/> the reach's stretch,
    /// <see cref="Entry"/> 1 + the clip's index, <see cref="Contact"/> the way it is met (+1 rising, −1 falling) — so the state's bytes are the earlier build's.
    /// </summary>
    public Vector3 Goal;
    public Vector3 GoalSpeed;
}
