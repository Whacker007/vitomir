// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The halos** (the world draw's last loop; `rlDirect.dll`'s halo draw): a
/// thing whose render flags carry `HALO` and whose template names a `HALOMATERIAL` glows. These pin
/// the three template tags, the fade — in and out at 500 a second by whether
/// the eye can see the thing — the cap of 32, and the colour scaled by the fade.
/// </summary>
public class HaloTests
{
    [Fact]
    public void A_halo_comes_from_its_template_and_the_flashlight_s_is_a_fluoro_one()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var thing = new WorldThing { Index = -1, Guid = 1, Scene = Formats.NsdThing.Placed("lantern1_halo", Vector3.Zero, Vector3.Zero, 0), Template = "lantern1_halo" };
        var lantern = Halos.From(thing, templates.Resolve("lantern1_halo"))!;
        Assert.Equal("Halo1_white", lantern.Material, ignoreCase: true);
        Assert.Equal(64f, lantern.Size);
        Assert.Equal(0xffffffffu, lantern.Colour);

        var flashlight = Halos.From(thing, templates.Resolve("flashlight"))!;
        Assert.Equal(0.1f, flashlight.Size, 3);
        Assert.Equal(0xfff0faffu, flashlight.Colour);                       // 240, 250, 255
        Assert.Null(Halos.From(thing, templates.Resolve("torchU")));        // no HALOMATERIAL
    }

    [Fact]
    public void A_halo_fades_in_and_out_at_500_a_second_and_its_colour_with_it()
    {
        var halo = new Halo(new WorldThing { Index = -1, Guid = 1, Scene = new Formats.NsdThing(), Template = "halo_small_orange" }, "Halo1_white", 16f, 0xffff993eu);
        Assert.Equal(0f, halo.Alpha);                                       // every halo starts out
        Assert.Equal(0u, Halos.Shown(halo));
        halo.Alpha = 127.5f;                                                // a quarter of a second in
        Assert.Equal(0x80804c1fu, Halos.Shown(halo));                       // each channel halved, rounded to even
        halo.Alpha = Halos.Full;
        Assert.Equal(0xffff993eu, Halos.Shown(halo));
    }

    [Fact]
    public void The_sewer_s_lamps_are_seen_and_fade_in_over_half_a_second()
    {
        using var files = Install.Open();
        var sewer = Location.Load(files, "N1_SEW2", sky: false, player: "Christof");
        var halos = new Halos();
        var eye = new Vector3(4300, 2094, 20);
        int sector = sewer.Solid!.SectorAt(eye);
        halos.Step(sewer, eye, sector, 0.1f);
        Assert.NotEmpty(halos.Drawn);
        Assert.True(halos.Drawn.Count <= Halos.Most);
        var red = halos.Drawn.First(h => Vector3.Distance(h.Thing.Position, new Vector3(4640, 2094, -8)) < 1f);
        Assert.Equal(50f, red.Alpha, 1);                                    // 0.1 s × 500
        for (int i = 0; i < 10; i++) halos.Step(sewer, eye, sector, 0.1f);
        Assert.Equal(Halos.Full, red.Alpha);
    }

    [Fact]
    public void Things_on_the_ground_twinkle_for_half_a_second_every_five()
    {
        // The pulse at the end of the light pass: idle until five seconds in, then sin(elapsed × 0.36°)
        // for 500 ms, then idle until the next start five seconds after the last.
        var halos = new Halos();
        halos.Pulse(0);
        Assert.Equal(0f, halos.Twinkle);
        halos.Pulse(4999);
        Assert.Equal(0f, halos.Twinkle);
        halos.Pulse(5000);                                                  // starts: 0 on its first frame
        Assert.Equal(0f, halos.Twinkle);
        halos.Pulse(5250);
        Assert.Equal(1f, halos.Twinkle, 3);                                 // the peak, a quarter-second in
        Assert.Equal(0xffffffffu, halos.TwinkleColour());
        halos.Pulse(5400);
        Assert.Equal(MathF.Sin(144f * MathF.PI / 180f), halos.Twinkle, 3);
        halos.Pulse(5501);
        Assert.Equal(0f, halos.Twinkle);
        halos.Pulse(9999);
        Assert.Equal(0f, halos.Twinkle);
        halos.Pulse(10000);
        halos.Pulse(10125);
        Assert.Equal(MathF.Sin(45f * MathF.PI / 180f), halos.Twinkle, 3);
    }

    [Fact]
    public void A_bag_of_gold_on_the_floor_glints_at_the_pulse()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var christof = place.Player!.Thing;
        var gold = place.Place("GoldBag", christof.Position + new Vector3(40, 0, 0), Vector3.Zero, christof.Sector)!;
        Assert.NotEqual(0u, gold.RenderFlags & Halos.TwinkleFlag);
        Assert.NotNull(place.Materials.Named(Halos.TwinkleMaterial));
        var halos = new Halos();
        halos.Begin(place.Milliseconds);
        var eye = christof.Position + new Vector3(-100, 0, 80);
        int sector = place.Solid!.SectorAt(eye, (int)christof.Sector);
        place.Milliseconds += 5000;
        halos.Step(place, eye, sector, 0.016f);
        place.Milliseconds += 250;
        halos.Step(place, eye, sector, 0.016f);
        Assert.Contains(gold.Position, halos.Twinkles);
        Assert.Equal(1f, halos.Twinkle, 3);
    }
}
