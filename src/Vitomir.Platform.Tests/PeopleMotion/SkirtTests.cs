// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// The townsman's tunic hangs its skirt from the two thighs (16 swing chains, 8 on each; the models repository's swing bones, which the platform
/// leaves at rest), and an earlier build drew each half rigid with its own thigh: the hem tore open between them (in play the clothing still went a bit
/// crazy, and on stairs the cloth around the legs got very wild). <see cref="SkirtRule"/> drapes it: hung from the pelvis as at rest, kept out
/// of the legs. Measured over the player's clips: how far the hem between neighbouring chains is stretched from its rest and how deep a chain goes
/// into a leg, with the rule and without (skips without <c>VITOMIR_MODELS_DIR</c>).
/// </summary>
public sealed class SkirtTests(ITestOutputHelper output)
{
    static readonly string[] Clips = ["stand", "walk", "run", "sprint", "crouch_walk", "crouch", "walk_uphill", "run_downhill", "walk_stairs_up", "run_stairs_up", "run_stairs_down"];

    [Fact]
    public void The_player_s_tunic_skirt_is_found_hung_from_both_thighs()
    {
        var (_, _, person) = Player();
        var skeleton = person.Model.Skeleton;
        var rule = SkirtRule.For(skeleton);
        Assert.NotNull(rule);
        Assert.Equal(16, rule!.Chains.Length);
        Assert.Equal(8, rule.Chains.Count(c => c.Thigh == skeleton.Find("thigh_l")));
        Assert.All(rule.Chains, c => Assert.Equal(5, c.Bones.Length));
        Assert.Equal(16, rule.Hem.Length);
        // A body with no skirt hung from the thighs has none (the made-up person).
        var (made, kind, _) = PeopleDoubles.Kind();
        Assert.Null(made.Kind(kind).Person!.Skirt);
    }

    [Fact]
    public void The_hem_holds_together_in_every_gait()
    {
        var (assets, kind, person) = Player();
        var skeleton = person.Model.Skeleton;
        var motion = assets.Kind(kind).Person!;
        var rule = motion.Skirt!;
        int n = skeleton.Count;
        var local = new BoneTransform[n];
        var model = new Matrix4x4[n];
        for (int b = 0; b < n; b++) local[b] = skeleton.Bones[b].Rest;
        PoseEvaluator.ToModel(skeleton, local, model);
        // At rest the rule changes nothing.
        rule.Apply(local);
        var check = new Matrix4x4[n];
        PoseEvaluator.ToModel(skeleton, local, check);
        Assert.All(rule.Chains.SelectMany(c => c.Bones), b => Assert.InRange(Vector3.Distance(check[b].Translation, model[b].Translation), 0f, 1e-4f));
        Assert.InRange(rule.Deepest(check), -1f, 1e-4f);
        var lines = new List<string>();
        float worstAll = 0f, worstSplitAll = 0f, deepAll = 0f, deepSplitAll = 0f;
        foreach (string name in Clips)
        {
            INotedClip? clip = null;
            for (int i = 0; i < assets.ClipCount && clip is null; i++) if (assets.Clip(i) is INotedClip c && c.Name == name) clip = c;
            if (clip is null) continue;   // a round without it (the stair clips)
            float worst = 0f, worstSplit = 0f, deep = 0f, deepSplit = 0f;
            for (float t = 0f; t <= clip.Duration; t += 1f / 30f)
                foreach (bool on in new[] { false, true })
                {
                    clip.Sample(t, skeleton, local);
                    motion.Bend?.Apply(local);
                    motion.Twist?.Apply(local);
                    if (on) rule.Apply(local);
                    PoseEvaluator.ToModel(skeleton, local, model);
                    float st = rule.Stretch(model), dp = rule.Deepest(model);
                    if (on) (worst, deep) = (MathF.Max(worst, st), MathF.Max(deep, dp));
                    else (worstSplit, deepSplit) = (MathF.Max(worstSplit, st), MathF.Max(deepSplit, dp));
                }
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"{name}: the hem between neighbouring chains at most x{worst:0.00} its rest (split x{worstSplit:0.00}); a chain at most {deep * 100f:0.0} cm into a leg (split {deepSplit * 100f:0.0})"));
            (worstAll, worstSplitAll) = (MathF.Max(worstAll, worst), MathF.Max(worstSplitAll, worstSplit));
            (deepAll, deepSplitAll) = (MathF.Max(deepAll, deep), MathF.Max(deepSplitAll, deepSplit));
        }
        output.WriteLine(string.Join("\n", lines));
        Assert.True(worstSplitAll > 4f, $"split hem tears open ({worstSplitAll:0.00})");
        Assert.InRange(worstAll, 1f, 2.75f);
        Assert.InRange(deepAll, -1f, 0.02f);
    }
}
