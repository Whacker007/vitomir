#!/usr/bin/env python3
# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""Reads an event trace (VITOMIR_EVENT_TRACE_1) and its harm trace beside it, and prints a plain timeline:
who did what, what each swing met, and what each body contact did to Health, stage by stage (why a hit dealt no
wound — no damage, soak, halving; armour), and the powers: each use with its Blood, each refusal with its reason (nothing spent),
each end. Celerity's flurry — each extra blow a swing carried is its own harm of the same contact ("flurry k of n"),
and its own harm-trace line (keyed by the contact and its flurry index); Health on the 1-1000 scale and its band changes.
For the rules package's receipts.

    python3 tools/platform/arena/trace_timeline.py <bout>.events.jsonl [--sheets]
"""
import json
import math
import sys


# Why a hit dealt no wound (HarmResolved.ResistedBy).
RESISTED = {"NoDamage": "no damage: no success and none automatic", "Soak": "soaked: soak took every success",
            "Halving": "halved to nothing: a vampire's bashing",
            "Armor": "stopped by armour: the armour's successes alone kept the wounds off"}   # the armour's own "resisted"


def main(path, sheets=False):
    names, chars = {}, {}
    harm = {}
    harm_path = path.replace(".events.jsonl", ".harm.jsonl")
    try:
        for line in open(harm_path, encoding="utf-8"):
            h = json.loads(line)
            cid = h.get("contact_id")
            if cid is not None:
                harm[(cid, h.get("flurry", 0))] = h
    except FileNotFoundError:
        pass
    out = []
    for line in open(path, encoding="utf-8"):
        d = json.loads(line)
        k = d["kind"]
        if k == "head":
            for p in d["people"]:
                names[p["id"]] = p["name"]
                chars[p["id"]] = p.get("character", "")
            out.append(f"# {d['build']}, game {d['game']}, profile {d['notes'].get('profile', '')}, seed {d['seed']}, switches '{d['notes'].get('switches', '')}'")
            out.append(f"# {d['evidence']}")
            for p in d["people"]:
                s = p.get("sheet", {})
                eff = s.get("effective", {})
                sheet = s.get("sheet", {})
                abil = {t: v for t, v in eff.items() if t in ("melee", "brawl", "dodge", "athletics", "alertness") and v}
                powers = ", ".join(f"{x['power']} {x['rank']} ({'/'.join(x.get('options', []))})" for x in sheet.get("powers", [])) or "none"
                out.append(f"#   {p['id']} {p['name']} ({p.get('character', '')}): {sheet.get('species', '?')} template {sheet.get('template', '?')}; "
                           f"STR {eff.get('strength')} DEX {eff.get('dexterity')} STA {eff.get('stamina')}; {abil}; powers {powers}")
                if sheets:
                    out.append("#     " + json.dumps(s))
            continue
        e = d["event"]
        n = lambda x: names.get(x, x)
        t = f"{d['seconds']:7.2f}"
        if k == "AttackBegun":
            out.append(f"{t} {n(e['Entity'])} begins a {e['Strength'].lower()} blow #{e['Swing']} ({e['Role']}), aimed {math.degrees(e['AimPitch']):+.0f} deg")
        elif k == "AttackRefused":
            out.append(f"{t} {n(e['Entity'])}'s attack refused: {e['Why']}")
        elif k == "StrikeMoment":
            out.append(f"{t} {n(e['Entity'])} strike mark #{e['Swing']}: tip {e['TipSpeed']:.1f} m/s")
        elif k == "ContactMade":
            what = f"{n(e['Target'])}'s {e['Met'].lower()}" if e["Met"] != "Body" else f"{n(e['Target'])} — {e['Site']}"
            if e["Met"] == "Level":
                what = "the level"
            out.append(f"{t} contact {e['ContactId']}: {n(e['Attacker'])} #{e['Swing']} meets {what} ({e['Material'].lower()}), "
                       f"{e['ImpactSpeed']:.1f} m/s, impulse {e['NormalImpulse']:.1f} N s{', PROTECTED' if e['Protected'] else ''}")
        elif k == "SwingEnded":
            out.append(f"{t} {n(e['Attacker'])} #{e['Swing']} ends: met {e['Contacts']} person(s), {e['Rejected']} repeat(s) rejected, "
                       f"{'stopped' if e['Stopped'] else 'not stopped'}{'  << met nothing' if e['Contacts'] == 0 and not e['Stopped'] else ''}")
        elif k == "HarmResolved":
            c = f", contest {e.get('AttackSuccesses')}-{e.get('DefenceSuccesses')} => {e.get('ContestChange'):+d} dice" if e.get("Contested") else ""
            why = RESISTED.get(e.get("ResistedBy", "None"), "") if e["Wounds"] == 0 else ""
            fl = f" (flurry {e.get('Flurry')} of {e.get('FlurryBlows')})" if e.get("Flurry") else (f" (its swing carries {e.get('FlurryBlows')} more)" if e.get("FlurryBlows") else "")
            band = f" [{e.get('Band')}]" if e.get("Band") not in (None, "None") else ""
            out.append(f"{t} harm {e['ContactId']}{fl}: {e['Kind'].lower()}, damage {e['DamageDice']} dice -> {e['DamageSuccesses']} + {e['AutoSuccesses']} auto"
                       f"{c}; soak {e['SoakDice']} dice -> {e['SoakSuccesses']}; wounds {e['Wounds']}{f' ({why})' if why else ''}; Health {e['HealthBefore']} -> {e['HealthAfter']}{band}")
            h = harm.get((e["ContactId"], e.get("Flurry", 0)))
            if h:
                rb = f", resisted by {h['resisted_by']}" if h.get("resisted_by") else ""
                out.append(f"          (harm trace: outcome {h.get('outcome')}{rb}; stages {', '.join(st.get('step', '?') for st in h.get('events', []))})")
        elif k == "PowerUsed":
            renewed = " — a renewal (paid again)" if e.get("Renewed") else ""
            slot = f"slot {e['Slot']}" if e.get("Slot") else "no slot"
            out.append(f"{t} {n(e['Entity'])} uses {e['Power']} {e['Rank']} ({slot}, action {e.get('ActionId')}): {e['Cost']} Blood "
                       f"({e['BloodBefore']} -> {e['BloodAfter']}), {e['Seconds']:g} s{renewed}")
        elif k == "PowerRefused":
            what = f"{e['Power']} (slot {e.get('Slot')})" if e.get("Power") else f"slot {e.get('Slot')} (empty)"
            out.append(f"{t} {n(e['Entity'])}'s {what} refused: {e['Reason']} — nothing spent, no effect")
        elif k == "PowerEnded":
            out.append(f"{t} {n(e['Entity'])}'s {e['Power']} ends: {e['Cause'].lower()}")
        elif k == "HealthBandChanged":
            out.append(f"{t} {n(e['Who'])}'s Health band: {e['From']} -> {e['To']} (Health {e['Health']} of {e['HealthMax']}; labels only)")
        elif k == "ActorDied":
            out.append(f"{t} STOP: {n(e['Who'])} at zero Health ({e.get('Stop', '')}, {e['Kind'].lower()}) — a harness stop, not a ruled death")
        elif k == "ProtectedProtest":
            out.append(f"{t} {n(e['Who'])} protests the blow (protected)")
        elif k == "CombatTempo":
            out.append(f"{t} {n(e['Entity'])} tempo: swing x{e['Swing']:.3f}, recovery x{e['Recovery']:.3f}, block rise x{e['Rise']:.3f}")
        elif k in ("BlockRaised", "BlockLowered"):
            out.append(f"{t} {n(e['Entity'])} {'raises' if k == 'BlockRaised' else 'lowers'} the block")
        elif k == "ContactDuplicates":
            continue
        else:
            out.append(f"{t} {k}: {json.dumps(e)}")
    print("\n".join(out))


if __name__ == "__main__":
    main(sys.argv[1], "--sheets" in sys.argv)
