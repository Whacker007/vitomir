// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A thing riding another is carried, not put** (found by a comparison with 1.1): each
/// frame the child is set at its attach point and, when the parent is in a world, filed in the
/// sector it is now in — and no region test is run for it (the region test is
/// SetPosition's, and a rider does not move itself). Vitomir had put the child through
/// the whole of `SetPosition`, so a torch carried into a region was heard entering it.
/// </summary>
public class RiderTests
{
    [Fact]
    public void A_torch_carried_into_a_region_is_not_heard_entering_it()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var christof = place.Player!.Thing;
        var region = place.Things.First(t => t.Type == ThingType.Region && (t.RegionFlags & Regions.NoTracking) == 0
                                             && t.Scene.Model is { Planes.Length: > 0 }
                                             && Regions.Contains(t, t.Position));
        var torch = place.Place("torchFlame", christof.Position, christof.Orientation, christof.Sector)!;
        Assert.True(Attachments.Attach(christof, torch, -1, Vector3.Zero, torch.Rotation, 0));
        var seen = new List<string>();
        place.Regions!.Entered = (r, who) => seen.Add($"in {r.Guid} {who.Guid}");

        place.Move(christof, region.Position);
        Attachments.Update(christof);

        Assert.Equal(region.Position, torch.Position);
        Assert.Equal(christof.Sector, torch.Sector);                       // filed where it now is
        Assert.Equal([$"in {region.Guid} {christof.Guid}"], seen);          // and only Christof is heard
    }
}
