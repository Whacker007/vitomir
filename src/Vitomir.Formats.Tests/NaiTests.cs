// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats.Tests;

/// <summary>
/// The <c>.nai</c> reader against 1.1's reading of the file, field by field: its
/// defaults and which field each key sets, which is the only thing
/// that ties a key to its field. A comparison with 1.1 checked every field of both against
/// <see cref="NaiFile"/>; these pin that, so a change to one key's field or default cannot hide
/// behind a file that happens not to set it. No game files needed.
/// </summary>
public class NaiTests
{
    /// <summary>
    /// **Every default is the engine's constructor** (a value for each
    /// field) — including `verylowhealth` above `lowhealth`, the wrong way round, and the item keys
    /// no file sets.
    /// </summary>
    [Fact]
    public void Every_default_is_the_engines_constructor()
    {
        var ai = NaiFile.Read([], "empty.nai");
        Assert.Equal(0, ai.AttackType);
        Assert.Equal(0u, ai.AttackFlags);
        Assert.Equal(2000, ai.AttackRate);
        Assert.Equal(0.5f, ai.AttackAlt);
        Assert.Equal(0, ai.MoveType);
        Assert.Equal(0u, ai.MoveFlags);
        Assert.Equal(2000, ai.MoveRate);
        Assert.Equal(100f, ai.MoveDist);
        Assert.Equal(2000, ai.ChaseRate);
        Assert.Equal(200f, ai.ChaseDist);
        Assert.Equal(0, ai.SearchStyle);
        Assert.Equal(1000f, ai.SearchDist);
        Assert.Equal(1000, ai.SearchRate);
        Assert.Equal(500f, ai.BreakDist);
        Assert.Equal(0, ai.BehaviorClass);
        Assert.Equal(-1, ai.InterestNum);
        Assert.Equal(0f, ai.WanderRadius);
        Assert.Equal(0, ai.WakeupDelay);
        Assert.Equal(0u, ai.MindFlags);
        Assert.Equal(2000, ai.DiscHealRate);
        Assert.Equal(20000, ai.DiscBuffRate);
        Assert.Equal(4000, ai.DiscDamageRate);
        Assert.Equal(12000, ai.DiscFeedRate);
        Assert.Equal(0.75f, ai.DiscBuffPercent);
        Assert.Equal(0.5f, ai.DiscDamagePercent);
        Assert.Equal(0.5f, ai.DiscFeedPercent);
        Assert.Equal(2000, ai.ItemHealRate);
        Assert.Equal(20000, ai.ItemBuffRate);
        Assert.Equal(4000, ai.ItemDamageRate);
        Assert.Equal(0.75f, ai.ItemBuffPercent);
        Assert.Equal(0.5f, ai.ItemDamagePercent);
        Assert.Equal(2000, ai.ItemBloodRate);
        Assert.Equal(0.75f, ai.ItemBloodPercent);
        Assert.Equal(0.3f, ai.VeryLowHealth);
        Assert.Equal(0.15f, ai.LowHealth);
        Assert.Equal(0.3f, ai.LowBlood);
        Assert.Equal(0.2f, ai.HighDamage);
        Assert.Equal(5000, ai.FeedDuration);
        Assert.Empty(ai.Disciplines);
    }

    /// <summary>
    /// **Each key lands in the engine's field** (the reader's 39 keys): a file that
    /// gives every key a value of its own reads back each value in the one property that key feeds,
    /// converted as that case converts it — whole numbers by `atoi`, the flags by `%x`, the rates as
    /// seconds × 1000, `interestNum` truncated but not scaled, the fractions clamped.
    /// </summary>
    [Fact]
    public void Each_key_lands_in_the_engines_field()
    {
        const string text = """
            attackType 11
            attackFlags 0x12
            attackRate 1.5
            attackAlt 1.25
            moveType 13
            moveFlags 0x14
            moveDist 15.5
            moveRate 2.5
            chaseDist 16.5
            chaseRate 3.5
            searchDist 17.5
            searchRate 4.5
            breakDist 18.5
            behaviorClass 3
            interestNum 9.75
            wanderRadius 19.5
            wakeupDelay 5.5
            searchStyle 1
            verylowhealth 0.41
            lowhealth 0.42
            lowblood 0.43
            highdamage 0.44
            discipline Mesmerize 2
            discHealRate 6.5
            discBuffRate 7.5
            discDamageRate 8.5
            discBuffPercent 0.45
            discDamagePercent 0.46
            itemHealRate 9.5
            itemBuffRate 10.5
            itemDamageRate 11.5
            itemBuffPercent 0.47
            itemDamagePercent 0.48
            itemBloodRate 12.5
            itemBloodPercent 0.49
            mindFlags 0x1f
            discFeedRate 13.5
            discFeedPercent 1.5
            feedDuration 14.5
            """;
        var ai = NaiFile.Read(System.Text.Encoding.Latin1.GetBytes(text), "every.nai");
        Assert.Empty(ai.UnknownKeys);
        Assert.Empty(ai.ValuelessKeys);

        Assert.Equal(11, ai.AttackType);
        Assert.Equal(0x12u, ai.AttackFlags);
        Assert.Equal(1500, ai.AttackRate);
        Assert.Equal(1.25f, ai.AttackAlt);              // case 3: not clamped
        Assert.Equal(13, ai.MoveType);
        Assert.Equal(0x14u, ai.MoveFlags);
        Assert.Equal(15.5f, ai.MoveDist);
        Assert.Equal(2500, ai.MoveRate);
        Assert.Equal(16.5f, ai.ChaseDist);
        Assert.Equal(3500, ai.ChaseRate);
        Assert.Equal(17.5f, ai.SearchDist);
        Assert.Equal(4500, ai.SearchRate);
        Assert.Equal(18.5f, ai.BreakDist);
        Assert.Equal(3, ai.BehaviorClass);
        Assert.Equal(9, ai.InterestNum);                // field 0xe: trunc(atof), no × 1000
        Assert.Equal(19.5f, ai.WanderRadius);
        Assert.Equal(5500, ai.WakeupDelay);             // case 0x10: × 1000
        Assert.Equal(1, ai.SearchStyle);
        Assert.Equal(0.41f, ai.VeryLowHealth);
        Assert.Equal(0.42f, ai.LowHealth);
        Assert.Equal(0.43f, ai.LowBlood);
        Assert.Equal(0.44f, ai.HighDamage);
        Assert.Equal([new NaiDiscipline("Mesmerize", 2)], ai.Disciplines);
        Assert.Equal(6500, ai.DiscHealRate);
        Assert.Equal(7500, ai.DiscBuffRate);
        Assert.Equal(8500, ai.DiscDamageRate);
        Assert.Equal(0.45f, ai.DiscBuffPercent);
        Assert.Equal(0.46f, ai.DiscDamagePercent);
        Assert.Equal(9500, ai.ItemHealRate);
        Assert.Equal(10500, ai.ItemBuffRate);
        Assert.Equal(11500, ai.ItemDamageRate);
        Assert.Equal(0.47f, ai.ItemBuffPercent);
        Assert.Equal(0.48f, ai.ItemDamagePercent);
        Assert.Equal(12500, ai.ItemBloodRate);
        Assert.Equal(0.49f, ai.ItemBloodPercent);
        Assert.Equal(0x1fu, ai.MindFlags);              // the file's mindFlags, the table's mindflags
        Assert.Equal(13500, ai.DiscFeedRate);
        Assert.Equal(1f, ai.DiscFeedPercent);           // case 0x25: clamped to 1
        Assert.Equal(14500, ai.FeedDuration);
    }
}
