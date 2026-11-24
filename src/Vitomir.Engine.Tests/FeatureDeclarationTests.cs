// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Reflection;
using Vitomir.Engine.Features;
using Vitomir.Engine.Net;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The game's own thirty-two declarations (ours): that the registry holds the eleven Fixes,
/// eleven quality-of-life switches and ten repairs in the Fixes page's order; that the three registers read
/// their names, lines and defaults from it and from nowhere else — a property a declaration and a declaration a
/// property, both ways; **the registry's check on itself** (a switchable feature that changes the rules is the
/// host's, and a feature that is each machine's is presentation); that every rules feature is sent by a host,
/// and the one thing sent that is no rule is `soakedvoid`; that the only places two declarations meet are
/// the three written down (`matchedgait` refines `movespeed`; `bodycollision` tolerates `driving`; `packcells`
/// tolerates `dropcells`); and that the settings chain ends settled, with the reason said.
/// </summary>
public class FeatureDeclarationTests
{
    static FeatureRegistry Registry => GameFeatures.Registry;

    [Fact]
    public void The_registry_holds_the_three_registers_in_the_pages_order()
    {
        Assert.Equal(39, Registry.All.Count);
        Assert.Equal(["movespeed", "matchedgait", "bodycollision", "glowcolours", "shapetorch", "liverollover", "soakedvoid", "netprops", "berserker", "wakeonhit", "gunreach", "heldattack"],
                     Registry.Of(FeatureKind.Fix).Select(f => f.Name));
        // `freecamera` fourth: the settings line names the switches that are off in this order (`off: freecamera, winkey`).
        Assert.Equal(["quickequip", "driving", "camera", "freecamera", "soakpie", "keys", "alttab", "winkey", "whisperecho", "filmvolume", "relock", "skipopening", "skipstoryfilms", "dropcells", "heldshift", "spawnwalls", "partyorder"],
                     Registry.Of(FeatureKind.Qol).Select(f => f.Name));
        Assert.Equal(["droplanding", "closedround", "hostthrows", "memberstart", "keepcontrols", "packcells", "hoverpicture", "pendulumhome", "blastsight", "loadedeffects"],
                     Registry.Of(FeatureKind.Repair).Select(f => f.Name));
        Assert.Equal(Registry.All, [.. Registry.Of(FeatureKind.Fix), .. Registry.Of(FeatureKind.Qol), .. Registry.Of(FeatureKind.Repair)]);
        // No two share a name, in any register: a name alone says which switch is meant.
        Assert.Equal(39, Registry.All.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_declaration_says_where_it_acts_and_what_it_does()
    {
        Assert.All(Registry.All, f =>
        {
            Assert.True(f.Takes.Count > 0, $"{f.Id} takes no switch point");
            Assert.True(f.What.Length > 20, $"{f.Id} has no line for the page");
            Assert.Equal(FeatureWhen.AnyTime, f.When);                    // no feature is a new game's only, yet
            Assert.Empty(f.Requires);
            Assert.Empty(f.Excludes);
        });
        Assert.All(Registry.Points, p => Assert.True(p.What.Length > 10, p.Name));
        Assert.All(Registry.Data, d => Assert.True(d.What.Length > 5, d.Name));
    }

    /// <summary>
    /// **The self-check.** Whose a switch is and which side of the line it falls on are declared separately — the
    /// one by hand, the other computed from what the feature takes and changes — so each is a check on the other:
    /// a switch that changes the rules and were each machine's own would let two machines in one game play by
    /// different rules.
    /// </summary>
    [Fact]
    public void A_switchable_rules_feature_is_the_hosts_and_a_machines_feature_is_presentation()
    {
        Assert.All(Registry.All.Where(f => f.Switchable && f.Axis == FeatureAxis.Rules),
                   f => Assert.True(f.Whose == FeatureWhose.Host, $"{f.Id} changes the rules and is declared each machine's"));
        Assert.All(Registry.All.Where(f => f.Whose == FeatureWhose.Machine),
                   f => Assert.True(f.Axis == FeatureAxis.Presentation, $"{f.Id} is declared each machine's and changes the rules"));
        // The other way is not required, and is not so: a presentation feature may be the host's, when the host is
        // the machine that does what it switches. One is: `soakedvoid`, whose two scripts the host runs.
        Assert.Equal(["soakedvoid"], Registry.All.Where(f => f.Axis == FeatureAxis.Presentation && f.Whose == FeatureWhose.Host).Select(f => f.Name));
    }

    /// <summary>
    /// **Every rules feature is sent, and what is sent is the host's.** The two sets were one until `soakedvoid`
    /// was declared presentation (the white flash is purely visual; `SoakedHandlerTests` holds
    /// what makes it so): it changes no rule, so a save does not name it and it does not make a game modded — and
    /// the host still sends it, because the host runs the script that makes the flash.
    ///
    /// **What is sent and what counts are two questions.** Three quality-of-life switches are on the rules axis
    /// and the host's — the host carries them out — and none makes a game modded (<see cref="Feature.Counts"/>;
    /// the quality-of-life switches do not count towards a modded save): a save names
    /// the six gameplay Fixes and no more.
    /// </summary>
    [Fact]
    public void Every_rules_feature_is_sent_by_the_host_and_what_the_host_sends_is_the_hosts()
    {
        string[] rules = ["fixes.bodycollision", "fixes.shapetorch", "fixes.netprops", "fixes.berserker", "fixes.wakeonhit", "fixes.gunreach", "fixes.heldattack",
                          "qol.quickequip", "qol.driving", "qol.dropcells", "qol.heldshift", "qol.spawnwalls", "qol.partyorder"];
        Assert.Equal(rules, Registry.All.Where(f => f.Switchable && f.Axis == FeatureAxis.Rules).Select(f => f.Id));
        string[] counted = [.. rules.Where(id => id.StartsWith("fixes."))];
        Assert.Equal(7, counted.Length);
        Assert.Equal(counted, Registry.All.Where(f => f.Counts).Select(f => f.Id));
        Assert.Equal(counted, Registry.RulesOn(Fixes.Parse(["--fix", "all"], Nowhere, userPath: NoUserSettings), Qol.Parse(["--qol", "all"], Nowhere, userPath: NoUserSettings)));
        // No quality-of-life switch counts, whatever its axis; no repair does, since none can be switched.
        Assert.All(Registry.Of(FeatureKind.Qol), f => Assert.False(f.Counts));
        Assert.All(Registry.Of(FeatureKind.Repair), f => Assert.False(f.Counts));

        var sent = HostSwitches.Taken(new Fixes(), new Qol()).Select(s => s.Register + "." + s.Name).ToList();
        // What is sent is exactly what is declared the host's, in the registry's order …
        Assert.Equal(Registry.All.Where(f => f.Switchable && f.Whose == FeatureWhose.Host).Select(f => f.Id), sent);
        // … every rules feature is among it …
        Assert.All(rules, id => Assert.Contains(id, sent));
        // … and the one switch sent that is no rule is the flash.
        Assert.Equal(["fixes.soakedvoid"], sent.Except(rules));
        Assert.Equal(FeatureAxis.Presentation, Registry.Find("fixes", "soakedvoid")!.Axis);
        // Every repair but two changes the rules — the hover picture and an effect's looks after a load are each
        // machine's own picture; none can be switched, so none is the host's to send.
        Assert.Equal(["hoverpicture", "loadedeffects"], Registry.Of(FeatureKind.Repair).Where(f => f.Axis == FeatureAxis.Presentation).Select(f => f.Name));
        Assert.All(Registry.Of(FeatureKind.Repair).Where(f => f.Axis == FeatureAxis.Presentation), f => Assert.Equal(FeatureWhose.Machine, f.Whose));
    }

    [Fact]
    public void Whose_a_switch_is_in_a_network_game_is_what_its_declaration_says()
    {
        Assert.All(Registry.All.Where(f => f.Switchable), f =>
            Assert.Equal(f.Whose == FeatureWhose.Machine ? HostSwitches.Governor.Machine : HostSwitches.Governor.Host, HostSwitches.Of(f.Register, f.Name)));
        Assert.Equal(HostSwitches.Governor.Host, HostSwitches.Of("fixes", "not-a-switch-yet"));   // an unknown one is the host's
        Assert.Equal(HostSwitches.Governor.Host, HostSwitches.Of("fixes", "all"));
        Assert.Equal(HostSwitches.Governor.Machine, HostSwitches.Of("FIXES", "MoveSpeed"));
    }

    /// <summary>
    /// Every pair of declarations that meet — on a switch point, or on a datum one changes and the other reads —
    /// and how each is let stand. Over-declaring makes conflicts that are not there, so the list is pinned whole:
    /// a fourth meeting is a decision somebody has to make, not a side effect of a new declaration.
    /// </summary>
    [Fact]
    public void The_only_declarations_that_meet_are_the_three_written_down()
    {
        var meetings = new List<string>();
        for (int i = 0; i < Registry.All.Count; i++)
            for (int j = 0; j < i; j++)
            {
                var (a, b) = (Registry.All[i], Registry.All[j]);
                foreach (var point in a.Takes.Intersect(b.Takes)) meetings.Add($"{a.Name}+{b.Name} on {point.Name}");
                foreach (var datum in a.Changes.Intersect(b.Reads).Concat(b.Changes.Intersect(a.Reads)).Distinct()) meetings.Add($"{a.Name}+{b.Name} on {datum.Name}");
            }
        Assert.Equal(["matchedgait+movespeed on Player.RateFor", "driving+bodycollision on actor.steered", "packcells+dropcells on item.cell"], meetings);

        var matched = Registry.Find("fixes", "matchedgait")!;
        Assert.Equal("fixes.movespeed", matched.Refines);
        Assert.Equal(["qol.driving"], Registry.Find("fixes", "bodycollision")!.Tolerates);
        Assert.Equal(["qol.dropcells"], Registry.Find("repairs", "packcells")!.Tolerates);
        Assert.Equal(3, Registry.All.Count(f => f.Refines is not null || f.Tolerates.Count > 0));

        // So everything can be on at once, the defaults are allowed, and the one refusal is a refinement alone.
        Assert.Empty(Registry.Check(Registry.All));
        Assert.Empty(Registry.Check(Registry.All.Where(f => f.Default)));
        var refused = Registry.All.Where(f => f.Switchable).Select(f => Registry.Blocks(f, [])).OfType<Conflict>().ToList();
        Assert.Equal("matchedgait needs movespeed: it refines it", Assert.Single(refused).Sentence);
    }

    [Fact]
    public void Each_switch_has_one_property_and_each_property_one_declaration()
    {
        Agree<Fixes>(FeatureKind.Fix, new Fixes());
        Agree<Qol>(FeatureKind.Qol, new Qol());

        static void Agree<T>(FeatureKind kind, T fresh) where T : class
        {
            var tied = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(p => (Property: p, Tie: p.GetCustomAttribute<SwitchAttribute>())).Where(p => p.Tie is not null).ToList();
            // Both ways: every declaration of the kind has a property, and every tied property a declaration.
            Assert.Equal(Registry.Of(kind).Select(f => f.Name).Order(), tied.Select(p => p.Tie!.Name).Order());
            // And no boolean switch was left untied, to be set by hand and never listed.
            Assert.Empty(typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.PropertyType == typeof(bool) && p.GetIndexParameters().Length == 0 && p.GetCustomAttribute<SwitchAttribute>() is null).Select(p => p.Name));
            foreach (var (property, tie) in tied)
            {
                var feature = Registry.Of(kind).Single(f => f.Name == tie!.Name);
                // A fresh register is the declarations' defaults: there is no second place a default is written.
                Assert.Equal(feature.Default, (bool)property.GetValue(fresh)!);
                // The indexer, the property and `All` are one switch.
                var all = (IEnumerable<(string Name, bool On, string What)>)typeof(T).GetProperty("All")!.GetValue(fresh)!;
                var item = typeof(T).GetProperty("Item")!;
                item.SetValue(fresh, !feature.Default, [feature.Name.ToUpperInvariant()]);
                Assert.Equal(!feature.Default, (bool)property.GetValue(fresh)!);
                Assert.Equal(!feature.Default, all.Single(s => s.Name == feature.Name).On);
                Assert.Equal(feature.What, all.Single(s => s.Name == feature.Name).What);
                property.SetValue(fresh, feature.Default);
                Assert.Equal(feature.Default, (bool)item.GetValue(fresh, [feature.Name])!);
            }
        }
    }

    [Fact]
    public void The_three_registers_list_what_the_registry_declares()
    {
        Assert.Equal(Registry.Of(FeatureKind.Fix).Select(f => (f.Name, f.Default, f.What)), new Fixes().All);
        Assert.Equal(Registry.Of(FeatureKind.Qol).Select(f => (f.Name, f.Default, f.What)), new Qol().All);
        Assert.Equal(Registry.Of(FeatureKind.Repair).Select(f => (f.Name, f.What)), Repairs.All);
        // A repair's name is a constant the code site cites; each constant is a declaration and each declaration a constant.
        var constants = typeof(Repairs).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.Equal(Registry.Of(FeatureKind.Repair).Select(f => f.Name).Order(), constants.Order());

        Assert.All(Registry.All.Where(f => f.Switchable), f => Assert.True(f.Kind == FeatureKind.Fix ? Fixes.Known(f.Name) : Qol.Known(f.Name)));
        Assert.False(Fixes.Known("driving"));
        Assert.False(Qol.Known("gunreach"));
        Assert.False(Fixes.Known("droplanding"));                         // a repair is no switch
        // Every Fix is off by default and every quality-of-life switch on but two.
        Assert.All(Registry.Of(FeatureKind.Fix), f => Assert.False(f.Default));
        Assert.Equal(["freecamera", "winkey", "skipopening", "skipstoryfilms"], Registry.Of(FeatureKind.Qol).Where(f => !f.Default).Select(f => f.Name));
        Assert.All(Registry.Of(FeatureKind.Repair), f => Assert.True(f.Default && !f.Switchable));
    }

    [Fact]
    public void A_plain_run_is_faithful_and_a_gameplay_fix_makes_it_modded()
    {
        // Driving, the quick equip and the drop's cell are on by default and the host sends them, but they change
        // the way to an outcome and not the outcome: a game played as it comes is faithful, and names nothing.
        Assert.Equal("faithful", Registry.Profile(new Fixes(), new Qol()));
        Assert.Empty(Registry.RulesOn(new Fixes(), new Qol()));
        Assert.Equal("faithful rules; 10 repairs always on", Registry.Line(new Fixes(), new Qol()));
        // The log's header and the console say the same of the run's own switches (no test writes those).
        Assert.Equal(Registry.Line(Fixes.Current, Qol.Current), Diagnostics.Diag.FeaturesInForce);

        var none = Qol.Parse(["--no-qol", "all"], Nowhere, userPath: NoUserSettings);
        Assert.Equal("faithful", Registry.Profile(new Fixes(), none));
        Assert.Equal("faithful rules; 10 repairs always on", Registry.Line(new Fixes(), none));
        // One gameplay Fix, and it is modded and says by what — with the quality-of-life switches as they come or all off.
        var torch = new Fixes { ShapeTorch = true };
        Assert.Equal("modded rules (fixes.shapetorch); 10 repairs always on", Registry.Line(torch, new Qol()));
        Assert.Equal("modded rules (fixes.shapetorch); 10 repairs always on", Registry.Line(torch, none));
        // Presentation switches leave it faithful, whichever way they are set.
        var looks = new Fixes { MoveSpeed = true, MatchedGait = true, GlowColours = true, LiveRollover = true, SoakedVoid = true };
        none.Camera = none.SoakPie = none.Keys = none.WinKey = none.FreeCameraKey = true;
        Assert.Equal("faithful", Registry.Profile(looks, none));
    }

    [Fact]
    public void A_flag_takes_several_names_with_commas_between()
    {
        var complaints = new List<string>();
        var fixes = Fixes.Parse(["--fix", "gunreach,wakeonhit", "--fix", "berserker, nosuch"], Nowhere, complaints.Add, NoUserSettings);
        Assert.Equal("berserker, wakeonhit, gunreach", fixes.ToString());
        Assert.Equal("--fix nosuch: no such fix", Assert.Single(complaints));

        complaints.Clear();
        var qol = Qol.Parse(["--no-qol", "driving,camera", "--qol", "winkey,freecamera"], Nowhere, complaints.Add, NoUserSettings);
        Assert.Equal("off: driving, camera, skipopening, skipstoryfilms", qol.ToString());
        Assert.Empty(complaints);
    }

    [Fact]
    public void A_combination_the_registry_refuses_is_left_off_from_the_ini_too()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vitomir-features-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "vitomir.ini"), "[fixes]\nmatchedgait = on\ngunreach = on\n");
            var complaints = new List<string>();
            var fixes = Fixes.Parse([], dir, complaints.Add, NoUserSettings);
            Assert.False(fixes.MatchedGait);
            Assert.True(fixes.GunReach);
            Assert.Equal("[fixes] matchedgait is left off: matchedgait needs movespeed: it refines it", Assert.Single(complaints));

            // The command line supplies what the ini's switch needed: nothing to complain of.
            complaints.Clear();
            var both = Fixes.Parse(["--fix", "movespeed"], dir, complaints.Add, NoUserSettings);
            Assert.True(both.MatchedGait && both.MoveSpeed);
            Assert.Empty(complaints);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Both_registers_are_settled_together_in_place()
    {
        // A pair no reading produces — set by hand, as a replay's tape or a session's choice might — made allowed.
        var fixes = new Fixes { MatchedGait = true, GunReach = true };
        var qol = new Qol();
        var complaints = new List<string>();
        Registry.Settle(fixes, qol, complaints.Add);
        Assert.False(fixes.MatchedGait);
        Assert.True(fixes.GunReach);
        Assert.Equal(new Qol().ToString(), qol.ToString());
        Assert.Single(complaints);
        Registry.Settle(fixes, qol, complaints.Add);                       // and again: nothing more
        Assert.Single(complaints);
    }

    /// <summary>A directory with no `vitomir.ini` in it, so a test reads no machine's settings.</summary>
    static string Nowhere => Path.Combine(Path.GetTempPath(), "vitomir-no-such-settings");

    /// <summary>A player's settings file that is not there, so the machine's own cannot leak into a test.</summary>
    static string NoUserSettings => Path.Combine(Path.GetTempPath(), "vitomir-no-such-settings", "user.ini");
}
