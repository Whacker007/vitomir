// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours. The talking distance is 1.1's use reach, read from src/Vitomir.Engine/Rpg/Actions.cs (UseReach)
// and src/Vitomir.Engine/World/Bodies.cs (InReach) at 402bef2; nothing is referenced.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Cameras;

/// <summary>
/// The pointer's numbers: how far its ray reaches, and the talking distance — 1.1's own reach for a use or a
/// talk, 75 of its units <b>body to body</b> (the use tick's reach, through the
/// reach test: the bodies' heights overlap, and the distance across the ground between their centres less
/// both radii is no more than the reach), 1.04 m.
/// </summary>
public sealed record PointerSettings
{
    /// <summary>The talking distance between the two bodies' sides (m; 1.1's 75 units).</summary>
    public float TalkReach { get; init; } = 75f / 72f;
    /// <summary>How far the freed pointer's ray reaches (m; the reticle's own reach).</summary>
    public float Reach { get; init; } = 100f;
}

/// <summary>
/// What is under the focus this tick — the freed pointer's place, or the reticle while the pointer is captured — and
/// what a click there would do (a service, digested, so a replay that picks otherwise parts at once): the answer
/// (<see cref="Under"/>, the reticle's kind of answer, with its name and part left to the presentation), the verb
/// (<see cref="Verb"/>, from <see cref="PointerVerbs.Of"/>), why a target is refused, and the target a power asked
/// for (<see cref="Request"/>, the hook: <see cref="Ask"/>, <see cref="Withdraw"/>).
/// </summary>
public sealed class PointerState : IDigestedService
{
    /// <summary>Whether the pointer was free this tick (else the answer is the reticle's).</summary>
    public bool Free { get; internal set; }
    /// <summary>The freed pointer's place on the picture (0 to 1 across and down) this tick.</summary>
    public Vector2 Place { get; internal set; }
    /// <summary>What is under the pointer, or under the reticle while it is captured.</summary>
    public ReticleAnswer Under { get; internal set; } = ReticleAnswer.Nothing;
    public PointerVerb Verb { get; internal set; }
    /// <summary>The distance between the body's side and the person's under the focus (m; 1.1's measure), infinity for anything else.</summary>
    public float Gap { get; internal set; } = float.PositiveInfinity;
    /// <summary>Why a target there would be refused (<see cref="PointerVerb.Refused"/>), else none.</summary>
    public TargetRefusal Why { get; internal set; }
    /// <summary>The target asked for, if any: while one is, the verb is <see cref="PointerVerb.Target"/> or <see cref="PointerVerb.Refused"/>.</summary>
    public TargetRequest? Request { get; private set; }

    /// <summary>
    /// Asks the player for a target (a power's targeting, or the developer's <c>--target-test</c>): from the next tick
    /// the verb is target or refused by the request's kinds, range and need of sight, and a click publishes
    /// <see cref="TargetPicked"/> (the request then ends, unless it is <see cref="TargetRequest.Standing"/>) or
    /// <see cref="TargetRefused"/>. A request asked while one is open replaces it.
    /// </summary>
    public void Ask(in TargetRequest request) => Request = request;

    /// <summary>Ends the open request, if any, with nothing picked.</summary>
    public void Withdraw() => Request = null;

    void IDigestedService.Digest(ref Fnv1a64 h)
    {
        h.Add(Free ? 1 : 0);
        h.Add(Place.X); h.Add(Place.Y);
        h.Add((int)Under.Kind); h.Add(Under.Entity);
        h.Add(Under.Point.X); h.Add(Under.Point.Y); h.Add(Under.Point.Z);
        h.Add(Under.Distance); h.Add(Under.Blocked ? 1 : 0);
        h.Add((int)Verb); h.Add((int)Why);
        if (Request is { } r) { h.Add(1); h.Add(r.Id); h.Add((int)r.Kinds); h.Add(r.Range); h.Add(r.NeedsSight ? 1 : 0); h.Add(r.Standing ? 1 : 0); }
        else h.Add(0);
    }
}

/// <summary>
/// The ray through the freed pointer: from the camera through the pointer's
/// place on the picture, by the view's vertical field and the picture's shape, as the renderer projects — x across by
/// the field's half-tangent times the shape, y up by the half-tangent, about the view's forward, right and up (the
/// renderer's look-at with +Z up). Everything it reads is on the tape or made from it: the place and the shape are the
/// frame's (<see cref="ActionFrame.Pointer"/>, <see cref="ActionFrame.Aspect"/>), the view the camera's, its field the
/// cameras' settings; and it uses <see cref="DetMath"/> only, so a replay picks the same thing.
/// </summary>
public static class PointerPick
{
    /// <summary>The unit direction through <paramref name="place"/> (0 to 1 across and down the picture) from <paramref name="view"/>, on a picture of <paramref name="aspect"/>.</summary>
    public static Vector3 Direction(in ViewPoint view, Vector2 place, float aspect)
    {
        var forward = DetMath.Direction(view.Yaw, view.Pitch);
        var (s, c) = DetMath.SinCos(view.Yaw);
        var right = new Vector3(s, -c, 0f);
        var up = Vector3.Cross(right, forward);
        float fov = view.FieldOfViewDegrees > 0 ? view.FieldOfViewDegrees : 75f;
        float t = DetMath.Tan(fov * 0.5f * DetMath.DegToRad);
        float x = (2f * place.X - 1f) * t * aspect, y = (1f - 2f * place.Y) * t;
        return Vector3.Normalize(forward + right * x + up * y);
    }

