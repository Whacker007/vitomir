// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The shape change itself (<see cref="ShapeShift"/>), on the
/// real templates: what a shape takes — the model, the motion set, the foley, the weapon, a player's
/// portrait head — and what it leaves alone; the four flags; a second shape refused unless forced;
/// the claws ended; each piece given back and only the pieces kept; the gates a changed shape shuts;
/// a revive; and the record through a save and the post-load step that makes the shape again.
/// The scripts that ask for all this are pinned in `Vitomir.Scripting.Tests`.
/// </summary>
public class ShapeShiftTests
{
    static (Location Place, WorldThing Christof) Monastery(GameFiles files)
    {
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        return (place, place.Player!.Thing);
    }

    [Fact]
    public void A_wolf_s_shape_takes_the_body_the_walk_the_sound_the_bite_and_the_head_and_gives_them_back()
    {
        using var files = Install.Open();
        var (place, christof) = Monastery(files);
        var me = christof.Actor!;
        var fists = me.Weapon;
        float health = me.Health, walk = me.WalkSpeed, radius = christof.Radius;
        float strength = me.Stat[(int)ActorStat.Strength];
        string model = christof.Model, motions = christof.MotionSet, foley = christof.Foley;
        string head = me.HeadModel, headMotions = me.HeadMotionSet;

        Assert.True(ShapeShift.Shift(christof, "wolf_Grey", 0));
        Assert.True(ShapeShift.IsShifted(me));
        Assert.Equal("wolf_Grey", me.ShapeChange!.Template);
        // Tags 4, 9 and 6 — resolved through `_WolfShape` — and ACTOR_WEAPON's bite in the hand.
        Assert.Equal("Wolf.nod", christof.Model, ignoreCase: true);
        Assert.Equal("wolf.nms", christof.MotionSet, ignoreCase: true);
        Assert.Equal("enemyWolf.nag", christof.Foley, ignoreCase: true);
        Assert.Equal("Bite_wolfshape", me.Weapon?.Name);
        // A player's portrait head, tags 57 and 58.
        Assert.Equal("HEADwolf.nod", me.HeadModel, ignoreCase: true);
        Assert.Equal("HEADwolf.nms", me.HeadMotionSet, ignoreCase: true);
        // And nothing else: the template's own stats, health, speeds and size are not taken.
        Assert.Equal(health, me.Health);
        Assert.Equal(walk, me.WalkSpeed);
        Assert.Equal(strength, me.Stat[(int)ActorStat.Strength]);
        Assert.Equal(radius, christof.Radius);
        Assert.Equal("Christof", christof.Template, ignoreCase: true);
        // The record keeps what it replaced.
        var kept = me.ShapeChange;
        Assert.Equal(model, kept.Model);
        Assert.Equal(motions, kept.MotionSet);
        Assert.Equal(foley, kept.Foley);
        Assert.Same(fists, kept.Weapon);
        Assert.Equal(head, kept.HeadModel);
        Assert.Equal(headMotions, kept.HeadMotionSet);
        // The wolf is drawn and animated from its own set.
        Assert.NotNull(christof.Animation);
        Assert.Equal("wolf", Path.GetFileNameWithoutExtension(christof.Animation!.Set.Name), ignoreCase: true);

        Assert.True(ShapeShift.End(christof));
        Assert.False(ShapeShift.IsShifted(me));
        Assert.Equal(model, christof.Model);
        Assert.Equal(motions, christof.MotionSet);
        Assert.Equal(foley, christof.Foley);
        Assert.Same(fists, me.Weapon);
        Assert.Equal(head, me.HeadModel);
        Assert.Equal(headMotions, me.HeadMotionSet);
        Assert.Equal(Path.GetFileNameWithoutExtension(motions), Path.GetFileNameWithoutExtension(christof.Animation!.Set.Name), ignoreCase: true);
        // Nothing on: false.
        Assert.False(ShapeShift.End(christof));
    }

