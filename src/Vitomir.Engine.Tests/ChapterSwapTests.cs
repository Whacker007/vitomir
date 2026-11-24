// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The live chapter swap** (`World/ChapterSwap.cs`).
/// 1.1 never builds a loaded place again: a `ChangeScene` on one
/// swaps its chapter scene alone at the end of the frame, and everything else in the place — the level
/// scene's things, and whatever was dropped, spawned, moved or destroyed there — stays as it was,
/// while the new chapter's things are numbered from the game's running counter. `CNVT_3_1` does it to
/// the convent the party is standing in and then jumps into it; a place remembered from a save or let
/// go is read back and swapped the same way. Earlier, Vitomir built all of these again from the files.
/// </summary>
public class ChapterSwapTests
{
    /// <summary>Prague's campaign standing in <paramref name="start"/> — on <paramref name="files"/> when given, with skies when asked.</summary>
    internal static (Campaign Campaign, Chronicle Prague) Open(string start, Vitomir.Engine.Vfs.GameFiles? files = null, bool sky = false)
    {
        files ??= Install.Open();
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        var prague = ChronicleSet.Load(files)["prague"]!;
        var session = new Session { Chronicle = prague };
        session.Messages.Text = new Strings(files);
        var guids = new ThingGuids();
        Location Build(string name, string? scene, int entrance)
        {
            var place = Location.Load(files, name, things: false, sky: sky, scene: scene, player: "Christof",
                                      entrance: entrance, guids: ThingGuids.Building ?? guids);
            place.Session = session;
            return place;
        }
        Location BuildSaved(LocationSave record, int entrance)
        {
            var place = Build(record.Level, record.Scene.Length > 0 ? record.Scene : null, entrance);
            SaveApply.Place(place, record, effects, powers);
            return place;
        }
        int at = prague.Find(start);
        Assert.True(at >= 0, $"Prague has no {start}");
        prague.Current = at;
        var where = prague.At(at)!;
        var here = Build(where.LevelName, where.SceneName, 0);
        session.Here = here;
        return (new Campaign { Chronicle = prague, Build = Build, BuildSaved = BuildSaved, Here = here }, prague);
    }

    static int WorldCount(Location place) => place.WorldScene?.Things.Length ?? 0;

    static WorldThing[] Chapter(Location place) =>
        [.. place.Things.Where(t => t.Index >= WorldCount(place) && t.Index < WorldCount(place) + (place.ChapterScene?.Things.Length ?? 0))];

    static WorldThing LevelThing(Location place, Func<WorldThing, bool> which) =>
        place.Things.First(t => t.Index >= 0 && t.Index < WorldCount(place) && which(t));

