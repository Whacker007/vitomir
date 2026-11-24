// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Explosions;
using Vitomir.Engine.Scene;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **How a model is lit, every frame**: the picker — its scores,
/// its array and the two slips that write past the end of it, the sun's gate — the base light
/// the sector draw and the glow's raise, and the renderer's per-vertex arithmetic
/// (`rlDirect.dll`'s vertex lighting). Then the whole of it on the
/// monastery: a fireball lights the people near it, a torch carried past a thing lights it, and a
/// thing whose lights have not changed is not lit again.
/// </summary>
public class ModelLightTests
{
    static PickedLight Light(uint type = 0, float set = 300f, Vector3 at = default, Vector3 dir = default,
                             float falloff = 1f, float range = 300f, float cosPen = 0f, int style = 0, float score = 0f,
                             Vector3? colour = null) =>
        new(type, colour ?? new Vector3(255f), at, dir, set, falloff, range, cosPen, style, new object(), score, set);

    static readonly LightStyles Steady = new();

    // ---- the picker's scores ----

    [Fact]
    public void The_engine_measures_with_its_own_square_root()
    {
        // 0x5f3759df and one Newton step. Within two parts in a thousand.
        foreach (float d in new[] { 1f, 7f, 100f, 333.3f, 5000f })
            Assert.InRange(ModelLight.InverseRoot(d * d) * d * d, d * 0.998f, d * 1.002f);
        Assert.Equal(0f, ModelLight.InverseRoot(0f) * 0f);
    }

    [Fact]
    public void A_point_light_scores_from_the_things_surface_and_a_sun_by_its_unstyled_intensity()
    {
        var point = Light(set: 300f, at: Vector3.Zero, falloff: 1f);
        Assert.Equal(200f, ModelLight.Score(point, new Vector3(100, 0, 0), 0f, Steady), 0);
        Assert.Equal(232f, ModelLight.Score(point, new Vector3(100, 0, 0), 32f, Steady), 0);   // RADIUS 32

        // The sun (type 2) scores its set intensity whatever the style says — once the styled one is
        // not zero, which is the test every light passes first.
        var styles = new LightStyles(seed: 1);
        styles.Enable(16, true);
        var sun = Light(type: 2, set: 250f, style: 16);
        Assert.Equal(250f, ModelLight.Score(sun, new Vector3(9999, 0, 0), 0f, styles));
        styles.Enable(16, false);
        Assert.Equal(0f, ModelLight.Score(sun, Vector3.Zero, 0f, styles));
    }

    [Fact]
    public void A_spot_stops_at_its_cone_and_a_box_falls_off_with_the_square()
    {
        var spot = Light(type: 1, set: 300f, at: Vector3.Zero, dir: Vector3.UnitX, range: 300f, cosPen: 0.9f);
        Assert.Equal(200f, ModelLight.Score(spot, new Vector3(100, 0, 0), 0f, Steady), 0);
        Assert.Equal(0f, ModelLight.Score(spot, new Vector3(0, 100, 0), 0f, Steady));          // outside the cone
        Assert.Equal(0f, ModelLight.Score(spot, new Vector3(301, 0, 0), 0f, Steady));          // past its range

        var box = Light(type: 3, set: 400f, at: Vector3.Zero, falloff: 1f, range: 300f);
        Assert.Equal(400f / (20f * 20f), ModelLight.Score(box, new Vector3(20, 0, 0), 0f, Steady), 2);
        Assert.Equal(0f, ModelLight.Score(box, new Vector3(301, 0, 0), 0f, Steady));
    }

    // ---- the picker's array ----

    [Fact]
    public void The_strongest_are_kept_and_the_weakest_evicted()
    {
        var picker = new ModelLightPicker();
        picker.Begin();
        foreach (float s in new[] { 5f, 3f, 9f, 1f, 7f, -2f, 0f })
            picker.Offer(Light(score: s), max: 3);
        picker.Finish(Steady);
        Assert.Equal(new[] { 9f, 7f, 5f }, picker.Lights.ToArray().Select(l => l.Score));
    }

