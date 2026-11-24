// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// 28 channels, what gets one, what it sounds like, and when it stops.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Audio;

/// <summary>Which of the three pools a channel is in (`channel+0`).</summary>
public enum ChannelKind { None = 0, Flat = 1, Positional = 2, Stream = 3 }

/// <summary>A channel's flags. The low byte is what a caller asks for.</summary>
public static class ChannelFlag
{
    public const uint HiPriority = 0x1, LinkToPos = 0x2, LinkToThing = 0x4, Looping = 0x8, Local = 0x10,
                      True3D = 0x20, Fading = 0x40, PanSlide = 0x80, Voice = 0x100, ReleaseAtEnd = 0x200,
                      /// <summary>Not a channel flag at all: a script's way of asking for a stream.</summary>
                      AsStream = 0x400,
                      Absolute = 0x800, Active = 0x1000, HasSound = 0x2000, DirtyPosition = 0x4000,
                      DirtyPan = 0x8000, DirtyVolume = 0x10000, DirtyRate = 0x20000;
}

/// <summary>One of the 28 (a 0x54-byte record in the mixer).</summary>
public sealed class Channel
{
    public ChannelKind Kind { get; internal set; }
    /// <summary>Its slot, 0..27.</summary>
    public int Slot { get; internal set; }
    public Sound? Sound { get; internal set; }
    /// <summary>`slot | serial &lt;&lt; 8`, or 0xff when free.</summary>
    public int Id { get; internal set; } = 0xff;
    public uint Flags { get; internal set; }
    /// <summary>The volume now, the most it may be, and how fast it fades (per second).</summary>
    public float Volume { get; internal set; } = 0.5f;
    /// <inheritdoc cref="Volume"/>
    public float Target { get; internal set; } = 0.5f;
    /// <inheritdoc cref="Volume"/>
    public float FadeRate { get; internal set; }
    /// <summary>The pan and its slide.</summary>
    public float Pan { get; internal set; } = 0.5f;
    /// <inheritdoc cref="Pan"/>
    public float PanTarget { get; internal set; } = 0.5f;
    /// <inheritdoc cref="Pan"/>
    public float PanRate { get; internal set; }
    /// <summary>Far and near (defaults 5000 and 200).</summary>
    public float MaxDist { get; internal set; } = 5000f;
    /// <inheritdoc cref="MaxDist"/>
    public float MinDist { get; internal set; } = 200f;
    /// <summary>The playback rate (22,050 until told otherwise).</summary>
    public int Rate { get; internal set; } = Miles.OutputRate;
    /// <summary>Where it is. For a thing's sound, where the thing was when it last moved two units.</summary>
    public Vector3 Position { get; internal set; }
    /// <summary>The thing it follows.</summary>
    public WorldThing? Thing { get; internal set; }
    /// <summary>When it started, which is what "oldest" means.</summary>
    public int StartedAt { get; internal set; }

    public bool Active => (Flags & ChannelFlag.Active) != 0;

    // --- what Miles has been told, which only changes when the game says so ----------------------
    /// <summary>The 0..127 volume and pan Miles was last handed, and the rate it plays at.</summary>
    public int MilesVolume { get; internal set; }
    /// <inheritdoc cref="MilesVolume"/>
    public int MilesPan { get; internal set; }
    /// <inheritdoc cref="MilesVolume"/>
    public int MilesRate { get; internal set; }
    /// <summary>Where Miles thinks a positional channel is.</summary>
    public Vector3 MilesPosition { get; internal set; }
    /// <summary>Miles has played it to the end; the next frame frees the channel.</summary>
    public bool Done { get; internal set; }
    /// <summary>The Fast 2D provider has paused it: the listener is beyond its far distance.</summary>
    public bool Paused { get; internal set; }
    internal double Cursor;
    internal IPcmSource? Source;
    internal short[] StreamBuffer = [];
    internal int StreamFill, StreamRead;
    internal bool StreamEnded;

    public override string ToString() =>
        Active ? $"#{Slot} {Kind} {Sound?.Name} v{Volume:0.##}/{Target:0.##} p{Pan:0.##} f0x{Flags:x}" : $"#{Slot} free";

