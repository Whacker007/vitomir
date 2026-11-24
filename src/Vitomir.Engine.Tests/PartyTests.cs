// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Following the leader (and the walk itself).
///
/// Nothing in the shipped scenes carries `THING_AF_PARTY` — the party is assembled at run time by
/// the chronicle's own scripts — so these tests put a character in it the way the game does, by
/// setting the flag and the mode.
/// </summary>
public class PartyTests
{
    internal static (Location Place, Minds Minds, Simulation Sim, Mind Member) Recruit(GameFiles files, PartyStance stance)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1",
                                  player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var leader = place.Player!.Thing;

        // Somebody who walks, put well outside any leash so it has to come and find him.
        var member = minds.All.First(m => m.Mode == AiMode.PedWander);
        // `CodexPlayer.AddToParty` sets both: THING_AF_PARTY through `AddPlayer`, and the
        // formation bit, without which the party think short-circuits before the follow.
        member.Thing.Actor!.Flags |= Actor.Flag.Party;
        member.Thing.Actor.Flags2 |= Actor.Flag2.InFormation;
        // And a companion is a player-type thing: the party think's chain past the fight — hunt,
        // follow, nurse — is a type 7's only.
        member.Thing.Actor.IsPlayerThing = true;
        member.Thing.Actor.Stance = stance;
        member.Enter(AiMode.Party);
        member.Thing.Position = leader.Position;
        member.Walker.Ground();
        return (place, minds, new Simulation(place), member);
    }

    [Fact]
    public void A_party_member_comes_and_stands_by_the_leader()
    {
        using var files = Install.Open();
        var (place, _, sim, member) = Recruit(files, PartyStance.Neutral);
        var leader = place.Player!.Thing;

        // Send it away, then let it think.
        member.Thing.Position = leader.Position + new Vector3(900f, 0f, 0f);
        Assert.True(member.Walker.Ground(), "the test spot is not walkable ground");
        float away = Bodies.Gap(member.Thing, leader);
        Assert.True(away > 800f, $"it only started {away:0} away");

        sim.Advance(40f, 1f / 30f);

        // It closed to inside its leash, and stopped there rather than walking into him.
        float gap = Bodies.Gap(member.Thing, leader);
        Assert.True(gap < Minds.FollowReach * Minds.Leash(PartyStance.Neutral) + 1f,
                    $"it settled {gap:0} away, outside the {Minds.FollowReach:0}-unit leash");
        Assert.True(gap > 0f, "it walked into its leader");
    }

    /// <summary>
    /// Taken out of the formation, a companion stops following. The follow short-circuits on
    /// `InParty` and never reaches the follow, and the shipped manual says the same in a
    /// sentence: *"Once a character is 'out of the group' s/he will no longer follow
    /// the lead character."* This is the same run as the test above with the one bit cleared.
    /// </summary>
    [Fact]
    public void A_party_member_out_of_the_formation_stays_where_it_is_put()
    {
        using var files = Install.Open();
        var (place, _, sim, member) = Recruit(files, PartyStance.Neutral);
        var leader = place.Player!.Thing;
        member.Thing.Actor!.Flags2 &= ~Actor.Flag2.InFormation;

        member.Thing.Position = leader.Position + new Vector3(900f, 0f, 0f);
        Assert.True(member.Walker.Ground(), "the test spot is not walkable ground");
        var stood = member.Thing.Position;

        sim.Advance(40f, 1f / 30f);

        Assert.Equal(stood, member.Thing.Position);
        Assert.True(Bodies.Gap(member.Thing, leader) > 800f, "it followed anyway");
    }

    [Fact]
    public void The_stance_is_how_far_back_it_hangs()
    {
        using var files = Install.Open();
        var settled = new Dictionary<PartyStance, float>();
        foreach (var stance in new[] { PartyStance.Attack, PartyStance.Neutral, PartyStance.Defense })
        {
            var (place, minds, sim, member) = Recruit(files, stance);
            var leader = place.Player!.Thing;
            // This measures the *follow* leash, so the Attack-stance hunt has to be out of the way -
            // otherwise an aggressive companion goes off after the townspeople instead of settling,
            // which is correct behaviour and a different test. The engine has its own switch for it.
            minds.MainFlags |= AiMainFlag.NoPartySearch;
            member.Thing.Position = leader.Position + new Vector3(900f, 0f, 0f);
            member.Walker.Ground();
            sim.Advance(40f, 1f / 30f);
            settled[stance] = Bodies.Gap(member.Thing, leader);
        }

        // Each stance is content at its own distance, and the order is the engine's: an Attack-stance
        // member ranges two and a half times as far out as a Neutral one, a Defense member four
        // fifths as far.
        Assert.True(settled[PartyStance.Attack] > settled[PartyStance.Neutral],
                    $"attack {settled[PartyStance.Attack]:0} is no further out than neutral {settled[PartyStance.Neutral]:0}");
        Assert.True(settled[PartyStance.Defense] <= settled[PartyStance.Neutral] + 1f,
                    $"defence {settled[PartyStance.Defense]:0} is further out than neutral {settled[PartyStance.Neutral]:0}");
        Assert.All(settled, kv => Assert.True(
            kv.Value < Minds.FollowReach * Minds.Leash(kv.Key) + 1f,
            $"{kv.Key} settled {kv.Value:0} away, outside its own {Minds.FollowReach * Minds.Leash(kv.Key):0}-unit leash"));
    }

    [Fact]
    public void The_formation_is_a_fan_of_six_at_two_ranks()
    {
        // The formation: three angles, two radii, and the stance scales the radius.
        Assert.Equal(6, Minds.FormationAngle.Length);
        Assert.Equal(Minds.FormationAngle.Length, Minds.FormationRadius.Length);
        Assert.Equal([0f, 45f, -45f, 0f, 45f, -45f], Minds.FormationAngle);
        Assert.Equal([130f, 130f, 130f, 80f, 80f, 80f], Minds.FormationRadius);
        Assert.Equal(2.5f, Minds.Leash(PartyStance.Attack));
        Assert.Equal(1f, Minds.Leash(PartyStance.Neutral));
        Assert.Equal(0.8f, Minds.Leash(PartyStance.Defense));
    }

    // ---- the party's own combat decision ----

    [Fact]
    public void The_three_stance_scaled_quantities_are_three_different_sets_of_numbers()
    {
        // The trap this asserts against is real and was warned about two batches before the numbers
        // were all known: the follow leash, the fight leash and the health/blood thresholds are each
        // scaled by party stance, and each has its own constants. Merging any two would give a
        // plausible wrong answer, so they are pinned side by side.
        Assert.Equal(2.5f, Minds.Leash(PartyStance.Attack));
        Assert.Equal(1.0f, Minds.Leash(PartyStance.Neutral));
        Assert.Equal(0.8f, Minds.Leash(PartyStance.Defense));

        Assert.Equal(2.5f, Minds.FightLeash(PartyStance.Attack));
        Assert.Equal(1.0f, Minds.FightLeash(PartyStance.Neutral));
        Assert.Equal(0.4f, Minds.FightLeash(PartyStance.Defense));   // not 0.8

        Assert.NotEqual(Minds.Leash(PartyStance.Defense), Minds.FightLeash(PartyStance.Defense));
    }

    [Fact]
    public void A_fight_that_drags_a_member_too_far_from_the_player_is_dropped()
    {
        // The allowance is breakDist scaled by stance, measured straight-line
        // from the player. No shipped .nai sets breakDist, so it is the engine's 500 for everybody -
        // 1,250 / 500 / 200 by stance.
        using var files = Install.Open();
        var (place, minds, _, member) = Recruit(files, PartyStance.Neutral);
        var leader = place.Player!.Thing;

        Assert.Equal(500f, member.Class.BreakDist);

        // A target it is happily fighting, close by.
        var enemy = minds.All.First(m => !ReferenceEquals(m, member) && m.Thing.Actor is not null).Thing;
        member.Aim(enemy, minds.Now);
        Assert.NotNull(member.Target);

        // Now put the member well past its own leash from the player and let it think.
        float allowed = member.Class.BreakDist * Minds.FightLeash(PartyStance.Neutral);
        member.Thing.Position = leader.Position + new Vector3(allowed + 400f, 0f, 0f);
        member.NextThink = minds.Now;
        minds.Tick(0.1f);

        Assert.Null(member.Target);
    }

    [Fact]
    public void The_party_think_delay_carries_no_jitter()
    {
        // The enemy decision adds up to a fifth again; the party one does not. Asserted
        // because it is the kind of difference that gets "tidied" into a shared helper later.
        using var files = Install.Open();
        var (place, minds, _, member) = Recruit(files, PartyStance.Neutral);

        var enemy = minds.All.First(m => !ReferenceEquals(m, member) && m.Thing.Actor is not null).Thing;

        // Think several times and confirm the delay never varies.
        var delays = new HashSet<int>();
        for (int i = 0; i < 8; i++)
        {
            member.Aim(enemy, minds.Now);
            member.Enter(AiMode.Party);
            member.NextThink = minds.Now;
            int before = minds.Now;
            minds.Tick(0.05f);
            if (member.NextThink > before) delays.Add(member.NextThink - minds.Now);
        }

        Assert.NotEmpty(delays);
        Assert.True(delays.Count <= 2, $"the delay varied: {string.Join(", ", delays.Order())}");
    }

    [Fact]
    public void An_attack_stance_companion_picks_its_own_fight_and_the_other_stances_do_not()
    {
        // Play found this gap: Erik on Attack stance stood and did nothing in the Tremere
        // chantry, because the party handler only fought a target something else had given it and
        // nothing ever did. The companion's search is what gives it one.
        using var files = Install.Open();

        foreach (var stance in new[] { PartyStance.Attack, PartyStance.Neutral, PartyStance.Defense })
        {
            var place = Location.Load(files, "p1_arc3", things: true, sky: false, scene: "ARC3_17_1",
                                      player: "Christof");
            var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
            var erik = minds.All.First(m => m.Thing.Template.Equals("erik", StringComparison.OrdinalIgnoreCase));
            erik.Thing.Actor!.Flags |= Actor.Flag.Party;
            erik.Thing.Actor.Stance = stance;
            erik.Enter(AiMode.Party);
            Assert.Null(erik.Target);

            var sim = new Simulation(place);
            sim.Advance(4f, 1f / 30f);

            if (stance == PartyStance.Attack)
                Assert.True(erik.Target is not null, "an Attack-stance companion found nobody to fight");
            else
                Assert.True(erik.Target is null, $"a {stance}-stance companion started a fight on its own");
        }
    }

    [Fact]
    public void The_hunt_reaches_the_ai_files_own_search_distance_not_the_sphere_query()
    {
        // The 2,500-unit sphere in the companion's search is a broad phase; the per-candidate test behind it
        // (the sight test with the range flag set) rejects anything past the class's range. An earlier note in
        // this tree had it the other way round.
        using var files = Install.Open();
        var place = Location.Load(files, "p1_arc3", things: true, sky: false, scene: "ARC3_17_1",
                                  player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var erik = minds.All.First(m => m.Thing.Template.Equals("erik", StringComparison.OrdinalIgnoreCase));

        Assert.True(erik.Class.SearchDist < 2500f,
                    $"parErik.nai searches {erik.Class.SearchDist}, so this test proves nothing");

        erik.Thing.Actor!.Flags |= Actor.Flag.Party;
        erik.Thing.Actor.Stance = PartyStance.Attack;
        erik.Enter(AiMode.Party);

        var sim = new Simulation(place);
        sim.Advance(4f, 1f / 30f);

        if (erik.Target is { } found)
            Assert.True(Vector3.Distance(found.Position, erik.Thing.Position) <= erik.Class.SearchDist + 1f,
                        "it took a target further off than its own searchDist");
    }

    [Fact]
    public void A_companion_that_loses_its_target_stays_in_the_party()
    {
        // In play Erik finished a fight, paced away, stopped, and never followed again. The cause was
        // this: the party combat decision sent him to his base mode when the target went, which is
        // what the *enemy* decision does. The engine's party version only clears the
        // target - a party member stays in `party` for ever, which is the whole point of the mode.
        using var files = Install.Open();
        var (place, minds, sim, member) = Recruit(files, PartyStance.Neutral);
        var leader = place.Player!.Thing;

        // Give it a target, then take that target away the way a death does.
        var victim = minds.All.First(m => !ReferenceEquals(m, member)
                                       && !ReferenceEquals(m.Thing, leader)
                                       && m.Thing.Actor is not null);
        member.Aim(victim.Thing, minds.Now);
        Assert.Equal(AiMode.Party, member.Mode);

        victim.Thing.Actor!.Flags |= Actor.Flag.Dead;
        member.NextThink = minds.Now;
        minds.Tick(0.1f);

        Assert.Null(member.Target);
        Assert.Equal(AiMode.Party, member.Mode);

        // And it goes back to following rather than standing there.
        member.Thing.Position = leader.Position + new Vector3(700f, 0f, 0f);
        Assert.True(member.Walker.Ground(), "the test spot is not walkable ground");
        float away = Bodies.Gap(member.Thing, leader);
        sim.Advance(40f, 1f / 30f);
        Assert.True(Bodies.Gap(member.Thing, leader) < away - 100f,
                    "it never came back to its leader after the fight");
    }

    // ---- getting out of the way (AI event 7) ----

    /// <summary>
    /// Christof and Wilhem, both `THING_TYPE_PLAYER`, which is what event 7 needs at **both** ends.
    /// The Old Town recruit used by the tests above is an ordinary civilian and would fail the
    /// gates — correctly, because only the playable cast shoves the playable cast.
    /// </summary>
    static (Location Place, Minds Minds, Simulation Sim, Mind Wilhem, Mind Christof) Companions(GameFiles files)
    {
        var place = Location.Load(files, "P1_UNIV", things: true, sky: false, scene: "UNIV_7_7",
                                  player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var christof = minds.All.First(m => ReferenceEquals(m.Thing, place.Player!.Thing));
        var wilhem = minds.All.First(m => !ReferenceEquals(m, christof)
                                       && m.Thing.Type == ThingType.Player);
        foreach (var m in new[] { christof, wilhem })
        {
            m.Thing.Actor!.Flags |= Actor.Flag.Party;
            m.Thing.Actor.Flags2 |= Actor.Flag2.InFormation;
            m.Enter(AiMode.Party);
        }
        return (place, minds, new Simulation(place), wilhem, christof);
    }

    [Fact]
    public void A_blocked_party_member_asks_the_people_in_the_way_to_shift()
    {
        // In play Erik was boxed in by three Tremere with no way back to Christof. The engine's
        // answer is event 7: the **blocked** character asks, and the ask carries only
        // who asked.
        using var files = Install.Open();
        var (place, _, _, wilhem, christof) = Companions(files);

        // Moved the engine's way, so the sector index the shout gathers through knows where he is.
        place.Move(christof.Thing, wilhem.Thing.Position + new Vector3(40f, 0f, 0f));
        christof.MakeWayFor = null;

        wilhem.Walker.Jammed?.Invoke();

        Assert.Same(wilhem.Thing, christof.MakeWayFor);
    }

    [Fact]
    public void Nobody_outside_the_party_asks_and_nobody_mid_feed_listens()
    {
        using var files = Install.Open();
        var (place, _, _, wilhem, christof) = Companions(files);
        place.Move(christof.Thing, wilhem.Thing.Position + new Vector3(40f, 0f, 0f));

        // A character that is not in party mode shouts nothing at all (the shout is one mode test).
        wilhem.Enter(AiMode.EnemySearch);
        christof.MakeWayFor = null;
        wilhem.Walker.Jammed?.Invoke();
        Assert.Null(christof.MakeWayFor);

        // Nor does one that is in the party but has lost the flag.
        wilhem.Enter(AiMode.Party);
        wilhem.Thing.Actor!.Flags &= ~Actor.Flag.Party;
        wilhem.Walker.Jammed?.Invoke();
        Assert.Null(christof.MakeWayFor);
        wilhem.Thing.Actor.Flags |= Actor.Flag.Party;

        // And a listener in the middle of a feed does not shift, at either end of it.
        foreach (var busy in new[] { ActionMode.Feeding, ActionMode.WristFeeding })
        {
            christof.Thing.Actor!.Action = busy;
            wilhem.Walker.Jammed?.Invoke();
            Assert.Null(christof.MakeWayFor);
        }

        // With the feed over, it listens again.
        christof.Thing.Actor!.Action = ActionMode.None;
        wilhem.Walker.Jammed?.Invoke();
        Assert.Same(wilhem.Thing, christof.MakeWayFor);
    }

    [Fact]
    public void Being_asked_to_shift_walks_directly_away_from_whoever_asked()
    {
        using var files = Install.Open();
        var (_, minds, sim, wilhem, christof) = Companions(files);

        // Wilhem is the one asked, by a Christof standing just to one side of him.
        christof.Thing.Position = wilhem.Thing.Position + new Vector3(40f, 0f, 0f);
        var before = wilhem.Thing.Position;
        wilhem.MakeWayFor = christof.Thing;
        wilhem.NextThink = minds.Now;

        sim.Advance(3f, 1f / 30f);

        var moved = wilhem.Thing.Position - before;
        moved.Z = 0f;
        Assert.True(moved.Length() > 1f, "it did not move at all");

        var away = before - christof.Thing.Position;
        away.Z = 0f;
        Assert.True(Vector3.Dot(Vector3.Normalize(away), Vector3.Normalize(moved)) > 0.5f,
                    "it stepped somewhere other than away from whoever asked");
        Assert.Null(wilhem.MakeWayFor);
    }

    // ---- the party piling in (AI event 9's party half) ----

    /// <summary>
    /// **An attacker in the thick of a fight rallies the party** — the event handler's gate asks for an
    /// `enemysearch` character by its *home* mode, and one swinging at you is in
    /// `enemy` by its current one. Read as the current mode until then, which made the rally
    /// unreachable from any real fight. In 1.1's first monastery, with
    /// a Cappadocian and a skeleton swinging at a hurt Christof, Wilhem's target swaps
    /// between the two every fifth of a second: each attacker's shout hands the party the other.
    /// </summary>
    [Fact]
    public void An_attacker_in_the_fight_rallies_the_party_by_its_home_mode()
    {
        using var files = Install.Open();
        var (_, minds, _, wilhem, christof) = Companions(files);
        var others = minds.All.Where(m => !ReferenceEquals(m, wilhem) && !ReferenceEquals(m, christof)
                                       && m.Thing.Actor is not null
                                       && m.Class.SearchStyle != Minds.GuardSearchStyle).Take(2).ToArray();
        var (attacker, bystander) = (others[0], others[1]);
        attacker.Thing.Position = christof.Thing.Position + new Vector3(60f, 0f, 0f);
        bystander.Thing.Position = christof.Thing.Position + new Vector3(160f, 0f, 0f);
        wilhem.Thing.Position = christof.Thing.Position + new Vector3(-90f, 0f, 0f);

        // At heart a searcher, swinging now: home mode `enemysearch`, current mode `enemy`.
        attacker.Enter(AiMode.EnemySearch);
        attacker.Enter(AiMode.Enemy, andBase: false);
        attacker.Aim(christof.Thing, minds.Now);
        wilhem.Aim(null, minds.Now);

        minds.Raise(attacker, Minds.AiEvent.Attacked, Minds.AttackShoutRange);

        Assert.NotNull(wilhem.Target);
    }

    /// <summary>And the other side of the same gate: a sender that is not a searcher at heart —
    /// here a wanderer — rallies nobody, whatever mode it is in when it swings.</summary>
    [Fact]
    public void An_attacker_that_is_not_a_searcher_at_heart_rallies_nobody()
    {
        using var files = Install.Open();
        var (_, minds, _, wilhem, christof) = Companions(files);
        var others = minds.All.Where(m => !ReferenceEquals(m, wilhem) && !ReferenceEquals(m, christof)
                                       && m.Thing.Actor is not null
                                       && m.Class.SearchStyle != Minds.GuardSearchStyle).Take(2).ToArray();
        var (attacker, bystander) = (others[0], others[1]);
        attacker.Thing.Position = christof.Thing.Position + new Vector3(60f, 0f, 0f);
        bystander.Thing.Position = christof.Thing.Position + new Vector3(160f, 0f, 0f);

        attacker.Enter(AiMode.PedWander);
        attacker.Enter(AiMode.EnemySearch, andBase: false);
        attacker.Aim(christof.Thing, minds.Now);
        wilhem.Aim(null, minds.Now);

        minds.Raise(attacker, Minds.AiEvent.Attacked, Minds.AttackShoutRange);

        Assert.Null(wilhem.Target);
    }

    [Fact]
    public void An_attack_on_one_party_member_gives_the_others_a_target()
    {
        // The rally, reached from the event-9 case. The offer is refused
        // for anything that is itself in the party, which is what stops companions targeting each
        // other when the 500-unit sweep hands them one another.
        using var files = Install.Open();
        var (place, minds, _, wilhem, christof) = Companions(files);

        // Somebody hostile nearby to be handed over.
        var enemy = minds.All.First(m => !ReferenceEquals(m, wilhem) && !ReferenceEquals(m, christof)
                                      && m.Thing.Actor is not null).Thing;
        enemy.Position = christof.Thing.Position + new Vector3(150f, 0f, 0f);
        wilhem.Thing.Position = christof.Thing.Position + new Vector3(90f, 0f, 0f);
        wilhem.Aim(null, minds.Now);

        minds.Rally(christof, enemy);

        Assert.NotNull(wilhem.Target);
    }

    /// <summary>
    /// **The rally asks whether the victim can make the attacker out** (the sight test, range
    /// flag clear): stealth against the victim's perception first, then the line. An attacker hidden
    /// from the victim brings nobody in; Vitomir asked only the line until then.
    /// </summary>
    [Fact]
    public void An_attacker_hidden_from_the_victim_does_not_bring_the_party_in()
    {
        using var files = Install.Open();
        var (_, minds, _, wilhem, christof) = Companions(files);
        var enemy = minds.All.First(m => !ReferenceEquals(m, wilhem) && !ReferenceEquals(m, christof)
                                      && m.Thing.Actor is not null).Thing;
        enemy.Position = christof.Thing.Position + new Vector3(150f, 0f, 0f);
        wilhem.Thing.Position = christof.Thing.Position + new Vector3(90f, 0f, 0f);
        wilhem.Aim(null, minds.Now);
        christof.Thing.Actor!.Perception = 1;
        enemy.Actor!.Stealth = 3;
        wilhem.Thing.Actor!.Perception = 5;              // the member's own eyes are not asked

        minds.Rally(christof, enemy);
        Assert.Null(wilhem.Target);

        enemy.Actor.Stealth = 1;                         // within his perception: in they come
        minds.Rally(christof, enemy);
        Assert.Same(enemy, wilhem.Target);
    }

    /// <summary>
    /// Somebody out of formation does not answer the rally either. `InParty` is the second test in
    /// The rally's own filter, and the manual says it in the same breath as the follow.
    /// </summary>
    [Fact]
    public void A_companion_out_of_the_formation_does_not_come_to_the_leaders_aid()
    {
        using var files = Install.Open();
        var (_, minds, _, wilhem, christof) = Companions(files);
        var enemy = minds.All.First(m => !ReferenceEquals(m, wilhem) && !ReferenceEquals(m, christof)
                                      && m.Thing.Actor is not null).Thing;
        enemy.Position = christof.Thing.Position + new Vector3(150f, 0f, 0f);
        wilhem.Thing.Position = christof.Thing.Position + new Vector3(90f, 0f, 0f);
        wilhem.Aim(null, minds.Now);
        wilhem.Thing.Actor!.Flags2 &= ~Actor.Flag2.InFormation;

        minds.Rally(christof, enemy);

        Assert.Null(wilhem.Target);
    }

    [Fact]
    public void A_party_member_is_never_offered_as_the_enemy()
    {
        using var files = Install.Open();
        var (_, minds, _, wilhem, christof) = Companions(files);
        wilhem.Aim(null, minds.Now);

        // Christof is in the party, so handing him over as the enemy does nothing at all.
        minds.Rally(christof, christof.Thing);

        Assert.Null(wilhem.Target);
    }

    /// <summary>
    /// **The rally gives each member its pick by the whole set-target**: the
    /// pick is checked, so a dead one — the dead hear a shout like the living — is stored as
    /// nobody; a member that never reacted reacts, and the react does not stop its walk
    /// (the locomotion is put back, not a stop). Earlier, the rally stored the pick alone, so
    /// a steered Christof kept a corpse as his target and no companion ever reacted to a rally.
    /// </summary>
    [Fact]
    public void The_rally_gives_its_pick_by_the_whole_set_target()
    {
        using var files = Install.Open();
        var (place, minds, _, wilhem, christof) = Companions(files);
        var others = minds.All.Where(m => !ReferenceEquals(m, wilhem) && !ReferenceEquals(m, christof)
                                          && m.Thing.Actor is not null).Take(2).ToList();
        var (enemy, corpse) = (others[0].Thing, others[1].Thing);
        enemy.Position = christof.Thing.Position + new Vector3(150f, 0f, 0f);
        corpse.Position = christof.Thing.Position + new Vector3(-150f, 0f, 0f);
        christof.Aim(null, minds.Now);
        Assert.False(christof.HasNoticed);
        var away = christof.Thing.Position + new Vector3(0f, 300f, 0f);
        Assert.True(christof.Walker.WalkTo(away, place.Walk!.Find(christof.Thing.Position), clamp: false));

        minds.Rally(christof, enemy);                                     // the selected member: the pick outright
        Assert.Same(enemy, christof.Target);
        Assert.True(christof.HasNoticed, "the rally's set-target did not react");
        Assert.True(christof.Walker.Walking, "the react stopped the walk");

        corpse.Actor!.Flags |= Actor.Flag.Dead;
        minds.Rally(christof, corpse);                                    // a dead listener's turn
        Assert.Null(christof.Target);                                     // Nobody
    }

    /// <summary>
    /// **The oscillation check counts a take after a drop** (<see cref="Mind.Retakes"/>, ours): a target
    /// handed over for another — the rally does it to the selected member once for every listener of a
    /// shout — is not a drop. Earlier, every change counted, and an idle steered Christof under two
    /// attackers read as a loop in the story sweep (TEU1_22_1, TEU3_22_1, CNVT_7_1).
    /// </summary>
    [Fact]
    public void The_oscillation_check_counts_a_take_after_a_drop_not_a_target_handed_over()
    {
        using var files = Install.Open();
        var (_, minds, _, wilhem, christof) = Companions(files);
        var others = minds.All.Where(m => !ReferenceEquals(m, wilhem) && !ReferenceEquals(m, christof)
                                          && m.Thing.Actor is not null).Take(2).Select(m => m.Thing).ToList();
        christof.Aim(null, minds.Now);
        int retakes = christof.Retakes, changes = christof.Acquisitions;

        christof.Aim(others[0], minds.Now);
        christof.Aim(others[1], minds.Now);
        christof.Aim(others[0], minds.Now);
        Assert.Equal(changes + 3, christof.Acquisitions);
        Assert.Equal(retakes + 1, christof.Retakes);

        christof.Aim(null, minds.Now);
        christof.Aim(others[1], minds.Now);
        Assert.Equal(retakes + 2, christof.Retakes);
    }

    [Fact]
    public void A_defensive_companion_does_not_pile_in_but_the_victim_always_does()
    {
        using var files = Install.Open();
        var (place, minds, _, wilhem, christof) = Companions(files);
        var enemy = minds.All.First(m => !ReferenceEquals(m, wilhem) && !ReferenceEquals(m, christof)
                                      && m.Thing.Actor is not null).Thing;
        enemy.Position = christof.Thing.Position + new Vector3(150f, 0f, 0f);
        wilhem.Thing.Position = christof.Thing.Position + new Vector3(90f, 0f, 0f);

        wilhem.Thing.Actor!.Stance = PartyStance.Defense;
        wilhem.Aim(null, minds.Now);
        christof.Aim(null, minds.Now);

        minds.Rally(christof, enemy);

        // The Defense member stays out of it; the one that was actually attacked does not get to.
        Assert.Null(wilhem.Target);
        Assert.NotNull(christof.Target);
    }

    [Fact]
    public void A_companion_that_is_feeding_is_left_alone()
    {
        using var files = Install.Open();
        var (_, minds, _, wilhem, christof) = Companions(files);
        var enemy = minds.All.First(m => !ReferenceEquals(m, wilhem) && !ReferenceEquals(m, christof)
                                      && m.Thing.Actor is not null).Thing;
        enemy.Position = christof.Thing.Position + new Vector3(150f, 0f, 0f);
        wilhem.Thing.Position = christof.Thing.Position + new Vector3(90f, 0f, 0f);
        wilhem.Aim(null, minds.Now);

        foreach (var busy in new[] { ActionMode.Feeding, ActionMode.WristFeeding })
        {
            wilhem.Thing.Actor!.Action = busy;
            minds.Rally(christof, enemy);
            Assert.Null(wilhem.Target);
        }
    }

    [Fact]
    public void A_companion_too_far_from_the_victim_does_not_hear_about_it()
    {
        using var files = Install.Open();
        var (place, minds, _, wilhem, christof) = Companions(files);
        var enemy = minds.All.First(m => !ReferenceEquals(m, wilhem) && !ReferenceEquals(m, christof)
                                      && m.Thing.Actor is not null).Thing;
        enemy.Position = christof.Thing.Position + new Vector3(150f, 0f, 0f);
        wilhem.Aim(null, minds.Now);

        // Just outside the 1,250 the engine measures from the victim.
        wilhem.Thing.Position = christof.Thing.Position + new Vector3(Minds.RallyRange + 50f, 0f, 0f);
        minds.Rally(christof, enemy);
        Assert.Null(wilhem.Target);

        // And just inside it.
        wilhem.Thing.Position = christof.Thing.Position + new Vector3(Minds.RallyRange - 50f, 0f, 0f);
        minds.Rally(christof, enemy);
        Assert.NotNull(wilhem.Target);
    }

    /// <summary>Somebody for Christof to swing at, 150 units off, nobody's friend and attackable.</summary>
    static Mind Stranger(Minds minds, Mind wilhem, Mind christof, AiMode heart)
    {
        var them = minds.All.First(m => !ReferenceEquals(m, wilhem) && !ReferenceEquals(m, christof)
                                     && m.Thing.Actor is not null && m.Thing.Type == ThingType.Actor);
        them.Thing.Actor!.Flags &= ~Actions.Unattackable;
        them.Thing.Position = christof.Thing.Position + new Vector3(150f, 0f, 0f);
        them.Enter(heart);
        them.Class.SearchStyle = 0;
        wilhem.Thing.Position = christof.Thing.Position + new Vector3(-90f, 0f, 0f);
        wilhem.Aim(null, minds.Now);
        return them;
    }

    /// <summary>
    /// **The player's own attack brings the party in** (the tail of the attack order, calling the rally with
    /// the steered character as the victim). Listed as unpinned since batch 9 and read: the
    /// gate is who the target is, and against an ordinary enemy there is no gate at all.
    /// </summary>
    [Fact]
    public void The_players_own_attack_brings_the_party_in()
    {
        using var files = Install.Open();
        var (place, minds, _, wilhem, christof) = Companions(files);
        var enemy = Stranger(minds, wilhem, christof, AiMode.EnemySearch);
        Assert.Same(christof.Thing, place.Steered);

        var got = Actions.Attack(christof.Thing, enemy.Thing, christof.Thing.Actor!.Weapon, 0, place.Milliseconds);

        Assert.Equal(OrderResult.Started, got);
        // Not necessarily the same one: two times in three a member spreads out onto somebody else
        // within 750 units, and in the university there is always somebody else.
        Assert.NotNull(wilhem.Target);
    }

    /// <summary>
    /// **But not on a townsperson or a guard — unless Ctrl is held.** A target whose home mode is
    /// `pedwander` or whose AI file says `searchStyle 1` is left to the player
    /// alone; Ctrl (`flags &amp; 2`) lets the party in on them too. The manual's name for
    /// Ctrl + left click on a target is *party attack*.
    /// </summary>
    [Fact]
    public void Not_on_a_townsperson_or_a_guard_unless_ctrl_is_held()
    {
        using var files = Install.Open();
        var (place, minds, _, wilhem, christof) = Companions(files);
        var weapon = christof.Thing.Actor!.Weapon;

        var walker = Stranger(minds, wilhem, christof, AiMode.PedWander);
        Actions.Attack(christof.Thing, walker.Thing, weapon, 0, place.Milliseconds);
        Assert.Null(wilhem.Target);

        christof.Thing.Actor.Action = ActionMode.None;
        Actions.Attack(christof.Thing, walker.Thing, weapon, 0, place.Milliseconds, mods: InputModifier.Ctrl);
        Assert.Same(walker.Thing, wilhem.Target);

        // A guard is a searcher at heart, and is let off by its AI file instead.
        var guard = Stranger(minds, wilhem, christof, AiMode.EnemySearch);
        guard.Class.SearchStyle = Minds.GuardSearchStyle;
        christof.Thing.Actor.Action = ActionMode.None;
        Actions.Attack(christof.Thing, guard.Thing, weapon, 0, place.Milliseconds);
        Assert.Null(wilhem.Target);

        christof.Thing.Actor.Action = ActionMode.None;
        Actions.Attack(christof.Thing, guard.Thing, weapon, 0, place.Milliseconds, mods: InputModifier.Ctrl);
        Assert.Same(guard.Thing, wilhem.Target);
    }

    /// <summary>
    /// **Ctrl is also an order to drop what you are doing**: the rally's second argument counts as
    /// the victim being hurt, so a companion that already has a target takes the new one, and on its
    /// own it lets in a companion that is feeding, which nothing else does.
    /// </summary>
    [Fact]
    public void Ctrl_pulls_in_a_companion_that_is_busy_or_feeding()
    {
        using var files = Install.Open();
        var (place, minds, _, wilhem, christof) = Companions(files);
        var weapon = christof.Thing.Actor!.Weapon;
        var enemy = Stranger(minds, wilhem, christof, AiMode.EnemySearch);
        var other = minds.All.First(m => m.Thing.Type == ThingType.Actor && !ReferenceEquals(m, enemy)).Thing;

        wilhem.Aim(other, minds.Now);
        Actions.Attack(christof.Thing, enemy.Thing, weapon, 0, place.Milliseconds);
        Assert.Same(other, wilhem.Target);                    // busy: left to it

        christof.Thing.Actor.Action = ActionMode.None;
        Actions.Attack(christof.Thing, enemy.Thing, weapon, 0, place.Milliseconds, mods: InputModifier.Ctrl);
        Assert.Same(enemy.Thing, wilhem.Target);

        wilhem.Aim(null, minds.Now);
        wilhem.Thing.Actor!.Action = ActionMode.Feeding;
        christof.Thing.Actor.Action = ActionMode.None;
        Actions.Attack(christof.Thing, enemy.Thing, weapon, 0, place.Milliseconds);
        Assert.Null(wilhem.Target);                           // feeding: left alone

        christof.Thing.Actor.Action = ActionMode.None;
        Actions.Attack(christof.Thing, enemy.Thing, weapon, 0, place.Milliseconds, mods: InputModifier.Ctrl);
        Assert.Same(enemy.Thing, wilhem.Target);
    }

    /// <summary>Only the character being steered: a companion's own attack rallies nobody.</summary>
    [Fact]
    public void A_companions_own_attack_rallies_nobody()
    {
        using var files = Install.Open();
        var (place, minds, _, wilhem, christof) = Companions(files);
        var enemy = Stranger(minds, wilhem, christof, AiMode.EnemySearch);
        christof.Aim(null, minds.Now);

        Actions.Attack(wilhem.Thing, enemy.Thing, wilhem.Thing.Actor!.Weapon, 0, place.Milliseconds);

        Assert.Null(christof.Target);
    }
}
