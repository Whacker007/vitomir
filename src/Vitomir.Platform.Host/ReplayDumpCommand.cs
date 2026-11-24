// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render;

namespace Vitomir.Platform.Host;

/// <summary>
/// <c>--replay-dump &lt;tape&gt; [--dump-dir &lt;folder&gt;] [--dump-stills a-b:step,…] [--picture WxH]</c> — how the
/// engine sees what the player saw. The tape is replayed headless, as <c>--replay-check</c> replays it (the options from its head), and the
/// player's animation is written tick by tick to <c>dump.tsv</c> in the folder (by default <c>&lt;tape&gt;-dump</c> beside the tape):
/// the role, the clip and its time, the clip blended from and the blend, the legs' yaw and mode, the facing, the speed across, the
/// crouch, the place and the action (<see cref="Columns"/>: a column is one line there). At the ticks <c>--dump-stills</c> names (a
/// tick, or a range with a step: <c>300-420:10,600</c>; ticks counted from 1, as the TSV's) a still is drawn from the player's camera,
/// <c>t00300.png</c> …; only then are the people's maps decoded. The tape's digests are not compared (a tape made on another install
/// parts from them, and its player's motion is still his); another set of modules is said and the tape dumped anyway. Exit 0, or 1
/// with a plain message for a tape that is not there or cannot be read, a range that is not one, or a picture size that is not one.
/// </summary>
public static class ReplayDumpCommand
{
    /// <summary>The file the rows go to, in the dump's folder.</summary>
    public const string TableFile = "dump.tsv";

    /// <summary>What a column reads: the session after the tick, the player's animation, legs and body, and a clip's name by its index.</summary>
    public readonly record struct Row(Session Session, CharacterAnimation Anim, PoseRig Rig, Body Body, Func<int, string> Clip);

    const float Deg = 57.29578f;

    static string F(float v, string format) => v.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>
    /// The table's columns, in order: a header and how its value is written. Adding a column is one line here (lane A's turn chain
    /// for one). Kept as the earlier diagnostic wrote them.
    /// </summary>
    public static readonly (string Name, Func<Row, string> Value)[] Columns =
    [
        ("tick", r => r.Session.Tick.ToString(CultureInfo.InvariantCulture)),
        ("s", r => (r.Session.Tick / (double)World.TicksPerSecond).ToString("0.000", CultureInfo.InvariantCulture)),
        ("role", r => r.Anim.Role.ToString()),
        ("clip", r => r.Clip(r.Anim.Clip)),
        ("time", r => F(r.Anim.Time, "0.000")),
        ("prev", r => r.Clip(r.Anim.PreviousClip)),
        ("prevTime", r => F(r.Anim.PreviousTime, "0.000")),
        ("blend", r => F(r.Anim.Blend, "0.00")),
        ("legYaw", r => F(r.Rig.LegYaw * Deg, "0.0")),
        ("legMode", r => r.Rig.LegMode.ToString()),
        ("yaw", r => F(r.Body.Yaw * Deg, "0.0")),
        ("speed", r => F(MathF.Sqrt(r.Body.Velocity.X * r.Body.Velocity.X + r.Body.Velocity.Y * r.Body.Velocity.Y), "0.00")),
        ("crouched", r => r.Body.Crouched.ToString(CultureInfo.InvariantCulture)),
        ("x", r => F(r.Body.Feet.X, "0.00")),
        ("y", r => F(r.Body.Feet.Y, "0.00")),
        ("action", r => r.Anim.Action.ToString()),
        ("actionClip", r => r.Clip(r.Anim.ActionClip)),
        ("z", r => F(r.Body.Feet.Z, "0.000")),                       // the feet's height, for the balls' below
        ("ballL", r => Ball(r, true)),                                // each ball of the foot as drawn, in the world (x,y,z)
        ("ballR", r => Ball(r, false)),
        ("holdL", r => Hold(r, true)),                                // how much each foot is held at its lock (- without locks)
        ("holdR", r => Hold(r, false)),
        ("chest", r => Chain(r, true)),                               // the turn chain's chest and head from the facing (degrees, - without it)
        ("head", r => Chain(r, false)),
        ("waist", r => Waist(r).Waist),                               // the largest turn between neighbours of the pelvis and spine_01 to _03 (degrees)
        ("neck", r => Waist(r).Neck),                                 // the same from spine_03 through neck_01 to head
        ("waistShift", r => Waist(r).Shift),                          // how far spine_01 is off where the pelvis carries it (cm)
        ("line", r => Ground(r, g => F(g.Line, "0.000"))),            // the drawn body's line above the capsule's feet (m; - without the rules)
        ("slope", r => Ground(r, g => F(g.Slope, "0.00"))),           // the slope clips' share (-1 downhill … 1 uphill)
        ("hips", r => F(r.Rig.HipDrop, "0.000")),                     // the hips' drop (m)
        ("cam", r => Camera(r)),                                      // the view's place this tick (x,y,z, m: the third-person camera or the eye)
        ("camYaw", r => F(r.Session.World.Service<Cameras.ViewState>().View.Yaw * Deg, "0.0")),     // the view's yaw and pitch (degrees)
        ("camPitch", r => F(r.Session.World.Service<Cameras.ViewState>().View.Pitch * Deg, "0.0")),
        ("boom", r => F(r.Session.World.Service<Cameras.ViewState>().Boom, "0.000")),               // the third-person boom after the walls (m)
        ("thighs", r => Thighs(r)),                                   // each thigh's raise from straight down as drawn (degrees, l,r)
        ("hem", r => Hem(r)),                                         // a skirt hung from the thighs: its hem's largest stretch between chains (x its rest; - without one)
    ];

