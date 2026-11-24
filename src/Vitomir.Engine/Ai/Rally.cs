// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The rally: whom the companions are given when one of them is attacked.
// Moved here from Minds.cs, when a comparison with 1.1
// found the pick carried through the loop and the draw taken from the whole flood.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ai;

public sealed partial class Minds
{
    /// <summary>
    /// The party half of AI event 9 (the rally): somebody has attacked a party member,
    /// so the party piles in. The gate in front of it is narrow — the **sender** must be an
    /// `enemysearch` character by its home mode and its target must be a `THING_TYPE_PLAYER` carrying
    /// `THING_AF_PARTY` — and what is handed over is the **listener's** own thing, which is why the
    /// first thing this does is refuse a party member: the shout sweeps 500 units and offers the
    /// victim's party every non-party character near the fight, one call at a time.
    ///
    /// Then, for each party slot in order: skip one with no mind,
    /// one that is not in the party, one on **Defense** stance unless it is the victim itself, and one
    /// that is **feeding or being fed on**. The selected member is given the current pick with no
    /// further questions. Everybody else must have no target already — unless the victim
    /// is **hurt**, which overrides that — and must be within **1,250 units** of the victim.
    ///
    /// **There is one pick for the whole rally**. It starts as the attacker, and
    /// the loop keeps it: whatever a member is sent after becomes what the members after it — the
    /// selected one included — are offered. Earlier, Vitomir started every member from the attacker.
    ///
    /// **And a member may move it.** One that is neither the victim nor responding to a hurt one,
    /// two times in three (0.66), draws **a random entry from everything within 750 units
    /// of the pick** — the sphere query, made **once**, round the pick of the
    /// first member to get this far, and kept for the rest of the loop (a flag). The
    /// index is `rand × (1/32767) × count`, truncated; the pick moves only if that
    /// entry is a character with a mind of its own whose home mode is `enemysearch` (its home
    /// mode is read). Everything the query finds is in the draw — the pick itself, the party, a
    /// villager, a door with a collision shape — so in a crowd the pick seldom moves. Until then
    /// Vitomir drew among the searchers alone, and moved it whenever there was one.
    ///
    /// One oddity, kept as 1.1 has it: the **can-I-see-it test is run from the victim's mind, not the
    /// member's** (the test is made with the incoming mind, while the target is set on the
    /// member's). So the party takes the victim's word for what is there. The test is the whole of
    /// the sight test, not only the line: the victim's perception against the pick's stealth first, so an
    /// attacker hidden from the victim — Obfuscate, a hidden archer — brings nobody in; until then
    /// Vitomir asked only the line. A member whose pick the
    /// victim cannot see falls back on the attacker, when the victim can see that — and the
    /// attacker is then the pick for the rest; one that can see neither is given nothing, and the pick
    /// it drew stays the pick.
    ///
    /// **A member is given its pick by the whole set-target** (<see cref="SetTarget"/>):
    /// the pick is checked and a dead one is stored as nobody — the dead hear the shout, so a
    /// corpse near the fight is a listener like any other, and the selected member's target goes to nobody
    /// when a dead listener comes last; a member that has never reacted reacts; its pets are
    /// handed the pick. Earlier, Vitomir stored the pick alone (<see cref="Mind.Aim"/>), so a steered
    /// Christof kept a corpse as his target and a companion never reacted to a rally.
    ///
    /// <paramref name="forced"/> is whether the call is forced, which only the player's own orders
    /// ever set — Ctrl on an attack or on walking into range to cast. It counts
    /// as the victim being hurt, and on its own it also brings in a member that is feeding
    /// (the forcing itself, not the hurt test).
    /// </summary>
    public void Rally(Mind victim, WorldThing enemy, bool forced = false)
    {
        if (enemy.Type is not (ThingType.Actor or ThingType.Player)) return;
        if (enemy.Type == ThingType.Player && enemy.Actor is { } e && e.Has(World.Actor.Flag.Party)) return;
        if (victim.Thing.Actor is not { } hurt) return;

        bool urgent = forced || Hurting(hurt);
        var pick = enemy;
        List<WorldThing>? near = null;

        foreach (var member in PartySlots())
        {
            if (member.Thing.Actor is not { } them || !them.Has(World.Actor.Flag.Party)) continue;
            // Somebody out of formation does not answer. `InParty` is the second test in
            // the rally's own filter, ahead of the stance one, and the manual puts it in the same
            // breath as the follow: *"when a coterie member is separated from your group, s/he will
            // not come to the lead character's aid if he is damaged."*
            if (!them.Has2(World.Actor.Flag2.InFormation)) continue;
            if (them.Stance == PartyStance.Defense && !ReferenceEquals(member.Thing, victim.Thing)) continue;
            if (!forced && them.Action is Rpg.ActionMode.Feeding or Rpg.ActionMode.WristFeeding) continue;

            // The selected member takes the pick outright (compared with the party's selected member).
            if (ReferenceEquals(member.Thing, Selected)) { SetTarget(member, pick); continue; }   // the set-target in full

            if (member.Target is not null && !urgent) continue;
            if (Vector3.Distance(member.Thing.Position, victim.Thing.Position) > Rules.GameRules.Current.Ai.RallyRange[0]) continue;

            if (!ReferenceEquals(member.Thing, victim.Thing) && !urgent && Random.Unit() < Rules.GameRules.Current.Ai.RallyRange[1])
            {
                near ??= Flood(pick);
                int drawn = (int)(Random.Unit() * near.Count);
                // An index of the count itself (the roll at its maximum) or a draw from an empty
                // list reads past what the query wrote in the engine; here it moves nothing (ours).
                if (drawn < near.Count && near[drawn].Mind is { BaseMode: AiMode.EnemySearch })
                    pick = near[drawn];
            }

            // The sight test with its range flag clear (it is 0 through the whole rally): a character,
            // alive and here, whose stealth is within the victim's perception, and then the line —
            // so an attacker hidden from the victim brings nobody in (`MadeOut`).
            if (!MadeOut(victim, pick))
            {
                if (ReferenceEquals(pick, enemy)) continue;
                if (!MadeOut(victim, enemy)) continue;
                pick = enemy;
            }
            SetTarget(member, pick);
        }
    }

