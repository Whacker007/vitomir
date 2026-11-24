// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Perception;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The automatic guard turns only toward a threat its owner can see (the rules package's <i>"constrained
/// by visible/known threat, player guard intent, facing, finite turn and authentic geometry. It must not become an omniscient
/// all-angle successful Block"</i>): within its kind's sight cone and past cover, by perception's own test, as well as within the
/// guard's arc. A blow from a foe it cannot see meets an unturned guard; with <see cref="GuardSettings.SightRequired"/> off, the earlier build's
/// guard (any threat in the arc). Skips without the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class GuardSightTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);
    static readonly Lazy<OneOneCharacter?> Christof = new(() => Library.Value?.Character("Christof", "BroadSword"));

    static int Ticks(float seconds) => (int)MathF.Round(seconds * World.TicksPerSecond);

    /// <summary>
    /// A world with the guard (its sight <paramref name="sight"/>), perception with an ordinary kind (60° either side, the
    /// world's default kind) and a wide-eyed one (80°), and a wall when <paramref name="wall"/> stands between the two.
    /// </summary>
    static (World W, int Kind) Make(bool sight, bool perception = true, Vector3? wallFrom = null, Vector3? wallTo = null)
    {
        Assert.SkipUnless(Christof.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var assets = new CharacterAssets();
        int kind = OneOneLibrary.Register(assets, Christof.Value!);
        var level = new TestLevel().Floor(-20, -20, 20, 20);
        if (wallFrom is { } a && wallTo is { } z) level = level.Box(a, z);
        var b = new WorldBuilder(7);
        MovementServices.Register(b, level.Build());
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        b.Peek.Service<GuardSettings>().SightRequired = sight;
        b.Service(new HurtRule { OnContact = false });
        b.Event<HarmResolved>();
        ContactServices.Register(b, new ContactConfig());
        if (perception) PerceptionServices.Register(b);
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        if (perception)
        {
            var config = w.Service<PerceptionConfig>();
            config.Add(new ObserverKind(AssetId.Of("test", "perception/ordinary"), "ordinary", [], ObserverNumbers.Ordinary, true));
            config.Add(new ObserverKind(AssetId.Of("test", "perception/wide"), "wide", [], ObserverNumbers.Ordinary with { ConeDegrees = 80f }, true));
            config.DefaultKind = "ordinary";
        }
        return (w, kind);
    }

    static EntityId Put(World w, int kind, float x, float y, float yawDegrees)
    {
        var e = MovementServices.SpawnBody(w, new(x, y, 0.01f), yawDegrees * DetMath.DegToRad, Locomotion.Sandbox);
        w.Set(e, CharacterAnimation.For(w.Service<CharacterAssets>(), kind));
        w.Set(e, MotionInput.Standing);
        CombatServices.Arm(w, e);
        return e;
    }

    /// <summary>The guard's turn (degrees) 0.25 s after a foe at <paramref name="bearing"/>° from its facing, 1.4 m off, begins a blow.</summary>
    float Turned(float bearing, bool sight, bool perception = true, int observerKind = -1, bool wall = false)
    {
        var (w, kind) = wall
            ? Make(sight, perception, new Vector3(-0.5f, 0.65f, 0f), new Vector3(0.5f, 0.75f, 2.6f))   // a wall across the line, head high and more
            : Make(sight, perception);
        var g = Put(w, kind, 0, 0, 90);
        if (observerKind >= 0) PerceptionServices.Observe(w, g, observerKind);
        float b = (90f + bearing) * DetMath.DegToRad;
        var (s, c) = DetMath.SinCos(b);
        var a = Put(w, kind, c * 1.4f, s * 1.4f, 90f + bearing + 180f);
        w.Get<CombatIntent>(g).Block = true;
        for (int i = 0; i < 40; i++) MovementServices.Step(w, ActionFrame.Idle);
        Assert.Equal(0f, w.Get<CharacterAnimation>(g).GuardTwist);
        ref var intent = ref w.Get<CombatIntent>(a);
        intent.Aims = true;
        intent.Attack = true;
        MovementServices.Step(w, ActionFrame.Idle);
        w.Get<CombatIntent>(a).Attack = false;
        for (int i = 0; i < 1 + Ticks(0.25f); i++) MovementServices.Step(w, ActionFrame.Idle);
        Assert.True(ActionRoles.IsAttack(w.Get<CharacterAnimation>(a).Action), "the foe's blow plays");
        return w.Get<CharacterAnimation>(g).GuardTwist * DetMath.RadToDeg;
    }

    [Fact]
    public void The_guard_turns_toward_a_foe_it_sees_and_not_toward_one_in_its_arc_but_outside_its_sight_cone()
    {
        float seen = Turned(40f, sight: true);                      // within the arc (70°) and the ordinary cone (60°)
        float outside = Turned(65f, sight: true);                   // within the arc, outside the cone: not seen
        float outsideOff = Turned(65f, sight: false);               // The earlier build's guard: any threat in its arc
        float wide = Turned(65f, sight: true, observerKind: 1);     // an observer of a wider cone (80°) sees it
        float noPerception = Turned(65f, sight: true, perception: false);   // a world without perception: the earlier build's
        output.WriteLine($"twist (deg): seen at 40 {seen:0.0}; at 65 outside the cone {outside:0.0}, sight off {outsideOff:0.0}, a wide-eyed observer {wide:0.0}, no perception {noPerception:0.0}");
        Assert.True(seen > 10f, $"seen {seen}");
        Assert.Equal(0f, outside);
        Assert.True(outsideOff > 10f, $"sight off {outsideOff}");
        Assert.True(wide > 10f, $"wide {wide}");
        Assert.True(noPerception > 10f, $"no perception {noPerception}");
    }

    [Fact]
    public void The_guard_does_not_turn_toward_a_foe_behind_a_wall_it_cannot_see_past()
    {
        float walled = Turned(0f, sight: true, wall: true);
        float walledOff = Turned(10f, sight: false, wall: true);    // the line a little aside, so the sight-off guard has a turn to make
        float walledOn = Turned(10f, sight: true, wall: true);
        output.WriteLine($"twist (deg) behind a wall: straight on {walled:0.0}; at 10, sight off {walledOff:0.0}, on {walledOn:0.0}");
        Assert.Equal(0f, walledOn);
        Assert.True(MathF.Abs(walledOff) > 1f, $"sight off {walledOff}");
    }

    // ---- the player's guard sees by the player's view ----

    /// <summary>
    /// The player's guard's turn (degrees) 0.25 s after a foe at <paramref name="bearing"/>° from its body's facing, 1.4 m off,
    /// begins a blow: the guard is the controlled body (a <see cref="Controlled"/> tag and a <see cref="CameraRig"/> in
    /// <paramref name="view"/>, pitched <paramref name="pitchDegrees"/>), block held by the frame as the player holds it.
    /// </summary>
    float PlayerTurned(float bearing, ViewKind view, float pitchDegrees = 0f, bool byView = true, bool perception = true)
    {
        var (w, kind) = Make(sight: true, perception);
        w.Service<GuardSettings>().PlayerSightByView = byView;
        var g = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 90f * DetMath.DegToRad, Locomotion.Sandbox);
        w.Set(g, CharacterAnimation.For(w.Service<CharacterAssets>(), kind));
        w.Set(g, MotionInput.Standing);
        CombatServices.Arm(w, g);
        ref var rig = ref w.Get<CameraRig>(g);
        rig.View = view;
        rig.Pitch = pitchDegrees * DetMath.DegToRad;
        float b = (90f + bearing) * DetMath.DegToRad;
        var (s, c) = DetMath.SinCos(b);
        var a = Put(w, kind, c * 1.4f, s * 1.4f, 90f + bearing + 180f);
        var block = new ActionFrame(Vector2.Zero, Vector2.Zero, Buttons.Block, Vector2.Zero);
        for (int i = 0; i < 40; i++) MovementServices.Step(w, block);
        Assert.True(w.Get<Combatant>(g).Blocking, "the player's block is raised");
        Assert.Equal(0f, w.Get<CharacterAnimation>(g).GuardTwist);
        ref var intent = ref w.Get<CombatIntent>(a);
        intent.Aims = true;
        intent.Attack = true;
        MovementServices.Step(w, block);
        w.Get<CombatIntent>(a).Attack = false;
        for (int i = 0; i < 1 + Ticks(0.25f); i++) MovementServices.Step(w, block);
        Assert.True(ActionRoles.IsAttack(w.Get<CharacterAnimation>(a).Action), "the foe's blow plays");
        return w.Get<CharacterAnimation>(g).GuardTwist * DetMath.RadToDeg;
    }

    [Fact]
    public void The_players_guard_sees_by_the_players_view_not_the_bodys_facing()
    {
        float firstAt40 = PlayerTurned(40f, ViewKind.First);
        float firstAt65 = PlayerTurned(65f, ViewKind.First);                          // past the 55° cone round the look, from the eyes
        float thirdAt65 = PlayerTurned(65f, ViewKind.Third);                          // the camera stands 2.5 m behind: on the picture
        float downAt30 = PlayerTurned(30f, ViewKind.First, pitchDegrees: -70f);       // looking at the ground
        float downAt30Body = PlayerTurned(30f, ViewKind.First, pitchDegrees: -70f, byView: false);   // guard-sight-body: the body's 60° cone
        float thirdAt65Body = PlayerTurned(65f, ViewKind.Third, byView: false);
        output.WriteLine($"the player's twist (deg): first person at 40 {firstAt40:0.0}, at 65 {firstAt65:0.0}; third person at 65 {thirdAt65:0.0}; " +
                         $"first person looking down at 30 {downAt30:0.0}, by the body {downAt30Body:0.0}; third person at 65 by the body {thirdAt65Body:0.0}");
        Assert.True(firstAt40 > 10f, $"first at 40: {firstAt40}");
        Assert.Equal(0f, firstAt65);
        Assert.True(thirdAt65 > 10f, $"third at 65: {thirdAt65}");
        Assert.Equal(0f, downAt30);
        Assert.True(downAt30Body > 10f, $"by the body: {downAt30Body}");
        Assert.Equal(0f, thirdAt65Body);                                                // the body's default kind: a 60° cone
        // The view's rule needs no perception (a world without it still hides what is off the picture).
        Assert.Equal(0f, PlayerTurned(65f, ViewKind.First, perception: false));
    }

    [Fact]
    public void The_players_view_rule_is_the_same_on_a_second_run_tick_for_tick()
    {
        // Deterministic: the same set-up run twice turns the guard the same, digest for digest.
        ulong Run()
        {
            var (w, kind) = Make(sight: true);
            var g = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 90f * DetMath.DegToRad, Locomotion.Sandbox);
            w.Set(g, CharacterAnimation.For(w.Service<CharacterAssets>(), kind));
            w.Set(g, MotionInput.Standing);
            CombatServices.Arm(w, g);
            var a = Put(w, kind, -0.9f, 1.07f, -50f);
            var block = new ActionFrame(Vector2.Zero, new Vector2(0.004f, 0f), Buttons.Block, Vector2.Zero);   // the look turning as it guards
            for (int i = 0; i < 40; i++) MovementServices.Step(w, block);
            w.Get<CombatIntent>(a).Aims = true;
            w.Get<CombatIntent>(a).Attack = true;
            MovementServices.Step(w, block);
            w.Get<CombatIntent>(a).Attack = false;
            for (int i = 0; i < Ticks(1f); i++) MovementServices.Step(w, block);
            return w.Digest();
        }
        Assert.Equal(Run(), Run());
    }
}
