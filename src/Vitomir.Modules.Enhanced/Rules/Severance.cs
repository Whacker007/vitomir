// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Vitomir.Modules.Enhanced.Configuration;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>
/// One part a blade can cut off (ours, <c>rules/severance</c>, ENGINE_SANDBOX): the post-soak harm that severs it, as a
/// share of the victim's purchased maximum Health (<see cref="ThresholdShare"/>; in Health <see cref="ThresholdOf"/>), its share of the purchased maximum Health the effective maximum loses while it is
/// missing (<see cref="Share"/>, null for none), the limb it belongs to (for the union: a limb's deficit is the most of its
/// missing parts' shares), the parts that go with it (<see cref="Takes"/>: a forearm takes its hand) and whether its loss
/// stops the body (the neck, the trunk).
/// </summary>
public sealed record SeverablePart(BodyRegion Region, double ThresholdShare, int ThresholdPermille, double? Share, string Limb, BodyRegion[] Takes, bool StopsBody, string Source)
{
    /// <summary>
    /// The threshold in Health for a victim whose purchased maximum is <paramref name="purchased"/>: the least whole Health at or
    /// above the share (exact: the share is held in thousandths), at least 1.
    /// </summary>
    public long ThresholdOf(long purchased) => Math.Max(1, (ThresholdPermille * Math.Max(0, purchased) + 999) / 1000);
}

/// <summary>
/// One kind of body's severable parts (<c>rules/severance</c> <c>bodies</c>, ENGINE_SANDBOX): the anatomy tables
/// of the platform it is for (<see cref="Anatomy"/>, by <see cref="AnatomyTable.Name"/>: a body is known by its skeleton), each
/// region's part (or none), and the limbs the structural deficit's union is taken by.: <i>"For things
/// like the szlactha though ... they have long necks and are supposed to be fairly weak, so a well placed hit there should
/// generally have a better chance at severing the head"</i> — so each body has its own table.
/// </summary>
public sealed class SeverableBody
{
    public string Id { get; init; } = "";
    /// <summary>The platform's anatomy tables this body's parts are for (<see cref="AnatomyTables"/>' names).</summary>
    public IReadOnlyList<string> Anatomy { get; init; } = [];
    public string Source { get; init; } = "";

    internal readonly SeverablePart?[] parts = new SeverablePart?[BodyRegions.Count];
    internal readonly int[] limbOf = new int[BodyRegions.Count];
    internal readonly double[] shareOf = new double[BodyRegions.Count];

    /// <summary>The limbs' names (the union's groups), in the order first named.</summary>
    public IReadOnlyList<string> Limbs { get; internal set; } = [];

    /// <summary>The part of <paramref name="region"/> a blade can cut off, or null (not severable).</summary>
    public SeverablePart? Part(BodyRegion region) => (uint)region < (uint)parts.Length ? parts[(int)region] : null;

    // ---- the unavailable parts ----
    internal readonly bool[] unavailable = new bool[BodyRegions.Count];

    /// <summary>
    /// (The rules package, INCOMPLETE_CAPABILITY: <i>"Foot and leg severance need associated movement
    /// restrictions; until implemented, label leg severance UNAVAILABLE in sandbox, do not allow silent walking on an absent
    /// leg"</i>): whether <paramref name="region"/>'s severance is <b>unavailable</b> — the data's <c>"unavailable"</c> for the
    /// part, not null: it could be cut, but what a body does without it (the gait, crawling, the jump)
    /// is not built, so it is never cut and the trace says why.
    /// </summary>
    public bool Unavailable(BodyRegion region) => (uint)region < (uint)unavailable.Length && unavailable[(int)region];
    // ---- end

    /// <summary>
    /// The share of the purchased maximum that the anatomy missing in <paramref name="bits"/> takes off the effective maximum
    /// : by limb the most of its missing parts' shares (a proximal loss subsumes the distal), the limbs added.
    /// Allocates nothing.
    /// </summary>
    public double ShareMissing(uint bits)
    {
        if (bits == 0) return 0;
        Span<double> most = stackalloc double[Math.Max(1, Limbs.Count)];
        most.Clear();
        for (int r = 1; r < BodyRegions.Count; r++)
        {
            if ((bits & (1u << r)) == 0 || limbOf[r] < 0) continue;
            if (shareOf[r] > most[limbOf[r]]) most[limbOf[r]] = shareOf[r];
        }
        double sum = 0;
        for (int i = 0; i < Limbs.Count; i++) sum += most[i];
        return sum;
    }

    /// <summary>The structural deficit in Health: <see cref="ShareMissing"/> of <paramref name="purchased"/>, rounded to the nearest (halves up).</summary>
    public long Deficit(uint bits, long purchased) =>
        bits == 0 || purchased <= 0 ? 0 : Math.Min(purchased, (long)Math.Round(ShareMissing(bits) * purchased, MidpointRounding.AwayFromZero));
}

/// <summary>
/// The blow's angle (<c>rules/severance</c> <c>angle</c>, ENGINE_SANDBOX; the rules package's angle bands): the band's
/// limits in degrees (and their sines, worked once with <see cref="DetMath.Sin"/>) and the share of a blow's harm that counts toward
/// cutting in each band (<see cref="ShareOf"/>; <see cref="AngleBand.None"/>, a contact with no measured speed, is
/// <see cref="Unmeasured"/>).
/// </summary>
public sealed class SeveranceAngle
{
    public double NormalFromDegrees { get; init; }
    public double AcuteFromDegrees { get; init; }
    public float NormalSine { get; init; }
    public float AcuteSine { get; init; }
    public double Normal { get; init; } = 1;
    public double Acute { get; init; } = 1;
    public double Glancing { get; init; } = 1;
    public double Unmeasured { get; init; } = 1;
    public string Source { get; init; } = "";

    /// <summary>The band of a blow that met its surface at <paramref name="sine"/> (the impact speed over the whole speed): at or above the normal limit's sine normal, at or above the acute's acute, else glancing (a tie goes to the squarer band).</summary>
    public AngleBand BandOf(float sine) => sine >= NormalSine ? AngleBand.Normal : sine >= AcuteSine ? AngleBand.Acute : AngleBand.Glancing;

    /// <summary>The band's cut share, before Melee's edge.</summary>
    public double ShareOf(AngleBand band) => band switch
    {
        AngleBand.Normal => Normal,
        AngleBand.Acute => Acute,
        AngleBand.Glancing => Glancing,
        _ => Unmeasured,
    };
}

