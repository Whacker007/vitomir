// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Runtime.CompilerServices;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Powers;

// The contract between the quick slots and the player's control (the platform), the powers and Blood (the enhanced
// module) and the arena's brain (the platform). Shared records: the code that owns a record's producer may add fields at
// the end, never change or reorder these.
//
// By design: Potence and Fortitude passive, as Revised has them (no activation); Celerity cast as 1.1
// casts it (one payment for a time by rank, a press while live renews it), (1 + rank)x movement and attack speed by
// default; Blood on 1.1's scale (a vampire's pool 100, bought to 1000); activation instant; bindable quick slots (a
// command wheel later shows the same); the masquerade mode governs movement.

/// <summary>The quick slots' count (bindable quick slots, keys 1 to 8 by default; the command wheel later shows the same entries).</summary>
public static class QuickSlots
{
    public const int Count = 8;
}

/// <summary>
/// A request to use one of a person's quick slots this tick (pressed, not held): written by the player's control from the
/// slot keys and by brains, read by the module's powers in <see cref="Phase.Act"/> and cleared by them. A world without a
/// module that reads it ignores it.
/// </summary>
public struct PowerIntent
{
    /// <summary>The slot pressed this tick, 1 to <see cref="QuickSlots.Count"/>; 0 for none.</summary>
    public int Slot;
}

/// <summary>What a quick slot shows (<see cref="SlotView"/>).</summary>
public enum SlotState : byte
{
    /// <summary>Nothing in it.</summary>
    Empty = 0,
    /// <summary>Its power can be used now.</summary>
    Ready = 1,
    /// <summary>Its power is on (<see cref="SlotView.SecondsLeft"/> of <see cref="SlotView.SecondsTotal"/>); a press renews it, as 1.1 does.</summary>
    Live = 2,
    /// <summary>The recast is running (<see cref="SlotView.RecastLeft"/>).</summary>
    Recast = 3,
    /// <summary>Not enough Blood for it (<see cref="SlotView.Cost"/>).</summary>
    Short = 4,
    /// <summary>Learned but not usable now: no semantics at this rank (6 to 10, refused by name), or the person is dead.</summary>
    Unusable = 5,
}

/// <summary>
/// When a brain would use a slot's power (<see cref="SlotView.Use"/>), set by the module from the power's kind so a brain
/// needs no module types: in a fight, or when hurt. 1.1's discipline table has the same split (<c>game.ndd</c>'s type column:
/// BUFF 2, "start of fight generally", and HEAL 1, "when damaged").
/// </summary>
public enum SlotUse : byte
{
    /// <summary>Use it in a fight (Celerity, Blood Strength, Dexterity, Stamina).</summary>
    Engage = 0,
    /// <summary>Use it when hurt (Blood Healing).</summary>
    Hurt = 1,
}

/// <summary>
/// One quick slot as the module shows it, for the HUD, the command wheel later and brains: the power by its index in the
/// module's catalogue (<see cref="IPowerCatalogue"/>; −1 for none), its state, the time left of a live power, the recast
/// left, and its Blood cost. when a brain would use it (<see cref="SlotUse"/>).
/// </summary>
public struct SlotView
{
    public int Power;
    public SlotState State;
    public float SecondsLeft;
    public float SecondsTotal;
    public float RecastLeft;
    public int Cost;
    public SlotUse Use;
    /// <summary>
    /// Added at the end: the body tempo the power itself gives while it is live (Celerity's attack
    /// factor; 1 for a power that gives none, 0 when the view does not say). A brain paces itself by this, never by the
    /// whole tempo, so the Abilities' tempo is not a second channel through a brain's policy.
    /// </summary>
    public float Tempo;

    public static SlotView None => new() { Power = -1 };
}

/// <summary>The <see cref="QuickSlots.Count"/> slots, inline.</summary>
[InlineArray(QuickSlots.Count)]
public struct SlotViews
{
    SlotView first;
}

/// <summary>
/// A person's quick slots as the module shows them, written each tick by the module for the people it gives powers to.
/// Not digested: a view of the module's own state, which is (its sheets and ledger).
/// </summary>
public struct PowerSlots
{
    public SlotViews Slots;
}

/// <summary>
/// A person's vitals as the module shows them (the HUD, brains): Health and Blood now and at most. <see cref="HasBlood"/>
/// false for a person with no Blood to spend (a mortal). Not digested, written by the module each tick.
/// <para>Added at the end: Health is on the 1–1000 scale; <see cref="Band"/> is its band
/// (<see cref="Combat.HealthBand"/>: Injured below 25 % of the maximum, Crippled below 10 %, Stopped at nothing — labels
/// only, the rules package's proposals).</para>
/// </summary>
public struct Vitals
{
    public float Health, HealthMax, Blood, BloodMax;
    public bool HasBlood;
    public Combat.HealthBand Band;
    /// <summary>
    /// Added at the end; the rules package's: the effective maximum Health while
    /// anatomy is missing — healing stops there; <see cref="HealthMax"/> stays the purchased maximum, the bands' denominator.
    /// 0 when nothing is missing (the same as <see cref="HealthMax"/>).
    /// </summary>
    public float HealthCap;
}

