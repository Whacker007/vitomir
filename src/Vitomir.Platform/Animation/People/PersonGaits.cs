// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's clips (the player's walk, run and sprint, and their starts and stops), played as its notes say, with
// The player's paces are the clips' own: 2.0, 4.0, 5.8 m/s, the feet planted) and quicker stops ( no waiting for the stride).
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>the player's gaits</b> (the models repository's clips; <see cref="PlayerGaits"/>) — the simulation's part (<b>SANDBOX</b>, ours). Off
/// (<see cref="Off"/>: <c>--player-gaits-off</c>, the setting <c>playerGaitsOff</c>) the player walks, runs, sprints, starts and stops as before did and the world has
/// no service of it. A tape carries it in its note <see cref="TapeNote"/>; a tape without it is the earlier build's.
/// </summary>
public sealed record PersonGaitSettings
{
    /// <summary>
    /// Whether the player plays <c>walk_player</c>, <c>run_player</c> and <c>sprint</c> and their starts and stops, at the clips' own paces (the
    /// man's and the woman's 2.0, 4.0 and 5.8 m/s under the human's numbers; another profile's or a pace scale's paces by as much).
    /// </summary>
    public bool Player { get; init; } = true;

    /// <summary>
    /// The player's ground speed (m/s) from which the run is played rather than the walk (crossed past the animation's hysteresis): 2.8, near the middle of the
    /// walk's 2.0 and the run's 4.0 by ratio (the walk drawn at most 1.35 of its rate there, the run 0.7). <c>playerRunFrom</c> (2.2 to 3.6).
    /// </summary>
    public float RunFrom { get; init; } = 2.8f;

    /// <summary>The player's ground speed (m/s) from which the sprint is played, ahead (4.8: between the run's 4.0 and the sprint's 5.8 by ratio). <c>playerSprintFrom</c> (4.2 to 5.6).</summary>
    public float SprintFrom { get; init; } = 4.8f;

    /// <summary>
    /// A stop is taken at once by the nearest of its four ways in a stride (the <c>enter_phase</c>s, a quarter apart), the rest of the way — at most
    /// an eighth of a stride, the loop's pose either side of the way in — blended into the stop's over this long (s; 0.2, the animation's own fade: over an eighth
    /// of a stride, 0.07 to 0.1 s, a foot on the ground was drawn sliding up to 6 m/s). <c>stopBlend</c> (0.05 to 0.4 s).
    /// </summary>
    public float StopBlend { get; init; } = 0.2f;

    /// <summary>None of it.</summary>
    public static PersonGaitSettings Off => new() { Player = false };

    /// <summary>The tape head's note.</summary>
    public const string TapeNote = "gaits";

    /// <summary>The note: <c>off</c>, else <c>player=1 run=2.8 sprint=4.8 blend=0.2</c>.</summary>
    public string Note() => !Player ? "off"
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"player=1 run={RunFrom:0.####} sprint={SprintFrom:0.####} blend={StopBlend:0.####}");

    /// <summary>These settings with a tape's note laid on; none (an older tape) or <c>off</c> is <see cref="Off"/>.</summary>
    public PersonGaitSettings WithNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note) || !note.Contains('=')) return Off;
        var s = this with { Player = true };
        foreach (var part in note.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || !float.TryParse(part[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            s = part[..eq] switch
            {
                "player" => s with { Player = v != 0f }, "run" => s with { RunFrom = v }, "sprint" => s with { SprintFrom = v }, "blend" => s with { StopBlend = v },
                _ => s,
            };
        }
        return s;
    }
}