/// <summary>
/// Melee's edge (<c>rules/severance</c> <c>melee_edge</c>, ENGINE_SANDBOX): the attacker's Ability (Melee, or the
/// class's own: Brawl for the claws) multiplies the cut share of the bands it applies to by the Abilities' moderate curve
/// (<c>base^((rank − reference) / ranks)</c>: the rules package's chapter 03, <c>2.25^(rank/10)</c>, the "moderate candidate",
/// used as a sandbox reference), normalised to <see cref="ReferenceRank"/> (where the bands' shares are the data's), at most
/// <see cref="Most"/>. Glancing stays 0 (0 times anything).
/// </summary>
public sealed class SeveranceEdge
{
    public string Ability { get; init; } = "melee";
    /// <summary>The classes that read another Ability (class id, ability id), in the file's order.</summary>
    public IReadOnlyList<(string Class, string Ability)> ByClass { get; init; } = [];
    public double Base { get; init; } = 2.25;
    public double Ranks { get; init; } = 10;
    public int ReferenceRank { get; init; }
    public double Most { get; init; } = 1;
    /// <summary>Which bands it applies to (by <see cref="AngleBand"/>).</summary>
    public IReadOnlyList<bool> Bands { get; init; } = [false, false, false, false];
    public string Source { get; init; } = "";

    /// <summary>The Ability the edge reads for weapon class <paramref name="classId"/>. Allocates nothing.</summary>
    public string AbilityFor(string? classId)
    {
        if (classId is not null)
            for (int i = 0; i < ByClass.Count; i++) if (ByClass[i].Class == classId) return ByClass[i].Ability;
        return Ability;
    }

    /// <summary>The edge's factor at <paramref name="rank"/>: <c>base^((rank − reference) / ranks)</c> (<see cref="DetMath.Pow"/>).</summary>
    public double Factor(int rank) => DetMath.Pow((float)Base, (float)((rank - ReferenceRank) / Ranks));
}

/// <summary>
/// Severance's numbers (ours, <c>enhanced:rules/severance</c>, schema <see cref="SchemaId"/>, every number
/// ENGINE_SANDBOX): which weapon classes have an edge and which kinds of harm cut, the measure the thresholds compare
/// (the blow's post-soak harm in Health), the continuation rule (the residual share), the structural deficit's rule (the
/// union by limb of the purchased maximum's shares, rounded to the nearest) and the sheet condition that keeps what is
/// missing, and the parts. the parts by kind of body (<see cref="Bodies"/>, each the platform's anatomy tables
/// it is for; <see cref="DefaultBody"/> where the world draws no bodies), the blow's angle band and its cut share
/// (<see cref="Angle"/>), and Melee's edge (<see cref="Edge"/>). Read strictly: a missing or wrong field is refused by name,
/// with every reason.
/// </summary>
public sealed class SeveranceData
{
    public const string SchemaId = "VITOMIR_ENHANCED_SEVERANCE";
    public const string Asset = "rules/severance";

    /// <summary>The rules this module knows for each choice the file names.</summary>
    public const string PostSoakHealth = "post_soak_health", ResidualShare = "residual_share", PurchasedMaximum = "purchased_maximum",
        ByLimbMost = "by_limb_most", Nearest = "nearest", Up = "up", SineOfImpact = "impact_speed_over_point_speed", AbilityCurve = "base^((rank-reference)/ranks)";

    public string File { get; private init; } = "";
    public string Status { get; private init; } = "";
    /// <summary>The weapon classes with an edge (rules/harm's ids), in the file's order.</summary>
    public IReadOnlyList<string> EdgedClasses { get; private init; } = [];
    /// <summary>Which kinds of harm can cut (by <see cref="HarmKind"/>).</summary>
    public IReadOnlyList<bool> Kinds { get; private init; } = [false, false, false];
    /// <summary>The sheet condition that keeps what is missing (<c>missing_anatomy</c>), saved with the sheet.</summary>
    public string Condition { get; private init; } = "missing_anatomy";
    public string EdgedSource { get; private init; } = "";
    public string HarmSource { get; private init; } = "";
    public string ContinuationSource { get; private init; } = "";
    public string StructuralSource { get; private init; } = "";

    /// <summary>The bodies' part tables, in the file's order.</summary>
    public IReadOnlyList<SeverableBody> Bodies { get; private set; } = [];
    /// <summary>The body judged where the world draws no bodies (the rules alone, a test): <c>default_body</c>.</summary>
    public SeverableBody DefaultBody { get; private set; } = new();

    // ---- the rules package's (by design): an unavailable part's counterfactual, for the trace only ----
    internal readonly BodyRegion[] analogue = new BodyRegion[BodyRegions.Count];

    /// <summary>
    /// The part whose threshold an unavailable part's blow is read against <b>in the trace only</b> (the data's
    /// <c>unavailable_counterfactual</c>: a thigh as its side's upper arm, a shin as its forearm, a foot as its hand; ENGINE_SANDBOX,
    /// for the rules package to replace with leg thresholds), or <see cref="BodyRegion.None"/>. Nothing is cut by it.
    /// </summary>
    public BodyRegion CounterfactualOf(BodyRegion region) => (uint)region < (uint)analogue.Length ? analogue[(int)region] : BodyRegion.None;
    // ---- end
    /// <summary>The blow's angle band and its cut share.</summary>
    public SeveranceAngle Angle { get; private init; } = new();
    /// <summary>Melee's edge.</summary>
    public SeveranceEdge Edge { get; private init; } = new();

    /// <summary>The body whose part table is <paramref name="id"/>, or null.</summary>
    public SeverableBody? Body(string id)
    {
        for (int i = 0; i < Bodies.Count; i++) if (Bodies[i].Id == id) return Bodies[i];
        return null;
    }

    /// <summary>The body for the platform's anatomy table <paramref name="anatomy"/> (<see cref="AnatomyTable.Name"/>), or null: a body with no part table is never cut. Allocates nothing.</summary>
    public SeverableBody? ForAnatomy(string anatomy)
    {
        for (int i = 0; i < Bodies.Count; i++)
            for (int k = 0; k < Bodies[i].Anatomy.Count; k++)
                if (Bodies[i].Anatomy[k] == anatomy) return Bodies[i];
        return null;
    }

    /// <summary>The limbs' names of the default body (the union's groups).</summary>
    public IReadOnlyList<string> Limbs => DefaultBody.Limbs;

    /// <summary>The part of <paramref name="region"/> the default body (<see cref="DefaultBody"/>, the man's) can lose, or null.</summary>
    public SeverablePart? Part(BodyRegion region) => DefaultBody.Part(region);

