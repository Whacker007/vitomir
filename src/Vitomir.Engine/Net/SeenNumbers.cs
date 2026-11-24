// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The typed half of message 4's handler (its switch on the thing's type) on a player's machine:
// what a character, an item, a weapon or a container takes from the host's flags message beside its flags.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **The handler's switch**. The handler first drops a
/// message whose type byte is not the thing's own; here a message of another kind than the thing is
/// (<see cref="Kind"/>) is dropped the same way.
/// </summary>
public static class SeenNumbers
{
    /// <summary>What <paramref name="t"/> is, in <see cref="ThingFlags"/>' kinds — which part of the message it takes.</summary>
    public static byte Kind(WorldThing t) => ThingFlags.Of(t).Kind;

    /// <summary>
    /// The typed part, onto this machine's copy:
    /// <list type="bullet">
    /// <item>a character: its teams, and — but on this player's own character, whose
    /// sheet and purse carry them (ours) — its actor flags and second flags (a conversation's bits kept), its aura,
    /// blood, mana and frenzy set through the clamped setter (as from the network: stat 6,
    /// 0x11, 8) and its money. 1.1's money setter on a player's machine hands a party character's money to the
    /// party's purse instead, which a network game never reads (it reads the character's own), so the purse is
    /// the character's own here;</item>
    /// <item>an item or armour: its count and item flags;</item>
    /// <item>a weapon: the two attack records' loads and its item flags;</item>
    /// <item>a container: its state word.</item>
    /// </list>
    /// </summary>
    public static void Apply(WorldThing t, ThingFlags f, bool mine)
    {
        if (f.Kind != Kind(t)) return;                                                   // The type must match
        switch (f.Kind)
        {
            case ThingFlags.Character when t.Actor is { } a:
                a.Team = f.Team;
                a.OriginalTeam = f.OriginalTeam;
                if (mine) return;
                a.Flags = f.Actor;
                a.Flags2 = (f.Actor2 & ~ThingFlags.LocalActor2) | (a.Flags2 & ThingFlags.LocalActor2);
                a.Aura = f.Aura;
                a.SetBaseStat((int)ActorStat.Blood, f.Blood);
                a.SetBaseStat((int)ActorStat.Mana, f.Mana);
                a.SetBaseStat((int)ActorStat.Frenzy, f.Frenzy);
                a.Cash = f.Cash;
                return;
            case ThingFlags.ItemKind when t.Item is { } item:
                item.Amount = f.Amount;
                item.Flags = (ItemFlags)f.ItemFlags;
                return;
            case ThingFlags.WeaponKind when t.Item is { Weapon: { } w } weapon:
                w.Attacks[0].Ammo = f.Load1;
                w.Attacks[1].Ammo = f.Load2;
                weapon.Flags = (ItemFlags)f.ItemFlags;
                return;
            case ThingFlags.ContainerKind:
                t.State = f.State;
                return;
        }
    }
}
