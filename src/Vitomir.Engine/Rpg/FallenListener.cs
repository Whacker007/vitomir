// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A fallen party member in a conversation (at the start and the end), read
// in a comparison with 1.1 and built.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **A fallen companion stands up for a conversation and vanishes after it.**
///
/// <para>The conversation's start walks its cast; for a <b>dead</b> player-type member
/// with a model (flag <c>0x10000000</c>) it does three more things after the
/// listen and the weapons: the model's action channel is stopped (the death pose held there goes, and the `listen` every member was just given
/// on the motion channel shows), <c>DON'T RENDER</c> is cleared, and the model's alpha is put to 1
/// at once (alpha 1.0 with no ramp — a body that had faded is solid again). So a fallen
/// Wilhem gets up and listens with everybody else.</para>
///
/// <para>The end gives the same member <c>DON'T RENDER</c>, and the body is
/// hidden from then on — until a revive, which clears it (<see cref="Damage.Revive"/>). The
/// animation update that follows for every member puts its locomotion back; nothing puts
/// the death pose back, and nothing needs to, since nothing draws it.</para>
///
/// <para>Vitomir did neither: the body lay where it fell through the conversation, and stayed.</para>
/// </summary>
public static class FallenListener
{
    /// <summary>The render flag the draw leaves a thing out for (<c>THING_RENDERFLAG_DONTRENDER</c>, 0x2).</summary>
    public const uint DontRender = 0x2;

    /// <summary>Whether <paramref name="who"/> is the case: a dead player-type thing with a model.</summary>
    public static bool Fallen(WorldThing who) =>
        who.Type == ThingType.Player && who.Actor is { Dead: true }
        && (who.Model.Length > 0 || who.Scene.Model is not null);

    /// <summary>The start's: up, drawn and solid.</summary>
    public static void Up(WorldThing who)
    {
        if (!Fallen(who)) return;
        who.Animation?.StopAction();
        who.RenderFlags &= ~DontRender;
        who.Model3D.SetAlpha(1f, 0f);                                           // alpha 1.0 at once
    }

    /// <summary>The end's: not drawn any more.</summary>
    public static void Hide(WorldThing who)
    {
        if (Fallen(who)) who.RenderFlags |= DontRender;
    }
}
