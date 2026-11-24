// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The freed pointer: what is under it — a ray from the camera through its place on the picture, found where the
/// renderer draws the thing, and the same in a replay —, the verb there (talk within 1.1's talking distance and in
/// sight, none otherwise; target or refused while a target is asked for), the click acting only where a verb applies,
/// the cursor each verb shows, the tape keeping the picture's shape, and the click's key shared with attack.
/// </summary>
public sealed class PointerTests
{
    static World MakeWorld(LevelSpace level, out EntityId player, ViewKind view = ViewKind.First)
    {
        var b = new WorldBuilder(1);
        MovementServices.Register(b, level);
        PointerServices.Register(b);
        var w = b.Build();
        player = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        w.Get<CameraRig>(player).View = view;
        return w;
    }

    static void Settle(World w, int ticks = 30)
    {
        for (int i = 0; i < ticks; i++) MovementServices.Step(w, ActionFrame.Idle);
    }

    /// <summary>Where the renderer draws <paramref name="p"/> on a picture of <paramref name="aspect"/> (0 to 1 across and down): its look-at with +Z up and its perspective.</summary>
    static Vector2 Project(ViewPoint v, Vector3 p, float aspect)
    {
        var f = DetMath.Direction(v.Yaw, v.Pitch);
        var view = Matrix4x4.CreateLookAt(v.Eye, v.Eye + f, Vector3.UnitZ);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(v.FieldOfViewDegrees * MathF.PI / 180f, aspect, 0.05f, 1000f);
        var c = Vector4.Transform(new Vector4(p, 1f), view * proj);
        return new Vector2((c.X / c.W + 1f) * 0.5f, (1f - c.Y / c.W) * 0.5f);
    }

    static ActionFrame Free(Vector2 place, Buttons more = Buttons.None, float aspect = 21f / 9f) =>
        new(Vector2.Zero, Vector2.Zero, Buttons.FreePointer | more, place, aspect);

    [Fact]
    public void A_body_where_the_picture_shows_it_is_picked_and_the_level_behind_otherwise()
    {
        var level = new TestLevel().Floor(-10, -10, 20, 10).Box(new(12, -10, 0), new(12.5f, 10, 4)).Build();
        var w = MakeWorld(level, out var player);
        var other = MovementServices.SpawnBody(w, new(5, 2, 0.01f), 0f, Locomotion.Sandbox);
        Settle(w);
        var view = w.Service<ViewState>().View;
        var state = w.Service<PointerState>();

        // On a wide picture (21:9), where the renderer draws the person's middle.
        MovementServices.Step(w, Free(Project(view, w.Get<Body>(other).Feet + new Vector3(0, 0, 1f), 21f / 9f)));
        Assert.True(state.Free);
        Assert.Equal(ReticleTarget.Person, state.Under.Kind);
        Assert.Equal(other.Packed, state.Under.Entity);

        // The same place on a 4:3 picture is elsewhere in the world: the shape matters.
        MovementServices.Step(w, Free(Project(view, w.Get<Body>(other).Feet + new Vector3(0, 0, 1f), 21f / 9f), aspect: 4f / 3f));
        Assert.NotEqual(ReticleTarget.Person, state.Under.Kind);

        // Beside the person, the wall behind; above the wall, the sky; off the picture, nothing.
        MovementServices.Step(w, Free(Project(view, new Vector3(12f, -3f, 1.5f), 21f / 9f)));
        Assert.Equal(ReticleTarget.Level, state.Under.Kind);
        Assert.InRange(state.Under.Point.X, 11.9f, 12.1f);
        Assert.InRange(state.Under.Point.Y, -3.1f, -2.9f);
        MovementServices.Step(w, Free(new Vector2(0.5f, 0.02f)));
        Assert.Equal(ReticleTarget.Nothing, state.Under.Kind);
        MovementServices.Step(w, Free(new Vector2(-0.1f, 0.5f)));
        Assert.Equal(ReticleTarget.Nothing, state.Under.Kind);

        // Captured again, the answer is the reticle's.
        MovementServices.Step(w, ActionFrame.Idle);
        Assert.False(state.Free);
        Assert.Equal(w.Service<ReticleState>().Answer, state.Under);
        _ = player;
    }

