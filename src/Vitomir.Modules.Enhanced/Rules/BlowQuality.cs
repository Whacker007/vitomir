// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Modules.Enhanced.Configuration;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>
/// One blow's reference for its quality (<c>rules/quality</c>'s <c>references</c>, ENGINE_SANDBOX): the clip a swing
/// played (<see cref="Clip"/>, the motion's name as 1.1 ships it), optionally only for one weapon class, one character kind or one
/// weapon template (1.1's, as the staged harm reads it: the same clip meets differently with a broadsword and a rapier), and that
/// blow's own median <c>q</c> over the arena's contacts (<see cref="QRef"/>), with how many contacts and which run gave it.
/// </summary>
public sealed record QualityReference(string Clip, string? Class, string? Character, double QRef, int Contacts, string Run, string Status, string Source,
    string? Weapon = null);

/// <summary>
/// Blow quality's numbers (<c>enhanced:rules/quality</c>, schema <see cref="SchemaId"/>, every number ENGINE_SANDBOX
/// until the rules package replaces it): the form (<c>g(Q) = min(cap, Q^p)</c>), whether Potence's automatic successes scale,
/// the hilt (a contact this near the guard is bashing), the cut share re-based on Q, and each blow's reference. Read strictly: a
/// missing or wrong field is refused by name, with every reason.
/// </summary>
public sealed class BlowQualityData
{
    public const string SchemaId = "VITOMIR_ENHANCED_BLOW_QUALITY";
    public const string Asset = "rules/quality";
    /// <summary>The one measure this module knows: the contact's speed along the struck surface's normal over its clip's peak tip speed at the swing's rate.</summary>
    public const string Measure = "impact_speed_over_peak_speed";

    public string File { get; private init; } = "(defaults)";
    /// <summary>The exponent p of <c>Q^p</c> (1 momentum-like, 2 energy-like) and the most the factor may be.</summary>
    public double Exponent { get; private init; } = 1;
    public double Cap { get; private init; } = 1.25;
    /// <summary>Whether Potence's automatic successes scale with the factor (false: unscaled, the engine's recommendation; the rules package decides).</summary>
    public bool PotenceScaled { get; private init; }
    /// <summary>The hilt: a contact of one of <see cref="HiltClasses"/> nearer the guard than <see cref="HiltBelow"/> (0 the guard, 1 the tip) is bashing harm.</summary>
    public float HiltBelow { get; private init; } = 0.1f;
    public IReadOnlyList<string> HiltClasses { get; private init; } = [];
    /// <summary>The cut share re-based on Q: at or above <see cref="CutNormalFrom"/> severance's normal share, at or above <see cref="CutAcuteFrom"/> its acute share, below it its glancing share (0: never cuts); Melee's edge as for the bands.</summary>
    public double CutNormalFrom { get; private init; } = 0.6;
    public double CutAcuteFrom { get; private init; } = 0.1;
    public IReadOnlyList<QualityReference> References { get; private init; } = [];
    public string Source { get; private init; } = "";

    /// <summary>The numbers' defaults (a world without the file: a test).</summary>
    public static BlowQualityData Defaults { get; } = new();

    /// <summary>The same numbers with other references (a test's).</summary>
    public BlowQualityData With(IReadOnlyList<QualityReference> references) => new()
    {
        File = File, Exponent = Exponent, Cap = Cap, PotenceScaled = PotenceScaled, HiltBelow = HiltBelow, HiltClasses = HiltClasses,
        CutNormalFrom = CutNormalFrom, CutAcuteFrom = CutAcuteFrom, References = references, Source = Source,
    };

    /// <summary>
    /// The reference of a blow that played <paramref name="clip"/> with weapon template <paramref name="weapon"/> of class
    /// <paramref name="classId"/> by a body of kind <paramref name="character"/>: the most particular one that matches (a weapon named
    /// counts most, then a class, then a character; the clip alone least), or null — no reference: Q is then 1 (the trace says so).
    /// Allocates nothing.
    /// </summary>
    public QualityReference? Find(string? clip, string? classId, string? character, string? weapon = null)
    {
        if (clip is null) return null;
        QualityReference? best = null;
        int bestScore = -1;
        for (int i = 0; i < References.Count; i++)
        {
            var r = References[i];
            if (!string.Equals(r.Clip, clip, StringComparison.Ordinal)) continue;
            if (r.Class is not null && !string.Equals(r.Class, classId, StringComparison.Ordinal)) continue;
            if (r.Character is not null && !string.Equals(r.Character, character, StringComparison.Ordinal)) continue;
            if (r.Weapon is not null && !string.Equals(r.Weapon, weapon, StringComparison.OrdinalIgnoreCase)) continue;
            int score = (r.Weapon is null ? 0 : 4) + (r.Class is null ? 0 : 2) + (r.Character is null ? 0 : 1);
            if (score > bestScore) { best = r; bestScore = score; }
        }
        return best;
    }

