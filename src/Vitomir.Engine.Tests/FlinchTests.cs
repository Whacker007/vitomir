// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The hit reaction** (the start of the damage reaction and the flinch): the chance of a flinch, the 3.5 s in which
/// a character that has flinched neither flinches nor cries out again, what it plays, and what it
/// cuts short, as 1.1's blows have them;
/// these tests pin the rules those blows follow.
/// </summary>
public class FlinchTests
{
    [Fact]
    public void The_chance_is_eight_a_point_less_stamina_held_between_5_and_95()
    {
        Assert.Equal(45f, Flinch.Chance(10f, 35f));          // a Cappadocian hit for 10
        Assert.Equal(5f, Flinch.Chance(4f, 35f));             // a scratch: the floor
        Assert.Equal(95f, Flinch.Chance(30f, 35f));           // a hard blow: the ceiling
        Assert.Equal(95f, Flinch.Chance(1.5f, -1000f));
    }

    [Fact]
    public void The_mode_is_hurt_plus_the_carry()
    {
        // 0xdd is `hurt` in the engine's body-mode table, and the carry offsets run as they do for
        // stand and walk: 1 is one-handed, 7 the bow.
        Assert.Equal("hurt", NmsMotionSet.BodyModes[Flinch.HurtMode]);
        Assert.Equal("hurt_onehand", NmsMotionSet.BodyModes[Flinch.HurtMode + 1]);
        Assert.Equal("hurt_bow", NmsMotionSet.BodyModes[Flinch.HurtMode + 7]);
    }

    static (Location Place, WorldThing Victim) Monastery(string template = "skel_sword")
    {
        var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var victim = template == "christof"
            ? place.Player!.Thing
            : place.Things.First(t => t.Template.Equals(template, StringComparison.OrdinalIgnoreCase));
        victim.Actor!.Stat[(int)ActorStat.Stamina] = 0f;     // a sure 95%
        return (place, victim);
    }

    /// <summary>An rng whose next draw is small: seed 1's first rand() is 41, 0.1% of the way.</summary>
    static Rng Sure() => new(1);

    static Harm Hit(WorldThing victim, float amount, int nowMs, Rng rng) =>
        Damage.Apply(victim, null, HitKind.Melee, DamageType.Normal, amount, rng, nowMs, skipSoak: true);

    [Fact]
    public void A_hard_blow_makes_a_character_flinch_on_its_action_channel()
    {
        var (place, victim) = Monastery();
        var actor = victim.Actor!;
        Hit(victim, 20f, 5_000, Sure());
        Assert.Equal(ActionMode.Hurt, actor.Action);
        Assert.Equal(5_000, actor.FlinchedAt);
        Assert.StartsWith("hurt", victim.Animation!.Action!.Mode.Name);
        Assert.True(actor.BusyUntil > 5_000, "busy for the animation");
        _ = place;
    }

    [Fact]
    public void Within_three_and_a_half_seconds_nothing_flinches_again()
    {
        var (_, victim) = Monastery();
        var actor = victim.Actor!;
        Hit(victim, 10f, 5_000, Sure());
        Assert.Equal(5_000, actor.FlinchedAt);
        actor.BusyUntil = 0;
        actor.Action = ActionMode.None;
        Hit(victim, 10f, 8_500, Sure());                     // FlinchedAt + 3500 >= now: quiet
        Assert.Equal(ActionMode.None, actor.Action);
        Assert.Equal(5_000, actor.FlinchedAt);
        Hit(victim, 10f, 8_501, Sure());                     // and one millisecond later, again
        Assert.Equal(ActionMode.Hurt, actor.Action);
        Assert.Equal(8_501, actor.FlinchedAt);
    }

