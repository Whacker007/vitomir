// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Combat;

/// <summary>
/// A person's fighting pace ("most of the enemies don't actually attack constantly, there's pauses as
/// well"): after a run of 1 to <see cref="RunMost"/> blows (drawn) the duelling brain pauses <see cref="PauseLeast"/> to
/// <see cref="PauseMost"/> seconds (drawn, from the end of its last blow's busy time) before it strikes again; meanwhile it keeps its
/// range, circles, guards and dodges as ever. Set by the module from the person's kind (the enhanced module's <c>guard.json</c>
/// <c>brain_pace</c>); a <see cref="DuelBrainSettings.PaceMostSeconds"/> of 0 or more (an arena spec's <c>brain.pace</c>) overrides it.
/// <b>The component unregistered is the earlier build's constant aggression</b> (the module's switch <c>pace-off</c>), the settings' override
/// included. Plain data, digested.
/// </summary>
public struct DuelPace
{
    public float PauseLeast, PauseMost;
    public int RunMost;

    /// <summary>
    /// The chance an opening (its foe staggered, stunned at a guard, its guard broken) cuts the pause
    /// short — the riposte — drawn once as each opening begins; by the person's Melee (Brawl bare-handed) through the module's curve.
    /// 1: every opening, 0: none.
    /// </summary>
    public float RiposteChance;
}

/// <summary>The pace registered and read.</summary>
public static class DuelPaces
{
    /// <summary>Registers the <see cref="DuelPace"/> component unless it is already (a module that sets it calls this).</summary>
    public static WorldBuilder Register(WorldBuilder b)
    {
        if (!b.Peek.TryStore<DuelPace>(out _)) b.Component<DuelPace>();
        return b;
    }

    /// <summary>
    /// The pace the brain of <paramref name="e"/> keeps: its settings' when they give one (<see cref="DuelBrainSettings.PaceMostSeconds"/>
    /// 0 or more; the run its <see cref="DuelBrainSettings.PaceRunMost"/>, else its own kind's, else 2), else its kind's; false with
    /// none (no pause above 0, or the component unregistered: <c>pace-off</c>).
    /// </summary>
    public static bool Of(Store<DuelPace>? paces, DuelBrainSettings s, EntityId e, out DuelPace pace)
    {
        pace = default;
        if (paces is null) return false;
        ref var own = ref paces.Find(e);
        bool has = !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref own);
        if (s.PaceMostSeconds >= 0f)
        {
            float least = MathF.Max(0f, s.PaceLeastSeconds);
            pace = new DuelPace
            {
                PauseLeast = least, PauseMost = MathF.Max(least, s.PaceMostSeconds),
                RunMost = s.PaceRunMost > 0 ? s.PaceRunMost : has && own.RunMost > 0 ? own.RunMost : 2,
                RiposteChance = has ? own.RiposteChance : 1f,   // the person's own, else every opening
            };
        }
        else if (has) pace = own;
        else return false;
        if (pace.RunMost < 1) pace.RunMost = 1;
        return pace.PauseMost > 0f;
    }
}
