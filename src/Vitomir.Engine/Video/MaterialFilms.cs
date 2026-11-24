// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Films played onto a material: `Codex.MaterialPlayback` and the original's four
// playback buffers. No shipped script calls it;
// only a mod would.

using Vitomir.Engine.Audio;
using Vitomir.Engine.Scene;
using Vitomir.Formats;

namespace Vitomir.Engine.Video;

/// <summary>
/// A film whose pictures can be read: what a material film needs beyond <see cref="IFilm"/>.
/// <see cref="VideoClip"/> is one; the tests' fakes are the others.
/// </summary>
public interface IFilmPicture : IFilm
{
    /// <summary>The frame now in <see cref="Pixels"/>, or −1 before the first one is read.</summary>
    int FrameIndex { get; }

    /// <summary>The current frame, RGBA, top row first, <see cref="FilmInfo.Width"/> × <see cref="FilmInfo.Height"/>.</summary>
    ReadOnlySpan<byte> Pixels { get; }
}

public sealed partial class VideoClip : IFilmPicture;

/// <summary>
/// 1.1's material films: up to four at once (six words a slot), each decoded
/// frame by frame into the picture of the texture its material was showing when the script asked.
///
/// <para><b>Starting one</b>. The first free slot takes it; with all four busy the call
/// does nothing (it answers −1, which the native throws away). The film is opened by the name the
/// script gave, `BinkOpen(name, bPreload ? BINKPRELOADALL (0x2000) : 0)`, and a film that will not open
/// takes no slot. The texture is the material's <i>current</i> one (the native passes
/// the material's showing frame, which MaterialSetFrame points), so a page-flipped
/// material carries the film on one of its frames only.</para>
///
/// <para><b>Each frame</b> (run by 1.1's frame every frame, after the mixer and
/// before the world tick). For each slot in use whose film has a frame due (<c>BinkWait</c> answering
/// 0): the frame is decoded and copied into the texture's picture (<c>BinkCopyToBuffer</c> at 0, 0,
/// the texture's own width as the pitch and its height as the limit); with <c>bHWUpdate</c> the card's
/// copy is refreshed from it (one is made if there is none yet, else the renderer
/// reloads its surfaces); then a film on its last frame without <c>bLoop</c> is closed, the slot
/// cleared — the last frame stays in the texture — and any other goes on to its next frame, which
/// after the last is the first again. (A third flag holds a film on its frame; its only
/// setter is called by nothing.)</para>
///
/// <para><b>Upside down.</b> 1.1 keeps a texture's rows bottom row first (<see cref="TextureLayout"/>),
/// and Bink copies the film's top row to the buffer's first row, so a film shows upside down against
/// the texture's own picture, and one smaller than the texture fills the rows of its <i>bottom</i> edge.
/// Vitomir does the same. (That the card's copy keeps the memory's row order is rlDirect's reload,
/// a renderer call whose detail is not known.)</para>
///
/// <para><b>Ours</b>: a film is played by elapsed time through <see cref="IFilm.Advance"/>, as the
/// story films are, and ends when FFmpeg has no more to give — a frame after 1.1's at most; a loop
/// reopens the film. 1.1 copies into a 16-bit (565) texture unless the texture is 32-bit
/// (the texture's flag <c>0x10</c>); Vitomir's textures are all 32-bit and the picture keeps its colour, alpha
/// opaque. A film wider or taller than the texture is clipped to it here; 1.1's pitch would spill a wider
/// film's rows into the next (Bink's clip is Bink's own, not reproduced). <c>bPreload</c> changes only
/// where Bink reads from, so it is taken and has nothing to do. 1.1's slots outlive the place — nothing closes them on a level change (1.1 never closes them all), so
/// its pointer dangles; Vitomir's go on painting a material nothing draws.</para>
///
/// <para><b>The sound</b> (<i>"Play the video volume but let's have it obey the sound
/// slider volume level"</i>). 1.1 opens the film without <c>BINKNOSOUND</c> (it asks for
/// <c>BINKPRELOADALL</c> or nothing), and Bink's sound system is Miles, set before every story film
/// <c>BinkSetSoundSystem(BinkOpenMiles, the mixer's digital driver)</c>) — so the film's
/// own track plays, flat, from its opening, on a Miles sample of Bink's own beside the mixer's 28
/// (binkw32.dll, not read). Vitomir plays it as the story films' is played
/// (<see cref="StoryFilm"/>): the film's sound as a streaming mixer channel, flat, centred, at full
/// level and <b>not</b> flagged absolute, so the Options' <b>Sound</b> slider (<c>opsoSoundScroll</c>,
/// the mixer's effects volume) scales it — while the QoL
/// <c>filmvolume</c> is on (off, 1.1's full volume: <see cref="Audio.FilmSound"/>). In
/// 1.1 no slider reaches Bink's sample: the sliders scale the mixer's channels one by one, and the
/// driver's master volume, which would, is set once to full at start (1.0) —
/// its setter has no caller. The sound starts with the film, stops when the film
/// is closed (at its end without <c>bLoop</c>, or by <see cref="Close"/> and <see cref="Dispose"/>),
/// and starts again when a loop opens the film again. <b>Ours</b>: being a mixer channel, it takes one
/// of the eight stream channels and goes with the mixer's stop-everything (a story film, a
/// change of place), where Bink's own sample would have played on.</para>
/// </summary>
public sealed class MaterialFilms : IDisposable
{
    /// <summary>How many films play at once: four slots.</summary>
    public const int Slots = 4;