    [Fact]
    public void A_flinch_cuts_a_swing_short_but_not_other_business()
    {
        var (_, victim) = Monastery();
        var actor = victim.Actor!;
        // Mid-swing and busy for another second: the flinch lets action 5 through.
        actor.Action = ActionMode.Swing;
        actor.BusyUntil = 6_000;
        Hit(victim, 10f, 5_000, Sure());
        Assert.Equal(ActionMode.Hurt, actor.Action);

        // Busy with something else — a feed's release, say — and it does not flinch at all, and
        // the quiet does not start.
        var (_, other) = Monastery();
        var busy = other.Actor!;
        busy.Action = ActionMode.Use;
        busy.BusyUntil = 6_000;
        Hit(other, 10f, 5_000, Sure());
        Assert.Equal(ActionMode.Use, busy.Action);
        Assert.Equal(0, busy.FlinchedAt);
    }

    [Fact]
    public void A_staked_or_thrown_character_does_not_flinch()
    {
        var (_, victim) = Monastery();
        victim.Actor!.Flags |= World.Actor.Flag.Staked;
        Hit(victim, 10f, 5_000, Sure());
        Assert.NotEqual(ActionMode.Hurt, victim.Actor.Action);

        var (_, thrown) = Monastery();
        thrown.Actor!.Action = ActionMode.Thrown;
        Hit(thrown, 10f, 5_000, Sure());
        Assert.Equal(ActionMode.Thrown, thrown.Actor.Action);
    }

    [Fact]
    public void A_scratch_of_one_point_does_nothing()
    {
        // `1.0 < damage`, strictly.
        var (_, victim) = Monastery();
        Hit(victim, 1f, 5_000, Sure());
        Assert.Equal(0, victim.Actor!.FlinchedAt);
    }

    [Fact]
    public void A_set_without_the_weapon_s_hurt_plays_plain_hurt()
    {
        // The knight of St John with his pole-arm in the log plays `hurt`: the flinch tries
        // `hurt` + carry and falls back to 0xdd when the set has not got it.
        var (place, victim) = Monastery("skel_bow");                        // hurt and hurt_bow only
        var set = victim.Animation!.Set;
        int carry = Enumerable.Range(1, 12).First(c => place.Motions!.Find(set, Flinch.HurtMode + c) is null);
        victim.Actor!.Weapon = new Weapon { Name = "odd", Carry = carry };
        Hit(victim, 10f, 5_000, Sure());
        Assert.Equal("hurt", victim.Animation!.Action!.Mode.Name);
    }

    [Fact]
    public void The_player_s_portrait_winces_with_the_body()
    {
        var (_, christof) = Monastery("christof");
        Hit(christof, 10f, 5_000, Sure());
        Assert.Equal(Ui.HeadMode.Impact, christof.Actor!.HeadAction);
        Assert.Equal(ActionMode.Hurt, christof.Actor.Action);
    }

    [Fact]
    public void In_a_fight_christof_flinches_and_never_twice_within_the_quiet()
    {
        // The monastery's own fight, a minute of it, run by the simulation: blows land, some make
        // him flinch, and no two flinches are ever closer than 3.5 s — the gate the logs
        // never saw broken in 38 blows.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON2", things: true, sky: false,
                                  scene: ChronicleSet.Load(files).ForLevel("P1_MON2")?.Entry?.SceneName,
                                  player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var sim = new Simulation(place);
        var christof = place.Player!.Thing;
        christof.Actor!.MaxHealth = christof.Actor.Health = 10_000f;                // outlive the minute
        var flinches = new List<int>();
        for (int frame = 0; frame < 30 * 60; frame++)
        {
            sim.Step(1f / 30f);
            if (christof.Actor.FlinchedAt != 0 && (flinches.Count == 0 || flinches[^1] != christof.Actor.FlinchedAt))
                flinches.Add(christof.Actor.FlinchedAt);
        }
        int hits = sim.Blows.Count(b => ReferenceEquals(b.Target, christof) && b.Harm.Taken > Flinch.Threshold);
        Assert.True(flinches.Count > 0, $"{hits} blows and not one flinch");
        Assert.True(flinches.Count <= hits);
        for (int i = 1; i < flinches.Count; i++)
            Assert.True(flinches[i] - flinches[i - 1] > Damage.HurtQuietMs, $"flinches at {flinches[i - 1]} and {flinches[i]}");
    }
}
