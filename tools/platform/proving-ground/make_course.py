# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""
The proving ground (ours): a movement test course for the platform's body, generated from the table below.

    python make_course.py <out-folder>

writes <out-folder>/proving-ground.gltf (+ .bin, grid.png, signs.png) and layout.json, which the faithful program cooks:

    vitomir --cook-l2 <out>/proving-ground.gltf --layout <out>/layout.json <levels>/proving-ground.l2 --ground ground --nav-agent human

(tools/platform/cook-proving-ground.ps1 does both.) Python 3.11, the standard library only.

How it is laid out: metres, X east, Y north, Z up, the floor at height 0. Every
station is a row of lanes side by side (along Y), one obstacle to a lane, each lane run along +X: its run-up begins at
the station's `at` (x, y) and its obstacle's near face is `runup` metres further east. A start anchor stands on every
lane's run-up 8 m before its obstacle, facing it (`<station>-<label>`), and one for the station (its first lane,
named for the station), so `vitomir-platform --start kerbs-0.35` puts you there.

Edit the course by editing STATIONS: copy a line and change its numbers. Heights are the top's height above the
floor the body stands on. What the human should do at each obstacle (its colour and the word on its sign) is
worked out from the human's numbers (HUMAN); a lane may say "expect" to choose.
"""
import json
import math
import os
import struct
import sys
import zlib

# ---------------------------------------------------------------------------------------------------- the table

# The human's numbers, used only to colour the obstacles and word the
# signs; the body's own numbers are the platform's, never these.
HUMAN = {
    "stride": 0.35,      # taken in stride, up or down
    "hop": 0.60,         # a tapped hop onto a top that can be stood on
    "hurdle": 1.00,      # a sprint hurdles a thin thing to this
    "vault": (0.60, 1.20),   # jump held: vault (thin) or climb onto (deep)
    "mantle": 1.90,      # jump held: mantle onto a top from the vault's top to this
    "grab": 2.80,        # a lip grabbed in a jump
    "deep": 0.35,        # a top at least this deep can be stood on (thinner is "thin"; the profiles' MantleMinDepth)
    "height": 1.80,      # the body standing
    "crouched": 1.15,    # the body crouched
    "width": 0.60,       # the body's width (twice its radius)
    "walked": 40.0,      # ground walked to this slope, degrees
}

# What the human should do, its colour (sRGB) and the word on the sign. One colour to each.
LEGEND = {
    "stride":   ((0.36, 0.72, 0.36), "STRIDE", "taken in stride (nothing to press)"),
    "stop":     ((0.98, 0.82, 0.22), "STOP", "stops you at any pace; a tap of jump, close, hops you onto it"),
    "hurdle":   ((0.98, 0.55, 0.16), "SPRINT", "a sprint hurdles it by itself"),
    "vault":    ((0.30, 0.52, 0.92), "HOLD: VAULT", "jump held: vault over it (thin) or climb onto it (deep)"),
    "mantle":   ((0.62, 0.38, 0.85), "HOLD: MANTLE", "jump held: mantle onto it"),
    "grab":     ((0.90, 0.25, 0.25), "HOLD: GRAB", "jump held: grab the lip; still held, pull up; let go, hang"),
    "toohigh":  ((0.38, 0.38, 0.42), "NO WAY", "too high, too steep or too narrow for a human"),
    "crouch":   ((0.25, 0.78, 0.82), "CROUCH", "crouch (X) to pass under it"),
    "notbuilt": ((0.95, 0.50, 0.80), "NOT BUILT", "a move not built yet: there to look at"),
    "ladder":   ((0.62, 0.88, 0.18), "USE: LADDER", "use (E, or Space) at its foot or top: forward climbs, back goes down, jump pushes off, crouch lets go"),
    "noclimb":  ((0.52, 0.34, 0.22), "NO CLIMB", "the level says not to be climbed (a no-climb mesh or area): it stops you"),
    "nostand":  ((0.14, 0.14, 0.16), "NO STAND", "the level says no body stands on its top (a no-stand mesh or area): you slide off it"),
    "neutral":  ((0.86, 0.84, 0.80), "", "platforms, landings and ways back up"),
    # The run's step-over; the sprint's vault (which replaced the hurdle) keeps the hurdle's orange.
    "stepover": ((0.99, 0.72, 0.42), "RUN: STEP OVER", "a run or faster steps over it by itself (thin, to 0.5 m)"),
    # Walls climbed by their holds, and a face with nothing to hold out of the masquerade.
    "wall":     ((0.70, 0.52, 0.34), "CLIMB", "a tap of jump at it gets on; the move keys climb (walk: careful, sprint: long), up goes over the top, down at the foot gets off, crouch lets go, jump with back pushes off"),
    "sheer":    ((0.60, 0.62, 0.70), "CLIMB: UNBOUND", "a face with nothing to hold: climbed only out of the masquerade (Caps Lock) or in a free place, with the splayed grip"),
    # Squeezing through gaps narrower than the body, and in stealth (C) the back to a wall in cover.
    "squeeze":  ((0.82, 0.66, 0.30), "SQUEEZE", "walk into it: a quarter turn, the move keys along it side-step through, out the far side"),
    "cover":    ((0.36, 0.44, 0.62), "C: STEALTH, BACK TO IT", "in stealth (C), stand with your back to the wall or back into it: cover; left and right along it; forward out"),
}
HOLD_STONE = (0.42, 0.36, 0.30)   # the holds drawn on a wall with holds (a mesh that stops nothing: decoration.passableMeshes)
FLOOR = (0.80, 0.79, 0.76)        # the course's floor (public)
FREE_FLOOR = (0.86, 0.62, 0.56)   # the free yard's floor
START_FLOOR = (0.70, 0.80, 0.90)  # the start square
DARK_FLOOR = (0.12, 0.13, 0.17)   # the start square's dark corner (light 0.1)
BUSH_GREENS = ((0.16, 0.36, 0.13), (0.27, 0.50, 0.19))   # the bush's clumps, dark and light

RUNUP = 9.0       # metres from a lane's start to its obstacle's near face (the anchor stands 1 m in: 8 m of run-up)
PITCH = 5.0       # metres from one lane's middle to the next's (2 m lanes, 3 m between)
WIDTH = 2.0       # an obstacle's width across its lane

# The course's floor: x from, x to, y from, y to.
COURSE = (-104.0, 60.0, -60.0, 70.0)   # to y 70 for the sprint-vault station
# The start square in the middle (anchor 0, "start"): x from, x to, y from, y to.
START_SQUARE = (-30.0, -10.0, -20.0, 20.0)

# Perception: areas that conceal or have their own light, the keys the levels
# will author — a bush in the start square's north-west corner (conceal 0.2: seen a fifth as far) and a dark corner in
# its south-west (light 0.1). drawn so they can be found — the bush as green clumps that stop nothing (with the
# signs), the dark corner as a darker floor — each with a sign facing the start ("BUSH" "CONCEAL 0.2", "DARK" "LIGHT 0.1").
PERCEPTION_AREAS = [
    {"name": "bush", "kind": "zone", "exposure": 0, "conceal": 0.2,
     "box": {"origin": [-29.0, 14.0, -1.0], "rotZ": 0, "size": [3.0, 3.0, 4.0]}},
    {"name": "dark-corner", "kind": "zone", "exposure": 0, "light": 0.1,
     "box": {"origin": [-30.0, -20.0, -1.0], "rotZ": 0, "size": [6.0, 6.0, 8.0]}},
]

# The stations. kind: what each lane builds; at: (x, y) where the first lane's run-up begins (its south-west corner);
# lanes: one obstacle each, west to east along the lane, south to north lane by lane. A lane's label names its start
# (<station>-<label>) and is printed on its sign under the number. Numbers in metres, slopes in degrees.
STATIONS = [
    dict(name="stairs", kind="stairs", at=(-100, -55), lanes=[
        dict(riser=0.15), dict(riser=0.20), dict(riser=0.25), dict(riser=0.30), dict(riser=0.35), dict(riser=0.40),
        dict(riser=0.20, open=True, label="open"),
        dict(riser=0.20, headroom=2.0, label="head2.0"),
        dict(riser=0.20, headroom=1.85, label="head1.85"),
        dict(riser=0.20, headroom=1.70, label="head1.70")]),
    dict(name="kerbs", kind="kerb", at=(-100, -5), lanes=[
        dict(height=0.10), dict(height=0.15), dict(height=0.20), dict(height=0.25),
        dict(height=0.30), dict(height=0.35), dict(height=0.40), dict(height=0.45)]),
    dict(name="slopes", kind="ramp", at=(-76, -55), lanes=[
        dict(degrees=15), dict(degrees=25), dict(degrees=35), dict(degrees=40),
        dict(degrees=42), dict(degrees=45), dict(degrees=50)]),
    dict(name="hop", kind="block", at=(-76, -15), lanes=[
        dict(height=0.40, depth=0.4), dict(height=0.45, depth=0.4), dict(height=0.50, depth=0.4),
        dict(height=0.55, depth=0.4), dict(height=0.60, depth=0.4), dict(height=0.65, depth=0.4),
        dict(height=0.45, depth=0.4, width=0.4, label="stool"),
        dict(kind="table", height=0.75, depth=0.8, width=1.4, label="table")]),
    dict(name="crouch", kind="tunnel", at=(-76, 30), lanes=[
        dict(clear=1.30), dict(clear=1.16), dict(clear=1.00)]),
    dict(name="hurdle", kind="block", at=(-54, -55), prefer="hurdle", lanes=[
        dict(height=0.6, depth=0.1), dict(height=0.8, depth=0.1), dict(height=1.0, depth=0.1), dict(height=1.1, depth=0.1),
        dict(height=0.45, depth=0.4, label="bench"),
        dict(height=0.60, depth=0.7, label="trough")]),
    dict(name="vault", kind="block", at=(-54, -20), lanes=[
        dict(height=0.6, depth=0.3), dict(height=0.8, depth=0.3), dict(height=1.0, depth=0.3),
        dict(height=1.2, depth=0.3), dict(height=1.3, depth=0.3),
        dict(height=0.7, depth=2.0, label="climb0.7"), dict(height=0.9, depth=2.0, label="climb0.9"),
        dict(height=1.1, depth=2.0, label="climb1.1")]),
    dict(name="mantle", kind="block", at=(-54, 25), lanes=[
        dict(height=1.3, depth=2.0), dict(height=1.5, depth=2.0), dict(height=1.7, depth=2.0),
        dict(height=1.9, depth=2.0), dict(height=2.0, depth=2.0)]),
    dict(name="doors", kind="door", at=(-30, -55), pitch=4.0, wall=3.0, lanes=[
        dict(width=0.6, height=2.2, label="w0.6"), dict(width=0.7, height=2.2, label="w0.7"),
        dict(width=0.8, height=2.2, label="w0.8"), dict(width=1.0, height=2.2, label="w1.0"),
        dict(width=1.0, height=1.8, label="h1.8"), dict(width=1.0, height=1.9, label="h1.9"),
        dict(width=1.0, height=2.0, label="h2.0")]),
    dict(name="grab", kind="block", at=(-30, 25), lanes=[
        dict(height=2.2, depth=2.0), dict(height=2.4, depth=2.0), dict(height=2.6, depth=2.0),
        dict(height=2.8, depth=2.0), dict(height=3.0, depth=2.0),
        dict(kind="canopy", height=2.5, depth=2.0, room=0.8, label="canopy")]),
    dict(name="gaps", kind="gap", at=(-6, -55), lanes=[
        dict(gap=1.0), dict(gap=1.5), dict(gap=2.0), dict(gap=2.5), dict(gap=3.0),
        dict(gap=1.5, height=2.0, drop=1.0, label="drop")]),
    dict(name="pit", kind="pit", at=(-6, -21), pitch=10.0, lanes=[dict(depth=4.0, size=4.0)]),
    # The ladder and the shimmy are built (a ladder up a tower, its key in the layout's traversal.ladders; a ledge
    # to hang from and shimmy along); the beam is not. The station keeps its name, so its starts keep theirs.
    dict(name="not-built", kind="notbuilt", at=(-6, -5), title="LADDER LEDGE BEAM", lanes=[
        # 4.2 m (rung 14's top), the models repository's upright ladder's own (rungs 0.30 m apart, the first 0.30 m up, 0.20 m off the wall).
        dict(kind="ladder", height=4.2, label="ladder"),
        dict(kind="beam", height=1.0, width=0.2, length=6.0, label="beam"),
        dict(kind="shimmy", lip=2.4, length=10.0, pitch=12.0, label="shimmy")]),
    # The ladders on clips -- a ladder leaning 15° (its rungs 0.30 m along the rails, a floor at rung
    # 14's top), an upright one with a landing to its left level with rung 2 (0.6 m), and an upright one with a braced ledge to its right level
    # with rung 8 (2.4 m: rung 6 over the ladder position at rung 3), flush with the wall it stands against.
    dict(name="ladders", kind="ladder", at=(44, -55), title="LADDERS", lanes=[
        dict(height=round(0.3 + 13 * 0.3 * math.cos(math.radians(15.0)), 4), lean=15.0, label="lean"),
        dict(height=3.0, side="l", label="side"),
        dict(height=4.2, ledge="r", ledge_at=2.4, width=4.0, label="ledge")]),
    # The hang's corners and leaps. Corners: a braced block (top 2.2 m, a standing grab) with an outside
    # corner 1.5 m to the left of its lane's middle and an inside corner 1.5 m to the right (a second block coming toward the climber); a free beam (a slab
    # 0.2 m thick at 2.2 m, nothing under its edges) the same; and a wall 0.3 m thick whose end's other face is too short to hang on (the corner refused).
    dict(name="corners", kind="corner", at=(-100, 40), title="HANG: CORNERS", lanes=[
        dict(height=2.2, form="braced", label="braced", pitch=10.0),
        dict(height=2.2, form="free", label="free", pitch=10.0),
        dict(height=2.2, form="thin", label="short", pitch=8.0)]),
    # Leaps: columns 1.2 m apart side by side (0.3 m gaps), the middle one with a lip 1.0 m above its own (3.2 m, a groove between: no top to stand on),
    # the right one a lip at 3.2 m with one 1.2 m below it (2.0 m, in a groove); and a wall with a facing one 2.0 m behind (both 2.2 m).
    dict(name="leaps", kind="leap", at=(-54, 53), title="HANG: LEAPS", lanes=[
        dict(height=2.2, form="columns", label="columns", pitch=8.0),
        dict(height=2.2, form="behind", label="behind", pitch=7.0)]),
    # Climbing walls, between the hang's corners and its leaps. Holds: a wall with holds 4 m wide with a top to
    # stand on at 4.01 m (level with the clip over the top's own from the lattice's sixth row), its holds a lattice at the clips' spacing (the layout's area
    # "climb": "holds", the stones drawn where the clips' own are); corners: a wall with holds with an outside corner to the left and an inside corner to the
    # right, its holds the level's own (the area's list, a little off the lattice, so the IK has them to find); sheer: a face with nothing to hold (climbed
    # only out of the masquerade); noclimb: the same marked not to be climbed (its mesh).
    dict(name="walls", kind="wall", at=(-76, 47.5), title="WALLS", lanes=[
        dict(height=4.0107, form="holds", width=4.0, label="holds", pitch=6.5),
        dict(height=3.2, form="corners", label="corners", pitch=7.5),
        dict(height=4.0107, form="sheer", width=3.0, label="sheer", pitch=5.0, expect="sheer"),
        dict(height=4.0107, form="noclimb", width=2.0, label="noclimb", pitch=3.0, expect="noclimb")]),
    # Squeezing, stealth with cover, ducking. Gaps 3 m long between walls 2.4 m high, 0.30 m
    # (too narrow: refused, said once), 0.34 (the clips' least), 0.40 and 0.50 wide, and one 0.40 m wide that turns left after 1.5 m (refused: a bend); a wall
    # 5 m long beside the lane (its north side; both ends free) for cover; a wall across the lane with an opening 1.3 m high and 1.5 m deep to duck under
    # walking. The lanes 3 m apart (the bend's 4.5, the cover's 3.5): these are walked into.
    dict(name="stealth", kind="stealth", at=(44, -39.5), pitch=3.0, title="STEALTH, SQUEEZE, DUCK", lanes=[
        dict(kind="squeeze", gap=0.30, label="0.30", expect="toohigh"),
        dict(kind="squeeze", gap=0.34, label="0.34", expect="squeeze"),
        dict(kind="squeeze", gap=0.40, label="0.40", expect="squeeze"),
        dict(kind="squeeze", gap=0.50, label="0.50", expect="squeeze"),
        dict(kind="squeeze", gap=0.40, bend=1, label="bend", expect="toohigh", pitch=4.5),
        dict(kind="cover", height=2.4, length=5.0, label="cover", expect="cover", pitch=3.5),
        dict(kind="duck", height=2.4, clear=1.3, length=1.5, label="duck", expect="crouch")]),
    dict(name="drops", kind="drop", at=(18, -55), lanes=[
        dict(height=1), dict(height=2), dict(height=3), dict(height=4), dict(height=6), dict(height=10)]),
    # The free yard: a place marked free for the Masquerade (an elder unbound: --profile elder, Caps Lock), its floor
    # another tint; its start stands outside it, so walking in switches the mode by itself.
    dict(name="free-yard", kind="yard", at=(20, -15), size=(36, 36), runup=12.0, pitch=12.0, lanes=[
        dict(height=3.0, depth=2.0, width=6.0, label="3.0"),
        dict(height=4.5, depth=2.0, width=6.0, label="4.5"),
        dict(height=6.0, depth=6.0, width=6.0, label="roof6.0")]),
    # The no-climb station: a mantle block, a grab wall and a hop top the level
    # marks not to be climbed -- their mesh ("noclimb") named in the layout's traversal.noClimbMeshes -- each beside an
    # unmarked twin, and a mantle block marked by an area ("climb": "none") instead. A marked lane records kind
    # "noclimb" and how it is marked ("noclimb": "mesh" or "area").
    dict(name="no-climb", kind="block", at=(-6, 24), lanes=[
        dict(height=1.5, noclimb="mesh", label="mantle"), dict(height=1.5, label="mantle-twin"),
        dict(height=2.4, noclimb="mesh", label="grab"), dict(height=2.4, label="grab-twin"),
        dict(height=0.5, depth=0.6, noclimb="mesh", label="hop"), dict(height=0.5, depth=0.6, label="hop-twin"),
        dict(height=1.5, noclimb="area", label="mantle-area")]),
    # The pace: thin rails a run steps over (to 0.5 m; 0.55 m stops it), and a
    # bench, a trough and a low fence a sprint vaults over (to 1.0 m high and 0.7 m deep), never onto, with a block of
    # 0.8 m by 0.8 m it must not vault (too deep). Each lane builds a box like a block's (kinds "rail" and "vaultable",
    # so the other stations' rows leave them alone); a walk stops at every one.
    dict(name="step-over", kind="rail", at=(18, 28), lanes=[
        dict(height=0.40, depth=0.1, expect="stepover"), dict(height=0.50, depth=0.1, expect="stepover"),
        dict(height=0.55, depth=0.1, expect="hurdle")]),
    dict(name="sprint-vault", kind="vaultable", at=(18, 46), lanes=[
        dict(height=0.45, depth=0.4, expect="hurdle", label="bench"), dict(height=0.60, depth=0.6, expect="hurdle", label="trough"),
        dict(height=1.00, depth=0.1, expect="hurdle", label="fence"), dict(height=0.80, depth=0.8, expect="stop", label="block")]),
    # The no-stand station: tops the level marks as no place to stand -- their
    # mesh ("nostand") named in the layout's traversal.noStandMeshes, or an area round the top ("stand": "none") -- each
    # beside an unmarked twin: a 0.5 m top a plain jump lands on (the body slides off), a 1.5 m block (no mantle onto
    # it), a platform up a short stair whose top beyond the stair's head is marked (walking stops at it), a 0.5 m top
    # marked by an area, and a thin 0.8 m wall marked (a vault still goes over it). A marked lane records kind
    # "nostand" and how it is marked ("nostand": "mesh" or "area").
    dict(name="no-stand", kind="nostand", at=(38, 24), pitch=4.5, lanes=[
        dict(height=0.5, nostand="mesh", expect="nostand", label="jump"), dict(height=0.5, label="jump-twin"),
        dict(height=1.5, nostand="mesh", expect="nostand", label="mantle"), dict(height=1.5, label="mantle-twin"),
        dict(kind="nostair", height=0.6, nostand="mesh", expect="nostand", label="stair"),
        dict(kind="nostair", height=0.6, expect="stride", label="stair-twin"),
        dict(height=0.5, nostand="area", expect="nostand", label="jump-area"),
        dict(height=0.8, depth=0.3, nostand="mesh", expect="nostand", label="vault")]),
]

# ---------------------------------------------------------------------------------------------------- what to expect


def expect_of(station, lane):
    """What the human should do at a lane (a LEGEND key), from the human's numbers; a lane's "expect" decides."""
    if "expect" in lane:
        return lane["expect"]
    if lane.get("noclimb"):
        return "noclimb"
    kind = lane.get("kind", station["kind"])
    h = lane.get("height", 0.0)
    if kind == "stairs":
        if lane.get("headroom", 9.0) < HUMAN["height"]:
            return "crouch"         # lower than the body: a crouch passes (a stair that needs it is a level's finding)
        return "stride" if lane["riser"] <= HUMAN["stride"] else "stop"
    if kind == "ramp":
        return "stride" if lane["degrees"] <= HUMAN["walked"] else "toohigh"
    if kind == "tunnel":
        return "crouch" if lane["clear"] >= HUMAN["crouched"] else "toohigh"
    if kind == "door":
        if lane["width"] <= HUMAN["width"]:
            return "toohigh"
        if lane["height"] <= HUMAN["height"] + 0.05:
            return "crouch"
        return "stride"
    if kind in ("gap", "drop", "pit"):
        return "neutral"
    if kind == "ladder":
        return "ladder"
    if kind == "shimmy":
        return "grab"               # a tap grabs the ledge; hanging, left and right shimmy along it
    if kind in ("corner", "leap"):
        return "grab"               # a tap grabs the lip; hanging, round its corners, the leaps
    if kind == "wall":
        return "wall"               # a tap gets on; the move keys climb
    if kind == "beam":
        return "notbuilt"
    if kind == "canopy":
        return "grab"
    if station["kind"] == "yard":
        return "toohigh"
    deep = lane.get("depth", 2.0) >= HUMAN["deep"]
    if h <= HUMAN["stride"]:
        return "stride"
    if not deep and station.get("prefer") == "hurdle" and h <= HUMAN["hurdle"]:
        return "hurdle"
    if deep and h <= HUMAN["hop"]:
        return "stop"
    if h <= HUMAN["vault"][1]:
        return "vault"
    if deep and h <= HUMAN["mantle"]:
        return "mantle"
    if h <= HUMAN["grab"] and deep:
        return "grab"
    return "toohigh"


# The no-stand station's signs, by lane.
NOSTAND_WORDS = {"jump": "NO STAND: SLIDE", "mantle": "NO STAND", "stair": "NO STAND: STOP", "jump-area": "NO STAND: AREA",
                 "vault": "NO STAND: VAULT"}


def sign_word(station, lane, expect):
    kind = lane.get("kind", station["kind"])
    if lane.get("nostand"):
        return NOSTAND_WORDS.get(lane.get("label"), "NO STAND")
    deep = lane.get("depth", 2.0) >= HUMAN["deep"]
    if lane.get("noclimb") == "area":
        return "NO CLIMB: AREA"
    if kind in ("block", "kerb", "table") and expect == "vault" and deep:
        return "HOLD: CLIMB"
    if expect == "stop" and lane.get("height", 0) <= HUMAN["hop"] and lane.get("height", 0) > HUMAN["stride"]:
        return "STOP / TAP: HOP"
    if kind == "canopy":
        return "HOLD: HANG"
    if kind == "shimmy":
        return "HANG: SHIMMY"
    if kind == "corner":
        return {"braced": "HANG: CORNERS", "free": "HANG: BEAM CORNERS", "thin": "CORNER: TOO SHORT"}[lane["form"]]
    if kind == "leap":
        return {"columns": "HANG: JUMP+SIDE, JUMP, CROUCH+USE", "behind": "HANG: BACK+JUMP"}[lane["form"]]
    if kind == "wall":
        return {"holds": "CLIMB: HOLDS", "corners": "CLIMB: CORNERS", "sheer": "CLIMB: UNBOUND", "noclimb": "NO CLIMB"}[lane["form"]]
    if kind == "squeeze":
        return "BENDS: NO WAY" if lane.get("bend") else ("TOO NARROW" if expect == "toohigh" else "SQUEEZE")
    if kind == "duck":
        return "CROUCH WALK: DUCK"
    if kind == "ladder" and lane.get("lean"):
        return "USE: LADDER %d°" % round(lane["lean"])
    if kind == "ladder" and lane.get("side"):
        return "LADDER: LANDING"                           # off and on at rung 2
    if kind == "ladder" and lane.get("ledge"):
        return "LADDER: LEDGE"                             # into the braced hang at rung 8 and back
    if kind == "gap":
        return "JUMP"
    if kind == "drop":
        return "STEP OFF"
    if kind == "pit":
        return "NO WAY OUT"
    if kind == "ramp" and expect == "toohigh":
        return "TOO STEEP"
    if kind == "door" and expect == "toohigh":
        return "TOO NARROW"
    if kind == "tunnel" and expect == "toohigh":
        return "TOO LOW"
    if station["kind"] == "yard":
        return "ELDER"
    return LEGEND[expect][1]


def metres(v):
    """A height as the signs and the starts' names print it: two decimals under 2 m (0.40, 1.85), else one (2.0, 2.2)."""
    if v < 2.0 or round(v * 100) % 10:
        return "%.2f" % v
    return "%.1f" % v


