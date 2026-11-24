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
/// Feeding (the bite, the drain, letting go and
/// draining to death) and frenzy (its start, its end, and the script call
/// that starts it), on the shipped Prague street with Christof and its people, minds awake.
/// </summary>
public class FeedingTests
{
    static (Location Place, WorldThing Christof, WorldThing Other) Open(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Steered!;
        var me = christof.Actor!;
        me.Type = ActorType.Vampire;
        me.Stat[(int)ActorStat.BloodPool] = 100f;
        me.Stat[(int)ActorStat.Blood] = 20f;
        me.BaseStat[(int)ActorStat.Blood] = 20f;
        me.Stat[(int)ActorStat.BloodRating] = 2f;
        var other = minds.All.First(m => !ReferenceEquals(m.Thing, christof) && m.Thing.Actor is not null).Thing;
        other.Actor!.Team = me.Team + 1;
        other.Actor.Stat[(int)ActorStat.BloodPool] = 40f;
        other.Actor.Stat[(int)ActorStat.Blood] = 40f;
        other.Actor.BaseStat[(int)ActorStat.Blood] = 40f;
        return (place, christof, other);
    }

    /// <summary>
    /// **Full blood refuses a bystander, and only a bystander** (target `NEUTRAL` or
    /// `TALKTO`, and the feeder's pool not above its blood). A play-test, against a video of
    /// 1.1, where any of the vampires can feed on anyone however full their blood is: the Lady was
    /// in `OLDT_7_7`, where she is an ordinary `civ05younglady` — no bystander flag at all, so the rule
    /// never applied; the whole scene has no feedable bystander in it. The first night in Old Town
    /// (`OLDT_7_1`) has the `_pe` bystander versions, and there a full Christof is turned away.
    /// </summary>
    [Theory]
    [InlineData("OLDT_7_7", "civ05younglady", 4)]      // an ordinary townswoman: fed on, full or not
    [InlineData("OLDT_7_1", "civ05younglady_pe", 1)]   // the bystander version: refused while full
    public void A_full_vampire_is_refused_a_bystander_and_nobody_else(string scene, string template, int full)
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OLDT", things: true, sky: false, scene: scene, player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Steered!;
        var me = christof.Actor!;
        me.Type = ActorType.Vampire;
        me.Stat[(int)ActorStat.BloodPool] = 80f;
        me.Stat[(int)ActorStat.Blood] = 80f;
        var lady = place.Things.First(t => string.Equals(t.Template, template, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(full, Feeding.Order(christof, lady));

        // One point short of full and the bystander is fair game too.
        Actions.Cancel(christof, force: true);
        me.Stat[(int)ActorStat.Blood] = 79f;
        Assert.Equal(4, Feeding.Order(christof, lady));
    }

    [Fact]
    public void A_neck_bite_restrains_the_victim_and_puts_it_sixty_in_front()
    {
        using var files = Install.Open();
        var (_, christof, victim) = Open(files);
        Feeding.Bite(christof, victim, forced: true);

        Assert.Equal(ActionMode.Feeding, christof.Actor!.Action);
        Assert.Same(victim, christof.Actor.Target);
        Assert.Equal(ActionMode.FedOn, victim.Actor!.Action);
        Assert.NotNull(victim.Effects.Find("ef_restrain"));
        var gap = victim.Position - christof.Position;
        Assert.Equal(Feeding.NeckGap, new Vector2(gap.X, gap.Y).Length(), 2);
    }

    /// <summary>
    /// **A feeder given an order lets go first** (every order cancels before it takes over:
    /// the attack order for an attack). In play a Cappadocian feeding on Wilhem was struck by
    /// Christof and let go, but Wilhem just played the standing animation with the bow instead
    /// of the recovery from being fed on. The attack order had taken her over with her
    /// teeth still in him — her feed never stopped, so Wilhem was never released, and his own tick's
    /// release kept cutting his animation off. Now the feed ends properly: both play their release.
    /// </summary>
    [Fact]
    public void A_feeder_ordered_to_attack_lets_go_and_the_victim_plays_its_release()
    {
        using var files = Install.Open();
        var (place, christof, victim) = Open(files);
        Feeding.Bite(christof, victim, forced: true);
        Assert.Equal(ActionMode.FedOn, victim.Actor!.Action);
        int now = place.Milliseconds + 2000;
        place.Milliseconds = now;

        victim.Actor.Flags &= ~(Actor.Flag.Neutral | Actor.Flag.TalkTo);
        var result = Actions.Attack(christof, victim, christof.Actor!.Weapon ?? christof.Actor.DefaultWeapon, 0, now,
                                    motions: place.Motions);
        Assert.Equal(OrderResult.Busy, result);                 // his release makes him busy
        Assert.Equal("feedrelease", christof.Animation!.Action?.Mode.Name, ignoreCase: true);
        Assert.Equal(ActionMode.None, christof.Actor.Action);
        Assert.Equal(ActionMode.None, victim.Actor.Action);     // she is let go
        Assert.Null(victim.Effects.Find("ef_restrain"));
        Assert.Equal("feedreleased", victim.Animation!.Action?.Mode.Name, ignoreCase: true);
        Assert.True(victim.Actor.BusyUntil > now, "the release keeps her busy for its length");
    }

    /// <summary>The feeder gains three times its Blood Rating a drain, the victim loses three, and the next drain is a second away.</summary>
    [Fact]
    public void Each_drain_moves_blood_from_the_victim_to_the_feeder()
    {
        using var files = Install.Open();
        var (place, christof, victim) = Open(files);
        Feeding.Bite(christof, victim, forced: true);
        int now = place.Milliseconds + 1000;
        Feeding.Tick(christof, christof.Actor!, now);

        Assert.Equal(26f, christof.Actor!.Stat[(int)ActorStat.Blood], 3);
        Assert.Equal(37f, victim.Actor!.Stat[(int)ActorStat.Blood], 3);
        Assert.Equal(now + 1000, christof.Actor.ActionRetryAt);
        Feeding.Tick(christof, christof.Actor, now + 500);      // not yet
        Assert.Equal(37f, victim.Actor.Stat[(int)ActorStat.Blood], 3);
    }

    /// <summary>The drain that would empty the victim still pays — and kills it.</summary>
    [Fact]
    public void Draining_the_last_of_it_kills_and_still_pays()
    {
        using var files = Install.Open();
        var (place, christof, victim) = Open(files);
        Feeding.Bite(christof, victim, forced: true);
        victim.Actor!.Stat[(int)ActorStat.Blood] = 2f;
        Feeding.Tick(christof, christof.Actor!, place.Milliseconds + 1000);

        Assert.True(victim.Actor.Has(Actor.Flag.Dead));
        Assert.Equal(26f, christof.Actor!.Stat[(int)ActorStat.Blood], 3);
        Assert.Equal(ActionMode.None, christof.Actor.Action);
        Assert.Null(victim.Effects.Find("ef_restrain"));
    }

    /// <summary>Letting go of a victim with 25 or more (Feed at level 0 or 1) never kills it.</summary>
    [Fact]
    public void Letting_go_of_a_victim_with_blood_to_spare_releases_it()
    {
        using var files = Install.Open();
        var (_, christof, victim) = Open(files);
        Feeding.Bite(christof, victim, forced: true);
        Feeding.Stop(christof);

        Assert.False(victim.Actor!.Has(Actor.Flag.Dead));
        Assert.Equal(ActionMode.None, christof.Actor!.Action);
        Assert.Equal(ActionMode.None, victim.Actor.Action);
        Assert.Null(victim.Effects.Find("ef_restrain"));
    }

    /// <summary>
    /// A friend is taken at the wrist, 75 in front and facing the feeder — unless the donor is down to
    /// 10, when it says no.
    /// </summary>
    [Fact]
    public void A_friend_gives_the_wrist_unless_it_is_down_to_ten()
    {
        using var files = Install.Open();
        var (_, christof, friend) = Open(files);
        friend.Actor!.Team = christof.Actor!.Team;
        friend.Actor.Stat[(int)ActorStat.Blood] = 10f;
        Feeding.Bite(christof, friend);
        Assert.Equal(ActionMode.None, friend.Actor.Action);
        Assert.NotEqual(ActionMode.WristFeeding, christof.Actor.Action);

        christof.Actor.BusyUntil = 0;
        friend.Actor.Stat[(int)ActorStat.Blood] = 30f;
        Feeding.Bite(christof, friend);
        Assert.Equal(ActionMode.WristFeeding, christof.Actor.Action);
        Assert.Equal(ActionMode.WristFedOn, friend.Actor.Action);
        Assert.Null(friend.Effects.Find("ef_restrain"));
        var gap = friend.Position - christof.Position;
        Assert.Equal(Feeding.WristGap, new Vector2(gap.X, gap.Y).Length(), 2);
    }

    /// <summary>
    /// `ActorFrenzy` is +100 frenzy by effect, and the frenzy check does the rest: mode 9, the marker and
    /// its two stat effects, and the mouse refused. `ActorUnfrenzy` is −99, which is under 25.
    /// </summary>
    [Fact]
    public void Frenzy_comes_on_at_a_hundred_and_goes_at_twenty_five()
    {
        using var files = Install.Open();
        var (_, christof, _) = Open(files);
        var me = christof.Actor!;
        me.Stat[(int)ActorStat.Frenzy] = 0f;
        me.SetBaseStat((int)ActorStat.Frenzy, 0f);

        Frenzy.Provoke(christof);
        Assert.True(Frenzy.Frenzied(christof));
        Assert.Equal(AiMode.Frenzied, christof.Mind!.Mode);
        Assert.NotNull(christof.Effects.Find("ef_frenzy"));
        Assert.NotNull(christof.Effects.Find("ef_frz_strength"));
        Assert.NotNull(christof.Effects.Find("ef_frz_dexterity"));
        Assert.NotNull(christof.Effects.Find("ef_decreasefrenzyfast"));
        Assert.True(CastInput.Refused(christof));

        Frenzy.Soothe(christof);
        Assert.False(Frenzy.Frenzied(christof));
        Assert.Null(christof.Effects.Find("ef_frenzy"));
        Assert.Null(christof.Effects.Find("ef_frz_strength"));
        Assert.NotNull(christof.Effects.Find("ef_decreasefrenzy"));
        Assert.False(CastInput.Refused(christof));
    }

    [Fact]
    public void A_human_never_frenzies()
    {
        using var files = Install.Open();
        var (_, christof, _) = Open(files);
        christof.Actor!.Type = ActorType.Human;
        Frenzy.Begin(christof);
        Assert.False(Frenzy.Frenzied(christof));
        Assert.Null(christof.Effects.Find("ef_frenzy"));
    }
    /// <summary>
    /// Once the teeth are in, neither of them goes anywhere. The victim's own mind keeps thinking —
    /// a wanderer still picks somewhere to go — but every AI walk goes through the move order,
    /// and that refuses a character held by `ef_restrain`. The first feed in play had the
    /// monk wander off in the fed-on pose, because the minds walked without that gate.
    /// </summary>
    [Fact]
    public void A_neck_victim_stays_where_the_bite_put_it_while_its_mind_thinks()
    {
        using var files = Install.Open();
        var (place, christof, _) = Open(files);
        var victim = place.Minds!.All.First(m => m.Mode == AiMode.PedWander && m.Thing.Actor is not null).Thing;
        victim.Actor!.Stat[(int)ActorStat.BloodPool] = 60f;
        victim.Actor.Stat[(int)ActorStat.Blood] = 60f;
        victim.Actor.BaseStat[(int)ActorStat.Blood] = 60f;
        Feeding.Bite(christof, victim, forced: true);
        var there = victim.Position;

        var sim = new Simulation(place);
        for (int second = 0; second < 8; second++)
        {
            sim.Advance(1f, 1f / 30f);
            // Its mind picks a spot to wander to within a few seconds; the order must refuse it.
            Assert.False(victim.Mind!.Moving, $"the victim set off walking {second + 1} s into the feed");
        }

        Assert.Equal(ActionMode.FedOn, victim.Actor.Action);
        Assert.True(Vector3.Distance(there, victim.Position) < 1f,
                    $"the victim moved {Vector3.Distance(there, victim.Position):0} units while being fed on");
    }

    /// <summary>
    /// Celerity is a MOVESPEED multiplier, and the engine applies it where a walk is ordered
    /// (the move order's speed times the walk scale) — so it has to reach the walker, or it does nothing at all.
    /// Driving goes at the speed a click would.
    /// </summary>
    [Fact]
    public void Celerity_makes_the_walker_faster_by_its_level()
    {
        using var files = Install.Open();
        var (place, christof, _) = Open(files);
        var walker = place.Player!;
        walker.Running = true;
        walker.Move(Vector2.UnitY, 1f / 30f);
        float plain = walker.Speed;
        EffectRuntime.Apply(christof, "ef_disc_celerity1", 3, durationMs: 60_000);
        Assert.Equal(3f, christof.Actor!.MoveSpeedScale, 3);            // effects.ned: 1.5, 2, 2.5, 3 …
        walker.Move(Vector2.UnitY, 1f / 30f);
        Assert.Equal(plain * 3f, walker.Speed, 1);
    }
    /// <summary>
    /// A click on the ground while feeding goes through the move order's gate: in the
    /// bite's first second it is simply refused; after it, the feed is let go — the release plays and
    /// makes him busy — and the click still does not walk. Nothing walks until the release is over.
    /// </summary>
    [Fact]
    public void Letting_go_by_clicking_away_plays_the_release_before_anything_walks()
    {
        using var files = Install.Open();
        var (place, christof, _) = Open(files);
        // A passer-by, who runs off afterwards rather than hitting back.
        var victim = place.Minds!.All.First(m => m.Mode == AiMode.PedWander && m.Thing.Actor is not null).Thing;
        victim.Actor!.Stat[(int)ActorStat.BloodPool] = 60f;
        victim.Actor.Stat[(int)ActorStat.Blood] = 60f;
        victim.Actor.BaseStat[(int)ActorStat.Blood] = 60f;
        Feeding.Bite(christof, victim, forced: true);
        var me = christof.Actor!;
        Assert.False(Actions.ReadyToMove(christof));                 // the bite's own second
        Assert.Equal(ActionMode.Feeding, me.Action);

        var sim = new Simulation(place);
        sim.Advance(1.2f, 1f / 30f);
        Assert.Equal(ActionMode.Feeding, me.Action);
        Assert.False(Actions.ReadyToMove(christof));                 // lets go, and is busy releasing
        Assert.Equal(ActionMode.None, me.Action);
        Assert.Equal(ActionMode.None, victim.Actor!.Action);
        Assert.True(me.BusyUntil > place.Milliseconds, "the release left him free to walk at once");

        sim.Advance((me.BusyUntil - place.Milliseconds) / 1000f + 0.1f, 1f / 30f);
        Assert.True(Actions.ReadyToMove(christof),
                    $"now {place.Milliseconds} busy {me.BusyUntil} action {me.Action} dead {me.Dead} held {me.Has2(Actor.Flag2.Restrained)}");
    }
}
