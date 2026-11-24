// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats;
using Vitomir.Formats.L2;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The light probes on the CPU (ours; <see cref="ModernProbeBake"/>, <see cref="ModernProbes"/>,
/// <see cref="L2ProbeGrid"/>): the ray tree finds what the rays hit, a probe in the open sees the sky and
/// one under a roof does not, a probe inside a wall is filled from its neighbours, the shaded wall of a
/// street whose other wall is sunlit is lit by the bounce, another sun keeps a rougher bounce, and the chunk
/// keeps the grid through writing and reading.
/// </summary>
public class ModernProbeTests
{
    /// <summary>Scene builder in metres: closed boxes (outward faces) and a ground square, each with an albedo.</summary>
    internal sealed class Builder
    {
        public readonly List<Vector3> Positions = [];
        public readonly List<int> Indices = [];
        public readonly List<Vector3> Albedo = [];

        public Builder Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 albedo)
        {
            int o = Positions.Count;
            Positions.AddRange([a, b, c, d]);
            Indices.AddRange([o, o + 1, o + 2, o, o + 2, o + 3]);
            Albedo.Add(albedo); Albedo.Add(albedo);
            return this;
        }

        public Builder Ground(float half, Vector3 albedo) =>
            Quad(new(-half, -half, 0), new(half, -half, 0), new(half, half, 0), new(-half, half, 0), albedo);

        public Builder Box(Vector3 min, Vector3 max, Vector3 albedo)
        {
            Vector3 P(int x, int y, int z) => new(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z);
            Quad(P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1), albedo);   // top
            Quad(P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0), albedo);   // bottom
            Quad(P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1), albedo);   // +x
            Quad(P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0), albedo);   // −x
            Quad(P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0), albedo);   // +y
            Quad(P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1), albedo);   // −y
            return this;
        }

        public L2ProbeGrid Bake(Vector3 min, Vector3 max, Vector3 toSun, int rays = 128) => ModernProbeBake.Bake(new ModernProbeBakeInput
        {
            Positions = [.. Positions], Indices = [.. Indices], Albedo = [.. Albedo], Min = min, Max = max,
            ToSun = Vector3.Normalize(toSun), Rays = rays, Threads = 2, Spacing = 1f,
        });
    }

    /// <summary>A street 6 m wide running along y between two walls 2 m thick and 8 m high; the sun 45° up over +x lights the west wall's face (x = −3) and leaves the east one's (x = 3) in shade.</summary>
    internal static Builder Street(float westAlbedo) => new Builder()
        .Ground(20f, new Vector3(0.3f))
        .Box(new(-5, -12, 0), new(-3, 12, 8), new Vector3(westAlbedo))
        .Box(new(3, -12, 0), new(5, 12, 8), new Vector3(0.5f));

    internal static readonly Vector3 StreetSun = Vector3.Normalize(new Vector3(1, 0, 1));

    static float SkyUp(L2ProbeGrid g, int x, int y, int z)
    {
        Span<float> b = stackalloc float[9];
        ModernAmbient.Basis(Vector3.UnitZ, b);
        int p = g.Index(x, y, z);
        float v = 0;
        for (int k = 0; k < 9; k++) v += g.Visibility[p * 9 + k] * b[k];
        return v;
    }

    [Fact]
    public void The_ray_tree_finds_the_nearest_face_and_which_side_was_hit()
    {
        var b = new Builder().Box(new(-1, -1, -1), new(1, 1, 1), Vector3.One).Box(new(4, -1, -1), new(6, 1, 1), Vector3.One);
        var bvh = new ModernBvh([.. b.Positions], [.. b.Indices]);
        var hit = bvh.Cast(new Vector3(-5, 0, 0), Vector3.UnitX, 100f);
        Assert.NotNull(hit);
        Assert.InRange(hit!.Value.Distance, 3.99f, 4.01f);
        Assert.False(hit.Value.Back);
        var inside = bvh.Cast(Vector3.Zero, Vector3.UnitX, 100f);
        Assert.True(inside!.Value.Back);
        Assert.InRange(inside.Value.Distance, 0.99f, 1.01f);
        Assert.False(bvh.Occluded(new Vector3(0, 0, 2), Vector3.UnitZ, 100f));
        Assert.True(bvh.Occluded(new Vector3(2.5f, 0, 0), Vector3.UnitX, 100f));
        Assert.False(bvh.Occluded(new Vector3(2.5f, 0, 0), Vector3.UnitX, 1f));
    }

    [Fact]
    public void A_probe_in_the_open_sees_the_sky_and_one_under_a_roof_does_not()
    {
        // A roof 8 m square, 0.3 m thick, on nothing, 3 m up; the probes 1 m apart from (−10, −10, 0).
        var b = new Builder().Ground(20f, new Vector3(0.3f)).Box(new(-4, -4, 3), new(4, 4, 3.3f), new Vector3(0.4f));
        var g = b.Bake(new Vector3(-10, -10, 0.5f), new Vector3(10, 10, 6), StreetSun);
        int open = 2, under = 10;   // x = −8 m and x = 0 m
        Assert.Equal(1, g.State[g.Index(open, 10, 1)]);
        Assert.InRange(SkyUp(g, open, 10, 1), 0.8f, 1.4f);
        Assert.InRange(SkyUp(g, under, 10, 1), -0.2f, 0.25f);
        // Its total sky (the first coefficient against 2√π for the whole sphere) is what the eaves leave: some, far less than half.
        float share = g.Visibility[g.Index(under, 10, 1) * 9] / (2f * MathF.Sqrt(MathF.PI));
        Assert.InRange(share, 0.01f, 0.3f);
        float openShare = g.Visibility[g.Index(open, 10, 1) * 9] / (2f * MathF.Sqrt(MathF.PI));
        Assert.InRange(openShare, 0.45f, 0.55f);   // the upper half: the ground hides the lower
    }

    [Fact]
    public void A_probe_inside_a_wall_is_filled_from_the_open_beside_it()
    {
        var b = Street(0.5f);
        var g = b.Bake(new Vector3(-6, -3, 0.5f), new Vector3(6, 3, 10), StreetSun);
        // x = −4 m is inside the west wall, a metre from either face: no move of 0.45 m gets it out.
        int inside = g.Index(2, 3, 3);
        int street = g.Index(6, 3, 3);
        Assert.Equal(-4f, g.Position(2, 3, 3).X);
        Assert.Equal(0f, g.Position(6, 3, 3).X);
        Assert.Equal(0, g.State[inside]);
        Assert.NotEqual(0, g.State[street]);
        Assert.True(g.Visibility[inside * 9] > 0.1f, "a filled probe has its neighbours' sky, not nothing");
    }

    [Fact]
    public void The_shaded_wall_of_a_street_is_lit_by_the_sunlit_wall_across_from_it()
    {
        Vector3 Shade(float westAlbedo)
        {
            var g = Street(westAlbedo).Bake(new Vector3(-6, -3, 0.5f), new Vector3(6, 3, 10), StreetSun);
            var probes = new ModernProbes(g);
            var none = new Vector3[9];
            var light = probes.Compute(none, none, new Vector3(6f), StreetSun, Vector3.Zero, 1f);
            // Half a metre in front of the east wall's shaded face, facing west, 5 m up.
            return probes.IrradianceAt(light, new Vector3(2.5f, 0, 5f), -Vector3.UnitX);
        }
        var dark = Shade(0.02f);
        var lit = Shade(0.7f);
        Assert.True(lit.X > dark.X * 3f + 0.2f, $"the bounce from a bright sunlit wall: {lit.X:0.000} against {dark.X:0.000} from a black one");
        // About what the sunlit wall gives: its radiance 0.7 · 6 · cos 45° / π ≈ 0.95 over a good part of the half the wall's face sees.
        Assert.InRange(lit.X, 0.4f, 3f);
    }

    [Fact]
    public void The_sky_part_follows_the_sky_and_another_sun_keeps_a_rougher_bounce()
    {
        var g = Street(0.7f).Bake(new Vector3(-6, -3, 0.5f), new Vector3(6, 3, 10), StreetSun);
        var probes = new ModernProbes(g);
        var blue = ModernAmbient.Irradiance(d => d.Z > 0 ? new Vector3(0.2f, 0.3f, 0.6f) : Vector3.Zero);
        var grey = ModernAmbient.Irradiance(d => d.Z > 0 ? new Vector3(0.4f) : Vector3.Zero);
        var none = new Vector3[9];
        var at = new Vector3(0, 0, 1.5f);
        var underBlue = probes.IrradianceAt(probes.Compute(blue, none, Vector3.Zero, StreetSun, Vector3.Zero, 1f), at, Vector3.UnitZ);
        var underGrey = probes.IrradianceAt(probes.Compute(grey, none, Vector3.Zero, StreetSun, Vector3.Zero, 1f), at, Vector3.UnitZ);
        Assert.True(underBlue.Z > underBlue.X * 2f, "a blue sky lights the street blue");
        Assert.InRange(underGrey.X / underGrey.Z, 0.97f, 1.03f);
        // The street's floor sees the sky only between its walls: less than the open ground's π · 0.4.
        Assert.InRange(underGrey.X, 0.2f, MathF.PI * 0.4f * 0.8f);

        Assert.Equal(1f, probes.SunShare(StreetSun));
        Assert.Equal(0f, probes.SunShare(Vector3.Normalize(new Vector3(-1, 0, 1))));
        // The sun from the west instead: the baked bounce is gone, the old model's surroundings stand in for it.
        var bounce = ModernAmbient.Irradiance(_ => new Vector3(0.3f));
        var west = probes.Compute(none, bounce, new Vector3(6f), Vector3.Normalize(new Vector3(-1, 0, 1)), Vector3.Zero, 1f);
        var e = probes.IrradianceAt(west, new Vector3(2.5f, 0, 5f), -Vector3.UnitX);
        Assert.InRange(e.X, 0.1f, 1.5f);
    }

    [Fact]
    public void The_probe_chunk_keeps_the_grid_through_writing_and_reading()
    {
        var g = Street(0.5f).Bake(new Vector3(-6, -3, 0.5f), new Vector3(6, 3, 6), StreetSun, rays: 64);
        var chunk = g.Encode();
        var cell = L2Cell.Read(new L2Cell { Chunks = [chunk] }.Write());
        var back = L2ProbeGrid.Decode(cell.Find(L2ProbeGrid.Tag)!);
        Assert.Equal((g.Nx, g.Ny, g.Nz, g.Spacing, g.Origin, g.ToSun, g.Rays), (back.Nx, back.Ny, back.Nz, back.Spacing, back.Origin, back.ToSun, back.Rays));
        Assert.Equal(g.State, back.State);
        for (int i = 0; i < g.Visibility.Length; i++) Assert.InRange(back.Visibility[i] - g.Visibility[i], -0.01f, 0.01f);
        for (int i = 0; i < g.SunBounce.Length; i++) Assert.InRange(back.SunBounce[i] - g.SunBounce[i], -0.01f, 0.01f);
        for (int i = 0; i < g.SkyBounce.Length; i++) Assert.InRange(back.SkyBounce[i] - g.SkyBounce[i], -0.01f, 0.01f);
        // A reader that does not know the chunk passes it by: a cell's other chunks read as before.
        var mixed = L2Cell.Read(new L2Cell { Chunks = [new L2Chunk("XTRA", 1, [1, 2, 3]), chunk] }.Write());
        Assert.Equal(2, mixed.Chunks.Count);
        Assert.Throws<NodFormatException>(() => L2ProbeGrid.Decode(chunk with { Version = 2 }));
    }

    [Fact]
    public void The_bake_is_the_same_every_time()
    {
        var a = Street(0.5f).Bake(new Vector3(-6, -3, 0.5f), new Vector3(6, 3, 6), StreetSun, rays: 64);
        var b = Street(0.5f).Bake(new Vector3(-6, -3, 0.5f), new Vector3(6, 3, 6), StreetSun, rays: 64);
        Assert.Equal(a.Visibility, b.Visibility);
        Assert.Equal(a.SunBounce, b.SunBounce);
        Assert.Equal(a.SkyBounce, b.SkyBounce);
    }
}