    /// <summary>Back to the constructor's values — what a stop leaves behind, bar the position.</summary>
    internal void Clear()
    {
        Sound = null; Id = 0xff; Flags = 0; Rate = Miles.OutputRate; Thing = null;
        Done = Paused = false; Cursor = 0;
        Source?.Dispose(); Source = null; StreamFill = StreamRead = 0; StreamEnded = false;
    }
}

/// <summary>
/// The mixer: **4 flat channels, 16 positional ones and 8 streams**, each a
/// record the game drives and Miles plays. Everything the game does to a sound goes through here,
/// and the rules that decide what a player hears are the mixer's, not the callers':
///
/// <list type="bullet">
/// <item>a recording already playing <b>three times</b> loses its oldest play before a fourth
/// starts;</item>
/// <item>a full pool refuses a new sound — unless the new one is <c>hipriority</c>, which takes the
/// first channel that is not;</item>
/// <item>a positional sound is <b>refused outright</b> when the listener is beyond its far distance;
/// </item>
/// <item>a stereo recording, or an `.mp3` loaded whole, never goes on a positional channel: it
/// plays flat, and its distance becomes a straight line from full volume at the listener to
/// nothing at <c>far − near</c>.</item>
/// </list>
///
/// <see cref="Update"/> is the per-frame half; <see cref="Render"/>
/// is what Miles would be doing between frames, and is the only part here that does not follow
/// the original game or the Miles DLLs: the resampling to 22,050 Hz is a plain linear interpolation.
/// <para>It is the Classic module's sound source (<see cref="Vitomir.Platform.Audio.IAudioSource"/>): the platform's
/// <see cref="Vitomir.Platform.Audio.AudioOutput"/> pulls it once a frame at 22,050 Hz stereo, on the frame's thread.</para>
/// </summary>
public sealed class Mixer : Vitomir.Platform.Audio.IAudioSource
{
    /// <summary>What it makes: 22,050 Hz, 16-bit stereo, the wave-out format 1.1 opens through Miles.</summary>
    public static readonly Vitomir.Platform.Audio.AudioFormat Output = new(Miles.OutputRate, 2);

    /// <inheritdoc cref="Output"/>
    public Vitomir.Platform.Audio.AudioFormat Format => Output;

    public const int FlatCount = 4, PositionalCount = 16, StreamCount = 8, Count = FlatCount + PositionalCount + StreamCount;

    /// <summary>All 28, flat first, then positional, then streams.</summary>
    public Channel[] Channels { get; } = new Channel[Count];

    /// <summary>`[mixer] effectsVolume`, 0.75 unless the ini says otherwise.</summary>
    public float EffectsVolume { get; set; } = 0.75f;

    /// <summary>The listener: where the game hears from, and which way it faces.</summary>
    public Vector3 ListenerPosition { get; set; }
    /// <inheritdoc cref="ListenerPosition"/>
    public Vector3 ListenerForward { get; set; } = Vector3.UnitY;
    /// <inheritdoc cref="ListenerPosition"/>
    public Vector3 ListenerRight { get; set; } = Vector3.UnitX;

    /// <summary>The clock a channel's start time is stamped from.</summary>
    public int Now { get; set; }
    /// <summary>The game's millisecond clock, which a speaker's mouth is timed on.</summary>
    public int GameMs { get; set; }

    /// <summary>Where a "release when done" sound is released to.</summary>
    public SoundBank? Bank { get; set; }

    int flatActive, positionalActive, streamActive, serial;

    public Mixer()
    {
        for (int i = 0; i < Count; i++)
            Channels[i] = new Channel
            {
                Slot = i,
                Kind = i < FlatCount ? ChannelKind.Flat : i < FlatCount + PositionalCount ? ChannelKind.Positional : ChannelKind.Stream,
            };
    }

    /// <summary>The channel an id names, while it is still that id's.</summary>
    public Channel? Get(int id)
    {
        int slot = id & 0xff;
        return slot < Count && Channels[slot].Id == id ? Channels[slot] : null;
    }

    /// <summary>How many channels of each pool are playing.</summary>
    public (int Flat, int Positional, int Stream) ActiveCounts => (flatActive, positionalActive, streamActive);

    IEnumerable<Channel> Pool(ChannelKind kind) => Channels.Where(c => c.Kind == kind);

