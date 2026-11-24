// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.RegularExpressions;

namespace Vitomir.Formats.Tests;

/// <summary>
/// The mode table, checked against the game's own copy of it. <see cref="NmsMotionSet.BodyModes"/>
/// is the game's own table; the scripts compile against <c>Codex.java</c>,
/// which declares the same table as MOTION_* constants. Two independent sources, and a script
/// asking for MOTION_TALK has to land on the same animation the motion set calls <c>talk</c>.
/// </summary>
public partial class MotionModeTableTests
{
    [GeneratedRegex(@"MOTION_([A-Z0-9_]+)\s*=\s*(\d+)\s*;")]
    private static partial Regex Constant();

    static Dictionary<int, string> GameConstants()
    {
        GameData.RequireInstall();
        var path = Path.Combine(GameData.Dir!, "Codex", "System", "Codex.java");
        Assert.SkipUnless(File.Exists(path), "this install has no Codex script sources");
        var table = new Dictionary<int, string>();
        foreach (Match m in Constant().Matches(File.ReadAllText(path)))
            table[int.Parse(m.Groups[2].Value)] = m.Groups[1].Value.ToLowerInvariant();
        return table;
    }

    /// <summary>
    /// Five constants are spelled differently from the keyword a motion set writes — the Java name
    /// reads more naturally, and one of the two fixes the file format's typo. The number is what
    /// matters, and in all five it agrees.
    /// </summary>
    static readonly Dictionary<int, (string Java, string File)> RenamedInJava = new()
    {
        [115] = ("damagemedium", "damagemed"),
        [124] = ("deathsunlight", "deahtsun"),      // the Java spells the file format's typo correctly
        [146] = ("spellhands", "handsspell"),
        [147] = ("spellthrow", "throwspell"),
        [148] = ("spellspit", "spitspell"),
    };

    [Fact]
    public void Every_mode_number_the_scripts_use_is_a_mode_of_the_original_table()
    {
        var game = GameConstants();
        Assert.Equal(257, game.Count);                       // MOTION_INVALID plus 256 modes
        Assert.Equal("invalid", game[0]);
        var wrong = new List<string>();
        foreach (var (index, name) in game.OrderBy(k => k.Key))
        {
            if (index == 0) continue;
            Assert.InRange(index, 1, NmsMotionSet.BodyModes.Length - 1);
            string ours = NmsMotionSet.BodyModes[index];
            if (RenamedInJava.TryGetValue(index, out var renamed))
            {
                // A known pair: the Java's name and the file format's keyword, same number.
                Assert.Equal(renamed.Java, name);
                Assert.Equal(renamed.File, ours);
                continue;
            }
            if (!string.Equals(ours, name, StringComparison.OrdinalIgnoreCase))
                wrong.Add($"{index}: Codex.java says {name}, our table says {ours}");
        }
        Assert.Empty(wrong);
    }

    [Fact]
    public void The_numbers_the_tests_and_the_engine_use_are_the_game_s_own()
    {
        var game = GameConstants();
        Assert.Equal("stand", game[33]);
        Assert.Equal("walk", game[1]);
        Assert.Equal("run", game[17]);
        Assert.Equal("talk", game[117]);
        Assert.Equal(NmsMotionSet.Idle, NmsMotionSet.BodyModes[33]);
    }

    [Fact]
    public void Our_table_has_one_mode_the_scripts_cannot_ask_for()
    {
        // The game's table runs to 257; Codex.java stops at 256. Nothing can play `hidden` from a
        // script — it is the engine's own, for an actor that has been taken off the screen.
        var game = GameConstants();
        Assert.Equal(256, game.Keys.Max());
        Assert.Equal(258, NmsMotionSet.BodyModes.Length);
        Assert.Equal("hidden", NmsMotionSet.BodyModes[257]);
        Assert.Equal("staked", NmsMotionSet.BodyModes[256]);
    }
}