    /// <summary>Each thigh's raise from straight down in the drawn pose (degrees, <c>l,r</c>), or - for a body that is not a person.</summary>
    static string Thighs(Row r)
    {
        var world = r.Session.World;
        var assets = world.Service<CharacterAssets>();
        if (assets.Kind(r.Anim.Kind).Person is null) return "-";
        var skeleton = assets.ModelOf(r.Anim.Kind).Skeleton;
        var bones = world.Service<CharacterPoses>().Bones(world, r.Session.Player).ToArray();
        string Raise(string side)
        {
            int thigh = skeleton.Find("thigh_" + side), calf = skeleton.Find("calf_" + side);
            if (thigh < 0 || calf < 0) return "-";
            var d = System.Numerics.Vector3.Normalize(bones[calf].Translation - bones[thigh].Translation);
            return F(MathF.Acos(Math.Clamp(-d.Z, -1f, 1f)) * Deg, "0");
        }
        return Raise("l") + "," + Raise("r");
    }

    /// <summary>The hem's largest stretch of a skirt hung from the thighs as drawn (<see cref="Animation.People.SkirtRule.Stretch"/>), or -.</summary>
    static string Hem(Row r)
    {
        var world = r.Session.World;
        var assets = world.Service<CharacterAssets>();
        if (assets.Kind(r.Anim.Kind).Person?.Skirt is not { } skirt) return "-";
        return F(skirt.Stretch(world.Service<CharacterPoses>().Bones(world, r.Session.Player)), "0.00");
    }

    /// <summary>The view's place this tick (<see cref="Cameras.ViewState.View"/>), <c>x,y,z</c> (m).</summary>
    static string Camera(Row r)
    {
        var p = r.Session.World.Service<Cameras.ViewState>().View.Eye;
        return string.Create(CultureInfo.InvariantCulture, $"{p.X:0.0000},{p.Y:0.0000},{p.Z:0.0000}");
    }

    /// <summary>A column of the player's <see cref="Animation.People.PersonGround"/>, or - in a world without it.</summary>
    static string Ground(Row r, Func<Animation.People.PersonGround, string> value)
    {
        var world = r.Session.World;
        if (!world.TryStore<Animation.People.PersonGround>(out var grounds) || !grounds.Has(r.Session.Player)) return "-";
        return value(grounds.Get(r.Session.Player));
    }

    /// <summary>The turn chain's chest or head (<see cref="Animation.People.PersonTurn"/>) in degrees, or - in a world without the chain.</summary>
    static string Chain(Row r, bool chest)
    {
        var world = r.Session.World;
        if (!world.TryStore<Animation.People.PersonTurn>(out var chains) || !chains.Has(r.Session.Player)) return "-";
        var t = chains.Get(r.Session.Player);
        return F((chest ? t.Chest : t.Head) * Deg, "0.0");
    }

    /// <summary>
    /// How the drawn pose's trunk is turned bone by bone (<see cref="Animation.People.TrunkTurns"/>): the largest turn about the
    /// vertical between neighbouring bones from the pelvis to spine_03 and from spine_03 to the head (degrees), and how far spine_01's
    /// joint is off where the pelvis carries it (cm); - for a body that is not a person.
    /// </summary>
    static (string Waist, string Neck, string Shift) Waist(Row r)
    {
        var world = r.Session.World;
        var assets = world.Service<CharacterAssets>();
        var kind = assets.Kind(r.Anim.Kind);
        if (kind.Person is not { } person) return ("-", "-", "-");
        var skeleton = assets.ModelOf(r.Anim.Kind).Skeleton;
        var t = Animation.People.TrunkTurns.Of(skeleton, person.Rig, world.Service<CharacterPoses>().Bones(world, r.Session.Player));
        return (F(t.Waist * Deg, "0.0"), F(t.Neck * Deg, "0.0"), F(t.Shift * 100f, "0.0"));
    }