    /// <summary>
    /// A free channel of a pool, or — when the pool is full and the new sound is
    /// <c>hipriority</c> — the first channel that is not, stopped.
    /// </summary>
    Channel? Take(ChannelKind kind, bool hiPriority)
    {
        int active = kind switch { ChannelKind.Flat => flatActive, ChannelKind.Positional => positionalActive, _ => streamActive };
        int total = kind switch { ChannelKind.Flat => FlatCount, ChannelKind.Positional => PositionalCount, _ => StreamCount };
        if (active < total) return Pool(kind).FirstOrDefault(c => !c.Active);
        if (!hiPriority) return null;
        foreach (var c in Pool(kind))
            if ((c.Flags & ChannelFlag.HiPriority) == 0) { StopChannel(c); return c; }
        return null;
    }

    /// <summary>A fourth play of a recording stops the oldest of the three already going.</summary>
    void MakeRoomFor(Sound sound)
    {
        if (sound.PlayCount != 3) return;
        Channel? oldest = null;
        foreach (var c in Channels)
            if (c.Active && ReferenceEquals(c.Sound, sound) && (oldest is null || (uint)c.StartedAt < (uint)oldest.StartedAt))
                oldest = c;
        if (oldest is not null) StopChannel(oldest);
    }

    /// <summary>The volume and its ceiling as every play sets them: both end up the clamped request.</summary>
    static void SetLevel(Channel c, float volume)
    {
        if (volume != c.Target)
        {
            c.Target = Math.Clamp(volume, 0f, 1f);
            c.Volume = c.Volume < 0f ? 0f : Math.Min(c.Volume, c.Target);
            c.Flags |= ChannelFlag.DirtyVolume;
        }
        if (volume != c.Volume)
        {
            c.Volume = volume < 0f ? 0f : Math.Min(volume, c.Target);
            c.Flags |= ChannelFlag.DirtyVolume;
        }
    }

    void SetRate(Channel c, Sound sound, int rate)
    {
        if (rate == -1) rate = sound.Rate > 0 ? sound.Rate : Miles.OutputRate;
        if (c.Rate != rate) { c.Rate = rate; c.Flags |= ChannelFlag.DirtyRate; }
    }

    /// <summary>
    /// `Mixer::Play` — flat, or a stream for a stream. Answers the channel's id, or 0 when
    /// nothing could be had. <paramref name="rate"/> −1 is "the recording's own".
    /// </summary>
    public int Play2D(Sound? sound, float volume, float pan, uint flags, int rate = -1)
    {
        if (sound is null) return 0;
        MakeRoomFor(sound);
        var c = Take(sound.Stream ? ChannelKind.Stream : ChannelKind.Flat, (flags & ChannelFlag.HiPriority) != 0);
        if (c is null) return 0;
        SetLevel(c, volume);
        if (pan != c.Pan) { c.Pan = Math.Clamp(pan, 0f, 1f); c.Flags |= ChannelFlag.DirtyPan; }
        SetRate(c, sound, rate);
        c.Flags = flags;
        c.Sound = sound;
        c.Flags = flags | ChannelFlag.HasSound;
        return Start(c, sound);
    }

    /// <summary>
    /// `Mixer::PlayAtThing`. Refused when the listener is further than
    /// <paramref name="max"/> from the thing. A voice line on a character is also refused while
    /// that character's mouth is still busy with the last one.
    /// </summary>
    public int PlayAtThing(Sound? sound, WorldThing thing, float max, float min, float volume, uint flags, int rate = -1)
    {
        if (sound is null) return 0;
        MakeRoomFor(sound);
        if (Vector3.Distance(ListenerPosition, thing.Position) > max) return 0;
        bool flat = (sound.Flags & (SoundFlags.Stereo | SoundFlags.Compressed)) != 0;
        var c = Take(flat ? ChannelKind.Flat : ChannelKind.Positional, (flags & ChannelFlag.HiPriority) != 0);
        if (c is null) return 0;
        c.StartedAt = Now;
        c.Flags = flags | ChannelFlag.LinkToThing;
        if (sound.Stream && c.Kind != ChannelKind.Stream) return 0;   // a stream has nowhere to go here
        c.Sound = sound;
        c.Thing = thing;
        c.Flags = flags | ChannelFlag.HasSound | ChannelFlag.LinkToThing;
        thing.Flags |= WorldThing.Flag.SoundLink;
        SetLevel(c, volume);
        c.MinDist = min;
        c.MaxDist = max;
        if (Vector3.Distance(thing.Position, c.Position) >= 2f) { c.Position = thing.Position; c.Flags |= ChannelFlag.DirtyPosition; }
        SetRate(c, sound, rate);
        if (thing.Type is ThingType.Actor or ThingType.Player && (flags & ChannelFlag.Voice) != 0)
        {
            if ((uint)GameMs < (uint)thing.MouthBusyUntil) return 0;
            // The voice — handed the channel's id *before* this play has given it one, so what the
            // character remembers is the free marker, 0xff, and nothing can stop it by that id later.
            thing.MouthBusyUntil = GameMs + sound.LengthMs;
            thing.MouthChannel = c.Id;
        }
        return Start(c, sound);
    }

