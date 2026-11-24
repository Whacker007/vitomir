// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Scene;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The characters' shadows**: what the shadow draw draws for a
/// thing's render flags under each shadow mode and at each distance, the shade a light gives and the
/// budget the sun's shadow takes what is left of, where a point light is moved to, the light
/// The thing draw makes up for a thing with none and its loop over a thing's lights, the surface search
/// behind a cast shadow, the rectangle a rough shadow lays on the floor, the
/// Advanced screen's shadow bars, the silhouette picture's rows — and that Christof, in a
/// monastery corridor, casts his own silhouette onto its floor. All of it without a GPU.
/// </summary>
public class ShadowTests
{
    const uint Both = Shadows.DropShadow | Shadows.FullShadow;

    static WorldThing Someone(Vector3 at, uint flags = Both, float radius = 30f, float height = 100f) => new()
    {
        Index = 0, Guid = 1, Scene = NsdThing.Placed("test", Vector3.Zero, Vector3.Zero, 0), Template = "test",
        Type = ThingType.Actor, Collide = CollideShape.Cylinder, Radius = radius, Height = height,
        RenderFlags = flags, Position = at, Sector = 0,
    };

    static PickedLight Point(Vector3 at, float range) =>
        new(0, new Vector3(255f), at, Vector3.Zero, 300f, 1f, range, 0f, 0, "test");

    static PickedLight Sun(Vector3 direction) =>
        new(2, new Vector3(255f), Vector3.Zero, Vector3.Normalize(direction), 300f, 1f, 0f, 0f, 0, "sun");

    /// <summary>
    /// One sector holding one floor, a square <paramref name="half"/> either side of the origin at
    /// z = 0, facing up and wound counter-clockwise seen from above (as the level's floors are), and
    /// optionally a ceiling over it at <paramref name="ceiling"/>, facing down.
    /// </summary>
    static NilLevel Room(float half = 500f, float? ceiling = null)
    {
        var verts = new List<Vector3>
        {
            new(-half, -half, 0), new(half, -half, 0), new(half, half, 0), new(-half, half, 0),
        };
        var planes = new List<NilPlane> { new(Vector3.UnitZ, 0f) };
        var surfaces = new List<NilSurface> { new(0, 4, -1, 0, 0) };
        if (ceiling is float c)
        {
            verts.AddRange([new(-half, -half, c), new(-half, half, c), new(half, half, c), new(half, -half, c)]);
            planes.Add(new(-Vector3.UnitZ, -c));
            surfaces.Add(new(1, 4, -1, 4, 0));
        }
        var sector = new NilSector
        {
            Vertices = [.. verts], SurfacePlanes = [.. planes], Surfaces = [.. surfaces],
            SectorVertices = [.. Enumerable.Range(0, verts.Count).Select(i => new NilSectorVertex((ushort)i, 0, 0, Vector2.Zero, Vector2.Zero))],
            Groups = [new NilGroup(surfaces.Count, 0, 0, verts.Count, [], 0, 0)],
            // A collision tree, as every real floor's sector has: the floor's plane, open above and
            // solid below — the blob's query counts only planes its walk down the tree marks.
            BspNodes = [0, NilBspNode.Internal, 1, 2, 0, NilBspNode.LeafBit, 0, 0, 0, NilBspNode.LeafBit | NilBspNode.SolidBit, 0, 0],
        };
        return new NilLevel { Sectors = [sector] };
    }

    // ---- which shadow (the shadow draw's start) -------------------------------------------------------

