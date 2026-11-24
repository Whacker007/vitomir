// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Navigation;
using Vitomir.Platform.Perception;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// What a death does to a body (<see cref="DeathSystem"/>, <see cref="DeathHoldSystem"/>): the player's keys move it
/// nowhere, a wanderer stops, a witness sees and walks no more, it plays its death and refuses its attacks, and it stays
/// where it fell, a body the hit volumes still find. The deaths here are synthetic <see cref="ActorDied"/> records (only
/// tests make them; in the game the rules publish one when a person's Health reaches nothing).
/// </summary>
public sealed class DeathTests
{
    static ActorDied Died(EntityId who, long tick, HarmKind kind = HarmKind.Lethal) => new(who, tick, 1, EntityId.None, kind);

    [Fact]
    public void A_dead_body_is_held_where_it_fell_and_neither_walks_a_route_nor_sees()
    {
        var level = new LevelSpace("floor", new TestLevel().Floor(-40, -40, 40, 40).Build("floor").Collision) { Places = LevelPlaces.None };
        var r = PerceptionTests.Make(level, new Vector3(0, 0, 0.01f), build: b => DeathServices.Register(b));
        var w = r.W;
        var witness = PerceptionTests.Witness(r, "guard", 6, 0, 180);
        var wanderer = MovementServices.SpawnWanderer(w, new Vector3(-6, 3, 0.01f), 0f, Locomotion.Sandbox, radius: 15f);
        var forward = new ActionFrame(new Vector2(0, 1), Vector2.Zero, Buttons.None, default);
        PerceptionTests.Run(r, 240, forward);                         // two seconds: the player runs (the floor has no navigation: the wanderer waits)
        Assert.True(w.Has<RouteWalker>(wanderer) && w.Has<Observer>(witness));
        Assert.True(w.Get<Body>(r.Player).Feet.Length() > 3f, $"the player ran to {w.Get<Body>(r.Player).Feet}");

        foreach (var e in new[] { r.Player, witness, wanderer }) w.Events<ActorDied>().Publish(Died(e, w.Tick));
        PerceptionTests.Run(r, 60, forward);                          // half a second to come to a stop
        var player = w.Get<Body>(r.Player).Feet;
        var walker = w.Get<Body>(wanderer).Feet;
        PerceptionTests.Run(r, 360, forward);                         // three more seconds pressing forward
        Assert.True(Vector3.Distance(player, w.Get<Body>(r.Player).Feet) < 1e-3f, $"the dead player moved from {player} to {w.Get<Body>(r.Player).Feet}");
        Assert.True(Vector3.Distance(walker, w.Get<Body>(wanderer).Feet) < 1e-3f, "the dead wanderer moved");
        Assert.Equal(default, w.Get<MoveIntent>(r.Player).Direction);
        foreach (var e in new[] { r.Player, witness, wanderer })
        {
            Assert.True(w.Has<Dead>(e));
            Assert.True(w.Alive(e));
            Assert.True(w.Has<Body>(e));                               // still a body
            Assert.False(w.Has<RouteWalker>(e));                       // no routes
            Assert.False(w.Has<Observer>(e));                          // no perception
        }

        // A second death of the same person changes nothing.
        long first = w.Get<Dead>(witness).Tick;
        w.Events<ActorDied>().Publish(Died(witness, w.Tick));
        PerceptionTests.Run(r, 2, ActionFrame.Idle);
        Assert.Equal(first, w.Get<Dead>(witness).Tick);
    }