    /// <summary>Whether weapon class <paramref name="classId"/> has an edge.</summary>
    public bool Edged(string classId)
    {
        for (int i = 0; i < EdgedClasses.Count; i++) if (EdgedClasses[i] == classId) return true;
        return false;
    }

    /// <summary>The default body's <see cref="SeverableBody.ShareMissing"/>.</summary>
    public double ShareMissing(uint bits) => DefaultBody.ShareMissing(bits);

    /// <summary>The default body's <see cref="SeverableBody.Deficit"/> (a body's own is <see cref="SeverableBody.Deficit"/>; the bodies' structural shares are the same today).</summary>
    public long Deficit(uint bits, long purchased) => DefaultBody.Deficit(bits, purchased);

    /// <summary>The anatomy missing after <paramref name="part"/> is cut off from a body missing <paramref name="bits"/>: the part and what goes with it.</summary>
    public static uint Lose(SeverablePart part, uint bits)
    {
        bits |= 1u << (int)part.Region;
        foreach (var t in part.Takes) bits |= 1u << (int)t;
        return bits;
    }

    /// <summary>A region by its name as the hit volumes' table says it (<c>"left forearm"</c>); <see cref="BodyRegion.None"/> for none or an unknown name. Allocates nothing.</summary>
    public static BodyRegion RegionOf(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty) return BodyRegion.None;
        foreach (var r in BodyRegions.All)
            if (name.SequenceEqual(BodyRegions.Name(r))) return r;
        return BodyRegion.None;
    }

    /// <summary>The sheet condition's text for <paramref name="bits"/>: the missing regions' names in the regions' order, a comma between (<c>left forearm,left hand</c>).</summary>
    public static string Text(uint bits)
    {
        var sb = new StringBuilder();
        foreach (var r in BodyRegions.All)
        {
            if ((bits & (1u << (int)r)) == 0) continue;
            if (sb.Length > 0) sb.Append(',');
            sb.Append(BodyRegions.Name(r));
        }
        return sb.ToString();
    }

    /// <summary>The bits the sheet condition's text names (unknown names are left out). Allocates nothing.</summary>
    public static uint Parse(ReadOnlySpan<char> text)
    {
        uint bits = 0;
        while (!text.IsEmpty)
        {
            int comma = text.IndexOf(',');
            var item = (comma < 0 ? text : text[..comma]).Trim();
            if (RegionOf(item) is var r and not BodyRegion.None) bits |= 1u << (int)r;
            text = comma < 0 ? [] : text[(comma + 1)..];
        }
        return bits;
    }

    internal static SeveranceData? Read(JsonElement root, string file, List<string> problems, HarmData? harm)
    {
        int before = problems.Count;
        var c = new JsonChecks(file, problems);
        c.Schema(root, SchemaId);
        string status = c.Str(root, "status") ?? "";
        if (status.Length > 0 && status != "ENGINE_SANDBOX") c.Fail($"status is '{status}': severance's numbers are ENGINE_SANDBOX until the rules package gives them");

        var edged = new List<string>();
        var kinds = new bool[3];
        string edgedSource = "";
        if (c.Obj(root, "edged") is { } e)
        {
            if (c.Arr(e, "classes") is { } cls)
                foreach (var x in cls.EnumerateArray())
                {
                    if (x.ValueKind != JsonValueKind.String) { c.Fail("edged.classes must hold class ids"); continue; }
                    string id = x.GetString()!;
                    if (harm is not null && !harm.Classes.ContainsKey(id)) c.Fail($"edged.classes names class '{id}', which {harm.File} does not have");
                    else if (harm is not null && harm.Classes[id].Kind == HarmKind.Bashing) c.Fail($"edged.classes names class '{id}', whose harm is bashing: bashing never severs");
                    edged.Add(id);
                }
            if (c.Arr(e, "kinds") is { } ks)
                foreach (var x in ks.EnumerateArray())
                    switch (x.ValueKind == JsonValueKind.String ? x.GetString() : null)
                    {
                        case "lethal": kinds[(int)HarmKind.Lethal] = true; break;
                        case "aggravated": kinds[(int)HarmKind.Aggravated] = true; break;
                        case "bashing": c.Fail("edged.kinds names bashing: bashing never severs (by design)"); break;
                        default: c.Fail($"edged.kinds: {x.GetRawText()} is not lethal or aggravated"); break;
                    }
            edgedSource = c.Str(e, "source") ?? "";
        }
        string harmSource = "", contSource = "", structSource = "", condition = "";
        if (c.Obj(root, "harm") is { } h)
        {
            if (c.Str(h, "measure") is { } m && m != PostSoakHealth) c.Fail($"harm.measure '{m}' is not one this module knows ({PostSoakHealth})");
            // Thresholds are shares of the victim's purchased maximum, rounded up to whole Health.
            if (c.Str(h, "threshold_of") is { } of && of != PurchasedMaximum) c.Fail($"harm.threshold_of '{of}' is not one this module knows ({PurchasedMaximum})");
            if (c.Str(h, "threshold_rounding") is { } tr && tr != Up) c.Fail($"harm.threshold_rounding '{tr}' is not one this module knows ({Up})");
            _ = c.Str(h, "threshold_source");
            harmSource = c.Str(h, "source") ?? "";
        }
        if (c.Obj(root, "continuation") is { } co)
        {
            if (c.Str(co, "rule") is { } m && m != ResidualShare) c.Fail($"continuation.rule '{m}' is not one this module knows ({ResidualShare})");
            contSource = c.Str(co, "source") ?? "";
        }
        if (c.Obj(root, "structural") is { } st)
        {
            if (c.Str(st, "of") is { } m && m != PurchasedMaximum) c.Fail($"structural.of '{m}' is not one this module knows ({PurchasedMaximum})");
            if (c.Str(st, "union") is { } u && u != ByLimbMost) c.Fail($"structural.union '{u}' is not one this module knows ({ByLimbMost})");
            if (c.Str(st, "rounding") is { } rd && rd != Nearest) c.Fail($"structural.rounding '{rd}' is not one this module knows ({Nearest})");
            condition = c.Str(st, "condition") ?? "";
            if (condition.Length > 0 && !TraitLayout.IsId(condition)) c.Fail($"structural.condition '{condition}' is not a lower-case identifier (a sheet condition's id)");
            structSource = c.Str(st, "source") ?? "";
        }

        // The angle band and its cut share; Melee's edge.
        var angle = ReadAngle(root, c);
        var edge = ReadEdge(root, c, harm);

        var data = new SeveranceData
        {
            File = file, Status = status, EdgedClasses = edged, Kinds = kinds, Condition = condition, EdgedSource = edgedSource,
            HarmSource = harmSource, ContinuationSource = contSource, StructuralSource = structSource, Angle = angle, Edge = edge,
        };
        // The parts by body (each the platform's anatomy tables it is for), and the body the rules alone judge.
        var bodies = new List<SeverableBody>();
        var anatomies = new List<string>();
        if (c.Obj(root, "bodies") is { } bs)
            foreach (var b in bs.EnumerateObject())
            {
                if (!TraitLayout.IsId(b.Name)) { c.Fail($"bodies.{b.Name}: a body's id is a lower-case identifier"); continue; }
                if (b.Value.ValueKind != JsonValueKind.Object) { c.Fail($"bodies.{b.Name} must be an object"); continue; }
                var names = new List<string>();
                if (c.Arr(b.Value, "anatomy") is { } an)
                    foreach (var x in an.EnumerateArray())
                    {
                        string? name = x.ValueKind == JsonValueKind.String ? x.GetString() : null;
                        bool known = false;
                        foreach (var t in AnatomyTables.All) if (t.Name == name) known = true;
                        if (!known) c.Fail($"bodies.{b.Name}.anatomy: {x.GetRawText()} is no anatomy table the platform knows ({string.Join(", ", AnatomyTables.All.Select(t => $"'{t.Name}'"))})");
                        else if (anatomies.Contains(name!)) c.Fail($"bodies.{b.Name}.anatomy: '{name}' is another body's already (a skeleton has one part table)");
                        else { names.Add(name!); anatomies.Add(name!); }
                    }
                var body = new SeverableBody { Id = b.Name, Anatomy = names, Source = c.Str(b.Value, "source") ?? "" };
                if (c.Obj(b.Value, "parts") is { } ps) ReadParts(ps, body, c, $"bodies.{b.Name}.parts");
                bodies.Add(body);
            }
        data.Bodies = bodies;
        string defaultBody = c.Str(root, "default_body") ?? "";
        if (defaultBody.Length > 0)
        {
            if (data.Body(defaultBody) is { } db) data.DefaultBody = db;
            else c.Fail($"default_body '{defaultBody}' is none of the bodies ({string.Join(", ", bodies.Select(x => x.Id))})");
        }
        _ = c.Str(root, "default_body_source");
        _ = c.Str(root, "parts_null_source");
        _ = c.Str(root, "parts_unavailable_source", required: false);   // the unavailable parts
        // The unavailable parts' counterfactual analogues (trace only), each an unavailable part read as a part
        // every body that has the first has as severable.
        if (root.TryGetProperty("unavailable_counterfactual", out var cf) && cf.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in cf.EnumerateObject())
            {
                if (p.Name == "source") continue;
                var from = RegionOf(p.Name);
                var to = p.Value.ValueKind == JsonValueKind.String ? RegionOf(p.Value.GetString()) : BodyRegion.None;
                if (from == BodyRegion.None || to == BodyRegion.None) { c.Fail($"unavailable_counterfactual.{p.Name}: {p.Value.GetRawText()} is no pair of body regions"); continue; }
                foreach (var b in bodies)
                    if (b.Unavailable(from) && b.Part(to) is null) c.Fail($"unavailable_counterfactual.{p.Name}: body {b.Id} has no severable '{BodyRegions.Name(to)}' to read it against");
                data.analogue[(int)from] = to;
            }
            _ = c.Str(cf, "source");
        }
        return problems.Count > before ? null : data;
    }

    /// <summary>A part's value in the data when its severance is unavailable (its movement not built).</summary>
    public const string UnavailableText = "unavailable";

    /// <summary>One body's parts (the earlier build's reading, per body): every region named, null when it is not severable; an earlier build, <see cref="UnavailableText"/> when its severance is unavailable.</summary>
    static void ReadParts(JsonElement ps, SeverableBody body, JsonChecks c, string at)
    {
        var limbs = new List<string>();
        Array.Fill(body.limbOf, -1);
        var named = new bool[BodyRegions.Count];
        foreach (var p in ps.EnumerateObject())
        {
            var region = RegionOf(p.Name);
            if (region == BodyRegion.None) { c.Fail($"{at}.{p.Name} is no body region (the hit volumes' table: head, neck, chest, …, left forearm, …)"); continue; }
            named[(int)region] = true;
            if (p.Value.ValueKind == JsonValueKind.Null) continue;   // not severable
            // ---- "unavailable": severable in principle, but its movement is not built, so never cut.
            if (p.Value.ValueKind == JsonValueKind.String)
            {
                if (p.Value.GetString() == UnavailableText) body.unavailable[(int)region] = true;
                else c.Fail($"{at}.{p.Name}: {p.Value.GetRawText()} is no part (a part's object, null when it is not severable, or \"{UnavailableText}\" when its severance waits for what the body does without it)");
                continue;
            }
            // ---- end
            var v = p.Value;
            double tshare = v.TryGetProperty("threshold_share", out var ts) ? c.Num(ts, $"{at}.{p.Name}.threshold_share") ?? 0 : 0;
            if (!v.TryGetProperty("threshold_share", out _)) c.Fail($"{at}.{p.Name}.threshold_share is missing (a share of the purchased maximum Health)");
            else if (!(tshare > 0)) c.Fail($"{at}.{p.Name}.threshold_share must be above 0, not {tshare}");
            double thousandths = tshare * 1000;
            if (Math.Abs(thousandths - Math.Round(thousandths)) > 1e-6) c.Fail($"{at}.{p.Name}.threshold_share {tshare} must be whole thousandths");
            double? share = null;
            if (!v.TryGetProperty("structural_share", out var sh)) c.Fail($"{at}.{p.Name}.structural_share is missing (null for none)");
            else if (sh.ValueKind != JsonValueKind.Null)
            {
                share = c.Num(sh, $"{at}.{p.Name}.structural_share");
                if (share is { } s && (s < 0 || s >= 1)) c.Fail($"{at}.{p.Name}.structural_share {s} must be a share from 0 up to (not) 1");
            }
            string limb = c.Str(v, "limb") ?? "";
            var takes = new List<BodyRegion>();
            if (c.Arr(v, "takes") is { } tk)
                foreach (var t in tk.EnumerateArray())
                {
                    var tr = t.ValueKind == JsonValueKind.String ? RegionOf(t.GetString()) : BodyRegion.None;
                    if (tr == BodyRegion.None || tr == region) c.Fail($"{at}.{p.Name}.takes: {t.GetRawText()} is no other body region");
                    else takes.Add(tr);
                }
            bool stops = c.Bool(v, "stops_body") ?? false;
            if (share is not null && limb.Length == 0) c.Fail($"{at}.{p.Name}: a structural share needs its limb (the union is by limb)");
            if (stops && share is not null) c.Fail($"{at}.{p.Name}: a part whose loss stops the body has no structural share (null)");
            if (share is { } ss && tshare <= ss)
                c.Fail($"{at}.{p.Name}: its threshold_share {tshare} must be above its structural_share {ss} (a cut's harm always above its structural loss: the rules package's HOLD case ruled out)");
            string source = c.Str(v, "source") ?? "";
            int li = -1;
            if (limb.Length > 0)
            {
                li = limbs.IndexOf(limb);
                if (li < 0) { limbs.Add(limb); li = limbs.Count - 1; }
            }
            body.parts[(int)region] = new SeverablePart(region, tshare, (int)Math.Round(thousandths), share, limb, [.. takes], stops, source);
            body.limbOf[(int)region] = share is null ? -1 : li;
            body.shareOf[(int)region] = share ?? 0;
        }
        foreach (var r in BodyRegions.All)
            if (!named[(int)r]) c.Fail($"{at} names no '{BodyRegions.Name(r)}' (every region, null when it is not severable)");
        // A part's share must not be below a part it takes on the same limb (the proximal subsumes the distal).
        foreach (var p in body.parts)
            if (p is { Share: { } s })
                foreach (var t in p.Takes)
                    if (body.parts[(int)t] is { Share: { } ts } && ts > s && body.limbOf[(int)t] == body.limbOf[(int)p.Region])
                        c.Fail($"{at}.{BodyRegions.Name(p.Region)}: its share {s} is below the share {ts} of '{BodyRegions.Name(t)}', which it takes (the proximal loss subsumes the distal)");
        body.Limbs = limbs;
    }

    /// <summary>The angle's limits (degrees, the normal's above the acute's, both inside 0 to 90) and the bands' cut shares (0 to 1; the glancing band's 0 by by design: a skim never cuts).</summary>
    static SeveranceAngle ReadAngle(JsonElement root, JsonChecks c)
    {
        if (c.Obj(root, "angle") is not { } a) return new SeveranceAngle();
        if (c.Str(a, "measure") is { } m && m != SineOfImpact) c.Fail($"angle.measure '{m}' is not one this module knows ({SineOfImpact}: the contact's ImpactSpeed over its PointSpeed, the sine of the angle)");
        double normal = a.TryGetProperty("normal_from_degrees", out var nd) ? c.Num(nd, "angle.normal_from_degrees") ?? 0 : 0;
        double acute = a.TryGetProperty("acute_from_degrees", out var ad) ? c.Num(ad, "angle.acute_from_degrees") ?? 0 : 0;
        if (!a.TryGetProperty("normal_from_degrees", out _) || !a.TryGetProperty("acute_from_degrees", out _)) c.Fail("angle needs normal_from_degrees and acute_from_degrees (about 33 and 10)");
        else if (!(acute > 0 && acute < normal && normal < 90)) c.Fail($"angle: the limits must rise, 0 < acute_from_degrees ({acute}) < normal_from_degrees ({normal}) < 90");
        double Share(string key)
        {
            if (c.Obj(a, "cut_share") is not { } cs) return 1;
            if (!cs.TryGetProperty(key, out var v)) { c.Fail($"angle.cut_share.{key} is missing (a share from 0 to 1)"); return 1; }
            double s = c.Num(v, $"angle.cut_share.{key}") ?? 1;
            if (s < 0 || s > 1) c.Fail($"angle.cut_share.{key} {s} must be a share from 0 to 1");
            return s;
        }
        double gl = Share("glancing");
        if (gl != 0) c.Fail($"angle.cut_share.glancing {gl} must be 0: a skim never cuts a part off (by design)");
        return new SeveranceAngle
        {
            NormalFromDegrees = normal, AcuteFromDegrees = acute,
            NormalSine = DetMath.Sin((float)(normal * DetMath.DegToRad)), AcuteSine = DetMath.Sin((float)(acute * DetMath.DegToRad)),
            Normal = Share("normal"), Acute = Share("acute"), Glancing = gl, Unmeasured = Share("unmeasured"),
            Source = c.Str(a, "source") ?? "",
        };
    }

    /// <summary>Melee's edge — its Ability, the classes that read another, the curve's base, ranks and reference rank, the most, the bands.</summary>
    static SeveranceEdge ReadEdge(JsonElement root, JsonChecks c, HarmData? harm)
    {
        if (c.Obj(root, "melee_edge") is not { } m) return new SeveranceEdge();
        if (c.Str(m, "curve") is { } cv && cv != AbilityCurve) c.Fail($"melee_edge.curve '{cv}' is not one this module knows ({AbilityCurve})");
        string ability = c.Str(m, "ability") ?? "melee";
        if (!TraitLayout.IsId(ability)) c.Fail($"melee_edge.ability '{ability}' is not an Ability's id");
        var byClass = new List<(string, string)>();
        if (c.Obj(m, "ability_by_class", required: false) is { } bc)
            foreach (var p in bc.EnumerateObject())
            {
                if (harm is not null && !harm.Classes.ContainsKey(p.Name)) c.Fail($"melee_edge.ability_by_class names class '{p.Name}', which {harm.File} does not have");
                string? ab = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
                if (ab is null || !TraitLayout.IsId(ab)) c.Fail($"melee_edge.ability_by_class.{p.Name} must be an Ability's id");
                else byClass.Add((p.Name, ab));
            }
        double Num(string key, double fallback)
        {
            if (!m.TryGetProperty(key, out var v)) { c.Fail($"melee_edge.{key} is missing"); return fallback; }
            return c.Num(v, $"melee_edge.{key}") ?? fallback;
        }
        double b = Num("base", 2.25), ranks = Num("ranks", 10), most = Num("most", 1);
        int reference = c.Int(m, "reference_rank") ?? 0;
        if (!(b >= 1)) c.Fail($"melee_edge.base {b} must be 1 or more (a higher rank never cuts worse)");
        if (!(ranks > 0)) c.Fail($"melee_edge.ranks {ranks} must be above 0");
        if (!(most > 0 && most <= 1)) c.Fail($"melee_edge.most {most} must be a share above 0, at most 1");
        if (reference < 0 || reference > 10) c.Fail($"melee_edge.reference_rank {reference} must be a rank from 0 to 10");
        var bands = new bool[4];
        if (c.Arr(m, "bands") is { } bl)
            foreach (var x in bl.EnumerateArray())
                switch (x.ValueKind == JsonValueKind.String ? x.GetString() : null)
                {
                    case "normal": bands[(int)AngleBand.Normal] = true; break;
                    case "acute": bands[(int)AngleBand.Acute] = true; break;
                    case "glancing": c.Fail("melee_edge.bands names glancing: a skim never cuts, however good the edge (by design)"); break;
                    default: c.Fail($"melee_edge.bands: {x.GetRawText()} is not normal or acute"); break;
                }
        return new SeveranceEdge
        {
            Ability = ability, ByClass = byClass, Base = b, Ranks = ranks, ReferenceRank = reference, Most = most, Bands = bands,
            Source = c.Str(m, "source") ?? "",
        };
    }
}

