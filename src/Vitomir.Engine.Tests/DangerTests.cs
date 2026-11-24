// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Danger is a balance of strength, not a head count** (the danger query and the strength
/// score, a comparison with 1.1's first gap). The number picks the discipline ladder's
/// rungs in every fight, so these pin it by hand-worked cases: the score of one character term by
/// term, the bands of the ratio, and the balance in a real place — one strong enemy against a crowd
/// of weak ones, which the old head count ran the wrong way round in both directions — with the dead
/// counted, as the query does not pass over them.
/// </summary>
public class DangerTests
{
    /// <summary>A character with nothing on it but what a case sets.</summary>
    static Actor Blank(ActorType type, float body, float max, float health, float generation = 13f)
    {
        var a = new Actor { Type = type, MaxHealth = max, Health = health };
        a.Stat[(int)ActorStat.Strength] = a.Stat[(int)ActorStat.Dexterity] = a.Stat[(int)ActorStat.Stamina] = body;
        a.Stat[(int)ActorStat.Generation] = generation;
        return a;
    }

    /// <summary>
    /// The strength score's terms one by one: body and the first two soaks and maximum health, truncated;
    /// the weapon's damage and accuracy; (14 − generation) × 50 for a vampire of 13 or under;
    /// (level + 1) × 10 a discipline — here none, the list being the game's.
    /// </summary>
    [Fact]
    public void A_characters_strength_is_its_body_its_weapon_and_its_blood()
    {
        // A human: 3 + 2 + 4 + soaks 1 + 2 + 100 = 112; a sword of 20 at accuracy 0.5 makes 132.5,
        // truncated to 132. Mana full: no penalty.
        var human = Blank(ActorType.Human, 0f, 100f, 100f);
        human.Stat[(int)ActorStat.Strength] = 3f;
        human.Stat[(int)ActorStat.Dexterity] = 2f;
        human.Stat[(int)ActorStat.Stamina] = 4f;
        human.Soak[(int)DamageType.Normal] = 1f;
        human.Soak[(int)DamageType.Lethal] = 2f;
        human.Stat[(int)ActorStat.Mana] = human.Stat[(int)ActorStat.ManaPool] = 10f;
        Assert.Equal(112, Minds.Strength(human));
        var sword = new Weapon { Name = "sword" };
        sword.Current.Damage = 20;
        sword.Current.Accuracy = 0.5f;
        human.Weapon = sword;
        Assert.Equal(132, Minds.Strength(human));

        // A vampire of the 12th generation: 15 + 150 = 165, and 100 for his blood.
        var christof = Blank(ActorType.Vampire, 5f, 150f, 150f, generation: 12f);
        christof.Stat[(int)ActorStat.Blood] = christof.Stat[(int)ActorStat.BloodPool] = 20f;
        Assert.Equal(265, Minds.Strength(christof));
        // A 14th-generation one gets nothing for it (generation ≤ 13).
        christof.Stat[(int)ActorStat.Generation] = 14f;
        Assert.Equal(165, Minds.Strength(christof));
        // A ghoul's "generation" is nobody's business: the term is a vampire's only.
        var ghoul = Blank(ActorType.Ghoul, 5f, 150f, 150f, generation: 0f);
        Assert.Equal(165, Minds.Strength(ghoul));
    }

    /// <summary>
    /// The penalties: health below 95% on one ladder, blood or mana on the other;
    /// a ghoul or a monster, having neither, pays the health ladder's steps twice. The
    /// arithmetic is single precision (as in 1.1), so 90 of 100 is 0.9 exactly as a float and is **not** above the
    /// 90% step — it pays the 75% step's 50, not 25.
    /// </summary>
    [Fact]
    public void Wounds_and_an_empty_reserve_cost_strength_and_a_ghoul_pays_for_its_wounds_twice()
    {
        var human = Blank(ActorType.Human, 3f, 100f, 100f);
        human.Stat[(int)ActorStat.Mana] = human.Stat[(int)ActorStat.ManaPool] = 10f;
        Assert.Equal(109, Minds.Strength(human));
        human.Health = 91f;  Assert.Equal(109 - 25, Minds.Strength(human));
        human.Health = 90f;  Assert.Equal(109 - 50, Minds.Strength(human));
        human.Health = 80f;  Assert.Equal(109 - 50, Minds.Strength(human));
        human.Health = 60f;  Assert.Equal(109 - 100, Minds.Strength(human));
        human.Health = 30f;  Assert.Equal(109 - 150, Minds.Strength(human));
        human.Health = 0f;   Assert.Equal(109 - 200, Minds.Strength(human));
        // Mana 4 of 10: (4 + 1) / (10 + 1) = 0.45, above the 25% step: 100.
        human.Health = 100f;
        human.Stat[(int)ActorStat.Mana] = 4f;
        Assert.Equal(109 - 100, Minds.Strength(human));
        Assert.Equal(109, Minds.Strength(human, raw: true));

        // A vampire with no blood left: 1 / 21 is under a quarter — 150.
        var dry = Blank(ActorType.Vampire, 3f, 100f, 100f, generation: 14f);
        dry.Stat[(int)ActorStat.BloodPool] = 20f;
        Assert.Equal(109 - 150, Minds.Strength(dry));

        // A ghoul at 60%: 100 for the wounds, and 75 more for the same wounds on the second ladder.
        var ghoul = Blank(ActorType.Ghoul, 3f, 100f, 60f);
        Assert.Equal(109 - 100 - 75, Minds.Strength(ghoul));
    }

