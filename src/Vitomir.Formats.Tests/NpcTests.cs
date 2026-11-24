// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats.Tests;

/// <summary>
/// The player-character files. Eighteen ship, and between them they name every one of the nineteen
/// stats — so the whole `ACTOR_STAT_*` enum is covered by the data rather than by an assumption.
/// </summary>
public class NpcTests
{
    // Christof's, verbatim and trimmed. The header is his own file's, and the ordering note is the
    // one the original had to obey because it applied each line as it read it.
    const string Christof = """
        // Christof Dark Ages human
        // Note: bloodpool MUST come before blood!!!

        name            Christof
        cash            50
        totalexperience 0

        // STATS        min     max     current
        strength        0       100     30
        dexterity       0       100     35
        bloodpool       0       1000    100
        blood           0       1000    100
        humanity        0       100     60
        frenzy          0       100     0
        faith           0       100     50
        generation      0       100     9
        bloodrating     0       100     1.0

        // DISCIPLINE   name                    level           quick
        discipline      BloodHealing            3               0

        // ITEM         template        (main | belt | equip)
        item            CuthbertLetter          belt
        item            broadsword              equip
        """;

    [Fact]
    public void A_player_character_file_reads_as_a_character()
    {
        var npc = NpcFile.Parse(Christof, "Christof.NPC");
        Assert.Empty(npc.Warnings);
        Assert.Equal("Christof", npc.Name);
        Assert.Equal(50, npc.Cash);
        Assert.Equal(0, npc.TotalExperience);

        // The numbers on a real character sheet, out of the file rather than the template.
        Assert.Equal(30f, npc.Stats[(int)ActorStatIndex.Strength].Current);
        Assert.Equal(35f, npc.Stats[(int)ActorStatIndex.Dexterity].Current);
        Assert.Equal(60f, npc.Stats[(int)ActorStatIndex.Humanity].Current);
        Assert.Equal(50f, npc.Stats[(int)ActorStatIndex.Faith].Current);
        Assert.Equal(9f, npc.Stats[(int)ActorStatIndex.Generation].Current);

        // `1.0` parses as a number rather than falling to zero.
        Assert.Equal(1f, npc.Stats[(int)ActorStatIndex.BloodRating].Current);
    }

    /// <summary>
    /// **The bounds are the point.** `frenzy 0 100 0` is the floor that stops `ef_decreasefrenzy`
    /// running to −infinity, which is what it did in Vitomir until the clamp went in.
    /// </summary>
    [Fact]
    public void Every_stat_row_carries_the_bounds_the_engine_clamps_into()
    {
        var npc = NpcFile.Parse(Christof, "Christof.NPC");
        var frenzy = npc.Stats[(int)ActorStatIndex.Frenzy];
        Assert.Equal(0f, frenzy.Min);
        Assert.Equal(100f, frenzy.Max);
        Assert.Equal(0f, frenzy.Current);
        // The pools get a thousand rather than a hundred, which is why blood can pass 100.
        Assert.Equal(1000f, npc.Stats[(int)ActorStatIndex.BloodPool].Max);
    }

    [Fact]
    public void Disciplines_carry_a_level_and_the_power_slot_they_sit_in()
    {
        var npc = NpcFile.Parse(Christof, "Christof.NPC");
        var d = Assert.Single(npc.Disciplines);
        Assert.Equal("BloodHealing", d.Name);
        Assert.Equal(3, d.Level);
        Assert.Equal(0, d.Quick);
    }

    [Fact]
    public void Starting_items_name_a_template_and_where_it_goes()
    {
        var npc = NpcFile.Parse(Christof, "Christof.NPC");
        Assert.Equal(2, npc.Items.Count);
        Assert.Equal(NpcPlacement.Belt, npc.Items[0].Where);
        Assert.Equal("broadsword", npc.Items[1].Template);
        Assert.Equal(NpcPlacement.Equip, npc.Items[1].Where);
    }

    [Fact]
    public void An_unknown_keyword_is_reported_rather_than_swallowed()
    {
        // A mod's new keyword has to be visible, which is the whole of the mod rule in miniature.
        var npc = NpcFile.Parse("name Nobody\nwibble 3\n", "test.NPC");
        Assert.Equal("Nobody", npc.Name);
        Assert.Single(npc.Warnings);
        Assert.Contains("wibble", npc.Warnings[0]);
    }

