// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The proving ground (ours): the generated movement course
/// (<c>tools/platform/proving-ground/make_course.py</c>), cooked, driven lane by lane from each lane's own start with the
/// body's motor and the human's numbers — one test to each row of the movement design's controls table (3.10) the
/// motor does today: the stride, the stop, the hop, the hurdle, the vault and climb, the mantle, the grab with its
/// pull-up and hang — each on a tap, the obstacle deciding —, the crouch, and the slopes; and the
/// no-climb station's marked tops refused, and told so, beside their twins. The lanes and what the human should do at each are read from the
/// generator's record in the cooked package's copy of the glTF (<c>assets/proving-ground.gltf</c>, its
/// <c>extras.provingGround</c>), so a lane added to the table is driven too. Found by <c>VITOMIR_COURSE_LEVEL</c>
/// (the cooked <c>proving-ground.l2</c>); skipped when it is not set. Also: <c>--start</c> by name.
/// </summary>
public sealed partial class ProvingGroundTests
{
    /// <summary>One lane of the course as the generator recorded it: where its start and obstacle are, and its numbers.</summary>
    sealed record Lane(string Start, string Station, string Kind, string Label, string Expect, Vector3 At,
                       IReadOnlyDictionary<string, float> Numbers)
    {
        public float N(string key, float fallback = float.NaN) => Numbers.TryGetValue(key, out float v) ? v : fallback;
        public float Face => N("face");
        public float Base => N("base", 0f);
        public float Top => N("top", 0f);
        public float Height => N("height", N("riser", 0f));
        public float Depth => N("depth", 2f);
        /// <summary>Thin, by the human's table and the motor: shallower than a top that can be stood on (MantleMinDepth, 0.35 m).</summary>
        public bool Thin => Depth < 0.35f;
        public override string ToString() => Start;
    }

    sealed record CourseData(LevelSpace Level, List<Lane> Lanes);

