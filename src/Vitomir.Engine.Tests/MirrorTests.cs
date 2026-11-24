// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Particles;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The portal walk and the mirrors**: which sectors a
/// camera sees through the portals, which Reflect surfaces are in
/// view and the one plane the view is reflected in, the reflection's arithmetic (−2,
/// a constant of −2) and the near plane put on the glass (ours), and the particles' 60-frame rule driven by
/// the walk — against Count Orsi's ballroom floor and St. Thomas's.
/// </summary>
public class MirrorTests
{
    static (Vector3 Eye, Matrix4x4 View, Matrix4x4 Projection) Camera(Vector3 eye, Vector3 at) =>
        (eye, Matrix4x4.CreateLookAt(eye, at, Vector3.UnitZ), Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 16f / 9f, 10f, 10000f));

    /// <summary>
    /// The walk starts in the eye's sector with the whole view and reaches only sectors whose
    /// portals it sees; every rectangle lies inside the view, and a sector behind the eye's back
    /// through a portal it faces away from is not reached.
    /// </summary>
    [Fact]
    public void The_walk_starts_in_the_eyes_sector_and_keeps_to_the_view()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "V1_ORSI", things: false, sky: false);
        var (eye, view, proj) = Camera(new Vector3(-1100f, 400f, 400f), new Vector3(-1280f, 400f, 264f));
        var walk = new PortalView();
        int start = place.Solid!.SectorAt(eye, -1);
        walk.Walk(place.Level, start, eye, view * proj, ScreenRect.Full);
        Assert.Equal(start, walk.Visible[0].Sector);
        Assert.Equal(ScreenRect.Full, walk.Visible[0].Rect);
        Assert.True(walk.Visible.Count > 1);
        Assert.True(walk.Visible.Count < place.Level.Sectors.Length);
        Assert.All(walk.Visible, v => Assert.True(v.Rect.X0 >= -1.0001f && v.Rect.X1 <= 1.0001f && v.Rect.Y0 >= -1.0001f && v.Rect.Y1 <= 1.0001f, $"{v}"));
        Assert.All(walk.Visible, v => Assert.True(walk.Sees(v.Sector)));
    }

    /// <summary>
    /// **Orsi's floor** (four Reflect groups, Trans 66, at z 264 facing up): seen from above, it is a
    /// mirror in view; the plane is the floor's, and the view is reflected in it — a point 36 above
    /// the floor comes out 36 below — with the near plane on the glass: the room above kept, what is
    /// under the floor cut away.
    /// </summary>
    [Fact]
    public void Orsis_floor_is_a_mirror_and_the_view_is_turned_over_in_it()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "V1_ORSI", things: false, sky: false);
        var (eye, view, proj) = Camera(new Vector3(-1100f, 400f, 400f), new Vector3(-1280f, 400f, 264f));
        var walk = new PortalView();
        walk.Walk(place.Level, place.Solid!.SectorAt(eye, -1), eye, view * proj, ScreenRect.Full);
        var m = Mirrors.Find(place.Level, walk, eye, view, proj);
        Assert.NotNull(m);
        Assert.Equal(new Vector3(0f, 0f, 1f), m!.Plane.Normal, new Vector3Tolerance(1e-4f));
        Assert.Equal(264f, m.Plane.Dist, 3);
        Assert.Equal([6, 7, 8, 9], m.Sectors.Order().ToArray());
        Assert.False(m.Rect.IsEmpty);

        var r = Mirrors.Mirror(m.Plane);
        Assert.Equal(new Vector3(-1200f, 400f, 228f), Vector3.Transform(new Vector3(-1200f, 400f, 300f), r), new Vector3Tolerance(1e-3f));
        Assert.Equal(new Vector3(0f, 0f, -1f), Vector3.TransformNormal(Vector3.UnitZ, r), new Vector3Tolerance(1e-5f));

        // The room above the floor is kept (0 ≤ z/w ≤ 1), what is under it is not.
        foreach (float z in new[] { 280f, 350f, 500f })
        {
            var c = Vector4.Transform(new Vector4(-1250f, 420f, z, 1f), m.ViewProjection);
            Assert.InRange(c.Z / c.W, 0f, 1f);
        }
        var under = Vector4.Transform(new Vector4(-1250f, 420f, 240f, 1f), m.ViewProjection);
        Assert.True(under.Z / under.W < 0f);
    }

    /// <summary>
    /// A mirror is not seen from behind (<c>dist ≤ n · eye</c>): under Orsi's floor
    /// there is no reflection, nor looking away from it.
    /// </summary>
    [Fact]
    public void A_mirror_seen_from_behind_or_off_screen_is_no_mirror()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "V1_ORSI", things: false, sky: false);
        var (eye, view, proj) = Camera(new Vector3(-1100f, 400f, 400f), new Vector3(-1100f, 400f, 900f));   // looking up
        var walk = new PortalView();
        walk.Walk(place.Level, place.Solid!.SectorAt(eye, -1), eye, view * proj, ScreenRect.Full);
        Assert.Null(Mirrors.Find(place.Level, walk, eye, view, proj));
    }

    /// <summary>
    /// **The particles' 60-frame rule follows the walk**: an emitter whose thing is in a sector the
    /// walk did not reach is not touched (the rule runs as the thing is drawn), so it drops out of
    /// the live list after 60 frames and its slot is free — where every emitter in the place used to
    /// count as seen. Riders count with what they ride.
    /// </summary>
    [Fact]
    public void An_emitter_the_walk_does_not_reach_drops_out_after_sixty_frames()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2");
        var ps = place.Particles;
        var sim = new Simulation(place);
        sim.Step(1f / 30f);
        int before = ps.Live.Count(e => e is not null);
        var walk = new PortalView();
        var (eye, view, proj) = Camera(new Vector3(15f, 250f, 160f), new Vector3(15f, 400f, 160f));
        walk.Walk(place.Level, place.Solid!.SectorAt(eye, -1), eye, view * proj, ScreenRect.Full);
        ps.View = walk;
        for (int i = 0; i < 70; i++) sim.Step(1f / 30f);
        var live = ps.Live.Where(e => e is not null).ToList();
        Assert.True(live.Count < before, $"{live.Count} live of {before}");
        Assert.All(live, e => Assert.True(e!.Thing is null || e.Thing.Sector < 0 || walk.Sees((int)e.Thing.Sector)
                                          || e.Thing.AttachedTo is not null));
    }
}

/// <summary>Compares vectors within a tolerance.</summary>
file sealed class Vector3Tolerance(float eps) : IEqualityComparer<Vector3>
{
    public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= eps;
    public int GetHashCode(Vector3 v) => 0;
}
