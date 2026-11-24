// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The modes a discipline puts a mind into: mesmerised, dazed, afraid and
/// frenzied — who may be put there, what each clears, and the way back. The way in is the effect
/// apply with the effect's creator; the way out is the removal, whatever mode the mind
/// has moved on to.
/// </summary>
public class MindModeTests
{
    static (Location Place, Minds Minds, WorldThing Christof, Mind Mind) Open(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Steered!;
        var mind = minds.All.First(m => !ReferenceEquals(m.Thing, christof) && m.Thing.Actor is not null
                                        && m.Class.MindFlags == 0 && (m.Flags & Mind.FlagUnaffected) == 0);
        return (place, minds, christof, mind);
    }

    [Fact]
    public void The_mesmerise_effect_takes_the_mind_and_its_end_gives_it_back()
    {
        using var files = Install.Open();
        var (_, _, christof, mind) = Open(files);
        var home = mind.BaseMode;
        var e = EffectRuntime.Apply(mind.Thing, "ef_mesmerized", 0, durationMs: 10_000, creator: christof);
        Assert.NotNull(e);
        Assert.Equal(AiMode.Mesmerized, mind.Mode);
        Assert.Same(christof, mind.Target);
        Assert.Equal(home, mind.BaseMode);                         // not its home mode

        EffectRuntime.Remove(mind.Thing, e!, notify: true);
        Assert.Equal(home, mind.Mode);
        Assert.Null(mind.Target);
    }

    [Fact]
    public void A_locked_mind_refuses_unless_forced()
    {
        using var files = Install.Open();
        var (_, minds, christof, mind) = Open(files);
        mind.Flags |= Mind.FlagUnaffected;
        minds.Mesmerize(mind, christof);
        minds.Daze(mind, christof);
        minds.Frighten(mind, christof);
        minds.EnterFrenzy(mind);
        Assert.NotEqual(AiMode.Mesmerized, mind.Mode);
        Assert.NotEqual(AiMode.Dazed, mind.Mode);
        Assert.NotEqual(AiMode.Afraid, mind.Mode);
        Assert.NotEqual(AiMode.Frenzied, mind.Mode);

        minds.Daze(mind, christof, force: true);
        Assert.Equal(AiMode.Dazed, mind.Mode);
    }

    /// <summary>
    /// The ranks: frenzy beats everything, mesmerism beats a daze and fear, a daze beats fear. A
    /// stronger mode is not displaced by a weaker one; a stronger one arriving clears the weaker
    /// effects.
    /// </summary>
    [Fact]
    public void Stronger_modes_hold_and_clear_the_weaker()
    {
        using var files = Install.Open();
        var (_, minds, christof, mind) = Open(files);
        EffectRuntime.Apply(mind.Thing, "ef_dazed", 0, durationMs: 10_000, creator: christof);
        Assert.Equal(AiMode.Dazed, mind.Mode);
        minds.Frighten(mind, christof);
        Assert.Equal(AiMode.Dazed, mind.Mode);                     // fear does not displace a daze

        EffectRuntime.Apply(mind.Thing, "ef_mesmerized", 0, durationMs: 10_000, creator: christof);
        Assert.Equal(AiMode.Mesmerized, mind.Mode);
        Assert.Null(mind.Thing.Effects.Find("ef_dazed"));          // the daze is taken off
        minds.Daze(mind, christof);
        Assert.Equal(AiMode.Mesmerized, mind.Mode);

        minds.EnterFrenzy(mind);
        Assert.Equal(AiMode.Frenzied, mind.Mode);
        Assert.Null(mind.Thing.Effects.Find("ef_mesmerized"));
        minds.Mesmerize(mind, christof);
        Assert.Equal(AiMode.Frenzied, mind.Mode);
        minds.LeaveFrenzy(mind);
        Assert.Equal(mind.BaseMode, mind.Mode);
    }

    /// <summary>The way out releases the mind whatever mode it is in by then (the effect's removal does not look).</summary>
    [Fact]
    public void An_ending_effect_releases_whatever_mode_the_mind_is_in()
    {
        using var files = Install.Open();
        var (_, minds, christof, mind) = Open(files);
        var fear = EffectRuntime.Apply(mind.Thing, "ef_afraid", 0, durationMs: 10_000, creator: christof)!;
        Assert.Equal(AiMode.Afraid, mind.Mode);
        mind.Enter(AiMode.Enemy, andBase: false);                  // something else took it since
        EffectRuntime.Remove(mind.Thing, fear, notify: true);
        Assert.Equal(mind.BaseMode, mind.Mode);
    }
}