    [Fact]
    public void A_full_shadow_thing_draws_its_own_silhouette_in_mode_3_and_the_stock_one_in_mode_2()
    {
        // FULL SHADOW asks for 5 and DROP SHADOW for 2; the mode then decides.
        Assert.Equal(ShadowKind.None, Shadows.Kind(Both, 0));
        Assert.Equal(ShadowKind.None, Shadows.Kind(0, 3));
        Assert.Equal(ShadowKind.Blob, Shadows.Kind(Shadows.FullShadow, 1));
        Assert.Equal(ShadowKind.Blob, Shadows.Kind(Shadows.DropShadow, 1));
        Assert.Equal(ShadowKind.Rough, Shadows.Kind(Shadows.FullShadow, 2));
        Assert.Equal(ShadowKind.Full, Shadows.Kind(Shadows.FullShadow, 3));
        Assert.Equal(ShadowKind.Full, Shadows.Kind(Both, 3));
        Assert.Equal(ShadowKind.Rough, Shadows.Kind(Shadows.DropShadow, 3));
        Assert.Equal(ShadowKind.FullHardware, Shadows.Kind(Both, 4));
        Assert.Equal(ShadowKind.Rough, Shadows.Kind(Shadows.DropShadow, 4));
        // 5 and anything past it leave a full shadow as asked — and the shadow draw draws nothing for it.
        Assert.Equal(ShadowKind.FullPoly, Shadows.Kind(Both, 5));
        Assert.Equal(ShadowKind.FullPoly, Shadows.Kind(Both, 7));
    }

    [Fact]
    public void Past_the_full_distance_a_full_shadow_is_drawn_rough_and_past_the_farthest_not_at_all()
    {
        var high = ShadowSettings.From(DetailPresets.Named[3]);    // index 7: full 2,000, farthest 4,000
        Assert.Equal(ShadowKind.Full, Shadows.AtDistance(ShadowKind.Full, 2000f, high));
        Assert.Equal(ShadowKind.Rough, Shadows.AtDistance(ShadowKind.Full, 2001f, high));
        Assert.Equal(ShadowKind.Rough, Shadows.AtDistance(ShadowKind.FullPoly, 2001f, high));
        Assert.Equal(ShadowKind.Blob, Shadows.AtDistance(ShadowKind.Blob, 2001f, high));
        Assert.Equal(ShadowKind.None, Shadows.AtDistance(ShadowKind.Rough, 4001f, high));
    }

    // ---- the shade and the budget --------------------------------------------------------------

    [Fact]
    public void A_point_light_gives_full_shade_at_the_light_and_none_at_its_range()
    {
        var thing = Someone(Vector3.Zero, radius: 30f);
        var torch = Point(new Vector3(0, 0, 200), 400f);
        // 255 × (1 − (d − radius) / range), the fraction held to 0..1.
        Assert.Equal(255f, Shadows.Shade(thing, torch, 20f, 0f));
        Assert.Equal(255f * (1f - 170f / 400f), Shadows.Shade(thing, torch, 200f, 0f), 3);
        Assert.Equal(0f, Shadows.Shade(thing, torch, 500f, 0f));
        // A point light's shade never looks at the budget.
        Assert.Equal(255f * (1f - 170f / 400f), Shadows.Shade(thing, torch, 200f, 140f), 3);
    }

    [Fact]
    public void The_sun_shadow_gets_what_is_left_of_the_budget_of_150()
    {
        var thing = Someone(Vector3.Zero);
        var sun = Sun(new Vector3(1, 0, -1));
        Assert.Equal(150f, Shadows.Shade(thing, sun, 32f, 0f));
        Assert.Equal(40f, Shadows.Shade(thing, sun, 32f, 110f));
        Assert.True(Shadows.Shade(thing, sun, 32f, 151f) < Shadows.Faintest);
    }

    [Fact]
    public void A_light_below_the_head_is_raised_to_the_top_of_the_thing_and_the_sun_stands_32_up_its_beam()
    {
        var thing = Someone(new Vector3(10, 20, 30), height: 100f);
        Assert.Equal(new Vector3(0, 0, 130), Shadows.From(thing, Point(new Vector3(0, 0, 40), 300f)));
        Assert.Equal(new Vector3(0, 0, 500), Shadows.From(thing, Point(new Vector3(0, 0, 500), 300f)));
        // Not a cylinder: its radius is what it counts as tall.
        var sphere = new WorldThing
        {
            Index = 0, Guid = 2, Scene = NsdThing.Placed("ball", Vector3.Zero, Vector3.Zero, 0), Template = "ball", Collide = CollideShape.Sphere, Radius = 16f,
            Height = 100f, Position = Vector3.Zero,
        };
        Assert.Equal(new Vector3(0, 0, 16), Shadows.From(sphere, Point(new Vector3(0, 0, 5), 300f)));
        var sun = Sun(new Vector3(0, 0, -1));
        Assert.Equal(new Vector3(10, 20, 62), Shadows.From(thing, sun));
    }