def number_text(station, lane):
    kind = lane.get("kind", station["kind"])
    if kind == "stairs":
        if "headroom" in lane:
            return metres(lane["headroom"])
        return "%.2f" % lane["riser"]
    if kind == "ramp":
        return "%d°" % lane["degrees"]
    if kind == "tunnel":
        return metres(lane["clear"])
    if kind == "door":
        return "%.1f X %.1f" % (lane["width"], lane["height"])
    if kind == "gap":
        return "%.1f" % lane["gap"]
    if kind == "drop":
        return "%g M" % lane["height"]
    if kind == "pit":
        return "%.1f" % lane["depth"]
    if kind == "ladder":
        return metres(lane["height"])
    if kind == "shimmy":
        return metres(lane["lip"])
    if kind == "beam":
        return kind.upper()
    if kind == "canopy":
        return metres(lane["height"])
    if kind == "squeeze":
        return "%.2f" % lane["gap"]
    if kind == "cover":
        return "COVER"
    if kind == "duck":
        return metres(lane["clear"])
    return metres(lane["height"])


def label_of(station, lane):
    if "label" in lane:
        return lane["label"]
    kind = lane.get("kind", station["kind"])
    if kind == "stairs":
        return "%.2f" % lane["riser"]
    if kind == "ramp":
        return "%d" % lane["degrees"]
    if kind == "tunnel":
        return metres(lane["clear"])
    if kind == "gap":
        return "%.1f" % lane["gap"]
    if kind == "drop":
        return "%g" % lane["height"]
    if kind == "pit":
        return "deep"
    return metres(lane["height"])

