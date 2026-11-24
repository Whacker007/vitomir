// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Using a thing: walking up to it and calling its script's <c>clicked</c>.
/// </summary>
public class UseTests
{
    static (WorldThing Thing, Actor Actor) Someone()
    {
        var actor = new Actor { Type = ActorType.Human };
        actor.Health = actor.MaxHealth = 100;
        return (new WorldThing
        {
            Index = 0, Guid = 1, Scene = new Formats.NsdThing(), Template = "christof",
            Type = ThingType.Actor, Actor = actor, Radius = 32f, Height = 128f,
        }, actor);
    }

    static WorldThing Prop(float x) => new()
    {
        Index = 1, Guid = 2, Scene = new Formats.NsdThing(), Template = "lever",
        Type = ThingType.Prop, Radius = 20f, Height = 40f, Position = new Vector3(x, 0, 0),
    };

    [Fact]
    public void The_cursor_offers_use_on_every_prop()
    {
        // The cursor's case 2 returns 4 outright: **every** THING_TYPE_PROP is usable, with no flag
        // test and no look at whether anything would happen. That is why an invisible
        // camera-blocking tracker_thing offers the same verb a door does.
        var (me, _) = Someone();
        Assert.Equal(CursorVerb.Use, Cursor.Verb(me, Prop(10f)));
    }

    [Fact]
    public void Something_within_reach_is_used_where_it_stands()
    {
        var (me, actor) = Someone();
        var lever = Prop(60f);                 // bodies 60 − 32 − 20 = 8 apart
        Assert.True(Bodies.Gap(me, lever) <= Actions.UseReach);

        Assert.Equal(OrderResult.Started, Actions.Use(me, lever, nowMs: 0));
        // A use is not held the way a swing is: the action is over on the same tick. With no place
        // behind the thing there is no script to call, which is the ordinary case for a prop.
        // Ending the action clears the target with it, the way every other Stop does.
        Assert.Equal(ActionMode.None, actor.Action);
        Assert.Null(actor.Target);
    }

    [Fact]
    public void Something_out_of_reach_is_walked_at_rather_than_used()
    {
        var (me, actor) = Someone();
        var lever = Prop(400f);
        Assert.True(Bodies.Gap(me, lever) > Actions.UseReach);

        Assert.Equal(OrderResult.Started, Actions.Use(me, lever, nowMs: 0));
        // No mind to walk it, so the order stops rather than hanging — but it got as far as
        // deciding to walk, which is the branch under test.
        Assert.Equal(ActionMode.None, actor.Action);
    }

    [Fact]
    public void A_busy_or_dead_character_does_not_use_anything()
    {
        var (me, actor) = Someone();
        var lever = Prop(60f);
        actor.BusyUntil = 500;
        Assert.Equal(OrderResult.Busy, Actions.Use(me, lever, nowMs: 100));
        actor.BusyUntil = 0;
        actor.Flags |= Actor.Flag.Dead;
        Assert.Equal(OrderResult.Busy, Actions.Use(me, lever, nowMs: 100));
    }

    [Fact]
    public void Using_a_scripted_thing_calls_its_clicked()
    {
        // End to end, with the game's own Java: the monastery runs ClickExit twice, and ClickExit
        // is one of the **117 shipped classes out of 465** that implement `clicked`. Walking up to
        // one and using it is how a level is left.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON2", things: true, sky: false, scene: "MON2_8_2",
                                  player: "Christof");
        var scripted = place.Things.First(t => t.Script.Length > 0 && t.Type == ThingType.Prop);

        var heard = new List<(int Thing, int By)>();
        place.Clicked = (thing, by) => { heard.Add((thing.Guid, by.Guid)); return true; };

        var me = place.Player!.Thing;
        me.Position = scripted.Position;        // standing on it, so no walking is needed
        Assert.Equal(OrderResult.Started, Actions.Use(me, scripted, nowMs: 0));
        Assert.Equal([(scripted.Guid, me.Guid)], heard);
    }

    [Fact]
    public void The_reach_and_the_retry_are_the_engine_s_own()
    {
        // The use compares against 75 units and adds 250 ms. Both are smaller than the
        // attack's, which is the sort of number that is only right if it is 1.1's own.
        Assert.Equal(75f, Actions.UseReach);
        Assert.Equal(250, Actions.UseRetryMs);
        Assert.True(Actions.UseRetryMs < Actions.ApproachRetryMs);
    }
}
