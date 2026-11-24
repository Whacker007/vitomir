// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The developer console's commands (`ConsoleCommands`, read handler by handler out of
/// the console's table), each run through the console's own line against the monastery with
/// Christof: what it changes in the game and what it says, including 1.1's odd corners — a bare
/// `lightblobs` turns them off, `god 0`/`1` work silently on the party's first member, `camtest`'s
/// numbers go to the shot as shot, distance, angle, `scaleme`'s milliseconds become seconds.
/// </summary>
public class ConsoleCommandTests
{
    sealed class Rig
    {
        public required DevConsole Console { get; init; }
        public required Location Place { get; init; }
        public required Session Session { get; init; }
        public required List<string> Said { get; init; }
        public required WorldCamera Camera { get; init; }
        public float? Gamma;
        public WorldThing Me => Place.Steered!;
        public string? Last => Said.Count > 0 ? Said[^1] : null;
        public void Run(string line) => Console.Run(line);
    }

    static Rig Open(Func<bool?, string?>? logWindow = null)
    {
        var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateGame };
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var session = new Session { Here = place };
        var console = new DevConsole(menus);
        menus.DevConsole = console;
        var said = new List<string>();
        console.Posted += said.Add;
        var camera = new WorldCamera { HeadOf = null };
        camera.Start(place.Steered);
        Rig? rig = null;
        ConsoleCommands.Register(console, new ConsoleCommands.Game
        {
            Session = session, Here = () => place, Menus = menus, Camera = () => camera, Text = text,
            Gamma = g => rig!.Gamma = g, LogWindow = logWindow,
        });
        rig = new Rig { Console = console, Place = place, Session = session, Said = said, Camera = camera };
        return rig;
    }

    /// <summary>
    /// `shapeshift wolf_grey` puts the steered Christof in the wolf's shape (the shape change, no
    /// flags, not forced), a second name over it is refused, and a bare `shapeshift` gives him back his
    /// own.
    /// </summary>
    [Fact]
    public void Shapeshift_takes_a_shape_and_a_bare_one_gives_the_own_back()
    {
        var rig = Open();
        string own = rig.Me.Model;
        rig.Run("shapeshift wolf_grey");
        Assert.Equal("wolf_grey", rig.Me.Actor!.ShapeChange?.Template, ignoreCase: true);
        Assert.NotEqual(own, rig.Me.Model);
        rig.Run("shapeshift skeleton_0");                       // not forced: refused over the wolf
        Assert.Equal("wolf_grey", rig.Me.Actor!.ShapeChange?.Template, ignoreCase: true);
        rig.Run("shapeshift");
        Assert.Null(rig.Me.Actor!.ShapeChange);
        Assert.Equal(own, rig.Me.Model);
    }

    [Fact]
    public void God_2_and_3_set_and_clear_the_current_control_and_say_so()
    {
        var r = Open();
        r.Run("god 2");
        Assert.True(r.Me.Actor!.Has(Actor.Flag.Invulnerable));
        Assert.EndsWith("is now invulnerable.", r.Last);
        r.Run("god 3");
        Assert.False(r.Me.Actor!.Has(Actor.Flag.Invulnerable));
        Assert.EndsWith("is now vulnerable.", r.Last);
    }

    /// <summary>**`god 1` and `god 0` are silent** and work on the party's first member.</summary>
    [Fact]
    public void God_1_is_silent_and_a_bare_god_does_nothing()
    {
        var r = Open();
        r.Run("god");
        Assert.False(r.Me.Actor!.Has(Actor.Flag.Invulnerable));
        r.Run("god 1");
        Assert.Empty(r.Said);
        Assert.True(r.Place.PartyMembers().First().Actor!.Has(Actor.Flag.Invulnerable));
        r.Run("god 0");
        Assert.False(r.Place.PartyMembers().First().Actor!.Has(Actor.Flag.Invulnerable));
    }

    /// <summary>**A bare `lightblobs` turns them off**: only a word other than "off" turns them on.</summary>
    [Fact]
    public void A_bare_lightblobs_turns_the_blobs_off()
    {
        var r = Open();
        r.Run("lightblobs yes");
        Assert.False(r.Place.Lights.BlobsOff);
        Assert.Equal("Lightblobs enabled", r.Last);
        r.Run("lightblobs");
        Assert.True(r.Place.Lights.BlobsOff);
        Assert.Equal("Lightblobs disabled.", r.Last);
    }

    /// <summary>
    /// **`shadow` and `numshadows` set the live shadow settings**:
    /// the five in the printout's order (mode, detail, height,
    /// full, farthest), a mode past 5 refused, and at most ten shadows a thing.
    /// </summary>
    [Fact]
    public void Shadow_and_numshadows_set_the_live_shadow_settings()
    {
        var r = Open();
        try
        {
            r.Run("shadow 2 0.15 2 1000 2000");
            Assert.Equal("Shadow type 2: Rough texture", r.Said[^2]);
            Assert.Equal("Shadow detail: 2 0.15 2.00 1000.00 2000.00", r.Last);
            var live = ShadowConsole.Live();
            Assert.Equal((2, 0.15f, 2f, 1000f, 2000f), (live.Mode, live.Detail, live.Height, live.FullDist, live.MaxDist));
            r.Run("shadow 6");
            Assert.Equal("Invalid shadow detail 6.", r.Last);
            r.Run("numshadows 12");
            Assert.Equal("Max object shadows set to: 10.", r.Last);
            r.Run("numshadows");
            Assert.Equal("Current # shadows per object: 10", r.Last);
        }
        finally { ShadowConsole.Detail = null; ShadowConsole.PerThing = null; }
    }

    [Fact]
    public void Particles_answers_switches_and_ignores_anything_else()
    {
        var r = Open();
        r.Run("particles");
        Assert.Equal("Particles are enabled.", r.Last);
        r.Run("particles 0");
        Assert.True(r.Place.Particles.Off);
        Assert.Equal("Particles disabled.", r.Last);
        int before = r.Said.Count;
        r.Run("particles perhaps");
        Assert.Equal(before, r.Said.Count);
        r.Run("particles on");
        Assert.False(r.Place.Particles.Off);
    }

    [Fact]
    public void Damageme_takes_its_amount_unsoaked_and_killme_and_revive_kill_and_raise()
    {
        var r = Open();
        var me = r.Me.Actor!;
        float before = me.Health;
        r.Run("damageme 30");
        Assert.Equal(before - 30f, me.Health, 2);
        r.Run("damageme lots");
        Assert.Equal("Usage: damageme <amount> [<damageType>]", r.Last);
        r.Run("killme");
        Assert.True(me.Dead);
        r.Run("revive");
        Assert.False(me.Dead);
        Assert.Equal(me.MaxHealth, me.Health, 2);
    }

    [Fact]
    public void Xp_gives_a_thousand_unless_told_otherwise()
    {
        var r = Open();
        var me = r.Me.Actor!;
        long xp = me.Experience;
        r.Run("xp");
        Assert.Equal(xp + 1000, me.Experience);
        r.Run("xp 250");
        Assert.Equal(xp + 1250, me.Experience);
    }

    [Fact]
    public void Addthing_makes_each_template_where_the_player_stands()
    {
        var r = Open();
        int count = r.Place.Things.Count;
        r.Run("addthing GoldBag nosuchthing");
        Assert.Equal("GoldBag added at current player's position.", r.Said[0]);
        Assert.Equal("Couldn't find template nosuchthing.", r.Said[1]);
        var made = r.Place.Things[^1];
        Assert.Equal(count + 1, r.Place.Things.Count);
        Assert.Equal(r.Me.Position, made.Position);
    }

    /// <summary>`adddiscipline` sets level 1 (the discipline is set at 1) and says "level 2", counting from one.</summary>
    [Fact]
    public void Adddiscipline_sets_level_one_and_calls_it_level_two()
    {
        var r = Open();
        r.Run("adddiscipline Feed");
        Assert.Equal("Discipline 'Feed' added and set to level 2 for current player", r.Last);
        var feed = r.Place.Powers!["Feed"]!;
        Assert.Equal(1, r.Me.Actor!.Disciplines.Of(feed)!.Level);
        r.Run("adddiscipline Nonsense");
        Assert.Equal("Couldn't add discipline Discipline 'Nonsense' to current player", r.Last);
    }

    [Fact]
    public void Speed_puts_the_speed_cheat_on_the_party_a_level_below_the_number()
    {
        var r = Open();
        r.Run("speed 3");
        Assert.Equal("Speed cheat set to level 3.", r.Last);
        Assert.Contains(r.Me.Effects.All, e => e.Name == "ef_speedcheat" && e.Level == 2);
        r.Run("speed 0");
        Assert.Equal("Speed cheat disabled.", r.Last);
        Assert.DoesNotContain(r.Me.Effects.All, e => e.Name == "ef_speedcheat" && !e.Spent);
    }

    /// <summary>**`camtest a b c` frames shot a at distance b and angle c** (the arguments go to the camera's shot in that order).</summary>
    [Fact]
    public void Camtest_frames_the_player_with_shot_distance_angle_in_that_order()
    {
        var r = Open();
        ConsoleCommands.CamTestSide = 0;
        r.Run("camtest side");
        Assert.Equal("Camera side is now: 1", r.Last);
        r.Run("camtest 6 2 1");
        Assert.Equal(CameraMode.Cutscene, r.Camera.Mode);
        Assert.Equal((6, 1, 2, 1), r.Camera.LastShot);
        ConsoleCommands.CamTestSide = 0;
    }

    [Fact]
    public void Free_ammo_and_no_item_stat_checks_are_the_engine_s_own_switches()
    {
        var r = Open();
        try
        {
            r.Run("idkfa");
            Assert.False(Weapon.FreeAmmo);                 // bare: off
            r.Run("idkfa please");
            Assert.True(Weapon.FreeAmmo);
            Assert.Equal("Free ammo on.", r.Last);
            r.Run("noitemstatchecks on");
            Assert.True(Equipment.NoStatChecks);
            r.Run("freecasting on");
            Assert.Equal(Cast.FreeBlood | Cast.FreeChecks, Cast.FreeCasting);
        }
        finally
        {
            Weapon.FreeAmmo = false;
            Equipment.NoStatChecks = false;
            Cast.FreeCasting = 0;
        }
    }

    [Fact]
    public void The_chronicle_flag_number_is_held_to_0x400_and_a_missing_one_is_asked_for()
    {
        var files = Install.Open();
        var r = Open();
        r.Session.Chronicle = Chronicle.Load(files, ChronicleSet.Load(files).All[0].Name);
        r.Run("setchronflag 5000");
        Assert.Equal("Chron flag #1024 is now SET.", r.Last);
        r.Run("getchronflag");
        Assert.Equal("You must pass a chronicle flag number to use this command.", r.Last);
    }

    [Fact]
    public void Echo_prints_and_clears_the_message_box()
    {
        var r = Open();
        r.Run("echo hello there");
        Assert.Equal("hello there", r.Last);
        r.Session.Messages.Add("something");
        r.Run("echo *CLS");
        Assert.Empty(r.Session.Messages.Lines);
    }

    [Fact]
    public void Whereis_says_the_floor_and_the_position_with_the_sector()
    {
        var r = Open();
        r.Run("whereis");
        Assert.StartsWith("Floor ", r.Said[0]);
        Assert.EndsWith($"template {r.Me.Template}", r.Said[0]);
        Assert.StartsWith("Pos (", r.Said[1]);
        Assert.EndsWith($"sector {r.Me.Sector}", r.Said[1]);
    }

    [Fact]
    public void Team_shows_both_teams_and_sets_one_up_to_nineteen()
    {
        var r = Open();
        r.Run("team 40");
        Assert.Equal(19, r.Me.Actor!.Team);
        r.Run("team");
        Assert.Equal($"Current team: 19 (original team {r.Me.Actor!.OriginalTeam})", r.Last);
    }

    [Fact]
    public void A_bare_gamma_is_one()
    {
        var r = Open();
        r.Run("gamma 1.5");
        Assert.Equal(1.5f, r.Gamma);
        r.Run("gamma");
        Assert.Equal(1f, r.Gamma);
    }

    [Fact]
    public void Stakeme_stakes_and_unstakes()
    {
        var r = Open();
        r.Run("stakeme");
        Assert.True(r.Me.Actor!.Has(Actor.Flag.Staked));
        r.Run("stakeme");
        Assert.False(r.Me.Actor!.Has(Actor.Flag.Staked));
    }

    [Fact]
    public void Emit_makes_a_test_emitter_at_the_player()
    {
        var r = Open();
        int count = r.Place.Things.Count;
        r.Run("emit");
        Assert.Equal(count + 1, r.Place.Things.Count);
        Assert.Equal(ConsoleCommands.TestEmitter, r.Place.Things[^1].Template, ignoreCase: true);
    }

    /// <summary>`maxfps f` keeps `round(1000 / f)` milliseconds a frame and says the rate that comes to, to six places.</summary>
    [Fact]
    public void Maxfps_rounds_to_whole_milliseconds_and_a_bare_one_is_off()
    {
        var r = Open();
        try
        {
            r.Run("maxfps 30");
            Assert.Equal(33, ConsoleCommands.FrameCapMs);
            Assert.Equal("Framerate Cap: ON (30.303030 FPS)", r.Last);
            r.Run("maxfps");
            Assert.Equal(0, ConsoleCommands.FrameCapMs);
            Assert.Equal("Framerate Cap: OFF", r.Last);
        }
        finally { ConsoleCommands.FrameCapMs = 0; }
    }

    /// <summary>`embraceme` sets ACTOR_TYPE to 0 and nothing else.</summary>
    [Fact]
    public void Embraceme_makes_the_player_a_vampire_by_type_alone()
    {
        var r = Open();
        r.Me.Actor!.Type = ActorType.Human;
        r.Run("embraceme");
        Assert.Equal(ActorType.Vampire, r.Me.Actor!.Type);
    }

    /// <summary>
    /// **The blow's numbers**: "%A damages %T" and the sum — the rolled damage over the
    /// multiplier, the multiplier, what came off, what was taken — with 1.1's unmatched bracket.
    /// </summary>
    [Fact]
    public void Combat_messages_print_the_blow_s_sum()
    {
        var r = Open();
        var victim = r.Place.Things.First(t => t.Type == ThingType.Actor && t.Actor is not null && !ReferenceEquals(t, r.Me));
        var line = CombatMessages.Line(r.Me, victim, 20f, 14f, 2f)!;
        Assert.EndsWith(" (10.0 * 2) - 6 = 14)", line);
        Assert.Contains("damages", line);
        Assert.EndsWith(" (20) - 6 = 14)", CombatMessages.Line(r.Me, victim, 20f, 14f, 0f));
        Assert.Null(CombatMessages.Line(r.Me, victim, 0f, 0f, 0f));
        r.Run("combatmessages yes");
        try
        {
            Assert.True(CombatMessages.On);
            // In cyan (colour 0x0000FFFF; the blit forces the alpha bit).
            r.Place.Session = r.Session;
            CombatMessages.Blow(r.Me, victim, 20f, 14f, 2f);
            Assert.Equal(new Vitomir.Engine.Ui.Message(line, 0xFF00FFFFu), r.Session.Messages.Lines[^1]);
        }
        finally { CombatMessages.On = false; }
    }

    /// <summary>`totals` counts the monks' experience (not Christof's own, a friend) and says the three sums.</summary>
    [Fact]
    public void Totals_says_what_the_place_is_worth()
    {
        var r = Open();
        r.Run("totals");
        Assert.StartsWith("Total XP value in scene : ", r.Said[^3]);
        Assert.StartsWith("Total $$ value in scene : ", r.Said[^2]);
        Assert.StartsWith("Total item val in scene : ", r.Said[^1]);
        int xp = int.Parse(r.Said[^3].Split(':')[1]);
        int expected = r.Place.Things.Where(t => t.InWorld && t.Type is ThingType.Actor or ThingType.Player && t.Actor is { } a
                                                && !a.Has(Actor.Flag.Invulnerable) && !Sight.Friendly(r.Me, t))
                                     .Sum(t => t.Actor!.Xp);
        Assert.Equal(expected, xp);
    }

    /// <summary>`cash` is the party's purse for Christof, a player-type party member.</summary>
    [Fact]
    public void Cash_shows_and_sets_the_party_purse()
    {
        var r = Open();
        r.Session.Gold = 12;
        r.Run("cash");
        Assert.Equal("Current Cash: 12", r.Last);
        r.Run("cash 500");
        Assert.Equal(500, r.Session.Gold);
    }

    /// <summary>A render or driver knob 1.1 has and Vitomir does not answers as much — never "Unrecognized".</summary>
    [Fact]
    public void A_knob_vitomir_does_not_have_says_so()
    {
        var r = Open();
        r.Run("soundcache 4000");
        Assert.Contains("Vitomir has no such setting", r.Last);
        Assert.All(DevConsole.OriginalCommands, n => Assert.True(r.Console.Commands.ContainsKey(n), $"{n} has no handler"));
    }

    /// <summary>
    /// **`logwindow`** (ours): bare, the host's log window toggled; <c>on</c> or 1 opens it, <c>off</c> or 0 closes it, in
    /// any case; the answer is the host's own line (the one its log gets); anything else says how it is used and asks nothing.
    /// </summary>
    [Fact]
    public void Logwindow_toggles_or_sets_the_host_s_log_window_and_answers_its_line()
    {
        var asked = new List<bool?>();
        bool open = false;
        string Host(bool? on)
        {
            asked.Add(on);
            open = on ?? !open;
            return open ? "log window: opened (Vitomir log: classic), a viewer of its own (process 42): closing it ends the viewer, never the game"
                        : "log window: closed";
        }
        var r = Open(Host);
        r.Run("logwindow");
        Assert.StartsWith("log window: opened (Vitomir log: classic)", r.Last);
        r.Run("logwindow");
        Assert.Equal("log window: closed", r.Last);
        r.Run("logwindow ON");
        Assert.StartsWith("log window: opened", r.Last);
        r.Run("LogWindow off");
        Assert.Equal("log window: closed", r.Last);
        r.Run("logwindow 1");
        r.Run("logwindow 0");
        Assert.Equal([null, null, true, false, true, false], asked);
        r.Run("logwindow maybe");
        Assert.Equal("logwindow: on, off, or nothing to toggle it", r.Last);
        Assert.Equal(6, asked.Count);
        // A game whose host has no log window says so.
        var none = Open();
        none.Run("logwindow on");
        Assert.StartsWith("log window: not here", none.Last);
    }

    /// <summary>`logwindow`'s reading of its word, with no game behind it (any OS, no install).</summary>
    [Theory]
    [InlineData("", null)]
    [InlineData("on", true)]
    [InlineData("On", true)]
    [InlineData("1", true)]
    [InlineData("off", false)]
    [InlineData("OFF", false)]
    [InlineData("0", false)]
    public void Logwindow_reads_on_off_or_nothing(string word, bool? expected)
    {
        bool? got = default;
        bool called = false;
        string said = ConsoleCommands.LogWindowCommand(word.Length == 0 ? [] : [word], on => { called = true; got = on; return "the host's line"; });
        Assert.True(called);
        Assert.Equal(expected, got);
        Assert.Equal("the host's line", said);
        Assert.Equal("logwindow: on, off, or nothing to toggle it", ConsoleCommands.LogWindowCommand(["sideways"], _ => throw new InvalidOperationException()));
        Assert.StartsWith("log window: not here", ConsoleCommands.LogWindowCommand([], null));
    }
}
