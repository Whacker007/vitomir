// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Loading a recording by name — as the original's loader and the three under it do.
using Microsoft.Extensions.Logging;
using NLayer;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.Audio;

/// <summary>
/// Every recording the game has asked for, by name.
///
/// A name is looked up with its folder and its extension **thrown away** and in lower
/// case, so <c>Prague_Petrin.wav</c> and <c>prague_petrin.MP3</c> are the same sound — which is why a
/// scene that names its music <c>.wav</c> still gets the <c>.mp3</c> (MON2 and MON3 do). What
/// happens on a miss depends on whether the caller asked for a stream:
///
/// <list type="bullet">
/// <item><b>not a stream</b>: <c>NAME.WAV</c> in the six effect folders (<see cref="EffectFolders"/>,
/// resource kind 0xb), and failing that <c>NAME.MP3</c> in the three stream folders, loaded whole;</item>
/// <item><b>a stream</b> (conversations, music, the ambient, and a script sound with flag 0x400):
/// <c>NAME.MP3</c> in the stream folders (kind 0x18) first, then <c>NAME.WAV</c> in the effect
/// folders.</item>
/// </list>
///
/// A hit on the cache answers the sound already loaded **whatever was asked for** — a name first
/// loaded as an effect is an effect for good. The cache is keyed on each record's own name, compared
/// without regard to case, and a **stream's** record keeps the name exactly as it was
/// asked for, extension and all (the loader copies the argument, not the key). So a stream is looked
/// up as <c>prague_petrin</c> and filed as <c>Prague_Petrin.mp3</c>, and is never found again: every
/// request for a stream makes a new record. That is also why <c>GetMusicName</c> answers the name
/// with its extension.
///
/// The original also keeps an 8,000,000-byte budget for samples in memory
/// and evicts the least recently played to stay inside it. The eviction never touches a sound that
/// is playing, and nothing the game ships is anywhere near that size, so it has no audible effect;
/// here everything loaded simply stays.
/// </summary>
public sealed class SoundBank(GameFiles files)
{
    readonly ILogger log = Diag.For<SoundBank>();
    readonly Dictionary<string, Sound> byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resource kind 0xb, registered in this order at start-up.</summary>
    public static readonly string[] EffectFolders =
        ["sounds/objects", "sounds/actors", "sounds/voice", "sounds/environment", "sounds/gameplay", "misc/foley"];

    /// <summary>Resource kind 0x18.</summary>
    public static readonly string[] StreamFolders = ["sounds/dialog", "sounds/stereoambient", "sounds/music"];

    /// <summary>Everything loaded so far.</summary>
    public IReadOnlyCollection<Sound> Loaded => byName.Values;

    /// <summary>
    /// The cache key: at most 31 characters, lower case, then the folder and the
    /// extension stripped (working from the last backslash and the last dot).
    /// </summary>
    public static string Key(string name)
    {
        var s = name.Length > 31 ? name[..31] : name;
        s = s.ToLowerInvariant();
        int slash = s.LastIndexOf('\\');
        if (slash >= 0) s = s[(slash + 1)..];
        int dot = s.LastIndexOf('.');
        if (dot >= 0) s = s[..dot];
        return s;
    }

    /// <summary>
    /// `gameSound::Load`. A hit adds an owner; a miss loads it, or answers null when no
    /// folder has it — which is what 1.1 answers too, and why a missing file is silence rather
    /// than an error everywhere in the engine.
    /// </summary>
    public Sound? Load(string name, bool stream = false)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var key = Key(name);
        if (key.Length == 0) return null;
        if (byName.TryGetValue(key, out var hit)) { hit.LoadCount++; return hit; }

