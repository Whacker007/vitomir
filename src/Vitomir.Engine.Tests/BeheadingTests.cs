// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Decapitation** (the roll in the flinch, then the beheading): who can lose a head and to
/// what, the head that is made, the headless body that stands in for the old one, the
/// `ef_beheaded` that finishes the job, and the victims that cannot be beheaded for want of a head
/// template.
/// </summary>
public class BeheadingTests
{
    static (Location Place, WorldThing Victim, WorldThing Attacker) Monastery()
    {
        var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var victim = place.Things.First(t => t.Template.Equals("Skel_Sword", StringComparison.OrdinalIgnoreCase));
        var christof = place.Player!.Thing;
        var sword = new Weapon { Name = "sword", Model = "broadsword" };
        sword.Attacks[0].Flags = WeaponFlag.CanBehead;
        christof.Actor!.Weapon = sword;
        return (place, victim, christof);
    }

    [Fact]
    public void The_roll_is_damage_plus_a_tenth_of_strength_as_a_percent()
    {
        // rand01 x 100 < Strength x 0.1 + damage. Seed 1's first rand() is 41: 0.1251 of the way.
        var (_, victim, christof) = Monastery();
        christof.Actor!.Stat[(int)ActorStat.Strength] = 1f;
        Assert.False(Beheading.Roll(victim, christof, HitKind.Melee, 0.02f, new Rng(1), 1_000));   // 0.12
        Assert.False(victim.Actor!.Has2(World.Actor.Flag2.IsBeheaded));
        Assert.True(Beheading.Roll(victim, christof, HitKind.Melee, 0.03f, new Rng(1), 1_000));    // 0.13
        Assert.True(victim.Actor.Has2(World.Actor.Flag2.IsBeheaded));
    }

    [Fact]
    public void Only_a_bladed_melee_blow_by_a_character_rolls()
    {
        var (place, victim, christof) = Monastery();
        var rng = new Rng(1);
        Assert.False(Beheading.Roll(victim, christof, HitKind.Projectile, 50f, rng, 1_000));
        Assert.False(Beheading.Roll(victim, null, HitKind.Melee, 50f, rng, 1_000));
        christof.Actor!.Weapon!.Attacks[0].Flags = 0;                                     // not BLADED
        Assert.False(Beheading.Roll(victim, christof, HitKind.Melee, 50f, rng, 1_000));
        // None of those reached the rand(): the conditions come first.
        Assert.Equal(new Rng(1).Next(), rng.Next());
        _ = place;
    }

