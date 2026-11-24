// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.IO.Compression;
using System.Text;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The files read ahead of the game (ours: <see cref="FileCache"/>, <see cref="Prefetcher"/>,
/// <see cref="SoundPrefetch"/>). What they pin is that reading ahead
/// never changes a result — a read answered from the cache is the same bytes the files give, a read
/// that races the read-ahead reads the file itself, a foley set or a voice loaded after the read-ahead
/// is the one loaded without it — and that the memory is let go: an entry is taken by its read, a place
/// or conversation let go takes its entries with it, and the cap is kept.
/// </summary>
public sealed class FileCacheTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("vitomir-cache").FullName;

    public FileCacheTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "game"));
        using (var zip = ZipFile.Open(Path.Combine(root, "game", "Resource.nob"), ZipArchiveMode.Create))
            foreach (var (name, text) in new[] { ("sounds/voice/a.wav", "archive a"), ("sounds/voice/b.wav", "archive b") })
                using (var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.NoCompression).Open())) w.Write(text);
        Directory.CreateDirectory(Path.Combine(root, "game", "sounds", "voice"));
        File.WriteAllText(Path.Combine(root, "game", "sounds", "voice", "B.WAV"), "loose b");
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    static string Text(byte[] b) => Encoding.UTF8.GetString(b);

    [Fact]
    public void A_read_answered_from_the_cache_is_the_same_bytes_as_a_read_from_the_files_and_is_taken_by_it()
    {
        using var files = new GameFiles(Path.Combine(root, "game"));
        using var plain = new GameFiles(Path.Combine(root, "game"));
        var ahead = new PrefetchReader(files, "place");
        Assert.True(ahead.Fetch("sounds/voice/a.wav", out _));
        Assert.True(ahead.Fetch(@"\sounds\voice\b.wav", out _));                // the loose file wins, as for a read
        Assert.False(ahead.Fetch("sounds/voice/none.wav", out _));
        Assert.Equal(2, files.Cache.Count);

        foreach (var path in new[] { "sounds/voice/a.wav", "SOUNDS/VOICE/B.wav" })
        {
            Assert.True(files.TryRead(path, out var cached));
            Assert.Equal(Text(plain.Read(path)), Text(cached));
        }
        Assert.Equal("loose b", Text(plain.Read("sounds/voice/b.wav")));
        Assert.Equal(0, files.Cache.Count);                                        // each taken by its read
        Assert.Equal(0, files.Cache.Bytes);
        Assert.Equal("archive a", Text(files.Read("sounds/voice/a.wav")));        // and read again from the files
    }

    [Fact]
    public void A_read_that_races_the_read_ahead_reads_the_file_itself_and_the_late_copy_is_not_kept()
    {
        var cache = new FileCache();
        Assert.True(cache.Claim("x"));
        Assert.False(cache.TryTake("x", out _));                                  // the game got there first
        Assert.False(cache.Put("x", [1, 2, 3], "place"));
        Assert.False(cache.Holds("x"));
        Assert.Equal(1, cache.Late);
        Assert.Equal(0, cache.Bytes);
    }

    [Fact]
    public void A_place_let_go_releases_its_entries_and_only_its_own()
    {
        var cache = new FileCache();
        object convent = new(), oldTown = new();
        foreach (var (path, scope) in new[] { ("a", convent), ("b", oldTown), ("c", convent) })
        {
            Assert.True(cache.Claim(path));
            Assert.True(cache.Put(path, new byte[100], scope));
        }
        Assert.True(cache.TryPeek("c", oldTown, out _));                          // the Old Town asked for c too: it is its now
        Assert.Equal(100, cache.Release(convent));
        Assert.Equal(["b", "c"], cache.Paths());
        Assert.Equal(200, cache.Bytes);
        Assert.Equal(300, cache.PeakBytes);
    }

    [Fact]
    public void Past_its_cap_the_cache_lets_the_oldest_go()
    {
        var cache = new FileCache { Cap = 250 };
        foreach (var path in new[] { "a", "b", "c" })
        {
            Assert.True(cache.Claim(path));
            cache.Put(path, new byte[100], null);
        }
        Assert.Equal(["b", "c"], cache.Paths());
        Assert.Equal(1, cache.Evicted);
        Assert.True(cache.Claim("huge"));
        Assert.False(cache.Put("huge", new byte[300], null));                     // bigger than the whole cap: not kept
        Assert.Equal(["b", "c"], cache.Paths());
    }

    [Fact]
    public void A_floors_surface_sound_is_read_from_its_material_once()
    {
        var file = Path.Combine(root, "game", "materials", "floor.nam");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "sound wood\n");
        using var files = new GameFiles(Path.Combine(root, "game"));
        int wood = Array.IndexOf(KeyframeSounds.SurfaceWords, "wood");
        Assert.Equal(wood, KeyframeSounds.MaterialSound(files, "floor"));
        File.Delete(file);
        Assert.Equal(wood, KeyframeSounds.MaterialSound(files, "FLOOR"));        // remembered, not opened again
        using var fresh = new GameFiles(Path.Combine(root, "game"));
        Assert.Equal(KeyframeSounds.Stone, KeyframeSounds.MaterialSound(fresh, "floor"));   // per set of files
    }

    static Location Convent(GameFiles files)
    {
        var where = ChronicleSet.Load(files).ForLevel("P1_CNVT");
        Assert.SkipWhen(where is null, "no convent in this install");
        return Location.Load(files, "P1_CNVT", sky: false, scene: where!.Value.Entry!.SceneName, player: "Christof");
    }

    [Fact]
    public void The_convents_foley_read_ahead_loads_exactly_the_sets_loaded_without_it_and_the_game_takes_every_file()
    {
        using var files = Install.Open();
        using var plain = new GameFiles(files.Root);
        var place = Convent(files);
        var bank = new SoundBank(files);
        var foley = new FoleySets(files, bank);
        var ahead = new SoundPrefetch(files, bank, foley);
        ahead.Place(place);
        // A guard against a hang, not a speed test: three minutes (one was too few on a laptop busy with the
        // other test assemblies, levels fork).
        Assert.True(ahead.Worker.WaitIdle(180_000));
        var read = files.Cache.Paths();
        Assert.Contains(read, p => p.EndsWith(".nag", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(read, p => p.EndsWith(".wav", StringComparison.OrdinalIgnoreCase));

        var plainFoley = new FoleySets(plain, new SoundBank(plain));
        foreach (var name in SoundPrefetch.SetNames(place).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var set = foley.Get(name);
            var alone = plainFoley.Get(name);
            Assert.Equal(alone is null, set is null);
            if (set is null || alone is null) continue;
            for (int slot = 0; slot < Nag.Slots; slot++)
            {
                Assert.Equal(alone[slot].Count, set[slot].Count);
                for (int i = 0; i < set[slot].Count; i++)
                {
                    var (x, y) = (set[slot][i].Sound, alone[slot][i].Sound);
                    Assert.Equal((y.Name, y.Path, y.LengthMs, y.Flags), (x.Name, x.Path, x.LengthMs, x.Flags));
                    Assert.Equal(y.Pcm, x.Pcm);
                    Assert.Equal(y.Lips.Length, x.Lips.Length);
                }
            }
        }
        Assert.Empty(files.Cache.Paths());                                        // every file read ahead was taken by a load
    }

    [Fact]
    public void A_conversations_voices_read_ahead_are_the_ones_it_loads()
    {
        using var files = Install.Open();
        using var plain = new GameFiles(files.Root);
        // The smithy's: its three recordings are among the few conversation voices the cloud's install has.
        Assert.SkipUnless(files.TryRead("conversations/4_1_Smithy.nco", out var data), "no smithy conversation in this install");
        var talk = NcoFile.Read(data, "4_1_Smithy.nco").Conversations
            .First(c => c.Events.SelectMany(e => e.Steps).Any(s => s.Id.Equals("Christof_4_2_54", StringComparison.OrdinalIgnoreCase)));
        var bank = new SoundBank(files);
        var ahead = new SoundPrefetch(files, bank, new FoleySets(files, bank));
        object scope = new();
        ahead.Conversation(scope, talk);
        Assert.True(ahead.Worker.WaitIdle(180_000));          // a guard, not a speed test (as above)
        Assert.SkipUnless(files.Cache.Paths().Any(p => p.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)), "none of its voices is in this install");

        var plainBank = new SoundBank(plain);
        foreach (var step in talk.Events.SelectMany(e => e.Steps).Where(s => s.Id.Length > 0))
        {
            var voice = bank.Load(step.Id, stream: true);
            var alone = plainBank.Load(step.Id, stream: true);
            Assert.Equal(alone?.Path, voice?.Path);
            Assert.Equal(alone?.LengthMs, voice?.LengthMs);
        }
        Assert.Empty(files.Cache.Paths());                                        // every voice read ahead was taken by its load
    }
}
