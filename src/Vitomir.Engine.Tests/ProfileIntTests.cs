// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The `[gameOptions]` switches read the way 1.1 reads them**
/// (<c>GetPrivateProfileIntA</c> with the switch's bit as its default): the leading number, so a word
/// with no digits switches it **off**, where Vitomir had kept what it was. Only a hand-edited
/// <c>masquerade.ini</c> meets it: the game's own writer puts 0 or 1.
/// </summary>
public class ProfileIntTests
{
    [Fact]
    public void A_value_is_its_leading_number_and_one_with_no_digits_is_nought()
    {
        Assert.Equal(1, IniFile.ProfileInt("1", 0));
        Assert.Equal(0, IniFile.ProfileInt("0", 1));
        Assert.Equal(4, IniFile.ProfileInt(" 4", 0));
        Assert.Equal(1, IniFile.ProfileInt("1abc", 0));
        Assert.Equal(-2, IniFile.ProfileInt("-2", 0));
        Assert.Equal(0, IniFile.ProfileInt("yes", 1));
        Assert.Equal(7, IniFile.ProfileInt("", 7));
        Assert.Equal(7, IniFile.ProfileInt(null, 7));
    }

    [Fact]
    public void A_switch_written_as_a_word_is_off()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-pint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "masquerade.ini"),
                "[gameOptions]\nMousePitch=yes\nNoBlood=1abc\nCastBuff=on\nRepeatAttack=4\n");
            var o = GameOptions.Parse(dir, dir, Path.Combine(dir, "none.ini"));
            Assert.False(o.Has(GameOptions.MousePitch));
            Assert.True(o.Has(GameOptions.NoBlood));
            Assert.False(o.HasAi(GameOptions.CastBuff));
            Assert.True(o.Has(GameOptions.RepeatAttack));
        }
        finally { Directory.Delete(dir, true); }
    }
}
