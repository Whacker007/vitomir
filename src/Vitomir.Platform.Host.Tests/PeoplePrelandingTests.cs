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
/// The models repository — the prelanding before a standing landing — as the program gives it (<c>prelandingOff</c>, <c>--prelanding-off</c>;
/// the note <c>prelanding</c>). A tape without the note is the earlier build's. On the proving ground (skip without it, the 1.1 install or the models repository's files): on a
/// set without the clips (an older set) the world digests as with <c>--prelanding-off</c>, said once in the log; with them (a newer set) a
/// standing fall plays the prelanding into its landing, and a recording carries the note and replays digest for digest.
/// </summary>
public sealed class PeoplePrelandingTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    [Fact]
    public void The_settings_and_flags_give_the_prelanding_and_the_prelanding_off_switch_turns_it_off()
    {
        var plain = new PlatformSettings();
        Assert.True(plain.PersonPrelandingFor(new Cli([])).Clips);
        Assert.False(plain.PersonPrelandingFor(new Cli(["--prelanding-off"])).Any);
        Assert.False(plain.PersonPrelandingFor(new Cli(["--over-clips-off"])).Any);   // it rides on the moves (the ground below foreseen)
        Assert.False(plain.PersonPrelandingFor(new Cli(["--air-clips-off"])).Any);    // and on the air's clips
        Assert.True(plain.PersonPrelandingFor(new Cli(["--ledge-clips-off"])).Any);   // not on the ledge
        Assert.False(new PlatformSettings { PrelandingOff = true }.PersonPrelandingFor(new Cli([])).Any);
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-prelanding-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { PrelandingOff = true }.Save(path);
            Assert.Contains("\"prelandingOff\": true", File.ReadAllText(path));
            Assert.True(PlatformSettings.Load(path).PrelandingOff);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_without_the_note_has_no_prelanding()
    {
        var head = new TapeHeader();
        Assert.False(new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonPrelanding.Any);
        head.Notes[PersonPrelandingSettings.TapeNote] = new PersonPrelandingSettings().Note();
        var on = new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonPrelanding;
        Assert.True(on.Clips);
        Assert.Equal(new PersonPrelandingSettings().FromSeconds, on.FromSeconds);
        head.Notes[PersonPrelandingSettings.TapeNote] = "clips=1 from=0.2";
        Assert.Equal(0.2f, new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonPrelanding.FromSeconds);
        head.Notes[PersonPrelandingSettings.TapeNote] = "off";
        Assert.False(new SessionOptions { Level = "", Say = _ => { } }.From(head).PersonPrelanding.Any);
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-prelanding-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start, List<string>? said = null, int people = 0) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = people,
        Start = start, Body = PeopleBodies.Models, ModelsFolder = models, Say = line => said?.Add(line),
    };

    static ActionFrame Frame(float ahead, Buttons buttons, float right = 0f) => new(new Vector2(right, ahead), Vector2.Zero, buttons, new Vector2(0.5f, 0.5f), 16f / 9f);

    /// <summary>A place (the session's <c>Place</c>) <paramref name="ahead"/> m ahead of a start's feet and <paramref name="up"/> m up, facing as it faces.</summary>
    static string Ahead(SessionOptions options, float ahead, float up)
    {
        using var probe = Session.Create(options with { Say = _ => { } });
        var at = probe.World.Get<Body>(probe.Player).Feet;
        float yaw = probe.World.Get<Body>(probe.Player).Yaw;
        var (sn, cs) = DetMath.SinCos(yaw);
        var p = at + new Vector3(cs, sn, 0f) * ahead + new Vector3(0f, 0f, up);
        return string.Create(CultureInfo.InvariantCulture, $"{p.X:0.###},{p.Y:0.###},{p.Z:0.###},{yaw * 180f / MathF.PI:0.#}");
    }

    /// <summary>Still for <paramref name="ticks"/> ticks (a body placed in the air falls).</summary>
    static IEnumerable<ActionFrame> Still(int ticks)
    {
        for (int i = 0; i < ticks; i++) yield return Frame(0f, Buttons.None);
    }

    [Fact]
    public void Without_the_clips_the_world_is_as_with_it_off_and_with_them_a_standing_fall_plays_the_prelanding()
    {
        var (course, game, models) = Needs();
        var said = new List<string>();
        var start = On(course, game, models, "drops-1", said, people: 4);
        var options = start with { Place = Ahead(start, 2.5f + 0.35f, 0f) };   // just past the 1 m ledge's edge, standing
        using var now = Session.Create(options);
        using var then = Session.Create(options with { PersonPrelanding = PersonPrelandingSettings.Off });
        var assets = now.World.Service<CharacterAssets>();
        var person = assets.Kind(now.World.Get<CharacterAnimation>(now.Player).Kind).Person!;
        if (person.Air is not { Prelandings.Length: > 0 })
        {
            // On an older set: said once, and it digests as the off switch gives it.
            Assert.Equal(person.Over is null ? 0 : 1, said.Count(l => l.Contains("no prelandings")));
            int compared = 0;
            foreach (var frame in Still(240))
            {
                then.Step(frame);
                now.Step(frame);
                Assert.Equal(then.Digest(), now.Digest());
                compared++;
            }
            output.WriteLine($"on an older set: {compared} digests the same as the prelanding off's");
            return;
        }
        Assert.DoesNotContain(said, l => l.Contains("no prelandings"));
        var clips = new List<string>();
        foreach (var frame in Still(240))
        {
            now.Step(frame);
            var name = assets.Clip(now.World.Get<CharacterAnimation>(now.Player).Clip).Name;
            if (clips.Count == 0 || clips[^1] != name) clips.Add(name);
        }
        output.WriteLine("clips: " + string.Join(", ", clips.Where(c => !c.Contains(':'))));
        int pre = clips.IndexOf("prelanding_soft");
        Assert.True(pre > 0, "the prelanding played");
        Assert.Equal("land_soft", clips[pre + 1]);
    }

    [Fact]
    public void A_recording_notes_the_prelanding_and_replays_on_it()
    {
        var (course, game, models) = Needs();
        var start = On(course, game, models, "drops-1");
        var options = start with { Place = Ahead(start, 2.5f + 0.35f, 0f) };
        Tape tape;
        using (var session = Session.Create(options))
        {
            tape = new Tape { Header = session.TapeHeader("test") };
            foreach (var frame in Still(120).Append(Frame(0f, Buttons.Jump)).Concat(Still(180)))
            {
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
            }
        }
        Assert.Equal(options.PersonPrelanding.Note(), tape.Header.Notes[PersonPrelandingSettings.TapeNote]);
        var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
        Assert.Equal(options.PersonPrelanding.Note(), again.PersonPrelanding.Note());
        using var replay = Session.Create(again);
        int compared = 0;
        foreach (var frame in tape.Frames)
        {
            replay.Step(frame);
            if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
        }
        output.WriteLine($"{compared} digests replayed");
        Assert.True(compared >= 5);
    }

    /// <summary>A tick of the player as drawn: its clip, time, rate and the air's mode, on the ground or not, the pelvis and the hands over its feet.</summary>
    sealed record Row(long Tick, string Clip, float Time, float Rate, string Previous, AirMode Mode, bool Ground, Vector3 Feet, Vector3 Pelvis, Vector3 HandL, Vector3 HandR);

    static Row Read(Session s, CharacterAssets assets)
    {
        var e = s.Player;
        var body = s.World.Get<Body>(e);
        var a = s.World.Get<CharacterAnimation>(e);
        var rig = assets.Kind(a.Kind).Person!.Rig;
        var place = assets.Kind(a.Kind).Placement(body.Feet, body.Yaw);
        var bones = s.World.Service<CharacterPoses>().Bones(s.World, e);
        Vector3 pelvis = Vector3.Transform(bones[rig.Pelvis].Translation, place) - body.Feet;
        Vector3 handL = Vector3.Transform(bones[rig.LeftArm.Tip].Translation, place) - body.Feet;
        Vector3 handR = Vector3.Transform(bones[rig.RightArm.Tip].Translation, place) - body.Feet;
        var mode = s.World.TryStore<PersonAir>(out var airs) && airs.Has(e) ? airs.Get(e).Mode : AirMode.None;
        return new Row(s.Tick, assets.Clip(a.Clip).Name, a.Time, a.Rate, a.PreviousClip >= 0 ? assets.Clip(a.PreviousClip).Name : "-", mode,
                       body.State == BodyState.Ground, body.Feet, pelvis, handL, handR);
    }

    static float Pop(List<Row> rows, Func<Row, Vector3> of, int from, int to)
    {
        float pop = 0f;
        for (int i = Math.Max(2, from); i < Math.Min(rows.Count, to); i++)
            pop = MathF.Max(pop, ((of(rows[i]) - of(rows[i - 1])) - (of(rows[i - 1]) - of(rows[i - 2]))).Length());
        return pop;
    }

    /// <summary>Every standing landing of the run: the prelanding's first tick (or the fall's), its rate, the contact's tick, the landing, and the pops.</summary>
    static IEnumerable<string> Landings(List<Row> rows, CharacterAssets assets, int kind)
    {
        var air = assets.Kind(kind).Person!.Air!;
        for (int i = 1; i < rows.Count; i++)
        {
            if (!rows[i].Ground || rows[i - 1].Ground || rows[i].Mode != AirMode.Land || !air.Stands(Array.FindIndex(Enumerable.Range(0, assets.ClipCount).ToArray(), k => assets.Clip(k).Name == rows[i].Clip))) continue;
            int s = i - 1;
            while (s > 0 && rows[s - 1].Mode == rows[i - 1].Mode && rows[s - 1].Clip == rows[i - 1].Clip) s--;
            bool pre = rows[i - 1].Mode == AirMode.Pre;
            float pIn = Pop(rows, r => r.Pelvis, s - 2, s + 6), hIn = MathF.Max(Pop(rows, r => r.HandL, s - 2, s + 6), Pop(rows, r => r.HandR, s - 2, s + 6));
            float pLand = Pop(rows, r => r.Pelvis, i - 2, i + 6), hLand = MathF.Max(Pop(rows, r => r.HandL, i - 2, i + 6), Pop(rows, r => r.HandR, i - 2, i + 6));
            yield return $"tick {rows[s].Tick}: {(pre ? $"{rows[s].Clip} at {F(rows[s].Time, "0.000")} (from {rows[s].Previous}), rate {F(rows[Math.Min(s + 1, i - 1)].Rate, "0.000")}" : $"{rows[s].Clip} ({rows[s].Mode})")}, "
                         + $"{F((i - s) * World.TickSeconds, "0.000")} s before the contact at tick {rows[i].Tick} ({rows[i].Clip} at {F(rows[i].Time, "0.000")}"
                         + $"{(rows[i].Previous != "-" ? $" faded from {rows[i].Previous}" : ", no blend")}; fell {F(rows[i - 1].Feet.Z - rows[i].Feet.Z, "0.00")} m in its last tick); "
                         + $"pops mm/tick²: into it pelvis {F(pIn * 1000f, "0.0")} hands {F(hIn * 1000f, "0.0")}, into the landing pelvis {F(pLand * 1000f, "0.0")} hands {F(hLand * 1000f, "0.0")}";
        }
    }

    /// <summary>
    /// With <c>VITOMIR_PRELANDING_TAPES</c> (a folder): the tapes the stills are drawn from, each with the prelanding (<c>…-now</c>) and with the earlier build's
    /// (<c>…-before</c>), and every standing landing in them measured: a standing fall off <c>drops-1</c> (placed just past its edge) and off 2.5 m (beside
    /// <c>drops-3</c>, half a metre under its top) and off 0.35 m (beside <c>drops-1</c>: a short hop down, under its lead to the ground); a standing jump in
    /// the start square; the shimmy ledge's hang and a drop to the ground (the earlier build's <c>shimmydrop</c>); and the townsfolk on the models repository's crowd on the
    /// course for 40 s (whether any lands standing). (A walk across <c>kerbs-0.25</c> never leaves the ground: the motor steps it down.)
    /// </summary>
    [Fact]
    public void Tapes_for_the_stills()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VITOMIR_PRELANDING_TAPES") is { Length: > 0 }, "VITOMIR_PRELANDING_TAPES is not set");
        string dir = Environment.GetEnvironmentVariable("VITOMIR_PRELANDING_TAPES")!;
        Directory.CreateDirectory(dir);
        var (course, game, models) = Needs();
        static IEnumerable<ActionFrame> WalkGrabDrop(Session s)
        {
            var from = s.World.Get<Body>(s.Player).Feet;
            int t = 0;
            while (s.World.Get<Body>(s.Player).Feet.X - from.X < 8f - 0.55f - 0.3f && t++ < 12 * World.TicksPerSecond) yield return Frame(1f, Buttons.Walk);
            for (int i = 0; i < 60; i++) yield return Frame(0f, Buttons.None);
            yield return Frame(0f, Buttons.Jump);
            for (int i = 0; i < 3 * World.TicksPerSecond; i++) yield return Frame(0f, Buttons.None);
            yield return Frame(0f, Buttons.Crouch);
            for (int i = 0; i < 3 * World.TicksPerSecond; i++) yield return Frame(0f, Buttons.None);
        }
        static IEnumerable<ActionFrame> Walk(int ticks)
        {
            for (int t = 0; t < ticks; t++) yield return Frame(1f, Buttons.Walk);
            for (int t = 0; t < 120; t++) yield return Frame(0f, Buttons.None);
        }
        var drops1 = On(course, game, models, "drops-1");
        var drops3 = On(course, game, models, "drops-3");
        foreach (var (pre, suffix) in new[] { (new PersonPrelandingSettings(), "now"), (PersonPrelandingSettings.Off, "before") })
            foreach (var (name, options, kind) in new[]
            {
                ("fall1", drops1 with { Place = Ahead(drops1, 2.5f + 0.35f, 0f) }, 0),
                ("fall25", drops3 with { Place = Ahead(drops3, 2.5f + 0.35f, -0.5f) }, 0),
                ("jump", On(course, game, models, "0"), 1),
                ("fall035", drops1 with { Place = Ahead(drops1, 2.5f + 0.35f, -0.65f) }, 0),
                ("ledgedrop", On(course, game, models, "not-built-shimmy"), 3),
            })
            {
                using var session = Session.Create(options with { PersonPrelanding = pre });
                var assets = session.World.Service<CharacterAssets>();
                var tape = new Tape { Header = session.TapeHeader(name) };
                var frames = kind switch
                {
                    0 => Still(240), 1 => Still(60).Append(Frame(0f, Buttons.Jump)).Concat(Still(180)), 2 => Walk(8 * World.TicksPerSecond), _ => WalkGrabDrop(session),
                };
                var rows = new List<Row>();
                foreach (var frame in frames)
                {
                    session.Step(frame);
                    tape.Frames.Add(frame);
                    if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                    rows.Add(Read(session, assets));
                }
                string path = Path.Combine(dir, $"{name}-{suffix}.tape");
                tape.Write(path);
                int k = session.World.Get<CharacterAnimation>(session.Player).Kind;
                output.WriteLine($"{name}-{suffix}: {tape.Frames.Count} ticks");
                foreach (var line in Landings(rows, assets, k)) output.WriteLine("  " + line);
                output.WriteLine("  clips: " + string.Join(", ", rows.Select(r => r.Clip).Where((c, i) => i == 0 || rows[i - 1].Clip != c).Where(c => !c.Contains(':'))));
            }
        // The townsfolk on the course: whether any falls and lands standing (and so plays a prelanding).
        using (var crowd = Session.Create(On(course, game, models, "0") with { People = 12, TownsfolkBody = TownsfolkBodies.Models }))
        {
            int falls = 0, pres = 0, landed = 0;
            var was = new Dictionary<ulong, AirMode>();
            for (int t = 0; t < 40 * World.TicksPerSecond; t++)
            {
                crowd.Step(Frame(0f, Buttons.None));
                if (!crowd.World.TryStore<PersonAir>(out var airs)) break;
                foreach (var e in airs.Entities)
                {
                    if (e == crowd.Player) continue;
                    var m = airs.Get(e).Mode;
                    was.TryGetValue(e.Packed, out var before);
                    if (m == AirMode.Fall && before != AirMode.Fall) falls++;
                    var ca = crowd.World.Get<CharacterAnimation>(e);
                    var cb = crowd.World.Get<Body>(e);
                    var cassets = crowd.World.Service<CharacterAssets>();
                    if (m == AirMode.Pre && before != AirMode.Pre)
                    {
                        pres++;
                        output.WriteLine($"  a townsperson at tick {crowd.Tick}: {cassets.Clip(ca.Clip).Name} at x{F(ca.Rate)} over ({F(cb.Feet.X)}, {F(cb.Feet.Y)}, {F(cb.Feet.Z)}) falling {F(cb.Velocity.Z)} m/s");
                    }
                    if (m == AirMode.Land && before != AirMode.Land)
                    {
                        landed++;
                        output.WriteLine($"  a townsperson at tick {crowd.Tick}: {cassets.Clip(ca.Clip).Name} at ({F(cb.Feet.X)}, {F(cb.Feet.Y)}, {F(cb.Feet.Z)}), the drop {F(cb.LastFallHeight)} m");
                    }
                    was[e.Packed] = m;
                }
            }
            output.WriteLine($"the townsfolk on the course for 40 s ({was.Count} with the air's clips): {falls} falls, {pres} prelandings, {landed} landings");
        }
    }
}
