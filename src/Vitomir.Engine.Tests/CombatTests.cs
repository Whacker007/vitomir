// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The blow: the weapon a character swings, and the four rolls that decide what it does.
/// Every number asserted here is the original game's.
/// </summary>
public class CombatTests
{
    static WorldThing Character(Actor actor) => new()
    {
        Index = 0,
        Guid = 1,
        Scene = new NsdThing(),
        Template = "test",
        Type = ThingType.Actor,
        Actor = actor,
    };

    static Actor Fighter(float strength = 0, float dexterity = 0, float stamina = 0,
                         ActorType type = ActorType.Vampire)
    {
        var a = new Actor();
        a.Stat[(int)ActorStat.Strength] = strength;
        a.Stat[(int)ActorStat.Dexterity] = dexterity;
        a.Stat[(int)ActorStat.Stamina] = stamina;
        a.Type = type;
        return a;
    }

    [Fact]
    public void A_weapons_melee_type_names_its_own_attack()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var modes = NmsMotionSet.BodyModes;

        // The attack block of the engine's mode table, `claw` through `chaingun`.
        int first = Array.IndexOf(modes, "claw"), last = Array.IndexOf(modes, "chaingun");
        Assert.Equal(Weapon.AttackModeBase, first);

        var named = new Dictionary<string, string>();
        int armed = 0;
        foreach (var name in templates.Names)
        {
            var w = Weapon.From(name, templates.Resolve(name));
            if (w is null) continue;
            for (int i = 0; i < w.AttackCount; i++)
            {
                int mode = w.AttackMode(i);
                // Nothing a weapon asks to swing falls outside the attack block.
                Assert.InRange(mode, first, last);
                if (i == 0) named[name] = modes[mode];
                armed++;
            }
        }
        Assert.True(armed > 275, $"{armed} attack records across 275 weapons");

        // A spread of weapons whose animation everyone knows by looking at it.
        Assert.Equal("polethrust", named["Halberd"]);
        Assert.Equal("polethrust", named["staff_Virstania"]);
        Assert.Equal("crossbow", named["crossBow"]);
        Assert.Equal("bow", named["longBow"]);
        Assert.Equal("pistol", named["gun1hand"]);
        Assert.Equal("pistol", named["taser"]);
        Assert.Equal("shotgun", named["gun2hand"]);
        Assert.Equal("chainsaw", named["chainsaw"]);
        Assert.Equal("flamethrower", named["flamethrower"]);
        Assert.Equal("chaingun", named["chaingun"]);
        Assert.Equal("swing", named["baseballBat"]);
        Assert.Equal("stake", named["Stake"]);
        Assert.Equal("bluntbash2", named["WarHammer"]);
        Assert.Equal("swordslash2", named["BastardSword"]);
        Assert.Equal("claw", named["Claw"]);
        Assert.Equal("punch", named["Punch"]);

