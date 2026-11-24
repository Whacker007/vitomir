// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// The receipts the log derives as it collects (the rules package's), written in
/// the stream's <c>derived</c> (<see cref="ReceiptNames"/>) and read by <see cref="ScenarioReceipts"/>. Reads only the world and the
/// events: a run with them is the run without them, digest for digest.
/// <list type="bullet">
/// <item><b>Which defence</b> (<c>defence</c> on <c>ContactMade</c>, <c>GuardResolved</c>, <c>SwingEnded</c>, <c>FlurryStrike</c>):
/// <c>guardHeld</c> (a raised shield or blade whose block went up before the blow began), <c>reactiveBlock</c> (raised at or after the
/// blow began: against this blow), <c>timedParry</c> (met in the parry window), <c>shieldInterception</c> / <c>bladeInterception</c>
/// (in the way, its holder's block not raised), <c>clash</c> (two blades both swinging), <c>dodge</c> (the blow met nothing and its
/// target was dodging through it, within its reach as it began: say how in <c>defenceEvidence</c>), <c>none</c> (a body met, or a blow
/// that met nothing with no dodge: <c>outcome</c> <c>missed</c>). A blade stopped at a guard is <c>outcome</c> <c>stopped</c>, never
/// <c>missed</c>.</item>
/// <item><b>The brain's decisions</b>: a <c>BrainDecided</c> is the parent of what it caused (the swing begun or refused, the block raised,
/// the parry, the dodge, the power used or refused), and says <c>caused</c> (that event's id), <c>causedKind</c> and <c>executed</c>
/// (false when nothing came of it: muted, superseded, refused). A defence's decision follows from the foe's <c>AttackBegun</c>.</item>
/// <item><b>Health's books on each harm</b> (<c>HarmResolved</c>): the region struck (<c>harmRegion</c>, <c>harmSide</c>), the target's
/// purchased and effective maximum and Blood at the tick's end; <c>ActorDied</c>: <c>harnessStop</c> (<c>ZERO_HEALTH_STOP</c>,
/// <c>SEVERED_STOP</c>) and <c>finalDeath</c> false (no death exists yet).</item>
/// <item><b>A flurry strike's identity</b>: <c>parentContact</c> (the one physical contact it re-meets) and
/// <c>recheckedAtOwnMoment</c> (true: the platform checked the blade's line against the body at the strike's own moment; false under
/// <c>flurry-compressed</c>); an extra blow's <c>HarmResolved</c> names its <c>FlurryStrike</c> (<c>strikeEventId</c>; its line comes
/// first in the tick, so its parent stays its contact).</item>
/// </list>
/// </summary>
public sealed partial class ScenarioEventLog
{
    /// <summary>The receipts' derived fields, as the stream writes them (<c>derived</c>).</summary>
    public static readonly string[] ReceiptNames =
    [
        "defence", "defenceEvidence", "outcome", "defender", "blockRaisedSeconds", "blowBegunSeconds",
        "dodgeEventId", "distanceAtBlowMetres", "distanceAtEndMetres", "attackerReachMetres", "dodgeMovedMetres",
        "caused", "causedKind", "executed",
        "harmRegion", "harmSide", "healthMaxPurchased", "healthMaxEffective", "blood", "harnessStop", "finalDeath",
        "parentContact", "recheckedAtOwnMoment", "strikeEventId", "resolution",
    ];

    /// <summary>The defences' names (<c>defence</c>).</summary>
    public static readonly string[] Defences = ["guardHeld", "reactiveBlock", "timedParry", "dodge", "shieldInterception", "bladeInterception", "clash", "none"];

    readonly Dictionary<string, long> blockRaisedAt = new(StringComparer.Ordinal);
    readonly HashSet<string> blockUp = new(StringComparer.Ordinal);
    readonly Dictionary<(string?, double), long> blowBegunAt = [], shotAt = [];
    readonly Dictionary<(string?, double), Dictionary<string, float>> distanceAtBlow = [];
    readonly Dictionary<string, (int Id, long Begun, long? Ended, double? Moved)> lastDodge = new(StringComparer.Ordinal);
    readonly Dictionary<(string?, double), (int Body, int Guard, int Level)> swingMet = [];
    readonly Dictionary<(string?, string), (int Id, long Tick)> pendingDecision = [];
    readonly Dictionary<double, int> flurryStrikeEvent = [];
    readonly Dictionary<int, List<int>> dodgedBlows = [];   // a dodge's event → the blows it was told for (its distance moved told when it ends)