# ---------------------------------------------------------------------------------------------------- geometry


def srgb_to_linear(c):
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c]


class Course:
    """The triangles by mesh and material (Z up, metres), the signs, the anchors and the lanes' record."""

    def __init__(self):
        self.meshes = {}         # mesh -> material -> [positions, normals, uvs, indices]
        self.materials = {}      # material -> (linear colour, texture)
        self.signs = []          # (text line 1, text line 2)
        self.anchors = []
        self.lanes = []
        self.plan = []           # (station, x0, x1, y0, y1)
        self.holes = []          # floor holes (x0, x1, y0, y1)
        self.no_climb = []       # the areas marked "climb": "none" (the layout's areas)
        self.no_stand = []       # the areas marked "stand": "none"
        self.ladders = []        # the layout's traversal.ladders
        self.holds = []          # the areas of walls with holds ("climb": "holds")

    def material(self, name, colour, texture="grid"):
        if name not in self.materials:
            self.materials[name] = (srgb_to_linear(colour), texture)
        return name

    def cat(self, expect, top):
        """The material of a category: tops (stood on) are ground, every other face a wall."""
        colour = LEGEND[expect][0]
        if top:
            return self.material(expect + "_ground", colour)
        return self.material(expect + "_side", tuple(c * 0.82 for c in colour))

    def poly(self, mat, pts, uvs=None, mesh="course"):
        """A flat convex polygon, counter-clockwise seen from the side it faces; its normal by Newell's rule."""
        nx = ny = nz = 0.0
        for i, a in enumerate(pts):
            b = pts[(i + 1) % len(pts)]
            nx += (a[1] - b[1]) * (a[2] + b[2])
            ny += (a[2] - b[2]) * (a[0] + b[0])
            nz += (a[0] - b[0]) * (a[1] + b[1])
        length = math.sqrt(nx * nx + ny * ny + nz * nz)
        if length < 1e-9:
            return
        n = (nx / length, ny / length, nz / length)
        prim = self.meshes.setdefault(mesh, {}).setdefault(mat, [[], [], [], []])
        base = len(prim[0])
        for i, p in enumerate(pts):
            prim[0].append(p)
            prim[1].append(n)
            if uvs is not None:
                prim[2].append(uvs[i])
            elif abs(n[2]) >= max(abs(n[0]), abs(n[1])):
                prim[2].append((p[0] / 5.0, -p[1] / 5.0))
            elif abs(n[0]) >= abs(n[1]):
                prim[2].append((-p[1] / 5.0 if n[0] > 0 else p[1] / 5.0, -p[2] / 5.0))
            else:
                prim[2].append((p[0] / 5.0 if n[1] > 0 else -p[0] / 5.0, -p[2] / 5.0))
        for i in range(1, len(pts) - 1):
            prim[3].extend((base, base + i, base + i + 1))

    def box(self, x0, x1, y0, y1, z0, z1, expect, bottom=None, mesh="course", top_mat=None, side_mat=None):
        """A solid box; its top is ground; its bottom is left out when it stands on the floor."""
        top = top_mat or self.cat(expect, True)
        side = side_mat or self.cat(expect, False)
        if bottom is None:
            bottom = z0 > 0.001
        P = lambda x, y, z: (x, y, z)
        self.poly(top, [P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1)], mesh=mesh)
        if bottom:
            self.poly(side, [P(x0, y0, z0), P(x0, y1, z0), P(x1, y1, z0), P(x1, y0, z0)], mesh=mesh)
        self.poly(side, [P(x0, y0, z0), P(x1, y0, z0), P(x1, y0, z1), P(x0, y0, z1)], mesh=mesh)   # -Y
        self.poly(side, [P(x0, y1, z0), P(x0, y1, z1), P(x1, y1, z1), P(x1, y1, z0)], mesh=mesh)   # +Y
        self.poly(side, [P(x0, y1, z0), P(x0, y0, z0), P(x0, y0, z1), P(x0, y1, z1)], mesh=mesh)   # -X
        self.poly(side, [P(x1, y0, z0), P(x1, y1, z0), P(x1, y1, z1), P(x1, y0, z1)], mesh=mesh)   # +X

    def profile(self, segments, y0, y1):
        """
        Ground built up from the floor along +X between y0 and y1: each segment (x0, x1, z0, z1, expect) a top
        rising from z0 to z1 (flat, or a ramp), solid down to the floor; a segment at height 0 is the floor (a gap).
        The faces between segments of different heights face the lower side; the sides are trapezia.
        """
        previous = 0.0
        prev_exp = None
        for i, (x0, x1, z0, z1, exp) in enumerate(segments):
            # the face at x0 between what was before (previous) and this segment's start (z0)
            if abs(z0 - previous) > 1e-6:
                lo, hi = min(previous, z0), max(previous, z0)
                side = self.cat(exp if z0 > previous else prev_exp, False)
                if z0 > previous:   # faces -X
                    self.poly(side, [(x0, y1, lo), (x0, y0, lo), (x0, y0, hi), (x0, y1, hi)])
                else:               # faces +X
                    self.poly(side, [(x0, y0, lo), (x0, y1, lo), (x0, y1, hi), (x0, y0, hi)])
            if z0 > 1e-6 or z1 > 1e-6:
                top = self.cat(exp, True)
                side = self.cat(exp, False)
                self.poly(top, [(x0, y0, z0), (x1, y0, z1), (x1, y1, z1), (x0, y1, z0)])
                self.poly(side, [(x0, y0, 0.0), (x1, y0, 0.0), (x1, y0, z1), (x0, y0, z0)])
                self.poly(side, [(x0, y1, 0.0), (x0, y1, z0), (x1, y1, z1), (x1, y1, 0.0)])
            previous, prev_exp = z1, exp
        if previous > 1e-6:
            x = segments[-1][1]
            side = self.cat(prev_exp, False)
            self.poly(side, [(x, y0, 0.0), (x, y1, 0.0), (x, y1, previous), (x, y0, previous)])

    def sign(self, x, y, base, line1, line2, wide=1.6, high=0.4, lift=1.0):
        """A board facing -X (towards a lane's run-up) on a post, drawn and stopping nothing (the "signs" mesh)."""
        index = len(self.signs) + 1          # cell 0 is blank: the board's back and post
        self.signs.append((line1, line2))
        mat = self.material("signs", (1.0, 1.0, 1.0), texture="signs")
        u0, v0, u1, v1 = sign_cell(index)
        bu0, bv0, bu1, bv1 = sign_cell(0)
        ya, yb = y - wide / 2, y + wide / 2
        za, zb = base + lift, base + lift + high
        # the front, seen from -X: its left (u 0) at +Y, its top (v 0) up
        self.poly(mat, [(x, yb, za), (x, ya, za), (x, ya, zb), (x, yb, zb)],
                  uvs=[(u0, v1), (u1, v1), (u1, v0), (u0, v0)], mesh="signs")
        self.poly(mat, [(x + 0.02, ya, za), (x + 0.02, yb, za), (x + 0.02, yb, zb), (x + 0.02, ya, zb)],
                  uvs=[(bu0, bv1), (bu1, bv1), (bu1, bv0), (bu0, bv0)], mesh="signs")
        self.box(x, x + 0.06, y - 0.03, y + 0.03, 0.0, za, "neutral", mesh="signs", top_mat=mat, side_mat=mat)
        return index

    def sign_facing(self, x, y, base, line1, line2, yaw, wide=1.6, high=0.4, lift=1.0):
        """
        A board on a post as sign() makes, its face looking along yaw (radians, counter-clockwise from +X: towards whoever
        reads it), drawn and stopping nothing (the bush's and the dark corner's, facing the start).
        """
        index = len(self.signs) + 1
        self.signs.append((line1, line2))
        mat = self.material("signs", (1.0, 1.0, 1.0), texture="signs")
        u0, v0, u1, v1 = sign_cell(index)
        bu0, bv0, bu1, bv1 = sign_cell(0)
        fx, fy = math.cos(yaw), math.sin(yaw)
        lx, ly = fy, -fx                       # the reader's left
        za, zb = base + lift, base + lift + high
        L = (x + lx * wide / 2, y + ly * wide / 2)
        R = (x - lx * wide / 2, y - ly * wide / 2)
        self.poly(mat, [(L[0], L[1], za), (R[0], R[1], za), (R[0], R[1], zb), (L[0], L[1], zb)],
                  uvs=[(u0, v1), (u1, v1), (u1, v0), (u0, v0)], mesh="signs")
        bx, by = -fx * 0.02, -fy * 0.02
        self.poly(mat, [(R[0] + bx, R[1] + by, za), (L[0] + bx, L[1] + by, za), (L[0] + bx, L[1] + by, zb), (R[0] + bx, R[1] + by, zb)],
                  uvs=[(bu0, bv1), (bu1, bv1), (bu1, bv0), (bu0, bv0)], mesh="signs")
        px, py = x - fx * 0.05, y - fy * 0.05
        self.box(px - 0.03, px + 0.03, py - 0.03, py + 0.03, 0.0, za, "neutral", mesh="signs", top_mat=mat, side_mat=mat)
        return index

    def clump(self, x, y, base, radius, height, shade, sides=8):
        """
        A low-poly clump of leaves (the bush): rings of sides points — the foot a little in, the widest at under half
        its height, the shoulder in again — and a crown; flat colour (the signs' blank cell), in the signs' mesh, so it
        stops nothing.
        """
        mat = self.material("bush_%d" % shade, BUSH_GREENS[shade], texture="signs")
        u0, v0, u1, v1 = sign_cell(0)
        uv = ((u0 + u1) / 2, (v0 + v1) / 2)
        rings = [(0.8, 0.0), (1.0, 0.42), (0.62, 0.82)]
        turn = (x * 7.3 + y * 3.1) % 1.0      # each clump a little turned, so they do not line up
        pts = [[(x + radius * k * math.cos(2 * math.pi * (i + turn) / sides), y + radius * k * math.sin(2 * math.pi * (i + turn) / sides),
                 base + height * h) for i in range(sides)] for k, h in rings]
        for a, b in zip(pts, pts[1:]):
            for i in range(sides):
                j = (i + 1) % sides
                self.poly(mat, [a[i], a[j], b[j], b[i]], uvs=[uv] * 4, mesh="signs")
        crown = (x, y, base + height)
        top = pts[-1]
        for i in range(sides):
            self.poly(mat, [top[i], top[(i + 1) % sides], crown], uvs=[uv] * 3, mesh="signs")

    def plate(self, x, y, base, index, wide=2.0, deep=0.5):
        """The sign's lettering painted on the ground before an obstacle (from x towards it), read from the run-up."""
        mat = self.material("signs", (1.0, 1.0, 1.0), texture="signs")
        u0, v0, u1, v1 = sign_cell(index)
        ya, yb, z = y - wide / 2, y + wide / 2, base + 0.01
        # seen from above, its near edge (towards -X) the text's foot and its +Y side the text's left
        self.poly(mat, [(x, ya, z), (x + deep, ya, z), (x + deep, yb, z), (x, yb, z)],
                  uvs=[(u1, v1), (u1, v0), (u0, v0), (u0, v1)], mesh="signs")

    def anchor(self, name, x, y, z, yaw=0.0):
        if any(a["name"] == name for a in self.anchors):
            raise SystemExit("two starts are named %s" % name)
        self.anchors.append({"kind": "start", "name": name, "position": [round(x, 3), round(y, 3), round(z + 0.02, 3)],
                             "rotation": [0, 0, yaw]})