/// <summary>
/// <b>the player's own gaits</b> (the models repository) and <b>their starts and stops</b>, found by name in a set; null on
/// <see cref="PersonMotion.PlayerGaits"/> for a set without <c>walk_player</c> and <c>run_player</c> (the earlier build's gaits, exactly). Three gaits —
/// 0 the walk (<c>walk_player</c>), 1 the run (<c>run_player</c>), 2 the sprint (<c>sprint</c>) — each a loop played straight ahead (within the sprint's cone, the
/// legs turned toward the way, as before sprint), drawn for its notes' <c>ground_speed_m_s</c>; each with a start from either foot and four stops a
/// stride (<c>stop_l</c>, <c>stop_r</c> and their <c>_early</c>, their ways in a quarter of a stride apart, read from the notes' <c>enter_phase</c>).
/// </summary>
public sealed class PlayerGaits
{
    /// <summary>The three loops (−1 for one the set lacks: the sprint) and the speeds they are drawn for (m/s, by the kind's scale).</summary>
    public required int[] Loops { get; init; }
    public required float[] Speeds { get; init; }

    /// <summary>Each gait's starts, [gait × 2 + (0 left, 1 right)] (−1 for none).</summary>
    public required int[] Starts { get; init; }

    /// <summary>Each gait's stops, [gait × 4 + k] (−1 for none), in no order: their <c>enter_phase</c>s say which is which.</summary>
    public required int[] Stops { get; init; }

    /// <summary>The clips that begin and the stops that end in the stand's own pose (handed to and from it with the quick fade; <see cref="PersonStarts.Measure"/>'s rule).</summary>
    public int[] FromStand { get; internal set; } = [];
    public int[] ToStand { get; internal set; } = [];

    /// <summary>The gait's names in the set.</summary>
    public static readonly string[] Names = ["walk_player", "run_player", "sprint"];

    /// <summary>The starts' and stops' prefixes: <c>walk_player_</c>, <c>run_player_</c>, <c>sprint_</c>.</summary>
    static readonly string[] Prefixes = ["walk_player_", "run_player_", "sprint_"];

    /// <summary>Which gait a loop is (−1: none of them).</summary>
    public int GaitOfLoop(int clip) => clip < 0 ? -1 : Array.IndexOf(Loops, clip);

    /// <summary>Whether <paramref name="clip"/> is one of the three loops.</summary>
    public bool IsLoop(int clip) => GaitOfLoop(clip) >= 0;

    /// <summary>Which gait a start is (−1: none).</summary>
    public int GaitOfStart(int clip) => clip < 0 ? -1 : Array.IndexOf(Starts, clip) is int i and >= 0 ? i / 2 : -1;

    /// <summary>Which gait a stop is (−1: none).</summary>
    public int GaitOfStop(int clip) => clip < 0 ? -1 : Array.IndexOf(Stops, clip) is int i and >= 0 ? i / 4 : -1;

    /// <summary>Whether <paramref name="clip"/> is one of the starts or stops.</summary>
    public bool Steps(int clip) => GaitOfStart(clip) >= 0 || GaitOfStop(clip) >= 0;

    /// <summary>Whether <paramref name="clip"/> is any of the player's clips (the loops, the starts, the stops).</summary>
    public bool Holds(int clip) => IsLoop(clip) || Steps(clip);

    /// <summary>The start of a gait from a foot (−1: none).</summary>
    public int Start(int gait, bool left) => Starts[gait * 2 + (left ? 0 : 1)];

    /// <summary>
    /// The gait the player's speed asked is played at: the walk below <paramref name="runFrom"/>, the sprint from <paramref name="sprintFrom"/> (when the set
    /// has it), else the run.
    /// </summary>
    public int GaitFor(float speed, float runFrom, float sprintFrom) => speed < runFrom ? 0 : speed >= sprintFrom && Loops[2] >= 0 ? 2 : 1;

    /// <summary>
    /// Of a gait's stops, the one whose <c>enter_phase</c> is nearest <paramref name="phase"/> (a share of the stride, round the cycle), and how far the
    /// loop is past it (a share of the stride, −0.5 to 0.5: negative when its way in is still ahead).
    /// </summary>
    public int StopAt(CharacterAssets assets, int gait, float phase, out float past)
    {
        int best = -1;
        past = 0f;
        float nearest = float.MaxValue;
        for (int k = 0; k < 4; k++)
        {
            int c = Stops[gait * 4 + k];
            if (c < 0 || assets.Clip(c) is not INotedClip { Notes.StartStop: { } notes }) continue;
            float d = phase - notes.Phase;
            d -= MathF.Round(d);
            if (MathF.Abs(d) < nearest) (nearest, best, past) = (MathF.Abs(d), c, d);
        }
        return best;
    }

