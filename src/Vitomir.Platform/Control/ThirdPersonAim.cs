// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the third-person swing's pitch from the view's pitch (by design for ).
namespace Vitomir.Platform.Control;

/// <summary>
/// How the player's blow is pitched in third person (read by <see cref="AttackSystem"/>, "the aim wanted"). By
/// default from the <b>view's pitch</b>, as first person's, plus <see cref="LevelOffsetDegrees"/>: the swing's arc (the
/// presentation's <c>SwingArcs</c>) then moves smoothly with the look. Earlier builds took it from the chest to the reticle's
/// point (<see cref="Animation.AimOffset.Toward"/>), which jumps as the crosshair passes from a near thing to a far one; kept
/// behind <see cref="FromReticle"/> (the setting <c>thirdPersonAimFromReticle</c>, off) for comparison. Part of the
/// simulation (a blow's pitch is an outcome): the session carries it in a recording's head (an older tape was made
/// from the reticle). Absent from a world, the default.
/// </summary>
public sealed record ThirdPersonAimSettings
{
    /// <summary>The old way: the pitch from the chest to the reticle's point (the view's pitch when the reticle is on nothing).</summary>
    public bool FromReticle { get; init; }

    /// <summary>
    /// Degrees added to the view's pitch (up positive; ENGINE_SANDBOX, ours): 0 — a level third-person view swings as a level
    /// first-person one, the forehand across a man's chest and near arm in front (measured with the arc).
    /// </summary>
    public float LevelOffsetDegrees { get; init; }

    /// <summary>The tape head's note: <c>reticle</c>, or <c>view</c> with the offset (<c>view:0</c>).</summary>
    public const string TapeNote = "thirdPersonAim";

    public string Note => FromReticle ? "reticle" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"view:{LevelOffsetDegrees:0.###}");

    /// <summary>The settings a tape's note names; a tape without it was made earlier, from the reticle.</summary>
    public static ThirdPersonAimSettings FromNote(string? note)
    {
        if (string.IsNullOrEmpty(note) || note == "reticle") return new ThirdPersonAimSettings { FromReticle = true };
        float offset = note.StartsWith("view:", StringComparison.Ordinal)
            && float.TryParse(note[5..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var o) ? o : 0f;
        return new ThirdPersonAimSettings { LevelOffsetDegrees = offset };
    }
}