    /// <summary>Whether the hilt rule applies to weapon class <paramref name="classId"/> (a blade's or a haft's: not the hands, not the blunt).</summary>
    public bool HasHilt(string classId)
    {
        for (int i = 0; i < HiltClasses.Count; i++) if (string.Equals(HiltClasses[i], classId, StringComparison.Ordinal)) return true;
        return false;
    }

    internal static BlowQualityData? Read(JsonElement root, string file, List<string> problems, HarmData? harm)
    {
        int before = problems.Count;
        var c = new JsonChecks(file, problems);
        c.Schema(root, SchemaId);
        string status = c.Str(root, "status") ?? "";
        if (status != "ENGINE_SANDBOX") c.Fail($"status is '{status}': blow quality's numbers are ENGINE_SANDBOX (ours) until the rules package gives them");
        if (c.Str(root, "measure") is { } m && m != Measure) c.Fail($"measure '{m}' is not one this module knows ({Measure}: the contact's ImpactSpeed over its PeakSpeed)");
        double N(JsonElement o, string key, double least, double most)
        {
            if (!o.TryGetProperty(key, out var v)) { c.Fail($"{key} is missing"); return least; }
            double x = c.Num(v, key) ?? least;
            if (!(x >= least && x <= most)) c.Fail($"{key} {x} is outside {least} to {most}");
            return x;
        }
        double p = 1, cap = 1.25, below = 0.1, normalFrom = 0.6, acuteFrom = 0.1;
        bool potence = false;
        var hiltClasses = new List<string>();
        var refs = new List<QualityReference>();
        if (c.Obj(root, "form") is { } f)
        {
            p = N(f, "p", 0.25, 4);
            cap = N(f, "cap", 1, 10);
            if (c.Bool(f, "potence_scaled") is { } ps) potence = ps;
        }
        if (c.Obj(root, "hilt") is { } h)
        {
            below = N(h, "along_blade_below", 0, 1);
            if (c.Str(h, "kind") is { } k && k != "bashing") c.Fail($"hilt.kind '{k}' must be 'bashing' (a meeting at the hilt is a bash, never a cut)");
            if (c.Arr(h, "classes") is { } hc)
                foreach (var x in hc.EnumerateArray())
                {
                    string? id = x.ValueKind == JsonValueKind.String ? x.GetString() : null;
                    if (id is null) { c.Fail("hilt.classes must hold class ids"); continue; }
                    if (harm is not null && !harm.Classes.ContainsKey(id)) c.Fail($"hilt.classes names class '{id}', which {harm.File} does not have");
                    else if (harm is not null && harm.Classes[id].Kind == HarmKind.Bashing) c.Fail($"hilt.classes names class '{id}', whose harm is bashing already");
                    else if (harm is not null && id == harm.BareHands) c.Fail($"hilt.classes names the bare hands' class '{id}': a hand has no hilt");
                    else hiltClasses.Add(id);
                }
        }
        if (c.Obj(root, "cut_share") is { } cs)
        {
            normalFrom = N(cs, "normal_from_quality", 0, 10);
            acuteFrom = N(cs, "acute_from_quality", 0, 10);
            if (!(acuteFrom < normalFrom)) c.Fail($"cut_share: acute_from_quality ({acuteFrom}) must be below normal_from_quality ({normalFrom})");
        }
        if (c.Arr(root, "references") is { } rl)
            foreach (var x in rl.EnumerateArray())
            {
                string? clip = c.Str(x, "clip");
                string? cls = c.Str(x, "class", required: false);
                string? character = c.Str(x, "character", required: false);
                string? weapon = c.Str(x, "weapon", required: false);
                if (weapon is not null && harm is not null && harm.ClassOf(weapon) is null) c.Fail($"references: weapon '{weapon}' (clip {clip}) has no class in {harm.File}");
                if (cls is not null && harm is not null && !harm.Classes.ContainsKey(cls)) c.Fail($"references: class '{cls}' (clip {clip}) is not one of {harm.File}'s");
                double q = x.TryGetProperty("q_ref", out var qv) ? c.Num(qv, "references.q_ref") ?? 0 : 0;
                if (!(q > 0 && q <= 1)) c.Fail($"references: clip {clip}'s q_ref {q} must be above 0, at most 1");
                int n = c.Int(x, "contacts") ?? 0;
                string run = c.Str(x, "run") ?? "", st = c.Str(x, "status") ?? "", src = c.Str(x, "source") ?? "";
                if (st != "ENGINE_SANDBOX") c.Fail($"references: clip {clip}'s status '{st}' must be ENGINE_SANDBOX (measured here; the rules package replaces it)");
                if (clip is null) continue;
                foreach (var o in refs)
                    if (o.Clip == clip && o.Class == cls && o.Character == character && o.Weapon == weapon)
                        c.Fail($"references: clip {clip} (class {cls ?? "any"}, character {character ?? "any"}, weapon {weapon ?? "any"}) is given twice");
                refs.Add(new QualityReference(clip, cls, character, q, n, run, st, src, weapon));
            }
        var data = new BlowQualityData
        {
            File = file, Exponent = p, Cap = cap, PotenceScaled = potence, HiltBelow = (float)below, HiltClasses = hiltClasses,
            CutNormalFrom = normalFrom, CutAcuteFrom = acuteFrom, References = refs, Source = c.Str(root, "source", required: false) ?? "",
        };
        return problems.Count == before ? data : null;
    }
}