    /// <summary>A drive with the pointer freed over a person in talking distance: swept across the picture and clicked.</summary>
    static ActionFrame PointerDrive(int t)
    {
        if (t < 30) return ActionFrame.Idle;
        float across = ((t - 30) % 180) / 180f;
        var held = Buttons.FreePointer | ((t % 20) < 2 ? Buttons.PointerClick : Buttons.None);
        if (t >= 200 && t < 230) held = (t % 20) < 2 ? Buttons.Use : Buttons.None;     // a spell with the reticle
        return new ActionFrame(new Vector2(0, t > 300 ? 0.3f : 0f), Vector2.Zero, held, new Vector2(0.1f + 0.8f * across, 0.55f), 21f / 9f);
    }

    [Fact]
    public void A_recorded_freed_pointer_drive_replays_picking_the_same_things_at_every_tick()
    {
        var level = new TestLevel().Floor(-10, -10, 20, 10).Build();
        var a = MakeWorld(level, out var player);
        MovementServices.SpawnBody(a, new(1.5f, 0.3f, 0.01f), 0f, Locomotion.Sandbox);
        var tape = new Tape();
        var picks = new List<(ReticleTarget, ulong, PointerVerb)>();
        int talks = 0, changes = 0;
        for (int t = 0; t < 360; t++)
        {
            var f = PointerDrive(t);
            MovementServices.Step(a, f);
            tape.Frames.Add(f);
            tape.Digests[a.Tick] = a.Digest();
            var s = a.Service<PointerState>();
            picks.Add((s.Under.Kind, s.Under.Entity, s.Verb));
            talks += a.Events<TalkRequested>().ThisTick.Count;
            changes += a.Events<PointerVerbChanged>().ThisTick.Count;
        }
        Assert.Contains(picks, p => p.Item3 == PointerVerb.Talk);
        Assert.True(talks > 0, "the drive asked to talk");
        Assert.True(changes >= 2, "the verb changed as the pointer swept");

        var path = Path.Combine(Path.GetTempPath(), $"pointer-{Environment.ProcessId}.tape");
        try
        {
            tape.Write(path);
            var back = Tape.Read(path);
            Assert.Equal(TapeHeader.CurrentVersion, back.Header.Version);
            Assert.Equal(tape.Frames, back.Frames);       // the picture's shape comes back with every frame
            var b = MakeWorld(level, out _);
            MovementServices.SpawnBody(b, new(1.5f, 0.3f, 0.01f), 0f, Locomotion.Sandbox);
            for (int t = 0; t < back.Frames.Count; t++)
            {
                MovementServices.Step(b, back.Frames[t]);
                Assert.Equal(back.Digests[b.Tick], b.Digest());
                var s = b.Service<PointerState>();
                Assert.Equal(picks[t], (s.Under.Kind, s.Under.Entity, s.Verb));
            }
        }
        finally { File.Delete(path); }
        _ = player;
    }

    [Theory]
    [InlineData(1.5f, PointerVerb.Talk)]      // 1.5 m between centres: 0.9 m between their sides, within 1.1's 1.04
    [InlineData(2.0f, PointerVerb.None)]      // 1.4 m between their sides: too far to talk
    public void A_person_on_the_reticle_is_talked_to_within_one_one_s_distance_and_not_beyond(float x, PointerVerb verb)
    {
        var level = new TestLevel().Floor(-10, -10, 20, 10).Build();
        var w = MakeWorld(level, out var player);
        var other = MovementServices.SpawnBody(w, new(x, 0, 0.01f), 0f, Locomotion.Sandbox);
        Settle(w);
        var state = w.Service<PointerState>();
        Assert.Equal(ReticleTarget.Person, state.Under.Kind);
        Assert.Equal(other.Packed, state.Under.Entity);
        Assert.Equal(verb, state.Verb);
        Assert.InRange(state.Gap, x - 0.6f - 0.01f, x - 0.6f + 0.01f);
        Assert.Equal(75f / 72f, w.Service<PointerSettings>().TalkReach);
        _ = player;
    }

