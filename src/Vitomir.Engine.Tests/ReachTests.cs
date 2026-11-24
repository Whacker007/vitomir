// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **1.1's reach test** (<see cref="Bodies.InReach"/>), built from a comparison with 1.1's
/// first gap. The two bodies' height spans must
/// overlap, and then the distance across the ground — x and y only — less both radii is
/// held to the reach. Earlier, Vitomir measured centre to centre in three dimensions less the radii
/// and asked nothing of height, so on a slope or a stair a target was out of reach sooner than in 1.1,
/// and one on a ledge above the attacker's head could still be struck. These pin both halves, each
/// shape's span, and the blow that now lands on a target a step up.
/// </summary>
public class ReachTests
{
    static WorldThing Body(Vector3 at, CollideShape shape = CollideShape.Cylinder, float radius = 32f,
                           float height = 72f, int guid = 99) => new()
    {
        Index = -1, Guid = guid, Scene = new NsdThing(), Template = "t", Type = ThingType.Actor,
        Collide = shape, Radius = radius, Height = height, Position = at,
        Actor = new Actor { Type = ActorType.Human, Health = 100f, MaxHealth = 100f },
    };

    [Fact]
    public void The_reach_is_measured_across_the_ground_less_both_radii()
    {
        var me = Body(Vector3.Zero);
        // 32 + 32 of body and 40 of clear ground: in at 40, out at 39.9.
        var them = Body(new Vector3(104f, 0f, 0f), guid: 100);
        Assert.True(Bodies.InReach(me, them, 40f));
        Assert.False(Bodies.InReach(me, them, 39.9f));

        // Half a body up a slope: the three-dimensional gap is 16 more, the reach test's the same.
        them.Position = new Vector3(104f, 0f, 36f);
        Assert.True(Bodies.Gap(me, them) > 40f);
        Assert.True(Bodies.InReach(me, them, 40f));

        // x and y both count; only z is left out.
        them.Position = new Vector3(0f, -104f, 10f);
        Assert.True(Bodies.InReach(me, them, 40f));
        Assert.False(Bodies.InReach(me, them, 39.9f));
    }

    [Fact]
    public void Bodies_whose_heights_do_not_overlap_are_out_of_reach()
    {
        // A cylinder's span is its feet to its height: 0..72 against a character standing above.
        var me = Body(Vector3.Zero);
        var above = Body(new Vector3(64f, 0f, 72f), guid: 100);
        Assert.True(Bodies.InReach(me, above, 0f));           // feet level with the top of the head
        above.Position = above.Position with { Z = 72.5f };
        Assert.False(Bodies.InReach(me, above, 500f));        // a ledge over the head: never in reach

        // And the other way: the attacker up the ledge, its target below its feet.
        Assert.False(Bodies.InReach(above, me, 500f));
        me.Position = me.Position with { Z = 0.5f };
        Assert.True(Bodies.InReach(above, me, 0f));
    }

    [Fact]
    public void Each_shape_has_its_own_height_span()
    {
        // A cylinder stands on its point.
        Assert.Equal((10f, 82f), Bodies.HeightSpan(Body(new Vector3(0, 0, 10))));
        // A box spans its own bottom and top, unturned.
        var crate = Body(new Vector3(0, 0, 10), CollideShape.Box, radius: 20f);
        crate.BoxMin = new Vector3(-20, -20, -5);
        crate.BoxMax = new Vector3(20, 20, 30);
        Assert.Equal((5f, 40f), Bodies.HeightSpan(crate));
        // A sphere, and a thing with no body at all, its radius either side of its centre.
        Assert.Equal((-6f, 26f), Bodies.HeightSpan(Body(new Vector3(0, 0, 10), CollideShape.Sphere, radius: 16f)));
        Assert.Equal((-6f, 26f), Bodies.HeightSpan(Body(new Vector3(0, 0, 10), CollideShape.None, radius: 16f)));

        // So a sphere hanging just below a cylinder's feet reaches it; one a hair lower does not.
        var ball = Body(new Vector3(40f, 0f, -16f), CollideShape.Sphere, radius: 16f);
        var man = Body(Vector3.Zero);
        Assert.True(Bodies.InReach(ball, man, 0f));
        ball.Position = ball.Position with { Z = -16.5f };
        Assert.False(Bodies.InReach(ball, man, 0f));
    }

    [Fact]
    public void A_blow_lands_on_a_target_a_step_up_that_three_dimensions_found_out_of_reach()
    {
        var me = Body(Vector3.Zero);
        var sword = new Weapon { Name = "sword" };
        me.Actor!.Weapon = sword;
        me.Actor.Action = ActionMode.Swing;
        float reach = sword.Current.MaxDistance;

        // Just inside the reach across the ground, and 40 up a stair: 1.1 rolls the blow.
        var up = Body(new Vector3(64f + reach - 1f, 0f, 40f), guid: 100);
        me.Actor.Target = up;
        Assert.True(Bodies.Gap(me, up) > reach, "the old measure found it out of reach");
        Assert.False(StrikeReach.Whiffs(me, me.Actor, sword));

        // Standing on a ledge above the swordsman's head: the swing meets the air, however close.
        up.Position = new Vector3(40f, 0f, 80f);
        Assert.True(StrikeReach.Whiffs(me, me.Actor, sword));
    }
}
