// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **A scene script's things are found by their scene index**. In
/// an autosave in Old Town by day: the monk beside the Knights of St John outside the convent spoke
/// the old man's speech from the east gate, the party was then standing at the gate, and the old man
/// himself answered nothing. `OTDY_4_1` names its "Stranger" by his index in the chapter scene;
/// Vitomir read that index as a position in the place's thing list, and a load had taken things out
/// of the list before it — so every later thing had moved up one and the monk stood where the old
/// man should have.
/// </summary>
[Collection("scripts")]
public class SceneParamTests(Scripts s)
{
    [Fact]
    public void The_old_man_at_the_gate_is_the_stranger_even_after_things_before_him_are_gone()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var place = Location.Load(files, "P1_OTDY", sky: false, scene: "OTDY_4_1", player: "Christof");
        place.Awaken(new AiClasses(files), 1);
        place.Session = new Vitomir.Engine.Session { Here = place };
        var stranger = place.Things.First(t => t.Template.Equals("stranger", StringComparison.OrdinalIgnoreCase));
        var monk = place.Things.First(t => t.Template.Equals("civ09monk1_pe", StringComparison.OrdinalIgnoreCase));

        // What a load or a pick-up does: a thing earlier in the list than both of them goes.
        var gone = place.Things.First(t => t.Index >= 0 && t.Index < monk.Index && t.Index < stranger.Index
                                            && t.Actor is null && t.Script.Length == 0);
        place.Remove(gone, drop: false);

        var talk = new Conversations();
        var camera = new WorldCamera();
        camera.Start(place.Player!.Thing);
        var scenes = new CodexScenes(s.Runtime!)
        {
            Chronicles = ChronicleSet.Load(files), Camera = camera, Talk = talk,
            Nls = new Vitomir.Engine.Ui.Strings(files).Find,
        };
        scenes.Enter(place);
        try
        {
            var christof = place.Player!.Thing;
            Assert.False(scenes.Clicked(monk, christof) && talk.Current is not null,
                         $"the monk started {talk.Current?.Talk.Name}");
            Assert.Null(talk.Current);
            scenes.Clicked(stranger, christof);
            Assert.NotNull(talk.Current);
            Assert.Equal("4_1_Stranger", talk.Current!.Talk.Name, ignoreCase: true);
        }
        finally { scenes.Leave(); }
    }
}
