// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Wearing things, and what wearing them does to the numbers.
///
/// The interesting half of this file is not the unit tests but the ones that rebuild **Christof's
/// actual character sheet** out of the shipped templates and `misc/effects.ned`. A play to a
/// known state gave a screenshot of the sheet and the saves behind it, and every number in them is
/// now derivable: rags and leathergloves give bash 5, lethal 15, aggravated 10; a `ringMAN` takes
/// Manipulation from 25 to 30. If any link of the chain — the tag ids, the level-into-value-row
/// rule, the compose mode, the base/current split — is wrong, those numbers come out wrong.
///
///
/// </summary>
public class EquipmentTests
{
    /// <summary>
    /// A player-type character (`THING_TYPE_PLAYER`) — the only kind an item's restrictions bind:
    /// the wear goes through the use test, which lets anybody else wear anything.
    /// </summary>
    static (WorldThing Thing, Actor Actor) Someone(ActorType type = ActorType.Human)
    {
        var actor = new Actor { Type = type, IsPlayerThing = true };
        actor.Health = actor.MaxHealth = 100;
        var thing = new WorldThing
        {
            Index = 0, Guid = 1, Scene = new NsdThing(), Template = "christof",
            Type = ThingType.Player, Actor = actor,
        };
        return (thing, actor);
    }

    static Item Wearable(string name, ItemType type, ItemFlags flags = ItemFlags.Identified,
                         IReadOnlyList<ItemEffect>? effects = null,
                         IReadOnlyList<ItemRestriction>? restrictions = null) =>
        new()
        {
            Name = name, Type = type, Flags = flags,
            Effects = effects ?? [], Restrictions = restrictions ?? [],
            Location = Slot.Inventory,
        };

    // ---- the slot mask ------------------------------------------------------------------

    [Fact]
    public void Each_item_type_goes_where_the_paperdoll_says()
    {
        Assert.Equal(Slot.Head, Item.SlotsFor(ItemType.Headgear));
        Assert.Equal(Slot.Neck, Item.SlotsFor(ItemType.Neckwear));
        Assert.Equal(Slot.Body, Item.SlotsFor(ItemType.Armor));
        Assert.Equal(Slot.RightWeapon, Item.SlotsFor(ItemType.Weapon));
        Assert.Equal(Slot.LeftWeapon, Item.SlotsFor(ItemType.Shield));
        Assert.Equal(Slot.LeftWeapon, Item.SlotsFor(ItemType.Utility));
        Assert.Equal(Slot.RightArm | Slot.LeftArm, Item.SlotsFor(ItemType.Handwear));
        Assert.Equal(Slot.RightHand | Slot.LeftHand, Item.SlotsFor(ItemType.Rings));
        // Everything else is carried, never worn.
        Assert.Equal(Slot.None, Item.SlotsFor(ItemType.Ammo));
        Assert.Equal(Slot.None, Item.SlotsFor(ItemType.Scroll));
        Assert.Equal(Slot.None, Item.SlotsFor(ItemType.General));
    }

    [Fact]
    public void The_nine_worn_bits_are_consecutive_and_below_the_containers()
    {
        Assert.Equal(0x1FF, Slot.Worn);
        Assert.True(Slot.Worn < Slot.QuickUse);
        Assert.Equal(0, Slot.Worn & Slot.Containers);
    }

    [Fact]
    public void Container_sizes_are_the_ones_the_placement_code_and_the_ui_agree_on()
    {
        Assert.Equal((5, 1), Slot.Size(Slot.QuickUse));
        Assert.Equal((10, 4), Slot.Size(Slot.Inventory));
        Assert.Equal((10, 9), Slot.Size(Slot.Vault));
    }

    [Fact]
    public void A_hat_will_not_go_in_a_weapon_slot()
    {
        var (thing, _) = Someone();
        var hat = Wearable("skullcap", ItemType.Headgear);
        thing.Actor!.Inventory.Add(hat);
        Assert.Equal(EquipResult.WrongSlot,
                     Equipment.Equip(thing, hat, Slot.RightWeapon, Effects(), 0));
    }

    // ---- pairs --------------------------------------------------------------------------

