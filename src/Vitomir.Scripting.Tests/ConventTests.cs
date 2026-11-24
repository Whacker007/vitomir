// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// The convent's own scripts — the scenes a new game opens on. `CNVT_3_1` wakes Christof in bed and
/// arms him; `CNVT_3_3` lays him back in it for the archbishop's conversation. Both do it by
/// **teleporting the party to a conversation spot from `beginscene`**, which is the thing a game's
/// own placement has to leave alone (*"Christof decided to sleep several feet
/// from his bed"* — the harness was standing him on the arrival point after the scripts had run).
/// </summary>
[Collection("scripts")]
public class ConventTests(Scripts s)
{
    [Theory]
    [InlineData("CNVT_3_1")]
    [InlineData("CNVT_3_3")]
    public void The_scene_puts_Christof_where_it_wants_him_and_it_is_not_the_arrival_point(string scene)
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var place = Location.Load(files, "P1_CNVT", sky: false, scene: scene, player: "Christof", entrance: 0);
        place.Awaken(new AiClasses(files), 1);
        var session = new Vitomir.Engine.Session { Here = place };
        place.Session = session;
        var camera = new WorldCamera();
        camera.Start(place.Player!.Thing);
        var scenes = new CodexScenes(s.Runtime!)
        {
            Chronicles = ChronicleSet.Load(files), Camera = camera, Talk = new Conversations(),
            Nls = new Vitomir.Engine.Ui.Strings(files).Find,
        };
        var arrival = place.Arrivals.Length > 0 ? place.Arrivals[0].Position : place.StartPosition;
        scenes.Enter(place);
        var stood = place.Player!.Thing.Position;
        scenes.Leave();

        // The scene moved him, and it moved him somewhere the arrival point is not.
        Assert.True(Vector3.Distance(stood, arrival) > 32f,
                    $"{scene}: the script left him {Vector3.Distance(stood, arrival):0} from the arrival point");

        // And it left him at the height the start asked for. The convent's bedside start is 14
        // units up, in the bed; grounding him to the walk map underneath sank him into the
        // mattress, which is why the teleport passes the teleport's "don't move
        // me" argument (`LocationParty.TeleportPartyToConversation`).
        var start = place.ConversationStart(0, 1);
        Assert.NotNull(start);
        Assert.Equal(start!.Position.Z, stood.Z, 2);
    }

    /// <summary>
    /// **A character with no AI mind can still be walked by a script.** In the engine a walker is
    /// part of the thing: `SendActorToPos` looks the guid up, checks the type and the
    /// health, finds the ground and calls the walk straight on the thing, with no mind in the path.
    /// Ours hung the walker off the mind, so the convent's archbishop — placed by the scene with no
    /// AI file — could not be moved at all, and `CNVT_3_3`'s order to walk him to the bedside did
    /// nothing (*"Geza does NOT walk over"*).
    /// </summary>
    [Fact]
    public void The_archbishop_walks_to_his_keyframe_although_he_has_no_mind()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var place = Location.Load(files, "P1_CNVT", sky: false, scene: "CNVT_3_3", player: "Christof", entrance: 0);
        place.Awaken(new AiClasses(files), 1);

        var geza = place.Things.FirstOrDefault(t => string.Equals(t.Actor?.CastId, "Geza",
                                                                  StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(geza);
        Assert.Null(geza!.Mind);                                  // the scene gives him no AI file
        Assert.True(geza.Keyframes.Length > 1, "the scene is meant to give him somewhere to walk to");

        var walker = place.WalkerFor(geza);
        Assert.NotNull(walker);                                   // this is what was missing

        var to = geza.Keyframes[1].Position;
        var from = geza.Position;
        Assert.True(Vector3.Distance(from, to) > 100f, "his keyframe is meant to be a walk away");
        Assert.True(walker!.WalkTo(to, place.Walk!.Find(to), speed: 90f, clamp: false, persist: true));

        var sim = new Simulation(place) { Camera = new WorldCamera() };
        for (int i = 0; i < 600 && Vector3.Distance(geza.Position, to) > 16f; i++) sim.Step(1f / 30f);

        Assert.True(Vector3.Distance(geza.Position, to) <= 16f,
                    $"he stopped {Vector3.Distance(geza.Position, to):0} units short of the bedside");
    }

}