    /// <summary>`Mixer::PlayAtPoint`. Only a stereo recording is kept off the positional pool here.</summary>
    public int PlayAtPoint(Sound? sound, Vector3 position, float max, float min, float volume, uint flags, int rate = -1)
    {
        if (sound is null) return 0;
        MakeRoomFor(sound);
        if (Vector3.Distance(ListenerPosition, position) > max) return 0;
        bool flat = (sound.Flags & SoundFlags.Stereo) != 0;
        var c = Take(flat ? ChannelKind.Flat : ChannelKind.Positional, (flags & ChannelFlag.HiPriority) != 0);
        if (c is null) return 0;
        c.Flags = flags | ChannelFlag.LinkToPos;
        c.StartedAt = Now;
        if (sound.Stream && c.Kind != ChannelKind.Stream) return 0;
        c.Sound = sound;
        c.Thing = null;
        c.Flags = flags | ChannelFlag.HasSound | ChannelFlag.LinkToPos;
        SetLevel(c, volume);
        c.MinDist = min;
        c.MaxDist = max;
        if (Vector3.Distance(position, c.Position) >= 2f) { c.Position = position; c.Flags |= ChannelFlag.DirtyPosition; }
        SetRate(c, sound, rate);
        return Start(c, sound);
    }

    /// <summary>Hand the channel to Miles and give it its id.</summary>
    int Start(Channel c, Sound sound)
    {
        c.Done = c.Paused = false;
        c.Cursor = 0;
        switch (c.Kind)
        {
            case ChannelKind.Flat:
                if (sound.Pcm is null) return 0;
                if ((c.Flags & (ChannelFlag.LinkToThing | ChannelFlag.LinkToPos)) != 0)
                {
                    var at = (c.Flags & ChannelFlag.LinkToThing) != 0 && c.Thing is { } t ? t.Position : c.Position;
                    float v = Linear(c, at);
                    if (v != c.Volume) { c.Volume = Math.Clamp(v, 0f, c.Target); c.Flags |= ChannelFlag.DirtyVolume; }
                }
                c.MilesVolume = Miles.Volume(c.Volume, EffectsVolume, (c.Flags & ChannelFlag.Absolute) != 0);
                c.MilesPan = Miles.Pan(c.Pan);
                c.MilesRate = c.Rate;
                break;
            case ChannelKind.Positional:
                if (sound.Pcm is null) return 0;
                c.MilesPosition = c.Position;
                c.MilesVolume = Miles.Volume(c.Volume, EffectsVolume, (c.Flags & ChannelFlag.Absolute) != 0);
                c.MilesRate = c.Rate;
                break;
            case ChannelKind.Stream:
                if (sound.Open is null) return 0;
                c.Source = sound.Open();
                c.MilesVolume = Miles.Volume(c.Volume, EffectsVolume, (c.Flags & ChannelFlag.Absolute) != 0);
                c.MilesPan = Miles.Pan(c.Pan);
                c.MilesRate = c.Source.Rate;                     // a stream plays at its file's rate
                break;
            default: return 0;
        }
        c.Flags &= ~(ChannelFlag.DirtyVolume | ChannelFlag.DirtyPan | ChannelFlag.DirtyRate | ChannelFlag.DirtyPosition);
        c.StartedAt = Now;
        serial = (serial + 1) & 0xffffff;
        c.Id = c.Slot | serial << 8;
        c.Flags |= ChannelFlag.Active;
        switch (c.Kind)
        {
            case ChannelKind.Flat: flatActive++; break;
            case ChannelKind.Positional: positionalActive++; break;
            default: streamActive++; break;
        }
        sound.PlayCount++;
        return c.Id;
    }