        // The second record is a *different* swing where a template sets one: the Vozhd's claw
        // opens with `claw` and follows with `clawspecial`, and the szlachta's does the reverse.
        var vozhd = Weapon.From("Claw_vozhd", templates.Resolve("Claw_vozhd"))!;
        Assert.Equal(2, vozhd.AttackCount);
        Assert.Equal("claw", modes[vozhd.AttackMode(0)]);
        Assert.Equal("clawspecial", modes[vozhd.AttackMode(1)]);
        var szlachta = Weapon.From("Claw_szlachta", templates.Resolve("Claw_szlachta"))!;
        Assert.Equal("punch", modes[szlachta.AttackMode(0)]);
        Assert.Equal("claw", modes[szlachta.AttackMode(1)]);
    }

    [Fact]
    public void How_a_weapon_is_carried_is_not_how_it_is_swung()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var modes = NmsMotionSet.BodyModes;
        var halberd = Weapon.From("Halberd", templates.Resolve("Halberd"))!;

        // WEAPON_TYPE1 is the carry class, and it is added to whichever locomotion block the
        // character is in. Four independent blocks all agree on the halberd's 4.
        Assert.Equal(4, halberd.Carry);
        Assert.Equal("walk_polearm", modes[halberd.CarryMode(Weapon.WalkBase)]);
        Assert.Equal("run_polearm", modes[halberd.CarryMode(Weapon.RunBase)]);
        Assert.Equal("stand_polearm", modes[halberd.CarryMode(Weapon.StandBase)]);
        Assert.Equal("limp_polearm", modes[halberd.CarryMode(Weapon.LimpBase)]);

        // Reading it as an attack mode instead gives the wrong swing entirely, which is the mistake
        // this pins shut.
        Assert.NotEqual("polethrust", modes[Weapon.AttackModeBase + halberd.Carry]);

        // The utility stance swaps thirteen attacks and collapses the carry classes to three.
        Assert.Equal("util_punch", modes[Weapon.UtilAttackModes[24]]);
        Assert.Equal("util_pistol", modes[Weapon.UtilAttackModes[12]]);
        Assert.Equal("walk_util_gun", modes[Weapon.WalkBase + Weapon.UtilCarryGun]);
    }

    [Fact]
    public void Every_weapon_in_the_game_reads()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        int weapons = 0, twoAttacks = 0, ranged = 0, behead = 0, stake = 0, silver = 0, noStrength = 0;
        var noStrengthNames = new List<string>();
        foreach (var name in templates.Names)
        {
            var w = Weapon.From(name, templates.Resolve(name));
            if (w is null) continue;
            weapons++;
            if (w.AttackCount == 2) twoAttacks++;
            for (int i = 0; i < w.AttackCount; i++)
            {
                var at = w.Attacks[i];
                if (at.Is(WeaponFlag.Ranged)) ranged++;
                if (at.Is(WeaponFlag.CanBehead)) behead++;
                if (at.Is(WeaponFlag.CanStake)) stake++;
                if (at.Is(WeaponFlag.Silver)) silver++;
                if (at.Is(WeaponFlag.NoStrength)) { noStrength++; noStrengthNames.Add(name); }
                // The engine never writes a negative reach or a negative reload.
                Assert.True(at.MaxDistance >= 0, $"{name} attack {i} reaches {at.MaxDistance}");
                Assert.True(at.ReloadTime >= 0, $"{name} attack {i} reloads in {at.ReloadTime}");
            }
        }
        Assert.Equal(275, weapons);
        Assert.True(twoAttacks > 0 && twoAttacks < weapons, $"{twoAttacks} of {weapons} have two attacks");

        // Only the taser skips the attacker's Strength, so in 1.1 a gun's damage scales with it.
        Assert.Equal(["taser", "weap_taser"], noStrengthNames.Order().ToArray());
        Assert.True(ranged > 0 && behead > 0 && stake > 0 && silver > 0,
                    $"ranged {ranged}, behead {behead}, stake {stake}, silver {silver}");
    }

    [Fact]
    public void A_second_attack_tag_is_what_gives_a_weapon_two_attacks()
    {
        var tags = new Dictionary<uint, NodTag>
        {
            [Tags.Type] = NodTag.Of(Tags.Type, (uint)ThingType.Weapon),
            [Tags.WeaponFirst.Damage] = NodTag.Of(Tags.WeaponFirst.Damage, 30u),
        };
        Assert.Equal(1, Weapon.From("one", tags)!.AttackCount);

        tags[Tags.WeaponSecond.Damage] = NodTag.Of(Tags.WeaponSecond.Damage, 40u);
        var two = Weapon.From("two", tags)!;
        Assert.Equal(2, two.AttackCount);
        Assert.Equal(30, two.Attacks[0].Damage);
        Assert.Equal(40, two.Attacks[1].Damage);

        // Asking for an attack the weapon has not got is ignored, as the engine ignores it.
        two.Select(5);
        Assert.Equal(0, two.CurrentAttack);
        two.Select(1);
        Assert.Equal(1, two.CurrentAttack);
    }

    [Fact]
    public void WEAPON_FLAGS_makes_the_second_attack_the_special_one()
    {
        var tags = new Dictionary<uint, NodTag>
        {
            [Tags.Type] = NodTag.Of(Tags.Type, (uint)ThingType.Weapon),
            [Tags.WeaponFlags] = NodTag.Of(Tags.WeaponFlags, WeaponFlag.SecondAttackIsSpecial),
            [Tags.WeaponSecond.Damage] = NodTag.Of(Tags.WeaponSecond.Damage, 10u),
        };
        var w = Weapon.From("w", tags)!;
        Assert.False(w.Attacks[0].Is(WeaponFlag.SpecialAnimation));
        Assert.True(w.Attacks[1].Is(WeaponFlag.SpecialAnimation));
    }

    [Fact]
    public void A_moving_attacker_has_to_close_to_the_midpoint_but_a_shot_does_not()
    {
        var melee = new WeaponAttack { MinDistance = 20f, MaxDistance = 70f };
        Assert.Equal(70f, melee.Reach(attackerMoving: false));
        Assert.Equal(45f, melee.Reach(attackerMoving: true));

        var gun = new WeaponAttack { MinDistance = 20f, MaxDistance = 700f, Flags = WeaponFlag.Ranged };
        Assert.Equal(700f, gun.Reach(attackerMoving: true));
    }

    [Fact]
    public void The_damage_roll_is_a_die_per_ten_points_plus_the_remainder()
    {
        // 30 weapon damage + 25 Strength = a pool of 55: five dice and half a die of remainder.
        var attacker = Character(Fighter(strength: 25));
        var attack = new WeaponAttack { Damage = 30 };

        // Same seed, same answer — and the answer has to sit inside what the dice can produce.
        float a = Combat.RollDamage(attacker, attack, new Rng(7));
        float b = Combat.RollDamage(attacker, attack, new Rng(7));
        Assert.Equal(a, b);
        Assert.InRange(a, 5 * Combat.DieFloor, 5 * (Combat.DieFloor + Combat.DieSpan) + 5f);

        // With the flag set the pool is the weapon alone: three dice, no remainder.
        var taser = new WeaponAttack { Damage = 30, Flags = WeaponFlag.NoStrength };
        float c = Combat.RollDamage(attacker, taser, new Rng(7));
        Assert.InRange(c, 3 * Combat.DieFloor, 3 * (Combat.DieFloor + Combat.DieSpan));

        // A pool under ten is all remainder, so it can roll arbitrarily close to nothing.
        var scratch = new WeaponAttack { Damage = 5 };
        float d = Combat.RollDamage(Character(Fighter()), scratch, new Rng(1));
        Assert.InRange(d, 0f, 5f);
    }

    [Fact]
    public void A_human_gets_no_stamina_against_lethal_damage()
    {
        var rng = () => new Rng(3);
        var vampire = Character(Fighter(stamina: 40, type: ActorType.Vampire));
        var human = Character(Fighter(stamina: 40, type: ActorType.Human));
        vampire.Actor!.Soak[(int)DamageType.Lethal] = 10;
        human.Actor!.Soak[(int)DamageType.Lethal] = 10;

        float v = Combat.Soak(vampire, DamageType.Lethal, rng());
        float h = Combat.Soak(human, DamageType.Lethal, rng());
        Assert.True(v > h, $"vampire soaked {v}, human {h}");

        // Against NORMAL both get their Stamina, so the two come out the same.
        Assert.Equal(Combat.Soak(vampire, DamageType.Normal, rng()),
                     Combat.Soak(human, DamageType.Normal, rng()));
    }

    [Fact]
    public void Elemental_damage_is_soaked_at_half_and_poison_ignores_fortitude()
    {
        var target = Character(Fighter());
        target.Actor!.Soak[(int)DamageType.Fire] = 40;
        target.Actor!.Soak[(int)DamageType.Poison] = 40;

        // The roll is uniform over 0..soak, so the halving shows up as a halved ceiling. One
        // generator drawn many times: seeding a fresh one per sample only walks the LCG's first
        // output, which for small consecutive seeds barely moves.
        var rng = new Rng(1);
        float worstFire = 0, worstPoison = 0;
        for (int i = 0; i < 2000; i++)
        {
            worstFire = MathF.Max(worstFire, Combat.Soak(target, DamageType.Fire, rng));
            worstPoison = MathF.Max(worstPoison, Combat.Soak(target, DamageType.Poison, rng));
        }
        Assert.InRange(worstFire, 19f, 20f);
        Assert.InRange(worstPoison, 39f, 40f);
    }

    [Fact]
    public void Nothing_is_soaked_when_there_is_nothing_to_soak_with()
    {
        var bare = Character(Fighter());
        foreach (DamageType t in Enum.GetValues<DamageType>())
            Assert.Equal(0f, Combat.Soak(bare, t, new Rng(11)));
    }

    [Fact]
    public void A_critical_doubles_what_silver_already_doubled()
    {
        var attacker = Character(Fighter(strength: 100, dexterity: 1000));
        var target = Character(Fighter());
        target.Actor!.Flags |= Actor.Flag.HurtBySilver;
        var attack = new WeaponAttack { Damage = 100, Accuracy = 1000, Flags = WeaponFlag.Silver };

        // Swing until one comes out a critical; the roll is uniform, so it does not take long.
        var rng = new Rng(5);
        Blow blow = default;
        for (int i = 0; i < 100 && !blow.Critical; i++) blow = Combat.Swing(attacker, target, attack, rng);
        Assert.True(blow.Critical, "no critical in 100 swings against a defenceless target");
        Assert.Equal(Combat.MultiplierStep * 2, blow.Multiplier);

        // Without the silver flag only the critical doubles it.
        var plain = new WeaponAttack { Damage = 100, Accuracy = 1000 };
        Blow bare = default;
        for (int i = 0; i < 100 && !bare.Critical; i++) bare = Combat.Swing(attacker, target, plain, rng);
        Assert.True(bare.Critical);
        Assert.Equal(Combat.MultiplierStep, bare.Multiplier);
    }

    [Fact]
    public void A_helpless_target_and_a_back_turned_one_are_easier_to_hit()
    {
        var attacker = Character(Fighter(dexterity: 30));
        var target = Character(Fighter(dexterity: 30));
        var attack = new WeaponAttack { Accuracy = 10 };

        float upright = Combat.Defense(target, helpless: false, attackerIsPlayer: false, facing: 0f, new Rng(9));
        float held = Combat.Defense(target, helpless: true, attackerIsPlayer: false, facing: 0f, new Rng(9));
        Assert.Equal(upright - Combat.HelplessDefencePenalty, held);

        // The backstab is the player's alone, and only past the 0.2 the engine asks for.
        float behind = Combat.Defense(target, false, attackerIsPlayer: true, facing: 1f, new Rng(9));
        Assert.Equal(upright - Combat.BackstabScale, behind);
        Assert.Equal(upright, Combat.Defense(target, false, attackerIsPlayer: true, facing: 0.1f, new Rng(9)));
        Assert.Equal(upright, Combat.Defense(target, false, attackerIsPlayer: false, facing: 1f, new Rng(9)));
    }

    [Fact]
    public void A_character_with_no_skill_at_all_still_lands_the_occasional_blow()
    {
        var attacker = Character(Fighter(dexterity: 10));
        var target = Character(Fighter(dexterity: 10));
        var attack = new WeaponAttack { Damage = 20, Accuracy = 5 };
        int hits = 0;
        var rng = new Rng(1);
        for (int i = 0; i < 1000; i++) if (Combat.Swing(attacker, target, attack, rng).Hit) hits++;
        Assert.InRange(hits, 1, 999);
    }
}
