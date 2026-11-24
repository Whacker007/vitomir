// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Dying, and what is left on the floor afterwards. Everything asserted here was checked against
/// the original game rather than remembered.
/// </summary>
public class DeathTests
{
    static WorldThing Character(Actor actor, ThingType type = ThingType.Actor) => new()
    {
        Index = 0,
        Guid = 1,
        Scene = new NsdThing(),
        Template = "test",
        Type = type,
        Actor = actor,
    };

    static Actor Body(ActorType type = ActorType.Vampire, uint flags = 0, int skeleton = 0)
    {
        var a = new Actor { Type = type, Flags = flags, SkeletonId = skeleton };
        a.Health = a.MaxHealth = 100;
        return a;
    }

    [Fact]
    public void Health_defaults_to_a_hundred_because_the_constructor_says_so()
    {
        var bare = Actor.From(new Dictionary<uint, NodTag>());
        Assert.Equal(Actor.DefaultHealth, bare.Health);
        Assert.Equal(Actor.DefaultHealth, bare.MaxHealth);
        Assert.Equal(Actor.Teams.None, bare.Team);
        Assert.Equal(ActorType.Vampire, bare.Type);   // ACTOR_TYPE 0 is the zero of the field
    }

    [Fact]
    public void Who_is_immune_to_what()
    {
        Assert.True(Damage.Refuses(Body(flags: Actor.Flag.Invulnerable), DamageType.Fire));

        var stone = Body(flags: Actor.Flag.InvulnerableToPhysical);
        foreach (var t in new[] { DamageType.Normal, DamageType.Lethal, DamageType.Aggravated })
            Assert.True(Damage.Refuses(stone, t), $"{t} should bounce off");
        Assert.False(Damage.Refuses(stone, DamageType.Fire));

        // Sunlight and faith are a vampire's problem and nobody else's.
        foreach (var t in new[] { DamageType.Sun, DamageType.Faith })
        {
            Assert.False(Damage.Refuses(Body(ActorType.Vampire), t));
            Assert.True(Damage.Refuses(Body(ActorType.Ghoul), t));
            Assert.True(Damage.Refuses(Body(ActorType.Human), t));
            Assert.True(Damage.Refuses(Body(ActorType.Monster), t));
        }
    }

    [Fact]
    public void An_ordinary_body_fades_at_thirty_seconds_and_is_gone_at_thirty_five()
    {
        var thing = Character(Body(ActorType.Human));
        Damage.Kill(thing, null, HitKind.Melee, DamageType.Lethal, nowMs: 1_000);

        Assert.True(thing.Actor!.Dead);
        Assert.Equal(1_000 + Damage.Corpse.FadeMs, thing.Actor.FadeAt);
        Assert.Equal(1_000 + Damage.Corpse.ExpireMs, thing.ExpireAt);
        Assert.Equal(5_000, Damage.Corpse.ExpireMs - Damage.Corpse.FadeMs);
    }

    [Fact]
    public void A_party_members_body_fades_in_five_seconds_and_is_never_removed()
    {
        var thing = Character(Body(), ThingType.Player);
        Damage.Kill(thing, null, HitKind.Melee, DamageType.Lethal, nowMs: 0);

        Assert.Equal(Damage.Corpse.PartyFadeMs, thing.Actor!.FadeAt);
        Assert.Equal(0, thing.ExpireAt);
        // And a party member never dusts, however much of a vampire they are.
        Assert.False(Damage.Dusts(thing));
        Assert.False(thing.Actor.Dusted);
    }

    [Fact]
    public void An_invulnerable_character_cannot_be_killed_at_all()
    {
        var thing = Character(Body(flags: Actor.Flag.Invulnerable));
        Assert.False(Damage.Kill(thing, null, HitKind.Melee, DamageType.Aggravated, 0));
        Assert.False(thing.Actor!.Dead);
    }

    [Fact]
    public void A_corpse_stops_being_a_target_when_the_fade_starts_not_when_it_ends()
    {
        var thing = Character(Body(ActorType.Human));
        Damage.Kill(thing, null, HitKind.Melee, DamageType.Lethal, 0);
        Assert.True(Damage.CanBeRaised(thing));

        // The actor tick clears the clock as the fade begins: from then on Awaken cannot reach it,
        // and the body is still visible for another 4.8 seconds.
        thing.Actor!.FadeAt = 0;
        Assert.False(Damage.CanBeRaised(thing));

        // A party member is reachable for ever, fade or no fade.
        var party = Character(Body(), ThingType.Player);
        Damage.Kill(party, null, HitKind.Melee, DamageType.Lethal, 0);
        party.Actor!.FadeAt = 0;
        Assert.True(Damage.CanBeRaised(party));
    }