    /// <summary>What <c>bPreload</c> asks <c>BinkOpen</c> for: BINKPRELOADALL.</summary>
    public const int PreloadAll = 0x2000;

    sealed class Slot
    {
        public required MaterialCache? Cache;
        public required LoadedMaterial Material;
        public required int Image;                  // which of the material's images is showing
        public required Func<IFilmPicture?> Open;
        public required IFilmPicture Film;
        public required bool Loop;
        public required bool HardwareUpdate;
        public Mixer? Mixer;                        // ours: where the film's sound plays (Bink's own Miles sample in 1.1)
        public int Channel;                         // the sound's channel id, 0 with none
        public double Ms;                           // the film's own clock since it (re)started
        public int Shown = -1;                      // the frame last copied into the texture
    }

    readonly Slot?[] slots = new Slot?[Slots];

    /// <summary>How many slots are playing.</summary>
    public int Playing => slots.Count(s => s is not null);

    /// <summary>Whether a slot is in use (<c>+0</c> of its six words).</summary>
    public bool InUse(int slot) => slots[slot] is not null;

    /// <summary>The mixer channel a slot's film sound plays in, 0 when it has none (no sound, no mixer, or none to spare).</summary>
    public int SoundChannel(int slot) => slots[slot]?.Channel ?? 0;

    /// <summary>
    /// Starts a film on a material: the slot it took, or −1 when all four are busy, the
    /// material has no picture, or the film will not open. <paramref name="open"/> opens the film, and
    /// opens it again for a loop. With a <paramref name="mixer"/> the film's sound plays from now
    /// (see the summary).
    /// </summary>
    public int Start(LoadedMaterial material, Func<IFilmPicture?> open, bool preload, bool loop, bool hardwareUpdate,
                     MaterialCache? cache = null, Mixer? mixer = null)
    {
        _ = preload;                                // BINKPRELOADALL: where Bink reads from, nothing to see
        int free = Array.IndexOf(slots, null);
        if (free < 0) return -1;                    // Four in use
        if (material.Image is null) return -1;      // ours: 1.1 would assert on the empty texture later
        if (open() is not { } film) return -1;      // BinkOpen failed, the slot stays free
        slots[free] = new Slot
        {
            Cache = cache, Material = material, Image = material.CurrentImage, Open = open, Film = film,
            Loop = loop, HardwareUpdate = hardwareUpdate, Mixer = mixer,
        };
        PlaySound(slots[free]!);
        return free;
    }

