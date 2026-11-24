// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A companion and feeding**, from a play-test on the `SP3` save (Monastery 1,
/// Christof and Wilhem). Two questions, both answered by what 1.1 does:
///
/// <list type="bullet">
/// <item><b>Why Wilhem, with 100 of 120 blood, goes to feed.</b> Not because anything reads
/// Christof's blood: the ladder's short-of-blood test reads the mind's own actor at every
/// call (the mind is the one asking throughout). It is the ladder's **whatever-its-blood** roll — 10%
/// for a party vampire, 5% for anyone else — and the other half of the same
/// reading is the surprise: a character short of blood with danger 3 or less never reaches the feed
/// rung at all. In 1.1 Wilhem at 100 of 120 goes
/// `approachfeed` on the Cappadocian with Christof at 0 blood.</item>
/// <item><b>Whether a companion keeps hitting a victim a friend is feeding on.</b> It does not: the
/// enemy qualifier leaves alone a target in action 9 or 12 whose feeder is a friend and
/// not the looker, and the party fight lets it go. In 1.1 Wilhem's target goes
/// to nothing at his next think after Christof's bite.</item>
/// </list>
/// </summary>
public class PartyFeedTests
{
    /// <summary>
    /// The monastery scene of that save, with Christof steered, one Cappadocian as the victim and
    /// a second made into a party member standing in for Wilhem — on Christof's team, a player-type
    /// party vampire in the attack stance. Everybody else is taken out of the world.
    /// </summary>
    static (Location Place, Minds Minds, Simulation Sim, WorldThing Christof, WorldThing Ally, WorldThing Victim) Stair()
    {
        var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var powers = new DisciplineDefinitions(files);
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), powers);
        var christof = place.Player!.Thing;
        christof.Actor!.Type = ActorType.Vampire;
        var cappadocians = minds.All.Where(m => m.Thing.Template.Equals("cappadocian", StringComparison.OrdinalIgnoreCase))
                                    .Select(m => m.Thing).ToList();
        var victim = cappadocians[0];
        var ally = cappadocians[1];
        victim.Actor!.Stat[(int)ActorStat.Blood] = victim.Actor.BaseStat[(int)ActorStat.Blood] = 80f;
        foreach (var m in minds.All)
            if (m.Thing != victim && m.Thing != christof && m.Thing != ally) m.Thing.Flags |= WorldThing.Flag.Remove;

        var me = ally.Actor!;
        me.Team = christof.Actor.Team;
        me.Type = ActorType.Vampire;
        me.IsPlayerThing = true;
        me.Flags |= Actor.Flag.Party;
        me.Flags2 |= Actor.Flag2.InFormation;
        me.Stance = PartyStance.Attack;
        me.Disciplines.Add(powers["Feed"]!, 0, -1);
        ally.Mind!.Enter(AiMode.Party);
        Assert.False(Sight.Friendly(ally, victim));
        Assert.True(Sight.Friendly(ally, christof));

        place.Move(christof, new Vector3(-296.4f, -587.2f, -367.2f));
        place.Player.Ground();
        place.Move(victim, new Vector3(-220f, -645.4f, -391.7f));
        victim.Mind!.Walker.Ground();
        place.Move(ally, new Vector3(-330f, -560f, -367.2f));
        ally.Mind.Walker.Ground();
        var sim = new Simulation(place);
        sim.Step(1f / 60f);                                   // the removals go
        return (place, minds, sim, christof, ally, victim);
    }

    /// <summary>
    /// The ladder's feed, walked a thousand times each way with `discFeedPercent` at 0 — so the feed
    /// rung fails its own roll every time it is reached and arms its cooldown, which is
    /// how a test sees that it was reached. Christof is nearly dry throughout, as he was in that
    /// save; it makes no difference to Wilhem, because the test reads Wilhem's blood.
    /// </summary>
    [Fact]
    public void A_companion_full_of_blood_still_rolls_to_feed_and_one_short_of_it_does_not()
    {
        var (_, minds, _, christof, ally, victim) = Stair();
        var leader = christof.Actor!;
        leader.Stat[(int)ActorStat.BloodPool] = 110f;
        leader.Stat[(int)ActorStat.Blood] = 20f;
        var mind = ally.Mind!;
        var me = ally.Actor!;
        mind.Class.DiscFeedPercent = 0f;
        me.Stat[(int)ActorStat.BloodPool] = 120f;

        int Reached(int danger)
        {
            mind.Danger = danger;
            int reached = 0;
            for (int i = 0; i < 1000; i++)
            {
                mind.Arm(AiTimer.Feed, 0);
                minds.Reach(mind, victim);
                if (!mind.Ready(AiTimer.Feed, minds.Now)) reached++;
            }
            return reached;
        }

        // 100 of 120, as in the save: not short (the line is 120 × lowblood × 1.2 for the attack
        // stance), so the only way to the rung is the party vampire's 10%.
        me.Stat[(int)ActorStat.Blood] = 100f;
        Assert.InRange(Reached(0), 60, 140);

        // Short of blood with danger 3 or less, the ladder returns before the feed: the
        // rung "danger under 2 and short of blood" behind that return can never be reached.
        me.Stat[(int)ActorStat.Blood] = 5f;
        Assert.Equal(0, Reached(0));
        Assert.Equal(0, Reached(3));

        // And past danger 3 the short-of-blood roll comes in on top: 30% for a party member,
        // then the 10% — 37% in all.
        Assert.InRange(Reached(4), 310, 430);
    }

    /// <summary>
    /// The qualifier's feeding clause on its own: whoever the looker
    /// counts a friend feeding on the target — at the neck (action 9) or the wrist (12) — makes it a
    /// target to leave alone; the feeder itself is not put off its own victim, and a feed by
    /// somebody the looker does not count a friend changes nothing.
    /// </summary>
    [Fact]
    public void A_target_a_friend_is_feeding_on_is_left_alone_but_not_by_the_feeder()
    {
        var (_, _, _, christof, ally, victim) = Stair();
        Assert.False(Minds.FedOnByAFriend(ally, victim));

        var them = victim.Actor!;
        them.Action = ActionMode.FedOn;
        them.Target = christof;
        Assert.True(Minds.FedOnByAFriend(ally, victim));
        Assert.False(Minds.FedOnByAFriend(christof, victim));

        them.Action = ActionMode.WristFedOn;
        Assert.True(Minds.FedOnByAFriend(ally, victim));

        // Fed on by somebody who is not the looker's friend: still fair game.
        ally.Actor!.Team = christof.Actor!.Team + 1;
        Assert.False(Minds.FedOnByAFriend(ally, victim));
    }

    /// <summary>
    /// **The whole of it, in the simulation.** Wilhem's stand-in is fighting the Cappadocian; Christof
    /// bites her; at its next think the party fight lets her go (`SetTarget(0)`) and the
    /// attack-stance hunt passes her over for as long as the feed lasts — in 1.1
    /// Wilhem's target goes to nothing a fraction of a second after the bite. When the feed ends she is a target
    /// again, and the hunt takes her.
    /// </summary>
    [Fact]
    public void A_companion_lets_go_of_a_victim_a_friend_is_feeding_on_and_takes_her_again_after()
    {
        var (_, _, sim, christof, ally, victim) = Stair();
        var mind = ally.Mind!;
        mind.Aim(victim, sim.Milliseconds);
        sim.Advance(1f, 1f / 60f);
        Assert.Same(victim, mind.Target);

        Feeding.Bite(christof, victim, forced: true);
        Assert.Equal(ActionMode.FedOn, victim.Actor!.Action);
        sim.Advance(1f, 1f / 60f);
        Assert.Null(mind.Target);
        Assert.Equal(AiMode.Party, mind.Mode);

        for (int i = 0; i < 20; i++)
        {
            sim.Advance(0.25f, 1f / 60f);
            Assert.Equal(ActionMode.FedOn, victim.Actor.Action);
            Assert.Null(mind.Target);
        }

        Feeding.Stop(christof);
        Assert.NotEqual(ActionMode.FedOn, victim.Actor.Action);
        sim.Advance(2f, 1f / 60f);
        Assert.Same(victim, mind.Target);
    }

    /// <summary>
    /// **A companion's walk up to feed lasts one think** (as in 1.1, where
    /// Wilhem goes `attack → approachfeed` and back to `approachfeed → attack` about 0.55 s
    /// on, one `attackRate` of 500 ms after the think that cast it). The feed rung arms its own
    /// cooldown when it casts, so on the next think the party fight passes the
    /// ladder by and orders the attack, which the order does not refuse — ACTION 7 is not
    /// busy — and which cancels the walk to feed first. Vitomir's party think is
    /// the same, and this pins it: the feed is ordered as the shipped `DisciplineFeed` orders it from
    /// the cast, the rung's cooldown and the think's delay set as the rung and the fight set them.
    /// </summary>
    [Fact]
    public void A_companion_walking_up_to_feed_goes_back_to_attacking_on_its_next_think()
    {
        var (place, minds, sim, _, ally, victim) = Stair();
        place.Move(ally, new Vector3(-700f, -590f, -252f));
        ally.Mind!.Walker.Ground();
        var mind = ally.Mind;
        var me = ally.Actor!;
        me.Stat[(int)ActorStat.Blood] = 20f;
        mind.Aim(victim, minds.Now);
        // The think that cast Feed: the rung's cooldown (discFeedRate), the fight's attackRate.
        mind.Arm(AiTimer.Feed, minds.Now + mind.Class.DiscFeedRate);
        mind.NextThink = minds.Now + mind.Class.AttackRate;
        Assert.Equal(4, Feeding.Order(ally, victim));
        Assert.Equal(ActionMode.ApproachFeed, me.Action);
        int cast = minds.Now;

        while (me.Action == ActionMode.ApproachFeed && minds.Now - cast < 3000) sim.Step(1f / 60f);
        Assert.Equal(ActionMode.Attack, me.Action);
        Assert.Same(victim, me.Target);
        Assert.InRange(minds.Now - cast, mind.Class.AttackRate, mind.Class.AttackRate + 50);
        Assert.True(Bodies.Gap(ally, victim) > Feeding.Reach, "it was cut off on the way, not at the bite");
    }
}