    [Fact]
    public void The_light_made_up_for_a_thing_with_none_is_white_range_350_at_minus_50_plus_50_plus_100()
    {
        var l = Shadows.Invented(new Vector3(100, 200, 300));
        Assert.Equal((0u, new Vector3(50, 250, 400), 350f, 400f), (l.Type, l.Position, l.Range, l.Set));
    }

    // ---- the loop over a thing's lights ---------------------------------------------

    [Fact]
    public void A_thing_with_no_lights_casts_once_from_the_made_up_light()
    {
        var shadows = new Shadows { Settings = new ShadowSettings(2, 0f, 3f, 4000f, 2000f, 1) };
        var thing = Someone(Vector3.Zero, Shadows.DropShadow);
        shadows.ThingShadows(Room(), thing, [], new Vector3(0, -300, 200));
        Assert.Equal(1, shadows.Cast);
        Assert.NotEmpty(shadows.Polys);
        // From (−50, +50, +100) the shadow falls away to +x, −y: its far end is out that way.
        var far = shadows.Polys.SelectMany(p => p.Points).OrderBy(p => p.X - p.Y).Last();
        Assert.True(far.X > 0f && far.Y < 0f);
    }

    [Fact]
    public void A_thing_casts_from_its_lights_in_turn_until_maxThingShadows_are_done_and_a_faint_one_is_passed_over()
    {
        var room = Room();
        var thing = Someone(Vector3.Zero, Shadows.DropShadow);
        var eye = new Vector3(0, -300, 200);
        var faint = Point(new Vector3(0, 390, 50), 300f);      // 390 away, range 300: shade 0
        var near = Point(new Vector3(100, 0, 150), 400f);
        var other = Point(new Vector3(-100, 0, 150), 400f);
        var one = new Shadows { Settings = new ShadowSettings(2, 0f, 3f, 4000f, 2000f, 1) };
        one.ThingShadows(room, thing, [faint, near, other], eye);
        Assert.Equal(1, one.Cast);                              // the faint one was passed over; `near` did it
        var two = new Shadows { Settings = one.Settings with { PerThing = 2 } };
        two.ThingShadows(room, thing, [faint, near, other], eye);
        Assert.Equal(2, two.Cast);
        // No shadows at all with maxThingShadows 0 (the thing draw skips the whole loop).
        var none = new Shadows { Settings = one.Settings with { PerThing = 0 } };
        none.ThingShadows(room, thing, [near], eye);
        Assert.Equal(0, none.Cast);
    }

    [Fact]
    public void Each_cast_shadow_spends_its_shade_so_the_sun_after_a_torch_gets_less()
    {
        var room = Room();
        var thing = Someone(Vector3.Zero, Shadows.DropShadow);
        var torch = Point(new Vector3(300, 0, 150), 400f);                // shade 60: the sun gets 90
        var shadows = new Shadows { Settings = new ShadowSettings(2, 0f, 3f, 4000f, 2000f, 2) };
        shadows.ThingShadows(room, thing, [torch, Sun(new Vector3(1, 1, -2))], new Vector3(0, -300, 200));
        Assert.Equal(2, shadows.Cast);
        float torchShade = Shadows.Shade(thing, torch, Vector3.Distance(Vector3.Zero, Shadows.From(thing, torch)), 0f);
        Assert.Equal(torchShade, shadows.Spent - (Shadows.Budget - torchShade), 2);
        float sunMax = shadows.Polys.Where(p => p.Shade.Max() <= Shadows.Budget - torchShade + 0.01f).SelectMany(p => p.Shade).Max();
        Assert.Equal(Shadows.Budget - torchShade, sunMax, 2);
    }

