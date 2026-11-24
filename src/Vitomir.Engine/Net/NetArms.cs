// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The network arms of pieces built for single player: the special-move throw's broadcast
// and its replay on the players' machines (message 0xc, read on each player's
// machine and replayed through the same strike), dropping money from a player's machine (the drop's
// client arm, message 0x1d), and a player's conversation abandoned by the place changing (the
// conversation's client arm, which tells the host it ended).
// Held automatic fire's arm is the order 0x1e (`NetOrders.Forward`, `Rpg.HeldFire`). The engine
// code asks here; the host and each player's machine answer for the place they are playing.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// Where the engine's single-player code finds the network game its place belongs to — **per place,
/// not per process**, as <see cref="NetOrders"/> is, so a test can run a host and two players side by
/// side. A host's <see cref="HostArms"/> and a player's <see cref="ClientArms"/> register themselves
/// when made and answer for whichever place their game is playing.
/// </summary>
public static class NetArms
{
    static readonly List<WeakReference<object>> all = [];

    internal static void Register(object arms)
    {
        lock (all)
        {
            all.RemoveAll(w => !w.TryGetTarget(out _));
            all.Add(new WeakReference<object>(arms));
        }
    }

    static T? For<T>(Location? place) where T : class
    {
        if (place is null) return null;
        lock (all)
            foreach (var w in all)
                if (w.TryGetTarget(out var o) && o is T arms && Serves(o, place))
                    return arms;
        return null;
    }

    /// <summary>A host answers for every one of its places (each player in theirs, `HostPlaces`); a player's machine for the one it is playing.</summary>
    static bool Serves(object o, Location place) => o switch
    {
        HostArms h => ReferenceEquals(h.Place, place) || h.Serves(place),
        ClientArms c => ReferenceEquals(c.Place, place),
        _ => false,
    };

    /// <summary>
    /// A strike on the host that is not a throw (the strike's swing, on a host): the players in
    /// the place are told (message 0xc with its last byte 0) and each machine strikes again.
    /// Nothing elsewhere.
    /// </summary>
    public static void Struck(WorldThing attacker, WorldThing? target, Vector3 point, Weapon weapon) =>
        For<HostArms>(attacker.Place)?.Struck(attacker, target, point, weapon);

    /// <summary>
    /// A strike on the host has become a throw (the strike's throw branch, on a host): the
    /// players in the place are told (message 0xc with its last byte 1). Nothing elsewhere.
    /// </summary>
    public static void Threw(WorldThing thrower, WorldThing victim, Weapon weapon) =>
        For<HostArms>(thrower.Place)?.Threw(thrower, victim, weapon);

    /// <summary>
    /// On a player's machine, money dropped from the pack goes to the host as an amount (the drop's
    /// client arm) and is taken from this machine's purse at once, bag or no
    /// bag; answers true. Anywhere else answers false and the bag is made here.
    /// </summary>
    public static bool DropCash(WorldThing who, int amount) =>
        For<ClientArms>(who.Place) is { } c && c.DropCash(who, amount);

    /// <summary>
    /// What 1.1 does to a character before writing it back when its player leaves the game (the leave,
    /// then the character file's write), so the file holds it whole and at rest:
    /// <list type="number">
    /// <item>every effect off, worn gear's too;</item>
    /// <item>revived at full health and blood, dead or not, without the awakening's noise (full health and
    /// blood) — which puts back the effects a character is born with;</item>
    /// <item>every discipline's state byte and recast clock cleared;</item>
    /// <item>not busy, no action, the action animation stopped, no target;</item>
    /// <item>what is in the hand put into the pack, as the hand is emptied anywhere (put away, then
    /// unlinked from the hand) — left in the hand when the pack has no room.</item>
    /// </list>
    /// **Not built**: a step between the last two, which sets a field of each effect left on
    /// the character to 1.
    /// </summary>
    public static void TidyForLeaving(WorldThing body)
    {
        if (body.Actor is not { } me) return;
        EffectRuntime.RemoveAll(body, worn: true);
        Damage.Revive(body, 1f, 1f, sound: false);
        foreach (var d in me.Disciplines.All)
        {
            d.Active = false;
            d.RecastUntil = 0;
        }
        me.BusyUntil = 0;
        me.Action = ActionMode.None;
        body.Animation?.StopAction();
        me.Target = null;
        if (me.Held is { } held)
        {
            Ground.ClearHand(body);
            if (Stow.Put(body, held, steering: false) is StowResult.Refused) Ground.Hand(body, held);
        }
    }
}