    [Fact]
    public void A_second_shape_is_refused_and_a_forced_one_frees_the_first_without_undoing_it()
    {
        using var files = Install.Open();
        var (_, christof) = Monastery(files);
        var me = christof.Actor!;
        Assert.False(ShapeShift.Shift(christof, "noSuchTemplate", 0));
        Assert.Null(me.ShapeChange);
        Assert.True(ShapeShift.Shift(christof, "wolf_Grey", 0));
        Assert.False(ShapeShift.Shift(christof, "wolf_Black", 0));
        Assert.Equal("Wolf.nod", christof.Model, ignoreCase: true);

        // Forced (only the post-load step forces it): the black wolf's record keeps the grey wolf as
        // "what it had", so its end gives back the grey wolf — Christof's own body is lost.
        Assert.True(ShapeShift.Shift(christof, "wolf_Black", 0, forced: true));
        Assert.Equal("wolf_black.nod", christof.Model, ignoreCase: true);
        Assert.True(ShapeShift.End(christof));
        Assert.Equal("Wolf.nod", christof.Model, ignoreCase: true);
        Assert.Equal("Bite_wolfshape", me.Weapon?.Name);
    }

    [Fact]
    public void Weaponless_and_nofoley_take_a_monk_s_body_with_empty_hands_and_Christof_s_own_voice()
    {
        using var files = Install.Open();
        var (_, christof) = Monastery(files);
        var me = christof.Actor!;
        var fists = me.Weapon;
        string foley = christof.Foley, head = me.HeadModel;

        // Mask of 1,000 Faces' flags.
        Assert.True(ShapeShift.Shift(christof, "Civ09monk1", ShapeShift.NoFoley | ShapeShift.Weaponless));
        Assert.Equal("civ09monk1.nod", christof.Model, ignoreCase: true);
        Assert.Equal("civ09Monk.nms", christof.MotionSet, ignoreCase: true);
        Assert.Equal(foley, christof.Foley);
        Assert.Null(me.ShapeChange!.Foley);
        // The hand is empty — not the fists: no `SetWeapon`, so nothing falls back.
        Assert.Null(me.Weapon);
        Assert.Same(fists, me.ShapeChange.Weapon);
        // A monk has no ACTOR_HEADMODEL: the portrait keeps Christof's face.
        Assert.Equal(head, me.HeadModel);
        Assert.Null(me.ShapeChange.HeadModel);

        Assert.True(ShapeShift.End(christof));
        Assert.Same(fists, me.Weapon);
        Assert.Equal("christof.nod", christof.Model, ignoreCase: true);
    }

    [Fact]
    public void Nohead_keeps_the_portrait_and_noweapon_keeps_the_hand_but_not_its_picture()
    {
        using var files = Install.Open();
        var (_, christof) = Monastery(files);
        var me = christof.Actor!;
        var fists = me.Weapon;
        string head = me.HeadModel;
        Assert.True(ShapeShift.Shift(christof, "wolf_Grey", ShapeShift.NoHead | ShapeShift.NoWeapon));
        Assert.Equal(head, me.HeadModel);
        Assert.Same(fists, me.Weapon);
        Assert.Null(me.ShapeChange!.Weapon);
        // The fresh model's weapon slot is empty until a different weapon goes in.
        Assert.Null(ShapeShift.DrawnWeapon(me, me.Weapon));
        var other = Weapon.From("Claw02", christof.Place!.Templates!.Resolve("Claw02"))!;
        Assert.Same(other, ShapeShift.DrawnWeapon(me, other));
        Assert.True(ShapeShift.End(christof));
        Assert.Same(fists, ShapeShift.DrawnWeapon(me, me.Weapon));
    }

    [Fact]
    public void A_shape_ends_the_claws_first_and_the_fists_are_what_it_keeps()
    {
        using var files = Install.Open();
        var (place, christof) = Monastery(files);
        var me = christof.Actor!;
        var fists = me.Weapon;
        Assert.True(WeaponOverride.Override(christof, Weapon.From("Claw02", place.Templates!.Resolve("Claw02"))!, true, true));
        Assert.True(ShapeShift.Shift(christof, "wolf_Grey", 0));
        Assert.False(WeaponOverride.Active(me));
        Assert.Same(fists, me.ShapeChange!.Weapon);
        ShapeShift.End(christof);
        Assert.Same(fists, me.Weapon);
    }