# ---------------------------------------------------------------------------------------------------- the stations

TREAD = 0.30
WAY_RISER = 0.20    # the risers of the ways up to platforms and ledges


def steps_up(x, rise, riser, tread, exp):
    """Segments of a flight from the floor up to (not including) a top at `rise`: x moves on by each tread."""
    n = max(1, int(round(rise / riser)))
    segs = []
    for k in range(1, n):
        segs.append((x, x + tread, k * rise / n, k * rise / n, exp))
        x += tread
    return segs, x


def steps_down(x, rise, riser, tread, exp):
    n = max(1, int(round(rise / riser)))
    segs = []
    for k in range(n - 1, 0, -1):
        segs.append((x, x + tread, k * rise / n, k * rise / n, exp))
        x += tread
    return segs, x


def build_station(c, st):
    kind = st["kind"]
    ax, ay = st["at"]
    runup = st.get("runup", RUNUP)
    lanes = st["lanes"]
    y = ay
    centres = []
    for lane in lanes:
        pitch = lane.get("pitch", st.get("pitch", PITCH))
        centres.append((y + pitch / 2, pitch))
        y += pitch
    y_end = y
    x_end = ax + runup
    xf = ax + runup                 # the obstacles' near face

    if kind == "door":
        x_end = max(x_end, build_doors(c, st, centres, xf))

    if kind == "drop":
        tallest = max(l["height"] for l in lanes)
        edge = ax + (round(tallest / WAY_RISER) - 1) * TREAD + 3.0

    for i, (lane, (yc, pitch)) in enumerate(zip(lanes, centres)):
        lk = lane.get("kind", kind)
        exp = expect_of(st, lane)
        label = label_of(st, lane)
        name = "%s-%s" % (st["name"], label)
        w = lane.get("width", WIDTH)
        ya, yb = yc - w / 2, yc + w / 2
        record = {"start": name, "station": st["name"], "kind": lk, "label": label, "expect": exp,
                  "y": round(yc, 3), "face": round(xf, 3), "base": 0.0}
        record.update({k: v for k, v in lane.items() if isinstance(v, (int, float)) and not isinstance(v, bool)})
        start = (ax + 1.0, yc, 0.0)
        sign_at = (xf - 1.2, yc + w / 2 + 0.9, 0.0)

        if lk == "stairs":
            r = lane["riser"]
            n = lane.get("steps", max(2, int(round(1.2 / r))))
            rise = n * r
            landing = lane.get("landing", 2.0)
            flight = "stride" if r <= HUMAN["stride"] else "stop"
            if lane.get("open"):
                x = xf
                for k in range(1, n):
                    c.box(x, x + TREAD, ya, yb, k * r - 0.05, k * r, flight, bottom=True)
                    x += TREAD
                c.box(x, x + landing, ya, yb, 0.0, rise, flight)
                x += landing
                for k in range(n - 1, 0, -1):
                    c.box(x, x + TREAD, ya, yb, k * r - 0.05, k * r, flight, bottom=True)
                    x += TREAD
            else:
                up, x = steps_up(xf, rise, r, TREAD, flight)
                segs = up + [(x, x + landing, rise, rise, flight)]
                down, x = steps_down(x + landing, rise, r, TREAD, flight)
                c.profile(segs + down, ya, yb)
            if "headroom" in lane:
                # A slab over the flight's lower part, its edge over the nosing of the step two below the landing; the
                # headroom is from that nosing up to the slab's underside (the way the East Gate tower's was measured).
                k = max(1, n - 2)
                edge = xf + (k - 1) * TREAD
                under = k * r + lane["headroom"]
                c.box(xf - 3.0, edge, ya - 0.5, yb + 0.5, under, under + 0.3,
                      "crouch" if lane["headroom"] < HUMAN["height"] else "stride", bottom=True)
                record.update(edge=round(edge, 3), nosing=round(k * r, 3), under=round(under, 3))
            record.update(top=round(rise, 3), cross=round(x + 0.5, 3), far=round(x, 3))
            x_end = max(x_end, x + 3)

        elif lk == "kerb":
            h, d = lane["height"], lane.get("depth", 3.0)
            c.profile([(xf, xf + d, h, h, exp)], ya, yb)
            record.update(top=h, cross=round(xf + d + 0.5, 3), far=round(xf + d, 3))
            x_end = max(x_end, xf + d + 3)

        elif lk == "ramp":
            deg = lane["degrees"]
            length = lane.get("length", 4.0)
            run, rise = length * math.cos(math.radians(deg)), length * math.sin(math.radians(deg))
            c.profile([(xf, xf + run, 0.0, rise, exp), (xf + run, xf + run + 2.0, rise, rise, "neutral")], ya, yb)
            record.update(top=round(rise, 3), cross=round(xf + run + 0.3, 3), far=round(xf + run + 2.0, 3))
            x_end = max(x_end, xf + run + 4)

        elif lk == "block" or lk == "yard":
            h, d = lane["height"], lane.get("depth", 2.0)
            mark = lane.get("noclimb")
            c.box(xf, xf + d, ya, yb, 0.0, h, exp, mesh="noclimb" if mark == "mesh" else "course")
            if mark == "area":
                # the block's top and its edges, half a metre round and from half a metre under the top to a metre over
                c.no_climb.append({"name": name, "kind": "zone", "exposure": 0, "climb": "none",
                                   "box": {"origin": [round(xf - 0.5, 3), round(ya - 0.5, 3), round(h - 0.5, 3)], "rotZ": 0,
                                           "size": [round(d + 1.0, 3), round(w + 1.0, 3), 1.5]}})
            if mark:
                record.update(kind="noclimb", noclimb=mark)
            record.update(top=h, depth=d, cross=round(xf + d + 0.4, 3), far=round(xf + d, 3))
            x_end = max(x_end, xf + d + (8.0 if exp == "hurdle" or st.get("prefer") == "hurdle" else 3.0))

        elif lk in ("rail", "vaultable"):
            # The pace's stations: a box across the lane, as a block's, with room beyond to come down and run on.
            h, d = lane["height"], lane["depth"]
            c.box(xf, xf + d, ya, yb, 0.0, h, exp)
            record.update(top=h, depth=d, cross=round(xf + d + 0.4, 3), far=round(xf + d, 3))
            x_end = max(x_end, xf + d + 8.0)

        elif lk == "table":
            h, d = lane["height"], lane["depth"]
            c.box(xf, xf + d, ya, yb, h - 0.05, h, exp, bottom=True)
            for lx in (xf + 0.05, xf + d - 0.11):
                for ly in (ya + 0.05, yb - 0.11):
                    c.box(lx, lx + 0.06, ly, ly + 0.06, 0.0, h - 0.05, exp)
            record.update(top=h, depth=d, cross=round(xf + d + 0.4, 3), far=round(xf + d, 3))
            x_end = max(x_end, xf + d + 3)

        elif lk == "canopy":
            h, d, room = lane["height"], lane["depth"], lane["room"]
            c.box(xf, xf + d, ya, yb, 0.0, h, exp)
            c.box(xf, xf + d, ya, yb, h + room, h + room + 0.3, "toohigh", bottom=True)
            for py in (ya, yb - 0.2):
                c.box(xf + d - 0.2, xf + d, py, py + 0.2, h, h + room, "toohigh")
            record.update(top=h, depth=d, room=room, far=round(xf + d, 3))
            x_end = max(x_end, xf + d + 2)

        elif lk == "tunnel":
            clear, length = lane["clear"], lane.get("length", 3.0)
            inner = lane.get("inner", 1.2)
            c.box(xf, xf + length, ya, yc - inner / 2, 0.0, clear + 0.3, "toohigh")
            c.box(xf, xf + length, yc + inner / 2, yb, 0.0, clear + 0.3, "toohigh")
            c.box(xf, xf + length, ya, yb, clear, clear + 0.3, exp, bottom=True)
            record.update(top=0.0, cross=round(xf + length + 0.5, 3), far=round(xf + length, 3))
            x_end = max(x_end, xf + length + 4)

        elif lk == "door":
            record.update(top=0.0, cross=round(xf + 0.3 + 0.5, 3), far=round(xf + 0.3, 3))
            sign_at = (xf - 1.2, yc + 1.2, 0.0)

        elif lk == "gap":
            h = lane.get("height", 1.0)
            far_h = h - lane.get("drop", 0.0)
            g = lane["gap"]
            up, x = steps_up(ax, h, WAY_RISER, TREAD, "neutral")
            near = (x, x + runup, h, h, "neutral")
            edge = x + runup
            segs = up + [near, (edge, edge + g, 0.0, 0.0, "neutral")]
            segs.append((edge + g, edge + g + 4.0, far_h, far_h, "neutral"))
            down, x2 = steps_down(edge + g + 4.0, far_h, WAY_RISER, TREAD, "neutral")
            c.profile(segs + down, ya, yb)
            start = (edge - 8.0, yc, h)
            record.update(face=round(edge, 3), base=h, top=far_h, cross=round(edge + g + 0.3, 3), far=round(edge + g + 4.0, 3))
            sign_at = (edge - 1.2, yc + w / 2 + 0.9, 0.0)
            x_end = max(x_end, x2 + 2)

        elif lk == "drop":
            h = lane["height"]
            n = int(round(h / WAY_RISER))
            x0 = edge - 3.0 - (n - 1) * TREAD
            up, x = steps_up(x0, h, WAY_RISER, TREAD, "neutral")
            c.profile(up + [(x, edge, h, h, "neutral")], ya, yb)
            start = (edge - 2.5, yc, h)
            record.update(face=round(edge, 3), base=float(h), top=0.0, cross=round(edge + 0.5, 3), far=round(edge, 3))
            sign_at = (edge - 1.2, yc + w / 2 + 0.9, 0.0)
            x_end = max(x_end, edge + 8.0)

        elif lk == "pit":
            size, depth = lane["size"], lane["depth"]
            px0, px1, py0, py1 = xf, xf + size, yc - size / 2, yc + size / 2
            c.holes.append((px0, px1, py0, py1))
            wall = c.cat("toohigh", False)
            z0 = -depth
            c.poly(wall, [(px0, py0, z0), (px0, py1, z0), (px0, py1, 0.0), (px0, py0, 0.0)])   # faces +X
            c.poly(wall, [(px1, py1, z0), (px1, py0, z0), (px1, py0, 0.0), (px1, py1, 0.0)])   # faces -X
            c.poly(wall, [(px1, py0, z0), (px0, py0, z0), (px0, py0, 0.0), (px1, py0, 0.0)])   # faces +Y
            c.poly(wall, [(px0, py1, z0), (px1, py1, z0), (px1, py1, 0.0), (px0, py1, 0.0)])   # faces -Y
            c.poly(c.cat("neutral", True), [(px0, py0, z0), (px1, py0, z0), (px1, py1, z0), (px0, py1, z0)])
            record.update(top=-depth, cross=round(px0 + size / 2, 3), far=round(px1, 3))
            sign_at = (xf - 1.2, yc + size / 2 + 0.9, 0.0)
            x_end = max(x_end, px1 + 3)

        elif lk in ("nostand", "nostair"):
            # The no-stand station: a block, or a platform up a short stair, its top marked (mesh "nostand", or an
            # area round the top only, "stand": "none") or not (the twins). A marked lane's top is coloured "nostand".
            h, d = lane["height"], lane.get("depth", 2.0)
            mark = lane.get("nostand")
            look = exp
            x = xf
            if lk == "nostair":
                # the stair (risers 0.2 m) and its head, a metre deep, never marked; the platform beyond it, 3 m deep
                up, x = steps_up(xf, h, WAY_RISER, TREAD, "stride")
                c.profile(up + [(x, x + 1.0, h, h, "stride")], ya, yb)
                x += 1.0
                d = 3.0
                record.update(head=round(x, 3))
            c.box(x, x + d, ya, yb, 0.0, h, look, mesh="nostand" if mark == "mesh" else "course")
            if mark == "area":
                # round the top only: a little wider than the block, from 0.15 m under the top to 0.25 m over it
                c.no_stand.append({"name": name, "kind": "zone", "exposure": 0, "stand": "none",
                                   "box": {"origin": [round(x - 0.1, 3), round(ya - 0.1, 3), round(h - 0.15, 3)], "rotZ": 0,
                                           "size": [round(d + 0.2, 3), round(w + 0.2, 3), 0.4]}})
            if mark:
                record.update(kind="nostand", nostand=mark)
            else:
                record.update(kind="nostair" if lk == "nostair" else "block")
            record.update(top=h, depth=d, cross=round(x + d + 0.4, 3), far=round(x + d, 3))
            x_end = max(x_end, x + d + 3.0)

        elif lk == "ladder":
            # A tower whose top is a floor (2.5 m deep), and a ladder up its face, facing back along -X; the ladder's line in the layout is its
            # climbing face (the rungs' and the rails' front), from the ground to the top's height. The rails and rungs are their own mesh ("ladder"),
            # named not to be climbed (traversal.noClimbMeshes), so a jump's moves never take a rung for a lip: a ladder is climbed by use.
            # The models repository's ladders: rungs every 0.30 m along the rails from 0.30 m up, each one's top at that height (the
            # floor at the top is a rung's), 0.04 m deep, between rails 0.48 m apart (their middles); upright, 0.20 m out from the face; leaning
            # (lean: degrees off upright), its top rung's back on the floor's edge and its foot out. A side landing (side: "l" or "r", the climber's
            # left or right as he faces the ladder) level with rung 2; a braced ledge (ledge: "l" or "r", its top ledge_at) beside it, flush with the face.
            h = lane["height"]
            lean = lane.get("lean", 0.0)
            tilt = math.tan(math.radians(lean))
            side, ledge = lane.get("side"), lane.get("ledge")
            # the climber faces +X: his left is +Y
            if ledge:
                lz = lane["ledge_at"]
                sgn = 1.0 if ledge == "l" else -1.0
                near, far = yc + sgn * 0.35, yc + sgn * w / 2
                c.box(xf, xf + 2.5, min(near, yc - sgn * w / 2), max(near, yc - sgn * w / 2), 0.0, h, "neutral")
                c.box(xf, xf + 2.5, min(near, far), max(near, far), 0.0, lz, "grab")
                record.update(ledge=lz, ledgeSide=ledge)
            else:
                c.box(xf, xf + 2.5, ya, yb, 0.0, h, "neutral")
            front_top = xf - (0.04 if lean > 0.0 else 0.2)

            def front(z):
                return front_top - (h - z) * tilt
            for k in (-1, 1):
                ry = yc + k * 0.24
                if lean > 0.0:
                    # a slanted rail: a box from its foot to its top along the lean
                    b0, b1, t0, t1 = front(0.0), front(0.0) + 0.05, front(h), front(h) + 0.05
                    y0, y1 = ry - 0.025, ry + 0.025
                    m = c.cat(exp, False)
                    c.poly(m, [(b0, y0, 0.0), (b1, y0, 0.0), (t1, y0, h), (t0, y0, h)], mesh="ladder")
                    c.poly(m, [(b0, y1, 0.0), (t0, y1, h), (t1, y1, h), (b1, y1, 0.0)], mesh="ladder")
                    c.poly(m, [(b0, y1, 0.0), (b0, y0, 0.0), (t0, y0, h), (t0, y1, h)], mesh="ladder")
                    c.poly(m, [(b1, y0, 0.0), (b1, y1, 0.0), (t1, y1, h), (t1, y0, h)], mesh="ladder")
                    c.poly(c.cat(exp, True), [(t0, y0, h), (t1, y0, h), (t1, y1, h), (t0, y1, h)], mesh="ladder")
                else:
                    c.box(xf - 0.2, xf - 0.14, ry - 0.025, ry + 0.025, 0.0, h, exp, mesh="ladder")
            n = 1
            while True:
                z = 0.3 + (n - 1) * 0.3 * math.cos(math.radians(lean))
                if z > h + 1e-3:
                    break
                x = front(z)
                c.box(x, x + 0.04, yc - 0.24, yc + 0.24, z - 0.04, z, exp, bottom=True, mesh="ladder")
                n += 1
            if side:
                sgn = 1.0 if side == "l" else -1.0
                # the landing beside the foot, up to the face, its top level with rung 2's (0.6 m), from just past the rail
                c.box(xf - 1.3, xf, min(yc + sgn * 0.3, yc + sgn * 1.5), max(yc + sgn * 0.3, yc + sgn * 1.5), 0.0, 0.6, "neutral")
                record.update(landing=0.6, landingSide=side)
            c.ladders.append({"name": name, "foot": [round(front(0.0), 3), round(yc, 3), 0.0],
                              "top": [round(front(h), 3), round(yc, 3), round(h, 3)], "width": 0.53, "facing": 180})
            record.update(top=round(h, 3), far=round(xf + 2.5, 3), line=round(front(0.0), 3), lean=lean)
            x_end = max(x_end, xf + 5.5)

        elif lk == "beam":
            h, bw, length = lane["height"], lane.get("width", 0.2), lane.get("length", 6.0)
            c.box(xf, xf + length, yc - bw / 2, yc + bw / 2, h - 0.15, h, exp, bottom=True)
            for px in (xf, xf + length - 0.2):
                c.box(px, px + 0.2, yc - bw / 2, yc + bw / 2, 0.0, h - 0.15, exp)
            record.update(top=h, far=round(xf + length, 3))
            x_end = max(x_end, xf + length + 3)

        elif lk == "corner":
            # The corners (the climber faces +X; his left is +Y). Braced: a block from 1.5 m right of the middle to 1.5 m left of it (the outside
            # corner at the left end, its side's lip 2.5 m long), and one coming toward the climber at the right (its face 1.5 m right of the middle, 2 m
            # out from the first's: the inside corner). Free: the same of slabs 0.2 m thick. Thin: a wall 0.3 m thick, 3 m wide (its ends too short).
            h, form = lane["height"], lane["form"]
            z0 = h - 0.2 if form == "free" else 0.0
            if form == "thin":
                c.box(xf, xf + 0.3, yc - 1.5, yc + 1.5, 0.0, h, "grab")
                record.update(top=h, far=round(xf + 0.3, 3))
            else:
                c.box(xf, xf + 2.5, yc - 1.5, yc + 1.5, z0, h, "grab", bottom=form == "free")
                c.box(xf - 2.0, xf + 2.5, yc - 3.5, yc - 1.5, z0, h, "grab", bottom=form == "free")
                record.update(top=h, far=round(xf + 2.5, 3), outside=1.5, inside=1.5)
            sign_at = (xf - 1.2, yc + 2.4, 0.0)
            x_end = max(x_end, xf + 3)
        elif lk == "leap":
            # The leaps (the climber faces +X; his left is +Y). Columns 1.2 m wide, 1.2 m apart (0.3 m gaps): the middle one's top 2.2 m with a lip
            # 1.0 m over it in its face (a groove 0.6 m high and 0.15 m deep between: no top to stand on, the upper hang's feet on the face below; the
            # upper lip's underside over the grabs' look from the ground, 0.3 m over the highest grab's band, 2.76 m); the
            # left one 2.2 m; the right one a lip at 3.2 m with one 1.2 m below it (2.0 m) under a groove 0.4 m high and 0.15 m deep. Behind: a wall
            # (2.2 m) and a facing one 2.0 m out from it (2.2 m), the way in round its ends.
            h, form = lane["height"], lane["form"]
            if form == "columns":
                c.box(xf, xf + 2.5, yc - 0.6, yc + 0.6, 0.0, h, "grab")
                c.box(xf + 0.15, xf + 2.5, yc - 0.6, yc + 0.6, h, h + 0.6, "neutral")
                c.box(xf, xf + 2.5, yc - 0.6, yc + 0.6, h + 0.6, h + 1.0, "grab")
                c.box(xf, xf + 2.5, yc + 0.9, yc + 2.1, 0.0, h, "grab")
                lo = h + 1.0 - 1.2
                c.box(xf, xf + 2.5, yc - 2.1, yc - 0.9, 0.0, lo, "grab")
                c.box(xf + 0.15, xf + 2.5, yc - 2.1, yc - 0.9, lo, lo + 0.4, "neutral")
                c.box(xf, xf + 2.5, yc - 2.1, yc - 0.9, lo + 0.4, h + 1.0, "grab")
                record.update(top=h, far=round(xf + 2.5, 3), upper=round(h + 1.0, 3), lower=round(lo, 3))
                sign_at = (xf - 1.2, yc + 2.9, 0.0)
            else:
                # The back wall 6 m wide, a ledge that goes on: jump with a side held there pulls up (no leap along it).
                c.box(xf, xf + 2.5, yc - 3.0, yc + 3.0, 0.0, h, "grab")
                c.box(xf - 3.0, xf - 2.0, yc - 1.5, yc + 1.5, 0.0, h, "grab")
                record.update(top=h, far=round(xf + 2.5, 3), behind=2.0, along=3.0)
                sign_at = (xf - 1.2, yc + 2.4, 0.0)
            x_end = max(x_end, xf + 3)
        elif lk == "wall":
            # The walls (the climber faces +X; his left is +Y). The holds' lattice: wall positions 0.4 m apart along the face from the level's own origin
            # (the face's coordinate along it, here y, rounded to 0.4) and up from the ground (rows 0.134 m + 0.4 j over it: the man's wall position on the
            # ground), the holds where the clips' own are at each (WALL_HOLDS); the motor finds them so (BodyMotor.Wall.cs), these are only drawn.
            h, form = lane["height"], lane["form"]
            if form == "holds":
                c.box(xf, xf + 2.0, ya, yb, 0.0, h, exp)
                wall_stones(c, xf, -1.0, (ya, yb), h, lattice_along(ya, yb))
                c.holds.append(holds_area(name, xf, ya, yb, h))
                record.update(top=h, far=round(xf + 2.0, 3), face=round(xf, 3))
            elif form == "corners":
                # The outside corner where the face ends to the left (0.3 m past a wall position: the clip's own), the inside one where a block comes toward
                # the climber to the right (0.5 m past one); a top at 3.2 m (none to climb over here).
                y_out = round(math.floor((yc + 1.1 - 0.3) / 0.4 + 0.5) * 0.4 + 0.3, 4)
                y_in = round(math.floor((yc - 1.3 + 0.5) / 0.4 + 0.5) * 0.4 - 0.5, 4)
                c.box(xf, xf + 2.0, y_in - 1.2, y_out, 0.0, h, exp)
                c.box(xf - 2.0, xf, y_in - 1.2, y_in, 0.0, h, exp)
                given = []
                given += wall_stones(c, xf, -1.0, (y_in, y_out), h, lattice_along(y_in + 0.05, y_out - 0.05), jitter=True)
                # The outside corner's other face (looking +Y at y_out): its wall positions from the corner's end (0.3 m out along it), x the way along.
                given += wall_stones(c, y_out, 1.0, (xf, xf + 2.0), h, [round(xf + 0.3 + 0.4 * k, 4) for k in range(0, 4)], jitter=True, along_x=True)
                # The inside corner's other face (looking +Y at y_in): from the corner's end (0.5 m toward the climber along it).
                given += wall_stones(c, y_in, 1.0, (xf - 2.0, xf), h, [round(xf - 0.5 - 0.4 * k, 4) for k in range(0, 4)], jitter=True, along_x=True)
                area = holds_area(name, xf, y_in - 1.2, y_out, h)
                area["box"] = {"origin": [round(xf - 2.6, 3), round(y_in - 1.8, 3), -0.1], "rotZ": 0,
                               "size": [round(2.6 + 2.2, 3), round(y_out - y_in + 1.8 + 0.6, 3), round(h + 0.4, 3)]}
                area["holds"] = given
                c.holds.append(area)
                record.update(top=h, far=round(xf + 2.0, 3), outside=round(y_out - yc, 3), inside=round(yc - y_in, 3), givenHolds=len(given))
                sign_at = (xf - 1.2, y_out + 0.9, 0.0)
            else:
                c.box(xf, xf + 2.0, ya, yb, 0.0, h, exp, mesh="noclimb" if form == "noclimb" else "course")
                record.update(top=h, far=round(xf + 2.0, 3))
            x_end = max(x_end, xf + 5)
        elif lk == "squeeze":
            # A gap between two walls (the body walks in along +X). The bend: 1.5 m straight, then it turns left (+Y) for 1.5 m.
            g, hh, wall_h, t = lane["gap"], lane["gap"] / 2, 2.4, 0.6
            if lane.get("bend"):
                c.box(xf, xf + 1.5, yc + hh, yc + hh + t, 0.0, wall_h, exp)                       # the left wall, to the turn
                c.box(xf + 1.5 - t, xf + 1.5, yc + hh + t, yc + hh + 1.5, 0.0, wall_h, exp)       # the turned leg's west wall
                c.box(xf, xf + 1.5 + g, yc - hh - t, yc - hh, 0.0, wall_h, exp)                   # the right wall, to the end
                c.box(xf + 1.5 + g, xf + 1.5 + g + t, yc - hh - t, yc + hh + 1.5, 0.0, wall_h, exp)   # across the end, on as the turned leg's east wall
                record.update(top=0.0, far=round(xf + 1.5 + g + t, 3), length=1.5)
            else:
                length = lane.get("length", 3.0)
                c.box(xf, xf + length, yc + hh, yc + hh + t, 0.0, wall_h, exp)
                c.box(xf, xf + length, yc - hh - t, yc - hh, 0.0, wall_h, exp)
                record.update(top=0.0, cross=round(xf + length + 0.8, 3), far=round(xf + length, 3), length=length)
            sign_at = (xf - 1.5, yc + 1.1, 0.0)
            x_end = max(x_end, xf + 6)
        elif lk == "cover":
            # A wall along the lane's north side (its face at yc + 0.6, looking -Y), both its ends free (the cover's end: a stop, said once).
            length, h = lane.get("length", 6.0), lane["height"]
            c.box(xf, xf + length, yc + 0.6, yc + 0.9, 0.0, h, exp)
            record.update(top=0.0, far=round(xf + length, 3), wallY=round(yc + 0.6, 3), length=length)
            sign_at = (xf - 1.5, yc - 1.1, 0.0)
            x_end = max(x_end, xf + length + 1)
        elif lk == "duck":
            # A wall across the lane, an opening 1.6 m wide and lane["clear"] high through it, lane["length"] deep: ducked under walking.
            clear, length, h = lane["clear"], lane.get("length", 1.5), lane["height"]
            c.box(xf, xf + length, yc - 1.5, yc - 0.8, 0.0, h, "toohigh")
            c.box(xf, xf + length, yc + 0.8, yc + 1.5, 0.0, h, "toohigh")
            c.box(xf, xf + length, yc - 0.8, yc + 0.8, clear, h, exp, bottom=True)
            record.update(top=0.0, cross=round(xf + length + 0.5, 3), far=round(xf + length, 3))
            sign_at = (xf - 1.5, yc + 1.1, 0.0)
            x_end = max(x_end, xf + length + 3)
        elif lk == "shimmy":
            lip, length = lane["lip"], lane.get("length", 10.0)
            c.box(xf, xf + 0.3, yc - length / 2, yc + length / 2, 0.0, lip + 1.6, exp)
            c.box(xf - 0.15, xf, yc - length / 2, yc + length / 2, lip - 0.1, lip, exp, bottom=True)
            record.update(top=lip, far=round(xf + 0.3, 3))
            sign_at = (xf - 1.2, yc - length / 2 - 0.9, 0.0)
            x_end = max(x_end, xf + 3)
        else:
            raise SystemExit("station %s: no kind %s" % (st["name"], lk))

        if i == 0:
            # the station's own start: its first lane's, except the yard's, which stands outside it on the public
            # floor so that its edge is crossed on foot
            first = (ax - 5.0, ay + st["size"][1] / 2, 0.0) if kind == "yard" else start
            c.anchor(st["name"], *first)
            # the station's board, beside its start
            c.sign(first[0] + 2.0, first[1] - 1.6, first[2], st.get("title", st["name"].upper()), "--START " + st["name"].upper(), lift=1.4)
        c.anchor(name, *start)
        record["at"] = c.anchors[-1]["position"]      # where the start stands (its feet 2 cm over the ground)
        c.lanes.append(record)
        word = sign_word(st, lane, exp)
        index = c.sign(sign_at[0], sign_at[1], sign_at[2] + record["base"], number_text(st, lane), word)
        plate_x = (record["face"] if lk not in ("gap", "drop") else record["face"]) - 1.7
        if lk == "pit":
            plate_x = xf - 1.7
        c.plate(plate_x, yc, record["base"], index)

    c.plan.append((st["name"], ax, x_end, ay, y_end))
    return x_end


