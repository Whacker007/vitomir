// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A character carried through a door is not born again.** In 1.1 the thing itself makes the journey:
/// a new game has Christof as guid 5, with the same two effects, through the
/// convent, the convent by day and Old Town. Vitomir builds a fresh body in the new place, which the
/// place's waking gives its birth effects (`Location.Awaken`), and moved the travelling character's
/// own list in beside them — so Christof got back at the first door the `ef_decreasefrenzy` the
/// convent's opening takes off him when it makes him human (`CNVT_3_1`). The fresh body's own are
/// discarded with its character now (`Campaign.Carry`).
/// </summary>
public class CarriedEffectsTests
{
    [Fact]
    public void A_carried_character_keeps_its_own_effects_and_nothing_more()
    {
        var dir = GameFiles.Locate();
        Assert.SkipWhen(dir is null, "no game install");
        using var files = new GameFiles(dir!);
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        var chronicle = where.Chronicle;
        chronicle.Current = chronicle.Find(where.Entry!.Name);
        var classes = new AiClasses(files);
        var effects = new EffectDefinitions(files);
        Location Build(string name, string? scene, int entrance)
        {
            var place = Location.Load(files, name, sky: false, scene: scene, player: "Christof", entrance: entrance);
            place.Awaken(classes, 1, effects);
            return place;
        }
        var here = Location.Load(files, "P1_MON1", sky: false, scene: where.Entry.SceneName, player: "Christof");
        here.Awaken(classes, 1, effects);
        var christof = here.Player!.Thing;
        christof.Actor!.Flags |= Actor.Flag.Party;
        Assert.NotNull(christof.Effects.Find("ef_decreasefrenzy"));      // born with it: the vampire's own

        // What CNVT_3_1 does to make him human, and what it gives him instead.
        EffectRuntime.Remove(christof, "ef_decreasefrenzy");
        EffectRuntime.Apply(christof, "ef_increasemanaot", 0);
        string[] Live(WorldThing who) => [.. who.Effects.All.Where(e => !e.Spent).Select(e => e.Definition.Name).Order()];
        var before = Live(christof);

        var campaign = new Campaign { Chronicle = chronicle, Build = Build, Here = here };
        Assert.True(campaign.Chronicle.TakeExit(0));
        Assert.NotNull(campaign.Travel());
        var arrived = campaign.Here.Player!.Thing;
        Assert.NotSame(christof, arrived);
        Assert.Equal(before, Live(arrived));
        Assert.Null(arrived.Effects.Find("ef_decreasefrenzy"));
    }
}
