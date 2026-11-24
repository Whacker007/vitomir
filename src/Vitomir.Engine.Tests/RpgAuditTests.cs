// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What a comparison with 1.1 of the `Rpg` module found and fixed in
/// the same pass: a behaviour of 1.1's that Vitomir had not built. Each test names the
/// behaviour it pins.
/// </summary>
public class RpgAuditTests
{
    static (Location Place, WorldThing Christof) Monastery()
    {
        var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var christof = place.Player!.Thing;
        christof.Actor!.Soak[(int)DamageType.Normal] = 1000f;     // every blow soaked away
        return (place, christof);
    }

    static void GiveShield(Location place, WorldThing thing) =>
        thing.Actor!.Offhand = Item.From("NormanShield", place.Templates!.Resolve("NormanShield"));

    static Harm Strike(WorldThing victim, HitKind kind, int nowMs) =>
        Damage.Apply(victim, null, kind, DamageType.Normal, 0.01f, new Rng(1), nowMs);

    [Fact]
    public void Shooting_a_citizen_dead_costs_the_shooter_humanity()
    {
        // The kill is credited to a character, a projectile's owner or a blast's
        // maker, and it is the credited one whom a THING_AF_HUMANITYLOSS death costs 5.
        var (place, christof) = Monastery();
        var me = christof.Actor!;
        me.Flags |= Actor.Flag.HumanityTrack;
        me.BaseStat[(int)ActorStat.Humanity] = me.Stat[(int)ActorStat.Humanity] = 70;
        var shot = new WorldThing
        {
            Index = 0, Guid = 90_001, Scene = new NsdThing(), Template = "arrow",
            Type = ThingType.Projectile, Place = place, SpawnedBy = christof.Guid,
        };
        var citizen = new Actor { Type = ActorType.Human, Flags = Actor.Flag.HumanityLoss };
        citizen.Health = citizen.MaxHealth = 100;
        var body = new WorldThing
        {
            Index = 0, Guid = 90_002, Scene = new NsdThing(), Template = "test", Type = ThingType.Actor,
            Actor = citizen,
        };
        Assert.True(Damage.Kill(body, shot, HitKind.Projectile, DamageType.Lethal, 1_000));
        Assert.Equal(70 - Damage.HumanityPerKill, me.Stat[(int)ActorStat.Humanity]);
        Assert.Equal(70 - Damage.HumanityPerKill, me.BaseStat[(int)ActorStat.Humanity]);
    }

    [Fact]
    public void A_summoned_pet_is_never_saved()
    {
        // The pet effect's apply marks a SUMMONED pet THING_FLAG_SAVENEVER.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var lead = place.Player!.Thing;
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var elemental = place.Place("elemental", lead.Position + new System.Numerics.Vector3(60f, 0f, 0f),
                                    lead.Orientation, lead.Sector)!;
        Assert.False(elemental.Has(WorldThing.Flag.SaveNever));
        elemental.Actor!.Flags |= Actor.Flag.Summoned;
        Assert.NotNull(EffectRuntime.Apply(elemental, "ef_disc_summonelemental", 1, durationMs: 15000, creator: lead));
        Assert.Equal(lead.Guid, elemental.Mind!.MasterGuid);
        Assert.True(elemental.Has(WorldThing.Flag.SaveNever));
    }

    [Fact]
    public void A_body_lies_empty_handed_and_a_revive_gives_it_its_weapon_back()
    {
        // The death and the death's looks hide the weapon's picture on every death; the revive shows it again.
        var actor = new Actor { Type = ActorType.Human };
        actor.Health = actor.MaxHealth = 100;
        var thing = new WorldThing
        {
            Index = 0, Guid = 1, Scene = new NsdThing(), Template = "test", Type = ThingType.Actor, Actor = actor,
        };
        Assert.False(actor.WeaponHidden);
        Assert.True(Damage.Kill(thing, null, HitKind.Melee, DamageType.Lethal, 1_000));
        Assert.True(actor.WeaponHidden);
        Damage.Revive(thing);
        Assert.False(actor.WeaponHidden);
    }

    [Fact]
    public void A_feeder_puts_its_weapon_out_of_sight_until_it_lets_go()
    {
        // The bite hides the feeder's weapon when the bite lands; the release shows it.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Steered!;
        var me = christof.Actor!;
        me.Type = ActorType.Vampire;
        me.Stat[(int)ActorStat.BloodPool] = 100f;
        var victim = minds.All.First(m => !ReferenceEquals(m.Thing, christof) && m.Thing.Actor is not null).Thing;
        victim.Actor!.Team = me.Team + 1;
        victim.Actor.Stat[(int)ActorStat.Blood] = victim.Actor.BaseStat[(int)ActorStat.Blood] = 40f;

        Feeding.Bite(christof, victim, forced: true);
        Assert.Equal(ActionMode.Feeding, me.Action);
        Assert.True(me.WeaponHidden);
        Assert.False(victim.Actor.WeaponHidden);                                  // the neck: only the feeder's
        Feeding.Stop(christof);
        Assert.False(me.WeaponHidden);
    }

