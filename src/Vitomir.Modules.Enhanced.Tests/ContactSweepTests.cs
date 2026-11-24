// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Text;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The fixed-contact harness (the rules package's <i>"Fixed-contact controls sweeping squareness, closing
/// velocity, edge place, motion rate and armor"</i>: <i>"Near-zero-speed tangent-slide fixtures vs firm normal contact with equal
/// Strength/weapon, measured harm and cuts"</i>). One contact built at chosen geometry — the angle between the blade's motion and the
/// struck surface, the point's closing speed, the place along the blade, the swing's rate (its tempo: the speed and the clip's peak
/// together), the struck part bare or under a mail shirt — run through the module's own staged harm and severance (the module's
/// data, blow quality on and off) with the same dice streams in every cell (the world's streams from one seed: paired), many times
/// a cell, for Strength 15, 200 and 1000. The table is written to <c>contact-sweep.md</c> beside the tests (and to
/// <c>$VITOMIR_CONTACT_SWEEP_OUT</c> when set). It shows, and the test checks, that the tempo cancels (the same Q and the same harm
/// at ×1, ×2 and ×3), that the angle enters once (through q; the cut share from Q's band, not the angle's band on top), and that
/// armour comes after quality (the armour's dice the same whatever the blow's work).
/// </summary>
public sealed class ContactSweepTests
{
    /// <summary>The blow every cell plays: Christof's quick forehand with 1.1's Rapier (its reference 0.46; its peak tip speed 45.7 m/s at rate 1).</summary>
    const string Clip = "chrisAttackSword", Weapon = "Rapier", Character = "Christof";
    const float Peak = 45.7f;
    /// <summary>The typical contact of that blow (the earlier build's medians): the point's speed 25.8 m/s, met at 56° (sine 0.83), 0.4 of the way along the blade.</summary>
    const float Closing = 25.8f, Along = 0.4f;
    const double Degrees = 56;
    const int Trials = 4000;
    static readonly int[] Strengths = [15, 200, 1000];
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    sealed record Cell(string Sweep, string Value, double Degrees, float Closing, float Along, float Tempo, bool Mail);

    /// <summary>A cell's means over its trials.</summary>
    sealed record Result(double Q, double Factor, double Wounds, double Cuts, double LowWork, double ArmorDice, AngleBand AngleBand, AngleBand CutBand, double CutShare);

    static ContactMade Contact(Cell c)
    {
        float sine = DetMath.Sin((float)(c.Degrees * DetMath.DegToRad));
        float point = c.Closing * c.Tempo;
        return new ContactMade(1, new EntityId(1, 0), 1, new EntityId(2, 0), ContactMet.Body, "left upper arm", ContactMaterial.Cloth, 10,
            Vector3.Zero, Vector3.UnitX, point * sine, point * sine * 1.1f, false, false, true, false,
            PointSpeed: point, AlongBlade: c.Along, TipSpeed: Peak * c.Tempo * 0.9f, PeakSpeed: Peak * c.Tempo);
    }

    static Result Run(HarmRules rules, SeveranceRules sever, SeveranceData data, ArmorRules armor, Cell c, int strength, ulong seed)
    {
        var a = new Striker(new EntityId(1, 0), "vampire", strength, 0, false, Weapon, SwingClip: Clip, Character: Character);
        var worn = c.Mail ? armor.Set("mail_shirt") : null;
        // The combat rules' trained guard: a human, Stamina 25, 100 Health bought (severance's thresholds are its shares).
        var t = new Struck(new EntityId(2, 0), "human", 25, 0, false, true, 100, 100, Armor: worn);
        var contact = Contact(c);
        var rnd = new WorldRandom(new RandomStreams(seed));
        var r = new HarmResult();
        double q = 0, f = 0, wounds = 0, cuts = 0, low = 0, armorDice = 0, share = 0;
        AngleBand band = AngleBand.None, cutBand = AngleBand.None;
        for (int i = 0; i < Trials; i++)
        {
            rules.Resolve(in contact, in a, in t, rnd, r);
            sever.Judge(in contact, r, 0, 100, data.DefaultBody, 2);   // Melee 2: the edge's reference rank (the bands' own shares)
            q += r.Quality; f += r.QualityFactor; wounds += r.Wounds; armorDice += r.ArmorPotential; share += r.CutShare;
            if (r.Severed != BodyRegion.None) cuts++;
            if (r.ResistedBy == ResistedBy.LowWork) low++;
            band = r.Angle; cutBand = r.CutShareFromQuality ? r.QualityCutBand : r.Angle;
        }
        return new Result(q / Trials, f / Trials, wounds / Trials, cuts / Trials, low / Trials, armorDice / Trials, band, cutBand, share / Trials);
    }

