// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats.Tests;

/// <summary>
/// The `.nsw` reader against real saves. Like `!main.nsg`
/// there is no length anywhere, so a whole file landing on its last byte is the proof — and then
/// its records hanging together: every thing names its scene the way the game writes it, and
/// indexes a slot that scene has; every capture and timer names a script in the same file.
/// </summary>
public class NswTests
{
    static string[] Files()
    {
        GameData.RequireInstall();
        string dir = Path.Combine(GameData.Dir!, "SaveGames");
        Assert.SkipUnless(Directory.Exists(dir), $"no SaveGames folder in {GameData.Dir}");
        var files = Directory.EnumerateFiles(dir, "location_*.nsw", SearchOption.AllDirectories)
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.SkipWhen(files.Length == 0, $"no .nsw files in {dir}");
        return files;
    }

    [Fact]
    public void Every_location_file_parses_to_its_last_byte()
    {
        var failures = new Dictionary<string, string>();
        foreach (var f in Files())
        {
            try { NswWorld.ReadFile(f); }
            catch (NodFormatException e) { failures[f] = e.Message; }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Select(kv => $"{kv.Key}: {kv.Value}")));
    }

    [Fact]
    public void Each_thing_names_a_slot_of_its_scene_and_the_sectors_are_numbered_in_order()
    {
        foreach (var f in Files())
        {
            var w = NswWorld.ReadFile(f);
            Assert.Equal(Enumerable.Range(0, w.Sectors.Length).Select(i => (uint)i), w.Sectors.Select(s => s.Index & 0xffff));
            Assert.All(w.Sectors, s => Assert.Equal((int)(s.CountWord & 0xffff), s.Guids.Length));
            foreach (var t in w.Things)
            {
                Assert.True(t.Scene == NswWorld.OwnScene || t.Scene == -1 || (w.ChapterScene is { } c && t.Scene == c.Id), $"{f}: {t.Name} scene {t.Scene}");
                if (t.Scene == NswWorld.OwnScene) Assert.True(t.Field28 < w.WorldScene!.Count, $"{f}: {t.Name} index {t.Field28}");
                else if (t.Scene >= 0) Assert.True(t.Field28 < w.ChapterScene!.Count, $"{f}: {t.Name} index {t.Field28}");
                // A projectile is never saved, so a file never has one.
                Assert.NotEqual(NsgThingType.Projectile, t.Type);
                Assert.False(t.Item is { IsEquipped: true }, $"{f}: an equipped {t.Name}");
                Assert.False(t.Actor is { } a && (a.Field1F4 & 0x20000000) != 0 && t.Type == NsgThingType.Player, $"{f}: a party member");
            }
            // No two things share a slot of a scene.
            var slots = w.Things.Where(t => t.Scene != -1).Select(t => (t.Scene, t.Field28)).ToArray();
            Assert.Equal(slots.Length, slots.Distinct().Count());
            // Every capture is of a thing in the file.
            var guids = w.Things.Select(t => t.Guid).ToHashSet();
            Assert.All(w.Captures, c => Assert.Contains(c.ThingGuid, guids));
        }
    }
}