    [Fact]
    public void Nobody_talks_to_the_dead()
    {
        var level = new TestLevel().Floor(-10, -10, 20, 10).Build();
        var b = new WorldBuilder(1);
        MovementServices.Register(b, level);
        PointerServices.Register(b);
        DeathServices.Register(b);
        var w = b.Build();
        var player = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        w.Get<CameraRig>(player).View = ViewKind.First;
        var other = MovementServices.SpawnBody(w, new(1.5f, 0, 0.01f), 0f, Locomotion.Sandbox);
        Settle(w);
        Assert.Equal(PointerVerb.Talk, w.Service<PointerState>().Verb);
        w.Events<ActorDied>().Publish(new ActorDied(other, w.Tick, 1, player));
        Settle(w, 4);
        Assert.Equal(other.Packed, w.Service<PointerState>().Under.Entity);     // still a body the reticle finds
        Assert.Equal(PointerVerb.None, w.Service<PointerState>().Verb);
    }

    [Fact]
    public void Nothing_is_talked_to_behind_a_wall_out_of_the_eyes_sight_or_when_it_is_the_player()
    {
        var walled = new TestLevel().Floor(-10, -10, 20, 10).Box(new(0.8f, -1f, 0f), new(0.9f, 1f, 3f)).Build();
        var w = MakeWorld(walled, out var player);
        MovementServices.SpawnBody(w, new(1.5f, 0, 0.01f), 0f, Locomotion.Sandbox);
        Settle(w);
        var state = w.Service<PointerState>();
        Assert.Equal(ReticleTarget.Level, state.Under.Kind);     // the wall, not the person behind it
        Assert.Equal(PointerVerb.None, state.Verb);

        // Seen by the camera but not from the eyes (the reticle's blocked test): no talk.
        var open = MakeWorld(new TestLevel().Floor(-10, -10, 20, 10).Build(), out var me);
        var near = MovementServices.SpawnBody(open, new(1.5f, 0, 0.01f), 0f, Locomotion.Sandbox);
        Settle(open);
        var settings = open.Service<PointerSettings>();
        var seen = new ReticleAnswer(ReticleTarget.Person, near.Packed, new(1.2f, 0, 1.6f), 1.2f, null, null, false);
        Assert.Equal(PointerVerb.Talk, PointerVerbs.Of(open, me, seen, null, settings, out _, out _));
        Assert.Equal(PointerVerb.None, PointerVerbs.Of(open, me, seen with { Blocked = true }, null, settings, out _, out _));
        // The player itself is never talked to.
        Assert.Equal(PointerVerb.None, PointerVerbs.Of(open, me, seen with { Entity = me.Packed }, null, settings, out _, out _));
        _ = player;
    }

