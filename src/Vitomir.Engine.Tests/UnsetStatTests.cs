// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A stat a template does not set is 20** (the actor's constructor writes 20 into all
/// nineteen, base and current, before the template's tags are read). In 1.1
/// (a new game to Old Town by day) every Old Town townsperson
/// arrives with humanity, frenzy, generation, frenzy rating, mana and mana pool at 20, where Vitomir had
/// 0 — and the Monastery's every monk, rat and skeleton the same. The
/// numbers here are 1.1's stats for them.
/// </summary>
public class UnsetStatTests
{
    static float[] Stats(TemplateSet templates, string name) =>
        Actor.From(templates.Resolve(name))!.BaseStat.ToArray();

    [Fact]
    public void A_stat_the_template_leaves_alone_starts_at_twenty()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);

        // A monk sets nothing but his blood: `_actor`'s others, and 20 for the six nobody sets.
        Assert.Equal(new[] { 20, 20, 20, 20, 20, 20, 70, 20, 20, 20, 20, 20, 20, 20, 20, 1, 70, 20, 20 },
                     Stats(templates, "civ10monk2").Select(v => (int)v));
        // A rat sets its humanity to 0 and keeps it; its frenzy, generation, frenzy rating and mana are 20.
        Assert.Equal(new[] { 5, 15, 15, 5, 5, 5, 80, 0, 20, 20, 20, 0, 0, 0, 20, 1, 80, 20, 20 },
                     Stats(templates, "rat").Select(v => (int)v));
        // And both copies: the current row starts as the base row does.
        var monk = Actor.From(templates.Resolve("civ10monk2"))!;
        Assert.Equal(Actor.UnsetStat, monk.Stat[8]);
        Assert.Equal(Actor.UnsetStat, monk.BaseStat[17]);
    }
}