    [Fact]
    public void A_thing_in_a_sector_not_drawn_this_frame_casts_nothing_but_uses_its_turn()
    {
        var thing = Someone(Vector3.Zero, Shadows.DropShadow);
        var shadows = new Shadows { Settings = new ShadowSettings(2, 0f, 3f, 4000f, 2000f, 1), SectorDrawn = _ => false };
        Assert.True(shadows.One(Room(), thing, Point(new Vector3(100, 0, 150), 400f), new Vector3(0, -300, 200)));
        Assert.Equal((0, 0), (shadows.Cast, shadows.Polys.Count));
    }

    // ---- the surface search ----------------------------------------------

    [Fact]
    public void The_search_finds_the_floor_a_beam_falls_on_but_not_a_ceiling_it_rises_from()
    {
        var room = Room(500f, ceiling: 300f);
        var hits = new List<ShadowHit>();
        // Going down: the floor faces against the beam, the ceiling with it.
        Assert.Equal(1, Shadows.FindSurfaces(room, 0, new Vector3(0, 0, 4), Vector3.Normalize(new Vector3(1, 0, -1)), 400f, hits));
        Assert.Equal((0, new Vector3(0, 0, 0), 4f), (hits[0].Surface, hits[0].Foot, hits[0].Distance));
        hits.Clear();
        // Going up (a light under the floor): only the ceiling, and only while it is within reach.
        Assert.Equal(1, Shadows.FindSurfaces(room, 0, new Vector3(0, 0, 4), Vector3.UnitZ, 400f, hits));
        Assert.Equal(1, hits[0].Surface);
        hits.Clear();
        Assert.Equal(0, Shadows.FindSurfaces(room, 0, new Vector3(0, 0, 4), Vector3.UnitZ, 200f, hits));
    }

    [Fact]
    public void A_floor_counts_while_the_point_is_within_the_radius_outside_an_edge()
    {
        var room = Room(100f);
        var down = -Vector3.UnitZ;
        var hits = new List<ShadowHit>();
        Assert.Equal(1, Shadows.FindSurfaces(room, 0, new Vector3(150, 0, 4), down, 60f, hits));
        Assert.Equal(0, Shadows.FindSurfaces(room, 0, new Vector3(170, 0, 4), down, 60f, hits));
        // And the point must be in front of the plane by no more than the radius.
        Assert.Equal(0, Shadows.FindSurfaces(room, 0, new Vector3(0, 0, 70), down, 60f, hits));
        Assert.Equal(0, Shadows.FindSurfaces(room, 0, new Vector3(0, 0, -1), down, 60f, hits));
    }

    // ---- what is laid on the floor -------------------------------------------------------------

    [Fact]
    public void A_rough_shadow_lies_on_the_floor_from_the_feet_away_from_the_light_darkest_at_the_feet()
    {
        var thing = Someone(Vector3.Zero, Shadows.DropShadow, radius: 30f, height: 100f);
        var torch = Point(new Vector3(-200, 0, 150), 600f);
        var shadows = new Shadows { Settings = new ShadowSettings(2, 0f, 3f, 4000f, 2000f, 1) };
        Assert.True(shadows.One(Room(), thing, torch, new Vector3(0, -300, 200)));
        var pts = shadows.Polys.SelectMany(p => p.Points.Zip(p.Shade)).ToList();
        Assert.NotEmpty(pts);
        Assert.All(pts, p => Assert.Equal(Shadows.Hover, p.First.Z, 3));                      // 0.3 off the floor
        Assert.True(pts.Max(p => p.First.X) > 100f);                                          // it runs away from the light
        float atFeet = pts.OrderBy(p => MathF.Abs(p.First.X)).First().Second;
        float atEnd = pts.OrderBy(p => p.First.X).Last().Second;
        Assert.True(atFeet > atEnd);
        Assert.Equal(ShadowTexture.Silhouette, shadows.Polys[0].Texture);
        // Its width is 2.5 radii across the beam.
        Assert.Equal(75f, pts.Max(p => p.First.Y) - pts.Min(p => p.First.Y), 1);
    }

