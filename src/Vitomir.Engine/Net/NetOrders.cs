// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The one place a client's orders leave for the host. In 1.1 every order function asks
// whether the network mode is 1 — "am I a client?" — once its own gates have passed, and if so sends the
// order to the server instead of carrying it out; the server's order handler then gives
// the same order to the same function. This file is that question and that send.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// 1.1's network mode: 2 on the server, 1 on a client (`NetIsServer`,
/// `NetIsClient`). Single player is Vitomir's name for "no network game at all".
/// </summary>
public enum NetMode { Single = 0, Client = 1, Server = 2 }

/// <summary>
/// The order kinds of 1.1's order message, which are the action modes the order
/// functions set on the actor; each is sent from its own function on a client and carried out
/// by the order handler's switch on the server.
/// </summary>
public enum OrderKind : byte
{
    /// <summary>0: stop — cancel and stop walking. Sent by the cancel and by Space's stop.</summary>
    Stop = 0,
    /// <summary>1: walk to a point, the move order.</summary>
    Move = 1,
    /// <summary>2: attack a thing or a point, with the attack's number and the weapon.</summary>
    Attack = 2,
    /// <summary>3: pick up — only an item, a weapon or armour, and only one in the world.</summary>
    Take = 3,
    /// <summary>4: use.</summary>
    Use = 4,
    /// <summary>7: feed — only on an actor or a player.</summary>
    Feed = 7,
    /// <summary>0xe: drop what is carried at a point, or into the world with no target.</summary>
    Drop = 0xe,
    /// <summary>0xf: pull a stake out.</summary>
    Unstake = 0xf,
    /// <summary>0x15: stop feeding — only while feeding (modes 8 and 11).</summary>
    StopFeeding = 0x15,
    /// <summary>0x1e: turn to aim, the one order sent unreliably.</summary>
    Aim = 0x1e,
}

/// <summary>
/// One order as 1.1's order message carries it: who, what, at whom, where, and the extras an attack carries; and (ours, protocol 21) the
/// order's number on a player's machine that predicts her own walk (<see cref="OwnWalk"/>), which the host answers with
/// <see cref="Msg.Taken"/> (0: not numbered, not answered; a recording made before it has none).
/// </summary>
public readonly record struct NetOrder(int Actor, OrderKind Kind, int Target, Vector3 Point, int Flags,
                                       byte AttackIndex = 0, int Weapon = 0, ushort Seq = 0)
{
    public void Write(NetWriter w) =>
        w.I32(Actor).U8((byte)Kind).I32(Target).Vec(Point).I32(Flags).U8(AttackIndex).I32(Weapon).U16(Seq);

    public static NetOrder Read(NetReader r) =>
        new(r.I32(), (OrderKind)r.U8(), r.I32(), r.Vec(), r.I32(), r.U8(), r.I32(), r.More ? r.U16() : (ushort)0);
}

/// <summary>
/// A discipline cast as 1.1's message 0x18 carries it: the caster, the target, a scroll
/// being read (an item of type 0xb, or none), the input's modifiers, the point, and the discipline's
/// name — the name, because each character has its own instance of it; and (ours, protocol 22) the cast's number on a
/// player's machine that predicts her own (<see cref="OwnCast"/>), from the same count as her orders' (<see cref="NetOrder.Seq"/>),
/// which the host answers with <see cref="Msg.Taken"/> and why (0: not numbered, not answered, as 1.1's is not).
/// </summary>
public readonly record struct NetCast(int Caster, int Target, int Scroll, int Mods, Vector3? Point, string Discipline, ushort Seq = 0)
{
    public void Write(NetWriter w)
    {
        w.I32(Caster).I32(Target).I32(Scroll).I32(Mods).Bool(Point.HasValue);
        if (Point is { } p) w.Vec(p);
        w.Str(Discipline).U16(Seq);
    }

    public static NetCast Read(NetReader r)
    {
        int caster = r.I32(), target = r.I32(), scroll = r.I32(), mods = r.I32();
        Vector3? point = r.Bool() ? r.Vec() : null;
        string discipline = r.Str();
        return new NetCast(caster, target, scroll, mods, point, discipline, r.More ? r.U16() : (ushort)0);
    }
}