    /// <summary>A ball of the foot as the pose draws it, in the world (<c>x,y,z</c>, m), or - for a body without one.</summary>
    static string Ball(Row r, bool left)
    {
        var world = r.Session.World;
        var kind = world.Service<CharacterAssets>().Kind(r.Anim.Kind);
        if (kind.Limbs is not { } limbs) return "-";
        int ball = (left ? limbs.LeftLeg : limbs.RightLeg).Tip;
        if (ball < 0) return "-";
        var p = PoseMath.Transform(world.Service<CharacterPoses>().Bones(world, r.Session.Player)[ball].Translation, kind.Placement(r.Body.Feet, r.Body.Yaw));
        return string.Create(CultureInfo.InvariantCulture, $"{p.X:0.0000},{p.Y:0.0000},{p.Z:0.0000}");
    }

    /// <summary>A foot's hold at its lock (<see cref="Animation.People.PersonFeet"/>), or - in a world without the feet's store.</summary>
    static string Hold(Row r, bool left)
    {
        var world = r.Session.World;
        if (!world.TryStore<Animation.People.PersonFeet>(out var feet) || !feet.Has(r.Session.Player)) return "-";
        var f = feet.Get(r.Session.Player);
        return F(left ? f.LeftHold : f.RightHold, "0.00");
    }

    /// <summary>The table's header line.</summary>
    public static string Header => string.Join('\t', Columns.Select(c => c.Name));