/// <summary>The module's powers by index (a service the module registers): the option's id and a short label for a slot.</summary>
public interface IPowerCatalogue
{
    int Count { get; }

    /// <summary>The power's option id (<c>celerity.active</c>).</summary>
    string Id(int power);

    /// <summary>A short name for a slot and the wheel (<c>Celerity</c>; <c>Blood Str</c>).</summary>
    string Label(int power);

    /// <summary>An addition with a default: the power's full name for words (the log, a tooltip): <c>Blood Strength</c>.</summary>
    string Name(int power) => Label(power);
}

/// <summary>Why a power was refused (a typed reason, and nothing spent and no effect).</summary>
public enum PowerDenial : byte
{
    None = 0,
    /// <summary>The slot holds nothing.</summary>
    EmptySlot = 1,
    /// <summary>The power or its option is not learned.</summary>
    Unlearned = 2,
    /// <summary>The power has no semantics at this rank (ranks 6 to 10 until defined; gate-29).</summary>
    NoSemantics = 3,
    /// <summary>Not enough Blood.</summary>
    NoBlood = 4,
    /// <summary>The recast is running.</summary>
    Recast = 5,
    /// <summary>The person is dead (a zero-Health stop).</summary>
    Dead = 6,
    /// <summary>The person has no sheet.</summary>
    NoSheet = 7,
    /// <summary>The power is passive: always on, nothing to use.</summary>
    Passive = 8,
    /// <summary>The power would do nothing now (a heal at full Health; 1.1's <c>RPG_DISC_NOBLOODHEALING</c>).</summary>
    NoEffect = 9,
}

/// <summary>Why a live power ended.</summary>
public enum PowerEndCause : byte
{
    /// <summary>Its time ran out.</summary>
    Expired = 0,
    /// <summary>A new use replaced it (it goes on: <see cref="PowerUsed.Renewed"/>).</summary>
    Renewed = 1,
    /// <summary>Cancelled.</summary>
    Cancelled = 2,
    /// <summary>The person stopped (zero Health).</summary>
    Died = 3,
}

/// <summary>
/// A power used: who, which (the option id), at what rank, from which slot (0: not from a slot), the tick, the
/// ledger's action id (one commit per id), the Blood paid and the Blood before and after, how long it lasts (s; 0 for
/// one with no duration), and whether it renewed one already live. the tick its live
/// window ends (live while the world's tick is below it; <see cref="Tick"/> is its start), for the rules package's entitlement
/// receipt. the effect's amount — a temporary Attribute's rise (raw points), a heal's Health
/// gained (no more than it lacked); 0 for an effect with none (Celerity).
/// </summary>
public readonly record struct PowerUsed(
    EntityId Entity, string Power, int Rank, int Slot, long Tick, long ActionId, int Cost, int BloodBefore, int BloodAfter,
    float Seconds, bool Renewed, long EndTick = 0, int Amount = 0);

/// <summary>A power refused: nothing spent, no effect . <see cref="Power"/> null for an empty slot.</summary>
public readonly record struct PowerRefused(EntityId Entity, string? Power, int Slot, PowerDenial Reason, long Tick);

/// <summary>
/// A live power ended. the tick its live window began, the action id that paid for it and
/// its rank, so a window's receipt is whole from this line alone (start, end, why).
/// </summary>
public readonly record struct PowerEnded(EntityId Entity, string Power, long Tick, PowerEndCause Cause, long StartTick = 0, long ActionId = 0, int Rank = 0);

/// <summary>
/// A body's pace scale (Celerity's movement); absent means 1, so a world without it is the same world, digest for
/// digest. The motor's pace for each gait is the larger of the body's own and the human's times <see cref="Factor"/>, and
/// its acceleration and braking likewise; a body in the masquerade mode ignores it (the masquerade keeps a body's
/// movement a human's). Written by a module.
/// </summary>
public struct PaceScale
{
    public float Factor;
}

/// <summary>The contract's registration (each party calls it; it registers what is not registered yet).</summary>
public static class PowerContract
{
    public static WorldBuilder Register(WorldBuilder b)
    {
        var w = b.Peek;
        if (!w.TryStore<PowerIntent>(out _)) b.Component<PowerIntent>();
        if (!w.TryStore<PowerSlots>(out _)) b.Component<PowerSlots>(digested: false);
        if (!w.TryStore<Vitals>(out _)) b.Component<Vitals>(digested: false);
        if (!w.TryStore<PaceScale>(out _)) b.Component<PaceScale>();
        if (!w.TryService<EventQueue<PowerUsed>>(out _)) b.Event<PowerUsed>();
        if (!w.TryService<EventQueue<PowerRefused>>(out _)) b.Event<PowerRefused>();
        if (!w.TryService<EventQueue<PowerEnded>>(out _)) b.Event<PowerEnded>();
        return b;
    }
}
