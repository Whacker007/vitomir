// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The play-session journal. Its whole value is that somebody who did not watch the run can read
/// what happened, so what is tested here is the things that would quietly destroy that: a format
/// that shifts, a field that swallows a tab, an event that never gets written.
///
/// One test sets the global journal (<see cref="Journal.Current"/>) while a level runs, so the class runs on its own:
/// another class's simulation running beside it wrote its deaths into that journal, and once into it after it was
/// closed (<c>ArcherDuelTests</c> on a Windows PC, "Cannot write to a closed TextWriter").
/// </summary>
[Collection(nameof(JournalTests))]
[CollectionDefinition(nameof(JournalTests), DisableParallelization = true)]
public class JournalTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "vitomir-journal-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Journal.Current = null;
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch { }
    }

    Journal Open() => Journal.Open(folder, "P1_TEST", "SCENE_1", ["--journal"]);

    [Fact]
    public void A_line_is_the_time_the_kind_and_then_the_fields()
    {
        var journal = Open();
        journal.Write(1234, JournalKind.Blow, ("by", "rat"), ("hit", true), ("dmg", 7.25f));
        journal.Dispose();

        var line = Lines(journal).Single(l => !l.StartsWith('#'));
        var parts = line.Split('\t');
        Assert.Equal("1234", parts[0]);
        Assert.Equal("blow", parts[1]);
        Assert.Equal("by=rat hit=1 dmg=7.25", parts[2]);
    }

    [Fact]
    public void Nothing_a_template_name_can_contain_breaks_the_format()
    {
        // Every line has to stay three tab-separated columns, whatever lands in a field.
        var journal = Open();
        journal.Write(0, "test", ("a", "two words"), ("b", "with\ta tab"), ("c", "with\nnewline"),
                                 ("d", "quote\"inside"), ("skipped", null), ("e", 1));
        journal.Dispose();

        var line = Lines(journal).Single(l => !l.StartsWith('#'));
        Assert.Equal(3, line.Split('\t').Length);
        Assert.DoesNotContain('\n', line);
        Assert.Contains("a=\"two words\"", line);
        Assert.Contains("d=quote'inside", line);
        Assert.DoesNotContain("skipped", line);        // a null field is left out, not written empty
    }

    [Fact]
    public void The_file_opens_with_the_run_and_closes_with_a_tally()
    {
        var journal = Open();
        journal.Write(1, JournalKind.Blow);
        journal.Write(2, JournalKind.Blow);
        journal.Write(3, JournalKind.Death);
        journal.Dispose();
        journal.Dispose();                              // twice is safe

        var text = Lines(journal);
        Assert.Contains(text, l => l.StartsWith("# level\tP1_TEST"));
        Assert.Contains(text, l => l.StartsWith("# scene\tSCENE_1"));
        Assert.Contains(text, l => l == "# blow\t2");
        Assert.Contains(text, l => l == "# death\t1");
        // And no byte-order mark, because the file is meant to be grepped.
        Assert.False(File.ReadAllBytes(journal.Path)[0] == 0xEF);
    }

    [Fact]
    public void A_gap_is_named_once_and_counted_every_time()
    {
        var journal = Open();
        for (int i = 0; i < 5; i++) journal.Gap(i, "pick-up", ("on", "sword"));
        journal.Dispose();

        Assert.Single(Lines(journal), l => l.Contains("\tgap\t"));
        Assert.Contains(Lines(journal), l => l == "# gap:pick-up\t5");
    }

    [Fact]
    public void A_folder_gets_a_file_named_for_the_run_and_a_path_is_taken_as_given()
    {
        using var byFolder = Journal.Open(folder, "P1_HAVN", null, []);
        Assert.StartsWith(folder, byFolder.Path);
        Assert.EndsWith("-P1_HAVN.tsv", byFolder.Path);

        string named = Path.Combine(folder, "mine.tsv");
        using var byName = Journal.Open(named, "P1_HAVN", null, []);
        Assert.Equal(named, byName.Path);
    }

    [Fact]
    public void A_real_fight_leaves_a_readable_account_of_itself()
    {
        // The end-to-end check, and the thing the journal exists for: run a level nobody is
        // watching and ask the file what happened.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON2", things: true, sky: false,
            scene: ChronicleSet.Load(files).ForLevel("P1_MON2")?.Entry?.SceneName, player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));

        var journal = Journal.Open(folder, "P1_MON2", null, []);
        Journal.Current = journal;
        try
        {
            var sim = new Simulation(place);
            sim.Advance(70f, 1f / 30f);
        }
        finally { Journal.Current = null; }
        journal.Dispose();

        var lines = Lines(journal);
        // The rat finds Christof, closes, and swings: a target, orders and blows, all with names.
        Assert.Contains(lines, l => l.Contains("\ttarget\t") && l.Contains("took=Christof"));
        Assert.Contains(lines, l => l.Contains("\torder\t") && l.Contains("what=attack"));
        Assert.Contains(lines, l => l.Contains("\tblow\t") && l.Contains("at=Christof"));
        // And the census, which is what tells a quiet stretch from a stalled one.
        Assert.Contains(lines, l => l.Contains("\ttick\t") && l.Contains("playerHp="));
        Assert.True(lines.Count(l => l.Contains("\ttick\t")) >= 30, "the census should run throughout");
    }

    static string[] Lines(Journal journal) => File.ReadAllLines(journal.Path);
}
