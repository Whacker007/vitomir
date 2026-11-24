// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Features;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Rules;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The rules file** (ours): the game's rule numbers as keys of a <c>rules.cfg</c>, 1.1's numbers the defaults. These tests read and
/// build rules without touching <see cref="GameRules.Current"/>; the ones that play a game by changed rules are
/// <see cref="GameRulesInForceTests"/>, which run alone.
/// </summary>
public class GameRulesTests
{
    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Vitomir.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("the source tree");
    }

    /// <summary>The Classic module's shipped copy, in the source tree.</summary>
    static string Shipped => Path.Combine(Root(), "src", "Vitomir.Modules.Classic", "Data", RulesFile.FileName);

    /// <summary>
    /// **The shipped <c>rules.cfg</c> is the code's**: written from the defaults, it is the checked-in file to the byte. Run with
    /// <c>VITOMIR_WRITE_RULES=1</c> to write it again after a key changes.
    /// </summary>
    [Fact]
    public void The_shipped_rules_file_is_written_from_the_code()
    {
        string made = RulesFile.Write();
        if (Environment.GetEnvironmentVariable("VITOMIR_WRITE_RULES") == "1") File.WriteAllText(Shipped, made);
        Assert.True(File.Exists(Shipped), $"{Shipped} is missing: run this test with VITOMIR_WRITE_RULES=1");
        Assert.True(made == File.ReadAllText(Shipped).Replace("\r\n", "\n"),
            "the shipped rules.cfg has drifted from the code: run this test with VITOMIR_WRITE_RULES=1 and check the file in");
    }

    /// <summary>
    /// **The defaults are today's constants, key for key**: each key's value as the file writes it (a float to the bit) is the number
    /// the engine used before the file existed, pinned here; and the shipped file, read through the layers, gives the same.
    /// </summary>
    [Fact]
    public void The_defaults_are_todays_constants_key_for_key_and_the_shipped_file_reads_back_to_them()
    {
        var defaults = GameRules.Defaults();
        var missing = RuleKey.All.Where(k => !Expected.ContainsKey(k.Id)).Select(k => k.Id).ToList();
        Assert.Empty(missing);
        Assert.DoesNotContain(Expected.Keys, id => RuleKey.Find(id) is null);
        var wrong = RuleKey.All.Where(k => k.Format(defaults) != Expected[k.Id]).Select(k => $"{k.Id} = {k.Format(defaults)}, not {Expected[k.Id]}").ToList();
        Assert.Empty(wrong);

        var read = RulesFile.Resolve([RulesLayer.Of(RulesFile.Classic, File.ReadAllText(Shipped), Shipped)]);
        Assert.Empty(read.Said);
        Assert.True(read.Rules.IsDefault);
        Assert.Equal(defaults.Hash(), read.Rules.Hash());
        Assert.All(RuleKey.All, k => Assert.Equal(RulesFile.Classic, read.Sources[k]));
        // And as the engine's own types, to the bit: a few that the arithmetic leans on.
        Assert.Equal(BitConverter.SingleToInt32Bits(1.4f), BitConverter.SingleToInt32Bits(read.Rules.Advancement.DisciplineLevelMultipliers[1]));
        Assert.Equal(BitConverter.DoubleToInt64Bits(Shops.BuySlope), BitConverter.DoubleToInt64Bits(read.Rules.Items.BuyPriceFactor[1]));
        Assert.Equal(Combat.CriticalMargin, read.Rules.Combat.CriticalMargin);
        Assert.Equal(Feeding.Drain, read.Rules.Feeding.FeedDrainPerTick);
        Assert.Equal(Experience.RankBonus[4, 1].Health, read.Rules.Ranks.RankBonusGhoulHealth[3]);
    }

    /// <summary>Single precision kept: 500 × 1.4 is 700 (1.1's), not 699, from the file's 1.4 as from the constant.</summary>
    [Fact]
    public void A_float_read_from_the_file_is_the_constants_float()
    {
        var key = RuleKey.Find("Advancement.DisciplineLevelMultipliers")!;
        var parsed = (float[])key.Parse("1, 1.4, 2, 4, 8", out var why)!;
        Assert.Null(why);
        Assert.Equal(700, (int)((float)500 * parsed[1] * 1f));
        Assert.Equal(Advancement.LevelMultipliers, parsed);
    }

    [Fact]
    public void Layers_go_in_order_and_the_later_wins()
    {
        var resolved = RulesFile.Resolve(
        [
            RulesLayer.Of(RulesFile.Classic, "[Combat]\nCriticalMargin = 60\n"),
            RulesLayer.Of("tactics", "[Combat]\nCriticalMargin = 40\nHitMargin = 2\n; a comment\n# another\n"),
            RulesLayer.Of("harder", "[combat]\ncriticalmargin = 50   ; case does not matter\n"),
        ]);
        Assert.Equal(50, resolved.Rules.Combat.CriticalMargin);
        Assert.Equal(2, resolved.Rules.Combat.HitMargin);
        Assert.Equal("harder", resolved.Sources[RuleKey.Find("Combat.CriticalMargin")!]);
        Assert.Equal("tactics", resolved.Sources[RuleKey.Find("Combat.HitMargin")!]);
        Assert.Equal(RulesFile.Builtin, resolved.Sources[RuleKey.Find("Combat.BackstabScale")!]);
        Assert.False(resolved.Rules.IsDefault);
        Assert.Equal(["Combat.HitMargin", "Combat.CriticalMargin"], resolved.Rules.Changed().Select(k => k.Id));
    }

    [Fact]
    public void A_bad_value_keeps_the_layer_below_and_is_said()
    {
        var resolved = RulesFile.Resolve(
        [
            RulesLayer.Of("one", "[Feeding]\nFeedDrainPerTick = 4\nFeedTickMs = 800, 400\n[Ranks]\nRankThresholds = 0, 1000, 2000, 3000, 4000\n"),
            RulesLayer.Of("two", "[Feeding]\nFeedDrainPerTick = lots\nFeedTickMs = 700\n[Ranks]\nRankThresholds = 0, 10, 20, 30, 40, 50\n"),
        ]);
        Assert.Equal(4f, resolved.Rules.Feeding.FeedDrainPerTick);
        Assert.Equal([800, 400], resolved.Rules.Feeding.FeedTickMs);
        Assert.Equal([0, 1000, 2000, 3000, 4000], resolved.Rules.Ranks.RankThresholds);
        Assert.Equal("one", resolved.Sources[RuleKey.Find("Feeding.FeedDrainPerTick")!]);
        Assert.Contains(resolved.Said, l => l.Contains("FeedDrainPerTick = lots") && l.Contains("not a number") && l.Contains("4 kept"));
        Assert.Contains(resolved.Said, l => l.Contains("FeedTickMs = 700") && l.Contains("2 values are wanted"));
        Assert.Contains(resolved.Said, l => l.Contains("RankThresholds") && l.Contains("5 values are wanted"));
    }

    [Fact]
    public void An_unknown_key_is_warned_once_and_passed_over_and_a_fixed_key_keeps_its_value()
    {
        string text = "[Combat]\nCriticalMarginn = 50\n[Nowhere]\nSpeed = 3\nLoose = 1\n[Party]\nMaxParty = 6\n[Ai]\nMaxPets = 4\n";
        var first = RulesFile.Resolve([RulesLayer.Of("typo-module-a", text)]);
        Assert.True(first.Rules.IsDefault);
        Assert.Contains(first.Said, l => l.Contains("[Combat] CriticalMarginn is no rule of the game: passed over"));
        Assert.Contains(first.Said, l => l.Contains("[Nowhere] Speed is no rule"));
        Assert.Contains(first.Said, l => l.Contains("MaxParty = 6: fixed in this release") && l.Contains("four heads") && l.Contains("4 kept"));
        Assert.DoesNotContain(first.Said, l => l.Contains("MaxPets"));      // its own value is no change
        var again = RulesFile.Resolve([RulesLayer.Of("typo-module-a", text)]);
        Assert.Empty(again.Said);                                            // once a process
    }

    [Fact]
    public void A_value_out_of_its_range_is_refused()
    {
        var resolved = RulesFile.Resolve([RulesLayer.Of("ranges", "[Combat]\nVoxAttackChance = 1.5\n[Time]\nTriggerEveryFrames = 12\n[Stats]\nActorTypeDefault = 4\n")]);
        Assert.True(resolved.Rules.IsDefault);
        Assert.Contains(resolved.Said, l => l.Contains("VoxAttackChance") && l.Contains("above its most"));
        Assert.Contains(resolved.Said, l => l.Contains("TriggerEveryFrames") && l.Contains("power of two"));
        Assert.Contains(resolved.Said, l => l.Contains("ActorTypeDefault") && l.Contains("above its most"));
    }

    [Fact]
    public void Changed_rules_make_the_game_modded_by_name()
    {
        var registry = GameFeatures.Registry;
        var changed = RulesFile.Resolve([RulesLayer.Of("m", "[Ranks]\nRankBonusVampireHealth = 25, 30, 50, 100\n")]).Rules;
        Assert.Equal(FeatureRegistry.Faithful, registry.Profile(new Fixes(), new Qol(), GameRules.Defaults()));
        Assert.DoesNotContain(registry.RulesOn(new Fixes(), new Qol(), GameRules.Defaults()), n => n.StartsWith(FeatureRegistry.RulesFilePrefix));
        Assert.Equal(FeatureRegistry.Modded, registry.Profile(new Fixes(), new Qol(), changed));
        string name = FeatureRegistry.RulesFilePrefix + changed.ShortHash();
        Assert.Contains(name, registry.RulesOn(new Fixes(), new Qol(), changed));
        Assert.StartsWith($"modded rules ({name})", registry.Line(new Fixes(), new Qol(), changed));
        // A save that names them is read as modded, as a rules feature this build does not know is.
        Assert.Equal(FeatureRegistry.Modded, registry.ProfileOf(FeatureRegistry.Modded, [name]));
    }

    [Fact]
    public void The_tape_line_carries_the_hash_and_the_changed_keys_and_an_older_tape_is_the_defaults()
    {
        var changed = RulesFile.Resolve([RulesLayer.Of("m", "[Feeding]\nFeedTickMs = 800, 400\n[Items]\nBuyPriceFactor = 1.5, -0.6, 0.01\n")]).Rules;
        var fields = RulesFile.TapeFields(changed);
        Assert.Equal(["hash=" + changed.ShortHash(), "Feeding.FeedTickMs=800,%20400", "Items.BuyPriceFactor=1.5,%20-0.6,%200.01"], fields);
        var said = new List<string>();
        var back = RulesFile.FromTape(fields, said.Add);
        Assert.Empty(said);
        Assert.Equal(changed.Hash(), back.Hash());
        Assert.True(RulesFile.FromTape(null).IsDefault);
        Assert.Equal(["hash=" + GameRules.Defaults().ShortHash()], RulesFile.TapeFields(GameRules.Defaults()));
        // A tape whose keys do not make its hash says so (and plays on).
        RulesFile.FromTape(["hash=0000000000000000", "Feeding.FeedTickMs=800,%20400"], said.Add);
        Assert.Contains(said, l => l.Contains("is not what its keys make"));
    }

    [Fact]
    public void A_player_modules_rules_file_is_world_data_so_the_module_is_a_rules_module()
    {
        var c = Vitomir.Platform.PlayerModules.PackFiles.Classify("rules.cfg");
        Assert.Equal(Vitomir.Platform.PlayerModules.PackFileClass.Data, c.Class);
        Assert.True(Vitomir.Platform.PlayerModules.PackKindWords.IsRules(c.Kind));
        // Anywhere else a .cfg is a document still.
        Assert.Equal(Vitomir.Platform.PlayerModules.PackFileClass.Document, Vitomir.Platform.PlayerModules.PackFiles.Classify("misc/rules.cfg").Class);
        Assert.Equal(Vitomir.Platform.PlayerModules.PackFileClass.Document, Vitomir.Platform.PlayerModules.PackFiles.Classify("settings.cfg").Class);
    }

    [Fact]
    public void The_layers_are_the_classic_modules_file_then_each_modules_in_order()
    {
        string root = Path.Combine(Path.GetTempPath(), "vitomir-rules-" + Guid.NewGuid().ToString("N"));
        try
        {
            string classic = Path.Combine(root, "Classic"), a = Path.Combine(root, "a", "Data"), b = Path.Combine(root, "b", "Data"), none = Path.Combine(root, "c", "Data");
            foreach (var d in new[] { Path.Combine(classic, "Data"), a, b, none }) Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(classic, "Data", RulesFile.FileName), "[Combat]\nCriticalMargin = 60\n");
            File.WriteAllText(Path.Combine(a, RulesFile.FileName), "[Combat]\nCriticalMargin = 40\n");
            File.WriteAllText(Path.Combine(b, RulesFile.FileName), "[Combat]\nCriticalMargin = 45\n");
            var layers = RulesFile.Layers(classic, [("a", a), ("c", none), ("b", b)]).ToList();
            Assert.Equal([RulesFile.Classic, "a", "b"], layers.Select(l => l.Source));
            Assert.Equal(45, RulesFile.Resolve(layers).Rules.Combat.CriticalMargin);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void The_report_names_each_keys_layer_and_marks_the_changed()
    {
        var resolved = RulesFile.Resolve([RulesLayer.Of("tactics", "[Combat]\nCriticalMargin = 40\n")]);
        var lines = RulesFile.Report(resolved).ToList();
        Assert.StartsWith("rules: 1 key differs from 1.1's", lines[0]);
        Assert.Contains("[Combat]", lines);
        Assert.Contains("* CriticalMargin = 40  (tactics)", lines);
        Assert.Contains("  HitMargin = 0  (1.1)", lines);
        Assert.Equal(RuleKey.All.Count + RuleKey.All.Select(k => k.Section).Distinct().Count() + 1, lines.Count);
    }

    [Fact]
    public void The_join_identity_carries_the_rules_and_reads_them_back()
    {
        var changed = RulesFile.Resolve([RulesLayer.Of("m", "[Combat]\nCriticalMargin = 40\n")]).Rules;
        string rules = Net.NetIdentity.RulesOf(changed);
        Assert.Equal(changed.ShortHash() + ":Combat.CriticalMargin", rules);
        Assert.Equal("", Net.NetIdentity.RulesOf(GameRules.Defaults()));
        var mine = new Net.NetIdentity("aaaa000011112222", "Vitomir", "0123456789abcdef", 4102, "GOG") { Modules = ["m 1.0.0 0011223344556677"], Rules = rules };
        var w = new Net.NetWriter(Net.Msg.Hello);
        mine.Write(w);
        var back = Net.NetIdentity.Read(new Net.NetReader(w.ToArray()));
        Assert.Equal(rules, back.Rules);
        Assert.Equal(mine.Modules, back.Modules);
        Assert.Equal(mine.Data, back.Data);
        Assert.Null(back.Differs(mine));
        var plain = mine with { Rules = "" };
        string? line = plain.Differs(mine);
        Assert.NotNull(line);
        Assert.Contains("other rules", line);
        Assert.Contains("Combat.CriticalMargin", line);
        Assert.Contains("1.1's", line);
    }

    /// <summary>Every key's default as the file writes it: today's constants (a float to the bit), pinned.</summary>
    static readonly Dictionary<string, string> Expected = new(StringComparer.Ordinal)
    {
        ["Stats.StatUnsetDefault"] = "20",
        ["Stats.HealthDefault"] = "100, 100",
        ["Stats.ActorTypeDefault"] = "0",
        ["Stats.TeamDefault"] = "8",
        ["Stats.StatCeiling"] = "10000",
        ["Stats.StatFloor"] = "0",
        ["Stats.StatNoLimitMarker"] = "-1",
        ["Stats.SheetResetMax"] = "100, 1000",
        ["Stats.SheetResetValues"] = "20, 20, 20, 20, 20, 20, 100, 20, 20, 13, 20, 20, 20, 20, 20, 1, 100, 100, 100",
        ["Stats.RaisableStats"] = "0, 1, 2, 3, 4, 5, 11, 12, 13",
        ["Stats.RaisableStatsHuman"] = "10",
        ["Stats.ExperienceCap"] = "100000000",
        ["Ranks.RankThresholds"] = "0, 5000, 10000, 50000, 200000",
        ["Ranks.RankCount"] = "5",
        ["Ranks.RankBonusVampirePool"] = "20, 20, 30, 40",
        ["Ranks.RankBonusVampireHealth"] = "20, 30, 50, 100",
        ["Ranks.RankBonusGhoulPool"] = "10, 10, 15, 20",
        ["Ranks.RankBonusGhoulHealth"] = "25, 40, 75, 150",
        ["Ranks.RankBonusHumanPool"] = "20, 40, 60, 80",
        ["Ranks.RankBonusHumanHealth"] = "20, 40, 60, 80",
        ["Ranks.RankBonusMonsterPool"] = "0, 0, 0, 0",
        ["Ranks.RankBonusMonsterHealth"] = "0, 0, 0, 0",
        ["Ranks.MultiRankAwardsOnlyLast"] = "true",
        ["Ranks.EmbraceMaxHealthGain"] = "0",
        ["Healing.BornEffectsVampire"] = "ef_decreasefrenzy",
        ["Healing.BornEffectsGhoul"] = "ef_decreasefrenzy",
        ["Healing.BornEffectsHuman"] = "ef_increasemanaot, ef_increasebloodot",
        ["Healing.HealCalmsFrenzy"] = "1",
        ["Healing.HealFloor"] = "1",
        ["Healing.HumanBleedsOutAtZeroBlood"] = "true",
        ["Advancement.StatRaiseCostBands"] = "50, 60, 70, 80, 90",
        ["Advancement.StatRaiseCostRates"] = "4, 5, 6, 8, 10, 15",
        ["Advancement.StatRaiseStep"] = "1",
        ["Advancement.DisciplineLevelMultipliers"] = "1, 1.4, 2, 4, 8",
        ["Advancement.DisciplineOutOfClanMultiplier"] = "1.25",
        ["Advancement.DisciplineGroupFirstPrice"] = "true",
        ["Advancement.DisciplineMaxLevel"] = "5",
        ["Advancement.PartyAwardSplitNetwork"] = "true",
        ["Combat.HitMargin"] = "0",
        ["Combat.CriticalMargin"] = "60",
        ["Combat.CriticalMultiplierStep"] = "2",
        ["Combat.SilverDoubles"] = "true",
        ["Combat.HelplessDefencePenalty"] = "30",
        ["Combat.BackstabScale"] = "20",
        ["Combat.BackstabMinDot"] = "0.2",
        ["Combat.BackstabPlayersOnly"] = "true",
        ["Combat.DamageDieSpan"] = "9",
        ["Combat.DamageDieFloor"] = "1",
        ["Combat.DamagePointsPerDie"] = "10, 0.1",
        ["Combat.DamageIncludesStrength"] = "true",
        ["Combat.SoakNormal"] = "1, 1",
        ["Combat.SoakLethal"] = "1, 0, 1",
        ["Combat.SoakAggravated"] = "0, 1",
        ["Combat.ElementalSoakShare"] = "0.5",
        ["Combat.PoisonDiseaseIgnoreFortitude"] = "true",
        ["Combat.DeathThreshold"] = "1",
        ["Combat.SunAndFaithImmuneTypes"] = "2, 3, 1",
        ["Combat.PartyShotShare"] = "0.75",
        ["Combat.CappadocianFaithMultiplier"] = "1.5",
        ["Combat.ContainerMeleeBonus"] = "1",
        ["Combat.VoxAttackChance"] = "0.5",
        ["Ranged.ScatterFormula"] = "100, 0.01, 30, 15",
        ["Ranged.ProjectileAccuracyRoll"] = "1, 1",
        ["Ranged.ProjectileUnownedRoll"] = "50",
        ["Ranged.ProjectileDice"] = "0.1, 10, 9, 1",
        ["Ranged.BulletLethalMargin"] = "20",
        ["Ranged.PartyFriendlyFireOff"] = "true",
        ["Ranged.ThrownSpendsOne"] = "1",
        ["Wounds.FlinchMinDamage"] = "1",
        ["Wounds.FlinchChancePerPoint"] = "8",
        ["Wounds.FlinchChanceClamp"] = "5, 95, 100",
        ["Wounds.FlinchQuietMs"] = "3500",
        ["Wounds.WoundLadderThresholds"] = "0.69, 0.39, 0.19, 0.9, 0",
        ["Wounds.WoundDexterityLevels"] = "1, 2, 3, 3",
        ["Wounds.WoundStrengthLevels"] = "-1, 2, 3, 3",
        ["Wounds.WoundMoveSpeedLevels"] = "-1, 2, 3, 4",
        ["Wounds.WoundMoveSpeedMs"] = "0, 8000, 20000, 50000",
        ["Wounds.LimpBelowMoveSpeed"] = "0.8",
        ["Wounds.ShieldBlockOnFullSoak"] = "true",
        ["Stake.StakeRoll"] = "100",
        ["Stake.StakeDurationMs"] = "20000",
        ["Stake.UnstakeReach"] = "60",
        ["Stake.UnstakeRetryMs"] = "250",
        ["Stake.BeheadChance"] = "100, 0.1",
        ["Stake.BeheadEffectMs"] = "100000",
        ["Stake.BeheadBleedChoice"] = "0.5",
        ["Stake.BeheadBleedValues"] = "10, 0.2",
        ["Stake.SpecialThrowDeadThrowerDamage"] = "10",
        ["Stake.GibKillDamage"] = "80",
        ["Stake.GibPieces"] = "8",
        ["Stake.GibSprayThresholds"] = "5, 19",
        ["Stake.GoreLaunchSpeed"] = "20",
        ["Stake.WoundShowMsPerPoint"] = "80, 0.25, 0.33, 300, 8000",
        ["Casting.CastCostPaidAlways"] = "true",
        ["Casting.CastCostScale"] = "true",
        ["Casting.CastApproachRetryMs"] = "250",
        ["Casting.TerrifyEventRange"] = "1000",
        ["Casting.ScrollCastCount"] = "100",
        ["Casting.QuickSlotCount"] = "6",
        ["Feeding.FeedReach"] = "60",
        ["Feeding.FeedApproachRetryMs"] = "250",
        ["Feeding.FeedWalkWithin"] = "300",
        ["Feeding.FeedChaseBarkChance"] = "0.2",
        ["Feeding.FeedVictimGap"] = "60, 75",
        ["Feeding.FeedResistPerLevel"] = "20",
        ["Feeding.FeedResistClamp"] = "5, 95",
        ["Feeding.FeedPedestrianTeam"] = "10",
        ["Feeding.FeedDrainPerTick"] = "3",
        ["Feeding.FeedGainPerBloodRating"] = "3",
        ["Feeding.FeedTickMs"] = "1000, 500",
        ["Feeding.FeedFirstDrainMs"] = "1000, 1000",
        ["Feeding.FeedAiThinkPad"] = "4000, 9000",
        ["Feeding.FeedVictimCheckMs"] = "500",
        ["Feeding.FeedDonorFloor"] = "10",
        ["Feeding.FeedReleaseKillBase"] = "25",
        ["Feeding.FeedSuckNoiseChance"] = "0.1, 0.1",
        ["Feeding.NetClanBloodRating"] = "0.6, 0.75",
        ["Feeding.GiovanniFastDrinker"] = "true",
        ["Frenzy.FrenzyThreshold"] = "100",
        ["Frenzy.FrenzyCalm"] = "25",
        ["Frenzy.FrenzyNearBark"] = "80",
        ["Frenzy.FrenzyEffects"] = "ef_frz_strength, ef_frz_dexterity",
        ["Frenzy.GangrelFrenzyAppearanceChance"] = "0.1",
        ["Frenzy.FrenzyFromDamage"] = "0.01, -1.5, 2, 1.5, 0.5, 1.25, -0.8, 20",
        ["Frenzy.ProvokeFrenzyValue"] = "100, -99",
        ["Frenzy.HumanityPerKill"] = "5",
        ["Frenzy.HumanityLossActorTypes"] = "0",
        ["Frenzy.LowHumanityBark"] = "20",
        ["Frenzy.HumanityFloorOnRevive"] = "20",
        ["Frenzy.HumanityZeroEndsGame"] = "true",
        ["Frenzy.LowBloodBarkFraction"] = "0.2",
        ["Frenzy.FrenzyCapActorTypes"] = "true",
        ["Death.QuickDeathDamage"] = "19",
        ["Death.CorpseFadeMs"] = "30000",
        ["Death.PartyCorpseFadeMs"] = "5000",
        ["Death.CorpseExpireMs"] = "35000",
        ["Death.CorpseFadeDurationMs"] = "4800, 1000",
        ["Death.SkeletonLifetimeMs"] = "5000, 1000, 0.33",
        ["Death.ReviveDefaults"] = "0",
        ["Death.PartyLeaverReviveFraction"] = "0.1",
        ["Death.PedestrianGoesHome"] = "true",
        ["Death.TorporIsDeathForVampires"] = "true",
        ["Death.DrainKillHitKind"] = "5",
        ["Movement.RunBeyond"] = "300",
        ["Movement.RunAnimationBeyond"] = "200",
        ["Movement.DefaultWalkRunSpeed"] = "150, 250",
        ["Movement.RouteMaxCost"] = "5000",
        ["Movement.RouteStepPenalty"] = "10",
        ["Movement.RouteMaxSteps"] = "255",
        ["Movement.DetourTries"] = "16",
        ["Movement.WaitBeforeRetry"] = "0.3, 0.5",
        ["Movement.AiWalkGiveUpWaits"] = "10",
        ["Movement.ArrivedDistance"] = "0.1",
        ["Movement.AttackApproachRetryMs"] = "500",
        ["Movement.UseReach"] = "75",
        ["Movement.UseRetryMs"] = "250",
        ["Movement.TakeRetryMs"] = "150",
        ["Movement.TakePutReach"] = "50",
        ["Movement.ApproachBodyGap"] = "45",
        ["Movement.MeleeMidReach"] = "0.5",
        ["Movement.WeaponDefaults"] = "1, 50, 20, 200, 0.5",
        ["Sight.StealthPenaltyFeedOrSwing"] = "5",
        ["Sight.StealthPenaltyCasting"] = "3",
        ["Sight.StealthPenaltyWalking"] = "1",
        ["Sight.SightLineHeights"] = "0.7, 0.5",
        ["Sight.SightAlwaysWithin"] = "0.1",
        ["Sight.PerceptionStealthDefaults"] = "0, 0",
        ["Ai.AiRandomScale"] = "32767",
        ["Ai.ThreatRange"] = "512",
        ["Ai.DangerBands"] = "3, 2, 1, 0.9, 0.8, 0.5",
        ["Ai.StrengthGeneration"] = "13, 14, 50",
        ["Ai.StrengthPerDisciplineLevel"] = "10",
        ["Ai.StrengthHealthSteps"] = "0.95, 0.9, 0.75, 0.5, 0.25",
        ["Ai.StrengthHealthPenalty"] = "25, 50, 100, 150, 200",
        ["Ai.StrengthReservePenalty"] = "20, 50, 75, 100, 150",
        ["Ai.LadderDangerGates"] = "2, 2000, 3, 8000, 4, 5000, 2",
        ["Ai.AiOpportunisticFeedChance"] = "0.15, 0.05",
        ["Ai.AiOpportunisticFeedChanceCompanion"] = "0.3, 0.1",
        ["Ai.StanceCastScaleBuff"] = "1.2, 1, 0.7",
        ["Ai.StanceCastScaleDamage"] = "1.25, 1, 0.5",
        ["Ai.CompanionKeepBlood"] = "30",
        ["Ai.QuickSlotWeight"] = "5",
        ["Ai.CastShoutRange"] = "500",
        ["Ai.HurtShare"] = "0.3",
        ["Ai.HurtShareStance"] = "1.2, 1, 0.75",
        ["Ai.FormationAngle"] = "0, 45, -45, 0, 45, -45",
        ["Ai.FormationRadius"] = "130, 130, 130, 80, 80, 80",
        ["Ai.FollowReach"] = "180",
        ["Ai.StanceLeash"] = "2.5, 1, 0.8",
        ["Ai.StanceFightLeash"] = "2.5, 1, 0.4",
        ["Ai.PartyThinkMs"] = "800",
        ["Ai.HuntThinkMs"] = "300, 1000",
        ["Ai.EnemyThinkMs"] = "1000",
        ["Ai.AttackJitter"] = "0.2",
        ["Ai.TauntChance"] = "0.2",
        ["Ai.ManoeuvreArc"] = "60, 50",
        ["Ai.GiveUpOnTargetMs"] = "6000",
        ["Ai.ApproachForMs"] = "10000",
        ["Ai.ApproachThinkMs"] = "250, 500, 10",
        ["Ai.RetryCombatMs"] = "100",
        ["Ai.OutOfAmmoMessageMs"] = "5000",
        ["Ai.AttackShoutRange"] = "500",
        ["Ai.FearShoutRange"] = "1500",
        ["Ai.FedOnShoutRange"] = "2000",
        ["Ai.AssaultShoutRange"] = "2000",
        ["Ai.FrenzyTargetReach"] = "500",
        ["Ai.MesmerizeShakenOffChance"] = "0.9",
        ["Ai.MajestyThinkMs"] = "250, 200",
        ["Ai.MaxPets"] = "4",
        ["Ai.SummonedLingerMs"] = "5000, 1000",
        ["Ai.RallyRange"] = "1250, 0.66, 750",
        ["Ai.MakeWayRange"] = "120, 80, 10, 500",
        ["Ai.Sidestep"] = "250, 500, 100, 40, 50, 50, 30, 0.5",
        ["Ai.Idle"] = "4000, 0.2, 30000, 1000",
        ["Ai.Wander"] = "5000, 1000, 1000, 15000, 1000, 50, 0.975",
        ["Ai.Search"] = "500, 1000, 40, 120000, 1",
        ["Ai.TargetFlapLimit"] = "10, 1000",
        ["Items.BuyPriceFactor"] = "1.3, -0.6, 0.01",
        ["Items.SellPriceFactor"] = "0.7, 0.6, 0.01",
        ["Items.UnidentifiedSellShare"] = "4",
        ["Items.UnidentifiedEffectsWorthless"] = "true",
        ["Items.UniqueCostSkipsBuyRate"] = "true",
        ["Items.PriceFloor"] = "1",
        ["Items.EffectCostFormula"] = "true",
        ["Items.ContainerDropLifetimeMs"] = "10000",
        ["Items.TreasureClassCount"] = "256",
        ["Items.TreasureChancePercent"] = "0.01",
        ["Items.RepackMaxCells"] = "3",
        ["Items.AmmoDefaultCapacity"] = "20",
        ["Items.NewNetCharacterCash"] = "200",
        ["Time.FrameStepClampMs"] = "1, 1000",
        ["Time.TriggerEveryFrames"] = "16",
        ["Time.AnimationRate"] = "30",
        ["Time.EffectIconBlink"] = "5000, 500",
        ["Party.MaxParty"] = "4",
        ["Party.MaxPlayers"] = "8",
        ["Party.KeptPlaces"] = "6, 11",
        ["Party.SaveSlots"] = "30",
        ["Party.SayRange"] = "5000",
        ["Party.ConversationEarshot"] = "800",
        ["Party.ChatMaxDice"] = "20",
        ["Party.ChatLineMax"] = "120",
        ["Party.TrackHumanityFlag"] = "512",
        ["Party.NetQuestListMax"] = "8",
        ["Party.NetShopLineMax"] = "128",
        ["Party.ForwardedOrderBusyMs"] = "250, 200, 200",
        ["Party.ClientSceneTiming"] = "1500, 25000",
        ["Party.DefaultPort"] = "4994",
        ["Party.LanTimings"] = "2000, 8000",
        ["Party.NetSnapshot"] = "100, 500",
    };
}

