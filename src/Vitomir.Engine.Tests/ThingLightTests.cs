// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>Lighting a thing that carries no baked colours of its own.</summary>
public class ThingLightTests
{
    static NilLight Point(Vector3 at, float intensity, float falloff = 1f, uint style = 0) => new(
        Flags: 0, InSector: true, Type: NilLight.Point, Style: style,
        Color: new Vector3(255, 255, 255), Intensity: intensity, Intensity2: intensity,
        Direction: Vector3.Zero, Position: at, CosUmbra: 0, CosPenumbra: 0,
        Range: intensity / falloff, Falloff: falloff, RawHead: []);

    static readonly LightStyles Steady = new();

    [Fact]
    public void A_point_light_fades_linearly_and_reaches_exactly_as_far_as_its_range()
    {
        var light = Point(Vector3.Zero, 400f);                    // falloff 1, so range 400
        Assert.Equal(400f, light.Range);
        Assert.Equal(400f, ThingLight.Strength(light, Vector3.Zero, 0f, 1f), 3);
        Assert.Equal(300f, ThingLight.Strength(light, new Vector3(100, 0, 0), 0f, 1f), 3);
        Assert.Equal(0f, ThingLight.Strength(light, new Vector3(400, 0, 0), 0f, 1f), 3);
        Assert.True(ThingLight.Strength(light, new Vector3(500, 0, 0), 0f, 1f) < 0);
    }

    [Fact]
    public void A_things_radius_moves_the_fade_out_to_its_surface()
    {
        var light = Point(Vector3.Zero, 400f);
        Assert.Equal(350f, ThingLight.Strength(light, new Vector3(100, 0, 0), radius: 50f, 1f), 3);
    }

    [Fact]
    public void A_light_on_an_animated_style_dims_with_it()
    {
        var light = Point(Vector3.Zero, 400f, style: 7);
        Assert.Equal(400f, ThingLight.Strength(light, Vector3.Zero, 0f, styleValue: 1f), 3);
        Assert.Equal(200f, ThingLight.Strength(light, Vector3.Zero, 0f, styleValue: 0.5f), 3);
        // A style switched off is a light switched off, exactly as it is for the lightmap it drives.
        Assert.Equal(0f, ThingLight.Strength(light, Vector3.Zero, 0f, styleValue: 0f), 3);
    }

    [Fact]
    public void The_sun_reaches_everywhere_at_full_strength()
    {
        var sun = Point(Vector3.Zero, 250f) with { Type = 2, Direction = new Vector3(0, 0, -1) };
        Assert.Equal(250f, ThingLight.Strength(sun, new Vector3(9999, 9999, 9999), 0f, 1f), 3);
    }

    [Fact]
    public void A_spot_stops_at_its_cone_and_at_its_range()
    {
        var spot = Point(Vector3.Zero, 400f) with
        {
            Type = NilLight.Spot,
            Direction = new Vector3(1, 0, 0),
            CosPenumbra = 0.5f,                                   // a 60-degree half-angle
        };
        // Straight down the beam: the full linear fade.
        Assert.True(ThingLight.Strength(spot, new Vector3(100, 0, 0), 0f, 1f) > 0);
        // Out to the side, past the cone.
        Assert.Equal(0f, ThingLight.Strength(spot, new Vector3(0, 100, 0), 0f, 1f), 3);
        // Down the beam but past the range.
        Assert.Equal(0f, ThingLight.Strength(spot, new Vector3(900, 0, 0), 0f, 1f), 3);
    }

    [Fact]
    public void At_most_eight_lights_are_kept_and_they_are_the_strongest()
    {
        var lights = Enumerable.Range(1, 20).Select(i => Point(new Vector3(i * 10, 0, 0), 400f)).ToArray();
        var sector = new NilSector { Lights = lights };
        var chosen = ThingLight.Choose(sector, Vector3.Zero, 0f, Steady);

        Assert.Equal(ThingLight.MaxLights, chosen.Length);
        Assert.Equal(8, chosen.Length);
        // Strongest first, and the strongest are the nearest.
        Assert.Equal(chosen.Select(c => c.Strength).OrderByDescending(x => x), chosen.Select(c => c.Strength));
        Assert.Equal(new Vector3(10, 0, 0), chosen[0].Light.Position);
    }

