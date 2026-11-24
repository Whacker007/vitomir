// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Classic snapshot's producer.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Scene;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The snapshot's producer reads the game and leaves it as it was** (Engine.World.WorldSnapshot, Engine.Scene.SceneSnapshot),
/// fills its two frames in turn, and once its lists have grown copies the scene without allocating. No GPU: the producer is
/// the engine's, which the Classic module and vitomir-tools call.
/// </summary>
public class ClassicSnapshotTests
{
    const int W = 640, H = 360;

    sealed record Played(Location Place, Simulation Sim, WorldFrame Game, ScenePoses Poses, WorldSnapshot Snapshot, FreeCamera Camera);

    /// <summary>A place with its characters, a second of its world played through the frame's game side (with the poses, as a picture's).</summary>
    static Played Play(int frames = 30)
    {
        var place = Location.Load(Install.Open(), "P1_MON1", scene: "MON1_8_2", player: "Christof");
        var sim = new Simulation(place);
        var poses = new ScenePoses(place.Mesh) { Things = place.Drawn, Place = place };
        var game = new WorldFrame(place, sim) { Poses = poses, Ring = true };
        var snapshot = new WorldSnapshot(place, game, poses);
        var camera = place.Camera();
        for (int i = 0; i < frames; i++) game.Frame(camera, W, H, 1f / 30f, 1f / 30f);
        return new Played(place, sim, game, poses, snapshot, camera);
    }

    [Fact]
    public void Filling_the_snapshot_leaves_the_game_as_its_game_side_left_it()
    {
        var p = Play();
        var digest = SimDigest.Take(p.Place, 1, p.Sim.Milliseconds);
        var first = p.Snapshot.Fill(p.Camera, W, H);
        var second = p.Snapshot.Fill(p.Camera, W, H);
        Assert.Null(SimDigest.Compare(digest, SimDigest.Take(p.Place, 1, p.Sim.Milliseconds)));
        // What the frame's game side left is what the snapshot carries, the same twice over.
        Assert.True(first.Scene.ThingCount > 0);
        Assert.Equal(p.Place.Drawn.Count, first.Scene.ThingCount);
        Assert.True(first.Scene.Posed.Count > 0, "the characters were posed");
        Assert.Equal(first.Scene.VertexCount, second.Scene.VertexCount);
        Assert.True(first.Scene.Vertices.AsSpan(0, first.Scene.VertexCount).SequenceEqual(second.Scene.Vertices.AsSpan(0, second.Scene.VertexCount)));
        for (int i = 0; i < first.Scene.ThingCount; i++)
        {
            var (a, b) = (first.Scene.Things[i], second.Scene.Things[i]);
            Assert.Equal((a.Placed, a.Placement, a.ModelAlpha, a.Fade, a.InWorld, a.ModelHidden, a.Shell),
                         (b.Placed, b.Placement, b.ModelAlpha, b.Fade, b.InWorld, b.ModelHidden, b.Shell));
        }
        Assert.Equal(first.Layout, second.Layout);
        Assert.Equal(first.Camera, second.Camera);
        Assert.Equal(first.Shadows.Pieces, second.Shadows.Pieces);
    }

    [Fact]
    public void The_resources_go_once_and_the_frames_state_every_time()
    {
        var p = Play(1);
        var first = p.Snapshot.Fill(p.Camera, W, H);
        Assert.Equal(p.Place.Materials.Items.Count, first.Scene.Materials.Count);
        Assert.Equal(first.Scene.Drawable, first.Scene.NewPictures.Count);         // every material the place had, with its pictures
        Assert.Equal(p.Place.Mesh.Revision, first.Scene.Mesh.Revision);
        Assert.Equal(p.Place.Mesh.Batches.Count, first.Scene.Mesh.Batches.Length);
        Assert.Equal(p.Place.Level.Lightmaps.Length, p.Snapshot.Lightmaps.Count);
        var second = p.Snapshot.Fill(p.Camera, W, H);
        Assert.Empty(second.Scene.NewPictures);                                    // nothing new: nothing sent again
        Assert.Same(first.Scene.Mesh, second.Scene.Mesh);                          // the same revision, the same resource
        Assert.Same(first.Level, second.Level);
        Assert.True(second.Scene.MaterialFrames.Length >= p.Place.Materials.Items.Count);   // each material's frame, every frame
    }

    [Fact]
    public void The_frame_carries_the_cameras_fade_and_flash_as_its_game_side_left_them()
    {
        // The full-screen effects are the frame's world state; the effects pass reads only this.
        var p = Play(frames: 1);
        Assert.Equal(ClassicScreen.None, p.Snapshot.Fill(p.Camera, W, H).Screen);           // no camera's effects handed in
        var fx = new ScreenEffects();
        p.Snapshot.Effects = fx;
        Assert.Equal(ClassicScreen.None, p.Snapshot.Fill(p.Camera, W, H).Screen);           // none running
        fx.AddFade(255f, 0f, 1000, false, 0);
        fx.AddFlash(255f, 0f, 1000, 0x3366ff, false, 0);
        fx.Step(250, 250);
        var screen = p.Snapshot.Fill(p.Camera, W, H).Screen;
        Assert.Equal(new ClassicScreen(fx.FadeLevel, fx.FlashIntensity, fx.FlashColor), screen);
        Assert.True(screen.Fades && screen.Flashes && screen.FadeLevel < 255f && screen.FlashColor == 0x3366ff);
        fx.ClearAll();
        Assert.Equal(screen, p.Snapshot.Front.Screen);                                     // the published frame keeps what it was filled with
    }

    [Fact]
    public void The_two_frames_are_filled_in_turn_and_the_scenes_copy_allocates_nothing_once_grown()
    {
        var p = Play();
        var a = p.Snapshot.Fill(p.Camera, W, H);
        var b = p.Snapshot.Fill(p.Camera, W, H);
        Assert.NotSame(a, b);
        Assert.Same(a, p.Snapshot.Fill(p.Camera, W, H));
        Assert.Same(a, p.Snapshot.Front);
        // The copy itself (the things, the batches' bits, the posed and relit vertices, the materials' frames) into a frame
        // whose lists have grown to the place: nothing allocated.
        var scene = new ClassicScene();
        p.Snapshot.Scene.Fill(scene, 1f);
        p.Snapshot.Scene.Fill(scene, 1f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++) p.Snapshot.Scene.Fill(scene, 1f);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"the scene's copy allocated {allocated} bytes in ten fills");
    }
}
