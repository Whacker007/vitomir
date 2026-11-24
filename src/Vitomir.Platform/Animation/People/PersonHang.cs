// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's clips: the hang's corners and leaps
// (the motor's side BodyMotor.Hang.cs).
using Vitomir.Platform.Bodies;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>the hang's corners and leaps</b> (<b>SANDBOX</b>, ours): whether they are taken and their numbers. Off (<see cref="Off"/>: <c>--hang-moves-off</c>,
/// the setting <c>hangMovesOff</c>) a shimmy stops at a corner, the hang has no leaps (jump pulls up, jump with back held pushes off, crouch drops) and the world has
/// no <see cref="PersonHang"/> store. It rides on tier 1's moves (off with them) and needs a set with the clips (a newer set). <b>It changes the
/// simulation</b> (the corners' and the leaps' capsule paths, the body's facing carrying their turn); a tape carries it in its note <see cref="TapeNote"/>; a
/// tape without it is the earlier build's.
/// </summary>
public sealed record PersonHangSettings
{
    /// <summary>Whether any of it is on (on by default; off: the earlier build's, <c>--hang-moves-off</c>).</summary>
    public bool On { get; init; } = true;

    /// <summary>A shimmy into a corner of the lip goes round it where it can be taken.</summary>
    public bool Corners { get; init; } = true;

    /// <summary>The leaps from the braced hang.</summary>
    public bool Leaps { get; init; } = true;

    /// <summary>How far a leap's target lip may be off the clip's own offset and still be leapt to (m; <c>leapTolerance</c>, 0.05 to 0.5).</summary>
    public float LeapTolerance { get; init; } = 0.25f;

    /// <summary>How much nearer than its clip's own a corner may be from the hang's middle and still be gone round (m; <c>cornerSlack</c>, 0 to 0.3).</summary>
    public float CornerSlack { get; init; } = 0.15f;

    /// <summary>How far a corner's turn (or a facing ledge's face) may be off the clip's own and still be taken (degrees; <c>hangAngleTolerance</c>, 1 to 30).</summary>
    public float AngleToleranceDegrees { get; init; } = 15f;

    /// <summary>
    /// A side leap only where the lip breaks between the hands and the target (a tape's note without <c>breaks=1</c>, is
    /// the leap along a lip that goes on).
    /// </summary>
    public bool Breaks { get; init; } = true;

    /// <summary>Whether any is on.</summary>
    public bool Any => On;

    /// <summary>None.</summary>
    public static PersonHangSettings Off => new() { On = false };

    /// <summary>The tape head's note.</summary>
    public const string TapeNote = "hang";

    /// <summary>The note: <c>off</c>, else <c>on=1 corners=1 leaps=1 tol=0.25 slack=0.15 angle=15</c> and <c>breaks=1</c> when on.</summary>
    public string Note() => !On ? "off" : string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"on=1 corners={(Corners ? 1 : 0)} leaps={(Leaps ? 1 : 0)} tol={LeapTolerance:0.####} slack={CornerSlack:0.####} angle={AngleToleranceDegrees:0.####}{(Breaks ? " breaks=1" : "")}");

    /// <summary>These settings with a tape's note laid on; none (an older tape) or <c>off</c> is <see cref="Off"/>. The numbers go through the note's text.</summary>
    public PersonHangSettings WithNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note) || !note.Contains('=')) return Off;
        var s = this with { On = true, Breaks = false };   // a note without breaks= is the earlier build's
        foreach (var part in note.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || !float.TryParse(part[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            s = part[..eq] switch
            {
                "on" => s with { On = v != 0f }, "corners" => s with { Corners = v != 0f }, "leaps" => s with { Leaps = v != 0f }, "breaks" => s with { Breaks = v != 0f },
                "tol" => s with { LeapTolerance = v }, "slack" => s with { CornerSlack = v }, "angle" => s with { AngleToleranceDegrees = v }, _ => s,
            };
        }
        return s.On ? s : Off;
    }

    /// <summary>The motor's numbers.</summary>
    public HangRules Rules() => new()
    {
        Corners = Corners, Leaps = Leaps, Breaks = Breaks, LeapTolerance = Math.Clamp(LeapTolerance, 0.05f, 0.5f), CornerSlack = Math.Clamp(CornerSlack, 0f, 0.3f),
        AngleTolerance = Math.Clamp(AngleToleranceDegrees, 1f, 30f) * Core.DetMath.DegToRad,
    };

    /// <summary>
    /// Whether a person's drawn pose has its root's turn taken out (a service of the world's): a ledge clip that turns the body (a corner, the leap back)
    /// shown, or fading out — the body's facing carries that turn (BodyMotor.Hang.cs), so the root's own is not drawn too. Presentation of the motor's state.
    /// </summary>
    public static bool StillRoot(Core.World world, CharacterAssets assets, in CharacterAnimation state)
    {
        if (!world.TryService<PersonHangSettings>(out var hang) || !hang.On || state.Clip < 0) return false;
        if (assets.Kind(state.Kind).Person?.Ledge?.Paths is not { } paths || !paths.HasHangMoves) return false;
        foreach (var c in paths.Clips)
            if (c.Turn != 0f && c.Role != Bodies.LedgeRole.SideTurn && (c.Clip == state.Clip || (c.Clip == state.PreviousClip && state.Blend < 1f))) return true;
        return false;
    }
}

/// <summary>
/// <b>a person's corners and leaps</b> — the simulation's state (digested): the motor's (<see cref="HangMove"/>). Only the people given one by the
/// session have it (the player, with the ledge, the commit point and tier 1's moves), and a world has the store only with them on and a set that has the
/// clips (a newer set), so the earlier build's tapes and the digest as before.
/// </summary>
public struct PersonHang
{
    public HangMove Move;
}
