// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **What the first chapter swap left undone** (`World/ChapterSwap.cs`, `Render/SkySwap.cs`). The end
/// of the frame swaps a flagged location on the flag alone, so a chapter changed
/// away and back in one frame is loaded afresh; a new chapter whose file is there but will not load
/// leaves the place with none and its scene named `__INVALID__`; a chapter character the
/// swap deletes has its disciplines' scripts freed with it; and the
/// new chapter's sky replaces the old at once, where the old had none too.
/// </summary>
public class ChapterSwapLeftoversTests
{
    static WorldThing[] Chapter(Location place)
    {
        int world = place.WorldScene?.Things.Length ?? 0;
        return [.. place.Things.Where(t => t.Index >= world && t.Index < world + (place.ChapterScene?.Things.Length ?? 0))];
    }

    [Fact]
    public void A_chapter_changed_away_and_back_in_one_frame_is_loaded_afresh()
    {
        var (campaign, prague) = ChapterSwapTests.Open("Convent");
        var convent = campaign.Here;
        var before = Chapter(convent);
        Assert.NotEmpty(before);
        int highest = convent.Things.Max(t => t.Guid);

        // The swap flags the change to CNVT_3_3; the change back is a change from CNVT_3_3, and the
        // flag stays. The end of the frame looks only at the flag.
        prague.ChangeScene("Convent", "CNVT_3_3.nsd");
        prague.ChangeScene("Convent", "CNVT_3_1.nsd");
        var swap = Assert.Single(campaign.SwapChapters());

        Assert.Equal("CNVT_3_1", convent.SceneFile, ignoreCase: true);
        Assert.Equal(before.Length, swap.Gone.Length);
        Assert.All(before, t => Assert.True(t.Removed, $"{t.Template} of the old CNVT_3_1 was not torn down"));
        var after = Chapter(convent);
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(before.Select(t => t.Template), after.Select(t => t.Template));
        Assert.All(after, t => Assert.DoesNotContain(t, before));                     // new objects, not the old ones kept
        Assert.All(after, t => Assert.True(t.Guid > highest, $"{t.Template} kept an old number"));
        Assert.Empty(prague.Swaps);
    }

    [Fact]
    public void A_scene_already_the_location_s_is_no_change_and_swaps_nothing()
    {
        var (campaign, prague) = ChapterSwapTests.Open("Convent");
        var before = Chapter(campaign.Here);
        string name = prague.At(prague.Find("Convent"))!.Scene;                      // as the chronicle names it
        prague.ChangeScene("Convent", name.ToLowerInvariant());                       // _strcmpi answers 0
        Assert.Empty(prague.Swaps);
        Assert.Empty(campaign.SwapChapters());
        Assert.Equal(before, Chapter(campaign.Here));
    }