    [Fact]
    public void The_late_pass_counts_before_it_compares_and_can_drop_a_stronger_light()
    {
        // In the pass over the other sectors' lights the count goes up before the new
        // light is compared with the weakest, so the weakest's index can end up one past the last —
        // and when the array next fills, the light is compared with that slot instead, which a fresh
        // picker holds at zero. The stronger light goes there and is lost.
        var picker = new ModelLightPicker();
        picker.Begin();
        picker.Offer(Light(score: 5f), max: 2);
        picker.Offer(Light(score: 3f), max: 2, late: true);                 // weaker: the weakest is now "slot 2"
        picker.Offer(Light(score: 4f), max: 2, late: true);                 // stronger than 3, and lost
        picker.Finish(Steady);
        Assert.Equal(new[] { 5f, 3f }, picker.Lights.ToArray().Select(l => l.Score));

        // The same three in the first pass keep the stronger one.
        picker.Begin();
        foreach (float s in new[] { 5f, 3f, 4f }) picker.Offer(Light(score: s), max: 2);
        picker.Finish(Steady);
        Assert.Equal(new[] { 5f, 4f }, picker.Lights.ToArray().Select(l => l.Score));
    }

    [Fact]
    public void The_sun_is_lost_when_the_array_is_full_and_suns_are_handed_over_last()
    {
        var picker = new ModelLightPicker();
        picker.Begin();
        var torch = Light(score: 5f);
        picker.Offer(torch, max: 1);
        picker.OfferGlobal(Light(type: 2, score: 9f), max: 1);             // Past the end
        picker.Finish(Steady);
        Assert.Single(picker.Lights.ToArray());
        Assert.Same(torch.Source, picker.Lights[0].Source);
        Assert.Equal(5f, picker.Lights[0].Score);                           // its score goes past the end too

        // With room it is added — and sorted after every light that is not a sun.
        picker.Begin();
        picker.OfferGlobal(Light(type: 2, score: 900f), max: 4);
        picker.Offer(Light(score: 1f), max: 4);
        picker.Finish(Steady);
        Assert.Equal(new uint[] { 0, 2 }, picker.Lights.ToArray().Select(l => l.Type));
    }

    [Fact]
    public void A_sun_lost_to_a_full_array_leaves_the_order_the_lights_are_handed_in()
    {
        // The light and its score both go to the slot past the end, so the
        // weakest keeps its own score and stays last — and the first light, the one a shadow is cast
        // from first, is still the strongest.
        var picker = new ModelLightPicker();
        picker.Begin();
        var strong = Light(score: 5f);
        var weak = Light(score: 3f);
        picker.Offer(strong, max: 2);
        picker.Offer(weak, max: 2);
        picker.OfferGlobal(Light(type: 2, score: 9f), max: 2);
        picker.Finish(Steady);
        Assert.Equal(new[] { 5f, 3f }, picker.Lights.ToArray().Select(l => l.Score));
        Assert.Same(strong.Source, picker.Lights[0].Source);
        Assert.Same(weak.Source, picker.Lights[1].Source);
    }

    [Fact]
    public void Each_light_is_handed_over_at_this_frames_intensity()
    {
        // The set intensity times the style's brightness, and nothing while the style is off.
        var styles = new LightStyles(seed: 1);
        var picker = new ModelLightPicker();
        picker.Begin();
        picker.Offer(Light(set: 300f, style: 0, score: 1f), 8);
        picker.Offer(Light(set: 300f, style: 20, score: 2f), 8);
        styles.Enable(20, false);
        picker.Finish(styles);
        Assert.Equal(new[] { 0f, 300f }, picker.Lights.ToArray().Select(l => l.Now));
    }

    [Fact]
    public void The_sun_reaches_only_the_sectors_that_let_it_in()
    {
        // `sector+4 & 0x10000`. Prague's streets by day carry it; no monastery sector does.
        using var files = Install.Open();
        var day = NilLevel.Read(files.Read("levels/P1_OTDY.nil"), "P1_OTDY");
        var monastery = NilLevel.Read(files.Read("levels/P1_MON1.nil"), "P1_MON1");
        Assert.Contains(day.Sectors, s => (s.Flags & ModelLight.SunlitSector) != 0);
        Assert.DoesNotContain(monastery.Sectors, s => (s.Flags & ModelLight.SunlitSector) != 0);
    }

    // ---- the base light and the glow ----

    [Fact]
    public void The_base_is_the_brighter_ambient_over_392_and_an_item_lying_about_is_never_below_100()
    {
        var level = new NilLevel { Ambient = new Vector3(51000, 46000, 34000) };      // Prague by day
        var sector = new NilSector { Ambient = new Vector3(60000, 0, 0) };
        Assert.Equal(new Vector3(60000, 46000, 34000) / 392f, ModelLight.Base(level, sector, ThingType.Actor));

        var dark = new NilLevel { Ambient = Vector3.Zero };
        Assert.Equal(Vector3.Zero, ModelLight.Base(dark, null, ThingType.Actor));
        Assert.Equal(new Vector3(100f), ModelLight.Base(dark, null, ThingType.Item));
        Assert.Equal(new Vector3(100f), ModelLight.Base(dark, null, ThingType.Weapon));
        Assert.Equal(new Vector3(100f), ModelLight.Base(dark, null, ThingType.Armor));
        Assert.Equal(new Vector3(130.1f, 117.3f, 100f), ModelLight.Base(level, null, ThingType.Item), new Vec3Near(0.1f));
    }

