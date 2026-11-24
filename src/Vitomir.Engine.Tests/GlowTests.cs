// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The hover glow, and the containers that are its most common subject: a barrel glows the colour
/// of an attack because a barrel is broken rather than opened, and it breaks at the first blow.
/// </summary>
public class GlowTests
{
    static Location Monastery(Vitomir.Engine.Vfs.GameFiles files) =>
        Location.Load(files, "P1_MON1", things: true, sky: false, scene: "MON1_8_2", player: "Christof");

    static WorldThing Barrel(Location place) =>
        place.Things.First(t => t.Template.Equals("container_barrel1", StringComparison.OrdinalIgnoreCase) && t.InWorld);

    // ---- the containers ----

    [Fact]
    public void The_shipped_templates_split_containers_into_ones_you_break_and_ones_you_open()
    {
        // CONTAINER_HEALTH (tag 116) and DESTROY_TEMPLATE (tag 117). Every barrel has one health
        // point and turns into debris; the volatile ones turn into an explosion; chests have neither.
        using var files = Install.Open();
        var place = Monastery(files);
        var barrel = Barrel(place);
        Assert.Equal(1f, barrel.ContainerHealth);
        Assert.Equal("_container_barrel1_destroyed", barrel.DestroyTemplate, ignoreCase: true);
        Assert.NotEqual(0u, barrel.State & Container.Breakable);
        Assert.Equal(0u, barrel.State & Container.Opened);

        var chest = place.Things.FirstOrDefault(t => t.Type == ThingType.Container && t.ContainerHealth == 0f);
        if (chest is not null) Assert.Equal(0u, chest.State & Container.Breakable);
    }

    [Fact]
    public void A_breakable_container_offers_an_attack_an_openable_one_a_use_and_a_spent_one_nothing()
    {
        // The cursor's container case, reading the container's state word.
        using var files = Install.Open();
        var place = Monastery(files);
        var player = place.Player!.Thing;
        var barrel = Barrel(place);
        Assert.Equal(CursorVerb.Attack, Cursor.Verb(player, barrel));
        barrel.State &= ~Container.Breakable;
        Assert.Equal(CursorVerb.Use, Cursor.Verb(player, barrel));
        barrel.State |= Container.Opened;
        Assert.Equal(CursorVerb.None, Cursor.Verb(player, barrel));
    }

    [Fact]
    public void Damage_only_lands_on_a_breakable_unopened_container_and_breaking_it_is_opening_it()
    {
        // The opened bit and the breakable bit are both checked before the health is
        // touched; at zero it calls the same open a use order reaches.
        using var files = Install.Open();
        var place = Monastery(files);
        var barrel = Barrel(place);

        barrel.State &= ~Container.Breakable;
        Assert.Equal(0f, Container.Damage(barrel, 5f));
        Assert.Equal(1f, barrel.ContainerHealth);
        barrel.State |= Container.Breakable;

        Container.Damage(barrel, 0.5f);
        Assert.Equal(0.5f, barrel.ContainerHealth);
        Assert.True(barrel.InWorld);
        Container.Damage(barrel, 0.5f, place.Player!.Thing);
        Assert.NotEqual(0u, barrel.State & Container.Opened);
        Assert.True(barrel.Has(WorldThing.Flag.NoHighlight));
        // It had a DESTROY_TEMPLATE, so opening it destroyed it ...
        Assert.False(barrel.InWorld);
        // ... and what it turned into is debris with ten seconds to live.
        var debris = place.Things.Single(t => t.Template.Equals("_container_barrel1_destroyed", StringComparison.OrdinalIgnoreCase)
                                              && Vector3.Distance(t.Position, barrel.Position) < 1f);
        Assert.Equal(place.Milliseconds + Location.ContainerDropLifetime, debris.ExpireAt);
    }

    [Fact]
    public void Christof_breaks_a_barrel_with_one_blow_and_no_roll_to_hit()
    {
        // The whole path a click takes: the verb is Attack, the order closes and swings, and the
        // blow lands through the blow's container branch — damage plus one, no to-hit — so a barrel
        // with one health point cannot survive it. In play the barrel did not seem to work.
        using var files = Install.Open();
        var place = Monastery(files);
        var sim = new Simulation(place);
        var christof = place.Player!.Thing;
        var barrel = Barrel(place);
        // Bring it to him rather than him to it, as the death tests do: moving the player would
        // leave his walk node behind.
        barrel.Position = christof.Position with { X = christof.Position.X + 40f };
        barrel.Sector = christof.Sector;

        for (int frame = 0; frame < 30 * 10 && barrel.InWorld; frame++)
        {
            Actions.Attack(christof, barrel, christof.Actor!.Weapon, 0, sim.Milliseconds, motions: place.Motions);
            sim.Step(1f / 30f);
        }
        Assert.False(barrel.InWorld, "the barrel survived ten seconds of Christof");
        Assert.Contains(place.Things, t => t.Template.Equals("_container_barrel1_destroyed", StringComparison.OrdinalIgnoreCase));
    }

    // ---- the glow ----

