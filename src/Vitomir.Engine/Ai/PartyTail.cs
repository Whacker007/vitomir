// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The party think's follow and the gate in front of its nurse (the clause before the nurse).
// Found by a comparison with 1.1 and built.
using Microsoft.Extensions.Logging;

namespace Vitomir.Engine.Ai;

public sealed partial class Minds
{
    /// <summary>
    /// **The follow, and whether it leaves room for the nurse**. The party
    /// think's chain reaches <see cref="Nurse"/> when
    /// <list type="bullet">
    /// <item>the member is **out of formation** — `InParty` (the formation bit and the
    /// party slot together) is false, and the chain goes straight past the follow to the nurse; or</item>
    /// <item>it is in formation, **not busy** (the busy time — both of Vitomir's halves of
    /// it), **not dead**, **not restrained** — and **the follow did nothing**
    /// the follow returned 0, because the member was near enough to stand, or every slot
    /// round the leader was taken).</item>
    /// </list>
    /// A member in formation that is busy, dead or held neither follows nor nurses; one that walked
    /// after its leader nurses on a later think, when it has caught up. Earlier, Vitomir did the
    /// opposite in both places: it returned before the nurse for a member out of the group — so a
    /// companion told to stay put never healed itself — and nursed after a follow that walked.
    ///
    /// An out-of-formation member does not follow at all, which is the manual's *"Once a character
    /// is 'out of the group' s/he will no longer follow the lead character."* Everything before this
    /// still runs for it: it keeps fighting, keeps hunting on Attack stance, and stays where it is put.
    ///
    /// The one followed is the party's selected member — during a possession the caster
    /// standing still, not the body the player is walking about in. With nobody selected (the engine
    /// asserts there) the member neither follows nor nurses (ours).
    ///
    /// True when the chain stops here.
    /// </summary>
    bool FollowedInstead(Mind mind)
    {
        var me = mind.Thing.Actor!;
        if (!me.Has2(World.Actor.Flag2.InFormation)) return false;
        if (mind.BusyUntil > Now || me.BusyUntil > Now
            || me.Has(World.Actor.Flag.Dead) || me.Has2(World.Actor.Flag2.Restrained)) return true;
        if (Selected is not { } leader) return true;
        if (Follow(mind, leader)) return true;
        // Vitomir's follow also answers false when every slot is taken, which the engine's does too
        // Six tries) — the one case worth a line in the log.
        if (World.Bodies.Gap(mind.Thing, leader) > Leash(me.Stance) * Rules.GameRules.Current.Ai.FollowReach && announced.Add(AiMode.Pet))
            log.LogInformation("AI: {Name} has nowhere to stand by its leader", mind.Thing.Template);
        return false;
    }
}
