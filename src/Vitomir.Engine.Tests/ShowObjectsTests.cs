// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The show-objects key (ours, <see cref="ShowObjects"/>): held, every object on screen the pointer
/// could land on glows in the colour it would take under the pointer, and nothing alive or acting
/// does. Pinned against these places — the convent, the Old Town, a mine — and the
/// first monastery, whose door, lever and barrels were the hover glow's own first report; then the
/// view test, the glow it feeds, and the key itself (Left Alt, beside 1.1's Alt, remappable).
/// </summary>
public class ShowObjectsTests
{
    static Location Load(Vitomir.Engine.Vfs.GameFiles files, string level, string scene) =>
        Location.Load(files, level, things: true, sky: false, scene: scene, player: "Christof");

    static IEnumerable<WorldThing> Named(Location place, string template) =>
        place.Things.Where(t => t.InWorld && t.Template.Equals(template, StringComparison.OrdinalIgnoreCase));

    static void Lit(Location place, string template, uint colour)
    {
        var me = place.Player!.Thing;
        var things = Named(place, template).ToList();
        Assert.True(things.Count > 0, $"no {template} in {place.Name}");
        foreach (var t in things)
        {
            Assert.True(ShowObjects.Takes(me, t), $"{template} (guid {t.Guid}) is not lit");
            Assert.Equal(colour, ShowObjects.Colour(me, t));
        }
    }

    static void Dark(Location place, string template)
    {
        var me = place.Player!.Thing;
        var things = Named(place, template).ToList();
        Assert.True(things.Count > 0, $"no {template} in {place.Name}");
        foreach (var t in things) Assert.False(ShowObjects.Takes(me, t), $"{template} (guid {t.Guid}) is lit");
    }

    /// <summary>
    /// Nothing with an actor record, nothing of a character's type and nothing riding one is lit in
    /// any of the places — however the pointer treats it — but a living animal picked up like an item (a feeder rat).
    /// </summary>
    static void NoCharacters(Location place)
    {
        var me = place.Player!.Thing;
        foreach (var t in place.Things.Where(t => t.InWorld))
            if (ShowObjects.IsCharacter(ShowObjects.Root(t)) && !ShowObjects.TakenLikeAnItem(t))
                Assert.False(ShowObjects.Takes(me, t), $"{t.Template} (guid {t.Guid}) is alive or acting and lit");
    }

    const uint Use = 0xFF505000, Take = 0xFF005050, Attack = 0xFF600000;

    [Fact]
    public void In_the_convent_the_bed_the_cross_the_vault_and_the_way_out_are_lit_and_anezka_is_not()
    {
        using var files = Install.Open();
        var place = Load(files, "P1_CTDY", "CTDY_4_1");
        var me = place.Player!.Thing;
        Lit(place, "bed2b", Use);
        Lit(place, "savecross", Use);
        Lit(place, "vault", Use);
        Lit(place, "tracker_thing", Use);
        // Anezka is one the pointer lands on — to talk to her — and the key leaves her out.
        var anezka = Named(place, "anezka").Single();
        Assert.True(Highlight.Offers(me, anezka));
        Assert.False(ShowObjects.Takes(me, anezka));
        // The level author's NO HIGHLIGHT keeps the furniture out, as it keeps the pointer off it.
        Dark(place, "tablecandlestick");
        Dark(place, "bed1");
        // And a flame has no body for the pointer's ray to meet.
        Dark(place, "candleflame");
        NoCharacters(place);
    }

    [Fact]
    public void In_the_old_town_the_barrels_to_break_and_the_exits_are_lit_and_nobody_in_the_street_is()
    {
        using var files = Install.Open();
        var place = Load(files, "P1_OTDY", "OTDY_4_1");
        var me = place.Player!.Thing;
        Lit(place, "container_barrel1", Attack);
        Lit(place, "tracker_thing", Use);
        Dark(place, "barrel3");          // scenery barrels: NO HIGHLIGHT
        Dark(place, "cartempty");
        Dark(place, "ghost");            // a script's holder, NO HIGHLIGHT and no body
        Dark(place, "horse");
        Dark(place, "crow");
        // A passer-by made pointable — not neutral — draws the blade under the pointer and stays dark here.
        var who = place.Things.First(t => t.Type == ThingType.Actor && t.Actor is { } a
                                          && a.Has(Actor.Flag.Neutral) && !t.Has(WorldThing.Flag.NoHighlight));
        who.Actor!.Flags &= ~Actor.Flag.Neutral;
        Assert.True(Highlight.Offers(me, who));
        Assert.False(ShowObjects.Takes(me, who));
        NoCharacters(place);
    }