    /// <summary>The clips by name from a set (null without <c>walk_player</c> and <c>run_player</c>).</summary>
    public static PlayerGaits? From(Func<string, INotedClip?> named, Func<INotedClip, int> indexOf, float scale)
    {
        var loops = new int[3];
        var speeds = new float[3];
        for (int g = 0; g < 3; g++)
        {
            loops[g] = named(Names[g]) is { Loops: true } c && c.Notes.GroundSpeed > 0f ? indexOf(c) : -1;
            speeds[g] = loops[g] >= 0 ? named(Names[g])!.Notes.GroundSpeed * scale : 0f;
        }
        if (loops[0] < 0 || loops[1] < 0) return null;
        int Of(string name, bool start) => named(name) is { Loops: false, Notes.StartStop: { } ss } c && ss.Start == start && ss.Speeds.Length > 1 ? indexOf(c) : -1;
        var starts = new int[6];
        var stops = new int[12];
        string[] ways = ["stop_l", "stop_r", "stop_l_early", "stop_r_early"];
        for (int g = 0; g < 3; g++)
        {
            starts[g * 2] = Of(Prefixes[g] + "start_l", true);
            starts[g * 2 + 1] = Of(Prefixes[g] + "start_r", true);
            for (int k = 0; k < 4; k++) stops[g * 4 + k] = Of(Prefixes[g] + ways[k], false);
        }
        return new PlayerGaits { Loops = loops, Speeds = speeds, Starts = starts, Stops = stops };
    }

    /// <summary>Every clip (for the tables made with the rest).</summary>
    public IEnumerable<int> All => Loops.Concat(Starts).Concat(Stops).Where(c => c >= 0);

    /// <summary>
    /// The numbers the player moves at with its own gaits: each gait's pace the clip's (<see cref="Speeds"/>) by the body's own over the human's
    /// (<paramref name="human"/>), so a body on the human's numbers (the masquerade holds the player there) moves at the clips' paces exactly, and one faster
    /// (a profile's, a pace scale's) by as much; every other number its own.
    /// </summary>
    public Locomotion Paced(in Locomotion moving, in Locomotion human)
    {
        var l = moving;
        static float By(float own, float humans, float clip) => clip <= 0f ? own : humans > 1e-3f ? own * clip / humans : clip;
        l.WalkSpeed = By(moving.WalkSpeed, human.WalkSpeed, Speeds[0]);
        l.RunSpeed = By(moving.RunSpeed, human.RunSpeed, Speeds[1]);
        l.SprintSpeed = By(moving.SprintSpeed, human.SprintSpeed, Speeds[2]);
        return l;
    }

    /// <summary>The human's numbers the paces are by (the world's human profile unbound, else the platform's own: <see cref="Locomotion.Sandbox"/>).</summary>
    public static Locomotion Human(World world) =>
        world.TryService<Modules.MovementProfiles>(out var profiles) && profiles.HumanIndex >= 0
            ? profiles.Numbers(profiles.HumanIndex, Control.MasqueradeMode.Unbound) : Locomotion.Sandbox;

    /// <summary>
    /// Whether a body plays the player's gaits: the session's rule on (<paramref name="rules"/>), the body the player's (its start and stop on the spring:
    /// <see cref="PersonStep.Curve"/> 1, or 3 once taken) and its kind has them.
    /// </summary>
    public static bool Plays(PersonGaitSettings? rules, PersonMotion? person, in PersonStep step) =>
        rules is { Player: true } && person?.PlayerGaits is not null && step.Curve is 1 or 3;
}