/// <summary>What severance decided for a blow (the trace says it in words: <see cref="SeveranceRules.Reason"/>).</summary>
public enum SeverVerdict : byte
{
    /// <summary>Not judged (a flurry's extra blow, or no severance's rules).</summary>
    NotJudged = 0,
    /// <summary>The part was cut off.</summary>
    Severed,
    /// <summary>Severance is off (<c>severance-off</c>): every body contact stops the blade.</summary>
    Off,
    /// <summary>No body region was named.</summary>
    NoRegion,
    /// <summary>The region is not severable (null in the data: the head, the legs).</summary>
    NotSeverable,
    /// <summary>The harm's kind does not cut (bashing).</summary>
    KindDoesNotCut,
    /// <summary>The weapon's class has no edge.</summary>
    NoEdge,
    /// <summary>The post-soak harm is below the part's threshold (the harm times the cut share).</summary>
    BelowThreshold,
    /// <summary>The blow met the body glancing (its cut share 0): a skim never cuts a part off, however hard.</summary>
    Glancing,
    /// <summary>The struck body has no part table (its skeleton is none <c>rules/severance</c>'s bodies name): never cut.</summary>
    NoPartTable,
    /// <summary>
    /// The rules package's part's severance is <b>unavailable</b> in this sandbox (the data's
    /// <c>"unavailable"</c>: the legs and feet) — a leg cut off needs the movement that goes with it (crawling, the gait, the jump),
    /// not built, so it is never cut; the blade stops there as at any part it did not cut.
    /// </summary>
    Unavailable,
}