    [Fact]
    public void A_click_asks_to_talk_only_where_talk_applies_and_does_nothing_at_all_elsewhere()
    {
        var level = new TestLevel().Floor(-10, -10, 20, 10).Build();
        var w = MakeWorld(level, out var player);
        var other = MovementServices.SpawnBody(w, new(1.5f, 0.1f, 0.01f), 0f, Locomotion.Sandbox);
        Settle(w);
        var view = w.Service<ViewState>().View;
        var on = Project(view, w.Get<Body>(other).Feet + new Vector3(0, 0, 1.2f), 16f / 9f);
        var floor = Project(view, new Vector3(4f, -2f, 0f), 16f / 9f);
        int Talks() => w.Events<TalkRequested>().ThisTick.Count;
        int Any() => Talks() + w.Events<TargetPicked>().ThisTick.Count + w.Events<TargetRefused>().ThisTick.Count;

        MovementServices.Step(w, Free(on, aspect: 16f / 9f));
        Assert.Equal(PointerVerb.Talk, w.Service<PointerState>().Verb);
        MovementServices.Step(w, Free(on, Buttons.PointerClick, 16f / 9f));
        var asked = Assert.Single(w.Events<TalkRequested>().ThisTick);
        Assert.Equal((player, other, true), (asked.Who, asked.Whom, asked.Free));
        MovementServices.Step(w, Free(on, Buttons.PointerClick, 16f / 9f));     // held, not pressed again
        Assert.Equal(0, Talks());
        MovementServices.Step(w, Free(on, Buttons.Attack, 16f / 9f));           // attack is not the pointer's click
        Assert.Equal(0, Talks());

        MovementServices.Step(w, Free(floor, aspect: 16f / 9f));
        MovementServices.Step(w, Free(floor, Buttons.PointerClick, 16f / 9f));
        Assert.Equal(PointerVerb.None, w.Service<PointerState>().Verb);
        Assert.Equal(0, Any());

        // Captured, use on the reticle asks the same (the person is within reach ahead).
        Settle(w, 2);
        MovementServices.Step(w, new ActionFrame(Vector2.Zero, Vector2.Zero, Buttons.Use, new(0.5f, 0.5f)));
        var used = Assert.Single(w.Events<TalkRequested>().ThisTick);
        Assert.Equal((player, other, false), (used.Who, used.Whom, used.Free));
    }

    [Fact]
    public void The_verb_changing_is_published_once_per_change()
    {
        var level = new TestLevel().Floor(-10, -10, 20, 10).Build();
        var w = MakeWorld(level, out _);
        var other = MovementServices.SpawnBody(w, new(1.4f, 0.6f, 0.01f), 0f, Locomotion.Sandbox);
        Settle(w);
        // Settled with the reticle beside the person: none. Then the pointer over the person for a while, and off.
        var view = w.Service<ViewState>().View;
        var on = Project(view, w.Get<Body>(other).Feet + new Vector3(0, 0, 1.2f), 16f / 9f);
        var list = new List<PointerVerbChanged>();
        for (int t = 0; t < 40; t++)
        {
            MovementServices.Step(w, Free(t < 20 ? on : new Vector2(0.1f, 0.9f), aspect: 16f / 9f));
            list.AddRange(w.Events<PointerVerbChanged>().ThisTick);
        }
        Assert.Equal(2, list.Count);
        Assert.Equal((PointerVerb.Talk, PointerVerb.None, true), (list[0].Verb, list[0].Was, list[0].Free));
        Assert.Equal((PointerVerb.None, PointerVerb.Talk), (list[1].Verb, list[1].Was));
    }

