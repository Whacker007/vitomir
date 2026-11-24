// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The world draw's two clocks (`DrawClocks`, a comparison with 1.1): the animated materials run
/// on the interface's clock, which a pause never stops,
/// while the light styles, the lights and the emitters run on the game's. Pinned because Vitomir had
/// frozen the materials — water, fire, sliding textures — along with the game whenever it was paused.
/// </summary>
public class DrawClocksTests
{
    static Simulation Open(Vitomir.Engine.Vfs.GameFiles files) =>
        new(Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1"));

    [Fact]
    public void A_paused_game_s_materials_go_on_by_the_interface_s_clock()
    {
        using var files = Install.Open();
        var sim = Open(files);
        sim.InterfaceSeconds = 0.5f;
        sim.Step(0f);                                   // the game's clock stands
        Assert.Equal(0f, sim.Seconds);
        Assert.Equal(500.0, sim.Location.Materials.ClockMs, 3);
    }

    [Fact]
    public void The_interface_s_frame_is_taken_by_one_step_only()
    {
        using var files = Install.Open();
        var sim = Open(files);
        sim.InterfaceSeconds = 0.5f;
        sim.Step(0f);
        sim.Step(0.25f);                                // a harness's next slice: its own seconds
        Assert.Null(sim.InterfaceSeconds);
        Assert.Equal(750.0, sim.Location.Materials.ClockMs, 3);
    }

    [Fact]
    public void With_one_clock_the_materials_run_on_the_game_s()
    {
        using var files = Install.Open();
        var sim = Open(files);
        sim.Step(0.25f);
        Assert.Equal(250.0, sim.Location.Materials.ClockMs, 3);
    }
}
