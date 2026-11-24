// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The icons of what is on the steered character and the
/// right click that cancels one. The effects are the shipped `effects.ned` rows.
/// </summary>
public class EffectIconsTests
{
    static WorldThing Someone() => new()
    {
        Index = 0, Guid = 1, Scene = new NsdThing(), Template = "christof", Type = ThingType.Player,
        Actor = new Actor { Type = ActorType.Vampire, Health = 100, MaxHealth = 100 },
    };

    /// <summary>
    /// Disciplines on the bottom row, everything else on the row above; each row fills leftwards
    /// from x 609 in steps of 31. The engine's y is down from the top — 444 for the bottom row,
    /// which is 4 up from the bottom in the interface's y-up space — and the bar lifts both rows to
    /// 305 (143 up).
    /// </summary>
    [Fact]
    public void Disciplines_fill_the_bottom_row_and_the_rest_the_row_above()
    {
        using var files = Install.Open();
        var defs = new EffectDefinitions(files);
        var who = Someone();
        EffectRuntime.Apply(who, defs.File!["ef_disc_potence"]!, 0, now: 0, durationMs: 60_000);
        EffectRuntime.Apply(who, defs.File!["ef_poison"]!, 0, now: 0, durationMs: 60_000);
        EffectRuntime.Apply(who, defs.File!["ef_disc_fortitude"]!, 0, now: 0, durationMs: 60_000);
        EffectRuntime.Apply(who, defs.File!["ef_increaseblood"]!, 0, now: 0);   // no icon

        var icons = EffectIcons.Layout(who, barShown: false);
        Assert.Equal(3, icons.Count);
        var bottom = icons.Where(i => i.Effect.Definition.Row == 0).ToList();
        var above = icons.Where(i => i.Effect.Definition.Row != 0).ToList();
        Assert.Equal([609f, 578f], bottom.Select(i => i.X));
        Assert.All(bottom, i => Assert.Equal(4f, i.Y));
        Assert.Equal(609f, above.Single().X);
        Assert.Equal(40f, above.Single().Y);
        Assert.All(icons, i => Assert.Equal(200f / 255f, i.Alpha, 5));

        var lifted = EffectIcons.Layout(who, barShown: true);
        Assert.Equal(143f, lifted.First(i => i.Effect.Definition.Row == 0).Y);
        Assert.Equal(179f, lifted.First(i => i.Effect.Definition.Row != 0).Y);
    }

    /// <summary>
    /// The hit test walks the same list without skipping a removed instance, so between the removal
    /// and the reaping the icon drawn at 609 answers for the one that has gone.
    /// </summary>
    [Fact]
    public void The_pointer_walk_still_counts_an_effect_removed_this_frame()
    {
        using var files = Install.Open();
        var defs = new EffectDefinitions(files);
        var who = Someone();
        var first = EffectRuntime.Apply(who, defs.File!["ef_disc_potence"]!, 0, now: 0, durationMs: 60_000)!;
        var second = EffectRuntime.Apply(who, defs.File!["ef_disc_fortitude"]!, 0, now: 0, durationMs: 60_000)!;
        var atRight = EffectIcons.At(who, false, 615, 10);
        var atLeft = EffectIcons.At(who, false, 585, 10);
        Assert.NotNull(atRight);
        Assert.NotNull(atLeft);
        Assert.NotSame(atRight, atLeft);

        EffectRuntime.Remove(who, atRight!, notify: true);
        var drawn = EffectIcons.Layout(who, false);
        Assert.Same(atLeft, drawn.Single().Effect);
        Assert.Equal(609f, drawn.Single().X);                     // drawn one place to the right…
        Assert.Same(atRight, EffectIcons.At(who, false, 615, 10)); // …but the click still finds the spent one there
        Assert.Contains(first, who.Effects.All);
        Assert.Contains(second, who.Effects.All);
    }

    [Fact]
    public void A_right_click_takes_off_a_cancellable_effect_and_nothing_else()
    {
        using var files = Install.Open();
        var defs = new EffectDefinitions(files);
        var who = Someone();
        var poison = EffectRuntime.Apply(who, defs.File!["ef_poison"]!, 0, now: 0, durationMs: 60_000)!;
        var potence = EffectRuntime.Apply(who, defs.File!["ef_disc_potence"]!, 0, now: 0, durationMs: 60_000)!;

        Assert.False(EffectIcons.Cancel(who, false, 615, 50));   // the poison, row 1: not the player's to cancel
        Assert.False(poison.Spent);
        Assert.True(EffectIcons.Cancel(who, false, 615, 10));    // Potence, row 0
        Assert.True(potence.Spent);
        Assert.False(EffectIcons.Cancel(who, false, 300, 300));  // nothing there
    }
}
