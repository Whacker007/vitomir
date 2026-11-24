// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's clips: grabs that end braced,
// the low vaults at a run, ladders on clips, the braced feet.
using Vitomir.Platform.Bodies;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>tier 1's moves, part 1</b> (<b>SANDBOX</b>, ours): whether they play and their numbers. Off (<see cref="Off"/>: <c>--moves-off</c>, the
/// setting <c>movesOff</c>) every grab ends as before (the free twin and the earlier build's brace overlap), a run steps over and a sprint vaults low things on the earlier build's
/// stand-ins, a ladder is climbed on the earlier build's stand-in, and the world has no <see cref="PersonLadder"/> store. It rides on the commit point and the
/// ledge's clips: off with them. <b>It changes the simulation</b> (the braced grabs' and the low vaults' capsule paths, the ladders' capsule
/// following their clips); a tape carries it in its note <see cref="TapeNote"/>; a tape without it is the earlier build's.
/// </summary>
public sealed record PersonMovesSettings
{
    /// <summary>Whether any of it is on (on by default; off: the earlier build's, <c>--moves-off</c>).</summary>
    public bool On { get; init; } = true;

    /// <summary><c>grab_jump_up_braced</c> and <c>grab_fall_braced</c> where there is a wall under the edge (no <c>hang_brace_on</c> after a grab).</summary>
    public bool BracedGrabs { get; init; } = true;

    /// <summary><c>hurdle_low</c> and <c>vault_speed</c> by themselves at a run or a sprint, in place of the earlier build's step-over and sprint vault where they reach.</summary>
    public bool LowVaults { get; init; } = true;

    /// <summary>Ladders on the clips (the vertical and the leaning sets, the side landings, the ladder to a braced ledge and back).</summary>
    public bool Ladders { get; init; } = true;

    /// <summary>Sprint held on a ladder plays its climb this much faster (<c>ladderSprintRate</c>; 1 to 3; the default asked of him).</summary>
    public float LadderSprintRate { get; init; } = 2f;

    /// <summary>
    /// "the ladder animations need to play faster": the climb's cycles this much faster than the clips' own
    /// (<c>ladderClimbRate</c>, 1 to 3; 1.5: 0.87 s a cycle, about 0.69 m/s); sprint held multiplies it by <see cref="LadderSprintRate"/>, at most 3.
    /// </summary>
    public float LadderClimbRate { get; init; } = 1.5f;

    /// <summary>
    /// The getting-on and the getting-off (at the foot, at the top and its rails pair, onto and off a side landing) and the transitions to and from the
    /// braced hang this much faster than the clips' own (<c>ladderMoveRate</c>, 1 to 3; 1.5).
    /// </summary>
    public float LadderMoveRate { get; init; } = 1.5f;

    /// <summary>How far a level's ladder may lean off a set's own lean (0° vertical, 15° leaning) and still be climbed on that set (degrees; else the earlier build's stand-in).</summary>
    public float LeanToleranceDegrees { get; init; } = 2f;

    /// <summary>
    /// the most the braced feet's toes bend up at the ball against a wall (degrees; <c>toeBendLimitDeg</c>, 40: the clips' soft
    /// shoes). Below 40 the braced foot is moved back from the wall by the models repository's table (30° 9 mm, 20° 18, 10° 24, 0° 28). Presentation only.
    /// </summary>
    public float ToeBendLimitDegrees { get; init; } = 40f;

    /// <summary>Whether any is on.</summary>
    public bool Any => On;

    /// <summary>None.</summary>
    public static PersonMovesSettings Off => new() { On = false };

    /// <summary>The tape head's note.</summary>
    public const string TapeNote = "moves";

