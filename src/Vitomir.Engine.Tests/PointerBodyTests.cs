// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The body the pointer meets a thing by — the hover's things-only sweep, which
// meets each thing as its collide shape says: a sphere about its position, a cylinder on
// its feet, a box placed by its transform. Vitomir's pointer used to measure
// every shape as a cylinder rising from the thing's position, and play found the consequence at
// Golden Lane's door to Judith Bridge: the door could not always be clicked; near
// its top it could, lower it could not. The door is a model-less
// `tracker_thing` box centred halfway up the doorway, so the cylinder started at the door's middle.
using System.Numerics;
using Vitomir.Engine.Ui;
using Vitomir.Formats;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

public class PointerBodyTests
{
    static WorldThing Body(CollideShape shape, Vector3 at, float radius = 0f, float height = 0f,
                           Vector3 boxMin = default, Vector3 boxMax = default) => new()
    {
        Index = 0, Guid = 1, Scene = NsdThing.Placed("test", at, Vector3.Zero, 0), Template = "test",
        Type = ThingType.Prop, Position = at, Collide = shape, Radius = radius, Height = height,
        BoxMin = boxMin, BoxMax = boxMax,
    };

    /// <summary>
    /// **The door, low and high.** From where the game's camera sits behind Christof in front of
    /// Golden Lane's west door, the pointer at the foot of the door, its middle and its top all land
    /// on the exit (`GOLD_EXIT_DESC_4`, the `ClickExit` tracker), through the real pointer: the
    /// surface behind limits the reach (`WorldSweep.Pointer`) and the gate decides
    /// (`Highlight.Offers`). Before, the lower rows answered nothing and a click walked Christof to
    /// the floor in front of the door.
    /// </summary>
    [Fact]
    public void Golden_lanes_door_to_judith_bridge_takes_the_pointer_low_and_high()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_GOLDD", things: true, sky: false, player: "Christof");
        var me = place.Player!.Thing;
        var exit = place.Things.Single(t => t.DescriptionId == "GOLD_EXIT_DESC_4");
        Assert.Equal(CollideShape.Box, exit.Collide);
        // Where Christof stood when his click at last took (the use), and the
        // camera up and behind him.
        me.Position = new Vector3(-1002f, -1261f, 10f);
        var eye = new Vector3(-850f, -1240f, 230f);
        float bottom = exit.Position.Z + exit.BoxMin.Z, top = exit.Position.Z + exit.BoxMax.Z;
        Assert.True(bottom < exit.Position.Z - 100f, "the exit's box reaches well below its position");