    static IEnumerable<Cell> Cells()
    {
        foreach (var d in new[] { 2.0, 5, 10, 20, 33, 45, 56, 90 }) yield return new("squareness (degrees)", d.ToString(Ic), d, Closing, Along, 1, false);
        foreach (var v in new[] { 0.5f, 2, 5, 10, 15, 21, 25.8f, 30, 40 }) yield return new("closing speed (m/s)", v.ToString(Ic), Degrees, v, Along, 1, false);
        foreach (var a in new[] { 0f, 0.05f, 0.09f, 0.1f, 0.2f, 0.4f, 0.9f }) yield return new("place along the blade", a.ToString(Ic), Degrees, Closing, a, 1, false);
        foreach (var t in new[] { 1f, 2f, 3f }) yield return new("motion rate (tempo)", "x" + t.ToString(Ic), Degrees, Closing, Along, t, false);
        foreach (var (v, mail) in new[] { (Closing, false), (Closing, true), (5f, false), (5f, true) })
            yield return new("armour", $"{(mail ? "mail shirt" : "bare")}, {v.ToString(Ic)} m/s", Degrees, v, Along, 1, mail);
    }

    [Fact]
    public void The_fixed_contact_sweep_writes_its_table_and_shows_tempo_cancelling_the_angle_once_and_armour_after_quality()
    {
        var m = TestData.Sandbox();
        var on = HarmRules.Build(m.Config, m.Harm);
        on.Quality = new BlowQuality(m.Quality!, true);
        var off = HarmRules.Build(m.Config, m.Harm);
        off.Quality = new BlowQuality(m.Quality!, false);
        var sever = new SeveranceRules(m.Severance, on: true);
        var armor = new ArmorRules(m.Armor!, true);
        var results = new Dictionary<(Cell, int, bool), Result>();
        var sb = new StringBuilder();
        sb.AppendLine("# The fixed-contact sweep (blow quality)");
        sb.AppendLine();
        sb.AppendLine(FormattableString.Invariant($"One contact on the combat rules' trained guard's left upper arm (a human, Stamina 25, 100 Health; its threshold 75), Christof's quick forehand with the Rapier (q_ref {m.Quality!.Find(Clip, "sword_one_hand", Character, Weapon)!.QRef}, peak {Peak} m/s at rate 1), Melee 2 for the edge; typical geometry: point {Closing} m/s, {Degrees}°, {Along} along the blade, rate 1, bare. {Trials} trials a cell from the same seed's streams (paired). Each cell: blow quality on / off. *Wounds* a contact; *cuts* the share of contacts that cut the arm off; *low work* the share resisted by `LowWork`. ENGINE_SANDBOX numbers (rules/quality, rules/severance, combat/armor), the module's own stages."));
        sb.AppendLine();
        sb.AppendLine("| sweep | value | angle band | Q | factor | cut band (on) | Strength 15: wounds on / off | cuts on / off | low work | Strength 200: wounds on / off | cuts on / off | low work | Strength 1000: wounds on / off | cuts on / off | low work | armour dice |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var c in Cells())
        {
            var row = new StringBuilder();
            Result? first = null;
            foreach (int s in Strengths)
            {
                var rOn = Run(on, sever, m.Severance, armor, c, s, 77);
                var rOff = Run(off, sever, m.Severance, armor, c, s, 77);
                results[(c, s, true)] = rOn;
                results[(c, s, false)] = rOff;
                first ??= rOn;
                row.Append(FormattableString.Invariant($" {rOn.Wounds:0.00} / {rOff.Wounds:0.00} | {rOn.Cuts:0.000} / {rOff.Cuts:0.000} | {rOn.LowWork:0.000} |"));
            }
            sb.AppendLine(FormattableString.Invariant($"| {c.Sweep} | {c.Value} | {first!.AngleBand} | {first.Q:0.000} | {first.Factor:0.000} | {first.CutBand} |{row} {first.ArmorDice:0.00} |"));
        }
        string table = sb.ToString();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "contact-sweep.md"), table);
        if (Environment.GetEnvironmentVariable("VITOMIR_CONTACT_SWEEP_OUT") is { Length: > 0 } outPath) File.WriteAllText(outPath, table);