    [Fact]
    public void A_target_asked_for_is_taken_within_its_range_and_kind_and_refused_otherwise_and_sight_is_asked_or_not()
    {
        var level = new TestLevel().Floor(-30, -30, 30, 30).Build();
        var w = MakeWorld(level, out var player);
        Settle(w);
        var state = w.Service<PointerState>();
        var view = w.Service<ViewState>().View;
        var near = Project(view, new Vector3(3f, 0f, 0f), 16f / 9f);
        var far = Project(view, new Vector3(9f, 0f, 0f), 16f / 9f);

        state.Ask(new TargetRequest(7, TargetKinds.Place, 5f));
        MovementServices.Step(w, Free(far, aspect: 16f / 9f));
        Assert.Equal((PointerVerb.Refused, TargetRefusal.TooFar), (state.Verb, state.Why));
        MovementServices.Step(w, Free(far, Buttons.PointerClick, 16f / 9f));
        var refused = Assert.Single(w.Events<TargetRefused>().ThisTick);
        Assert.Equal((7, TargetRefusal.TooFar), (refused.Request, refused.Why));
        Assert.NotNull(state.Request);                        // a refusal leaves it open

        MovementServices.Step(w, Free(near, aspect: 16f / 9f));
        Assert.Equal(PointerVerb.Target, state.Verb);
        MovementServices.Step(w, Free(near, Buttons.PointerClick, 16f / 9f));
        var picked = Assert.Single(w.Events<TargetPicked>().ThisTick);
        Assert.Equal((player, 7, ReticleTarget.Level), (picked.Who, picked.Request, picked.Kind));
        Assert.InRange(picked.Point.X, 2.9f, 3.1f);
        Assert.Null(state.Request);                           // taken: the request ends
        MovementServices.Step(w, Free(near, aspect: 16f / 9f));
        Assert.Equal(PointerVerb.None, state.Verb);

        // A request for a person refuses the level; a standing one stays after a pick.
        state.Ask(new TargetRequest(8, TargetKinds.Person, 5f));
        MovementServices.Step(w, Free(near, aspect: 16f / 9f));
        Assert.Equal((PointerVerb.Refused, TargetRefusal.WrongKind), (state.Verb, state.Why));
        state.Ask(new TargetRequest(9, TargetKinds.Any, 5f, Standing: true));
        MovementServices.Step(w, Free(near, Buttons.PointerClick, 16f / 9f));
        Assert.Single(w.Events<TargetPicked>().ThisTick);
        Assert.NotNull(state.Request);

        // Sight: what the camera sees past the shoulder but the eyes do not is refused unless the request needs no sight.
        var settings = w.Service<PointerSettings>();
        var unseen = new ReticleAnswer(ReticleTarget.Level, 0, new(3f, 1f, 0f), 3.4f, null, null, Blocked: true);
        Assert.Equal(PointerVerb.Refused, PointerVerbs.Of(w, player, unseen, new TargetRequest(1, TargetKinds.Any, 5f), settings, out var why, out _));
        Assert.Equal(TargetRefusal.NotSeen, why);
        Assert.Equal(PointerVerb.Target, PointerVerbs.Of(w, player, unseen, new TargetRequest(1, TargetKinds.Any, 5f, NeedsSight: false), settings, out _, out _));
        Assert.Equal(PointerVerb.Refused, PointerVerbs.Of(w, player, ReticleAnswer.Nothing, new TargetRequest(1, TargetKinds.Any, 5f), settings, out why, out _));
        Assert.Equal(TargetRefusal.Nothing, why);
    }

    [Fact]
    public void The_target_test_switch_reads_its_range_sight_and_kind()
    {
        Assert.True(TargetRequest.TryParse("8,nolos", out var r, out _));
        Assert.Equal(new TargetRequest(1, TargetKinds.Any, 8f, NeedsSight: false, Standing: true), r);
        Assert.Equal("8,nolos", r.Text);
        Assert.True(TargetRequest.TryParse("2.5,person", out r, out _));
        Assert.Equal((TargetKinds.Person, 2.5f, true), (r.Kinds, r.Range, r.NeedsSight));
        Assert.Equal("2.5,person", r.Text);
        Assert.False(TargetRequest.TryParse("far", out _, out var error));
        Assert.Contains("--target-test", error);
        Assert.False(TargetRequest.TryParse("5,through", out _, out _));
        Assert.False(TargetRequest.TryParse("0", out _, out _));
    }

    [Fact]
    public void Each_verb_shows_its_one_one_cursor_with_its_point_at_its_anchor()
    {
        Assert.Equal(CursorId.Default, Cursors.For(PointerVerb.None));
        Assert.Equal(CursorId.Talk, Cursors.For(PointerVerb.Talk));
        Assert.Equal(CursorId.Target, Cursors.For(PointerVerb.Target));
        Assert.Equal(CursorId.Negative, Cursors.For(PointerVerb.Refused));
        Assert.Equal("ui/Materials/cursorTalk.tga", Cursors.Path(CursorId.Talk));
        Assert.Equal("ui/Materials/cursorDefault.tga", Cursors.Path(CursorId.Default));
        Assert.Equal(Vector2.Zero, Cursors.Hotspot(CursorId.Default, 64f));          // the blade's tip, top left
        Assert.Equal(new Vector2(32f), Cursors.Hotspot(CursorId.Talk, 64f));        // the centre
        Assert.Equal(new Vector2(32f), Cursors.Hotspot(CursorId.Target, 64f));
        Assert.Equal(new Vector2(32f), Cursors.Hotspot(CursorId.Negative, 64f));
        Assert.Null(OneOneCursors.From(null).Art(CursorId.Talk));                    // no install: the plain arrow
    }