    [Fact]
    public void A_throw_given_up_before_it_flies_gives_the_hands_back()
    {
        // Cancelling an attack whose weapon mode is THROWN ends the swap.
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var actor = new Actor { Type = ActorType.Vampire };
        actor.Health = actor.MaxHealth = 100;
        actor.Weapon = actor.DefaultWeapon = Weapon.From("Punch", templates.Resolve("Punch"));
        var thing = new WorldThing
        {
            Index = 0, Guid = 1, Scene = new NsdThing(), Template = "christof", Type = ThingType.Player, Actor = actor,
        };
        var fists = actor.Weapon;
        var flask = Weapon.From("holyWater", templates.Resolve("holyWater"))!;
        WeaponOverride.Override(thing, flask, clearOffhand: false, hideFists: false);
        actor.Action = ActionMode.Attack;                       // walking up to throw
        Actions.Cancel(thing);
        Assert.Equal(ActionMode.None, actor.Action);
        Assert.Same(fists, actor.Weapon);
        Assert.False(actor.Has2(Actor.Flag2.WeaponOverride));

        // Not for a weapon that is not thrown: the claws stay.
        WeaponOverride.Override(thing, Weapon.From("Claw01", templates.Resolve("Claw01"))!, clearOffhand: false, hideFists: false);
        actor.Action = ActionMode.Attack;
        Actions.Cancel(thing);
        Assert.True(actor.Has2(Actor.Flag2.WeaponOverride));
    }

    [Fact]
    public void A_frenzy_that_costs_appearance_takes_it_from_the_base()
    {
        // BaseStats[13] - 1, then the recompute. A worn bonus stays a bonus.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var christof = place.Player!.Thing;
        var a = christof.Actor!;
        a.Type = ActorType.Vampire;
        a.ClanRecord = new Clan
        {
            Name = "Gangrel", Weaknesses = Clan.WeakFrenzyAppearance, Groups = [],
            Caps = Enumerable.Repeat(Actor.NoStatLimit, Actor.Stats).ToArray(),
        };
        int app = (int)ActorStat.Appearance;
        a.SetBaseStat(app, 10f);
        a.Stat[app] = 15f;                                          // as though something worn gave 5
        for (int i = 0; i < 300 && a.BaseStat[app] == 10f; i++)
        {
            Frenzy.Begin(christof);                                 // one roll in ten costs a point
            Frenzy.End(christof);
            a.Stat[app] = a.BaseStat[app] + 5f;
            a.BusyUntil = 0;
        }
        Assert.Equal(9f, a.BaseStat[app]);
    }

    [Fact]
    public void A_burnt_body_burns_for_eight_seconds()
    {
        // The killing blow's copy of the wound switch, each effect now + 8000,
        // on a body left whole.
        var (place, christof) = Monastery();
        var a = christof.Actor!;
        a.Type = ActorType.Human;                                         // no dust
        a.Flags &= ~(Actor.Flag.AlwaysGibKill | Actor.Flag.CanGibKill);   // no gib
        int before = place.Things.Count(t => t.Template == WoundEffects.FireLight);
        Assert.True(Damage.Kill(christof, null, HitKind.Effect, DamageType.Fire, 5_000, 30f, place.Motions, new Rng(3)));
        var lights = place.Things.Where(t => t.Template == WoundEffects.FireLight).ToList();
        Assert.Equal(before + 1, lights.Count);
        Assert.Equal(5_000 + WoundEffects.DeathMs, lights.Last().ExpireAt);
        Assert.All(place.Things.Where(t => t.Template == WoundEffects.FireDamage),
                   t => Assert.Equal(5_000 + WoundEffects.DeathMs, t.ExpireAt));
    }

    [Fact]
    public void The_killing_frost_can_take_the_root_bone_too()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var christof = place.Player!.Thing;
        // A hit's frost starts at bone 1 (0x46694x); the death's at bone 0. Over enough
        // seeds the root turns up on the death's side only.
        bool rootOnDeath = false, rootOnHit = false;
        for (int seed = 1; seed < 40; seed++)
        {
            foreach (bool death in new[] { true, false })
            {
                var made = WoundEffects.Show(christof, null, DamageType.Cold, 5f, new Rng(seed), 0, death: death);
                bool root = christof.Attachments.Any(r => r.Bone == 0 && made.Contains(r.Child));
                if (death) rootOnDeath |= root; else rootOnHit |= root;
            }
        }
        Assert.True(rootOnDeath);
        Assert.False(rootOnHit);
    }

    [Fact]
    public void The_block_is_the_engines_MOTION_SHIELD()
    {
        Assert.Equal("shield", NmsMotionSet.BodyModes[Damage.ShieldMode]);
    }

    [Fact]
    public void A_melee_blow_soaked_away_on_a_shield_plays_the_block_and_nothing_else()
    {
        var (place, christof) = Monastery();
        GiveShield(place, christof);
        var actor = christof.Actor!;
        var action = actor.Action;
        var harm = Strike(christof, HitKind.Melee, 5_000);
        Assert.Equal(0f, harm.Taken);
        Assert.Equal("shield", christof.Animation!.Action!.Mode.Name);
        Assert.Equal(action, actor.Action);                                     // the action is left alone
        Assert.Equal(0, actor.BusyUntil);                                        // and nothing is busy
    }

    [Fact]
    public void No_block_for_a_shot_an_empty_off_hand_or_a_busy_character()
    {
        var (place, christof) = Monastery();
        var actor = christof.Actor!;
        Strike(christof, HitKind.Melee, 5_000);                                  // nothing in the off hand
        Assert.Null(christof.Animation!.Action);
        GiveShield(place, christof);
        Strike(christof, HitKind.Projectile, 5_000);                             // melee only
        Assert.Null(christof.Animation!.Action);
        actor.BusyUntil = 6_000;
        Strike(christof, HitKind.Melee, 5_000);                                  // busy
        Assert.Null(christof.Animation!.Action);
    }
}
