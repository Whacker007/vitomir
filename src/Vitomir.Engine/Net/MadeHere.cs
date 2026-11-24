// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a player's machine makes for itself. 1.1's host sends nothing flagged `NO NET SYNCH`,
// because every player's machine makes those things itself: it deals the host's blows
// and deaths again for their looks (message 0xd, 0xf), throws a gib
// kill's chunks (0x1f with 0x1b), strikes a head off (0x1f with 0x19) and runs
// every effect's script. Vitomir's players' machines do all five (the effects'
// scripts); the things the host's own copies of those make are kept back here.
using System.Runtime.CompilerServices;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **The host's side of what each machine makes for itself.** A blow's wound effects and loose chunk,
/// a death's chunks and wound effects, a gib kill's sixteen chunks and a beheading's head
/// and neck blood are made on the host inside <see cref="Everywhere"/>, which marks every thing so made
/// that carries `NO NET SYNCH` (0x100) as one each machine makes for itself; <see cref="NetSynch.Kept"/> then keeps
/// it back — never announced, flown, walked, caught up or taken out on the players' machines, as 1.1 keeps back
/// every such thing.
/// The gib kill and the beheading are told as they happen (<see cref="Gibbed"/>, <see cref="Beheaded"/>: 1.1's
/// message 0x1f) and go out before the things removed (<see cref="Take"/>), so a player's machine still
/// has the body to throw its chunks off.
/// <para>
/// The effects' scripts run on every player's machine too (`CodexScenes.PlayersMachine`), so the flagged things an
/// effect's script makes on the host are marked the same way (<see cref="ByEffect"/>). What any other script makes
/// flagged — a place's, a thing's (`ConveyerHold`'s `blood`, `MysticSpawner`'s magic), an item's (a player's machine
/// tells an item's script nothing as it is worn, so the torch's `torchFlame_nosave` is made on the host
/// alone) — is marked too (<see cref="ByScript"/>) and kept back as 1.1 keeps it, so no player's machine has it: on a
/// player's screen another character's lit torch is dark. The Fix `netprops` sends those (<see cref="NetSynch.Kept"/>).
/// </para>
/// </summary>
public static class MadeHere
{
    static readonly ConditionalWeakTable<WorldThing, object> own = new();
    static readonly ConditionalWeakTable<WorldThing, object> scripted = new();
    static readonly ConditionalWeakTable<WorldThing, Death> deaths = new();
    static readonly ConditionalWeakTable<Location, List<byte[]>> told = new();
    static readonly object Mark = new();

    sealed record Death(bool Whole, bool Dusted);

    /// <summary>
    /// Whether <paramref name="thing"/> was made on the host by something every player's machine does for itself
    /// (<see cref="Everywhere"/>), so that machine has its own.
    /// </summary>
    public static bool Own(WorldThing thing) => own.TryGetValue(thing, out _);

    /// <summary>
    /// Whether <paramref name="thing"/> was made flagged `NO NET SYNCH` by a script (<see cref="ByScript"/>) — a
    /// place's, a thing's, an item's or an effect's. 1.1's host sends none of them; an
    /// effect's is also <see cref="Own"/>, every player's machine making its own.
    /// </summary>
    public static bool Scripted(WorldThing thing) => scripted.TryGetValue(thing, out _);

    /// <summary>
    /// A script making a thing in <paramref name="place"/> — `SpawnThing`, `SpawnThingNear`,
    /// `CreateThing`: what is made meanwhile that carries `NO NET SYNCH` is marked <see cref="Scripted"/>, which
    /// <see cref="NetSynch.Kept"/> keeps back unless the Fix `netprops` is on. Marked on any machine, as
    /// <see cref="ByEffect"/> is, so a thing made before the place was hosted stays out of a newcomer's catch-up
    /// too.
    /// </summary>
    public static Making ByScript(Location? place) => new(place, script: true);

    /// <summary>
    /// A stretch of the host's work every player's machine does again for itself — around a blow's wound
    /// effects, a death's, a gib kill's chunks, a head struck off. Each thing made in <paramref name="about"/>'s
    /// place meanwhile that carries `NO NET SYNCH` is marked <see cref="Own"/> as the stretch ends. Nothing on a
    /// machine that is not a network host.
    /// </summary>
    public static Making Everywhere(WorldThing about) =>
        new(about.Place is { } place && NetOrders.Mode(place) == NetMode.Server ? place : null);