/// <summary>
/// Blow quality (the rules package's gate-08; the engine's call) — <b>a
/// contact delivers harm by the work it can do</b>, as one stage of the staged harm, after the contact is admitted and before the
/// pool is made whole, with <b>no roll</b>: a contact is never missed.
/// <list type="bullet">
/// <item><b>The measure</b> <c>q = ImpactSpeed / PeakSpeed</c>: the contact point's speed along the struck surface's normal over
/// the swing's clip's own peak tip speed at the rate it played (both scale with the swing's tempo, so Celerity's and Melee's tempo
/// cancel; it carries how square the blade met, the speed left in it and the place on the blade). <c>Q = q / q_ref</c>, the
/// blow's own reference (<see cref="BlowQualityData.Find"/>, by the clip the swing played): a typical contact of every blow is
/// Q = 1. No peak speed: unmeasured, Q = 1; no reference for the blow: Q = 1, said so.</item>
/// <item><b>The factor</b> <c>min(cap, Q^p)</c> multiplies the damage potential (Strength's dice and the weapon's addend, from the
/// swing's budget left); Potence's automatic successes are not scaled (<see cref="BlowQualityData.PotenceScaled"/>).</item>
/// <item><b>The hilt</b>: a contact of a blade's or a haft's class nearer the guard than <see cref="BlowQualityData.HiltBelow"/>
/// is <b>bashing</b> harm (soaked as bashing, halved on a vampire, never a cut).</item>
/// <item><b>The cut share re-based</b> (<see cref="CutBand"/>): Q's band in place of the angle's (the sine is inside q: the bands
/// on top would count the angle twice).</item>
/// </list>
/// With <see cref="On"/> false (the switch <c>blow-quality-off</c>) Q is still measured and recorded but the factor is 1, the
/// kind the class's and the cut share the angle's: the earlier build's harm exactly.
/// </summary>
public sealed class BlowQuality(BlowQualityData data, bool on, bool hiltOn = true)
{
    public BlowQualityData Data { get; } = data;
    /// <summary>Whether the factor, the hilt and the re-based cut share apply (off: <c>blow-quality-off</c>).</summary>
    public bool On { get; } = on;
    /// <summary>Whether the hilt rule applies (off: <c>blow-quality-hilt-off</c>; only with <see cref="On"/>).</summary>
    public bool HiltOn { get; } = hiltOn;

    /// <summary>
    /// The clip the swing <paramref name="swing"/> of <paramref name="attacker"/> plays (its action channel's clip, while its
    /// <see cref="Combatant.Swing"/> is still that swing): the name its reference is found by; null when unknown (no character, no
    /// combatant, another swing begun). Allocates nothing.
    /// </summary>
    /// <summary>
    /// The clip a contact's blow plays — for a fixed contact (<see cref="ContactMade.Fixed"/>) the clip of the blow
    /// it stands for (<see cref="ContactMade.BlowClip"/>), the body's kind the attacker's; else <see cref="ClipOf(World, EntityId, int, out string?)"/>.
    /// </summary>
    public static string? ClipOf(World world, in ContactMade c, out string? character)
    {
        if (!c.Fixed) return ClipOf(world, c.Attacker, c.Swing, out character);
        character = null;
        if (world.TryService<CharacterAssets>(out var assets) && world.TryStore<CharacterAnimation>(out var anims) && anims.Has(c.Attacker))
        {
            ref var anim = ref anims.Get(c.Attacker);
            if ((uint)anim.Kind < (uint)assets.KindCount) character = assets.Kind(anim.Kind).Name;
        }
        return c.BlowClip;
    }

