// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Particles;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A drawn place's first frame registers only what its walk reaches**. 1.1 puts an emitter in
/// its 128-slot list only as its thing is drawn (whose one caller is the thing draw,
/// run over the walk's sectors); a full list turns it away and it tries again
/// the next time it is drawn; an unseen one keeps its slot for 60 frames. Vitomir steps
/// the world before it walks, and on the first frame — no walk yet — had counted every thing as
/// drawn, so Monastery 1's torches out of sight filled the list beside the flames in view and a new
/// fire found no slot. Now the renderer's first step counts nothing as drawn.
/// </summary>
public class ParticleFirstFrameTests
{
    [Fact]
    public void A_drawn_place_s_first_frame_registers_only_what_the_walk_reaches()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2");
        var ps = place.Particles;
        var sim = new Simulation(place);
        ps.BeforeFirstDraw();                                      // the frame renderer, before its first step
        sim.Step(1f / 30f);
        Assert.All(ps.Live, slot => Assert.Null(slot));            // no walk yet: nothing was drawn

        // The walk that frame drew: from above where the party starts, looking along +x.
        var eye = place.StartPosition + new Vector3(0f, 0f, 120f);
        var view = Matrix4x4.CreateLookAt(eye, eye + new Vector3(100f, 0f, -10f), Vector3.UnitZ);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 16f / 9f, 10f, 10000f);
        var walk = new PortalView();
        int start = place.Solid!.SectorAt(eye, -1);
        Assert.True(start >= 0);
        walk.Walk(place.Level, start, eye, view * projection, ScreenRect.Full, PortalCamera.From(view, projection));
        ps.View = walk;
        for (int i = 0; i < 9; i++) sim.Step(1f / 30f);

        bool Seen(WorldThing t) { while (t.AttachedTo is { } up) t = up; return t.Sector < 0 || walk.Sees((int)t.Sector); }
        var live = ps.Live.Where(e => e is not null).Select(e => e!).ToList();
        Assert.NotEmpty(live);
        Assert.All(live, e => Assert.True(e.Thing is null || Seen(e.Thing), $"{e.Name} out of sight holds a slot"));
        Assert.True(live.Count < ParticleSystem.Slots, $"{live.Count} of {ParticleSystem.Slots}");
        Assert.Contains(ps.All, e => e.Name == "flametest" && !e.Registered);   // a torch out of sight holds none
        // So a new fire — a `damageme` flame — finds a slot at frame 10.
        var fire = ps.All.First(e => e.Name == "flametest" && !e.Registered);
        ps.Touch(fire);
        Assert.True(fire.Registered);
    }

    /// <summary>With no renderer (the tests, <c>--settle</c>) every thing still counts as drawn, and a walk from no sector hands that back.</summary>
    [Fact]
    public void Without_a_renderer_every_thing_counts_as_drawn_as_before()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2");
        var sim = new Simulation(place);
        sim.Step(1f / 30f);
        Assert.Equal(ParticleSystem.Slots, place.Particles.Live.Count(e => e is not null));
        var other = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2");
        other.Particles.BeforeFirstDraw();
        other.Particles.View = null;                               // the eye in no sector: the walk says nothing
        new Simulation(other).Step(1f / 30f);
        Assert.Equal(ParticleSystem.Slots, other.Particles.Live.Count(e => e is not null));
    }
}