    [Fact]
    public void A_light_that_does_not_reach_is_not_chosen()
    {
        var sector = new NilSector { Lights = [Point(new Vector3(1000, 0, 0), 100f)] };
        Assert.Empty(ThingLight.Choose(sector, Vector3.Zero, 0f, Steady));
    }

    [Fact]
    public void The_engine_invents_a_light_rather_than_leave_a_thing_black()
    {
        // The original's thing draw builds this on the stack when a thing has no lights at all.
        var at = new Vector3(100, 200, 300);
        var fallback = ThingLight.Fallback(at);
        Assert.Equal(NilLight.Point, fallback.Type);
        Assert.Equal(new Vector3(255, 255, 255), fallback.Color);
        Assert.Equal(400f, fallback.Intensity2);
        Assert.Equal(350f, fallback.Range);
        Assert.Equal(1f, fallback.Falloff);
        Assert.Equal(at + new Vector3(-50, 50, 100), fallback.Position);
    }

    [Fact]
    public void Ambient_is_colour_times_intensity_on_the_same_scale_as_a_light()
    {
        // Prague by day stores (51000, 46000, 34000), which is (255, 230, 170) at intensity 200.
        var day = ThingLight.Ambient(new Vector3(51000, 46000, 34000));
        Assert.Equal(255f / 255f * (200f / 255f), day.X, 3);
        Assert.Equal(230f / 255f * (200f / 255f), day.Y, 3);
        // East London at night is far dimmer, and blue.
        var night = ThingLight.Ambient(new Vector3(4200, 4650, 7650));
        Assert.True(night.X < 0.1f);
        Assert.True(night.Z > night.X);
    }

    [Fact]
    public void A_surface_square_to_a_light_takes_its_colour_and_one_turned_away_takes_none()
    {
        var light = Point(new Vector3(0, 0, 100), 255f);           // strength 255 at its own position
        var chosen = new[] { new ChosenLight(light, ThingLight.Strength(light, Vector3.Zero, 0f, 1f)) };

        var facing = ThingLight.Shade(chosen, Vector3.Zero, new Vector3(0, 0, 1), Vector3.Zero);
        var away = ThingLight.Shade(chosen, Vector3.Zero, new Vector3(0, 0, -1), Vector3.Zero);
        Assert.True(facing.X > away.X);
        Assert.Equal(0f, away.X, 3);                               // the ambient is all a back face gets

        // …and the ambient is the floor under everything.
        var withAmbient = ThingLight.Shade(chosen, Vector3.Zero, new Vector3(0, 0, -1), new Vector3(0.2f, 0.2f, 0.2f));
        Assert.Equal(0.2f, withAmbient.X, 3);
    }

    [Fact]
    public void The_colour_a_vertex_carries_never_leaves_the_byte_range()
    {
        var bright = Enumerable.Range(0, 8)
            .Select(i => new ChosenLight(Point(Vector3.Zero, 4000f), 4000f)).ToArray();
        var colour = ThingLight.Shade(bright, Vector3.Zero, new Vector3(0, 0, 1), new Vector3(0.5f, 0.5f, 0.5f));
        Assert.Equal(Vector3.One, colour);
        Assert.Equal(0xffffffffu, ThingLight.Pack(colour));
        Assert.Equal(0xff000000u, ThingLight.Pack(Vector3.Zero));
    }

    [Fact]
    public void A_person_on_a_sunlit_street_is_no_longer_the_flat_stand_in()
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        var day = Location.Load(files, "P1_OTDY", scene: set.ForLevel("P1_OTDY")?.Entry?.SceneName);
        Assert.Contains(day.Things, t => t.Type == ThingType.Actor);

        // The stand-in was a flat mid-grey on every vertex of every unlit thing. With the sector's
        // lights and the level's sun applied there is a spread instead.
        var thingColours = day.Mesh.Batches.Where(b => b.Layer == BatchLayer.Thing).Take(1)
                              .SelectMany(b => Enumerable.Range((int)b.FirstIndex, (int)b.IndexCount))
                              .Select(i => day.Mesh.Vertices[(int)day.Mesh.Indices[i]].Color)
                              .Distinct().ToList();
        Assert.True(thingColours.Count > 1, "every vertex of the first thing is the same colour");
        Assert.DoesNotContain(0xff808080u, thingColours);
    }
}
