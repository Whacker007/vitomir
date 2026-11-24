// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Modules.Enhanced.Configuration;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// Directional attacks' data (<c>Data/combat/directions.json</c>): the shipped file reads, its numbers are the
/// platform's own defaults (one source of truth), its table gives every kind its two quick blows and Christof's by name; a wrong
/// direction, rule or number is refused by name; the module registers directional attacks on, and off with <c>attack-spin</c>.
/// </summary>
public sealed class DirectionDataTests
{
    static DirectionsData? Read(string json, List<string> problems)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        return DirectionsData.Read(doc.RootElement.Clone(), "directions.json", problems);
    }

    static string Shipped => File.ReadAllText(TestData.File("combat/directions.json"));

    [Fact]
    public void The_shipped_table_reads_and_its_numbers_are_the_platform_s_defaults()
    {
        var problems = new List<string>();
        var d = Read(Shipped, problems);
        Assert.Empty(problems);
        Assert.NotNull(d);
        Assert.Equal(new DirectionSettings(), d!.Settings);
        Assert.Equal(3, d.Rows.Count);
        Assert.Equal(new DirectionRow(null, null, "stitched:left|quick:1", "stitched:right|quick:0", null, null), d.Rows[0]);   // the wind-up first
        Assert.Equal(new DirectionRow("Christof", 1, "stitched:left|flat:chrisAttack2Sword|chrisAttack2Sword", "stitched:right|flat:chrisAttackSword|chrisAttackSword", null, null), d.Rows[1]);   // the flattened side blows, else 1.1's (swings-oneone); with their wind-ups first
        Assert.Equal(new DirectionRow(null, 1, null, null, "stitched:overhead", "stitched:thrust"), d.Rows[2]);   // the stitched blows
    }

    [Fact]
    public void A_wrong_direction_rule_or_number_is_refused_by_name()
    {
        var problems = new List<string>();
        var bad = Shipped.Replace("\"foe_cone_degrees\": 60", "\"foe_cone_degrees\": 400")
                         .Replace("\"rule\": \"turn\"", "\"rule\": \"sideways\"")
                         .Replace("\"right\": \"stitched:right|quick:0\",", "\"right\": \"stitched:right|quick:0\", \"upward\": \"quick:0\",");   // the row names the wind-up first
        Assert.Null(Read(bad, problems));
        Assert.Contains(problems, p => p.Contains("foe_cone_degrees 400"));
        Assert.Contains(problems, p => p.Contains("ready.rule 'sideways'"));
        Assert.Contains(problems, p => p.Contains("'upward' is not a direction"));
    }

    [Fact]
    public void The_module_registers_directional_attacks_on_and_attack_spin_turns_them_off()
    {
        foreach (var spin in new[] { false, true })
        {
            var m = spin ? TestData.Sandbox(null, EnhancedModule.AttackSpinSwitch) : TestData.Sandbox();
            var b = new WorldBuilder(1);
            m.Register(b);
            var w = b.Build();
            var dirs = w.Service<DirectionalAttacks>();
            Assert.Equal(!spin, dirs.Enabled);
            Assert.Equal(!spin, DirectionalAttacks.On(w, out _));
            Assert.Equal(3, dirs.Rows.Count);
        }
    }
}
