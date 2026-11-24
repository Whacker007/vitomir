// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **The chapter swap as the scripts see it** (`CodexScenes.Swap.cs`).
/// The old chapter's things are deleted with their scripts — the scripts freed, their timers and
/// captures with them, and no `endscene` — and the new chapter's things come
/// with theirs, made from their parameters against the new chapter's own list and told `created`.
/// In the place the party is in that is at once; in a place kept loaded while the party is elsewhere
/// the new ones are made as the party comes back (ours).
/// </summary>
[Collection("scripts")]
public class ChapterSwapScriptTests(Scripts s)
{
    [Fact]
    public void The_old_chapter_s_scripts_go_with_their_things_and_the_new_chapter_s_come_with_theirs()
    {
        using var p = Place.Enter(s, "P1_CNVT", "CNVT_3_1");
        Assert.Contains("CNVT_3_1", p.Scenes.Running);
        var oldChapter = p.Where.Things.Where(t => t.Index >= (p.Where.WorldScene?.Things.Length ?? 0)
                                                   && t.Index < (p.Where.WorldScene?.Things.Length ?? 0) + p.Where.ChapterScene!.Things.Length).ToArray();
        int levelScripts = p.Scenes.ThingScripts - oldChapter.Count(t => t.Script.Length > 0);

        var swap = p.Where.SwapChapter("CNVT_3_3.nsd");

        Assert.NotNull(swap);
        Assert.DoesNotContain("CNVT_3_1", p.Scenes.Running);
        Assert.Contains("CNVT_3_3", p.Scenes.Running);
        Assert.Empty(p.Where.AwaitingScripts);
        Assert.Equal(levelScripts + swap!.Arrived.Count(t => t.Script.Length > 0), p.Scenes.ThingScripts);
        // What CNVT_3_1 had captured is let go: only scripts still running hold anything in the place.
        var holders = p.Where.Things.SelectMany(t => t.Captures).Select(c => c.Script).Distinct().ToArray();
        Assert.True(holders.Length <= p.Scenes.Running.Count(), "a freed script still holds a capture");
        // And the place goes on running with the new chapter's scripts in it.
        p.Run(2f);
    }

    [Fact]
    public void A_place_swapped_while_the_party_is_away_makes_its_new_scripts_as_the_party_comes_back()
    {
        using var p = Place.Enter(s, "P1_CNVT", "CNVT_3_1");
        p.Scenes.Leave(0, keep: true);                                         // the party goes; the place stays loaded

        var swap = p.Where.SwapChapter("CNVT_3_3.nsd");
        Assert.NotNull(swap);
        Assert.NotEmpty(p.Where.AwaitingScripts);                              // nothing made while away

        p.Scenes.Enter(p.Where);                                               // back: beginscene to the same objects, and the new ones made
        Assert.DoesNotContain("CNVT_3_1", p.Scenes.Running);
        Assert.Contains("CNVT_3_3", p.Scenes.Running);
        Assert.Empty(p.Where.AwaitingScripts);
    }
}