    [Fact]
    public void A_changed_chapter_is_swapped_in_the_live_place_and_everything_else_stays()
    {
        var (campaign, prague) = Open("Convent");
        var convent = campaign.Here;
        Assert.Equal("CNVT_3_1", convent.SceneFile, ignoreCase: true);
        int world = WorldCount(convent);
        var oldChapter = Chapter(convent);
        Assert.NotEmpty(oldChapter);

        // What happens in the place before the chapter changes: a level thing moved (a door opened),
        // another destroyed, an item dropped, a thing spawned, and one of the chapter's own taken up.
        // The door: a level thing with keyframes put at its second (opened) — the convent's level has
        // none, so a prop moved stands in for it.
        var moved = convent.Things.FirstOrDefault(t => t.Index >= 0 && t.Index < world && t.Keyframes.Length > 1)
                    ?? LevelThing(convent, t => t.Type == ThingType.Prop && t.Scene.Model is null);
        var movedTo = moved.Keyframes.Length > 1 ? moved.Keyframes[1].Position : moved.Position + new Vector3(0, 0, 40);
        convent.Move(moved, movedTo);
        var broken = LevelThing(convent, t => t.Type == ThingType.Prop && !ReferenceEquals(t, moved));
        convent.Remove(broken, drop: false);
        var christof = convent.Player!.Thing;
        var dropped = convent.Place("broadsword", christof.Position + new Vector3(30, 0, 0), Vector3.Zero, christof.Sector)!;
        var spawned = convent.Place("vitaerat", christof.Position + new Vector3(-30, 0, 0), Vector3.Zero, christof.Sector)!;
        var taken = oldChapter.First(t => t.Type is ThingType.Item or ThingType.Weapon or ThingType.Armor or ThingType.Prop);
        convent.Withdraw(taken);
        var levelBefore = convent.Things.Where(t => t.Index >= 0 && t.Index < world).ToArray();
        int counter = convent.Guids.Next;

        Assert.True(prague.ChangeScene("Convent", "CNVT_3_3.nsd"));
        var swaps = campaign.SwapChapters();                                   // the end of the frame

        var swap = Assert.Single(swaps);
        Assert.Same(convent, campaign.Here);                                    // the same place, not a new one
        Assert.Equal("CNVT_3_3", convent.SceneFile, ignoreCase: true);
        Assert.Empty(prague.Swaps);
        // The old chapter's things still in the world are destroyed; the one taken up is not.
        foreach (var t in oldChapter.Where(t => !ReferenceEquals(t, taken)))
        {
            Assert.DoesNotContain(t, convent.Things);
            Assert.True(t.Removed, $"{t.Template} of the old chapter is still there");
        }
        Assert.False(taken.Removed);
        Assert.Equal(-1, taken.Index);                                          // no scene's thing any more,
        Assert.StartsWith("P1_CNVT~", taken.SaveId);                            // named as a thing made in the place
        Assert.Equal(oldChapter.Length - 1, swap.Gone.Length);
        // Everything else is as it was.
        Assert.Equal(levelBefore, convent.Things.Where(t => t.Index >= 0 && t.Index < world).ToArray());
        Assert.Equal(movedTo, moved.Position);                                  // the door still open
        Assert.DoesNotContain(broken, convent.Things);
        Assert.Contains(dropped, convent.Things);
        Assert.Contains(spawned, convent.Things);
        Assert.Contains(christof, convent.Things);
        // The new chapter's things, in its file order, on its scene indices and numbered from the counter.
        var arrived = Chapter(convent);
        Assert.Equal(convent.ChapterScene!.Things.Length, arrived.Length);
        Assert.Equal(swap.Arrived, arrived.OrderBy(t => t.Index).ToArray());
        for (int i = 0; i < swap.Arrived.Length; i++)
        {
            Assert.Equal(world + i, swap.Arrived[i].Index);
            Assert.True(swap.Arrived[i].Guid >= counter, "a new chapter thing below the counter");
            if (i > 0) Assert.True(swap.Arrived[i].Guid > swap.Arrived[i - 1].Guid, "not numbered in file order");
            Assert.Same(swap.Arrived[i], convent.Thing(swap.Arrived[i].Guid));
            Assert.Equal($"CNVT_3_3#{i}", ThingIds.Placed(convent, swap.Arrived[i]), ignoreCase: true);
        }
        Assert.Equal(counter, swap.Arrived.Min(t => t.Guid) - Fallbacks(convent, swap.Arrived[0]));
        // The player follows the scene's things and is still nobody's placed thing.
        Assert.Equal(world + arrived.Length, christof.Index);
        Assert.Null(ThingIds.Placed(convent, christof));
        // Only the level's regions and the new chapter's are in the sectors' lists.
        for (long s = 0; s < convent.Level.Sectors.Length; s++)
            foreach (var r in convent.Regions!.In(s).OfType<WorldThing>())
                Assert.True(r.Index < world || swap.Arrived.Contains(r), $"an old chapter's region {r.Template} is still in sector {s}");
    }

    /// <summary>A character's fallback weapon spends a number before it (LocationGuids.SpendFallback).</summary>
    static int Fallbacks(Location place, WorldThing first) =>
        first.Actor is { WeaponTemplate.Length: > 0 } a && place.Templates!.Has(a.WeaponTemplate) ? 1 : 0;

    [Fact]
    public void A_region_the_swap_takes_away_is_left_without_a_word()
    {
        var (campaign, prague) = Open("Convent");
        var convent = campaign.Here;
        var christof = convent.Player!.Thing;
        int world = WorldCount(convent);
        // Christof stands in one of the chapter's regions, its bit set in his sector's list.
        int slot = -1;
        foreach (var region in Chapter(convent).Where(t => t.Type == ThingType.Region))
        {
            convent.Move(christof, region.Position);
            if ((slot = convent.Regions!.In(christof.Sector).ToList().IndexOf(region)) >= 0) break;
        }
        Assert.True(slot >= 0, "no chapter region to stand in");
        christof.InRegions |= 1u << slot;
        var heard = new List<string>();
        convent.Regions!.Exited = (r, who) => heard.Add(r.Template);

        prague.ChangeScene("Convent", "CNVT_3_3.nsd");
        campaign.SwapChapters();

        Assert.Empty(heard);                                                    // Cleared silently
        int levelRegions = convent.Regions.In(christof.Sector).Count(r => r is { } x && x.Index < world);
        Assert.Equal(0u, christof.InRegions & (levelRegions >= 32 ? 0u : ~((1u << levelRegions) - 1)) & (1u << slot));
    }