/// <summary>
/// A swing's attack budget (digested module state, on the attacker): the share of its swing's
/// damage potential left, set to 1 at the swing's first body contact (<see cref="ContactMade.Chain"/> 0), cut by each severing
/// contact to the residual share, and to nothing when a contact stops the blade. Never refilled: a later contact of the same
/// swing reads it, a new swing sets it afresh.
/// </summary>
public struct SwingBudget
{
    public int Swing;
    public float Left;
}

/// <summary>
/// Severance's rules (ours) on <see cref="SeveranceData"/>: <see cref="Judge"/> decides a blow's severance and the blade's
/// continuation, <see cref="EffectiveMaximum"/> the Health the missing anatomy leaves a person able to heal to. With
/// <see cref="On"/> false (the module's switch <c>severance-off</c>) nothing is severed and every body contact stops the blade:
/// today's harm. with <see cref="AngleOn"/> (off with <c>severance-angle-off</c>: the test) the blow's harm
/// counts toward cutting by its angle band's share; with <see cref="EdgeOn"/> (off with <c>severance-melee-off</c>) the attacker's
/// Melee moves that share on the Abilities' curve; each body is judged by its own part table (<see cref="BodyOf"/>).
/// </summary>
public sealed class SeveranceRules(SeveranceData data, bool on, bool angleOn = true, bool edgeOn = true)
{
    public SeveranceData Data { get; } = data;
    public bool On { get; } = on;
    /// <summary>Whether the angle's cut share counts (off: every blow's whole harm counts, the test).</summary>
    public bool AngleOn { get; } = angleOn;
    /// <summary>Whether Melee's edge moves the cut share (only with <see cref="AngleOn"/>).</summary>
    public bool EdgeOn { get; } = edgeOn;

