// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the host keeps to itself: a thing flagged `NO NET SYNCH`. In play, the
// Storyteller's ghost, put on the floor by the seat's right button, is "a huge grey bald head" — and 1.1's
// host draws the same head, but 1.1's players never see it.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **The thing flag `NO NET SYNCH`** (the SDK's `noteditordata.txt`). 1.1's host tests it
/// before every message that tells the players' machines of a thing — **made** (message 3: the
/// test skips the whole message), **walking** (message 9), **removed**
/// (message 0x10) and the newcomer's **catch-up** —
/// so a thing so flagged lives on the host alone.
/// <para>
/// **The Storyteller's ghost** is one: the `Storyteller` template (`Global.not`, `storyteller.nod` — a head
/// 96 units tall, `STHead2`'s grey bald face, idling on `storytellerHEAD.nms`) carries `SAVE NEVER`,
/// `NO NET SYNCH` and `DISABLE COLLIDE` (0x301). On the host 1.1 draws it as any character: its render flags
/// are `DROP SHADOW | FULL SHADOW` (0xc, no `DON'T RENDER`), no writer of the render flags touches
/// it, its stealth is zeroed as a thing is made and no template tag sets it, so the
/// stealth fade (asked by the thing draw) gives it 1.0 — opaque — and in the seat
/// 1.0 whatever the stealth. Its material is a plain 24-bit texture, no alpha. So the
/// Storyteller sees the head the camera follows, and the players never do. Vitomir's host drew it the same;
/// its players' machines were sent it (<see cref="WorldWatch"/> announced every thing but a projectile).
/// </para>
/// </summary>
public static class NetSynch
{
    /// <summary>
    /// Whether the host keeps <paramref name="thing"/> to itself: flagged `NO NET SYNCH` and either a character
    /// (type 0 or 7) — of the shipped templates only the Storyteller's — or a thing every player's machine
    /// makes for itself (<see cref="MadeHere.Own"/>): a blow's blood and sparks and loose chunk, a death's, a gib
    /// kill's sixteen chunks, a severed head and its neck's blood — which each machine now makes from the host's
    /// blow, death, gib kill and beheading — and what an effect's script makes, which each player's machine's own copy of the script makes
    /// there (<see cref="MadeHere.ByEffect"/>: the discipline effects' pads, rings and glows); or a thing any other
    /// script made (<see cref="MadeHere.Scripted"/>) — a place's or a thing's (`ConveyerHold`'s blood,
    /// `MysticSpawner`'s magic), an item's (the torch's flame) — which no player's machine makes, and which 1.1's host
    /// does not send either, so on the players' screens it is not there. The Fix `netprops` (<see cref="Fixes.NetProps"/>,
    /// the host's switch: the place's own <see cref="Location.Fixes"/>, else the process's) sends those. An explosion
    /// or a muzzle flash takes the flag only as it goes (`Explosions.ExplosionSystem`). A kept projectile is not flown
    /// on the players' machines either.
    /// </summary>
    public static bool Kept(WorldThing thing) =>
        (thing.Flags & WorldThing.Flag.NoNetSync) != 0
        && (thing.Type is ThingType.Actor or ThingType.Player || MadeHere.Own(thing)
            || (MadeHere.Scripted(thing) && !(thing.Place?.Fixes ?? Fixes.Current).NetProps));
}
