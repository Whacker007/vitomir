// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Powers;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>The pace scale</b> (ours; the contract's <see cref="PaceScale"/>, Celerity's movement): a body with a pace scale
/// of factor <i>f</i> above 1, and <b>not in the masquerade's mode</b> (<see cref="MasqueradeState.Mode"/> unbound, or no
/// masquerade state at all), moves at each gait at <b>max(its own pace, the human's × <i>f</i>)</b> — walk, run and sprint —,
/// its supernatural gait at max(its own, the human's sprint × <i>f</i>), and speeds up and stops at max(its own, the human's
/// × <i>f</i>). So a power replaces a body's resting speed and never multiplies it: an elder's baked-in pace is not made
/// faster again (the rules package: activation replaces rest; source passive counted once). Crouched it keeps the crouch's
/// pace, and charging a jump the walk it had (the caps stay). Nothing else changes: no jump, no reach, no climb (the
/// package: Celerity's rights cannot multiply a jump). In the masquerade's mode the factor is ignored (<i>"the
/// game will allow for some pretty crazy movement at the higher power levels, but masquerade mode will keep things under
/// control unless the player chooses to disable those or gets attacked"</i>): the body's numbers there are held to the
/// human's, whatever its pace scale says. A body without the component moves exactly as before, digest for digest. The
/// human's numbers are the world's <see cref="MovementProfiles.Human"/> (the platform's own human, <see cref="Locomotion.Sandbox"/>,
/// when no module names one). Worked out in the Move phase each tick (<see cref="BodySystem"/>), allocating nothing; the
/// motor sweeps its capsule, so a fast body does not pass through walls or people.
/// </summary>
public static partial class BodyMotor
{
    /// <summary>
    /// The numbers <paramref name="own"/> moves at under a pace scale of <paramref name="factor"/>: each gait's pace, the
    /// supernatural gait's, the acceleration and the braking the larger of its own and <paramref name="human"/>'s ×
    /// <paramref name="factor"/>; every other number its own (the crouch's pace among them). A factor of 1 or less gives
    /// <paramref name="own"/>.
    /// </summary>
    public static Locomotion Paced(in Locomotion own, in Locomotion human, float factor)
    {
        var l = own;
        if (!(factor > 1f)) return l;
        l.WalkSpeed = MathF.Max(own.WalkSpeed, human.WalkSpeed * factor);
        l.RunSpeed = MathF.Max(own.RunSpeed, human.RunSpeed * factor);
        l.SprintSpeed = MathF.Max(own.SprintSpeed, human.SprintSpeed * factor);
        // The supernatural gait is max(sprint, resting): max(its own, the human's sprint × f).
        l.RestingSpeed = MathF.Max(own.RestingSpeed, human.SprintSpeed * factor);
        l.AccelerationCapacity = MathF.Max(own.AccelerationCapacity, human.AccelerationCapacity * factor);
        l.BrakingDeceleration = MathF.Max(own.BrakingDeceleration, human.BrakingDeceleration * factor);
        return l;
    }

    /// <summary>
    /// The pace factor in force for a body (the contract's <see cref="PaceScale"/>): its factor when it has one above 1 and
    /// is not in the masquerade's mode, else 1. <paramref name="paces"/> and <paramref name="states"/> may be null (a world
    /// without them).
    /// </summary>
    public static float PaceFactor(Store<PaceScale>? paces, Store<MasqueradeState>? states, EntityId e)
    {
        if (paces is null || !paces.Has(e)) return 1f;
        float f = paces.Get(e).Factor;
        if (!(f > 1f)) return 1f;
        if (states is not null && states.Has(e) && states.Get(e).Mode == MasqueradeMode.Masquerade) return 1f;
        return f;
    }
}