    /// <summary>
    /// The band a contact met its body in: its <see cref="ContactMade.ImpactSpeed"/> (along the surface's normal)
    /// over its <see cref="ContactMade.PointSpeed"/> (the striking point's whole speed against the target) is the sine of the angle
    /// (90° square on, 0° a skim); <see cref="AngleBand.None"/> when the point had no speed (nothing to measure).
    /// </summary>
    public AngleBand BandOf(in ContactMade c, out float sine)
    {
        sine = 0f;
        if (!(c.PointSpeed > 0f)) return AngleBand.None;
        sine = Math.Clamp(c.ImpactSpeed / c.PointSpeed, 0f, 1f);
        return Data.Angle.BandOf(sine);
    }

    /// <summary>
    /// The cut share of a blow in <paramref name="band"/> by an attacker whose edge Ability is
    /// <paramref name="rank"/> (−1: not known, no edge): the band's share (<see cref="SeveranceAngle.ShareOf"/>), times Melee's edge
    /// (<see cref="SeveranceEdge.Factor"/>) for the bands it applies to, at most <see cref="SeveranceEdge.Most"/>; 1 with the angle
    /// off. <paramref name="factor"/> is the edge's factor used (1 when none).
    /// </summary>
    public float CutShare(AngleBand band, int rank, out double factor) => (float)CutShareExact(band, rank, out factor);

    /// <summary><see cref="CutShare"/> unrounded (the test's own number; the event carries it as a float).</summary>
    public double CutShareExact(AngleBand band, int rank, out double factor)
    {
        factor = 1;
        if (!AngleOn) return 1;
        double share = Data.Angle.ShareOf(band);
        if (EdgeOn && rank >= 0 && Data.Edge.Bands[(int)band] && share > 0)
        {
            factor = Data.Edge.Factor(rank);
            share = Math.Min(Data.Edge.Most, share * factor);
        }
        return share;
    }

    /// <summary>
    /// The part table <paramref name="e"/>'s body is judged by: where the world draws the person (the platform's
    /// <see cref="HitVolumes"/> and its <see cref="CharacterAnimation"/>), the body for its skeleton's anatomy table
    /// (<see cref="SeveranceData.ForAnatomy"/>; null — never cut — when its skeleton has no table or the table no part table); where
    /// it does not (the rules alone, a test), <see cref="SeveranceData.DefaultBody"/>. Allocates nothing.
    /// </summary>
    public SeverableBody? BodyOf(World world, EntityId e)
    {
        if (world.TryService<HitVolumes>(out var volumes) && world.TryService<CharacterAssets>(out _)
            && world.TryStore<CharacterAnimation>(out var anims) && anims.Has(e))
            return volumes.Fitted(world, e) is { } fitted ? Data.ForAnatomy(fitted.Table) : null;
        return Data.DefaultBody;
    }

