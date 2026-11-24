// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text;
using Vitomir.Platform.Core;
using Vitomir.Platform.Powers;

namespace Vitomir.Platform.Host.Arena;

/// <summary>
/// One fighter's uses of one power in a bout: uses (renewals included), renewals, the Blood they paid and the amount
/// their effect gave (<see cref="PowerUsed.Amount"/>: a temporary Attribute's rise, a heal's Health gained), summed.
/// </summary>
public sealed class PowerTally
{
    public int Uses, Renewals;
    public long Blood, Amount;
}

/// <summary>
/// The arena's powers by command: each fighter's uses of each power counted from the module's <see cref="PowerUsed"/>
/// (whatever the power does), and <c>powers.csv</c>, one row a bout, fighter and power used — so "Blood spent by command"
/// and "Health healed" read straight from it.
/// </summary>
public static class ArenaPowers
{
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    /// <summary>Counts a use to its fighter's tally by the power (<see cref="FighterTally.PowerOf"/>).</summary>
    public static void Count(FighterTally f, in PowerUsed u)
    {
        string power = FighterTally.PowerOf(u.Power);
        if (!f.ByPower.TryGetValue(power, out var t)) f.ByPower[power] = t = new PowerTally();
        t.Uses++;
        if (u.Renewed) t.Renewals++;
        t.Blood += u.Cost;
        t.Amount += u.Amount;
    }

    /// <summary>The header of <c>powers.csv</c>.</summary>
    public static List<string> Header(ArenaSpec spec)
    {
        var h = new List<string> { "point" };
        h.AddRange(spec.Sweeps.Select(s => s.Label(spec)));
        h.AddRange(["bout", "seed", "fighter", "power", "uses", "renewals", "blood_spent", "amount", "live_seconds"]);
        return h;
    }

    /// <summary>Writes <c>powers.csv</c>: a row for each fighter and power it used in each bout, in the bouts', fighters' and powers' order.</summary>
    public static void Write(string path, ArenaSpec spec, IEnumerable<BoutResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendJoin(',', Header(spec)).Append('\n');
        foreach (var r in results)
            for (int i = 0; i < r.Fighters.Length; i++)
                foreach (var (power, t) in r.Fighters[i].ByPower)
                {
                    var row = new List<string> { r.Point.ToString(Ic) };
                    row.AddRange(r.Values.Select(v => v.ToString(Ic)));
                    long live = r.Fighters[i].LiveTicks.TryGetValue(power, out long l) ? l : 0;
                    row.AddRange([r.Bout.ToString(Ic), r.Seed.ToString(Ic), spec.Fighters[i].Name, power, t.Uses.ToString(Ic), t.Renewals.ToString(Ic),
                        t.Blood.ToString(Ic), t.Amount.ToString(Ic), ArenaTables.Num(live / (double)World.TicksPerSecond)]);
                    sb.AppendJoin(',', row).Append('\n');
                }
        File.WriteAllText(path, sb.ToString());
    }
}
