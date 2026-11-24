// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.Json;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The arena's summary against its CSV: bouts made up with kills, hits, wounds and contacts are written to
/// <c>bouts.csv</c> and <c>contacts.csv</c>, and the page's numbers (carried inert in it as JSON) are worked out again from
/// the CSV alone — the win counts and rate with Wilson's interval, the time to the kill's mean, spread, median and range,
/// damage per hit, wounds per bout, contacts by kind — and agree; the page is self-contained (inline SVG, no script but the
/// inert numbers). Needs no game files.
/// </summary>
public sealed class ArenaSummaryTests
{
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    /// <summary>The page's numbers, read back from its inert JSON.</summary>
    internal static List<PointSummary> PageNumbers(string page)
    {
        const string open = "<script type=\"application/json\" id=\"arena-numbers\">";
        int a = page.IndexOf(open, StringComparison.Ordinal) + open.Length;
        int b = page.IndexOf("</script>", a, StringComparison.Ordinal);
        return JsonSerializer.Deserialize<List<PointSummary>>(page[a..b].Replace("<\\/", "</"))!;
    }

    /// <summary>Works the summary's numbers out again from <c>bouts.csv</c> and compares them with the page's.</summary>
    internal static void CheckAgainstCsv(ArenaSpec spec, string folder)
    {
        var rows = ArenaTables.Read(Path.Combine(folder, "bouts.csv"));
        var head = rows[0];
        int Col(string name) => Array.IndexOf(head, name) is var i and >= 0 ? i : throw new Xunit.Sdk.XunitException($"no column {name}");
        double D(string[] r, string name) => r[Col(name)] is { Length: > 0 } v ? double.Parse(v, Ic) : double.NaN;
        var page = PageNumbers(File.ReadAllText(Path.Combine(folder, "summary.html")));
        int subject = ArenaSummary.Subject(spec);
        Assert.Equal(spec.Points.Count, page.Count);
        var everyBout = rows.Skip(1).ToList();
        for (int p = 0; p < page.Count; p++)
        {
            var bouts = rows.Skip(1).Where(r => r[Col("point")] == p.ToString(Ic)).ToList();
            var s = page[p];
            Assert.Equal(bouts.Count, s.Bouts);
            for (int v = 0; v < spec.Sweeps.Count; v++)
                Assert.All(bouts, r => Assert.Equal(s.Values[v].ToString(Ic), r[Col(spec.Sweeps[v].Label(spec))]));
            for (int f = 0; f < spec.Fighters.Count; f++)
                Assert.Equal(bouts.Count(r => r[Col("winner")] == spec.Fighters[f].Name), s.Wins[f]);
            int wins = bouts.Count(r => r[Col("winner")] == spec.Fighters[subject].Name);
            Assert.Equal(wins / (double)bouts.Count, s.WinRate, 12);
            var (low, high) = ArenaSummary.Wilson(wins, bouts.Count);
            Assert.Equal(low, s.WinLow, 12);
            Assert.Equal(high, s.WinHigh, 12);
            var kills = bouts.Where(r => r[Col("end")] == "kill").Select(r => D(r, "kill_seconds")).Order().ToList();
            Assert.Equal(kills.Count, s.Kills);
            if (kills.Count == 0) Assert.Null(s.KillMean);
            else
            {
                double mean = kills.Average();
                Assert.Equal(mean, s.KillMean!.Value, 5);
                Assert.Equal(kills.Count < 2 ? 0 : Math.Sqrt(kills.Sum(k => (k - mean) * (k - mean)) / (kills.Count - 1)), s.KillSd!.Value, 5);
                Assert.Equal(kills[0], s.KillMin!.Value, 5);
                Assert.Equal(kills[^1], s.KillMax!.Value, 5);
            }
            double hits = bouts.Sum(r => spec.Fighters.Sum(f => D(r, $"{f.Name}.hits")));
            Assert.Equal(hits, s.Hits);
            double wounds = bouts.Sum(r => spec.Fighters.Sum(f => D(r, $"{f.Name}.wounds_dealt")));
            if (hits == 0) Assert.Null(s.WoundsPerHit);
            else
            {
                Assert.Equal(wounds / hits, s.WoundsPerHit!.Value, 9);
                // The mean dice per hit, from each fighter's mean and hits.
                double dice = bouts.Sum(r => spec.Fighters.Sum(f => D(r, $"{f.Name}.hits") is var h and > 0 ? h * D(r, $"{f.Name}.mean_damage_dice") : 0));
                Assert.Equal(dice / hits, s.DicePerHit!.Value, 4);
            }
            for (int f = 0; f < spec.Fighters.Count; f++)
                Assert.Equal(bouts.Average(r => D(r, $"{spec.Fighters[f].Name}.wounds_dealt")), s.WoundsPerBout[f], 9);
            string[] kinds = ["contacts_body", "contacts_shield", "contacts_weapon", "contacts_level"];
            for (int k = 0; k < 4; k++)
                Assert.Equal(bouts.Average(r => spec.Fighters.Sum(f => D(r, $"{f.Name}.{kinds[k]}"))), s.ContactsPerBout[k], 9);
            Assert.Equal(bouts.Average(r => spec.Fighters.Sum(f => D(r, $"{f.Name}.swings"))), s.SwingsPerBout, 9);

            // Per fighter, what its powers did and what each swing came to (one power: live_seconds is its window).
            double seconds = bouts.Sum(r => D(r, "seconds"));
            for (int f = 0; f < spec.Fighters.Count; f++)
            {
                string n = spec.Fighters[f].Name;
                double Sum(string c) => bouts.Sum(r => D(r, $"{n}.{c}"));
                Assert.Equal(Sum("blood_spent") / bouts.Count, s.BloodSpentPerBout[f], 9);
                Assert.Equal(Sum("uses") / bouts.Count, s.UsesPerBout[f], 9);
                Assert.Equal(Sum("refused") / bouts.Count, s.RefusedPerBout[f], 9);
                Assert.Equal(Sum("live_seconds") / bouts.Count, s.LiveSecondsPerBout[f], 5);
                Assert.Equal(Sum("swings_live") / bouts.Count, s.SwingsLivePerBout[f], 9);
                double swings = Sum("swings");
                Assert.Equal(swings / seconds, s.SwingsPerSecond[f], 5);
                if (swings == 0) Assert.Null(s.BodyContactsPerSwing[f]);
                else
                {
                    Assert.Equal(Sum("contacts_body") / swings, s.BodyContactsPerSwing[f]!.Value, 9);
                    Assert.Equal(Sum("wounds_dealt") / swings, s.WoundsPerSwing[f]!.Value, 9);
                }
                // The entitlement receipt, pooled: the swings a second while live and while not (this point's), and the extra swings a
                // bout against the run's unpaid rate (every point's time with no power live).
                double live = Sum("live_seconds"), unpaidSeconds = seconds - live;
                if (live > 1e-9) Assert.Equal(Sum("swings_live") / live, s.LiveSwingsPerSecond[f]!.Value, 4);
                else Assert.Null(s.LiveSwingsPerSecond[f]);
                double runUnpaid = everyBout.Sum(r => D(r, "seconds") - D(r, $"{n}.live_seconds"));
                double runRate = everyBout.Sum(r => D(r, $"{n}.swings") - D(r, $"{n}.swings_live")) / runUnpaid;
                Assert.Equal(runRate, s.RunUnpaidSwingsPerSecond[f]!.Value, 4);
                Assert.Equal((Sum("swings_live") - live * runRate) / bouts.Count, s.ExtraSwingsPerBout[f]!.Value, 4);
                // The flurry's blows, and the receipt's extra actions (swings while live + flurry blows − live seconds × the run's
                // unpaid rate); the Health lost at least the drop from start to end (more where it was healed).
                Assert.Equal(Sum("flurry_blows") / bouts.Count, s.FlurryBlowsPerBout[f], 9);
                Assert.Equal((Sum("swings_live") + Sum("flurry_blows") - live * runRate) / bouts.Count, s.ExtraActionsPerBout[f]!.Value, 4);
                double drop = bouts.Where(r => r[Col($"{n}.health_start")].Length > 0).Sum(r => D(r, $"{n}.health_start") - D(r, $"{n}.health_end"));
                Assert.True(s.HealthLostPerBout[f] >= drop / bouts.Count - 1e-9, $"{n}: Health lost {s.HealthLostPerBout[f]} a bout, below its drop {drop / bouts.Count}");
                if (unpaidSeconds <= 1e-9) { Assert.Null(s.UnpaidSwingsPerSecond[f]); continue; }
                Assert.Equal((swings - Sum("swings_live")) / unpaidSeconds, s.UnpaidSwingsPerSecond[f]!.Value, 4);
                // Each bout's own rate is its row's (swings while none was live over the time none was).
                foreach (var r in bouts)
                    if (D(r, "seconds") - D(r, $"{n}.live_seconds") > 1e-9)
                        Assert.Equal((D(r, $"{n}.swings") - D(r, $"{n}.swings_live")) / (D(r, "seconds") - D(r, $"{n}.live_seconds")), D(r, $"{n}.swings_unpaid_per_s"), 4);
            }
        }
    }