    [Fact]
    public void In_the_mine_what_lies_on_the_floor_the_chest_and_the_barrels_are_lit_and_the_szlachta_and_rats_are_not()
    {
        using var files = Install.Open();
        var place = Load(files, "P1_SIM1", "SIM1_5_1");
        Lit(place, "potionhealth", Take);
        Lit(place, "silvercoins", Take);
        Lit(place, "goldbag", Take);
        Lit(place, "holywater", Take);             // a weapon on the floor
        Lit(place, "container_fancychest", Use);
        Lit(place, "container_barrel1", Attack);
        Lit(place, "exit_message", Use);
        Dark(place, "szlachta");
        Dark(place, "rat");
        Dark(place, "mine_cart");
        Dark(place, "ghost");
        NoCharacters(place);
    }

    [Fact]
    public void In_the_first_monastery_the_door_the_lever_the_chests_and_the_loot_are_lit_and_the_raising_doors_and_the_dead_are_not()
    {
        using var files = Install.Open();
        var place = Load(files, "P1_MON1", "MON1_8_2");
        var me = place.Player!.Thing;
        Lit(place, "dungeon_door", Use);
        Lit(place, "small_lever1", Use);
        Lit(place, "container_smallchest", Use);
        Lit(place, "container_barrel1", Attack);
        Lit(place, "buckler", Take);
        Lit(place, "discscroll_awaken_1", Take);
        Dark(place, "door_raise1_nhl");   // opened by the lever, never pointed at (NO HIGHLIGHT, BLOCKSELECT)
        Dark(place, "cappadocian");
        Dark(place, "skel_sword");
        Dark(place, "corpse_awaken");
        Dark(place, "corpseactor04");
        NoCharacters(place);

        // An opened chest offers nothing, so it goes dark.
        var chest = Named(place, "container_smallchest").First();
        chest.State |= Container.Opened;
        Assert.False(ShowObjects.Takes(me, chest));
    }

    /// <summary>
    /// A character is judged by its type **and** its actor record: anything with an actor is out,
    /// whatever its type says — the summoned, a ghost, a golem, an animal, a shape.
    /// </summary>
    /// <summary>
    /// The feeder rats are lit, in the pointer's own colour for them (its use-a-character blue), as the item a click makes of them; the hostile rats
    /// and their leaders beside them are not. A dead feeder rat is not.
    /// </summary>
    [Fact]
    public void In_the_second_monastery_the_feeder_rats_are_lit_and_the_hostile_rats_are_not()
    {
        using var files = Install.Open();
        var place = Load(files, "P1_MON2", "MON2_8_2");
        Lit(place, "rat_small", 0xFF000050);       // the hover's colour for a character it uses, as the pointer gives it
        Dark(place, "rat");
        Dark(place, "ratleader");
        NoCharacters(place);
        var rat = Named(place, "rat_small").First();
        Assert.True(ShowObjects.TakenLikeAnItem(rat));
        rat.Actor!.Flags |= Actor.Flag.Dead;
        Assert.False(ShowObjects.TakenLikeAnItem(rat));
        Assert.False(ShowObjects.Takes(place.Player!.Thing, rat));
    }

    [Fact]
    public void Anything_with_an_actor_record_is_alive_or_acting_whatever_its_type()
    {
        using var files = Install.Open();
        var place = Load(files, "P1_MON1", "MON1_8_2");
        var barrel = Named(place, "container_barrel1").First();
        Assert.False(ShowObjects.IsCharacter(barrel));
        barrel.Actor = Named(place, "cappadocian").First().Actor;
        Assert.True(ShowObjects.IsCharacter(barrel));
        Assert.False(ShowObjects.Takes(place.Player!.Thing, barrel));
    }

