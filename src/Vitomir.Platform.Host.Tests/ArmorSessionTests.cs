// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// Armour in a session and in the arena: <c>--armor who=piece[,piece]</c> read and refused by name, the people
/// it names dressed through the module after their sheets, a recording's head carrying it so a replay dresses them the same
/// (every digest agreeing); the arena's <c>"armor"</c> per fighter read, a fighter wearing exactly its spec's (none without it,
/// whatever its 1.1 template wears in play), the armour's columns filled, and with <c>armor-off</c> a bout in armour is the bout
/// without it, row for row. Skips without the 1.1 install or the proving ground.
/// </summary>
public sealed class ArmorSessionTests
{
    static (string Course, string Game) Grounds()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR, or C:\\Games\\VTMR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-armor-tests-userdata"));
        return (course!, game!);
    }

    static ArmorRequest Parse(string text) => ArmorRequest.TryParse(text, out var r, out string e) ? r : throw new InvalidOperationException(e);

    [Fact]
    public void An_armor_request_is_who_and_its_pieces_and_a_malformed_one_is_refused_by_name()
    {
        var r = Parse("Guard=plate_harness, full_helm");
        Assert.Equal("Guard", r.Who);
        Assert.Equal(["plate_harness", "full_helm"], r.Pieces);
        Assert.Empty(Parse("player=none").Pieces);
        Assert.Empty(Parse("player=").Pieces);
        var two = ArmorRequest.FromNote(ArmorRequest.ToNote([r, Parse("player=mail_shirt")]));
        Assert.Equal(["Guard=plate_harness, full_helm", "player=mail_shirt"], two.Select(x => x.Text));
        foreach (var bad in new[] { "mail_shirt", "=mail_shirt", "a=b;c=d" })
        {
            Assert.False(ArmorRequest.TryParse(bad, out _, out string error));
            Assert.Contains("expected <who>=<piece>", error);
        }
    }

    [Fact]
    public void A_session_dresses_whom_armor_names_its_tape_carries_it_and_a_replay_dresses_them_the_same()
    {
        var (course, game) = Grounds();
        var said = new List<string>();
        var options = new SessionOptions
        {
            Level = course, Game = game, People = 2, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" },
            Armor = [Parse("player=padded_coat,mail_shirt,light_helm")], Say = said.Add,
        };
        TapeHeader head;
        var frames = new List<ActionFrame>();
        var digests = new Dictionary<long, ulong>();
        using (var session = Session.Create(options))
        {
            var sheets = session.World.Service<IPersonSheets>();
            Assert.Equal(["padded_coat", "mail_shirt", "light_helm"], sheets.WornBy(session.Player));
            Assert.Contains(said, l => l.StartsWith("armor: Christof (e0) wears padded_coat, mail_shirt, light_helm", StringComparison.Ordinal));
            head = session.TapeHeader("test");
            Assert.Equal("player=padded_coat,mail_shirt,light_helm", head.Notes[SessionOptions.ArmorNote]);
            for (int i = 0; i < 60; i++)
            {
                var f = ActionFrame.Idle with { Move = new System.Numerics.Vector2(0, i < 30 ? 1 : 0) };
                frames.Add(f);
                session.Step(f);
                if (i % 10 == 9) digests[session.Tick] = session.Digest();
            }
        }
        var again = new SessionOptions { Level = "", Game = game }.From(head);
        Assert.Equal(["player=padded_coat,mail_shirt,light_helm"], again.Armor.Select(a => a.Text));
        using (var replay = Session.Create(again))
        {
            Assert.Equal(["padded_coat", "mail_shirt", "light_helm"], replay.World.Service<IPersonSheets>().WornBy(replay.Player));
            foreach (var f in frames)
            {
                replay.Step(f);
                if (digests.TryGetValue(replay.Tick, out ulong want)) Assert.Equal(want, replay.Digest());
            }
        }

        // A piece the module has not, or two in one slot, stops the start by name.
        var x = Assert.Throws<SessionException>(() => Session.Create(options with { Armor = [Parse("player=bucket")] }));
        Assert.Contains("'bucket' is no piece of armour", x.Message);
        x = Assert.Throws<SessionException>(() => Session.Create(options with { Armor = [Parse("player=mail_shirt,plate_harness")] }));
        Assert.Contains("one piece a slot", x.Message);
    }

    static ArenaSpec Spec(string armorB, float seconds, int bouts, string extra = "")
    {
        var (course, _) = Grounds();
        return ArenaSpec.Read($$"""
        { "fighters": [ { "name": "A", "template": "Christof", "weapon": "Rapier", "shield": false, "sheet": "sandbox.newly_embraced", "place": [38, 3], "facing": 0 },
                        { "name": "B", "template": "rudolfGuard", "weapon": "Rapier", "shield": false, "sheet": "sandbox.guard_trained", "place": [42, 3], "facing": 180 {{armorB}} } ],
          "seconds": {{seconds.ToString(CultureInfo.InvariantCulture)}}, "bouts": {{bouts}}, "tapes": 0, "seed": 3000 {{extra}} }
        """, Path.GetTempPath(), course);
    }

    static SessionOptions Baseline()
    {
        var (course, game) = Grounds();
        return new SessionOptions { Level = course, Game = game, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" } };
    }

    [Fact]
    public void The_arena_s_armor_is_read_and_a_bad_one_is_refused()
    {
        var spec = Spec(""", "armor": ["mail_shirt", "light_helm"]""", 1f, 1);
        Assert.Equal(["mail_shirt", "light_helm"], spec.Fighters[1].Armor!);
        Assert.Null(spec.Fighters[0].Armor);
        Assert.Equal(["mail_shirt", "light_helm"], spec.Cast([]).Fighters[1].Armor!);
        Assert.DoesNotContain("\"Armor\"", ArenaCast.Read(Spec("", 1f, 1).Cast([]).Write()).Write());   // none: the cast's text as before armour
        var x = Assert.Throws<ArenaSpecException>(() => Spec(""", "armor": "mail_shirt" """, 1f, 1));
        Assert.Equal(ArenaSpecError.WrongType, x.Error);
        Assert.Contains("\"armor\" is a list", x.Message);
    }

    [Fact]
    public void A_fighter_wears_exactly_its_spec_s_armour_and_armour_off_gives_the_bout_without_it()
    {
        string root = Path.Combine(Path.GetTempPath(), $"vitomir-armor-arena-{Environment.ProcessId}");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var bare = ArenaRunner.Execute(Spec("", 20f, 3), Baseline(), Path.Combine(root, "bare"), _ => { });
        var worn = ArenaRunner.Execute(Spec(""", "armor": ["mail_shirt", "light_helm"]""", 20f, 3), Baseline(), Path.Combine(root, "worn"), _ => { });
        var off = ArenaRunner.Execute(Spec(""", "armor": ["mail_shirt", "light_helm"]""", 20f, 3, """, "switches": ["armor-off"]"""), Baseline(), Path.Combine(root, "off"), _ => { });

        // Nobody in armour: no blow met any (rudolfGuard's template wears a mail shirt in play, not in the arena unless asked).
        Assert.All(bare.Bouts, b => Assert.All(b.Fighters, f => Assert.Equal((0, 0L), (f.ArmorBlows, f.ArmorDice))));
        Assert.All(bare.Contacts.Where(c => c.Harm is not null), c => Assert.Null(c.Harm!.Value.Armor));
        // In armour: the guard's covered parts met it, its gaps did not.
        var hits = worn.Contacts.Where(c => c.Harm is { } h && h.Target == c.Contact.Target && c.TargetIndex == 1).Select(c => (c.Contact.Site, c.Harm!.Value)).ToList();
        Assert.Contains(hits, h => h.Value.Armor is not null);
        Assert.All(hits, h => Assert.Equal(h.Site is "chest" or "belly" or "hips" or "left upper arm" or "right upper arm" ? "mail_shirt" : h.Site == "head" ? "light_helm" : null, h.Value.Armor));
        Assert.True(worn.Bouts.Sum(b => b.Fighters[1].ArmorKept) > 0);
        Assert.Equal(worn.Bouts.Sum(b => b.Fighters[1].ArmorBlows), hits.Count(h => h.Value.Armor is not null));
        // Armour off: the bout without armour, outcome for outcome (but the digest: what is worn stays on the sheet).
        Assert.Equal(bare.Bouts.Select(b => (b.Winner, b.Ticks, b.KillSeconds)), off.Bouts.Select(b => (b.Winner, b.Ticks, b.KillSeconds)));
        Assert.Equal(bare.Contacts.Select(c => (c.Contact, c.Harm)), off.Contacts.Select(c => (c.Contact, c.Harm)));
        var wornSpec = Spec(""", "armor": ["mail_shirt", "light_helm"]""", 20f, 3);
        var header = ArenaTables.BoutHeader(wornSpec);
        Assert.Contains("B.armor", header);
        Assert.Equal("mail_shirt+light_helm", ArenaTables.BoutRow(wornSpec, worn.Bouts[0])[header.IndexOf("B.armor")]);
        Assert.Contains("armor_kept", ArenaTables.ContactHeader(wornSpec));
        Directory.Delete(root, true);
    }
}
