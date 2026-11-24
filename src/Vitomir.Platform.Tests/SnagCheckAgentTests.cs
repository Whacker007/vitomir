// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Navigation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The snag check and the agent a package's navigation was baked for (ours): the agent is read from the
/// cells' <c>NAVM</c> (<see cref="LevelNavigation.Agent"/>); it differs from a body where it climbs more than the
/// body steps, walks steeper than the body, or is thinner or shorter (a bolder or smaller agent plans routes the
/// body cannot take; a more cautious one does not); and a snag check says so in a note line and in its summary,
/// with the pace, the profile and the body it drove. The East Gate test level (<c>VITOMIR_TEST_LEVEL</c>) was baked
/// for the cook's default agent; its parts are skipped when it is absent.
/// </summary>
public sealed class SnagCheckAgentTests(ITestOutputHelper output)
{
    static readonly NavAgent Human = new(0.3f, 1.8f, 0.35f, 40f, 0.15f, 0.05f);

    [Fact]
    public void An_agent_bolder_or_smaller_than_the_body_differs_from_it_and_a_more_cautious_one_does_not()
    {
        var body = Locomotion.Sandbox;
        Assert.Equal(new[] { "climb", "slope", "radius", "height" }, NavAgent.CookDefault.DiffersFrom(body));
        Assert.Empty(Human.DiffersFrom(body));
        Assert.Equal("", Human.Note(body));
        Assert.Empty((Human with { Radius = 0.4f, Height = 2f, Climb = 0.3f, MaxSlope = 35f }).DiffersFrom(body));
        Assert.Equal(new[] { "climb" }, (Human with { Climb = 0.4f }).DiffersFrom(body));
        Assert.Equal(new[] { "height" }, (Human with { Height = 1.7f }).DiffersFrom(body));
        var note = NavAgent.CookDefault.Note(body);
        Assert.Contains("climbs 0.4 m (the body steps 0.35 m)", note);
        Assert.Contains("walks slopes to 45° (the body to 40°)", note);
        Assert.Contains("is 0.25 m round (the body 0.3 m)", note);
        Assert.Contains("is 1.7 m tall (the body 1.8 m)", note);
        using var json = JsonDocument.Parse(Human.Json());
        Assert.Equal(0.35, json.RootElement.GetProperty("climb").GetDouble(), 3);
    }

    static LevelSpace Gate()
    {
        var path = Environment.GetEnvironmentVariable("VITOMIR_TEST_LEVEL");
        Assert.SkipUnless(path is not null && File.Exists(Path.Combine(path, "level.json")), "no level package (VITOMIR_TEST_LEVEL)");
        return LevelSpace.Open(path!);
    }

    [Fact]
    public void A_snagcheck_says_the_pace_the_profile_the_body_and_the_packages_agent_and_notes_where_they_differ()
    {
        var level = Gate();
        Assert.NotNull(level.Navigation!.Agent);
        Assert.False(level.Navigation.MixedAgents);
        var agent = level.Navigation.Agent!.Value;
        output.WriteLine($"{level.Name}: baked for {agent}");

        var report = SnagCheck.Run(level, new SnagCheckOptions
        {
            Pairs = 2, Seed = 3, Gait = Gait.Walk, Numbers = Locomotion.Sandbox, Profile = "human", Mode = "masquerade",
            Notes = ["a note of the caller's"],
        });
        foreach (var line in report.Lines) output.WriteLine(line);
        var lines = report.Lines.Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal("note", lines[0].GetProperty("kind").GetString());
        Assert.Equal("a note of the caller's", lines[0].GetProperty("text").GetString());
        var differs = agent.DiffersFrom(Locomotion.Sandbox);
        if (differs.Count > 0) Assert.StartsWith("the package's navigation was baked for an agent that", lines[1].GetProperty("text").GetString());
        Assert.Equal(1 + (differs.Count > 0 ? 1 : 0), report.Notes.Count);

        var summary = lines[^1];
        Assert.True(summary.GetProperty("summary").GetBoolean());
        Assert.Equal("walk", summary.GetProperty("pace").GetString());
        Assert.Equal("human", summary.GetProperty("profile").GetString());
        Assert.Equal("masquerade", summary.GetProperty("mode").GetString());
        Assert.Equal(0.35, summary.GetProperty("body").GetProperty("step").GetDouble(), 3);
        Assert.Equal(agent.Climb, (float)summary.GetProperty("navAgent").GetProperty("climb").GetDouble(), 3);
        Assert.Equal(differs, summary.GetProperty("navAgentDiffers").EnumerateArray().Select(e => e.GetString()!).ToList());
        Assert.Equal(report.Notes.Count, summary.GetProperty("notes").GetInt32());
        // The pair lines and the finding lines keep their fields.
        Assert.All(lines.Where(l => l.TryGetProperty("pair", out _) && !l.TryGetProperty("kind", out _)),
            l => Assert.Equal(new[] { "pair", "from", "to", "corners", "length", "arrived", "seconds" }, l.EnumerateObject().Select(p => p.Name).ToList()));
    }
}
