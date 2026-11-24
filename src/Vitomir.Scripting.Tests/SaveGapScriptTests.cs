// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>The script half of the save review's gaps (`SaveGapTests` has the rest).</summary>
public partial class SaveScriptTests
{
    /// <summary>
    /// **A discipline running on the party keeps its timers across a save** (1.1's timer section is the
    /// whole game's). Majesty ticks every two seconds, putting `ef_disc_majesty2` on whoever is
    /// near; its timer was left out of the save, so after a load the effect ran on and did nothing.
    /// </summary>
    [Fact]
    public void A_running_Majesty_still_ticks_after_a_load()
    {
        SaveGame first;
        int timers;
        using (var g = Begin())
        {
            var christof = g.Place.Player!.Thing;
            var me = christof.Actor!;
            me.Type = ActorType.Vampire;
            me.Stat[(int)ActorStat.BloodPool] = 100f;
            me.SetBaseStat((int)ActorStat.Blood, 100f);
            me.Stat[(int)ActorStat.Blood] = 100f;
            DisciplineBook.SetLevel(christof, new DisciplineDefinitions(g.Files), "Majesty", 0);
            Assert.NotNull(EffectRuntime.Apply(christof, "ef_disc_majesty", 0, 60000, christof));
            g.Step(20);
            timers = g.Scenes.Save(g.Place, guid => $"t{guid}", _ => "loose").Timers.Count(t => t.Script == "loose");
            Assert.True(timers > 0);
            var npc = g.Place.Things.First(t => t.Actor is { Dead: false } && !ReferenceEquals(t, christof) && t.Type == ThingType.Actor);
            g.Place.Move(npc, christof.Position + new Vector3(100, 0, 0));
            first = SaveCapture.Take(g.Place, g.Session);
            Assert.Contains(first.World[first.Header.Location].Timers, t => t.Script.StartsWith(ThingIds.Party, StringComparison.Ordinal));
        }
        using var back = Begin(SaveArchive.Read(Write(first)));
        Assert.Equal(timers, back.Scenes.Save(back.Place, guid => $"t{guid}", _ => "loose").Timers.Count(t => t.Script == "loose"));
        back.Step(300);                                                             // ten seconds
        Assert.Contains(back.Place.Things, t => t.Effects.Find("ef_disc_majesty2") is not null);
    }

    /// <summary>**The weather a script asked for comes back with the chronicle**.</summary>
    [Fact]
    public void The_weather_a_script_set_comes_back()
    {
        SaveGame first;
        using (var g = Begin())
        {
            g.Scenes.WeatherEffect = "drizzle";
            first = SaveCapture.Take(g.Place, g.Session);
        }
        Assert.Equal("drizzle", first.Chronicle!.Weather);
        using var back = Begin(SaveArchive.Read(Write(first)));
        Assert.Equal("drizzle", back.Scenes.WeatherEffect);
    }
}