    /// <summary>
    /// One frame of 1.1's playback walk over <paramref name="milliseconds"/> of the film's
    /// own clock: each film's due frame copied into its texture, the card told when it asked to be, the
    /// ended ones closed or looped.
    /// </summary>
    public void Step(double milliseconds)
    {
        for (int i = 0; i < Slots; i++)
        {
            if (slots[i] is not { } s) continue;
            s.Ms += milliseconds;
            if (s.Film.Advance((int)s.Ms))
            {
                if (s.Film.FrameIndex != s.Shown) Paint(s);
                continue;
            }
            // Run out: the frame closes a film on its last frame unless it loops; after the last,
            // Bink's next frame is the first. Ours: the film is opened again for it.
            if (!s.Loop) { Close(i); continue; }
            s.Film.Dispose();
            StopSound(s);
            if (s.Open() is not { } again) { slots[i] = null; continue; }
            s.Film = again;
            s.Ms = 0;
            s.Shown = -1;
            PlaySound(s);                           // the loop's sound from its start again
            if (s.Film.Advance(0)) Paint(s);
        }
    }

    // BinkCopyToBuffer(film, pitch, texture height, 0, 0, …): the film's top
    // row to the texture's first row — its bottom edge — and so on up; then the card's copy when asked.
    static void Paint(Slot s)
    {
        s.Shown = s.Film.FrameIndex;
        var picture = s.Material.Images[s.Image]!;
        CopyUpsideDown(s.Film.Pixels, s.Film.Info.Width, s.Film.Info.Height, picture);
        if (s.HardwareUpdate) s.Cache?.Repaint(s.Material, s.Image);
    }

    /// <summary>
    /// A film frame (RGBA, top row first) copied into a texture's picture the way 1.1's buffer takes
    /// it: film row <c>r</c> to texture row <c>r</c> counted from the bottom edge — in
    /// <see cref="TgaImage.Rgba"/>'s top-first rows, row <c>Height − 1 − r</c> — at the left edge,
    /// clipped to the texture, alpha opaque.
    /// </summary>
    public static void CopyUpsideDown(ReadOnlySpan<byte> frame, int width, int height, TgaImage picture)
    {
        int w = Math.Min(width, picture.Width), h = Math.Min(height, picture.Height);
        if (frame.Length < width * height * 4) return;
        for (int r = 0; r < h; r++)
        {
            var from = frame.Slice(r * width * 4, w * 4);
            var to = picture.Rgba.AsSpan((picture.Height - 1 - r) * picture.Width * 4, w * 4);
            from.CopyTo(to);
            for (int x = 3; x < to.Length; x += 4) to[x] = 255;
        }
    }

    /// <summary>
    /// Closes one film (which nothing in 1.1 calls; the frame does the same inline when a
    /// film ends): the slot cleared, the picture left as the last frame made it.
    /// </summary>
    public void Close(int slot)
    {
        if (slots[slot] is { } s) { s.Film.Dispose(); StopSound(s); }
        slots[slot] = null;
    }

    /// <summary>
    /// The film's sound started: a stream channel, flat and centred, at level 1, which the effects volume — the
    /// Sound slider — scales as it scales the story films' (<see cref="StoryFilm"/>) while the QoL <c>filmvolume</c> is
    /// on, and does not with it off (1.1's full volume; <see cref="Audio.FilmSound"/>). Nothing with no mixer or no
    /// sound track.
    /// </summary>
    static void PlaySound(Slot s) =>
        s.Channel = s.Mixer is { } mixer ? Vitomir.Engine.Audio.FilmSound.Play(mixer, s.Film.Sound()) : 0;

    /// <summary>The film's sound stopped with it, if its channel is still its own.</summary>
    static void StopSound(Slot s)
    {
        if (s.Channel != 0) s.Mixer?.Stop(s.Channel);
        s.Channel = 0;
    }

    /// <summary>Closes every film: 1.1 does it at exit and in a close-all which nothing calls.</summary>
    public void Dispose()
    {
        for (int i = 0; i < Slots; i++) Close(i);
    }
}
