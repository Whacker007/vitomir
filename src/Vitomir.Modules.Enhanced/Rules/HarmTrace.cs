// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using System.Text.Json;
using Vitomir.Modules.Enhanced.Configuration;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>
/// One contact's resolution as a JSON line (ours), shaped as the rules package's <c>ResolutionTrace</c> (the combat rules'
/// interfaces: request id, module stamp, admission, events by stage, injuries as <c>DamageResolution</c>, the random
/// draws, the state hashes) with what the staged harm adds: the contact's evidence, every stage's inputs and outputs and the profile
/// values each read with their gates, and the Health before and after. Schema <see cref="Schema"/>, keys in a fixed order,
/// one line per contact read — and one more per extra blow of Celerity's flurry (<c>flurry</c> 1, 2, …, the request
/// <c>contact:&lt;id&gt;/flurry:&lt;k&gt;</c>), with the wounds (injury units) and the Health they cost apart, and the bands.
/// The stages <c>budget</c> (the swing's attack budget in and out) and, for the swing's own blow, <c>severance</c> (the
/// threshold, the verdict, the structural loss and the effective maximum), each with its rule id and status; and at the end the
/// chain, the budget left, whether the blade goes on and the part cut off. The world's state hashes are not computed (a whole-world digest per contact is too dear):
/// they are null, and the tick names the state.
/// </summary>
public static class HarmTrace
{
    public const string Schema = "VITOMIR_ENHANCED_RESOLUTION_TRACE";
    public const int FormatVersion = 1;

    /// <summary>The combat rules' <c>Outcome</c> name of an outcome.</summary>
    public static string OutcomeName(HarmOutcome o) => o.ToString();