    [Fact]
    public void A_shadow_that_finds_no_surface_spends_nothing()
    {
        var thing = Someone(new Vector3(5000, 0, 0), Shadows.DropShadow);    // far off the floor's edge
        var shadows = new Shadows { Settings = new ShadowSettings(2, 0f, 3f, 99999f, 99999f, 1) };
        Assert.True(shadows.One(Room(100f), thing, Point(new Vector3(5000, 0, 200), 400f), Vector3.Zero));
        Assert.Equal((0, 0f), (shadows.Polys.Count, shadows.Spent));
    }

    [Fact]
    public void The_blob_is_round_under_the_thing_at_full_white_whatever_the_light()
    {
        var thing = Someone(new Vector3(0, 0, 0), Shadows.FullShadow, radius: 40f);
        var shadows = new Shadows { Settings = new ShadowSettings(1, 0f, 2f, 1000f, 500f, 1) };
        Assert.True(shadows.One(Room(), thing, Point(new Vector3(-300, 0, 20), 50f), Vector3.Zero));
        var poly = Assert.Single(shadows.Polys);
        Assert.Equal(ShadowTexture.Blob, poly.Texture);
        Assert.All(poly.Shade, s => Assert.Equal(255f, s));
        // A square the width of the circle a 50-radius sphere 4 above the floor cuts in it.
        float half = MathF.Sqrt(50f * 50f - 16f);
        Assert.Equal(2f * half, poly.Points.Max(p => p.X) - poly.Points.Min(p => p.X), 2);
        Assert.Equal(0f, shadows.Spent);
    }

    [Fact]
    public void The_full_shadow_is_laid_with_its_own_picture_and_nothing_without_a_model()
    {
        var thing = Someone(Vector3.Zero, Both);
        var torch = Point(new Vector3(-200, 0, 150), 600f);
        var shadows = new Shadows { Settings = new ShadowSettings(3, 0.5f, 3f, 4000f, 2000f, 1) };
        Assert.True(shadows.One(Room(), thing, torch, new Vector3(0, -300, 200)));
        Assert.Empty(shadows.Polys);                                        // no model, no shadow
        var model = new Box();
        Assert.True(shadows.One(Room(), thing, torch, new Vector3(0, -300, 200), model));
        Assert.NotEmpty(shadows.Polys);
        Assert.All(shadows.Polys, p => Assert.Equal((ShadowTexture.Bitmap, 0), (p.Texture, p.Bitmap)));
        Assert.Equal(1, shadows.BitmapCount);
        // The picture is four radii across and one and a half heights tall, from a radius below the axis.
        Assert.Equal((120f, 150f, 30f), (model.Width, model.Tall, model.Radius));
    }

    /// <summary>A model that draws a square in the middle of the picture and remembers what it was asked.</summary>
    sealed class Box : IShadowModel
    {
        public float Width, Tall, Radius;
        public void Draw(byte[] into, Matrix4x4 light, float width, float tall, float radius)
        {
            (Width, Tall, Radius) = (width, tall, radius);
            Array.Clear(into);
            MeshSilhouette.Fill(into, new Vector2(32, 32), new Vector2(96, 32), new Vector2(96, 96));
        }
    }

    [Fact]
    public void The_picture_is_filled_upside_down_row_127_at_the_feet()
    {
        var bits = new byte[Shadows.BitmapSize * Shadows.BitmapSize];
        // A triangle low in the picture (z 0..10, the feet end) fills rows near the bottom.
        MeshSilhouette.Fill(bits, new Vector2(10, 0), new Vector2(50, 0), new Vector2(30, 10));
        int lit = bits.Count(b => b == 255);
        Assert.InRange(lit, 150, 250);
        int top = Array.FindIndex(bits, b => b != 0) / Shadows.BitmapSize;
        Assert.InRange(top, 117, 127);
        Assert.Equal(255, bits[127 * Shadows.BitmapSize + 30]);
        Assert.Equal(0, bits[0 * Shadows.BitmapSize + 30]);
    }