    public static int Run(Cli cli, PlatformSettings settings, HostLog log, string path)
    {
        var tape = ReadTape(path);
        var stills = Ticks(cli.Arg("--dump-stills"));
        (int W, int H) size = (960, 540);
        if (cli.Arg("--picture") is { } picture) size = cli.Size("--picture") ?? throw new SessionException($"--picture is a size such as 960x540, not '{picture}'");
        string outDir = Path.GetFullPath(cli.Arg("--dump-dir") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", Path.GetFileNameWithoutExtension(path) + "-dump"));
        int limit = settings.CharacterTextureLimitFor(cli.Arg("--character-texture-limit"));
        if (cli.Arg("--level") is null && !string.IsNullOrEmpty(tape.Header.Level) && !Directory.Exists(tape.Header.Level) && !Space.BuiltInGrounds.Is(tape.Header.Level))
            throw new SessionException($"--replay-dump: the tape was recorded on {tape.Header.Level}, which is not here: give --level <the same level's folder>");

        var options = SessionOptions.ForTape(tape.Header, cli, settings, log) with { DecodePeopleImages = stills.Count > 0,
                                                                                    StairsDrawLastTick = settings.StairsDrawLastTick || cli.Flag("--stairs-draw-last-tick"),   // presentation, not on the tape
                                                                                    SkirtSplit = settings.SkirtSplit || cli.Flag("--skirt-split"),                    // likewise
                                                                                    PersonSwing = settings.PersonSwingFor(cli) };                                  // likewise
        // The stills drawn this share of the way from the tick before to the tick (1, the tick itself): a picture between ticks, as a
        // screen whose rate is not the tick's draws most of its frames.
        float alpha = 1f;
        if (cli.Arg("--dump-alpha") is { } alphaText
            && !(float.TryParse(alphaText, NumberStyles.Float, CultureInfo.InvariantCulture, out alpha) && alpha >= 0f && alpha <= 1f))
            throw new SessionException($"--dump-alpha is a share from 0 to 1, not '{alphaText}'");
        // --dump-without-notes replays the tape's input with this build's people (the turn, the kneel and the feet from the settings and
        // the flags, not the tape's notes): play as it is now on an older tape, to set beside the tape as it was (--feet-off … apply).
        if (cli.Flag("--dump-without-notes"))
            options = options with { PersonTurn = settings.PersonTurnFor(cli), CrouchKneel = settings.CrouchKneel || cli.Flag("--crouch-kneel"), PersonFeet = settings.PersonFeetFor(cli), PersonGround = settings.PersonGroundFor(cli),
                                     CameraHardWalls = settings.CameraHardWalls || cli.Flag("--camera-hard-walls"),
                                     PersonSprintFrom = settings.SprintFromFor(cli), PersonCrouchHeight = settings.PersonCrouchHeightFor(cli),
                                     PersonStarts = settings.PersonStartsFor(cli),
                                     PersonAir = settings.PersonAirFor(cli),
                                     PersonOver = settings.PersonOverFor(cli),
                                     PersonLedge = settings.PersonLedgeFor(cli),
                                     PersonPrelanding = settings.PersonPrelandingFor(cli),
                                     PersonHands = settings.PersonHandsFor(cli),
                                     PersonCommit = settings.PersonCommitFor(cli),
                                     PersonMoves = settings.PersonMovesFor(cli),
                                     PersonHang = settings.PersonHangFor(cli),
                                     PersonWall = settings.PersonWallFor(cli),
                                     PersonStealth = settings.PersonStealthFor(cli),
                                     PersonGaits = settings.PersonGaitsFor(cli),
                                     PersonPresent = settings.PersonPresentFor(cli) };
        using var session = Session.Create(options);
        var differences = session.Modules.Differences(tape.Header.Modules, out bool sameModules);
        foreach (var d in differences) log.Say($"replay-dump: {d}");
        if (!sameModules) log.Say($"replay-dump: {path} was recorded with other modules than game {session.Modules.Game.Id} loads here; dumped anyway (the motion may not be the recording's)");
        long last = tape.Frames.Count;
        foreach (int t in stills)
            if (t > last) { log.Say($"replay-dump: the tape has {last} ticks; the stills asked past it (from tick {t}) are not drawn"); break; }

        var frame = Frame(cli.Arg("--dump-frame"));
        string? framedName = cli.Arg("--dump-of");     // the framed view on another character (a townsperson)
        var framed = EntityId.None;
        var world = session.World;
        var assets = world.Service<CharacterAssets>();
        string Clip(int c) => c >= 0 && c < assets.ClipCount ? assets.Clip(c).Name : "-";
        Directory.CreateDirectory(outDir);
        PlatformRenderer? renderer = null;
        int drawn = 0;
        try
        {
            using var w = new StreamWriter(Path.Combine(outDir, TableFile));
            w.WriteLine(Header);
            var cells = new string[Columns.Length];
            for (int i = 0; i < tape.Frames.Count; i++)
            {
                session.Step(tape.Frames[i]);
                var pl = session.Player;
                var anim = world.Get<CharacterAnimation>(pl);
                var rig = world.Has<PoseRig>(pl) ? world.Get<PoseRig>(pl) : default;
                var body = world.Get<Body>(pl);
                var row = new Row(session, anim, rig, body, Clip);
                for (int c = 0; c < Columns.Length; c++) cells[c] = Columns[c].Value(row);
                w.WriteLine(string.Join('\t', cells));
                if (!stills.Contains((int)session.Tick)) continue;
                renderer ??= MakeRenderer();
                var view = new FrameView { InterfaceScale = settings.InterfaceScale };
                session.Fill(view, alpha);
                if (framedName is not null && !world.Alive(framed)) framed = Of(world, assets, framedName);
                var on = world.Alive(framed) && world.Has<Body>(framed) ? world.Get<Body>(framed) : body;
                if (frame is { } f) view.View = FrameOn(on.Feet, on.Yaw, f);   // --dump-frame, a side view of the player (or of --dump-of's)
                view.StatusLine = string.Create(CultureInfo.InvariantCulture,
                    $"tick {session.Tick}{(alpha < 1f ? $" (alpha {alpha:0.00})" : "")}  {Clip(anim.Clip)} {anim.Time:0.00}  leg {rig.LegYaw * Deg:0}  yaw {body.Yaw * Deg:0}");
                for (int k = 0; k < Math.Max(1, renderer.StillFrames); k++) renderer.Draw(view, null);
                renderer.SaveScreenshot(Path.Combine(outDir, $"t{session.Tick:00000}.png"));
                drawn++;
            }
        }
        finally { renderer?.Dispose(); }
        log.Say($"replay-dump: {tape.Frames.Count} ticks of {path} to {Path.Combine(outDir, TableFile)}{(drawn > 0 ? $", {drawn} stills at {size.W}x{size.H}" : "")}");
        return 0;

        PlatformRenderer MakeRenderer()
        {
            var made = PlatformRenderer.CreateHeadless(size.W, size.H, new RendererOptions(settings.Driver, settings.Quality, false, Look: Session.LookFor(options.Night),
                                                                                       CharacterTextureLimit: limit));
            made.CharacterTextures = session.Texture;
            made.CursorArt = session.CursorArt;
            made.LoadLevel(options.Level);
            session.PictureAspect = size.W / (float)size.H;
            return made;
        }
    }

    /// <summary>
    /// <c>--dump-frame &lt;metres&gt;[,&lt;degrees&gt;[,&lt;height&gt;[,&lt;at&gt;]]]</c> — the stills' view put on the player from that far (3 m) at that
    /// angle from its facing (90: from its left side; 0 from the front), the eye that high (1.2 m), looking at that height on it (0.8 m), as
    /// <c>--frame-person</c> puts a headless picture's: the side views of the stairs, the starts and stops and the clothes. Null without it.
    /// </summary>
    public static (float Metres, float Degrees, float Height, float At)? Frame(string? text)
    {
        if (text is null) return null;
        var parts = text.Split(',');
        float Part(int i, float fallback)
        {
            if (parts.Length <= i || parts[i].Length == 0) return fallback;
            return float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v
                : throw new SessionException($"--dump-frame is metres[,degrees[,height[,at]]], such as 3,90,1.2,0.8, not '{text}'");
        }
        return (Part(0, 3f), Part(1, 90f), Part(2, 1.2f), Part(3, 0.8f));
    }

    /// <summary>
    /// <c>--dump-of &lt;text&gt;</c> — <c>--dump-frame</c>'s view put on the first character (in the world's order) whose kind's name has the
    /// text in it, any case (<c>person_08</c>, <c>Monk</c>, <c>Civ11female</c>: a townsperson of the crowd, for its clothes), else the player.
    /// </summary>
    public static EntityId Of(World world, CharacterAssets assets, string text)
    {
        var anims = world.Store<CharacterAnimation>();
        var entities = anims.Entities;
        for (int i = 0; i < entities.Length; i++)
            if (assets.Kind(anims.Data[i].Kind).Name.Contains(text, StringComparison.OrdinalIgnoreCase)) return entities[i];
        return EntityId.None;
    }

    /// <summary>The view <see cref="Frame"/> asks for on feet at <paramref name="feet"/> facing <paramref name="yaw"/>.</summary>
    public static ViewPoint FrameOn(System.Numerics.Vector3 feet, float yaw, (float Metres, float Degrees, float Height, float At) f)
    {
        float angle = yaw + f.Degrees / Deg;
        var at = feet + new System.Numerics.Vector3(0f, 0f, f.At);
        var eye = feet + new System.Numerics.Vector3(MathF.Cos(angle) * f.Metres, MathF.Sin(angle) * f.Metres, f.Height);
        var d = at - eye;
        return new ViewPoint(eye, MathF.Atan2(d.Y, d.X), MathF.Atan2(d.Z, MathF.Sqrt(d.X * d.X + d.Y * d.Y)), 45f);
    }

    /// <summary>The tape, or a plain refusal: not there, or not a tape this program can read.</summary>
    public static Tape ReadTape(string path)
    {
        if (!File.Exists(path)) throw new SessionException($"--replay-dump: there is no tape at {Path.GetFullPath(path)}");
        try { return Tape.Read(path); }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or FormatException or NotSupportedException)
        {
            throw new SessionException($"--replay-dump: {Path.GetFullPath(path)} is not a tape this program can read ({e.Message})");
        }
    }

