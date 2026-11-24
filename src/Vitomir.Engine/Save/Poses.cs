// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A pose held at the end of an animation, across a save: 1.1's model block.
using Vitomir.Engine.Anim;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Save;

/// <summary>
/// **A body lies where it fell after a load.** 1.1's model block writes, after the model's
/// name, the animation on the model's track — its name and its flags
/// — whenever those flags hold `0x1010`: `endpause`, or the engine's own "stopped at
/// the end" bit. That is a corpse's death, and anything else a script left holding its last frame.
/// The reader loads the animation and plays it again at 30 a second with the flags and
/// **`0x800`**, which starts it at its last frame. Nothing else of what a character is playing is
/// kept, so everything else starts afresh.
///
/// Vitomir's save did not keep it, and every body a load brought back stood up. <see cref="Of"/>
/// takes the held channel as the pose, <see cref="Put"/> plays it again from its end.
/// </summary>
public static class Poses
{
    /// <summary>The flags that make the writer write the animation: `endpause` and the held bit.</summary>
    public const uint Kept = (uint)NmsFlags.EndPause | HeldBit;
    /// <summary>The engine's "stopped at the end" bit on a channel (<see cref="ThingAnimation.Channel.Held"/>).</summary>
    public const uint HeldBit = 0x1000;

    /// <summary>The animation a thing is holding at its end, or null when it is holding none.</summary>
    public static PoseSave? Of(WorldThing t)
    {
        foreach (var channel in new[] { t.Animation?.Action, t.Animation?.Motion })
        {
            if (channel is null) continue;
            // Mid-play or held, it is written — and played again from its end either way (0x800).
            // The held bit itself is not kept here, so a pose loaded and saved again is the same pose.
            if ((((uint)channel.Flags | (channel.Held ? HeldBit : 0)) & Kept) == 0) continue;
            return new PoseSave { Animation = channel.Mode.File, Mode = channel.Mode.Index >= 0 ? channel.Mode.Name : null, Flags = (uint)channel.Flags };
        }
        return null;
    }

    /// <summary>
    /// Plays <paramref name="pose"/> on <paramref name="t"/> again, from its last frame (with
    /// `flags | 0x800`), and holds it there. A thing with nothing to animate is left alone.
    /// </summary>
    public static void Put(Location place, WorldThing t, PoseSave pose)
    {
        if (t.Animation is not { } anim || place.Motions is not { } motions) return;
        // The mode it was played as, when the set has it with this animation; otherwise the file
        // alone, as 1.1 plays it — by name, with no mode at all.
        NmsMode? mode = null;
        NadAnimation? animation = null;
        if (pose.Mode is { } name && motions.Find(anim.Set, name) is { } found
            && string.Equals(found.Mode.File, pose.Animation, StringComparison.OrdinalIgnoreCase))
            (mode, animation) = found;
        animation ??= motions.Animation(pose.Animation);
        if (animation is null) return;
        mode ??= new NmsMode(-1, NmsModeKind.Body, pose.Mode ?? pose.Animation, pose.Animation,
                             (NmsFlags)(pose.Flags & ~HeldBit));
        anim.Play(mode, animation, motions, onMotion: false);
        if (anim.Action is { } held && ReferenceEquals(held.Player.Animation, animation))
        {
            held.Player.Loop = false;
            held.Player.Frame = animation.Duration;
            held.Held = true;
        }
    }
}
