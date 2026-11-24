// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>
/// The Blood ledger (minimal; the rules package's <c>ReserveCost → Commit</c> and its interfaces'
/// <c>IResourceLedger</c>, its first three verbs): every spend of a person's <c>blood</c> resource goes through it, as whole
/// numbers, atomically. <see cref="TryReserve"/> holds an amount against what is there now (never more than the current less
/// what is already held); <see cref="Commit"/> spends a held amount from the sheet; <see cref="Release"/> lets it go, nothing
/// spent. An action id is fresh only once: a reservation under an id already used is refused, and a commit finds its hold only
/// once, so a replayed action cannot spend twice. A refusal spends nothing. Each entry is a line in the combat trace
/// (the harm trace's file, <see cref="CombatTrace"/>; schema <see cref="Schema"/>) with the Blood and the held amount before and
/// after. Digested (its counters and holds); the Blood itself is on the sheets, which are. Allocates nothing but the trace's
/// line (when there is a trace).
/// </summary>
public sealed class BloodLedger : IDigestedService
{
    public const string Schema = "VITOMIR_ENHANCED_BLOOD_LEDGER";
    public const int FormatVersion = 1;

    readonly struct Hold(EntityId actor, long amount, long actionId)
    {
        public readonly EntityId Actor = actor;
        public readonly long Amount = amount;
        public readonly long ActionId = actionId;
    }

    readonly ActorSheets sheets;
    readonly World world;
    readonly List<Hold> holds = new(4);
    readonly List<Hold> recent = new(8);    // the commits of the last second (the per-second limit), the tick in ActionId's place

    public BloodLedger(ActorSheets sheets, BloodData data, World world)
    {
        this.sheets = sheets;
        Data = data;
        this.world = world;
    }

    public BloodData Data { get; }

    /// <summary>The trace the entries are written to (the program's <c>--combat-trace</c>): the world's <see cref="CombatTrace"/>, found when first needed, unless set.</summary>
    public CombatTrace? Trace { get; set; }

    /// <summary>The last action id given out by <see cref="NewActionId"/>, and the highest id reserved under.</summary>
    public long Issued { get; private set; }
    public long LastReserved { get; private set; }

    /// <summary>The entries so far: reservations, commits, releases, refusals; and the Blood spent in all.</summary>
    public int Reserves { get; private set; }
    public int Commits { get; private set; }
    public int Releases { get; private set; }
    public int Refusals { get; private set; }
    public long Spent { get; private set; }

    /// <summary>The reason of the last refusal (for a test or a log line).</summary>
    public string LastRefusal { get; private set; } = "";

    /// <summary>A fresh action id (rising through a run).</summary>
    public long NewActionId()
    {
        Issued = Math.Max(Issued, LastReserved) + 1;
        return Issued;
    }

    /// <summary>Whether the person has Blood to spend (a <c>blood</c> resource on its sheet).</summary>
    public bool HasBlood(EntityId e) => sheets.Has(e) && sheets.Resource(e, Data.Resource) is not null;

    /// <summary>The person's Blood now and at most (0, 0 without any).</summary>
    public (long Current, long Maximum) Blood(EntityId e) =>
        sheets.Has(e) && sheets.Resource(e, Data.Resource) is { } r ? (r.Current, r.Maximum) : (0, 0);

    /// <summary>What is held against the person's Blood now.</summary>
    public long Held(EntityId e)
    {
        long n = 0;
        for (int i = 0; i < holds.Count; i++) if (holds[i].Actor == e) n += holds[i].Amount;
        return n;
    }

    /// <summary>
    /// Holds <paramref name="amount"/> of the actor's Blood under <paramref name="actionId"/>; false (and nothing held) when
    /// the id was used already, the actor has no Blood, the amount is below nothing or above the per-action limit, or the
    /// Blood less what is held is short.
    /// </summary>
    public bool TryReserve(EntityId actor, long amount, long actionId, out string reason)
    {
        var (current, _) = Blood(actor);
        long held = Held(actor);
        if (actionId <= LastReserved) reason = $"action id {actionId} was used already (a replay cannot spend twice)";
        else if (!HasBlood(actor)) reason = $"{actor} has no Blood to spend";
        else if (amount < 0) reason = $"an amount of {amount} is below nothing";
        else if (Data.PerActionLimit > 0 && amount > Data.PerActionLimit) reason = $"{amount} is over the per-action limit of {Data.PerActionLimit} ({Data.File})";
        else if (Data.PerSecondLimit > 0 && SpentInSecond(actor) + held + amount > Data.PerSecondLimit)
            reason = $"{amount} more would pass the per-second limit of {Data.PerSecondLimit} ({SpentInSecond(actor) + held} spent or held in the last second; {Data.File})";
        else if (current - held < amount) reason = $"not enough Blood ({current - held} of {amount})";
        else reason = "";
        if (reason.Length > 0)
        {
            Refusals++;
            LastRefusal = reason;
            Line("refuse", actor, actionId, amount, current, current, held, held, reason);
            return false;
        }
        holds.Add(new Hold(actor, amount, actionId));
        LastReserved = actionId;
        Reserves++;
        Line("reserve", actor, actionId, amount, current, current, held, held + amount, null);
        return true;
    }

