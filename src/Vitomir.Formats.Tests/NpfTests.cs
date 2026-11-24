// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.TestData;

namespace Vitomir.Formats.Tests;

/// <summary>
/// 1.1's multiplayer character file: a 0x6c-byte header and one player record of the save's grammar, read to its last byte —
/// on bytes built here (<see cref="NpfBytes"/>) and on two real ones, when they are on this machine (never in the repository).
/// </summary>
public class NpfTests
{
    [Fact]
    public void A_built_character_reads_whole_header_sheet_pack_disciplines_effects_and_player()
    {
        var c = Npf.Read(new NpfBytes().Build());
        Assert.Equal(("npf", 5u, "Anna", "npf000003", "Brujah", 1250u), (c.Header.Magic, c.Header.Version, c.Header.Name, c.Header.Login, c.Header.ClanName, c.Header.Experience));
        Assert.Null(c.Header.Refusal);
        Assert.Equal(NsgThingType.Player, c.Thing.Type);
        Assert.Equal("mp_christof_da", c.Thing.Name);
        Assert.Equal("mpchristof.nag", c.Thing.Template);
        Assert.Equal("christof.nod", c.Thing.Model.Model);
        Assert.Equal(("brujah", 345u, 0x8000u, 80f, 120f), (c.Actor.Text208, c.Actor.Field230, c.Actor.Field1F4, c.Actor.Health, c.Actor.MaxHealth));
        Assert.Equal(10f, c.Actor.BaseStats[0]);
        Assert.Equal(30f, c.Actor.Stats[18]);
        Assert.Equal(["potence", "celerity", "feed", "awe"], c.Actor.Disciplines.Select(d => d.Name));
        Assert.Equal([2, 1, 0, -1], c.Actor.Disciplines.Select(d => d.Level));
        Assert.Equal("potence", c.Actor.SelectedDiscipline);
        Assert.Equal(["longsword", "bloodpack", "stake"], c.Actor.Inventory.Items.Select(i => i.Name));
        Assert.Equal([0x8u, 0x400u, 0x4000u], c.Actor.Inventory.Items.Select(i => i.Item!.Location));
        Assert.Equal("punch", c.Actor.Inventory.UnarmedWeapon?.Name);
        Assert.Equal(["ef_decreasefrenzy", "ef_potence", "ef_swordworn"], c.Actor.Effects.Select(e => e.Name));
        Assert.Equal(("Anna", 250u, 1250u, "christofH.nod"), (c.Player.Text3C4, c.Player.Field3E8, c.Player.Field3EC, c.Player.Text3F0));
    }

    [Fact]
    public void One_one_s_own_check_is_the_magic_and_version_5_and_a_short_or_cut_file_does_not_read()
    {
        Assert.Equal("its magic is \"nsg\", not \"npf\"", Npf.ReadHeader(new NpfBytes { Magic = "nsg" }.Build())!.Refusal);
        Assert.Equal("it is version 4; 1.1 reads only 5", Npf.ReadHeader(new NpfBytes { Version = 4 }.Build())!.Refusal);
        Assert.Null(Npf.ReadHeader(new byte[0x6b]));
        Assert.Throws<NodFormatException>(() => Npf.Read(new NpfBytes { Version = 4 }.Build()));
        var whole = new NpfBytes().Build();
        Assert.Throws<NodFormatException>(() => Npf.Read(whole[..^1]));                  // cut short: the player's last byte is missing
        Assert.Throws<NodFormatException>(() => Npf.Read([.. whole, 0]));                 // one over: not the writer's
        Assert.Throws<NodFormatException>(() => Npf.Read(new byte[0x20]));
    }

    /// <summary>Two installs' own, where they are: a copy under <c>VITOMIR_NPF_SAMPLES</c> and the game folder's (<c>VTMR_GAME_DIR</c>).</summary>
    public static readonly string[] Samples =
    [
        Path.Combine(Environment.GetEnvironmentVariable("VITOMIR_NPF_SAMPLES") ?? "", "Games--Vampire The Masquerade - Redemption", "Crypt", "npf000000.npf"),
        Path.Combine(GameData.Dir ?? "", "Crypt", "npf000000.npf"),
    ];

    [Fact]
    public void Real_characters_read_to_their_last_byte_as_1_1_wrote_them()
    {
        var found = Samples.Where(File.Exists).ToArray();
        Assert.SkipWhen(found.Length == 0, "no 1.1 character file here (a player's own samples: VITOMIR_NPF_SAMPLES, or VTMR_GAME_DIR's Crypt)");
        foreach (var path in found)
        {
            var c = Npf.ReadFile(path);
            Assert.Null(c.Header.Refusal);
            // The header is written from the player it heads: the name and the experience are the player's own.
            Assert.Equal(c.Header.Name, c.Player.Text3C4);
            Assert.Equal(c.Header.Experience, c.Player.Field3EC);
            Assert.Equal(Path.GetFileNameWithoutExtension(path), c.Header.Login);
            Assert.StartsWith("mp_", c.Thing.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(19, c.Actor.Stats.Length);
            Assert.NotNull(c.Actor.Inventory.UnarmedWeapon);                              // every player is asserted to have one
            Assert.Contains(c.Actor.Disciplines, d => d.Name == "feed");
        }
    }
}
