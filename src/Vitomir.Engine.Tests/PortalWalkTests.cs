// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The portal walk's camera tests**: beyond the eye's
/// side of a portal and its quad on the screen, 1.1 passes a portal over when the camera looks along
/// its normal by less than half the field of view (against the camera's half-angle cosine,
/// worked out with the projection) or when all four of its corners lie past the far clip (against
/// the camera's far clip, the scene's ZCLIP or 10,000). These pin that the camera's three numbers come
/// back out of Vitomir's own matrices, that the far test cuts the walk short, and that neither test
/// ever adds a sector.
/// </summary>
public class PortalWalkTests
{
    static (Vector3 Eye, Matrix4x4 View, Matrix4x4 Projection) Camera(Vector3 eye, Vector3 at, float far = 10000f) =>
        (eye, Matrix4x4.CreateLookAt(eye, at, Vector3.UnitZ), Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 16f / 9f, 10f, far));

    /// <summary>
    /// The look, the half field of view across (the engine keeps the horizontal one) and
    /// the far clip are read back out of a look-at and a perspective projection.
    /// </summary>
    [Fact]
    public void The_camera_numbers_come_back_out_of_the_matrices()
    {
        var (_, view, proj) = Camera(new Vector3(10f, 20f, 30f), new Vector3(110f, 20f, 30f), 2500f);
        var c = PortalCamera.From(view, proj);
        Assert.True(Vector3.Distance(Vector3.UnitX, c.Forward) < 1e-5f, $"{c.Forward}");
        Assert.Equal(2500f, c.Far, 0.5f);
        // Horizontal: tan(half) = tan(0.6) × 16/9.
        float half = MathF.Atan(MathF.Tan(0.6f) * 16f / 9f);
        Assert.Equal(MathF.Cos(half), c.CosHalfFov, 1e-4f);
        // The corner: tan² across plus tan² up and down.
        float corner = MathF.Atan(MathF.Sqrt(MathF.Pow(MathF.Tan(half), 2) + MathF.Pow(MathF.Tan(0.6f), 2)));
        Assert.Equal(MathF.Sin(corner), c.SinHalfCorner, 1e-4f);
    }

    /// <summary>
    /// **The facing test drops only what is off the screen, on any window's shape**: over the Old
    /// Town, eyes near portals looking every way, the walk with the camera's tests reaches exactly the
    /// sectors the walk without them does (no far clip in play) — at 1.1's 4:3, at 16:9 and at 21:9.
    /// With 1.1's bound (the cosine of half the view across) a 16:9 window dropped sectors on the
    /// screen in about one look in thirty near a portal, and along the camera's path of a
    /// play-test in 85 of 6,324 frames: the flashing seen in play.
    /// </summary>
    [Fact]
    public void A_wide_screen_never_drops_a_sector_the_camera_sees()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: false, sky: false);
        var level = place.Level;
        var solid = place.Solid!;
        float fovY = 2f * MathF.Atan(MathF.Tan(40f * MathF.PI / 180f) * 3f / 4f);   // 80° across at 4:3
        foreach (var aspect in new[] { 4f / 3f, 16f / 9f, 21f / 9f })
        {
            var rng = new Random(7);
            int looks = 0;
            for (int i = 0; i < 1500; i++)
            {
                var sector = level.Sectors[rng.Next(level.Sectors.Length)];
                if (sector.Portals.Length == 0) continue;
                var portal = sector.Portals[rng.Next(sector.Portals.Length)];
                if (portal.Quad.Length < 3) continue;
                var middle = (portal.Quad[0] + portal.Quad[1] + portal.Quad[2]) / 3f;
                var eye = middle + portal.Normal * (5f + (float)rng.NextDouble() * 200f)
                        + new Vector3((float)rng.NextDouble() * 100f - 50f, (float)rng.NextDouble() * 100f - 50f, (float)rng.NextDouble() * 60f - 30f);
                int start = solid.SectorAt(eye, -1);
                if (start < 0) continue;
                float yaw = (float)(rng.NextDouble() * Math.PI * 2), pitch = (float)((rng.NextDouble() - 0.5) * 0.9);
                var forward = new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch));
                var view = Matrix4x4.CreateLookAt(eye, eye + forward, Vector3.UnitZ);
                var proj = Matrix4x4.CreatePerspectiveFieldOfView(fovY, aspect, 10f, 1e6f);
                var plain = new PortalView();
                plain.Walk(level, start, eye, view * proj, ScreenRect.Full);
                var tested = new PortalView();
                tested.Walk(level, start, eye, view * proj, ScreenRect.Full, PortalCamera.From(view, proj));
                Assert.True(plain.Visible.Count == tested.Visible.Count,
                    $"aspect {aspect:0.00}, look {i}: {plain.Visible.Count - tested.Visible.Count} of {plain.Visible.Count} sectors dropped from {eye}");
                looks++;
            }
            Assert.True(looks > 1000, $"{looks}");
        }
    }

    /// <summary>
    /// **A walk starts from the sector the eye is really in**: an eye a tenth of a unit past a
    /// portal, with the sector it came from as the hint, is still answered with that sector by the
    /// search (its slack), and a walk from there would pass over the portal it is behind and never
    /// reach the sector it is in. Settled, the walk starts beyond the portal and reaches it.
    /// </summary>
    [Fact]
    public void An_eye_just_past_a_portal_starts_the_walk_beyond_it()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: false, sky: false);
        var level = place.Level;
        var solid = place.Solid!;
        int tried = 0;
        for (int a = 0; a < level.Sectors.Length && tried < 40; a++)
            foreach (var portal in level.Sectors[a].Portals)
            {
                if ((portal.Flags & 1) != 0 || portal.Quad.Length < 3 || portal.NeighbourSector >= (uint)level.Sectors.Length) continue;
                int b = (int)portal.NeighbourSector;
                var middle = portal.Quad.Aggregate(Vector3.Zero, (sum, q) => sum + q) / portal.Quad.Length;
                var eye = middle - portal.Normal * 0.1f;
                if (solid.SectorAt(eye, a) != a || solid.Depth(b, eye) < 0.05f) continue;   // clearly in b, not on a corner shared with a third
                tried++;
                int got = PortalView.SettledStart(level, solid, a, eye);
                Assert.True(got == b, $"from {a} past its portal into {b}: started from {got}");
                var fwd = -portal.Normal;
                var view = Matrix4x4.CreateLookAt(eye, eye + fwd, Vector3.UnitZ);
                var proj = Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 16f / 9f, 10f, 1e6f);
                var from = new PortalView();
                from.Walk(level, PortalView.SettledStart(level, solid, a, eye), eye, view * proj, ScreenRect.Full, PortalCamera.From(view, proj));
                Assert.True(from.Sees(b));
                break;
            }
        Assert.True(tried >= 10, $"{tried}");
    }

    /// <summary>
    /// The far test: a polygon whose every corner lies past the far clip is not seen, one
    /// with a corner this side of it is.
    /// </summary>
    [Fact]
    public void A_portal_wholly_past_the_far_clip_is_not_seen()
    {
        var (_, view, proj) = Camera(Vector3.Zero, Vector3.UnitX);
        var vp = view * proj;
        Vector3[] far = [new(900f, -50f, -50f), new(900f, 50f, -50f), new(900f, 50f, 50f), new(900f, -50f, 50f)];
        Assert.True(PortalView.Project(far, vp, ScreenRect.Full, out _));
        Assert.False(PortalView.Project(far, vp, ScreenRect.Full, out _, PortalView.NearW, 800f));
        Vector3[] across = [new(700f, -50f, -50f), new(900f, 50f, -50f), new(900f, 50f, 50f), new(700f, -50f, 50f)];
        Assert.True(PortalView.Project(across, vp, ScreenRect.Full, out _, PortalView.NearW, 800f));
    }

    /// <summary>
    /// In Count Orsi's palace: with the camera's tests the walk reaches a subset of the sectors it
    /// reaches without them, and a short far clip reaches fewer still — every sector it keeps is
    /// one a portal nearer than the clip leads to, or the eye's own.
    /// </summary>
    [Fact]
    public void The_far_clip_cuts_the_walk_short_and_the_tests_never_add_a_sector()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "V1_ORSI", things: false, sky: false);
        var eye = new Vector3(-1100f, 400f, 400f);
        var at = new Vector3(-1280f, 400f, 264f);
        int start = place.Solid!.SectorAt(eye, -1);
        var (_, view, proj) = Camera(eye, at);
        var plain = new PortalView();
        plain.Walk(place.Level, start, eye, view * proj, ScreenRect.Full);
        var tested = new PortalView();
        tested.Walk(place.Level, start, eye, view * proj, ScreenRect.Full, PortalCamera.From(view, proj));
        Assert.All(tested.Visible, v => Assert.True(plain.Sees(v.Sector)));

        var (_, _, shortProj) = Camera(eye, at, 150f);
        var near = new PortalView();
        near.Walk(place.Level, start, eye, view * shortProj, ScreenRect.Full, PortalCamera.From(view, shortProj));
        Assert.True(near.Visible.Count < tested.Visible.Count, $"{near.Visible.Count} vs {tested.Visible.Count}");
        Assert.Equal(start, near.Visible[0].Sector);
        Assert.All(near.Visible, v => Assert.True(tested.Sees(v.Sector)));
    }

    /// <summary>
    /// **A walk that runs to its step limit does not run out of stack** (a play-test:
    /// the game ended with a stack overflow in the portal walk in the third monastery level).
    /// With the eye on the plane of the portals at y = 3804 there, both of a pair of back-to-back portals
    /// pass the eye's-side test, and the two sectors hand each other rectangles that grow by
    /// the last bits of a float, so the walk goes round them to <see cref="PortalView.MaxSteps"/>. 1.1
    /// recurses and Vitomir did, four thousand calls deep, which overflowed Windows' 1 MB
    /// main-thread stack; the walk now keeps its own stack. Run here on a thread with 256 KB, where the
    /// recursion could not have finished.
    /// </summary>
    [Fact]
    public void A_walk_to_the_step_limit_needs_no_deep_stack()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON3", things: false, sky: false);
        var level = place.Level;
        var solid = place.Solid!;
        var eye = new Vector3(-416f, 3804f, 800f);
        float yaw = 150f * MathF.PI / 180f, pitch = -70f * MathF.PI / 180f;
        var forward = new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch));
        var view = Matrix4x4.CreateLookAt(eye, eye + forward, Vector3.UnitZ);
        float fovY = 2f * MathF.Atan(MathF.Tan(40f * MathF.PI / 180f) * 3f / 4f);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(fovY, 16f / 9f, 10f, 10000f);
        int start = PortalView.SettledStart(level, solid, solid.SectorAt(eye, -1), eye);
        var walk = new PortalView();
        var thread = new Thread(() => walk.Walk(level, start, eye, view * proj, ScreenRect.Full, PortalCamera.From(view, proj)), 256 * 1024);
        thread.Start();
        thread.Join();
        Assert.True(walk.Steps > PortalView.MaxSteps, $"{walk.Steps} steps: not the ring this test is about");
        Assert.Equal(start, walk.Visible[0].Sector);
        Assert.Equal(walk.Visible.Count, walk.Visible.Select(v => v.Sector).Distinct().Count());
    }
}
