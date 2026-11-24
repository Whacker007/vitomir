// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Combat;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The arena's spec: what it reads — fighters with their templates, weapons, sheets, places,
/// facings, brains and foes, the bouts, the seed base, the seconds, the tapes, a sweep or a grid — and the defaults it fills
/// in (round the free yard, facing in, the next fighter the foe); every error by name; the points of a grid in order; a
/// bout's cast written and read back the same; the example in tools/platform/arena read as it stands. Needs no game files.
/// </summary>
public sealed class ArenaSpecTests
{
    /// <summary>A folder that passes for a level package (it has a level.json), for reading specs without a level.</summary>
    internal static readonly Lazy<string> FakeLevel = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-arena-fake-level-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "level.json"), "{}");
        return dir;
    });

    static ArenaSpec Read(string json) => ArenaSpec.Read(json, Path.GetTempPath(), FakeLevel.Value);

    const string Two = """ "fighters": [ { "name": "A" }, { "name": "B", "template": "rudolfGuard" } ] """;

    [Fact]
    public void A_full_spec_reads_its_fighters_brains_sheets_and_sweep()
    {
        var sheetFile = Path.Combine(Path.GetTempPath(), $"vitomir-arena-sheet-{Environment.ProcessId}.json");
        File.WriteAllText(sheetFile, "{ \"sheet\": true }");
        var spec = Read($$"""
        {
          // comments and trailing commas are allowed
          "title": "t", "time": "day", "bouts": 7, "seed": 99, "seconds": 12.5, "tapes": 1,
          "fighters": [
            { "name": "Christof", "template": "Christof", "weapon": "Rapier", "place": [30, 4, 0.5], "facing": 90,
              "sheet": { "template": "sandbox.townsperson", "values": { "melee": 3, "strength": 40 }, "powers": { "potence": { "rank": 2, "options": ["potence.passive"] } } },
              "brain": { "kind": "duel", "strongShare": 0.5, "blockChance": 1 }, "foe": "Dummy" },
            { "name": "Dummy", "template": "rudolfGuard", "sheet": { "file": "{{sheetFile.Replace("\\", "\\\\")}}" }, "brain": { "kind": "dummy", "mode": "block" } },
            { "name": "S", "brain": { "kind": "script", "actions": "attack@1, strong@2+0.5^10, block@3+1" } },
          ],
          "sweep": { "fighter": "Christof", "power": "potence", "options": ["potence.passive"], "from": 0, "to": 4, "step": 2 },
        }
        """);
        Assert.Equal("t", spec.Title);
        Assert.False(spec.Night);
        Assert.Equal((7, 99UL, 12.5f, 1), (spec.Bouts, spec.Seed, spec.Seconds, spec.Tapes));
        Assert.Equal(3, spec.Fighters.Count);
        var c = spec.Fighters[0];
        Assert.Equal(("Christof", "Christof", "Rapier", 30f, 4f, 0.5f, 90f), (c.Name, c.Template, c.Weapon, c.X, c.Y, c.Z, c.Facing));
        Assert.Equal(CastBrain.Duel, c.Brain);
        Assert.Equal(0.5f, c.Numbers!.StrongShare);
        Assert.Equal(1f, c.Numbers.BlockChance);
        Assert.Equal(new DuelBrainSettings().BackOffChance, c.Numbers.BackOffChance);
        Assert.Equal(1, c.Foe);
        Assert.Equal("sandbox.townsperson", c.Sheet.Template);
        Assert.Equal([new CastValue("melee", 3), new CastValue("strength", 40)], c.Sheet.Values[..2]);
        // The townsperson has no option of Potence, so the spec names it (a rank alone would do nothing: refused by name).
        Assert.Equal(("potence", 2, true), (c.Sheet.Values[2].Id, c.Sheet.Values[2].Value, c.Sheet.Values[2].Power));
        Assert.Equal(["potence.passive"], c.Sheet.Values[2].Options!);
        Assert.Equal(CastBrain.Block, spec.Fighters[1].Brain);
        Assert.Equal("{ \"sheet\": true }", spec.Fighters[1].Sheet.Text);
        Assert.Equal(CastBrain.Script, spec.Fighters[2].Brain);
        Assert.Equal(0, spec.Fighters[2].Foe);     // the next fighter, round
        var sweep = Assert.Single(spec.Sweeps);
        Assert.Equal((0, "potence", true), (sweep.Fighter, sweep.Id, sweep.Power));
        Assert.Equal([0, 2, 4], sweep.Values);
        Assert.Equal(["potence.passive"], sweep.Options!);
        Assert.Equal("Christof.power.potence", sweep.Label(spec));
    }

    [Fact]
    public void Fighters_with_no_place_stand_round_the_free_yard_facing_in_each_against_the_next()
    {
        var spec = Read($$"""{ {{Two}} }""");
        Assert.Equal(FakeLevel.Value, spec.Level);
        Assert.True(spec.Night);
        Assert.Equal((10, 1UL, 60f, 2), (spec.Bouts, spec.Seed, spec.Seconds, spec.Tapes));
        var (a, b) = (spec.Fighters[0], spec.Fighters[1]);
        Assert.Equal((38f, 3f, 0.02f, 0f), (a.X, a.Y, a.Z, a.Facing));
        Assert.Equal((42f, 3f, 180f), (b.X, b.Y, b.Facing));
        Assert.Equal(("Christof", "rudolfGuard"), (a.Template, b.Template));
        Assert.Equal((1, 0), (a.Foe, b.Foe));
        Assert.Equal("BroadSword", a.Weapon);     // Christof's starting sword, as the player's
        Assert.Null(b.Weapon);                    // the guard's own
        Assert.Null(a.Numbers);
        Assert.Equal(CastBrain.Duel, a.Brain);
        Assert.Empty(spec.Sweeps);
        Assert.Single(spec.Points);
        Assert.Empty(spec.Points[0]);
    }

    [Theory]
    [InlineData("not json", ArenaSpecError.NotJson)]
    [InlineData("[1, 2]", ArenaSpecError.NotJson)]
    [InlineData("""{ "fighters": [ { } ] }""", ArenaSpecError.TooFewFighters)]
    [InlineData("""{ "fighters": [ { }, { } ], "bout": 3 }""", ArenaSpecError.UnknownField)]
    [InlineData("""{ "fighters": [ { "nmae": "A" }, { } ] }""", ArenaSpecError.UnknownField)]
    [InlineData("""{ "fighters": [ { "name": "A" }, { "name": "a" } ] }""", ArenaSpecError.BadName)]
    [InlineData("""{ "fighters": [ { "name": "A:B" }, { } ] }""", ArenaSpecError.BadName)]
    [InlineData("""{ "fighters": [ { "place": [1] }, { } ] }""", ArenaSpecError.BadPlace)]
    [InlineData("""{ "fighters": [ { "brain": "berserk" }, { } ] }""", ArenaSpecError.BadBrain)]
    [InlineData("""{ "fighters": [ { "brain": { "kind": "dummy", "mode": "dance" } }, { } ] }""", ArenaSpecError.BadBrain)]
    [InlineData("""{ "fighters": [ { "brain": { "kind": "duel", "rage": 1 } }, { } ] }""", ArenaSpecError.UnknownField)]
    [InlineData("""{ "fighters": [ { "brain": { "kind": "duel", "blockChance": 2 } }, { } ] }""", ArenaSpecError.OutOfRange)]
    [InlineData("""{ "fighters": [ { "brain": { "kind": "script", "actions": "dance@1" } }, { } ] }""", ArenaSpecError.BadScript)]
    [InlineData("""{ "fighters": [ { "brain": { "kind": "script" } }, { } ] }""", ArenaSpecError.BadScript)]
    [InlineData("""{ "fighters": [ { "sheet": 5 }, { } ] }""", ArenaSpecError.BadSheet)]
    [InlineData("""{ "fighters": [ { "sheet": { "values": { "strength": 1.5 } } }, { } ] }""", ArenaSpecError.BadSheet)]
    [InlineData("""{ "fighters": [ { "sheet": { "file": "no-such-sheet.json" } }, { } ] }""", ArenaSpecError.SheetFileMissing)]
    [InlineData("""{ "fighters": [ { "name": "A", "foe": "A" }, { } ] }""", ArenaSpecError.UnknownFoe)]
    [InlineData("""{ "fighters": [ { }, { } ], "sweep": { "fighter": "A" } }""", ArenaSpecError.BadSweep)]
    [InlineData("""{ "fighters": [ { }, { } ], "sweep": { "fighter": "A", "trait": "strength", "power": "potence", "values": [1] } }""", ArenaSpecError.BadSweep)]
    [InlineData("""{ "fighters": [ { }, { } ], "sweep": { "fighter": "A", "trait": "strength", "values": [] } }""", ArenaSpecError.BadSweep)]
    [InlineData("""{ "fighters": [ { }, { } ], "sweep": { "fighter": "Z", "trait": "strength", "values": [1] } }""", ArenaSpecError.SweepUnknownFighter)]
    [InlineData("""{ "fighters": [ { }, { } ], "sweeps": [ { "fighter": "A", "trait": "strength", "values": [1] }, { "fighter": "A", "trait": "stamina", "values": [1] }, { "fighter": "B", "trait": "dexterity", "values": [1] } ] }""", ArenaSpecError.TooManySweeps)]
    [InlineData("""{ "fighters": [ { }, { } ], "bouts": 0 }""", ArenaSpecError.OutOfRange)]
    [InlineData("""{ "fighters": [ { }, { } ], "seed": "one" }""", ArenaSpecError.WrongType)]
    [InlineData("""{ "fighters": [ { }, { } ], "time": "noon" }""", ArenaSpecError.OutOfRange)]
    [InlineData("""{ "fighters": [ { }, { } ], "combat_tempo": 9 }""", ArenaSpecError.OutOfRange)]
    [InlineData("""{ "fighters": [ { }, { } ], "combat_tempo": "slow" }""", ArenaSpecError.WrongType)]
    [InlineData("""{ "fighters": [ { }, { } ], "level": "/no/such/level.l2" }""", ArenaSpecError.LevelNotFound)]
    // Powers with options, Blood, the quick slots, the shield
    [InlineData("""{ "fighters": [ { "sheet": { "powers": { "celerity": { "rank": 2, "options": [] } } } }, { } ] }""", ArenaSpecError.BadPower)]
    [InlineData("""{ "fighters": [ { "sheet": { "powers": { "celerity": { "rank": 2, "options": ["Celerity Active"] } } } }, { } ] }""", ArenaSpecError.BadPower)]
    [InlineData("""{ "fighters": [ { "sheet": { "powers": { "celerity": { "rank": 2, "options": ["celerity.active", "celerity.active"] } } } }, { } ] }""", ArenaSpecError.BadPower)]
    [InlineData("""{ "fighters": [ { "sheet": { "powers": { "celerity": { "options": ["celerity.active"] } } } }, { } ] }""", ArenaSpecError.BadPower)]
    [InlineData("""{ "fighters": [ { "sheet": { "powers": { "celerity": { "rank": 2, "option": "celerity.active" } } } }, { } ] }""", ArenaSpecError.UnknownField)]
    [InlineData("""{ "fighters": [ { "sheet": { "powers": { "celerity": "two" } } }, { } ] }""", ArenaSpecError.BadPower)]
    [InlineData("""{ "fighters": [ { "sheet": { "powers": { "celerity": 1.5 } } }, { } ] }""", ArenaSpecError.BadPower)]
    [InlineData("""{ "fighters": [ { }, { } ], "sweep": { "fighter": "A", "power": "celerity", "options": [], "values": [0, 1] } }""", ArenaSpecError.BadPower)]
    [InlineData("""{ "fighters": [ { }, { } ], "sweep": { "fighter": "A", "trait": "melee", "options": ["celerity.active"], "values": [1] } }""", ArenaSpecError.BadSweep)]
    [InlineData("""{ "fighters": [ { "slots": [0] }, { } ] }""", ArenaSpecError.BadSlots)]
    [InlineData("""{ "fighters": [ { "slots": [9] }, { } ] }""", ArenaSpecError.BadSlots)]
    [InlineData("""{ "fighters": [ { "slots": [1, 1] }, { } ] }""", ArenaSpecError.BadSlots)]
    [InlineData("""{ "fighters": [ { "slots": 1 }, { } ] }""", ArenaSpecError.BadSlots)]
    [InlineData("""{ "fighters": [ { "blood": -1 }, { } ] }""", ArenaSpecError.OutOfRange)]
    [InlineData("""{ "fighters": [ { "blood": "full" }, { } ] }""", ArenaSpecError.WrongType)]
    [InlineData("""{ "fighters": [ { "shield": "no" }, { } ] }""", ArenaSpecError.WrongType)]
    [InlineData("""{ "fighters": [ { "brain": { "kind": "duel", "renewSeconds": -1 } }, { } ] }""", ArenaSpecError.OutOfRange)]
    public void Each_error_is_named(string json, ArenaSpecError expected)
    {
        var e = Assert.Throws<ArenaSpecException>(() => Read(json));
        Assert.Equal(expected, e.Error);
        Assert.StartsWith("arena: ", e.Message);
    }

    [Fact]
    public void No_level_anywhere_is_named()
    {
        string? before = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        try
        {
            Environment.SetEnvironmentVariable("VITOMIR_COURSE_LEVEL", null);
            var e = Assert.Throws<ArenaSpecException>(() => ArenaSpec.Read($$"""{ {{Two}} }""", Path.GetTempPath()));
            Assert.Equal(ArenaSpecError.NoLevel, e.Error);
        }
        finally { Environment.SetEnvironmentVariable("VITOMIR_COURSE_LEVEL", before); }
        Assert.Equal(ArenaSpecError.FileMissing, Assert.Throws<ArenaSpecException>(() => ArenaSpec.ReadFile("/no/such/spec.json", FakeLevel.Value)).Error);
    }

    [Fact]
    public void A_grid_s_points_run_the_second_sweep_inside_the_first_and_its_cast_sets_both_values_last()
    {
        var spec = Read($$"""
        { {{Two}}, "sweeps": [ { "fighter": "A", "trait": "strength", "values": [10, 20] },
                               { "fighter": "B", "trait": "stamina", "from": 5, "to": 1, "step": 2 } ] }
        """);
        Assert.Equal([[10, 5], [10, 3], [10, 1], [20, 5], [20, 3], [20, 1]], spec.Points);
        var cast = spec.Cast([20, 3]);
        Assert.Equal([new CastValue("strength", 20)], cast.Fighters[0].Sheet.Values);
        Assert.Equal([new CastValue("stamina", 3)], cast.Fighters[1].Sheet.Values);
    }

    [Fact]
    public void A_cast_is_written_and_read_back_the_same()
    {
        var spec = Read($$"""{ {{Two}}, "sweep": { "fighter": "A", "trait": "strength", "values": [33] } }""");
        var cast = spec.Cast(spec.Points[0]);
        string text = cast.Write();
        var back = ArenaCast.Read(text);
        Assert.Equal(text, back.Write());
        Assert.Equal(cast.Fighters[0].Sheet.Values, back.Fighters[0].Sheet.Values);
        Assert.Equal(ArenaSpecError.CastUnreadable, Assert.Throws<ArenaSpecException>(() => ArenaCast.Read("{\"Fighters\": []}")).Error);
        Assert.Equal("arena:Christof|", ArenaCast.KindKey("Christof", null));
    }

    [Fact]
    public void The_example_in_tools_reads_as_it_stands()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tools", "platform", "arena"))) dir = dir.Parent;
        Assert.SkipUnless(dir is not null, "the repository's tools/platform/arena is not above the tests");
        // The receipts too (their sheet files aside), and every fighter names its sheet (so the dressing rule changes nothing).
        var arena = Path.Combine(dir!.FullName, "tools", "platform", "arena");
        var files = Directory.GetFiles(arena, "*.json").Concat(Directory.GetFiles(Path.Combine(arena, "receipts"), "*.json")).Where(f => !f.EndsWith(".sheet.json", StringComparison.Ordinal)).ToList();
        Assert.True(files.Count >= 20, $"{files.Count} specs");
        foreach (var file in files)
        {
            var spec = ArenaSpec.ReadFile(file, FakeLevel.Value);
            Assert.True(spec.Fighters.Count >= 2, file);
            Assert.All(spec.Fighters, f => Assert.True(f.Sheet.Template is not null || f.Sheet.Text is not null, $"{file}: {f.Name} names no sheet"));
        }
        // The matched pairs and the Celerity sweeps.
        var bare = ArenaSpec.ReadFile(Path.Combine(arena, "matched-blade-no-shield.json"), FakeLevel.Value);
        var shielded = ArenaSpec.ReadFile(Path.Combine(arena, "matched-blade-and-shield.json"), FakeLevel.Value);
        Assert.All(bare.Fighters, f => Assert.Equal(("Rapier", false), (f.Weapon, f.Shield)));
        Assert.All(shielded.Fighters, f => Assert.Equal(("weapRapier_shield", true), (f.Weapon, f.Shield)));
        Assert.Equal((200, bare.Seed), (bare.Bouts, shielded.Seed));
        foreach (var (name, switches) in new[] { ("celerity-matched.json", Array.Empty<string>()), ("celerity-matched-oneone.json", ["celerity-curve-oneone"]) })
        {
            var c = ArenaSpec.ReadFile(Path.Combine(arena, name), FakeLevel.Value);
            var sweep = Assert.Single(c.Sweeps);
            Assert.Equal(("celerity", true, 200, bare.Seed), (sweep.Id, sweep.Power, c.Bouts, c.Seed));
            Assert.Equal([0, 1, 2, 3, 4, 5], sweep.Values);
            Assert.Equal(["celerity.active"], sweep.Options!);
            Assert.Equal(switches, c.Switches);
            Assert.All(c.Fighters, f => Assert.Equal(("Rapier", false), (f.Weapon, f.Shield)));
        }
    }

    // ---- the Blood commands' spec, its twin, and the brain's healBelow ----

    [Fact]
    public void The_Blood_commands_spec_and_its_twin_pair_by_seed_and_the_brain_takes_healBelow()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tools", "platform", "arena"))) dir = dir.Parent;
        Assert.SkipUnless(dir is not null, "the repository's tools/platform/arena is not above the tests");
        var arena = Path.Combine(dir!.FullName, "tools", "platform", "arena");
        var with = ArenaSpec.ReadFile(Path.Combine(arena, "common-commands.json"), FakeLevel.Value);
        var without = ArenaSpec.ReadFile(Path.Combine(arena, "common-commands-without.json"), FakeLevel.Value);
        Assert.Equal((200, 3000UL, 200, 3000UL), (with.Bouts, with.Seed, without.Bouts, without.Seed));
        var c = with.Fighters[0];
        Assert.Equal(["blood_strength", "blood_dexterity", "blood_stamina", "blood_healing"], c.Sheet.Values.Select(v => v.Id));
        Assert.All(c.Sheet.Values, v => Assert.Equal((1, true, "common." + v.Id), (v.Value, v.Power, v.Options!.Single())));
        Assert.Equal(0.3f, c.Numbers?.HealBelow ?? new DuelBrainSettings().HealBelow);
        Assert.Empty(without.Fighters[0].Sheet.Values);
        Assert.All(with.Fighters.Concat(without.Fighters), f => Assert.Equal(("Rapier", false), (f.Weapon, f.Shield)));

        var spec = Read("""{ "fighters": [ { "brain": { "kind": "duel", "healBelow": 0.5 } }, { } ] }""");
        Assert.Equal(0.5f, spec.Fighters[0].Numbers!.HealBelow);
        Assert.Equal(0.3f, new DuelBrainSettings().HealBelow);                // 1.1's lowhealth (brujah*.nai)
        Assert.Equal(ArenaSpecError.OutOfRange, Assert.Throws<ArenaSpecException>(() => Read("""{ "fighters": [ { "brain": { "kind": "duel", "healBelow": 1.5 } }, { } ] }""")).Error);
    }

    // ---- powers with options, Blood, the quick slots, the shield ----

    [Fact]
    public void Powers_read_as_a_rank_or_with_options_and_blood_slots_and_shield_reach_the_cast()
    {
        var spec = Read("""
        { "fighters": [
            { "name": "A", "sheet": { "template": "sandbox.newly_embraced", "powers": { "potence": 1, "celerity": { "rank": 3, "options": ["celerity.active"] } } },
              "blood": 70, "slots": [2, 1], "shield": false, "brain": { "kind": "duel", "powerShare": 2, "renewSeconds": 3, "powerWaitSeconds": 0.25 } },
            { "name": "B", "template": "rudolfGuard", "sheet": "sandbox.guard_trained", "shield": true } ],
          "sweep": { "fighter": "A", "power": "celerity", "options": ["celerity.active"], "values": [0, 5] } }
        """);
        var a = spec.Fighters[0];
        Assert.Equal(("potence", 1, true), (a.Sheet.Values[0].Id, a.Sheet.Values[0].Value, a.Sheet.Values[0].Power));
        Assert.Null(a.Sheet.Values[0].Options);                 // a rank alone: the sheet's options kept
        Assert.Equal(["celerity.active"], a.Sheet.Values[1].Options!);
        Assert.Equal((70, false), (a.Blood, a.Shield));
        Assert.Equal([2, 1], a.Slots!);
        Assert.Equal((2f, 3f, 0.25f), (a.Numbers!.PowerShare, a.Numbers.RenewSeconds, a.Numbers.PowerWaitSeconds));
        Assert.Equal((null, null, true), (spec.Fighters[1].Blood, spec.Fighters[1].Slots, spec.Fighters[1].Shield));
        Assert.Equal(0b11, Session.SlotMask(a.Slots!));
        Assert.Equal(0xFF, new DuelBrainSettings().PowerSlots);     // every slot by default

        // The cast at Celerity 5: the swept rank with its options last, the rest as given; written and read back the same.
        var cast = spec.Cast(spec.Points[1]);
        var swept = cast.Fighters[0].Sheet.Values[^1];
        Assert.Equal(("celerity", 5), (swept.Id, swept.Value));
        Assert.Equal(["celerity.active"], swept.Options!);
        Assert.Equal((70, false), (cast.Fighters[0].Blood, cast.Fighters[0].Shield));
        Assert.Equal([2, 1], cast.Fighters[0].Slots!);
        string text = cast.Write();
        Assert.Equal(text, ArenaCast.Read(text).Write());
        // A cast with none of them writes as the earlier build did (a tape's head unchanged).
        var plain = Read($$"""{ {{Two}} }""").Cast([]).Write();
        Assert.DoesNotContain("Blood", plain);
        Assert.DoesNotContain("Slots", plain);
        Assert.DoesNotContain("Shield", plain);
        Assert.DoesNotContain("Options", plain);
    }

    /// <summary>A module's sheets as a stand-in (no game files): what it was asked, and a description of each person's powers.</summary>
    sealed class StandInSheets(bool takesOptions) : Modules.IPersonSheets
    {
        public readonly List<string> Asked = [];
        public readonly Dictionary<string, (int Rank, List<string> Options)> Powers = [];
        public IReadOnlyList<string> Templates => ["t"];
        public bool TryTemplate(Core.EntityId person, string templateId, out string error) { Asked.Add($"template {templateId}"); error = ""; return true; }
        public bool TryImport(Core.EntityId person, string sheetText, out string error) { error = ""; return true; }
        public bool TrySet(Core.EntityId person, string traitId, int value, out string error) { Asked.Add($"set {traitId} {value}"); error = ""; return true; }
        public bool TryLearn(Core.EntityId person, string powerId, int rank, out string error)
        {
            Asked.Add($"learn {powerId} {rank}");
            if (rank == 0) Powers.Remove(powerId);
            else Powers[powerId] = (rank, Powers.TryGetValue(powerId, out var had) ? had.Options : []);
            error = "";
            return true;
        }
        public bool TryLearn(Core.EntityId person, string powerId, int rank, IReadOnlyList<string> options, out string error)
        {
            if (!takesOptions) return ((Modules.IPersonSheets)new Default()).TryLearn(person, powerId, rank, options, out error);
            Asked.Add($"learn {powerId} {rank} {string.Join("+", options)}");
            if (options.Contains("nothing.reads")) { error = $"no rule reads {powerId}'s options"; return false; }
            if (options.FirstOrDefault(o => o is not ("celerity.active" or "potence.passive" or "fortitude.passive" or "nothing.reads")) is { } unknown)
            { error = $"the module knows no option '{unknown}'"; return false; }
            Powers[powerId] = (rank, [.. options]);
            error = "";
            return true;
        }
        public bool TryRead(Core.EntityId person, string id, out int value) { value = Powers.TryGetValue(id, out var p) ? p.Rank : 0; return true; }
        public string Give(Core.EntityId person, string text, string source) => "";
        public bool TryDescribe(Core.EntityId person, out string json)
        {
            json = System.Text.Json.JsonSerializer.Serialize(new { sheet = new { powers = Powers.Select(p => new { options = p.Value.Options, power = p.Key, rank = p.Value.Rank }) } });
            return true;
        }

        /// <summary>The contract's default TryLearn with options (a module that takes none).</summary>
        sealed class Default : Modules.IPersonSheets
        {
            public IReadOnlyList<string> Templates => [];
            public bool TryTemplate(Core.EntityId person, string templateId, out string error) => throw new NotImplementedException();
            public bool TryImport(Core.EntityId person, string sheetText, out string error) => throw new NotImplementedException();
            public bool TrySet(Core.EntityId person, string traitId, int value, out string error) => throw new NotImplementedException();
            public bool TryLearn(Core.EntityId person, string powerId, int rank, out string error) => throw new NotImplementedException();
            public bool TryRead(Core.EntityId person, string id, out int value) => throw new NotImplementedException();
            public string Give(Core.EntityId person, string text, string source) => throw new NotImplementedException();
            public bool TryDescribe(Core.EntityId person, out string json) => throw new NotImplementedException();
        }
    }

    static CastFighter Fighter(params CastValue[] values) =>
        new() { Name = "A", Template = "Christof", X = 0, Y = 0, Z = 0, Facing = 0, Sheet = new CastSheet { Template = "t", Values = [.. values] } };

    [Fact]
    public void A_power_s_options_go_to_the_module_and_a_rank_that_would_do_nothing_is_refused_by_name()
    {
        var e = new Core.EntityId(1, 1);
        // With options: learned with them; a rank alone then keeps them (the sheet has one); 0 unlearns, whatever the options.
        var sheets = new StandInSheets(takesOptions: true);
        Session.GiveSheet(sheets, e, Fighter(new CastValue("celerity", 3, true, ["celerity.active"]), new CastValue("celerity", 4, true)) with { Blood = 40 });
        Assert.Equal(["template t", "learn celerity 3 celerity.active", "learn celerity 4", "set blood 40"], sheets.Asked);
        Assert.Equal(4, sheets.Powers["celerity"].Rank);
        Assert.Equal(["celerity.active"], sheets.Powers["celerity"].Options);
        Session.GiveSheet(sheets, e, Fighter(new CastValue("celerity", 0, true, ["celerity.active"])));
        Assert.Equal("learn celerity 0", sheets.Asked[^1]);
        Assert.False(sheets.Powers.ContainsKey("celerity"));

        // A rank alone on a sheet with no option of it: it would do nothing, so it is refused, by name (the survey's finding).
        var none = Assert.Throws<SessionException>(() => Session.GiveSheet(new StandInSheets(true), e, Fighter(new CastValue("fortitude", 2, true))));
        Assert.Contains("fighter A: power fortitude = 2", none.Message);
        Assert.Contains("would do nothing", none.Message);
        // An option the module's rules do not read: the module refuses it, and the arena says who and what.
        var unread = Assert.Throws<SessionException>(() => Session.GiveSheet(new StandInSheets(true), e, Fighter(new CastValue("potence", 1, true, ["nothing.reads"]))));
        Assert.Contains("power potence = 1 (nothing.reads): no rule reads potence's options", unread.Message);
        // An option the module does not know: refused by name.
        var unknown = Assert.Throws<SessionException>(() => Session.GiveSheet(new StandInSheets(true), e, Fighter(new CastValue("celerity", 2, true, ["celerity.flight"]))));
        Assert.Contains("power celerity = 2 (celerity.flight): the module knows no option 'celerity.flight'", unknown.Message);
        // A module that takes no options (the contract's default) refuses them by name.
        var old = Assert.Throws<SessionException>(() => Session.GiveSheet(new StandInSheets(false), e, Fighter(new CastValue("celerity", 1, true, ["celerity.active"]))));
        Assert.Contains("does not take a power's options", old.Message);
    }

    [Fact]
    public void A_spec_s_combat_tempo_is_read_and_a_tape_carries_the_program_s_tempo()
    {
        // The global combat tempo every bout plays, over the rules module's; none by default.
        Assert.Null(Read($$"""{ {{Two}} }""").CombatTempo);
        Assert.Equal(0.6f, Read($$"""{ {{Two}}, "combat_tempo": 0.6 }""").CombatTempo);
        var options = new SessionOptions { Level = "x" };
        Assert.Equal(0.6f, options.From(new Vitomir.Platform.Control.TapeHeader { Notes = new() { [CombatTempoOverride.TapeNote] = "0.6" } }).CombatTempo);
        Assert.Null(options.From(new Vitomir.Platform.Control.TapeHeader()).CombatTempo);
        Assert.Null(options.From(new Vitomir.Platform.Control.TapeHeader { Notes = new() { [CombatTempoOverride.TapeNote] = "" } }).CombatTempo);
        Assert.Null(SessionOptions.TempoOf("9"));
        Assert.Equal(0.7f, SessionOptions.TempoOf("0.7"));
    }
}
