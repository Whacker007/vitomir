// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Anim;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Features;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The guard under two declarations**. The Fixes `movespeed` and `matchedgait` are declared
/// presentation, each machine's own: they change the rate a locomotion cycle plays at
/// (<c>Player.RateFor</c>, only for a mode flagged <see cref="NmsFlags.MoveSpeed"/>). A cycle's rate moves the
/// moment its keyframe tags come round, and the tag dispatcher (<c>World/ThingTick.cs</c>) does more
/// than make noises: `Fire` and `Strike` land a blow, `Cast` fires a discipline, `Project` nocks an arrow,
/// `Repeat` loops held fire, `Throwdeath` lands a thrown body, `Codex1`–`Codex3` call a script, and a
/// `noloop endtag` or `noloop fidget` mode fires a tag as it ends. So the declaration is true only if **no
/// cycle whose rate the Fixes may change carries any of those** — which is a fact about the install's data, and
/// this reads it: every motion set, every mode with the flag.
/// </summary>
public class LocomotionTagTests
{
    /// <summary>The tags that only make a noise, or nothing (<see cref="KeyframeSounds"/>; `Suck` has no handler).</summary>
    static readonly HashSet<string> Harmless = ["Lwalk", "Rwalk", "Lrun", "Rrun", "Fall", "Flap", "Suck", "Idle", "Idle2"];

    [Fact]
    public void No_cycle_whose_rate_a_fix_may_change_carries_a_tag_the_rules_act_on()
    {
        using var files = Install.Open();
        var motions = new MotionSets(files);
        int sets = 0, modes = 0, cycles = 0;
        var seen = new SortedDictionary<string, int>();
        var offenders = new List<string>();
        foreach (var path in files.List("3d/Motions/").Where(p => p.EndsWith(".nms", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (motions.Set(Path.GetFileName(path)) is not { } set) continue;
            sets++;
            foreach (var mode in set.Modes)
            {
                if (!mode.Has(NmsFlags.MoveSpeed)) continue;
                modes++;
                // The two tags the mixer makes up as a mode ends (0x13, 0x14) come when the cycle's length says.
                if (mode.Has(NmsFlags.EndTag) || mode.Has(NmsFlags.Fidget))
                    offenders.Add($"{set.Name}: mode {mode.Name} ({mode.File}) is flagged {mode.Flags}");
                if (motions.Animation(mode.File) is not { } animation) continue;
                cycles++;
                foreach (var tag in animation.Tags)
                {
                    seen[tag.Name] = seen.GetValueOrDefault(tag.Name) + 1;
                    if (!Harmless.Contains(tag.Name))
                        offenders.Add($"{set.Name}: mode {mode.Name} ({mode.File}) carries {tag.Name} at frame {tag.Frame:0.##}");
                }
            }
        }
        string found = string.Join(", ", seen.Select(p => $"{p.Key} x{p.Value}"));
        Assert.True(offenders.Count == 0,
            $"movespeed and matchedgait cannot be declared presentation: {offenders.Count} of the install's locomotion cycles carry a tag the rules act on:\n  "
            + string.Join("\n  ", offenders.Take(60)));
        // It read the install, and not a corner of it.
        Assert.True(sets > 100, $"only {sets} motion sets were read");
        Assert.True(modes > 400, $"only {modes} flagged modes were found in {sets} sets");
        Assert.True(cycles > 400, $"only {cycles} of the {modes} flagged modes have an animation ({found})");
        // Every tag met is a footfall or another noise, and the footfalls are there (the stride is measured from them).
        Assert.Contains("Lwalk", seen.Keys);
        Assert.Contains("Rrun", seen.Keys);
        Assert.All(seen.Keys, name => Assert.Contains(name, Harmless));
        // What the declaration then says.
        Assert.False(GameFeatures.Data.LocomotionRate.RulesRead);
        Assert.Equal(FeatureAxis.Presentation, GameFeatures.Registry.Find("fixes", "movespeed")!.Axis);
        Assert.Equal(FeatureAxis.Presentation, GameFeatures.Registry.Find("fixes", "matchedgait")!.Axis);
        TestContext.Current.SendDiagnosticMessage($"locomotion tags: {sets} sets, {modes} flagged modes, {cycles} with an animation: {found}");
    }
}