    // ---- with 1.1's characters ----

    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    [Fact]
    public void The_dead_play_their_death_refuse_their_attacks_and_the_volumes_still_find_them_where_they_fell()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var lib = Library.Value!;
        var assets = new CharacterAssets();
        int christofKind = OneOneLibrary.Register(assets, lib.Character("Christof", "BroadSword")!);
        int guardKind = OneOneLibrary.Register(assets, lib.Character("rudolfGuard")!);
        var level = new TestLevel().Floor(-60, -60, 60, 60).Build();
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b);
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        DeathServices.Register(b);
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        var christof = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), DetMath.HalfPi, Locomotion.Sandbox);
        w.Set(christof, CharacterAnimation.For(assets, christofKind));
        w.Set(christof, MotionInput.Standing);
        w.Set(christof, new MasqueradeState { Mode = MasqueradeMode.Unbound });
        CombatServices.Arm(w, christof);
        var guard = MovementServices.SpawnBody(w, new(0, 1.8f, 0.01f), -DetMath.HalfPi, Locomotion.Sandbox);
        w.Set(guard, CharacterAnimation.For(assets, guardKind));
        w.Set(guard, MotionInput.Standing);
        CombatServices.Arm(w, guard);
        for (int i = 0; i < 30; i++) MovementServices.Step(w, ActionFrame.Idle);
        var standing = w.Service<HitVolumes>().Posed(w, guard).ToArray();
        float standingTop = standing.Max(v => MathF.Max(v.Capsule.A.Z, v.Capsule.B.Z));

        // The guard is beaten to death (bashing: the slow death), Christof cut down (lethal: the quick one).
        w.Events<ActorDied>().Publish(Died(guard, w.Tick, HarmKind.Bashing));
        w.Events<ActorDied>().Publish(Died(christof, w.Tick, HarmKind.Lethal));
        var guardFeet = w.Get<Body>(guard).Feet;
        var refused = new List<AttackRefused>();
        var press = new ActionFrame(Vector2.Zero, Vector2.Zero, Buttons.Attack, default);
        for (int i = 0; i < 4 * World.TicksPerSecond; i++)
        {
            MovementServices.Step(w, i % 60 < 5 ? press : ActionFrame.Idle);       // Christof presses attack now and then
            refused.AddRange(w.Events<AttackRefused>().ThisTick);
        }
        Assert.Equal(ActionRole.DeathSlow, w.Get<CharacterAnimation>(guard).Action);
        Assert.Equal(ActionRole.DeathQuick, w.Get<CharacterAnimation>(christof).Action);
        Assert.Equal("christofDeath", assets.Clip(w.Get<CharacterAnimation>(guard).ActionClip).Name);
        Assert.NotEmpty(refused);
        Assert.All(refused, x => Assert.Equal((christof, AttackRefusal.Dead), (x.Entity, x.Why)));
        Assert.Empty(w.Events<AttackBegun>().ThisTick);

        // Where it fell, and the volumes on its fallen pose: lower than standing, and a ray down onto it finds it.
        Assert.True(Vector3.Distance(guardFeet, w.Get<Body>(guard).Feet) < 0.05f);
        var fallen = w.Service<HitVolumes>().Posed(w, guard).ToArray();
        Assert.Equal(standing.Length, fallen.Length);
        float fallenTop = fallen.Max(v => MathF.Max(v.Capsule.A.Z, v.Capsule.B.Z));
        Assert.True(fallenTop < standingTop - 0.6f, $"the dead guard's volumes reach {fallenTop:0.00} m, standing {standingTop:0.00} m");
        var chest = fallen.First(v => v.Region == BodyRegion.Chest).Capsule;
        var over = (chest.A + chest.B) / 2 + new Vector3(0, 0, 2f);
        Assert.True(w.Service<HitVolumes>().Ray(w, guard, over, -Vector3.UnitZ, 3f, out var hit));
        Assert.NotEqual(BodyRegion.None, hit.Region);
    }

    [Fact]
    public void With_rules_a_blow_flinches_only_when_it_harms_and_not_again_within_the_wait()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var lib = Library.Value!;
        var assets = new CharacterAssets();
        int guardKind = OneOneLibrary.Register(assets, lib.Character("rudolfGuard")!);
        var level = new TestLevel().Floor(-60, -60, 60, 60).Build();
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b);
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        b.Service(new HurtRule { OnContact = false });                                     // as a rules module sets it
        DeathServices.Register(b);
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        MovementServices.SpawnControlled(w, new(-10, 0, 0.01f), 0f, Locomotion.Sandbox);
        var guard = MovementServices.SpawnBody(w, new(0, 1.8f, 0.01f), -DetMath.HalfPi, Locomotion.Sandbox);
        w.Set(guard, CharacterAnimation.For(assets, guardKind));
        w.Set(guard, MotionInput.Standing);
        CombatServices.Arm(w, guard);
        for (int i = 0; i < 30; i++) MovementServices.Step(w, ActionFrame.Idle);

        bool Blow(int wounds, int healthAfter = 5)
        {
            w.Events<HarmResolved>().Publish(new HarmResolved(1, EntityId.None, guard, w.Tick, HarmKind.Lethal, 4, 1, 0, 2, 2, wounds, 7, healthAfter));
            bool hurt = false;
            for (int i = 0; i < 6; i++)
            {
                MovementServices.Step(w, ActionFrame.Idle);
                hurt |= w.Get<CharacterAnimation>(guard).Action == ActionRole.Hurt;
            }
            return hurt;
        }
        void Wait(float seconds) { for (int i = 0; i < seconds * World.TicksPerSecond; i++) MovementServices.Step(w, ActionFrame.Idle); }

        Assert.False(Blow(0));                                                             // soaked to nothing: no flinch
        Assert.False(w.Has<Flinch>(guard));
        Assert.True(Blow(1));                                                              // harm: a flinch
        Wait(2f);
        Assert.False(Blow(2));                                                             // within 3.5 s of the last: none
        Wait(2f);
        Assert.True(Blow(1));                                                              // 4 s on: one more
        Wait(4f);
        Assert.False(Blow(3, healthAfter: 0));                                             // the blow that kills: the death, not a flinch
    }
}
