// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Gait = Vitomir.Platform.Bodies.Gait;
using static Vitomir.Platform.Tests.PeopleMotion.WallClimbTests;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// <b>The body answers its input at once</b> (a standing test, the earlier build's walls first, other movement states to be added): for each input,
/// the drawn body visibly reacts within <see cref="Budget"/> ticks of the key going down — 4 frames at 60 fps (about 67 ms), 8 ticks of the 120 Hz simulation —
/// "visibly" meaning the pelvis or the leading hand at least <see cref="Visible"/> (5 mm) from where it would have been with no input. Each case is a scene played
/// twice from the same start: once with no input, once with the input from the tick it is pressed (the moment the case names); deterministic, so the two are the
/// same until the press. To add a state: a <see cref="Case"/> (its scene with no input, when the key goes down, what is pressed) in its own fact.
/// </summary>
public sealed class MovementResponseTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.0") => v.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>The frames at 60 fps the body may take to react, in the simulation's ticks (4 × 1/60 s at 120 Hz).</summary>
    public const int Budget = 8;

    /// <summary>How far (m) the pelvis or the leading hand must be from where it would have been for the reaction to show.</summary>
    public const float Visible = 0.005f;

    const float Tick = World.TickSeconds;

    static readonly Vector2 Up = new(1f, 0f), Down = new(-1f, 0f), Left = new(0f, 1f);

    /// <summary>
    /// A case: its name, the world and the scene with no input (<paramref name="Scene"/>), the tick the key goes down found from that run's rows
    /// (<paramref name="PressAt"/>, −1: the case cannot be set up), and the input from then (<paramref name="Pressed"/>: the ticks since the press, the body).
    /// </summary>
    internal sealed record Case(string Name, PersonWallSettings Walls, Scene Scene, Func<List<Row>, LedgePaths, int> PressAt, Func<int, Body, Ask> Pressed);

    /// <summary>
    /// How many ticks from the key going down (the press's own tick counted as the first) until the pelvis or the leading hand is <see cref="Visible"/> from the run
    /// with no input, and how far each was by then (int.MaxValue: never within the run).
    /// </summary>
    internal static (int Ticks, float Pelvis, float Hand, string Clip) Response(CharacterAssets assets, int kind, Case c)
    {
        var paths = WallClimbTests.Paths(assets, kind);
        var calm = Play(assets, kind, c.Walls, c.Scene);
        int press = c.PressAt(calm, paths);
        Assert.True(press > 0 && press < calm.Count - Budget - 2, $"{c.Name}: no moment to press ({Story(calm)})");
        var plan = c.Scene.Plan;
        var hit = Play(assets, kind, c.Walls, c.Scene with { Plan = (i, b) => i < press ? plan(i, b) : c.Pressed(i - press, b) });
        // The leading hand: of the clip the pressed run plays after the press, the hand whose hold it lets go first (both where they let go together).
        string clip = hit[Math.Min(hit.Count - 1, press + 2)].Clip;
        var lc = paths.Clips.FirstOrDefault(x => x.Name == clip);
        var hands = lc?.Holds.Where(h => h.Hand && h.On < 0f).ToList() ?? [];
        float first = hands.Count > 0 ? hands.Min(h => h.Off) : 0f;
        bool left = hands.Count == 0 || hands.Any(h => h.Limb == 0 && h.Off <= first + 0.02f), right = hands.Count == 0 || hands.Any(h => h.Limb == 1 && h.Off <= first + 0.02f);
        // The two runs the same until the press (else the measure is of something else).
        Assert.True(Vector3.Distance(calm[press - 1].Pelvis, hit[press - 1].Pelvis) < 1e-4f && Vector3.Distance(calm[press - 1].KnuckleL, hit[press - 1].KnuckleL) < 1e-4f
                    && Vector3.Distance(calm[press - 1].KnuckleR, hit[press - 1].KnuckleR) < 1e-4f, $"{c.Name}: the runs part before the press");
        for (int i = press; i < Math.Min(calm.Count, hit.Count); i++)
        {
            float p = Vector3.Distance(calm[i].Pelvis, hit[i].Pelvis);
            float h = MathF.Max(left ? Vector3.Distance(calm[i].KnuckleL, hit[i].KnuckleL) : 0f, right ? Vector3.Distance(calm[i].KnuckleR, hit[i].KnuckleR) : 0f);
            if (p >= Visible || h >= Visible) return (i - press + 1, p, h, clip);
        }
        return (int.MaxValue, 0f, 0f, clip);
    }

    /// <summary>Asserts each case for both bodies within <see cref="Budget"/>, printing each one's ticks.</summary>
    void Within(params Case[] cases)
    {
        var late = new List<string>();
        foreach (var who in new[] { "man", "woman" })
        {
            var (assets, kind) = WallClimbTests.Body(who);
            WallClimbTests.Paths(assets, kind);
            foreach (var c in cases)
            {
                var (ticks, p, h, clip) = Response(assets, kind, c);
                string line = $"{who}, {c.Name}: {(ticks == int.MaxValue ? "no reaction" : $"{ticks} ticks ({F(ticks * Tick * 1000f, "0")} ms)")} into {clip}; the pelvis {F(p * 1000f)} mm, the leading hand {F(h * 1000f)} mm off the run with no input";
                output.WriteLine(line);
                if (ticks > Budget) late.Add(line);
            }
        }
        Assert.True(late.Count == 0, "late: " + string.Join("; ", late));
    }

    // ---------------------------------------------------------------- the walls

    static readonly PersonWallSettings Flowing = new();

    /// <summary>The first tick a row's clip is <paramref name="clip"/> from <paramref name="from"/>, and <paramref name="after"/> ticks on (−1: none).</summary>
    static int When(List<Row> rows, string clip, int after = 12, int from = 0)
    {
        int i = rows.FindIndex(from, r => r.Clip == clip);
        return i < 0 ? -1 : i + after;
    }

    /// <summary>On the wall at rest after a standing getting-on (a tap of jump at the wall, nothing held).</summary>
    static Scene StandingOn(int ticks = 600) => new("on", [HoldsWall(Top)], Before(), (i, b) => new Ask(i < 20 ? Up : Vector2.Zero, Jump: i == 0), ticks);

    /// <summary>On the wall at rest at the top row (five quick moves up held, let go in the fifth).</summary>
    static Scene AtTheTop() => new("top", [HoldsWall(Top)], Before(), (i, b) => new Ask(i < 20 || b.Feet.Z < 1.85f ? Up : Vector2.Zero, Jump: i == 0), 1100);   // no state: played twice

    [Fact]
    public void Wall_inputs_show_within_four_frames_of_the_key_going_down()
    {
        var atRest = (Func<List<Row>, LedgePaths, int>)((rows, _) => When(rows, "wall_idle"));
        Within(
            new Case("the first move after a catch in the air", Flowing,
                new Scene("caught", [HoldsWall(Top)], new Vector3(-6f, 0f, 0f), (i, b) =>
                {
                    bool on = b.State is BodyState.Hanging or BodyState.Traversing;
                    return new Ask(on ? Vector2.Zero : Up, on ? Gait.Run : Gait.Sprint, Jump: b.Feet.X > -1.8f && b.Feet.X < -1.3f && b.State == BodyState.Ground);
                }, 500),
                atRest, (t, b) => new Ask(Up)),
            new Case("the first move after a standing grab", Flowing, StandingOn(), atRest, (t, b) => new Ask(Up)),
            new Case("a chained move while the key is held", Flowing,
                // Up held into the first move and let go in it: with no input it ends in the idle; pressed again after its last limb's hold, the next flows on.
                new Scene("chain", [HoldsWall(Top)], Before(), (i, b) => new Ask(i < 20 || (b.Feet.Z < 0.2f && b.State == BodyState.Traversing) ? Up : Vector2.Zero, Jump: i == 0), 700),
                (rows, paths) =>
                {
                    int s = rows.FindIndex(r => r.Began.StartsWith("Move ", StringComparison.Ordinal));
                    if (s < 0) return -1;
                    var c = paths.Clips.First(x => x.Name == rows[s].Clip);
                    float lastOn = c.Holds.Where(h => h.On >= 0f && h.On < c.Duration).Max(h => h.On);
                    return s + (int)MathF.Ceiling((lastOn + Flowing.Rate * Tick) / (Flowing.Rate * Tick)) + 1;
                },
                (t, b) => new Ask(Up)),
            new Case("over the top", Flowing, AtTheTop(), atRest, (t, b) => new Ask(Up)),
            new Case("a diagonal", Flowing, StandingOn(), atRest, (t, b) => new Ask(Vector2.Normalize(Up + Left))),
            new Case("let go", Flowing, StandingOn(), atRest, (t, b) => new Ask(Vector2.Zero, Crouch: true)),
            new Case("push off", Flowing, StandingOn(), atRest, (t, b) => new Ask(t == 0 ? Down : Vector2.Zero, Jump: t == 0)),
            // A move refused (the face ends 0.55 m to the right: no hold there, the corner not yet in reach) shows the intent: the right hand reaches.
            new Case("a move refused (no hold that way)", Flowing,
                new Scene("refused", [new Wall(new Vector3(1f, -0.55f, 0f), new Vector3(3f, 3f, Top))], Before(), (i, b) => new Ask(i < 20 ? Up : Vector2.Zero, Jump: i == 0), 600),
                atRest, (t, b) => new Ask(-Left)));
    }

    // ---------------------------------------------------------------- squeezing, stealth with cover, ducking

    /// <summary>
    /// A case of the squeeze, the cover, the stealth toggle or the duck (SqueezeCoverTests' worlds): its name, the world made afresh for each run, the scene
    /// with no input, the tick the key goes down (from that run's rows; −1 none), the input from then, and the run's length.
    /// </summary>
    internal sealed record SideCase(string Name, Func<CharacterAssets, int, (World World, EntityId Body)> Make, Func<int, Body, SqueezeCoverTests.Ask> Scene,
                                    Func<List<SqueezeCoverTests.Row>, int> PressAt, Func<int, Body, SqueezeCoverTests.Ask> Pressed, int Ticks);

    /// <summary>As <see cref="Response"/> for a <see cref="SideCase"/>: the pelvis or either hand <see cref="Visible"/> from the run with no input.</summary>
    internal static (int Ticks, float Pelvis, float Hand, string Clip) Response(CharacterAssets assets, int kind, SideCase c)
    {
        var (w0, e0) = c.Make(assets, kind);
        var calm = SqueezeCoverTests.Drive(w0, e0, assets, kind, c.Scene, c.Ticks);
        int press = c.PressAt(calm);
        Assert.True(press > 0 && press < calm.Count - Budget - 2, $"{c.Name}: no moment to press ({SqueezeCoverTests.Story(calm)})");
        var (w1, e1) = c.Make(assets, kind);
        var hit = SqueezeCoverTests.Drive(w1, e1, assets, kind, (i, b) => i < press ? c.Scene(i, b) : c.Pressed(i - press, b), c.Ticks);
        Assert.True(Vector3.Distance(calm[press - 1].Pelvis, hit[press - 1].Pelvis) < 1e-4f && Vector3.Distance(calm[press - 1].HandL, hit[press - 1].HandL) < 1e-4f,
                    $"{c.Name}: the runs part before the press");
        string clip = hit[Math.Min(hit.Count - 1, press + 2)].Drawn;
        for (int i = press; i < Math.Min(calm.Count, hit.Count); i++)
        {
            float p = Vector3.Distance(calm[i].Pelvis, hit[i].Pelvis);
            float h = MathF.Max(Vector3.Distance(calm[i].HandL, hit[i].HandL), Vector3.Distance(calm[i].HandR, hit[i].HandR));
            if (p >= Visible || h >= Visible) return (i - press + 1, p, h, clip);
        }
        return (int.MaxValue, 0f, 0f, clip);
    }

    void Within(params SideCase[] cases)
    {
        var late = new List<string>();
        foreach (var who in new[] { "man", "woman" })
        {
            var (assets, kind) = WallClimbTests.Body(who);
            SqueezeCoverTests.Paths(assets, kind);
            foreach (var c in cases)
            {
                var (ticks, p, h, clip) = Response(assets, kind, c);
                string line = $"{who}, {c.Name}: {(ticks == int.MaxValue ? "no reaction" : $"{ticks} ticks ({F(ticks * Tick * 1000f, "0")} ms)")} into {clip}; the pelvis {F(p * 1000f)} mm, a hand {F(h * 1000f)} mm off the run with no input";
                output.WriteLine(line);
                if (ticks > Budget) late.Add(line);
            }
        }
        Assert.True(late.Count == 0, "late: " + string.Join("; ", late));
    }

    static readonly PersonStealthSettings Stealth = new();

    /// <summary>The first row after <paramref name="from"/> drawn on <paramref name="clip"/> (its name's end), and <paramref name="after"/> ticks on (−1: none).</summary>
    static int Drawn(List<SqueezeCoverTests.Row> rows, string clip, int after = 12, int from = 0)
    {
        int i = rows.FindIndex(from, r => r.Drawn.EndsWith(clip, StringComparison.Ordinal));
        return i < 0 ? -1 : i + after;
    }

    [Fact]
    public void Squeeze_stealth_cover_and_duck_inputs_show_within_four_frames_of_the_key_going_down()
    {
        var east = new Vector2(1f, 0f);
        var west = new Vector2(-1f, 0f);
        var north = new Vector2(0f, 1f);   // toward the cover wall (y 1) from a body facing −Y: backing
        var south = new Vector2(0f, -1f);  // away from it: forward out of cover
        var none = new SqueezeCoverTests.Ask(Vector2.Zero);
        Func<CharacterAssets, int, (World, EntityId)> open = (a, k) => SqueezeCoverTests.Make(a, k, Stealth, new Vector3(-20f, 0f, 0f), 0f, false);
        Func<CharacterAssets, int, (World, EntityId)> mouth = (a, k) => SqueezeCoverTests.Make(a, k, Stealth, new Vector3(1.62f, 0f, 0f), 0f, false, SqueezeCoverTests.Gap(0.40f));
        Func<CharacterAssets, int, (World, EntityId)> wall = (a, k) => SqueezeCoverTests.Make(a, k, Stealth, new Vector3(1.0f, 0.5f, 0f), -MathF.PI / 2f, true, SqueezeCoverTests.CoverWall());
        // Just out of cover (the back still within reach): standing still does not take it again (SideMove.Rearm); backing does.
        Func<CharacterAssets, int, (World, EntityId)> rearmed = (a, k) =>
        {
            var (w, e) = wall(a, k);
            w.Get<PersonStealth>(e).Move.Rearm = 1;
            return (w, e);
        };
        Within(
            new SideCase("the stealth toggle, standing", open, (i, b) => none, rows => 60, (t, b) => new SqueezeCoverTests.Ask(Vector2.Zero, Toggle: t == 0), 200),
            new SideCase("the stealth toggle, walking", open, (i, b) => new SqueezeCoverTests.Ask(east, Gait.Walk), rows => 200,
                (t, b) => new SqueezeCoverTests.Ask(east, Gait.Walk, Toggle: t == 0), 340),
            new SideCase("into cover (backing to the wall)", rearmed, (i, b) => none, rows => 60, (t, b) => new SqueezeCoverTests.Ask(north), 200),
            new SideCase("out of cover (forward)", wall, (i, b) => none, rows => Drawn(rows, "cover_idle", 30), (t, b) => new SqueezeCoverTests.Ask(south), 500),
            new SideCase("into a squeeze (at the gap's mouth)", mouth, (i, b) => none, rows => 60, (t, b) => new SqueezeCoverTests.Ask(east), 200),
            new SideCase("out of a squeeze (back the way in)", mouth, (i, b) => new SqueezeCoverTests.Ask(i < 10 ? east : Vector2.Zero),
                rows => Drawn(rows, "squeeze_idle", 30), (t, b) => new SqueezeCoverTests.Ask(west), 600),
            new SideCase("a side-step along cover", wall, (i, b) => none, rows => Drawn(rows, "cover_idle", 30), (t, b) => new SqueezeCoverTests.Ask(east), 500),
            new SideCase("ducking in, walking", open, (i, b) => new SqueezeCoverTests.Ask(east, Gait.Walk), rows => 200,
                (t, b) => new SqueezeCoverTests.Ask(east, Gait.Walk, Crouch: true), 340),
            new SideCase("ducking out, crouch-walking", open, (i, b) => new SqueezeCoverTests.Ask(east, Gait.Walk, Crouch: true), rows => 200,
                (t, b) => new SqueezeCoverTests.Ask(east, Gait.Walk), 340));
    }

    // ---------------------------------------------------------------- the ground states (the inertialized joins, the reaction for every one)

    /// <summary>
    /// A case of the ground states (the player's own gaits, <see cref="PlayerGaitTests"/>'s world): its name, the scene with no input (what is asked each tick),
    /// the tick the key goes down, what is asked from then, the run's length.
    /// </summary>
    internal sealed record GroundCase(string Name, Func<int, PlayerGaitTests.Ask> Scene, int PressAt, Func<int, PlayerGaitTests.Ask> Pressed, int Ticks);

    /// <summary>As <see cref="Response"/> for a <see cref="GroundCase"/>: the pelvis or either hand <see cref="Visible"/> from the run with no input.</summary>
    internal static (int Ticks, float Pelvis, float Hand, string Clip) Response(CharacterAssets assets, int kind, GroundCase c)
    {
        var (w0, e0) = PlayerGaitTests.Make(assets, kind);
        var calm = PlayerGaitTests.Drive(w0, e0, assets, kind, (i, b) => c.Scene(i), c.Ticks);
        int press = c.PressAt;
        var (w1, e1) = PlayerGaitTests.Make(assets, kind);
        var hit = PlayerGaitTests.Drive(w1, e1, assets, kind, (i, b) => i < press ? c.Scene(i) : c.Pressed(i - press), c.Ticks);
        Assert.True(Vector3.Distance(calm[press - 1].Pelvis, hit[press - 1].Pelvis) < 1e-4f && Vector3.Distance(calm[press - 1].HandL, hit[press - 1].HandL) < 1e-4f,
                    $"{c.Name}: the runs part before the press");
        string clip = hit[Math.Min(hit.Count - 1, press + 2)].Clip;
        for (int i = press; i < Math.Min(calm.Count, hit.Count); i++)
        {
            float p = Vector3.Distance(calm[i].Pelvis, hit[i].Pelvis);
            float h = MathF.Max(Vector3.Distance(calm[i].HandL, hit[i].HandL), Vector3.Distance(calm[i].HandR, hit[i].HandR));
            if (p >= Visible || h >= Visible) return (i - press + 1, p, h, clip);
        }
        return (int.MaxValue, 0f, 0f, clip);
    }

    void Within(params GroundCase[] cases)
    {
        var late = new List<string>();
        foreach (var who in new[] { "man", "woman" })
        {
            var (assets, kind) = WallClimbTests.Body(who);
            PlayerGaitTests.Gaits(assets, kind);
            foreach (var c in cases)
            {
                var (ticks, p, h, clip) = Response(assets, kind, c);
                string line = $"{who}, {c.Name}: {(ticks == int.MaxValue ? "no reaction" : $"{ticks} ticks ({F(ticks * Tick * 1000f, "0")} ms)")} into {clip}; the pelvis {F(p * 1000f)} mm, a hand {F(h * 1000f)} mm off the run with no input";
                output.WriteLine(line);
                if (ticks > Budget) late.Add(line);
            }
        }
        Assert.True(late.Count == 0, "late: " + string.Join("; ", late));
    }

    /// <summary>
    /// Every ground state's change — the walk, the run and the sprint from standing and into each other both ways, each one's stop, the crouch in
    /// and out standing and walking — shows within <see cref="Budget"/> ticks of the key, its join inertialized (the new clip whole at once).
    /// </summary>
    [Fact]
    public void Ground_state_inputs_show_within_four_frames_of_the_key_going_down()
    {
        var east = new Vector2(1f, 0f);
        PlayerGaitTests.Ask Still(int i) => new(Vector2.Zero);
        Func<int, PlayerGaitTests.Ask> Going(Gait g, bool crouch = false) => i => new(east, g, crouch);
        const int settled = 360, length = 420, standing = 60;
        Within(
            new GroundCase("a walk from standing", Still, standing, Going(Gait.Walk), 120),
            new GroundCase("a run from standing", Still, standing, Going(Gait.Run), 120),
            new GroundCase("a sprint from standing", Still, standing, Going(Gait.Sprint), 120),
            new GroundCase("walking, the run", Going(Gait.Walk), settled, Going(Gait.Run), length),
            new GroundCase("walking, the sprint", Going(Gait.Walk), settled, Going(Gait.Sprint), length),
            new GroundCase("running, the walk", Going(Gait.Run), settled, Going(Gait.Walk), length),
            new GroundCase("running, the sprint", Going(Gait.Run), settled, Going(Gait.Sprint), length),
            new GroundCase("sprinting, the run", Going(Gait.Sprint), settled, Going(Gait.Run), length),
            new GroundCase("sprinting, the walk", Going(Gait.Sprint), settled, Going(Gait.Walk), length),
            new GroundCase("a stop from the walk", Going(Gait.Walk), settled, Still, length),
            new GroundCase("a stop from the run", Going(Gait.Run), settled, Still, length),
            new GroundCase("a stop from the sprint", Going(Gait.Sprint), settled, Still, length),
            new GroundCase("the crouch, standing", Still, standing, i => new(Vector2.Zero, Crouch: true), 120),
            new GroundCase("out of the crouch, standing", i => new(Vector2.Zero, Crouch: true), 240, Still, 300),
            new GroundCase("the crouch, walking", Going(Gait.Walk), settled, Going(Gait.Walk, crouch: true), length),
            new GroundCase("out of the crouch, crouch-walking", Going(Gait.Walk, crouch: true), settled, Going(Gait.Walk), length));
    }
}
