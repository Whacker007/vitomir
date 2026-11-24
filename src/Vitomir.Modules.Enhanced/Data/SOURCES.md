# The enhanced module's data files and where they come from

These are the rules package's own rules data for the enhanced Vampire module (no game content), copied from

**the rules package of 2026-10-18** (its background, decisions and engine handoff)

and the module reads them from its folder (`Modules/Enhanced/Data/`) by id, through the platform's registry
(`enhanced:vampire_trait_registry`, `enhanced:profiles/prototype_sandbox`, …: a file's id is its path here,
lower-cased, without its extension). Each file's SHA-256 is
that of the file in the package (the package's own `MANIFEST_SHA256.json` and `engine_h1/MANIFEST_SHA256.json`
agree), but two, whose SHA-256 is the copy's: the gate register names its gates by their number (`gate-01` to
`gate-36`, the package's own ids being pointers into its private documents) and says its reference, owner and stage
fields in words; and the research sandbox's profile id is `SANDBOX_EXPLICIT_OPT_IN`, without the package's release id. If a file here no longer matches, it has been changed locally: the module still reads it (the configuration
hash in every trace then differs), and its configuration report says the file does not match the package.

| File | Path inside the package | SHA-256 |
|---|---|---|
| `vampire_trait_registry.json` | `engine_h1/contracts/vampire_trait_registry.json` | `c36415e47c23281f8e017665c160d5b014114cc9749e443b2d27311d2e3ab791` |
| `profiles/enhanced_unapproved_template.json` | `engine_h1/configs/enhanced_unapproved_template.json` | `9a6e61d0f4bd941a528cc16afb9aafd2ed0b5a358934d00ab07a10a02126a42f` |
| `profiles/prototype_sandbox.json` | `engine_h1/configs/prototype_sandbox.json` | `02562ffa5a9b1dbb030e0c81b054249e72c50f0b1c24a0c899f843241a4de199` |
| `NUMERIC_GATE_REGISTER.json` | `engine_h1/governance/NUMERIC_GATE_REGISTER.json` (also `governance/NUMERIC_GATE_REGISTER_H1.json`, identical) | `86370eb3d2bda4d91dd991f29c8977cbc496f77546e1a42a944bcf39507181a0` |
| `power_verb_contracts.json` | `engine_h1/contracts/power_verb_contracts.json` | `7ec7a988cff2e10c257ccc9f38044f907842f969f327df5a9e6d22847886ae9b` |

What each is:

- **`vampire_trait_registry.json`** — the nine Attributes (purchased 0..1,000, temporary effective overcap
  allowed), the thirty Abilities (0..10) with their working aliases, the rank domain of learned powers (0..10,
  options learned separately), and the familiarity families (0..100 %).
- **`profiles/enhanced_unapproved_template.json`** — the shipping profile `enhanced-vampire-SHIPPING-UNAPPROVED`: disabled,
  not shipping, its critical numbers null until a human approves them. The module refuses to run it and names why.
- **`profiles/prototype_sandbox.json`** — the research sandbox `SANDBOX_EXPLICIT_OPT_IN`: research
  coefficients, opt-in only (the program's `--sandbox`), never production defaults.
- **`NUMERIC_GATE_REGISTER.json`** — the 36 open numbers (gates `gate-01` to `gate-36`) with their research
  status; none is adopted for the game yet.
- **`power_verb_contracts.json`** — 24 typed power contracts (a representative subset, not the catalogue).

One file here is **ours**, not the package's:

- **`profile_value_gates.json`** — which gate of the register governs which value of a profile. The package has
  no such mapping; this is Vitomir's reading, marked `VITOMIR_PROPOSED_UNCONFIRMED_BY_RULES_PACKAGE`,
  for the rules package to confirm or correct.

Ours too, not the package's (the staged harm), each marked SANDBOX:

- **`rules/harm.json`** — what the staged harm needs that the profile does not give: the dice policy's name and the
  purposes of its random streams, the weapon classes (kind, addend: the one-handed sword's from the profile, the others
  SANDBOX) and which 1.1 weapon template is which, who soaks what (by species), Fortitude's soak per rank and the
  engine-sandbox Health (both ENGINE_SANDBOX: the profile has no value for either).
- **`rules/severance.json`** (ENGINE_SANDBOX) — severance and the swing's attack budget: the edged weapon classes
  and the kinds that cut, the measure the thresholds compare (the blow's post-soak harm in Health), the
  continuation rule (the residual share), the structural deficit's rule (the union by limb of the purchased
  maximum's shares) and the sheet condition that keeps what is missing, and each body region's threshold, share,
  limb, the parts that go with it and whether its loss stops the body. The thresholds are the shares of the victim's
  purchased maximum Health (45 %, 60 %, 75 %, 90 %, 200 %), the structural shares the package's illustrative figures.
- **`combat/armor.json`** (SANDBOX) — armour: the pieces, the body regions each covers (ours), its soak in
  1.1's points by kind of harm (each piece's `ef_soak_normal`, `ef_soak_lethal`, `ef_soak_aggravated` at the level its 1.1
  template names, `misc/effects.ned`; the template named in each piece's `oneone` and `source`), the bridge to dice (20 points a
  die: Stamina's raw/20), the layering rule (sum; one piece a slot), the kinds armour soaks (bashing, lethal, and
  aggravated from claws and fangs only), the sheet condition that keeps what a person wears (`worn_armor`) and what
  1.1's guards and watch are dressed in by their template (a mail hauberk and a light helm: their `_Lawmen_DA` soaks 30/60/30, their models' cut).
- **`rules/quality.json`** (ENGINE_SANDBOX) — blow quality (the rules package's gate-08; the
  package is asked for the form): the measure (a contact's speed along the struck surface's normal over its clip's peak tip speed), the
  form (the damage potential times `min(cap, Q^p)`, p 1, cap 1.25, Potence unscaled), the hilt (a blade's contact nearer the
  guard than a tenth of the blade is bashing), severance's cut share re-based on Q, and each blow's reference `q_ref` (its own
  median q over the arena's contacts, with the run that measured it). Ours: no number here is the package's.
- **`sheets/christof.json`, `sheets/guard.json`, `sheets/watchman.json`** — the fighters' SANDBOX sheet templates, given
  to the player and to 1.1's `rudolfGuard` and `KnightOfStJohn_torch` by the character they are drawn as.