    [Fact]
    public void A_grid_s_summary_agrees_with_its_csv_too()
    {
        var spec = ArenaSpec.Read("""
            { "fighters": [ { "name": "A" }, { "name": "B" } ], "bouts": 6,
              "sweeps": [ { "fighter": "A", "trait": "strength", "values": [10, 20, 30, 40] }, { "fighter": "B", "trait": "stamina", "values": [5, 25, 45] } ] }
            """, Path.GetTempPath(), ArenaSpecTests.FakeLevel.Value);
        var (folder, summary, page) = MadeUp(spec, "grid", 91);
        CheckAgainstCsv(spec, folder);
        Assert.Equal(12, summary.Count);
        Assert.Equal(5, CountOf(page, "<svg "));
        Assert.Contains("B.stamina = 45", page);
    }

    [Fact]
    public void The_summary_s_numbers_worked_out_again_from_the_csv_agree()
    {
        var spec = ArenaSpec.Read("""
            { "fighters": [ { "name": "A" }, { "name": "B" } ], "bouts": 5,
              "sweep": { "fighter": "B", "trait": "strength", "values": [10, 20, 30] } }
            """, Path.GetTempPath(), ArenaSpecTests.FakeLevel.Value);
        var (folder, summary, page) = MadeUp(spec, "line", 77);
        var contacts = ArenaTables.Read(Path.Combine(folder, "contacts.csv"));
        Assert.All(summary, s => Assert.Equal(0, s.Uses));    // no power: no power chart
        CheckAgainstCsv(spec, folder);
        Assert.Contains(summary, s => s.Kills > 0);
        Assert.Contains(summary, s => s.Hits > 0);
        // The subject is the swept fighter (B).
        Assert.Equal(1, ArenaSummary.Subject(spec));
        // One row per contact, its harm beside it.
        Assert.Equal(1 + spec.Points.Count * spec.Bouts, contacts.Count);
        Assert.Equal("6", contacts[1][Array.IndexOf(contacts[0], "health_after")]);
        Assert.Equal("chest", contacts[1][Array.IndexOf(contacts[0], "site")]);
        // Self-contained: inline SVG charts, the numbers' table, no script but the inert numbers, nothing fetched.
        // Three charts more (swings a second, body contacts and wounds a swing); the powers' only when one was used;
        // And Health lost a bout.
        Assert.Equal(5 + 3 + 1, CountOf(page, "<svg "));
        Assert.Contains("Health lost a bout", page);
        Assert.Equal(1, CountOf(page, "<script"));
        Assert.DoesNotContain("http://", page);
        Assert.DoesNotContain("https://", page);
        Assert.Contains("enhanced-vampire 0123", page);
    }