    /// <summary>
    /// The dust zeroes the fade clock, but the kill sets it again after the death effects,
    /// so the invisible remains are a `TARGET DEAD` target for the usual thirty seconds.
    /// (This test said the opposite until then, on the dust's reading alone.)
    /// </summary>
    [Fact]
    public void Dusting_leaves_the_kill_s_fade_clock_so_the_remains_are_a_target_for_thirty_seconds()
    {
        var thing = Character(Body(ActorType.Vampire));
        Damage.Kill(thing, null, HitKind.Melee, DamageType.Lethal, 0);
        Assert.True(thing.Actor!.Dusted);
        Assert.Equal(Damage.Corpse.FadeMs, thing.Actor.FadeAt);
        Assert.True(Damage.CanBeRaised(thing));

        // NOSKELDEATH puts the body back.
        var ahzra = Character(Body(ActorType.Vampire, Actor.Flag.NoSkeletonDeath));
        Damage.Kill(ahzra, null, HitKind.Melee, DamageType.Lethal, 0);
        Assert.False(ahzra.Actor!.Dusted);
        Assert.True(Damage.CanBeRaised(ahzra));
    }

    [Fact]
    public void Only_vampires_dust_and_only_with_a_skeleton_template_to_dust_into()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        bool Exists(int id) => templates.Find($"skeleton_{id}") is not null;

        int actors = 0, dust = 0;
        var vampiresThatDoNot = new List<string>();
        foreach (var name in templates.Names)
        {
            var tags = templates.Resolve(name);
            if (!tags.TryGetValue(Tags.Type, out var t)) continue;
            var type = (ThingType)t.AsUInt();
            if (type is not (ThingType.Actor or ThingType.Player)) continue;
            actors++;
            var thing = Character(Actor.From(tags), type);
            if (Damage.Dusts(thing, Exists)) dust++;
            else if (thing.Actor!.Type == ActorType.Vampire) vampiresThatDoNot.Add(name);
        }
        Assert.Equal(696, actors);
        Assert.Equal(305, dust);

        // Every vampire that leaves a body is a party/multiplayer character, the one NOSKELDEATH,
        // or one of the two whose SKELETON_ID names a template the game does not ship.
        Assert.Contains("Ahzra", vampiresThatDoNot);
        Assert.Contains("Cappadocian_Skeleton", vampiresThatDoNot);
        Assert.Contains("Christof", vampiresThatDoNot);
        Assert.Equal(36, vampiresThatDoNot.Count);