    /// <summary>
    /// A flat channel's distance law: <c>target − target × d ÷ (far − near)</c>.
    /// Straight, with no near zone, reaching nothing at <c>far − near</c> rather than at far.
    /// </summary>
    float Linear(Channel c, Vector3 at) =>
        -c.Target * (Vector3.Distance(at, ListenerPosition) / (c.MaxDist - c.MinDist)) + c.Target;

    /// <summary>`Mixer::Stop` by id. False when the id is stale.</summary>
    public bool Stop(int id) => Get(id) is { } c && StopChannel(c);

    /// <summary>The stop itself.</summary>
    public bool StopChannel(Channel c)
    {
        if (!c.Active) return false;
        switch (c.Kind)
        {
            case ChannelKind.Flat: flatActive--; break;
            case ChannelKind.Positional: positionalActive--; break;
            default: streamActive--; break;
        }
        var sound = c.Sound;
        if (sound is not null)
        {
            sound.PlayCount--;
            if ((c.Flags & ChannelFlag.ReleaseAtEnd) != 0) Bank?.Release(sound, free: true);
        }
        c.Clear();
        return true;
    }

    /// <summary>Everything off, as a level change or the menus do.</summary>
    public void StopAll()
    {
        foreach (var c in Channels) StopChannel(c);
    }

    /// <summary>
    /// A thing is going away: its positional sounds stop following it and stay where it
    /// was. Only the positional pool is walked, so a flat sound on a thing keeps the last position
    /// it had.
    /// </summary>
    public void Forget(WorldThing thing)
    {
        foreach (var c in Pool(ChannelKind.Positional))
            if ((c.Flags & (ChannelFlag.Active | ChannelFlag.LinkToThing)) != 0 && ReferenceEquals(c.Thing, thing))
                c.Flags = (c.Flags & ~ChannelFlag.LinkToThing) | ChannelFlag.LinkToPos;
        thing.Flags &= ~WorldThing.Flag.SoundLink;
    }

    /// <summary>
    /// A channel's volume, held under its ceiling — what `SetMusicVolume`,
    /// `SetAmbientVolume` and the ducking all go through. It can lower a sound, and raise it again
    /// no further than it started.
    /// </summary>
    public static void SetVolume(Channel c, float volume)
    {
        if (volume == c.Volume) return;
        c.Volume = volume < 0f ? 0f : Math.Min(volume, c.Target);
        c.Flags |= ChannelFlag.DirtyVolume;
    }

    /// <summary>Starts a fade towards the channel's ceiling (or, with a negative rate, out) at a rate per second.</summary>
    public static void Fade(Channel c, float rate)
    {
        c.FadeRate = rate;
        c.Flags |= ChannelFlag.Fading;
    }

    /// <summary>The effects volume changed: every channel is told again.</summary>
    public void SetEffectsVolume(float volume)
    {
        EffectsVolume = volume;
        foreach (var c in Channels) c.Flags |= ChannelFlag.DirtyVolume;
    }

    /// <summary>
    /// The frame's channel pass (28 channels, after the listener has moved): finished
    /// sounds are freed, point sounds the listener has left are stopped, sounds on things follow
    /// them, fades and pan slides move on, and whatever changed is passed to Miles.
    /// </summary>
    public void Update(float seconds)
    {
        foreach (var c in Channels) UpdateChannel(c, seconds);
    }

