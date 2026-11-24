// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Render;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Scripting;

namespace Vitomir.Render.Tests;

/// <summary>
/// Frames drawn with no window: what the renderer actually puts on screen, checked without anyone
/// watching. Each test skips with a reason when there is no install or no GPU.
/// </summary>
[Collection("headless")]
public class FrameTests(Headless h)
{
    void Ready() => Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");

    /// <summary>
    /// Two draws of the same level are the same picture.
    ///
    /// **This fails on the cloud container's software Vulkan (Mesa `lvp`), and the failure is
    /// real rather than driver noise.** Measured, four frames in a row: the **first**
    /// differs from the other three in 0.19% of its pixels, and frames two, three and four are
    /// byte-identical to each other, in the same places every run. Each frame builds a fresh
    /// `Location` and `FrameRenderer` and only the `Gpu` is shared, so something that belongs to the
    /// device — most likely a texture upload — is not finished when the first frame is drawn. A
    /// missing wait of that kind can hide entirely on one driver and show on another, and that is
    /// what is happening: the suite run on **Direct3D 12** the same day had all nineteen
    /// tests pass, none skipped. So the difference is Vulkan-only, and the open question is whether
    /// it is a barrier this tree owes and D3D12 happens to supply, or a Mesa `lvp` artefact. The
    /// upload path itself (`Gpu.Upload`) submits its copy pass on its own command buffer, which
    /// SDL_GPU orders against later submissions, so the suspect is narrower than "uploads": the one
    /// thing that happens once per device rather than once per frame.
    /// </summary>
    [Fact]
    public void A_level_draws_the_same_frame_twice()
    {
        Ready();
        var a = h.Frame("P1_HAVN");
        var b = h.Frame("P1_HAVN");
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("P1_HAVN")]      // Prague, the first haven
    [InlineData("N1_SLUM")]      // New York, the slums
    [InlineData("V1_ORSI")]      // Vienna
    [InlineData("l1_tol4")]      // London, the Tower's outer ward, under the storm
    public void A_level_draws_something_other_than_black(string level)
    {
        Ready();
        var frame = h.Frame(level);
        Assert.InRange(Headless.Mean(frame), 0.01f, 0.9f);
    }

    [Fact]
    public void Every_level_loads_and_draws()
    {
        Ready();
        var levels = h.Files!.List("levels/")
                      .Where(n => n.EndsWith(".nil", StringComparison.OrdinalIgnoreCase))
                      .Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToArray();
        Assert.True(levels.Length > 100, $"expected the game's levels, found {levels.Length}");
        var blank = new List<string>();
        foreach (var level in levels)
            if (Headless.Mean(h.Frame(level, 160, 90)) <= 0.002f) blank.Add(level);
        Assert.Empty(blank);
    }

    [Fact]
    public void Turning_the_things_off_changes_what_is_drawn()
    {
        Ready();
        var withThings = h.Frame("N1_SLUM");
        var location = Location.Load(h.Files!, "N1_SLUM", things: false);
        using var frame = new FrameRenderer(h.Gpu!, location);
        var without = Headless.Shoot(frame, 320, 180, location.Camera());
        Assert.True(Headless.Differing(withThings, without) > 0.1f, "the scene's things should be visible");
    }

    [Fact]
    public void A_thing_that_animates_looks_different_a_moment_later()
    {
        Ready();
        // Old Town by day hangs the Four Stags' sign on a bracket and flies two flags; all three
        // have a motion set whose only mode is `create`, and all three are always moving. Nothing
        // else in the level's own scene changes on its own, so any difference is the animation.
        var now = h.Frame("P1_OTDY");
        var later = h.Frame("P1_OTDY", seconds: 1.2f);
        Assert.True(Headless.Differing(now, later) > 0.002f, "nothing moved in 1.2 seconds");
        // And it is the things that moved, not the whole frame drifting.
        Assert.True(Headless.Differing(now, later) < 0.5f, "far more of the frame changed than the props");
    }

    [Fact]
    public void An_idling_character_leaves_the_rest_pose()
    {
        Ready();
        // The merchant in the square idles: hands together, then to his face, then on his hips.
        // Posed vertices stay on the thing, so what changes is his shape, not where he is.
        var location = Location.Load(h.Files!, "P1_OTDY", scene: "OTDY_4_1");
        var actor = location.Mesh.Animated.First(a => a.Animation.Mode!.Name == "stand");
        var rest = actor.Animation.Model.Vertices.Select(v => v.Position).ToArray();
        using var frame = new FrameRenderer(h.Gpu!, location);
        frame.Game.Advance(0.6f);
        var still = Headless.Shoot(frame, 320, 180, location.Camera());
        Assert.NotEmpty(still);
        var moved = actor.Vertices.Select((v, i) => System.Numerics.Vector3.Distance(
            System.Numerics.Vector3.Transform(rest[i], actor.World), v.Position)).Max();
        Assert.True(moved > 1f, $"the idle moved nothing (largest vertex move {moved:F2})");
    }

