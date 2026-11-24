// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Obfuscate stacking, as in 1.1** (Monastery 1).
/// Cloak the Gathering cast and then Cloak of Shadows, both at the top level:
/// the stored stealth goes 0 → 5 → 10, back to 5 when the Gathering runs out a minute
/// after its cast and to 0 when the Cloak runs out ninety seconds after its own. Both rows of
/// `effects.ned` are `HIDE` composed `ADD` (compose 5), so the fold sums them and the
/// recompute writes the sum; the durations are the scripts' (`Discipline.java`, the top
/// entries of `CLOAKTHEGATHERING_DURATIONS` and `CLOAKOFSHADOWS_DURATIONS`). And every change of mind
/// the enemies make meanwhile is what the stealth table and the sight test say it should be.
/// </summary>
public class ObfuscateStackingTests
{
    /// <summary>The top level, counted from zero as the scripts pass it (Christof's Obfuscate at 5).</summary>
    const int TopLevel = 4;

    /// <summary>The scripts' durations at that level: a minute for the Gathering, ninety seconds for the Cloak.</summary>
    const int GatheringMs = 60000, CloakMs = 90000;

    static WorldThing Character(int stealth = 0, ActionMode doing = ActionMode.None, int busyUntil = 0) => new()
    {
        Index = -1, Guid = 5, Scene = new NsdThing(), Template = "christof", Type = ThingType.Actor,
        Actor = new Actor { Stealth = stealth, Action = doing, BusyUntil = busyUntil, Health = 100f, MaxHealth = 100f },
    };

    [Fact]
    public void The_two_cloaks_add_up_and_come_off_one_at_a_time()
    {
        using var files = Install.Open();
        var ned = new EffectDefinitions(files).File!;
        var gathering = ned["ef_disc_cloakthegathering"]!;
        var cloak = ned["ef_disc_cloakofshadows"]!;
        Assert.Equal((EffectKind.Hide, EffectCompose.Add), (gathering.Kind, gathering.Compose));
        Assert.Equal((EffectKind.Hide, EffectCompose.Add), (cloak.Kind, cloak.Compose));

        var christof = Character();
        var me = christof.Actor!;
        var rng = new Rng(1);
        // The log's moments, on its own game clock less 5,649,009: the Gathering's cast, then the Cloak's.
        EffectRuntime.Apply(christof, gathering, TopLevel, now: 0, durationMs: GatheringMs);
        Assert.Equal(5, me.Stealth);
        EffectRuntime.Apply(christof, cloak, TopLevel, now: 2380, durationMs: CloakMs);
        Assert.Equal(10, me.Stealth);

        // Walking, running, swinging and feeding leave the stored byte alone — the log's column stayed
        // at 10 through all of them; what they cost is taken as it is read, below.
        EffectRuntime.Tick(christof, 59_900, rng);
        Assert.Equal(10, me.Stealth);
        EffectRuntime.Tick(christof, 60_000, rng);
        Assert.Equal(5, me.Stealth);
        EffectRuntime.Tick(christof, 92_380, rng);
        Assert.Equal(0, me.Stealth);
    }

    /// <summary>
    /// Every change of mind the log shows, as one row each: what Christof had stored, what he was
    /// doing, the looker's perception (2 for a Cappadocian under her Heightened Senses, `ef_disc_hsenses`
    /// at her level; 0 for the rest), and whether the looker made him out. The log's own seconds are in
    /// the comments. "Made out" is the stealth the table leaves against the looker's perception.
    /// </summary>
    [Theory]
    [InlineData(10, ActionMode.Swing, 500, false, 0, false)]   // 14.8 s: the skeleton archer he struck goes to approach (mode 16)
    [InlineData(10, ActionMode.None, 0, true, 2, false)]       // 35.6 s: running past her, Heightened Senses up — approach
    [InlineData(10, ActionMode.Feeding, 0, false, 2, false)]   // 60.3 s: fed on at 10 and still she cannot see him
    [InlineData(5, ActionMode.Feeding, 0, false, 2, true)]     // 67.5 s: the Gathering runs out mid-feed — she turns on him that tenth
    [InlineData(5, ActionMode.Feeding, 0, false, 0, true)]     // 67.7 s: and three more, perception 0, from their search
    [InlineData(5, ActionMode.None, 0, false, 2, false)]       // 70.4–71.7 s: the feed over, all of them lose him again
    [InlineData(5, ActionMode.Swing, 500, false, 0, true)]     // 74.5 s: his swing brings four back
    [InlineData(5, ActionMode.Swing, 0, false, 2, false)]      // 75.7 s: and they lose him as it ends
    [InlineData(5, ActionMode.Move, 0, true, 2, false)]        // 76.7–81 s: he runs off unseen
    [InlineData(0, ActionMode.None, 0, false, 0, true)]        // 99.9 s: the Cloak runs out — found at once
    public void What_the_enemies_did_is_what_the_stealth_table_says(int stored, ActionMode doing, int busyUntil,
                                                                    bool moving, int perception, bool madeOut)
    {
        var looker = Character();
        looker.Actor!.Perception = perception;
        Assert.Equal(madeOut, Sight.Notices(looker, Character(stored, doing, busyUntil), targetMoving: moving));
    }
}