# Where each limb's hold is from the man's wall position (the models repository's wall_idle: traverse.wall.holds, left_m and up_m) and the wall
# position's height over the ground (its ground_up_m, -0.134): the lattice's holds are these at every wall position, 0.4 m apart along the face and up. The
# woman's own are within 1.5 cm of them (her hands 1.5 cm lower). Only drawn: the motor reads the clips' own (BodyMotor.Wall.cs).
WALL_HOLDS = [("hand", 0.23, 1.577), ("hand", -0.21, 1.477), ("foot", 0.18, 0.316), ("foot", -0.15, 0.436)]
WALL_ROW0 = 0.134
WALL_STEP = 0.4


def lattice_along(a, b):
    """The wall positions' coordinates along a face from a to b (the level's own: multiples of the step), a hold's width inside its ends."""
    k0 = math.ceil((a + 0.3) / WALL_STEP - 1e-6)
    k1 = math.floor((b - 0.3) / WALL_STEP + 1e-6)
    return [round(k * WALL_STEP, 4) for k in range(k0, k1 + 1)]


def wall_stones(c, plane, out, span, top, positions, jitter=False, along_x=False):
    """
    The holds on a face (its plane at x = plane looking along -X or +X by out; along_x: at y = plane looking along +Y, its positions along x) drawn at
    every wall position of `positions` and row up to the top, as stones standing out of the face (a hand's 0.1 m wide and 0.08 deep, a foot's 0.2 and 0.1),
    in a mesh that stops nothing; their tops' middles (the hold's point) returned, nudged a little off the lattice when `jitter` (the level's own holds).
    """
    points = []
    seen = set()
    mat = c.material("hold_stone", HOLD_STONE)
    for k, p in enumerate(positions):
        j = 0
        while True:
            z0 = WALL_ROW0 + WALL_STEP * j
            if z0 + 1.577 > top - 0.15:
                break
            for n, (limb, left, up) in enumerate(WALL_HOLDS):
                # Along the face: the climber's left. A face looking -X (its climber facing +X): left is +y; looking +Y (facing -Y): left is +x.
                a = p + left
                z = z0 + up
                if jitter:
                    a += ((k * 7 + j * 3 + n * 5) % 5 - 2) * 0.012
                    z += ((k * 3 + j + n) % 3 - 1) * 0.01
                lo, hi = span
                if a < lo + 0.06 or a > hi - 0.06 or z > top - 0.08:
                    continue
                key = (round(a, 2), round(z, 2))
                if key in seen:
                    continue
                seen.add(key)
                wide, deep = (0.1, 0.08) if limb == "hand" else (0.2, 0.1)
                if along_x:
                    c.box(a - wide / 2, a + wide / 2, plane, plane + out * deep if out > 0 else plane, z - 0.05, z, "wall", mesh="holds", top_mat=mat, side_mat=mat, bottom=True)
                    points.append([round(a, 4), round(plane, 4), round(z, 4)])
                else:
                    c.box(plane + out * deep, plane, a - wide / 2, a + wide / 2, z - 0.05, z, "wall", mesh="holds", top_mat=mat, side_mat=mat, bottom=True)
                    points.append([round(plane, 4), round(a, 4), round(z, 4)])
            j += 1
    return points


