// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Held automatic fire: what the `Repeat` keyframe tag (tag 16) does
// and how the `RepeatTo` tag's frame is found,
// as in 1.1.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **Held fire with an automatic weapon.** 35 shipped attack animations — every one a modern-day
/// gun, flamethrower or chainsaw for Christof, Lily, Pink, Samuel, the Nosferatu gunmen and the
/// London Giovanni — carry two tags around their `Fire` (or `Strike`): `RepeatTo` just before it and
/// `Repeat` just after (Christof's Uzi: `RepeatTo` at frame 9, `Fire` at 10.67, `Repeat` at 19, of
/// 39). When the play head reaches `Repeat` with the button still down and a
/// <see cref="WeaponFlag.Repeatable"/> attack in hand, the steered character turns to what is under
/// the pointer and the animation **jumps back to `RepeatTo`**, so the `Fire` tag comes round again:
/// one shot a loop, for as long as the button is held, with the aim following the mouse. Let go and
/// the animation plays out and the attack ends as any other does.
///
/// It is not the *auto repeat attack* option (<see cref="SwingRepeat"/>), which orders a
/// whole new attack when one has ended; this runs inside the attack, and nothing gates it but the
/// button. The two stack: an automatic weapon let go of plays out, and then the option — if it is
/// on and the button is down again — starts the next.
/// </summary>
public static class HeldFire
{
    /// <summary>The keyframe tag the loop goes round at, `Repeat`.</summary>
    public const uint RepeatTag = 16;
    /// <summary>The keyframe tag it goes back to, `RepeatTo` (tag type 0x11).</summary>
    public const uint RepeatToTag = 17;

    /// <summary>
    /// What the pointer is doing, as 1.1 reads it from the engine's input globals — Vitomir has
    /// the input layer leave it on the steered character each frame (<see cref="Actor.Pointing"/>)
    /// instead, as it does for <see cref="SwingRepeat"/>.
    /// </summary>
    /// <param name="Held">The **left** button (the first binding) **or** the middle one
    /// (the third) is down.</param>
    /// <param name="Under">What the pointer is resting on — the rollover pass's own answer,
    /// so exactly what the cursor offers.</param>
    /// <param name="Ground">The point on the level's surfaces under the pointer (`collidePos`,
    /// cast from the camera through the pointer), or null when the ray meets
    /// nothing.</param>
    public sealed record Pointer(bool Held, WorldThing? Under, Vector3? Ground);

    /// <summary>
    /// The frame of an animation's first tag of type <paramref name="type"/>, or −1 when it has none
    /// The tags in order, the first match, else −1.0).
    /// </summary>
    public static float TagFrame(NadAnimation animation, uint type)
    {
        foreach (var tag in animation.Tags)
            if (tag.Type == type) return tag.Frame;
        return NoTag;
    }

    /// <summary>What <see cref="TagFrame"/> answers for a tag that is not there.</summary>
    public const float NoTag = -1f;

    /// <summary>
    /// **The `Repeat` tag** (the dispatcher sends only a character or a party member).
    /// Nothing happens unless all of these hold:
    /// <list type="bullet">
    /// <item>the character is the one being steered (`GetCurrentControl`);</item>
    /// <item>it is in the attack (<see cref="ActionMode.Swing"/>, 5);</item>
    /// <item>it is answering to orders — not the party's selected member with its mind in charge
    /// (<see cref="CastInput.Refused"/>);</item>
    /// <item>the weapon's **current** attack is <see cref="WeaponFlag.Repeatable"/> (0x4) — the 26
    /// automatic templates: the pistols and revolvers, the Uzis, the assault rifles, the chainguns,
    /// the flamethrowers and the chainsaws. The shotgun, the rifles and the rocket launcher are not;</item>
    /// <item>the left or the middle button is down (<see cref="Pointer.Held"/>).</item>
    /// </list>
    /// Then the aim:
    /// <list type="bullet">
    /// <item>an attack **on a thing** goes on only while the pointer is still on **that** thing
    /// (the target is the thing under the pointer) — sweep off it and the loop stops, the animation playing out.
    /// The character turns to it;</item>
    /// <item>an attack **on a point** (no target thing) takes the ground under the pointer
    /// as its new point and turns to it — so the stream of fire follows the
    /// mouse across the floor. No ground under the pointer, no loop.</item>
    /// </list>
    /// And the loop: the action channel's play head goes back to the animation's `RepeatTo` frame
    /// (an animation without one does not loop), and the character is busy for the attack's
    /// `RELOADTIME` from now — which is what keeps the attack from ending under it
    /// (the swing's end waits for the busy time). **The turn comes first and happens whatever the frame
    /// turns out to be.** A player's machine in a network game, having jumped back itself, sends the
    /// host order 0x1e with the target and the point, and the host jumps its own copy back
    /// (<see cref="JumpBack"/>). **Ours** on a player's machine: the attack is the host's, so
    /// the loop there does not ask for <see cref="ActionMode.Swing"/> — the tag crossed on the action
    /// channel stands for it (the host's strike message 0xc does put this machine's character in the
    /// swing, `Net.NetArms`, as 1.1's).
    /// </summary>
    /// <returns>True when the play head went back — the engine's tag walk then stops for this
    /// frame, the next tag lying beyond the new play head.</returns>
    public static bool Repeat(WorldThing thing, int nowMs)
    {
        if (thing.Type is not (ThingType.Actor or ThingType.Player) || thing.Actor is not { } me) return false;
        if (thing.Place is not { } place || !ReferenceEquals(place.Steered, thing)) return false;
        bool client = Net.NetOrders.Mode(place) == Net.NetMode.Client;
        if (me.Action != ActionMode.Swing && !client) return false;
        if (CastInput.Refused(thing)) return false;
        if (me.Weapon is not { } weapon || !weapon.Current.Is(WeaponFlag.Repeatable)) return false;
        if (me.Pointing is not { Held: true } pointer) return false;

        Vector3 at;
        // The target is looked up by id; one that has left the world is nobody, and the
        // attack is on the point. **Ours:** Vitomir holds the thing rather than its id, so a gone
        // target is let go here, which is what every later look-up of the id would have found.
        if (me.Target is { InWorld: false }) me.Target = null;
        if (me.Target is { } target)
        {
            if (!ReferenceEquals(target, pointer.Under)) return false;
            at = target.Position;
        }
        else
        {
            if (pointer.Ground is not { } ground) return false;
            me.ActionPoint = ground;
            at = ground;
        }
        thing.Face(at);

        if (thing.Animation is not { Action: { } channel } animation) return false;
        float frame = TagFrame(channel.Player.Animation, RepeatToTag);
        if (frame < 0f) return false;
        animation.Rewind(frame);
        if (client) Net.NetOrders.Forward(thing, Net.OrderKind.Aim, me.Target, at);
        me.BusyUntil = nowMs + weapon.Current.ReloadTime;
        return true;
    }

    /// <summary>
    /// The host's arm of order 0x1e when the character is already in the attack
    /// (<see cref="ActionMode.Swing"/>): the action channel's play head back to the `RepeatTo` frame
    /// (tag type 0x11, the tags passed forgotten), so the `Fire` tag comes round again on the
    /// host. Unlike the tag's own loop it sets **no** busy time. False when nothing is playing or the
    /// animation has no `RepeatTo`.
    /// </summary>
    public static bool JumpBack(WorldThing thing)
    {
        if (thing.Animation is not { Action: { } channel } animation) return false;
        float frame = TagFrame(channel.Player.Animation, RepeatToTag);
        if (frame < 0f) return false;
        animation.Rewind(frame);
        return true;
    }
}
