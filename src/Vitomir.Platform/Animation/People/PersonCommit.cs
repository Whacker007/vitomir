// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: some predictiveness at some point in the jumping or falling motion,
// which means that at some point the engine commits the player's actor to the motion.
using Vitomir.Platform.Bodies;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>the commit point in the air</b> (<b>SANDBOX</b>, ours): whether it is on and its numbers. Off (<see cref="Off"/>:
/// <c>--commit-off</c>) a body in the air grabs as before did (the motor's 0.19 s stand-in grab from a jump, the falling grab with jump held, the free hang
/// before a brace), the prelanding plays as before (faster to fit a short fall, its fade in its own time) and the world has no <see cref="PersonCommit"/>
/// store. It rides on the ledge's clips and the moves: off with them. <b>It changes the simulation</b> (a catch's clip carries the capsule
/// from the commit; nothing is caught from a fall without use held); a tape carries it in its note <see cref="TapeNote"/>; a tape without it is the earlier build's.
/// </summary>
public sealed record PersonCommitSettings
{
    /// <summary>Whether the commit point is on (on by default; off: the earlier build's, <c>--commit-off</c>).</summary>
    public bool On { get; init; } = true;

    /// <summary>How long before the contact foreseen the body commits (s; <c>commitLead</c>).</summary>
    public float LeadSeconds { get; init; } = 0.3f;

    /// <summary>A jump taken at a wall catches its lip by itself (<c>autoCatch</c>).</summary>
    public bool AutoCatch { get; init; } = true;

    /// <summary>The cone about the jump's way, degrees each side, in which a lip met is the one jumped at (<c>autoCatchCone</c>).</summary>
    public float AutoConeDegrees { get; init; } = 60f;

    /// <summary>The rules' version (1: the earlier build's — <c>--commit-rules-1</c>, a tape's note without <c>v</c>; <see cref="AirCommitRules.Version"/>).</summary>
    public int Version { get; init; } = 2;

    /// <summary>The earlier build's commit point (its rules and its 30° cone; no going on from the grab).</summary>
    public PersonCommitSettings AsRules1 => this with { Version = 1, AutoConeDegrees = 30f, Climb = false };

    /// <summary>From a grab straight into the pull-up when the player holds on (<see cref="AirCommitRules.Climb"/>; off: the earlier build's, <c>--climb-wait-out</c>).</summary>
    public bool Climb { get; init; } = true;

    /// <summary>The pull-up's rate going on from a grab (<c>pullUpRate</c>; 1 to 2).</summary>
    public float PullUpRate { get; init; } = 1.3f;

    /// <summary>The earlier build's climb (the grab waited out, the pull-up at its own rate from its first frame).</summary>
    public PersonCommitSettings WaitOutClimb => this with { Climb = false };

    /// <summary>How much of a lip above the arc's reach the arms take up at the contact (m; <c>catchArmReach</c>), the capsule the rest.</summary>
    public float ArmReach { get; init; } = 0.12f;

    /// <summary>Braced by default: how long before a free-ending grab's end the brace begins where there is a wall (s; <c>braceOverlap</c>; 0 at its end).</summary>
    public float BraceOverlap { get; init; } = 0.45f;

    /// <summary>Whether any is on.</summary>
    public bool Any => On;

    /// <summary>None.</summary>
    public static PersonCommitSettings Off => new() { On = false };

    /// <summary>The tape head's note.</summary>
    public const string TapeNote = "commit";

    /// <summary>
    /// The note: <c>off</c>, else <c>on=1 lead=0.3 auto=1 cone=60 arms=0.12 brace=0.45 v=2 climb=1 pull=1.3</c> (the earlier build's had no <c>v</c>, the earlier build's no
    /// <c>climb</c>).
    /// </summary>
    public string Note() => !On ? "off" : string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"on=1 lead={LeadSeconds:0.####} auto={(AutoCatch ? 1 : 0)} cone={AutoConeDegrees:0.####} arms={ArmReach:0.####} brace={BraceOverlap:0.####}")
        + (Version >= 2 ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $" v={Version}") : "")
        + (Climb ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $" climb=1 pull={PullUpRate:0.####}") : "");

    /// <summary>These settings with a tape's note laid on; none (an older tape) or <c>off</c> is <see cref="Off"/>. The numbers go through the note's text.</summary>
    public PersonCommitSettings WithNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note) || !note.Contains('=')) return Off;
        var s = this with { On = true, Version = 1, Climb = false };   // a note without v is the earlier build's; without climb, the earlier build's
        foreach (var part in note.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || !float.TryParse(part[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            s = part[..eq] switch
            {
                "on" => s with { On = v != 0f }, "lead" => s with { LeadSeconds = v }, "auto" => s with { AutoCatch = v != 0f },
                "cone" => s with { AutoConeDegrees = v }, "arms" => s with { ArmReach = v }, "brace" => s with { BraceOverlap = v },
                "v" => s with { Version = (int)v }, "climb" => s with { Climb = v != 0f }, "pull" => s with { PullUpRate = v }, _ => s,
            };
        }
        return s.On ? s : Off;
    }

    /// <summary>The motor's numbers.</summary>
    public AirCommitRules Rules() => new()
    {
        Lead = Math.Clamp(LeadSeconds, 0.05f, 1f), AutoCatch = AutoCatch, AutoCone = Math.Clamp(AutoConeDegrees, 0f, 90f),
        ArmReach = Math.Clamp(ArmReach, 0f, 0.5f), BraceOverlap = Math.Clamp(BraceOverlap, 0f, 1.2f), Version = Version,
        Climb = Climb, PullUpRate = Math.Clamp(PullUpRate, 1f, 2f),
    };
}

/// <summary>
/// <b>a person's commit point in the air</b> — the simulation's state (digested): the motor's <see cref="AirCommit"/>. Only the player is given one
/// (with the ledge's clips: the townsfolk neither jump nor grab), and a world has the store only with it on, so the earlier build's digest as before.
/// </summary>
public struct PersonCommit
{
    /// <summary>The motor's: what the body has committed to, a lip fallen past, a catch's flight.</summary>
    public AirCommit Move;

    /// <summary>
    /// The animation's: a catch's clip entered at its matching phase (<see cref="PeopleLedge"/>) — the clip's time it was entered at (its pose nearest the one
    /// shown) and the motor's clip time then; its time is laid from there to the hands' contact at the contact's moment. <see cref="Matched"/> 1 while so.
    /// 2 while a pull-up going on, entered at its matching phase, has its time brought onto the motor's (<see cref="BodyMotor.FlowConverge"/>).
    /// </summary>
    public float AnimFrom;
    public float MotorFrom;
    public int Matched;
}