/// <summary>The host's side of <see cref="NetArms"/>.</summary>
public sealed class HostArms
{
    readonly GameServer server;

    public HostArms(GameServer server)
    {
        this.server = server;
        NetArms.Register(this);
        // A player's conversation the journey left behind and their machine never reported (ours):
        // let go when they arrive — before the scripts hear `joined` and `beginscene`, which may start
        // another (this handler is the first registered).
        server.Entered += (p, _) => p.Talking = null;
        // A player leaving, dropped or kicked has their teleporter pairs shut — by
        // `HostPlaces`, which knows the place each player is in.
    }

    internal Location? Place => server.Place;

    /// <summary>Whether a place is one of the host's (its own or one a player has gone to alone).</summary>
    internal bool Serves(Location place) => server.Places.Of(place) is not null;

    /// <summary>
    /// Message 0xc for a strike that is not a throw (its last byte 0): the
    /// attacker's GUID, the target's (0 for a strike at a point), the weapon's (its item's thing — a
    /// character's own fists have none here, and none that another machine could find in 1.1 either),
    /// the action point, the weapon's current attack and 0, to the players in
    /// the attacker's place. **Ours**: a weapon swapped in by `OverrideActorWeapon` (the
    /// claws) is a thing of its own in 1.1, found by its GUID; here it is made from its template, so the
    /// template rides with the message.
    /// </summary>
    internal void Struck(WorldThing attacker, WorldThing? target, Vector3 point, Weapon weapon)
    {
        if (attacker.Actor is not { } me) return;
        int item = me.Held is { Weapon: { } w, Thing: { } thing } && ReferenceEquals(w, weapon) ? thing.Guid : 0;
        string swapped = WeaponOverride.Active(me) && item == 0 ? weapon.Name : "";
        var message = new NetWriter(Msg.Struck).I32(attacker.Guid).I32(target?.Guid ?? 0).I32(item)
            .Vec(target is null ? point : me.ActionPoint).U8((byte)weapon.CurrentAttack).U8(0).Str(swapped).ToArray();
        foreach (var p in server.Players.Where(p => !p.Host && p.Ready && p.Avatar?.Place == attacker.Place))
            server.Reliable(p, message);
    }

    /// <summary>
    /// Message 0xc: the thrower's and the victim's GUIDs, the weapon's, the thrower's action
    /// point, its weapon's current attack and 1 — the strike was a throw — sent to
    /// the players in the thrower's place. **Ours**: the throw's type and distance go with
    /// it, so a player's machine whose copy of the thrower does not hold the host's weapon throws alike.
    /// </summary>
    internal void Threw(WorldThing thrower, WorldThing victim, Weapon weapon)
    {
        if (thrower.Actor is not { } me) return;
        var w = new NetWriter(Msg.Thrown).I32(thrower.Guid).I32(victim.Guid).I32(me.Held?.Thing?.Guid ?? 0)
            .Vec(me.ActionPoint).U8((byte)weapon.CurrentAttack).U8(1).U8((byte)weapon.ThrowType).F32(weapon.ThrowDistance);
        var message = w.ToArray();
        foreach (var p in server.Players.Where(p => !p.Host && p.Ready && p.Avatar?.Place == thrower.Place))
            server.Reliable(p, message);
        server.Note($"net: {thrower.Template} throws {victim.Template}");
    }

