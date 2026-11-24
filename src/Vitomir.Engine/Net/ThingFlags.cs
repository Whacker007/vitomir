// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// 1.1's message 4: a thing's flags and, by its type, a few numbers of
// its own. The host sends it from the end of the generic thing tick for a thing marked
// `THING_FLAG_MODIFIED` (0x20000000) — which the flag setters mark, with 0x80000000, on a host — and from
// `SetItemFlags`/`ClearItemFlags` at once; the catch-up sends it for every thing.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// A thing's flags as message 4 carries them. Every thing: its type (a byte; the handler drops
/// a message whose type is not the thing's), its render flags but `REFLECTION ONLY` (bit 0x80 masked off), its
/// thing flags and its collide type (a byte). Then by type:
/// <list type="bullet">
/// <item>a character (0, 7): its actor flags and second flags, its aura
/// (<c>SetActorPredefinedAura</c>), its team and original team (set with their setters),
/// three base stats — blood (6), mana (0x11) and frenzy (8), each set through the
/// clamped setter as from the network — and its money;</item>
/// <item>an item or armour (1, 6): its count (which the handler writes into the same word) and its item
/// flags;</item>
/// <item>a weapon (5): the two attack records' loads (<see cref="WeaponAttack.Ammo"/>) and its
/// item flags — sent unreliably from the tick (the send's reliable argument 0);</item>
/// <item>a container (8): its state word (bit 0 opened).</item>
/// </list>
/// **What makes it go** is 1.1's: the flags (render, thing, collide), a character's flags, aura, teams and money, and
/// an item's flags are what the setters mark (each setter of those marks the thing for the next message) — <see cref="Trigger"/>; the stats, the count, the loads and a
/// container's state are not marked by what changes them (the stat setter marks second flag 0x10000000, the owner's own
/// stats message 6, instead; the open sets the opened bit and `NO HIGHLIGHT` with no mark), so they **ride**
/// on the next message and a player's machine holds them as they stood then — as 1.1's does.
/// </summary>
public readonly record struct ThingFlags(int Guid, byte Kind, uint Render, uint Thing, byte Collide,
                                         uint Actor, uint Actor2, byte Aura, byte Team, byte OriginalTeam,
                                         float Blood, float Mana, float Frenzy, int Cash,
                                         uint ItemFlags, int Amount, int Load1, int Load2, uint State)
{
    /// <summary>What follows the common part: nothing, a character's, an item's, a weapon's, a container's.</summary>
    public const byte None = 0, Character = 1, ItemKind = 2, WeaponKind = 3, ContainerKind = 4;

    /// <summary>The most bytes one takes on the wire (a character's).</summary>
    public const int Size = 4 + 1 + 4 + 4 + 1 + (4 + 4 + 1 + 1 + 1 + 4 + 4 + 4 + 4);

    /// <summary>Whether it is a character's, with actor flags, teams, stats and money.</summary>
    public bool IsActor => Kind == Character;

    /// <summary>
    /// `REFLECTION ONLY` (0x80): the first-person look puts it on the character whose eyes it looks
    /// through (<see cref="WorldThing.RenderFlags"/>), which is that machine's own business — never sent
    /// (the send masks it off, `&amp; 0xffffff7f`), and never taken from the host (ours).
    /// </summary>
    public const uint LocalRender = 0x80;

    /// <summary>
    /// The thing flags that are each machine's own bookkeeping — animating, marked for removal,
    /// targetted, finalised and the save's three "modified" bits (<see cref="WorldThing.Flag"/>) —
    /// never sent and never taken (ours).
    /// </summary>
    public const uint LocalThing = WorldThing.Flag.Animating | WorldThing.Flag.Remove | WorldThing.Flag.Targetted
                                   | WorldThing.Flag.Finalized | WorldThing.Flag.Modified
                                   | WorldThing.Flag.FramesModified | WorldThing.Flag.EverModified;

    /// <summary>
    /// The second actor flags a conversation running on a player's machine sets there — in a
    /// conversation, the talking hands, speaking (<see cref="World.Actor.Flag2"/>) — which that machine
    /// keeps (ours: 1.1 runs a player's conversation on their machine too).
    /// </summary>
    public const uint LocalActor2 = World.Actor.Flag2.Conversation | World.Actor.Flag2.UsingHands | World.Actor.Flag2.Speaking;

    /// <summary>
    /// The part whose change sends it — what 1.1's setters mark — with the numbers that only ride zeroed. The host
    /// sends a thing's flags when this differs from what it last sent (<see cref="HostSeen.Changes"/>).
    /// </summary>
    public ThingFlags Trigger() => this with { Blood = 0, Mana = 0, Frenzy = 0, Amount = 0, Load1 = 0, Load2 = 0, State = 0 };

    public static ThingFlags Of(WorldThing t)
    {
        var f = new ThingFlags { Guid = t.Guid, Render = t.RenderFlags & ~LocalRender, Thing = t.Flags & ~LocalThing,
                                 Collide = (byte)t.Collide };
        if (t.Actor is { } a)
            return f with
            {
                Kind = Character, Actor = a.Flags, Actor2 = a.Flags2 & ~LocalActor2, Aura = (byte)a.Aura,
                Team = (byte)a.Team, OriginalTeam = (byte)a.OriginalTeam,
                Blood = a.BaseStat[(int)ActorStat.Blood], Mana = a.BaseStat[(int)ActorStat.Mana],
                Frenzy = a.BaseStat[(int)ActorStat.Frenzy], Cash = a.Cash,               // A network game's own purse
            };
        if (t.Type == ThingType.Weapon && t.Item is { Weapon: { } w } weapon)
            return f with { Kind = WeaponKind, Load1 = w.Attacks[0].Ammo, Load2 = w.Attacks[1].Ammo, ItemFlags = (uint)weapon.Flags };
        if (t.Type is ThingType.Item or ThingType.Armor && t.Item is { } item)
            return f with { Kind = ItemKind, Amount = item.Amount, ItemFlags = (uint)item.Flags };
        if (t.Type == ThingType.Container) return f with { Kind = ContainerKind, State = t.State };
        return f;
    }

    public void Write(NetWriter w)
    {
        w.I32(Guid).U8(Kind).U32(Render).U32(Thing).U8(Collide);
        switch (Kind)
        {
            case Character:
                w.U32(Actor).U32(Actor2).U8(Aura).U8(Team).U8(OriginalTeam).F32(Blood).F32(Mana).F32(Frenzy).I32(Cash);
                break;
            case ItemKind: w.I32(Amount).U32(ItemFlags); break;
            case WeaponKind: w.I32(Load1).I32(Load2).U32(ItemFlags); break;
            case ContainerKind: w.U32(State); break;
        }
    }

    public static ThingFlags Read(NetReader r)
    {
        var f = new ThingFlags { Guid = r.I32(), Kind = r.U8(), Render = r.U32(), Thing = r.U32(), Collide = r.U8() };
        return f.Kind switch
        {
            Character => f with
            {
                Actor = r.U32(), Actor2 = r.U32(), Aura = r.U8(), Team = r.U8(), OriginalTeam = r.U8(),
                Blood = r.F32(), Mana = r.F32(), Frenzy = r.F32(), Cash = r.I32(),
            },
            ItemKind => f with { Amount = r.I32(), ItemFlags = r.U32() },
            WeaponKind => f with { Load1 = r.I32(), Load2 = r.I32(), ItemFlags = r.U32() },
            ContainerKind => f with { State = r.U32() },
            _ => f,
        };
    }
}
