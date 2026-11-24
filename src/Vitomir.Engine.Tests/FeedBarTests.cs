// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Audio;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>The feed bar: the victim's blood, and the drain loop's three speeds.</summary>
public class FeedBarTests
{
    static WorldThing Character(int guid, ThingType type) => new()
    {
        Index = guid - 1, Guid = guid, Scene = new NsdThing(), Template = "x", Type = type,
        Actor = new Actor { Type = ActorType.Vampire, Health = 100, MaxHealth = 100 },
    };

    [Fact]
    public void The_bar_shows_the_victims_blood_and_the_loop_slows_as_it_drains()
    {
        using var files = Install.Open();
        var audio = new GameAudio(files);
        var feeder = Character(1, ThingType.Player);
        var victim = Character(2, ThingType.Actor);
        victim.Actor!.Stat[(int)ActorStat.BloodPool] = 40f;
        victim.Actor.Stat[(int)ActorStat.Blood] = 30f;
        var bar = new FeedBar();
        var loops = FeedBar.Loops.Select(n => audio.Bank.Load(n)!).ToArray();
        var fast = loops[0];

        bar.Update(feeder, false, audio);
        Assert.False(bar.Shown);                                  // not feeding

        feeder.Actor!.Action = ActionMode.Feeding;
        feeder.Actor.Target = victim;
        bar.Update(feeder, false, audio);
        Assert.True(bar.Shown);
        Assert.Equal(0.75f, bar.Fraction);
        Assert.Equal((2, 4), (bar.Left, bar.Bottom));
        Assert.Equal(0, bar.Band);
        var loop = audio.Mixer.Channels.Single(c => c.Active && ReferenceEquals(c.Sound, fast));
        Assert.Equal(0f, loop.Pan);                               // pan 0, as the engine passes it
        Assert.True((loop.Flags & ChannelFlag.Looping) != 0);

        victim.Actor.Stat[(int)ActorStat.Blood] = 19f;           // under half
        bar.Update(feeder, true, audio);
        Assert.Equal(1, bar.Band);
        Assert.Equal(131, bar.Bottom);                            // over the status bar
        Assert.DoesNotContain(audio.Mixer.Channels, c => c.Active && ReferenceEquals(c.Sound, fast));

        victim.Actor.Stat[(int)ActorStat.Blood] = 9f;            // under a quarter
        bar.Update(feeder, true, audio);
        Assert.Equal(2, bar.Band);

        feeder.Actor.Action = ActionMode.None;
        bar.Update(feeder, true, audio);
        Assert.False(bar.Shown);
        Assert.Equal(-1, bar.Band);
        Assert.DoesNotContain(audio.Mixer.Channels, c => c.Active && loops.Contains(c.Sound!));
    }

    /// <summary>A feeder whose target is not a character leaves the pane exactly as it was.</summary>
    [Fact]
    public void A_target_that_is_not_a_character_leaves_the_pane_alone()
    {
        var feeder = Character(1, ThingType.Player);
        var bar = new FeedBar();
        feeder.Actor!.Action = ActionMode.WristFeeding;
        feeder.Actor.Target = new WorldThing { Index = 5, Guid = 6, Scene = new NsdThing(), Template = "crate", Type = ThingType.Prop };
        bar.Update(feeder, false, null);
        Assert.False(bar.Shown);
    }
}
