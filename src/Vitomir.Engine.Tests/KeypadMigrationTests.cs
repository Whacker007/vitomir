// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The keypad pair moved to the quick keys in an old <c>vitomir.ini</c>** (ours). The key page writes
/// every binding, so a file saved before the quick keys holds 1.1's keypad zoom and never got the new
/// defaults — a real one, copied here as it was. <see cref="KeypadMigration"/> moves
/// the pair once, only when the zoom is exactly the old default, leaves whatever the player chose, and writes
/// the quick keys so it never runs again. These tests hold that on that file and on the cases around it.
/// </summary>
public class KeypadMigrationTests
{
    /// <summary>A <c>vitomir.ini</c> saved before the quick keys, as it was.</summary>
    const string OlderFile = """
[options]
ForceSubtitles = 1
NoBlood = 0
RepeatAttack = 1
AutoSlew = 0
MousePitch = 1
BackpackAutoLoad = 1
UseQuickDisc = 1
UseQuickItems = 1
CastHealing = 1
CastBuff = 1
CastDamage = 1
CastSpecial = 1
gamma = 1.3
effectsVolume = 0.44
musicVolume = 0.22
speakerType = 0
curDetailLevel = 4
alwaysRun = 1

[fixes]
movespeed = off
matchedgait = off
bodycollision = on
glowcolours = on
shapetorch = on
liverollover = on
soakedvoid = on
netprops = off

[qol]
quickequip = on
driving = on
camera = on
soakpie = on
keys = on
alttab = on
winkey = off
whisperecho = on
freecamera = off
filmvolume = on
dropcells = on

[keys]
act = Left Click
cast = Right Click
secondaryclick = Middle Click
partyclick = none
altclick = none
holdclick = none
secondary = Left Shift, Right Shift
party = Left Ctrl, Right Ctrl
alt = Left Alt, Right Alt
hold = Space
turnleft = Keypad 4
turnright = Keypad 6
tiltup = Keypad 8
tiltdown = Keypad 2
zoomout = -, Keypad -
zoomin = =, Keypad +
firstperson = Z, Mouse 4
swivel = X, Keypad 5, Mouse 5
dragswivel = Middle Click
skipline = Space
regroup = R
ungroup = Shift+R
group = G
attackstance = A
neutralstance = N
defencestance = E
pause = Pause
character = C
inventory = I
quests = Q
disciplines = D
map = M
closepanes = Backspace
statusbar = F11
nextcharacter = Tab
character1 = F6
character2 = F7
character3 = F8
character4 = F9
quick1 = F1
quick2 = F2
quick3 = F3
quick4 = F4
quick5 = F5
discipline1 = 1
discipline2 = 2
discipline3 = 3
discipline4 = 4
discipline5 = 5
discipline6 = 6
feed = F
heal = H
screenshot = F12
console = `
forward = W
back = S
stepleft = A
stepright = D
run = Left Shift
""";

