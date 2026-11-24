// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the host's mover does to the mind of the character it moves
// (a comparison with 1.1's gap 13).
using System.Runtime.CompilerServices;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Net;

/// <summary>
/// **The host's mover and the moved mind**. 1.1's character makes the journey itself, mind
/// and all; Vitomir's is made again in the other place (a player's from their sheet, `NetCharacter`; a
/// character nobody plays from its template, `Campaign.Carry`), with a new mind. What the mover does to
/// the mind is kept here, on either side of that:
/// <list type="bullet">
/// <item>**A player's character** (the slot branch — a remote player's, and slot 0's before its journey),
/// once the place is loaded and what it was doing cancelled
/// (A possession is ended — on the body, when the mind is possessing (mode 13) a
/// character (type 0 or 7) that is not dead (dead flag 0x10000000) and is possessed:
/// its possessing effects come off (the effects' end with 0 and 999999.9);
/// and on the mover itself when it is possessed (mind bit 0x10000000) —
/// then, **unless it is frenzied**, its mind starts over as a revive's does
/// (<see cref="Minds.Revived"/>). A frenzied one keeps its frenzy across
/// (<see cref="Leaving"/>, <see cref="Arrived"/>, <see cref="Journeyed"/>).</item>
/// <item>**A character nobody plays** (the last branch, the one being steered): no possession is ended and
/// the mind is not reset — it goes as it is — and in the other place its interest spots are looked up
/// again (<see cref="Mind.Settle"/>). Here the mind's state is carried onto the new
/// thing's (<see cref="Carried"/>).</item>
/// </list>
/// </summary>
public static class MoverMinds
{
    /// <summary>The players whose character was frenzied as the mover took it: its frenzy goes on in the other place.</summary>
    static readonly ConditionalWeakTable<NetPlayer, object> frenzied = new();

    /// <summary>
    /// The mover's mind step for a player's character about to go: the possessions ended, and the mind reset
    /// unless frenzied. Answers whether it was frenzied (its mind kept in mode 9).
    /// </summary>
    public static bool Leaving(WorldThing who)
    {
        if (who.Mind is not { } mind || who.Place?.Minds is not { } minds) return false;
        if (mind.Mode == AiMode.Possessing && mind.Target is { Removed: false, Type: ThingType.Actor or ThingType.Player } body
            && body.Actor?.Dead != true && body.Mind is { } held && (held.Flags & Mind.FlagPossessed) != 0)
            EffectRuntime.RemoveKind(body, EffectKind.Possessing, 0);
        if ((mind.Flags & Mind.FlagPossessed) != 0)
            EffectRuntime.RemoveKind(who, EffectKind.Possessing, 0);
        if (Frenzy.Frenzied(who)) return true;
        minds.Revived(mind);
        return false;
    }

    /// <summary>A remote player's character leaving by the mover (<see cref="Leaving"/>), its frenzy remembered for its arrival.</summary>
    internal static void Leaving(NetPlayer player, WorldThing who)
    {
        frenzied.Remove(player);
        if (Leaving(who)) frenzied.Add(player, true);
    }

    /// <summary>
    /// A remote player's character made again in the place it went to (`GameServer.Catch`): its new mind is
    /// the reset one; a frenzied one's is put back in frenzy (as a load's is, `Save.LoadedMinds`) —
    /// **ours**, since 1.1's mind never left mode 9 — so the frenzy's end still finds it.
    /// </summary>
    public static void Arrived(NetPlayer player)
    {
        if (!frenzied.TryGetValue(player, out _)) return;
        frenzied.Remove(player);
        if (player.Avatar is { Mind: { } mind, Place.Minds: { } minds } && !Frenzy.Frenzied(player.Avatar)) minds.EnterFrenzy(mind);
    }

    /// <summary>
    /// The host's own character across its journey alone (slot 0): the journey's own test ends
    /// the steered possession first (`Location.EndSteeredPossession`); the mind it arrives with is
    /// reset or, when <paramref name="was"/> was frenzied, put back in frenzy (ours, as <see cref="Arrived"/>).
    /// </summary>
    public static void Journeyed(WorldThing? was, WorldThing? now)
    {
        if (was is null || now is null || ReferenceEquals(was, now) || now.Mind is not { } mind || now.Place?.Minds is not { } minds) return;
        if (Frenzy.Frenzied(was)) { if (!Frenzy.Frenzied(now)) minds.EnterFrenzy(mind); }
        else minds.Revived(mind);
    }

    /// <summary>
    /// The mover's last branch for a character nobody plays: the mind goes as it is — its mode and home mode,
    /// its flags and its clocks, carried onto the new thing's (ours: 1.1's thing makes the journey) — and its
    /// interest spots are those of the place it came to. Its target and its pets stay
    /// behind (ours: a GUID names a thing of one place here).
    /// </summary>
    public static void Carried(WorldThing was, WorldThing now)
    {
        if (was.Mind is not { } old || now.Mind is not { } mind || now.Place?.Minds is not { } minds) return;
        int since = was.Place?.Minds?.Now ?? minds.Now;
        int Later(int at) => at == 0 ? 0 : at - since + minds.Now;
        mind.Enter(old.BaseMode, andBase: true);
        mind.Enter(old.Mode, andBase: false);
        mind.Flags = old.Flags;
        mind.HasNoticed = old.HasNoticed;
        mind.NextThink = Later(old.NextThink);
        mind.NextFear = Later(old.NextFear);
        mind.NextFearShout = Later(old.NextFearShout);
        mind.NextFrenzyShout = Later(old.NextFrenzyShout);
        mind.Settle(minds.Spots, now.Place.Walk);
    }
}