    [Fact]
    public void The_colour_comes_from_the_verb_and_is_picked_when_the_hover_changes()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var player = place.Player!.Thing;
        var barrel = Barrel(place);
        var door = place.Things.First(t => t.Template.Equals("dungeon_door", StringComparison.OrdinalIgnoreCase));
        var glow = new HoverGlow();

        glow.Point(barrel, player);
        Assert.Equal(0xFF600000u, glow.Colour(barrel));     // attack
        glow.Point(door, player);
        Assert.Equal(0xFF505000u, glow.Colour(door));       // use, not a person

        // Resting on the same thing again does not re-pick: change what the verb would say and
        // the barrel keeps the colour it was given until the hover changes.
        glow.Point(barrel, player);
        barrel.State &= ~Container.Breakable;
        glow.Point(barrel, player);
        Assert.Equal(0xFF600000u, glow.Colour(barrel));
    }

    [Fact]
    public void It_comes_up_in_a_third_of_a_second_and_the_tint_goes_the_moment_the_pointer_leaves()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var player = place.Player!.Thing;
        var barrel = Barrel(place);
        var glow = new HoverGlow();

        glow.Point(barrel, player);
        glow.Step(0.1f);
        Assert.Equal(0.3f, glow.Intensity(barrel), 3);
        Assert.Equal(HoverGlow.LiftLevel * 0.3f, glow.Base(barrel), 2);
        glow.Step(1f);
        Assert.Equal(1f, glow.Intensity(barrel));
        Assert.Single(glow.Lit());

        // Gone at once: the tint pass is gated on the hover bit, not on the intensity.
        glow.Point(null, player);
        Assert.Empty(glow.Lit());
        // The intensity, and with it the light, is still coming down.
        glow.Step(0.1f);
        Assert.Equal(0.7f, glow.Intensity(barrel), 3);
        Assert.Equal(HoverGlow.LiftLevel * 0.7f, glow.Base(barrel), 2);
        Assert.Contains(glow.Lifts(), l => ReferenceEquals(l.Thing, barrel));
    }

    [Fact]
    public void The_light_keeps_the_last_value_it_was_given_when_the_fade_reaches_zero()
    {
        // The base is rewritten only while the intensity is not zero — so the glow's own record
        // keeps the last raise. (On screen that residue lasts only on a thing riding another: the
        // sector's draw rewrites every other thing's base each frame; ModelLightTests.)
        using var files = Install.Open();
        var place = Monastery(files);
        var player = place.Player!.Thing;
        var barrel = Barrel(place);
        var glow = new HoverGlow();
        glow.Point(barrel, player);
        glow.Step(1f);
        glow.Point(null, player);
        for (int i = 0; i < 20; i++) glow.Step(1f / 60f);
        Assert.Equal(0f, glow.Intensity(barrel));
        Assert.Equal(HoverGlow.LiftLevel * 3f / 60f, glow.Base(barrel), 2);
        Assert.Contains(glow.Lifts(), l => ReferenceEquals(l.Thing, barrel));

        // And pointing back at it starts from where the fade got to.
        glow.Point(barrel, player);
        glow.Step(0.1f);
        Assert.Equal(0.3f, glow.Intensity(barrel), 3);
    }

    [Fact]
    public void A_thing_that_leaves_the_world_is_forgotten()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var barrel = Barrel(place);
        var glow = new HoverGlow();
        glow.Point(barrel, place.Player!.Thing);
        glow.Step(1f);
        place.Remove(barrel);
        glow.Step(0.01f);
        Assert.Empty(glow.Lifts());
        Assert.Equal(0f, glow.Intensity(barrel));
    }

    [Fact]
    public void The_reference_build_shows_the_outer_bytes_exchanged_and_the_fix_puts_them_back()
    {
        // Measured off a video: the monastery door gains (0, 80, 80) — cyan — for 0xFF505000.
        Assert.Equal(new Vector3(0, 80, 80), HoverGlow.Shown(0xFF505000u, authored: false) * 255f);
        Assert.Equal(new Vector3(0, 0, 96), HoverGlow.Shown(0xFF600000u, authored: false) * 255f);
        Assert.Equal(new Vector3(80, 80, 0), HoverGlow.Shown(0xFF505000u, authored: true) * 255f);
        Assert.Equal(new Vector3(96, 0, 0), HoverGlow.Shown(0xFF600000u, authored: true) * 255f);
        Assert.False(new Fixes().GlowColours);
    }

    [Fact]
    public void Raising_a_light_past_white_scales_it_down_by_its_brightest_channel()
    {
        // The renderer: the base is added, and a sum over full is divided by its largest
        // channel — the hue survives and nothing clips.
        Assert.Equal(new Vector3(0.3f, 0.3f, 0.3f), HoverGlow.Raise(new Vector3(0.1f, 0.1f, 0.1f), 0.2f));
        var up = HoverGlow.Raise(new Vector3(0.8f, 0.4f, 0.2f), 0.6f);
        Assert.Equal(1f, up.X, 5);
        Assert.Equal(1.0f / 1.4f, up.Y, 5);
        Assert.Equal(0.8f / 1.4f, up.Z, 5);
    }
}
