#!/usr/bin/env python3
# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
# Ours: the guards' one-on-one strength as knobs. Reads candidates.json (beside it) (the
# levers of each candidate setting for the trained guard and the watchman: their sheets' values, their duelling brains' block chance
# and pauses, their armour) and writes one arena spec per candidate and match-up, then tabulates the runs. The defaults are not
# changed by any of this: a candidate is a spec's fighter, nothing else.
#
#   python3 guard_candidates.py specs <out folder>         one spec per candidate x match-up, and run.txt with a command line each
#   python3 guard_candidates.py table <runs folder>        the table (wins, undecided, health lost by the winner) from <runs>/<name>/bouts.csv
#
# A run: vitomir-platform-cli --arena <out>/<name>.json --out <runs>/<name> --sandbox
import csv
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def load():
    with open(os.path.join(HERE, "candidates.json"), encoding="utf-8") as f:
        return json.load(f)


def spec(data, cand_name, cand, mu):
    foe = data["foes"][mu["foe"]]
    lever = cand.get(mu["foe"], {})
    sheet = foe["sheet"]
    if lever.get("values"):
        sheet = {"template": foe["sheet"], "values": lever["values"]}
    brain = "duel"
    if lever.get("brain"):
        brain = dict(lever["brain"])
        brain["kind"] = "duel"
    me = {"name": "Christof", "template": "Christof", "weapon": mu["weapon"], "shield": False, "sheet": mu["sheet"],
          "place": [38, 3], "facing": 0, "brain": "duel"}
    if mu["sheet"] == "sandbox.player":
        me["slots"] = []
    them = {"name": foe["name"], "template": foe["template"], "weapon": foe["weapon"], "shield": foe["shield"], "sheet": sheet,
            "place": [42, 3], "facing": 180, "brain": brain}
    if lever.get("armor"):
        them["armor"] = lever["armor"]
    s = {"title": f"guard candidate {cand_name}: {mu['title']}", "fighters": [me, them], "bouts": mu.get("bouts", 200),
         "seed": 3000, "seconds": 60, "tapes": 1}
    if mu.get("melee"):
        s["sweep"] = {"fighter": "Christof", "trait": "melee", "values": mu["melee"]}
    return s


def specs(out):
    data = load()
    os.makedirs(out, exist_ok=True)
    lines = []
    for cn, cand in data["candidates"].items():
        if cn in ("note",):
            continue
        for mu in data["matchups"]:
            name = f"{cn}--{mu['name']}"
            with open(os.path.join(out, name + ".json"), "w", encoding="utf-8") as f:
                json.dump(spec(data, cn, cand, mu), f, indent=1)
            lines.append(f"vitomir-platform-cli --arena {os.path.join(out, name + '.json')} --out <runs>/{name} --sandbox")
    with open(os.path.join(out, "run.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print(f"{len(lines)} specs in {out}")


def table(runs):
    data = load()
    print("| Candidate | Match-up | Melee | Wins | Undecided | Mean bout s | Health lost by the winner (Christof's, in his wins) | The foe's health lost |")
    print("|---|---|---|---|---|---|---|---|")
    for cn in data["candidates"]:
        for mu in data["matchups"]:
            p = os.path.join(runs, f"{cn}--{mu['name']}", "bouts.csv")
            if not os.path.exists(p):
                continue
            rows = list(csv.DictReader(open(p, encoding="utf-8")))
            foe = data["foes"][mu["foe"]]["name"]
            pts = {}
            for r in rows:
                pts.setdefault(r.get("Christof.melee", "") if mu.get("melee") else "", []).append(r)
            for m, rs in pts.items():
                n = len(rs)
                won = [r for r in rs if r["winner"] == "Christof"]
                lost = sum(1 for r in rs if r["winner"] == foe)

                def hl(r, who):
                    start = float(r[f"{who}.health_start"] or 0)
                    return 1 - float(r[f"{who}.health_end"] or 0) / start if start > 0 else 0

                hw = sum(hl(r, "Christof") for r in won) / len(won) if won else float("nan")
                hf = sum(hl(r, foe) for r in rs) / n
                secs = sum(float(r["seconds"]) for r in rs) / n
                print(f"| {cn} | {mu['title']} | {m or '-'} | {100 * len(won) / n:.1f} % | {100 * (n - len(won) - lost) / n:.0f} % | {secs:.1f} | {hw:.2f} | {hf:.2f} |")


if __name__ == "__main__":
    if len(sys.argv) != 3 or sys.argv[1] not in ("specs", "table"):
        print(__doc__ or "usage: guard_candidates.py specs <out> | table <runs>")
        sys.exit(2)
    (specs if sys.argv[1] == "specs" else table)(sys.argv[2])
