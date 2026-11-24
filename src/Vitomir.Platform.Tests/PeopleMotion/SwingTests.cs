// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// The swing bones' simulator (<see cref="SwingRule"/>, the character models' reference simulator ported) and where it runs
/// (<see cref="PersonSwingSystem"/>): the record read from a glb; on a made-up skeleton (no files) a chain hanging to rest and settling, its
/// length kept, out of a capsule, off the ground, a web's stretch held, 60 steps a second whatever the tick, its reset; with the models
/// stream's files (skipping without <c>VITOMIR_MODELS_DIR</c>) the people's swing in a world: deterministic, nothing digested changed, the
/// switch to the earlier build's. <c>VITOMIR_SWING_DUMP</c> (a folder) also writes a run for <c>tools/platform/swing_compare.py</c> to check against the
/// reference.
/// </summary>
public sealed class SwingTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.0000") => v.ToString(format, CultureInfo.InvariantCulture);

    // ----- a made-up skeleton: root, pelvis at 1 m, and chains hanging from the pelvis -----

    /// <summary>A bone frame whose +Z (the length's way) points down: half a turn about X.</summary>
    static readonly Quaternion Down = new(1f, 0f, 0f, 0f);

    /// <summary>
    /// A skeleton of a root, a pelvis 1 m up, a thigh-like bone and a calf below it (for capsules), and <paramref name="chains"/> chains of
    /// three 0.2 m bones hanging from the pelvis at <paramref name="spread"/> m round it, and its record.
    /// </summary>
    static (Skeleton Skeleton, SwingRecord Record) Made(int chains = 1, float spread = 0.1f, SwingCapsule[]? capsules = null, SwingWeb? web = null,
                                                        Func<SwingChain, int, SwingChain>? chain = null)
    {
        var bones = new List<BoneInfo>
        {
            new("root", -1, BoneTransform.Identity),
            new("pelvis", 0, new BoneTransform(Quaternion.Identity, new Vector3(0, 0, 1f))),
            new("thigh_l", 1, new BoneTransform(Down, new Vector3(-0.3f, 0.3f, 0f))),
            new("calf_l", 2, new BoneTransform(Quaternion.Identity, new Vector3(0, 0, 0.45f))),
        };
        var specs = new List<SwingChain>();
        for (int c = 0; c < chains; c++)
        {
            float a = c * DetMath.TwoPi / chains;
            var names = new string[3];
            for (int j = 0; j < 3; j++)
            {
                names[j] = $"swing_test_{c:00}_{j}";
                bones.Add(j == 0
                    ? new BoneInfo(names[j], 1, new BoneTransform(Down, new Vector3(spread * DetMath.Cos(a), spread * DetMath.Sin(a), 0f)))
                    : new BoneInfo(names[j], bones.Count - 1, new BoneTransform(Quaternion.Identity, new Vector3(0, 0, 0.2f))));
            }
            var spec = new SwingChain { Bones = names, Gravity = 9.8f, Stiffness = 1.5f, Drag = 20f, Radius = 0.035f, Carried = 1f, Lengths = [0.2f, 0.2f, 0.2f], Colliders = "legs" };
            specs.Add(chain?.Invoke(spec, c) ?? spec);
        }
        var skeleton = new Skeleton { Bones = [.. bones], InverseBind = [.. bones.Select(_ => Matrix4x4.Identity)] };
        var record = new SwingRecord(specs, [new("legs", capsules ?? [])], web is null ? [] : [web]);
        return (skeleton, record);
    }

    /// <summary>The skeleton's bones in model space with the pelvis turned by <paramref name="turn"/> and moved by <paramref name="move"/>.</summary>
    static Matrix4x4[] Body(Skeleton skeleton, Quaternion turn = default, Vector3 move = default)
    {
        var local = skeleton.Bones.Select(b => b.Rest).ToArray();
        if (turn != default) local[1] = local[1] with { Rotation = turn };
        local[1] = local[1] with { Translation = local[1].Translation + move };
        var model = new Matrix4x4[local.Length];
        PoseEvaluator.ToModel(skeleton, local, model);
        return model;
    }

    static Vector3 RestTail(Skeleton skeleton, int bone, float length)
    {
        var model = Body(skeleton);
        return model[bone].Translation + SwingRule.Along(model[bone]) * length;
    }

    [Fact]
    public void One_chain_hangs_to_rest_and_settles_keeping_its_length()
    {
        // Not carried by its head (all of the head's travel left to the drag), so it swings when the body is moved.
        var (skeleton, record) = Made(chain: (c, _) => c with { Carried = 0f });
        var rule = SwingRule.For(skeleton, record)!;
        Assert.Equal(1, rule.ChainCount);
        Assert.Equal(3, rule.BoneCount);
        var state = rule.NewState();
        var rest = Body(skeleton);
        rule.Reset(state, rest);
        int last = skeleton.Find("swing_test_00_2");
        var hang = RestTail(skeleton, last, 0.2f);
        Assert.InRange(Vector3.Distance(state.Tails()[2], hang), 0f, 1e-5f);
        // The pelvis tipped 30 degrees and moved 0.3 m for a quarter of a second, then back: the chain swings, then hangs as at rest again.
        float most = 0f, worstLength = 0f;
        for (int k = 0; k < 240; k++)
        {
            float share = k < 15 ? k / 15f : k < 30 ? 1f : 0f;
            var body = Body(skeleton, DetMath.AxisAngle(Vector3.UnitY, share * 30f * DetMath.DegToRad), new Vector3(0, 0.3f * share, 0));
            rule.Step(state, body, 1f / 60f, null);
            var tails = state.Tails();
            most = MathF.Max(most, Vector3.Distance(tails[2], hang));
            // Each bone keeps its length: its tail 0.2 m from its head (the parent's tail, or the pelvis's point).
            var heads = new[] { body[skeleton.Find("swing_test_00_0")].Translation }.Concat(tails[..2]).ToArray();
            for (int j = 0; j < 3; j++) worstLength = MathF.Max(worstLength, MathF.Abs(Vector3.Distance(heads[j], tails[j]) - 0.2f));
        }
        var end = state.Tails()[2];
        float moving = Vector3.Distance(end, state.PreviousTails()[2]);
        output.WriteLine($"swung up to {F(most)} m from its rest; 3 s later {F(Vector3.Distance(end, hang) * 1000f, "0.000")} mm from it, moving {F(moving * 1000f, "0.0000")} mm a step; length kept to {F(worstLength * 1e6f, "0.0")} µm");
        Assert.True(most > 0.05f, "it swung");
        Assert.InRange(Vector3.Distance(end, hang), 0f, 1e-3f);
        Assert.InRange(moving, 0f, 1e-4f);
        Assert.InRange(worstLength, 0f, 2e-5f);
    }

    [Fact]
    public void A_tail_is_put_out_of_a_capsule_and_kept_off_the_ground()
    {
        // A capsule (0.08 m) across where the chain's last bone hangs: its tail goes round it, the chain's radius further off.
        var capsule = new SwingCapsule { From = "thigh_l", To = "calf_l", Radius = 0.08f, Fixed = true };
        var (skeleton, record) = Made(capsules: [capsule]);
        var rule = SwingRule.For(skeleton, record)!;
        Assert.Equal(0.08f, rule.CapsuleRadii[0].Radii[0], 4);   // fixed: kept
        var state = rule.NewState();
        var body = Body(skeleton);
        // The capsule (between the thigh's and the calf's heads) moved across the chain at 0.65 m, 4 cm to its side, between its first and second joints.
        var a = body[1].Translation + new Vector3(0.14f, -0.3f, -0.35f);
        var b = body[1].Translation + new Vector3(0.14f, 0.3f, -0.35f);
        body[2].Translation = a;
        body[3].Translation = b;
        rule.Reset(state, Body(skeleton));
        for (int k = 0; k < 120; k++) rule.Step(state, body, 1f / 60f, null);
        float nearest = float.MaxValue;
        foreach (var t in state.Tails()) nearest = MathF.Min(nearest, Distance(t, a, b));
        output.WriteLine($"tails at least {F(nearest)} m from the capsule's axis (0.08 + 0.035)");
        Assert.InRange(nearest, 0.08f + 0.035f - 1e-3f, 9f);

        // The ground 0.1 m above where the hem hangs (a body crouched to it; the pelvis a little tipped, so no bone is plumb and pushed up its
        // own length): no tail below it and its folds' 12 mm.
        var (plain, plainRecord) = Made();
        var hung = SwingRule.For(plain, plainRecord)!;
        var s2 = hung.NewState();
        var rest = Body(plain, DetMath.AxisAngle(Vector3.UnitY, 10f * DetMath.DegToRad));
        hung.Reset(s2, rest);
        for (int k = 0; k < 120; k++) hung.Step(s2, rest, 1f / 60f, 0.5f);
        float lowest = s2.Tails().Min(t => t.Z);
        output.WriteLine($"lowest tail {F(lowest)} m with the ground at 0.5");
        Assert.InRange(lowest, 0.5f + SwingRule.GroundClear - 1e-3f, 9f);   // (each step's length restored after the push up takes back a little of it)
        // In the air (no ground) it hangs through.
        var s3 = hung.NewState();
        hung.Reset(s3, rest);
        for (int k = 0; k < 120; k++) hung.Step(s3, rest, 1f / 60f, null);
        Assert.True(s3.Tails().Min(t => t.Z) < 0.5f);
    }

    /// <summary>
    /// "at full run, the left thigh would clip through the bottom of the tunic": between steps a limb that has moved into
    /// the cloth the last step left (a running thigh, a tick's worth at 120 Hz) has the carried tails drawn out of it, as a step would put them;
    /// The earlier build's drawing (<c>keepOut</c> off) left them in it. Where nothing is in a capsule the drawing is the step's own.
    /// </summary>
    [Fact]
    public void Between_steps_a_limb_moved_into_the_cloth_has_the_tails_drawn_out_of_it()
    {
        var capsule = new SwingCapsule { From = "thigh_l", To = "calf_l", Radius = 0.08f, Fixed = true };
        var (skeleton, record) = Made(capsules: [capsule]);
        var rule = SwingRule.For(skeleton, record)!;
        var state = rule.NewState();
        // The capsule well clear of the chain while it settles.
        var body = Body(skeleton);
        Vector3 Across(float x) => body[1].Translation + new Vector3(x, 0f, -0.45f);
        void Place(Matrix4x4[] m, float x) { m[2].Translation = Across(x) + new Vector3(0f, -0.3f, 0f); m[3].Translation = Across(x) + new Vector3(0f, 0.3f, 0f); }
        Place(body, 0.6f);
        rule.Reset(state, body);
        for (int k = 0; k < 120; k++) rule.Step(state, body, 1f / 60f, null);
        int n = skeleton.Count;
        var bones = rule.Bones.ToArray();
        float Nearest(Matrix4x4[] posed, Vector3 a, Vector3 b)
        {
            float nearest = float.MaxValue;
            foreach (int bone in bones)
            {
                var tail = posed[bone].Translation + SwingRule.Along(posed[bone]) * 0.2f;
                nearest = MathF.Min(nearest, Distance(tail, a, b));
            }
            return nearest;
        }
        // Drawn on the step's own pose: the same with and without (nothing in the capsule).
        var still = (Matrix4x4[])body.Clone();
        var keptStill = (Matrix4x4[])body.Clone();
        rule.Pose(state, skeleton, still, Span<Matrix4x4>.Empty, 0f, keepOut: false);
        rule.Pose(state, skeleton, keptStill, Span<Matrix4x4>.Empty, 0f, keepOut: true);
        for (int i = 0; i < n; i++) Assert.Equal(still[i], keptStill[i]);
        // Then, before the next step, the capsule moved across the chain's middle, 4 cm to its side.
        var moved = (Matrix4x4[])body.Clone();
        Place(moved, 0.14f);
        var carried = (Matrix4x4[])moved.Clone();
        var kept = (Matrix4x4[])moved.Clone();
        rule.Pose(state, skeleton, carried, Span<Matrix4x4>.Empty, 0f, keepOut: false);
        rule.Pose(state, skeleton, kept, Span<Matrix4x4>.Empty, 0f, keepOut: true);
        Vector3 a = moved[2].Translation, b = moved[3].Translation;
        float inUnkept = Nearest(carried, a, b), now = Nearest(kept, a, b);
        output.WriteLine($"a tail {F(inUnkept)} m from the capsule's axis drawn without the keep-out, {F(now)} m now (the capsule 0.08, the chain's radius 0.035 more)");
        Assert.True(inUnkept < 0.08f, "without the keep-out drawing left a tail inside the capsule");
        // Out of the limb (the step's one correction, its length kept, as a step makes it: the chain's own radius not all regained at once).
        Assert.InRange(now, 0.08f, 9f);
        Assert.True(now > inUnkept + 0.02f);
        // Each bone keeps its length.
        foreach (int bone in bones)
            if (rule.Bones.Contains(skeleton.Bones[bone].Parent))
                Assert.InRange(Vector3.Distance(kept[bone].Translation, kept[skeleton.Bones[bone].Parent].Translation), 0.2f - 1e-4f, 0.2f + 1e-4f);
    }

    static float Distance(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float t = Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector3.Distance(p, a + ab * t);
    }

    [Fact]
    public void A_web_keeps_neighbours_within_their_stretch()
    {
        // Eight chains round the pelvis in a closed web; one of them light and not carried (it lags far behind when the body is jerked away),
        // the rest carried: with stretch 1.15 its neighbours hold it to them, without it it is left behind.
        SwingChain Lagging(SwingChain c, int k) => k == 0 ? c with { Carried = 0f, Drag = 2f } : c;
        float Worst(float stretch)
        {
            var (sk, rec) = Made(chains: 8, spread: 0.15f, web: new SwingWeb([0, 1, 2, 3, 4, 5, 6, 7], true, stretch), chain: Lagging);
            var rule = SwingRule.For(sk, rec)!;
            var state = rule.NewState();
            rule.Reset(state, Body(sk));
            var restTails = state.Tails();
            float worst = 0f;
            for (int k = 0; k < 90; k++)
            {
                float share = Math.Clamp(k / 12f, 0f, 1f);
                rule.Step(state, Body(sk, default, new Vector3(0.4f * share, 0, 0)), 1f / 60f, null);
                var tails = state.Tails();
                for (int c = 0; c < 8; c++)
                    for (int j = 0; j < 3; j++)
                    {
                        int a = c * 3 + j, b = (c + 1) % 8 * 3 + j;
                        worst = MathF.Max(worst, Vector3.Distance(tails[a], tails[b]) / Vector3.Distance(restTails[a], restTails[b]));
                    }
            }
            return worst;
        }
        float held = Worst(1.15f), free = Worst(0f);
        output.WriteLine($"neighbours at most x{F(held, "0.000")} their rest apart with stretch 1.15, x{F(free, "0.000")} without");
        Assert.True(free > 1.5f, "the light chain is left behind");
        Assert.InRange(held, 1f, 1.2f);
    }

    [Fact]
    public void A_body_on_the_stairs_line_is_not_flung_when_its_capsule_steps_a_riser()
    {
        // The model's origin is the capsule's feet; a body drawn on the stairs' line is posed that far above them, and the capsule steps a whole
        // riser in a tick while the line takes it back: in the model's space the whole body jumps 0.25 m. Simulated on the line's level (lift), the
        // chain does not feel it; on the model's own, it is flung (the step after sees the jump as the tail's speed).
        var (skeleton, record) = Made(chains: 4);
        var rule = SwingRule.For(skeleton, record)!;
        float Worst(bool onTheLine)
        {
            // (The pelvis leant 20 degrees, so no bone is plumb and takes a jump along its own length.)
            var lean = DetMath.AxisAngle(Vector3.UnitY, 20f * DetMath.DegToRad);
            var state = rule.NewState();
            var still = rule.NewState();
            rule.Reset(state, Body(skeleton, lean));
            rule.Reset(still, Body(skeleton, lean));
            float worst = 0f;
            for (int k = 0; k < 60; k++)
            {
                float lift = k >= 10 ? 0.25f : 0f;
                rule.Step(state, Body(skeleton, lean, new Vector3(0, 0, lift)), 1f / 60f, 0f, onTheLine ? lift : 0f);
                rule.Step(still, Body(skeleton, lean), 1f / 60f, 0f);
                var a = state.Tails();
                var b = still.Tails();
                for (int i = 0; i < a.Length; i++) worst = MathF.Max(worst, Vector3.Distance(a[i] - new Vector3(0, 0, onTheLine ? 0f : lift), b[i]));
            }
            return worst;
        }
        float line = Worst(true), model = Worst(false);
        output.WriteLine($"a riser stepped: the chain at most {F(line * 1000f, "0.000")} mm from a body that did not move, on the line's level; {F(model * 1000f, "0.0")} mm on the model's");
        Assert.InRange(line, 0f, 1e-5f);
        Assert.True(model > 0.05f);
    }

    [Fact]
    public void Sixty_steps_a_second_whatever_the_tick()
    {
        foreach (int tps in new[] { 30, 60, 120, 240 })
        {
            int steps = 0;
            for (long t = 0; t < tps * 3L; t++) steps += PersonSwingSettings.Due(t + 5, 60, tps);
            Assert.Equal(180, steps);
        }
        Assert.Equal(1, PersonSwingSettings.Due(1, 60, World.TicksPerSecond) + PersonSwingSettings.Due(2, 60, World.TicksPerSecond));   // one in each two ticks
        // The same motion (the pelvis rocked by a function of time) stepped from a 60, 120 and 240 Hz tick: the same tails, bit for bit.
        var (skeleton, record) = Made(chains: 4);
        var rule = SwingRule.For(skeleton, record)!;
        Vector3[] Run(int tps)
        {
            var state = rule.NewState();
            rule.Reset(state, Body(skeleton));
            for (long t = 0; t < tps * 2L; t++)
            {
                int due = PersonSwingSettings.Due(t, 60, tps);
                if (due == 0) continue;
                // The body as it is at the end of the tick, the time of the step (k / 60).
                float time = (t + 1) / (float)tps;
                var body = Body(skeleton, DetMath.AxisAngle(Vector3.UnitX, 0.8f * DetMath.Sin(time * 7f)), new Vector3(0, 0.1f * DetMath.Sin(time * 3f), 0));
                for (int k = 0; k < due; k++) rule.Step(state, body, 1f / 60f, 0f);
            }
            return state.Tails();
        }
        var at60 = Run(60);
        Assert.Equal(at60, Run(120));
        Assert.Equal(at60, Run(240));
    }

    [Fact]
    public void A_reset_puts_the_chain_back_as_its_body_leads_it()
    {
        var (skeleton, record) = Made();
        var rule = SwingRule.For(skeleton, record)!;
        var state = rule.NewState();
        rule.Reset(state, Body(skeleton));
        for (int k = 0; k < 20; k++) rule.Step(state, Body(skeleton, DetMath.AxisAngle(Vector3.UnitY, 1f)), 1f / 60f, null);
        // Begun again on a body turned 90 degrees: each bone as it would be following the pelvis rigidly, not moving.
        var turned = Body(skeleton, DetMath.AxisAngle(Vector3.UnitY, DetMath.HalfPi));
        rule.Reset(state, turned);
        Assert.Equal(0, state.Steps);
        int last = skeleton.Find("swing_test_00_2");
        var lead = turned[1].Translation + Vector3.Transform(new Vector3(0.1f, 0, -0.6f), DetMath.AxisAngle(Vector3.UnitY, DetMath.HalfPi));
        Assert.InRange(Vector3.Distance(state.Tails()[2], lead), 0f, 1e-5f);
        Assert.Equal(state.Tails(), state.PreviousTails());
        Assert.True(last > 0);
    }

    [Fact]
    public void Heavy_stuff_settles_quickly_and_a_long_chain_is_found_by_its_names()
    {
        // Mail (CONVENTIONS' stuffs: 12 / 4 / 30, fall_drag 6, settle 15 within 5 degrees) let go from 25 degrees: within a tenth of it in about 0.3 s.
        var (skeleton, record) = Made(chain: (c, _) => c with { Gravity = 12f, Stiffness = 4f, Drag = 30f, FallDrag = 6f, Settle = 15f, SettleWithin = 5f });
        var rule = SwingRule.For(skeleton, record)!;
        var state = rule.NewState();
        var rest = Body(skeleton);
        rule.Reset(state, Body(skeleton, DetMath.AxisAngle(Vector3.UnitX, 25f * DetMath.DegToRad)));
        var hang = RestTail(skeleton, skeleton.Find("swing_test_00_0"), 0.2f);
        float start = Vector3.Distance(state.Tails()[0], hang);
        int within = -1;
        for (int k = 0; k < 120 && within < 0; k++)
        {
            rule.Step(state, rest, 1f / 60f, null);
            if (Vector3.Distance(state.Tails()[0], hang) < 0.1f * start) within = k + 1;
        }
        output.WriteLine($"mail from 25 degrees within a tenth of its rest after {within} steps ({F(within / 60f, "0.00")} s)");
        Assert.InRange(within, 5, 40);
        // A chain naming a bone the skeleton lacks is left out; none left, no rule.
        Assert.Null(SwingRule.For(skeleton, record with { Chains = [record.Chains[0] with { Bones = ["swing_none_0"] }] }));
    }

    // ----- the models repository's people -----

    [Fact]
    public void The_record_is_read_from_the_glb_and_refitted_to_the_person()
    {
        var (_, kind, woman) = Woman();
        var record = woman.Swing;
        Assert.NotNull(record);
        // The townswoman's dress: 16 chains of 6 from the pelvis, a closed web at 1.3, the legs' capsules and the rungs between the shins as its
        // middles (her braids are left out under the headcloth, and their record with them).
        Assert.Equal(16, record!.Chains.Count);
        Assert.All(record.Chains, c => { Assert.Equal(6, c.Bones.Length); Assert.Equal("legs_long", c.Colliders); Assert.Equal("between", c.Middles); Assert.NotNull(c.SideDeg); });
        Assert.Equal(9.8f, record.Chains[0].Gravity);
        Assert.Equal(1.5f, record.Chains[0].Stiffness);
        Assert.Equal(20f, record.Chains[0].Drag);
        Assert.Equal(1f, record.Chains[0].Carried);
        var web = Assert.Single(record.Webs);
        Assert.True(web.Closed);
        Assert.Equal(1.3f, web.Stretch);
        Assert.Equal(Enumerable.Range(0, 16), web.Chains);
        Assert.Equal(["legs_long", "between"], record.Colliders.Select(g => g.Key));
        var between = record.Colliders[1].Value;
        Assert.All(between, c => { Assert.Equal("calf_l", c.From); Assert.Equal("foot_l", c.FromToward); Assert.Equal(0.3f, c.Within); Assert.True(c.Fixed); });
        Assert.Contains(record.Colliders[0].Value, c => c.Sided);

        var (assets, _, _) = Woman();
        var rule = assets.Kind(kind).Person!.Swing!;
        Assert.Equal(16, rule.ChainCount);
        Assert.Equal(16 * 6, rule.BoneCount);
        // The legs' capsules are fixed: kept as the record has them.
        Assert.Equal(record.Colliders[0].Value.Select(c => c.Radius), rule.CapsuleRadii[0].Radii);

        // The maiden (person_05) with her braids: the dress's record and the hair's merged in the recipe's order (2 chains of 4 from spine_03,
        // the chest's capsules, which are not fixed: refitted to her, no larger than the record's).
        var lib = People.PeopleLibraryTests.Lib();
        var maiden = lib.Crowd().FirstOrDefault(p => p.Name == "person_05");
        Assert.SkipUnless(maiden is not null, "no person_05 in this round's crowd");
        var braided = lib.Person(maiden!.BakedRecipe());
        Assert.Equal(18, braided.Swing!.Chains.Count);
        Assert.All(braided.Swing.Chains.Skip(16), c => { Assert.Equal(4, c.Bones.Length); Assert.Equal("chest", c.Colliders); });
        var braids = SwingRule.For(braided.Model.Skeleton, braided.Swing)!;
        Assert.Equal(16 * 6 + 2 * 4, braids.BoneCount);
        output.WriteLine(string.Join("; ", braids.CapsuleRadii.Select(g => $"{g.Group}: {string.Join(" ", g.Radii.Select(r => F(r)))}")));
        var chest = braided.Swing.Colliders.First(g => g.Key == "chest").Value;
        Assert.All(braids.CapsuleRadii.First(g => g.Group == "chest").Radii.Zip(chest), p => Assert.InRange(p.First, 0.005f, p.Second.Radius + 1e-6f));

        // A crowd person's own file (fitted by the models repository, its lengths given): refitted again here, it hardly changes.
        var nun = lib.Crowd().FirstOrDefault(p => p.Name == "person_10");
        Assert.SkipUnless(nun is not null, "no person_10 in this round's crowd");
        var baked = lib.Person(nun!.BakedRecipe());
        var bakedRule = SwingRule.For(baked.Model.Skeleton, baked.Swing)!;
        Assert.All(baked.Swing!.Chains, c => Assert.NotNull(c.Lengths));
        float most = 0f;
        foreach (var (spec, fitted) in baked.Swing.Chains.Zip(bakedRule.ChainRadii))
            for (int j = 0; j < fitted.Length; j++) most = MathF.Max(most, MathF.Abs(fitted[j] - spec.Radii![j]));
        output.WriteLine($"{nun.Name}'s chains' radii refitted here: at most {F(most * 1000f, "0.0")} mm from her file's");
        Assert.InRange(most, 0f, 0.003f);
    }

    /// <summary>A world with the woman (the dress and the braids) walking ahead, then stopping and crouching; her swing bones' matrices each tick.</summary>
    static (List<ulong> Digests, List<Matrix4x4[]> Swing, PersonSwings? Swings) Walk(CharacterAssets assets, int kind, int ticks = 360)
    {
        var (world, e) = Make(assets, kind, Floor(), Vector3.Zero, DetMath.HalfPi);
        var rule = assets.Kind(kind).Person!.Swing!;
        var bones = rule.Bones.ToArray();
        var digests = new List<ulong>();
        var swing = new List<Matrix4x4[]>();
        var feet = Vector3.Zero;
        for (int t = 0; t < ticks; t++)
        {
            float speed = t < 200 ? 1.3f : 0f;
            var velocity = new Vector3(0, speed, 0);
            feet += velocity * Tick;
            Step(world, e, feet, DetMath.HalfPi, true, velocity: velocity, crouched: t >= 260 ? 1 : 0);
            digests.Add(world.Digest());
            var model = world.Service<CharacterPoses>().Bones(world, e);
            var row = new Matrix4x4[bones.Length];
            for (int k = 0; k < bones.Length; k++) row[k] = model[bones[k]];
            swing.Add(row);
        }
        return (digests, swing, world.TryService<PersonSwings>(out var s) ? s : null);
    }

    [Fact]
    public void The_swing_is_deterministic_changes_nothing_digested_and_has_its_switch()
    {
        var (assets, kind, _) = Woman();
        var rule = assets.Kind(kind).Person!.Swing!;
        var once = Walk(assets, kind);
        var again = Walk(assets, kind);
        Assert.NotNull(once.Swings);
        Assert.True(once.Swings!.Steps > 0);
        Assert.Equal(once.Digests, again.Digests);
        for (int t = 0; t < once.Swing.Count; t++) Assert.Equal(once.Swing[t], again.Swing[t]);

        // The earlier build's (--swing-off): no simulator in the world; the digests the same tick for tick; the dress as before (at rest: hung from the hips).
        var before = assets.PersonSwing;
        try
        {
            assets.PersonSwing = PersonSwingSettings.Off;
            var off = Walk(assets, kind);
            Assert.Null(off.Swings);
            Assert.Equal(once.Digests, off.Digests);
            float moved = 0f;
            for (int t = 0; t < once.Swing.Count; t++)
                for (int b = 0; b < rule.BoneCount; b++) moved = MathF.Max(moved, Vector3.Distance(once.Swing[t][b].Translation, off.Swing[t][b].Translation));
            output.WriteLine($"the swung dress's joints at most {F(moved)} m from the swing off's; {once.Swings.Steps} steps in {once.Swing.Count} ticks");
            Assert.True(moved > 0.05f, "the dress swings");
            Assert.InRange(once.Swings.Steps, once.Swing.Count / 2 - 2, once.Swing.Count / 2 + 2);   // 60 a second from the 120 Hz tick
        }
        finally { assets.PersonSwing = before; }
    }

    [Fact]
    public void The_dress_is_kept_off_the_legs_and_the_ground_walking_and_crouching()
    {
        var (assets, kind, person) = Woman();
        var skeleton = person.Model.Skeleton;
        var (world, e) = Make(assets, kind, Floor(), Vector3.Zero, DetMath.HalfPi);
        var rule = assets.Kind(kind).Person!.Swing!;
        var hem = rule.ChainBones.Take(16).Select(c => c[^1]).ToArray();
        int[] legs = [skeleton.Find("thigh_l"), skeleton.Find("calf_l"), skeleton.Find("foot_l"), skeleton.Find("thigh_r"), skeleton.Find("calf_r"), skeleton.Find("foot_r")];
        var feet = Vector3.Zero;
        float lowest = 9f, settled = 9f, deepest = 0f, stretch = 0f;
        var restModel = new Matrix4x4[skeleton.Count];
        PoseEvaluator.ToModel(skeleton, skeleton.Bones.Select(b => b.Rest).ToArray(), restModel);
        for (int t = 0; t < 480; t++)
        {
            float speed = t < 240 ? 1.3f : 0f;
            var velocity = new Vector3(0, speed, 0);
            feet += velocity * Tick;
            Step(world, e, feet, DetMath.HalfPi, true, velocity: velocity, crouched: t >= 300 ? 1 : 0);
            Assert.True(Finite(world, e));
            if (t < 30) continue;
            var model = world.Service<CharacterPoses>().Bones(world, e);
            foreach (var chain in rule.ChainBones.Take(16))
                foreach (int b in chain.Skip(2))   // below the lap
                {
                    lowest = MathF.Min(lowest, model[b].Translation.Z);
                    if (t >= 400) settled = MathF.Min(settled, model[b].Translation.Z);
                    for (int side = 0; side < 2; side++)
                        deepest = MathF.Max(deepest, 0.05f - Distance(model[b].Translation, model[legs[side * 3 + 1]].Translation, model[legs[side * 3 + 2]].Translation));
                }
            for (int k = 0; k < 16; k++)
            {
                float now = Vector3.Distance(model[hem[k]].Translation, model[hem[(k + 1) % 16]].Translation);
                float rest = Vector3.Distance(restModel[hem[k]].Translation, restModel[hem[(k + 1) % 16]].Translation);
                stretch = MathF.Max(stretch, now / rest);
            }
        }
        output.WriteLine($"the dress's joints: lowest {F(lowest)} m (crouched and still {F(settled)}), at most {F(deepest * 100f, "0.0")} cm inside a shin's 5 cm, its hem's joints at most x{F(stretch, "0.00")} their rest apart");
        // The ground holds each tail as it is stepped, but what the webs and the legs push after it may put a joint a little through it for a
        // step or two while she drops into the crouch (swing.py's order, kept); crouched and still, the hem lies on it.
        Assert.InRange(lowest, -0.04f, 9f);
        Assert.InRange(settled, -0.001f, 9f);
        Assert.InRange(deepest, 0f, 0.03f);
        Assert.InRange(stretch, 0.5f, 1.5f);
    }

    /// <summary>
    /// With <c>VITOMIR_SWING_DUMP</c> (a folder): a run of a crowd person's dress (person_10, the nun; else the woman) over the walk, the run and
    /// the crouch, every 60 Hz step's body and the port's tails, for <c>tools/platform/swing_compare.py</c> to step the reference over and
    /// compare. In the models repository's skeleton space (Blender's: the platform's half turned about Z), each bone's frame with its length along +Y.
    /// </summary>
    [Fact]
    public void A_run_for_the_reference()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VITOMIR_SWING_DUMP") is { Length: > 0 }, "VITOMIR_SWING_DUMP is not set");
        string dir = Environment.GetEnvironmentVariable("VITOMIR_SWING_DUMP")!;
        var lib = People.PeopleLibraryTests.Lib();
        // The nun (a long skirt from the hips: sided capsules, the rungs as middles, the web at 1.3), the townsman (a tunic from the thighs:
        // radii, the web at 1.15) and the guard's outfit on the base man (mail: fall_drag and settle; the scabbard's along, the shield's across).
        foreach (var (who, clips) in new[] { ("person_10", new[] { ("walk", 2.0f), ("run", 1.5f), ("crouch", 1.5f), ("stand", 1.0f) }),
                                             ("person_04", new[] { ("walk", 1.5f), ("sprint", 1.5f), ("crouch_walk", 1.5f) }),
                                             ("guard", new[] { ("walk", 1.0f), ("run", 1.5f), ("crouch", 1.5f), ("stand", 1.0f) }),
                                             ("player", new[] { ("walk_stairs_up", 2.0f), ("walk_stairs_down", 2.0f), ("run_stairs_down", 1.5f) }) })
        {
            var crowd = lib.Crowd().FirstOrDefault(p => p.Name == who);
            Assert.SkipUnless(crowd is not null || who is "guard" or "player", $"no {who}");
            var person = lib.Person(crowd?.BakedRecipe() ?? (who == "player" ? PersonRecipe.Player : new PersonRecipe("guard", "male", "base_male", ["guard_male"])));
            var assets = new CharacterAssets();
            int kind = PeopleKinds.Register(assets, person, lib.Motions(person.Recipe.Sex), who);
            var motion = assets.Kind(kind).Person!;
            var rule = motion.Swing!;
            var skeleton = person.Model.Skeleton;
            int n = skeleton.Count;
            INotedClip Clip(string name)
            {
                for (int i = 0; i < assets.ClipCount; i++) if (assets.Clip(i) is INotedClip c && c.Name == name) return c;
                throw new InvalidOperationException(name);
            }
            var local = new BoneTransform[n];
            var model = new Matrix4x4[n];
            var restModel = new Matrix4x4[n];
            PoseEvaluator.ToModel(skeleton, skeleton.Bones.Select(b => b.Rest).ToArray(), restModel);
            var state = rule.NewState();
            var steps = new List<object>();
            bool first = true;
            float[] Frame(in Matrix4x4 m)
            {
                // Columns: X, the length's way (+Z here), X × it; the point; each turned half about Z into Blender's space.
                static Vector3 B(Vector3 v) => new(-v.X, -v.Y, v.Z);
                Vector3 x = B(new(m.M11, m.M12, m.M13)), y = B(new(m.M31, m.M32, m.M33)), z = B(-new Vector3(m.M21, m.M22, m.M23)), t = B(m.Translation);
                return [x.X, y.X, z.X, t.X, x.Y, y.Y, z.Y, t.Y, x.Z, y.Z, z.Z, t.Z, 0, 0, 0, 1];
            }
            foreach (var (name, seconds) in clips)
            {
                var clip = Clip(name);
                for (int k = 0; k < (int)(seconds * 60f); k++)
                {
                    clip.Sample(k / 60f, skeleton, local);
                    motion.Bend?.Apply(local);
                    motion.Twist?.Apply(local);
                    PoseEvaluator.ToModel(skeleton, local, model);
                    if (first) { rule.Reset(state, model); first = false; }
                    else rule.Step(state, model, 1f / 60f, 0f);
                    steps.Add(new { clip = name, body = Enumerable.Range(0, n).Select(b => Frame(model[b])).ToArray(), tails = state.Tails().Select(v => new[] { -v.X, -v.Y, v.Z }).ToArray() });
                }
            }
            // The record as the port runs it: its chains' radii and capsules' sizes refitted here, every bone's length.
            var record = person.Swing!;
            var lengths = new Dictionary<string, float>();
            var chainBones = rule.ChainBones;
            var chainRadii = rule.ChainRadii;
            var caps = rule.CapsuleRadii.ToDictionary(g => g.Group, g => g.Radii);
            var spec = new
            {
                chains = record.Chains.Select((c, i) => new Dictionary<string, object?>
                {
                    ["bones"] = c.Bones, ["gravity"] = c.Gravity, ["stiffness"] = c.Stiffness, ["drag"] = c.Drag, ["radius"] = c.Radius, ["radii"] = c.Radii is null ? null : chainRadii[i],
                    ["colliders"] = c.Colliders, ["middles"] = c.Middles, ["side_deg"] = c.SideDeg, ["carried"] = c.Carried,
                    ["fall_drag"] = c.FallDrag, ["settle"] = c.Settle == 0f ? null : c.Settle, ["settle_within"] = c.SettleWithin, ["along"] = c.Along, ["across"] = c.Across,
                    ["across_dir"] = c.AcrossDir is { } ad ? new[] { ad.X, ad.Y, ad.Z } : null,
                }.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value)).ToArray(),
                colliders = record.Colliders.ToDictionary(g => g.Key, g => g.Value.Select((c, i) => new Dictionary<string, object?>
                {
                    ["from"] = c.From, ["to"] = c.To, ["radius"] = caps[g.Key][i], ["span"] = new[] { c.SpanFrom, c.SpanTo }, ["fixed"] = c.Fixed,
                    ["from_toward"] = c.FromToward, ["to_toward"] = c.ToToward, ["at"] = c.At, ["within"] = c.Within == 0f ? null : c.Within, ["sided"] = c.Sided ? true : null,
                }.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value)).ToArray()),
                webs = record.Webs.Select(w => new { chains = w.Chains, closed = w.Closed, stretch = w.Stretch }).ToArray(),
            };
            foreach (var bone in rule.Bones) lengths[skeleton.Bones[bone].Name] = rule.Length[rule.SlotOf[bone]];
            var dump = new
            {
                person = who,
                names = skeleton.Bones.Select(b => b.Name).ToArray(),
                parents = skeleton.Bones.Select(b => b.Parent).ToArray(),
                rest = Enumerable.Range(0, n).Select(b => Frame(restModel[b])).ToArray(),
                order = rule.Bones.Select(b => skeleton.Bones[b].Name).ToArray(),
                lengths, spec, dt = 1f / 60f, ground = 0f, steps,
            };
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"swing-{who}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(dump));
            output.WriteLine($"{path}: {steps.Count} steps of {rule.BoneCount} bones");
        }
    }
}
