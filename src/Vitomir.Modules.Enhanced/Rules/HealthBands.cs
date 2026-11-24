// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>
/// Each person's Health band on its <see cref="Vitals"/> (<see cref="Phase.Act"/>, order
/// <see cref="HealthBandServices.BandOrder"/>, after the powers wrote the vitals): <see cref="HealthBand.Injured"/> below 25 %
/// of the purchased maximum, <see cref="HealthBand.Crippled"/> below 10 %, <see cref="HealthBand.Stopped"/> at nothing
/// (<see cref="HarmData.Band"/>; the rules package's chapter 06 proposals, in <c>rules/harm</c>'s <c>health.bands</c>).
/// <b>Labels only</b>: nothing reads them for an effect. A change of band — by harm, by healing, by a change of the
/// maximum — is published as <see cref="HealthBandChanged"/> (for the log, the). Allocates nothing per tick (but the
/// event's queue as it grows).
/// </summary>
public sealed class HealthBandSystem(HarmData data) : ISystem
{
    public void Run(World world)
    {
        if (!world.TryStore<Vitals>(out var vitals)) return;
        world.TryService<EventQueue<HealthBandChanged>>(out var changed);
        var people = vitals.Entities;
        var views = vitals.Data;
        for (int i = 0; i < people.Length; i++)
        {
            ref var v = ref views[i];
            var band = data.Band((long)v.Health, (long)v.HealthMax);
            if (band == v.Band) continue;
            changed?.Publish(new HealthBandChanged(people[i], world.Tick, v.Band, band, (int)v.Health, (int)v.HealthMax));
            v.Band = band;
        }
    }
}

/// <summary>The Health bands registered.</summary>
public static class HealthBandServices
{
    /// <summary>In <see cref="Phase.Act"/>, after the powers (<see cref="PowerServices.PowersOrder"/>, which write the vitals) and before the tempo.</summary>
    public const int BandOrder = 85;

    /// <summary>Registers the event <see cref="HealthBandChanged"/> and the system <c>enhanced.health-bands</c>.</summary>
    public static WorldBuilder Register(WorldBuilder b, HarmData data)
    {
        if (!b.Peek.TryService<EventQueue<HealthBandChanged>>(out _)) b.Event<HealthBandChanged>();
        b.System(Phase.Act, BandOrder, "enhanced.health-bands", new HealthBandSystem(data));
        return b;
    }
}
