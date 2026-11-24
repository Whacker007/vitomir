// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Engine.Scene;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The model a character holds: how the engine names it, and where it lands once the character's
/// own skeleton is posing it.
/// </summary>
public class HeldWeaponTests
{
    static Location Load(string level, string? scene = null)
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        return Location.Load(files, level, scene: scene ?? set.ForLevel(level)?.Entry?.SceneName);
    }

    static Weapon Armed(string model, uint flags = 0)
    {
        var w = new Weapon { Name = "w", Model = model };
        w.Attacks[0].Flags = flags;
        return w;
    }

    [Fact]
    public void A_weapon_is_named_for_the_body_that_holds_it()
    {
        // "%s_%d" of WEAPON_MODEL and the character's SKELETONID. The scythe ships five
        // times because the game has five skeletons that can hold one.
        Assert.Equal("scythe_0", HeldModel.Weapon(Armed("scythe"), 0, "cappadocian2.nod"));
        Assert.Equal("scythe_3", HeldModel.Weapon(Armed("scythe"), 3, "cappFemale.nod"));
    }

    [Fact]
    public void A_claw_is_named_for_the_character_that_grows_it()
    {
        // SPECIFIC MODELS (0x4000) sends the name through the specific-model lookup instead: the character's own
        // model, then the armour, then the weapon.
        var claw = Armed("claws", WeaponFlag.SpecificModels);
        Assert.Equal("christof_claws", HeldModel.Weapon(claw, 0, "christof.nod"));
        Assert.Equal("christof_plate_claws", HeldModel.Weapon(claw, 0, "christof.nod", "plate"));
    }

    [Fact]
    public void A_character_with_nothing_to_show_holds_nothing()
    {
        // 289 of the 448 armed actor templates carry a weapon with no WEAPON_MODEL — Punch, Bite,
        // enemy_Claw — and the engine clears the slot rather than looking for a file.
        Assert.Equal("", HeldModel.Weapon(null, 0, "christof.nod"));
        Assert.Equal("", HeldModel.Weapon(Armed(""), 0, "christof.nod"));
    }

    [Fact]
    public void The_name_is_cut_at_the_engine_s_buffer()
    {
        // Every write in the lookup is bounded to 0x20; nothing shipped comes near it.
        string long_ = new('a', 40);
        Assert.Equal(HeldModel.NameLength - 1,
                     HeldModel.Weapon(Armed("x", WeaponFlag.SpecificModels), 0, long_).Length);
    }

    [Fact]
    public void Every_armed_template_in_the_game_resolves_to_a_model_that_ships()
    {
        // The whole shipped set, run through the recipe: 448 actor templates name an ACTOR_WEAPON,
        // 159 of those weapons have a WEAPON_MODEL, and 158 of the 159 names are files in the
        // archive. The one that is not is a multiplayer milkmaid's `bucket_0`.
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        int armed = 0, named = 0, found = 0;
        var missing = new List<string>();
        foreach (var name in templates.Names)
        {
            var tags = templates.Resolve(name);
            var actor = Actor.From(tags);
            if (actor.WeaponTemplate.Length == 0) continue;
            armed++;
            var weapon = Weapon.From(actor.WeaponTemplate, templates.Resolve(actor.WeaponTemplate));
            string model = tags.TryGetValue(Tags.Model, out var m) ? m.AsString() : "";
            string held = HeldModel.Weapon(weapon, actor.SkeletonId, model);
            if (held.Length == 0) continue;
            named++;
            if (files.TryRead($"3d/models/{held}.nod", out _)) found++; else missing.Add($"{name}: {held}");
        }
        Assert.True(armed > 400, $"only {armed} armed templates");
        Assert.True(named > 150, $"only {named} of them name a held model");
        Assert.Equal(["MPCiv06Milkmaid: bucket_0"], missing);
        Assert.Equal(named - 1, found);
    }

    [Fact]
    public void An_armed_character_carries_its_weapon_s_geometry()
    {
        var monastery = Load("P1_MON2", "MON2_8_2");
        var carriers = monastery.Mesh.Animated.Where(a => a.Carried.Count > 0).ToList();
        Assert.NotEmpty(carriers);
        Assert.Contains(carriers, a => a.Carried[0].Name == "scythe_0");
        foreach (var a in carriers)
        {
            var part = Assert.Single(a.Carried);
            // The carried model is the same rig: as many bones as the body it rides.
            Assert.True(part.Model.Bones.Length >= a.Animation.Model.Bones.Length);
            Assert.Equal(part.Model.Vertices.Length, part.Vertices.Length);
        }
    }

    [Fact]
    public void The_weapon_is_posed_into_the_hand_and_not_left_at_the_origin()
    {
        // The test that matters, and the one a picture cannot make: the scythe's vertices have to
        // land inside the character's own box, near the hand, in world space. A model posed with
        // the wrong rest inverses, or not posed at all, lands somewhere else entirely.
        var monastery = Load("P1_MON2", "MON2_8_2");
        var carrier = monastery.Mesh.Animated.First(a => a.Carried.Count > 0);
        var body = Bounds(carrier.Vertices.Select(v => v.Position));
        var held = Bounds(carrier.Carried[0].Vertices.Select(v => v.Position));
        var centre = (held.Min + held.Max) * 0.5f;
        var grown = (Min: body.Min - new Vector3(Reach), Max: body.Max + new Vector3(Reach));
        Assert.True(centre.X >= grown.Min.X && centre.X <= grown.Max.X
                 && centre.Y >= grown.Min.Y && centre.Y <= grown.Max.Y
                 && centre.Z >= grown.Min.Z && centre.Z <= grown.Max.Z,
                    $"the held model sits at {centre}, outside the character at {body.Min}..{body.Max}");
    }

    [Fact]
    public void Changing_weapons_changes_what_is_in_the_hand()
    {
        using var files = Install.Open();
        var monastery = Location.Load(files, "P1_MON2", scene: "MON2_8_2");
        var carrier = monastery.Mesh.Animated.First(a => a.Carried.Count > 0);
        var thing = monastery.Drawn[carrier.Thing];
        var actor = thing.Actor!;
        Assert.Equal("scythe_0", Shown(carrier).Name);

        // Swap to a broadsword: the scythe goes away, the sword arrives, and the mesh has grown.
        int was = monastery.Mesh.VertexCount;
        actor.Weapon = Armed("broadSword");
        Assert.True(monastery.Mesh.Rearm(carrier, thing, files, monastery.Materials));
        Assert.Equal("broadSword_0", Shown(carrier).Name);
        Assert.Single(carrier.Carried, p => !p.Shown);
        Assert.True(monastery.Mesh.VertexCount > was, "the sword's geometry was never appended");

        // Back to the scythe: the vertices it already has are taken out again, not appended twice.
        int grown = monastery.Mesh.VertexCount;
        actor.Weapon = Armed("scythe");
        Assert.True(monastery.Mesh.Rearm(carrier, thing, files, monastery.Materials));
        Assert.Equal("scythe_0", Shown(carrier).Name);
        Assert.Equal(2, carrier.Carried.Count);
        Assert.Equal(grown, monastery.Mesh.VertexCount);

        // Nothing at all: the hand empties, and the model that is put away draws no triangle,
        // because every one of them has collapsed to a point.
        actor.Weapon = null;
        Assert.True(monastery.Mesh.Rearm(carrier, thing, files, monastery.Materials));
        Assert.DoesNotContain(carrier.Carried, p => p.Shown);
        carrier.Pose();
        Assert.All(carrier.Carried, p => Assert.All(p.Vertices, v => Assert.Equal(p.Vertices[0].Position, v.Position)));

        // And a frame in which nothing changed costs nothing.
        Assert.False(monastery.Mesh.Rearm(carrier, thing, files, monastery.Materials));
    }

    static AnimatedThing.Part Shown(AnimatedThing a, int slot = HeldModel.WeaponSlot) =>
        a.Carried.Single(p => p.Shown && p.Slot == slot);

    [Fact]
    public void Equipping_a_weapon_is_what_arms_the_character()
    {
        // The engine's equip switches on ITEM_TYPE and hands a weapon to SetWeapon,
        // which sets the weapon held. Until then ours moved an icon and nothing else, so Christof
        // fought with his fists while holding a broadsword.
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var effects = new EffectDefinitions(files);
        var (thing, actor) = Someone();
        actor.Weapon = actor.DefaultWeapon = Weapon.From("Punch", templates.Resolve("Punch"));

        var sword = Item.From("broadSword", templates.Resolve("broadSword"))!;
        Assert.NotNull(sword.Weapon);                       // an item that is a weapon carries one
        actor.Inventory.Add(sword);
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(thing, sword, Slot.None, effects, 0));
        Assert.Same(sword.Weapon, actor.Weapon);

        // And taking it off goes back to the built-in one rather than leaving him unable to fight.
        Equipment.Unequip(thing, sword, effects);
        Assert.Same(actor.DefaultWeapon, actor.Weapon);
    }

    [Fact]
    public void A_shield_is_named_for_the_body_that_carries_it()
    {
        // The shield's set, and no per-character alternative: ARMOR_MODEL then the skeleton id.
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var shield = Item.From("NormanShield", templates.Resolve("NormanShield"))!;
        Assert.Equal("normanshield", shield.ArmorModel, ignoreCase: true);
        Assert.Equal("normanshield_0", HeldModel.Offhand(shield, 0), ignoreCase: true);
        Assert.Equal("normanshield_3", HeldModel.Offhand(shield, 3), ignoreCase: true);
        Assert.Equal("", HeldModel.Offhand(null, 0));
        Assert.True(files.TryRead("3d/models/" + HeldModel.Offhand(shield, 0) + ".nod", out _),
                    "the shield's own model does not ship");
    }

    [Fact]
    public void A_shield_in_the_off_hand_is_drawn_beside_the_weapon()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var monastery = Location.Load(files, "P1_MON2", scene: "MON2_8_2");
        var carrier = monastery.Mesh.Animated.First(a => a.Carried.Count > 0);
        var thing = monastery.Drawn[carrier.Thing];

        thing.Actor!.Offhand = Item.From("NormanShield", templates.Resolve("NormanShield"));
        Assert.True(monastery.Mesh.Rearm(carrier, thing, files, monastery.Materials));
        // Both hands full, one model each, and they are different models in different slots.
        Assert.Equal("scythe_0", Shown(carrier).Name);
        Assert.Equal("normanshield_0", Shown(carrier, HeldModel.OffhandSlot).Name, ignoreCase: true);
    }

    static (WorldThing Thing, Actor Actor) Someone()
    {
        var actor = new Actor { Type = ActorType.Human };
        actor.Health = actor.MaxHealth = 100;
        var thing = new WorldThing
        {
            Index = 0, Guid = 1, Scene = new Formats.NsdThing(), Template = "christof",
            Type = ThingType.Actor, Actor = actor,
        };
        return (thing, actor);
    }

    /// <summary>How far outside the body a two-handed weapon may legitimately stick out.</summary>
    const float Reach = 60f;

    static (Vector3 Min, Vector3 Max) Bounds(IEnumerable<Vector3> points)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in points) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        return (min, max);
    }
}