    /// <summary>The most a decision waits for what it causes (s): a block's reaction, a dodge's; then it is not linked (stays <c>executed</c> false).</summary>
    public const double DecisionLinkSeconds = 2.0;

    static double Sec(long tick) => Math.Round(tick / (double)World.TicksPerSecond, 4);

    EntityId EntityOf(string? name)
    {
        if (name is null) return EntityId.None;
        foreach (var (e, n) in names) if (n == name) return e;
        return EntityId.None;
    }

    float? Feet(string? a, string? b)
    {
        if (!world.TryStore<Body>(out var bodies)) return null;
        var ea = EntityOf(a);
        var eb = EntityOf(b);
        if (ea.IsNone || eb.IsNone) return null;
        ref var ba = ref bodies.Find(ea);
        ref var bb = ref bodies.Find(eb);
        if (Unsafe.IsNullRef(ref ba) || Unsafe.IsNullRef(ref bb)) return null;
        float dx = bb.Feet.X - ba.Feet.X, dy = bb.Feet.Y - ba.Feet.Y;
        return MathF.Round(MathF.Sqrt(dx * dx + dy * dy), 3);
    }

    float? ReachOf(string? name)
    {
        var e = EntityOf(name);
        if (e.IsNone || !world.TryStore<DuelBrain>(out var brains)) return null;
        ref var b = ref brains.Find(e);
        return Unsafe.IsNullRef(ref b) || b.Reach <= 0f ? null : MathF.Round(b.Reach, 3);
    }