    /// <summary>The earlier build's form: the default body's table, and no edge Ability known (the band's own share).</summary>
    public void Judge(in ContactMade c, HarmResult r, uint missing, long purchased) => Judge(in c, r, missing, purchased, Data.DefaultBody, -1);

    /// <summary>
    /// Decides the severance of the swing's own blow just resolved into <paramref name="r"/> (Applied or Resisted), on a body
    /// missing <paramref name="missing"/> whose purchased maximum Health is <paramref name="purchased"/>: the post-soak harm in
    /// Health (the wounds × the translator, before Health caps it) against the part's threshold (its share of the
    /// purchased maximum, rounded up: <see cref="SeverablePart.ThresholdOf"/>), for an edged class
    /// and a lethal or aggravated blow. A cut sets <see cref="HarmResult.Severed"/>, the anatomy after, the structural loss (the
    /// union's difference), whether it stops the body, and the residual share the blade goes on with
    /// (<c>(harm − threshold) / harm</c> of what it had); a blow that does not cut stops the blade (nothing left). Allocates
    /// nothing but a reason's text.
    /// <para>The blow's angle band from its geometry (<see cref="BandOf"/>, written whatever the switches) and,
    /// with severance and the angle on, its <b>cut share</b> (<see cref="CutShare"/>, the attacker's edge Ability
    /// <paramref name="edgeRank"/>, −1 for none): the test is the post-soak harm <b>times the cut share</b> against the threshold,
    /// a glancing blow (share 0) never cuts, and the blade goes on with <c>(cut − threshold) / cut</c> of what it had. The harm to
    /// Health is not touched. The part table is <paramref name="body"/>'s (null: the body has none, never cut).</para>
    /// </summary>
    public void Judge(in ContactMade c, HarmResult r, uint missing, long purchased, SeverableBody? body, int edgeRank)
    {
        r.Region = SeveranceData.RegionOf(c.Site);
        r.PostSoakHealth = (long)r.Wounds * r.HealthPerWound;
        r.MissingBefore = r.MissingAfter = missing;
        var deficits = body ?? Data.DefaultBody;
        r.DeficitBefore = r.DeficitAfter = deficits.Deficit(missing, purchased);
        r.PurchasedMaximum = purchased;
        r.Continues = false;
        r.BudgetOut = 0f;
        r.SeverThreshold = 0;
        // The angle band (measured whatever the switches), the cut share (with severance and the angle on).
        r.Angle = BandOf(in c, out r.Sine);
        r.AngleOn = On && AngleOn;
        r.EdgeOn = On && AngleOn && EdgeOn;
        r.EdgeRank = edgeRank;
        r.Body = body?.Id;
        r.AngleRule = Data.Angle;
        r.EdgeRule = Data.Edge;
        // ---- the cut share re-based on the blow's quality (its band from Q in place of the angle's: the sine is
        // inside q, so the angle's band on top would count it twice); the angle's band still measured and traced. Melee's edge as ever.
        var shareBand = r.CutShareFromQuality ? r.QualityCutBand : r.Angle;
        r.CutShareBase = AngleOn ? (float)Data.Angle.ShareOf(shareBand) : 1f;
        double share = On ? CutShareExact(shareBand, edgeRank, out r.EdgeFactor) : 1;
        // ---- end
        r.CutShare = (float)share;
        // The harm that counts toward cutting, to a millionth of Health (a share's last bits never tip a blow over a threshold).
        r.CutHarm = share == 1 ? r.PostSoakHealth : Math.Round(r.PostSoakHealth * share, 6, MidpointRounding.AwayFromZero);
        if (!On) { r.Verdict = SeverVerdict.Off; return; }
        if (body is null) { r.Verdict = SeverVerdict.NoPartTable; return; }
        if (body.Part(r.Region) is not { } part)
        {
            r.Verdict = r.Region == BodyRegion.None ? SeverVerdict.NoRegion
                : body.Unavailable(r.Region) ? SeverVerdict.Unavailable   // the unavailable parts
                : SeverVerdict.NotSeverable;
            // The counterfactual, for the trace only — the threshold the leg's analogue would have, nothing cut.
            if (r.Verdict == SeverVerdict.Unavailable && Data.CounterfactualOf(r.Region) is var to and not BodyRegion.None && body.Part(to) is { } like)
            {
                r.CounterfactualRegion = to;
                r.CounterfactualShare = like.ThresholdShare;
                r.CounterfactualThreshold = (int)Math.Min(int.MaxValue, like.ThresholdOf(purchased));
                r.CounterfactualWouldCut = Data.Kinds[(int)r.Kind] && r.Class is not null && Data.Edged(r.Class.Id) && share > 0 && r.CutHarm >= r.CounterfactualThreshold;
            }
            return;
        }
        r.SeverThreshold = (int)Math.Min(int.MaxValue, part.ThresholdOf(purchased));
        r.SeverThresholdShare = part.ThresholdShare;
        if (!Data.Kinds[(int)r.Kind]) { r.Verdict = SeverVerdict.KindDoesNotCut; return; }
        if (r.Class is null || !Data.Edged(r.Class.Id)) { r.Verdict = SeverVerdict.NoEdge; return; }
        r.SeverEligible = true;
        if (!(share > 0)) { r.Verdict = SeverVerdict.Glancing; return; }
        if (r.CutHarm < r.SeverThreshold) { r.Verdict = SeverVerdict.BelowThreshold; return; }

        r.Verdict = SeverVerdict.Severed;
        r.Severed = part.Region;
        r.StopsBody = part.StopsBody;
        r.MissingAfter = SeveranceData.Lose(part, missing);
        r.DeficitAfter = body.Deficit(r.MissingAfter, purchased);
        r.StructuralLoss = r.DeficitAfter - r.DeficitBefore;
        double rest = (r.CutHarm - r.SeverThreshold) / r.CutHarm;
        r.BudgetOut = (float)(r.BudgetIn * rest);
        r.Continues = r.BudgetOut > 0f;
    }

