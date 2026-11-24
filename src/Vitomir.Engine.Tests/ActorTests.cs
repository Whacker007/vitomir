// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The character state a template describes: who the scripts can name, what they are made of, and
/// which of the engine's category-10 tags each field comes from.
/// </summary>
public class ActorTests
{
    static Location Load(string level)
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        return Location.Load(files, level, scene: set.ForLevel(level)?.Entry?.SceneName);
    }

    [Fact]
    public void Only_characters_carry_character_state()
    {
        var town = Load("P1_OTDY");
        Assert.All(town.Things, t => Assert.Equal(t.Type is ThingType.Actor or ThingType.Player, t.IsActor));
        Assert.Contains(town.Things, t => t.IsActor);
        // A barrel is a container and a bench is a prop; neither has anything to say about clans.
        Assert.DoesNotContain(town.Things, t => t.Type == ThingType.Prop && t.IsActor);
    }

    [Fact]
    public void A_template_s_tags_become_the_character()
    {
        var town = Load("P1_OTDY");
        var knight = town.Things.First(t => t.Template.Contains("convknight", StringComparison.OrdinalIgnoreCase));
        var actor = knight.Actor!;
        Assert.Equal(ActorType.Human, actor.Type);
        Assert.True(actor.Health > 0, "a knight with no health");
        Assert.True(actor.MaxHealth >= actor.Health);
        Assert.True(actor.WalkSpeed > 0, "a knight who cannot walk");
        // The stats are floats on disk and land in sensible human ranges, not as reinterpreted ints.
        Assert.InRange(actor.Stat[(int)ActorStat.Strength], 0f, 1000f);
        Assert.Equal(actor.Stat, actor.BaseStat);
    }

    [Fact]
    public void The_named_cast_is_findable_by_name()
    {
        // 136 templates carry a CASTID; it is how a scene script gets hold of a character without
        // knowing where in the thing list the level builder put them. Old Town by day has three.
        var town = Load("P1_OTDY");
        var named = town.Things.Where(t => t.Actor is { CastId.Length: > 0 }).ToList();
        Assert.NotEmpty(named);
        Assert.All(named, t => Assert.False(string.IsNullOrWhiteSpace(t.Actor!.CastId)));
        // Most actors are extras with no name at all, which is what makes the named ones worth finding.
        Assert.True(named.Count < town.Things.Count(t => t.IsActor));
    }

    [Fact]
    public void Clans_map_both_ways()
    {
        var actor = new Actor { Clan = "tremere" };
        Assert.Equal(12, actor.ClanId);                       // ACTOR_CLAN_TREMERE
        Assert.True(actor.SetClan("Brujah"));                 // the shipped templates vary in case
        Assert.Equal(1, actor.ClanId);
        Assert.False(actor.SetClan("Followers of Set"));      // not one of the sixteen
        Assert.Equal(1, actor.ClanId);
        Assert.Equal("none", new Actor().AuraOrName);
    }

    /// <summary>
    /// The fifteen by name, then 16 for any other clan the clan file has — `human`, in the
    /// shipped `misc/game.ncd` — and 0 for a name it has not, `userdefined` and `none` included.
    /// </summary>
    [Fact]
    public void A_clan_the_clan_file_has_beyond_the_fifteen_answers_16()
    {
        using var files = Install.Open();
        var clans = Vitomir.Engine.Rpg.Clans.For(files);
        Assert.Equal(16, clans.IdOf("human"));
        Assert.Equal(16, clans.IdOf("HUMAN"));
        Assert.Equal(12, clans.IdOf("Tremere"));
        Assert.Equal(15, clans.IdOf("caitiff"));
        Assert.Equal(0, clans.IdOf("userdefined"));
        Assert.Equal(0, clans.IdOf("none"));
        Assert.Equal(16, new Actor { Clan = "human", ClanRecord = clans["human"] }.ClanId);
        Assert.Equal(0, new Actor { Clan = "human" }.ClanId);     // no record: not in any file
    }

    [Fact]
    public void Flags_and_teams_are_the_game_s_own_numbers()
    {
        var actor = new Actor();
        Assert.Equal(Actor.Teams.None, actor.Team);
        Assert.False(actor.Has(Actor.Flag.AiPaused));
        actor.Flags |= Actor.Flag.AiPaused | Actor.Flag.Neutral;
        Assert.True(actor.Has(Actor.Flag.AiPaused));
        Assert.True(actor.Has(Actor.Flag.Neutral));
        Assert.Equal(0x4u, Actor.Flag.AiPaused);              // THING_AF_AIPAUSED
        Assert.Equal(0x40000u, Actor.Flag.Neutral);           // THING_AF_NEUTRAL
        Assert.True(Actor.Teams.IsEnemy(12));
        Assert.True(Actor.Teams.IsPlayer(0));
        Assert.False(Actor.Teams.IsPlayer(Actor.Teams.None));
    }

    [Fact]
    public void Every_character_in_the_game_reads_without_nonsense()
    {
        // A tag read as the wrong type shows up instantly: a float read as an int is in the
        // billions, and an int read as a float is about 1e-43. Sweep the whole cast for either.
        // Geometry is left out, so this is the thing lists of all 112 levels in a few seconds.
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        var levels = files.List("levels/")
                          .Where(n => n.EndsWith(".nil", StringComparison.OrdinalIgnoreCase))
                          .Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToArray();
        int actors = 0, named = 0;
        foreach (var level in levels)
        {
            var where = Location.Load(files, level, things: false, sky: false,
                                      scene: set.ForLevel(level)?.Entry?.SceneName);
            foreach (var t in where.Things)
            {
                if (t.Actor is not { } a) continue;
                actors++;
                if (a.CastId.Length > 0) named++;
                Assert.InRange(a.Health, 0f, 100_000f);
                Assert.InRange(a.MaxHealth, 0f, 100_000f);
                Assert.InRange(a.WalkSpeed, 0f, 10_000f);
                Assert.InRange(a.RunSpeed, 0f, 10_000f);
                Assert.InRange(a.Xp, 0, 1_000_000);
                foreach (var stat in a.Stat) Assert.InRange(stat, 0f, 100_000f);
                foreach (var soak in a.Soak) Assert.InRange(soak, 0f, 100_000f);
                Assert.InRange((int)a.Type, 0, 3);
                Assert.InRange(a.Team, 0, Actor.Teams.EnemyLast);
                Assert.InRange(a.Aura, 0u, (uint)Actor.Auras.Length - 1);
            }
        }
        Assert.Equal((1_802, 107), (actors, named));
    }

    /// <summary>
    /// **Every base-stat write is clamped, and this is why.** A character sheet showed frenzy
    /// at −184 and falling; `Simulation.StatDrift` named it as −2 every two seconds, which is
    /// `ef_decreasefrenzy` — the effect every vampire is born with (`Spawn.Born`) —
    /// doing exactly what `effects.ned` authors it to do. The original cannot show it because
    /// The engine's stat write clamps; Vitomir clamped nowhere.
    /// </summary>
    [Fact]
    public void A_base_stat_write_is_clamped_the_way_the_engine_clamps_it()
    {
        var a = new Actor();
        // The fallback bounds: 0 to the global ceiling of 10,000.
        Assert.Equal(0f, a.SetBaseStat((int)ActorStat.Frenzy, -50f));
        Assert.Equal(Actor.StatCeiling, a.SetBaseStat((int)ActorStat.Strength, 99_999f));

        // An authored maximum wins over the ceiling, and an authored minimum over the floor —
        // **for a player-type character only**. The stat write reads the `.NPC` rows when the thing is
        // THING_TYPE_PLAYER and otherwise holds everyone to 0 and their clan's cap (or 10,000).
        a.StatMax[(int)ActorStat.Frenzy] = 100f;
        a.StatMin[(int)ActorStat.Humanity] = 10f;
        Assert.Equal(400f, a.SetBaseStat((int)ActorStat.Frenzy, 400f));
        a.IsPlayerThing = true;
        Assert.Equal(100f, a.SetBaseStat((int)ActorStat.Frenzy, 400f));
        Assert.Equal(10f, a.SetBaseStat((int)ActorStat.Humanity, 0f));

        // −1 is the engine's own "no limit" sentinel, not a maximum of −1.
        a.StatMax[(int)ActorStat.Wits] = Actor.NoStatLimit;
        Assert.Equal(Actor.StatCeiling, a.SetBaseStat((int)ActorStat.Wits, 50_000f));
    }

    /// <summary>
    /// Blood is clamped a second time against the pool it is held in, which the engine does after
    /// the ordinary clamp and only for stat 6.
    /// </summary>
    [Fact]
    public void Blood_cannot_exceed_the_pool_it_is_held_in()
    {
        var a = new Actor();
        a.Stat[(int)ActorStat.BloodPool] = 80f;
        Assert.Equal(80f, a.SetBaseStat((int)ActorStat.Blood, 500f));
        Assert.Equal(40f, a.SetBaseStat((int)ActorStat.Blood, 40f));
        // and the pool itself is not blood, so it is not held to itself
        Assert.Equal(300f, a.SetBaseStat((int)ActorStat.BloodPool, 300f));
    }

    /// <summary>
    /// The frenzy drain a vampire is born with runs for ever and settles at the floor rather than
    /// running away — the whole point of the clamp, as one test.
    /// </summary>
    [Fact]
    public void The_frenzy_a_vampire_is_born_with_settles_at_zero_rather_than_running_away()
    {
        var a = new Actor { Type = ActorType.Vampire };
        a.StatMax[(int)ActorStat.Frenzy] = 100f;
        a.SetBaseStat((int)ActorStat.Frenzy, 30f);          // a fight put it up
        for (int tick = 0; tick < 200; tick++) a.AddBaseStat((int)ActorStat.Frenzy, -2f);
        Assert.Equal(0f, a.BaseStat[(int)ActorStat.Frenzy]);
    }

    /// <summary>
    /// A player-character file replaces the template's numbers, and brings the clamp bounds with
    /// them — which is the point: without the bounds the frenzy drain has no floor.
    /// </summary>
    [Fact]
    public void A_player_character_file_replaces_the_templates_numbers_and_brings_its_bounds()
    {
        var a = new Actor { Type = ActorType.Vampire };
        for (int i = 0; i < Actor.Stats; i++) a.Stat[i] = a.BaseStat[i] = 20f;   // the template's

        var npc = Vitomir.Formats.NpcFile.Parse("""
            name            Christof
            strength        0       100     30
            bloodpool       0       1000    100
            blood           0       1000    100
            frenzy          0       100     0
            totalexperience 1265
            """, "Christof.NPC");

        int taken = a.Apply(npc);
        Assert.Equal(4, taken);
        Assert.Equal("Christof", a.Name);
        Assert.Equal(30f, a.Stat[(int)ActorStat.Strength]);
        Assert.Equal(1265, a.Experience);

        // The bounds came with the values, so the drain now has a floor.
        Assert.Equal(100f, a.StatMax[(int)ActorStat.Frenzy]);
        for (int tick = 0; tick < 50; tick++) a.AddBaseStat((int)ActorStat.Frenzy, -2f);
        Assert.Equal(0f, a.BaseStat[(int)ActorStat.Frenzy]);

        // A stat the file does not name keeps the template's value and the fallback ceiling.
        Assert.Equal(20f, a.Stat[(int)ActorStat.Wits]);
        Assert.Equal(Actor.StatCeiling, a.StatMax[(int)ActorStat.Wits]);
    }

    /// <summary>
    /// The pool is applied before what it holds, which is the one ordering rule the shipped files
    /// carry in a comment — *"bloodpool MUST come before blood!!!"* — because a blood write is
    /// clamped against the pool.
    /// </summary>
    [Fact]
    public void The_pool_is_applied_before_the_blood_it_holds()
    {
        var a = new Actor();
        a.Stat[(int)ActorStat.BloodPool] = 100f;             // the pool's current as the reset leaves it
        // Written the wrong way round on purpose: blood first, then the pool that allows it.
        var npc = Vitomir.Formats.NpcFile.Parse("""
            blood           0       1000    100
            bloodpool       0       1000    100
            """, "backwards.NPC");
        a.Apply(npc);
        Assert.Equal(100f, a.Stat[(int)ActorStat.Blood]);   // not clamped to a pool of zero
    }

    /// <summary>
    /// **Three of the eighteen shipped files ask for more than their own row allows** — Christof
    /// and ChristophDa both carry `mana 0 100 110`, ChristofModern `blood 0 100 110`. The stat write writes
    /// the value clamped against the minimum and **the pool's current value**, which the
    /// sheet's own pool line does not move — only the recompute after the whole file does.
    /// So Christof starts with **100 mana of a 110 pool**, and it fills from there: **seen in 1.1**
    /// (a new game: mana 100, pool 110, then 101, 102 …). An earlier build
    /// read it as 110; the later reading (Serena's blood, `CharacterSheetTests`) is the one 1.1 shows.
    /// </summary>
    [Fact]
    public void A_pool_holder_asking_past_its_row_gets_what_the_pool_s_current_allows()
    {
        var a = new Actor { IsPlayerThing = true };           // only a player's rows are read
        a.Stat[(int)ActorStat.ManaPool] = 100f;               // the pool's current as the reset leaves it
        var npc = Vitomir.Formats.NpcFile.Parse("""
            manapool        0       1000    110
            mana            0       100     110
            """, "Christof.NPC");
        a.Apply(npc);
        Assert.Equal(110f, a.Stat[(int)ActorStat.ManaPool]);
        Assert.Equal(100f, a.Stat[(int)ActorStat.Mana]);
    }
}