def holds_area(name, xf, y0, y1, top):
    """the layout's area of a wall with holds ("climb": "holds"): a box round its face (x = xf, looking -X) from y0 to y1, to its top."""
    return {"name": name + "-area", "kind": "zone", "exposure": 0, "climb": "holds",
            "box": {"origin": [round(xf - 0.5, 3), round(y0 - 0.1, 3), -0.1], "rotZ": 0, "size": [1.0, round(y1 - y0 + 0.2, 3), round(top + 0.4, 3)]}}


def build_doors(c, st, centres, xf):
    """One wall across the station with a doorway in each lane: jambs at full height, a lintel over each opening."""
    height = st.get("wall", 3.0)
    thick = 0.3
    y0 = centres[0][0] - centres[0][1] / 2
    y = y0
    for lane, (yc, pitch) in zip(st["lanes"], centres):
        w, h = lane["width"], lane["height"]
        exp = expect_of(st, lane)
        jamb = "neutral"
        c.box(xf, xf + thick, y, yc - w / 2, 0.0, height, jamb)
        c.box(xf, xf + thick, yc - w / 2, yc + w / 2, h, height, exp, bottom=True)
        y = yc + w / 2
    last_y, last_pitch = centres[-1]
    c.box(xf, xf + thick, y, last_y + last_pitch / 2, 0.0, height, "neutral")
    return xf + thick + 5.0