    /// <summary>
    /// A player's message 0x1d: the amount dropped from their character's purse as a bag
    /// at their feet through the ordinary drop (on the slot's character, which must be in a
    /// place). A purse that does not cover it drops nothing.
    /// </summary>
    internal void DropCash(NetPlayer p, NetReader r)
    {
        int amount = r.I32();
        if (p.Avatar is not { InWorld: true } body) return;
        bool dropped = Ui.DropCash.Drop(body, amount);
        server.Note($"net: {p.Name} drops {amount} — {(dropped ? "a bag" : "not covered")}");
    }
}

/// <summary>A player's machine's side of <see cref="NetArms"/>.</summary>
public sealed class ClientArms
{
    readonly GameClient client;
    readonly Action<byte[]> send;

    public ClientArms(GameClient client, Action<byte[]> send)
    {
        this.client = client;
        this.send = send;
        NetArms.Register(this);
    }

    internal Location? Place => client.Mirror?.Place;

    /// <summary>What happened, for the console and the tests.</summary>
    public event Action<string>? Said;

    /// <summary>
    /// Message 0xc read as a throw (the strike replayed as one): the thrower must be a character or a
    /// party member; its weapon's current attack is put as the host's (clamped to 0 or 1, and only when
    /// the weapon has that many), and — the last byte being 1 — the strike is the throw with the roll
    /// skipped: the grab itself, the two turned to each other, the victim put in front of the thrower
    /// and restrained, and both motions played (<see cref="SpecialMove.Grab"/>). **Ours**: the weapon is
    /// not swapped for the host's — the throw's type and distance come in the message.
    /// </summary>
    internal void Thrown(NetReader r, WorldMirror? mirror)
    {
        int thrower = r.I32(), victim = r.I32();
        r.I32();                                  // the weapon's GUID
        var point = r.Vec();
        int attack = Math.Min((int)r.U8(), 1);
        bool threw = r.U8() != 0;
        int type = r.U8();
        float distance = r.F32();
        if (mirror?.Find(thrower) is not { Type: ThingType.Actor or ThingType.Player, Actor: { } me } body) return;
        client.Strikes.Thrown(body);                // a swing of hers guessed here is not what the host did
        if (me.Weapon is { } w && attack < w.AttackCount) w.CurrentAttack = attack;
        me.ActionPoint = point;
        if (!threw || mirror.Find(victim) is not { } them) return;
        var result = SpecialMove.Grab(body, them, type, distance, mirror.Place.Milliseconds);
        // The host's next snapshot carries the two motions the grab has just started here: they are
        // not started a second time (`WorldMirror.Replayed`).
        mirror.Replayed(body);
        mirror.Replayed(them);
        Said?.Invoke($"throw: {body.Template} throws {them.Template} — {result}");
    }

