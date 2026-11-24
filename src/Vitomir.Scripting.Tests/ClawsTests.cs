// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// The claws swap through the game's own scripts: Feral Claws cast from `DisciplineFeralClaws`,
/// whose effect's script (`EffectDiscFeralClaws`) calls `OverrideActorWeapon` as it starts and
/// `CancelOverrideActorWeapon` as it ends; and the Black Gloves, whose item script asks `canuse`,
/// answers `worn` by putting on an effect whose own script swaps the claws in. Nothing here is a
/// stand-in: the classes are the shipped ones.
/// </summary>
[Collection("scripts")]
public class ClawsTests(Scripts s)
{
    static Place Monastery(Scripts s)
    {
        var m = Place.Enter(s, "P1_MON1");
        var me = m.Christof.Actor!;
        me.Type = ActorType.Vampire;
        me.Stat[(int)ActorStat.BloodPool] = 100f;
        me.SetBaseStat((int)ActorStat.Blood, 100f);
        me.Stat[(int)ActorStat.Blood] = 100f;
        me.Flags2 |= Actor.Flag2.ScrollCast;                        // `Discipline.Fizzled` never fizzles a scroll
        return m;
    }

    [Fact]
    public void Feral_Claws_puts_the_claws_in_Christof_s_hands_and_gives_his_fists_back()
    {
        using var m = Monastery(s);
        var me = m.Christof.Actor!;
        var fists = me.Weapon;
        var claws = DisciplineBook.SetLevel(m.Christof, m.Powers, "FeralClaws", 0);
        Assert.NotNull(claws);
        Assert.Equal(OrderResult.Started, Cast.Order(m.Christof, null, null, claws!, InputModifier.Space));
        Assert.True(m.RunUntil(() => m.Christof.Effects.Find("ef_disc_feralclaws") is not null, 5f));

        // FERALCLAWS_TEMPLATES[0] is `Claw01`: in the hand, with the fists kept aside, and the
        // two flag bits the script and the swap set.
        Assert.Equal("Claw01", me.Weapon?.Name);
        Assert.Same(fists, me.PrevWeapon);
        Assert.True(me.Has2(Actor.Flag2.WeaponOverride));
        Assert.True(me.Has(Actor.Flag.NoShapeDiscipline));
        // And it is drawn: a claw carries SPECIFIC MODELS, so it is Christof's own pair.
        Assert.True(me.Weapon!.Attacks[0].Is(WeaponFlag.SpecificModels));
        Assert.Equal("christof_claws", HeldModel.Weapon(me.Weapon, me.SkeletonId, m.Christof.Model), ignoreCase: true);
        Assert.True(m.Files.TryRead("3d/models/christof_claws.nod", out _), "Christof's claws do not ship");

        // FERALCLAWS_DURATIONS[0] is 15 000 ms; the effect ends and its script hands the fists back.
        Assert.True(m.RunUntil(() => m.Christof.Effects.Find("ef_disc_feralclaws") is null, 20f));
        Assert.Same(fists, me.Weapon);
        Assert.Null(me.PrevWeapon);
        Assert.False(me.Has2(Actor.Flag2.WeaponOverride));
        Assert.False(me.Has(Actor.Flag.NoShapeDiscipline));
    }

    [Fact]
    public void The_override_natives_answer_as_the_engine_does()
    {
        using var m = Monastery(s);
        var me = m.Christof.Actor!;
        var self = s.Runtime!.NewThingRef("CodexActor", m.Christof.Guid);
        // A template that is no weapon is refused (type 5 or nothing).
        Assert.False(Place.Bln(Place.Call(s, "CodexActor.OverrideActorWeapon", self, "TomeCelerity", true, true)));
        Assert.False(Place.Bln(Place.Call(s, "CodexActor.OverrideActorWeapon", self, "noSuchTemplate", true, true)));
        Assert.True(Place.Bln(Place.Call(s, "CodexActor.OverrideActorWeapon", self, "Claw03", true, true)));
        Assert.Equal("Claw03", me.Weapon?.Name);
        // True again, and nothing changes: the second swap is refused inside the weapon swap.
        Assert.True(Place.Bln(Place.Call(s, "CodexActor.OverrideActorWeapon", self, "Claw05", true, true)));
        Assert.Equal("Claw03", me.Weapon?.Name);
        Assert.True(Place.Bln(Place.Call(s, "CodexActor.CancelOverrideActorWeapon", self)));
        Assert.True(Place.Bln(Place.Call(s, "CodexActor.CancelOverrideActorWeapon", self)));
        Assert.NotEqual("Claw03", me.Weapon?.Name);
        // Not a character: false.
        var prop = m.Where.Things.First(t => t.Type == ThingType.Prop);
        Assert.False(Place.Bln(Place.Call(s, "CodexActor.OverrideActorWeapon", s.Runtime.NewThingRef("CodexActor", prop.Guid), "Claw01", true, true)));
    }

    [Fact]
    public void The_black_gloves_ask_whether_they_may_go_on_and_grow_claws_while_worn()
    {
        using var m = Monastery(s);
        var me = m.Christof.Actor!;
        var fists = me.Weapon;
        var glovesThing = m.Where.Make("blackGloves")!;
        var gloves = glovesThing.Item!;
        me.Inventory.Add(gloves);
        glovesThing.Owner = m.Christof;

        // `ItemBlackGloves.canuse`: not while the wearer carries THING_AF_NOSHAPEDISC and the gloves
        // are not already on (the use test asks it first).
        me.Flags |= Actor.Flag.NoShapeDiscipline;
        Assert.False(Equipment.CanUse(m.Christof, gloves));
        me.Flags &= ~Actor.Flag.NoShapeDiscipline;
        Assert.True(Equipment.CanUse(m.Christof, gloves));

        // `worn` puts `ef_item_blackgloves` on, whose `EffectItemBlackGloves` swaps the claws in.
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(m.Christof, gloves, Slot.None, m.Effects, m.Where.Milliseconds));
        Assert.NotNull(m.Christof.Effects.Find("ef_item_blackgloves"));
        Assert.Equal("Claw", me.Weapon?.Name);
        Assert.True(me.Has(Actor.Flag.NoShapeDiscipline));
        // Worn, so its own `canuse` now says yes whatever the flag says.
        Assert.True(Equipment.CanUse(m.Christof, gloves));

        // `unworn` takes the effect off, and the effect's end gives the fists back.
        Assert.True(Equipment.Unequip(m.Christof, gloves, m.Effects));
        Assert.Null(m.Christof.Effects.Find("ef_item_blackgloves"));
        Assert.Same(fists, me.Weapon);
        Assert.False(me.Has2(Actor.Flag2.WeaponOverride));
    }
}