    [Fact]
    public void The_player_is_drawn_and_walking_moves_them_across_the_frame()
    {
        Ready();
        // Christof in Old Town square, seen from behind. Walking has to change the picture, and it
        // has to change it because he moved — so the same walk from a camera that does not follow
        // him leaves him in a different place on screen.
        var location = Location.Load(h.Files!, "P1_OTDY", scene: "OTDY_4_1", player: "Christof");
        var player = location.Player!;
        Assert.True(player.Node >= 0);
        using var frame = new FrameRenderer(h.Gpu!, location);
        var camera = location.Camera();
        camera.Pitch = FreeCamera.ChasePitch;
        camera.Yaw = player.Heading + 90f;
        camera.Orbit(player.Position + new System.Numerics.Vector3(0, 0, FreeCamera.ChaseHeight),
                     FreeCamera.ChaseDistance);

        var standing = Headless.Shoot(frame, 320, 180, camera);
        for (int i = 0; i < 60; i++) { player.Move(player.Facing, 1f / 60f); frame.Game.Advance(1f / 60f); }
        Assert.False(player.Blocked);
        var walked = Headless.Shoot(frame, 320, 180, camera);
        // The camera has not moved, so everything that changed is him.
        float moved = Headless.Differing(standing, walked);
        Assert.InRange(moved, 0.01f, 0.6f);
    }

    [Fact]
    public void A_materials_pass_lines_change_what_is_drawn()
    {
        Ready();
        // P1_CTDY's light shafts are a two-pass material; without the passes they are a plain texture.
        var passes = h.Frame("P1_CTDY");
        var plain = h.Frame("P1_CTDY", prepare: l =>
        {
            foreach (var m in l.Materials.Items) m.DrawPasses = false;
        });
        Assert.True(Headless.Differing(passes, plain) > 0.02f, "the cathedral's light shafts should show");
    }
}

/// <summary>
/// The Tower of London's storm, drawn: the game's own Lightning script, the engine's light styles
/// and page-flipped materials, and the frames that come out the other end. This is the one test
/// that covers the whole chain, which is where every bug in it has been hiding.
/// </summary>
[Collection("headless")]
public class StormTests(Headless h)
{
    void Ready() => Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");

    /// <summary>The script layer over the player's install (its shipped classes, the sources with VITOMIR_SCRIPT_SOURCES), or null when it has none.</summary>
    static CodexScenes? Scripts(Location location)
    {
        var dir = GameFiles.Locate();
        if (dir is null) return null;
        if (CodexCompiler.SourcesBySeam ? !Directory.Exists(Path.Combine(dir, "Codex")) : !File.Exists(Path.Combine(dir, "Codex.nob"))) return null;
        var compiler = CodexCompiler.ForInstall(dir, Path.Combine(Path.GetTempPath(), "vitomir-codex-tests"));
        var scenes = new CodexScenes(ScriptRuntime.Start(compiler, new CodexHost()));
        scenes.Enter(location);
        return scenes;
    }

    [Fact]
    public void With_the_script_running_the_sky_is_quiet_with_occasional_strikes()
    {
        Ready();
        var location = Location.Load(h.Files!, "l1_tol4");
        var scenes = Scripts(location);
        Assert.SkipWhen(scenes is null, "this install has no Codex script sources");
        using var frame = new FrameRenderer(h.Gpu!, location) { Scripts = scenes! };
        var camera = location.Camera();

        var means = new List<float>();
        for (int i = 0; i < 600; i++)                     // ten seconds at sixty frames
        {
            frame.Game.Advance(1f / 60f);
            means.Add(Headless.Mean(Headless.Shoot(frame, 160, 90, camera)));
        }

        // The storm is genuinely random: the dark interval between strikes is `1 + random×3`
        // seconds, rolled by the game's own Lightning class with an unseeded Math.random(). So the
        // test asserts on the distribution over ten seconds, never on one frame. In particular the
        // brightest frame depends on whether two or three of the three bolts happen to overlap
        // inside the window, which is luck — it made this test fail on one machine and pass on
        // another with the same code.
        float quiet = means.Order().ElementAt(means.Count / 2);
        var lit = means.Where(m => m > quiet * 1.05f).ToList();
        Assert.InRange(lit.Count, 6, 300);                // strikes of 0.2 s, not a constant flicker
        Assert.True(means.Max() > quiet * 1.2f, "a strike should be brighter than the quiet sky");
        // How much brighter the sky is *while a bolt is lit*, averaged over every lit frame, which
        // does not depend on catching a peak. Measured 1.28-1.32 over repeated runs.
        float litMean = lit.Average() / quiet;
        Assert.True(litMean > 1.10f, $"a strike averages only {litMean:F2}x the quiet sky");
    }

    [Fact]
    public void With_no_script_running_the_bolts_stay_on_their_black_frame()
    {
        Ready();
        // Nothing else may switch them: the page flip is 100 s and the storm is entirely script-driven.
        var location = Location.Load(h.Files!, "l1_tol4");
        var bolt = location.Materials.Find("lightning1")!;
        using var frame = new FrameRenderer(h.Gpu!, location);
        frame.Game.Advance(10f);
        Assert.Equal(0, bolt.CurrentFrame);
        Assert.True(Headless.Mean(Headless.Shoot(frame, 320, 180, location.Camera())) < 0.1f);
    }
}