    /// <summary>The verdict of <paramref name="r"/> in words, for the trace.</summary>
    public static string Reason(HarmResult r) => r.Verdict switch
    {
        SeverVerdict.Glancing when r.CutShareFromQuality => $"the blow met the {BodyRegions.Name(r.Region)} with too little work to cut (quality Q {r.Quality:0.###}, below rules/quality's cut_share.acute_from_quality {r.QualityRule?.CutAcuteFrom:0.###}): a skim never cuts a part off",
        SeverVerdict.Severed when r.CutShareFromQuality && r.CutShare < 1f => $"{r.PostSoakHealth} Health after soak, {r.CutHarm:0.#} of it cutting (quality Q {r.Quality:0.###}, cut share {r.CutShare:0.###}), reaches the {BodyRegions.Name(r.Severed)}'s {r.SeverThreshold}",
        SeverVerdict.BelowThreshold when r.CutShareFromQuality && r.CutShare < 1f => $"{r.PostSoakHealth} Health after soak, {r.CutHarm:0.#} of it cutting (quality Q {r.Quality:0.###}, cut share {r.CutShare:0.###}), is below the {BodyRegions.Name(r.Region)}'s {r.SeverThreshold}",
        SeverVerdict.Severed when r.CutShare < 1f => $"{r.PostSoakHealth} Health after soak, {r.CutHarm:0.#} of it cutting ({r.Angle}, cut share {r.CutShare:0.###}), reaches the {BodyRegions.Name(r.Severed)}'s {r.SeverThreshold}",
        SeverVerdict.BelowThreshold when r.CutShare < 1f => $"{r.PostSoakHealth} Health after soak, {r.CutHarm:0.#} of it cutting ({r.Angle}, cut share {r.CutShare:0.###}), is below the {BodyRegions.Name(r.Region)}'s {r.SeverThreshold}",
        SeverVerdict.Glancing => $"the blow met the {BodyRegions.Name(r.Region)} glancing (sine {r.Sine:0.###}): a skim never cuts a part off (rules/severance: angle.cut_share.glancing 0)",
        SeverVerdict.NoPartTable => "the struck body has no part table (rules/severance: bodies): it is never cut",
        SeverVerdict.Severed => $"{r.PostSoakHealth} Health after soak reaches the {BodyRegions.Name(r.Severed)}'s {r.SeverThreshold}",
        SeverVerdict.Off => "severance is off (the switch severance-off): every body contact stops the blade",
        SeverVerdict.NoRegion => "no body region named",
        SeverVerdict.NotSeverable => $"the {BodyRegions.Name(r.Region)} is not severable (rules/severance: null)",
        SeverVerdict.Unavailable => $"UNAVAILABLE: the {BodyRegions.Name(r.Region)}'s severance waits for what a body does without it (crawling, the gait, the jump): its movement is not built (rules/severance: \"unavailable\"), so it is never cut",
        SeverVerdict.KindDoesNotCut => $"{r.Kind} harm never severs (blades only)",
        SeverVerdict.NoEdge => $"class '{r.Class?.Id}' has no edge (rules/severance: edged.classes)",
        SeverVerdict.BelowThreshold => $"{r.PostSoakHealth} Health after soak is below the {BodyRegions.Name(r.Region)}'s {r.SeverThreshold}",
        _ => "not judged",
    };

    /// <summary>
    /// The effective maximum Health of <paramref name="e"/> whose purchased maximum is <paramref name="purchased"/>: the purchased
    /// less the structural deficit of what it is missing (<see cref="MissingParts"/>). The purchased when nothing
    /// is missing. Allocates nothing.
    /// </summary>
    public long EffectiveMaximum(World world, EntityId e, long purchased) => purchased - (BodyOf(world, e) ?? Data.DefaultBody).Deficit(Missing(world, e), purchased);

    /// <summary>What <paramref name="e"/> is missing (<see cref="MissingParts.Bits"/>; 0 for none or no store).</summary>
    public static uint Missing(World world, EntityId e)
    {
        if (!world.TryStore<MissingParts>(out var store)) return 0;
        ref var m = ref store.Find(e);
        return Unsafe.IsNullRef(ref m) ? 0 : m.Bits;
    }

    /// <summary>
    /// Writes what <paramref name="e"/> is missing: the platform's <see cref="MissingParts"/> and the sheet's condition (the record
    /// a save keeps: <see cref="SeveranceData.Condition"/>). Called on a severance (allocates the condition's text).
    /// </summary>
    public void SetMissing(World world, ActorSheets sheets, EntityId e, uint bits)
    {
        var store = world.Store<MissingParts>();
        ref var m = ref store.Find(e);
        if (Unsafe.IsNullRef(ref m)) store.Set(e, new MissingParts { Bits = bits });
        else m.Bits = bits;
        if (sheets.Has(e)) sheets.SetCondition(e, Data.Condition, SeveranceData.Text(bits));
    }
}

/// <summary>
/// The anatomy kept in step with the sheets (<see cref="Phase.Act"/>, order <see cref="SeveranceServices.AnatomyOrder"/>,
/// before the powers write the vitals): each person whose sheet has the condition <c>missing_anatomy</c> (a loaded save, an
/// imported sheet) gets <see cref="MissingParts"/> with its bits; one whose sheet has none has none. The sheet is the rules'
/// record, the component the platform's view of it. Allocates nothing per tick.
/// </summary>
public sealed class AnatomySystem(SeveranceRules rules) : ISystem
{
    public void Run(World world)
    {
        if (!world.TryService<ActorSheets>(out var sheets) || !world.TryStore<MissingParts>(out var store)) return;
        var people = world.Store<ActorIdentity>().Entities;
        string id = rules.Data.Condition;
        for (int i = 0; i < people.Length; i++)
        {
            var e = people[i];
            if (!sheets.Has(e)) continue;
            uint bits = sheets.Condition(e, id) is { } text ? SeveranceData.Parse(text) : 0;
            ref var m = ref store.Find(e);
            bool has = !Unsafe.IsNullRef(ref m);
            if (bits == 0) { if (has) store.Remove(e); }
            else if (!has) store.Set(e, new MissingParts { Bits = bits });
            else if (m.Bits != bits) m.Bits = bits;
        }
    }
}

/// <summary>Severance registered.</summary>
public static class SeveranceServices
{
    /// <summary>In <see cref="Phase.Act"/>, before the powers (<see cref="PowerServices.PowersOrder"/>), which write the vitals' cap from it.</summary>
    public const int AnatomyOrder = 78;

    /// <summary>
    /// Registers the <see cref="SeveranceRules"/> service, the anatomy (<see cref="AnatomyServices.Register"/>: <see cref="MissingParts"/>
    /// and <see cref="PartSevered"/>, unless registered), the budget (<see cref="SwingBudget"/>) and the system <c>enhanced.anatomy</c>.
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b, SeveranceRules rules)
    {
        b.Service(rules);
        AnatomyServices.Register(b);
        b.Component<SwingBudget>();
        b.System(Phase.Act, AnatomyOrder, "enhanced.anatomy", new AnatomySystem(rules));
        return b;
    }
}
