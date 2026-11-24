// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>The teleporter's two ends and the haven it builds, numbered in the order 1.1 numbers them.</summary>
public class TeleporterGuidTests
{
    /// <summary>
    /// **The teleporter numbers both ends before it loads the portal's place** (a GUID for the near end and then
    /// for the far end, then the placing, then the place's load). The
    /// far end's GUID is the one after the near end's, and a haven built fresh for it is numbered
    /// after both. Vitomir made the near end, built the haven, then made the far end.
    /// </summary>
    [Fact]
    public void The_teleporter_numbers_both_ends_before_the_haven_is_built()
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        var chronicle = where.Chronicle;
        chronicle.Current = where.Location;
        Location Build(string level, string? scene, int entrance) =>
            Location.Load(files, level, sky: false, scene: scene, player: "Christof", entrance: entrance);
        var here = Build("P1_MON1", where.Entry!.SceneName, -1);
        here.Player!.Thing.Actor!.Flags |= Actor.Flag.Party;
        var session = new Session { Here = here, Chronicle = chronicle };
        here.Session = session;
        var campaign = new Campaign { Chronicle = chronicle, Build = Build, Here = here };
        session.Teleports.Campaign = campaign;
        int haven = chronicle.Portal.Location;
        Assert.False(campaign.Resident.ContainsKey(haven));                  // built fresh by the cast
        int next = here.Guids.Next;

        Assert.True(session.Teleports.Create(here.Player.Thing, "havenPortal", "havenPortal", 0, 0));

        static WorldThing Portal(Location place) =>
            place.Things.Single(t => !t.Removed && t.Template.Equals("havenPortal", StringComparison.OrdinalIgnoreCase));
        var near = Portal(here);
        var far = Portal(campaign.Resident[haven]);
        Assert.Equal(next, near.Guid);
        Assert.Equal(near.Guid + 1, far.Guid);
        Assert.True(campaign.Resident[haven].SceneGuids.First > far.Guid);
    }
}