    [Fact]
    public void The_glow_raises_the_base_to_at_least_150_and_scales_it()
    {
        // The glow's raise, while the model's glow intensity is not zero.
        Assert.Equal(new Vector3(75f), ModelLight.Glow(Vector3.Zero, 0.5f));
        Assert.Equal(new Vector3(100f, 75f, 75f), ModelLight.Glow(new Vector3(200f, 10f, 0f), 0.5f));
        Assert.Equal(new Vector3(7f, 8f, 9f), ModelLight.Glow(new Vector3(7f, 8f, 9f), 0f));
    }

    // ---- one vertex (the renderer's arithmetic) ----

    [Fact]
    public void A_point_light_square_to_a_vertex_gives_its_colour_times_what_is_left_over_392()
    {
        var torch = Light(set: 270f, at: new Vector3(0, 0, 100), falloff: 1f, colour: new Vector3(239, 176, 29));
        var lit = ModelLight.Vertex([torch], Vector3.Zero, Vector3.UnitZ, Vector3.Zero);
        Assert.Equal(new Vector3(239, 176, 29) * (170f / 392f), lit, new Vec3Near(0.3f));

        // Turned away, or past where its fall-off reaches nothing, it gives nothing; the range is not
        // what cuts a point light off.
        Assert.Equal(Vector3.Zero, ModelLight.Vertex([torch], Vector3.Zero, -Vector3.UnitZ, Vector3.Zero));
        Assert.Equal(Vector3.Zero, ModelLight.Vertex([torch], new Vector3(0, 0, -200), Vector3.UnitZ, Vector3.Zero));
        // At an angle it gives the cosine's share.
        var slant = ModelLight.Vertex([torch with { Position = new Vector3(100, 0, 100) }], Vector3.Zero, Vector3.UnitZ, Vector3.Zero);
        float d = MathF.Sqrt(20000f);
        Assert.Equal(new Vector3(239, 176, 29) * ((270f - d) * (100f / d) / 392f), slant, new Vec3Near(0.5f));
    }

    [Fact]
    public void A_sun_lights_by_the_angle_alone_and_a_spot_only_inside_its_cone()
    {
        var sun = Light(type: 2, set: 196f, dir: -Vector3.UnitZ);
        Assert.Equal(new Vector3(127.5f), ModelLight.Vertex([sun], new Vector3(5000, 0, 0), Vector3.UnitZ, Vector3.Zero), new Vec3Near(0.01f));

        // The spot points down; the vertex below it is inside the cone and one off to the side is not.
        var spot = Light(type: 1, set: 300f, at: new Vector3(0, 0, 100), dir: -Vector3.UnitZ, cosPen: 0.9f);
        Assert.NotEqual(Vector3.Zero, ModelLight.Vertex([spot], Vector3.Zero, Vector3.UnitZ, Vector3.Zero));
        Assert.Equal(Vector3.Zero, ModelLight.Vertex([spot], new Vector3(200, 0, 0), Vector3.UnitZ, Vector3.Zero));
    }

    [Fact]
    public void The_base_is_added_and_a_colour_past_white_is_scaled_to_keep_its_hue()
    {
        // The sum, the brightest channel held to 255 with the others scaled alike.
        Assert.Equal(0xff000000u | (20u << 8) | (20u << 16), ModelLight.Pack(new Vector3(0, 10, 20), new Vector3(0, 10, 0)));
        uint packed = ModelLight.Pack(new Vector3(510, 255, 0), Vector3.Zero);
        Assert.Equal(255u, packed & 0xff);
        Assert.Equal(128u, (packed >> 8) & 0xff);                           // 127.5 rounds to even
        Assert.Equal(0u, (packed >> 16) & 0xff);
    }

    // ---- the monastery ----

    static Location Monastery(Vfs.GameFiles files) =>
        Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");

    static uint[] ColoursOf(Location place, WorldThing thing)
    {
        var a = place.Mesh.Animated.First(x => ReferenceEquals(place.Drawn[x.Thing], thing));
        return a.Vertices.Select(v => v.Color).ToArray();
    }

    static float Brightness(IEnumerable<uint> colours) =>
        colours.Sum(c => (float)((c & 0xff) + ((c >> 8) & 0xff) + ((c >> 16) & 0xff)));

