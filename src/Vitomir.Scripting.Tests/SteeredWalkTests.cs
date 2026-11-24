// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **A scene that walks the player.** `OLDT_7_4` — the night Christof is Embraced — fades in, plays
/// a camera path, and one second later sends Christof running at 220 into Ecaterina's region with
/// `SendActorToPos`. Entering the region is what makes him a vampire. In the first wave
/// a headless harness saw him stand still for twenty seconds while the path played:
/// the order was given and the route found, but nobody walked it, because Vitomir stepped the
/// steered character's route from the viewer's frame loop and not from the world's tick, where the
/// engine steps every walker. These pin that the world walks it
/// (<see cref="SteeredWalk"/>), so the scene plays with nothing but the simulation running.
/// </summary>
[Collection("scripts")]
public class SteeredWalkTests(Scripts s)
{
    static (CodexScenes Scenes, Location Where, GameFiles Files) Enter(Scripts s, string level, string scene)
    {
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var location = Location.Load(files, level, sky: false, scene: scene, player: "Christof");
        location.Awaken(new AiClasses(files));
        var scenes = new CodexScenes(s.Runtime!) { Chronicles = set };
        scenes.Enter(location);
        return (scenes, location, files);
    }

    [Fact]
    public void The_Embrace_scene_runs_Christof_into_Ecaterina_s_region_with_only_the_world_ticking()
    {
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_OLDT", "OLDT_7_4");
        var sim = new Simulation(where) { Scripts = scenes };
        var chris = where.Player!.Thing;
        var start = chris.Position;
        chris.Actor!.Type = ActorType.Human;          // as CNVT_3_1 left him

        // The script's timer is a second; the order is a run at 220 (not the template's 250).
        float fastest = 0f;
        for (int i = 0; i < 30 * 2; i++)
        {
            sim.Step(1f / 30f);
            fastest = MathF.Max(fastest, where.Player.Speed);
        }
        Assert.True(Vector3.Distance(start, chris.Position) > 100f,
                    $"Christof is still at {chris.Position}, {Vector3.Distance(start, chris.Position):0} from where he began");
        Assert.Equal(220f, fastest, 0.01f);

        // And he gets there: the region's `entered` makes him a vampire (SetActorType).
        for (int i = 0; i < 30 * 30 && chris.Actor.Type != ActorType.Vampire; i++) sim.Step(1f / 30f);
        Assert.Equal(ActorType.Vampire, chris.Actor.Type);
        scenes.Leave();
        files.Dispose();
    }
}
