// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Navigation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// The platform's movement registered in one call (ours): the level's space, the bodies, direct control,
/// the cameras, the reticle and the routes, and helpers that put bodies into a world.
/// </summary>
public static class MovementServices
{
    /// <summary>The systems' places in their phases (modules put theirs before or after by these numbers).</summary>
    public const int ControlOrder = 100, RoutesOrder = 100, BodiesOrder = 100, CameraOrder = 100, ReticleOrder = 110;

    /// <summary>
    /// Registers, in this order (the digest's order):
    /// <list type="number">
    /// <item>components <see cref="Body"/>, <see cref="Locomotion"/>, <see cref="MoveIntent"/>, <see cref="Controlled"/>,
    /// <see cref="CameraRig"/>, <see cref="EyeEase"/>, <see cref="RouteWalker"/>, <see cref="Traversal"/>;</item>
    /// <item>events <see cref="JumpAttempted"/>, <see cref="JumpTookOff"/>, <see cref="JumpApex"/>, <see cref="Landed"/>,
    /// <see cref="TraversalStarted"/>, <see cref="TraversalEnded"/>, <see cref="LedgeGrabbed"/>, <see cref="LedgeDropped"/>, then
    /// <see cref="Control.SprintKept"/> and <see cref="Control.SupernaturalSpeed"/>;</item>
    /// <item>services the <see cref="LevelSpace"/>, <see cref="TickInput"/>, <see cref="CameraSettings"/>
    /// (<paramref name="cameras"/> or the defaults), <see cref="ViewState"/>, <see cref="ReticleState"/>;</item>
    /// <item>systems <c>Input/100 platform.control</c> (<see cref="PlayerControlSystem"/>), <c>Think/100 platform.routes</c>
    /// (<see cref="RouteSystem"/>), <c>Move/100 platform.bodies</c> (<see cref="BodySystem"/>), <c>Post/100 platform.camera</c>
    /// (<see cref="CameraSystem"/>), <c>Post/110 platform.reticle</c> (<see cref="ReticleSystem"/>);</item>
    /// <item>the world's edges (<see cref="WorldEdges.Register"/>): the exits' thresholds and the safety net under the level.</item>
    /// </list>
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b, LevelSpace level, CameraSettings? cameras = null)
    {
        b.Component<Body>().Component<Locomotion>().Component<MoveIntent>()
         .Component<Controlled>().Component<CameraRig>().Component<EyeEase>().Component<RouteWalker>().Component<Traversal>();
        b.Event<JumpAttempted>().Event<JumpTookOff>().Event<JumpApex>().Event<Landed>()
         .Event<TraversalStarted>().Event<TraversalEnded>().Event<LedgeGrabbed>().Event<LedgeDropped>();
        JumpEvents.Register(b);
        LadderEvents.Register(b);    // on and off ladders, the shimmy, a push-off from a lip, for the log
        HangEvents.Register(b);      // the hang's corners and leaps, for the log
        WallEvents.Register(b);      // the walls, for the log
        SideEvents.Register(b);      // squeezing, cover and stealth, for the log
        b.Event<Control.SprintKept>().Event<Control.SupernaturalSpeed>();     // the pace's, for the log
        b.Event<EdgeStopped>().Event<ClimbedDown>().Event<NoStandLanded>();   // the edge guard's stops and climbs down, for the log
        b.Event<PlayerStopped>();                                             // what stopped the player, for the log
        b.Event<AirCommitted>();                                              // the commit point in the air, for the log
        Dodges.Register(b);                                                   // the dodge (Bodies/Dodge.cs)
        b.Service(level).Service(new TickInput()).Service(cameras ?? new CameraSettings()).Service(new ViewState()).Service(new ReticleState());
        b.System(Phase.Input, ControlOrder, "platform.control", new PlayerControlSystem());
        b.System(Phase.Think, RoutesOrder, "platform.routes", new RouteSystem());
        b.System(Phase.Move, BodiesOrder, "platform.bodies", new BodySystem());
        b.System(Phase.Post, CameraOrder, "platform.camera", new CameraSystem());
        b.System(Phase.Post, ReticleOrder, "platform.reticle", new ReticleSystem());
        return WorldEdges.Register(b);
    }

    /// <summary>One tick of a world with the movement registered: this tick's input set (<see cref="TickInput.Set"/>), then the world stepped.</summary>
    public static void Step(World world, in ActionFrame frame)
    {
        world.Service<TickInput>().Set(frame);
        world.Step();
    }

    /// <summary>A body standing at <paramref name="feet"/> facing <paramref name="yaw"/> with <paramref name="numbers"/>, making no move yet (it settles onto the ground on its first ticks).</summary>
    public static EntityId SpawnBody(World world, Vector3 feet, float yaw, in Locomotion numbers)
    {
        var e = world.Create();
        world.Set(e, Body.At(feet, yaw));
        world.Set(e, numbers);
        world.Set(e, new MoveIntent { Facing = yaw, Faces = true, Gait = Gait.Run });
        world.Set(e, new EyeEase());
        world.Set(e, new Traversal());
        return e;
    }

    /// <summary>The body the player drives: a body with the <see cref="Controlled"/> tag and a <see cref="CameraRig"/> looking its way, and a <see cref="StartStopSpring"/> in a world whose player starts and stops on the spring.</summary>
    public static EntityId SpawnControlled(World world, Vector3 feet, float yaw, in Locomotion numbers)
    {
        var e = SpawnBody(world, feet, yaw, numbers);
        world.Set(e, new Controlled());
        world.Set(e, CameraRig.Facing(yaw));
        // The player starts and stops on the spring in a world that has it (StartStopSprings.RegisterPlayer).
        if (world.TryStore<StartStopSpring>(out var springs)) springs.Set(e, new StartStopSpring());
        return e;
    }

    /// <summary>A body that wanders the level's routes on its own.</summary>
    public static EntityId SpawnWanderer(World world, Vector3 feet, float yaw, in Locomotion numbers, float radius = 25f)
    {
        var e = SpawnBody(world, feet, yaw, numbers);
        world.Set(e, RouteWalker.Wanderer(radius));
        return e;
    }
}