    /// <summary>
    /// Only what is on screen: the portal walk's sectors (the renderer's own) and the view.
    /// The camera `GlowFrameTests` looks at the lever's barrels with sees them; turned round, it does not.
    /// </summary>
    [Fact]
    public void Only_what_the_frames_walk_reaches_and_the_view_holds_is_lit()
    {
        using var files = Install.Open();
        var place = Load(files, "P1_MON1", "MON1_8_2");
        var me = place.Player!.Thing;
        var camera = new FreeCamera { Position = new Vector3(1414f, -902.5f, -104.4f), Yaw = 90f, Pitch = -14f };
        var barrel = Named(place, "container_barrel1").OrderBy(t => Vector3.Distance(t.Position, new Vector3(1414f, -495f, -270f))).First();

        var (found, walk) = Frame(place, me, camera);
        Assert.NotNull(walk);
        Assert.True(found.ContainsKey(barrel), "the barrel in front of the camera is not lit");
        Assert.Equal(Attack, found[barrel]);
        Assert.All(found.Keys, t => Assert.True(t.Sector < 0 || walk!.Sees((int)t.Sector), $"{t.Template} is in a sector the walk missed"));
        Assert.All(found.Keys, t => Assert.False(ShowObjects.IsCharacter(t)));
        // The walk leaves out things the view alone would take: rooms beyond the walls.
        var proj = camera.Projection(16f / 9f);
        int behindWalls = place.Things.Count(t => ShowObjects.Takes(me, t) && ShowObjects.OnScreen(t, camera.View * proj, null)
                                                  && !ShowObjects.OnScreen(t, camera.View * proj, walk));
        Assert.True(behindWalls > 0, "the walk took nothing out");

        camera.Yaw = -90f;
        var (behind, _) = Frame(place, me, camera);
        Assert.False(behind.ContainsKey(barrel), "the barrel behind the camera is lit");
    }

    static (Dictionary<WorldThing, uint> Found, PortalView? Walk) Frame(Location place, WorldThing me, FreeCamera camera)
    {
        var proj = camera.Projection(16f / 9f);
        var walk = ShowObjects.Walk(place, camera.Position, camera.View, proj);
        return (ShowObjects.Find(place.Things, me, camera.View * proj, walk), walk);
    }

    [Fact]
    public void The_view_test_takes_a_sphere_partly_inside_and_refuses_one_wholly_outside()
    {
        var camera = new FreeCamera { Position = Vector3.Zero, Yaw = 0f, Pitch = 0f };   // looking along +X
        var vp = camera.View * camera.Projection(4f / 3f);
        Assert.True(ShowObjects.InView(vp, new Vector3(500f, 0f, 0f), 10f));
        Assert.False(ShowObjects.InView(vp, new Vector3(-500f, 0f, 0f), 10f));      // behind
        Assert.False(ShowObjects.InView(vp, new Vector3(500f, 2000f, 0f), 10f));    // far off to the side
        Assert.True(ShowObjects.InView(vp, new Vector3(500f, 2000f, 0f), 1900f));   // but big enough to reach in
        Assert.False(ShowObjects.InView(vp, new Vector3(20000f, 0f, 0f), 10f));     // past the far clip
    }

    /// <summary>
    /// It is presentation only: finding what to light reads the world and writes nothing into it.
    /// </summary>
    [Fact]
    public void Finding_what_to_light_changes_nothing_in_the_world()
    {
        using var files = Install.Open();
        var place = Load(files, "P1_MON1", "MON1_8_2");
        var me = place.Player!.Thing;
        var before = place.Things.Select(t => (t.Flags, t.State, t.Position, t.Sector, t.InWorld, t.Actor?.Flags)).ToList();
        var camera = new FreeCamera { Position = new Vector3(1414f, -902.5f, -104.4f), Yaw = 90f, Pitch = -14f };
        _ = Frame(place, me, camera);
        Assert.Equal(before, place.Things.Select(t => (t.Flags, t.State, t.Position, t.Sector, t.InWorld, t.Actor?.Flags)).ToList());
    }

    // ---- the glow it feeds ----