    /// <summary>The receipts of one event as it is added (before its parent is found).</summary>
    void Receipt(string kind, object e, Dictionary<string, object?> f, string? by, int id, long t)
    {
        switch (e)
        {
            case Control.AttackBegun ab:
            {
                blowBegunAt[(by, ab.Swing)] = t;
                var dist = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (var n in names.Values) if (n != by && Feet(by, n) is { } d) dist[n] = d;
                distanceAtBlow[(by, ab.Swing)] = dist;
                break;
            }
            case ArrowLoosed al: shotAt[(by, al.Shot)] = t; break;
            case Control.BlockRaised: if (by is not null) { blockRaisedAt[by] = t; blockUp.Add(by); } break;
            case Control.BlockLowered: if (by is not null) blockUp.Remove(by); break;
            case DodgeBegun: if (by is not null) lastDodge[by] = (id, t, null, null); break;
            case DodgeEnded de:
                if (by is not null && lastDodge.TryGetValue(by, out var ld))
                {
                    lastDodge[by] = (ld.Id, ld.Begun, t, Math.Round(de.Moved, 3));
                    if (dodgedBlows.TryGetValue(ld.Id, out var blows)) foreach (int k in blows) events[k].Fields["dodgeMovedMetres"] = Math.Round((double)de.Moved, 3);
                }
                break;
            case ContactMade c:
            {
                string? on = f.GetValueOrDefault("Target") as string;
                if (c.Met != ContactMet.Level) Defend(f, by, on, c.Met, c.Guard, c.Shot > 0 ? shotAt.GetValueOrDefault((by, c.Shot), -1) : blowBegunAt.GetValueOrDefault((by, c.Swing), -1), c.Shot > 0);
                if (c.Shot == 0 && !c.Fixed)
                {
                    var m = swingMet.GetValueOrDefault((by, c.Swing));
                    swingMet[(by, c.Swing)] = c.Met switch
                    {
                        ContactMet.Body => (m.Body + 1, m.Guard, m.Level),
                        ContactMet.Level => (m.Body, m.Guard, m.Level + 1),
                        _ => (m.Body, m.Guard + 1, m.Level),
                    };
                }
                break;
            }
            case GuardResolved g:
                if (contactMade.TryGetValue(g.ContactId, out int ci))
                    foreach (var n in (string[])["defence", "defenceEvidence", "blockRaisedSeconds", "blowBegunSeconds"])
                        if (events[ci].Fields.TryGetValue(n, out var v)) f[n] = v;
                break;
            case SwingEnded se:
            {
                var m = swingMet.GetValueOrDefault((by, se.Swing));
                if (m.Body > 0) { f["outcome"] = "landed"; break; }
                if (m.Guard > 0) { f["outcome"] = "stopped"; break; }
                if (m.Level > 0) { f["outcome"] = "metLevel"; break; }
                Dodged(f, by, se.Swing, t, id);
                break;
            }
            case FlurryStrike fs:
            {
                flurryStrikeEvent[fs.Strike] = id;
                f["parentContact"] = (double)fs.ContactId;
                // Its harm (published in the same tick, its line before this one): told which strike it was.
                bool harmedHere = false;
                for (int k = events.Count - 1; k >= 0 && events[k].Tick >= t; k--)
                    if (events[k].Raw is HarmResolved sh && sh.Strike == fs.Strike) { events[k].Fields["strikeEventId"] = (double)id; harmedHere = true; }
                f["recheckedAtOwnMoment"] = fs.Check != FlurryCheck.Compressed;
                f["resolution"] = fs.Check == FlurryCheck.Compressed ? ExtraBlowResolution.Compressed : ExtraBlowResolution.Sweep;
                string? on = f.GetValueOrDefault("Target") as string;
                switch (fs.Check)
                {
                    case FlurryCheck.Dodged:
                        f["defence"] = "dodge"; f["outcome"] = "dodged";
                        f["defenceEvidence"] = "the platform's own check at the strike's moment: the blade's line met nothing of the target's, and the target was dodging or had dodged since the swing's contact";
                        break;
                    case FlurryCheck.Guarded:
                        Defend(f, by, on, fs.Guard == GuardMeet.Shield || fs.Guard == GuardMeet.Incidental ? ContactMet.Shield : ContactMet.Weapon, fs.Guard, blowBegunAt.GetValueOrDefault((by, fs.Swing), -1), false);
                        f["outcome"] = harmedHere ? "landed" : "stopped";   // landed: it overpowered the guard (its harm was resolved on the part behind)
                        break;
                    case FlurryCheck.OutOfReach: f["defence"] = "none"; f["outcome"] = "missed"; break;
                    case FlurryCheck.Met: case FlurryCheck.Compressed: f["defence"] = "none"; f["outcome"] = harmedHere ? "landed" : "unresolved"; break;
                    default: f["outcome"] = fs.Check == FlurryCheck.TargetDown ? "targetDown" : "attackerStopped"; break;
                }
                break;
            }
            case HarmResolved h:
            {
                long own = FlurryBlows.Id(h.ContactId, 0);
                string? site = null;
                if (h.Strike != 0 && h.Strike != own && flurryStrikeEvent.TryGetValue(h.Strike, out int si) && events[si].Raw is FlurryStrike sfs)
                    site = sfs.Region == BodyRegion.None ? null : BodyRegions.Name(sfs.Region);
                else if (contactMade.TryGetValue(h.ContactId, out int hc) && events[hc].Raw is ContactMade hcm) site = hcm.Site;
                if (RegionOf(site) is { } r) { f["harmRegion"] = r.Region; if (r.Side is not null) f["harmSide"] = r.Side; }
                Books(f, h.Target);
                break;
            }
            case ActorDied ad:
                f["harnessStop"] = ArenaTables.StopName(ad.Stop);
                f["finalDeath"] = false;
                break;
        }
    }

    /// <summary>The target's books at the tick's end (Health's purchased and effective maximum, Blood).</summary>
    void Books(Dictionary<string, object?> f, EntityId who)
    {
        if (who.IsNone || !world.TryStore<Powers.Vitals>(out var vitals)) return;
        ref var v = ref vitals.Find(who);
        if (Unsafe.IsNullRef(ref v) || v.HealthMax <= 0f) return;
        f["healthMaxPurchased"] = Math.Round((double)v.HealthMax, 3);
        f["healthMaxEffective"] = Math.Round((double)(v.HealthCap > 0f ? v.HealthCap : v.HealthMax), 3);
        if (v.HasBlood) f["blood"] = Math.Round((double)v.Blood, 3);
    }