    static string Temp(string text)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-keypad-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Diag.FileName);
        File.WriteAllText(path, text);
        return path;
    }

    // The bindings the program would install from this file alone (no ini beside the executable).
    static KeyBindings Read(string path) => KeyBindings.Parse(installedDir: Path.GetDirectoryName(path) + "-none", userPath: path);

    static KeyChord K(int key) => new(key);

    [Fact]
    public void An_older_file_gets_the_keypad_pair_on_the_quick_keys_and_the_rest_of_it_is_untouched()
    {
        var path = Temp(OlderFile);
        try
        {
            // Before: the keypad zooms, as 1.1's does, and the quick keys have nothing.
            var before = Read(path);
            Assert.Equal(new[] { K(KeyNames.Minus), K(KeyNames.KeypadMinus) }, before[KeyCommand.ZoomOut]);
            Assert.Contains(K(KeyNames.KeypadPlus), before[KeyCommand.ZoomIn]);

            Assert.NotNull(KeypadMigration.Run(path));
            var ini = IniFile.Read(path);
            Assert.Equal("-", ini["keys.zoomout"]);
            Assert.Equal("=", ini["keys.zoomin"]);
            Assert.Equal("Keypad +", ini["keys.quicksave"]);
            Assert.Equal("Keypad -", ini["keys.quickload"]);
            var after = Read(path);
            Assert.Equal(new[] { K(KeyNames.Minus) }, after[KeyCommand.ZoomOut]);
            Assert.Equal(new[] { K(KeyNames.EqualsKey) }, after[KeyCommand.ZoomIn]);
            Assert.Equal(new[] { K(KeyNames.KeypadPlus) }, after[KeyCommand.QuickSave]);
            Assert.Equal(new[] { K(KeyNames.KeypadMinus) }, after[KeyCommand.QuickLoad]);
            // Every other command as he had it, and every other line of the file as it was.
            foreach (var info in KeyBindings.All.Where(i => i.Command is not (KeyCommand.ZoomOut or KeyCommand.ZoomIn or KeyCommand.QuickSave or KeyCommand.QuickLoad)))
                Assert.Equal(before[info.Command], after[info.Command]);
            var was = OlderFile.Replace("\r\n", "\n").Split('\n');
            var now = File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            Assert.Equal(was.Where(l => !l.StartsWith("zoom", StringComparison.Ordinal)),
                         now.Where(l => !l.StartsWith("zoom", StringComparison.Ordinal) && !l.StartsWith("quicksave", StringComparison.Ordinal)
                                   && !l.StartsWith("quickload", StringComparison.Ordinal)));

            // Once: the file has its quick keys now, and a second start changes nothing.
            var written = File.ReadAllText(path);
            Assert.Null(KeypadMigration.Run(path));
            Assert.Equal(written, File.ReadAllText(path));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void A_zoom_the_player_changed_is_left_alone_and_the_quick_keys_take_no_key_it_has()
    {
        var path = Temp("[keys]\nzoomout = -, Keypad -, Mouse 4\nzoomin = =, Keypad +\nregroup = R\n");
        try
        {
            Assert.NotNull(KeypadMigration.Run(path));
            var ini = IniFile.Read(path);
            Assert.Equal("-, Keypad -, Mouse 4", ini["keys.zoomout"]);
            Assert.Equal("=, Keypad +", ini["keys.zoomin"]);
            Assert.Equal("none", ini["keys.quicksave"]);
            Assert.Equal("none", ini["keys.quickload"]);
            Assert.Empty(Read(path)[KeyCommand.QuickSave]);
            Assert.Null(KeypadMigration.Run(path));                  // and never again
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void A_zoom_moved_off_the_keypad_already_leaves_the_keypad_to_the_quick_keys()
    {
        var path = Temp("[keys]\nzoomout = -\nzoomin = =\n");
        try
        {
            Assert.NotNull(KeypadMigration.Run(path));
            var keys = Read(path);
            Assert.Equal(new[] { K(KeyNames.Minus) }, keys[KeyCommand.ZoomOut]);
            Assert.Equal(new[] { K(KeyNames.KeypadPlus) }, keys[KeyCommand.QuickSave]);
            Assert.Equal(new[] { K(KeyNames.KeypadMinus) }, keys[KeyCommand.QuickLoad]);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Theory]
    [InlineData("[options]\nalwaysRun = 1\n")]                                            // no [keys]: the defaults already
    [InlineData("[keys]\nzoomout = -, Keypad -\nzoomin = =, Keypad +\nquickload = none\n")]   // past the quick keys: the player's choice
    [InlineData("[keys]\nquicksave = Keypad +\n")]
    public void A_file_with_no_keys_or_with_a_quick_key_is_not_touched(string text)
    {
        var path = Temp(text);
        try
        {
            Assert.Null(KeypadMigration.Run(path));
            Assert.Equal(text, File.ReadAllText(path));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
        Assert.Null(KeypadMigration.Run(Path.Combine(Path.GetTempPath(), $"vitomir-none-{Guid.NewGuid():N}.ini")));
    }
}