/// <summary>What a player did in their own pack (the gesture, not its outcome).</summary>
public enum PackKind : byte
{
    /// <summary>A left click on the pane at a point: lift, drop, wear, swap (`InventoryHands.Click`; 1.1's 0x13 … 0x16).</summary>
    Click = 0,
    /// <summary>A left click on a grid the pane does not own — the quick-use strip (`InventoryHands.ClickIn`).</summary>
    ClickIn = 1,
    /// <summary>Shift and a right click: the quick equip or unequip (`InventoryHands.RightClick`, a QoL switch).</summary>
    QuickEquip = 2,
    /// <summary>What is in hand put back where it came from (`InventoryHands.PutBack`).</summary>
    PutBack = 3,
    /// <summary>An item used — drunk, read, eaten (`Items.Use`; 1.1's 0x17).</summary>
    Use = 4,
    /// <summary>A shop's drag starting: the item at a pane point, or a belt cell (slot <c>QuickUse</c>), lifted (`InventoryHands.ShopLift`, ours).</summary>
    ShopLift = 5,
    /// <summary>A shop's drag ending: the item in hand put down at a pane point, a belt cell, or nowhere (`InventoryHands.ShopDrop`, ours).</summary>
    ShopDrop = 6,
}

/// <summary>
/// A move in the pack as a player's machine sends it (ours: the gesture, replayed on the host by the
/// same `InventoryHands` over the same pane, where 1.1 sends each of the moves it makes of — take
/// 0x13, drop 0x14, wear 0x15, give 0x16, use 0x17; their bodies are read only as far as their
/// checks).
/// </summary>
public readonly record struct PackGesture(int Actor, PackKind Kind, int X = 0, int Y = 0, int Slot = 0, int Mods = 0, int Item = 0)
{
    public void Write(NetWriter w) => w.I32(Actor).U8((byte)Kind).I32(X).I32(Y).I32(Slot).I32(Mods).I32(Item);
    public static PackGesture Read(NetReader r) => new(r.I32(), (PackKind)r.U8(), r.I32(), r.I32(), r.I32(), r.I32(), r.I32());
}

/// <summary>Where a client's orders go (the client session).</summary>
public interface IOrderSink
{
    void Send(NetOrder order);
    /// <summary>A discipline cast (1.1's message 0x18).</summary>
    void Send(NetCast cast);
    /// <summary>A move in the pack (1.1's messages 0x13 … 0x17).</summary>
    void Send(PackGesture move);
    /// <summary>A vault move (1.1's message 0x16, <see cref="VaultMoves"/>). a sink that has none drops it.</summary>
    void Send(VaultMove move) { }
    /// <summary>Her own walk predicted on this machine (<see cref="OwnWalk"/>), or null.</summary>
    OwnWalk? Own => null;
    /// <summary>The start of her own swing predicted on this machine (<see cref="OwnStrike"/>), or null.</summary>
    OwnStrike? Strikes => null;
    /// <summary>The start of her own discipline predicted on this machine (<see cref="OwnCast"/>), or null.</summary>
    OwnCast? Casts => null;
}

/// <summary>
/// The client's side of the order functions. A place a network game is played in is registered
/// here with its mode — **per place, not per process**, so a test can run a host and two clients side
/// by side — and each order function asks <see cref="Forward"/> first.
/// </summary>
public static class NetOrders
{
    sealed class Entry(NetMode mode, IOrderSink? sink)
    {
        public NetMode Mode = mode;
        public IOrderSink? Sink = sink;
    }

    static readonly ConditionalWeakTable<Location, Entry> places = new();

    /// <summary>Registers a place as a client's (its orders go to <paramref name="sink"/>) or a server's.</summary>
    public static void Attach(Location place, NetMode mode, IOrderSink? sink = null)
    {
        places.AddOrUpdate(place, new Entry(mode, sink));
    }

    /// <summary>Forgets a place: it is single player again.</summary>
    public static void Detach(Location place) => places.Remove(place);

    /// <summary>The network mode of the place a thing is in (1.1's one network mode, but per place).</summary>
    public static NetMode Mode(Location? place) =>
        place is not null && places.TryGetValue(place, out var e) ? e.Mode : NetMode.Single;