    /// <summary>Which defence met a blow at a shield or a blade (<paramref name="begun"/>: the blow's or the shot's tick, −1 unknown).</summary>
    void Defend(Dictionary<string, object?> f, string? by, string? on, ContactMet met, GuardMeet guard, long begun, bool arrow)
    {
        if (met == ContactMet.Body) { f["defence"] = "none"; return; }
        string? d = guard switch
        {
            GuardMeet.Clash => "clash",
            GuardMeet.Parry => "timedParry",
            GuardMeet.Incidental => met == ContactMet.Shield ? "shieldInterception" : "bladeInterception",
            _ => null,
        };
        if (d is not null)
        {
            f["defence"] = d;
            f["defenceEvidence"] = d switch
            {
                "clash" => "the platform's contact: two blades, both in their active windows, met (GuardMeet.Clash)",
                "timedParry" => "the platform's contact: the blade met the blow inside its holder's parry window (GuardMeet.Parry)",
                _ => "the platform's contact: in the blow's way, its holder's block not raised (GuardMeet.Incidental): no Block for the rules",
            };
            return;
        }
        // A raised guard (GuardMeet.Shield or Blade; or, with the guard rules off or for an arrow, a block up): held before the blow, or raised against it.
        bool raised = guard is GuardMeet.Shield or GuardMeet.Blade || (on is not null && blockUp.Contains(on));
        if (!raised)
        {
            f["defence"] = met == ContactMet.Shield ? "shieldInterception" : "bladeInterception";
            f["defenceEvidence"] = "met at a shield or blade in the way, its holder's block not raised";
            return;
        }
        long up = on is not null && blockRaisedAt.TryGetValue(on, out long r) ? r : -1;
        string what = arrow ? "the arrow loosed" : "the blow begun";
        if (up < 0 || begun < 0)
        {
            f["defence"] = "guardHeld";
            f["defenceEvidence"] = up < 0 ? "the guard was raised (the platform's contact) and no raising of it was seen in this run: up from the start"
                                          : $"block raised at {Sec(up).ToString(CultureInfo.InvariantCulture)} s; when {what} unknown";
            if (up >= 0) f["blockRaisedSeconds"] = Sec(up);
            return;
        }
        f["blockRaisedSeconds"] = Sec(up);
        f["blowBegunSeconds"] = Sec(begun);
        bool reactive = up >= begun;
        f["defence"] = reactive ? "reactiveBlock" : "guardHeld";
        f["defenceEvidence"] = string.Create(CultureInfo.InvariantCulture,
            $"block raised at {Sec(up)} s, {what} at {Sec(begun)} s: {(reactive ? "raised against this one" : "already up")}");
    }