    /// <summary>Spends the amount held under <paramref name="actionId"/> from the sheet; false when nothing is held under it (committed already or released).</summary>
    public bool Commit(long actionId, out long before, out long after)
    {
        int i = Find(actionId);
        before = after = 0;
        if (i < 0)
        {
            Refusals++;
            LastRefusal = $"no reservation under action id {actionId} (committed already or released)";
            Line("refuse", default, actionId, 0, 0, 0, 0, 0, LastRefusal);
            return false;
        }
        var h = holds[i];
        var r = sheets.Resource(h.Actor, Data.Resource)!.Value;
        long held = Held(h.Actor);
        before = r.Current;
        after = r.Current - h.Amount;
        sheets.SetResource(h.Actor, r with { Current = after });
        holds.RemoveAt(i);
        Commits++;
        Spent += h.Amount;
        if (Data.PerSecondLimit > 0) recent.Add(new Hold(h.Actor, h.Amount, world.Tick));
        Line("commit", h.Actor, actionId, h.Amount, before, after, held, held - h.Amount, null);
        return true;
    }

    /// <summary>Lets the amount held under <paramref name="actionId"/> go, nothing spent; false when nothing is held under it.</summary>
    public bool Release(long actionId)
    {
        int i = Find(actionId);
        if (i < 0) return false;
        var h = holds[i];
        var (current, _) = Blood(h.Actor);
        long held = Held(h.Actor);
        holds.RemoveAt(i);
        Releases++;
        Line("release", h.Actor, actionId, h.Amount, current, current, held, held - h.Amount, null);
        return true;
    }

    /// <summary>What the actor committed in the last second (<see cref="BloodData.CycleTicks"/> ticks, this one among them); older commits are forgotten.</summary>
    public long SpentInSecond(EntityId e)
    {
        long now = world.Tick, n = 0;
        for (int i = recent.Count - 1; i >= 0; i--)
        {
            if (recent[i].ActionId <= now - BloodData.CycleTicks) { recent.RemoveAt(i); continue; }
            if (recent[i].Actor == e) n += recent[i].Amount;
        }
        return n;
    }

    int Find(long actionId)
    {
        for (int i = 0; i < holds.Count; i++) if (holds[i].ActionId == actionId) return i;
        return -1;
    }

    void Line(string entry, EntityId actor, long actionId, long amount, long before, long after, long heldBefore, long heldAfter, string? reason)
    {
        if (Trace is null && world.TryService<CombatTrace>(out var found)) Trace = found;
        if (Trace is not { Enabled: true } t) return;
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("schema", Schema);
            w.WriteNumber("format_version", FormatVersion);
            w.WriteNumber("tick", world.Tick);
            w.WriteString("entry", entry);
            w.WriteNumber("action_id", actionId);
            w.WriteString("actor", actor.ToString());
            w.WriteString("resource", Data.Resource);
            w.WriteNumber("amount", amount);
            w.WriteNumber("blood_before", before);
            w.WriteNumber("blood_after", after);
            w.WriteNumber("held_before", heldBefore);
            w.WriteNumber("held_after", heldAfter);
            if (reason is not null) w.WriteString("reason", reason);
            w.WriteString("status", "ENGINE_SANDBOX (" + Data.File + ")");
            w.WriteEndObject();
        }
        t.Write(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    public void Digest(ref Fnv1a64 hash)
    {
        hash.Add(Issued); hash.Add(LastReserved);
        hash.Add(Reserves); hash.Add(Commits); hash.Add(Releases); hash.Add(Refusals); hash.Add(Spent);
        hash.Add(holds.Count);
        for (int i = 0; i < holds.Count; i++) { hash.Add(holds[i].Actor.Packed); hash.Add(holds[i].Amount); hash.Add(holds[i].ActionId); }
        hash.Add(recent.Count);
        for (int i = 0; i < recent.Count; i++) { hash.Add(recent[i].Actor.Packed); hash.Add(recent[i].Amount); hash.Add(recent[i].ActionId); }
    }
}
