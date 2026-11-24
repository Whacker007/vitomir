// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Gait = Vitomir.Platform.Bodies.Gait;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// (<i>"how rigid the torso and even the head can be … the central body tends to stay fairly upright and unmoving"</i>): <b>does any engine layer
/// flatten the clips' own torso and head motion?</b> For the player's walk, run, sprint, a start, a stop, the crouched walk and a turn on the spot, each tick's pose
/// is made again layer by layer — the clip as played (its blends and fades: <see cref="PoseEvaluator.Local(CharacterAssets, in CharacterAnimation, Span{BoneTransform})"/>);
/// with the turn chain; with the limbs placed (the foot locks, the hips' rise and drop, the soft legs); with the ground (the stair line, the slope clip); with the
/// bend and twist rules; and with the lean, look and lead (the whole drawn pose) — and the pelvis's, the spine's, the neck's and the head's turns in the model
/// (pitch about its side, roll about its front, yaw about the vertical) are measured from their mean: each layer's motion against the clip's own, axis by axis
/// (the share kept: 100% none taken away). Skips without the models repository's files or.
/// </summary>
public sealed class TorsoMotionAuditTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0") => v.ToString(format, CultureInfo.InvariantCulture);

    static readonly string[] Bones = ["pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "head"];
    static readonly string[] Layers = ["clip", "+chain", "+limbs (feet, hips)", "+ground", "+bend", "+lean/look"];

    /// <summary>The model-space turn of each audited bone, layer by layer, for one tick (radians, as an axis times its angle).</summary>
    static Quaternion[,] Pose(World w, EntityId e, CharacterAssets assets, int[] bones, PoseEvaluator evaluator, BoneTransform[] local, Matrix4x4[] model, Matrix4x4[] skin)
    {
        var state = w.Get<CharacterAnimation>(e);
        var rig = w.Get<PoseRig>(e);
        var chain = CharacterPoses.Chain(w, e);
        var feet = CharacterPoses.Feet(w, e);
        var ground = CharacterPoses.Ground(w, e);
        var flight = CharacterPoses.Flight(w, e);
        var present = PersonPresentRule.Of(w, e);
        var skeleton = assets.ModelOf(state.Kind).Skeleton;
        int n = skeleton.Count;
        var result = new Quaternion[Layers.Length, bones.Length];
        void Take(int layer) { for (int b = 0; b < bones.Length; b++) result[layer, b] = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(model[bones[b]])); }
        // The clip as played (and its join, the drawn base: the inertialized join where it is under way, else the crossfade).
        evaluator.Present = present with { Roll = 0f, Head = 0f, Chest = 0f, Pitch = 0f };
        var noFeet = feet;
        noFeet.On = false;
        evaluator.Evaluate(assets, state, new PoseRig(), model, skin, default, default, noFeet, default, flight);
        Take(0);
        evaluator.Evaluate(assets, state, new PoseRig { LegYaw = rig.LegYaw, LegMode = rig.LegMode }, model, skin, default, chain, noFeet, default, flight);
        Take(1);
        var flat = rig;
        flat.RootOffset = Vector3.Zero;
        var unbent = feet;
        unbent.Bend = false;
        evaluator.Evaluate(assets, state, flat, model, skin, default, chain, unbent, default, flight);
        Take(2);
        evaluator.Evaluate(assets, state, rig, model, skin, default, chain, unbent, ground, flight);
        Take(3);
        evaluator.Evaluate(assets, state, rig, model, skin, default, chain, feet, ground, flight);
        Take(4);
        evaluator.Present = present;
        evaluator.Evaluate(assets, state, rig, model, skin, default, chain, feet, ground, flight);
        Take(5);
        evaluator.Present = default;
        return result;
    }

    /// <summary>The peak-to-peak of each axis (pitch, roll, yaw; degrees) of a bone's turn about its mean over a run of ticks.</summary>
    static Vector3 Range(List<Quaternion> qs)
    {
        var mean = qs[0];
        var sum = Vector4.Zero;
        foreach (var q in qs)
        {
            var v = new Vector4(q.X, q.Y, q.Z, q.W);
            if (Vector4.Dot(v, new Vector4(mean.X, mean.Y, mean.Z, mean.W)) < 0f) v = -v;
            sum += v;
        }
        sum = Vector4.Normalize(sum);
        mean = new Quaternion(sum.X, sum.Y, sum.Z, sum.W);
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var q in qs)
        {
            var r = PersonJoinRule.Log(q * Quaternion.Conjugate(mean));
            lo = Vector3.Min(lo, r);
            hi = Vector3.Max(hi, r);
        }
        return (hi - lo) * DetMath.RadToDeg;
    }

    /// <summary>A comparison with 1.1 cases: a name, what is asked (by tick), and the ticks measured (from, to).</summary>
    static IEnumerable<(string Name, Func<int, PlayerGaitTests.Ask> Plan, int From, int To)> Cases()
    {
        var east = new Vector2(1f, 0f);
        yield return ("walk", i => new(east, Gait.Walk), 300, 400);
        yield return ("run", i => new(east, Gait.Run), 300, 380);
        yield return ("sprint", i => new(east, Gait.Sprint), 300, 368);
        yield return ("a start (the run's)", i => new(i < 60 ? Vector2.Zero : east, Gait.Run), 61, 160);
        yield return ("a stop (the run's)", i => new(i < 300 ? east : Vector2.Zero, Gait.Run), 300, 390);
        yield return ("the crouched walk", i => new(east, Gait.Walk, Crouch: true), 300, 420);
        yield return ("a turn on the spot (90° left)", i => new(Vector2.Zero, Face: i < 60 ? 0f : DetMath.HalfPi), 60, 200);
    }

    [Theory]
    [InlineData("man")]
    [InlineData("woman")]
    public void No_engine_layer_flattens_the_clips_own_torso_and_head_motion_beyond_what_it_needs(string who)
    {
        var (assets, kind) = WallClimbTests.Body(who);
        PlayerGaitTests.Gaits(assets, kind);
        var skeleton = assets.ModelOf(kind).Skeleton;
        int[] bones = [.. Bones.Select(skeleton.Find)];
        Assert.DoesNotContain(-1, bones);
        var evaluator = new PoseEvaluator();
        var local = new BoneTransform[skeleton.Count];
        var model = new Matrix4x4[skeleton.Count];
        var skin = new Matrix4x4[skeleton.Count];
        var flattened = new List<string>();
        foreach (var (name, plan, from, to) in Cases())
        {
            var (w, e) = PlayerGaitTests.Make(assets, kind);
            for (int i = 0; i < 20; i++) { PlayerGaitTests.Set(w, e, new(Vector2.Zero)); w.Step(); }
            var series = new List<Quaternion>[Layers.Length, bones.Length];
            for (int l = 0; l < Layers.Length; l++) for (int b = 0; b < bones.Length; b++) series[l, b] = [];
            for (int i = 0; i < to; i++)
            {
                var ask = plan(i);
                PlayerGaitTests.Set(w, e, ask);
                if (!float.IsNaN(ask.Face)) w.Get<Body>(e).Yaw = ask.Face;
                w.Step();
                if (i < from) continue;
                var q = Pose(w, e, assets, bones, evaluator, local, model, skin);
                for (int l = 0; l < Layers.Length; l++) for (int b = 0; b < bones.Length; b++) series[l, b].Add(q[l, b]);
            }
            output.WriteLine($"{who}, {name} ({F((to - from) * World.TickSeconds, "0.00")} s): each bone's motion, pitch / roll / yaw peak to peak (°), and each layer's share of the clip's");
            for (int b = 0; b < bones.Length; b++)
            {
                var clip = Range(series[0, b]);
                var line = $"  {Bones[b],-9} clip {F(clip.X, "0.0")} / {F(clip.Y, "0.0")} / {F(clip.Z, "0.0")}";
                for (int l = 1; l < Layers.Length; l++)
                {
                    var r = Range(series[l, b]);
                    string Share(float a, float c) => c < 0.5f ? "-" : F(a / c * 100f) + "%";
                    line += $" | {Layers[l]} {Share(r.X, clip.X)} {Share(r.Y, clip.Y)} {Share(r.Z, clip.Z)}";
                    // Flattened: an axis that moves at least 2° in the clip kept under 80% by a layer (the turn's yaw is the chain's by design: its own lead taken out).
                    bool turnYaw = name.StartsWith("a turn", StringComparison.Ordinal);
                    for (int axis = 0; axis < 3; axis++)
                    {
                        float c = axis == 0 ? clip.X : axis == 1 ? clip.Y : clip.Z, a = axis == 0 ? r.X : axis == 1 ? r.Y : r.Z;
                        if (c >= 2f && a < 0.8f * c && !(turnYaw && l == 1 || turnYaw && axis == 2))
                            flattened.Add($"{who} {name}: {Bones[b]} {(axis == 0 ? "pitch" : axis == 1 ? "roll" : "yaw")} {F(c, "0.0")}° in the clip, {F(a, "0.0")}° {Layers[l]}");
                    }
                }
                output.WriteLine(line);
            }
        }
        foreach (var f in flattened) output.WriteLine("flattened: " + f);
        Assert.True(flattened.Count == 0, string.Join("\n", flattened));
    }
}
