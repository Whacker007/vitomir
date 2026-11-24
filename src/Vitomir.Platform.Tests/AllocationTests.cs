// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Reflection;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>The collection the allocation tests run in: alone, after every other test, never beside one.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AllocationCollection
{
    public const string Name = "allocations";
}

/// <summary>
/// Measuring that a tick allocates nothing, for <see cref="AllocationTests"/>.
///
/// <para><b>Why so</b>: <c>A_tick_of_bodies_making_moves_allocates_nothing</c> failed once on a Windows PC (1,360 bytes
/// in 600 ticks, <c>dotnet test</c>, Debug, 16 threads) and passed alone and on every rerun. What a thread allocates
/// is counted for the thread, and on it the runtime makes one-offs of its own now and then (a method tiered up, a type
/// first loaded, a test beside it on the same thread between its awaits); the code under test allocating per tick or
/// per event allocates in every window it runs. So the tests run in a collection of their own with nothing beside
/// them (<see cref="AllocationCollection"/>), each window is measured more than once (<see cref="None"/>: a fresh world
/// run the same way, or the same world's next window), and a test fails only when <b>every</b> measure allocated — a
/// real allocation shows in all of them, a one-off in one. And every event queue the world registers is given room
/// first and turned over each tick (<see cref="Queues"/>, <see cref="Warm"/>), not a hand-kept list: a queue left off
/// such a list (the earlier build's <c>EdgeStopped</c>, <c>ClimbedDown</c>, <c>NoStandLanded</c>, <c>SprintKept</c>,
/// <c>SupernaturalSpeed</c>, the earlier build's <c>PlayerStopped</c>) grows its list the first time its event fires in the window,
/// once, and is never emptied.</para>
/// </summary>
internal static class Allocations
{
    /// <summary>How many times a window is measured.</summary>
    public const int Measures = 2;

    /// <summary>How many events of a kind each queue's lists are given room for before measuring.</summary>
    public const int Room = 64;

    static readonly FieldInfo QueuesField = typeof(World).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(f => f.FieldType == typeof(List<IEventQueue>));

    /// <summary>Every event queue <paramref name="world"/> registers, in its order.</summary>
    public static IEventQueue[] Queues(World world) => [.. (List<IEventQueue>)QueuesField.GetValue(world)!];

    /// <summary>
    /// Gives both lists of every event queue of <paramref name="world"/> room for <see cref="Room"/> events (a list's first
    /// growths, once a world) and leaves them empty.
    /// </summary>
    public static IEventQueue[] Warm(World world)
    {
        var queues = Queues(world);
        foreach (var q in queues)
        {
            var publish = q.GetType().GetMethod(nameof(EventQueue<int>.Publish))!;
            object[] one = [Activator.CreateInstance(q.GetType().GetGenericArguments()[0])!];
            for (int turn = 0; turn < 2; turn++)
            {
                for (int i = 0; i < Room; i++) publish.Invoke(q, one);
                q.TurnOver();
            }
            q.TurnOver();
        }
        return queues;
    }

    /// <summary>
    /// Measures <paramref name="window"/> (which runs a window and returns the bytes the thread allocated in it) up to
    /// <see cref="Measures"/> times, and fails only when every measure allocated.
    /// </summary>
    public static void None(string what, Func<long> window)
    {
        var spent = new long[Measures];
        for (int i = 0; i < Measures; i++)
        {
            spent[i] = window();
            if (spent[i] == 0) return;
        }
        Assert.Fail($"{what}: allocated in every measure ({string.Join(", ", spent)} bytes)");
    }
}