    [Fact]
    public void Shown_things_glow_as_the_hovered_one_does_each_in_its_own_colour_and_the_hover_goes_on()
    {
        using var files = Install.Open();
        var place = Load(files, "P1_MON1", "MON1_8_2");
        var me = place.Player!.Thing;
        var barrel = Named(place, "container_barrel1").First();
        var chest = Named(place, "container_smallchest").First();
        var enemy = Named(place, "cappadocian").First();
        var glow = new HoverGlow();

        glow.Point(enemy, me);                                         // the pointer on an enemy
        glow.Show(new Dictionary<WorldThing, uint> { [barrel] = Attack, [chest] = Use });
        glow.Step(1f / HoverGlow.FadeRate / 2f);                       // half way up
        Assert.Equal(0.5f, glow.Intensity(barrel), 3);
        Assert.Equal(0.5f, glow.Intensity(chest), 3);
        Assert.Equal(0.5f, glow.Intensity(enemy), 3);
        glow.Step(1f);
        var lit = glow.Lit().ToDictionary(p => (object)p.Thing, p => p.Add, ReferenceEqualityComparer.Instance);
        Assert.Equal(3, lit.Count);
        Assert.Equal(HoverGlow.Shown(Attack, false), lit[barrel]);
        Assert.Equal(HoverGlow.Shown(Use, false), lit[chest]);
        Assert.Equal(HoverGlow.Shown(Attack, false), lit[enemy]);    // the hover's own colour
        Assert.True(glow.IsShown(barrel));

        // Let go: the tint goes the next frame and the light fades, as the hover's does.
        glow.Show(null);
        glow.Step(0.01f);
        Assert.DoesNotContain(glow.Lit(), p => ReferenceEquals(p.Thing, barrel));
        Assert.True(glow.Intensity(barrel) > 0f);
        Assert.True(glow.Base(barrel) > 0f);
        Assert.Contains(glow.Lit(), p => ReferenceEquals(p.Thing, enemy));
    }

    // ---- the key ----

    [Fact]
    public void The_key_is_left_alt_held_remappable_and_on_the_camera_page()
    {
        var info = KeyBindings.Of(KeyCommand.ShowObjects);
        Assert.Equal("showobjects", info.Id);
        Assert.Equal(CommandKind.Held, info.Kind);
        Assert.Equal([new KeyChord(KeyNames.LAlt)], info.Defaults);
        Assert.Contains(KeysPage.Groups, g => g.Commands.Contains(KeyCommand.ShowObjects));

        var keys = new KeyBindings();
        keys.Apply(IniFile.Parse("[keys]\nshowobjects = V, Mouse 4\n"));
        Assert.Equal([new KeyChord(KeyNames.Letter('V')), KeyChord.Mouse(4)], keys[KeyCommand.ShowObjects]);
        Assert.True(keys.Held(KeyCommand.ShowObjects, i => i == KeyNames.Letter('V'), KeyMods.None));
        Assert.Contains(keys.Values(), v => v.Key == "showobjects" && v.Value == "V, Mouse 4");
    }

    /// <summary>
    /// **Beside 1.1's Alt, not instead of it.** A player's own bindings have <c>alt = Left Alt, Right
    /// Alt</c>: holding Left Alt is the key held *and* the Alt flag of the click's mask, so an Alt-click
    /// is still the pick-up to the cursor or the shot at the floor. Moved elsewhere, the key sets no Alt.
    /// </summary>
    [Fact]
    public void Left_alt_is_both_the_key_and_the_alt_flag_and_the_key_moved_away_sets_no_alt()
    {
        var keys = new KeyBindings();
        keys.Apply(IniFile.Parse("[keys]\nalt = Left Alt, Right Alt\n"));
        bool LeftAlt(int i) => i == KeyNames.LAlt;
        Assert.True(keys.Held(KeyCommand.ShowObjects, LeftAlt, KeyMods.Alt));
        Assert.Equal(InputModifier.Alt, keys.Mask(LeftAlt, KeyMods.Alt));
        // Right Alt is the flag alone.
        Assert.False(keys.Held(KeyCommand.ShowObjects, i => i == KeyNames.RAlt, KeyMods.Alt));
        // The polled keyboard never has 1.1's Alt put down for it (KeyInput.State), and bound to V it sets no Alt.
        Assert.False(KeyBindings.HandsOn(KeyCommand.ShowObjects));
        Assert.True(KeyBindings.HandsOn(KeyCommand.Alt));
        keys.Apply(IniFile.Parse("[keys]\nshowobjects = V\n"));
        Assert.Equal(InputModifier.None, keys.Mask(i => i == KeyNames.Letter('V'), KeyMods.None));
    }

    [Fact]
    public void With_the_keys_switch_off_it_is_on_nothing()
    {
        Assert.Empty(KeyBindings.Original()[KeyCommand.ShowObjects]);
        Assert.Equal(KeyBindings.Of(KeyCommand.Alt).Defaults, KeyBindings.Original()[KeyCommand.Alt]);
    }
}