        // The tempo cancels: the same Q, factor and harm at x1, x2, x3 (the same dice: identical, not merely close).
        var rates = Cells().Where(c => c.Sweep.StartsWith("motion", StringComparison.Ordinal)).ToArray();
        foreach (int s in Strengths)
            foreach (bool q in new[] { true, false })
            {
                var one = results[(rates[0], s, q)];
                foreach (var other in rates.Skip(1))
                {
                    var o = results[(other, s, q)];
                    Assert.Equal(one.Q, o.Q, 5);
                    Assert.Equal(one.Factor, o.Factor, 5);
                    Assert.Equal((one.Wounds, one.Cuts, one.LowWork), (o.Wounds, o.Cuts, o.LowWork));
                }
            }
        // The angle enters once: an acute contact's harm is scaled by its Q (the sine is inside it) and its cut share is its Q band's,
        // not the angle band's share on top of Q; a glancing skim's work is near nothing, so its harm and its cuts are.
        var acute = Cells().First(c => c.Sweep.StartsWith("squareness", StringComparison.Ordinal) && c.Value == "20");
        var square = Cells().First(c => c.Sweep.StartsWith("squareness", StringComparison.Ordinal) && c.Value == "90");
        var skim = Cells().First(c => c.Sweep.StartsWith("squareness", StringComparison.Ordinal) && c.Value == "2");
        Assert.Equal(AngleBand.Acute, results[(acute, 1000, true)].AngleBand);
        double sin20 = Math.Sin(20 * Math.PI / 180), sin90 = 1;
        Assert.Equal(sin20 / sin90, results[(acute, 1000, true)].Q / results[(square, 1000, true)].Q, 3);   // the angle in Q, once
        Assert.True(results[(skim, 1000, true)].Wounds < 0.2 * results[(skim, 1000, false)].Wounds);
        Assert.Equal(0, results[(skim, 1000, true)].Cuts);
        // Armour after quality: the armour's dice are the same whatever the blow's work, and the low-work blow loses to the soak too.
        var armoured = Cells().Where(c => c.Mail).ToArray();
        Assert.Equal(results[(armoured[0], 200, true)].ArmorDice, results[(armoured[1], 200, true)].ArmorDice);
        Assert.True(results[(armoured[0], 200, true)].ArmorDice > 0);
        // Off is the class's whole potential whatever the geometry: the closing speed moves no harm.
        var slow = Cells().First(c => c.Sweep.StartsWith("closing", StringComparison.Ordinal) && c.Value == "0.5");
        var fast = Cells().First(c => c.Sweep.StartsWith("closing", StringComparison.Ordinal) && c.Value == "40");
        Assert.Equal(results[(slow, 200, false)].Wounds, results[(fast, 200, false)].Wounds);
        Assert.True(results[(slow, 200, true)].Wounds < 0.1 * results[(fast, 200, true)].Wounds);
    }
}
