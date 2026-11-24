// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// Disciplines cast through the game's own scripts: the engine's order and fire (`Rpg.Cast`), the
/// `Discipline*` class's `cast`, the effects it puts on, and the `EffectDisc*` class that hears them
/// start and end. Nothing here is a stand-in — the classes are the shipped ones.
/// </summary>
[Collection("scripts")]
public class DisciplineTests(Scripts s)
{
    sealed class Scene : IDisposable
    {
        public required GameFiles Files { get; init; }
        public required Location Place { get; init; }
        public required CodexScenes Scenes { get; init; }
        public required Simulation Sim { get; init; }
        public required WorldThing Christof { get; init; }
        public required DisciplineDefinitions Powers { get; init; }

        public void Run(float seconds)
        {
            for (float t = 0; t < seconds; t += 1f / 30f) Sim.Step(1f / 30f);
        }

        public void Dispose()
        {
            Scenes.Leave();
            Files.Dispose();
        }
    }

    /// <summary>
    /// The monastery with Christof in it, the minds awake with both tables, and the scripts in. He
    /// is made a vampire with blood to spend, and flagged as casting from a scroll — which is the
    /// one thing `Discipline.Fizzled` reads before its `Math.random()`, so no test here can fizzle.
    /// </summary>
    Scene Enter()
    {
        s.Ready();
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var owner = set.ForLevel("P1_MON1")!.Value;
        var place = Location.Load(files, "P1_MON1", sky: false, scene: owner.Entry!.SceneName, player: "Christof");
        var powers = new DisciplineDefinitions(files);
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), powers);
        var christof = place.Player!.Thing;
        var me = christof.Actor!;
        me.Type = ActorType.Vampire;
        me.Stat[(int)ActorStat.BloodPool] = 100f;
        me.SetBaseStat((int)ActorStat.Blood, 100f);
        me.Stat[(int)ActorStat.Blood] = 100f;
        me.Flags2 |= Actor.Flag2.ScrollCast;
        var camera = new WorldCamera();
        camera.Start(christof);
        var talk = new Conversations();
        var scenes = new CodexScenes(s.Runtime!)
        {
            Chronicles = set, Camera = camera, Talk = talk, Nls = new Vitomir.Engine.Ui.Strings(files).Find,
        };
        scenes.Enter(place);
        return new Scene
        {
            Files = files, Place = place, Scenes = scenes, Christof = christof, Powers = powers,
            Sim = new Simulation(place) { Scripts = scenes, Camera = camera, Talk = talk },
        };
    }

    [Fact]
    public void Celerity_puts_its_two_effects_on_and_its_script_draws_the_trail()
    {
        using var m = Enter();
        var celerity = DisciplineBook.SetLevel(m.Christof, m.Powers, "Celerity", 0);
        Assert.NotNull(celerity);
        // Learnt after the scripts started, so the script object is made on the spot.
        Assert.NotNull(celerity!.Script);
        int trails = m.Place.Things.Count(t => t.Template.Equals("celerityTrail", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(OrderResult.Started, Cast.Order(m.Christof, null, null, celerity, InputModifier.Space));
        m.Run(3f);                                            // the spell animation's `Cast` tag

        Assert.NotNull(m.Christof.Effects.Find("ef_disc_celerity1"));
        Assert.NotNull(m.Christof.Effects.Find("ef_disc_celerity2"));
        Assert.Equal(1, celerity.CastCount);
        // `EffectDiscCelerity.effectstarted` spawned the trail and attached it.
        Assert.Equal(trails + 1, m.Place.Things.Count(t => t.Template.Equals("celerityTrail", StringComparison.OrdinalIgnoreCase)));
        // Blood was spent: Celerity's first level costs what `game.ndd` says.
        Assert.Equal(100f - celerity.Definition.CostAt(0), m.Christof.Actor!.Stat[(int)ActorStat.Blood], 1);
        // And the console heard "Christof uses Discipline Celerity" in the game's own words.
        Assert.Contains(m.Scenes.Lines, l => l.Contains("Celerity", StringComparison.OrdinalIgnoreCase)
                                             && !l.Contains("RPG_", StringComparison.Ordinal));
    }

    [Fact]
    public void When_the_time_is_up_the_effect_script_takes_its_trail_and_the_second_effect_away()
    {
        using var m = Enter();
        var celerity = DisciplineBook.SetLevel(m.Christof, m.Powers, "Celerity", 0)!;
        Cast.Order(m.Christof, null, null, celerity, InputModifier.Space);
        m.Run(3f);
        Assert.NotNull(m.Christof.Effects.Find("ef_disc_celerity1"));

        m.Run(16f);                                           // CELERITY_DURATIONS[0] is 15 000 ms
        Assert.Null(m.Christof.Effects.Find("ef_disc_celerity1"));
        Assert.Null(m.Christof.Effects.Find("ef_disc_celerity2"));
        Assert.DoesNotContain(m.Place.Things, t => t.Template.Equals("celerityTrail", StringComparison.OrdinalIgnoreCase)
                                                    && t.AttachedTo == m.Christof);
    }

    /// <summary>
    /// The attributes a discipline's `game.ndd` requirements test, raised the way cheats
    /// raise them in 1.1 — Summon Elemental asks for Intelligence and Wits of 70.
    /// </summary>
    static void Cheat(WorldThing who)
    {
        ActorStat[] attributes = [ActorStat.Strength, ActorStat.Dexterity, ActorStat.Stamina, ActorStat.Perception,
                                  ActorStat.Intelligence, ActorStat.Wits, ActorStat.Charisma, ActorStat.Manipulation,
                                  ActorStat.Appearance];
        foreach (var stat in attributes)
        {
            who.Actor!.SetBaseStat((int)stat, 100f);
            who.Actor.Stat[(int)stat] = 100f;
        }
    }

    /// <summary>
    /// **Possession, through the game's own scripts** (`DisciplinePossession`, `EffectDiscPossession`).
    /// In 1.1 the fifth level cost Christof 20 blood, lasted 90 seconds,
    /// handed the controls to the Cappadocian he took, and gave her Christof's team — every
    /// Cappadocian near her went for her at once. The resist roll is made to pass: the body's Wits
    /// are 0, so `random × wits` can never beat `random × charisma`.
    /// </summary>
    [Fact]
    public void Possession_takes_the_body_gives_it_the_casters_team_and_gives_it_back()
    {
        using var m = Enter();
        var body = m.Place.Things.First(t => t.Template.Equals("cappadocian_female_conv1", StringComparison.OrdinalIgnoreCase));
        body.Actor!.Stat[(int)ActorStat.Wits] = 0f;
        body.Actor.SetBaseStat((int)ActorStat.Wits, 0f);
        m.Christof.Actor!.Stat[(int)ActorStat.Generation] = 1f;
        m.Place.Move(m.Christof, body.Position + new System.Numerics.Vector3(150f, 0f, 0f));
        Cheat(m.Christof);
        var possession = DisciplineBook.SetLevel(m.Christof, m.Powers, "Possession", 4)!;
        int team = body.Actor.Team;

        int ordered = m.Place.Milliseconds;
        Cast.Order(m.Christof, body, null, possession, InputModifier.None);
        m.Run(4f);

        var effect = body.Effects.Find("ef_disc_possession");
        Assert.NotNull(effect);
        Assert.InRange(effect!.Until - ordered, 90000, 94000);     // 90 s from the cast's own moment
        Assert.Equal(100f - 20f, m.Christof.Actor.Stat[(int)ActorStat.Blood], 1);
        Assert.Same(body, m.Place.Steered);
        Assert.Equal(m.Christof.Actor.Team, body.Actor.Team);

        Damage.Kill(body, null, HitKind.Melee, DamageType.Lethal, nowMs: m.Place.Milliseconds);
        Assert.Same(m.Christof, m.Place.Steered);
        Assert.Equal(team, body.Actor.Team);                  // `EndEffect` gives the old team back
    }

    /// <summary>
    /// **Tab ends a possession, and then the party can go for the body**. In play
    /// the controls were taken back with F6 and neither Christof nor Wilhem could attack the
    /// Cappadocian: F6 leaves the possession running, and a body on Christof's team is friendly, so
    /// the cursor offers nothing. Tab ends it — the effect's script gives the
    /// body its own team back — and the same cursor offers an attack.
    /// </summary>
    [Fact]
    public void Tab_ends_a_possession_and_the_body_is_an_enemy_again()
    {
        using var m = Enter();
        var body = m.Place.Things.First(t => t.Template.Equals("cappadocian_female_conv1", StringComparison.OrdinalIgnoreCase));
        body.Actor!.Stat[(int)ActorStat.Wits] = 0f;
        body.Actor.SetBaseStat((int)ActorStat.Wits, 0f);
        m.Christof.Actor!.Stat[(int)ActorStat.Generation] = 1f;
        m.Place.Move(m.Christof, body.Position + new System.Numerics.Vector3(150f, 0f, 0f));
        Cheat(m.Christof);
        var possession = DisciplineBook.SetLevel(m.Christof, m.Powers, "Possession", 4)!;
        int team = body.Actor.Team;
        Cast.Order(m.Christof, body, null, possession, InputModifier.None);
        m.Run(4f);
        Assert.Same(body, m.Place.Steered);
        Assert.True(Sight.Friendly(m.Christof, body));                     // on Christof's side now

        m.Place.SteerTab(() => 1f);
        Assert.Same(m.Christof, m.Place.Steered);
        Assert.Equal(team, body.Actor.Team);
        Assert.False(Sight.Friendly(m.Christof, body));
    }

    /// <summary>
    /// **Summon Elemental** (`DisciplineSummonElemental`): in 1.1 the first level cost 25 blood and
    /// put an `elemental` beside Christof — summoned, in mode `pet`, for fifteen seconds. The
    /// scripts count the pets before they will cast again (`AIGetNumPets` against `AI_MAXPETS`).
    /// </summary>
    [Fact]
    public void Summon_Elemental_makes_Christof_a_pet_for_fifteen_seconds()
    {
        using var m = Enter();
        Cheat(m.Christof);
        var summon = DisciplineBook.SetLevel(m.Christof, m.Powers, "SummonElemental", 0)!;
        int ordered = m.Place.Milliseconds;
        Assert.Equal(OrderResult.Started, Cast.Order(m.Christof, null, null, summon, InputModifier.Space));
        m.Run(4f);

        var elemental = m.Place.Things.Single(t => t.Template.Equals("elemental", StringComparison.OrdinalIgnoreCase));
        Assert.True(elemental.Actor!.Has(Actor.Flag.Summoned));
        Assert.Equal(AiMode.Pet, elemental.Mind!.Mode);
        Assert.Equal(m.Christof.Guid, elemental.Mind.MasterGuid);
        var effect = elemental.Effects.Find("ef_disc_summonelemental")!;
        Assert.InRange(effect.Until - ordered, 15000, 19000);
        Assert.Equal(100f - 25f, m.Christof.Actor!.Stat[(int)ActorStat.Blood], 1);
        Assert.Equal(1, Minds.PetCount(m.Christof.Mind!));
    }

    [Fact]
    public void A_script_can_ask_for_a_discipline_and_the_group_opens_behind_it()
    {
        using var m = Enter();
        // `SetActorDisciplineLevel` is what the tomes and OLDT_7_4 call; Feed opens Blood Healing.
        var feed = DisciplineBook.SetLevel(m.Christof, m.Powers, "Feed", 0);
        Assert.NotNull(feed);
        var next = m.Christof.Actor!.Disciplines["BloodHealing"];
        Assert.NotNull(next);
        Assert.Equal(-1, next!.Level);
        Assert.Equal(0u, next.Flags & Discipline.Unannounced);
    }

    [Fact]
    public void Formatted_messages_fill_their_slots_the_engine_way()
    {
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RPG_DISCMSG_CASTS"] = "%C uses Discipline %d.",
            ["CELERITY"] = "Celerity",
        };
        string? Find(string k) => table.GetValueOrDefault(k);
        // Upper-case keys go in as they are; lower-case ones are looked up; a missing key is nothing.
        Assert.Equal("Christof uses Discipline Celerity.",
                     Vitomir.Engine.Ui.Strings.Format(Find, "RPG_DISCMSG_CASTS", "%CChristof%dcelerity"));
        Assert.Equal("Christof uses Discipline .",
                     Vitomir.Engine.Ui.Strings.Format(Find, "RPG_DISCMSG_CASTS", "%CChristof"));
        Assert.Equal("NO_SUCH_ID", Vitomir.Engine.Ui.Strings.Format(Find, "NO_SUCH_ID", "%CChristof"));
    }
}
