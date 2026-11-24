// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// A party put down on a start by a script (`TeleportPartyToConversation`, `TeleportPartyToEntrance`:
/// the party's teleport → the thing's placing) is not tested against the regions there. The
/// placement carries the region word into the new sector — a region the new sector does not list hears
/// <c>exited</c> — but the test that fires <c>entered</c> is reached only from SetPosition and
/// SetTransform, so a member put down inside a region hears nothing until it next
/// moves. Found by a comparison with 1.1; Vitomir had fired
/// <c>entered</c> on the spot, which could start a region's trap or conversation in the middle of the
/// scripted scene that moved the party.
/// </summary>
public class PartyTeleportRegionTests
{
    static (Location Place, GameFiles Files) Monastery()
    {
        var dir = GameFiles.Locate();
        Assert.SkipWhen(dir is null, "no game install");
        var files = new GameFiles(dir!);
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        return (Location.Load(files, "P1_MON1", sky: false, scene: where.Entry!.SceneName, player: "Christof"), files);
    }

    [Fact]
    public void A_party_put_down_inside_a_region_hears_entered_only_when_it_moves()
    {
        var (place, files) = Monastery();
        using var _ = files;
        var christof = place.Player!.Thing;
        var region = place.Things.First(t => t.Type == ThingType.Region && (t.RegionFlags & Regions.NoTracking) == 0
                                             && t.Scene.Model is { Planes.Length: > 0 }
                                             && Regions.Contains(t, t.Position));
        var seen = new List<string>();
        place.Regions!.Entered = (r, who) => seen.Add($"in {r.Guid} {who.Guid}");
        place.Regions.Exited = (r, who) => seen.Add($"out {r.Guid} {who.Guid}");

        // A conversation start of our own in the middle of the region (group 9, the leader's slot).
        uint sector = (uint)place.At(region.Position, christof.Sector);
        place.ConversationStarts = [new NsdStart(sector, [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f], region.Position, 9, 1)];

        Assert.True(place.TeleportPartyToConversation(9));
        Assert.Equal(region.Position, christof.Position);
        Assert.Empty(seen);

        // The first step it takes is what tests it (SetPosition → the region test).
        place.Move(christof, region.Position + new Vector3(0.5f, 0, 0));
        Assert.Equal([$"in {region.Guid} {christof.Guid}"], seen);
    }
}