    public static string Line(HarmRules rules, in ContactMade contact, HarmResult r)
    {
        var c = contact;
        var config = rules.Config;
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("schema", Schema);
            w.WriteNumber("format_version", FormatVersion);
            // A flurry's extra blows are their own resolutions of the same contact: contact:<id>/flurry:<k>.
            string request = r.Flurry == 0 ? $"contact:{c.ContactId}" : $"contact:{c.ContactId}/flurry:{r.Flurry}";
            w.WriteString("request_id", request);
            w.WriteNumber("contact_id", c.ContactId);
            w.WriteNumber("flurry", r.Flurry);
            w.WriteNumber("flurry_blows", r.FlurryBlows);
            w.WriteNumber("tick", c.Tick);
            w.WriteString("outcome", OutcomeName(r.Outcome));
            // Why a resolved contact dealt no wound (the "resisted" split): NoDamage, Soak or Halving.
            if (r.Outcome == HarmOutcome.Resisted) w.WriteString("resisted_by", r.ResistedBy.ToString());
            if (r.Refusal is not null) w.WriteString("reason", r.Refusal);

            var stamp = config.Stamp;
            w.WriteStartObject("module");
            w.WriteString("module_id", stamp.ModuleId);
            w.WriteString("profile_id", stamp.ProfileId);
            w.WriteString("formula_version", stamp.FormulaVersion);
            w.WriteBoolean("nonshipping_research", stamp.NonshippingResearch);
            w.WriteString("config_hash", stamp.ConfigHash);
            w.WriteString("harm_data", rules.Data.File);
            if (rules.Contest is { } k) w.WriteString("skill_data", k.Data.File);    // the contest's numbers
            w.WriteBoolean("contest", rules.Contest is not null);
            w.WriteEndObject();

            w.WriteStartObject("admission");
            w.WriteBoolean("accepted", r.Outcome is HarmOutcome.Applied or HarmOutcome.Resisted);
            w.WriteString("reason", r.Outcome switch
            {
                HarmOutcome.Applied or HarmOutcome.Resisted => "None",
                HarmOutcome.Denied when r.DeadTarget => "IllegalTarget",
                HarmOutcome.Denied => "ImmuneByNamedRule",
                HarmOutcome.Blocked => "Covered",
                HarmOutcome.Missed => "OutOfRange",
                _ => "InvalidData",
            });
            w.WriteString("applied_rule_id", "E3.staged_harm.sandbox");
            w.WriteEndObject();

            w.WriteStartObject("evidence");
            w.WriteString("attacker", c.Attacker.ToString());
            w.WriteNumber("swing", c.Swing);
            w.WriteString("target", c.Target.ToString());
            w.WriteString("met", c.Met.ToString());
            if (c.Site is null) w.WriteNull("site"); else w.WriteString("site", c.Site);
            w.WriteString("material", c.Material.ToString());
            w.WriteStartArray("point"); w.WriteNumberValue(c.Point.X); w.WriteNumberValue(c.Point.Y); w.WriteNumberValue(c.Point.Z); w.WriteEndArray();
            w.WriteStartArray("normal"); w.WriteNumberValue(c.Normal.X); w.WriteNumberValue(c.Normal.Y); w.WriteNumberValue(c.Normal.Z); w.WriteEndArray();
            w.WriteNumber("impact_speed", c.ImpactSpeed);
            w.WriteNumber("normal_impulse", c.NormalImpulse);
            w.WriteBoolean("pre_contact_blocked", c.PreContactBlocked);
            w.WriteBoolean("hit_cover_or_shield", c.HitCoverOrShield);
            w.WriteBoolean("line_of_fire", c.LineOfFire);
            w.WriteBoolean("protected", c.Protected);
            // The blow's quality as measured (gate-08, open: recorded, not used).
            w.WriteNumber("point_speed", c.PointSpeed);
            w.WriteNumber("along_blade", c.AlongBlade);
            w.WriteNumber("tip_speed", c.TipSpeed);
            w.WriteNumber("peak_speed", c.PeakSpeed);
            if (c.Fixed)   // a fixed contact, and the clip of the blow it stands for (written only for one)
            {
                w.WriteBoolean("fixed", true);
                if (c.BlowClip is null) w.WriteNull("blow_clip"); else w.WriteString("blow_clip", c.BlowClip);
            }
            w.WriteEndObject();

            bool staged = r.Class is not null && (r.Outcome is HarmOutcome.Applied or HarmOutcome.Resisted);
            w.WriteStartArray("events");
            if (staged)
            {
                Stage(w, request, "potential", "Contact", rules, [HarmRules.DamageDiceValue, r.Class!.BonusProfileValue], inputs =>
                {
                    inputs.WriteNumber("strength_effective", r.Strength);
                    if (r.Arrow)   // an arrow's work, in place of Strength
                    {
                        inputs.WriteNumber("arrow_shot", r.ArrowShot);
                        inputs.WriteNumber("arrow_mass_kg", r.ArrowMassKg);
                        inputs.WriteNumber("arrow_speed", r.ArrowSpeed);
                        inputs.WriteNumber("arrow_work_joules", r.ArrowWork);
                    }
                    inputs.WriteString("weapon", r.Weapon ?? "");
                    inputs.WriteString("weapon_class", r.Class.Id);
                    inputs.WriteString("weapon_class_status", r.Class.Status);
                    inputs.WriteNumber("impact_speed", c.ImpactSpeed);
                    inputs.WriteString("impact_speed_use", r.QualityJudged && r.QualityOn
                        ? "used by the quality stage below (ENGINE_SANDBOX: the rules package leaves the form open)"
                        : "recorded, not used: gate-08 (native contact quality -> damage potential) is open");
                }, outputs =>
                {
                    outputs.WriteString("kind", r.Hilt ? r.Class.Kind.ToString() : r.Kind.ToString());   // the class's kind (the hilt's below)
                    outputs.WriteNumber("strength_dice", r.StrengthDice);
                    if (r.Arrow) outputs.WriteNumber("arrow_work_dice", r.WorkDice);
                    outputs.WriteNumber("weapon_bonus", r.Bonus);
                    outputs.WriteNumber("potential", r.QualityJudged ? r.PotentialBeforeQuality : r.Potential);   // before the quality stage
                });
                // ---- blow quality, one stage between the potential and the pool (gate-08; no roll)
                if (r.QualityJudged && r.QualityMeasured)   // a contact with no peak speed (unmeasured: Q 1) has no stage, only the line's end fields
                    Stage(w, request, "quality", "Contact", rules, [], inputs =>
                    {
                        var d = r.QualityRule!;
                        inputs.WriteString("rule", "blow quality (gate-08)");
                        inputs.WriteString("status", "ENGINE_SANDBOX: the form, the cap, the hilt and the references ours (rules/quality); the rules package gives them");
                        inputs.WriteString("quality_data", d.File);
                        inputs.WriteBoolean("on", r.QualityOn);
                        inputs.WriteNumber("impact_speed", c.ImpactSpeed);
                        inputs.WriteNumber("peak_speed", c.PeakSpeed);
                        inputs.WriteNumber("along_blade", c.AlongBlade);
                        inputs.WriteString("measure", "q = impact_speed / peak_speed: the speed into the struck surface over the swing's clip's own peak tip speed at its rate (tempo cancels: Celerity's and Melee's)");
                        if (r.QualityClip is null) inputs.WriteNull("swing_clip"); else inputs.WriteString("swing_clip", r.QualityClip);
                        if (r.QualityCharacter is null) inputs.WriteNull("character"); else inputs.WriteString("character", r.QualityCharacter);
                        inputs.WriteString("weapon", r.Weapon ?? "");
                        if (r.QualityReference is { } q)
                        {
                            inputs.WriteNumber("q_ref", q.QRef);
                            inputs.WriteString("q_ref_for", $"{q.Clip}{(q.Weapon is null ? "" : $", weapon {q.Weapon}")}{(q.Class is null ? "" : $", class {q.Class}")}{(q.Character is null ? "" : $", character {q.Character}")}");
                            inputs.WriteString("q_ref_run", q.Run);
                        }
                        else inputs.WriteNull("q_ref");
                        inputs.WriteString("form", $"min({d.Cap}, Q^{d.Exponent}) times the damage potential (Strength's dice and the weapon's addend); Potence's automatic successes {(d.PotenceScaled ? "scaled" : "unscaled")}; no roll: the contact is never missed");
                        inputs.WriteNumber("potential_before", r.PotentialBeforeQuality);
                        inputs.WriteNumber("hilt_along_blade_below", d.HiltBelow);
                    }, outputs =>
                    {
                        outputs.WriteBoolean("measured", r.QualityMeasured);
                        outputs.WriteNumber("q", r.QualitySpeedRatio);
                        outputs.WriteNumber("quality", r.Quality);
                        outputs.WriteString("quality_use", !r.QualityMeasured ? "unmeasured (no peak speed): Q 1"
                            : r.QualityReference is null ? "no reference for this blow (rules/quality's references): Q 1" : "Q = q / q_ref");
                        outputs.WriteNumber("factor", r.QualityFactor);
                        outputs.WriteNumber("factor_if_on", r.QualityFactorRaw);
                        outputs.WriteNumber("potential", r.Potential);
                        outputs.WriteBoolean("hilt", r.Hilt);
                        outputs.WriteBoolean("hilt_met", r.HiltMeasured);
                        outputs.WriteString("kind", r.Kind.ToString());
                        if (r.QualityRolled)
                        {
                            outputs.WriteNumber("pool_at_q1", r.QualityPoolAtOne);
                            outputs.WriteNumber("successes_at_q1", r.QualitySuccessesAtOne);
                            outputs.WriteNumber("wounds_at_q1", r.QualityWoundsAtOne);
                            outputs.WriteNumber("dice_counted", r.QualityDiceCounted);
                            outputs.WriteString("dice_use", "one set of d10s, as many as the larger pool: the blow counts the first dice_counted; the Q = 1 pool (the label LowWork's only) all it needs");
                            outputs.WriteBoolean("would_wound_at_q1", r.QualityWouldWound);
                        }
                        outputs.WriteString("cut_band", r.QualityCutBand.ToString());
                        outputs.WriteBoolean("cut_share_from_quality", r.CutShareFromQuality);
                    });
                // ---- end
                if (r.Contested)
                    Stage(w, request, "contest", "Contact", rules, [rules.Contest!.Data.DifficultyProfileValue], inputs =>
                    {
                        var k = rules.Contest.Data;
                        inputs.WriteString("status", "SANDBOX: a research comparator, not a committed default");
                        inputs.WriteString("skill_data", k.File);
                        inputs.WriteString("dexterity_dice", k.DexterityDice.Text);
                        inputs.WriteNumber("difficulty", rules.Contest.Difficulty);
                        inputs.WriteNumber("attacker_dexterity_effective", r.AttackDexterity);
                        inputs.WriteString("attacker_ability", r.AttackAbilityId);
                        inputs.WriteNumber("attacker_ability_dots", r.AttackAbility);
                        inputs.WriteNumber("defender_dexterity_effective", r.DefenceDexterity);
                        inputs.WriteNumber("defender_dodge", r.DefenceDodge);
                        inputs.WriteString("defender_dodge_use", r.DefenceAbilityId == k.DodgeAbility   // the Dodge only in its burst
                            ? "counted: the defender's dodge was in its burst at the contact (its body moving out of the blade's path)"
                            : "not counted: Dodge counts only while the defender's dodge is in its burst, not its start or recovery");
                        inputs.WriteNumber("defender_melee", r.DefenceMelee);
                        inputs.WriteBoolean("defender_holds_weapon", r.DefenceArmed);
                        inputs.WriteBoolean("defender_guarding", r.DefenceGuarding);
                        inputs.WriteString("defender_ability", r.DefenceAbilityId);
                        inputs.WriteNumber("defender_ability_dots", r.DefenceAbility);
                        inputs.WriteNumber("damage_pool", r.PoolBeforeContest);
                        if (k.AddMost is { } most) inputs.WriteNumber("add_most", most); else inputs.WriteNull("add_most");
                        inputs.WriteNumber("take_most_share_of_pool", k.TakeMostShare);
                        inputs.WriteString("gate", "never: the contact is real and is resolved whatever the contest says (no hidden miss)");
                    }, outputs =>
                    {
                        outputs.WriteNumber("attack_dexterity_dice", r.AttackDexterityDice);
                        outputs.WriteNumber("attack_potential", r.AttackPotential);
                        outputs.WriteNumber("attack_pool", r.AttackPool);
                        outputs.WriteNumber("attack_successes", r.AttackSuccesses);
                        outputs.WriteNumber("defence_dexterity_dice", r.DefenceDexterityDice);
                        outputs.WriteNumber("defence_potential", r.DefencePotential);
                        outputs.WriteNumber("defence_pool", r.DefencePool);
                        outputs.WriteNumber("defence_successes", r.DefenceSuccesses);
                        outputs.WriteNumber("net", r.ContestNet);
                        outputs.WriteNumber("change", r.ContestChange);
                        outputs.WriteBoolean("limited", r.ContestLimited);
                        outputs.WriteNumber("damage_pool", r.DamagePool);
                    });
                Stage(w, request, "conversion", "DamageConversion", rules, [HarmRules.DamageDifficultyValue], inputs =>
                {
                    inputs.WriteNumber("potential", r.Potential);
                    inputs.WriteNumber("difficulty", rules.DamageDifficulty);
                    inputs.WriteString("dice_policy", rules.Data.DicePolicy);
                    inputs.WriteString("fractional_pool", rules.Data.FractionalPool);
                }, outputs =>
                {
                    outputs.WriteNumber("pool", r.DamagePool);
                    outputs.WriteNumber("damage_successes", r.DamageSuccesses);
                });
                Stage(w, request, "auto_successes", "DamageConversion", rules, [rules.Data.PotencePerRankProfileValue], inputs =>
                {
                    inputs.WriteString("power", rules.Data.PotenceOption);
                    inputs.WriteBoolean("learned", r.PotenceLearned);
                    inputs.WriteNumber("rank", r.PotenceRank);
                }, outputs => outputs.WriteNumber("auto_successes", r.AutoSuccesses));
                Stage(w, request, "soak", "Soak", rules, [HarmRules.SoakDiceValue, HarmRules.SoakDifficultyValue], inputs =>
                {
                    inputs.WriteString("kind", r.Kind.ToString());
                    inputs.WriteNumber("stamina_effective", r.Stamina);
                    inputs.WriteBoolean("stamina_soaks", r.StaminaSoaks);
                    inputs.WriteNumber("fortitude_rank", r.FortitudeRank);
                    inputs.WriteBoolean("fortitude_soaks", r.FortitudeSoaks);
                    inputs.WriteNumber("fortitude_dice_per_rank", rules.Data.FortitudeSoakDicePerRank);
                    inputs.WriteString("fortitude_status", rules.Data.FortitudeStatus);
                    inputs.WriteNumber("difficulty", rules.SoakDifficulty);
                }, outputs =>
                {
                    outputs.WriteNumber("stamina_dice", r.StaminaDice);
                    outputs.WriteNumber("fortitude_dice", r.FortitudeDice);
                    outputs.WriteNumber("soak_potential", r.SoakPotential);
                    outputs.WriteNumber("pool", r.SoakPool);
                    outputs.WriteNumber("soak_successes", r.SoakSuccesses);
                    if (r.ArmorJudged)   // the pool and the successes above include the armour's (the stage below)
                    {
                        outputs.WriteNumber("armor_pool", r.ArmorPool);
                        outputs.WriteNumber("armor_successes", r.ArmorSuccesses);
                    }
                });
                // ---- the armour over the struck part (gate-13 open: SANDBOX).
                if (r.ArmorJudged)
                    Stage(w, request, "armor", "Soak", rules, [], inputs =>
                    {
                        inputs.WriteString("rule", "armour over the struck part");
                        inputs.WriteString("status", "SANDBOX: 1.1's soak points by the bridge, the coverage ours (combat/armor); gate-13 (armor_matrix_profile) is open");
                        inputs.WriteString("region", BodyRegions.Name(r.ArmorRegion));
                        inputs.WriteString("kind", r.Kind.ToString());
                        inputs.WriteString("weapon_class", r.Class?.Id ?? "");
                        inputs.WriteString("worn", r.ArmorWorn);
                        if (r.Armor is null) inputs.WriteNull("over_part"); else inputs.WriteString("over_part", r.Armor);
                        inputs.WriteNumber("difficulty", rules.SoakDifficulty);
                    }, outputs =>
                    {
                        outputs.WriteString("verdict", r.ArmorVerdict.ToString());
                        outputs.WriteString("reason", ArmorRules.Reason(r));
                        outputs.WriteNumber("armor_dice", r.ArmorPotential);
                        outputs.WriteNumber("pool", r.ArmorPool);
                        outputs.WriteNumber("successes", r.ArmorSuccesses);
                        outputs.WriteNumber("wounds_kept", r.ArmorKept);
                    });
                // ---- end
                Stage(w, request, "wounds", "Soak", rules, [rules.Data.BashingHalvedProfileValue], inputs =>
                {
                    inputs.WriteNumber("damage_successes", r.DamageSuccesses);
                    inputs.WriteNumber("auto_successes", r.AutoSuccesses);
                    inputs.WriteNumber("soak_successes", r.SoakSuccesses);
                }, outputs =>
                {
                    outputs.WriteNumber("wounds_before_halving", r.WoundsBeforeHalving);
                    outputs.WriteBoolean("bashing_halved", r.Halved);
                    outputs.WriteNumber("wounds", r.Wounds);
                });
                Stage(w, request, "health", "HealthChange", rules, [rules.Data.HealthProfileValue], inputs =>
                {
                    inputs.WriteString("resource", rules.Data.HealthResource);
                    inputs.WriteString("health_status", rules.EngineSandboxHealth ? rules.Data.HealthStatus : "SHEET");
                    inputs.WriteNumber("health_before", r.HealthBefore);
                    inputs.WriteNumber("health_maximum", r.HealthMaximum);
                    // The wounds are the stages' injury units; the translator converts them to Health.
                    inputs.WriteNumber("wounds", r.Wounds);
                    inputs.WriteNumber("hp_per_wound", r.HealthPerWound);
                    inputs.WriteString("hp_per_wound_status", "ENGINE_SANDBOX: the deliberate wounds-to-HP translator; 7 x 15 >= 100 keeps the default person's seven-wound stop");
                }, outputs =>
                {
                    outputs.WriteNumber("health_lost", r.HealthLost);
                    outputs.WriteNumber("health_after", r.HealthAfter);
                    outputs.WriteString("band_before", r.BandBefore.ToString());
                    outputs.WriteString("band_after", r.BandAfter.ToString());
                    outputs.WriteString("band_use", "labels only: the package's proposals (below 25 % Injured, below 10 % Crippled of the purchased maximum), no penalty");
                    outputs.WriteBoolean("died", r.Died);
                });
                // The swing's attack budget in and out and, for the swing's own blow, severance:
                // the threshold, the verdict, the structural loss and the effective maximum.
                Stage(w, request, "budget", "AttackBudget", rules, [], inputs =>
                {
                    inputs.WriteString("rule", "the swing's attack budget");
                    inputs.WriteString("status", "ENGINE_SANDBOX: one non-replenishing budget a swing; a later body contact resolved from the share left (the residual share)");
                    inputs.WriteNumber("chain", r.Chain);
                    inputs.WriteNumber("budget_in", r.BudgetIn);
                    inputs.WriteNumber("full_potential", r.FullPotential);
                    inputs.WriteNumber("full_auto_successes", r.FullAutoSuccesses);
                }, outputs =>
                {
                    outputs.WriteNumber("potential", r.Potential);
                    outputs.WriteNumber("auto_successes", r.AutoSuccesses);
                    outputs.WriteBoolean("continues", r.Continues);
                    outputs.WriteNumber("budget_out", r.BudgetOut);
                });
                // ---- the blow's angle band and its cut share before severance ----
                if (r.SeveranceJudged && r.AngleRule is { } ar)
                    Stage(w, request, "angle", "Severance", rules, [], inputs =>
                    {
                        inputs.WriteString("rule", "the angle band and its cut share");
                        inputs.WriteString("status", "ENGINE_SANDBOX: the limits the package's (approximately normal 33-90, acute 10-33, glancing 0-10; TUNABLE/UNADOPTED), the bands' cut shares by design, Melee's edge ours (rules/severance)");
                        inputs.WriteNumber("impact_speed", c.ImpactSpeed);
                        inputs.WriteNumber("point_speed", c.PointSpeed);
                        inputs.WriteString("measure", "impact_speed / point_speed: the sine of the angle the striking point met the surface at (90 square on, 0 a skim)");
                        inputs.WriteNumber("normal_from_degrees", ar.NormalFromDegrees);
                        inputs.WriteNumber("acute_from_degrees", ar.AcuteFromDegrees);
                        inputs.WriteBoolean("angle_on", r.AngleOn);
                        inputs.WriteBoolean("melee_edge_on", r.EdgeOn);
                        inputs.WriteString("edge_ability", r.EdgeAbility);
                        if (r.EdgeRank >= 0) inputs.WriteNumber("edge_rank", r.EdgeRank); else inputs.WriteNull("edge_rank");
                        if (r.EdgeRule is { } er)
                        {
                            inputs.WriteString("edge_curve", $"{er.Base}^((rank - {er.ReferenceRank}) / {er.Ranks}), the share at most {er.Most}");
                            inputs.WriteString("edge_source", "the package's chapter 03 moderate Ability candidate 2.25^(rank/10), a sandbox reference ('research sensitivity in one eligible lane only')");
                        }
                    }, outputs =>
                    {
                        outputs.WriteNumber("sine", r.Sine);
                        outputs.WriteNumber("degrees", r.Angle == AngleBand.None ? 0 : DetMath.Asin(r.Sine) * DetMath.RadToDeg);
                        outputs.WriteString("band", r.Angle.ToString());
                        outputs.WriteNumber("band_cut_share", r.CutShareBase);
                        outputs.WriteNumber("edge_factor", r.EdgeFactor);
                        outputs.WriteNumber("cut_share", r.CutShare);
                        outputs.WriteNumber("cut_harm", r.CutHarm);
                        outputs.WriteString("cut_harm_use", "post-soak Health x the cut share, against the part's threshold; Health is charged the whole post-soak harm whatever the angle");
                        if (r.Body is null) outputs.WriteNull("body"); else outputs.WriteString("body", r.Body);
                    });
                // ---- end ----
                if (r.SeveranceJudged)
                    Stage(w, request, "severance", "Severance", rules, [], inputs =>
                    {
                        inputs.WriteString("rule", "severance: the post-soak harm against the part's threshold");
                        inputs.WriteString("status", "ENGINE_SANDBOX: thresholds by design: shares of the purchased maximum (rules/severance), structural shares the package's illustrative figures");
                        inputs.WriteString("region", BodyRegions.Name(r.Region));
                        inputs.WriteString("kind", r.Kind.ToString());
                        inputs.WriteString("weapon_class", r.Class!.Id);
                        inputs.WriteNumber("wounds", r.Wounds);
                        inputs.WriteNumber("hp_per_wound", r.HealthPerWound);
                        inputs.WriteNumber("post_soak_health", r.PostSoakHealth);
                        inputs.WriteString("post_soak_health_use", "before Health caps it");
                        if (r.SeverThreshold > 0) inputs.WriteNumber("threshold", r.SeverThreshold); else inputs.WriteNull("threshold");
                        if (r.SeverThreshold > 0) inputs.WriteNumber("threshold_share", r.SeverThresholdShare); else inputs.WriteNull("threshold_share");
                        inputs.WriteString("threshold_use", "the share of the purchased maximum, rounded up to whole Health (ENGINE_SANDBOX)");
                        inputs.WriteBoolean("eligible", r.SeverEligible);
                        inputs.WriteString("missing_before", SeveranceData.Text(r.MissingBefore));
                        inputs.WriteNumber("purchased_maximum", r.PurchasedMaximum);
                    }, outputs =>
                    {
                        outputs.WriteString("verdict", r.Verdict.ToString());
                        outputs.WriteString("reason", SeveranceRules.Reason(r));
                        outputs.WriteString("availability", r.Verdict switch   // the legs labelled, not merely null
                        {
                            SeverVerdict.Unavailable => "UNAVAILABLE",
                            SeverVerdict.NotSeverable or SeverVerdict.NoRegion or SeverVerdict.NoPartTable => "NOT_SEVERABLE",
                            _ => "AVAILABLE",
                        });
                        if (r.CounterfactualRegion != BodyRegion.None)   // the leg's counterfactual, trace only
                        {
                            outputs.WriteStartObject("counterfactual");
                            outputs.WriteString("status", "COUNTERFACTUAL, trace only: nothing is cut (leg severance UNAVAILABLE); ENGINE_SANDBOX analogue, rules/severance unavailable_counterfactual");
                            outputs.WriteString("read_as", BodyRegions.Name(r.CounterfactualRegion));
                            outputs.WriteNumber("threshold_share", r.CounterfactualShare);
                            outputs.WriteNumber("threshold", r.CounterfactualThreshold);
                            outputs.WriteNumber("cut_harm", r.CutHarm);
                            outputs.WriteBoolean("would_cut", r.CounterfactualWouldCut);
                            outputs.WriteEndObject();
                        }
                        outputs.WriteString("severed", BodyRegions.Name(r.Severed));
                        outputs.WriteBoolean("stops_body", r.StopsBody);
                        outputs.WriteString("stop_rule", r.StopsBody ? "SeveredStop: a harness stop, ENGINE_SANDBOX (what it is for each species is the rules package's)" : "");
                        outputs.WriteString("missing_after", SeveranceData.Text(r.MissingAfter));
                        outputs.WriteString("structural_rule", "the structural deficit: the union by limb of the missing parts' shares, charged once");
                        outputs.WriteNumber("structural_deficit_before", r.DeficitBefore);
                        outputs.WriteNumber("structural_deficit_after", r.DeficitAfter);
                        outputs.WriteNumber("structural_loss", r.StructuralLoss);
                        outputs.WriteNumber("effective_maximum", r.PurchasedMaximum - r.DeficitAfter);
                        outputs.WriteString("effective_maximum_use", "healing stops there; the purchased maximum, the bands' denominator, unchanged");
                        outputs.WriteNumber("health_charged", r.HealthLost);
                        outputs.WriteString("health_charged_use", "current Health charged once, by the blow: the structural loss is not a second charge");
                        outputs.WriteBoolean("continues", r.Continues);
                        outputs.WriteNumber("budget_out", r.BudgetOut);
                    });
            }
            w.WriteEndArray();

            w.WriteStartArray("injuries");
            if (staged)
            {
                w.WriteStartObject();
                w.WriteString("type", r.Kind.ToString());
                w.WriteNumber("damage_successes", r.DamageSuccesses);
                w.WriteNumber("automatic_successes", r.AutoSuccesses);
                w.WriteNumber("soak_successes", r.SoakSuccesses);
                w.WriteNumber("final_wounds", r.Wounds);
                w.WriteNumber("injury_units", r.Wounds);                  // stored apart from the Health they cost
                w.WriteNumber("health_per_injury_unit", r.HealthPerWound);
                if (c.Site is null) w.WriteNull("site_id"); else w.WriteString("site_id", c.Site);
                w.WriteNumber("actual_health_delta", r.HealthAfter - r.HealthBefore);
                w.WriteString("conversion_parameter_version", $"{rules.Data.DicePolicy} d{rules.DamageDifficulty}/s{rules.SoakDifficulty}");
                w.WriteString("random_trace_id", request);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("random_draws");
            foreach (var d in r.Draws)
            {
                w.WriteStartObject();
                w.WriteString("purpose", d.Purpose);
                w.WriteNumber("position", d.Position);
                if (d.IsD10) w.WriteNumber("d10", d.D10); else w.WriteNumber("unit", d.Unit);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteNumber("health_before", r.HealthBefore);
            w.WriteNumber("health_after", r.HealthAfter);
            // The chain, the budget left and the cut, for a reader that wants them without the stages.
            w.WriteNumber("chain", c.Chain);
            w.WriteNumber("budget_left", r.BudgetOut);
            w.WriteBoolean("continues", r.Continues);
            w.WriteString("severed", BodyRegions.Name(r.Severed));
            w.WriteNumber("structural_loss", r.StructuralLoss);
            w.WriteString("angle", r.Angle.ToString());   // the band and the cut share
            w.WriteNumber("cut_share", r.CutShare);
            if (r.Armor is null) w.WriteNull("armor"); else w.WriteString("armor", r.Armor);
            w.WriteNumber("armor_dice", r.ArmorPool);
            w.WriteNumber("armor_kept", r.ArmorKept);
            w.WriteNull("state_hash_before");
            w.WriteNull("state_hash_after");
            // ---- the flurry work: the logical strike apart from the physical contact; an extra blow's check and guard verdict.
            w.WriteString("strike", FlurryBlows.Text(r.Strike));
            if (r.Flurry > 0)
            {
                w.WriteString("flurry_check", FlurryBlows.Name(r.FlurryCheck));
                if (r.GuardVerdict is { } gv) w.WriteString("flurry_guard_verdict", gv.ToString());
            }
            // ---- blow quality at the line's end (when the stage judged the blow)
            if (r.QualityJudged)
            {
                w.WriteNumber("q", r.QualitySpeedRatio);
                w.WriteNumber("q_ref", r.QualityRef);
                w.WriteNumber("quality", r.Quality);
                w.WriteNumber("quality_factor", r.QualityFactor);
                w.WriteBoolean("hilt", r.Hilt);
                if (r.SeveranceJudged) w.WriteString("cut_share_from", BlowQuality.CutShareFrom(r));
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    static void Stage(Utf8JsonWriter w, string request, string step, string stage, HarmRules rules, string?[] values,
        Action<Utf8JsonWriter> inputs, Action<Utf8JsonWriter> outputs)
    {
        w.WriteStartObject();
        w.WriteString("event_id", $"{request}.{step}");
        w.WriteString("step", step);
        w.WriteString("stage", stage);
        w.WriteStartObject("inputs"); inputs(w); w.WriteEndObject();
        w.WriteStartObject("outputs"); outputs(w); w.WriteEndObject();
        w.WriteStartArray("values");
        foreach (var key in values)
        {
            if (key is null) continue;
            ConfigValue? v = null;
            try { v = rules.Config.Value(key); } catch (KeyNotFoundException) { }
            w.WriteStartObject();
            w.WriteString("key", key);
            if (v is null) w.WriteNull("value"); else if (v.Json is null) w.WriteNull("value"); else w.WriteString("value", v.Json);
            w.WriteString("gate", v?.GateId ?? "");
            w.WriteString("gate_status", v?.GateResearchStatus ?? "");
            w.WriteString("status", v?.Status.ToString() ?? "");
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }
}
