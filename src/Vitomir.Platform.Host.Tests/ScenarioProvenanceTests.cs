// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Control;
using Vitomir.Platform.Host.Scenario;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// A first run of the packaged program found two gaps, now closed: a packaged program sits
/// outside its clone, so <c>results.json</c> could not name the source it ran (now the packaging script writes the clone's state
/// beside the program, <see cref="GitInfo.BuildInfoFile"/>, and the runner reads it); and a value measure of a distance said
/// <c>value</c> where it meant metres (now a value measure takes its observable's own unit).
/// </summary>
public sealed class ScenarioProvenanceTests
{
    [Fact]
    public void A_packaged_programs_build_info_gives_the_source_it_was_packaged_from()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-build-info-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, GitInfo.BuildInfoFile);
            // As the packaging script's ConvertTo-Json writes it (PowerShell 5 indents with four spaces).
            File.WriteAllText(path, """
                {
                    "format":  "vitomir-build-info/1",
                    "version":  "1.0",
                    "batch":  null,
                    "commit":  "b28f1cba2abde526d2bbc901f985d585cf14158f",
                    "branch":  "codex/melee-rank-sweep-02",
                    "dirty":  true,
                    "diffSha256":  "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                    "packagedUtc":  "2026-10-24T18:00:00.0000000Z"
                }
                """);
            var g = GitInfo.Packaged(path);
            Assert.Equal(new GitInfo("b28f1cba2abde526d2bbc901f985d585cf14158f", "codex/melee-rank-sweep-02", true,
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"), g);

            File.WriteAllText(path, """{ "commit": "b28f1cba2abde526d2bbc901f985d585cf14158f", "branch": "master", "dirty": false, "diffSha256": null }""");
            Assert.Equal(new GitInfo("b28f1cba2abde526d2bbc901f985d585cf14158f", "master", false, null), GitInfo.Packaged(path));

            File.WriteAllText(path, "not json");
            Assert.Null(GitInfo.Packaged(path));
            Assert.Null(GitInfo.Packaged(Path.Combine(dir, "none.json")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// A study of the scenario runner: a console program published by <c>dotnet publish</c> in an offline sandbox had no
    /// modules beside it and named no source. Now the console project lays the modules out as the host's does, and the build writes
    /// the clone's state into the program (the host project's <c>VitomirSourceIdentity</c>), read when neither the live clone nor
    /// the packaging script's file answers. Built from a git clone (as every test run here is), the program knows its commit.
    /// </summary>
    [Fact]
    public void A_program_built_from_a_clone_knows_the_commit_it_was_built_from()
    {
        var g = GitInfo.Built();
        if (g is null) return; // built where git was not to be had: nothing written, and Read() says so (null commit)
        Assert.Matches("^[0-9a-f]{40}$", g.Commit!);
        Assert.NotNull(g.Dirty);
        Assert.Null(g.DiffSha256);
    }

    [Fact]
    public void A_value_measure_of_an_observable_takes_its_unit()
    {
        Assert.Equal("m", ScenarioRunner.ObservableUnit(new ObservableForm("Christof.distanceTo.Guard")));
        Assert.Equal("m", ScenarioRunner.ObservableUnit(new ObservableAtEventForm("Christof.distanceTo.Guard", new EventSelector("AttackBegun"))));
        Assert.Equal("m/s", ScenarioRunner.ObservableUnit(new ObservableForm("Guard.speed")));
        Assert.Equal("s", ScenarioRunner.ObservableUnit(new ObservableForm("time")));
        Assert.Equal("count", ScenarioRunner.ObservableUnit(new ObservableForm("Guard.contacts.shield")));
        Assert.Null(ScenarioRunner.ObservableUnit(new ObservableForm("Guard.nothing")));
        Assert.Null(ScenarioRunner.ObservableUnit(new TimeOfForm(new EventSelector("AttackBegun"))));
    }
}