    void UpdateChannel(Channel c, float seconds)
    {
        if (!c.Active) return;
        if (c.Done) { StopChannel(c); return; }

        if ((c.Flags & ChannelFlag.LinkToPos) != 0 && Vector3.Distance(c.Position, ListenerPosition) > c.MaxDist)
        {
            StopChannel(c);
            return;
        }
        if ((c.Flags & ChannelFlag.LinkToThing) != 0 && c.Thing is { } thing &&
            Vector3.Distance(c.Position, thing.Position) > 2f)
        {
            c.Position = thing.Position;
            c.Flags |= ChannelFlag.DirtyPosition;
        }
        if ((c.Flags & ChannelFlag.Fading) != 0)
        {
            float v = c.FadeRate * seconds + c.Volume;
            c.Volume = v;
            if (c.FadeRate < 0f && v < 0f) { StopChannel(c); return; }
            if (c.Target <= v) { c.Volume = c.Target; c.Flags &= ~ChannelFlag.Fading; }
            c.Flags |= ChannelFlag.DirtyVolume;
        }
        if ((c.Flags & ChannelFlag.PanSlide) != 0)
        {
            float p = c.PanRate * seconds + c.Pan;
            c.Pan = p;
            if (c.PanRate < 0f && p <= c.PanTarget) { c.Pan = c.PanTarget; c.Flags &= ~ChannelFlag.PanSlide; }
            else if (c.PanTarget <= p) { c.Pan = c.PanTarget; c.Flags &= ~ChannelFlag.PanSlide; }
            c.Flags |= ChannelFlag.DirtyPan;
        }

        bool absolute = (c.Flags & ChannelFlag.Absolute) != 0;
        switch (c.Kind)
        {
            case ChannelKind.Flat:
                if ((c.Flags & (ChannelFlag.DirtyPosition | ChannelFlag.LinkToPos)) != 0)
                {
                    c.Flags |= ChannelFlag.DirtyVolume;
                    c.Volume = Linear(c, c.Position);              // not clamped here; the hand-over clamps
                }
                if ((c.Flags & ChannelFlag.DirtyVolume) != 0) c.MilesVolume = Miles.Volume(c.Volume, EffectsVolume, absolute);
                if ((c.Flags & ChannelFlag.DirtyPan) != 0) c.MilesPan = Miles.Pan(c.Pan);
                if ((c.Flags & ChannelFlag.DirtyRate) != 0) c.MilesRate = c.Rate;
                c.Flags &= ~(ChannelFlag.DirtyVolume | ChannelFlag.DirtyPan | ChannelFlag.DirtyRate);
                break;
            case ChannelKind.Positional:
                if ((c.Flags & ChannelFlag.DirtyVolume) != 0) c.MilesVolume = Miles.Volume(c.Volume, EffectsVolume, absolute);
                if ((c.Flags & ChannelFlag.DirtyPosition) != 0) c.MilesPosition = c.Position;
                if ((c.Flags & ChannelFlag.DirtyRate) != 0) c.MilesRate = c.Rate;
                c.Flags &= ~(ChannelFlag.DirtyVolume | ChannelFlag.DirtyPosition | ChannelFlag.DirtyRate);
                break;
            case ChannelKind.Stream:
                if ((c.Flags & ChannelFlag.DirtyVolume) != 0) c.MilesVolume = Miles.Volume(c.Volume, EffectsVolume, absolute);
                if ((c.Flags & ChannelFlag.DirtyPan) != 0) c.MilesPan = Miles.Pan(c.Pan);
                c.Flags &= ~(ChannelFlag.DirtyVolume | ChannelFlag.DirtyPan | ChannelFlag.DirtyRate);
                break;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Miles' half: the samples.

    /// <summary>
    /// Mixes <paramref name="frames"/> stereo frames at 22,050 Hz into <paramref name="into"/>
    /// (interleaved left, right), from what Miles has been told. Nothing here changes a channel's
    /// game-side state except the play position and "finished".
    /// </summary>
    public void Render(Span<short> into, int frames)
    {
        var mix = new int[frames * 2];
        foreach (var c in Channels)
        {
            if (!c.Active || c.Done || c.Sound is null) continue;
            int volume = c.MilesVolume, pan = c.MilesPan;
            if (c.Kind == ChannelKind.Positional)
            {
                var placed = Miles.Fast2D(c.MilesVolume, c.MilesPosition, ListenerPosition, ListenerForward,
                                          ListenerRight, c.MinDist, c.MaxDist);
                c.Paused = placed.Paused;
                if (placed.Paused) continue;
                volume = placed.Volume; pan = placed.Pan;
            }
            var (left, right) = Miles.Gains(volume, pan);
            if (c.Kind == ChannelKind.Stream) RenderStream(c, mix, frames, left, right);
            else RenderSample(c, mix, frames, left, right);
        }
        for (int i = 0; i < frames * 2; i++) into[i] = (short)Math.Clamp(mix[i], short.MinValue, short.MaxValue);
    }

    void RenderSample(Channel c, int[] mix, int frames, int left, int right)
    {
        var pcm = c.Sound!.Pcm!;
        int channels = c.Sound.Channels;
        int length = pcm.Length / channels;
        if (length == 0) { c.Done = true; return; }
        double step = (double)c.MilesRate / Miles.OutputRate;
        bool loop = (c.Flags & ChannelFlag.Looping) != 0;
        double at = c.Cursor;
        for (int f = 0; f < frames; f++)
        {
            if (at >= length)
            {
                if (!loop) { c.Done = true; break; }
                at -= length;
            }
            int i0 = (int)at;
            int i1 = i0 + 1 < length ? i0 + 1 : loop ? 0 : i0;
            float t = (float)(at - i0);
            int l, r;
            if (channels == 2)
            {
                l = (int)(pcm[i0 * 2] + (pcm[i1 * 2] - pcm[i0 * 2]) * t);
                r = (int)(pcm[i0 * 2 + 1] + (pcm[i1 * 2 + 1] - pcm[i0 * 2 + 1]) * t);
            }
            else l = r = (int)(pcm[i0] + (pcm[i1] - pcm[i0]) * t);
            mix[f * 2] += l * left >> 11;
            mix[f * 2 + 1] += r * right >> 11;
            at += step;
        }
        c.Cursor = at;
    }

    void RenderStream(Channel c, int[] mix, int frames, int left, int right)
    {
        var src = c.Source;
        if (src is null) { c.Done = true; return; }
        int channels = src.Channels;
        double step = (double)c.MilesRate / Miles.OutputRate;
        bool loop = (c.Flags & ChannelFlag.Looping) != 0;
        int needed = ((int)Math.Ceiling(frames * step) + 8) * channels * 2;
        if (c.StreamBuffer.Length < needed)
        {
            var grown = new short[needed];
            c.StreamBuffer.AsSpan(0, c.StreamFill).CopyTo(grown);
            c.StreamBuffer = grown;
        }
        for (int f = 0; f < frames; f++)
        {
            while ((int)c.Cursor + 1 >= c.StreamFill / channels && !c.StreamEnded) Refill(c, src, loop);
            int i0 = (int)c.Cursor;
            if (i0 >= c.StreamFill / channels) { c.Done = true; break; }
            int avail = c.StreamFill / channels;
            int i1 = Math.Min(i0 + 1, avail - 1);
            float t = (float)(c.Cursor - i0);
            var b = c.StreamBuffer;
            int l, r;
            if (channels == 2)
            {
                l = (int)(b[i0 * 2] + (b[i1 * 2] - b[i0 * 2]) * t);
                r = (int)(b[i0 * 2 + 1] + (b[i1 * 2 + 1] - b[i0 * 2 + 1]) * t);
            }
            else l = r = (int)(b[i0] + (b[i1] - b[i0]) * t);
            mix[f * 2] += l * left >> 11;
            mix[f * 2 + 1] += r * right >> 11;
            c.Cursor += step;
        }
    }

    /// <summary>
    /// **The most a refill decodes at once**, per channel (ours): two MPEG frames of 1,152. The stream's
    /// buffer only ever grows — to what the longest frame's mix asked for — and a refill that filled all
    /// of it decoded that much at once whenever the cursor ran out. After a half-second frame that was
    /// about 1.7 s of a 44.1 kHz song in one go, some 30 ms of work, on a frame every 1.6 s for as long
    /// as the song played: the stutter seen in play.
    /// In blocks the same samples come out in the same order; the work is only spread over
    /// the frames that play them.
    /// </summary>
    public const int RefillFrames = 2304;

    /// <summary>Keeps the last frame of what was decoded and reads the next block behind it.</summary>
    static void Refill(Channel c, IPcmSource src, bool loop)
    {
        int channels = src.Channels;
        int keepFrom = Math.Max(0, (int)c.Cursor);
        int keep = Math.Max(0, c.StreamFill / channels - keepFrom);
        var b = c.StreamBuffer;
        if (keep > 0) Array.Copy(b, keepFrom * channels, b, 0, keep * channels);
        c.Cursor -= keepFrom;
        c.StreamFill = keep * channels;
        int room = Math.Min(b.Length - c.StreamFill, RefillFrames * channels);
        room -= room % channels;
        if (room <= 0) return;
        int n = src.Read(b.AsSpan(c.StreamFill, room));
        if (n <= 0)
        {
            if (loop) { src.Rewind(); n = src.Read(b.AsSpan(c.StreamFill, room)); }
            if (n <= 0) { c.StreamEnded = true; return; }
        }
        c.StreamFill += n;
    }
}