    [Fact]
    public void A_fireball_lights_the_people_near_it()
    {
        // The second and third loops of the picker: a light a thing carries is chosen for the models
        // around it. `exp_fireball` carries one (150, falloff 1.5) and nothing in the monastery's
        // model lighting did earlier.
        using var files = Install.Open();
        var place = Monastery(files);
        var christof = place.Player!.Thing;
        var lighting = new ThingLighting();
        foreach (var a in place.Mesh.Animated) a.Pose();
        lighting.Relight(place.Mesh, place, place.Drawn);
        float before = Brightness(ColoursOf(place, christof));

        var at = christof.Position + new Vector3(0, 0, 60f) + Vector3.TransformNormal(new Vector3(0, 60f, 0), christof.Rotation);
        var blast = place.Place("exp_fireball", at, christof.Orientation, christof.Sector)!;
        var sim = new Simulation(place);
        sim.Step(0.3f);
        Assert.True(place.Lights.Of(blast) is { Intensity: > 0f }, "the fireball's light is not on");
        foreach (var a in place.Mesh.Animated) a.Pose();
        lighting.Relight(place.Mesh, place, place.Drawn);
        float after = Brightness(ColoursOf(place, christof));
        Assert.True(after > before * 1.05f, $"Christof went from {before} to {after}");
        Assert.Contains(ModelLightReport.Describe(place, christof), l => l.Contains("carried"));
    }

    [Fact]
    public void A_man_standing_where_1_1_saved_Wilhem_is_lit_by_the_corridors_torches()
    {
        // In the SP4 save 1.1 lights Wilhem in the corridor from the front, warm; an earlier build drew him
        // black. The lights were never the matter — sector 44, the one his feet are in, lists 14 —
        // but its walker put him in 172, the sector above, which has no surfaces and no lights.
        using var files = Install.Open();
        var place = Monastery(files);
        var walker = place.Player!;
        walker.Thing.Position = new Vector3(-915.8151f, -610.0678f, -252f);
        Assert.True(walker.Ground());
        Assert.Equal(44L, walker.Thing.Sector);

        var picker = new ModelLightPicker();
        ModelLight.Pick(picker, ModelLight.DefaultMaxLights, walker.Thing, place.Level, place.Styles, place.Lights, prelit: false);
        Assert.Equal(ModelLight.DefaultMaxLights, picker.Count);
        Assert.All(picker.Lights.ToArray(), l => Assert.True(l.Source is NilLight && l.Score > 0f));
        // The strongest is the torch 30 units east of Christof, 108 over the floor (300, falloff 1).
        Assert.Equal(new Vector3(-795, -596, -144), picker.Lights[0].Position);

        var lighting = new ThingLighting();
        foreach (var a in place.Mesh.Animated) a.Pose();
        lighting.Relight(place.Mesh, place, place.Drawn);
        var colours = ColoursOf(place, walker.Thing);
        // Most of him is lit (the side away from every torch is black, as it should be), and warm.
        float lit = colours.Count(c => (c & 0xffffff) != 0) / (float)colours.Length;
        Assert.True(lit > 0.5f, $"only {lit:P0} of his vertices are lit");
        float red = colours.Sum(c => (float)(c & 0xff)), blue = colours.Sum(c => (float)((c >> 16) & 0xff));
        Assert.True(red > 2f * blue, $"red {red}, blue {blue}");
    }

    [Fact]
    public void Only_the_animating_things_the_frame_posed_are_lit()
    {
        // The frame poses and lights what its view reaches (the sector draw draws a sector's things
        // from the sector's list); the rest keep the vertices they had.
        using var files = Install.Open();
        var place = Monastery(files);
        var lighting = new ThingLighting();
        foreach (var a in place.Mesh.Animated) a.Pose();
        lighting.Relight(place.Mesh, place, place.Drawn);      // the first frame lights what stands still as well
        lighting.Relight(place.Mesh, place, place.Drawn);
        int all = lighting.VerticesLit;
        Assert.True(place.Mesh.Animated.Count > 1 && all > 0);

        var walker = place.Mesh.Animated.First(a => ReferenceEquals(place.Drawn[a.Thing], place.Player!.Thing));
        lighting.Posed = [walker];
        lighting.Relight(place.Mesh, place, place.Drawn);
        Assert.True(lighting.VerticesLit > 0 && lighting.VerticesLit < all, $"{lighting.VerticesLit} of {all}");
        Assert.DoesNotContain(lighting.Changed, s => place.Mesh.Animated.Any(a => a.FirstVertex == s.First));   // never as one standing still

        lighting.Posed = null;
        lighting.Relight(place.Mesh, place, place.Drawn);
        Assert.Equal(all, lighting.VerticesLit);
    }

