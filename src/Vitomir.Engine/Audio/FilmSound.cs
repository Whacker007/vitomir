// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A film's sound on the mixer, and the one place its level is decided.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Audio;

/// <summary>
/// **A film's sound** — a story film's (<see cref="Video.StoryFilm"/>) and a material film's
/// (<see cref="Video.MaterialFilms"/>) — started on the mixer, and **the one place its level is decided**, so a later
/// sound category for films (<i>"I want to eventually have some way of differentiating between
/// speech, sounds, music, cutscenes, and in game videos"</i>) takes over here and nowhere else.
/// <para>
/// In 1.1 a film's sound is Bink's own: Bink plays the film's track on a Miles sample of its own, set before every
/// story film (<c>BinkSetSoundSystem(BinkOpenMiles, …)</c>) and opened with sound for a material film,
/// and **no slider reaches it** — the sliders scale the mixer's channels one by one, and the driver's master
/// volume, which would, is set once to full at start (1.0; its setter has
/// no caller). Vitomir plays the track as a streaming mixer channel, flat and centred, at level 1 (ours: Bink's sample
/// is not the mixer's). With the QoL <c>filmvolume</c> on (<see cref="Qol.FilmVolume"/>, the default) the channel is
/// not flagged absolute, so the Options' <b>Sound</b> slider (<c>opsoSoundScroll</c>, the
/// mixer's effects volume) scales it; off, it is flagged <see cref="ChannelFlag.Absolute"/>, which
/// the effects volume does not touch (<see cref="Miles.Volume"/>: music and the ambient are played so), and it plays at 1.1's
/// full volume whatever the slider says.
/// </para>
/// </summary>
public static class FilmSound
{
    /// <summary>The level a film's track is played at: full, as Bink's sample (ours: a mixer channel's level).</summary>
    public const float Level = 1f;

    /// <summary>Where it sits: the middle, as Bink's flat sample.</summary>
    public const float Pan = 0.5f;

    /// <summary>
    /// The channel flags a film's track is played with: none with <c>filmvolume</c> on (the Sound slider scales it),
    /// <see cref="ChannelFlag.Absolute"/> with it off (1.1's full volume). <paramref name="qol"/> null reads
    /// <see cref="Qol.Current"/>.
    /// </summary>
    public static uint Flags(Qol? qol = null) => (qol ?? Qol.Current).FilmVolume ? 0u : ChannelFlag.Absolute;

    /// <summary>
    /// The film's track started on <paramref name="mixer"/>: its channel's id, or 0 with no track or no channel free.
    /// </summary>
    public static int Play(Mixer mixer, Sound? track, Qol? qol = null) =>
        track is null ? 0 : mixer.Play2D(track, Level, Pan, Flags(qol));
}
