// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What a character reaches for: the item chooser and the rungs of the
/// discipline ladder that can be checked without a discipline layer.
///
/// The chooser is the half that is real, so it is the half worth testing hardest: it works off the
/// **effects** an item carries rather than the item's type, which means a test has to build items
/// the way the game's own templates do.
/// </summary>
public class CastingTests
{
    static (Location Place, Minds Minds, Mind Member, Actor Me) Recruit(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1",
                                  player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var member = minds.All.First(m => !ReferenceEquals(m.Thing, minds.Steered) && m.Thing.Actor is not null);
        var me = member.Thing.Actor!;
        me.Flags |= Actor.Flag.Party;
        me.Flags2 |= Actor.Flag2.InFormation;
        member.Enter(AiMode.Party);
        return (place, minds, member, me);
    }

    /// <summary>An item carrying one named effect, in a given slot — which is all the chooser reads.</summary>
    static Item Carrying(string effect, int where = Slot.Inventory, string name = "test item") =>
        new() { Name = name, Location = where, Effects = [new ItemEffect(effect, 0, null, 0)] };

    /// <summary>
    /// The same again, with `misc/game.ndd` loaded — which is what the discipline half needs. A
    /// character's disciplines come off the real table, so a test never invents a cost or a range.
    /// </summary>
    static (Location Place, Minds Minds, Mind Member, Actor Me, DisciplineDefinitions Powers) Learn(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1",
                                  player: "Christof");
        var powers = new DisciplineDefinitions(files);
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), powers);
        var member = minds.All.First(m => !ReferenceEquals(m.Thing, minds.Steered) && m.Thing.Actor is not null);
        var me = member.Thing.Actor!;
        me.Flags |= Actor.Flag.Party;
        me.Flags2 |= Actor.Flag2.InFormation;
        member.Enter(AiMode.Party);
        return (place, minds, member, me, powers);
    }

    /// <summary>Teaches a character one discipline off the shipped table.</summary>
    static Discipline Teach(Actor me, DisciplineDefinitions powers, string name, int level = 0, int slot = -1)
    {
        var definition = powers[name];
        Assert.NotNull(definition);
        return me.Disciplines.Add(definition, level, slot);
    }

    /// <summary>
    /// Category 2 is a vial: a stat effect on BLOOD for a vampire, on MANA for a human. The two
    /// shipped effects are `ef_blood` and `ef_mana`, and which one a character wants is the same
    /// `ACTOR_TYPE` branch the status bar makes.
    /// </summary>
    [Fact]
    public void A_vampire_wants_the_blood_vial_and_a_human_the_mana_one()
    {
        using var files = Install.Open();
        var (_, minds, _, me) = Recruit(files);
        var blood = Carrying("ef_increaseblood", name: "vial of vitae");
        var mana = Carrying("ef_increasemana", name: "flask of mana");
        me.Inventory.Add(blood);
        me.Inventory.Add(mana);

        me.Type = ActorType.Vampire;
        Assert.Same(blood, minds.Choose(me, 2));

        me.Type = ActorType.Human;
        Assert.Same(mana, minds.Choose(me, 2));

        // And a healing item is neither of them.
        Assert.Null(minds.Choose(me, 1));
    }

    /// <summary>Category 1 is anything whose effect heals, instantly or over time.</summary>
    [Fact]
    public void A_healing_item_is_chosen_by_its_effect_and_not_by_its_type()
    {
        using var files = Install.Open();
        var (_, minds, _, me) = Recruit(files);
        var effects = new EffectDefinitions(files);
        // Take a real healing row out of the game's own file rather than naming one.
        var heal = effects.File!.All.First(e => e.Kind is EffectKind.HealInstant or EffectKind.HealOverTime);
        var salve = Carrying(heal.Name, name: "salve");
        me.Inventory.Add(salve);
        me.Inventory.Add(Carrying("ef_increaseblood"));

        Assert.Same(salve, minds.Choose(me, 1));
    }

    /// <summary>
    /// The chooser asks what an effect's **kind and stat** are and never what its sign is, so
    /// `ef_decreaseblood` — an item that drains you — is as good a candidate as `ef_increaseblood`
    /// when a character decides it is short of blood. The original's, and pinned here so nobody
    /// "fixes" it by accident.
    /// </summary>
    [Fact]
    public void An_item_that_drains_blood_counts_as_something_to_drink()
    {
        using var files = Install.Open();
        var (_, minds, _, me) = Recruit(files);
        me.Type = ActorType.Vampire;
        var cursed = Carrying("ef_decreaseblood", name: "cursed flask");
        me.Inventory.Add(cursed);

        Assert.Same(cursed, minds.Choose(me, 2));
    }

    /// <summary>
    /// `ITEM_FLAGS` bit `0x10000` takes an item out of the AI's reach entirely — the first test the
    /// chooser makes, before anything about the item's effects.
    /// </summary>
    [Fact]
    public void An_item_marked_no_party_ai_use_is_never_chosen()
    {
        using var files = Install.Open();
        var (_, minds, _, me) = Recruit(files);
        me.Type = ActorType.Vampire;
        var vial = Carrying("ef_increaseblood");
        vial.Flags |= ItemFlags.NoPartyAiUse;
        me.Inventory.Add(vial);

        Assert.Null(minds.Choose(me, 2));
    }

    /// <summary>
    /// A quick-slot item is entered in the candidate list five times instead of once, for a
    /// player-type character only. With one of each in the pack, the quick one wins five draws in
    /// six — and because the winner is picked by the **clock** rather than by a random number, the
    /// six draws are exactly reproducible.
    /// </summary>
    [Fact]
    public void A_quick_slot_item_is_five_times_as_likely_and_only_for_a_party_member()
    {
        using var files = Install.Open();
        var (_, minds, member, me) = Recruit(files);
        me.Type = ActorType.Vampire;
        var quick = Carrying("ef_increaseblood", Slot.QuickUse, "quick vial");
        var packed = Carrying("ef_increaseblood", Slot.Inventory, "packed vial");
        me.Inventory.Add(packed);
        me.Inventory.Add(quick);

        // The inventory pushes on the front, so the list is [quick, packed] and the pool is
        // [quick x5, packed]. Six consecutive milliseconds take each entry once.
        me.IsPlayerThing = true;
        var drawn = new List<Item?>();
        for (int i = 0; i < 6; i++) { minds.Tick(0.001f); drawn.Add(minds.Choose(me, 2)); }
        Assert.Equal(5, drawn.Count(d => ReferenceEquals(d, quick)));
        Assert.Equal(1, drawn.Count(d => ReferenceEquals(d, packed)));

        // An enemy gets no such weighting: two candidates, one each.
        me.IsPlayerThing = false;
        drawn.Clear();
        for (int i = 0; i < 6; i++) { minds.Tick(0.001f); drawn.Add(minds.Choose(me, 2)); }
        Assert.Equal(3, drawn.Count(d => ReferenceEquals(d, quick)));
        Assert.Equal(3, drawn.Count(d => ReferenceEquals(d, packed)));
        _ = member;
    }

    /// <summary>
    /// `UseQuickItems` confines a party member to its quick slots and leaves an enemy alone — the
    /// player's own Control Options switch, which the engine keeps in the AI's main flags word.
    /// </summary>
    [Fact]
    public void Quick_items_only_binds_a_party_member_and_not_an_enemy()
    {
        using var files = Install.Open();
        var (_, minds, _, me) = Recruit(files);
        me.Type = ActorType.Vampire;
        var packed = Carrying("ef_increaseblood", Slot.Inventory, "packed vial");
        me.Inventory.Add(packed);
        minds.MainFlags |= AiMainFlag.QuickItemsOnly;

        me.IsPlayerThing = true;
        Assert.Null(minds.Choose(me, 2));

        me.IsPlayerThing = false;
        Assert.Same(packed, minds.Choose(me, 2));
    }

    /// <summary>
    /// A badly hurt companion with nothing else to do drinks: the tail of the party think.
    /// It needs both thresholds — badly hurt *and* short of blood — before it reaches
    /// for a vial.
    /// </summary>
    [Fact]
    public void A_badly_hurt_companion_short_of_blood_drinks_a_vial()
    {
        using var files = Install.Open();
        var (place, minds, member, me) = Recruit(files);
        me.Type = ActorType.Vampire;
        me.IsPlayerThing = true;       // the party think's tail is a player-type's only
        var vial = Carrying("ef_increaseblood", Slot.Inventory, "vial of vitae");
        vial.Amount = 5;
        me.Inventory.Add(vial);
        // Stand it by the leader so the follow is satisfied and the tail is reached.
        member.Thing.Position = place.Player!.Thing.Position + new Vector3(60f, 0f, 0f);
        member.Walker.Ground();

        // Healthy: nothing happens.
        var sim = new Simulation(place);
        sim.Advance(1f, 1f / 30f);
        Assert.NotEqual(AiReach.BloodItem, minds.LastReach.Reach);

        // Badly hurt and nearly dry.
        me.Health = me.MaxHealth * 0.05f;
        me.Stat[(int)ActorStat.BloodPool] = 20f;
        me.Stat[(int)ActorStat.Blood] = 1f;
        sim.Advance(3f, 1f / 30f);

        Assert.Equal(AiReach.BloodItem, minds.LastReach.Reach);
        Assert.Same(vial, minds.LastReach.Item);
        // The whole chain, in one assertion: the ladder decided, the chooser picked, the use path
        // ran, the effect landed and the tick kept it. Nothing about this works if any link is out.
        Assert.True(me.Stat[(int)ActorStat.Blood] > 1f, "it reached for the vial and nothing happened");
    }

    /// <summary>
    /// And it is a real drink now: the item's effects land on the drinker and
    /// a charge goes. `ef_increaseblood` is an instant base-stat effect, so the blood shows up in
    /// the base row straight away.
    /// </summary>
    [Fact]
    public void Drinking_the_vial_puts_its_effect_on_the_drinker_and_spends_a_charge()
    {
        using var files = Install.Open();
        var (place, minds, member, me) = Recruit(files);
        me.Type = ActorType.Vampire;
        var vial = Carrying("ef_increaseblood", Slot.Inventory, "vial of vitae");
        vial.Amount = 2;
        me.Inventory.Add(vial);
        me.Stat[(int)ActorStat.BloodPool] = 20f;
        me.BaseStat[(int)ActorStat.Blood] = 1f;
        me.Stat[(int)ActorStat.Blood] = 1f;

        var used = Items.Use(member.Thing, vial, minds.Now, new EffectDefinitions(files).File);

        Assert.Equal(UseResult.Used, used);
        Assert.Equal(1, vial.Amount);
        Assert.True(me.BaseStat[(int)ActorStat.Blood] > 1f, "the vial did nothing");
        _ = place;
    }

    /// <summary>
    /// A weapon or a scroll is "used" by going to the hand: no charge, no effects — but only a
    /// **thrown** weapon: a sword is wearable, and a wearable item that is not
    /// thrown is never used at all, silently (`RpgGapTests` has the rest).
    /// </summary>
    [Fact]
    public void A_weapon_that_is_not_thrown_is_not_used_at_all()
    {
        using var files = Install.Open();
        var (_, minds, member, me) = Recruit(files);
        var blade = new Item { Name = "sword", Type = ItemType.Weapon, Amount = 1 };
        me.Inventory.Add(blade);

        Assert.Equal(UseResult.Refused, Items.Use(member.Thing, blade, minds.Now, null));
        Assert.Equal(1, blade.Amount);
        Assert.Null(me.Held);
    }

    /// <summary>
    /// The last charge either destroys the item or leaves it in the pack marked
    /// <see cref="ItemFlags.Spent"/> — and a spent item is refused for ever after.
    /// </summary>
    [Fact]
    public void The_last_charge_spends_the_item_or_destroys_it()
    {
        using var files = Install.Open();
        var (_, minds, member, me) = Recruit(files);
        var defs = new EffectDefinitions(files).File;

        var keeps = Carrying("ef_increaseblood", name: "flask");
        keeps.Amount = 1;
        me.Inventory.Add(keeps);
        Assert.Equal(UseResult.UsedUp, Items.Use(member.Thing, keeps, minds.Now, defs));
        Assert.False(keeps.Destroyed);
        Assert.True((keeps.Flags & ItemFlags.Spent) != 0);
        Assert.Equal(UseResult.Refused, Items.Use(member.Thing, keeps, minds.Now, defs));

        var goes = Carrying("ef_increaseblood", name: "single-use flask");
        goes.Amount = 1;
        goes.Flags |= ItemFlags.DestroyedAtZeroCharges;
        me.Inventory.Add(goes);
        Assert.Equal(UseResult.UsedUp, Items.Use(member.Thing, goes, minds.Now, defs));
        Assert.True(goes.Destroyed);
    }

    /// <summary>
    /// An item's restrictions bind a **player-type** character only (the use test returns true
    /// immediately for anything else), so a guard can use what a party member cannot.
    /// </summary>
    [Fact]
    public void Restrictions_bind_a_party_member_and_not_an_enemy()
    {
        using var files = Install.Open();
        var (_, minds, member, me) = Recruit(files);
        var defs = new EffectDefinitions(files).File;
        me.Type = ActorType.Vampire;
        var holy = new Item
        {
            Name = "holy relic", Amount = 5,
            Effects = [new ItemEffect("ef_increaseblood", 0, null, 0)],
            Restrictions = [new ItemRestriction(RestrictionType.Human, 0f)],
        };
        me.Inventory.Add(holy);

        me.IsPlayerThing = true;
        Assert.Equal(UseResult.Refused, Items.Use(member.Thing, holy, minds.Now, defs));

        me.IsPlayerThing = false;
        Assert.Equal(UseResult.Used, Items.Use(member.Thing, holy, minds.Now, defs));
    }

    /// <summary>
    /// Heal is one cooldown for both the spell and the salve, so drinking one stops
    /// the other — the engine's, and the kind of thing that looks like a bug until you see the
    /// single slot.
    /// </summary>
    [Fact]
    public void The_heal_item_and_the_heal_cast_share_one_cooldown()
    {
        using var files = Install.Open();
        var (_, _, member, _) = Recruit(files);
        Assert.True(member.Ready(AiTimer.Heal, 0));
        member.Arm(AiTimer.Heal, 5000);
        Assert.False(member.Ready(AiTimer.Heal, 4999));
        Assert.True(member.Ready(AiTimer.Heal, 5000));
        // And it is genuinely one slot of eight, not eight slots that happen to agree.
        Assert.True(member.Ready(AiTimer.Blood, 0));
        Assert.True(member.AnyReady(0));
    }

    /// <summary>
    /// Nothing is reached for at all while the master flag is set, and the character is told not to
    /// ask again for a second — which is the engine's way of making a switched-off AI cheap.
    /// </summary>
    [Fact]
    public void The_master_flag_stops_the_whole_ladder()
    {
        using var files = Install.Open();
        var (_, minds, member, me) = Recruit(files);
        me.Type = ActorType.Vampire;
        me.Inventory.Add(Carrying("ef_increaseblood"));
        me.Health = me.MaxHealth * 0.05f;
        me.Stat[(int)ActorStat.BloodPool] = 20f;
        me.Stat[(int)ActorStat.Blood] = 1f;
        minds.MainFlags |= AiMainFlag.NoPartyCast;
        me.IsPlayerThing = true;

        Assert.Equal(AiReach.None, minds.Reach(member, null).Reach);
        Assert.True(member.NextThink >= minds.Now + 1000);
    }

    /// <summary>
    /// The discipline chooser asks `game.ndd` for a **category**, and the category is that file's own
    /// `type` column — so which discipline answers a rung is data, not code. Three disciplines, three
    /// rungs, one each.
    /// </summary>
    [Fact]
    public void Each_rung_gets_the_discipline_whose_table_type_matches_it()
    {
        using var files = Install.Open();
        var (_, minds, member, me, powers) = Learn(files);
        me.Type = ActorType.Vampire;
        me.Stat[(int)ActorStat.BloodPool] = 100f;
        me.Stat[(int)ActorStat.Blood] = 100f;
        var heal = Teach(me, powers, "BloodHealing");
        var buff = Teach(me, powers, "Celerity");
        var damage = Teach(me, powers, "Fireball");

        Assert.Same(heal, minds.Pick(member.Thing, (int)DisciplineType.Heal, null));
        Assert.Same(buff, minds.Pick(member.Thing, (int)DisciplineType.Buff, null));
        Assert.Same(damage, minds.Pick(member.Thing, Minds.DiscDamageFrom, null));
        // And a rung nobody answers gets nothing rather than a near miss.
        Assert.Null(minds.Pick(member.Thing, (int)DisciplineType.Summon, null));
        Assert.Null(minds.Pick(member.Thing, (int)DisciplineType.Feed, null));
    }

    /// <summary>
    /// The damage rung asks for **10 and means "anything above 9"**, and the table's header reserves
    /// 7, 8 and 9 — so the band is exactly DAMAGE, MIND and FRENZY. The consequence is worth having
    /// a test for: a character whose only offensive power is Mesmerize will have it cast at it by the
    /// *damage* rung, and a buff rung will not touch it.
    /// </summary>
    [Fact]
    public void The_damage_rung_also_casts_mind_and_frenzy_disciplines()
    {
        using var files = Install.Open();
        var (_, minds, member, me, powers) = Learn(files);
        me.Type = ActorType.Vampire;
        me.Stat[(int)ActorStat.BloodPool] = 100f;
        me.Stat[(int)ActorStat.Blood] = 100f;
        var mind = Teach(me, powers, "Mesmerize");
        Assert.Equal(DisciplineType.Mind, mind.Type);

        Assert.Same(mind, minds.Pick(member.Thing, Minds.DiscDamageFrom, null));
        Assert.Null(minds.Pick(member.Thing, (int)DisciplineType.Buff, null));

        // Frenzy too, and it is the same rung rather than one of its own — so with both learnt the
        // damage rung's pool has two entries and the clock alternates between them.
        var frenzy = Teach(me, powers, "Passion");
        Assert.Equal(DisciplineType.Frenzy, frenzy.Type);
        var drawn = new List<Discipline?>();
        for (int i = 0; i < 2; i++) { minds.Tick(0.001f); drawn.Add(minds.Pick(member.Thing, Minds.DiscDamageFrom, null)); }
        Assert.Contains(mind, drawn);
        Assert.Contains(frenzy, drawn);
    }

    /// <summary>
    /// A discipline it cannot pay for is not a candidate at all — and the instance bit `0x2` waives
    /// the cost outright rather than discounting it, which is how a scripted character casts
    /// something it could never afford.
    /// </summary>
    [Fact]
    public void What_it_cannot_afford_is_not_chosen_unless_the_cast_is_free()
    {
        using var files = Install.Open();
        var (_, minds, member, me, powers) = Learn(files);
        me.Type = ActorType.Vampire;
        var celerity = Teach(me, powers, "Celerity");
        Assert.Equal(30, celerity.Cost);

        me.Stat[(int)ActorStat.BloodPool] = 100f;
        me.Stat[(int)ActorStat.Blood] = 29f;
        Assert.Null(minds.Pick(member.Thing, (int)DisciplineType.Buff, null));

        // Exactly the cost is enough — the test is `cost > blood`, not `>=`.
        me.Stat[(int)ActorStat.Blood] = 30f;
        Assert.Same(celerity, minds.Pick(member.Thing, (int)DisciplineType.Buff, null));

        me.Stat[(int)ActorStat.Blood] = 0f;
        Assert.Null(minds.Pick(member.Thing, (int)DisciplineType.Buff, null));
        celerity.Flags |= Discipline.Free;
        Assert.Equal(0, celerity.Cost);
        Assert.Same(celerity, minds.Pick(member.Thing, (int)DisciplineType.Buff, null));
    }

    /// <summary>
    /// Blood of Potency is a <c>BLOODCOST</c> multiplier, and the chooser applies it before it asks
    /// whether the caster can pay — so the same character with the same blood can afford Celerity
    /// with it up and not without. The multiplier is the shipped one (0.9 at the first level), and
    /// it is a real effect off `effects.ned` rather than a number invented here.
    /// </summary>
    [Fact]
    public void The_blood_cost_multiplier_decides_what_it_can_afford()
    {
        using var files = Install.Open();
        var (_, minds, member, me, powers) = Learn(files);
        var effects = new EffectDefinitions(files);
        me.Type = ActorType.Vampire;
        Teach(me, powers, "Celerity");                      // 30 blood at every level
        me.Stat[(int)ActorStat.BloodPool] = 100f;
        me.Stat[(int)ActorStat.Blood] = 28f;
        Assert.Null(minds.Pick(member.Thing, (int)DisciplineType.Buff, null));

        var potency = effects.File!["ef_disc_bloodofpotency"]!;
        Assert.Equal(EffectKind.BloodCost, potency.Kind);
        EffectRuntime.Apply(member.Thing, potency, 0, minds.Now, durationMs: 60_000);
        Assert.Equal(0.9f, member.Thing.Effects.Value(EffectKind.BloodCost), 3);

        // 30 x 0.9 = 27, which 28 covers.
        Assert.NotNull(minds.Pick(member.Thing, (int)DisciplineType.Buff, null));
    }

    /// <summary>
    /// A human pays in mana, the same `ACTOR_TYPE` branch the item chooser and the status bar both
    /// make — so a mortal ally with an empty mana pool casts nothing however much blood it has.
    /// </summary>
    [Fact]
    public void A_human_pays_for_a_discipline_out_of_mana()
    {
        using var files = Install.Open();
        var (_, minds, member, me, powers) = Learn(files);
        Teach(me, powers, "Celerity");
        me.Stat[(int)ActorStat.Blood] = 100f;
        me.Stat[(int)ActorStat.Mana] = 0f;

        me.Type = ActorType.Vampire;
        Assert.NotNull(minds.Pick(member.Thing, (int)DisciplineType.Buff, null));

        me.Type = ActorType.Human;
        Assert.Null(minds.Pick(member.Thing, (int)DisciplineType.Buff, null));
        me.Stat[(int)ActorStat.Mana] = 30f;
        Assert.NotNull(minds.Pick(member.Thing, (int)DisciplineType.Buff, null));
    }

    /// <summary>
    /// `maxDist` is the AI's reach, and it is checked against the **target** rather than against
    /// anything the caster is doing — so Fireball (1000) drops out of the pool at 1500 away while
    /// Celerity (9999, cast on yourself) never does.
    /// </summary>
    [Fact]
    public void A_discipline_out_of_its_range_is_not_chosen()
    {
        using var files = Install.Open();
        var (place, minds, member, me, powers) = Learn(files);
        me.Type = ActorType.Vampire;
        me.Stat[(int)ActorStat.BloodPool] = 100f;
        me.Stat[(int)ActorStat.Blood] = 100f;
        var fireball = Teach(me, powers, "Fireball");
        Assert.Equal(1000f, fireball.Definition.MaxDistance);

        var target = place.Player!.Thing;
        member.Thing.Position = target.Position + new Vector3(500f, 0f, 0f);
        Assert.Same(fireball, minds.Pick(member.Thing, Minds.DiscDamageFrom, target));

        member.Thing.Position = target.Position + new Vector3(1500f, 0f, 0f);
        Assert.Null(minds.Pick(member.Thing, Minds.DiscDamageFrom, target));
        // With nothing to point at, range never comes up.
        Assert.Same(fireball, minds.Pick(member.Thing, Minds.DiscDamageFrom, null));
    }

    /// <summary>
    /// The manual's *"the Disciplines you place in the Quick Use Slots are more likely to be used by
    /// the AI than any other powers"*, as the number it actually is: a quick-slot discipline goes
    /// into the pool **five times**, for a player-type character only, and the winner comes off the
    /// clock — so six consecutive milliseconds take each entry exactly once.
    /// </summary>
    [Fact]
    public void A_quick_slot_discipline_is_five_times_as_likely_and_only_for_a_party_member()
    {
        using var files = Install.Open();
        var (_, minds, member, me, powers) = Learn(files);
        me.Type = ActorType.Vampire;
        me.Stat[(int)ActorStat.BloodPool] = 100f;
        me.Stat[(int)ActorStat.Blood] = 100f;
        var quick = Teach(me, powers, "Celerity", slot: 0);
        var known = Teach(me, powers, "Potence");

        me.IsPlayerThing = true;
        var drawn = new List<Discipline?>();
        for (int i = 0; i < 6; i++) { minds.Tick(0.001f); drawn.Add(minds.Pick(member.Thing, (int)DisciplineType.Buff, null)); }
        Assert.Equal(5, drawn.Count(d => ReferenceEquals(d, quick)));
        Assert.Equal(1, drawn.Count(d => ReferenceEquals(d, known)));

        me.IsPlayerThing = false;
        drawn.Clear();
        for (int i = 0; i < 6; i++) { minds.Tick(0.001f); drawn.Add(minds.Pick(member.Thing, (int)DisciplineType.Buff, null)); }
        Assert.Equal(3, drawn.Count(d => ReferenceEquals(d, quick)));
        Assert.Equal(3, drawn.Count(d => ReferenceEquals(d, known)));
    }

    /// <summary>
    /// `UseQuickDisciplines` is the twin of the item switch: it confines a party member to what is in
    /// its six slots and leaves an enemy alone.
    /// </summary>
    [Fact]
    public void Quick_disciplines_only_binds_a_party_member_and_not_an_enemy()
    {
        using var files = Install.Open();
        var (_, minds, member, me, powers) = Learn(files);
        me.Type = ActorType.Vampire;
        me.Stat[(int)ActorStat.BloodPool] = 100f;
        me.Stat[(int)ActorStat.Blood] = 100f;
        var packed = Teach(me, powers, "Celerity");
        minds.MainFlags |= AiMainFlag.QuickDisciplinesOnly;

        me.IsPlayerThing = true;
        Assert.Null(minds.Pick(member.Thing, (int)DisciplineType.Buff, null));

        me.IsPlayerThing = false;
        Assert.Same(packed, minds.Pick(member.Thing, (int)DisciplineType.Buff, null));

        // And putting it in a slot satisfies the switch.
        me.IsPlayerThing = true;
        packed.QuickSlot = 3;
        Assert.Same(packed, minds.Pick(member.Thing, (int)DisciplineType.Buff, null));
    }

    /// <summary>
    /// The item rungs live **inside** the discipline ladder, which is the gate worth pinning: the
    /// ladder returns before it looks at anything when a character has neither a discipline nor an
    /// inventory, so a character with vials and no disciplines never drinks one. The engine's own
    /// discipline-list test, and it means teaching a companion one useless power turns its item use on.
    /// </summary>
    [Fact]
    public void A_character_with_no_disciplines_never_walks_the_ladder_however_full_its_pack()
    {
        using var files = Install.Open();
        var (_, minds, member, me, powers) = Learn(files);
        me.Type = ActorType.Vampire;
        me.IsPlayerThing = true;
        Assert.False(me.HasDisciplines);
        Assert.Equal(AiReach.None, minds.Reach(member, null).Reach);

        // And a pack full of vials does not change that. The gate is the discipline list alone, ANDed
        // with the rest of the entry test — there is no inventory alternative anywhere
        // in the function, which is the whole force of "the item rungs live inside the ladder".
        me.Health = me.MaxHealth * 0.05f;
        me.Stat[(int)ActorStat.BloodPool] = 100f;
        me.Stat[(int)ActorStat.Blood] = 1f;
        var vial = Carrying("ef_increaseblood", name: "vial of vitae");
        vial.Amount = 5;
        me.Inventory.Add(vial);
        Assert.Equal(AiReach.None, minds.Reach(member, null).Reach);
        Assert.Equal(5, vial.Amount);

        // Teach it one useless power and the ladder runs — and *now* the vials are in reach too.
        me.Stat[(int)ActorStat.Blood] = 100f;
        Teach(me, powers, "BloodHealing");
        Assert.True(me.HasDisciplines);

        var reached = minds.Reach(member, null);
        Assert.Equal(AiReach.HealCast, reached.Reach);
        Assert.Equal("BloodHealing", reached.Wanted);
        // And it is cast: the rung's order goes through the player's own cast order
        // with Space held, and the character is now in the cast action.
        Assert.True(reached.Acted);
        Assert.Equal(ActionMode.Cast, me.Action);
    }

    /// <summary>
    /// `AIMAIN_FLAG_NOPARTYCOMBAT` stops a companion's swing, not its ladder: 1.1's party fight runs
    /// the discipline and item decision first and tests the flag after it, so a hurt
    /// companion told not to fight still heals itself (a comparison with 1.1).
    /// </summary>
    [Fact]
    public void No_party_combat_stops_the_swing_but_not_the_ladder()
    {
        using var files = Install.Open();
        var (_, minds, member, me, powers) = Learn(files);
        me.Type = ActorType.Vampire;
        me.IsPlayerThing = true;
        me.Stat[(int)ActorStat.BloodPool] = 100f;
        me.Stat[(int)ActorStat.Blood] = 100f;
        Teach(me, powers, "BloodHealing");
        me.Health = me.MaxHealth * 0.05f;
        var enemy = minds.All.First(m => !ReferenceEquals(m, member) && !ReferenceEquals(m.Thing, minds.Steered)
                                         && m.Thing.Actor is { } a && !a.Has(Actor.Flag.Party));
        // Made an enemy the member will not leave alone: another team, not neutral, not to be talked to.
        enemy.Thing.Actor!.Team = Actor.Teams.EnemyFirst;
        enemy.Thing.Actor.Flags &= ~Minds.LeaveAloneFlags;
        member.Class.BreakDist = 0f;                     // no fight leash
        member.Aim(enemy.Thing, minds.Now);
        minds.MainFlags |= AiMainFlag.NoPartyCombat;
        foreach (var m in minds.All) { m.NextThink = int.MaxValue; m.NextIdle = int.MaxValue; }
        member.NextThink = 0;

        minds.Tick(0.001f);

        Assert.Equal(AiReach.HealCast, minds.LastReach.Reach);
        Assert.Equal(ActionMode.Cast, me.Action);
    }
}