    [Fact]
    public void A_thing_standing_still_is_lit_again_only_when_its_lights_change()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var lighting = new ThingLighting();
        lighting.Relight(place.Mesh, place, place.Drawn);
        int first = lighting.Changed.Count;
        Assert.True(first > 0, "nothing standing still was lit on the first frame");

        // Nothing moved and no style changed: the steady-lit things are not lit again. Those under a
        // flickering light are, which is why this is fewer rather than none.
        lighting.Relight(place.Mesh, place, place.Drawn);
        Assert.True(lighting.Changed.Count < first, $"{lighting.Changed.Count} of {first} lit again with nothing changed");

        // And one whose lights did change is: a torch carried to a barrel.
        var barrel = place.Things.First(t => t.Template.Equals("container_barrel1", StringComparison.OrdinalIgnoreCase) && t.InWorld);
        var flame = place.Place("torchFlame", barrel.Position + new Vector3(0, 40, 60), Vector3.Zero, barrel.Sector)!;
        place.Lights.Step(place.Milliseconds, 16, place.Styles);
        Assert.NotNull(place.Lights.Of(flame));
        lighting.Relight(place.Mesh, place, place.Drawn);
        Assert.Contains(lighting.Changed, s => ReferenceEquals(place.Drawn[s.Thing], barrel));
    }

    [Fact]
    public void A_thing_that_moves_and_a_light_whose_style_changes_are_both_lit_again()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var lighting = new ThingLighting();
        lighting.Relight(place.Mesh, place, place.Drawn);
        lighting.Relight(place.Mesh, place, place.Drawn);

        // A barrel pushed along is lit where it now is.
        var barrel = place.Things.First(t => t.Template.Equals("container_barrel1", StringComparison.OrdinalIgnoreCase) && t.InWorld);
        place.Move(barrel, barrel.Position + new Vector3(20, 0, 0));
        lighting.Relight(place.Mesh, place, place.Drawn);
        Assert.Contains(lighting.Changed, s => ReferenceEquals(place.Drawn[s.Thing], barrel));

        // A light of the barrel's sector put on the torches' flicker (style 1): each step of the
        // style is a new light on the barrel, as it is on the walls the light's lightmap drives.
        var sector = place.Level.Sectors[barrel.Sector];
        Assert.NotEmpty(sector.Lights);
        for (int i = 0; i < sector.Lights.Length; i++) sector.Lights[i] = sector.Lights[i] with { Style = 1 };
        int changedAt = 0;
        for (int f = 0; f < 30; f++)
        {
            place.Styles.Step(100f);
            lighting.Relight(place.Mesh, place, place.Drawn);
            if (lighting.Changed.Any(s => ReferenceEquals(place.Drawn[s.Thing], barrel))) changedAt++;
        }
        Assert.True(changedAt > 5, $"the barrel was lit again on {changedAt} of 30 flickering frames");
    }

    [Fact]
    public void A_carried_torch_is_one_of_the_lights_on_the_man_carrying_it()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var christof = place.Player!.Thing;
        var torch = place.Make("torchU")!.Item!;
        christof.Actor!.Inventory.Add(torch);
        Rpg.Equipment.Equip(christof, torch, Rpg.Slot.None, new Rpg.EffectDefinitions(files), 0);
        // The torch's own script is what lights it; without scripts, hang its flame on it as the
        // script does (`ItemUtilityTorch.CreateTorch`).
        var flame = place.Place("torchFlame_nosave", christof.Position, christof.Orientation, christof.Sector)!;
        var body = torch.Thing!;
        var tag = body.Animation?.TagBone(34) ?? -1;                         // MOTIONTAG_USER0
        Assert.True(Attachments.Attach(body, flame, tag, Vector3.Zero, Matrix4x4.Identity, Attachments.AutoRemove));
        var sim = new Simulation(place);
        sim.Step(0.05f);
        var picker = new ModelLightPicker();
        ModelLight.Pick(picker, 4, christof, place.Level, place.Styles, place.Lights, prelit: false);
        Assert.Contains(picker.Lights.ToArray(), l => l.Source is DynamicLight d && ReferenceEquals(d, place.Lights.Of(flame)));
    }

    /// <summary>Vectors compared a channel at a time within a tolerance.</summary>
    sealed class Vec3Near(float within) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => MathF.Abs(a.X - b.X) <= within && MathF.Abs(a.Y - b.Y) <= within && MathF.Abs(a.Z - b.Z) <= within;
        public int GetHashCode(Vector3 v) => 0;
    }
}