    /// <summary>
    /// **`ACTOR_RPGSTATS` is a name, and half the templates write the extension into it.**
    /// `darkAges.not`'s `Christof` says `Christof`; `Multiplayer.not`'s `MP_Christof_DA` says
    /// `MP_Christof.NPC`; and `MP_Serena` and `MP_Kazi` disagree with each other about the same
    /// file. Appending unconditionally sends every multiplayer character to a path that does not
    /// exist, which is what this did until then.
    /// </summary>
    [Theory]
    [InlineData("Christof", "misc/Christof.npc")]
    [InlineData("MP_Christof.NPC", "misc/MP_Christof.npc")]
    [InlineData("MP_Serena", "misc/MP_Serena.npc")]
    [InlineData("ChristofModern.npc", "misc/ChristofModern.npc")]
    public void The_rpgstats_tag_is_a_name_whether_or_not_it_carries_the_extension(string tag, string path) =>
        Assert.Equal(path, NpcFile.PathFor(tag));

    /// <summary>Every shipped file, from the player's own install.</summary>
    [Fact]
    public void Every_shipped_player_character_file_parses_with_nothing_unknown()
    {
        using var nob = GameData.Open("Resource.nob");
        var files = nob.NamesWithExtension(".npc").ToList();
        Assert.SkipWhen(files.Count == 0, "no .NPC entries in Resource.nob");

        var warnings = new List<string>();
        var named = new HashSet<int>();
        foreach (var path in files)
        {
            var npc = NpcFile.Read(nob.Read(path), Path.GetFileName(path));
            warnings.AddRange(npc.Warnings);
            Assert.False(string.IsNullOrWhiteSpace(npc.Name), $"{npc.File} has no name");
            for (int i = 0; i < NpcFile.StatCount; i++)
                if (npc.Stats[i].Authored) named.Add(i);
        }
        Assert.True(warnings.Count == 0, string.Join("\n", warnings));

        Assert.Equal(18, files.Count);          // eighteen ship; a mod adds to this, the archive does not

        // Between them the shipped files name **every** stat, so the enum is covered by data.
        Assert.Equal(NpcFile.StatCount, named.Count);
    }

    /// <summary>
    /// **Three shipped rows ask for a value their own maximum forbids**, and they are the same
    /// mistake three times: the pool's current copied into the holder's row without the holder's
    /// maximum coming with it. The engine's clamp corrects them on load, so this is pinned as a
    /// property of the data rather than repaired in the reader — a mod that authors the same shape
    /// has to behave the same way.
    /// </summary>
    [Fact]
    public void Three_shipped_rows_ask_for_more_than_their_own_maximum_allows()
    {
        using var nob = GameData.Open("Resource.nob");
        var files = nob.NamesWithExtension(".npc").ToList();
        Assert.SkipWhen(files.Count == 0, "no .NPC entries in Resource.nob");

        var over = new List<string>();
        foreach (var path in files)
        {
            var npc = NpcFile.Read(nob.Read(path), Path.GetFileName(path));
            for (int i = 0; i < NpcFile.StatCount; i++)
            {
                var row = npc.Stats[i];
                if (row.Authored && (row.Current > row.Max || row.Current < row.Min))
                    over.Add($"{Path.GetFileNameWithoutExtension(path).ToLowerInvariant()}:{NpcFile.StatNames[i]}");
            }
        }
        over.Sort(StringComparer.Ordinal);
        Assert.Equal(["christof:mana", "christofmodern:blood", "christophda:mana"], over);
    }

    /// <summary>The stat order this file's keywords are in, mirrored so the tests can read.</summary>
    enum ActorStatIndex
    {
        Strength = 0, Dexterity = 1, Stamina = 2, Perception = 3, Intelligence = 4, Wits = 5,
        Blood = 6, Humanity = 7, Frenzy = 8, Generation = 9, Faith = 10, Charisma = 11,
        Manipulation = 12, Appearance = 13, FrenzyRating = 14, BloodRating = 15, BloodPool = 16,
        Mana = 17, ManaPool = 18,
    }
}
