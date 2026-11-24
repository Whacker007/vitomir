// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine;
using Vitomir.Engine.Net;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **The Storyteller's scene index to the scripts, on a host**: `GetNSSIndex` answers the
/// host's index and `SetNSSIndex` moves it within the list's steps, which is what the scene pane greys by
/// (`Net.SceneIndex`). Earlier, both natives answered −1 and did nothing on a host too, so the two multiplayer
/// chronicles' `SetNSSIndex` calls (`MP_CTDChronicle`, `MP_LOTChronicle`) never greyed a step.
/// </summary>
[Collection("scripts")]
public class SceneIndexScriptTests(Scripts s)
{
    static object? Call(Scripts s, string key, params object?[] args) =>
        s.Engine.get(new object[] { key, null!, args.Select(v => v is int i ? (object)java.lang.Integer.valueOf(i) : v).ToArray() });

    static int Int(object? o) => ((java.lang.Integer)o!).intValue();

    [Fact]
    public void On_a_host_the_scripts_read_and_move_the_scene_panes_index_within_its_steps()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var settings = GameSettings.For(set["MP_CurseDarkness"]!, "Anna's game", GameFlags.Moderated);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        place.Session = new Session { Here = place };
        var scenes = new CodexScenes(s.Runtime!) { Chronicles = set };
        scenes.Enter(place);
        // Single player first: -1, and nothing moved.
        Call(s, "CodexSequence.SetNSSIndex", 0);
        Assert.Equal(-1, Int(Call(s, "CodexSequence.GetNSSIndex")));

        using var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
        NetNatives.Register(scenes, server);
        var steps = StoryPowers.Steps(files, set["MP_CurseDarkness"]!);
        Assert.Equal(3, steps.Count);                                            // CTD's .nss: three steps
        server.SceneIndex.Read("MP_CurseDarkness", steps.Count);
        Assert.Equal(-1, Int(Call(s, "CodexSequence.GetNSSIndex")));              // nothing reached yet

        Call(s, "CodexSequence.SetNSSIndex", 1);
        Assert.Equal(1, server.SceneIndex.Reached);
        Assert.Equal(1, Int(Call(s, "CodexSequence.GetNSSIndex")));
        Assert.True(server.SceneIndex.Greyed(0) && server.SceneIndex.Greyed(1) && !server.SceneIndex.Greyed(2));

        // Out of the list, either way, moves nothing.
        Call(s, "CodexSequence.SetNSSIndex", 3);
        Call(s, "CodexSequence.SetNSSIndex", -1);
        Assert.Equal(1, Int(Call(s, "CodexSequence.GetNSSIndex")));
        // Backwards is taken: nothing in the native asks for more than the range.
        Call(s, "CodexSequence.SetNSSIndex", 0);
        Assert.Equal(0, Int(Call(s, "CodexSequence.GetNSSIndex")));
        scenes.Leave();
    }
}
