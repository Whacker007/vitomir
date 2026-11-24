// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **The story sweep** (<see cref="StorySweep"/>, ours; <c>--story-sweep</c>):
/// which places it starts, and the shape of what it writes. It exists to find breakage in chapters
/// nobody has played, so what matters here is that it reaches every single-player place, that a scene's
/// failure is caught and written down with the script's own stack rather than stopping the run, and that
/// a scene's row is the same however many scenes run around it. Two short scenes stand in for the
/// whole game: the convent's opening, which runs clean, and the third monastery level, whose shipped
/// scene hangs <c>ActorAIOff_TriggerOn</c> on a rat leader with no actor (parameter −1), so the
/// script's constructor throws a <c>NullPointerException</c>.
/// </summary>
[Collection("scripts")]
public class StorySweepTests(Scripts s)
{
    [Fact]
    public void The_targets_are_every_single_player_place_and_the_chapter_scenes_no_line_names()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var targets = StorySweep.Targets(files, ChronicleSet.Load(files));

        Assert.DoesNotContain(targets, t => t.Chronicle.StartsWith("MP_", StringComparison.OrdinalIgnoreCase));
        foreach (var name in new[] { "Prague", "Prague2", "Vienna", "London", "New_York" })
            Assert.Contains(targets, t => t.Chronicle.Equals(name, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(targets, t => t is { Scene: "CNVT_3_1", Origin: "chronicle", Location: "Convent" });
        Assert.Contains(targets, t => t is { Scene: "CNVT_3_3", Origin: "advance", Location: "Convent" });
        Assert.DoesNotContain(targets, t => t.Scene.Length == 0);            // the locked-only locations are never entered
        Assert.Equal(targets.Count, targets.Select(t => t.Scene).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Two_scenes_write_a_row_each_and_a_script_failure_carries_the_java_stack()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var dir = Path.Combine(Path.GetTempPath(), "vitomir-story-sweep-test");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        int code = StorySweep.Sweep(files, s.Compiler!, dir, "CNVT_3_1,MON3_8_3", 5f, TextWriter.Null);

        Assert.Equal(0, code);
        var rows = File.ReadAllLines(Path.Combine(dir, "story-sweep.tsv")).Where(l => !l.StartsWith('#')).ToList();
        Assert.Equal(string.Join('\t', StorySweep.Columns), rows[0]);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(StorySweep.Columns.Length, r.Split('\t').Length));

        var cells = rows.Skip(1).Select(r => StorySweep.Columns.Zip(r.Split('\t')).ToDictionary(p => p.First, p => p.Second)).ToList();
        Assert.All(cells, c => Assert.Equal("1", c["started"]));
        Assert.All(cells, c => Assert.Equal("1", c["finished"]));
        Assert.All(cells, c => Assert.True(int.Parse(c["native_calls"]) > 0, "the scripts ran"));
        var monastery = cells.Single(c => c["scene"] == "MON3_8_3");
        Assert.NotEqual("0", monastery["script_failures"]);

        var findings = File.ReadAllLines(Path.Combine(dir, "story-findings.tsv")).Where(l => !l.StartsWith('#')).ToList();
        Assert.Equal("scene\tkind\tmessage\tdetail", findings[0]);
        var npe = findings.Single(f => f.Contains("ActorAIOff_TriggerOn: java.lang.NullPointerException", StringComparison.Ordinal));
        Assert.Contains("\tscript-failure\t", npe);
        Assert.Contains("ActorAIOff_TriggerOn.<init>:", npe);            // the script's own frame, with its line

        var md = File.ReadAllText(Path.Combine(dir, "story-sweep.md"));
        Assert.Contains("| kind | distinct | scenes affected |", md);
        Assert.Contains("## script-failure", md);
        Assert.StartsWith("done: 2 scenes", File.ReadAllText(Path.Combine(dir, "story-progress.txt")));
    }

    [Fact]
    public void A_scene_runs_the_same_alone_as_after_another()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var sweep = new StorySweep(files, s.Compiler!);
        var targets = StorySweep.Targets(files, ChronicleSet.Load(files));
        var convent = targets.Single(t => t.Scene == "CNVT_3_1");
        var monastery = targets.Single(t => t.Scene == "MON3_8_3");

        static string Row(StorySweep.Result r) => string.Join('\t', StorySweep.Row(r).Split('\t')[..^1]);   // all but the wall clock
        var alone = Row(sweep.Run(monastery, 5f));
        sweep.Run(convent, 5f);
        var after = Row(sweep.Run(monastery, 5f));

        Assert.Equal(alone, after);
    }
}
