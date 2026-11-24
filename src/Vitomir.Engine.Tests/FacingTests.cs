// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Which way a character is pointing, and the one moment the engine makes sure of it.
/// </summary>
public class FacingTests
{
    static WorldThing At(float x, float y, float z = 0f, bool actor = false) =>
        new()
        {
            Index = 0, Guid = 1, Scene = new Formats.NsdThing(), Template = "t",
            Type = actor ? ThingType.Actor : ThingType.Prop,
            Actor = actor ? new Actor() : null,
            Position = new Vector3(x, y, z),
        };

    /// <summary>The heading a thing at the origin needs to look at a point, by the mover's own formula.</summary>
    static float Toward(float x, float y) => MathF.Atan2(-x, y) * (180f / MathF.PI);

    [Fact]
    public void Facing_a_point_turns_to_it()
    {
        var thing = At(0, 0);
        // +Y is where a thing with no turn on it already looks, so that one is zero; the other
        // three are the quarter turns around it.
        foreach (var (x, y, heading) in new[] { (0f, 100f, 0f), (100f, 0f, -90f), (0f, -100f, 180f), (-100f, 0f, 90f) })
        {
            thing.Orientation = new Vector3(11, 22, 33);
            thing.Face(new Vector3(x, y, 0));
            Assert.Equal(heading, thing.Orientation.Z, 3);
            Assert.Equal(World.Player.Wrap(Toward(x, y)), thing.Orientation.Z, 3);
        }
    }

    [Fact]
    public void Facing_is_level_and_ignores_how_far_above_or_below_the_target_is()
    {
        // The facing takes the target's x and y with the thing's **own** z, so the turn is the same
        // whether the target is at its feet or over its head; and the look-at rebuilds all three
        // rows, which leaves the thing upright.
        var thing = At(0, 0, 50);
        thing.Orientation = new Vector3(30, 40, 0);
        thing.Face(new Vector3(100, 100, 9000));
        Assert.Equal(World.Player.Wrap(Toward(100, 100)), thing.Orientation.Z, 3);
        Assert.Equal(0f, thing.Orientation.X);
        Assert.Equal(0f, thing.Orientation.Y);
    }

    [Fact]
    public void Facing_where_it_already_stands_leaves_it_alone()
    {
        // The look-at compares the direction's length against zero and returns before writing anything.
        var thing = At(10, 20, 30);
        var was = new Vector3(1, 2, 3);
        thing.Orientation = was;
        thing.Face(new Vector3(10, 20, 999));
        Assert.Equal(was, thing.Orientation);
    }

    [Fact]
    public void A_swing_turns_the_attacker_to_its_target()
    {
        // The bug this closes: the approach walks to a spot *beside* the target, so the heading a
        // character stops on points wherever that last step went — often away from what it is
        // about to hit. The swing faces the target before it plays the animation.
        var attacker = At(0, 0, actor: true);
        var target = At(0, -300, actor: true);
        attacker.Orientation = new Vector3(0, 0, 0);         // looking the other way entirely

        Actions.Strike(attacker, target, new Weapon { Name = "w" }, nowMs: 0);
        Assert.Equal(180f, attacker.Orientation.Z, 3);
        Assert.Same(target, attacker.Actor!.Target);
    }

    [Fact]
    public void Whatever_swings_at_Christof_in_the_monastery_is_looking_at_him()
    {
        // The test the unit one above cannot make: a real fight, with the mover running. The swing
        // stops the walk **and then** turns, and the order is what matters — a walk left running
        // turns the character back towards where it was going on the very next frame, which is the
        // shape the bug had.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON2", things: true, sky: false,
                                  scene: ChronicleSet.Load(files).ForLevel("P1_MON2")?.Entry?.SceneName,
                                  player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var sim = new Simulation(place);
        var christof = place.Player!.Thing;

        var swung = new List<(string Who, float Off)>();
        var swinging = new HashSet<WorldThing>();
        for (int frame = 0; frame < 60 * 30; frame++)
        {
            // **Christof keeps moving**, which is the condition the bug needed. Against a target
            // that stands still an attacker walks straight at it and stops facing it by accident,
            // and the swing looks right however it was aimed; it is the circling that leaves the
            // last step pointing somewhere else. Measured both ways on this scene: standing still,
            // the worst swing is 15.6° off with the turn and 15.6° off without it — the test sees
            // nothing. Walking, it is the number the assertion below pins.
            place.Player!.Move(new Vector2(MathF.Cos(frame * 0.05f), MathF.Sin(frame * 0.05f)), 1f / 30f);
            sim.Step(1f / 30f);
            foreach (var thing in place.Things)
            {
                if (thing.Actor is not { Action: ActionMode.Swing, Target: { } at } me) { swinging.Remove(thing); continue; }
                if (!ReferenceEquals(at, christof) || me.Has(Actor.Flag.Dead)) continue;
                // The frame each swing begins: since the attack order strikes inside itself,
                // a rat's swing could start at Christof's heels, and one long
                // swing's frames drift 2.7° each as he circles past — the drift, not the aim.
                if (!swinging.Add(thing)) continue;
                float wanted = Player.Wrap(MathF.Atan2(-(at.Position.X - thing.Position.X),
                                                        at.Position.Y - thing.Position.Y) * (180f / MathF.PI));
                swung.Add((thing.Template, MathF.Abs(Player.Wrap(thing.Orientation.Z - wanted))));
            }
            if (swung.Count > 8) break;
        }

        Assert.NotEmpty(swung);
        // A swing is allowed to drift while the target walks on — but not to point away from it.
        // **Measured on this scene, 21 swings, both ways.** Without the turn the worst swing is
        // 179.7° off and the median is around 175 — the attackers are facing almost exactly
        // backwards, which is what play showed. With it the worst is 11.4°, which is the drift
        // a moving target earns between the turn and the blow landing.
        Assert.All(swung, x => Assert.True(x.Off < 45f,
                                           $"{x.Who} swung {x.Off:0.#}° away from what it was hitting"));
    }
}