    /// <summary>
    /// The party's minds in slot order — the leader first, then the others as they joined
    /// (<see cref="Location.PartyMembers"/>) — which is the order the rally walks the party's
    /// members, and so the order the pick is carried in. With no place, every mind in the party.
    /// </summary>
    IEnumerable<Mind> PartySlots()
    {
        if (where is null)
        {
            foreach (var m in All.Where(m => m.Thing.Actor?.Has(World.Actor.Flag.Party) == true).ToArray())
                yield return m;
            yield break;
        }
        foreach (var t in where.PartyMembers().ToArray())
            if (t.Mind is { } m) yield return m;
    }

    /// <summary>
    /// Everything the sphere query finds within <see cref="RallyNearby"/> of <paramref name="pick"/>,
    /// in its place's sector (the sphere query over the flood, asked with the pick's
    /// position and sector) — <see cref="Explosions.ExplosionDamage.Gather"/>, the one query
    /// every caller of it shares. The query entries all carry bit 2, the "is a thing" bit the draw
    /// tests (both of its writers set it). With no place, every thing
    /// within the distance (ours).
    /// </summary>
    List<WorldThing> Flood(WorldThing pick)
    {
        if (where is not null)
            return Explosions.ExplosionDamage.Gather(where, pick.Position, Rules.GameRules.Current.Ai.RallyRange[2], pick.Sector)
                             .Select(r => r.Thing).ToList();
        return Everyone.Where(t => Vector3.Distance(t.Position, pick.Position) <= Rules.GameRules.Current.Ai.RallyRange[2]).ToList();
    }
}