/// <summary>
/// That the platform's ticks allocate nothing once going (gathered here from the classes that had them, run
/// alone and measured more than once, <see cref="Allocations"/>): bodies making moves on a course; bodies, control,
/// cameras and reticle under random play; the exits and the safety net; animating and posing characters; and placing
/// the limbs.
/// </summary>
[Collection(AllocationCollection.Name)]
public sealed class AllocationTests
{
    [Fact]
    public void A_tick_of_bodies_making_moves_allocates_nothing()
    {
        // From TraversalTests: a fresh course each measure, the same script.
        Allocations.None("600 ticks of the course", () =>
        {
            var w = TraversalTests.CourseWorld(out _);
            var input = w.Service<TickInput>();
            for (int t = 0; t < 100; t++) { input.Set(TraversalTests.Script(t)); w.Step(); }
            ISystem[] systems = [new PlayerControlSystem(), new BodySystem(), new CameraSystem(), new ReticleSystem()];
            var queues = Allocations.Warm(w);
            foreach (var s in systems) s.Run(w);
            int moving = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 100; t < TraversalTests.CourseTicks; t++)
            {
                input.Set(TraversalTests.Script(t));
                foreach (var q in queues) q.TurnOver();
                foreach (var s in systems) s.Run(w);
                foreach (ref readonly var m in w.Store<Traversal>().Data) if (m.Kind != TraversalKind.None) moving++;
            }
            long spent = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(moving > 50, $"only {moving} ticks of moves");
            return spent;
        });
    }

    [Fact]
    public void A_tick_of_bodies_control_cameras_and_reticle_allocates_nothing()
    {
        // From BodyMotorTests: random play on the playground, a fresh world each measure.
        Allocations.None("600 ticks of random play", () =>
        {
            var w = BodyMotorTests.MakeWorld(BodyMotorTests.Playground(), 3, out _);
            var input = w.Service<TickInput>();
            var rng = new Rng(4);
            var frame = ActionFrame.Idle;
            for (int t = 0; t < 600; t++) { frame = BodyMotorTests.RandomFrame(rng, frame, t); input.Set(frame); w.Step(); }
            // The movement's systems themselves (the schedule's own stepping is the core's).
            ISystem[] systems = [new PlayerControlSystem(), new BodySystem(), new CameraSystem(), new ReticleSystem()];
            var queues = Allocations.Warm(w);
            foreach (var s in systems) s.Run(w);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 600; t < 1200; t++)
            {
                frame = BodyMotorTests.RandomFrame(rng, frame, t);
                input.Set(frame);
                foreach (var q in queues) q.TurnOver();
                foreach (var s in systems) s.Run(w);
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });
    }

    [Fact]
    public void A_tick_of_the_exits_and_the_safety_net_allocates_nothing()
    {
        // From WorldEdgesTests: a townsman walking off the floor's end at x = 20, again and again; a fresh world each measure.
        Allocations.None("900 ticks of the exits and the safety net", () =>
        {
            var (w, player) = WorldEdgesTests.Play(WorldEdgesTests.DoorLevel(), new Vector3(0, 0.5f, 0.01f));
            var faller = MovementServices.SpawnBody(w, new Vector3(18, -5, 0.01f), 0f, Locomotion.Sandbox);
            var run = new ActionFrame(new Vector2(0, 1), Vector2.Zero, Buttons.None, default);
            var queues = Allocations.Warm(w);
            for (int t = 0; t < 240; t++) { w.Get<MoveIntent>(faller).Direction = new Vector2(1, 0); MovementServices.Step(w, run); }
            ISystem[] systems = [new PlayerControlSystem(), new BodySystem(), new ExitThresholdSystem(), new FallRecoverySystem()];
            foreach (var s in systems) s.Run(w);
            int recovered = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 0; t < 900; t++)
            {
                w.Service<TickInput>().Set(run);
                w.Get<MoveIntent>(faller).Direction = new Vector2(1, 0);
                foreach (var q in queues) q.TurnOver();
                foreach (var s in systems) s.Run(w);
                recovered += w.Events<BodyRecovered>().ThisTick.Count;
            }
            long spent = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(recovered >= 1, "the townsman never fell out");
            Assert.InRange(w.Get<Body>(player).Feet.X, 4.65f, 4.701f);
            return spent;
        });
    }

    [Fact]
    public void Animating_and_posing_characters_allocates_nothing_once_they_are_going()
    {
        // From AnimationTests: the same world's next hundred ticks each measure.
        var (assets, kind) = AnimationTests.TestKind();
        var system = new AnimationSystem(assets);
        var world = new WorldBuilder(5).Animation(assets).Build();
        var poses = world.Service<CharacterPoses>();
        var entities = new EntityId[50];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = world.Create();
            world.Set(entities[i], CharacterAnimation.For(assets, kind));
            world.Set(entities[i], new MotionInput { GroundSpeed = i * 0.1f, Grounded = true });
        }
        var into = new Matrix4x4[2];
        void Tick()
        {
            system.Run(world);
            foreach (var e in entities) poses.CopySkin(world, e, into, firstPerson: true);
        }
        for (int i = 0; i < 10; i++) Tick();
        Allocations.None("100 ticks of animation", () =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Tick();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });
    }

    [Fact]
    public void Placing_the_limbs_allocates_nothing_per_tick_once_going()
    {
        // From TraversalPoseTests: the same world's next hundred ticks each measure.
        var (assets, kind) = TraversalPoseTests.PersonKind();
        var level = new TestLevel().Floor(-4, -4, 0, 40).Box(new Vector3(0, -4, 0), new Vector3(4, 40, 0.22f)).Build("step");
        var b = new WorldBuilder(5).Component<Body>().Component<Traversal>().Service(level).Animation(assets);
        BodyMotionSystem.Register(b);
        var world = b.Build();
        var people = new EntityId[20];
        for (int i = 0; i < people.Length; i++)
        {
            var e = people[i] = world.Create();
            world.Set(e, Body.At(new Vector3(0.05f, i, 0.22f), DetMath.HalfPi) with { State = BodyState.Ground, Velocity = new Vector3(0, i * 0.1f, 0) });
            world.Set(e, i % 3 == 0 ? new Traversal { Kind = TraversalKind.Hang, Lip = new(0, i + 0.3f, 2.5f), LipNormal = new(0, -1, 0) } : new Traversal());
            world.Set(e, CharacterAnimation.For(assets, kind));
            world.Set(e, MotionInput.Standing);
        }
        var poses = world.Service<CharacterPoses>();
        var into = new Matrix4x4[assets.MaxBones];
        void Tick()
        {
            world.Step();
            foreach (var e in people) poses.CopySkin(world, e, into);
        }
        Allocations.Warm(world);
        for (int i = 0; i < 30; i++) Tick();
        Allocations.None("100 ticks of placing the limbs", () =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Tick();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });
    }
}