    [Fact]
    public void Two_rings_take_the_two_hands_in_order()
    {
        var (thing, actor) = Someone();
        var first = Wearable("ringMAN", ItemType.Rings);
        var second = Wearable("ringAPP", ItemType.Rings);
        actor.Inventory.Add(first);
        actor.Inventory.Add(second);

        Assert.Equal(EquipResult.Equipped, Equipment.Equip(thing, first, Slot.None, Effects(), 0));
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(thing, second, Slot.None, Effects(), 0));
        Assert.Equal(Slot.RightHand, first.Location);
        Assert.Equal(Slot.LeftHand, second.Location);
    }

    [Fact]
    public void Gloves_hold_both_wrists_so_a_bracelet_has_nowhere_to_go()
    {
        var (thing, actor) = Someone();
        var gloves = Wearable("leathergloves", ItemType.Handwear,
                              ItemFlags.Identified | ItemFlags.TwoHanded);
        var bracelet = Wearable("silverBracelet", ItemType.Handwear);
        actor.Inventory.Add(gloves);
        actor.Inventory.Add(bracelet);

        Equipment.Equip(thing, gloves, Slot.None, Effects(), 0);
        Assert.Equal(Slot.RightArm, gloves.Location);

        // The original refuses this: a two-handed glove holds the
        // pair, so putting the bracelet on takes the gloves off rather than sharing.
        Equipment.Equip(thing, bracelet, Slot.LeftArm, Effects(), 0);
        Assert.Equal(Slot.Inventory, gloves.Location);
        Assert.Equal(Slot.LeftArm, bracelet.Location);
    }

    // ---- restrictions -------------------------------------------------------------------

    [Fact]
    public void A_nohuman_item_refuses_a_mortal_and_allows_a_vampire()
    {
        var blocked = new[] { new ItemRestriction(RestrictionType.NoHuman, 0f) };
        var (mortal, mortalActor) = Someone(ActorType.Human);
        var ring = Wearable("ringDiscipline", ItemType.Rings, restrictions: blocked);
        mortalActor.Inventory.Add(ring);
        Assert.Equal(EquipResult.Restricted, Equipment.Equip(mortal, ring, Slot.None, Effects(), 0));

        var (kindred, kindredActor) = Someone(ActorType.Vampire);
        var same = Wearable("ringDiscipline", ItemType.Rings, restrictions: blocked);
        kindredActor.Inventory.Add(same);
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(kindred, same, Slot.None, Effects(), 0));
    }

    [Fact]
    public void A_faith_item_wants_a_human_specifically_which_shuts_out_ghouls_too()
    {
        var human = new[] { new ItemRestriction(RestrictionType.Human, 0f) };
        foreach (var kind in new[] { ActorType.Vampire, ActorType.Ghoul })
        {
            var (thing, actor) = Someone(kind);
            var cross = Wearable("holyCross", ItemType.Neckwear, restrictions: human);
            actor.Inventory.Add(cross);
            Assert.Equal(EquipResult.Restricted, Equipment.Equip(thing, cross, Slot.None, Effects(), 0));
        }
    }

    [Fact]
    public void A_stat_restriction_is_honoured_even_though_nothing_shipped_uses_one()
    {
        var (thing, actor) = Someone();
        actor.BaseStat[(int)ActorStat.Strength] = 20f;
        actor.Stat[(int)ActorStat.Strength] = 20f;
        var heavy = Wearable("plateMail", ItemType.Armor,
                             restrictions: [new ItemRestriction(RestrictionType.Strength, 40f)]);
        actor.Inventory.Add(heavy);
        Assert.Equal(EquipResult.Restricted, Equipment.Equip(thing, heavy, Slot.None, Effects(), 0));

        actor.BaseStat[(int)ActorStat.Strength] = actor.Stat[(int)ActorStat.Strength] = 40f;
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(thing, heavy, Slot.None, Effects(), 0));
    }

    /// <summary>
    /// GENERATION is compared the other way up from every other stat (a comparison
    /// with 1.1): a generation above the value is refused, so the value is the highest generation — the
    /// weakest blood — that may use the item, and equal passes.
    /// </summary>
    [Fact]
    public void A_generation_restriction_is_a_maximum_not_a_minimum()
    {
        var (thing, actor) = Someone();
        var elder = Wearable("elderRing", ItemType.Rings,
                             restrictions: [new ItemRestriction(RestrictionType.Generation, 10f)]);
        actor.Inventory.Add(elder);
        actor.BaseStat[(int)ActorStat.Generation] = actor.Stat[(int)ActorStat.Generation] = 13f;
        Assert.Equal(EquipResult.Restricted, Equipment.Equip(thing, elder, Slot.None, Effects(), 0));
        actor.BaseStat[(int)ActorStat.Generation] = actor.Stat[(int)ActorStat.Generation] = 10f;
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(thing, elder, Slot.None, Effects(), 0));
    }

    // ---- Christof's own sheet, rebuilt out of the shipped data ---------------------------

    /// <summary>An item as the game actually ships it, tags and all.</summary>
    static Item Shipped(TemplateSet templates, string name)
    {
        var item = Item.From(name, templates.Resolve(name));
        Assert.SkipWhen(item is null, $"no template called {name} in this install");
        item!.Location = Slot.Inventory;
        return item;
    }

    [Fact]
    public void Rags_and_leathergloves_give_Christof_the_soak_his_sheet_shows()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var effects = new EffectDefinitions(files);
        var (thing, actor) = Someone();

        var rags = Shipped(templates, "rags");
        var gloves = Shipped(templates, "leathergloves");
        actor.Inventory.Add(rags);
        actor.Inventory.Add(gloves);

        // Both routes through the effect chain, in one character: the rags set an explicit
        // ITEM_EFFECTVALUE of 5, the gloves set ITEM_EFFECTLEVEL 0 and take the first of the
        // effect's six values, which is 10.
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(thing, rags, Slot.None, effects, 0));
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(thing, gloves, Slot.None, effects, 0));

        Assert.Equal(Slot.Body, rags.Location);
        Assert.Equal(Slot.RightArm, gloves.Location);

        // The numbers off that character sheet, to the point.
        Assert.Equal(5f, actor.Soak[(int)DamageType.Normal]);
        Assert.Equal(15f, actor.Soak[(int)DamageType.Lethal]);
        Assert.Equal(10f, actor.Soak[(int)DamageType.Aggravated]);

        // And the base row never moved, which is what the sheet's left column is.
        Assert.Equal(0f, actor.BaseSoak[(int)DamageType.Normal]);
        Assert.Equal(0f, actor.BaseSoak[(int)DamageType.Lethal]);
        Assert.Equal(0f, actor.BaseSoak[(int)DamageType.Aggravated]);
    }

    [Fact]
    public void Taking_the_gloves_off_gives_the_soak_back()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var effects = new EffectDefinitions(files);
        var (thing, actor) = Someone();
        var gloves = Shipped(templates, "leathergloves");
        actor.Inventory.Add(gloves);

        Equipment.Equip(thing, gloves, Slot.None, effects, 0);
        Assert.Equal(10f, actor.Soak[(int)DamageType.Lethal]);

        Equipment.Unequip(thing, gloves, effects);
        Assert.Equal(Slot.Inventory, gloves.Location);
        Assert.Equal(0f, actor.Soak[(int)DamageType.Lethal]);
    }

    [Fact]
    public void A_ring_of_manipulation_moves_the_stat_the_shop_reads()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var effects = new EffectDefinitions(files);
        var (thing, actor) = Someone();
        actor.BaseStat[(int)ActorStat.Manipulation] = 25f;
        Equipment.Refresh(thing);
        Assert.Equal(25f, actor.Stat[(int)ActorStat.Manipulation]);

        var ring = Shipped(templates, "ringMAN");
        actor.Inventory.Add(ring);
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(thing, ring, Slot.None, effects, 0));

        // 25 -> 30, which is the number that made his next purchase cheaper.
        Assert.Equal(30f, actor.Stat[(int)ActorStat.Manipulation]);
        Assert.Equal(25f, actor.BaseStat[(int)ActorStat.Manipulation]);
    }

    [Fact]
    public void The_jewellery_ladder_is_priced_by_its_effect_level()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var effects = new EffectDefinitions(files);

        // Four rings, one effect, four levels, four prices. This is what settles that the level is
        // counted from zero: read one-based, the first two would be identical and cost twice as
        // much for nothing. The effect's add indexes the array directly.
        foreach (var (name, cost, appearance) in new[]
        {
            ("ringOnyx", 25, 5f), ("ringSilver", 50, 10f),
            ("ringGold", 100, 15f), ("ringDiamond", 500, 20f),
        })
        {
            var (thing, actor) = Someone();
            var ring = Shipped(templates, name);
            actor.Inventory.Add(ring);
            Assert.Equal(cost, ring.BaseCost);
            Equipment.Equip(thing, ring, Slot.None, effects, 0);
            Assert.Equal(appearance, actor.Stat[(int)ActorStat.Appearance]);
        }
    }

    [Fact]
    public void Two_rings_stack_because_their_effect_composes_by_adding()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var effects = new EffectDefinitions(files);
        var (thing, actor) = Someone();
        actor.BaseStat[(int)ActorStat.Appearance] = 30f;

        var first = Shipped(templates, "ringGold");        // ef_stat_appearance
        var second = Shipped(templates, "ringSilver");     // the same effect again
        actor.Inventory.Add(first);
        actor.Inventory.Add(second);
        Equipment.Equip(thing, first, Slot.None, effects, 0);
        Equipment.Equip(thing, second, Slot.None, effects, 0);

        Assert.Equal(Slot.RightHand, first.Location);
        Assert.Equal(Slot.LeftHand, second.Location);
        // ringGold is level 2 (+15) and ringSilver level 1 (+10), and `ef_stat_appearance` composes
        // by adding, so both count.
        Assert.Equal(55f, actor.Stat[(int)ActorStat.Appearance]);
    }

    [Fact]
    public void The_shipped_templates_carry_the_tags_this_reads()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);

        // The six items in that inventory, against the costs and flags its saves record.
        foreach (var (name, cost, flags) in new (string, int, ItemFlags)[]
        {
            ("BroadSword", 50, ItemFlags.Identified),
            ("rags", 5, ItemFlags.Identified),
            ("Buckler", 30, ItemFlags.Identified),
            ("torchU", 5, ItemFlags.Identified | ItemFlags.CarrySpecial),
            ("holyWater", 100, ItemFlags.DestroyedAtZeroCharges | ItemFlags.Identified),
            ("cuthbertLetter", 50,
             ItemFlags.DestroyedAtZeroCharges | ItemFlags.Identified | ItemFlags.NoSell),
        })
        {
            var item = Item.From(name, templates.Resolve(name));
            Assert.SkipWhen(item is null, $"no template called {name}");
            Assert.Equal(cost, item!.BaseCost);
            Assert.Equal(flags, item.Flags);
        }

        // Every glove holds both wrists; no bracelet does. That asymmetry is what `0x40` is for.
        foreach (var glove in new[] { "leathergloves", "gauntlets", "gloves_cold" })
            Assert.True(Item.From(glove, templates.Resolve(glove))!.TwoHanded, glove);
        foreach (var bracelet in new[] { "silverBracelet", "goldBracelet", "diamondBracelet" })
            Assert.False(Item.From(bracelet, templates.Resolve(bracelet))!.TwoHanded, bracelet);
    }

    [Fact]
    public void The_shipped_restrictions_gate_on_creature_type_and_never_on_a_stat()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);

        var discipline = Item.From("ringDiscipline", templates.Resolve("ringDiscipline"));
        Assert.SkipWhen(discipline is null, "no ringDiscipline in this install");
        Assert.Contains(discipline!.Restrictions, r => r.Type == RestrictionType.NoHuman);

        var cross = Item.From("holyCross", templates.Resolve("holyCross"))!;
        Assert.Contains(cross.Restrictions, r => r.Type == RestrictionType.Human);

        // A plain stat ring is open to everybody, as in play.
        Assert.Empty(Item.From("ringOnyx", templates.Resolve("ringOnyx"))!.Restrictions);
    }

    static EffectDefinitions Effects() => new(Install.Open());
}
