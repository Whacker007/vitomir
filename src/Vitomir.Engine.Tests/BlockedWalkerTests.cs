// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Old Town by day as a human, clicking past townspeople on
/// purpose, then Judith Bridge and Petrin Hill. Whenever a person stands on Christof's route,
/// 1.1 does the same three things — stands for a third of a second, steps 70 or 100 units
/// off to one side, and carries on to where he was sent. The same clicks through Vitomir with
/// the townspeople moved along 1.1's tracks once had Vitomir's Christof stand, step aside —
/// and stop there, three times in four.
/// </summary>
public class BlockedWalkerTests
{
    /// <summary>
    /// The stranger by the east gate of Old Town, standing on the route Christof was clicked along:
    /// from (3742, −586) to (3756, −56), with the stranger at (3744, −384)
    /// where the scene puts him and where he stands in 1.1. 1.1: stands 0.22 s at (3746, −444),
    /// steps 68 units off at 95°, carries on and arrives.
    /// </summary>
    [Fact]
    public void A_walker_blocked_by_a_person_waits_steps_round_and_carries_on()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1);
        var stranger = place.Things.Single(t => t.Template.Equals("stranger", StringComparison.OrdinalIgnoreCase));
        stranger.Actor!.Flags |= Actor.Flag.AiPaused;          // he stood still in 1.1 too
        Assert.Equal(new Vector3(3744f, -384f, 0f), stranger.Position);

        var player = place.Player!;
        player.Thing.Position = new Vector3(3742f, -586f, 0f);
        Assert.True(player.Ground());
        var goal = new Vector3(3756.4f, -56.1f, -30.5f);
        Assert.True(player.WalkTo(goal, place.Walk!.Find(goal)));

        var sim = new Simulation(place);
        float waited = 0f;
        Vector3? stoodAt = null, steppedTo = null;
        for (int i = 0; i < 30 * 8 && player.Walking; i++)
        {
            sim.Step(1f / 30f);                 // walks the steered route too (SteeredWalk)
            if (player.Waiting && !player.Detouring) { waited += 1f / 30f; stoodAt ??= player.Position; }
            if (player.Detouring) steppedTo ??= player.Destination;
        }

        Assert.False(player.Walking, "still walking after eight seconds");
        Assert.True(Vector3.Distance(player.Position, goal) < 2f, $"it stopped at {player.Position}, not at the click");
        Assert.NotNull(stoodAt);
        Assert.InRange(waited, 0.25f, 0.4f);                    // the person wait, 300 ms
        Assert.NotNull(steppedTo);
        float aside = Vector2.Distance(new(stoodAt!.Value.X, stoodAt.Value.Y), new(steppedTo!.Value.X, steppedTo.Value.Y));
        Assert.Contains(MathF.Round(aside), new[] { 50f, 70f, 100f });   // the detour list's distances
    }
}