/// <summary>
/// **A game played by changed rules** (ours): these put rules in force (<see cref="GameRules.Current"/>, the process's) and put the
/// defaults back, so they run alone.
/// </summary>
[Collection(nameof(GameRulesInForceTests))]
public class GameRulesInForceTests
{
    static GameRules Changed(string text) => RulesFile.Resolve([RulesLayer.Of("test", text)]).Rules;

    /// <summary>A vampire party member of 100 health, its pool 100 and blood 40.</summary>
    static WorldThing Vampire()
    {
        var actor = new Actor { Type = ActorType.Vampire };
        actor.Stat[(int)ActorStat.BloodPool] = actor.SetBaseStat((int)ActorStat.BloodPool, 100);
        actor.Stat[(int)ActorStat.Blood] = actor.SetBaseStat((int)ActorStat.Blood, 40);
        actor.Health = actor.MaxHealth = 100;
        return new WorldThing { Index = 0, Guid = 1, Scene = new NsdThing(), Template = "christof", Type = ThingType.Player, Actor = actor };
    }

    [Fact]
    public void A_changed_rank_bonus_changes_what_a_rank_gives()
    {
        var was = GameRules.Current;
        try
        {
            var christof = Vampire();
            Experience.Award(christof, 5_000);
            Assert.Equal(100f + 20f, christof.Actor!.MaxHealth);                       // 1.1's: kindred rank 1, +20

            GameRules.Current = Changed("[Ranks]\nRankBonusVampireHealth = 35, 30, 50, 100\n"
                                      + "RankThresholds = 0, 1000, 10000, 50000, 200000\n");
            var wilhelm = Vampire();
            Experience.Award(wilhelm, 1_000);
            Assert.Equal(1, Ui.CharacterPane.Rank(wilhelm.Actor!.Experience));
            Assert.Equal(100f + 35f, wilhelm.Actor.MaxHealth);
        }
        finally { GameRules.Current = was; }
    }