    /// <summary>
    /// On a client, sends the order to the host and answers true — the caller then
    /// returns as though the order had been taken, which is what 1.1's do (the attack returns 4).
    /// Anywhere else answers false and the order is carried out here.
    /// </summary>
    public static bool Forward(WorldThing who, OrderKind kind, WorldThing? target = null, Vector3 point = default,
                               int flags = 0, byte attack = 0, WorldThing? weapon = null)
    {
        if (who.Place is not { } place || !places.TryGetValue(place, out var e) || e.Mode != NetMode.Client
            || e.Sink is not { } sink) return false;
        sink.Send(new NetOrder(who.Guid, kind, target?.Guid ?? 0, point, flags, attack, weapon?.Guid ?? 0));
        return true;
    }

    /// <summary>
    /// On a player's machine that predicts its own character (<see cref="OwnWalk"/>), the prediction when
    /// <paramref name="who"/> is her: the walk just forwarded is started here too. Null anywhere else.
    /// </summary>
    public static OwnWalk? Predicting(WorldThing who) =>
        who.Place is { } place && places.TryGetValue(place, out var e) && e.Mode == NetMode.Client
        && e.Sink?.Own is { } own && own.Is(who) ? own : null;

    /// <summary>
    /// On a player's machine that predicts its own character (<see cref="OwnStrike"/>), the start of her own swing when
    /// <paramref name="who"/> is her. Null anywhere else.
    /// </summary>
    public static OwnStrike? Strikes(WorldThing who) =>
        who.Place is { } place && places.TryGetValue(place, out var e) && e.Mode == NetMode.Client
        && e.Sink?.Strikes is { } own && own.Is(who) ? own : null;

    /// <summary>
    /// On a player's machine that predicts its own character (<see cref="OwnCast"/>), the start of her own discipline when
    /// <paramref name="who"/> is her. Null anywhere else.
    /// </summary>
    public static OwnCast? Casts(WorldThing who) =>
        who.Place is { } place && places.TryGetValue(place, out var e) && e.Mode == NetMode.Client
        && e.Sink?.Casts is { } own && own.Is(who) ? own : null;

    /// <summary>
    /// On a player's machine, tells the host about a move in the pack, and answers true; the move is
    /// made on this machine too, for the pane (ours — the host replays it and its next sheet is the
    /// word). Anywhere else answers false.
    /// </summary>
    public static bool TellPack(WorldThing who, PackGesture move)
    {
        if (who.Place is not { } place || !places.TryGetValue(place, out var e) || e.Mode != NetMode.Client
            || e.Sink is not { } sink) return false;
        sink.Send(move with { Actor = who.Guid });
        return true;
    }

    /// <summary>On a player's machine, asks the host for a vault move (<see cref="VaultMoves.Ask"/>) and answers true; anywhere else false.</summary>
    public static bool TellVault(WorldThing who, VaultMove move)
    {
        if (who.Place is not { } place || !places.TryGetValue(place, out var e) || e.Mode != NetMode.Client
            || e.Sink is not { } sink) return false;
        sink.Send(move);
        return true;
    }

    /// <summary>
    /// The cast order's client arm (message 0x18): on a client the cast goes to
    /// the host by the discipline's name and the order answers "started". 1.1 sends it from the middle
    /// of the order — after the busy gates and the discipline's own `cancast` script, and then plays
    /// the cast's start on the player's machine too; **ours** sends it at the top, whatever her machine
    /// thinks of it, and the host does all of it, the animation coming back in the snapshot — unless
    /// the start is certain on her machine by the same rules on her copy, when its start is
    /// shown at once there (<see cref="OwnCast"/>) and the host's answer to the numbered cast bears it
    /// out or takes it back.
    /// </summary>
    public static bool ForwardCast(WorldThing caster, WorldThing? target, Vector3? point, string discipline,
                                   int mods, WorldThing? scroll)
    {
        if (caster.Place is not { } place || !places.TryGetValue(place, out var e) || e.Mode != NetMode.Client
            || e.Sink is not { } sink) return false;
        sink.Send(new NetCast(caster.Guid, target?.Guid ?? 0, scroll?.Guid ?? 0, mods, point, discipline));
        return true;
    }
}