    /// <summary>
    /// A stretch of an effect's script on the host — its `effectstarted`, its `effectended`
    /// and its timers — which every player's machine runs again for itself, since
    /// the effect reaches it (the script made there too): what the script makes
    /// flagged `NO NET SYNCH` — the `EffectDisc…` pads, rings and glows — is marked <see cref="Own"/> and kept back.
    /// Only for an effect that goes to every player's machine — a row flagged `NOT SYNCED` (0x80) or `OWNER ONLY`
    /// (0x40) runs its script on the host alone or on one machine besides, so what it makes is not the machines' own:
    /// it is kept back as any script's is (<see cref="Scripted"/>), and sent with the Fix `netprops`. No shipped row
    /// with a script carries either flag (`effects.ned`).
    /// Marked on any machine, not only a host's: a pad made before the place was hosted — an effect put on as the
    /// place woke — is then kept out of a newcomer's catch-up too, its machine making its own
    /// as the effect reaches it.
    /// </summary>
    public static Making ByEffect(WorldThing on, Rpg.Effect effect) =>
        effect.Definition.Has(Formats.EffectFlags.NotSynced) || effect.Definition.Has(Formats.EffectFlags.OwnerOnly)
            ? default : new(on.Place);

    /// <summary>
    /// A thing <paramref name="effect"/>'s script had made, **made again by a load** under the number it had (the
    /// repair <c>loadedeffects</c>, <c>Save.LoadedEffects</c>): marked as it was when the script made it — a
    /// script's (<see cref="ByScript"/>), and every machine's own when the effect goes to every machine
    /// (<see cref="ByEffect"/>) — so a host keeps it back from the players' machines exactly as before the save.
    /// The stretches themselves cannot mark it: they go by the counter, and its number is an old one.
    /// </summary>
    public static void Again(Rpg.Effect effect, WorldThing made)
    {
        if ((made.Flags & WorldThing.Flag.NoNetSync) == 0) return;
        scripted.AddOrUpdate(made, Mark);
        if (!effect.Definition.Has(Formats.EffectFlags.NotSynced) && !effect.Definition.Has(Formats.EffectFlags.OwnerOnly))
            own.AddOrUpdate(made, Mark);
    }

    /// <summary>The stretch <see cref="Everywhere"/> opens: the place's GUID counter as it began.</summary>
    public readonly struct Making : IDisposable
    {
        readonly Location? place;
        readonly int from;
        readonly bool script;

        internal Making(Location? place, bool script = false)
        {
            this.place = place;
            this.script = script;
            from = place?.NextGuid ?? 0;
        }

        public void Dispose()
        {
            if (place is null) return;
            // The counter hands numbers out in order, so what was made meanwhile is what now has one
            // at or past where it stood. A host's numbers carry 0x40000000 (`ThingGuids.HostBit`); the counter not.
            foreach (var t in place.Things)
                if (ThingGuids.Raw(t.Guid) >= from && (t.Flags & WorldThing.Flag.NoNetSync) != 0)
                    (script ? scripted : own).AddOrUpdate(t, Mark);
        }
    }

    /// <summary>
    /// How a death's effects went on the host, for the death's message to say (<see cref="SeenBlow.Whole"/>,
    /// <see cref="SeenBlow.Dusted"/>): <paramref name="whole"/> false for a character killed in mid-air,
    /// <paramref name="dusted"/> for one turned to dust.
    /// </summary>
    public static void Died(WorldThing body, bool whole, bool dusted)
    {
        if (body.Place is { } place && NetOrders.Mode(place) == NetMode.Server) deaths.AddOrUpdate(body, new Death(whole, dusted));
    }

    /// <summary>What <see cref="Died"/> recorded of <paramref name="body"/>'s death: whole and not dusted when nothing was.</summary>
    public static (bool Whole, bool Dusted) DeathOf(WorldThing body) =>
        deaths.TryGetValue(body, out var d) ? (d.Whole, d.Dusted) : (true, false);

    /// <summary>
    /// A body gibbed on the host (the gib's message with 0x1b when this is the host, sent before the
    /// no-gore test — so every machine is told, whatever the host's own setting): <see cref="Msg.Gibbed"/>.
    /// </summary>
    public static void Gibbed(WorldThing body) => Tell(body.Place, new NetWriter(Msg.Gibbed).I32(body.Guid).ToArray());

    /// <summary>
    /// A head struck off on the host (the message with 0x19, the victim and the attacker, once the head is made and the
    /// body is headless): <see cref="Msg.Beheaded"/>.
    /// </summary>
    public static void Beheaded(WorldThing victim, WorldThing? attacker) =>
        Tell(victim.Place, new NetWriter(Msg.Beheaded).I32(victim.Guid).I32(attacker?.Guid ?? 0).ToArray());

    /// <summary>A message for the players' machines in <paramref name="place"/>, sent before the things removed (<see cref="Take"/>); nothing when it is not a host's.</summary>
    internal static void Tell(Location? place, byte[] message)
    {
        if (place is null || HostSeen.For(place) is null) return;
        told.GetOrCreateValue(place).Add(message);
    }

    /// <summary>
    /// The messages told in <paramref name="place"/> since the host last sent, taken — sent before the things
    /// removed, so the gibbed body is still on the players' machines when its message comes (1.1 sends 0x1b as the
    /// death effects run, before the body goes and before the death's own 0xf).
    /// </summary>
    public static List<byte[]> Take(Location place)
    {
        if (!told.TryGetValue(place, out var list) || list.Count == 0) return [];
        var taken = list.ToList();
        list.Clear();
        return taken;
    }
}
