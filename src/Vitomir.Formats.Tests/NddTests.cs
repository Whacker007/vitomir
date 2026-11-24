// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats.Tests;

/// <summary>
/// `misc/game.ndd` against the shipped file. This is the discipline economy — what each of the 73
/// costs to buy, what it costs to cast, how long before it can be cast again — so the numbers are
/// worth pinning: if a reader slips a column the whole RPG layer quietly changes balance.
///
/// The file documents itself, so nothing here was inferred: the header names every flag bit and
/// every AI type, and these tests mostly assert that the reader agrees with that header.
/// </summary>
public class NddTests
{
    static NddFile Read()
    {
        using var nob = GameData.Open("Resource.nob");
        Assert.SkipUnless(nob.TryRead("misc/game.ndd", out var data), "misc/game.ndd not in Resource.nob");
        return NddFile.Read(data, "misc/game.ndd");
    }

    [Fact]
    public void Every_row_of_the_shipped_file_reads_and_none_is_dropped()
    {
        var ndd = Read();
        Assert.Empty(ndd.Dropped);
        Assert.Equal(73, ndd.All.Count);
        // 28 columns each, which is the header's own line format — and no row is short, so there is
        // no per-row column fixup hiding anywhere in the reader.
        Assert.All(ndd.All, d => Assert.False(string.IsNullOrEmpty(d.Name)));
        // Names are unique, which is what makes the lookup safe.
        Assert.Equal(73, ndd.All.Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// The three physical disciplines every vampire knows about, read out in full. Each is its own
    /// one-discipline group, each costs 500 experience, and each costs the same blood at every level
    /// — so advancing Celerity makes it better without making it cheaper.
    /// </summary>
    [Fact]
    public void The_physical_disciplines_cost_the_same_blood_at_every_level()
    {
        var ndd = Read();
        foreach (var (name, blood) in new[] { ("Celerity", 30), ("Fortitude", 20), ("Potence", 20) })
        {
            var d = ndd[name];
            Assert.NotNull(d);
            Assert.Equal($"DGRP_{name}", d.Group);
            Assert.Equal(DisciplineType.Buff, d.Type);
            Assert.Equal(500, d.Experience);
            Assert.All(Enumerable.Range(0, NddDiscipline.Levels), l => Assert.Equal(blood, d.CostAt(l)));
            Assert.All(Enumerable.Range(0, NddDiscipline.Levels), l => Assert.Equal(1000, d.RecastAt(l)));
            // `TARGET NONE` — it goes on the caster, so the AI needs nothing to point at.
            Assert.True(d.Has(DisciplineFlags.TargetNone));
        }
    }

    /// <summary>
    /// `Feed` is the one thing in the table that gets you blood rather than spending it, and it
    /// reads that way: costed in experience (750, out of `DGRP_Common`, which is free to open) and
    /// **zero blood at every level**. Its flags are the interesting part — `TARGET ENEMY` and
    /// `TARGET FRIENDLY` together with `NOPARTYNOTIFY`, so feeding on one of your own companions
    /// does not register with the party as an attack.
    /// </summary>
    [Fact]
    public void Feeding_costs_no_blood_and_does_not_tell_the_party()
    {
        var d = Read()["Feed"];
        Assert.NotNull(d);
        Assert.Equal("DGRP_Common", d.Group);
        Assert.Equal(DisciplineType.Feed, d.Type);
        Assert.Equal(750, d.Experience);
        Assert.All(Enumerable.Range(0, NddDiscipline.Levels), l => Assert.Equal(0, d.CostAt(l)));
        Assert.True(d.Has(DisciplineFlags.TargetEnemy));
        Assert.True(d.Has(DisciplineFlags.TargetFriendly));
        Assert.True(d.Has(DisciplineFlags.NoPartyNotify));
        Assert.False(d.Has(DisciplineFlags.Offensive));
    }

    /// <summary>
    /// A row that uses most of the columns at once, so a column slip shows up: Fireball is an
    /// offensive area-of-effect discipline with a real range limit, a stat check, and a blood cost
    /// that <b>drops</b> with level — the only shape in the table where advancing makes it cheaper.
    /// </summary>
    [Fact]
    public void Fireball_reads_every_column_including_its_stat_check()
    {
        var d = Read()["Fireball"];
        Assert.NotNull(d);
        Assert.Equal("DGRP_Thau_LureOfFlames", d.Group);
        Assert.Equal("fireballIcon", d.Icon);
        Assert.Equal("DisciplineFireball", d.Script);            // the Codex class, case sensitive
        Assert.Equal(DisciplineType.Damage, d.Type);
        Assert.True(d.Has(DisciplineFlags.Offensive));
        Assert.True(d.Has(DisciplineFlags.AreaOfEffect));
        Assert.True(d.Has(DisciplineFlags.TargetEnemy));
        Assert.Equal(1000f, d.MaxDistance);
        Assert.Equal([10, 10, 10, 5, 5], d.BloodCost);
        // One check; the other two are `-`, and the loader leaves them out altogether.
        Assert.Equal([(4, 35f)], d.Checks);
    }

    /// <summary>
    /// Range is the AI's, not the player's: most disciplines carry `9999` and mean "wherever you
    /// are", and the ones with a real number are the ones that have to be aimed. Nothing has a
    /// minimum distance at all, so the chooser's `minDist` never keeps anything out.
    /// </summary>
    [Fact]
    public void Nothing_in_the_table_has_a_minimum_distance()
    {
        var ndd = Read();
        Assert.All(ndd.All, d => Assert.Equal(0f, d.MinDistance));
        // Five values in the whole table, and over half the rows take the first.
        Assert.Equal([200f, 550f, 1000f, 1500f, 9999f], ndd.All.Select(d => d.MaxDistance).Distinct().Order());
        Assert.Equal(38, ndd.All.Count(d => d.MaxDistance >= 9999f));
    }

    /// <summary>
    /// The AI types, counted. This is the table the discipline ladder reads, and the counts are
    /// the thing to know about it: <b>20 of the 73 are type 0</b> — the AI will never cast them at
    /// all, on anybody, so a third of the discipline list is player-only. And because the header
    /// reserves 7, 8 and 9, the chooser's damage rung asking for "above 9" is exactly DAMAGE, MIND
    /// and FRENZY — 26 disciplines, not 17.
    /// </summary>
    [Fact]
    public void A_third_of_the_table_is_player_only_and_the_damage_rung_takes_twenty_six()
    {
        var ndd = Read();
        var byType = ndd.All.GroupBy(d => d.Type).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(20, byType[DisciplineType.None]);
        Assert.Equal(2, byType[DisciplineType.Heal]);
        Assert.Equal(12, byType[DisciplineType.Buff]);
        Assert.Equal(5, byType[DisciplineType.Summon]);
        Assert.Equal(4, byType[DisciplineType.Hide]);
        Assert.Equal(3, byType[DisciplineType.Sense]);
        Assert.Equal(1, byType[DisciplineType.Feed]);
        Assert.Equal(17, byType[DisciplineType.Damage]);
        Assert.Equal(7, byType[DisciplineType.Mind]);
        Assert.Equal(2, byType[DisciplineType.Frenzy]);
        // Nothing uses a reserved number, which is why "above 9" is a clean band.
        Assert.DoesNotContain(ndd.All, d => (int)d.Type is 7 or 8 or 9);
        Assert.Equal(26, ndd.All.Count(d => (int)d.Type > 9));
        // And `TrueFaith` is a HIDE, which is the sort of thing only the table will tell you.
        Assert.Equal(DisciplineType.Hide, ndd["TrueFaith"]!.Type);
    }

    /// <summary>
    /// Every group in the table is one the group file declares, and the whole table adds up to
    /// <b>50,000 experience</b> to buy outright — before the 1,000 a group costs to open.
    /// </summary>
    [Fact]
    public void Every_group_is_a_real_group_and_the_whole_table_costs_fifty_thousand()
    {
        using var nob = GameData.Open("Resource.nob");
        Assert.SkipUnless(nob.TryRead("misc/game.ndd", out var ndd), "misc/game.ndd not in Resource.nob");
        Assert.SkipUnless(nob.TryRead("misc/game.ndg", out var ndg), "misc/game.ndg not in Resource.nob");
        var table = NddFile.Read(ndd, "misc/game.ndd");
        var groups = System.Text.Encoding.Latin1.GetString(ndg)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && l[0] != NddFile.Comment)
            .Select(l => l.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(20, table.All.Select(d => d.Group).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(table.All, d => Assert.Contains(d.Group, groups));
        Assert.Equal(50_000, table.All.Sum(d => d.Experience));
    }

    /// <summary>
    /// A missing value is written `-`, and it has to read as zero rather than throwing — which is
    /// what most of the check columns are. Hex flags are hex, decimal columns are decimal, and a
    /// short row is dropped with its line number rather than silently mangled.
    /// </summary>
    [Fact]
    public void A_dash_reads_as_zero_and_a_short_row_is_dropped_with_its_line()
    {
        string good = "Test DGRP_Common testIcon TestScript - 0x4028 10 0.0 1000.0 2.5 3 500 "
                    + "1 2 3 4 5 100 200 300 400 500 4 35 - - - -";
        var file = NddFile.Read(System.Text.Encoding.Latin1.GetBytes(
            "# a comment\n\n" + good + "\nShortRow DGRP_Common x\n"), "synthetic.ndd");

        Assert.Single(file.All);
        Assert.Single(file.Dropped);
        Assert.Contains("synthetic.ndd:4", file.Dropped[0]);      // the line number, not the index

        var d = file.All[0];
        Assert.Equal(DisciplineFlags.TargetEnemy | DisciplineFlags.Offensive | DisciplineFlags.AreaOfEffect, d.Flags);
        Assert.Equal(2.5f, d.Data);
        Assert.Equal(3, d.CastAnimation);
        Assert.Equal([1, 2, 3, 4, 5], d.BloodCost);
        Assert.Equal([100, 200, 300, 400, 500], d.RecastMs);
        Assert.Equal([(4, 35f)], d.Checks);
        // Levels count from zero and the top end clamps. Level −1 is not clamped: the engine
        // indexes the table directly and reads the word before it, the experience column.
        Assert.Equal(d.Experience, d.CostAt(-1));
        Assert.Equal(5, d.CostAt(99));
        // And the lookup ignores case, as the engine's own does.
        Assert.Same(d, file["tEsT"]);
        Assert.Null(file["nope"]);
    }
}