        Sound? made = stream
            ? OpenStream(key, name, ".mp3", StreamFolders) ?? OpenStream(key, name, ".wav", EffectFolders)
            : LoadWav(key) ?? LoadMp3Whole(key);
        if (made is null) return null;
        Lips(made);
        byName.TryAdd(made.Name, made);
        return made;
    }

    /// <summary>
    /// The lip-sync track read after every load: the file it came from with
    /// its extension made `.NLP`, opened in place, and the flag (bit 4) set when it has frames.
    /// </summary>
    void Lips(Sound sound)
    {
        sound.Flags &= ~SoundFlags.LipSync;
        if (!files.TryRead(LipsPath(sound.Path), out var data)) return;
        sound.Lips = Formats.Nlp.Read(data);
        if (sound.Lips.Length > 0) sound.Flags |= SoundFlags.LipSync;
    }

    /// <summary>Where a recording's lip track is: its path with the extension made <c>.nlp</c>.</summary>
    public static string LipsPath(string path)
    {
        int dot = path.LastIndexOf('.');
        int slash = path.LastIndexOfAny(['/', '\\']);
        return (dot > slash ? path[..dot] : path) + ".nlp";
    }

    /// <summary>
    /// `gameSound::Release`: one owner fewer. A stream goes at its last owner; an effect
    /// only when <paramref name="free"/> says so — the mixer's "release when done" (0x200) does.
    /// </summary>
    public void Release(Sound? sound, bool free = false)
    {
        if (sound is null) return;
        sound.LoadCount--;
        if (sound.LoadCount < 1 && (sound.Stream || free) && byName.TryGetValue(sound.Name, out var filed)
            && ReferenceEquals(filed, sound))
            byName.Remove(sound.Name);
    }

    /// <summary>
    /// The engine's open for a resource kind: each registered folder joined to the name
    /// in turn, then the name on its own.
    /// </summary>
    bool Find(string file, string[] folders, out string path, out byte[] data)
    {
        foreach (var dir in folders)
            if (files.TryRead(dir + "/" + file, out data)) { path = dir + "/" + file; return true; }
        path = file;
        return files.TryRead(file, out data);
    }

    Sound? LoadWav(string key)
    {
        if (!Find(key + ".wav", EffectFolders, out var path, out var data)) return null;
        var info = Wav.Read(data);
        if (info is null) { log.LogDebug("{Path} is not a WAVE file", path); return null; }
        return new Sound
        {
            Name = key, Path = path, Rate = info.Rate, Bits = info.Bits,
            Flags = SoundFlags.Loaded | (info.Stereo ? SoundFlags.Stereo : 0),
            LengthMs = info.LengthMs, Pcm = Wav.Pcm16(data, info), Channels = info.Channels, SourceRate = info.Rate,
        };
    }

    /// <summary>
    /// An `.mp3` asked for as an effect: loaded whole and flagged compressed, which keeps
    /// it off the 3D channels. The engine takes the rate from the stream's header and never sets a
    /// length, so <see cref="Sound.LengthMs"/> is zero.
    /// </summary>
    Sound? LoadMp3Whole(string key)
    {
        if (!Find(key + ".mp3", StreamFolders, out var path, out var data)) return null;
        try
        {
            using var mp3 = new MpegFile(new MemoryStream(data));
            var pcm = Decode(mp3);
            return new Sound
            {
                Name = key, Path = path, Rate = mp3.SampleRate, Bits = 16,
                Flags = SoundFlags.Loaded | SoundFlags.Compressed | (mp3.Channels == 2 ? SoundFlags.Stereo : 0),
                LengthMs = 0, Pcm = pcm, Channels = mp3.Channels, SourceRate = mp3.SampleRate,
            };
        }
        catch (Exception e) { log.LogDebug("{Path}: {Error}", path, e.Message); return null; }
    }

    /// <summary>
    /// A stream: found and measured now, opened again every time it plays. Its length is
    /// the stream's own clock (<c>AIL_stream_ms_position</c>), which for an `.mp3` is the frames'.
    /// </summary>
    Sound? OpenStream(string key, string asked, string extension, string[] folders)
    {
        if (!Find(key + extension, folders, out var path, out var data)) return null;
        string name = asked.Length > 31 ? asked[..31] : asked;
        if (extension == ".wav")
        {
            var info = Wav.Read(data);
            if (info is null) return null;
            var pcm = Wav.Pcm16(data, info);
            return new Sound
            {
                Name = name, Path = path, Stream = true, LengthMs = info.LengthMs, Channels = info.Channels,
                SourceRate = info.Rate, Open = () => new MemoryPcm(pcm, info.Rate, info.Channels),
            };
        }
        try
        {
            int ms, rate, channels;
            using (var probe = new MpegFile(new MemoryStream(data)))
            {
                ms = (int)probe.Duration.TotalMilliseconds;
                rate = probe.SampleRate;
                channels = probe.Channels;
            }
            return new Sound
            {
                Name = name, Path = path, Stream = true, LengthMs = ms, Channels = channels, SourceRate = rate,
                Open = () => new Mp3Pcm(data),
            };
        }
        catch (Exception e) { log.LogDebug("{Path}: {Error}", path, e.Message); return null; }
    }

    static short[] Decode(MpegFile mp3)
    {
        var all = new List<short>();
        var buf = new float[8192];
        int n;
        while ((n = mp3.ReadSamples(buf, 0, buf.Length)) > 0)
            for (int i = 0; i < n; i++) all.Add(ToShort(buf[i]));
        return [.. all];
    }

    internal static short ToShort(float f) => (short)Math.Clamp((int)MathF.Round(f * 32767f), short.MinValue, short.MaxValue);

    /// <summary>An `.mp3` decoded as it plays.</summary>
    sealed class Mp3Pcm : IPcmSource
    {
        readonly byte[] data;
        MpegFile file;
        float[] scratch = [];
        public Mp3Pcm(byte[] data) { this.data = data; file = new MpegFile(new MemoryStream(data)); }
        public int Rate => file.SampleRate;
        public int Channels => file.Channels;
        public int Read(Span<short> into)
        {
            if (scratch.Length < into.Length) scratch = new float[into.Length];
            int n = file.ReadSamples(scratch, 0, into.Length);
            for (int i = 0; i < n; i++) into[i] = ToShort(scratch[i]);
            return n;
        }
        public void Rewind() { file.Dispose(); file = new MpegFile(new MemoryStream(data)); }
        public void Dispose() => file.Dispose();
    }
}
