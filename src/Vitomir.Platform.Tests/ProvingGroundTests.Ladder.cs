// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Tests;

/// <summary>The proving ground's ladder and shimmy ledge (the "not-built" station's first and last lanes).</summary>
public sealed partial class ProvingGroundTests
{
    [Fact]
    public void The_course_s_ladder_is_climbed_from_its_foot_up_and_over_and_from_its_top_down_and_off()
    {
        // The course has four (the ladders station's three beside the earlier build's); this one is the earlier build's, now 4.2 m (rung 14's top, the clips' own).
        var lane = Assert.Single(Lanes("ladder"), l => l.Start == "not-built-ladder");
        Assert.Contains(Need().Level.Collision.Ladders.All, l => l.Name == "not-built-ladder");
        float top = lane.N("top");
        float line = lane.N("line");
        bool used = false, turned = false;
        int onTop = -1;
        var d = Go(lane, Gait.Run, 20 * 120, (i, b, m) =>
        {
            // Up: run at it, use at its foot, forward until standing on the top.
            if (onTop < 0 && b.State == BodyState.Ground && b.Feet.Z > top - 0.1f) onTop = i;
            if (onTop < 0)
            {
                m.Use = !used && b.State == BodyState.Ground && b.Feet.X >= line - 0.75f;
                used |= m.Use;
                return m;
            }
            // Down: still a moment, back to the edge, turn to the drop, use, then back (S) to the ground.
            if (i < onTop + 60) return m with { Direction = Vector2.Zero };
            if (!turned && b.Feet.X > line + 0.25f) return m with { Direction = new Vector2(-0.5f, 0f), Facing = MathF.PI };
            m = m with { Direction = Vector2.Zero, Facing = MathF.PI };
            if (!turned) { turned = true; m.Use = true; return m; }
            // Back (S) as the view looks at the drop: the wish towards the tower; the stick's back goes down.
            return b.State == BodyState.Ladder ? m with { Direction = new Vector2(1f, 0f) } : m;
        });
        Assert.True(d.Started.Take(5).SequenceEqual([TraversalKind.LadderOn, TraversalKind.Ladder, TraversalKind.PullUp, TraversalKind.LadderOn, TraversalKind.Ladder]), d.ToString());
        Assert.True(d.Stood >= top - 0.02f, d.ToString());
        Assert.Equal(BodyState.Ground, d.End.State);
        Assert.True(d.End.Feet.Z < 0.02f && d.End.Feet.X < line - 0.3f, d.ToString());
    }

    [Fact]
    public void Wanderers_on_routes_round_the_ladder_s_foot_and_top_never_get_on_it()
    {
        var level = Need().Level;
        var ladder = level.Collision.Ladders.All.First(l => l.Name == "not-built-ladder");   // The earlier build's of the four
        var b = new WorldBuilder(11);
        MovementServices.Register(b, level);
        var w = b.Build();
        var walkers = new List<EntityId>();
        for (int k = 0; k < 8; k++)
        {
            var at = (k % 2 == 0 ? ladder.Foot + ladder.Out * 0.8f : ladder.Top - ladder.Out * 0.8f) + new Vector3(0, (k / 2 - 1.5f) * 0.7f, 0.02f);
            walkers.Add(MovementServices.SpawnWanderer(w, at, MathF.PI * k / 4f, Locomotion.Sandbox, radius: 8f));
        }
        var frame = new Control.ActionFrame(Vector2.Zero, Vector2.Zero, Control.Buttons.None, default);
        var from = walkers.Select(e => w.Get<Body>(e).Feet).ToList();
        float[] went = new float[walkers.Count];
        for (int t = 0; t < 60 * 120; t++)
        {
            MovementServices.Step(w, frame);
            Assert.Empty(w.Events<LadderMounted>().ThisTick);
            for (int k = 0; k < walkers.Count; k++) went[k] = MathF.Max(went[k], Vector3.Distance(w.Get<Body>(walkers[k]).Feet, from[k]));
        }
        Assert.All(walkers, e => Assert.NotEqual(BodyState.Ladder, w.Get<Body>(e).State));
        Assert.True(went.Count(d => d > 2f) >= 4, $"the wanderers went {string.Join(", ", went.Select(d => d.ToString("0.0")))} m");
    }

    [Fact]
    public void The_course_s_ledge_at_2_4_m_is_grabbed_by_a_tap_and_shimmied_along_to_its_end()
    {
        var lane = Assert.Single(Lanes("shimmy"));
        float y = lane.N("y"), length = lane.N("length");
        bool took = false;
        var d = Go(lane, Gait.Walk, 20 * 120, (i, b, m) =>
        {
            if (b.State is not BodyState.Air and not BodyState.Ground) took = true;
            if (!took)
            {
                m.Jump = m.JumpHeld = b.State == BodyState.Ground && b.Feet.X >= lane.Face - 0.15f - 0.55f;
                return m;
            }
            // Hanging: to the right (−Y, facing +X) all the way.
            return m with { Direction = b.State == BodyState.Hanging ? new Vector2(0f, -1f) : Vector2.Zero };
        });
        Assert.True(d.Started.SequenceEqual([TraversalKind.Grab, TraversalKind.Hang]), d.ToString());
        Assert.Equal(BodyState.Hanging, d.End.State);
        Assert.Equal(y - length / 2f + BodyMotor.ShimmyHand, d.End.Feet.Y, 0.03f);
    }
}
