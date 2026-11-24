// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using SDL;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// As the program gives it: the run's and the sprint's own vault on the clips (<c>autoVaultOff</c>,
/// <c>--auto-vault-off</c>; the note <c>over</c>'s <c>auto=1</c>), the hands on the real top (<c>handsOnTopsOff</c>, <c>--hands-on-tops-off</c>; the note <c>hands</c>), and
/// the camera to watch from (the action <c>orbitCamera</c> on Right Alt, the settings <c>orbit*</c>, the mouse's look and the wheel routed to it). A tape
/// without the notes is the earlier build's. On the proving ground (skip without it, the 1.1 install or the models repository's files): a sprint and a run at the vault
/// station's 0.80 m wall vault it on the clip with no press (the sprint vault, a stop), and a recording replays digest for digest.
/// </summary>
public sealed class PeopleVaultAndHandsTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    [Fact]
    public void The_settings_and_flags_give_the_vault_and_the_hands_and_the_switches_turn_them_off()
    {
        var plain = new PlatformSettings();
        Assert.True(plain.PersonOverFor(new Cli([])).Auto);
        Assert.False(plain.PersonOverFor(new Cli(["--auto-vault-off"])).Auto);
        Assert.True(plain.PersonOverFor(new Cli(["--auto-vault-off"])).Clips);   // the clips stay; only the run's own vault goes
        Assert.False(new PlatformSettings { AutoVaultOff = true }.PersonOverFor(new Cli([])).Auto);
        Assert.True(plain.PersonHandsFor(new Cli([])).OnTops);
        Assert.False(plain.PersonHandsFor(new Cli(["--hands-on-tops-off"])).Any);
        Assert.False(plain.PersonHandsFor(new Cli(["--over-clips-off"])).Any);   // they ride on the moves' clips
        Assert.False(new PlatformSettings { HandsOnTopsOff = true }.PersonHandsFor(new Cli([])).Any);
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-vault-and-hands-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { AutoVaultOff = true, HandsOnTopsOff = true, OrbitFarthest = 8f }.Save(path);
            var text = File.ReadAllText(path);
            Assert.Contains("\"autoVaultOff\": true", text);
            Assert.Contains("\"handsOnTopsOff\": true", text);
            var back = PlatformSettings.Load(path);
            Assert.True(back.AutoVaultOff && back.HandsOnTopsOff);
            Assert.Equal(8f, back.OrbitCameraFor().FarthestMetres);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_without_the_notes_has_no_auto_vault_and_the_hands_as_the_clips_have_them()
    {
        var head = new TapeHeader();
        head.Notes[PersonOverSettings.TapeNote] = "clips=1 enter=0.12 cut=0.08 off=0.3";
        var o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.True(o.PersonOver.Clips);
        Assert.False(o.PersonOver.Auto);
        Assert.False(o.PersonHands.Any);
        head.Notes[PersonOverSettings.TapeNote] = new PersonOverSettings().Note();
        head.Notes[PersonHandsSettings.TapeNote] = new PersonHandsSettings().Note();
        o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.True(o.PersonOver.Auto);
        Assert.True(o.PersonHands.OnTops);
    }

    [Fact]
    public void The_orbit_camera_is_on_Right_Alt_and_an_older_file_gets_it_where_it_is_free()
    {
        Assert.Equal(["Right Alt"], PlatformSettings.DefaultBindings()[OrbitCamera.Action]);
        var said = new List<string>();
        var free = new Dictionary<string, string[]> { ["jump"] = ["Space"] };
        Assert.Equal("Right Alt", PlatformSettings.GiveOrbitItsKey(free, said.Add));
        var taken = new Dictionary<string, string[]> { ["use"] = ["Right Alt"] };
        Assert.Null(PlatformSettings.GiveOrbitItsKey(taken, said.Add));
        Assert.Empty(taken[OrbitCamera.Action]);
        Assert.Contains(said, l => l.Contains("Orbit Camera", StringComparison.Ordinal));
        // Right Alt is not one of the defaults' other keys (Left Alt frees the pointer).
        Assert.DoesNotContain(PlatformSettings.DefaultBindings(), kv => kv.Key != OrbitCamera.Action && kv.Value.Contains("Right Alt"));
    }

    static SDL_Event Key(SDL_Scancode code, bool down)
    {
        var e = new SDL_Event();
        e.type = (uint)(down ? SDL_EventType.SDL_EVENT_KEY_DOWN : SDL_EventType.SDL_EVENT_KEY_UP);
        e.key.scancode = code;
        return e;
    }

    static SDL_Event Motion(float dx, float dy)
    {
        var e = new SDL_Event();
        e.type = (uint)SDL_EventType.SDL_EVENT_MOUSE_MOTION;
        e.motion.xrel = dx;
        e.motion.yrel = dy;
        return e;
    }

    static SDL_Event Wheel(float y)
    {
        var e = new SDL_Event();
        e.type = (uint)SDL_EventType.SDL_EVENT_MOUSE_WHEEL;
        e.wheel.y = y;
        return e;
    }

    [Fact]
    public void Held_the_mouse_swings_the_orbit_not_the_player_and_the_wheel_zooms_unless_bound()
    {
        var s = new PlatformSettings();
        var input = new InputCollector(s);
        input.OrbitAllowed = true;
        // Not held: the look is the player's.
        input.Take(Motion(10f, 0f), true, 2560, 1440);
        Assert.NotEqual(Vector2.Zero, input.NextTick().Look);
        Assert.Equal(Vector2.Zero, input.TakeOrbit().Look);
        // Held: the orbit's, the player's look nought (the move keys as ever).
        input.Take(Key(SDL_Scancode.SDL_SCANCODE_RALT, true), true, 2560, 1440);
        Assert.True(input.OrbitHeld);
        input.Take(Key(SDL_Scancode.SDL_SCANCODE_W, true), true, 2560, 1440);
        input.Take(Motion(10f, -4f), true, 2560, 1440);
        var frame = input.NextTick();
        Assert.Equal(Vector2.Zero, frame.Look);
        Assert.Equal(new Vector2(0f, 1f), frame.Move);
        var (look, _) = input.TakeOrbit();
        Assert.True(look.X < 0f && look.Y > 0f, "the mouse right turns the look right (x negative), up tilts it up");
        // Not allowed (first person): the key does nothing, the look is the player's.
        input.OrbitAllowed = false;
        Assert.False(input.OrbitHeld);
        input.Take(Motion(10f, 0f), true, 2560, 1440);
        Assert.NotEqual(Vector2.Zero, input.NextTick().Look);
        input.Take(Key(SDL_Scancode.SDL_SCANCODE_RALT, false), true, 2560, 1440);
        // The wheel, unbound: the camera's; bound to an action: the action's.
        input.Take(Wheel(1f), true, 2560, 1440);
        Assert.Equal(1f, input.TakeOrbit().Wheel);
        s.Bindings["slot1"] = ["Wheel Up"];
        var bound = new InputCollector(s);
        bound.Take(Wheel(1f), true, 2560, 1440);
        Assert.Equal(0f, bound.TakeOrbit().Wheel);
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-vault-and-hands-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Start = start, Body = PeopleBodies.Models, ModelsFolder = models, Say = _ => { },
    };

    static ActionFrame Frame(float ahead, Buttons buttons) => new(new Vector2(0f, ahead), Vector2.Zero, buttons, new Vector2(0.5f, 0.5f), 16f / 9f);

    /// <summary>At a station's start (8 m before its face, facing it): <paramref name="gait"/> held ahead for 3.5 s, then still for 1.5 s.</summary>
    static IEnumerable<ActionFrame> RunAt(Buttons gait)
    {
        for (int t = 0; t < 5 * World.TicksPerSecond; t++) yield return Frame(t < 3.5f * World.TicksPerSecond ? 1f : 0f, t < 3.5f * World.TicksPerSecond ? gait : Buttons.None);
    }

    /// <summary>Walked to within 0.55 m of the face 8 m ahead, stopped, tapped.</summary>
    static IEnumerable<ActionFrame> WalkAndTap(Session s)
    {
        var from = s.World.Get<Body>(s.Player).Feet;
        int t = 0;
        while (s.World.Get<Body>(s.Player).Feet.X - from.X < 8f - 0.55f - 0.3f && t++ < 12 * World.TicksPerSecond) yield return Frame(1f, Buttons.Walk);
        for (int i = 0; i < 60; i++) yield return Frame(0f, Buttons.None);
        yield return Frame(0f, Buttons.Jump);
        for (int i = 0; i < 360; i++) yield return Frame(0f, Buttons.None);
    }

    [Fact]
    public void At_the_vault_station_a_sprint_and_a_run_vault_on_the_clip_by_themselves_and_with_auto_vault_off_the_sprint_vaults_on_its_stand_in()
    {
        var (course, game, models) = Needs();
        foreach (var (what, gait) in new[] { ("sprint", Buttons.Sprint), ("run", Buttons.None) })
            foreach (var (label, over) in new[] { ("auto vault", new PersonOverSettings()), ("auto vault off", new PersonOverSettings { Auto = false }) })
            {
                using var session = Session.Create(On(course, game, models, "vault-0.80") with { PersonOver = over });
                var assets = session.World.Service<CharacterAssets>();
                if (assets.Kind(session.World.Get<CharacterAnimation>(session.Player).Kind).Person?.Over?.Paths is null)
                {
                    Assert.Skip("the models repository's files are too old");
                    return;
                }
                var moves = new List<TraversalKind>();
                var clips = new List<string>();
                var from = session.World.Get<Body>(session.Player).Feet;
                foreach (var frame in RunAt(gait))
                {
                    session.Step(frame);
                    var k = session.World.Get<Traversal>(session.Player).Kind;
                    if (k != TraversalKind.None && (moves.Count == 0 || moves[^1] != k)) moves.Add(k);
                    var name = assets.Clip(session.World.Get<CharacterAnimation>(session.Player).Clip).Name;
                    if (clips.Count == 0 || clips[^1] != name) clips.Add(name);
                }
                float gone = Vector2.Distance(new Vector2(from.X, from.Y), new Vector2(session.World.Get<Body>(session.Player).Feet.X, session.World.Get<Body>(session.Player).Feet.Y));
                output.WriteLine($"{label} {what}: moves {string.Join(", ", moves)}; clips {string.Join(", ", clips.Where(c => !c.Contains(':')))}; {F(gone)} m on");
                if (label == "auto vault")
                {
                    Assert.Equal([TraversalKind.Vault], moves);
                    Assert.Contains("vault_low", clips);
                    Assert.True(gone > 8.6f, "over the wall");
                }
                else if (what == "sprint") Assert.Equal([TraversalKind.Hurdle], moves);
                else { Assert.Empty(moves); Assert.True(gone < 8f, "stopped at the wall"); }
            }
    }

    [Fact]
    public void A_walk_stopped_at_the_vault_station_s_wall_is_told_a_run_or_a_sprint_vaults_it()
    {
        var (course, game, models) = Needs();
        foreach (var (label, over, want) in new[] { ("auto vault", new PersonOverSettings(), "a run or a sprint vaults it (a walk: a tap)"), ("auto vault off", new PersonOverSettings { Auto = false }, "a sprint vaults it") })
        {
            var said = new List<string>();
            using var session = Session.Create(On(course, game, models, "vault-0.80") with { PersonOver = over, Say = said.Add });
            // The vault's own clip is what a run is told it can take; a models repository from before it has none, and a
            // walk is then told only of the sprint's stand-in: skipped, as the station's other tests skip.
            var assets = session.World.Service<CharacterAssets>();
            if (assets.Kind(session.World.Get<CharacterAnimation>(session.Player).Kind).Person?.Over?.Paths is null)
            {
                Assert.Skip("the models repository's files are too old");
                return;
            }
            for (int t = 0; t < 8 * World.TicksPerSecond; t++) session.Step(Frame(1f, Buttons.Walk));   // 8 m at a walk, into the wall
            var stop = said.FirstOrDefault(l => l.Contains("stop:", StringComparison.Ordinal));
            output.WriteLine($"{label}: {stop}");
            Assert.NotNull(stop);
            Assert.EndsWith(want, stop);
        }
    }

    [Fact]
    public void A_recording_notes_the_vault_and_the_hands_and_replays_on_them()
    {
        var (course, game, models) = Needs();
        foreach (var (start, sprint) in new[] { ("vault-0.80", true), ("mantle-1.70", false) })
        {
            var options = On(course, game, models, start);
            Tape tape;
            using (var session = Session.Create(options))
            {
                tape = new Tape { Header = session.TapeHeader("test") };
                foreach (var frame in sprint ? RunAt(Buttons.Sprint) : WalkAndTap(session))
                {
                    session.Step(frame);
                    tape.Frames.Add(frame);
                    if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                }
            }
            Assert.Equal(options.PersonOver.Note(), tape.Header.Notes[PersonOverSettings.TapeNote]);
            Assert.Equal(options.PersonHands.Note(), tape.Header.Notes[PersonHandsSettings.TapeNote]);
            var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
            Assert.True(again.PersonOver.Auto);
            Assert.True(again.PersonHands.OnTops);
            using var replay = Session.Create(again);
            int compared = 0;
            foreach (var frame in tape.Frames)
            {
                replay.Step(frame);
                if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
            }
            output.WriteLine($"{start}: {compared} digests replayed");
            Assert.True(compared >= 5);
        }
    }

    /// <summary>
    /// With <c>VITOMIR_VAULT_TAPES</c> (a folder): the tapes the stills are drawn from, each with the earlier build's (<c>…-now</c>) and with the switches back to
    /// The earlier build's (<c>…-before</c>): a sprint and a run at the vault station's 0.80 m wall; a mantle of 1.30 m and 1.90 m and a climb onto 0.7 m and 1.1 m (walked to
    /// 0.55 m from the face and tapped).
    /// </summary>
    [Fact]
    public void Tapes_for_the_stills()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VITOMIR_VAULT_TAPES") is { Length: > 0 }, "VITOMIR_VAULT_TAPES is not set");
        string dir = Environment.GetEnvironmentVariable("VITOMIR_VAULT_TAPES")!;
        Directory.CreateDirectory(dir);
        var (course, game, models) = Needs();
        foreach (var (over, hands, suffix) in new[] { (new PersonOverSettings(), new PersonHandsSettings(), "now"), (new PersonOverSettings { Auto = false }, PersonHandsSettings.Off, "before") })
            foreach (var (name, start, kind) in new[]
            {
                ("sprint080", "vault-0.80", 0), ("run080", "vault-0.80", 1), ("mantle130", "mantle-1.30", 2), ("mantle190", "mantle-1.90", 2),
                ("climb070", "vault-climb0.7", 2), ("climb110", "vault-climb1.1", 2),
            })
            {
                using var session = Session.Create(On(course, game, models, start) with { PersonOver = over, PersonHands = hands });
                var tape = new Tape { Header = session.TapeHeader(name) };
                var frames = kind switch { 0 => RunAt(Buttons.Sprint), 1 => RunAt(Buttons.None), _ => WalkAndTap(session) };
                foreach (var frame in frames)
                {
                    session.Step(frame);
                    tape.Frames.Add(frame);
                    if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                }
                string path = Path.Combine(dir, $"{name}-{suffix}.tape");
                tape.Write(path);
                output.WriteLine($"{path}: {tape.Frames.Count} ticks, the player ended at {session.World.Get<Body>(session.Player).Feet}");
            }
    }
}
