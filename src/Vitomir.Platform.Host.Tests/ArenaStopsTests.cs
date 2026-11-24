// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Combat;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The rules package's corrections to the arena's outputs (their review of returns 1 to 4, findings 3 and 8):
/// a fighter's stop is a harness stop, never a death (<c>stopped</c> in <c>died</c>'s place, each fighter's <c>stop</c> at the
/// end, the summary saying plainly that no torpor or Final Death exists); every swept point named by its actor, its sheet and
/// the variable.
/// </summary>
public sealed class ArenaStopsTests
{
    static ArenaSpec Spec(string sweep) => ArenaSpec.Read($$"""
        { "fighters": [
            { "name": "Christof", "template": "Christof", "sheet": "sandbox.newly_embraced" },
            { "name": "Guard", "template": "rudolfGuard", "sheet": { "template": "sandbox.guard_trained", "values": { "strength": 30, "melee": 3 } } } ],
          {{sweep}} }
        """, Path.GetTempPath(), ArenaSpecTests.FakeLevel.Value);

    [Fact]
    public void Each_swept_point_names_its_actor_its_sheet_and_the_variable()
    {
        var spec = Spec("""
            "sweep": { "fighter": "Christof", "trait": "strength", "values": [15, 200] }
            """);
        Assert.Equal("Christof (sandbox.newly_embraced): Strength 200", ArenaSummary.PointName(spec, spec.Points[1]));
        // The swept fighter's own values over its sheet are named, but the swept one; a power's rank with its options.
        var guard = Spec("""
            "sweep": { "fighter": "Guard", "trait": "melee", "values": [0, 5] }
            """);
        Assert.Equal("Guard (sandbox.guard_trained; strength 30): Melee 5", ArenaSummary.PointName(guard, guard.Points[1]));
        var power = Spec("""
            "sweep": { "fighter": "Christof", "power": "celerity", "options": ["celerity.active"], "values": [0, 3] }
            """);
        Assert.Equal("Christof (sandbox.newly_embraced): Celerity rank 0", ArenaSummary.PointName(power, power.Points[0]));
        Assert.Equal("Christof (sandbox.newly_embraced): Celerity rank 3 (celerity.active)", ArenaSummary.PointName(power, power.Points[1]));
        var grid = Spec("""
            "sweeps": [ { "fighter": "Christof", "trait": "strength", "values": [15] }, { "fighter": "Guard", "trait": "stamina", "values": [25] } ]
            """);
        Assert.Equal("Christof (sandbox.newly_embraced): Strength 15; Guard (sandbox.guard_trained; strength 30, melee 3): Stamina 25", ArenaSummary.PointName(grid, grid.Points[0]));

        // The page lists the points and puts each one's name in its row of the numbers; the console's line says it too (ArenaCommand).
        var results = new List<BoutResult>();
        for (int p = 0; p < spec.Points.Count; p++)
            results.Add(new BoutResult { Point = p, Values = spec.Points[p], Bout = 0, Seed = 1, Fighters = [new FighterTally(), new FighterTally { Stopped = true, Stop = "ZERO_HEALTH_STOP" }], Winner = 0, Ticks = 1200, KillSeconds = 10 });
        var summary = ArenaSummary.Compute(spec, results);
        Assert.Equal(["Christof (sandbox.newly_embraced): Strength 15", "Christof (sandbox.newly_embraced): Strength 200"], summary.Select(s => s.Name));
        Assert.Equal(["", "ZERO_HEALTH_STOP:1"], summary[1].StopsBy);
        string page = ArenaSummary.Page(spec, summary, new ArenaSummary.Head("test", "", "", 1, ""));
        Assert.Contains("<h2>The points</h2>", page);
        Assert.Contains("point 1: Christof (sandbox.newly_embraced): Strength 200", page);
        Assert.Contains("No torpor and no Final Death exist yet", page);
        Assert.Contains("ZERO_HEALTH_STOP:1", page);
        Assert.Contains("\"Name\":\"Christof (sandbox.newly_embraced): Strength 200\"", page);   // in the page's numbers (JSON) too
    }

    [Fact]
    public void A_fighters_stop_is_stopped_and_named_never_died()
    {
        Assert.Equal("stopped", ArenaTables.FighterColumns[^1]);
        Assert.DoesNotContain("died", ArenaTables.FighterColumns);
        var spec = Spec("""
            "bouts": 1
            """);
        var r = new BoutResult { Point = 0, Values = [], Bout = 0, Seed = 1, Fighters = [new FighterTally(), new FighterTally { Stopped = true, Stop = ArenaTables.DisarmedName }], Winner = 0, Disarmed = true, Ticks = 600 };
        var header = ArenaTables.BoutHeader(spec);
        var row = ArenaTables.BoutRow(spec, r);
        string Cell(string name) => row[header.IndexOf(name)];
        Assert.Equal(("0", "1", "", "DISARMED", "DISARMED"), (Cell("Christof.stopped"), Cell("Guard.stopped"), Cell("Christof.stop"), Cell("Guard.stop"), Cell("stop_state")));
        int fsn = ArenaTables.FlurryStrikeColumns.Length * 2;                 // The flurry work's block at the very end, after it
        int cq = (ArenaTables.QualityColumns.Length + ArenaTables.ReboundColumns.Length + ArenaTables.SolverColumns.Length + ArenaTables.DirectionColumns.Length + ArenaTables.FightColumns.Length + ArenaTables.StaggerRunColumns.Length + ArenaTables.PaceColumns.Length + ArenaTables.TempoColumns.Length + ArenaTables.SideColumns.Length + ArenaTables.ShieldReadColumns.Length) * 2 + ArenaTables.GroupColumns.Length;   // its block after                       // the quality work: its block after the flurry work's
        Assert.Equal(["Christof.stop", "Guard.stop"], header[^(cq + fsn + 2)..^(cq + fsn)]);   // after every block before
        Assert.Equal("ZERO_HEALTH_STOP", ArenaTables.StopName(ActorStop.ZeroHealthStop));
        Assert.Equal("SEVERED_STOP", ArenaTables.StopName(ActorStop.SeveredStop));
    }

    [Fact]
    public void A_hit_the_armour_alone_turned_to_nothing_reads_armor_in_contacts_csv()
    {
        var h = new HarmResolved(1, default, default, 0, HarmKind.Lethal, 3, 3, 0, 3, 3, 0, 100, 100, ResistedBy: ResistedBy.Armor, Armor: "mail_shirt", ArmorKept: 3);
        Assert.Equal("armor", ArenaTables.ResistedName(h));
        Assert.Equal("soak", ArenaTables.ResistedName(h with { ResistedBy = ResistedBy.Soak, ArmorKept = 0 }));
        Assert.Equal("", ArenaTables.ResistedName(h with { Wounds = 1, ResistedBy = ResistedBy.None }));
    }
}
