// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// Squeezing, stealth with cover and ducking as the program gives them: the settings
/// (<c>stealthOff</c> / <c>--stealth-off</c>, <c>stealthPace</c>, <c>stealthCrouchPace</c>, <c>stealthLower</c>, <c>stealthSneak</c>, <c>coverReach</c>,
/// <c>duckIn</c>, <c>duckOut</c>, <c>squeezeTurnRate</c>, <c>sideStopRate</c>, <c>sideHold</c>) and the stealth key (C where free); the note <c>stealth</c> (none:
/// The earlier build's); on the proving ground's stealth station (skip without it, the 1.1 install or the models repository's files and after): through the gaps,
/// the narrow one and the bend refused and said once, stealth and cover along the wall to its end and out, the duck walked under; a recording replays digest for
/// digest on the earlier build's and on the earlier build's; with <c>VITOMIR_STEALTH_TAPES</c>, the tapes the stills and the timings are drawn from.
/// </summary>
public sealed class PeopleStealthTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    [Fact]
    public void The_settings_give_the_stealth_and_the_switch_turns_it_off()
    {
        var plain = new PlatformSettings();
        var on = plain.PersonStealthFor(new Cli([]));
        Assert.Equal((true, 0.6f, 0.6f, 0.11f, true, 0.3f, 0.4f, 0.6f, 1.5f, 2f, 0.1f),
                     (on.On, on.Pace, on.CrouchPace, on.Lower, on.Sneak, on.CoverReach, on.DuckIn, on.DuckOut, on.TurnRate, on.StopRate, on.Hold));
        Assert.Equal(PersonStealthSettings.Off, plain.PersonStealthFor(new Cli(["--stealth-off"])));
        Assert.Equal(PersonStealthSettings.Off, new PlatformSettings { StealthOff = true }.PersonStealthFor(new Cli([])));
        Assert.Equal(PersonStealthSettings.Off, plain.PersonStealthFor(new Cli(["--walls-off"])));   // it rides on the walls
        Assert.Equal("off", PersonStealthSettings.Off.Note());
        Assert.Equal(0.5f, new PlatformSettings { StealthPace = 0.5f }.PersonStealthFor(new Cli([])).Pace);
        Assert.Equal(0.6f, new PlatformSettings { StealthPace = 2f }.PersonStealthFor(new Cli([])).Pace);   // out of 0.3 to 0.8: the default
        Assert.Equal(0.2f, new PlatformSettings { SideHold = 0.2f }.PersonStealthFor(new Cli([])).Hold);
        Assert.Equal(1.5f, new PlatformSettings { SqueezeTurnRate = 3f }.PersonStealthFor(new Cli([])).TurnRate);
        Assert.False(new PlatformSettings { StealthSneak = false }.PersonStealthFor(new Cli([])).Sneak);
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-stealth-stealth-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { StealthOff = true, StealthPace = 0.5f, CoverReach = 0.4f, DuckIn = 0.3f, SideStopRate = 2.5f }.Save(path);
            var text = File.ReadAllText(path);
            foreach (var key in new[] { "\"stealthOff\": true", "\"stealthPace\": 0.5", "\"coverReach\": 0.4", "\"duckIn\": 0.3", "\"sideStopRate\": 2.5", "\"stealthSneak\": true" })
                Assert.Contains(key, text);
            var back = PlatformSettings.Load(path);
            Assert.Equal((true, 0.5f, 0.4f, 0.3f, 2.5f), (back.StealthOff, back.StealthPace, back.CoverReach, back.DuckIn, back.SideStopRate));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_stealth_key_is_C_where_nothing_else_has_it_and_waits_for_one_where_C_is_taken()
    {
        // A set with crouch and cancel on X, C free: stealth is given C, once.
        var his = PlatformSettings.DefaultBindings();
        his["crouch"] = ["X"];
        his["cancel"] = ["X"];
        his.Remove(PlatformSettings.StealthAction);
        Assert.Equal("C", PlatformSettings.GiveStealthItsKey(his));
        Assert.Equal(["C"], his[PlatformSettings.StealthAction]);
        Assert.Null(PlatformSettings.GiveStealthItsKey(his));   // a file that has it keeps what it has
        // The defaults (C crouches): stealth has no key until one is given on the Controls page, said.
        var plain = PlatformSettings.DefaultBindings();
        plain.Remove(PlatformSettings.StealthAction);
        var said = new List<string>();
        Assert.Null(PlatformSettings.GiveStealthItsKey(plain, said.Add));
        Assert.Empty(plain[PlatformSettings.StealthAction]);
        Assert.Contains(said, l => l.Contains("Stealth", StringComparison.Ordinal));
    }

    [Fact]
    public void A_tape_with_no_stealth_note_plays_without_it_and_one_with_it_with_it()
    {
        var head = new TapeHeader();
        var o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.Equal(PersonStealthSettings.Off, o.PersonStealth);
        head.Notes[PersonStealthSettings.TapeNote] = "off";
        Assert.Equal(PersonStealthSettings.Off, new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonStealth);
        var mine = new PersonStealthSettings { Pace = 0.5f, Hold = 0.05f };
        head.Notes[PersonStealthSettings.TapeNote] = mine.Note();
        Assert.Equal(mine, new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonStealth);
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-stealth-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Start = start, Body = PeopleBodies.Models, ModelsFolder = models, Say = _ => { },
    };

    static SessionOptions WithStealthOff(SessionOptions o) => o with { PersonStealth = PersonStealthSettings.Off };

    static void NeedsStealth(SessionOptions options)
    {
        Session s;
        try { s = Session.Create(options); }
        catch (SessionException e) when (e.Message.Contains("start", StringComparison.OrdinalIgnoreCase)) { Assert.Skip($"the course has no lane {options.Start} (before the stealth's)"); throw; }
        using (s)
        {
            var person = s.World.Service<CharacterAssets>().Kind(s.World.Get<CharacterAnimation>(s.Player).Kind).Person;
            Assert.SkipUnless(person?.Ledge?.Paths is { HasSqueeze: true, HasCover: true }, "the models repository's files are too old (no squeeze or cover clips)");
        }
    }

    static ActionFrame Frame(Vector2 wish, Buttons buttons) => new(wish, Vector2.Zero, buttons, new Vector2(0.5f, 0.5f), 16f / 9f);

    /// <summary>The camera looks along +X (the lanes' way) from the start: forward +X, left +Y (the cover wall's side), right −Y.</summary>
    static readonly Vector2 Fwd = new(0f, 1f), Back = new(0f, -1f), ViewLeft = new(-1f, 0f), ViewRight = new(1f, 0f);

    /// <summary>Walking into a gap and through it (forward held, walking, until out the far side), then still.</summary>
    static Func<int, Session, ActionFrame> Through(float until) => (t, s) => s.World.Get<Body>(s.Player).Feet.X < until ? Frame(Fwd, Buttons.Walk) : Frame(Vector2.Zero, Buttons.None);

    /// <summary>
    /// The cover lane (the body faces the view, the program's default): forward (walking) to 2.5 m along the wall (5 m long), a step toward it, stealth on (a press of its
    /// key), the view turned to look away from the wall (a quarter turn of the mouse over a quarter of a second), back (S: backing into it, cover at once), still,
    /// then left along it (A, to the wall's east end: a stop, said once) until past <paramref name="toX"/>, right (D) for <paramref name="back"/> ticks, forward
    /// out (W, away from the wall), stealth off.
    /// </summary>
    static Func<int, Session, ActionFrame> Cover(float toX = 99f, int back = 240)
    {
        int phase = 0, at = 0;
        float y0 = float.NaN;
        return (t, s) =>
        {
            var b = s.World.Get<Body>(s.Player);
            var move = s.World.Get<Traversal>(s.Player).Kind;
            if (float.IsNaN(y0)) y0 = b.Feet.Y;
            void Next() { phase++; at = t; }
            switch (phase)
            {
                case 0: if (b.Feet.X < 55.5f) return Frame(Fwd, Buttons.Walk); Next(); return Frame(Vector2.Zero, Buttons.None);
                case 1: if (t - at < 30) return Frame(Vector2.Zero, Buttons.None); Next(); return Frame(ViewLeft, Buttons.Walk);
                case 2: if (b.Feet.Y < y0 + 0.25f && t - at < 120) return Frame(ViewLeft, Buttons.Walk); Next(); return Frame(Vector2.Zero, Buttons.None);
                case 3: if (t - at < 20) return Frame(Vector2.Zero, Buttons.None); Next(); return Frame(Vector2.Zero, Buttons.Stealth);   // stealth on
                case 4: if (t - at < 30) return Frame(Vector2.Zero, Buttons.None); Next(); return Look(-DetMath.HalfPi / 30f);
                case 5: if (t - at < 29) return Look(-DetMath.HalfPi / 30f); Next(); return Frame(Back, Buttons.None);   // the view (and the body) looking away
                case 6 when move != TraversalKind.Cover && t - at < 60: return Frame(Back, Buttons.None);   // backing into it
                case 6 when t - at < 260: return Frame(Vector2.Zero, Buttons.None);
                case 6: Next(); return Frame(ViewLeft, Buttons.None);
                case 7: if (b.Feet.X < toX && t - at < 1200) return Frame(ViewLeft, Buttons.None); Next(); return Frame(ViewRight, Buttons.None);
                case 8: if (t - at < back) return Frame(ViewRight, Buttons.None); Next(); return Frame(Vector2.Zero, Buttons.None);
                case 9: if (t - at < 60) return Frame(Vector2.Zero, Buttons.None); Next(); return Frame(Fwd, Buttons.None);
                case 10: if (move == TraversalKind.Cover || t - at < 20) return Frame(Fwd, Buttons.None); Next(); return Frame(Vector2.Zero, Buttons.None);
                case 11: if (t - at < 60) return Frame(Vector2.Zero, Buttons.None); Next(); return Frame(Vector2.Zero, Buttons.Stealth);
                default: return Frame(Vector2.Zero, Buttons.None);
            }
        };
    }

    /// <summary>A frame turning the view by <paramref name="yaw"/> (radians, counter-clockwise) and nothing else.</summary>
    static ActionFrame Look(float yaw) => new(Vector2.Zero, new Vector2(yaw, 0f), Buttons.None, new Vector2(0.5f, 0.5f), 16f / 9f);

    /// <summary>The duck lane: walking forward, crouch held from 1.2 m before the opening to 0.6 m past it.</summary>
    static Func<int, Session, ActionFrame> Duck() => (t, s) =>
    {
        float x = s.World.Get<Body>(s.Player).Feet.X;
        if (x > 58f) return Frame(Vector2.Zero, Buttons.None);
        return Frame(Fwd, Buttons.Walk | (x > 51.8f && x < 55.1f ? Buttons.Crouch : Buttons.None));
    };

    /// <summary>Stealth on standing, then walking ahead 3 s, stopped, off.</summary>
    static Func<int, Session, ActionFrame> Stealthy() => (t, s) => t switch
    {
        60 => Frame(Vector2.Zero, Buttons.Stealth),
        < 240 => Frame(Vector2.Zero, Buttons.None),
        < 600 => Frame(Fwd, Buttons.None),
        660 => Frame(Vector2.Zero, Buttons.Stealth),
        _ => Frame(Vector2.Zero, Buttons.None),
    };

    /// <summary>A run: the log, each tick's ledge clip (the motor's), the drawn clip, the body, the side moves begun and refused (tick, what), the tape.</summary>
    internal static (List<string> Said, List<(int Tick, string Clip, string Drawn, BodyState State, Vector3 Feet, TraversalKind Move)> Rows, List<(int Tick, string What)> Begun, Tape? Tape) Run(
        SessionOptions options, Func<int, Session, ActionFrame> plan, int ticks, bool record = false)
    {
        var said = new List<string>();
        using var s = Session.Create(options with { Say = said.Add });
        var assets = s.World.Service<CharacterAssets>();
        var paths = assets.Kind(s.World.Get<CharacterAnimation>(s.Player).Kind).Person?.Ledge?.Paths;
        var tape = record ? new Tape { Header = s.TapeHeader("test") } : null;
        var rows = new List<(int, string, string, BodyState, Vector3, TraversalKind)>();
        var begun = new List<(int, string)>();
        for (int t = 0; t < ticks; t++)
        {
            var frame = plan(t, s);
            s.Step(frame);
            if (tape is not null)
            {
                tape.Frames.Add(frame);
                if (s.Tick % Tape.DigestEvery == 0) tape.Digests[s.Tick] = s.Digest();
            }
            var l = s.World.TryStore<PersonLedge>(out var ls) && ls.Has(s.Player) ? ls.Get(s.Player).Move : default;
            string clip = l.Clip > 0 && paths?.At(l.Clip - 1) is { } c ? c.Name : "-";
            var b = s.World.Get<Body>(s.Player);
            rows.Add(((int)s.Tick, clip, assets.Clip(s.World.Get<CharacterAnimation>(s.Player).Clip).Name, b.State, b.Feet, s.World.Get<Traversal>(s.Player).Kind));
            foreach (var x in s.World.Events<SideMoveStarted>().ThisTick) if (x.Entity == s.Player) begun.Add(((int)s.Tick, $"{x.Act} {x.Way}"));
            foreach (var x in s.World.Events<SideMoveRefused>().ThisTick) if (x.Entity == s.Player) begun.Add(((int)s.Tick, $"no {x.Act} {x.Why}"));
            foreach (var x in s.World.Events<StealthChanged>().ThisTick) if (x.Entity == s.Player) begun.Add(((int)s.Tick, $"Stealth {(x.On ? "on" : "off")}"));
        }
        return (said, rows, begun, tape);
    }

    [Fact]
    public void On_the_proving_ground_the_gaps_are_squeezed_through_the_narrow_one_and_the_bend_refused_and_said_once()
    {
        var (course, game, models) = Needs();
        NeedsStealth(On(course, game, models, "stealth-0.40"));
        foreach (var lane in new[] { "0.34", "0.40", "0.50" })
        {
            var (said, rows, begun, _) = Run(On(course, game, models, $"stealth-{lane}"), Through(57.5f), 22 * World.TicksPerSecond);
            output.WriteLine($"stealth-{lane}: {string.Join(", ", begun.Select(b => $"{F(b.Tick * World.TickSeconds)} {b.What}"))}; end {F(rows[^1].Feet.X)} {rows[^1].Move}");
            foreach (var l in said.Where(l => l.Contains("squeeze:"))) output.WriteLine("  " + l);
            Assert.Contains(begun, b => b.What.StartsWith("SqueezeIn", StringComparison.Ordinal));
            Assert.Contains(begun, b => b.What.StartsWith("SqueezeOut", StringComparison.Ordinal));
            Assert.True(rows[^1].Feet.X > 56.2f && rows[^1].Move == TraversalKind.None && rows[^1].State == BodyState.Ground, $"out the far side: {F(rows[^1].Feet.X)}");
        }
        foreach (var (lane, why) in new[] { ("0.30", "narrower than the squeeze's clips allow"), ("bend", "the gap bends") })
        {
            var (said, rows, begun, _) = Run(On(course, game, models, $"stealth-{lane}"), Through(57.5f), 8 * World.TicksPerSecond);
            output.WriteLine($"stealth-{lane}: {string.Join(", ", begun.Select(b => b.What))}; {string.Join(" | ", said.Where(l => l.Contains("no squeeze")))}");
            Assert.Equal(1, said.Count(l => l.Contains("no squeeze") && l.Contains(why)));
            Assert.DoesNotContain(rows, r => r.Move == TraversalKind.Squeeze);
            Assert.True(rows[^1].Feet.X < 53f, $"stopped at the mouth: {F(rows[^1].Feet.X)}");
        }
    }

    [Fact]
    public void On_the_proving_ground_in_stealth_the_back_to_the_cover_wall_along_it_to_its_end_and_out_and_the_duck_walked_under()
    {
        var (course, game, models) = Needs();
        var o = On(course, game, models, "stealth-cover");
        NeedsStealth(o);
        var (said, rows, begun, _) = Run(o, Cover(), 26 * World.TicksPerSecond);
        output.WriteLine($"stealth-cover: {string.Join(", ", begun.Select(b => $"{F(b.Tick * World.TickSeconds)} {b.What}"))}");
        foreach (var l in said.Where(l => l.Contains("cover") || l.Contains("stealth"))) output.WriteLine("  " + l);
        Assert.Contains(said, l => l.Contains("stealth on"));
        Assert.Contains(begun, b => b.What.StartsWith("CoverIn", StringComparison.Ordinal));
        Assert.Contains(begun, b => b.What == "CoverStep 1");
        Assert.Contains(begun, b => b.What == "CoverStep -1");
        Assert.Contains(begun, b => b.What.StartsWith("CoverOut", StringComparison.Ordinal));
        Assert.Equal(1, said.Count(l => l.Contains("no cover: a side-step") && l.Contains("the wall ends there")));
        Assert.Contains(said, l => l.Contains("stealth off"));
        Assert.Equal(TraversalKind.None, rows[^1].Move);
        var (dsaid, drows, _, _) = Run(On(course, game, models, "stealth-duck"), Duck(), 14 * World.TicksPerSecond);
        int under = drows.FindIndex(r => r.Feet.X > 53.2f && r.Feet.X < 54.3f);
        output.WriteLine($"stealth-duck: under the opening on {drows[under].Drawn}; end {F(drows[^1].Feet.X)} on {drows[^1].Drawn}");
        Assert.Contains("crouch_walk", drows[under].Drawn, StringComparison.Ordinal);
        Assert.True(drows[^1].Feet.X > 57f, $"through: {F(drows[^1].Feet.X)}");
    }

    [Fact]
    public void A_recording_notes_the_stealth_and_replays_and_one_without_it_without_it()
    {
        var (course, game, models) = Needs();
        var o = On(course, game, models, "stealth-0.40");
        NeedsStealth(o);
        foreach (var options in new[] { o, WithStealthOff(o) })
        {
            var tape = Run(options, Through(57.5f), 10 * World.TicksPerSecond, record: true).Tape!;
            Assert.Equal(options.PersonStealth.Note(), tape.Header.Notes[PersonStealthSettings.TapeNote]);
            var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
            Assert.Equal(options.PersonStealth, again.PersonStealth);
            using var replay = Session.Create(again);
            Assert.Equal(options.PersonStealth.On, replay.World.TryStore<PersonStealth>(out _));   // The earlier build's world has no stealth store: its digests are the earlier build's
            int compared = 0;
            foreach (var frame in tape.Frames)
            {
                replay.Step(frame);
                if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
            }
            output.WriteLine($"{tape.Header.Notes[PersonStealthSettings.TapeNote]}: {compared} digests replayed");
            Assert.True(compared >= 8);
        }
    }

    /// <summary>
    /// With <c>VITOMIR_STEALTH_TAPES</c> (a folder): the tapes the stills and its timings are drawn from, and beside each the ticks to draw (<c>.ticks</c>: where
    /// each squeeze, cover or crouch clip begins, each side move begun, and stealth turned on or off) and its log.
    /// </summary>
    [Fact]
    public void Tapes_for_the_stills()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VITOMIR_STEALTH_TAPES") is { Length: > 0 }, "VITOMIR_STEALTH_TAPES is not set");
        string dir = Environment.GetEnvironmentVariable("VITOMIR_STEALTH_TAPES")!;
        Directory.CreateDirectory(dir);
        var (course, game, models) = Needs();
        var cases = new List<(string Name, SessionOptions Options, Func<Func<int, Session, ActionFrame>> Plan, int Seconds)>
        {
            ("squeeze-0.34", On(course, game, models, "stealth-0.34"), () => Through(57.5f), 22),
            ("squeeze-0.40", On(course, game, models, "stealth-0.40"), () => Through(57.5f), 22),
            ("squeeze-0.50", On(course, game, models, "stealth-0.50"), () => Through(57.5f), 22),
            ("refused-0.30", On(course, game, models, "stealth-0.30"), () => Through(57.5f), 10),
            ("refused-bend", On(course, game, models, "stealth-bend"), () => Through(57.5f), 10),
            ("stealth", On(course, game, models, "stealth-cover"), Stealthy, 7),
            ("cover", On(course, game, models, "stealth-cover"), () => Cover(), 26),
            ("duck", On(course, game, models, "stealth-duck"), Duck, 14),
            ("duck-off", WithStealthOff(On(course, game, models, "stealth-duck")), Duck, 14),
        };
        foreach (var (name, o, plan, seconds) in cases)
        {
            var said = new List<string>();
            using var session = Session.Create(o with { Say = said.Add });
            var assets = session.World.Service<CharacterAssets>();
            var tape = new Tape { Header = session.TapeHeader(name) };
            var marks = new List<(int Tick, string What)>();
            string last = "";
            var frames = plan();
            for (int t = 0; t < seconds * World.TicksPerSecond; t++)
            {
                var frame = frames(t, session);
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                string clip = assets.Clip(session.World.Get<CharacterAnimation>(session.Player).Clip).Name;
                if (clip.Contains(':', StringComparison.Ordinal)) clip = clip[(clip.IndexOf(':') + 1)..];
                if (clip.Contains('@', StringComparison.Ordinal)) clip = clip[..clip.IndexOf('@')];
                bool began = false;
                foreach (var x in session.World.Events<SideMoveStarted>().ThisTick) if (x.Entity == session.Player) began = true;
                foreach (var x in session.World.Events<StealthChanged>().ThisTick) if (x.Entity == session.Player) marks.Add(((int)session.Tick, x.On ? "stealth_on" : "stealth_off"));
                if (clip != last || began) marks.Add(((int)session.Tick, clip));
                last = clip;
            }
            string path = Path.Combine(dir, $"{name}.tape");
            tape.Write(path);
            File.WriteAllLines(Path.ChangeExtension(path, ".ticks"), marks.Select(m => $"{m.Tick} {m.What}"));
            File.WriteAllLines(Path.ChangeExtension(path, ".log"), said);
            output.WriteLine($"{path}: {tape.Frames.Count} ticks; {string.Join(", ", marks.Select(m => $"{m.Tick} {m.What}"))}");
        }
    }
}
