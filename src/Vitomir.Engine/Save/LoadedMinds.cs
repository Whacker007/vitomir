// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A load gives three minds back from the effects that hold them.
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Save;

/// <summary>
/// **A load gives three minds back** (the pass over a place just read from its save). A save keeps no mind: every
/// character's is made again in its home mode, in 1.1 and in Vitomir. So after the post-load fix-up
/// (<see cref="PostLoad"/>) and the re-creation, each character (type 0 or 7)
/// with a mind is looked at for three effects, in this order:
/// <list type="bullet">
/// <item><c>ef_frenzy</c>, by name: into frenzy
/// (<see cref="Minds.EnterFrenzy"/>), which ends any mesmerise, daze or fear on it first;</item>
/// <item>an effect of kind 0x19, afraid: afraid of nobody, not forced (<see cref="Minds.Frighten"/>), which the afraid handler lets go on its
/// first think;</item>
/// <item>an effect of kind 0x20, Majesty: struck by nobody
/// (<see cref="Minds.Majesty"/>), let go the same way.</item>
/// </list>
/// Mesmerised and dazed are not given back: such a character comes up in its home mode with its
/// effect still running, in 1.1 as here. The frenzy is the one that matters. Its end
/// (<see cref="Rpg.Frenzy.Watch"/>) asks whether the mind is in mode 9, so without this a frenzy
/// saved part-way through never ended — its strength and dexterity stayed on — and a frenzied player
/// had the controls back.
/// </summary>
/// <remarks>
/// The AI clock is not yet the save's when this runs (<see cref="Minds.Resume"/> comes after), so
/// the one-second waits the ways in set — the frenzy's first shout, the fear's first cry — are
/// already due when the game resumes, where 1.1's clock gives them their second (ours; no other
/// effect). The party's own reader (the party out of
/// `!main.nsg`, read only outside a network session) runs the same three for each member; here
/// the party comes back before the minds wake (`SaveApply.Party`, then `Location.Awaken` with
/// <c>fromSave</c>), so this pass reaches it too (`LoadedMindsTests`). A network join has no copy:
/// its character file is written with every effect off (the effects taken off before it is written) and read with the
/// post-load steps alone.
/// </remarks>
public static class LoadedMinds
{
    /// <summary>The effect a frenzy hangs on its character, which the load looks for by name.</summary>
    public const string FrenzyMarker = "ef_frenzy";

    /// <summary>The three for one character just given back by a load.</summary>
    public static void Restore(Minds minds, WorldThing thing)
    {
        if (thing.Type is not (ThingType.Actor or ThingType.Player) || thing.Mind is not { } mind) return;
        if (thing.Effects.Find(FrenzyMarker) is not null) minds.EnterFrenzy(mind);
        if (thing.Effects.Has(EffectKind.Afraid)) minds.Frighten(mind, null);
        if (thing.Effects.Has(EffectKind.Majesty)) minds.Majesty(mind, null);
    }
}
