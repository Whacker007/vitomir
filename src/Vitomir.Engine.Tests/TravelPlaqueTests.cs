// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// When a journey puts the loading plaque up (`Campaign.Builds`): only when the place has to be built
/// — the load calls the plaque, which draws `ui\LoadScreen.nui`, for a place with no world — and not
/// on a return to a place still loaded.
/// </summary>
public class TravelPlaqueTests
{
    [Fact]
    public void A_new_place_is_built_behind_the_plaque_and_a_return_to_a_loaded_one_is_not()
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        var chronicle = where.Chronicle;
        chronicle.Current = chronicle.Find(where.Entry!.Name);
        Location Build(string name, string? scene, int entrance) =>
            Location.Load(files, name, sky: false, scene: scene, player: "Christof", entrance: entrance);
        var campaign = new Campaign { Chronicle = chronicle, Build = Build, Here = Build("P1_MON1", where.Entry.SceneName, -1) };
        string monastery = chronicle.NameOf(chronicle.Current);

        Assert.False(campaign.Builds);                               // nothing asked for
        Assert.True(chronicle.TakeExit(0));
        Assert.True(campaign.Builds);                                // PetrinHillDay is not loaded
        Assert.NotNull(campaign.Travel());

        Assert.True(chronicle.Jump(monastery, 0));
        Assert.False(campaign.Builds);                               // the monastery still is
    }
}
