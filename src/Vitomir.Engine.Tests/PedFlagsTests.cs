// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The wanderer's two switches: `AIMAIN_FLAG_STOPPEDS`, bit 0x2000 of the AI's
/// main flags, parks every wanderer — its action cancelled unforced, its walk stopped, a second to the
/// next think — and a class's `mindflags` 0x1000 makes it run to its spot.
/// Neither is set by a shipped file; the pass of a comparison with 1.1 found Vitomir reading the first
/// from a switch of its own and walking where 1.1 runs.
/// </summary>
public class PedFlagsTests
{
    static (Minds Minds, Mind Ped) Wanderer(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var ped = minds.All.FirstOrDefault(m => m.Mode == AiMode.PedWander && m.SpotCount > 0 && m.Thing.Actor is not null);
        Assert.SkipWhen(ped is null, "no wanderer in this scene");
        foreach (var m in minds.All) { m.NextThink = int.MaxValue; m.NextIdle = int.MaxValue; }
        ped!.NextThink = 0;
        return (minds, ped);
    }

    [Fact]
    public void The_main_flags_stop_peds_bit_parks_a_wanderer_without_a_roll()
    {
        using var files = Install.Open();
        var (minds, ped) = Wanderer(files);
        minds.MainFlags |= AiMainFlag.StopPeds;
        long before = minds.Random.Draws;

        minds.Tick(0.001f);

        Assert.Equal(before, minds.Random.Draws);
        Assert.False(ped.Moving);
        Assert.Equal(minds.Now + Minds.IdleThink, ped.NextThink);
        Assert.True(minds.WanderingOff);
    }

    [Fact]
    public void A_hurried_wanderer_runs_to_its_spot()
    {
        using var files = Install.Open();
        var (minds, ped) = Wanderer(files);
        ped.Class.MindFlags |= Minds.MindHurry;
        for (int i = 0; i < 20 && !ped.Moving; i++)
        {
            ped.NextThink = 0;
            ped.Walker.Stop();
            minds.Tick(0.001f);
        }
        Assert.SkipWhen(!ped.Moving, "the wanderer found no spot it would walk to");
        Assert.True(ped.Walker.Running);
    }
}
