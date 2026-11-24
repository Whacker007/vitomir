// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Three of a comparison with 1.1's gaps in how a target is taken:
/// <list type="bullet">
/// <item>the enemy search's first sight shouts and thinks at once — the set-target reacts and sets
/// the "reacted" bit before the search tests it — and the
/// Attack-stance hunt reacts and shouts too;</item>
/// <item>setting the same target again re-stamps it;</item>
/// <item>mode 1 is the party think only for a party-flagged actor, and its chain past the
/// fight only for a player-type one.</item>
/// </list>
/// The shout itself (<c>vox_alert</c>) needs a sound device to hear; what is pinned is the state it
/// comes with — the react, and the think asked for now rather than after the wake-up.
/// </summary>
public class FirstSightTests
{
    static (Location Place, Minds Minds, WorldThing Christof) Open(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        return (place, minds, place.Player!.Thing);
    }

    /// <summary>
    /// A character that has never reacted takes Christof on sight: the react runs (it has noticed, the
    /// scripts hear <c>awaken</c>, the wake-up holds it), and then — the bit set — the think is asked
    /// for **now**, not after the <c>wakeupDelay</c>. Earlier, the next think waited out the delay.
    /// </summary>
    [Fact]
    public void The_first_sight_reacts_and_then_thinks_at_once()
    {
        using var files = Install.Open();
        var (place, minds, christof) = Open(files);
        var mind = minds.All.First(m => !ReferenceEquals(m.Thing, christof) && m.Thing.Actor is not null);
        mind.HasNoticed = false;
        mind.Class.WakeupDelay = 1500;
        var woke = new List<WorldThing>();
        place.Awakened = (who, _) => woke.Add(who);

        minds.Aggravate(mind, christof);

        Assert.Same(christof, mind.Target);
        Assert.Equal(AiMode.Enemy, mind.Mode);
        Assert.True(mind.HasNoticed);
        Assert.Contains(mind.Thing, woke);
        Assert.Equal(minds.Now + 1500, mind.BusyUntil);
        Assert.Equal(minds.Now, mind.NextThink);
    }

    /// <summary>
    /// Every set-target stamps the time taken, clears the engaged stamp and asks for a think now,
    /// the same target again included; only the heading waits for a change.
    /// So an enemy handed its target back five seconds in has six seconds from then before it gives
    /// up on a target it never reached. Earlier, the same target was a no-op.
    /// </summary>
    [Fact]
    public void Setting_the_same_target_again_restarts_it()
    {
        using var files = Install.Open();
        var (_, minds, christof) = Open(files);
        var mind = minds.All.First(m => !ReferenceEquals(m.Thing, christof) && m.Thing.Actor is not null);

        mind.Aim(christof, 1000);
        int took = mind.Acquisitions;
        Assert.Equal(1000, mind.TargetAt);
        mind.EngagedAt = 1800;
        mind.NextThink = 9000;
        mind.HeadingKnown = true;

        mind.Aim(christof, 5000);
        Assert.Equal(5000, mind.TargetAt);
        Assert.Equal(0, mind.EngagedAt);
        Assert.Equal(5000, mind.NextThink);
        Assert.True(mind.HeadingKnown);                       // the heading bit clears on a change only
        Assert.Equal(took, mind.Acquisitions);                // and the diagnostic counts changes only

        // Nobody again is a set too.
        mind.Aim(null, 6000);
        mind.NextThink = 9000;
        mind.Aim(null, 7000);
        Assert.Equal(7000, mind.TargetAt);
        Assert.Equal(7000, mind.NextThink);
    }

