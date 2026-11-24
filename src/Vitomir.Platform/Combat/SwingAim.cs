// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Runtime.CompilerServices;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Combat;

/// <summary>
/// The duelling brain's aim at its foe's body (with the flattened side blows; ours, SANDBOX). Until
/// Earlier the brain pitched its blow by a fixed angle — 15° up, level or 20° down (<see cref="DuelBrainSettings.HighDegrees"/>,
/// <see cref="DuelBrainSettings.LowDegrees"/>) — whatever the foe's height: 1.1's diagonal side blows cross 1.7 m of height on
/// their way, so they met a short foe anyway; a side blow swept level at the chest (the flattened ones) passes over him (the merged
/// arena: the newly Embraced's flattened forehand met the szlachta's body in 1 % of its swings). With this service registered (the
/// session does, unless <c>swings-oneone</c> keeps 1.1's side blows) the same draw (high, level, low) aims at a height on the foe's
/// own body — <see cref="High"/>, <see cref="Level"/> or <see cref="Low"/> of his top as his pose has it now (his hit volumes:
/// crouched, hunched or standing), kept within <see cref="Lowest"/> and <see cref="Highest"/> over his feet — and the blow is
/// pitched so a level sweep at <see cref="SweepHeight"/> (the attacker's, at a level aim) passes at that height at the foe's
/// distance: pitch = atan2(target − sweep height, distance), the distance at least <see cref="LeastDistance"/>. The aim offset then
/// clamps and eases it as it does every aim. A foe with no volumes (a world without contact) is taken at his capsule's height.
/// Deterministic (no draw of its own), allocates nothing.
/// </summary>
public sealed class SwingAim
{
    /// <summary>Shares of the foe's top the draw's three heights aim at: the head and shoulders, the chest, the hips.</summary>
    public float High { get; init; } = 0.88f;
    public float Level { get; init; } = 0.72f;
    public float Low { get; init; } = 0.45f;

    /// <summary>The aimed height's limits over the foe's feet (m): no sweep at the ankles, none over a tall man's head.</summary>
    public float Lowest { get; init; } = 0.45f;
    public float Highest { get; init; } = 1.75f;

    /// <summary>The height over the attacker's feet a level aim sweeps a side blow at (m): the flattened blows' (OneOneStitched.SwingHeight).</summary>
    public float SweepHeight { get; init; } = 1.40f;

    /// <summary>The least distance the pitch is worked over (m), as the aim offset's reticle aim (AimOffset.Toward).</summary>
    public float LeastDistance { get; init; } = 0.75f;

    /// <summary>
    /// The pitch (radians) aiming the blow of <paramref name="attacker"/> at <paramref name="foe"/>'s body: <paramref name="choice"/>
    /// 1 high, 0 level, −1 low.
    /// </summary>
    public float PitchAt(World world, in Body attacker, EntityId foe, in Body foeBody, int choice)
    {
        float top = Top(world, foe, in foeBody);
        float share = choice > 0 ? High : choice < 0 ? Low : Level;
        float target = foeBody.Feet.Z + Math.Clamp(top * share, Lowest, MathF.Max(Lowest, MathF.Min(Highest, top)));
        float dx = foeBody.Feet.X - attacker.Feet.X, dy = foeBody.Feet.Y - attacker.Feet.Y;
        float d = MathF.Max(LeastDistance, MathF.Sqrt(dx * dx + dy * dy));
        return DetMath.Atan2(target - (attacker.Feet.Z + SweepHeight), d);
    }

    /// <summary>"having a stab aimed at center chest makes sense": a thrust aims at this share of the foe's top (his chest, the draw's level height) ...</summary>
    public float ThrustShare { get; init; } = 0.72f;

    /// <summary>... from this share of the attacker's own top (his eyes: the stitched thrust is driven along the line toward the eyes' height ahead, OneOneStitched.ThrustAimDistance).</summary>
    public float ThrustFromShare { get; init; } = 0.92f;

    /// <summary>
    /// The pitch (radians) aiming <paramref name="attacker"/>'s thrust at the centre of <paramref name="foe"/>'s
    /// chest — <see cref="ThrustShare"/> of his top as his pose has it now (within <see cref="Lowest"/> and <see cref="Highest"/>) — from
    /// the attacker's eyes (<see cref="ThrustFromShare"/> of his own top): the drawn high, level or low plays no part. Deterministic,
    /// allocates nothing.
    /// </summary>
    public float ThrustPitchAt(World world, EntityId attacker, in Body attackerBody, EntityId foe, in Body foeBody)
    {
        float top = Top(world, foe, in foeBody);
        float target = foeBody.Feet.Z + Math.Clamp(top * ThrustShare, Lowest, MathF.Max(Lowest, MathF.Min(Highest, top)));
        float from = attackerBody.Feet.Z + Top(world, attacker, in attackerBody) * ThrustFromShare;
        float dx = foeBody.Feet.X - attackerBody.Feet.X, dy = foeBody.Feet.Y - attackerBody.Feet.Y;
        float d = MathF.Max(LeastDistance, MathF.Sqrt(dx * dx + dy * dy));
        return DetMath.Atan2(target - from, d);
    }

    // ---- the aim kept on the foe through the blow ----

    /// <summary>
    /// SANDBOX, ours: whether the duelling brain keeps its blow aimed at its foe's body through the blow — each tick
    /// while its own blow is under way, <see cref="PitchAt"/> (the same draw's height) or <see cref="ThrustPitchAt"/> from where both stand
    /// now and as the foe's pose has him now — as a player's look follows his foe through a blow (the aim offset eases the bend at its own
    /// rate). Earlier, the pitch was worked out once, at the press: against a man of Christof's height it hardly depends on the distance
    /// (his chest is about the height a level sweep passes), but against a low foe it does, steeply, and a foe closing during the wind-up
    /// (the earlier build's slower blows: 0.6 s from the press to the strike) was met by a blow aimed for where he had been — 0.3 m over a szlachta's
    /// head. Off (the switch <c>aim-at-press</c>): the earlier build's.
    /// </summary>
    public bool Track { get; init; } = true;

    /// <summary>The module's switch that keeps the brain's aim fixed at the press, as before: <c>aim-at-press</c>.</summary>
    public const string AimAtPressSwitch = "aim-at-press";

    /// <summary>The foe's top over his feet now: the highest of his posed hit volumes, else his capsule's height.</summary>
    public static float Top(World world, EntityId foe, in Body foeBody)
    {
        if (world.TryService<HitVolumes>(out var volumes))
        {
            var posed = volumes.Posed(world, foe);
            if (posed.Length > 0)
            {
                float top = float.MinValue;
                foreach (var v in posed) top = MathF.Max(top, MathF.Max(v.Capsule.A.Z, v.Capsule.B.Z) + v.Capsule.Radius);
                return MathF.Max(0.3f, top - foeBody.Feet.Z);
            }
        }
        ref var loco = ref world.Store<Locomotion>().Find(foe);
        return Unsafe.IsNullRef(ref loco) ? 1.8f : foeBody.CapsuleHeight(loco);
    }
}
