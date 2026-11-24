// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Control;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The powers in a session: <c>--learn [who@]power=rank[:options]</c> read and refused by name, the player
/// given Celerity at a rank with the game's options when none are named and another person by name; a slot pressed pays from
/// the player's Blood; the event trace writes <c>PowerUsed</c> with its live window and its head the learned powers and the
/// sheets' Blood; the combat trace has the ledger's entries; a refused option stops the start by name. Skips without the 1.1
/// install or the proving ground.
/// </summary>
public sealed class PowerSessionTests
{
    static (string Course, string Game) Grounds()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR, or C:\\Games\\VTMR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-power-tests-userdata"));
        return (course!, game!);
    }

    [Theory]
    [InlineData("celerity=3", "player", "celerity", 3, "")]
    [InlineData("celerity=2:celerity.active", "player", "celerity", 2, "celerity.active")]
    [InlineData("Night Watchman@fortitude=1", "Night Watchman", "fortitude", 1, "")]
    [InlineData("e3@celerity=0", "e3", "celerity", 0, "")]
    [InlineData("Guard@celerity=1:celerity.active, celerity.other", "Guard", "celerity", 1, "celerity.active,celerity.other")]
    public void A_learn_request_reads_who_the_power_the_rank_and_the_options(string text, string who, string power, int rank, string options)
    {
        Assert.True(LearnRequest.TryParse(text, out var r, out string error), error);
        Assert.Equal((who, power, rank, options), (r.Who, r.Power, r.Rank, string.Join(",", r.Options)));
        Assert.Equal(r.Text, LearnRequest.FromNote(LearnRequest.ToNote([r]))[0].Text);
    }

    [Theory]
    [InlineData("celerity")]
    [InlineData("=3")]
    [InlineData("celerity=x")]
    [InlineData("celerity=-1")]
    [InlineData("@celerity=1")]
    public void A_malformed_learn_request_is_refused_with_its_form(string text)
    {
        Assert.False(LearnRequest.TryParse(text, out _, out string error));
        Assert.Contains("expected [<who>@]<power>=<rank>", error);
    }

    [Fact]
    public void A_session_learns_by_name_pays_from_the_player_s_Blood_and_traces_the_use_and_the_ledger()
    {
        var (course, game) = Grounds();
        string dir = Directory.CreateTempSubdirectory("vitomir-power-session-").FullName;
        string events = Path.Combine(dir, "events.jsonl"), harm = Path.Combine(dir, "harm.jsonl");
        var said = new List<string>();
        var options = new SessionOptions
        {
            Level = course, Game = game, People = 2, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" },
            Learn = [Parse("celerity=2"), Parse("Night Watchman@fortitude=1")], EventTrace = events, CombatTrace = harm, Say = said.Add,
        };
        long start;
        using (var session = Session.Create(options))
        {
            var sheets = session.World.Service<IPersonSheets>();
            Assert.True(sheets.TryRead(session.Player, "celerity", out int rank));
            Assert.Equal(2, rank);
            Assert.Contains(said, l => l.StartsWith("learn: Christof (e0) celerity 2 with celerity.active", StringComparison.Ordinal));
            Assert.Contains(said, l => l.Contains("fortitude 1 with fortitude.passive", StringComparison.Ordinal));
            session.Step(ActionFrame.Idle);
            start = session.Tick;
            session.Step(ActionFrame.Idle with { Held = Buttons.Slot1 });   // the slot's key, through the player's control
            Assert.True(sheets.TryRead(session.Player, "blood", out int blood));
            Assert.Equal(70, blood);
            Assert.Equal(1.7320508f, session.World.Get<PaceScale>(session.Player).Factor, 5);   // the split's tempo, (1 + 2)^0.5
            for (int i = 0; i < 10; i++) session.Step(ActionFrame.Idle);
        }
        var lines = File.ReadAllLines(events).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        var head = lines[0];
        Assert.Equal("celerity=2;Night Watchman@fortitude=1", head.GetProperty("notes").GetProperty("learn").GetString());
        var player = head.GetProperty("people")[0].GetProperty("sheet").GetProperty("sheet");
        Assert.Equal(100, player.GetProperty("purchased").GetProperty("blood_pool").GetInt32());
        Assert.Contains(player.GetProperty("resources").EnumerateArray(), r => r.GetProperty("id").GetString() == "blood" && r.GetProperty("current").GetInt32() == 100);
        var used = Assert.Single(lines, l => l.GetProperty("kind").GetString() == "PowerUsed").GetProperty("event");
        Assert.Equal(("celerity.active", 2, 1, 30, 100, 70), (used.GetProperty("Power").GetString(), used.GetProperty("Rank").GetInt32(), used.GetProperty("Slot").GetInt32(),
            used.GetProperty("Cost").GetInt32(), used.GetProperty("BloodBefore").GetInt32(), used.GetProperty("BloodAfter").GetInt32()));
        Assert.Equal((start, start + 30 * 120), (used.GetProperty("Tick").GetInt64(), used.GetProperty("EndTick").GetInt64()));
        var ledger = File.ReadAllLines(harm).Select(l => JsonDocument.Parse(l).RootElement).Where(l => l.GetProperty("schema").GetString() == "VITOMIR_ENHANCED_BLOOD_LEDGER").ToList();
        Assert.Equal(["reserve", "commit"], ledger.Select(l => l.GetProperty("entry").GetString()));
        Assert.Equal((100, 70), (ledger[1].GetProperty("blood_before").GetInt32(), ledger[1].GetProperty("blood_after").GetInt32()));

        // A refused option stops the start by name.
        var bad = options with { Learn = [Parse("celerity=1:potence.passive")], EventTrace = null, CombatTrace = null };
        var x = Assert.Throws<SessionException>(() => Session.Create(bad));
        Assert.Contains("option 'potence.passive' is Potence's, not celerity's", x.Message);
        Directory.Delete(dir, recursive: true);
    }

    static LearnRequest Parse(string text) => LearnRequest.TryParse(text, out var r, out string e) ? r : throw new InvalidOperationException(e);
}