    public static string? ClipOf(World world, EntityId attacker, int swing, out string? character)
    {
        character = null;
        if (!world.TryService<CharacterAssets>(out var assets) || !world.TryStore<CharacterAnimation>(out var anims) || !anims.Has(attacker)) return null;
        ref var anim = ref anims.Get(attacker);
        if ((uint)anim.Kind < (uint)assets.KindCount) character = assets.Kind(anim.Kind).Name;
        if (world.TryStore<Combatant>(out var fighters) && fighters.Has(attacker) && fighters.Get(attacker).Swing != swing) return null;
        if ((uint)anim.ActionClip >= (uint)assets.ClipCount) return null;
        return assets.Clip(anim.ActionClip).Name;
    }

    /// <summary>
    /// The stage's judgement of <paramref name="contact"/> (a blow of class <paramref name="cls"/>, its swing's clip
    /// <paramref name="clip"/>, by a body of kind <paramref name="character"/>) into <paramref name="r"/>: q, the reference, Q, the
    /// factor (1 when off), the hilt (the kind then bashing), the cut share's band. Draws nothing; allocates nothing.
    /// </summary>
    public void Judge(in ContactMade contact, WeaponClass cls, string? clip, string? character, HarmResult r, string? weapon = null)
    {
        r.QualityJudged = true;
        r.QualityOn = On;
        r.QualityClip = clip;
        r.QualityCharacter = character;
        r.QualityRule = Data;
        r.QualityMeasured = contact.PeakSpeed > 0f;
        if (!r.QualityMeasured)
        {
            r.Quality = 1f; r.QualityFactor = 1f; r.QualitySpeedRatio = 0f; r.QualityRef = 0f;
            return;
        }
        r.QualitySpeedRatio = MathF.Max(0f, contact.ImpactSpeed) / contact.PeakSpeed;
        r.QualityReference = Data.Find(clip, cls.Id, character, weapon);
        r.QualityRef = r.QualityReference is { } q ? (float)q.QRef : 0f;
        r.Quality = r.QualityReference is null ? 1f : r.QualitySpeedRatio / r.QualityRef;
        double g = Data.Exponent == 1 ? r.Quality : Math.Pow(r.Quality, Data.Exponent);
        r.QualityFactorRaw = (float)Math.Min(Data.Cap, g);
        r.QualityFactor = On ? r.QualityFactorRaw : 1f;
        r.QualityCutBand = r.Quality >= Data.CutNormalFrom ? AngleBand.Normal : r.Quality >= Data.CutAcuteFrom ? AngleBand.Acute : AngleBand.Glancing;
        r.CutShareFromQuality = On && r.QualityReference is not null;   // a blow with no reference keeps the angle's band
        r.HiltOn = On && HiltOn;
        r.HiltMeasured = Data.HasHilt(cls.Id) && contact.AlongBlade < Data.HiltBelow;
        if (r.HiltOn && r.HiltMeasured)
        {
            r.Hilt = true;
            r.Kind = HarmKind.Bashing;
        }
    }

    /// <summary>
    /// <see cref="ResistedBy.LowWork"/> where it belongs: a blow resolved to no wound whose pool at Q = 1 — the same dice, the same
    /// soak — would have wounded (<see cref="HarmResult.QualityWouldWound"/>). Called after the stages and again after the armour's
    /// own reason, so low work, the first stage at fault, wins.
    /// </summary>
    public static void LowWorkResisted(HarmResult r)
    {
        if (r.Outcome == HarmOutcome.Resisted && r.Wounds == 0 && r.QualityWouldWound) r.ResistedBy = ResistedBy.LowWork;
    }

    /// <summary>Where the cut share came from (the trace's and the arena's word): <c>quality</c>, <c>band</c>, or <c>off</c>.</summary>
    public static string CutShareFrom(HarmResult r) => !r.AngleOn ? "off" : r.CutShareFromQuality ? "quality" : "band";
}