    [Fact]
    public void A_power_sweep_s_summary_shows_what_the_powers_did_and_agrees_with_its_csv()
    {
        // Made-up uses, Blood, refusals and time live, rising with the swept rank (a stand-in's numbers, no game files).
        var spec = ArenaSpec.Read("""
            { "fighters": [ { "name": "A" }, { "name": "B" } ], "bouts": 8,
              "sweep": { "fighter": "A", "power": "celerity", "options": ["celerity.active"], "values": [0, 1, 2, 3, 4, 5] } }
            """, Path.GetTempPath(), ArenaSpecTests.FakeLevel.Value);
        var (folder, summary, page) = MadeUp(spec, "powers", 5, powers: true);
        CheckAgainstCsv(spec, folder);
        Assert.Equal(0, summary[0].Uses);                       // rank 0: nothing learned, nothing used
        Assert.True(summary[5].Uses > 0 && summary[5].BloodSpentPerBout[0] > 0);
        Assert.All(summary, s => Assert.Equal(0, s.UsesPerBout[1]));
        // and the powers' four: Blood, uses, the pace live or not, the extra actions; the flurry's blows, Health lost.
        Assert.Equal(5 + 3 + 4 + 2, CountOf(page, "<svg "));
        Assert.Contains("Extra actions a bout its powers bought", page);
        Assert.Contains("A extra swings/bout", page);
        Assert.Contains("A extra actions/bout", page);
        Assert.Contains("Flurry blows a bout", page);
        Assert.True(summary[5].FlurryBlowsPerBout[0] > 0);
        Assert.Equal(0, summary[0].FlurryBlowsPerBout[0]);
        var rows = ArenaTables.Read(Path.Combine(folder, "bouts.csv"));
        Assert.Contains(rows.Skip(1), r => r[Array.IndexOf(rows[0], "A.refused_reasons")] == "NoBlood:1");
    }