    [Fact]
    public void The_head_comes_off_and_the_body_goes_headless()
    {
        var (place, victim, christof) = Monastery();
        int before = place.Things.Count;
        Assert.True(Beheading.Behead(victim, christof, new Rng(7), 1_000));
        var head = place.Things.Skip(before).FirstOrDefault(t => t.Template.Equals("Skel_Sword_head", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(head);
        Assert.True(victim.Actor!.Has2(World.Actor.Flag2.IsBeheaded));
        Assert.Equal("Skeleton_nohead", victim.Actor.BodyModel, ignoreCase: true);
        Assert.Contains(victim.Effects.All, e => e.Name == Beheading.Effect);
        // A skeleton is NOBLEED: no spurt on the neck.
        Assert.DoesNotContain(place.Things.Skip(before), t => t.Template == Beheading.Blood);

        // The mesh puts the headless body in slot 0 and stops drawing the whole one.
        var sim = new Simulation(place);
        sim.Step(1f / 30f);
        Assert.NotNull(victim.Shapes!.Slots[0].Model);
        Assert.True(victim.Shapes.BodyHidden);
        Assert.True(NamedShapes.IsHidden(victim, NamedShapes.Body, 0));
        int at = place.Drawn.ToList().IndexOf(victim);
        Assert.Contains(place.Mesh.Batches, b => b.Thing == at && b.Slot == 0);

        // And a second one does nothing: THING_AF2_ISBEHEADED.
        Assert.False(Beheading.Behead(victim, christof, new Rng(7), 1_100));
    }

    /// <summary>
    /// **The head has no maker, as in 1.1** (the beheading names none; a gib gets one), and it
    /// flies clear of the body it was born inside: the sweep does not report a body the move starts on
    /// the axis of (<see cref="WorldSweep"/>). Earlier, Vitomir named the body as the head's maker,
    /// because its old ray flight struck the neck on the first frame and the head was gone; with the
    /// maker taken away, twelve beheadings across the monastery's skeletons and zombies came out the
    /// same to the frame — the victim's health, where each head came to rest, which heads went.
    /// </summary>
    [Fact]
    public void The_head_has_no_maker_and_flies_clear_of_its_own_body()
    {
        for (int seed = 1; seed <= 6; seed++)
        {
            var (place, victim, christof) = Monastery();
            var sim = new Simulation(place);
            sim.Step(1f / 30f);
            int before = place.Things.Count;
            Assert.True(Beheading.Behead(victim, christof, new Rng(seed), place.Milliseconds));
            var head = place.Things.Skip(before).First(t => t.Template.EndsWith("_head", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(ThingType.Projectile, head.Type);
            Assert.Equal(0, head.SpawnedBy);
            var born = head.Position;
            sim.Step(1f / 30f);
            Assert.True(place.Things.Contains(head) && !head.Removed, $"seed {seed}: the head struck something on its first frame");
            for (int i = 0; i < 60; i++) sim.Step(1f / 30f);
            Assert.True(place.Things.Contains(head) && !head.Removed, $"seed {seed}: the head is gone");
            Assert.True(Vector3.Distance(head.Position, born) > 40f, $"seed {seed}: the head did not fly ({Vector3.Distance(head.Position, born):0})");
        }
    }

    /// <summary>
    /// **A fleeing corpse's bleed, as in 1.1** (a
    /// `corpse_awaken`, max 80): 64.2 at one look and 12.7 at the next, `ef_beheaded` put on at
    /// the blow, and the level-4 `ef_dmg_movespeed`, 50 s long, put on 14 ms later, **the
    /// frame after the blow**. A wound penalty is laid again only when the damage path takes health,
    /// so that was the bleed's first tick (the frame after its
    /// `now + 1`) taking its 16: the blow took it 64.2 → 28.7 (level 2), the tick 28.7 →
    /// 12.7 (level 4), both between two looks — and the second tick, 500 ms on, kills it
    /// (dead about half a second after the blow). The "first tick took nothing" once written down was the two changes
    /// read as one. Vitomir's does the same, frame for frame.
    /// </summary>
    [Fact]
    public void A_fleeing_corpses_first_bleed_tick_is_the_frame_after_the_blow()
    {
        using var files = Install.Open();
        for (int seed = 1; seed <= 40; seed++)
        {
            var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
            place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
            var corpse = place.Things.First(t => t.Template.Equals("corpse_awaken", StringComparison.OrdinalIgnoreCase));
            var christof = place.Player!.Thing;
            var sword = new Weapon { Name = "sword", Model = "broadsword" };
            sword.Attacks[0].Flags = WeaponFlag.CanBehead;
            christof.Actor!.Weapon = sword;
            var sim = new Simulation(place);
            sim.Step(1f / 30f);
            corpse.Actor!.Health = 28.7f;                                  // what the blow left (64.2 − 35.5)
            Assert.True(Beheading.Behead(corpse, christof, new Rng(seed), place.Milliseconds));
            var bleed = corpse.Effects.All.First(e => e.Name == Beheading.Effect);
            if (bleed.Value != corpse.Actor.MaxHealth * Beheading.Slow) continue;   // the quick branch: another roll
            int blow = place.Milliseconds;

            sim.Step(1f / 30f);                                           // the next frame: the first tick
            Assert.Equal(28.7f - 16f, corpse.Actor.Health, 3);
            var limp = corpse.Effects.All.First(e => e.Name == "ef_dmg_movespeed" && !e.Spent);
            Assert.Equal(place.Milliseconds + 50_000, limp.Until);        // laid again by the tick, level 4
            Assert.True(place.Milliseconds - blow < 100);

            int died = 0;
            for (int i = 0; i < 30 && died == 0; i++)
            {
                sim.Step(1f / 30f);
                if (corpse.Actor.Has(Actor.Flag.Dead)) died = place.Milliseconds;
                else Assert.Equal(12.7f, corpse.Actor.Health, 3);          // nothing between the ticks
            }
            Assert.InRange(died - blow, 500, 600);                           // the second tick
            return;
        }
        Assert.Fail("no seed in forty took the slow branch");
    }

    [Fact]
    public void An_even_chance_picks_a_quick_death_or_a_frightened_bleed_out()
    {
        bool quick = false, slow = false;
        for (int seed = 1; seed < 40 && !(quick && slow); seed++)
        {
            var (_, victim, christof) = Monastery();
            Assert.True(Beheading.Behead(victim, christof, new Rng(seed), 1_000));
            var effect = victim.Effects.All.First(e => e.Name == Beheading.Effect);
            float max = victim.Actor!.MaxHealth;
            if (effect.Value == max * Beheading.Quick) quick = true;
            else
            {
                Assert.Equal(max * Beheading.Slow, effect.Value, 3);
                Assert.Equal(AiMode.Afraid, victim.Mind!.Mode);                  // forced
                Assert.NotEqual(0u, victim.Mind.Flags & Mind.FlagUnaffected);
                slow = true;
            }
        }
        Assert.True(quick && slow);
    }

    /// <summary>
    /// **A skeleton's bleed is soaked by a roll, and about one tick in four gets through.** The slow
    /// branch's tick is 12 aggravated (0.2 × 60), and a skeleton's aggravated soak is 50, rolled
    /// (`rand01 × 50`, `Combat.Soak`) — so a tick takes 0–12 when the roll comes under 12, nothing
    /// otherwise. In 1.1 a `skel_claymore`'s fourth tick takes
    /// 10.2 and a `skel_bow`'s second 6.1, the other seven ticks nothing; and a
    /// `skel_sword` takes nothing from its three.
    /// </summary>
    [Fact]
    public void A_skeletons_bleed_ticks_are_soaked_by_a_roll()
    {
        var (_, victim, christof) = Monastery();
        var rng = new Rng(7);
        int through = 0; float sum = 0f;
        const int Ticks = 2000;
        for (int i = 0; i < Ticks; i++)
        {
            victim.Actor!.Health = 1000f;
            var harm = Damage.Apply(victim, christof, HitKind.Effect, DamageType.Aggravated, 12f, rng, 1000);
            if (harm.Taken > 0f)
            {
                through++; sum += harm.Taken;
                Assert.InRange(harm.Taken, 0.0001f, 12f);
            }
        }
        Assert.InRange(through / (float)Ticks, 0.21f, 0.27f);
        Assert.InRange(sum / through, 5f, 7f);
    }

    /// <summary>
    /// **The slow branch, as in 1.1**: a `skel_sword` beheaded
    /// at 27.8 health can stay at 27.8, afraid, for the 1.1 s until Christof's next blow kills it —
    /// the bleed's 12 a tick taking nothing visible off it in its three ticks (a 43% chance, the soak
    /// being rolled: <see cref="A_skeletons_bleed_ticks_are_soaked_by_a_roll"/>). Vitomir's first seed
    /// that gives the slow branch happens to soak all three too, which is what this pins: the branch,
    /// the fright and the timing, not that a skeleton never bleeds.
    /// </summary>
    [Fact]
    public void A_slow_beheading_takes_nothing_visible_off_a_skeleton_in_its_first_second()
    {
        for (int seed = 1; seed < 40; seed++)
        {
            var (place, victim, christof) = Monastery();
            // Where 1.1 had them: Christof 98 units off, the skeleton facing him.
            place.Move(christof, victim.Position + new Vector3(1.5f, 97.6f, 0f));
            place.Player!.Ground();
            var sim = new Simulation(place);
            sim.Step(1f / 60f);
            victim.Actor!.Health = 27.8f;
            Assert.True(Beheading.Behead(victim, christof, new Rng(seed), sim.Milliseconds));
            if (victim.Effects.All.First(e => e.Name == Beheading.Effect).Value != victim.Actor.MaxHealth * Beheading.Slow) continue;
            for (int frame = 0; frame < 66; frame++) sim.Step(1f / 60f);
            Assert.Equal(27.8f, victim.Actor.Health, 0.01f);
            Assert.False(victim.Actor.Has(Actor.Flag.Dead));
            Assert.Equal(AiMode.Afraid, victim.Mind!.Mode);
            return;
        }
        Assert.Fail("no seed gave the slow branch");
    }

    [Fact]
    public void A_victim_with_no_head_template_keeps_its_head()
    {
        // `%s_head` of the template: seven beheadable templates have none, and the beheading returns 0.
        var (place, victim, christof) = Monastery();
        var enemy = place.Place("testEnemy", victim.Position, victim.Orientation, victim.Sector);
        Assert.NotNull(enemy);
        Assert.True(enemy.Actor!.Has(World.Actor.Flag.CanBehead));
        Assert.False(Beheading.Behead(enemy, christof, new Rng(1), 1_000));
        Assert.False(enemy.Actor.Has2(World.Actor.Flag2.IsBeheaded));
        Assert.Equal("", enemy.Actor.BodyModel);
    }

    [Fact]
    public void With_no_gore_a_beheading_is_a_kill()
    {
        var (_, victim, christof) = Monastery();
        Gore.NoBlood = true;
        try
        {
            Assert.False(Beheading.Behead(victim, christof, new Rng(1), 1_000));
            Assert.True(victim.Actor!.Dead);
            Assert.False(victim.Actor.Has2(World.Actor.Flag2.IsBeheaded));
        }
        finally { Gore.NoBlood = false; }
    }

    [Fact]
    public void Armour_is_drawn_as_the_body_it_names()
    {
        // SetArmor: christof in plate is christof_plate, in slot 0, and the
        // plain body stops drawing; taking it off gives the body back.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var christof = place.Player!.Thing;
        var plate = Item.From("chainMail", place.Templates!.Resolve("chainMail"))!;
        christof.Actor!.Stat[(int)ActorStat.Strength] = 100f;                        // whatever it asks
        Assert.Equal(EquipResult.Equipped,
                     Equipment.Equip(christof, plate, Slot.None, new EffectDefinitions(files), 1_000));
        Assert.Equal("christof_chain", christof.Actor.BodyModel, ignoreCase: true);
        var sim = new Simulation(place);
        sim.Step(1f / 30f);
        Assert.NotNull(christof.Shapes!.Slots[0].Model);
        Assert.True(christof.Shapes.BodyHidden);

        Equipment.Unequip(christof, plate, new EffectDefinitions(files));
        Assert.Equal("", christof.Actor.BodyModel);
        sim.Step(1f / 30f);
        Assert.False(christof.Shapes.BodyHidden);
    }
}