    /// <summary>The note: <c>off</c>, else <c>on=1 braced=1 low=1 ladders=1 sprint=2 lean=2 climb=1.5 move=1.5</c> (the ladders' rates; a note without them, 1).</summary>
    public string Note() => !On ? "off" : string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"on=1 braced={(BracedGrabs ? 1 : 0)} low={(LowVaults ? 1 : 0)} ladders={(Ladders ? 1 : 0)} sprint={LadderSprintRate:0.####} lean={LeanToleranceDegrees:0.####} climb={LadderClimbRate:0.####} move={LadderMoveRate:0.####}")
        + (ToeBendLimitDegrees < 40f - 1e-3f ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $" toe={ToeBendLimitDegrees:0.####}") : "");

    /// <summary>These settings with a tape's note laid on; none (an older tape) or <c>off</c> is <see cref="Off"/>. The numbers go through the note's text.</summary>
    public PersonMovesSettings WithNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note) || !note.Contains('=')) return Off;
        var s = this with { On = true, ToeBendLimitDegrees = 40f, LadderClimbRate = 1f, LadderMoveRate = 1f };   // a note without toe: the clips' own feet; without climb, move: the earlier build's own rates
        foreach (var part in note.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || !float.TryParse(part[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            s = part[..eq] switch
            {
                "on" => s with { On = v != 0f }, "braced" => s with { BracedGrabs = v != 0f }, "low" => s with { LowVaults = v != 0f },
                "ladders" => s with { Ladders = v != 0f }, "sprint" => s with { LadderSprintRate = v }, "lean" => s with { LeanToleranceDegrees = v },
                "toe" => s with { ToeBendLimitDegrees = v }, "climb" => s with { LadderClimbRate = v }, "move" => s with { LadderMoveRate = v }, _ => s,
            };
        }
        return s.On ? s : Off;
    }

    /// <summary>
    /// The models repository's note: how far back from the wall a braced foot must sit for toes that bend up at most
    /// <paramref name="limitDegrees"/> at the ball (m): 40° (the clips' soft shoes) none, 30° 9 mm, 20° 18, 10° 24, 0° 28; between them by line.
    /// </summary>
    public static float BracedFootBack(float limitDegrees)
    {
        ReadOnlySpan<float> deg = [0f, 10f, 20f, 30f, 40f];
        ReadOnlySpan<float> back = [0.028f, 0.024f, 0.018f, 0.009f, 0f];
        float d = Math.Clamp(limitDegrees, 0f, 40f);
        for (int i = 1; i < deg.Length; i++)
            if (d <= deg[i]) return back[i - 1] + (back[i] - back[i - 1]) * (d - deg[i - 1]) / (deg[i] - deg[i - 1]);
        return 0f;
    }

    /// <summary>
    /// the hook for the outfits' shoes — for a person in a braced hang's clip (the hang, a shimmy, a brace on), how far its feet are moved back
    /// from the wall (the model's space, m) by <see cref="ToeBendLimitDegrees"/> (a service of the world's; 40, the default, none). Presentation only.
    /// </summary>
    public static float FootBackOf(Core.World world, Core.EntityId entity, CharacterAssets assets, in CharacterAnimation state)
    {
        if (!world.TryService<PersonMovesSettings>(out var moves) || !moves.On || moves.ToeBendLimitDegrees >= 40f - 1e-3f || state.Clip < 0) return 0f;
        var kind = assets.Kind(state.Kind);
        if (kind.Person?.Ledge?.Paths is not { } paths) return 0f;
        foreach (var c in paths.Clips)
            if (c.Clip == state.Clip)
                return c.Braced && c.Role is LedgeRole.Hang or LedgeRole.Shimmy or LedgeRole.BraceOn
                    ? BracedFootBack(moves.ToeBendLimitDegrees) / (kind.Scale > 0f ? kind.Scale : 1f) : 0f;
        return 0f;
    }

    /// <summary>The motor's numbers.</summary>
    public MovesRules Rules() => new()
    {
        BracedGrabs = BracedGrabs, LowVaults = LowVaults, Ladders = Ladders, LadderSprintRate = Math.Clamp(LadderSprintRate, 1f, 3f),
        LadderClimbRate = Math.Clamp(LadderClimbRate, 1f, 3f), LadderMoveRate = Math.Clamp(LadderMoveRate, 1f, 3f),
        LeanTolerance = Math.Clamp(LeanToleranceDegrees, 0.5f, 7f) * Core.DetMath.DegToRad,
    };
}