    [Fact]
    public void A_new_chapter_that_will_not_load_leaves_the_place_without_one_and_its_scene_named_INVALID()
    {
        // A scene file that is there but is not one: four bytes, no header (the scene read gives up at once).
        string overlay = Path.Combine(Path.GetTempPath(), "vitomir-amix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(overlay, "scenes"));
        try
        {
            File.WriteAllBytes(Path.Combine(overlay, "scenes", "AMIX_BROKEN.nsd"), [1, 2, 3, 4]);
            var files = new GameFiles(Install.Open().Root, [overlay]);
            var (campaign, prague) = ChapterSwapTests.Open("Convent", files);
            var convent = campaign.Here;
            var old = Chapter(convent);
            var level = convent.Things.Where(t => t.Index >= 0 && t.Index < (convent.WorldScene?.Things.Length ?? 0)).ToArray();

            prague.ChangeScene("Convent", "AMIX_BROKEN.nsd");
            var swap = Assert.Single(campaign.SwapChapters());

            Assert.True(swap.Failed);
            Assert.Null(swap.To);
            Assert.All(old, t => Assert.True(t.Removed, $"{t.Template} outlived a chapter torn down before the load failed"));
            Assert.Empty(swap.Arrived);
            Assert.Null(convent.ChapterScene);
            Assert.All(level, t => Assert.Contains(t, convent.Things));                 // the level scene stays
            Assert.Equal(Location.InvalidScene, prague.At(prague.Find("Convent"))!.Scene);

            // A ChangeScene to a real scene is a change from "__INVALID__", and swaps again.
            prague.ChangeScene("Convent", "CNVT_3_3.nsd");
            var again = Assert.Single(campaign.SwapChapters());
            Assert.False(again.Failed);
            Assert.Equal("CNVT_3_3", convent.SceneFile, ignoreCase: true);
            Assert.NotEmpty(again.Arrived);
        }
        finally { Directory.Delete(overlay, recursive: true); }
    }

    /// <summary>The script layer as the engine sees it, keeping what it made and what it was told to free.</summary>
    sealed class Recorded : IScriptObjects
    {
        public List<object> Made { get; } = [];
        public List<object> Freed { get; } = [];
        public object? Make(string className, WorldThing owner) { var o = new object(); Made.Add(o); return o; }
        public void Free(object script) => Freed.Add(script);
        public void EffectStarted(object script, WorldThing actor, Effect effect, int duration) { }
        public void EffectEnded(object script, WorldThing actor, Effect effect, int reason) { }
        public bool CanCast(object script, int level, WorldThing caster, WorldThing? target, Vector3? at) => true;
        public void Cast(object script, CastForm form, int level, WorldThing caster, WorldThing? target, Vector3 at) { }
        public void ActorCast(WorldThing caster, string discipline, int level, int castNum) { }
        public void ActorFeed(WorldThing feeder, WorldThing victim, int status) { }
        public void Stilled(WorldThing actor) { }
        public void Unstill(WorldThing actor) { }
        public void Rehome(object script, WorldThing owner) { }
    }

    [Fact]
    public void A_chapter_character_the_swap_deletes_has_its_discipline_scripts_freed_with_it()
    {
        var (campaign, prague) = ChapterSwapTests.Open("Convent");
        var convent = campaign.Here;
        var scripts = new Recorded();
        convent.ScriptObjects = scripts;
        var powers = new DisciplineDefinitions(Install.Open());
        var goer = Chapter(convent).First(t => t.Actor is not null);
        var stayer = convent.Player!.Thing;                                          // Christof is no chapter's
        Assert.NotNull(DisciplineBook.SetLevel(goer, powers, "BloodHealing", 1));   // and the next in its group, unlocked
        Assert.NotNull(DisciplineBook.SetLevel(stayer, powers, "BloodHealing", 1));
        var goers = goer.Actor!.Disciplines.All.Where(d => d.Script is not null).ToArray();
        var stayers = stayer.Actor!.Disciplines.All.Select(d => d.Script).OfType<object>().ToArray();
        Assert.NotEmpty(goers);

        prague.ChangeScene("Convent", "CNVT_3_3.nsd");
        Assert.Single(campaign.SwapChapters());

        Assert.True(goer.Removed);
        Assert.All(goers, d => Assert.Null(d.Script));
        Assert.Equal(goers.Length, scripts.Freed.Count);                              // Each one freed
        Assert.All(stayers, s => Assert.DoesNotContain(s, scripts.Freed));
    }

    [Fact]
    public void The_new_chapter_s_sky_replaces_the_old_even_where_the_old_had_none()
    {
        var (campaign, prague) = ChapterSwapTests.Open("OldTown", sky: true);
        var oldTown = campaign.Here;
        Assert.Equal("pragueEastNight", oldTown.Sky?.Name, ignoreCase: true);
        prague.ChangeScene("OldTown", "OLDT_25_1.nsd");                             // Prague2's Old Town, another sky
        Assert.Single(campaign.SwapChapters());
        Assert.Equal("Prague2Night", oldTown.Sky?.Name, ignoreCase: true);
        Assert.NotNull(oldTown.SkyWindow);

        var (days, story) = ChapterSwapTests.Open("ConventDay", sky: true);
        var conventDay = days.Here;
        Assert.NotNull(conventDay.Sky);
        story.ChangeScene("ConventDay", "CTDY_6_1.nsd");                            // STHM_6_1's: no sky
        Assert.Single(days.SwapChapters());
        Assert.Null(conventDay.Sky);
        Assert.Null(conventDay.SkyWindow);
        story.ChangeScene("ConventDay", "CTDY_4_1.nsd");                            // and back: the sky again
        Assert.Single(days.SwapChapters());
        Assert.NotNull(conventDay.Sky);
        Assert.NotNull(conventDay.SkyWindow);
    }

    [Fact]
    public void A_place_loaded_without_its_sky_stays_without_one()
    {
        var (campaign, prague) = ChapterSwapTests.Open("OldTown");                  // sky: false, as a viewer's
        Assert.Null(campaign.Here.Sky);
        prague.ChangeScene("OldTown", "OLDT_25_1.nsd");
        Assert.Single(campaign.SwapChapters());
        Assert.Null(campaign.Here.Sky);
    }
}