    [Fact]
    public void One_one_s_cursors_are_read_from_the_install_when_there_is_one()
    {
        using var install = GameInstall.TryOpen(Environment.GetEnvironmentVariable("VTMR_GAME_DIR"));
        Assert.SkipUnless(install is not null, "no 1.1 install (VTMR_GAME_DIR)");
        var cursors = OneOneCursors.From(install);
        Assert.Equal(Cursors.Count, cursors.Read);
        foreach (var id in new[] { CursorId.Default, CursorId.Talk, CursorId.Target, CursorId.Negative })
        {
            var art = cursors.Art(id)!;
            Assert.Equal((Cursors.Size, Cursors.Size), (art.Width, art.Height));
            Assert.Contains(art.Rgba.Where((_, i) => i % 4 == 3), a => a == 0);        // see-through round the picture
            Assert.Contains(art.Rgba.Where((_, i) => i % 4 == 3), a => a > 200);       // and the picture itself
        }
    }

    [Fact]
    public void The_pointers_click_shares_attacks_key_and_neither_takes_it_from_the_other()
    {
        static int? Code(string n) => n.Length == 1 ? n[0] : null;
        static string Name(int c) => ((char)c).ToString();
        var table = BindingTable.Build([new("attack", ["Mouse Left"]), new("pointerClick", ["Mouse Left"])], Code, Name);
        Assert.Empty(table.Problems);
        var left = InputSource.Mouse(BindingNames.SdlLeft);
        Assert.True(table.TryGet(left, out var action));
        Assert.Equal("attack", action);
        Assert.True(table.Clicks(left));
        var bindings = new Dictionary<string, string[]> { ["attack"] = ["Mouse Left"], ["pointerClick"] = [] };
        Assert.Empty(BindingTable.Rebind(bindings, "pointerClick", ["Mouse Left"], Code, Name));
        Assert.Equal(new[] { "Mouse Left" }, bindings["attack"]);
        Assert.Empty(BindingTable.Rebind(bindings, "attack", ["Mouse Left"], Code, Name));
        Assert.Equal(new[] { "Mouse Left" }, bindings["pointerClick"]);
    }

    [Fact]
    public void The_pointer_allocates_nothing_tick_by_tick()
    {
        var level = new TestLevel().Floor(-10, -10, 20, 10).Build();
        var w = MakeWorld(level, out _);
        MovementServices.SpawnBody(w, new(1.5f, 0.3f, 0.01f), 0f, Locomotion.Sandbox);
        w.Service<PointerState>().Ask(new TargetRequest(1, TargetKinds.Any, 4f, Standing: true));
        ISystem[] systems = [new PlayerControlSystem(), new BodySystem(), new CameraSystem(), new ReticleSystem(), new PointerSystem()];
        // Every queue the world registers warmed and turned over (Allocations.Warm), not a hand-kept list, and the
        // window measured more than once (Allocations.None: each the next 400 ticks), as the allocation tests measure.
        var queues = Allocations.Warm(w);
        var input = w.Service<TickInput>();
        int tick = 0;
        for (; tick < 400; tick++) { input.Set(PointerDrive(tick)); foreach (var q in queues) q.TurnOver(); foreach (var s in systems) s.Run(w); }
        Allocations.None("the pointer's 400 ticks", () =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int end = tick + 400; tick < end; tick++) { input.Set(PointerDrive(tick)); foreach (var q in queues) q.TurnOver(); foreach (var s in systems) s.Run(w); }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });
    }
}