    /// <summary>
    /// A made companion in the Attack stance, a villager turned enemy beside him and nobody else: the
    /// hunt takes the villager through the ordinary set-target — so a companion that has never
    /// reacted reacts (noticed, <c>awaken</c> to the scripts, held for its wake-up) — and asks for a
    /// think now. Earlier, the hunt aimed and nothing else.
    /// </summary>
    [Fact]
    public void An_attack_stance_companion_reacts_to_the_first_one_it_hunts()
    {
        using var files = Install.Open();
        var (place, minds, christof) = Open(files);
        var member = minds.All.First(m => !ReferenceEquals(m.Thing, christof) && m.Thing.Actor is not null);
        var victim = minds.All.First(m => m != member && !ReferenceEquals(m.Thing, christof) && m.Thing.Actor is not null).Thing;
        foreach (var t in place.Things)
            if (t.Type is ThingType.Actor or ThingType.Player && t != member.Thing && t != victim && t != christof) t.Removed = true;

        var me = member.Thing.Actor!;
        me.Team = christof.Actor!.Team;
        me.IsPlayerThing = true;
        me.Flags |= Actor.Flag.Party;
        me.Flags2 |= Actor.Flag2.InFormation;
        me.Stance = PartyStance.Attack;
        member.Enter(AiMode.Party);
        member.HasNoticed = false;
        member.Class.WakeupDelay = 1500;
        var them = victim.Actor!;
        them.Team = Actor.Teams.EnemyFirst;
        them.Flags &= ~(Actor.Flag.Neutral | Actor.Flag.TalkTo);
        place.Move(victim, member.Thing.Position + new Vector3(40f, 0f, 0f));
        var woke = new List<WorldThing>();
        place.Awakened = (who, _) => woke.Add(who);
        member.NextThink = 0;

        minds.Tick(0.05f);

        Assert.Same(victim, member.Target);
        Assert.True(member.HasNoticed);
        Assert.Contains(member.Thing, woke);
        Assert.True(member.BusyUntil > minds.Now);
        Assert.Equal(minds.Now, member.NextThink);
    }

    /// <summary>
    /// Mode 1 without <c>THING_AF_PARTY</c> is nothing at all (the case breaks out, no think
    /// asked for); with it and a type-0 thing, the party think runs to its combat decision and stops
    /// there — no hunt, even in the Attack stance, with an enemy beside it.
    /// </summary>
    [Fact]
    public void Mode_one_is_the_party_think_only_for_a_party_member_and_its_chain_only_for_a_player_type()
    {
        using var files = Install.Open();
        var (place, minds, christof) = Open(files);
        var member = minds.All.First(m => !ReferenceEquals(m.Thing, christof) && m.Thing.Actor is not null);
        var victim = minds.All.First(m => m != member && !ReferenceEquals(m.Thing, christof) && m.Thing.Actor is not null).Thing;
        foreach (var t in place.Things)
            if (t.Type is ThingType.Actor or ThingType.Player && t != member.Thing && t != victim && t != christof) t.Removed = true;
        var me = member.Thing.Actor!;
        me.Team = christof.Actor!.Team;
        me.Stance = PartyStance.Attack;
        me.Flags2 |= Actor.Flag2.InFormation;
        me.Flags &= ~Actor.Flag.Party;
        me.IsPlayerThing = true;
        member.Enter(AiMode.Party);
        victim.Actor!.Team = Actor.Teams.EnemyFirst;
        victim.Actor.Flags &= ~(Actor.Flag.Neutral | Actor.Flag.TalkTo);
        place.Move(victim, member.Thing.Position + new Vector3(40f, 0f, 0f));

        // Not flagged: nothing, and nothing rescheduled.
        member.NextThink = 0;
        minds.Tick(0.05f);
        Assert.Null(member.Target);
        Assert.Equal(0, member.NextThink);

        // Flagged but type 0: the think runs (it reschedules itself 800 ms on), the hunt does not.
        me.Flags |= Actor.Flag.Party;
        me.IsPlayerThing = false;
        minds.Tick(0.05f);
        Assert.Null(member.Target);
        Assert.Equal(minds.Now + Minds.PartyThink, member.NextThink);

        // A player-type: the hunt takes the villager.
        me.IsPlayerThing = true;
        member.NextThink = 0;
        minds.Tick(0.05f);
        Assert.Same(victim, member.Target);
    }
}
