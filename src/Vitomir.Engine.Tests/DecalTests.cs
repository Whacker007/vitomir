// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Explosions;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Decals**: the wall marks explosions leave and the impact marks
/// projectiles leave — how a mark's square is laid on the surfaces round a point and clips
/// it to each, how the table fills (slot 0 taken back when full), how the second loop of
/// the light pass darkens a mark over its last two seconds and frees it, and that a grenade and a bullet
/// really do leave theirs in the monastery.
/// </summary>
public class DecalTests
{
    static Location Monastery(Vitomir.Engine.Vfs.GameFiles files) =>
        Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");

    /// <summary>A point in the monastery's lower corridor, 60 above its floor (z −252).</summary>
    static readonly Vector3 Corridor = new(-945.458f, -580.13f, -192f);
    /// <summary>The same, 8 above the floor: within reach of a small mark (a mark reaches as far as it is wide).</summary>
    static readonly Vector3 Low = new(-945.458f, -580.13f, -244f);

    /// <summary>
    /// **The clip**: the mark's polygon cut by each edge of the surface's on the X/Z
    /// plane, a point kept on the side where <c>(p − b) × (a − b) ≥ 0</c>, a point put on the edge
    /// where the outline crosses it with its texture coordinates interpolated. A square half over a
    /// surface keeps the half that is over it; a square wholly off it leaves nothing.
    /// </summary>
    [Fact]
    public void A_mark_is_clipped_to_its_surface_on_the_x_z_plane()
    {
        // A surface from x 0 to 10, z 0 to 10, wound the way the level's are about +Y.
        var poly = new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 10), new Vector3(10, 0, 10), new Vector3(10, 0, 0) };
        var pts = new Vector3[65];
        var uvs = new Vector2[65];
        (pts[0], pts[1], pts[2], pts[3]) = (new(5, 0, -5), new(-5, 0, -5), new(-5, 0, 5), new(5, 0, 5));
        (uvs[0], uvs[1], uvs[2], uvs[3]) = (new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        int n = Decals.Clip(pts, uvs, 4, poly);
        Assert.Equal(4, n);
        var kept = pts.Take(n).Select(p => (p.X, p.Z)).OrderBy(p => p.X).ThenBy(p => p.Z).ToArray();
        Assert.Equal([(0f, 0f), (0f, 5f), (5f, 0f), (5f, 5f)], kept);
        // The corner (0, 0) is half way along both edges of the texture.
        int corner = Array.FindIndex(pts, 0, n, p => p.X == 0f && p.Z == 0f);
        Assert.Equal(new Vector2(0.5f, 0.5f), uvs[corner]);

        (pts[0], pts[1], pts[2], pts[3]) = (new(25, 0, 15), new(15, 0, 15), new(15, 0, 25), new(25, 0, 25));
        Assert.Equal(0, Decals.Clip(pts, uvs, 4, poly));
    }

    /// <summary>
    /// **A grenade's mark on the floor** (with the explosion's Z axis, straight up): 60
    /// under a point, a 120-unit square centred under it, laid 0.2 above the floor and cut in two by
    /// the two floor surfaces it spans; texture coordinates across the whole square.
    /// </summary>
    [Fact]
    public void A_mark_straight_down_lies_square_on_the_floor_below()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var decals = new Decals(place, 500);
        int sector = place.Solid!.SectorAt(Corridor, -1);
        Assert.True(decals.Stamp(sector, Vector3.UnitZ, Corridor, 0f, 120f, 15000, "BulletMark1", 0));
        var live = decals.Live.ToList();
        Assert.Equal(2, live.Count);
        Assert.Equal(2, decals.Count);
        var all = live.SelectMany(d => d.Points).ToList();
        Assert.All(all, p => Assert.Equal(-251.8f, p.Z, 3));
        Assert.Equal(Corridor.X - 60f, all.Min(p => p.X), 2);
        Assert.Equal(Corridor.X + 60f, all.Max(p => p.X), 2);
        Assert.Equal(Corridor.Y - 60f, all.Min(p => p.Y), 2);
        Assert.Equal(Corridor.Y + 60f, all.Max(p => p.Y), 2);
        var uv = live.SelectMany(d => d.Uvs).ToList();
        Assert.Equal(0f, uv.Min(t => t.X), 3);
        Assert.Equal(1f, uv.Max(t => t.X), 3);
        Assert.Equal(0f, uv.Min(t => t.Y), 3);
        Assert.Equal(1f, uv.Max(t => t.Y), 3);
        Assert.All(live, d => Assert.Equal(new Vector4(255f), d.Color));
    }

    /// <summary>
    /// **The fade** (the light pass's second loop): a mark loses <c>seconds / (life × 0.001)</c> of its
    /// life a frame; until its last 2,000 ms its colour stays full, then red, green and blue fall to
    /// <c>left ms × 0.0005 × 255</c> — alpha never — and when nothing is left it is freed.
    /// </summary>
    [Fact]
    public void A_mark_darkens_over_its_last_two_seconds_and_goes()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var decals = new Decals(place, 10);
        int sector = place.Solid!.SectorAt(Corridor, -1);
        Assert.True(decals.Stamp(sector, Vector3.UnitZ, Low, 0f, 40f, 5000, "BulletMark1", 0));
        var mark = decals.Live.First();
        for (int i = 0; i < 29; i++) decals.Step(0.1f);                // 2.9 s: 2.1 s left
        Assert.Equal(new Vector4(255f), mark.Color);
        for (int i = 0; i < 11; i++) decals.Step(0.1f);                // 4.0 s: 1 s left
        Assert.Equal(1000f * 0.0005f * 255f, mark.Color.X, 1);
        Assert.Equal(mark.Color.X, mark.Color.Z);
        Assert.Equal(255f, mark.Color.W);
        for (int i = 0; i < 11; i++) decals.Step(0.1f);                // past 5 s
        Assert.False(mark.InUse);
        Assert.Empty(decals.Live);
        Assert.Equal(0, decals.Count);
    }

    /// <summary>
    /// **A full table takes slot 0 back**: not the oldest, not the faintest — slot 0,
    /// unlinked first. With one slot, a second mark replaces the first; and every slot the table
    /// is short of means marks go, not that none are made. No slots at all is decals off (the very
    /// low preset): nothing is stamped.
    /// </summary>
    [Fact]
    public void A_full_table_gives_slot_zero_to_the_next_mark()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        int sector = place.Solid!.SectorAt(Corridor, -1);
        var one = new Decals(place, 1);
        one.Stamp(sector, Vector3.UnitZ, Low, 0f, 20f, 5000, "BulletMark1", 0);
        var first = one.Live.Single().Points;
        one.Stamp(sector, Vector3.UnitZ, Low + new Vector3(30f, 0f, 0f), 0f, 20f, 5000, "ExplosionMark1", 10);
        var only = one.Live.Single();
        Assert.Equal("ExplosionMark1", only.Material);
        Assert.NotEqual(first[0], only.Points[0]);
        Assert.Equal(2, one.Count);                                    // counted again, as 1.1 does

        var none = new Decals(place, 0);
        Assert.False(none.Stamp(sector, Vector3.UnitZ, Low, 0f, 20f, 5000, "BulletMark1", 0));
        // Nor is anything where no surface is within the mark's size (60 up, a 20-unit mark).
        Assert.False(new Decals(place, 5).Stamp(sector, Vector3.UnitZ, Corridor, 0f, 20f, 5000, "BulletMark1", 0));
    }

    /// <summary>
    /// **A grenade leaves its mark** (`grenadeEXP.nex`: WALLMARK 120 15000 at 50 ms, the template's
    /// `WALLMARK` `BulletMark1`): 50 ms in there is none, from then there is, for 15 seconds.
    /// </summary>
    [Fact]
    public void A_grenade_stamps_its_wall_mark_at_fifty_milliseconds()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var sim = new Simulation(place);
        while (sim.Milliseconds < 500) sim.Step(0.01f);
        int start = sim.Milliseconds;
        place.Place("exp_grenade", Corridor, Vector3.Zero, place.Solid!.SectorAt(Corridor, -1));
        while (sim.Milliseconds < start + 40) sim.Step(0.01f);
        Assert.Empty(place.Decals.Live);
        while (sim.Milliseconds < start + 70) sim.Step(0.01f);
        var marks = place.Decals.Live.ToList();
        Assert.NotEmpty(marks);
        Assert.All(marks, d => Assert.Equal("BulletMark1", d.Material));
        Assert.All(marks, d => Assert.Equal(15000, d.LifeMs));
    }

    /// <summary>
    /// **A bullet's impact mark**: the hitscan hands the wall to
    /// the shot, which stamps its `WALLMARK` (`BulletMark1`, the template's tag 244 on a projectile)
    /// along the wall's normal at twice its radius (20) — a mark 40 across, 0.2 in front of the wall
    /// — and, being PROJ_EXPLODE_ONWALL, bursts into its `exp_smokepuff` where it struck.
    /// </summary>
    [Fact]
    public void A_bullet_marks_the_wall_it_strikes_and_puffs_smoke_there()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var from = new Vector3(-945f, -600f, -200f);
        var reference = place.Player!.Thing;
        var probe = place.Place("proj_bullet", from, Vector3.Zero, reference.Sector)!;
        Assert.Equal("BulletMark1", place.Decals.ImpactMaterial(probe));
        place.Remove(probe, drop: false);
        Projectiles.Fire(place, reference, "proj_bullet", from, Vector3.Zero, 0);      // along +Y, down the corridor
        var puff = place.Things.Single(t => t.Template.Equals("exp_smokepuff", StringComparison.OrdinalIgnoreCase));
        var marks = place.Decals.Live.ToList();
        Assert.NotEmpty(marks);
        Assert.All(marks, d => Assert.Equal(20000, d.LifeMs));
        var all = marks.SelectMany(d => d.Points).ToList();
        // The wall it struck faces the shot (−Y): the mark lies 0.2 short of it, round where it struck.
        Assert.All(all, p => Assert.Equal(puff.Position.Y - 0.2f, p.Y, 2));
        Assert.True(all.Max(p => p.X) - all.Min(p => p.X) <= 40f * MathF.Sqrt(2f) + 0.01f);   // turned at random
        Assert.InRange(puff.Position.X, all.Min(p => p.X), all.Max(p => p.X));
    }
}