        // The five skeletons the game does ship, and the two ids that name nothing.
        foreach (int id in new[] { 0, 1, 3, 4, 5 }) Assert.True(Exists(id), $"skeleton_{id} is missing");
        foreach (int id in new[] { 2, 8 }) Assert.False(Exists(id), $"skeleton_{id} turned up after all");
    }

    [Fact]
    public void A_gib_kill_needs_the_flag_and_eighty_damage()
    {
        var always = Body(flags: Actor.Flag.AlwaysGibKill);
        var maybe = Body(flags: Actor.Flag.CanGibKill);
        var never = Body();

        Assert.True(Damage.Gibs(always, damage: 1f));
        Assert.False(Damage.Gibs(maybe, Damage.GibDamage));
        Assert.True(Damage.Gibs(maybe, Damage.GibDamage + 0.01f));
        Assert.False(Damage.Gibs(never, 1000f));
    }

    /// <summary>
    /// The scripts revive with `ReviveActor(100, 100)` — percentages, into a native that takes fractions
    /// (Awaken passes 0.15 to 0.75). 1.1's health setter holds the result to the maximum.
    /// </summary>
    [Fact]
    public void A_revive_asked_for_a_hundred_times_the_health_gives_the_maximum()
    {
        var thing = Character(Body(ActorType.Human));
        thing.Actor!.Stat[(int)ActorStat.BloodPool] = 10f;
        float max = thing.Actor.MaxHealth;
        Assert.True(max > 0f);
        Damage.Revive(thing, 100f, 100f);
        Assert.Equal(max, thing.Actor.Health);
        Damage.Revive(thing, 0.75f, 0.25f);
        Assert.Equal(max * 0.75f, thing.Actor.Health);
    }

    [Fact]
    public void A_revived_character_keeps_the_head_it_lost()
    {
        var thing = Character(Body(ActorType.Human));
        // A pool to refill: the revive writes blood through SetStat, and a
        // human whose blood is written to 0 dies of it on the spot.
        thing.Actor!.Stat[(int)ActorStat.BloodPool] = 10f;
        Damage.Kill(thing, null, HitKind.Melee, DamageType.Lethal, 0);
        thing.Actor!.Flags |= Actor.Flag.Staked;
        thing.Actor.Flags2 |= Actor.Flag2.IsBeheaded | Actor.Flag2.Limping;

        Damage.Revive(thing);
        Assert.False(thing.Actor.Dead);
        Assert.False(thing.Actor.Has(Actor.Flag.Staked));
        Assert.False(thing.Actor.Has2(Actor.Flag2.Limping));
        Assert.Equal(0, thing.Actor.FadeAt);
        Assert.Equal(0, thing.ExpireAt);
        Assert.Equal(thing.Actor.MaxHealth, thing.Actor.Health);

        // Nothing in the engine ever clears this one.
        Assert.True(thing.Actor.Has2(Actor.Flag2.IsBeheaded));
    }

    [Fact]
    public void A_party_member_takes_three_quarters_of_a_shot_and_all_of_a_swing()
    {
        var target = Character(Body(), ThingType.Player);
        var enemy = Character(Body(ActorType.Human));
        target.Actor!.Health = 1000;

        var shot = Damage.Apply(target, enemy, HitKind.Projectile, DamageType.Lethal, 100f, new Rng(1));
        Assert.Equal(100f * Combat.PartyShotShare, shot.Taken, 3);

        var swing = Damage.Apply(target, enemy, HitKind.Melee, DamageType.Lethal, 100f, new Rng(1));
        Assert.Equal(100f, swing.Taken, 3);

        // Party member against party member, the discount does not apply.
        var friend = Character(Body(), ThingType.Player);
        Assert.Equal(100f,
            Damage.Apply(target, friend, HitKind.Projectile, DamageType.Lethal, 100f, new Rng(1)).Taken, 3);
    }

    [Fact]
    public void A_cappadocian_takes_half_again_as_much_from_faith()
    {
        var cappadocian = Character(Body());
        cappadocian.Actor!.Clan = "cappadocian";
        cappadocian.Actor.Health = 1000;
        var other = Character(Body());
        other.Actor!.Clan = "ventrue";
        other.Actor.Health = 1000;

        Assert.Equal(100f * Combat.CappadocianFaith,
            Damage.Apply(cappadocian, null, HitKind.Effect, DamageType.Faith, 100f, new Rng(1)).Taken, 3);
        Assert.Equal(100f,
            Damage.Apply(other, null, HitKind.Effect, DamageType.Faith, 100f, new Rng(1)).Taken, 3);
    }

    [Fact]
    public void Being_hurt_shakes_a_vampire_and_not_a_human()
    {
        var vampire = Character(Body(ActorType.Vampire));
        var human = Character(Body(ActorType.Human));
        vampire.Actor!.Health = human.Actor!.Health = 1000;

        Assert.True(Damage.Apply(vampire, null, HitKind.Melee, DamageType.Normal, 50f, new Rng(1)).Frenzy > 0);
        Assert.Equal(0f, Damage.Apply(human, null, HitKind.Melee, DamageType.Normal, 50f, new Rng(1)).Frenzy);
    }

    [Fact]
    public void Blood_calms_a_vampire_a_short_fuse_does_not_and_fire_is_worse()
    {
        var a = new Actor { Type = ActorType.Vampire };
        float plain = Combat.FrenzyFromDamage(a, DamageType.Normal, 10f);

        a.Stat[(int)ActorStat.Blood] = 100;
        Assert.True(Combat.FrenzyFromDamage(a, DamageType.Normal, 10f) < plain);

        a.Stat[(int)ActorStat.Blood] = 0;
        a.Stat[(int)ActorStat.FrenzyRating] = 100;
        Assert.True(Combat.FrenzyFromDamage(a, DamageType.Normal, 10f) > plain);

        // Fire is a quarter worse than anything else, whatever the stats.
        Assert.Equal(Combat.FrenzyFromDamage(a, DamageType.Normal, 10f) * Combat.FireFrenzy,
                     Combat.FrenzyFromDamage(a, DamageType.Fire, 10f), 3);

        // Wits damp it, but only above 20.
        var calm = new Actor { Type = ActorType.Vampire };
        calm.Stat[(int)ActorStat.Wits] = Combat.WitsMatterAbove;
        Assert.Equal(plain, Combat.FrenzyFromDamage(calm, DamageType.Normal, 10f), 3);
        calm.Stat[(int)ActorStat.Wits] = 100;
        Assert.True(Combat.FrenzyFromDamage(calm, DamageType.Normal, 10f) < plain);
    }

    [Fact]
    public void Killing_a_citizen_costs_the_killer_humanity()
    {
        var christof = Body();
        christof.Flags |= Actor.Flag.HumanityTrack;
        christof.BaseStat[(int)ActorStat.Humanity] = christof.Stat[(int)ActorStat.Humanity] = 70;
        var killer = Character(christof, ThingType.Player);

        var citizen = Character(Body(ActorType.Human, Actor.Flag.HumanityLoss));
        Damage.Kill(citizen, killer, HitKind.Melee, DamageType.Lethal, 0);
        Assert.Equal(70 - Damage.HumanityPerKill, christof.Stat[(int)ActorStat.Humanity]);
        Assert.Equal(70 - Damage.HumanityPerKill, christof.BaseStat[(int)ActorStat.Humanity]);

        // A Szlachta is not a citizen, and nobody grieves.
        var enemy = Character(Body(ActorType.Ghoul));
        Damage.Kill(enemy, killer, HitKind.Melee, DamageType.Lethal, 0);
        Assert.Equal(70 - Damage.HumanityPerKill, christof.Stat[(int)ActorStat.Humanity]);

        // And only a vampire who tracks it is charged at all.
        var brute = Body(ActorType.Human);
        brute.BaseStat[(int)ActorStat.Humanity] = brute.Stat[(int)ActorStat.Humanity] = 70;
        Assert.Equal(0f, Damage.LoseHumanity(brute, Damage.HumanityPerKill));
        Assert.Equal(70, brute.Stat[(int)ActorStat.Humanity]);
    }

    [Fact]
    public void An_ordinary_citizen_is_not_neutral_and_can_be_attacked()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);

        // The mask an attack order is refused against: DEAD | NEUTRAL | TALKTO.
        const uint Refused = Actor.Flag.Dead | Actor.Flag.Neutral | Actor.Flag.TalkTo;
        int neutral = 0, humanityLoss = 0;
        foreach (var name in templates.Names)
        {
            var tags = templates.Resolve(name);
            if (!tags.TryGetValue(Tags.Type, out var t)) continue;
            if ((ThingType)t.AsUInt() is not (ThingType.Actor or ThingType.Player)) continue;
            var a = Actor.From(tags);
            if (a.Has(Actor.Flag.Neutral)) neutral++;
            if (a.Has(Actor.Flag.HumanityLoss)) humanityLoss++;

        }
        Assert.Equal(70, neutral);
        Assert.Equal(111, humanityLoss);

        // The civilians who walk the streets are attackable, and killing one costs Humanity.
        foreach (var n in new[] { "Civ01oldman", "Civ02beggar", "Civ03merchant1", "Civ04merchant2",
                                  "Civ05YoungLady", "Civ06Milkmaid", "Civ07drunk", "Civ09monk1" })
        {
            var a = Actor.From(templates.Resolve(n));
            Assert.True((a.Flags & Refused) == 0, $"{n} cannot be attacked");
            Assert.True(a.Has(Actor.Flag.HumanityLoss), $"{n} costs no Humanity");
        }

        // Their scripted stand-ins are the ones that cannot be touched — `_PE`, `_cheer`, and the
        // drunk who has passed out in the street.
        foreach (var n in new[] { "Civ01oldman_PE", "Civ04merchant2_cheer", "Civ02passedOut" })
            Assert.True((Actor.From(templates.Resolve(n)).Flags & Refused) != 0, $"{n} is attackable");

        // The four conversation knights are the ones that really are untouchable.
        foreach (var n in new[] { "convKnight", "convKnight2", "convKnight3", "convKnight4" })
            Assert.True((Actor.From(templates.Resolve(n)).Flags & Refused) != 0, $"{n} is attackable");
    }

    [Fact]
    public void Prague_swaps_its_whole_civilian_population_at_the_embrace()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        const uint Refused = Actor.Flag.Dead | Actor.Flag.Neutral | Actor.Flag.TalkTo;

        // Every civilian a scene places, by template name.
        string[] Civilians(string scene)
        {
            var nsd = NsdScene.Read(files.Read($"scenes/{scene}.nsd"), scene);
            return [.. nsd.Things.Select(t => t.Template)
                                 .Where(n => n.StartsWith("civ", StringComparison.OrdinalIgnoreCase))
                                 .Order()];
        }
        bool Untouchable(string name) => (Actor.From(templates.Resolve(name)).Flags & Refused) != 0;

        // Chapter 4 is daylight Prague, where Christof is still a mortal. Every civilian in it is
        // a `_PE` variant, and every one of those cannot be targeted at all.
        foreach (var day in new[] { "OTDY_4_1", "GOLDD_4_1", "JUDBD_4_1", "IN4D_4_1" })
        {
            var civ = Civilians(day);
            Assert.NotEmpty(civ);
            foreach (var n in civ)
                Assert.True(Untouchable(n), $"{day} places {n}, which can be attacked");
        }

        // The first night scene of chapter 7 is still pre-Embrace and still untouchable...
        foreach (var n in Civilians("OLDT_7_1")) Assert.True(Untouchable(n), $"OLDT_7_1 places {n}");

        // ...and from the next scene on, the same people are ordinary actors who can be killed —
        // at five Humanity each.
        foreach (var after in new[] { "OLDT_7_4", "OLDT_7_7", "OLDT_11_1", "OLDT_18_1" })
        {
            var civ = Civilians(after);
            Assert.NotEmpty(civ);
            foreach (var n in civ)
            {
                Assert.False(Untouchable(n), $"{after} places {n}, which cannot be attacked");
                Assert.True(Actor.From(templates.Resolve(n)).Has(Actor.Flag.HumanityLoss),
                            $"{after} places {n}, which costs no Humanity");
            }
        }
    }

    /// <summary>
    /// A character's scripts hear every blow it does not refuse as `damaged` before anything else sees
    /// it (sent), and their answer is the blow: 0 and nothing lands,
    /// more and more lands. (The scripts themselves: <c>Vitomir.Scripting.Tests.DamagedTests</c>.)
    /// </summary>
    [Fact]
    public void A_script_can_take_the_sting_out_of_a_blow_before_anything_else_sees_it()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var target = place.Player!.Thing;
        target.Actor!.Health = target.Actor.MaxHealth = 100;
        var told = new List<(WorldThing By, float Damage, int Type)>();
        float answer = 0f;
        place.ItemScripts = new Ground.Scripts(Damaged: (t, by, d, type) => { told.Add((by!, d, type)); return answer; });

        var harm = Damage.Apply(target, null, HitKind.Melee, DamageType.Lethal, 1000f, new Rng(1));
        Assert.Equal(new Harm(0f, false, false), harm);
        Assert.Equal(100f, target.Actor.Health);
        Assert.Equal([(null!, 1000f, (int)DamageType.Lethal)], told);

        answer = 20f;                                                     // poison: nothing soaks it
        Assert.Equal(20f, Damage.Apply(target, null, HitKind.Effect, DamageType.Poison, 1f, new Rng(1)).Taken);
        Assert.Equal(80f, target.Actor.Health);
    }

    // --- the death animation ---------------------------------------------------------------

    [Fact]
    public void The_death_mode_numbers_are_the_engines_own()
    {
        // The ids are indices into the engine's motion-mode table, so pin them against it rather
        // than against themselves.
        Assert.Equal("deathquick", NmsMotionSet.BodyModes[Damage.DeathMode.Quick]);
        Assert.Equal("deathslow", NmsMotionSet.BodyModes[Damage.DeathMode.Slow]);
        Assert.Equal("deathdramatic", NmsMotionSet.BodyModes[Damage.DeathMode.Dramatic]);
        Assert.Equal("deahtsun", NmsMotionSet.BodyModes[Damage.DeathMode.Sun]);   // the original's own typo
        Assert.Equal("deathstaked", NmsMotionSet.BodyModes[Damage.DeathMode.Staked]);
        Assert.Equal("deathclawed", NmsMotionSet.BodyModes[Damage.DeathMode.Clawed]);
        Assert.Equal("deathdrained", NmsMotionSet.BodyModes[Damage.DeathMode.Drained]);
    }

    [Fact]
    public void Which_death_a_character_plays()
    {
        // The death's looks, gate by gate.
        Assert.Equal(Damage.DeathMode.Staked,
            Damage.DeathModeFor(Body(flags: Actor.Flag.Staked), ActionMode.None, damage: 100f));
        // The stake wins over everything, including a drain and a heavy blow.
        Assert.Equal(Damage.DeathMode.Staked,
            Damage.DeathModeFor(Body(flags: Actor.Flag.Staked), ActionMode.WristFedOn, damage: 100f));

        // Both halves of a drain play the drained death - the one being drained, and, faithfully,
        // the one doing the draining.
        Assert.Equal(Damage.DeathMode.Drained, Damage.DeathModeFor(Body(), ActionMode.WristFedOn, 100f));
        Assert.Equal(Damage.DeathMode.Drained, Damage.DeathModeFor(Body(), ActionMode.FedOn, 100f));

        // And the way round that surprises: the BIG hit is the quick death.
        Assert.Equal(Damage.DeathMode.Quick, Damage.DeathModeFor(Body(), ActionMode.None, 19.001f));
        Assert.Equal(Damage.DeathMode.Slow, Damage.DeathModeFor(Body(), ActionMode.None, 19f));
        Assert.Equal(Damage.DeathMode.Slow, Damage.DeathModeFor(Body(), ActionMode.None, 0f));
    }

    /// <summary>
    /// The sun's branch: a killing blow of 19 or less whose damage type is the sun (5)
    /// or faith (6) asks for `deahtsun` (0x7c) — after the stake, the drain and the quick death, which
    /// all win over it. No shipped set has the mode, so it falls back to the slow death in play.
    /// </summary>
    [Fact]
    public void A_small_killing_blow_of_sun_or_faith_asks_for_the_suns_death()
    {
        Assert.Equal(Damage.DeathMode.Sun, Damage.DeathModeFor(Body(), ActionMode.None, 10f, DamageType.Sun));
        Assert.Equal(Damage.DeathMode.Sun, Damage.DeathModeFor(Body(), ActionMode.None, 19f, DamageType.Faith));
        Assert.Equal(Damage.DeathMode.Slow, Damage.DeathModeFor(Body(), ActionMode.None, 10f, DamageType.Fire));
        Assert.Equal(Damage.DeathMode.Quick, Damage.DeathModeFor(Body(), ActionMode.None, 19.001f, DamageType.Sun));
        Assert.Equal(Damage.DeathMode.Drained, Damage.DeathModeFor(Body(), ActionMode.FedOn, 5f, DamageType.Sun));
        Assert.Equal(Damage.DeathMode.Staked,
            Damage.DeathModeFor(Body(flags: Actor.Flag.Staked), ActionMode.None, 5f, DamageType.Faith));
    }

    [Fact]
    public void Three_of_the_engines_six_death_modes_can_never_play()
    {
        // The engine's motion-mode table names six deaths. No shipped motion set
        // carries deathdramatic, deathclawed or deahtsun - and that last is the engine's own
        // spelling, a typo for deathsun, so a set could not offer one even if it wanted to.
        using var files = Install.Open();
        var motions = new Anim.MotionSets(files);
        var found = new SortedSet<string>();
        int sets = 0;
        foreach (var path in files.List("3d/Motions/"))
        {
            var set = motions.Set(Path.GetFileName(path));
            if (set is null) continue;
            sets++;
            foreach (var m in set.Modes)
                if (m.Name.StartsWith("death", StringComparison.OrdinalIgnoreCase)
                    || m.Name.StartsWith("deaht", StringComparison.OrdinalIgnoreCase))
                    found.Add(m.Name.ToLowerInvariant());
        }
        Assert.True(sets > 200, $"only {sets} motion sets read");
        Assert.Equal(["deathdrained", "deathquick", "deathslow", "deathstaked"], found);
    }

    [Fact]
    public void A_character_killed_in_mid_air_plays_no_death_at_all()
    {
        // ACTION_THROWN leaves the death effects before they reach the animation.
        var thing = Character(Body());
        thing.Actor!.Action = ActionMode.Thrown;
        Assert.Equal(0, Damage.Fall(thing, ActionMode.Thrown, damage: 100f, motions: null));
    }

    [Fact]
    public void The_skeleton_falls_the_way_the_body_fell()
    {
        // The death resolves the death mode against the **body's** set and then hands the resolved
        // number to the skeleton death, which plays it on the skeleton. The five `skeleton_<n>` templates
        // carry motion sets that contain the four death modes and nothing else, which is the data
        // saying the same thing.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON2", things: true, sky: false, scene: "MON2_8_2");
        // Deliberately this instance's own rather than the location's, because a set that came
        // from another MotionSets used to answer "no such mode" instead of loading.
        var motions = new Anim.MotionSets(files);
        var victim = place.Things.First(t => t.Actor is { } a && a.SkeletonId == 0
                                             && Damage.Dusts(t, id => place.Templates!.Has(Damage.SkeletonTemplate(id)))
                                             && t.Animation is not null);

        int before = place.Things.Count;
        Damage.Kill(victim, killer: null, HitKind.Melee, DamageType.Lethal, damage: 100f, nowMs: 1_000, motions: motions);
        var bones = place.Things.Skip(before).FirstOrDefault(t => t.Template.StartsWith("skeleton_"));

        Assert.NotNull(bones);
        Assert.NotNull(bones!.Animation);
        // Not its rest pose, and not a loop: the quick death, held on its last frame.
        Assert.Equal(Formats.NmsMotionSet.BodyModes[Damage.DeathMode.Quick], bones.Animation!.Mode!.Name);
        Assert.False(bones.Animation.Player!.Loop);
        // And the same one the body is playing.
        Assert.Equal(victim.Animation!.Mode!.Name, bones.Animation.Mode!.Name);

        // It also has to actually *fall*. Posed at the end of the animation the bones are on the
        // floor; posed at frame zero — which is what a skeleton with nothing playing shows — they
        // are standing up. The gap between the two is the bug, stated as a number.
        var posed = new System.Numerics.Vector3[bones.Animation.Model.Vertices.Length];
        bones.Animation.Pose(posed);
        float standing = bones.Animation.Model.Vertices.Max(v => v.Position.Z);
        bones.Animation.Player!.Frame = bones.Animation.Player.Animation.Duration;
        bones.Animation.Pose(posed);
        float fallen = posed.Max(p => p.Z);
        Assert.True(fallen < standing / 2f,
                    $"the skeleton ends the death {fallen:0} high against {standing:0} standing");
    }

    [Fact]
    public void Killing_a_vampire_from_inside_the_animation_walk_does_not_break_the_walk()
    {
        // **A crash, found in play the same hour it became reachable.** The animation walk rolls
        // the blow when a strike tag crosses; a blow can kill; a vampire's death spawns a
        // skeleton; and a skeleton with a mixer is appended to the very list the walk is
        // enumerating. It threw `InvalidOperationException` the first time a Cappadocian died
        // inside the walk — which could not happen until a death-only motion
        // set a mixer, because before that the skeleton never reached the list at all.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON2", things: true, sky: false, scene: "MON2_8_2",
                                  player: "Christof");
        // No minds: the victim has to stand still for this, and what is being tested is the walk,
        // not the AI.
        var sim = new Simulation(place);
        var christof = place.Player!.Thing;
        var cappa = place.Things.First(t => t.Template.Equals("cappadocian", StringComparison.OrdinalIgnoreCase)
                                            && t.Actor is { Dead: false });

        // Bring it to him rather than him to it: moving the player would leave his walk node
        // behind, and none of that is what this test is about.
        cappa.Position = christof.Position with { X = christof.Position.X + 40f };
        cappa.Sector = christof.Sector;
        cappa.Actor!.Health = 1f;

        // Ordered again each frame, because one swing may miss and the order ends with the swing.
        for (int frame = 0; frame < 30 * 20 && !cappa.Actor.Dead; frame++)
        {
            Actions.Attack(christof, cappa, christof.Actor!.Weapon, 0, sim.Milliseconds, motions: place.Motions);
            sim.Step(1f / 30f);
        }

        Assert.True(cappa.Actor.Dead, "the Cappadocian survived five seconds of being punched at one health");
        Assert.True(cappa.Actor.Dusted);
        Assert.Contains(place.Things, t => t.Template.StartsWith("skeleton_", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_dusting_body_fades_out_in_its_second_and_stays_to_its_expiry()
    {
        // The engine takes a dusting body's alpha to nothing over a second and leaves
        // the skeleton as the remains; the body itself keeps the ordinary 35 s expiry. Until the
        // corpse fade was fed to the model's alpha, Vitomir removed the body at the second instead.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON2", things: true, sky: false, scene: "MON2_8_2");
        var victim = place.Things.First(t => t.Actor is { } a && a.SkeletonId == 0
                                             && Damage.Dusts(t, id => place.Templates!.Has(Damage.SkeletonTemplate(id))));
        Damage.Kill(victim, killer: null, HitKind.Melee, DamageType.Lethal, damage: 100f, nowMs: 1_000);
        Assert.True(victim.Actor!.Dusted);
        Assert.Equal(1_000 + Damage.Corpse.FadeMs, victim.Actor.FadeAt);      // the kill's, after the dust's zero
        Assert.Equal(1_000 + Damage.Corpse.ExpireMs, victim.ExpireAt);
        Assert.True(victim.Fx is { AlphaRamping: true });
        victim.Fx!.Tick(0.5f);
        Assert.Equal(0.5f, victim.ModelAlpha, 3);
        victim.Fx.Tick(0.6f);
        Assert.Equal(0f, victim.ModelAlpha);
    }

    [Fact]
    public void Dying_drops_whatever_the_character_was_doing()
    {
        var thing = Character(Body());
        thing.Actor!.Action = ActionMode.Swing;
        thing.Actor.BusyUntil = 9_999;
        Damage.Kill(thing, null, HitKind.Melee, DamageType.Lethal, nowMs: 100, damage: 50f);

        Assert.Equal(ActionMode.None, thing.Actor.Action);
        Assert.Equal(0, thing.Actor.BusyUntil);
    }

    [Fact]
    public void A_corpse_is_solid_until_the_fade_starts_and_gone_when_it_ends()
    {
        var a = Body(ActorType.Human);
        var thing = Character(a);
        Damage.Kill(thing, null, HitKind.Melee, DamageType.Lethal, nowMs: 0);

        Assert.Equal(1f, a.Opacity(0));          // not fading yet
        a.FadingFrom = Damage.Corpse.FadeMs;     // what the corpse step does at thirty seconds
        Assert.Equal(1f, a.Opacity(Damage.Corpse.FadeMs));
        Assert.Equal(0.5f, a.Opacity(Damage.Corpse.FadeMs + (int)(Damage.Corpse.FadeDurationMs / 2)), 3);
        Assert.Equal(0f, a.Opacity(Damage.Corpse.FadeMs + (int)Damage.Corpse.FadeDurationMs));

        // A dusting body goes five times faster.
        a.Dusted = true;
        Assert.Equal(0f, a.Opacity(Damage.Corpse.FadeMs + (int)Damage.Corpse.DustFadeMs));
    }

    [Fact]
    public void A_character_that_dies_falls_where_it_stands_and_stays_there()
    {
        // The whole path, in a real level: the death plays on the action channel, holds its last
        // frame (every death mode is `noloop endpause`), and the corpse clocks run from the tick.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON2", things: true, sky: false,
            scene: ChronicleSet.Load(files).ForLevel("P1_MON2")?.Entry?.SceneName, player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var sim = new Simulation(place);
        var thing = place.Player!.Thing;
        var actor = thing.Actor!;

        sim.Advance(1f, 1f / 30f);
        // Standing — or in the idle flourish his mind may roll for him in that second:
        // which roll of the AI's stream is his depends on the order the minds think in.
        Assert.Contains(thing.Animation?.Motion?.Mode?.Name, (string?[])["stand", "idle"]);
        Assert.Null(thing.Animation?.Action);

        Assert.True(Damage.Kill(thing, null, HitKind.Melee, DamageType.Lethal,
                                sim.Milliseconds, damage: 5f, motions: place.Motions));

        // A light blow folds the character up slowly.
        Assert.Equal("deathslow", thing.Animation?.Action?.Mode?.Name);
        int fadeAt = actor.FadeAt;
        Assert.Equal(sim.Milliseconds + Damage.Corpse.PartyFadeMs, fadeAt);

        // The animation runs off the end and stops there - that held frame is the body on the floor.
        sim.Advance(4f, 1f / 30f);
        Assert.True(thing.Animation!.Action!.Held);
        Assert.Equal("deathslow", thing.Animation.Action.Mode.Name);

        // Five seconds in, the tick starts the fade and clears the clock, which is what shuts a
        // TARGET DEAD discipline out.
        sim.Advance(2f, 1f / 30f);
        Assert.Equal(0, actor.FadeAt);
        Assert.NotEqual(0, actor.FadingFrom);
        Assert.False(Damage.CanBeRaised(thing) && thing.Type != ThingType.Player);

        // And by the end of the fade the body is invisible - but still there, because it is ours.
        // What is drawn is the model's alpha, ramped by the same 4.8 s.
        Assert.True(thing.ModelAlpha < 1f);
        sim.Advance(5f, 1f / 30f);
        Assert.Equal(0f, actor.Opacity(sim.Milliseconds));
        Assert.Equal(0f, thing.ModelAlpha);
        Assert.Equal(0, thing.ExpireAt);
        Assert.False(thing.Has(WorldThing.Flag.Remove));
    }

    [Fact]
    public void An_ordinary_bodys_expiry_is_what_takes_it_off_the_floor()
    {
        var thing = Character(Body(ActorType.Human));
        Damage.Kill(thing, null, HitKind.Melee, DamageType.Lethal, nowMs: 0);
        Assert.Equal(Damage.Corpse.ExpireMs, thing.ExpireAt);
        Assert.False(thing.Has(WorldThing.Flag.Remove));
    }

    // --- what a death leaves behind --------------------------------------------------

    /// <summary>
    /// A vampire that dusts leaves a **skeleton standing where it fell**, spawned from
    /// `skeleton_&lt;SKELETON_ID&gt;` and lasting five seconds. The dust was once a flag
    /// on the corpse and nothing more, because nothing could make a thing at run time.
    /// </summary>
    [Fact]
    public void A_dusting_vampire_leaves_a_skeleton_for_five_seconds()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1",
                                  player: "Christof");
        // Whether a *shipped* vampire dusts is settled across all 696 templates elsewhere in this
        // file; what is new here is that the dust leaves something, so the victim is built.
        var victim = place.Spawn("petRat", place.Player!.Thing)!;
        Assert.SkipWhen(victim.Actor is null, "petRat is not a character in this install");
        victim.Actor!.Type = ActorType.Vampire;
        victim.Actor.SkeletonId = 0;
        Assert.True(Damage.Dusts(victim, id => place.Templates!.Has(Damage.SkeletonTemplate(id))));
        int count = place.Things.Count;

        Damage.Kill(victim, null, HitKind.Melee, DamageType.Lethal, nowMs: 1_000);

        Assert.True(victim.Actor!.Dusted);
        // The smoke off its bones (`DustTests`) is counted apart: here only the skeleton.
        Assert.Equal(count + 1, place.Things.Count(t => t.Template != Damage.DustSmoke));
        var bones = place.Things.Last(t => t.Template != Damage.DustSmoke);
        Assert.Equal(Damage.SkeletonTemplate(victim.Actor.SkeletonId), bones.Template);
        Assert.Equal(victim.Position, bones.Position);
        Assert.Equal(1_000 + Damage.Corpse.SkeletonMs, bones.ExpireAt);

        // And it goes on its own, through the same tick that takes any expired thing.
        var sim = new Simulation(place);
        sim.Advance(7f);
        Assert.True(bones.Removed);
    }

    /// <summary>
    /// **A gibbed body leaves nothing.** Sixteen spawns — eight piece indices, each asked for twice
    /// — and then the body itself is destroyed on the spot, so there is no corpse, no outline and
    /// nothing to raise.
    ///
    /// **But the room is left with eighteen chunks, not sixteen**, and that is the engine's, not a
    /// slip. The death effects run the two damage sprays *first* and only then reach the
    /// gib kill, so a blow over 19 has already thrown two `gib_&lt;class&gt;_0` off the
    /// head before the sixteen come off random bones. The two paths compose; nothing dedupes them.
    /// This test asserts the sum **and** its parts, because the sum alone would have gone on passing
    /// if either half quietly stopped working.
    /// </summary>
    [Fact]
    public void A_gibbed_body_is_destroyed_and_replaced_by_its_pieces()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1",
                                  player: "Christof");
        // Build one rather than hunting for a template that gibs: the flag is the whole rule.
        var thing = place.Spawn("petRat", place.Player!.Thing)!;
        Assert.SkipWhen(thing.Actor is null, "petRat is not a character in this install");
        var actor = thing.Actor!;
        actor.Flags |= Actor.Flag.AlwaysGibKill;
        actor.GibClass = 4;                              // the class with all sixteen pieces
        actor.Type = ActorType.Human;                    // so it gibs rather than dusting
        int count = place.Things.Count;

        Damage.Kill(thing, null, HitKind.Melee, DamageType.Lethal, nowMs: 0, damage: 100f);

        Assert.True(thing.Removed);
        Assert.DoesNotContain(thing, place.Things);
        // Two from the death spray (100 damage clears both thresholds), then sixteen from the kill:
        // class 4 ships gib_4_0..12, so all eight indices the loop asks for exist.
        Assert.Equal(2, Damage.SpraysOnDeath(actor, HitKind.Melee, 100f));
        Assert.Equal(count - 1 + 2 + 16, place.Things.Count);
        Assert.All(place.Things.TakeLast(18), p => Assert.StartsWith("gib_4_", p.Template));
        // The spray's two are always piece 0. The kill's sixteen are **eight** distinct templates
        // asked for twice — class 4 ships thirteen, and gib_4_8..12 can never be reached, because
        // the loop counts 0..7 whatever the class has.
        Assert.All(place.Things.Skip(count - 1).Take(2), p => Assert.Equal("gib_4_0", p.Template));
        Assert.Equal(8, place.Things.TakeLast(16).Select(p => p.Template).Distinct().Count());
    }

    /// <summary>
    /// The piece counts are the shipped templates rather than the loop's eight, because a template
    /// that is not there is skipped — and **class 1 ships none at all**, so a class-1 actor comes
    /// apart silently. It is the sort of thing that looks like a bug in a reimplementation and is
    /// simply what the data does.
    /// </summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 8)]
    [InlineData(3, 10)]
    [InlineData(4, 16)]
    [InlineData(5, 8)]
    public void Each_gib_class_sheds_as_many_pieces_as_the_install_ships(int gibClass, int expected)
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1",
                                  player: "Christof");
        var thing = place.Spawn("petRat", place.Player!.Thing)!;
        Assert.SkipWhen(thing.Actor is null, "petRat is not a character in this install");
        thing.Actor!.GibClass = gibClass;

        Assert.Equal(expected, Damage.Gib(thing, thing.Actor));
    }
}