# ---------------------------------------------------------------------------------------------------- the floor


def area_rect(name):
    """A perception area's box as x from, x to, y from, y to (its rotZ is 0)."""
    box = next(a["box"] for a in PERCEPTION_AREAS if a["name"] == name)
    (x, y, _), (sx, sy, _) = box["origin"], box["size"]
    return (x, x + sx, y, y + sy)


def dark_corner():
    return area_rect("dark-corner")


def build_perception(c):
    """
    The bush drawn as clumps of leaves in its area (stopping nothing) and the dark corner's darker floor (by
    build_floor), each with a sign facing the start in the course's style.
    """
    bx0, bx1, by0, by1 = area_rect("bush")
    w, d = bx1 - bx0, by1 - by0
    for fx, fy, r, h, shade in [(0.25, 0.25, 0.75, 1.1, 0), (0.62, 0.2, 0.7, 0.9, 1), (0.8, 0.52, 0.65, 1.2, 0), (0.2, 0.72, 0.7, 1.3, 1),
                                (0.55, 0.78, 0.8, 1.5, 0), (0.85, 0.85, 0.5, 0.8, 1), (0.45, 0.5, 0.8, 1.4, 1), (0.12, 0.45, 0.5, 0.9, 0)]:
        c.clump(bx0 + fx * w, by0 + fy * d, 0.0, r, h, shade)
    sx, sy = START_SQUARE[0] + (START_SQUARE[1] - START_SQUARE[0]) / 2, START_SQUARE[2] + (START_SQUARE[3] - START_SQUARE[2]) / 2
    x, y = bx1 + 1.0, (by0 + by1) / 2          # beside it, east, so it hides nothing of the bush from the start
    c.sign_facing(x, y, 0.0, "BUSH", "CONCEAL 0.2", math.atan2(sy - y, sx - x))
    dx0, dx1, dy0, dy1 = dark_corner()
    x, y = dx1 + 0.7, dy1 + 0.7
    c.sign_facing(x, y, 0.0, "DARK", "LIGHT 0.1", math.atan2(sy - y, sx - x))