    /// <summary>
    /// The bands: friends ÷ foes, above 3, 2, 1, 0.9, 0.8 and 0.5 answering 1 to
    /// 6 and anything lower 7; foes worth nothing answer 0. A foes' sum below zero (the dead, below)
    /// makes the ratio negative — 7; a friends' sum below zero is read as unsigned — 1.
    /// </summary>
    [Fact]
    public void The_ratio_of_the_two_sides_falls_into_seven_bands()
    {
        Assert.Equal(0, Minds.Band(500, 0));
        Assert.Equal(1, Minds.Band(301, 100));
        Assert.Equal(2, Minds.Band(300, 100));   // exactly three is not more than three
        Assert.Equal(3, Minds.Band(150, 100));
        Assert.Equal(4, Minds.Band(95, 100));
        Assert.Equal(5, Minds.Band(9, 10));      // 0.9 as a float is the constant itself
        Assert.Equal(6, Minds.Band(51, 100));
        Assert.Equal(7, Minds.Band(50, 100));
        Assert.Equal(7, Minds.Band(300, -177));
        Assert.Equal(1, Minds.Band(unchecked((uint)-50), 300));
    }

    /// <summary>
    /// A party member in a real place (Old Town, `OTDY_4_1`), with four of the scene's characters
    /// made into the cases and everybody else taken out: one strong vampire against him is band 7
    /// — throw everything — where the head count said 1; three weak humans are band 1 — his side more
    /// than three times as strong — where it said 3. Two of the three killed leave their corpses in
    /// the query, each worth 23 − 200, and the foes' sum goes below zero: band 7 again. Nobody within
    /// 512 is band 0, and a pet is on neither side.
    /// </summary>
    [Fact]
    public void One_strong_enemy_is_danger_and_a_crowd_of_weak_ones_is_not()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var member = minds.All.First(m => !ReferenceEquals(m.Thing, minds.Steered) && m.Thing.Actor is { Disciplines.Count: 0 });
        var cast = minds.All.Where(m => m != member && !ReferenceEquals(m.Thing, minds.Steered)
                                        && m.Thing.Actor is { Disciplines.Count: 0 })
                            .Take(4).Select(m => m.Thing).ToList();
        Assert.Equal(4, cast.Count);
        foreach (var t in place.Things)
            if (t.Type is ThingType.Actor or ThingType.Player && t != member.Thing && !cast.Contains(t)) t.Removed = true;

        const int mine = 0, theirs = Actor.Teams.EnemyFirst;
        void Make(WorldThing t, ActorType type, int team, float body, float max, float generation)
        {
            var a = t.Actor!;
            Array.Clear(a.Stat);
            Array.Clear(a.Soak);
            a.Type = type; a.Team = team; a.Weapon = null; a.MaxHealth = a.Health = max;
            a.Flags &= ~Actor.Flag.Dead;
            a.Stat[(int)ActorStat.Strength] = a.Stat[(int)ActorStat.Dexterity] = a.Stat[(int)ActorStat.Stamina] = body;
            a.Stat[(int)ActorStat.Generation] = generation;
            a.Stat[(int)ActorStat.Blood] = a.Stat[(int)ActorStat.BloodPool] = 50f;
            place.Move(t, member.Thing.Position);
        }
        void Away(WorldThing t) => place.Move(t, member.Thing.Position + new System.Numerics.Vector3(5000f, 0f, 0f));

        Make(member.Thing, ActorType.Vampire, mine, 4f, 200f, 12f);          // 212 + 100 = 312
        Assert.Equal(312, Minds.Strength(member.Thing.Actor!));

        // One strong vampire: 18 + 400 + 300 = 718. 312 / 718 is under a half.
        Make(cast[0], ActorType.Vampire, theirs, 6f, 400f, 8f);
        foreach (var t in cast.Skip(1)) t.Removed = true;
        Assert.Equal(718, Minds.Strength(cast[0].Actor!));
        Assert.Equal(7, minds.Threat(member));

        // Three weak humans, 23 each: 312 / 69 is more than three.
        cast[0].Removed = true;
        foreach (var t in cast.Skip(1)) { t.Removed = false; Make(t, ActorType.Human, theirs, 1f, 20f, 13f); }
        Assert.Equal(23, Minds.Strength(cast[1].Actor!));
        Assert.Equal(1, minds.Threat(member));

        // Two of them dead where they fell: 23 − 177 − 177 is below zero.
        foreach (var t in cast.Skip(2)) { t.Actor!.Health = 0f; t.Actor.Flags |= Actor.Flag.Dead; }
        Assert.Equal(7, minds.Threat(member));

        // Nobody within 512: no foes, band 0.
        foreach (var t in cast.Skip(1)) Away(t);
        Assert.Equal(0, minds.Threat(member));

        // The strong vampire back, but a pet: on neither side, so still nobody.
        cast[0].Removed = false;
        Make(cast[0], ActorType.Vampire, theirs, 6f, 400f, 8f);
        cast[0].Mind!.Flags |= Mind.FlagPet;
        Assert.Equal(0, minds.Threat(member));
        cast[0].Mind!.Flags &= ~Mind.FlagPet;
        Assert.Equal(7, minds.Threat(member));
    }
}
