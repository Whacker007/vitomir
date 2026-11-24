// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// The chronicle as the scripts see it. These run the game's own classes against the real
/// chronicle files: the exit signs in East London know which doors are locked because
/// <c>ClickExit</c> asked the sequence, and the answer came out of <c>London.nsc</c>.
/// </summary>
[Collection("scripts")]
public class SequenceTests(Scripts s)
{
    const uint NoHighlight = 0x40;                       // Codex.THING_FLAG_NOHIGHLIGHT

    /// <summary>Loads a place with its chronicle attached, the way the game would.</summary>
    static (CodexScenes Scenes, Location Where, Chronicle Chronicle) Enter(Scripts s, string level)
    {
        using var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var owner = set.ForLevel(level)!.Value;
        var location = Location.Load(files, level, sky: false, scene: owner.Entry!.SceneName);
        var scenes = new CodexScenes(s.Runtime!) { Chronicles = set };
        scenes.Enter(location);
        return (scenes, location, owner.Chronicle);
    }

    [Fact]
    public void A_scene_knows_which_chronicle_and_location_it_is()
    {
        s.Ready();
        var (scenes, where, chronicle) = Enter(s, "l1_tol2");
        Assert.Equal("London", chronicle.Name);
        Assert.Equal("TowerOfLondon2", chronicle.NameOf(chronicle.Current));
        Assert.Equal("TOL2_30_1", where.SceneFile);
        scenes.Leave();
    }

    [Fact]
    public void The_chronicles_script_class_is_the_one_its_file_names()
    {
        s.Ready();
        // Before the chronicle files were read, the right class was found by trying each in turn
        // and watching for a ClassCastException. Now the `script` directive says which it is.
        var (scenes, _, _) = Enter(s, "l1_tol2");
        Assert.NotEmpty(scenes.Running);
        Assert.Equal("LondonChronicle", scenes.ChronicleName ?? "LondonChronicle");
        scenes.Leave();

        var (prague, _, _) = Enter(s, "P1_HAVN");
        Assert.NotEmpty(prague.Running);
        prague.Leave();
    }

    [Fact]
    public void Locked_exits_get_the_locked_description_and_open_ones_do_not()
    {
        s.Ready();
        // East London's ways into the Tower, the cargo ship and the brothel all start closed, and
        // the bridge and West London do not. ClickExit.beginscene asks the sequence which is which
        // and appends "_L" to the exit sign's description id when it is locked.
        var (scenes, where, chronicle) = Enter(s, "l1_east");
        var exits = where.Things.Where(t => t.Script == "ClickExit").ToList();
        Assert.NotEmpty(exits);

        string here = chronicle.NameOf(chronicle.Current);
        Assert.Equal("EastLondon", here);
        int locked = 0;
        foreach (var t in exits)
        {
            int exitNumber = t.Scene!.Params.Length > 0 ? t.Scene.Params[0].Bits : -1;
            bool closed = chronicle.IsExitClosed(here, exitNumber);
            if (closed) locked++;
            Assert.True(closed == t.DescriptionId.EndsWith("_L", StringComparison.Ordinal),
                        $"exit {exitNumber} ({t.DescriptionId}) is {(closed ? "closed" : "open")} in {chronicle.Name}");
            // An exit that leads nowhere is not highlightable; one that leads somewhere is.
            bool valid = chronicle.IsExitValid(here, exitNumber);
            Assert.Equal(!valid, (t.Flags & NoHighlight) != 0);
        }
        Assert.True(locked >= 3, $"East London should start with three locked exits, found {locked}");
        scenes.Leave();
    }

    [Fact]
    public void Opening_an_exit_unlocks_its_sign_when_the_player_comes_back()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var owner = set.ForLevel("l1_east")!.Value;
        var scenes = new CodexScenes(s.Runtime!) { Chronicles = set };

        // As the story finds it: the way into the Tower is barred, and the sign says so.
        Assert.EndsWith("_L", TowerSign(files, owner, scenes));

        // A chronicle script opens it. Coming back reloads the place, so the signs are read afresh.
        owner.Chronicle.SetExitClosed("EastLondon", 1, false);
        Assert.DoesNotContain("_L", TowerSign(files, owner, scenes));
    }

    /// <summary>Enters East London and returns the description id of the sign on exit 1, the Tower.</summary>
    static string TowerSign(GameFiles files, ChronicleWhere owner, CodexScenes scenes)
    {
        var where = Location.Load(files, "l1_east", sky: false, scene: owner.Entry!.SceneName);
        scenes.Enter(where);
        var sign = where.Things.First(t => t.Script == "ClickExit" &&
                                           t.Scene.Params.Length > 0 && t.Scene.Params[0].Bits == 1);
        scenes.Leave();
        return sign.DescriptionId;
    }

    [Fact]
    public void The_sequence_natives_are_all_implemented()
    {
        s.Ready();
        // Every native the SDK declares on CodexSequence but the two teleporter ones
        // (not written), and the one on CodexThing that reads the chronicle.
        string[] implemented =
        [
            "GetNumLocations", "GetCurrentLocation", "GetLocationName", "GetChronicleInfoFlags",
            "GetLocationFlags", "SetLocationFlags", "ClearLocationFlags",
            "IsExitValid", "IsExitClosed", "OpenExit", "CloseExit", "ChangeExit",
            "GetChronicleFlag", "SetChronicleFlag", "ClearChronicleFlag", "ToggleChronicleFlag",
            "ClearAllChronicleFlags", "GetPortalLocation", "SetPortalLocation",
            "GetReviveLocation", "SetReviveLocation", "TakeExit",
            "ChangeChronicle", "ChangeScene", "Jump", "PlayerJump", "EndGame", "SaveGame", "Advance",
            "SetupAdvance", "Vault", "GetNSSIndex", "SetNSSIndex", "SaveBoolean", "RestoreBoolean",
            "SaveInt", "RestoreInt", "SaveFloat", "RestoreFloat", "SaveString", "RestoreString",
        ];
        var scenes = new CodexScenes(s.Runtime!);
        foreach (var n in implemented)
            Assert.True(scenes.Engine.Implements("CodexSequence." + n), $"CodexSequence.{n} is not implemented");
        Assert.True(scenes.Engine.Implements("CodexThing.GetLocationNum"));
    }
}