    [Fact]
    public void Armour_stays_on_Christof_s_own_body_and_a_shield_is_not_on_a_wolf_s_arm()
    {
        using var files = Install.Open();
        var (place, christof) = Monastery(files);
        var me = christof.Actor!;
        var effects = new EffectDefinitions(files);
        me.Stat[(int)ActorStat.Strength] = 100f;
        var chain = Item.From("chainMail", place.Templates!.Resolve("chainMail"))!;
        var shield = Item.From("buckler", place.Templates.Resolve("buckler"))!;
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(christof, chain, Slot.None, effects, 1_000));
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(christof, shield, Slot.None, effects, 1_000));
        var sim = new Simulation(place);
        sim.Step(1f / 30f);
        Assert.True(christof.Shapes!.BodyHidden);
        string body = me.BodyModel, portrait = me.PortraitModel;
        Assert.NotEqual("", body);

        Assert.True(ShapeShift.Shift(christof, "wolf_Grey", 0));
        sim.Step(1f / 30f);
        // The wolf is a fresh model: nothing in its slot 0, its own body drawn, nothing on its arm.
        Assert.Equal("", me.BodyModel);
        Assert.Equal("", me.PortraitModel);
        Assert.False(christof.Shapes!.BodyHidden);
        Assert.Same(shield, me.Offhand);
        Assert.Null(ShapeShift.DrawnOffhand(me, me.Offhand));
        // And a changed shape can take nothing off or put anything on.
        Assert.False(Equipment.Unequip(christof, chain, effects));
        Assert.False(Equipment.CanUse(christof, chain));

        Assert.True(ShapeShift.End(christof));
        sim.Step(1f / 30f);
        Assert.Equal(body, me.BodyModel);
        Assert.Equal(portrait, me.PortraitModel);
        Assert.True(christof.Shapes!.BodyHidden);
        Assert.Same(shield, ShapeShift.DrawnOffhand(me, me.Offhand));
    }

    [Fact]
    public void A_changed_shape_casts_nothing()
    {
        using var files = Install.Open();
        var (place, christof) = Monastery(files);
        var powers = new DisciplineDefinitions(files);
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), powers);
        var me = christof.Actor!;
        me.Stat[(int)ActorStat.Blood] = 100f;
        var heal = DisciplineBook.SetLevel(christof, powers, "BloodHealing", 1)!;
        Assert.True(Cast.CanCast(christof, heal));
        ShapeShift.Shift(christof, "wolf_Grey", 0);
        Assert.False(Cast.CanCast(christof, heal));
        ShapeShift.End(christof);
        Assert.True(Cast.CanCast(christof, heal));
    }

    [Fact]
    public void The_claws_keep_the_beast_away_but_not_the_mask_whose_row_is_no_shape_change()
    {
        using var files = Install.Open();
        var (place, christof) = Monastery(files);
        var powers = new DisciplineDefinitions(files);
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), powers);
        var me = christof.Actor!;
        me.Stat[(int)ActorStat.Blood] = 100f;
        me.Stat[(int)ActorStat.Wits] = 30f;
        var beast = DisciplineBook.SetLevel(christof, powers, "ShapeOfTheBeast", 0)!;
        var mask = DisciplineBook.SetLevel(christof, powers, "Mask1000Faces", 0)!;
        me.Flags |= Actor.Flag.NoShapeDiscipline;                                  // what Feral Claws' script sets
        Assert.False(Cast.CanCast(christof, beast));                               // 0x18001 has 0x8000
        Assert.True(Cast.CanCast(christof, mask));                                 // 0x10018 has not
    }

    [Fact]
    public void A_corpse_revived_gets_up_in_its_own_shape()
    {
        using var files = Install.Open();
        var (_, christof) = Monastery(files);
        var me = christof.Actor!;
        ShapeShift.Shift(christof, "wolf_Grey", 0);
        me.Flags |= Actor.Flag.Dead;
        Damage.Revive(christof);
        Assert.False(ShapeShift.IsShifted(me));
        Assert.Equal("christof.nod", christof.Model, ignoreCase: true);
        // A living one keeps it: the revive undoes the shape only for the dead.
        ShapeShift.Shift(christof, "wolf_Grey", 0);
        Damage.Revive(christof);
        Assert.True(ShapeShift.IsShifted(me));
    }

    [Fact]
    public void A_wolf_that_takes_an_exit_arrives_a_wolf_and_ends_as_Christof_there()
    {
        // 1.1 moves the thing itself, record and all; Vitomir moves the actor and builds a new body.
        var files = new GameFiles(GameFiles.Locate()!);
        using var _ = files;
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        var chronicle = where.Chronicle;
        chronicle.Current = chronicle.Find(where.Entry!.Name);
        Location Build(string name, string? scene, int entrance) =>
            Location.Load(files, name, sky: false, scene: scene, player: "Christof", entrance: entrance);
        var campaign = new Campaign { Chronicle = chronicle, Build = Build, Here = Build("P1_MON1", where.Entry.SceneName, -1) };
        var christof = campaign.Here.Player!.Thing;
        string motions = christof.MotionSet;
        Assert.True(ShapeShift.Shift(christof, "wolf_Grey", 0));

        Assert.True(chronicle.TakeExit(0));
        Assert.NotNull(campaign.Travel());
        var arrived = campaign.Here.Player!.Thing;
        Assert.NotSame(christof, arrived);
        Assert.True(ShapeShift.IsShifted(arrived.Actor!));
        Assert.Equal("Wolf.nod", arrived.Model, ignoreCase: true);
        Assert.Equal("wolf.nms", arrived.MotionSet, ignoreCase: true);
        Assert.Equal("wolf", Path.GetFileNameWithoutExtension(arrived.Animation!.Set.Name), ignoreCase: true);

        Assert.True(ShapeShift.End(arrived));
        Assert.Equal("christof.nod", arrived.Model, ignoreCase: true);
        Assert.Equal(motions, arrived.MotionSet);
    }

    // ---- saves ------------------------------------------------------------------------------------

    sealed record Game(Location Place, Session Session, Chronicle Chronicle, EffectDefinitions Effects, DisciplineDefinitions Powers);

    static Game Start(GameFiles files, bool awaken = true)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        if (awaken) place.Awaken(new AiClasses(files), 1, effects, powers);
        var chronicle = Chronicle.Load(files, "Prague");
        chronicle.Current = chronicle.Find("OldTownDay");
        var session = new Session { Here = place, Chronicle = chronicle };
        place.Session = session;
        return new Game(place, session, chronicle, effects, powers);
    }

    [Fact]
    public void A_save_keeps_Christof_s_own_body_and_the_shape_s_name_and_the_load_makes_the_shape_again()
    {
        using var files = Install.Open();
        var g = Start(files);
        var christof = g.Place.Player!.Thing;
        var me = christof.Actor!;
        var swordThing = g.Place.Make("broadSword")!;
        me.Inventory.Add(swordThing.Item!);
        swordThing.Owner = christof;
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(christof, swordThing.Item!, Slot.None, g.Effects, 0));
        string model = christof.Model, foley = christof.Foley, head = me.HeadModel;
        Assert.True(ShapeShift.Shift(christof, "wolf_Grey", 0));

        // The writers ask the record first, and the name and flags go
        // at the end of the actor.
        var taken = SaveCapture.Take(g.Place, g.Session);
        var mine = taken.Party.Members[0];
        Assert.Equal(model, mine.Model);
        Assert.Equal(foley, mine.Foley);
        Assert.Equal(head, mine.Actor!.Head);
        Assert.Equal("wolf_Grey", mine.Actor.Shape?.Template);
        Assert.Equal(0u, mine.Actor.Shape!.Flags);

        string path = Path.Combine(Path.GetTempPath(), $"vitomir-shape-{Guid.NewGuid():N}.vsave");
        SaveGame save;
        try
        {
            SaveArchive.Write(taken, path);
            save = SaveArchive.Read(path);
        }
        finally { File.Delete(path); }
        var fresh = Start(files, awaken: false);
        SaveApply.Chronicle(fresh.Chronicle, save.Chronicle!);
        var world = save.World[save.Header.Location];
        var place = Location.Load(files, world.Level, things: true, sky: false,
                                  scene: world.Scene.Length > 0 ? world.Scene : null, player: "Christof", entrance: world.Entrance);
        var restore = SaveApply.Place(place, world, fresh.Effects, fresh.Powers);
        SaveApply.Party(place, save.Party, restore, fresh.Effects, fresh.Powers);

        // Made again, forced, over Christof as the load put him back — his sword in the hand.
        var back = place.Player!.Thing;
        var again = back.Actor!;
        Assert.True(ShapeShift.IsShifted(again));
        Assert.True(again.ShapeChange!.Built);
        Assert.Equal("Wolf.nod", back.Model, ignoreCase: true);
        Assert.Equal("wolf.nms", back.MotionSet, ignoreCase: true);
        Assert.Equal("Bite_wolfshape", again.Weapon?.Name);
        var sword = again.Inventory.Items.Single(i => i.Name.Equals("broadSword", StringComparison.OrdinalIgnoreCase));
        Assert.Same(sword.Weapon, again.ShapeChange.Weapon);
        Assert.Equal(model, again.ShapeChange.Model);

        Assert.True(ShapeShift.End(back));
        Assert.Equal(model, back.Model);
        Assert.Equal("Christof.nms", back.MotionSet, ignoreCase: true);
        Assert.Same(sword.Weapon, again.Weapon);
        Assert.Equal(head, again.HeadModel);
    }

    [Fact]
    public void The_post_load_step_makes_a_bare_record_into_the_shape()
    {
        using var files = Install.Open();
        var (_, christof) = Monastery(files);
        var me = christof.Actor!;
        // What the load reads back: the name and the flags, nothing kept.
        me.ShapeChange = ShapeShift.Loaded(new ShapeSave { Template = "Civ09monk1", Flags = ShapeShift.NoFoley | ShapeShift.Weaponless });
        Assert.False(me.ShapeChange!.Built);
        PostLoad.FixUp(christof);
        Assert.True(me.ShapeChange!.Built);
        Assert.Equal("civ09monk1.nod", christof.Model, ignoreCase: true);
        Assert.Null(me.Weapon);
        Assert.Null(ShapeShift.Loaded(null));
        Assert.Null(ShapeShift.Loaded(new ShapeSave()));
    }

    // ---- the hands of a changed shape (as play showed them) ------------------------------

    /// <summary>The same save and load as the test above, as the host does it: the place and the party from the file.</summary>
    static Location SaveAndLoad(GameFiles files, Game g)
    {
        var taken = SaveCapture.Take(g.Place, g.Session);
        string path = Path.Combine(Path.GetTempPath(), $"vitomir-shape-{Guid.NewGuid():N}.vsave");
        SaveGame save;
        try
        {
            SaveArchive.Write(taken, path);
            save = SaveArchive.Read(path);
        }
        finally { File.Delete(path); }
        var fresh = Start(files, awaken: false);
        SaveApply.Chronicle(fresh.Chronicle, save.Chronicle!);
        var world = save.World[save.Header.Location];
        var place = Location.Load(files, world.Level, things: true, sky: false,
                                  scene: world.Scene.Length > 0 ? world.Scene : null, player: "Christof", entrance: world.Entrance);
        var restore = SaveApply.Place(place, world, fresh.Effects, fresh.Powers);
        SaveApply.Party(place, save.Party, restore, fresh.Effects, fresh.Powers);
        return place;
    }

    static Item Torch(Location place, WorldThing christof, EffectDefinitions effects)
    {
        var body = place.Make("torchU")!;
        christof.Actor!.Inventory.Add(body.Item!);
        body.Owner = christof;
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(christof, body.Item!, Slot.None, effects, 0));
        return body.Item!;
    }

    static WorldThing Somebody(Location place, WorldThing christof) =>
        place.Things.First(t => t.IsActor && !ReferenceEquals(t, christof) && !t.Actor!.Has(Actor.Flag.Dead));

    /// <summary>
    /// The swing a wolf-shaped Christof makes: the bite's attack is
    /// `ATTACK_PUNCH` (melee type 24), which is body mode 89, `punch` — and `wolf.nms` has two of
    /// those, each with the `Strike` tag the blow waits for.
    /// </summary>
    static void BitesWithItsPunch(Location place, WorldThing wolf)
    {
        var me = wolf.Actor!;
        var bite = me.Weapon!;
        Assert.Equal("Bite_wolfshape", bite.Name);
        Assert.Equal(24, bite.Current.MeleeType);
        Assert.Equal("punch", Vitomir.Formats.NmsMotionSet.BodyModes[bite.AttackMode(0)]);
        me.BusyUntil = 0;
        Actions.Strike(wolf, Somebody(place, wolf), bite, place.Milliseconds, place.Motions);
        var swing = wolf.Animation!.Action;
        Assert.NotNull(swing);
        Assert.Equal("punch", swing!.Mode.Name);
        Assert.Contains(swing.Player.Animation.Tags, Actions.IsStrikeTag);
        Assert.Equal(ActionMode.Swing, me.Action);
    }

    [Fact]
    public void A_wolf_s_bite_plays_the_wolf_s_punch_whose_strike_tag_lands_the_blow()
    {
        using var files = Install.Open();
        var (place, christof) = Monastery(files);
        Assert.True(ShapeShift.Shift(christof, "wolf_Grey", 0));
        BitesWithItsPunch(place, christof);
    }

    [Fact]
    public void A_wolf_loaded_from_a_save_bites_with_its_punch_too()
    {
        using var files = Install.Open();
        var g = Start(files);
        Assert.True(ShapeShift.Shift(g.Place.Player!.Thing, "wolf_Grey", 0));
        var place = SaveAndLoad(files, g);
        var wolf = place.Player!.Thing;
        Assert.True(ShapeShift.IsShifted(wolf.Actor!));
        Assert.Equal("wolf", Path.GetFileNameWithoutExtension(wolf.Animation!.Set.Name), ignoreCase: true);
        BitesWithItsPunch(place, wolf);
    }

    /// <summary>
    /// **1.1's own gap, kept.** Nothing in the shape change touches the utility item (it never
    /// reads the utility item; only the equip, the unequip, the scene start and the network's gear message
    /// set it), so a torch held before Shape of the Beast is held by the wolf — and a torch up
    /// makes the swing swap the bite's `punch` (24) for `util_punch` (105). `wolf.nms` has neither that
    /// nor the fallback `swordslash` (66), so the motion lookup is asked for mode −1 and plays nothing: no
    /// swing on the screen and no `Strike` tag, which is the only way a melee blow lands (the animation's
    /// tag). The wolf stands, busy for the bite's reload, and bites nobody. Play showed exactly
    /// this: in wolf form Christof made no attack animations.
    /// </summary>
    [Fact]
    public void A_wolf_holding_a_torch_asks_for_the_util_punch_its_set_lacks_and_so_swings_nothing_as_1_1_does()
    {
        using var files = Install.Open();
        var (place, christof) = Monastery(files);
        var torch = Torch(place, christof, new EffectDefinitions(files));
        Assert.True(ShapeShift.Shift(christof, "wolf_Grey", 0));
        var me = christof.Actor!;
        Assert.Same(torch, me.Utility);
        Assert.True(Utility.Stance(me));
        var bite = me.Weapon!;
        Assert.Equal("util_punch", Vitomir.Formats.NmsMotionSet.BodyModes[bite.AttackMode(0, utility: true)]);
        var set = christof.Animation!.Set;
        Assert.Null(place.Motions!.Find(set, bite.AttackMode(0, utility: true)));
        Assert.Null(place.Motions.Find(set, Weapon.FallbackAttackMode));

        int now = place.Milliseconds;
        Actions.Strike(christof, Somebody(place, christof), bite, now, place.Motions);
        Assert.Null(christof.Animation.Action);                                    // nothing played
        Assert.Equal(ActionMode.Swing, me.Action);
        Assert.True(me.BusyUntil > now);                                           // busy for the reload all the same

        // Without the torch it bites.
        ShapeShift.End(christof);
        Assert.True(Equipment.Unequip(christof, torch, new EffectDefinitions(files)));
        Assert.True(ShapeShift.Shift(christof, "wolf_Grey", 0));
        BitesWithItsPunch(place, christof);
    }

    /// <summary>
    /// **Fix `shapetorch`** (off by default; reworked after a play-test): the
    /// torch leaves the hand but not its slot — let go and out of the world as setting no utility item does,
    /// so nothing is in the wolf's hand and it bites — and is remembered on the shape's record; the
    /// shape ended, it is back in the hand, on Christof's own finger bone, equipped all the while.
    /// A refused shape takes nothing. (1.1's own behaviour, the test above, was confirmed by a
    /// run and video: the torch at the wolf's tail, the attack order standing
    /// with no act for 2.3 s.)
    /// </summary>
    [Fact]
    public void With_the_shapetorch_fix_the_torch_leaves_the_hand_but_not_its_slot_and_comes_back_when_the_shape_ends()
    {
        using var files = Install.Open();
        var (place, christof) = Monastery(files);
        var torch = Torch(place, christof, new EffectDefinitions(files));
        var me = christof.Actor!;
        var fixes = new Fixes { ShapeTorch = true };
        int slot = torch.Location;
        Assert.NotEqual(0, slot & Slot.Worn);

        Assert.False(ShapeShift.Shift(christof, "noSuchTemplate", 0, fixes: fixes));
        Assert.Same(torch, me.Utility);

        Assert.True(ShapeShift.Shift(christof, "wolf_Grey", 0, fixes: fixes));
        Assert.Null(me.Utility);                                            // nothing in the hand
        Assert.False(Utility.Stance(me));
        Assert.False(torch.Thing!.InWorld);                                 // nothing drawn, no flame, no light
        Assert.Null(torch.Thing.AttachedTo);
        Assert.Equal(slot, torch.Location);                                 // still in its slot
        Assert.Contains(torch, me.Inventory.Items);
        Assert.Same(torch, me.ShapeChange!.Pocketed);
        BitesWithItsPunch(place, christof);

        Assert.True(ShapeShift.End(christof));
        Assert.Same(torch, me.Utility);                                     // back in the hand
        Assert.True(Utility.Stance(me));
        Assert.True(torch.Thing.InWorld);
        Assert.Same(christof, torch.Thing.AttachedTo);
        Assert.Equal(29, christof.Attachments.Single(a => ReferenceEquals(a.Child, torch.Thing)).Bone);   // his `Lfingers`, not the wolf's root
        Assert.Equal(slot, torch.Location);
        Assert.NotEqual(0, (int)torch.Flags & Equipment.EquippedBit);
    }

    /// <summary>
    /// The reason for the rework: the first version put the torch in the pack, and a **full pack**
    /// cannot take it. Now the pack is never asked — full or not, the torch leaves the hand, the
    /// pack is as it was, and the torch comes back.
    /// </summary>
    [Fact]
    public void With_the_shapetorch_fix_a_full_pack_changes_nothing()
    {
        using var files = Install.Open();
        var (place, christof) = Monastery(files);
        var torch = Torch(place, christof, new EffectDefinitions(files));
        var me = christof.Actor!;
        foreach (var template in new[] { "broadSword", "torchU" })
            for (int i = 0; i < 64; i++)
            {
                var filler = place.Make(template)!;
                if (!Stow.Place(me, filler.Item!, Slot.Inventory)) break;
                me.Inventory.Add(filler.Item!);
                filler.Owner = christof;
            }
        var (was, cell) = (torch.Location, torch.Cell);
        Assert.False(Stow.Place(me, torch, Slot.Inventory));                // the pack has no room for it
        (torch.Location, torch.Cell) = (was, cell);
        var pack = me.Inventory.Items.Where(i => i.Location == Slot.Inventory).ToList();

        Assert.True(ShapeShift.Shift(christof, "wolf_Grey", 0, fixes: new Fixes { ShapeTorch = true }));
        Assert.Null(me.Utility);
        Assert.Equal(was, torch.Location);
        Assert.Equal(pack, me.Inventory.Items.Where(i => i.Location == Slot.Inventory).ToList());
        BitesWithItsPunch(place, christof);
        Assert.True(ShapeShift.End(christof));
        Assert.Same(torch, me.Utility);
    }

    /// <summary>
    /// **A save taken in the shape.** The record writes only the shape's name and flags,
    /// so the torch is saved in its worn slot with its `BEINGWORN` bit off — which only the fix
    /// leaves. The load puts it back in the hand from its slot (the load's re-equips) and the
    /// post-load shape takes it out again, whatever the fix says in the loading run; the shape
    /// ended, it is in the hand.
    /// </summary>
    [Fact]
    public void With_the_shapetorch_fix_a_wolf_saved_and_loaded_gets_its_torch_back_when_the_shape_ends()
    {
        using var files = Install.Open();
        var g = Start(files);
        var christof = g.Place.Player!.Thing;
        Torch(g.Place, christof, g.Effects);
        Assert.True(ShapeShift.Shift(christof, "wolf_Grey", 0, fixes: new Fixes { ShapeTorch = true }));
        var place = SaveAndLoad(files, g);
        var wolf = place.Player!.Thing;
        var me = wolf.Actor!;
        Assert.True(ShapeShift.IsShifted(me));
        var torch = me.Inventory.Items.Single(i => i.Name.Equals("torchU", StringComparison.OrdinalIgnoreCase));
        Assert.Null(me.Utility);                                            // not in the wolf's hand
        Assert.False(torch.Thing!.InWorld);
        Assert.NotEqual(0, torch.Location & Slot.Worn);
        Assert.Same(torch, me.ShapeChange!.Pocketed);
        BitesWithItsPunch(place, wolf);

        Assert.True(ShapeShift.End(wolf));
        Assert.Same(torch, me.Utility);
        Assert.True(torch.Thing.InWorld);
        Assert.Same(wolf, torch.Thing.AttachedTo);
    }

    /// <summary>
    /// **Where a wolf's torch goes.** The attachment keeps a bone *number* (stored with
    /// the attachment) and the update reads that bone of whatever model the holder has now (the
    /// holder's model's bones), and the shape change leaves the attachment
    /// alone — so the torch, hung on Christof's bone 29 (his `Lfingers` tag), rides **the wolf's**
    /// bone 29, which is the middle of its tail (27, 28, 29 run back from the root). A screenshot
    /// has it hanging at the wolf's rump. The shape ended, it rides Christof's hand again.
    /// </summary>
    [Fact]
    public void The_torch_stays_in_a_wolf_s_hand_and_rides_the_wolf_s_bone_29_which_is_its_tail()
    {
        using var files = Install.Open();
        var (place, christof) = Monastery(files);
        var torch = Torch(place, christof, new EffectDefinitions(files));
        var body = torch.Thing!;
        var sim = new Simulation(place);
        int hand = christof.Animation!.Set.Tags.Single(t => t.Index == torch.Tag).Bone;
        Assert.Equal(29, hand);

        Assert.True(ShapeShift.Shift(christof, "wolf_Grey", 0));
        sim.Step(0.05f);
        var record = christof.Attachments.Single(a => ReferenceEquals(a.Child, body));
        Assert.Equal(29, record.Bone);
        var bones = christof.Animation!.Model.Bones;
        Assert.Equal([28, 27, 0], new[] { bones[29].Parent, bones[28].Parent, bones[27].Parent }.Select(b => (int)b));
        var tail = (christof.Animation.BoneMatrix(29) * Matrix4x4.CreateScale(christof.Scale) * christof.World).Translation;
        Assert.True(Vector3.Distance(tail, body.Position) < 30f, $"the torch is at {body.Position}, the tail at {tail}");

        Assert.True(ShapeShift.End(christof));
        sim.Step(0.05f);
        var fingers = (christof.Animation!.BoneMatrix(hand) * christof.World).Translation;
        Assert.True(Vector3.Distance(fingers, body.Position) < 40f, $"the torch is at {body.Position}, the hand at {fingers}");
    }

    /// <summary>
    /// **Arriving as a wolf** — by an exit or by a load — the torch is set again at the scene start,
    /// and the utility's set looks its tag up in the holder's motion set
    /// **as it is now** (the holder's current motion set): `wolf.nms` has no `tag` lines, so the torch goes on
    /// **bone 0 with no offset** — the wolf's root — and stays there after the shape ends, on
    /// Christof's root, until it is next set. 1.1's, and odd; kept.
    /// </summary>
    [Fact]
    public void A_wolf_arriving_somewhere_has_its_torch_set_again_on_the_wolf_s_set_which_puts_it_on_the_root_bone()
    {
        using var files = Install.Open();
        // By a load.
        {
            var g = Start(files);
            var christof = g.Place.Player!.Thing;
            Torch(g.Place, christof, g.Effects);
            Assert.True(ShapeShift.Shift(christof, "wolf_Grey", 0));
            var place = SaveAndLoad(files, g);
            var wolf = place.Player!.Thing;
            var torch = wolf.Actor!.Utility!;
            Assert.Equal("torchU", torch.Name, ignoreCase: true);
            var record = wolf.Attachments.Single(a => ReferenceEquals(a.Child, torch.Thing));
            Assert.Equal(0, record.Bone);
            Assert.Equal(Vector3.Zero, record.Relative.Translation);
        }
        // By an exit.
        {
            var set = ChronicleSet.Load(files);
            var where = set.ForLevel("P1_MON1")!.Value;
            var chronicle = where.Chronicle;
            chronicle.Current = chronicle.Find(where.Entry!.Name);
            Location Build(string name, string? scene, int entrance) =>
                Location.Load(files, name, sky: false, scene: scene, player: "Christof", entrance: entrance);
            var campaign = new Campaign { Chronicle = chronicle, Build = Build, Here = Build("P1_MON1", where.Entry.SceneName, -1) };
            var christof = campaign.Here.Player!.Thing;
            var torch = Torch(campaign.Here, christof, new EffectDefinitions(files));
            Assert.True(ShapeShift.Shift(christof, "wolf_Grey", 0));
            Assert.True(chronicle.TakeExit(0));
            Assert.NotNull(campaign.Travel());
            var arrived = campaign.Here.Player!.Thing;
            Assert.Same(torch, arrived.Actor!.Utility);
            Assert.Equal(0, arrived.Attachments.Single(a => ReferenceEquals(a.Child, torch.Thing)).Bone);
            Assert.True(ShapeShift.End(arrived));
            Assert.Equal(0, arrived.Attachments.Single(a => ReferenceEquals(a.Child, torch.Thing)).Bone);
        }
    }
}