    [Fact]
    public void Paying_every_rank_crossed_is_the_rules_files_choice()
    {
        var was = GameRules.Current;
        try
        {
            var once = Vampire();
            Experience.Award(once, 10_000);
            Assert.Equal(100f + 30f, once.Actor!.MaxHealth);                           // 1.1's quirk: only the second rank's
            GameRules.Current = Changed("[Ranks]\nMultiRankAwardsOnlyLast = false\n");
            var each = Vampire();
            Experience.Award(each, 10_000);
            Assert.Equal(100f + 20f + 30f, each.Actor!.MaxHealth);
        }
        finally { GameRules.Current = was; }
    }

    [Fact]
    public void A_changed_cost_rate_and_level_multiplier_change_what_advancement_costs()
    {
        var was = GameRules.Current;
        try
        {
            var who = new Actor { Type = ActorType.Vampire };
            who.BaseStat[(int)ActorStat.Strength] = 30f;
            Assert.Equal(120, Advancement.StatCost(who, (int)ActorStat.Strength));     // Christof's Strength 30 costs 120
            GameRules.Current = Changed("[Advancement]\nStatRaiseCostRates = 3, 5, 6, 8, 10, 15\n");
            Assert.Equal(90, Advancement.StatCost(who, (int)ActorStat.Strength));
        }
        finally { GameRules.Current = was; }
    }

    [Fact]
    public void A_changed_flinch_and_frenzy_threshold_change_the_rolls()
    {
        var was = GameRules.Current;
        try
        {
            Assert.Equal(95f, Flinch.Chance(100f, 0f));
            Assert.Equal(5f, Flinch.Chance(0f, 50f));
            Assert.Equal(16f - 4f, Flinch.Chance(2f, 4f));
            GameRules.Current = Changed("[Wounds]\nFlinchChancePerPoint = 10\nFlinchChanceClamp = 0, 50, 100\n");
            Assert.Equal(50f, Flinch.Chance(100f, 0f));
            Assert.Equal(20f - 4f, Flinch.Chance(2f, 4f));
        }
        finally { GameRules.Current = was; }
    }
}

[CollectionDefinition(nameof(GameRulesInForceTests), DisableParallelization = true)]
public sealed class GameRulesInForceCollection;