    [Fact]
    public void The_clip_carries_the_shade_and_interpolates_it_where_the_outline_crosses_an_edge()
    {
        // A surface from x 0 to 10, z 0 to 10 (wound as the decal test's), and a quad over its right edge.
        var poly = new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 10), new Vector3(10, 0, 10), new Vector3(10, 0, 0) };
        var pts = new Vector3[65]; var uvs = new Vector2[65]; var shade = new float[65];
        pts[0] = new(5, 0, 2); pts[1] = new(15, 0, 2); pts[2] = new(15, 0, 8); pts[3] = new(5, 0, 8);
        shade[0] = 0f; shade[1] = 200f; shade[2] = 200f; shade[3] = 0f;
        int n = Shadows.Clip(pts, uvs, shade, 4, poly);
        Assert.Equal(4, n);
        Assert.All(Enumerable.Range(0, n), i => Assert.InRange(pts[i].X, 5f, 10f));
        Assert.Contains(100f, shade[..n]);                                  // halfway along, at x 10
    }

    // ---- the options (the Advanced screen's bars) -------------------------

    [Fact]
    public void The_presets_and_the_Advanced_screen_bars_give_1_1_s_shadow_settings()
    {
        // 1.1's arithmetic, kept for the graphics page to come; Vitomir draws with the maxed-out
        // preset whatever these say (DetailPresetTests).
        Assert.Equal(new ShadowSettings(3, 0.5f, 3f, 4000f, 2000f, 1), ShadowSettings.From(DetailPresets.Of(3)));
        Assert.Equal(new ShadowSettings(1, 0.1f, 2f, 2000f, 1000f, 1), ShadowSettings.From(DetailPresets.Of(1)));   // low: index 2, blobs
        var veryLow = ShadowSettings.From(DetailPresets.Of(0));                                                    // very low: off, no thing shadows
        Assert.Equal((0, 0), (veryLow.Mode, veryLow.PerThing));
        // Custom, touched: the shadow-detail bar is the index the five are filled from.
        Assert.Equal(new ShadowSettings(2, 0.15f, 2f, 2000f, 1000f, 1), ShadowSettings.From(DetailPresets.WithShadow(DetailPresets.Of(4), 3)));
        // Index 10 sets the index alone (case 9 falls into the default).
        var normal = DetailPresets.Named[2];
        var ten = DetailPresets.WithShadow(normal, 10);
        Assert.Equal((10, normal.ShadowMode, normal.ShadowMaxDist), (ten.ShadowDetailIndex, ten.ShadowMode, ten.ShadowMaxDist));
        Assert.Equal(normal, DetailPresets.WithShadow(normal, 11));
        // What Vitomir draws: the index 9 and four a thing.
        Assert.Equal(new ShadowSettings(3, 1f, 4f, 6000f, 3000f, 4), ShadowSettings.Rendered);
    }

    // ---- in the game ---------------------------------------------------------------------------

    [Fact]
    public void Christof_casts_his_own_silhouette_on_the_monastery_floor()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var christof = place.Drawn.First(t => t is not null && t.Template.Equals("christof", StringComparison.OrdinalIgnoreCase));
        // The characters are both DROP SHADOW and FULL SHADOW.
        Assert.Equal(Both, christof.RenderFlags & Both);
        var shadows = new Shadows { Settings = ShadowSettings.From(DetailPresets.Named[3]) };
        shadows.Frame(place, place.Mesh, place.Drawn, christof.Position + new Vector3(200, 0, 150));
        // His shadow starts under his feet: the pieces over the spot he stands on.
        var feet = christof.Position;
        var his = shadows.Polys.Where(p => p.Points.Min(q => q.X) < feet.X && p.Points.Max(q => q.X) > feet.X
                                           && p.Points.Min(q => q.Y) < feet.Y && p.Points.Max(q => q.Y) > feet.Y).ToList();
        Assert.NotEmpty(his);
        Assert.Contains(his, p => p.Texture == ShadowTexture.Bitmap && shadows.Bitmaps[p.Bitmap].Count(b => b != 0) > 500);
        // On the floor he stands on, 0.3 above it.
        Assert.Contains(his, p => p.Points.All(q => MathF.Abs(q.Z - (christof.Position.Z + Shadows.Hover)) < 0.5f));
    }
}