def build_floor(c, yard):
    """The floor in squares of 2 m, cut at the start square's, the free yard's and the pits' edges; the pits left open."""
    x0, x1, y0, y1 = COURSE
    xs = {x0 + 2.0 * i for i in range(int((x1 - x0) / 2) + 1)}
    ys = {y0 + 2.0 * i for i in range(int((y1 - y0) / 2) + 1)}
    dark = dark_corner()
    for r in [START_SQUARE, yard, dark] + c.holes:
        xs.update((r[0], r[1]))
        ys.update((r[2], r[3]))
    xs = sorted(x for x in xs if x0 <= x <= x1)
    ys = sorted(y for y in ys if y0 <= y <= y1)
    floor = c.material("floor_ground", FLOOR)
    free = c.material("freeyard_ground", FREE_FLOOR)
    square = c.material("startsquare_ground", START_FLOOR)
    darker = c.material("darkcorner_ground", DARK_FLOOR)
    inside = lambda r, x, y: r[0] < x < r[1] and r[2] < y < r[3]
    for i in range(len(xs) - 1):
        for j in range(len(ys) - 1):
            cx, cy = (xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2
            if any(inside(h, cx, cy) for h in c.holes):
                continue
            mat = (free if inside(yard, cx, cy) else darker if inside(dark, cx, cy) else square if inside(START_SQUARE, cx, cy)
                   else floor)
            a, b, e, f = xs[i], xs[i + 1], ys[j], ys[j + 1]
            c.poly(mat, [(a, e, 0.0), (b, e, 0.0), (b, f, 0.0), (a, f, 0.0)])

# ---------------------------------------------------------------------------------------------------- pictures

FONT = {
    "0": ".###." "#...#" "#..##" "#.#.#" "##..#" "#...#" ".###.", "1": "..#...##....#....#....#....#...###.",
    "2": ".###.#...#....#...#...#...#...#####", "3": "#####" "...#." "..#.." "...#." "....#" "#...#" ".###.",
    "4": "...#...##..#.#.#..#.#####...#....#.", "5": "######....####.....#....##...#.###.",
    "6": "..##..#...#....####.#...##...#.###.", "7": "#####....#...#...#...#....#....#...",
    "8": ".###.#...##...#.###.#...##...#.###.", "9": ".###.#...##...#.####....#...#..##..",
    ".": "..........................##...##..", "-": "...............#####...............",
    ":": "....." ".##.." ".##.." "....." ".##.." ".##.." ".....", "/": "....#...#....#...#...#....#...#....",
    "°": ".##..#..#.#..#..##.................", " ": "...................................",
    "A": ".###.#...##...#######...##...##...#", "B": "####.#...##...#####.#...##...#####.",
    "C": ".###.#...##....#....#....#...#.###.", "D": "####.#...##...##...##...##...#####.",
    "E": "######....#....####.#....#....#####", "F": "######....#....####.#....#....#....",
    "G": ".###.#...##....#.####...##...#.####", "H": "#...##...##...#######...##...##...#",
    "I": ".###...#....#....#....#....#...###.", "J": "..###...#....#....#....##..#..##...",
    "K": "#...##..#.#.#..##...#.#..#..#.#...#", "L": "#....#....#....#....#....#....#####",
    "M": "#...###.###.#.##.#.##...##...##...#", "N": "#...##...###..##.#.##..###...##...#",
    "O": ".###.#...##...##...##...##...#.###.", "P": "####.#...##...#####.#....#....#....",
    "Q": ".###.#...##...##...##.#.##..#..##.#", "R": "####.#...##...#####.#.#..#..#.#...#",
    "S": ".#####....#.....###.....#....#####.", "T": "#####..#....#....#....#....#....#..",
    "U": "#...##...##...##...##...##...#.###.", "V": "#...##...##...##...##...#.#.#...#..",
    "W": "#...##...##...##.#.##.#.##.#..#.#..", "X": "#...##...#.#.#...#...#.#.#...##...#",
    "Y": "#...##...#.#.#...#....#....#....#..", "Z": "#####....#...#...#...#...#....#####",
    "+": "......#....#..#####..#....#........",
}
CELL_W, CELL_H, ATLAS_W = 256, 64, 2048


def sign_cell(index):
    """The atlas cell of a sign, as u0, v0, u1, v1 (a little inside the cell, so the mips do not bleed)."""
    per_row = ATLAS_W // CELL_W
    col, row = index % per_row, index // per_row
    h = atlas_height()
    return ((col * CELL_W + 1) / ATLAS_W, (row * CELL_H + 1) / h, ((col + 1) * CELL_W - 1) / ATLAS_W, ((row + 1) * CELL_H - 1) / h)


_ATLAS_ROWS = [16]


def atlas_height():
    return CELL_H * _ATLAS_ROWS[0]


def write_png(path, width, height, pixels):
    """RGBA8 rows, no filtering, zlib at level 9."""
    stride = width * 4
    raw = b"".join(b"\x00" + bytes(pixels[y * stride:(y + 1) * stride]) for y in range(height))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def grid_png(path, size=512):
    """5 m of floor: a line every metre, a stronger one every 5 m (the tile's edges), white between."""
    per_metre = size / 5.0

    def profile(i):
        p = (i + 0.5) / per_metre                 # metres from the tile's edge
        d1 = abs(p - round(p)) * per_metre        # pixels from the nearest metre
        d5 = min(p, 5.0 - p) * per_metre          # from the nearest 5 m
        v = 1.0
        v = min(v, 0.62 + 0.38 * min(1.0, max(0.0, d1 - 1.2)))
        v = min(v, 0.30 + 0.70 * min(1.0, max(0.0, d5 - 3.0)))
        return v
    line = [profile(i) for i in range(size)]
    pixels = bytearray(size * size * 4)
    for y in range(size):
        ry = line[y]
        for x in range(size):
            v = int(242 * min(ry, line[x]))
            o = (y * size + x) * 4
            pixels[o] = pixels[o + 1] = pixels[o + 2] = v
            pixels[o + 3] = 255
    write_png(path, size, size, pixels)


def signs_png(path, signs):
    height = atlas_height()
    if len(signs) + 1 > _ATLAS_ROWS[0] * (ATLAS_W // CELL_W):
        raise SystemExit("more signs than the atlas holds")
    pixels = bytearray(ATLAS_W * height * 4)
    board, edge, ink = (250, 246, 228), (60, 60, 60), (25, 25, 25)
    for y in range(height):
        for x in range(ATLAS_W):
            cx, cy = x % CELL_W, y % CELL_H
            colour = edge if cx < 3 or cx >= CELL_W - 3 or cy < 3 or cy >= CELL_H - 3 else board
            o = (y * ATLAS_W + x) * 4
            pixels[o:o + 4] = bytes((colour[0], colour[1], colour[2], 255))

    def text(s, cell, top, scale):
        s = s.upper()
        per_row = ATLAS_W // CELL_W
        x0 = (cell % per_row) * CELL_W
        y0 = (cell // per_row) * CELL_H + top
        width = len(s) * 6 * scale - scale
        if width > CELL_W - 8:
            scale = max(1, scale - 1)
            width = len(s) * 6 * scale - scale
        left = x0 + (CELL_W - width) // 2
        for k, ch in enumerate(s):
            glyph = FONT.get(ch, FONT[" "])
            for gy in range(7):
                for gx in range(5):
                    if glyph[gy * 5 + gx] != "#":
                        continue
                    for sy in range(scale):
                        for sx in range(scale):
                            px, py = left + (k * 6 + gx) * scale + sx, y0 + gy * scale + sy
                            o = (py * ATLAS_W + px) * 4
                            pixels[o:o + 3] = bytes(ink)
    for i, (one, two) in enumerate(signs):
        text(one, i + 1, 7, 5)
        text(two, i + 1, 46, 2)
    write_png(path, ATLAS_W, height, pixels)

# ---------------------------------------------------------------------------------------------------- glTF


def write_gltf(c, folder, name):
    """The meshes as glTF 2.0 (Y up: a point (x, y, z) goes over as (x, z, -y), a quarter turn that keeps winding)."""
    blob = bytearray()
    views, accessors, meshes, nodes = [], [], [], []
    mat_names = list(c.materials)
    textures = {"grid": 0, "signs": 1}

    def view(data, target):
        while len(blob) % 4:
            blob.append(0)
        views.append({"buffer": 0, "byteOffset": len(blob), "byteLength": len(data), "target": target})
        blob.extend(data)
        return len(views) - 1

    for mesh_name, prims in c.meshes.items():
        out = []
        for mat, (pos, nrm, uv, idx) in prims.items():
            gp = [(p[0], p[2], -p[1]) for p in pos]
            gn = [(n[0], n[2], -n[1]) for n in nrm]
            pv = view(b"".join(struct.pack("<3f", *p) for p in gp), 34962)
            mins = [min(p[k] for p in gp) for k in range(3)]
            maxs = [max(p[k] for p in gp) for k in range(3)]
            accessors.append({"bufferView": pv, "componentType": 5126, "count": len(gp), "type": "VEC3", "min": mins, "max": maxs})
            pa = len(accessors) - 1
            nv = view(b"".join(struct.pack("<3f", *n) for n in gn), 34962)
            accessors.append({"bufferView": nv, "componentType": 5126, "count": len(gn), "type": "VEC3"})
            na = len(accessors) - 1
            tv = view(b"".join(struct.pack("<2f", *t) for t in uv), 34962)
            accessors.append({"bufferView": tv, "componentType": 5126, "count": len(uv), "type": "VEC2"})
            ta = len(accessors) - 1
            iv = view(struct.pack("<%dI" % len(idx), *idx), 34963)
            accessors.append({"bufferView": iv, "componentType": 5125, "count": len(idx), "type": "SCALAR"})
            ia = len(accessors) - 1
            out.append({"attributes": {"POSITION": pa, "NORMAL": na, "TEXCOORD_0": ta}, "indices": ia,
                        "material": mat_names.index(mat)})
        meshes.append({"name": mesh_name, "primitives": out})
        nodes.append({"name": mesh_name, "mesh": len(meshes) - 1})
    materials = []
    for m in mat_names:
        colour, tex = c.materials[m]
        materials.append({"name": m, "pbrMetallicRoughness": {
            "baseColorFactor": [round(colour[0], 4), round(colour[1], 4), round(colour[2], 4), 1.0],
            "baseColorTexture": {"index": textures[tex]}, "metallicFactor": 0.0, "roughnessFactor": 0.9}})
    doc = {
        "asset": {"version": "2.0", "generator": "Vitomir proving ground (tools/platform/proving-ground/make_course.py)"},
        "scene": 0, "scenes": [{"name": name, "nodes": list(range(len(nodes)))}], "nodes": nodes, "meshes": meshes,
        "materials": materials,
        "images": [{"uri": "grid.png"}, {"uri": "signs.png"}],
        "samplers": [{"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497},
                     {"magFilter": 9729, "minFilter": 9987, "wrapS": 33071, "wrapT": 33071}],
        "textures": [{"source": 0, "sampler": 0}, {"source": 1, "sampler": 1}],
        "buffers": [{"uri": name + ".bin", "byteLength": len(blob)}],
        "bufferViews": views, "accessors": accessors,
        # The lanes, for the platform's tests (ProvingGroundTests reads them from the cooked package's assets/ copy).
        "extras": {"provingGround": {"human": HUMAN, "lanes": c.lanes}},
    }
    with open(os.path.join(folder, name + ".bin"), "wb") as f:
        f.write(blob)
    with open(os.path.join(folder, name + ".gltf"), "w", encoding="utf-8") as f:
        json.dump(doc, f, indent=1)
    return sum(len(p[3]) // 3 for prims in c.meshes.values() for p in prims.values())

# ---------------------------------------------------------------------------------------------------- the whole


def main(argv):
    if len(argv) != 2 or argv[1].startswith("-"):
        print(__doc__.strip().splitlines()[2].strip())
        return 2
    folder = argv[1]
    os.makedirs(folder, exist_ok=True)
    name = "proving-ground"
    # The signs' atlas is as tall as their count needs (a power of two), known before any sign is placed.
    count = 4 + sum(len(st["lanes"]) + 1 for st in STATIONS)   # the blank cell, START, BUSH and DARK, the stations
    rows = (count + ATLAS_W // CELL_W - 1) // (ATLAS_W // CELL_W)
    _ATLAS_ROWS[0] = 1
    while _ATLAS_ROWS[0] < rows:
        _ATLAS_ROWS[0] *= 2
    c = Course()
    cx, cy = (START_SQUARE[0] + START_SQUARE[1]) / 2, (START_SQUARE[2] + START_SQUARE[3]) / 2
    c.anchor("start", cx, cy, 0.0)
    c.sign(cx + 3.0, cy - 2.0, 0.0, "START", "VITOMIR PROVING GROUND", lift=1.4)
    build_perception(c)
    yard = (0.0, 0.0, 0.0, 0.0)
    for st in STATIONS:
        build_station(c, st)
        if st["kind"] == "yard":
            sx, sy = st["at"]
            yard = (sx, sx + st["size"][0], sy, sy + st["size"][1])
            c.plan[-1] = (st["name"], yard[0] - 6.0, yard[1], yard[2], yard[3])
    # Nothing may overlap: each station's ground with another's, or the start square.
    boxes = c.plan + [("start", START_SQUARE[0], START_SQUARE[1], START_SQUARE[2], START_SQUARE[3])]
    for i, a in enumerate(boxes):
        for b in boxes[i + 1:]:
            if a[1] < b[2] and b[1] < a[2] and a[3] < b[4] and b[3] < a[4]:
                raise SystemExit("stations %s and %s overlap" % (a[0], b[0]))
        if a[1] < COURSE[0] or a[2] > COURSE[1] or a[3] < COURSE[2] or a[4] > COURSE[3]:
            raise SystemExit("station %s is not on the floor (COURSE)" % a[0])
    build_floor(c, yard)
    signs_png(os.path.join(folder, "signs.png"), c.signs)
    grid_png(os.path.join(folder, "grid.png"))
    triangles = write_gltf(c, folder, name)
    x0, x1, y0, y1 = COURSE
    areas = [{"name": "course", "kind": "zone", "exposure": 0, "masquerade": "public",
              "box": {"origin": [x0 - 10, y0 - 10, -20.0], "rotZ": 0, "size": [x1 - x0 + 20, y1 - y0 + 20, 60.0]}}]
    if yard[1] > yard[0]:
        areas.append({"name": "free-yard", "kind": "zone", "exposure": 0, "masquerade": "free",
                      "box": {"origin": [yard[0], yard[2], -5.0], "rotZ": 0, "size": [yard[1] - yard[0], yard[3] - yard[2], 30.0]}})
    areas.extend(c.no_climb)
    areas.extend(c.no_stand)
    areas.extend(c.holds)   # the walls with holds
    areas.extend(PERCEPTION_AREAS)
    layout = {"anchors": c.anchors, "areas": areas, "decoration": {"passableMeshes": ["signs", "holds"]},
              "traversal": {"noClimbMeshes": ["noclimb", "ladder"], "noStandMeshes": ["nostand"], "ladders": c.ladders},
              "note": "the proving ground (tools/platform/proving-ground/make_course.py)"}
    with open(os.path.join(folder, "layout.json"), "w", encoding="utf-8") as f:
        json.dump(layout, f, indent=1)
    print("proving ground: %d triangles, %d materials, %d signs, %d starts, %d lanes" %
          (triangles, len(c.materials), len(c.signs), len(c.anchors), len(c.lanes)))
    print("the plan (metres; x east, y north; each station's lanes run east from x0):")
    for st, a, b, ya, yb in c.plan:
        print("  %-10s x %7.1f to %7.1f   y %6.1f to %6.1f" % (st, a, b, ya, yb))
    print("  %-10s x %7.1f to %7.1f   y %6.1f to %6.1f" % ("start", *START_SQUARE))
    print("written: %s" % os.path.abspath(os.path.join(folder, name + ".gltf")))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