    /// <summary>Whether <paramref name="place"/> is on the picture (not on the bars beside it).</summary>
    public static bool OnPicture(Vector2 place) => place.X >= 0f && place.X <= 1f && place.Y >= 0f && place.Y <= 1f;
}

/// <summary>
/// The Post phase's pointer, after the reticle (ours): for the controlled body, what is under the freed pointer
/// (<see cref="PointerPick"/> and the reticle's own cast, <see cref="ReticleSystem.Cast"/>) or, while the pointer is
/// captured, the reticle's answer; the verb there (<see cref="PointerVerbs.Of"/>); a change of verb published once
/// (<see cref="PointerVerbChanged"/>); and the click — the freed pointer's <see cref="Buttons.PointerClick"/>, or
/// <see cref="Buttons.Use"/> on the reticle — acting only where a verb applies: talk publishes
/// <see cref="TalkRequested"/>, target <see cref="TargetPicked"/>, refused <see cref="TargetRefused"/>, none nothing at all.
/// </summary>
public sealed class PointerSystem : ISystem
{
    public void Run(World world)
    {
        var state = world.Service<PointerState>();
        var view = world.Service<ViewState>();
        var input = world.Service<TickInput>();
        var settings = world.Service<PointerSettings>();
        var was = state.Verb;
        var who = view.Of;
        if (!view.HasView || !world.Alive(who) || !world.Has<CameraRig>(who))
        {
            state.Free = false;
            state.Under = ReticleAnswer.Nothing;
            state.Verb = PointerVerb.None;
            state.Why = TargetRefusal.None;
            state.Gap = float.PositiveInfinity;
            return;
        }
        var frame = input.Current;
        bool free = world.Get<CameraRig>(who).PointerFree;
        state.Free = free;
        state.Place = free ? frame.Pointer : new Vector2(0.5f, 0.5f);
        if (!free) state.Under = world.Service<ReticleState>().Answer;
        else if (!PointerPick.OnPicture(frame.Pointer)) state.Under = ReticleAnswer.Nothing;
        else
        {
            var dir = PointerPick.Direction(view.View, frame.Pointer, frame.PictureAspect);
            state.Under = ReticleSystem.Cast(world, view, view.View.Eye, dir, settings.Reach);
        }
        var under = state.Under;
        state.Verb = PointerVerbs.Of(world, who, under, state.Request, settings, out var why, out float gap);
        state.Why = why;
        state.Gap = gap;
        if (state.Verb != was)
            world.Events<PointerVerbChanged>().Publish(new PointerVerbChanged(who, world.Tick, state.Verb, was, under.Entity, free));

        var pressed = input.Pressed;
        bool click = free ? (pressed & Buttons.PointerClick) != 0 : (pressed & Buttons.Use) != 0;
        // With the commit point on, use held in the air is the catch's: a press of it off the ground talks to no one and picks nothing.
        if (click && !free && world.TryService<Animation.People.PersonCommitSettings>(out var commit) && commit.On && world.TryStore<Bodies.Body>(out var bodies)
            && bodies.Has(who) && bodies.Get(who).State != Bodies.BodyState.Ground) click = false;
        if (!click) return;
        switch (state.Verb)
        {
            case PointerVerb.Talk:
                world.Events<TalkRequested>().Publish(new TalkRequested(who, EntityId.FromPacked(under.Entity), world.Tick, gap, free));
                break;
            case PointerVerb.Target when state.Request is { } asked:
                world.Events<TargetPicked>().Publish(new TargetPicked(who, world.Tick, asked.Id, under.Kind, under.Entity, under.Point, under.Distance, free) { Part = under.Part });
                if (!asked.Standing) state.Withdraw();
                break;
            case PointerVerb.Refused when state.Request is { } refused:
                world.Events<TargetRefused>().Publish(new TargetRefused(who, world.Tick, refused.Id, why, under.Entity, under.Point, under.Distance, free));
                break;
        }
    }
}

/// <summary>The pointer registered in one call, after the movement (<see cref="MovementServices.Register"/>, whose reticle it reads).</summary>
public static class PointerServices
{
    /// <summary>The pointer's place in the Post phase: after the camera and the reticle (<see cref="MovementServices.ReticleOrder"/>).</summary>
    public const int PointerOrder = 120;

    /// <summary>
    /// Registers the events <see cref="PointerVerbChanged"/>, <see cref="TalkRequested"/>, <see cref="TargetPicked"/> and
    /// <see cref="TargetRefused"/>, the services <see cref="PointerSettings"/> (<paramref name="settings"/> or the
    /// defaults) and <see cref="PointerState"/>, and the system <c>Post/120 platform.pointer</c> (<see cref="PointerSystem"/>).
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b, PointerSettings? settings = null)
    {
        b.Event<PointerVerbChanged>().Event<TalkRequested>().Event<TargetPicked>().Event<TargetRefused>();
        b.Service(settings ?? new PointerSettings()).Service(new PointerState());
        b.System(Phase.Post, PointerOrder, "platform.pointer", new PointerSystem());
        return b;
    }
}
