// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.World;

namespace Vitomir.Render.Tests;

/// <summary>
/// **The old renderer only reads the game**. Everything a frame does to
/// the game around its picture is the frame's own game side (<see cref="WorldFrame"/>), run before the draw; the renderer
/// draws what it left. The proof chosen: the game taken after the frame's game side, the frame then drawn twice, and the game
/// taken again — the replay harness's own digest (<see cref="SimDigest"/>: the clock, every stream's draws, every thing's row)
/// and, beside it, everything the old renderer used to write that the digest does not see (the posed and lit vertices of the
/// shared mesh, the walk handed to the particles and the lights, the light blobs, the click ring's clock, the sky's frame, the
/// interface's clock, the light styles). A renderer that stepped, walked, posed or gathered would change one of them. A
/// read-only view of the game was weighed and left: the renderer reads the engine's own live objects (the mesh, the
/// particles, the lights) until the snapshot replaces them, and wrapping each would be that work twice.
/// </summary>
[Collection("headless")]
public class RendererReadOnlyTests(Headless h)
{
    const int W = 320, H = 180;

    [Fact]
    public void A_frame_drawn_twice_leaves_the_game_as_its_game_side_left_it()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var place = Location.Load(h.Files!, "P1_HAVN");
        using var frame = new FrameRenderer(h.Gpu!, place);
        frame.Marker = frame.Drawn.NewMarker(place.Materials.Get(WalkTarget.Material));
        var camera = place.Camera();
        // A ring on the floor ahead, so its clock has something to run (it runs in the game side, never the draw).
        if (Pick.Nearest(place.Level, camera.Position + camera.Forward * 260f, -System.Numerics.Vector3.UnitZ) is { } hit)
            frame.Sim.WalkTarget.Set(hit.Point, Pick.NormalAt(place.Level, hit));
        for (int i = 0; i < 30; i++) frame.Game.Frame(camera, W, H, 1f / 30f, 1f / 30f);   // a second of the world
        var digest = SimDigest.Take(place, 1, frame.Sim.Milliseconds);
        string rest = Rest(place, frame);

        for (int i = 0; i < 2; i++) _ = frame.RenderToImage(W, H, camera);

        Assert.Null(SimDigest.Compare(digest, SimDigest.Take(place, 1, frame.Sim.Milliseconds)));
        Assert.Equal(rest, Rest(place, frame));
    }

    /// <summary>What the old renderer wrote that the digest does not carry, as text.</summary>
    static string Rest(Location place, FrameRenderer frame)
    {
        var s = new StringBuilder();
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        long hash = 17;
        void Mix(float v) => hash = unchecked(hash * 31 + BitConverter.SingleToInt32Bits(v));
        foreach (var a in place.Mesh.Animated)
        {
            foreach (var v in a.Vertices) { Mix(v.Position.X); Mix(v.Position.Y); Mix(v.Position.Z); hash = unchecked(hash * 31 + v.Color); }
            foreach (var part in a.Carried)
                foreach (var v in part.Vertices) { Mix(v.Position.X); Mix(v.Position.Y); Mix(v.Position.Z); hash = unchecked(hash * 31 + v.Color); }
            Mix(a.Reach);
        }
        foreach (var v in place.Mesh.Vertices) hash = unchecked(hash * 31 + v.Color);
        s.Append("mesh=").Append(hash).Append(" animated=").Append(place.Mesh.Animated.Count);
        s.Append(" particlesView=").Append(ReferenceEquals(place.Particles.View, frame.Game.View) ? "walk" : place.Particles.View is null ? "none" : "other");
        s.Append(" lightsView=").Append(ReferenceEquals(place.Lights.View, frame.Game.View) ? "walk" : place.Lights.View is null ? "none" : "other");
        s.Append(" blobs=").Append(place.Lights.Blobs.Count);
        s.Append(" eye=").Append(place.Particles.Eye);
        s.Append(" ring=").Append(frame.Sim.WalkTarget.Active).Append(',').Append(F(frame.Sim.WalkTarget.Elapsed));
        s.Append(" sky=").Append(place.Sky?.Player is { } sky ? F(sky.Frame) : "-");
        s.Append(" ui=").Append(frame.Sim.InterfaceSeconds is { } ui ? F(ui) : "-");
        s.Append(" styles=");
        for (int i = 0; i < LightStyles.Count; i++) s.Append(F(place.Styles.Value(i))).Append(',');
        return s.ToString();
    }
}