    /// <summary>
    /// The ticks <c>--dump-stills</c> names: a list of a tick (<c>600</c>) or a range with an optional step (<c>300-420</c>,
    /// <c>300-420:10</c>), ticks from 1; empty when not given. Anything else is refused with what is wrong.
    /// </summary>
    public static HashSet<int> Ticks(string? given)
    {
        var ticks = new HashSet<int>();
        if (given is null) return ticks;
        foreach (var raw in given.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = raw.Split(':');
            var ab = range[0].Split('-');
            if (range.Length > 2 || ab.Length > 2 || !Tick(ab[0], out int a) || !Tick(ab.Length > 1 ? ab[1] : ab[0], out int b))
                throw new SessionException($"--dump-stills: '{raw}' is not a tick or a range of ticks (for example 300-420:10,600; ticks from 1)");
            int step = 1;
            if (range.Length > 1 && (!int.TryParse(range[1], NumberStyles.None, CultureInfo.InvariantCulture, out step) || step < 1))
                throw new SessionException($"--dump-stills: '{raw}' has a step that is not a whole number from 1");
            if (b < a) throw new SessionException($"--dump-stills: '{raw}' ends before it starts");
            for (long t = a; t <= b; t += step) ticks.Add((int)t);
        }
        if (ticks.Count == 0) throw new SessionException("--dump-stills names no tick (for example 300-420:10,600)");
        return ticks;

        static bool Tick(string s, out int t) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out t) && t >= 1;
    }
}
