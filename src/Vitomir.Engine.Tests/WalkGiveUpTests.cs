// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **How long a walk waits before it gives up** — ours twice over, since 1.1's walker never gives up:
/// the player's walks (a click, the held button) after <see cref="Player.GiveUpAfter"/>
/// half-second waits, and every walk a mind orders after
/// <see cref="Player.MindGiveUpAfter"/>. Until then the AI's walks
/// shared the player's six.
/// </summary>
public class WalkGiveUpTests
{
    /// <summary>The two counts, as decided.</summary>
    [Fact]
    public void The_player_gives_up_after_six_waits_and_a_mind_after_ten()
    {
        Assert.Equal(6, Player.GiveUpAfter);
        Assert.Equal(10, Player.MindGiveUpAfter);
    }

    /// <summary>
    /// Old Town by day: every townsperson the AI walks is on a mind's walk (ten waits), and the
    /// player's click that follows is the player's (six).
    /// </summary>
    [Fact]
    public void A_walk_the_AI_orders_carries_the_minds_count_and_a_click_the_players()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1);
        var sim = new Simulation(place);
        int walking = 0;
        for (int i = 0; i < 30 * 10; i++)
        {
            sim.Step(1f / 30f);
            foreach (var m in minds.All)
            {
                if (ReferenceEquals(m.Thing, place.Player?.Thing) || m.Mode == AiMode.PedFly || !m.Walker.Walking) continue;
                walking++;
                Assert.Equal(Player.MindGiveUpAfter, m.Walker.Patience);
            }
        }
        Assert.True(walking > 50, $"only {walking} samples of anybody walking");
        var player = place.Player!;
        var to = player.Thing.Position + new Vector3(120f, 0f, 0f);
        int node = player.Map.Find(to);
        Assert.True(node >= 0);
        Assert.True(player.WalkTo(to, node));
        Assert.Equal(Player.GiveUpAfter, player.Patience);
    }

    /// <summary>
    /// The route on which the whole-game walk test's click gives up (see
    /// <c>PathTests.A_walk_a_script_ordered_retries_for_ever_where_a_click_gives_up</c>), replayed
    /// as a mind's walk: at the moment the click gave up, the mind's walk is still trying.
    /// </summary>
    [Fact]
    public void A_minds_walk_is_still_trying_when_the_players_has_given_up()
    {
        using var files = Install.Open();
        (bool GaveUp, bool Walking, float Clock) Replay(bool mind, float stopAt)
        {
            var place = Location.Load(files, "P2_VYSM", things: false, sky: false, player: "Christof");
            var player = place.Player!;
            var map = place.Walk!;
            var ground = Enumerable.Range(0, map.Nodes.Length).Where(i => map.Nodes[i].Outline.Length > 3).ToArray();
            var random = new Random(5);
            for (int k = 0; k < 12; k++)
            {
                int a = ground[random.Next(ground.Length)], b = ground[random.Next(ground.Length)];
                if (a == b) continue;
                player.Thing.Position = Middle(map.Nodes[a]);
                if (!player.Ground()) continue;
                player.Stop();
                bool target = a == 12 && b == 70;
                if (!player.WalkTo(Middle(map.Nodes[b]), b, mind: mind && target)) continue;
                float clock = 0;
                for (; clock < (target ? stopAt : 120f) && player.Walking; clock += 1f / 60f) player.Follow(1f / 60f);
                if (target) return (player.GaveUp, player.Walking, clock);
            }
            throw new InvalidOperationException("the walk was not in the sequence");
        }
        var click = Replay(mind: false, stopAt: 120f);
        Assert.True(click.GaveUp, $"the click walk: {click}");
        var ai = Replay(mind: true, stopAt: click.Clock);
        Assert.False(ai.GaveUp, $"the mind's walk gave up with the click, at {click.Clock:0.0} s");
        Assert.True(ai.Walking);
    }

    static Vector3 Middle(WalkNode node)
    {
        var c = Vector2.Zero;
        foreach (var v in node.Outline) c += v;
        c /= node.Outline.Length;
        return new Vector3(c, node.Height(c.X, c.Y));
    }
}
