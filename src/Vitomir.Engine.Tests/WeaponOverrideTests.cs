// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The claws swap (<see cref="WeaponOverride"/>): what it puts
/// in the hands and what it keeps aside, that a second one does nothing, that the hands come back
/// whole, what a swap forbids meanwhile, that a thrown weapon ends one, and that
/// the claw is drawn — which needed the weapon's flags read in the template's own order.
/// </summary>
public class WeaponOverrideTests
{
    static (WorldThing Thing, Actor Actor) Someone(TemplateSet templates)
    {
        var actor = new Actor { Type = ActorType.Vampire };
        actor.Health = actor.MaxHealth = 100;
        actor.Weapon = actor.DefaultWeapon = Weapon.From("Punch", templates.Resolve("Punch"));
        var thing = new WorldThing
        {
            Index = 0, Guid = 1, Scene = new NsdThing(), Template = "christof", Type = ThingType.Player, Actor = actor,
        };
        return (thing, actor);
    }

    static Weapon Claw(TemplateSet templates, string name = "Claw01") => Weapon.From(name, templates.Resolve(name))!;

    [Fact]
    public void Claws_take_the_hand_and_the_shield_and_give_both_back()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var effects = new EffectDefinitions(files);
        var (thing, actor) = Someone(templates);
        var sword = Item.From("broadSword", templates.Resolve("broadSword"))!;
        var shield = Item.From("NormanShield", templates.Resolve("NormanShield"))!;
        actor.Inventory.Add(sword);
        actor.Inventory.Add(shield);
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(thing, sword, Slot.None, effects, 0));
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(thing, shield, Slot.None, effects, 0));
        Assert.Same(shield, actor.Offhand);

        var claw = Claw(templates);
        Assert.True(WeaponOverride.Override(thing, claw, clearOffhand: true, hideFists: true));
        Assert.Same(claw, actor.Weapon);
        Assert.Same(sword.Weapon, actor.PrevWeapon);
        Assert.Null(actor.Offhand);                                   // SetOffhand(null)
        Assert.Same(shield, actor.PrevOffhand);
        Assert.True(actor.Has2(Actor.Flag2.WeaponOverride));

        Assert.True(WeaponOverride.Cancel(thing));
        Assert.Same(sword.Weapon, actor.Weapon);
        Assert.Same(shield, actor.Offhand);
        Assert.Null(actor.PrevWeapon);
        Assert.Null(actor.PrevOffhand);
        Assert.False(actor.Has2(Actor.Flag2.WeaponOverride));
        // And a cancel with no swap in force is nothing (the bit is the whole guard).
        Assert.False(WeaponOverride.Cancel(thing));
    }

    [Fact]
    public void A_second_swap_while_one_is_on_changes_nothing_and_the_shield_can_be_kept()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var (thing, actor) = Someone(templates);
        var shield = Item.From("NormanShield", templates.Resolve("NormanShield"))!;
        actor.Offhand = shield;
        var fists = actor.Weapon;

        // Without bRemoveShield the shield stays and the kept shield is cleared.
        Assert.True(WeaponOverride.Override(thing, Claw(templates), clearOffhand: false, hideFists: false));
        Assert.Same(shield, actor.Offhand);
        Assert.Null(actor.PrevOffhand);
        Assert.Same(fists, actor.PrevWeapon);

        // The swap tests the bit first: a second swap neither replaces the claw nor loses the fists.
        var other = Claw(templates, "Claw05");
        Assert.False(WeaponOverride.Override(thing, other, clearOffhand: true, hideFists: true));
        Assert.NotSame(other, actor.Weapon);
        Assert.Same(fists, actor.PrevWeapon);
        Assert.Same(shield, actor.Offhand);

        WeaponOverride.Cancel(thing);
        Assert.Same(fists, actor.Weapon);
    }

    [Fact]
    public void While_the_claws_are_on_no_weapon_may_be_used_or_worn_and_the_one_put_aside_stays_on()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var effects = new EffectDefinitions(files);
        var (thing, actor) = Someone(templates);
        var sword = Item.From("broadSword", templates.Resolve("broadSword"))!;
        var dagger = Item.From("dagger", templates.Resolve("dagger"))!;
        actor.Inventory.Add(sword);
        actor.Inventory.Add(dagger);
        Equipment.Equip(thing, sword, Slot.None, effects, 0);
        WeaponOverride.Override(thing, Claw(templates), clearOffhand: true, hideFists: true);

        // A weapon while THING_AF2_WEAPONOVERRIDE is set is refused, to use and to wear.
        Assert.False(Equipment.CanUse(thing, dagger));
        Assert.Equal(EquipResult.Restricted, Equipment.Equip(thing, dagger, Slot.None, effects, 0));
        // The sword the swap put aside will not come off while it lasts.
        Assert.False(Equipment.Unequip(thing, sword, effects));
        Assert.NotEqual(Slot.Inventory, sword.Location);

        WeaponOverride.Cancel(thing);
        Assert.True(Equipment.CanUse(thing, dagger));
        Assert.True(Equipment.Unequip(thing, sword, effects));
        Assert.Same(actor.DefaultWeapon, actor.Weapon);
    }

    [Fact]
    public void A_weapon_swapped_in_and_thrown_gives_the_hands_back()
    {
        // After an attack with a thrown weapon, a swap in force is ended.
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var (thing, actor) = Someone(templates);
        var flask = Weapon.From("holyWater", templates.Resolve("holyWater"))!;
        Assert.True(flask.Current.Is(WeaponFlag.Thrown));
        var fists = actor.Weapon;
        WeaponOverride.Override(thing, flask, clearOffhand: false, hideFists: false);
        Projectiles.Thrown(thing, flask);
        Assert.Same(fists, actor.Weapon);
        Assert.False(actor.Has2(Actor.Flag2.WeaponOverride));
    }

    [Fact]
    public void The_claw_templates_are_the_six_with_specific_models()
    {
        // `_weapon` sets WEAPON_FLAGS1 0 for every weapon, and `Claw` sets WEAPON_FLAGS 0x4140 below
        // it: in the engine's order the flags win, and the first attack
        // carries SPECIFIC MODELS. Applied the other way round no template had it.
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var specific = templates.Names
            .Where(n => Weapon.From(n, templates.Resolve(n)) is { } w && w.Attacks[0].Is(WeaponFlag.SpecificModels))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(["Claw", "Claw01", "Claw02", "Claw03", "Claw04", "Claw05"], specific);
        // And the thrown flasks are thrown and ranged again on their first attack.
        Assert.Equal(WeaponFlag.Thrown | WeaponFlag.Ranged,
                     Weapon.From("holyWater", templates.Resolve("holyWater"))!.Attacks[0].Flags & (WeaponFlag.Thrown | WeaponFlag.Ranged));
    }

    [Fact]
    public void Christof_s_claws_are_drawn_in_his_hands_and_his_fists_come_back()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var monastery = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var christof = monastery.Player!.Thing;
        var carrier = monastery.Mesh.Animated.First(a => ReferenceEquals(monastery.Drawn[a.Thing], christof));
        monastery.Mesh.Rearm(carrier, christof, files, monastery.Materials);
        int before = monastery.Mesh.VertexCount;

        Assert.True(WeaponOverride.Override(christof, Claw(templates), clearOffhand: true, hideFists: true));
        Assert.True(monastery.Mesh.Rearm(carrier, christof, files, monastery.Materials));
        var shown = carrier.Carried.Single(p => p.Shown && p.Slot == HeldModel.WeaponSlot);
        Assert.Equal("christof_claws", shown.Name, ignoreCase: true);
        Assert.True(monastery.Mesh.VertexCount > before, "the claws' geometry was never appended");

        WeaponOverride.Cancel(christof);
        monastery.Mesh.Rearm(carrier, christof, files, monastery.Materials);
        Assert.DoesNotContain(carrier.Carried, p => p.Shown && p.Slot == HeldModel.WeaponSlot
                                                    && p.Name.Equals("christof_claws", StringComparison.OrdinalIgnoreCase));
    }
}
