#!/usr/bin/env python3
# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""Reads an arena run's bouts.csv and contacts.csv and prints, per swept value and per fighter, the stages a result is
made of (the rules package asked for them before any win rate): attempts; what each swing
met, with the swings that met nothing counted; damage dice, successes, soak and wounds per body contact (zero results
counted), per attempt; time to the first body contact; zero-Health stops and the bouts censored at the time limit. A
paired run (the same seeds at every value) also gets, per bout, whether the fighter's contacts kept the same sequence.
Why a body contact dealt no wound (no damage, soak, halving), and per value what the fighter's powers did and what
each swing came to: Blood spent, uses, renewals, refusals by reason, seconds live, swings a second, the swings while a
power was live and the swings a second while it was and while none was, and from them the extra swings its powers bought
(the rules package's entitlement receipt: swings live - seconds live x the unpaid rate — the unpaid rate of the whole run,
since within one value the fighter casts as it engages and its unpaid time is mostly the approach), body contacts and
wounds a swing. Celerity's flurry (one swing carrying several blows) — the flurry's blows and wounds,
and the receipt counts actions = swings + flurry blows (extra actions = swings live + flurry blows - seconds live x the
run's unpaid swing rate); Health (the 1-1000 scale) lost a bout. The
numbers do not assume how a power works (a faster swing, or one swing carrying several attacks): they count what happened.

    python3 tools/platform/arena/stage_report.py <arena output folder> [fighter ...]
"""
import csv
import statistics
import sys
from collections import defaultdict


def q(values, p):
    if not values:
        return float("nan")
    s = sorted(values)
    k = (len(s) - 1) * p
    lo, hi = int(k), min(int(k) + 1, len(s) - 1)
    return s[lo] + (s[hi] - s[lo]) * (k - lo)


def fmt(x, d=2):
    return "–" if x != x else f"{x:.{d}f}"


def powers_table(f, points, label, bouts):
    """per value, what the fighter's powers did and what each swing came to (empty columns in older runs)."""
    if f"{f}.uses" not in bouts[0]:
        return
    print(f"\n### {f}: powers and each swing\n")
    def live_of(b):
        v = b[f"{f}.live_seconds"]
        return sum(float(x.split(":")[1]) for x in v.split(";")) if ":" in v else float(v or 0)
    run_unpaid_s = sum(float(b["seconds"]) - live_of(b) for b in bouts)
    run_rate = sum(float(b[f"{f}.swings"]) - float(b[f"{f}.swings_live"] or 0) for b in bouts) / run_unpaid_s if run_unpaid_s > 0 else float("nan")
    print(f"The run's swings a second while none of {f}'s powers was live (every value pooled; the extra swings' baseline): {fmt(run_rate)}\n")
    flurry = f"{f}.flurry_blows" in bouts[0]
    print("| value | bouts | Blood spent / bout | uses / bout | renewals / bout | refused / bout (reasons) | live s / bout | swings / s | "
          "swings live / bout | swings / s live | unpaid swings / s (this value) | extra swings / bout (live - live s x the run's unpaid) | body / swing | wounds / swing |"
          + (" flurry blows / bout | actions / bout (swings + flurry) | extra actions / bout (swings live + flurry - live s x the run's unpaid) | Health lost / bout |" if flurry else ""))
    print("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|" + ("---|---|---|---|" if flurry else ""))
    for p in points:
        pb = [b for b in bouts if int(b["point"]) == p]
        n = len(pb) or 1
        num = lambda c: sum(float(b[f"{f}.{c}"] or 0) for b in pb)
        reasons = defaultdict(int)
        for b in pb:
            for part in filter(None, b[f"{f}.refused_reasons"].split(";")):
                r, c = part.split(":")
                reasons[r] += int(c)
        live = sum(live_of(b) for b in pb)
        secs = sum(float(b["seconds"]) for b in pb)
        swings = num("swings")
        swings_live = num("swings_live")
        unpaid_s = secs - live
        unpaid = (swings - swings_live) / unpaid_s if unpaid_s > 0 else float("nan")
        extra = (swings_live - live * run_rate) / n if run_rate == run_rate else float("nan")
        live_rate = swings_live / live if live > 0 else float("nan")
        body, wounds = num("contacts_body"), num("wounds_dealt")
        why = "; ".join(f"{r} {c}" for r, c in sorted(reasons.items()))
        tail = ""
        if flurry:
            fl = num("flurry_blows")
            extra_actions = (swings_live + fl - live * run_rate) / n if run_rate == run_rate else float("nan")
            lost = sum(float(b[f"{f}.health_start"]) - float(b[f"{f}.health_end"]) for b in pb if b[f"{f}.health_start"] and b[f"{f}.health_end"])
            tail = f" {fmt(fl / n, 2)} | {fmt((swings + fl) / n, 1)} | {fmt(extra_actions, 1)} | {fmt(lost / n, 1)} |"
        print(f"| {label[p]} | {len(pb)} | {fmt(num('blood_spent') / n, 1)} | {fmt(num('uses') / n)} | {fmt(num('renewals') / n)} | "
              f"{fmt(num('refused') / n)}{f' ({why})' if why else ''} | {fmt(live / n, 1)} | {fmt(swings / secs if secs else float('nan'))} | "
              f"{fmt(swings_live / n, 1)} | {fmt(live_rate)} | {fmt(unpaid)} | {fmt(extra, 1)} | {fmt(body / swings if swings else float('nan'))} | "
              f"{fmt(wounds / swings if swings else float('nan'))} |{tail}")
    print()


def main(folder, fighters=None):
    bouts = list(csv.DictReader(open(f"{folder}/bouts.csv", encoding="utf-8")))
    contacts = list(csv.DictReader(open(f"{folder}/contacts.csv", encoding="utf-8")))
    head = list(bouts[0].keys())
    swept = [c for c in head[1:head.index("bout")]]
    names = sorted({c.split(".")[0] for c in head if c.endswith(".swings")})
    fighters = fighters or names
    points = sorted({int(b["point"]) for b in bouts})
    label = {p: ", ".join(f"{c} = {next(b[c] for b in bouts if int(b['point']) == p)}" for c in swept) or "(no sweep)" for p in points}
    seqs = defaultdict(dict)  # (fighter, bout) -> point -> contact sequence
    print(f"# Stages by value: {folder}\n")
    for f in fighters:
        print(f"## {f}\n")
        print("| value | bouts | attempts | swings meeting nothing | body | shield | weapon | level | body / attempt | dmg dice / body | dmg succ / body | soak succ / body | wounds / body (mean, median, p10–p90, zero share) | wounds / attempt | first body contact s (median) | stops by | stopped | censored | no wound: no damage / soak / halving / armor |")
        print("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")
        for p in points:
            pb = [b for b in bouts if int(b["point"]) == p]
            pc = [c for c in contacts if int(c["point"]) == p and c["attacker"] == f]
            attempts = sum(int(b[f"{f}.swings"]) for b in pb)
            met = defaultdict(int)
            swings_met = set()
            for c in pc:
                met[c["met"]] += 1
                swings_met.add((c["bout"], c["swing"]))
            body = [c for c in pc if c["met"] == "body" and c["harm_kind"]]
            wounds = [int(c["wounds"]) for c in body]
            dd = [int(c["damage_dice"]) for c in body]
            ds = [int(c["damage_successes"]) + int(c["auto_successes"] or 0) for c in body]
            ss = [int(c["soak_successes"]) for c in body]
            first = []
            for b in pb:
                t = [float(c["seconds"]) for c in pc if c["bout"] == b["bout"] and c["met"] == "body"]
                if t:
                    first.append(min(t))
            # A stop is any stop_state (ZERO_HEALTH_STOP, SEVERED_STOP, DISARMED: harness stops, never deaths); a
            # older file has none, and then a stop is end = kill. A disarmed bout (end = time) was counted censored before.
            def ended(b):
                return bool(b.get("stop_state")) if "stop_state" in b else b["end"] == "kill"
            stops_by_f = sum(1 for b in pb if ended(b) and b["winner"] == f)
            stopped = sum(1 for b in pb if ended(b))
            censored = sum(1 for b in pb if not ended(b))
            nothing = attempts - len(swings_met)
            print(f"| {label[p]} | {len(pb)} | {attempts} | {nothing} | {met['body']} | {met['shield']} | {met['weapon']} | {met['level']} | "
                  f"{fmt(met['body'] / attempts if attempts else float('nan'))} | {fmt(statistics.mean(dd) if dd else float('nan'))} | "
                  f"{fmt(statistics.mean(ds) if ds else float('nan'))} | {fmt(statistics.mean(ss) if ss else float('nan'))} | "
                  f"{fmt(statistics.mean(wounds) if wounds else float('nan'))}, {fmt(statistics.median(wounds) if wounds else float('nan'), 1)}, "
                  f"{fmt(q(wounds, .1), 1)}–{fmt(q(wounds, .9), 1)}, {fmt(sum(1 for w in wounds if w == 0) / len(wounds) if wounds else float('nan'))} | "
                  f"{fmt(sum(wounds) / attempts if attempts else float('nan'))} | {fmt(statistics.median(first) if first else float('nan'))} | "
                  f"{stops_by_f} | {stopped} | {censored} | "
                  f"{'/'.join(str(sum(1 for c in body if c.get('resisted_by') == r)) for r in ('no_damage', 'soak', 'halving', 'armor')) if 'resisted_by' in (contacts[0] if contacts else {}) else '–'} |")
            for b in pb:
                seqs[(f, b["bout"])][p] = [(c["met"], c["site"]) for c in pc if c["bout"] == b["bout"]]
        powers_table(f, points, label, bouts)
        if len(points) > 1:
            same = sum(1 for (ff, _), s in seqs.items() if ff == f and len(s) == len(points) and all(s[p] == s[points[0]] for p in points))
            n = sum(1 for (ff, _) in seqs if ff == f)
            print(f"\nPaired: in {same} of {n} bouts {f}'s contacts (what each met, where) were the same sequence at every value; "
                  f"in the rest the fight itself went differently (a flinch, a stop or a blow's timing changed what followed), "
                  f"so those bouts compare outcomes, not the same blows.\n")
    print("Counts are whole runs; zero-wound body contacts are included; a censored bout reached the time limit with "
          "no stop (no time to a stop is counted for it). A stop is a test harness's (zero Health, severed, disarmed), never a death: "
          "no torpor or Final Death exists yet. SANDBOX numbers throughout.")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2:] or None)
