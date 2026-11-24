// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Which sound files to read ahead, and when. Vitomir's own.
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Audio;

/// <summary>
/// **Ours**: the sound files a place and a conversation will ask for, read ahead on the
/// <see cref="Prefetcher"/>'s thread so that the game's own reads find them in memory. Nothing here
/// changes what is loaded, when, or what it sounds like — the game still loads each sound when 1.1
/// did, through <see cref="SoundBank"/> and <see cref="FoleySets"/>; only the bytes are already there.
///
/// <list type="bullet">
/// <item><b>A place entered</b> (<see cref="Place"/>): the foley set (<c>.nag</c>) of every thing in
/// it, of every character's weapon and fists, and of everything they carry, that is not loaded yet —
/// 1.1 loads a set when its thing first makes a noise, which in the convent is in the
/// middle of the fight — and every recording each set names that the bank does not hold, with its
/// lip track.</item>
/// <item><b>A conversation started</b> (<see cref="Conversation"/>): every step's voice, found the
/// way a conversation finds it (a stream: <c>NAME.MP3</c> in the stream folders, then <c>NAME.WAV</c>
/// in the effect folders), with its lip track. The first line is on the same frame, so the
/// game usually reads that one itself; the rest are ready long before they are spoken.</item>
/// </list>
///
/// What each job needs from the game (which things, which sets are loaded, which sounds the bank
/// holds) is gathered here on the game's thread; the job itself only reads files and parses the
/// <c>.nag</c> text.
/// </summary>
public sealed class SoundPrefetch(GameFiles files, SoundBank bank, FoleySets foley)
{
    readonly ILogger log = Diag.For<SoundPrefetch>();

    /// <summary>The thread the files are read on.</summary>
    public Prefetcher Worker { get; } = new(files);

    /// <summary>The conversation last read ahead for; its leftovers go when the next one starts.</summary>
    object? lastTalk;

    /// <summary>Reads ahead the foley a place's things will need (called as the place is entered).</summary>
    public void Place(Location place)
    {
        Report();
        var sets = SetNames(place).Where(n => !foley.Has(n)).Select(FoleySets.Key)
                                  .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (sets.Count == 0) return;
        var held = Held();
        Worker.Enqueue(place, $"{place.Name}: {sets.Count} foley sets", r =>
        {
            foreach (var set in sets) FoleySet(r, set, held);
        });
    }

    /// <summary>
    /// Reads ahead every voice of <paramref name="talk"/> (called as it starts), its files belonging to
    /// <paramref name="scope"/>; what the conversation before it did not use is let go.
    /// </summary>
    public void Conversation(object scope, NcoConversation talk)
    {
        Report();
        if (lastTalk is not null && !ReferenceEquals(lastTalk, scope)) files.Cache.Release(lastTalk);
        lastTalk = scope;
        var ids = talk.Events.SelectMany(e => e.Steps).Select(s => s.Id).Where(id => id.Length > 0)
                             .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) return;
        Worker.Enqueue(scope, $"conversation {talk.Name}: {ids.Count} steps", r =>
        {
            foreach (var id in ids) Voice(r, id);
        });
    }

    /// <summary>The foley sets a place's things may load: their own, their weapons' and their belongings'.</summary>
    public static IEnumerable<string> SetNames(Location place)
    {
        foreach (var thing in place.Things)
        {
            if (thing.Foley.Length > 0) yield return thing.Foley;
            if (thing.Actor is not { } actor) continue;
            if (actor.Weapon?.Foley is { Length: > 0 } w) yield return w;
            if (actor.DefaultWeapon?.Foley is { Length: > 0 } fists) yield return fists;
            foreach (var item in actor.Inventory.Items)
            {
                if (item.Foley.Length > 0) yield return item.Foley;
                if (item.Weapon?.Foley is { Length: > 0 } iw) yield return iw;
            }
        }
    }

    /// <summary>The bank's sounds now, by their names (which for an effect is its key).</summary>
    HashSet<string> Held() => new(bank.Loaded.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);

    /// <summary>A set's <c>.nag</c> (the effect folders, then the bare name) and each effect it names.</summary>
    static void FoleySet(PrefetchReader r, string name, HashSet<string> held)
    {
        if (r.First(SoundBank.EffectFolders.Select(d => d + "/" + name).Append(name), out var nag) is null) return;
        foreach (var e in Nag.Read(System.Text.Encoding.Latin1.GetString(nag)))
        {
            var key = SoundBank.Key(e.Sound);
            if (key.Length == 0 || !held.Add(key)) continue;                      // loaded, or asked for already
            Lips(r, r.First(Search(key + ".wav", SoundBank.EffectFolders), out _)          // not a stream
                 ?? r.First(Search(key + ".mp3", SoundBank.StreamFolders), out _));
        }
    }

    /// <summary>A conversation step's voice, looked up as a stream (with the stream flag), and its lip track.</summary>
    static void Voice(PrefetchReader r, string id)
    {
        var key = SoundBank.Key(id);
        if (key.Length == 0) return;
        Lips(r, r.First(Search(key + ".mp3", SoundBank.StreamFolders), out _)
             ?? r.First(Search(key + ".wav", SoundBank.EffectFolders), out _));
    }

    /// <summary>A resource kind's search: each folder joined to the name, then the name alone.</summary>
    static IEnumerable<string> Search(string file, string[] folders) =>
        folders.Select(d => d + "/" + file).Append(file);

    /// <summary>The lip track read after every load: the file's own path with <c>.nlp</c> for its extension.</summary>
    static void Lips(PrefetchReader r, string? path)
    {
        if (path is null) return;
        r.Fetch(SoundBank.LipsPath(path), out _);
    }

    /// <summary>Logs, on the game's thread, what the jobs finished since the last call read ahead.</summary>
    public void Report()
    {
        foreach (var done in Worker.Drain())
            log.LogInformation("read ahead for {What}: {Files} files, {Kb:0} KB in {Ms:0} ms; the cache holds {Mb:0.0} MB{Failed}",
                done.What, done.Files, done.Bytes / 1024.0, done.Ms, done.CacheBytes / 1048576.0,
                done.Failed is null ? "" : $" (stopped: {done.Failed})");
    }
}
