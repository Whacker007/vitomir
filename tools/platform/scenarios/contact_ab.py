#!/usr/bin/env python3
# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""The fixed-contact A/B: native blows against the same blows as fixed
contacts, contact by contact.

  make    <native results folder> <attacker> <out folder> [--typical]
          For each traced run of a native scenario (its events/<run_id>.events.jsonl), writes <out folder>/<run_id>.scenario.json: a
          scenario of "kind": "contact" with the same actors standing (the attacker 1.3 m in front of its first defender), one fixed
          contact for each body contact the attacker's blows made, at the same tick, on the same region and side, with the native
          contact's own measured closing speed (PointSpeed), squareness (asin(ImpactSpeed / PointSpeed)), place on the blade
          (AlongBlade), peak speed (PeakSpeed) and blow (its AttackBegun's direction and strength), one run at the native run's seed.
          --typical: each contact at its blow's medians over every traced run instead (same tick, region and blow): the meeting's
          own variation taken away.
  compare <native results folder> <fixed results root> [<attacker>]
          Pairs each native run's harms (HarmResolved by the attacker, its own blows: Flurry 0) with its fixed run's
          (<fixed results root>/<run_id>/), in contact order, and prints what differs: wounds, quality, the dice, why resisted.

Run the fixed scenarios with: vitomir-platform --scenario <out folder> --out <fixed results root> --sandbox
"""
import json
import math
import os
import sys


def stream(path):
    with open(path, encoding="utf-8") as f:
        head = json.loads(f.readline())
        return head, [json.loads(line) for line in f if line.strip()]


def runs_csv(folder):
    rows = {}
    with open(os.path.join(folder, "runs.csv"), encoding="utf-8") as f:
        cols = f.readline().rstrip("\n").split(",")
        for line in f:
            cells = line.rstrip("\n").split(",")
            rows[cells[cols.index("run_id")]] = dict(zip(cols, cells))
    return rows


def region_word(derived):
    region, side = derived.get("region"), derived.get("side")
    if not region:
        return None
    return region if not side else side + region[0].upper() + region[1:]


def typical_of(native, attacker):
    """Each blow's (swing word, region) median closing speed, squareness, place on the blade and peak over every traced run."""
    import statistics
    seen = {}
    folder = os.path.join(native, "events")
    for name in sorted(os.listdir(folder)):
        if name.endswith(".events.jsonl"):
            for c in contacts_of(os.path.join(folder, name), attacker, name[: -len(".events.jsonl")]):
                seen.setdefault((c["swing"], c["region"]), []).append(c)
    keys = ("alongBladeShare", "closingSpeedMetresPerSecond", "squarenessDegrees", "peakSpeedMetresPerSecond")
    return {k: {f: round(statistics.median(c[f] for c in v), 6) for f in keys} for k, v in seen.items()}


def contacts_of(path, attacker, run_id):
    _, events = stream(path)
    begun, contacts = {}, []
    for e in events:
        ev = e["event"]
        if e["kind"] == "AttackBegun" and e["actors"].get("Entity") == attacker:   # a blow begun: its Entity is the attacker
            begun[ev["Swing"]] = ev
        if e["kind"] == "ContactMade" and e["actors"].get("Attacker") == attacker and ev["Met"] == "Body" and not ev.get("Fixed"):
            blow = begun.get(ev["Swing"], {})
            strength = str(blow.get("Strength", "Quick")).lower()
            direction = str(blow.get("Direction", "Right")).lower()
            point, impact = ev["PointSpeed"], ev["ImpactSpeed"]
            square = 90.0 if point <= 1e-6 else math.degrees(math.asin(max(0.0, min(1.0, impact / point))))
            contacts.append({
                "label": f"{run_id} contact {ev['ContactId']} (swing {ev['Swing']}, chain {ev['Chain']})",
                "atSeconds": ev["Tick"] / 120.0,
                "attacker": attacker,
                "defender": e["actors"].get("Target"),
                "region": region_word(e.get("derived", {})),
                "alongBladeShare": round(ev["AlongBlade"], 6),
                "closingSpeedMetresPerSecond": round(point, 6),
                "squarenessDegrees": round(square, 6),
                "swing": "strong" if strength == "strong" else direction if direction in ("left", "right", "overhead", "thrust") else "right",
                "peakSpeedMetresPerSecond": round(ev["PeakSpeed"], 6),
            })
    return contacts


def make(native, attacker, out, typical=False):
    resolved = json.load(open(os.path.join(native, "resolved.json"), encoding="utf-8"))
    medians = typical_of(native, attacker) if typical else None
    runs = runs_csv(native)
    os.makedirs(out, exist_ok=True)
    events_dir = os.path.join(native, "events")
    made = 0
    for name in sorted(os.listdir(events_dir)):
        if not name.endswith(".events.jsonl"):
            continue
        run_id = name[: -len(".events.jsonl")]
        head, _ = stream(os.path.join(events_dir, name))
        contacts = contacts_of(os.path.join(events_dir, name), attacker, run_id)
        if medians is not None:   # --typical: each contact at its blow's medians (the meeting's own variation taken away)
            for c in contacts:
                c.update(medians[(c["swing"], c["region"])])
        if not contacts:
            continue
        actors = []
        defender = contacts[0]["defender"]
        cast = {f["Name"]: f for f in json.loads(head["notes"]["arena"])["Fighters"]}   # where each stood at the run's start
        dplace = [cast[defender]["X"], cast[defender]["Y"]]
        for a in resolved["actors"]:
            o = {k: a[k] for k in ("name", "template", "sheet", "foe") if k in a}
            sheet = o.get("sheet")
            if isinstance(sheet, dict):
                o["sheet"] = {k: v for k, v in sheet.items() if k in ("template", "values", "powers") and v not in ("the sheet's", None)}
                if isinstance(o["sheet"].get("powers"), dict) and not o["sheet"]["powers"]:
                    del o["sheet"]["powers"]
            gear = a.get("gear", {})
            o["gear"] = {k: v for k, v in gear.items() if k in ("weapon", "armor") and v not in ("its own",)}
            if gear.get("shield") in (True, False):
                o["gear"]["shield"] = gear["shield"]
            o["place"] = [round(dplace[0] - 1.3, 4), dplace[1]] if a["name"] == attacker else [cast[a["name"]]["X"], cast[a["name"]]["Y"]]
            o["facing"] = {"toward": a["foe"]}
            actors.append(o)
        seed = int(runs[run_id]["seed"])
        scenario = {
            "format": "vitomir-scenario/1",
            "title": f"The A/B's fixed half: {run_id}'s body contacts as fixed contacts",
            "notes": f"Made by tools/platform/scenarios/contact_ab.py make from {os.path.basename(os.path.normpath(native))}'s {run_id} (seed {seed}): each of {attacker}'s body contacts at its own tick, region, closing speed, squareness, place on the blade, peak speed and blow; the actors standing.",
            "kind": "contact",
            "ground": resolved.get("ground") if isinstance(resolved.get("ground"), str) else "free-yard",
            "time": resolved.get("time", "night"),
            "actors": actors,
            "contacts": contacts,
            "stop": {"afterSeconds": round(contacts[-1]["atSeconds"] + 1.0, 3), "onHarnessStops": "first"},
            "runs": 1, "seed": seed,
            "measures": [
                {"name": "bodyContacts", "count": {"event": "ContactMade", "by": attacker, "where": {"met": "body"}}},
                {"name": "woundsPerContact", "rate": {"of": {"event": "HarmResolved", "by": attacker, "sum": "wounds"},
                                                       "per": {"event": "ContactMade", "by": attacker, "where": {"met": "body"}}}},
                {"name": "meanQuality", "rate": {"of": {"event": "HarmResolved", "by": attacker, "sum": "quality"},
                                                  "per": {"event": "HarmResolved", "by": attacker}}},
            ],
            "outputs": {"tracePolicy": "ALL", "csv": True, "summaryPage": False, "wideBouts": False},
        }
        with open(os.path.join(out, run_id + ".scenario.json"), "w", encoding="utf-8") as f:
            json.dump(scenario, f, indent=2)
            f.write("\n")
        made += 1
    print(f"contact_ab: wrote {made} fixed-contact scenario(s) to {out}")


def harms(path, attacker):
    _, events = stream(path)
    return [e["event"] for e in events if e["kind"] == "HarmResolved" and e["actors"].get("Attacker") == attacker and e["event"].get("Flurry", 0) == 0]


def compare(native, fixed_root, attacker="Christof"):
    pairs = same_wounds = same_q = same_dice = 0
    nw = fw = 0
    diffs = []
    runs = 0
    for name in sorted(os.listdir(os.path.join(native, "events"))):
        if not name.endswith(".events.jsonl"):
            continue
        run_id = name[: -len(".events.jsonl")]
        fpath = os.path.join(fixed_root, run_id, "events", "p000-r000.events.jsonl")
        if not os.path.exists(fpath):
            continue
        runs += 1
        a, b = harms(os.path.join(native, "events", name), attacker), harms(fpath, attacker)
        if len(a) != len(b):
            diffs.append(f"{run_id}: {len(a)} native harms, {len(b)} fixed")
        for k, (x, y) in enumerate(zip(a, b)):
            pairs += 1
            nw += x["Wounds"]
            fw += y["Wounds"]
            same_wounds += x["Wounds"] == y["Wounds"]
            same_q += abs(x["Quality"] - y["Quality"]) < 1e-3
            same_dice += (x["DamageDice"], x["DamageSuccesses"], x["SoakDice"], x["SoakSuccesses"]) == (y["DamageDice"], y["DamageSuccesses"], y["SoakDice"], y["SoakSuccesses"])
            if x["Wounds"] != y["Wounds"] or abs(x["Quality"] - y["Quality"]) >= 1e-3 or x["ResistedBy"] != y["ResistedBy"]:
                diffs.append(f"{run_id} #{k}: native wounds {x['Wounds']} Q {x['Quality']:.3f} dice {x['DamageDice']}/{x['DamageSuccesses']} soak {x['SoakDice']}/{x['SoakSuccesses']} {x['ResistedBy']} hilt {x['Hilt']}"
                             f" | fixed wounds {y['Wounds']} Q {y['Quality']:.3f} dice {y['DamageDice']}/{y['DamageSuccesses']} soak {y['SoakDice']}/{y['SoakSuccesses']} {y['ResistedBy']} hilt {y['Hilt']}")
    if pairs == 0:
        print("contact_ab: no pairs (run the fixed scenarios into <fixed results root>/<run_id>/ first)")
        return 1
    print(f"contact_ab: {runs} runs, {pairs} contact pairs: wounds equal in {same_wounds} ({100.0 * same_wounds / pairs:.1f} %), "
          f"quality equal in {same_q}, the dice equal in {same_dice}; mean wounds a contact native {nw / pairs:.3f}, fixed {fw / pairs:.3f}")
    for d in diffs[:40]:
        print("  " + d)
    if len(diffs) > 40:
        print(f"  ... and {len(diffs) - 40} more")
    return 0


if __name__ == "__main__":
    if len(sys.argv) >= 5 and sys.argv[1] == "make":
        make(sys.argv[2], sys.argv[3], sys.argv[4], typical="--typical" in sys.argv[5:])
    elif len(sys.argv) >= 4 and sys.argv[1] == "compare":
        sys.exit(compare(sys.argv[2], sys.argv[3], *(sys.argv[4:5])))
    else:
        print(__doc__)
        sys.exit(2)
