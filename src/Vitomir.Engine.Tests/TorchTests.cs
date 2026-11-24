// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The carried torch**: a utility
/// item held is its own thing in the world, attached to the hand bone its `ITEM_TAG` names in the
/// holder's motion set, and its `CARRY SPECIAL` flag (0x800) puts the holder in the `util_` motions.
/// These pin the setter, the stance's two mode pickers, the two-handed rule
/// and the party carrying it across a change of place.
/// </summary>
public class TorchTests
{
    static Location Monastery(Vfs.GameFiles files) =>
        Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");

    static (Location Place, WorldThing Christof, Item Torch, EffectDefinitions Effects) Held(Vfs.GameFiles files)
    {
        var place = Monastery(files);
        var christof = place.Player!.Thing;
        var torch = place.Make("torchU")!.Item!;
        christof.Actor!.Inventory.Add(torch);
        var effects = new EffectDefinitions(files);
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(christof, torch, Slot.None, effects, 0));
        return (place, christof, torch, effects);
    }

    [Fact]
    public void A_torch_goes_in_the_off_hand_and_rides_the_bone_its_tag_names()
    {
        using var files = Install.Open();
        var (place, christof, torch, _) = Held(files);
        var body = torch.Thing!;

        Assert.Equal(Slot.LeftWeapon, torch.Location);
        Assert.Same(torch, christof.Actor!.Utility);
        Assert.True(body.InWorld);
        Assert.Same(christof, body.AttachedTo);
        Assert.Same(christof, body.Owner);
        Assert.True((body.Flags & WorldThing.Flag.NoHighlight) != 0);       // the item's flag 0x40
        Assert.Equal(30, torch.Tag);                                        // LFINGERS

        // The bone is the one Christof's motion set puts tag 30 on, and the offset its three floats.
        var tag = christof.Animation!.Set.Tags.Single(t => t.Index == 30);
        var record = christof.Attachments.Single(a => ReferenceEquals(a.Child, body));
        Assert.Equal(tag.Bone, record.Bone);
        Assert.Equal(0u, record.Flags);

        // And it keeps up with the hand: after a step it is within a hand's reach of the bone.
        var sim = new Simulation(place);
        sim.Step(0.05f);
        var hand = (christof.Animation.BoneMatrix(tag.Bone) * christof.World).Translation;
        Assert.True(Vector3.Distance(hand, body.Position) < 40f,
                    $"the torch is at {body.Position}, the hand at {hand}, Christof at {christof.Position}");
    }

    [Fact]
    public void Taking_the_torch_off_takes_it_out_of_the_world_and_the_stance_with_it()
    {
        using var files = Install.Open();
        var (_, christof, torch, effects) = Held(files);
        Assert.True(Utility.Stance(christof.Actor));

        Assert.True(Equipment.Unequip(christof, torch, effects));
        Assert.Null(christof.Actor!.Utility);
        Assert.False(torch.Thing!.InWorld);
        Assert.Null(torch.Thing.AttachedTo);
        Assert.True((torch.Thing.Flags & WorldThing.Flag.NoHighlight) == 0);
        Assert.False(Utility.Stance(christof.Actor));
    }

    [Fact]
    public void Only_a_utility_item_that_carries_special_turns_the_stance_on()
    {
        // The torch is 0x802 and the buckler 0x2 in the smithy saves; the shipped utility items
        // all carry 0x800. The flag, not the slot, is the gate.
        var actor = new Actor();
        Assert.False(Utility.Stance(actor));
        actor.Utility = new Item { Type = ItemType.Utility, Flags = (ItemFlags)0x802 };
        Assert.True(Utility.Stance(actor));
        actor.Utility = new Item { Type = ItemType.Utility, Flags = ItemFlags.Identified };
        Assert.False(Utility.Stance(actor));
    }

    [Fact]
    public void The_utility_stance_stands_and_walks_in_the_util_motions()
    {
        using var files = Install.Open();
        var (place, christof, _, _) = Held(files);
        var walker = christof.Locomotion!;
        var set = christof.Animation!.Set;
        // No weapon, or one carried as class 0, is `util` (13); a one-handed one (1) is
        // `util_onehand` (14); a gun (5) is `util_gun` (15). The stand block is 33.
        int want = Player.ModeStand + walker.UtilityStance;
        if (place.Motions!.Find(set, want) is { } found)
        {
            walker.Reanimate();
            Assert.Equal(found.Mode.Index, christof.Animation.Current!.Mode.Index);
        }
        Assert.Contains(walker.UtilityStance, new[] { Weapon.UtilCarry, Weapon.UtilCarryOneHand, Weapon.UtilCarryGun, walker.Stance });
    }

    [Fact]
    public void A_swing_with_a_torch_up_takes_the_util_variant()
    {
        // Thirteen melee types have a `util_` swing; the rest keep their own.
        var w = new Weapon { Name = "w" };
        w.Attacks[0].MeleeType = 1;                                          // swordslash
        Assert.Equal(Weapon.AttackModeBase + 1, w.AttackMode(0));
        Assert.Equal(95, w.AttackMode(0, utility: true));                   // util_swordslash
        w.Attacks[0].MeleeType = 2;                                          // not one of the thirteen
        Assert.Equal(Weapon.AttackModeBase + 2, w.AttackMode(0, utility: true));
    }

    [Fact]
    public void A_two_handed_weapon_and_a_torch_will_not_be_held_together()
    {
        // A two-handed weapon refuses to go on beside a shield or a utility item, and a
        // shield or utility item refuses to go on beside a two-handed weapon.
        using var files = Install.Open();
        var (place, christof, torch, effects) = Held(files);
        var great = place.Make("GreatSword")!.Item!;
        Assert.True(great.TwoHanded, $"{great.Name} is not two-handed");
        // Strong enough for it (its restriction is STR 55), so that only the hands can refuse it.
        christof.Actor!.BaseStat[(int)ActorStat.Strength] = christof.Actor.Stat[(int)ActorStat.Strength] = 100f;
        christof.Actor.Inventory.Add(great);
        Assert.Equal(EquipResult.Restricted, Equipment.Equip(christof, great, Slot.None, effects, 0));

        Assert.True(Equipment.Unequip(christof, torch, effects));
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(christof, great, Slot.None, effects, 0));
        Assert.Equal(EquipResult.Restricted, Equipment.Equip(christof, torch, Slot.None, effects, 0));
    }

    [Fact]
    public void Leaving_a_place_lets_the_torch_go_and_arriving_puts_it_back_on_the_hand()
    {
        using var files = Install.Open();
        var (place, christof, torch, _) = Held(files);
        Utility.Leave(christof);
        Assert.False(torch.Thing!.InWorld);
        Assert.Null(torch.Thing.AttachedTo);
        Assert.Same(torch, christof.Actor!.Utility);                        // still held

        Utility.Rejoin(christof);
        Assert.True(torch.Thing.InWorld);
        Assert.Same(christof, torch.Thing.AttachedTo);
        Assert.Same(place, torch.Thing.Place);
    }

    [Fact]
    public void A_torch_on_the_hand_is_saved_with_the_pack_and_comes_back_on_the_hand()
    {
        // The save writes the utility item as its GUID and the load resolves it; the scene's
        // party entry puts it back on the hand. Vitomir writes the torch once, in the pack,
        // and its re-equip on the load (Equipment.Restore) hangs it on the hand again.
        using var files = Install.Open();
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        place.Awaken(new AiClasses(files), 1, effects, powers);
        var chronicle = Chronicle.Load(files, "Prague");
        chronicle.Current = chronicle.Find("OldTownDay");
        var session = new Session { Here = place, Chronicle = chronicle };
        place.Session = session;
        var christof = place.Player!.Thing;
        var torch = place.Make("torchU")!.Item!;
        christof.Actor!.Inventory.Add(torch);
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(christof, torch, Slot.None, effects, 0));

        var save = SaveCapture.Take(place, session, now: new DateTime(2026, 9, 24, 12, 0, 0));
        var world = save.World[save.Header.Location];
        Assert.DoesNotContain(world.Things.Values, t => t.Template.Equals("torchU", StringComparison.OrdinalIgnoreCase));

        var again = Location.Load(files, world.Level, things: true, sky: false,
                                  scene: world.Scene.Length > 0 ? world.Scene : null, player: "Christof", entrance: world.Entrance);
        var restore = SaveApply.Place(again, world, effects, powers);
        SaveApply.Party(again, save.Party, restore, effects, powers);
        Assert.NotNull(again.Player);
        var him = again.Player!.Thing;
        var held = him.Actor!.Utility;
        Assert.NotNull(held);
        Assert.Equal("torchU", held!.Name, ignoreCase: true);
        Assert.True(held.Thing!.InWorld);
        Assert.Same(him, held.Thing.AttachedTo);
        Assert.True(Utility.Stance(him.Actor));
        // Once on the hand, not twice: the saved attachment is not laid over the re-equip's.
        Assert.Single(him.Attachments, a => ReferenceEquals(a.Child, held.Thing));
        Assert.True(Equipment.Unequip(him, held, effects));
        Assert.DoesNotContain(him.Attachments, a => ReferenceEquals(a.Child, held.Thing));
    }
}