    static readonly Lazy<CourseData?> Course = new(() =>
    {
        var path = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        if (string.IsNullOrEmpty(path) || !File.Exists(Path.Combine(path, "level.json"))) return null;
        var gltf = Directory.Exists(Path.Combine(path, "assets")) ? Directory.GetFiles(Path.Combine(path, "assets"), "*.gltf").FirstOrDefault() : null;
        if (gltf is null) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(gltf));
        var lanes = new List<Lane>();
        foreach (var l in doc.RootElement.GetProperty("extras").GetProperty("provingGround").GetProperty("lanes").EnumerateArray())
        {
            var numbers = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var p in l.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Number) numbers[p.Name] = p.Value.GetSingle();
            var at = l.GetProperty("at");
            lanes.Add(new Lane(l.GetProperty("start").GetString()!, l.GetProperty("station").GetString()!, l.GetProperty("kind").GetString()!,
                               l.GetProperty("label").GetString()!, l.GetProperty("expect").GetString()!,
                               new Vector3(at[0].GetSingle(), at[1].GetSingle(), at[2].GetSingle()), numbers));
        }
        return new CourseData(LevelSpace.Open(path), lanes);
    });

    static CourseData Need()
    {
        Assert.SkipUnless(Course.Value is not null, "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        return Course.Value!;
    }

    static IEnumerable<Lane> Lanes(params string[] kinds) => Need().Lanes.Where(l => kinds.Contains(l.Kind));

    // ---------------------------------------------------------------- driving a lane

    /// <summary>What a drive did: the moves begun in order, the grabs and drops, the highest the feet rose, the body at the end.</summary>
    sealed class Drove
    {
        public readonly List<TraversalKind> Started = [], Refused = [];
        public int Grabs, Drops;
        public float Highest = float.MinValue, Farthest = float.MinValue, Stood = float.MinValue;
        public Body End;
        public override string ToString() => string.Create(CultureInfo.InvariantCulture,
            $"moves [{string.Join(", ", Started)}], ended at x {End.Feet.X:0.00} z {End.Feet.Z:0.00} ({End.State}{(End.Crouched != 0 ? ", crouched" : "")}), highest z {Highest:0.00} (stood at {Stood:0.00}), farthest x {Farthest:0.00}");
    }

    static readonly Gait[] Paces = [Gait.Walk, Gait.Run, Gait.Sprint];

    static float SpeedOf(Gait gait) => gait switch { Gait.Walk => 1.3f, Gait.Sprint => 5.5f, _ => 3.0f };

    /// <summary>Ticks to cover <paramref name="metres"/> at a gait, and two seconds more.</summary>
    static int TicksFor(float metres, Gait gait) => (int)((metres / SpeedOf(gait) + 2f) * 120f);

    /// <summary>
    /// Drives a body from <paramref name="lane"/>'s start along +X at a gait for <paramref name="ticks"/>, with the
    /// buttons <paramref name="buttons"/> gives each tick (from the tick, the body and the tick jump was first pressed).
    /// </summary>
    static Drove Go(Lane lane, Gait gait, int ticks, Func<int, Body, MoveIntent, MoveIntent>? buttons = null)
    {
        var level = Need().Level;
        var d = new Drove();
        // From the lane's start as the level has it (the feet 2 cm over the ground; a body put exactly on
        // a flat floor walks as well, MotorReachTests).
        Assert.True(level.TryFindStart(lane.Start, out int s, out string error), error);
        var body = Drive.Settle(level, level.Starts.ElementAt(s).Position);
        var move = default(Traversal);
        d.End = Drive.Moves(level, body, ref move, ticks, (i, b) =>
        {
            var m = Drive.Toward(1, 0, gait);
            return buttons is null ? m : buttons(i, b, m);
        }, (_, b, m, o) =>
        {
            if (o.MoveStarted != TraversalKind.None) d.Started.Add(o.MoveStarted);
            if (o.Refused != TraversalKind.None) d.Refused.Add(o.Refused);
            if (o.Grabbed) d.Grabs++;
            if (o.Dropped) d.Drops++;
            d.Highest = MathF.Max(d.Highest, b.Feet.Z);
            d.Farthest = MathF.Max(d.Farthest, b.Feet.X);
            if (b.State == BodyState.Ground && m.Kind == TraversalKind.None) d.Stood = MathF.Max(d.Stood, b.Feet.Z);
        });
        return d;
    }

    /// <summary>Jump pressed once the feet are at <paramref name="x"/> on the ground and held for <paramref name="hold"/> ticks (1: a tap).</summary>
    static Func<int, Body, MoveIntent, MoveIntent> PressAt(float x, int hold, bool crouch = false)
    {
        int pressed = -1;
        return (i, b, m) =>
        {
            if (pressed < 0 && b.Feet.X >= x && b.State == BodyState.Ground) pressed = i;
            m.Jump = i == pressed;
            m.JumpHeld = pressed >= 0 && i < pressed + hold;
            m.Crouch = crouch;
            return m;
        };
    }

    /// <summary>
    /// The lanes where the body does not do what the table says today, by the test's row, the start and the pace. Each
    /// is held to what it does now, so that a change to the motor that puts it right says so here and the entry is
    /// taken out. None, which put the earlier one right (the headroom on stairs, the 0.3 m walls hurdled, a plain
    /// jump's reach and the hop at 0.4 m, the lip at 2.0 m); a new one goes here with a line saying why.
    /// </summary>
    static readonly Dictionary<string, string> Findings = new(StringComparer.Ordinal)
    {
        // The flag refuses the hop (no move starts), but the tap is then a plain jump, and a plain jump at a walk
        // clears a 0.5 m top and lands on it (it lands on what its feet clear). The probe alone is asked; whether a
        // marked top also refuses a plain jump's landing is still to be decided.
        ["noclimb no-climb-hop walk"] = "a plain jump lands on the marked 0.5 m top",
    };

    static void Expect(List<string> wrong, string row, bool ok, Lane lane, Gait gait, string want, Drove d)
    {
        string pace = gait.ToString().ToLowerInvariant();
        bool known = Findings.ContainsKey($"{row} {lane.Start} {pace}");
        if (!ok && !known) wrong.Add($"{lane.Start} at a {pace}: wanted {want}; {d}");
        if (ok && known) wrong.Add($"{lane.Start} at a {pace} is now as the table says ({want}): take \"{row} {lane.Start} {pace}\" out of Findings");
    }

    /// <summary>
    /// What a sprint vaults (it replaced the hurdle): a thing above the stride to 1.0 m high and 0.7 m deep,
    /// thin or not, unless the level marks it not to be climbed.
    /// </summary>
    static bool SprintVaults(Lane lane) => lane.Kind != "noclimb" && lane.Height > 0.35f && lane.Height <= 1.0f && lane.Depth <= 0.7f + 1e-4f;

    /// <summary>What a run steps over: a thin thing above the stride to 0.5 m.</summary>
    static bool StepsOver(Lane lane) => lane.Kind != "noclimb" && lane.Thin && lane.Height > 0.35f && lane.Height <= 0.5f + 1e-4f;

    static void None(List<string> wrong) => Assert.True(wrong.Count == 0, $"{wrong.Count} lane(s) not as the table says:\n" + string.Join("\n", wrong));

    // ---------------------------------------------------------------- the course itself

    [Fact]
    public void Every_station_and_lane_has_its_start_where_the_generator_put_it_facing_its_obstacle()
    {
        var course = Need();
        var starts = course.Level.Starts.ToList();
        foreach (var lane in course.Lanes)
        {
            Assert.True(course.Level.TryFindStart(lane.Start, out int i, out string error), error);
            Assert.True(Vector3.Distance(starts[i].Position, lane.At) < 0.05f, $"{lane.Start}: the start at {starts[i].Position}, the lane at {lane.At}");
            Assert.Equal(0f, starts[i].Yaw, 3);
            if (lane.Kind != "drop") Assert.True(lane.Face - lane.At.X >= 7.99f, $"{lane.Start}: {lane.Face - lane.At.X:0.00} m of run-up");
            Assert.True(course.Level.TryFindStart(lane.Station, out _, out error), error);
        }
        Assert.Equal("start", starts[0].Name);
        foreach (var station in new[] { "stairs", "kerbs", "slopes", "hop", "hurdle", "vault", "mantle", "grab", "crouch", "doors", "gaps", "drops", "pit", "free-yard", "not-built" })
            Assert.True(course.Level.TryFindStart(station, out _, out _), station);
    }

    [Fact]
    public void The_free_yard_is_a_free_place_inside_a_public_course_and_its_start_stands_outside_it()
    {
        var course = Need();
        var places = course.Level.Places;
        Assert.True(course.Level.TryFindStart("free-yard", out int i, out _));
        var outside = course.Level.Starts.ElementAt(i).Position;
        Assert.Equal(PlaceKind.Public, places.At(outside));
        var inside = course.Lanes.First(l => l.Station == "free-yard").At;
        Assert.Equal(PlaceKind.Free, places.At(inside));
        Assert.Equal(PlaceKind.Public, places.At(course.Level.Starts.First().Position));
    }

    // ---------------------------------------------------------------- the stride and the stop

    [Fact]
    public void Stairs_kerbs_and_doorways_to_0_35_m_are_taken_in_stride_up_and_down_at_every_pace()
    {
        var wrong = new List<string>();
        foreach (var lane in Lanes("stairs", "kerb", "door").Where(l => l.Expect == "stride"))
            foreach (var gait in Paces)
            {
                var d = Go(lane, gait, TicksFor(lane.N("cross") - lane.At.X, gait));
                Expect(wrong, "stride", d.Started.Count == 0 && d.Farthest >= lane.N("cross") && d.Highest >= lane.Base + lane.Top - 0.05f, lane, gait, "over it in stride", d);
            }
        None(wrong);
    }

    [Fact]
    public void Moving_into_anything_higher_stops_at_it_sliding_along_at_any_pace_unless_a_sprint_vaults_it_or_a_run_steps_over_it()
    {
        var wrong = new List<string>();
        var lanes = Lanes("stairs", "kerb", "block", "table", "canopy", "yard").Where(l => l.Height > 0.35f && !l.Numbers.ContainsKey("headroom") && l.Expect != "neutral");
        foreach (var lane in lanes)
            foreach (var gait in Paces)
            {
                if (gait == Gait.Sprint && SprintVaults(lane)) continue;     // the sprint vault's
                if (gait != Gait.Walk && StepsOver(lane)) continue;           // the run's step-over
                var d = Go(lane, gait, TicksFor(lane.Face - lane.At.X, gait));
                Expect(wrong, "stop", d.Started.Count == 0 && d.End.Feet.X < lane.Face - 0.25f && d.Highest < lane.Base + 0.05f, lane, gait, "a stop at it", d);
            }
        None(wrong);
    }

    // ---------------------------------------------------------------- the hop and the hurdle

    [Fact]
    public void A_tap_of_jump_close_to_a_top_to_0_6_m_that_can_be_stood_on_hops_onto_it_and_higher_climbs_onto_it()
    {
        var wrong = new List<string>();
        foreach (var lane in Lanes("kerb", "block", "table").Where(l => !l.Thin && l.Height > 0.35f && l.Height <= 0.8f))
        {
            // Measured from the ground: a top of 0.6 m is the hop's; higher, the tap's jump climbs onto it.
            bool hops = lane.Height <= 0.6f + 1e-3f;
            var want = hops ? TraversalKind.HopUp : TraversalKind.ClimbOnto;
            var d = Go(lane, Gait.Walk, TicksFor(lane.Face - lane.At.X + 2f, Gait.Walk), PressAt(lane.Face - 0.6f, 1));
            Expect(wrong, "hop", d.Started.SequenceEqual([want]) && d.Stood >= lane.Top - 0.02f, lane, Gait.Walk, hops ? "a hop up onto it" : "a climb onto it", d);
        }
        None(wrong);
    }

    [Fact]
    public void Sprinting_into_a_thing_to_a_metre_high_and_0_7_m_deep_with_ground_beyond_vaults_it_and_nothing_else_is_vaulted()
    {
        // The sprint vault replaced the hurdle of thin things; the kind keeps the name Hurdle.
        var wrong = new List<string>();
        foreach (var lane in Lanes("block", "kerb", "table", "yard").Where(l => l.Height > 0.35f && l.Height <= 1.3f))
        {
            bool hurdles = SprintVaults(lane);
            var d = Go(lane, Gait.Sprint, TicksFor(lane.N("far") + 3f - lane.At.X, Gait.Sprint));
            Expect(wrong, "hurdle", hurdles ? d.Started.SequenceEqual([TraversalKind.Hurdle]) && d.End.Feet.X > lane.N("far") + 0.3f && d.End.Feet.Z < 0.02f
                                  : !d.Started.Contains(TraversalKind.Hurdle), lane, Gait.Sprint, hurdles ? "a hurdle" : "no hurdle", d);
        }
        None(wrong);
    }

    // ---------------------------------------------------------------- the pace's stations

    [Fact]
    public void The_step_over_station_s_rails_are_stepped_over_at_a_run_to_half_a_metre_vaulted_at_a_sprint_and_stop_a_walk()
    {
        var wrong = new List<string>();
        foreach (var lane in Lanes("rail"))
            foreach (var gait in Paces)
            {
                var d = Go(lane, gait, TicksFor(lane.N("far") + 3f - lane.At.X, gait));
                var want = gait == Gait.Sprint && SprintVaults(lane) ? TraversalKind.Hurdle
                         : gait != Gait.Walk && StepsOver(lane) ? TraversalKind.StepOver : TraversalKind.None;
                bool ok = want == TraversalKind.None
                    ? d.Started.Count == 0 && d.End.Feet.X < lane.Face - 0.25f && d.Highest < lane.Base + 0.05f
                    : d.Started.SequenceEqual([want]) && d.End.Feet.X > lane.N("far") + 0.3f && d.End.Feet.Z < 0.02f && d.Stood < 0.02f;
                Expect(wrong, "stepover", ok, lane, gait, want == TraversalKind.None ? "a stop at it" : $"a {want} over it, never onto", d);
            }
        None(wrong);
    }

    [Fact]
    public void The_sprint_vault_station_is_vaulted_at_a_sprint_never_onto_and_its_deep_block_and_a_walk_or_a_run_stop()
    {
        var wrong = new List<string>();
        foreach (var lane in Lanes("vaultable"))
            foreach (var gait in Paces)
            {
                var d = Go(lane, gait, TicksFor(lane.N("far") + 3f - lane.At.X, gait));
                bool vaults = gait == Gait.Sprint && SprintVaults(lane);
                bool ok = vaults
                    ? d.Started.SequenceEqual([TraversalKind.Hurdle]) && d.End.Feet.X > lane.N("far") + 0.3f && d.End.Feet.Z < 0.02f && d.Stood < 0.02f
                    : d.Started.Count == 0 && d.End.Feet.X < lane.Face - 0.25f && d.Highest < lane.Base + 0.05f;
                Expect(wrong, "sprintvault", ok, lane, gait, vaults ? "a vault over it, never onto" : "a stop at it", d);
            }
        Assert.Equal(["bench", "trough", "fence", "block"], Lanes("vaultable").Select(l => l.Label));
        None(wrong);
    }

    // ---------------------------------------------------------------- a tap (held before): vault, climb onto, mantle, grab

    [Fact]
    public void A_tap_at_0_6_to_1_2_m_vaults_a_thin_thing_and_climbs_onto_a_deep_one()
    {
        var wrong = new List<string>();
        foreach (var lane in Lanes("block", "table").Where(l => l.Height > 0.6f && l.Height <= 1.2f))
            foreach (var gait in new[] { Gait.Walk, Gait.Run })
            {
                var want = lane.Thin ? TraversalKind.Vault : TraversalKind.ClimbOnto;
                var d = Go(lane, gait, TicksFor(lane.N("far") + 3f - lane.At.X, gait), PressAt(lane.Face - (gait == Gait.Walk ? 0.55f : 0.9f), 1));
                bool there = lane.Thin ? d.End.Feet.X > lane.N("far") + 0.2f && d.End.Feet.Z < 0.02f : d.Stood >= lane.Top - 0.02f;
                Expect(wrong, "vault", d.Started.FirstOrDefault() == want && there, lane, gait, want == TraversalKind.Vault ? "a vault over it" : "a climb onto it", d);
            }
        None(wrong);
    }

    [Fact]
    public void A_tap_at_a_top_of_1_2_to_1_9_m_that_can_be_stood_on_mantles_onto_it()
    {
        var wrong = new List<string>();
        foreach (var lane in Lanes("block").Where(l => !l.Thin && l.Height > 1.2f && l.Height <= 1.9f))
            foreach (var gait in new[] { Gait.Walk, Gait.Run })
            {
                var d = Go(lane, gait, TicksFor(lane.Face - lane.At.X + 2f, gait), PressAt(lane.Face - (gait == Gait.Walk ? 0.55f : 0.9f), 1));
                Expect(wrong, "mantle", d.Started.FirstOrDefault() == TraversalKind.Mantle && d.Stood >= lane.Top - 0.02f,
                       lane, gait, "a mantle onto it", d);
            }
        None(wrong);
    }

    [Fact]
    public void A_tap_grabs_a_lip_to_2_8_m_and_forward_held_pulls_up_and_higher_is_not_reached()
    {
        var wrong = new List<string>();
        foreach (var lane in Lanes("block").Where(l => !l.Thin && l.Height > 1.9f && l.Station != "free-yard"))
        {
            bool reaches = lane.Height <= 2.8f;
            var d = Go(lane, Gait.Walk, TicksFor(lane.Face - lane.At.X + 3f, Gait.Walk), PressAt(lane.Face - 0.55f, 1));
            Expect(wrong, "grab", reaches ? d.Grabs == 1 && d.Started.Contains(TraversalKind.PullUp) && d.Stood >= lane.Top - 0.02f
                                  : d.Grabs == 0 && d.End.Feet.Z < 0.02f, lane, Gait.Walk, reaches ? "a grab and a pull-up" : "nothing reached", d);
        }
        None(wrong);
    }

    [Fact]
    public void The_canopy_with_no_room_above_is_held_and_hung_from_and_crouch_lets_go()
    {
        var wrong = new List<string>();
        foreach (var lane in Lanes("canopy"))
        {
            var held = Go(lane, Gait.Walk, TicksFor(lane.Face - lane.At.X + 3f, Gait.Walk), PressAt(lane.Face - 0.55f, 1));
            Expect(wrong, "canopy", held.Grabs == 1 && !held.Started.Contains(TraversalKind.PullUp) && held.End.State == BodyState.Hanging, lane, Gait.Walk, "a grab and a hang", held);
            int hung = -1;
            var press = PressAt(lane.Face - 0.55f, 1);
            var dropped = Go(lane, Gait.Walk, TicksFor(lane.Face - lane.At.X + 5f, Gait.Walk), (i, b, m) =>
            {
                m = press(i, b, m);
                if (hung < 0 && b.State == BodyState.Hanging) hung = i;
                m.Crouch = hung >= 0 && i > hung + 60;
                return m;
            });
            Expect(wrong, "canopy", dropped.Drops == 1 && dropped.End.Feet.Z < 0.02f && dropped.End.State == BodyState.Ground, lane, Gait.Walk, "a hang and a drop", dropped);
        }
        None(wrong);
    }

    [Fact]
    public void Nothing_held_at_the_grab_s_end_hangs_at_a_wall_of_2_6_m_and_jump_then_pulls_up()
    {
        var lane = Need().Lanes.First(l => l.Station == "grab" && MathF.Abs(l.Height - 2.6f) < 0.01f);
        bool took = false;
        int hung = -1;
        var d = Go(lane, Gait.Walk, TicksFor(lane.Face - lane.At.X + 4f, Gait.Walk), (i, b, m) =>
        {
            bool near = b.Feet.X >= lane.Face - 0.55f;
            if (b.State is not BodyState.Air and not BodyState.Ground) took = true;
            if (hung < 0 && b.State == BodyState.Hanging) hung = i;
            if (!near) return m;
            // A tap on the ground, forward let off once the hands take the lip (forward held would pull up).
            bool pressed = !took && b.State == BodyState.Ground;
            m.Jump = hung >= 0 ? i == hung + 60 : pressed;
            m.JumpHeld = m.Jump;
            if (took && (hung < 0 || i < hung + 60)) m = m with { Direction = Vector2.Zero };
            return m;
        });
        Assert.True(d.Started.SequenceEqual([TraversalKind.Grab, TraversalKind.Hang, TraversalKind.PullUp]), d.ToString());
        Assert.True(d.Stood >= 2.58f, d.ToString());
    }

    // ---------------------------------------------------------------- crouch and headroom

    [Fact]
    public void Crouched_a_body_passes_tunnels_doors_and_a_stair_down_to_its_crouched_height_and_standing_it_is_stopped()
    {
        var wrong = new List<string>();
        foreach (var lane in Lanes("tunnel", "door", "stairs").Where(l => l.Expect == "crouch" || l.Kind == "tunnel"))
        {
            float clear = lane.Kind == "tunnel" ? lane.N("clear") : lane.Kind == "door" ? lane.N("height") : lane.N("headroom");
            bool passes = clear >= 1.15f;
            var crouched = Go(lane, Gait.Walk, TicksFor(lane.N("cross") - lane.At.X, Gait.Walk) * 2, (i, b, m) => m with { Crouch = true });
            Expect(wrong, "crouch", passes == crouched.Farthest >= lane.N("cross"), lane, Gait.Walk, passes ? "through, crouched" : "stopped, crouched", crouched);
            // A stair under a slab its height or more over the nosings (1.8 m) is walked standing (the head is
            // the body's height over the ground under its middle); the course colours a stair for a crouch only under
            // the body's height (the human's 2.0 m stays the levels' headroom, a stair under it a finding of theirs).
            bool upright = lane.Kind == "stairs" && clear >= Drive.Numbers.Height;
            var standing = Go(lane, Gait.Run, TicksFor(lane.N("cross") - lane.At.X, Gait.Run));
            Expect(wrong, "crouch", upright == standing.Farthest >= lane.N("cross") && standing.Started.Count == 0, lane, Gait.Run,
                   upright ? "through, standing" : "stopped, standing", standing);
        }
        None(wrong);
    }

    [Fact]
    public void A_doorway_narrower_than_the_body_stops_it_and_a_wider_one_is_walked_through()
    {
        var wrong = new List<string>();
        foreach (var lane in Lanes("door").Where(l => l.N("height") >= 1.85f))
        {
            bool passes = lane.N("width") > 0.6f;
            var d = Go(lane, Gait.Walk, TicksFor(lane.N("cross") - lane.At.X, Gait.Walk));
            Expect(wrong, "doors", passes == d.Farthest >= lane.N("cross"), lane, Gait.Walk, passes ? "through" : "stopped", d);
        }
        None(wrong);
    }

    // ---------------------------------------------------------------- slopes

    [Fact]
    public void Ramps_to_40_degrees_are_walked_to_the_top_and_steeper_ones_are_not()
    {
        var wrong = new List<string>();
        foreach (var lane in Lanes("ramp"))
            foreach (var gait in new[] { Gait.Walk, Gait.Run })
            {
                bool walks = lane.N("degrees") <= 40f;
                var d = Go(lane, gait, TicksFor(lane.N("far") - lane.At.X, gait) * 2);
                Expect(wrong, "slopes", walks ? d.Highest >= lane.Top - 0.05f && d.Started.Count == 0 : d.Highest < lane.Top - 0.5f, lane, gait,
                       walks ? "up to the top" : "not up", d);
            }
        None(wrong);
    }

    // ---------------------------------------------------------------- the no-climb station

    [Fact]
    public void The_no_climb_stations_marked_tops_refuse_every_move_and_their_twins_are_taken()
    {
        var wrong = new List<string>();
        var station = Need().Lanes.Where(l => l.Station == "no-climb").ToList();
        Assert.True(station.Count(l => l.Kind == "noclimb") >= 4 && station.Count(l => l.Kind == "block") >= 3, "the station's lanes: three marked by their mesh, one by an area, three twins");
        foreach (var lane in station)
        {
            bool marked = lane.Kind == "noclimb";
            bool hop = lane.Height <= 0.6f;
            // As the hop, mantle and grab rows drive them: a tap close to a hop top, a mantle's or a grab's.
            var d = hop ? Go(lane, Gait.Walk, TicksFor(lane.Face - lane.At.X + 2f, Gait.Walk), PressAt(lane.Face - 0.6f, 1))
                        : Go(lane, Gait.Walk, TicksFor(lane.Face - lane.At.X + 3f, Gait.Walk), PressAt(lane.Face - 0.55f, 1));
            var want = hop ? TraversalKind.HopUp : lane.Height <= 1.9f ? TraversalKind.Mantle : TraversalKind.Grab;
            // Marked: no move, and the refusal told once (MoveRefused).
            bool ok = marked ? d.Started.Count == 0 && d.Grabs == 0 && d.Stood < lane.Top - 0.1f && d.End.Feet.X < lane.Face && d.Refused.SequenceEqual([want])
                             : d.Started.FirstOrDefault() == want && d.Stood >= lane.Top - 0.02f && d.Refused.Count == 0;
            Expect(wrong, "noclimb", ok, lane, Gait.Walk, marked ? "no move: a stop at it" : $"a {want} onto it", d);
        }
        None(wrong);
    }

    // ---------------------------------------------------------------- the no-stand station

    /// <summary>
    /// The no-stand station's lanes (make_course.py): a marked top is never stood on —
    /// a plain jump onto the 0.5 m one (mesh or area) gets there and slides off, the 1.5 m one is not mantled, walking up
    /// the stair stops at the marked platform's edge at every pace, and the thin marked wall is still vaulted — while
    /// each twin is hopped onto, mantled or walked on.
    /// </summary>
    [Fact]
    public void The_no_stand_stations_marked_tops_are_never_stood_on_and_their_twins_are()
    {
        var wrong = new List<string>();
        var course = Need();
        Assert.True(course.Level.TryFindStart("no-stand", out _, out string error), error);
        var station = course.Lanes.Where(l => l.Station == "no-stand").ToList();
        Assert.True(station.Count(l => l.Kind == "nostand") >= 5 && station.Count(l => l.Kind != "nostand") >= 3,
                    "the station's lanes: four marked by their mesh, one by an area, three twins");
        foreach (var lane in station)
        {
            bool marked = lane.Kind == "nostand";
            if (lane.Label.StartsWith("stair", StringComparison.Ordinal))
            {
                float head = lane.N("head");
                foreach (var gait in Paces)
                {
                    var d = Go(lane, gait, TicksFor(lane.N("far") - lane.At.X + 2f, gait));
                    bool ok = marked ? d.End.State == BodyState.Ground && MathF.Abs(d.End.Feet.Z - lane.Top) < 0.03f
                                       && d.Farthest <= head + 0.02f && d.Farthest > head - 0.4f && d.Started.Count == 0
                                     : d.Farthest > head + 2f;
                    Expect(wrong, "nostand", ok, lane, gait, marked ? "a stop at the marked platform's edge" : "walked onto the platform", d);
                }
                continue;
            }
            bool low = lane.Height <= 0.6f;
            // As the hop, mantle and vault rows drive them: a tap close to a low top; jump held through to a mantle or a vault.
            var drove = low ? Go(lane, Gait.Walk, TicksFor(lane.Face - lane.At.X + 2f, Gait.Walk), PressAt(lane.Face - 0.6f, 1))
                            : Go(lane, Gait.Walk, TicksFor(lane.Face - lane.At.X + 3f, Gait.Walk), PressAt(lane.Face - 0.55f, 200));
            string want;
            bool fine;
            if (lane.Thin)
            {
                want = "a vault over it";
                fine = drove.Started.FirstOrDefault() == TraversalKind.Vault && drove.Farthest > lane.N("far") + 0.3f && drove.End.Feet.Z < 0.05f;
            }
            else if (marked)
            {
                want = low ? "a plain jump onto it, then a slide off it" : "no move onto it";
                fine = drove.Stood < lane.Top - 0.1f && drove.End.Feet.Z < 0.05f && drove.End.Feet.X < lane.Face
                       && !drove.Started.Contains(TraversalKind.HopUp) && !drove.Started.Contains(TraversalKind.Mantle)
                       && (!low || drove.Highest >= lane.Top - 0.02f);
            }
            else
            {
                var move = low ? TraversalKind.HopUp : TraversalKind.Mantle;
                want = $"a {move} onto it";
                fine = drove.Started.FirstOrDefault() == move && drove.Stood >= lane.Top - 0.02f;
            }
            Expect(wrong, "nostand", fine, lane, Gait.Walk, want, drove);
        }
        None(wrong);
    }

    // ---------------------------------------------------------------- --start by name (needs no course)

    [Fact]
    public void A_start_is_chosen_by_number_or_by_name_and_an_unknown_name_is_refused_with_the_level_s_names()
    {
        var floor = new TestLevel().Floor(-5, -5, 5, 5).Build();
        LevelAnchor Start(string name, float x) => new($"t/starts/{name}", "start", name, new Vector3(x, 0, 0), 0f, 0);
        var level = new LevelSpace("course", floor.Collision, null,
            [Start("start", 0), new LevelAnchor("t/things/0", "prop", "mantle", Vector3.Zero, 0f, 0), Start("kerbs", 1), Start("Mantle", 2)]);
        Assert.True(level.TryFindStart("2", out int i, out _));
        Assert.Equal(2, i);
        Assert.True(level.TryFindStart("99", out i, out _));
        Assert.Equal(2, i);                                    // held to the list's end, as before
        Assert.True(level.TryFindStart(null, out i, out _));
        Assert.Equal(0, i);
        Assert.True(level.TryFindStart("mantle", out i, out _));
        Assert.Equal(2, i);                                    // a start's name, not another anchor's; case does not matter
        Assert.True(level.TryFindStart(" KERBS ", out i, out _));
        Assert.Equal(1, i);
        Assert.False(level.TryFindStart("stairs", out _, out string error));
        Assert.Equal("--start stairs: the level course has no start of that name; its starts are: start, kerbs, Mantle", error);
    }
    [Fact]
    public void The_course_s_stairs_and_kerbs_met_at_a_run_or_a_sprint_at_0_30_45_and_60_degrees_take_no_pace_move()
    {
        // A walk in play: sprinting diagonally across the foot of the drops station's way up (risers 0.2 m,
        // treads 0.3 m, open sides; near (28.5, -43.5)) the body vaulted the stair's corner. Every stair and kerb on the
        // course, at a run and a sprint, met square and at 30, 45 and 60 degrees either way, aimed at its middle and
        // towards both sides of its foot: no step-over, no sprint vault (a stair is walked in stride, or stops the body).
        var level = Need().Level;
        var feet = new List<(string Lane, Vector2 Foot)>();
        foreach (var lane in Lanes("stairs", "kerb")) feet.Add((lane.Start, new Vector2(lane.Face, lane.At.Y)));
        foreach (var lane in Lanes("drop"))
        {
            // The way up to the drop's top (the generator's steps_up): its risers 0.2 m, its treads 0.3 m, its foot
            // 3 m and a tread for each riser but one before the edge.
            int n = Math.Max(1, (int)MathF.Round(lane.Base / 0.2f));
            feet.Add((lane.Start + " (its way up)", new Vector2(lane.Face - 3f - (n - 1) * 0.3f, lane.At.Y)));
        }
        Assert.NotEmpty(feet);
        var seen = new List<string>();
        int runs = 0;
        foreach (var (name, foot) in feet)
            foreach (var gait in new[] { Gait.Run, Gait.Sprint })
                foreach (float deg in new[] { 0f, 30f, 45f, 60f, -30f, -45f, -60f })
                    foreach (float aim in new[] { -0.6f, 0f, 0.6f })
                    {
                        var (sin, cos) = DetMath.SinCos(deg * DetMath.DegToRad);
                        var start = foot + new Vector2(0f, aim) - new Vector2(cos, sin) * 4f;
                        var move = default(Traversal);
                        var started = new List<TraversalKind>();
                        Drive.Moves(level, Drive.Settle(level, new(start, 0.02f)), ref move, 240, (_, _) => Drive.Toward(cos, sin, gait),
                                    (_, _, _, o) => { if (o.MoveStarted != TraversalKind.None) started.Add(o.MoveStarted); });
                        runs++;
                        if (started.Any(k => k is TraversalKind.StepOver or TraversalKind.Hurdle))
                            seen.Add(string.Create(CultureInfo.InvariantCulture, $"{name} at {gait}, {deg}° aimed {aim} m aside: [{string.Join(", ", started)}]"));
                    }
        Assert.True(seen.Count == 0, $"{seen.Count} of {runs}: " + string.Join("; ", seen));
    }
}
