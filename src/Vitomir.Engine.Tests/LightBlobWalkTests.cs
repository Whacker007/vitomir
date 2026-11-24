// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Explosions;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The light blobs seen through the portal walk**:
/// The light pass runs in the world draw after the walk and takes its lights from the walk's list — the
/// things in each walked sector's own list, in the walk's order — so a torch in a room the camera
/// cannot see lays no blob, and a blob shows in the main view only on a surface of a walked sector
/// (the blobs are part of the group's draw). The models' lights take the same walk for their late
/// pass (the carried lights of the other sectors being drawn). With no walk every light
/// counts, as before.
/// </summary>
public class LightBlobWalkTests
{
    static Location Monastery(Vitomir.Engine.Vfs.GameFiles files) =>
        Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");

    static PortalView WalkFrom(Location place, Vector3 eye, Vector3 at)
    {
        var view = Matrix4x4.CreateLookAt(eye, at, Vector3.UnitZ);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 16f / 9f, 10f, 10000f);
        var walk = new PortalView();
        walk.Walk(place.Level, place.Solid!.SectorAt(eye, -1), eye, view * proj, ScreenRect.Full, PortalCamera.From(view, proj));
        return walk;
    }

    /// <summary>
    /// A torch by Christof lays its blobs while the walk reaches his sector, and none once the walk
    /// is one that does not (a camera in a sector far off, looking away); taking the walk off again
    /// brings them back. Every blob's surface the main view shows is in a walked sector.
    /// </summary>
    [Fact]
    public void A_torch_in_a_room_the_walk_did_not_reach_lays_no_blob()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var sim = new Simulation(place);
        var christof = place.Player!.Thing;
        var torch = place.Place("torchFlame", christof.Position + new Vector3(0f, 0f, 90f), Vector3.Zero, christof.Sector)!;
        sim.Step(0.03f);
        int all = place.Lights.Blobs.Count;
        Assert.True(all > 0);

        // A camera behind Christof looking at him: his sector is walked.
        var eye = christof.Position + new Vector3(0f, 0f, 60f);
        var walk = WalkFrom(place, eye, eye + new Vector3(1f, 0f, 0f));
        Assert.True(walk.Sees((int)torch.Sector));
        place.Lights.View = walk;
        place.Lights.GatherBlobs();
        Assert.Equal(all, place.Lights.Blobs.Count);
        Assert.Contains(place.Lights.Blobs, b => place.Lights.SurfaceSeen(b.Sector));

        // A walk that does not reach the torch's sector: no blob at all.
        var other = new PortalView();
        other.Walk(place.Level, FarSector(place, (int)torch.Sector), Vector3.Zero, Matrix4x4.Identity, ScreenRect.None);
        Assert.False(other.Sees((int)torch.Sector));
        place.Lights.View = other;
        place.Lights.GatherBlobs();
        Assert.Empty(place.Lights.Blobs);

        place.Lights.View = null;
        place.Lights.GatherBlobs();
        Assert.Equal(all, place.Lights.Blobs.Count);
        Assert.All(place.Lights.Blobs, b => Assert.True(place.Lights.SurfaceSeen(b.Sector)));
    }

    /// <summary>
    /// The model lights' late pass takes the carried lights of the other sectors being drawn:
    /// a torch in a sector the walk did not reach lights no one in another sector.
    /// </summary>
    [Fact]
    public void A_torch_the_walk_did_not_reach_lights_no_one_elsewhere()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var sim = new Simulation(place);
        // Wilhem, and a torch over his head that is counted in the next sector along (as one held
        // across a doorway is): lit by it while every sector counts.
        var elsewhere = place.Drawn.First(t => t is { InWorld: true, Actor: not null } && !ReferenceEquals(t, place.Player!.Thing));
        var torch = place.Place("torchFlame", elsewhere.Position + new Vector3(0f, 0f, 90f), Vector3.Zero, elsewhere.Sector)!;
        sim.Step(0.03f);
        torch.Sector = place.Level.Sectors[(int)elsewhere.Sector].Portals[0].NeighbourSector;
        var light = place.Lights.Of(torch)!;
        Assert.NotEqual(elsewhere.Sector, torch.Sector);
        var picker = new ModelLightPicker();
        ModelLight.Pick(picker, 8, elsewhere!, place.Level, place.Styles, place.Lights, prelit: false);
        Assert.Contains(picker.Lights.ToArray(), p => ReferenceEquals(p.Source, light));
        var other = new PortalView();
        other.Walk(place.Level, (int)elsewhere!.Sector, Vector3.Zero, Matrix4x4.Identity, ScreenRect.None);
        place.Lights.View = other;
        ModelLight.Pick(picker, 8, elsewhere, place.Level, place.Styles, place.Lights, prelit: false);
        Assert.DoesNotContain(picker.Lights.ToArray(), p => ReferenceEquals(p.Source, light));
        place.Lights.View = null;
    }

    /// <summary>A sector that is neither the given one nor any of its neighbours.</summary>
    static int FarSector(Location place, int near)
    {
        var skip = new HashSet<int> { near };
        foreach (var p in place.Level.Sectors[near].Portals) skip.Add((int)p.NeighbourSector);
        for (int i = 0; i < place.Level.Sectors.Length; i++) if (!skip.Contains(i)) return i;
        return -1;
    }
}