        foreach (float z in new[] { 60f, 110f, 180f, 250f, 310f })
            foreach (float y in new[] { -1270f, -1222f, -1175f })
            {
                var dir = Vector3.Normalize(new Vector3(exit.Position.X + exit.BoxMax.X, y, z) - eye);
                var ground = WorldSweep.Pointer(place, eye, dir);
                var hit = Pick.NearestThing(place.Things, eye, dir, out bool blocked,
                                            ground?.Distance ?? float.MaxValue, me, t => Highlight.Offers(me, t));
                Assert.False(blocked);
                Assert.True(ReferenceEquals(hit?.Thing, exit),
                            $"the pointer at ({y}, {z}) on the door answered {hit?.Thing.Template ?? "nothing"}");
            }
    }

    /// <summary>
    /// **The pointer meets each shape where the engine does.** A box is its bounds about the thing's
    /// position, so a box centred above the floor is met below its position; a sphere is
    /// about its centre, met below it too; a cylinder stands on its feet and is
    /// not met below them. The slack (<see cref="Pick.ClickSlack"/>, ours) widens each one.
    /// </summary>
    [Fact]
    public void Each_body_is_met_where_the_engine_meets_it()
    {
        var at = new Vector3(0f, 0f, 200f);
        var box = Body(CollideShape.Box, at, boxMin: new Vector3(-48f, -96f, -164f), boxMax: new Vector3(48f, 96f, 164f));
        var sphere = Body(CollideShape.Sphere, at, radius: 50f);
        var cylinder = Body(CollideShape.Cylinder, at, radius: 50f, height: 100f);
        var start = new Vector3(500f, 0f, 0f);

        // Level lines at heights below the position, above it, and past the slack.
        float? Level(WorldThing t, float z) => Pick.PointerMeets(t, start with { Z = z }, -Vector3.UnitX);

        Assert.Equal(500f - 48f - Pick.ClickSlack, Level(box, 60f)!.Value, 3);
        Assert.NotNull(Level(box, 360f));
        Assert.Null(Level(box, 200f - 164f - Pick.ClickSlack - 1f));

        Assert.NotNull(Level(sphere, 200f - 40f));
        Assert.NotNull(Level(sphere, 200f + 40f));
        Assert.Null(Level(sphere, 200f - 50f - Pick.ClickSlack - 1f));

        Assert.Null(Level(cylinder, 199f));
        Assert.NotNull(Level(cylinder, 250f));
        Assert.Null(Level(cylinder, 301f));
    }

    /// <summary>
    /// **What the engine's sweep never meets, the pointer never meets.** A thing with
    /// `THING_FLAG_DISABLECOLLIDE` is passed over by the sweep's own filter before its body
    /// is looked at, so no pointer lands on it; nor on a thing with no body at all.
    /// </summary>
    [Fact]
    public void A_thing_the_sweep_passes_over_is_not_pointed_at()
    {
        var box = Body(CollideShape.Box, Vector3.Zero, boxMin: new Vector3(-20f), boxMax: new Vector3(20f));
        var start = new Vector3(300f, 0f, 0f);
        Assert.NotNull(Pick.NearestThing([box], start, -Vector3.UnitX));
        box.Flags |= WorldThing.Flag.DisableCollide;
        Assert.Null(Pick.NearestThing([box], start, -Vector3.UnitX));
    }

    /// <summary>
    /// **A thing with no body is not under the pointer, so the pointer reaches what stands behind it**
    /// (*"I can't talk to the weaponsmith in Vienna. It highlights his
    /// sharpening wheel, and gives the talking skull cursor, but no conversation starts."*). Friedrich's
    /// wheel (`ViennaSmithyWheel`) is a character with `TALK TO`, no body and no script, seven units in
    /// front of him; 1.1's sweep meets no bodiless thing, so the pointer finds Friedrich, whose
    /// `WEAP_Friedrich` answers the click. From the shop's door, looking at his chest through the wheel.
    /// Libussa hidden by her script (`THING_COLLIDE_NONE`) is the same rule.
    /// </summary>
    [Fact]
    public void Vienna_s_weaponsmith_is_under_the_pointer_through_his_bodiless_wheel()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "V1_WEAP", things: true, sky: false, scene: "WEAP_20_2", player: "Christof");
        var wheel = place.Things.Single(t => t.Template.Equals("viennasmithywheel", StringComparison.OrdinalIgnoreCase));
        var smith = place.Things.Single(t => t.Template.Equals("viennasmithy", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(CollideShape.None, wheel.Collide);
        var eye = new Vector3(100f, -40f, 300f);
        var chest = smith.Position + new Vector3(0f, 0f, 60f);
        var ray = Vector3.Normalize(chest - eye);
        Assert.Null(Pick.PointerMeets(wheel, eye, ray));
        Assert.Same(smith, Pick.NearestThing([wheel, smith], eye, ray)?.Thing);
    }

    /// <summary>
    /// **A character a script has hidden cannot be pointed at.** `OLDT_Libussa` hides Libussa at half
    /// of the Old Town's scene starts after the embrace — `THING_RENDERFLAG_DONTRENDER` and
    /// `THING_COLLIDE_NONE` — and 1.1's pointer meets only bodies. Vitomir's own fallback for a character
    /// with no body (a cylinder of its radius) found her anyway, and a play-test clicked the
    /// empty courtyard into her rant. Shown again, she is pointable as before.
    /// </summary>
    [Fact]
    public void A_character_a_script_has_hidden_is_not_under_the_pointer()
    {
        var at = new Vector3(100f, 0f, 0f);
        var libussa = new WorldThing
        {
            Index = 0, Guid = 1, Scene = NsdThing.Placed("libussa", at, Vector3.Zero, 0), Template = "libussa",
            Type = ThingType.Actor, Position = at, Collide = CollideShape.None, Radius = 20f, Height = 90f,
            Actor = new Actor { Type = ActorType.Human, Name = "Libussa" },
        };
        var eye = new Vector3(0f, 0f, 50f);
        var dir = Vector3.UnitX;
        Assert.Null(Pick.PointerMeets(libussa, eye, dir));                         // her script took her body
        libussa.RenderFlags |= Pick.HiddenFlag;
        Assert.Null(Pick.PointerMeets(libussa, eye, dir));
        libussa.RenderFlags &= ~Pick.HiddenFlag;
        libussa.Collide = CollideShape.Cylinder;                                     // shown again, with her body
        Assert.NotNull(Pick.PointerMeets(libussa, eye, dir));
    }
}
