// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A revive, told to the players' machines as 1.1 tells it: the host's
// revive ends with a message (0x20: the actor and the health and blood
// fractions), and each player's machine revives its own copy with it (the network's revive, with the noise)
// — which shows the weapon a death hid there again.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **A revive on the players' machines** (1.1's message 0x20). The host tells each machine in the place
/// (<see cref="Told"/>, from <see cref="Damage.Revive"/>) and each machine does its own part of the revive
/// (<see cref="Shown"/>): the death undone and the body shown whole — the weapon the death hid
/// in the hand again.
/// </summary>
public static class SeenRevive
{
    /// <summary>
    /// A character revived on the host (on a host the revive ends by telling the
    /// players): <see cref="Msg.Revived"/> with its GUID and the two fractions, to everybody in the place, sent
    /// with the made things' news (<see cref="MadeHere.Take"/>). Nothing on a machine that is not a host.
    /// </summary>
    public static void Told(WorldThing thing, float health, float blood)
    {
        if (thing.Place is not { } place || NetOrders.Mode(place) != NetMode.Server) return;
        MadeHere.Tell(place, new NetWriter(Msg.Revived).I32(thing.Guid).F32(health).F32(blood).ToArray());
    }

    /// <summary>
    /// A revive the host told (the revive with its network argument and its noise
    /// argument both 1), on this machine's copy. What the revive does on a player's machine:
    /// <list type="bullet">
    /// <item>for a dead body: the death, the stake, the expiry and fade clocks off, the render
    /// flag 2 (`DON'T RENDER`) off, and a shape ended;</item>
    /// <item>permanent frenzy off; the health at its fraction of the maximum (the host's next snapshot carries the
    /// host's own);</item>
    /// <item>the limp off, the model's alpha back to 1, the death pose stopped, the locomotion back;</item>
    /// <item>**the weapon shown again** (the weapon model's render flag 8 cleared) — whether or not a death hid it;</item>
    /// <item>the awakening noise (foley 0x4d), when its mouth is free — always asked for here, since the message's
    /// reader passes 1 whatever the host's caller passed.</item>
    /// </list>
    /// Not here, as on 1.1's player's machine: the effects taken off and the born ones put back (both refused there unless from the network — the host's own effect messages carry
    /// them) and the mind (there is none). **Ours**: a dead pedestrian's walk home is left to the host's
    /// snapshot, which carries where it stands, and the pools other than health come with the sheet.
    /// </summary>
    public static void Shown(WorldThing thing, float health, float blood)
    {
        if (thing.Actor is not { } actor) return;
        if (actor.Has(Actor.Flag.Dead))
        {
            actor.Flags &= ~(Actor.Flag.Dead | Actor.Flag.Staked);
            thing.ExpireAt = 0;
            actor.FadeAt = 0;
            thing.RenderFlags &= ~FallenListener.DontRender;
            ShapeShift.End(thing);
        }
        actor.Dusted = false;
        actor.Flags2 &= ~Actor.Flag2.PermaFrenzy;
        actor.Health = Math.Clamp(actor.MaxHealth * health, 0f, actor.MaxHealth);     // the health's bounds
        actor.Flags2 &= ~Actor.Flag2.Limping;
        thing.ModelAlpha = 1f;
        actor.FadingFrom = 0;
        thing.Animation?.StopAction();
        thing.Locomotion?.Reanimate();
        actor.WeaponHidden = false;
        if (Audio.Noise.CanSpeak(thing)) Audio.Noise.Foley(thing, Audio.FoleyEvent.DeathAwaken);
    }
}
