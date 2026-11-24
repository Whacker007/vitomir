// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taking a target on sight: the enemy search's arm, the Attack-stance hunt's
// and the guards' rally — each a SetTarget and what follows it.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ai;

public sealed partial class Minds
{
    /// <summary>
    /// **The enemy search finds somebody**: set the target, go into
    /// the enemy mode (mode 3, the home mode kept), and then test the "reacted" bit
    /// (the mind's bit 2). The set-target has already reacted by then when the
    /// character never had — stop, <c>vox_wakeup</c>, the wake-up delay, the awakening, the
    /// <c>awaken</c> script call — and the react sets that bit. So the test always
    /// passes: 1.1 plays <c>vox_alert</c> and asks for a think **now** on the first sight as on every
    /// later one, the busy stamp the react left still holding the character for
    /// its wake-up. The other arm, a react of its own, cannot be reached after a
    /// set-target, and is kept only as the engine has it.
    ///
    /// Earlier, the first sight reacted and nothing more — no shout, and the next think only after
    /// the wake-up delay.
    /// </summary>
    void Sighted(Mind mind, WorldThing target)
    {
        SetTarget(mind, target);
        mind.Enter(AiMode.Enemy, andBase: false);
        if (!mind.HasNoticed) { React(mind, target); return; }   // unreachable after the set-target
        CallOut(mind);
    }

    /// <summary>
    /// **An Attack-stance member finds somebody** (the tail of the companion's hunt):
    /// set the target — which reacts, the first time, as any set-target does — then
    /// <c>vox_alert</c> and a think now, with **no** test of the reacted bit. Earlier, the hunt only
    /// aimed: no react, no <c>awaken</c> call, no shout.
    /// </summary>
    void Hunted(Mind mind, WorldThing target)
    {
        SetTarget(mind, target);
        CallOut(mind);
    }

    /// <summary>
    /// **A guard rallied by a fellow guard** (the event handler's guard half): set the target and go into the
    /// enemy mode (mode 3) — the set-target's own react the first time, and
    /// **no** shout; the think comes when the set-target (now) or its react (after the wake-up) asked
    /// for it. Earlier, this went through the search's arm and shouted when the guard had reacted
    /// before.
    /// </summary>
    void Rallied(Mind mind, WorldThing target)
    {
        SetTarget(mind, target);
        mind.Enter(AiMode.Enemy, andBase: false);
    }

    /// <summary>
    /// <c>vox_alert</c> (foley event `0x50`) and a think at once (the next think set to now), the two
    /// lines both the search's arm and the hunt's end with.
    /// </summary>
    void CallOut(Mind mind)
    {
        Audio.Noise.Foley(mind.Thing, Audio.FoleyEvent.VoxAlert);
        mind.NextThink = Now;
    }
}
