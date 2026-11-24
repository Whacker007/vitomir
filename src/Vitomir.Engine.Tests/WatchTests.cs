// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What the diagnostic overlay says. The content is the part that can be wrong in a way nobody
/// notices — a label that reads the same whether a character is dead or merely idle is worse than
/// no label — so it is tested here, with no GPU anywhere near it.
/// </summary>
public class WatchTests
{
    static (Location Place, Minds Minds, Simulation Sim) Chantry(string stance = "attack")
    {
        var files = Install.Open();
        var place = Location.Load(files, "p1_arc3", things: true, sky: false,
                                  scene: "ARC3_17_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        // Recruit Erik the way the chronicle would, which is the only way a party ever exists.
        foreach (var m in minds.All)
            if (m.Thing.Template.Equals("erik", StringComparison.OrdinalIgnoreCase) && m.Thing.Actor is { } a)
            {
                a.Flags |= Actor.Flag.Party;
                a.Stance = stance == "attack" ? PartyStance.Attack : PartyStance.Neutral;
            }
        return (place, minds, new Simulation(place));
    }

    [Fact]
    public void The_panel_names_the_level_the_player_and_the_party()
    {
        var (place, _, sim) = Chantry();
        var report = Watch.Look(place, sim);
        string all = string.Join("\n", report.Panel);

        Assert.Contains("p1_arc3", all);
        Assert.Contains("ARC3_17_1", all);
        Assert.Contains("Christof", all);
        Assert.Contains("hp 100/100", all);
        Assert.Contains("party (1)", all);
        Assert.Contains("rik", all);          // the cast id is "Erik"; the template is "erik"
        // And the counts that tell "nothing is moving" apart from "nothing is there".
        Assert.Contains("minds", all);
        Assert.Contains("0 dead", all);
    }

    [Fact]
    public void A_dead_character_reads_as_dead_and_greys_out()
    {
        // The whole reason the overlay exists: a level where nothing moves looks the same whether
        // the AI stalled or the player died, and those want opposite fixes.
        var (place, minds, sim) = Chantry();
        var victim = minds.All.First(m => m.Thing.Template == "hopper").Thing;
        Damage.Kill(victim, null, HitKind.Melee, DamageType.Lethal, 0);

        var label = Watch.Look(place, sim, range: float.MaxValue)
                         .Labels.First(l => ReferenceEquals(l.Thing, victim));
        Assert.Equal(WatchTint.Gone, label.Tint);
        Assert.Contains("dead", label.Lines);
        Assert.Equal(0f, label.Health);

        Assert.Contains("1 dead", string.Join("\n", Watch.Look(place, sim).Panel));
    }

    [Fact]
    public void A_label_says_what_a_character_is_doing_and_who_to()
    {
        var (place, minds, sim) = Chantry();
        var hunter = minds.All.First(m => m.Thing.Template == "tremereanc_da");
        var prey = place.Player!.Thing;
        hunter.Aim(prey, 0);

        var label = Watch.Look(place, sim, range: float.MaxValue)
                         .Labels.First(l => ReferenceEquals(l.Thing, hunter.Thing));
        Assert.Equal(WatchTint.Foe, label.Tint);
        Assert.Contains(label.Lines, l => l.Contains("-> Christof"));
        Assert.Equal(1f, label.Health);

        // The same character with nobody in mind is amber, not red.
        hunter.Aim(null, 0);
        Assert.Equal(WatchTint.Looking, Watch.Look(place, sim, range: float.MaxValue)
                                             .Labels.First(l => ReferenceEquals(l.Thing, hunter.Thing)).Tint);
    }

    [Fact]
    public void The_party_is_green_and_the_people_a_search_steps_over_are_not_red()
    {
        var (place, minds, sim) = Chantry();
        var labels = Watch.Look(place, sim, range: float.MaxValue).Labels;

        Assert.Equal(WatchTint.Friend, labels.First(l => ReferenceEquals(l.Thing, place.Player!.Thing)).Tint);
        Assert.Equal(WatchTint.Friend, labels.First(l => l.Thing.Template == "erik").Tint);

        // THING_AF_NEUTRAL and THING_AF_TALKTO are the two flags every search skips, so somebody
        // carrying one is not a threat however hostile their template looks.
        var bystander = minds.All.First(m => m.Thing.Template == "hopper");
        bystander.Thing.Actor!.Flags |= Actor.Flag.TalkTo;
        Assert.Equal(WatchTint.Idle, Watch.Look(place, sim, range: float.MaxValue)
                                          .Labels.First(l => ReferenceEquals(l.Thing, bystander.Thing)).Tint);
    }

    [Fact]
    public void Labels_are_nearest_first_and_capped()
    {
        var (place, _, sim) = Chantry();
        var me = place.Player!.Thing.Position;

        var all = Watch.Look(place, sim, range: float.MaxValue, limit: 100).Labels;
        Assert.True(all.Count > 3);
        // Ordered by where the *body* is, not where the label floats: a tall character's label sits
        // further from the eye than a short one standing beside it.
        for (int i = 1; i < all.Count; i++)
            Assert.True(Vector3.Distance(all[i].Thing.Position, me)
                        >= Vector3.Distance(all[i - 1].Thing.Position, me) - 0.01f,
                        "labels are not nearest first");

        Assert.Equal(3, Watch.Look(place, sim, range: float.MaxValue, limit: 3).Labels.Count);
        // And a tight range keeps only what is close, with the player always in.
        var near = Watch.Look(place, sim, range: 1f).Labels;
        Assert.Single(near);
        Assert.Same(place.Player!.Thing, near[0].Thing);
    }

    [Fact]
    public void A_label_floats_above_the_body_rather_than_inside_it()
    {
        var (place, _, sim) = Chantry();
        foreach (var label in Watch.Look(place, sim, range: float.MaxValue).Labels)
        {
            Assert.Equal(label.Thing.Position.X, label.At.X, 3);
            Assert.True(label.At.Z > label.Thing.Position.Z,
                        $"{label.Thing.Template}'s label is not above it");
        }
    }
}