    /// <summary>Bouts made up from a seed for every point of <paramref name="spec"/>, written as the arena writes them; the folder, the numbers and the page.</summary>
    static (string Folder, List<PointSummary> Summary, string Page) MadeUp(ArenaSpec spec, string name, ulong seed, bool powers = false)
    {
        var rng = new Rng(seed);
        var results = new List<BoutResult>();
        var contacts = new List<ContactRow>();
        long id = 1;
        for (int p = 0; p < spec.Points.Count; p++)
            for (int b = 0; b < spec.Bouts; b++)
            {
                var r = new BoutResult { Point = p, Values = spec.Points[p], Bout = b, Seed = 1 + (ulong)b, Fighters = [new FighterTally(), new FighterTally()], Ticks = 3600 };
                foreach (var f in r.Fighters)
                {
                    f.Swings = rng.NextInt(20);
                    f.Strikes = f.Swings;
                    f.ContactsBody = rng.NextInt(6);
                    f.ContactsShield = rng.NextInt(3);
                    f.ContactsWeapon = rng.NextInt(2);
                    f.ContactsLevel = rng.NextInt(2);
                    f.Hits = f.ContactsBody;
                    f.DamageDice = f.Hits * (2 + rng.NextInt(4));
                    f.DamageSuccesses = f.Hits * rng.NextInt(3);
                    f.WoundsDealt = f.Hits * rng.NextInt(3);
                    f.BlocksRaised = rng.NextInt(5);
                }
                r.Fighters[0].WoundsTaken = r.Fighters[1].WoundsDealt;
                r.Fighters[1].WoundsTaken = r.Fighters[0].WoundsDealt;
                if (rng.NextInt(3) > 0)
                {
                    int dead = rng.NextInt(2);
                    r.Fighters[dead].Stopped = true;                                   // was Died
                    r.Fighters[dead].Stop = "ZERO_HEALTH_STOP";
                    r.Fighters[dead].HealthLeft = 0;
                    r.Winner = 1 - dead;
                    r.KillSeconds = Math.Round(5 + rng.NextFloat() * 20, 3);
                    r.Ticks = (long)Math.Round(r.KillSeconds.Value * World.TicksPerSecond) + 1;
                }
                int rank = spec.Points[p].Length > 0 ? spec.Points[p][0] : 0;
                if (powers && rank > 0)
                {
                    // The first fighter cast its power: uses and Blood by the rank, a refusal now and then, a window within the bout.
                    var f = r.Fighters[0];
                    f.Uses = 1 + rng.NextInt(3);
                    f.Renewals = f.Uses - 1;
                    f.BloodStart = 100;
                    f.BloodSpent = 30L * f.Uses;
                    f.BloodEnd = 100 - (int)f.BloodSpent;
                    if (rng.NextInt(2) == 0) { f.PowersRefused = 1; f.RefusedBy[Powers.PowerDenial.NoBlood] = 1; }
                    long window = Math.Min(r.Ticks - 1, (long)(rank * 2.5 * World.TicksPerSecond));
                    f.LiveTicks["celerity"] = window;
                    f.LiveAnyTicks = window;
                    f.SwingsLive = Math.Min(f.Swings, rng.NextInt(4 + rank * 2));
                    f.FlurryBlows = f.SwingsLive * rank / 4;                              // blows its swings carried (no draw: the made-up stream unchanged)
                    f.FlurryWounds = f.FlurryBlows;
                    f.WoundsDealt += f.FlurryWounds;
                    r.Fighters[1].WoundsTaken += f.FlurryWounds;
                }
                foreach (var f in r.Fighters)                                             // Health on the 1-1000 scale
                {
                    f.HealthStart = 100;
                    f.HealthLost = Math.Min(100, 15 * f.WoundsTaken);
                    f.HealthEnd = 100 - (int)f.HealthLost;
                }

                r.Digest = rng.NextULong();
                results.Add(r);
                var c = new ContactMade(id++, EntityId.None, 1, EntityId.None, ContactMet.Body, "chest", ContactMaterial.Flesh, 100, new(1, 2, 1.2f), new(0, 1, 0), 9.5f, 12f, false, false, true, false);
                contacts.Add(new ContactRow(p, spec.Points[p], b, r.Seed, 0, 1, c) { Harm = new HarmResolved(c.ContactId, c.Attacker, c.Target, 100, HarmKind.Lethal, 5, 2, 0, 2, 1, 1, 7, 6) });
            }
        string folder = Path.Combine(Path.GetTempPath(), $"vitomir-arena-made-up-{name}-{Environment.ProcessId}");
        Directory.CreateDirectory(folder);
        ArenaTables.WriteBouts(Path.Combine(folder, "bouts.csv"), spec, results);
        ArenaTables.WriteContacts(Path.Combine(folder, "contacts.csv"), spec, contacts);
        var summary = ArenaSummary.Compute(spec, results);
        string page = ArenaSummary.Page(spec, summary, new ArenaSummary.Head("Vitomir Platform test", "enhanced-vampire 0123", "SANDBOX", 1, "fast"));
        File.WriteAllText(Path.Combine(folder, "summary.html"), page);
        return (folder, summary, page);
    }

    [Fact]
    public void Wilsons_interval_is_the_textbook_s()
    {
        // 8 of 10: 0.4902 to 0.9433 (Wilson 1927; the usual tables).
        var (low, high) = ArenaSummary.Wilson(8, 10);
        Assert.Equal(0.4902, low, 4);
        Assert.Equal(0.9433, high, 4);
        Assert.Equal((0.0, 1.0), ArenaSummary.Wilson(0, 0));
        Assert.Equal(0.0, ArenaSummary.Wilson(0, 20).Low, 12);
    }

    static int CountOf(string text, string what)
    {
        int n = 0;
        for (int i = text.IndexOf(what, StringComparison.Ordinal); i >= 0; i = text.IndexOf(what, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }
}
