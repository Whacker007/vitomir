// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Engine.Audio;

// **Ours: the sound of an area of a cell level**. In 1.1 a room
// like the smithy is a level of its own, and its music, ambient loop and room type start when the level's
// scene does (the music's level then its play, the ambient's
// play then its level, then the room type) — after the level change has stopped every
// channel. In a cell level the room is part of the map and there is no level change, so the same two
// streams are changed over a short crossfade instead. Nothing here runs for a 1.1 level.
public sealed partial class GameAudio
{
    /// <summary>How long an area's music and ambient take to change over, seconds (ours).</summary>
    public const float AreaCrossfade = 1.5f;

    /// <summary>
    /// The room type last asked for (set when it is below 26 and there is a
    /// 3D provider). 1.1 hands it to Miles (<c>AIL_set_3D_room_type</c>) and its "Fast 2D" provider ignores
    /// it, so nothing is heard of it there or here; it is kept so that an area's is known.
    /// </summary>
    public uint RoomType { get; set; }

    /// <summary>Room types the mixer accepts: below this (26).</summary>
    public const uint RoomTypes = 0x1a;

    /// <summary>The id of no channel (<c>musicChannel</c> and <c>ambientChannel</c> start as it).</summary>
    const int None = 0xff;

    Channel? fadingMusic, fadingAmbient;
    Sound? fadingMusicSound, fadingAmbientSound;
    float fadingMusicFrom, fadingAmbientFrom, fadeFor, fadeAt = -1f;

    /// <summary>Whether a crossfade is under way.</summary>
    public bool Crossfading => fadeAt >= 0f;

    /// <summary>
    /// Changes the music and the ambient over <paramref name="seconds"/> (ours). A track asked for that is
    /// already playing goes on playing and only takes the new level; a null one leaves what is playing
    /// alone — 1.1's rule for a scene that names none (the last level's music goes on) —
    /// unless <paramref name="stopMissing"/>, which fades it out: the way back to an outside that had none. The
    /// new track starts as a scene's does (the level set, then the play; the ambient played at the old
    /// level and then given its own, the level start's order) and rises from silence while the old one falls.
    /// </summary>
    public void Crossfade(Sound? music, float musicLevel, Sound? ambient, float ambientLevel, float seconds = AreaCrossfade,
                          bool stopMissing = false)
    {
        EndCrossfade();
        bool any = false;
        // Going back to an outside that had no track: the room's falls silent and is not replaced.
        if (stopMissing && music is null && MusicChannel is { } lastMusic)
        {
            (fadingMusic, fadingMusicFrom, fadingMusicSound) = (lastMusic, lastMusic.Volume, lastMusic.Sound);
            musicChannel = None;
            Music = null;
            MusicTo(musicLevel);
            any = true;
        }
        if (stopMissing && ambient is null && AmbientChannel is { } lastAmbient)
        {
            (fadingAmbient, fadingAmbientFrom, fadingAmbientSound) = (lastAmbient, lastAmbient.Volume, lastAmbient.Sound);
            ambientChannel = None;
            Ambient = null;
            AmbientTo(ambientLevel);
            any = true;
        }
        if (music is not null)
        {
            if (Same(Music, music) && MusicChannel is not null) MusicTo(musicLevel);
            else
            {
                fadingMusic = MusicChannel;
                fadingMusicFrom = fadingMusic?.Volume ?? 0f;
                fadingMusicSound = fadingMusic?.Sound;
                musicChannel = None;                                // the old track is the fade's now, not StartMusic's to stop
                Music = null;
                MusicTo(musicLevel);
                StartMusic(music);
                any = true;
            }
        }
        if (ambient is not null)
        {
            if (Same(Ambient, ambient) && AmbientChannel is not null) AmbientTo(ambientLevel);
            else
            {
                fadingAmbient = AmbientChannel;
                fadingAmbientFrom = fadingAmbient?.Volume ?? 0f;
                fadingAmbientSound = fadingAmbient?.Sound;
                ambientChannel = None;
                Ambient = null;
                StartAmbient(ambient);
                AmbientTo(ambientLevel);
                any = true;
            }
        }
        if (!any) return;
        fadeFor = MathF.Max(seconds, 0f);
        fadeAt = 0f;
        StepCrossfade(0f);
    }

    /// <summary>Moves a crossfade on by <paramref name="seconds"/>; nothing when none is under way.</summary>
    public void StepCrossfade(float seconds)
    {
        if (fadeAt < 0f) return;
        fadeAt += seconds;
        float k = fadeFor <= 0f ? 1f : Math.Clamp(fadeAt / fadeFor, 0f, 1f);
        if (k >= 1f) { EndCrossfade(); return; }
        if (Fading(fadingMusic, fadingMusicSound) is { } oldMusic) { oldMusic.Volume = fadingMusicFrom * (1f - k); oldMusic.Flags |= ChannelFlag.DirtyVolume; }
        if (Fading(fadingAmbient, fadingAmbientSound) is { } oldAmbient) { oldAmbient.Volume = fadingAmbientFrom * (1f - k); oldAmbient.Flags |= ChannelFlag.DirtyVolume; }
        // The new ones: what they would be at now (the duck and the silence included), scaled by the fade.
        if (fadingMusic is not null && MusicChannel is { } m) { ApplyMusic(); m.Volume *= k; m.Flags |= ChannelFlag.DirtyVolume; }
        if (fadingAmbient is not null && AmbientChannel is { } a) { ApplyAmbient(); a.Volume *= k; a.Flags |= ChannelFlag.DirtyVolume; }
    }

    /// <summary>Ends a crossfade at once: the old tracks stopped, the new ones at their levels.</summary>
    public void EndCrossfade()
    {
        if (Fading(fadingMusic, fadingMusicSound) is { } oldMusic && !ReferenceEquals(oldMusic, MusicChannel)) Mixer.StopChannel(oldMusic);
        if (Fading(fadingAmbient, fadingAmbientSound) is { } oldAmbient && !ReferenceEquals(oldAmbient, AmbientChannel)) Mixer.StopChannel(oldAmbient);
        bool was = fadeAt >= 0f;
        AbandonCrossfade();
        if (!was) return;
        ApplyMusic();
        ApplyAmbient();
    }

    /// <summary>
    /// Forgets a crossfade without touching a channel — for a level change, which has stopped every channel
    /// and started the new place's own (<see cref="Enter"/>).
    /// </summary>
    public void AbandonCrossfade()
    {
        fadingMusic = fadingAmbient = null;
        fadingMusicSound = fadingAmbientSound = null;
        fadeAt = -1f;
    }

    /// <summary>A channel handed to the fade, while it still plays the sound it had then (a slot is used again once stopped).</summary>
    static Channel? Fading(Channel? c, Sound? sound) => c is { Active: true } && ReferenceEquals(c.Sound, sound) ? c : null;

    /// <summary>A scene's percentage as a level, the way the music and ambient levels read one.</summary>
    public static float LevelOf(int percent) => Unsigned(percent);

    void MusicTo(float level) { MusicLevel = Math.Clamp(level, 0f, 1f); ApplyMusic(); }      // held to 0..1
    void AmbientTo(float level) { AmbientLevel = level; ApplyAmbient(); }                    // Not held

    static bool Same(Sound? a, Sound? b) => a is not null && b is not null && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
}