    /// <summary>A blow that met nothing: a dodge when a person within its reach as it began was dodging through it, else missed.</summary>
    void Dodged(Dictionary<string, object?> f, string? by, int swing, long end, int id)
    {
        long begun = blowBegunAt.GetValueOrDefault((by, swing), -1);
        float? reach = ReachOf(by);
        string? who = null;
        float best = float.MaxValue;
        (int Id, long Begun, long? Ended, double? Moved) dodge = default;
        var dist = distanceAtBlow.GetValueOrDefault((by, swing));
        foreach (var (name, dg) in lastDodge)
        {
            if (name == by || dg.Begun > end || (begun >= 0 && dg.Ended is { } en && en < begun)) continue;
            float d0 = dist is not null && dist.TryGetValue(name, out float dd) ? dd : float.MaxValue;
            if (who is null || d0 < best) { best = d0; who = name; dodge = dg; }
        }
        bool inReach = who is not null && (reach is null || best == float.MaxValue || best <= reach.Value);   // a distance unknown (no bodies) is not judged
        if (who is null || !inReach)
        {
            f["outcome"] = "missed";
            f["defence"] = "none";
            f["defenceEvidence"] = who is null ? "met nothing; nobody was dodging through it"
                : string.Create(CultureInfo.InvariantCulture, $"met nothing; {who} dodged but was {best} m away as it began, beyond its reach of {reach} m");
            return;
        }
        float? after = Feet(by, who);
        f["outcome"] = "dodged";
        f["defence"] = "dodge";
        f["defender"] = who;
        f["dodgeEventId"] = (double)dodge.Id;
        if (best < float.MaxValue) f["distanceAtBlowMetres"] = Math.Round((double)best, 3);
        if (after is { } a) f["distanceAtEndMetres"] = Math.Round((double)a, 3);
        if (reach is { } rr) f["attackerReachMetres"] = Math.Round((double)rr, 3);
        if (dodge.Moved is { } mv) f["dodgeMovedMetres"] = mv;
        else (dodgedBlows.TryGetValue(dodge.Id, out var l) ? l : dodgedBlows[dodge.Id] = []).Add(id);   // told when the dodge ends
        f["defenceEvidence"] = string.Create(CultureInfo.InvariantCulture,
            $"met nothing; {who}'s dodge (event {dodge.Id}, from {Sec(dodge.Begun)} s) ran through the blow{(dodge.Moved is { } m2 ? $" and moved {m2} m" : "")}; " +
            $"{(best < float.MaxValue ? $"{best} m" : "an unknown distance")} apart as the blow began{(after is { } a2 ? $", {a2} m as it ended" : "")}" +
            $"{(reach is { } r3 ? $" (its reach {r3} m)" : " (its reach unknown: no duelling brain)")}. Inferred from the motion, not swept: the blade's path against where the body would have been is not computed");
    }

    // ---- the brain's decisions and what they caused ----

    static string? GroupOf(string kind) => kind switch
    {
        "AttackBegun" or "AttackRefused" => "Attack",
        "BlockRaised" => "Block",
        "ParryBegun" => "Parry",
        "DodgeBegun" or "DodgeRefused" => "Dodge",
        "PowerUsed" or "PowerRefused" => "Power",
        _ => null,
    };

    /// <summary>A decision remembered for what it will cause; a caused event's decision (its parent), the decision told what it caused.</summary>
    int? Decided(string kind, Dictionary<string, object?> f, string? by, int id)
    {
        if (kind == "BrainDecided")
        {
            string? choice = f.GetValueOrDefault("Choice") as string;
            string? group = choice switch { "Attack" => "Attack", "Block" or "HoldBlock" => "Block", "Parry" => "Parry", "Dodge" => "Dodge", "Power" => "Power", _ => null };
            if (group is not null)
            {
                f["executed"] = false;
                pendingDecision[(by, group)] = (id, f.TryGetValue("Tick", out var tk) && tk is double td ? (long)td : world.Tick - 1);
            }
            return null;
        }
        if (pendingDecision.Count == 0 || GroupOf(kind) is not { } g || !pendingDecision.TryGetValue((by, g), out var p)) return null;
        pendingDecision.Remove((by, g));
        var decision = events[p.Id];
        bool hold = decision.Fields.GetValueOrDefault("Choice") as string == "HoldBlock";
        long now = f.TryGetValue("Tick", out var t2) && t2 is double d2 ? (long)d2 : world.Tick - 1;
        if (!hold && now - p.Tick > (long)(DecisionLinkSeconds * World.TicksPerSecond)) return null;
        decision.Fields["caused"] = (double)id;
        decision.Fields["causedKind"] = kind;
        decision.Fields["executed"] = !kind.EndsWith("Refused", StringComparison.Ordinal);
        return p.Id;
    }

    /// <summary>A defence's decision follows from the foe's blow it answers (its <c>AttackBegun</c>).</summary>
    int? DecisionParent(Dictionary<string, object?> f)
    {
        if (f.GetValueOrDefault("Choice") as string is not ("Block" or "Parry" or "Dodge" or "NoDefence")) return null;
        return f.GetValueOrDefault("Target") is string foe && f.GetValueOrDefault("FoeSwing") is double sw && sw > 0
            && swingBegun.TryGetValue((foe, sw), out int p) ? p : null;
    }
}
