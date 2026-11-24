// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **The woman with news of the golem**. As Mendel's first conversation starts, NQTR_11_1 turns
/// the AI off and sends `_RunPed1` (civ05younglady_nq) to her fourth keyframe at 120, through the
/// archway. The keyframe is at z −16 and the floor under it works out to −15.999998: a hair above the
/// point, which Vitomir refused as ground above it, so she stood still in play. 1.1's test
/// takes a point within 1e-5 of a plane as on it (<see cref="WalkMap.FindUnder"/>).
/// </summary>
[Collection("scripts")]
public class RunningWomanTests(Scripts s)
{
    [Fact]
    public void The_woman_with_news_of_the_golem_runs_through_the_archway()
    {
        using var p = Place.Enter(s, "p1_nqtr", "NQTR_11_1");
        var ped = p.Where.Things.First(t => t.Template == "civ05younglady_nq");
        var goal = ped.Keyframes[3].Position;
        p.Where.Minds!.MainFlags |= AiMainFlag.Paused;                        // Codex.AIOff, as the script does first
        var self = s.Runtime!.NewThingRef("CodexActor", ped.Guid);
        Assert.True(Place.Bln(Place.Call(s, "CodexActor.SendActorToPos", self, new[] { goal.X, goal.Y, goal.Z }, 120f)));
        Assert.True(p.RunUntil(() => Vector2.Distance(new(ped.Position.X, ped.Position.Y), new(goal.X, goal.Y)) < 1f, 20f),
                    $"she is at {ped.Position}, not {goal}");
    }
}
