// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Features;
using Vitomir.Engine.Save;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What a save records of the switches it was played with, and what a load does about it
/// (ours): the header's `profile` and `rules` are written from the feature
/// registry; a save from before says nothing and a load of it says nothing; a load names each rules feature the
/// save and the run differ on and leaves the player's own switches standing — but a feature chosen at a new
/// game only is put in force as the save has it (no real feature is one yet, so that path is walked with an
/// invented registry); and a name this build does not know is said and otherwise ignored.
/// </summary>
public class SaveSwitchesTests
{
    static (Location Place, Session Session) Start(Vfs.GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var chronicle = Chronicle.Load(files, "Prague");
        chronicle.Current = chronicle.Find("OldTownDay");
        var session = new Session { Here = place, Chronicle = chronicle };
        place.Session = session;
        return (place, session);
    }

    [Fact]
    public void A_save_says_what_it_was_played_with()
    {
        using var files = Install.Open();
        var (place, session) = Start(files);

        // A plain run is faithful: the quality-of-life switches on by default do not count
        // ("i think the QoL things should not count towards a modded save"), and a save does not name them.
        var plain = SaveCapture.Take(place, session, fixes: new Fixes(), qol: new Qol());
        Assert.Equal("faithful", plain.Header.Profile);
        Assert.Empty(plain.Header.Rules);

        // The gameplay Fixes by name, in the registry's order; a Fix that is presentation and a quality-of-life
        // switch are not the save's business, whichever way they are set.
        var some = SaveCapture.Take(place, session, fixes: new Fixes { GunReach = true, GlowColours = true, WakeOnHit = true, SoakedVoid = true }, qol: new Qol { Driving = false, Camera = false });
        Assert.Equal("modded", some.Header.Profile);
        Assert.Equal(["fixes.wakeonhit", "fixes.gunreach"], some.Header.Rules);

        var none = SaveCapture.Take(place, session, fixes: new Fixes { MoveSpeed = true }, qol: new Qol { QuickEquip = false, Driving = false, DropCells = false });
        Assert.Equal("faithful", none.Header.Profile);
        Assert.Empty(none.Header.Rules);

        // Not given, the place's own Fixes are the ones written (and the run's quality-of-life switches).
        place.Fixes = new Fixes { Berserker = true };
        Assert.Contains("fixes.berserker", SaveCapture.Take(place, session).Header.Rules);

        // And it goes through the file: written, read, the same.
        string zip = Path.Combine(Path.GetTempPath(), $"vitomir-save-{Guid.NewGuid():n}{SaveArchive.Extension}");
        try
        {
            SaveArchive.Write(some, zip);
            var header = SaveArchive.ReadHeader(zip);
            Assert.Equal("modded", header.Profile);
            Assert.Equal(some.Header.Rules, header.Rules);
            Assert.Equal(some.Header.Rules, SaveArchive.Read(zip).Header.Rules);
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void A_save_from_before_reads_as_not_saying_and_a_load_of_it_says_nothing()
    {
        // The header as a build before this one wrote it: no `profile`, and `rules` written empty.
        string text = Jsonc.Write(new SaveHeader { Description = "The Convent (00:24:25)", Chronicle = "Prague", Location = "ConventDay" }, "an older save");
        string older = string.Join('\n', text.Split('\n').Where(line => !line.Contains("profile", StringComparison.OrdinalIgnoreCase)));
        Assert.NotEqual(text, older);
        var header = Jsonc.Read<SaveHeader>(older);
        Assert.Equal("", header.Profile);
        Assert.Empty(header.Rules);
        Assert.Equal("The Convent (00:24:25)", header.Description);

        var lines = new List<string>();
        var fixes = new Fixes { GunReach = true };
        Assert.Empty(LoadSwitches.Report(header, fixes, new Qol(), lines.Add));
        Assert.Empty(lines);
        Assert.True(fixes.GunReach);
    }

    /// <summary>
    /// **A save says `changed`** where today's says `modded` (<i>"I think all we need is
    /// 'faithful' vs 'modded'."</i>), and its `rules` may name `fixes.soakedvoid`, which was a rules feature for
    /// that one build. The word is read as `modded`; the name the registry no longer counts as a rule is passed over
    /// without a word, on whichever side it is; and the differences that are rules are still said.
    ///
    /// **And its word is that build's, not today's.** Two earlier builds counted the quality-of-life switches, so
    /// this save — a game played as it came, with one visual Fix — says `modded` and names four switches, none of
    /// which counts now. Read by its names (<see cref="FeatureRegistry.ProfileOf"/>) it is faithful; and a load of
    /// it with the quality-of-life switches set another way says nothing, since they are the player's own.
    /// </summary>
    [Fact]
    public void A_save_of_an_earlier_build_that_says_changed_is_read_as_modded()
    {
        string written = Jsonc.Write(new SaveHeader { Description = "The Convent (00:24:25)", Rules = ["fixes.soakedvoid", "qol.quickequip", "qol.driving", "qol.dropcells"] }, "a an earlier build's save")
            .Replace("\"profile\": \"\"", "\"profile\": \"changed\"");
        Assert.Contains("\"changed\"", written);
        var header = Jsonc.Read<SaveHeader>(written);
        Assert.Equal("modded", header.Profile);
        Assert.Equal(FeatureRegistry.Modded, FeatureRegistry.ProfileRead("changed"));
        Assert.Equal("faithful", FeatureRegistry.ProfileRead("faithful"));
        Assert.Equal("", FeatureRegistry.ProfileRead(""));
        Assert.Equal("", FeatureRegistry.ProfileRead(null));
        // Written again it says `modded`.
        Assert.Contains("\"profile\": \"modded\"", Jsonc.Write(header, "the same, written today"));

        var lines = new List<string>();
        Assert.Empty(LoadSwitches.Report(header, new Fixes(), new Qol(), lines.Add));                       // soakedvoid off here: no rule differs
        Assert.Empty(LoadSwitches.Report(header, new Fixes { SoakedVoid = true }, new Qol(), lines.Add));
        Assert.Empty(LoadSwitches.Report(header, new Fixes(), new Qol { QuickEquip = false, Driving = false, DropCells = false }, lines.Add));
        Assert.Empty(lines);
        LoadSwitches.Report(header, new Fixes { GunReach = true }, new Qol(), lines.Add);
        Assert.Equal(["load: the save was played with gunreach off; it is on here"], lines);

        var registry = GameFeatures.Registry;
        Assert.Equal("faithful", registry.ProfileOf(header.Profile, header.Rules));
        Assert.Equal("faithful", registry.ProfileOf("modded", ["qol.quickequip", "qol.driving", "qol.dropcells"]));   // a plain game
        Assert.Equal("faithful", registry.ProfileOf("faithful", []));
        Assert.Equal("modded", registry.ProfileOf("modded", ["fixes.shapetorch", "qol.driving"]));
        Assert.Equal("modded", registry.ProfileOf("modded", ["fixes.betterdoors"]));   // a name this build does not know may be a rule
        Assert.Equal("", registry.ProfileOf("", []));                                  // a save that does not say
        Assert.Equal("", registry.ProfileOf(null, []));
    }

    [Fact]
    public void A_load_names_each_difference_and_the_players_own_switches_stand()
    {
        var header = new SaveHeader { Profile = "modded", Rules = ["fixes.gunreach", "qol.quickequip", "qol.driving", "qol.dropcells"] };
        var fixes = new Fixes { WakeOnHit = true, GlowColours = true };
        var qol = new Qol { Driving = false, Camera = false };
        var lines = new List<string>();
        var found = LoadSwitches.Report(header, fixes, qol, lines.Add);
        // Driving is off here and on in the save (an older save, which named it): the player's own, and not said.
        Assert.Equal(["load: the save was played with wakeonhit off; it is on here",
                      "load: the save was played with gunreach on; it is off here"], lines);
        // The shape the dialog will want: the feature, what the save has, what is in force, whether it is forced.
        Assert.Equal([("fixes.wakeonhit", false, true, false), ("fixes.gunreach", true, false, false)],
                     found.Select(d => (d.Id, d.InSave, d.InForce, d.Forced)));
        Assert.All(found, d => Assert.NotNull(d.Feature));
        // Reported, not acted on.
        Assert.Equal("glowcolours, wakeonhit", fixes.ToString());
        Assert.Equal("off: driving, camera, freecamera, winkey, skipopening, skipstoryfilms", qol.ToString());

        // The save and the run agree: nothing to say.
        lines.Clear();
        Assert.Empty(LoadSwitches.Report(new SaveHeader { Profile = "modded", Rules = ["fixes.wakeonhit"] }, fixes, qol, lines.Add));
        Assert.Empty(lines);
    }

    /// <summary>
    /// No feature is a new game's only yet, so the path is walked with a registry that says one is: `gunreach`
    /// there is chosen at a new game, and `berserker` decides the same thing (invented, both).
    /// </summary>
    [Fact]
    public void A_feature_chosen_at_a_new_game_only_is_put_in_force_as_the_save_has_it()
    {
        var point = new SwitchPoint("Invented.Point", "something invented", Rules: true);
        var registry = new FeatureRegistry(
        [
            new Feature(FeatureKind.Fix, "gunreach", "chosen at a new game") { Takes = [point], When = FeatureWhen.NewGame },
            new Feature(FeatureKind.Fix, "berserker", "decides the same thing") { Takes = [point] },
            new Feature(FeatureKind.Qol, "driving", "a quality-of-life switch the host carries out") { Default = true, Takes = [new SwitchPoint("Invented.Other", "something else", true)] },
        ]);

        // The save has it on, the run off — and the run has on a switch that cannot stand beside it.
        var fixes = new Fixes { Berserker = true };
        var qol = new Qol();
        var lines = new List<string>();
        var found = LoadSwitches.Report(new SaveHeader { Profile = "modded", Rules = ["fixes.gunreach", "qol.driving"] }, fixes, qol, lines.Add, registry);
        Assert.True(fixes.GunReach);                                      // forced on
        Assert.False(fixes.Berserker);                                    // and what conflicts with it gone
        Assert.True(qol.Driving);
        Assert.Equal(["load: the save was played with gunreach on, and it is chosen at a new game only: it is on for this game",
                      "load: the save was played with berserker off; it is on here",
                      "load: [fixes] berserker is left off: berserker and gunreach both decide something invented: only one can be on"], lines);
        Assert.True(found[0].Forced);
        Assert.False(found[1].Forced);

        // The save has it off, the run on: off it goes, and an ordinary difference is still only said.
        var mine = new Fixes { GunReach = true };
        var driving = new Qol { Driving = false };
        lines.Clear();
        LoadSwitches.Report(new SaveHeader { Profile = "modded", Rules = ["qol.driving"] }, mine, driving, lines.Add, registry);
        Assert.False(mine.GunReach);
        Assert.False(driving.Driving);                                    // the player's own: not the save's to say or set
        Assert.Equal(["load: the save was played with gunreach off, and it is chosen at a new game only: it is off for this game"], lines);
    }

    [Fact]
    public void A_feature_the_save_names_that_this_build_does_not_have_is_said_and_otherwise_ignored()
    {
        var fixes = new Fixes();
        var qol = new Qol();
        var lines = new List<string>();
        var found = LoadSwitches.Report(new SaveHeader { Profile = "modded", Rules = ["qol.quickequip", "qol.driving", "qol.dropcells", "fixes.betterdoors"] }, fixes, qol, lines.Add);
        Assert.Equal(["load: the save was played with fixes.betterdoors on, which this build does not have"], lines);
        Assert.Null(Assert.Single(found).Feature);
        Assert.Equal(new Fixes().ToString(), fixes.ToString());
        Assert.Equal(new Qol().ToString(), qol.ToString());
    }
}