    [Fact]
    public void The_party_standing_in_the_place_goes_on_standing_in_it_when_CNVT_3_1_jumps_back_in()
    {
        var (campaign, prague) = Open("Convent");
        var convent = campaign.Here;
        var christof = convent.Player!.Thing;
        var thrownAway = new List<Location>();
        campaign.Discarded += thrownAway.Add;
        Assert.False(campaign.Builds);

        // CNVT_3_1's convended: the next day, and a jump back into the convent (then state 5).
        Assert.True(prague.ChangeScene("Convent", "CNVT_3_3.nsd"));
        Assert.True(prague.Jump("Convent", 0));
        Assert.False(campaign.Builds);                                          // no loading plaque: it is loaded
        var arrival = campaign.Travel()!.Value;                                 // swaps first, as 1.1's swap runs before the main loop's request

        Assert.True(arrival.Returned);
        Assert.Same(convent, arrival.To);
        Assert.Same(convent, campaign.Here);
        Assert.Same(christof, convent.Player!.Thing);
        Assert.Contains(christof, convent.Things);
        Assert.Empty(thrownAway);
        Assert.Equal("CNVT_3_3", convent.SceneFile, ignoreCase: true);
        Assert.Equal(convent.ChapterScene!.Things.Length, Chapter(convent).Length);
        Assert.Equal(prague.Find("Convent"), prague.Current);
    }

    [Fact]
    public void A_remembered_place_whose_chapter_has_changed_is_read_back_and_swapped()
    {
        var (campaign, prague) = Open("Smithy");
        // The convent as a save remembers it, in its first chapter: a level thing destroyed there.
        var old = campaign.Build("P1_CNVT", "CNVT_3_1", 0);
        var broken = LevelThing(old, t => t.Type == ThingType.Prop);
        string brokenId = ThingIds.Placed(old, broken)!;
        old.Remove(broken, drop: false);
        var record = SaveCapture.OfPlace(old);
        Assert.Equal("CNVT_3_1", record.Scene, ignoreCase: true);
        int index = prague.Find("Convent");
        campaign.Parked[index] = record;

        // The chapter moves on while the convent is not loaded, and the party goes there.
        prague.ChangeScene("Convent", "CNVT_3_3.nsd");
        Assert.True(prague.Jump("Convent", 0));
        var arrival = campaign.Travel()!.Value;

        Assert.True(arrival.Restored);                                          // read back, not built from the files
        var convent = arrival.To;
        Assert.Equal("CNVT_3_3", convent.SceneFile, ignoreCase: true);
        Assert.DoesNotContain(convent.Things, t => ThingIds.Placed(convent, t) == brokenId);   // still broken
        Assert.DoesNotContain(convent.Things, t => ThingIds.Placed(convent, t)?.StartsWith("CNVT_3_1#", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Equal(convent.ChapterScene!.Things.Length, Chapter(convent).Length);
        Assert.All(Chapter(convent), t => Assert.False(ThingGuids.IsPlaceholder(t.Guid)));   // the counter's numbers, not a load's
    }

    [Fact]
    public void A_swapped_place_is_saved_and_read_back_as_it_stands()
    {
        var (campaign, prague) = Open("Convent");
        var convent = campaign.Here;
        var broken = LevelThing(convent, t => t.Type == ThingType.Prop);
        string brokenId = ThingIds.Placed(convent, broken)!;
        convent.Remove(broken, drop: false);
        prague.ChangeScene("Convent", "CNVT_3_3.nsd");
        campaign.SwapChapters();

        var record = SaveCapture.OfPlace(convent);
        Assert.Equal("CNVT_3_3", record.Scene, ignoreCase: true);
        convent.Guids.Loading = true;                                           // as the campaign reads one back
        Location back;
        try { back = campaign.BuildSaved!(record, 0); }
        finally { convent.Guids.Loading = false; }

        Assert.Equal("CNVT_3_3", back.SceneFile, ignoreCase: true);
        Assert.DoesNotContain(back.Things, t => ThingIds.Placed(back, t) == brokenId);
        foreach (var t in Chapter(convent))
        {
            string id = ThingIds.Placed(convent, t)!;
            Assert.Equal(t.Guid, back.Things.Single(b => ThingIds.Placed(back, b) == id).Guid);
        }
    }

    [Fact]
    public void A_scene_that_is_not_there_swaps_nothing_and_a_place_not_loaded_is_left_to_its_build()
    {
        var (campaign, prague) = Open("Convent");
        var convent = campaign.Here;
        var before = convent.Things.ToArray();
        prague.ChangeScene("Convent", "NO_SUCH_SCENE.nsd");
        prague.ChangeScene("Smithy", "SMTH_99_9.nsd");                          // not loaded
        Assert.Empty(campaign.SwapChapters());                                  // the file is not there
        Assert.Equal(before, convent.Things.ToArray());
        Assert.Equal("CNVT_3_1", convent.SceneFile, ignoreCase: true);
        Assert.Empty(prague.Swaps);
    }
}
