// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Anim;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Characters out of their rest pose: the motion set a template names, the mode it plays when
/// nothing is happening, and the vertices that move because of it.
/// </summary>
public class ThingAnimationTests
{
    static Location Load(string level)
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        return Location.Load(files, level, scene: set.ForLevel(level)?.Entry?.SceneName);
    }

    [Fact]
    public void A_template_s_motion_set_reaches_its_things()
    {
        var town = Load("P1_OTDY");
        var civilian = town.Things.First(t => t.Type == ThingType.Actor && t.MotionSet.Length > 0);
        Assert.NotNull(civilian.Animation);
        Assert.Equal(NmsMotionSet.Idle, civilian.Animation!.Mode!.Name);
        Assert.True(civilian.Animation.Player!.Animation.Duration > 0);
    }

    [Fact]
    public void The_people_in_a_street_are_the_ones_that_animate()
    {
        var east = Load("l1_east");
        Assert.NotEmpty(east.Mesh.Animated);
        // Every animating thing has a skeleton to animate; nothing without one gets in.
        Assert.All(east.Mesh.Animated, a => Assert.True(a.Animation.Model.Bones.Length > 1));
        Assert.True(east.Mesh.Animated.Count <= east.Mesh.Skinned,
                    "more things animating than have a skeleton");
    }

    [Fact]
    public void A_prop_that_is_always_moving_plays_its_create_mode()
    {
        // Ten shipped sets have nothing but `create` — flags, banners, a buoy, an inn's swinging
        // sign. They are the things the level keeps in motion with no script involved.
        var town = Load("P1_OTDY");
        var sign = town.Things.First(t => t.MotionSet.Contains("sign", StringComparison.OrdinalIgnoreCase)
                                          && t.Animation is not null);
        Assert.Equal("create", sign.Animation!.Mode!.Name);
    }

    [Fact]
    public void Posing_moves_the_vertices_and_keeps_their_colours()
    {
        var town = Load("P1_OTDY");
        var actor = town.Mesh.Animated.First(a => a.Animation.Mode!.Name == NmsMotionSet.Idle);
        var before = actor.Vertices.Select(v => v.Position).ToArray();
        var colours = actor.Vertices.Select(v => v.Color).ToArray();
        actor.Animation.Advance(0.5f);
        actor.Pose();
        Assert.True(before.Zip(actor.Vertices).Any(p => Vector3.Distance(p.First, p.Second.Position) > 0.5f),
                    "half a second of the idle moved nothing");
        Assert.Equal(colours, actor.Vertices.Select(v => v.Color));
        // And it stays where its thing is: an idle does not walk anyone across the square.
        var thing = town.Things[actor.Thing];
        Assert.All(actor.Vertices, v => Assert.True(Vector3.Distance(v.Position, thing.Position) < 500f,
                                                    "a posed vertex left the neighbourhood"));
    }

    [Fact]
    public void An_animation_that_does_not_loop_stops_on_its_last_frame()
    {
        using var files = Install.Open();
        var motions = new MotionSets(files);
        var set = motions.Set("civ04Merchant");
        Assert.NotNull(set);
        var death = set!.Modes.First(m => m.Has(NmsFlags.NoLoop));
        var animation = motions.Animation(death.File)!;
        var player = new AnimationPlayer(animation) { Loop = false };
        player.Advance(1000f);
        Assert.Equal(animation.Duration, player.Frame);
        var looping = new AnimationPlayer(animation);
        looping.Advance(1000f);
        Assert.InRange(looping.Frame, 0f, animation.Duration);
    }

    [Fact]
    public void A_thing_with_no_skeleton_or_no_set_simply_stands_still()
    {
        using var files = Install.Open();
        var motions = new MotionSets(files);
        var model = NodModel.Read(files.Read("3d/models/ironBench.nod"), "ironBench.nod");
        Assert.Null(ThingAnimation.For(model, "civ04Merchant", motions));   // no bones to drive
        var person = NodModel.Read(files.Read("3d/models/civ04Merchant2.nod"), "civ04Merchant2.nod");
        Assert.Null(ThingAnimation.For(person, "", motions));               // no motion set named
        Assert.NotNull(ThingAnimation.For(person, "civ04Merchant", motions));
    }

    [Fact]
    public void A_mode_whose_animation_is_missing_is_not_playable()
    {
        // countOrsi.nms points `stand` at erik_listen.nad, which does not ship. The engine drops a
        // mode it cannot load, and so do we — the character keeps its rest pose instead.
        using var files = Install.Open();
        var motions = new MotionSets(files);
        var orsi = motions.Set("countOrsi");
        Assert.NotNull(orsi!.Stand);
        Assert.Null(motions.Find(orsi, NmsMotionSet.Idle));
    }
}