    /// <summary>
    /// Message 0xc read for a strike that is not a throw (the strike replayed as a swing): the attacker must
    /// be a character; when the host's weapon is not the one this machine's copy holds, it is swapped in
    /// (after the swap in force is cancelled, when there is one); its current
    /// attack is put as the host's (clamped to 0 or 1, and only when the weapon has that many); a
    /// target that is a character or a container is struck (and marked targetted, thing flag 0x2000000),
    /// and with none the point is. The strike is this machine's own: the walk stopped, the turn, the
    /// swing and its noise, the war cry. **Not** the throw's roll: 1.1 rolls it again here (it
    /// skips the roll only for a replayed throw), so a player's machine could throw where the host swung;
    /// a player's machine never rolls it (<see cref="World.Repairs.HostThrows"/> in
    /// <see cref="SpecialMove.Try"/>) and throws only when the host's throw comes (<see cref="Thrown"/>).
    /// The host's next snapshot does not start the swing again (<see cref="WorldMirror.Replayed"/>).
    /// **Ours**: a weapon found by its item GUID — one of the character's own items, which the kit
    /// already keeps in step — is put in the hand the ordinary way, not as a swap; 1.1 swaps it in with
    /// the weapon swap, which here would leave a later kit unable to change the hand (the kit follows the
    /// snapshot, where 1.1's 0x13 comes at once). A swapped-in weapon named by its template (the
    /// claws) is made here and swapped in as 1.1 does.
    /// </summary>
    internal void Struck(NetReader r, WorldMirror? mirror)
    {
        int attacker = r.I32(), target = r.I32(), item = r.I32();
        var point = r.Vec();
        int attack = Math.Min((int)r.U8(), 1);
        r.U8();                                   // 0: not a throw
        string swapped = r.Str();
        if (mirror?.Find(attacker) is not { Type: ThingType.Actor or ThingType.Player, Actor: { } me } body) return;
        var place = mirror.Place;
        if (item != 0 && mirror.Find(item)?.Item?.Weapon is { } own && !ReferenceEquals(own, me.Weapon))
        {
            WeaponOverride.Cancel(body);
            me.Weapon = own;
            body.Locomotion?.Reanimate();
        }
        else if (swapped.Length > 0 && !string.Equals(me.Weapon?.Name, swapped, StringComparison.OrdinalIgnoreCase)
                 && place.Templates is { } templates && templates.Has(swapped) && Weapon.From(swapped, templates.Resolve(swapped)) is { } claws)
        {
            WeaponOverride.Cancel(body);
            WeaponOverride.Override(body, claws, clearOffhand: true, hideFists: true);        // the swap, the off hand cleared and the fists hidden
        }
        if (me.Weapon is not { } weapon) return;
        if (attack < weapon.AttackCount) weapon.CurrentAttack = attack;
        var at = target != 0 ? mirror.Find(target) : null;
        if (at is { Type: ThingType.Actor or ThingType.Player or ThingType.Container } them)
        {
            them.Flags |= WorldThing.Flag.Targetted;
            me.Target = them;
            // Her own swing, started here at her click (OwnStrike), is borne out and not started again.
            if (client.Strikes.Struck(body, them)) Actions.StrikeStarted(body, them, weapon, place.Milliseconds, client.Strikes.SwingMs);
            else Actions.Strike(body, them, weapon, place.Milliseconds, place.Motions);
        }
        else
        {
            client.Strikes.Struck(body, null);      // a strike of hers at a point is not her swing guessed here
            me.Target = null;
            me.ActionPoint = point;
            Actions.StrikeAt(body, point, weapon, place.Milliseconds, place.Motions);
        }
        mirror.Replayed(body);
        Said?.Invoke($"strike: {body.Template} strikes {(target != 0 ? mirror.Find(target)?.Template ?? "nothing here" : "at a point")} with {weapon.Name}");
    }

    /// <summary>
    /// The amount sent (message 0x1d) and taken from the purse here —
    /// the character's own cash and, for this machine's character, the session's purse the pack pane
    /// shows, both of which the host's purse message puts (<see cref="ClientShops"/>).
    /// </summary>
    internal bool DropCash(WorldThing who, int amount)
    {
        send(new NetWriter(Msg.DropCash).I32(amount).ToArray());
        if (who.Actor is { } a) a.Cash -= amount;
        if (ReferenceEquals(who, client.Avatar) && who.Place?.Session is { } session) session.Gold -= amount;
        Said?.Invoke($"drop: {amount} asked of the host");
        return true;
    }

    /// <summary>
    /// The game is moving (`Msg.Travel`): a conversation running here is abandoned as a place change
    /// abandons one — torn down, and on a player's machine the host told it ended, aborted,
    /// with its code (for the script's `convended`). Before the world is let go.
    /// </summary>
    internal void Travelling(Conversations? talk)
    {
        if (talk?.Current is null || Place is not { } place) return;
        talk.Abandon(place);
        Said?.Invoke("conversation: abandoned by the journey");
    }
}
