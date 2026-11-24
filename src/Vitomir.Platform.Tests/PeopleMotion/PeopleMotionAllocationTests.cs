// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// That animating and posing the models repository's people allocates nothing per tick once going (as
/// <see cref="AllocationTests"/>, alone in their collection and measured more than once): people walking and running every way
/// round a circle, turning on the spot as the view turns, crouched; their poses, twist and hit volumes asked for every tick.
/// </summary>
[Collection(AllocationCollection.Name)]
public sealed class PeopleMotionAllocationTests
{
    const int Period = 600;

    static (World World, EntityId[] People) World(CharacterAssets assets, int kind)
    {
        var b = new WorldBuilder(3).Component<Body>().Component<Traversal>().Component<MoveIntent>().Service(PeopleWorlds.Floor()).Animation(assets)
                .Service(new DirectionPace()).HitVolumes();
        BodyMotionSystem.Register(b);
        var world = b.Build();
        var people = new EntityId[4];
        for (int i = 0; i < people.Length; i++)
        {
            var e = people[i] = world.Create();
            world.Set(e, Body.At(new Vector3(i * 3f, 0, 0), DetMath.HalfPi) with { State = BodyState.Ground });
            world.Set(e, new Traversal());
            world.Set(e, CharacterAnimation.For(assets, kind));
            world.Set(e, MotionInput.Standing);
        }
        world.Service<HitVolumes>().Prepare(assets);
        return (world, people);
    }

    /// <summary>One tick of the script: a walker and a runner going every way round (their facing fixed), a stander whose view turns, a crouched walker.</summary>
    static void Tick(World world, EntityId[] people, int t)
    {
        float round = t * DetMath.TwoPi / Period;
        for (int i = 0; i < people.Length; i++)
        {
            ref var body = ref world.Get<Body>(people[i]);
            var way = new Vector3(DetMath.Cos(round), DetMath.Sin(round), 0f);
            (body.Velocity, body.Yaw, body.Crouched) = i switch
            {
                0 => (way * 1.1f, DetMath.HalfPi, 0),
                1 => (way * 2.8f, DetMath.HalfPi, 0),
                2 => (Vector3.Zero, DetMath.HalfPi + DetMath.Sin(round * 2f) * 2f, 0),
                _ => (DetMath.Sin(round * 2f) > 0f ? way * 0.7f : Vector3.Zero, DetMath.HalfPi, 1),   // crouched, walking and still by turns
            };
            body.Feet += body.Velocity * PeopleWorlds.Tick;
        }
        world.Step();
        var poses = world.Service<CharacterPoses>();
        var volumes = world.Service<HitVolumes>();
        foreach (var e in people)
        {
            _ = poses.Skin(world, e);
            _ = volumes.Posed(world, e);
        }
    }

    static void Measure(string what, Func<(CharacterAssets, int)> kind) =>
        Allocations.None(what, () =>
        {
            var (assets, k) = kind();
            var (world, people) = World(assets, k);
            var queues = Allocations.Warm(world);
            for (int t = 0; t < Period; t++) { foreach (var q in queues) q.TurnOver(); Tick(world, people, t); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 0; t < Period; t++) { foreach (var q in queues) q.TurnOver(); Tick(world, people, t); }
            long spent = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Contains(world.Get<CharacterAnimation>(people[2]).Role, new[] { MotionRole.Idle, MotionRole.Turn });
            return spent;
        });

    [Fact]
    public void Animating_and_posing_people_allocates_nothing_per_tick()
    {
        var made = PeopleDoubles.Kind();
        Measure("600 ticks of four made-up people", () => (made.Assets, made.Kind));
    }

    [Fact]
    public void Animating_and_posing_people_with_round_30s_crouch_allocates_nothing_per_tick()
    {
        var made = PeopleDoubles.Kind(crouch: true);
        Measure("600 ticks of four made-up people with the crouch's clips", () => (made.Assets, made.Kind));
    }

    [Fact]
    public void Animating_and_posing_the_player_allocates_nothing_per_tick()
    {
        var (assets, kind, _) = PeopleWorlds.Player();
        Measure("600 ticks of four players", () => (assets, kind));
    }
}
