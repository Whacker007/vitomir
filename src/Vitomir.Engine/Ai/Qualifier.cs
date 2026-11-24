// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The two clauses at the end of the enemy qualifier that this tree had not
// read until then: a target somebody friendly is feeding on, and — for a party member — a target held
// by Mesmerize for a friend. Both make the qualifier answer "leave it alone", and every caller of
// the qualifier then lets go of the target or passes it over (the party fight, the pet's,
// the enemy's, the enemy search and the party hunt).
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ai;

public sealed partial class Minds
{
    /// <summary>
    /// **A target being fed on by a friend is left alone**. When the
    /// target's action is <see cref="ActionMode.FedOn"/> (9, at the neck) or
    /// <see cref="ActionMode.WristFedOn"/> (12, a donor at the wrist), its action target
    /// (the feeder: <see cref="Feeding"/> sets it at the bite) is looked up; if that
    /// is an actor or a player (thing type 0 or 7), the looker counts it a friend and it
    /// is not the looker itself, the qualifier answers yes.
    ///
    /// So when Christof bites a Cappadocian, Wilhem lets go of her at his next think and his hunt
    /// passes her over until the feed ends; in 1.1 Wilhem's target goes from her to
    /// nothing a fraction of a second after Christof's bite
    /// (his next think). The feeder keeps its own victim, and an enemy's friend
    /// feeding on you does not stop that enemy — it is the looker's friends that count.
    /// </summary>
    public static bool FedOnByAFriend(WorldThing me, WorldThing target)
    {
        if (target.Actor is not { Action: ActionMode.FedOn or ActionMode.WristFedOn } them) return false;
        if (them.Target is not { Type: ThingType.Actor or ThingType.Player } feeder) return false;
        return Sight.Friendly(me, feeder) && !ReferenceEquals(me, feeder);
    }

    /// <summary>
    /// **A party member leaves alone what Mesmerize holds for a friend**.
    /// Only for a looker with the party flag that is a player-type thing: when the
    /// target's mind is mesmerised (mode 10), the mind's own target — which the way in
    /// sets to the mesmeriser (<see cref="Mesmerize"/>) — is looked up; if it is an actor
    /// or a player in the same world as the target and the looker counts it a friend,
    /// the qualifier answers yes. A companion does not shoot the Cappadocian Christof has just
    /// mesmerised; an enemy's own party rule does not exist, so an enemy still would.
    /// </summary>
    public static bool HeldForAFriend(WorldThing me, WorldThing target)
    {
        if (me.Actor is not { IsPlayerThing: true } mine || !mine.Has(Actor.Flag.Party)) return false;
        if (target.Mind is not { Mode: AiMode.Mesmerized } held) return false;
        var theirs = held.Target is { Type: ThingType.Actor or ThingType.Player } t
                     && ReferenceEquals(t.Place, target.Place) ? t : null;
        return Sight.Friendly(me, theirs);
    }
}
