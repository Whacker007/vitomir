// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Net;
using Vitomir.Engine.World;
using Vitomir.Formats;
using Vitomir.TestData;

namespace Vitomir.Engine.Tests;

/// <summary>
/// 1.1's character file made Vitomir's (<see cref="OriginalCharacter"/>): the record's fields to the <c>.vplayer</c>'s, on bytes built
/// as 1.1 writes them, and on two real ones where they are (never in the repository).
/// </summary>
public class OriginalCharacterTests
{
    [Fact]
    public void The_record_s_sheet_disciplines_pack_purse_flags_and_lasting_effects_come_across()
    {
        var notes = new List<string>();
        var c = OriginalCharacter.From(Npf.Read(new NpfBytes { Flags = 0x8000 | Actor.Flag.Dead, Flags2 = 0x10 }.Build()), notes);
        Assert.Equal(("Anna", "mp_christof_da", ActorType.Vampire, "brujah", 250L, 345), (c.Name, c.Template, c.Type, c.Clan, c.Experience, c.Cash));
        var s = c.Saved!;
        Assert.Equal(10f, s.BaseStats[0]);
        Assert.Equal(30f, s.Stats[18]);
        Assert.Equal((1250L, 250L, 80f, 120f, true, 345, false), (s.Experience, s.Spendable, s.Health, s.MaxHealth, s.Dead, s.Cash, s.Kit));
        Assert.Equal((0x8000u | Actor.Flag.Dead, 0x10u), (s.Flags, s.Flags2));
        Assert.Equal([new SheetDiscipline("potence", 2, 1), new SheetDiscipline("celerity", 1, -1), new SheetDiscipline("feed", 0, -1), new SheetDiscipline("awe", -1, -1)], s.Disciplines);
        Assert.Equal([new SheetItem("longsword", 0x8, 0, 0, 1), new SheetItem("bloodpack", 0x400, 3, 1, 4), new SheetItem("stake", 0x4000, 9, 8, 1)], s.Pack);
        // The one for ever comes; the timed one (another game's clock) and the worn sword's (it comes with the sword) do not.
        Assert.Equal(["ef_decreasefrenzy"], s.Effects!.Select(e => e.Name));
        Assert.Equal((0, 0, -2f, 200f), (s.Effects![0].Left, s.Effects[0].NextIn, s.Effects[0].Value, s.Effects[0].Fade));
        Assert.Contains(notes, n => n.Contains("1 of Anna's effects had a time left", StringComparison.Ordinal));
        Assert.Contains(notes, n => n.StartsWith("Anna: mp_christof_da, Kindred (brujah), 1250 experience (250 to spend), 3 disciplines known, 3 items", StringComparison.Ordinal));
    }

    [Fact]
    public void A_human_has_no_clan_a_bare_one_is_dressed_from_its_kit_and_a_name_given_wins()
    {
        var bytes = new NpfBytes { Type = 2, Clan = "human", ClanName = "Human" };
        bytes.Items.Clear();
        var c = OriginalCharacter.From(Npf.Read(bytes.Build()), [], name: "  Someone Else  ");
        Assert.Equal(("Someone Else", ActorType.Human, ""), (c.Name, c.Type, c.Clan));
        Assert.True(c.Saved!.Kit);
        Assert.Empty(c.Saved.Pack);
        var parsed = Crypt.Parse(new Crypt(Path.GetTempPath()).Text(c))!;                // and it reads back as a .vplayer
        Assert.Equal(c.Saved.Disciplines, parsed.Saved!.Disciplines);
        Assert.Equal((c.Name, c.Template, c.Saved.Cash, c.Saved.Kit), (parsed.Name, parsed.Template, parsed.Saved.Cash, parsed.Saved.Kit));
    }

    [Fact]
    public void Original_characters_are_made_Vitomir_s()
    {
        string[] samples =
        [
            Path.Combine(Environment.GetEnvironmentVariable("VITOMIR_NPF_SAMPLES") ?? "", "Games--Vampire The Masquerade - Redemption", "Crypt", "npf000000.npf"),
            Path.Combine(Environment.GetEnvironmentVariable("VTMR_GAME_DIR") ?? @"C:\Games\VTMR", "Crypt", "npf000000.npf"),
        ];
        var found = samples.Where(File.Exists).ToArray();
        Assert.SkipWhen(found.Length == 0, "no 1.1 character file here (a player's own samples)");
        foreach (var path in found)
        {
            var npf = Npf.ReadFile(path);
            var c = OriginalCharacter.From(npf, []);
            Assert.Equal(npf.Header.Name, c.Name);
            Assert.Equal(npf.Thing.Name, c.Template);
            Assert.Equal((long)npf.Player.Field3EC, c.Saved!.Experience);
            Assert.Equal(npf.Actor.Disciplines.Length, c.Saved.Disciplines.Count);
            Assert.True(c.Saved.Kit);                                                    // never taken into a game: nothing of its own carried
            Assert.Equal((int)npf.Actor.Field230, c.Cash);
        }
    }
}
