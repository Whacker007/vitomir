// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A film the story asks for, shown from inside the game (the main sequence's state 1).
using Vitomir.Engine.Audio;

namespace Vitomir.Engine.Video;

/// <summary>
/// What a film player has to be able to do for <see cref="StoryFilm"/>: <see cref="VideoClip"/> is
/// the real one; a test gives it a fake.
/// </summary>
public interface IFilm : IDisposable
{
    /// <summary>What the file says about itself.</summary>
    FilmInfo Info { get; }

    /// <summary>Brings the picture up to this many milliseconds in; false once the film has run out.</summary>
    bool Advance(int ms);

    /// <summary>The film's sound as a streaming sound the mixer can play, or null when it has none.</summary>
    Sound? Sound();
}

/// <summary>
/// A film the story asked for (`Codex.PlayVideo`), from the moment the main sequence takes
/// the request to the moment the script that asked hears `videoended`.
///
/// **How 1.1 goes into a film.** The native only queues it (request 14). At the end of the
/// frame the main sequence takes the request: with films on it
/// opens the film — which silences the music and the ambient, switches the
/// display to 640×480 and clears both buffers to black — then **stops every sound the mixer is
/// playing**, lets go of the end text, and puts the whole
/// game into **state 1** (flags 0x21), the film state. A film that will not open, or films off, and
/// the script is told `videoended` at once instead.
///
/// **Held.** State 1 runs no world: its frame is only the film — nothing
/// thinks, nothing walks, no clock moves, no interface is drawn. Each frame it asks whether
/// **Escape or Space** was pressed this frame (the keys' stamps
/// against the frame's); if so it clears the whole key state and closes the
/// film. Otherwise it plays the film's next frame, which closes it after the
/// last. No other key and no click ends a story film.
///
/// **How it comes back.** Closing  closes the Bink file, lifts the silence
/// (which restarts the music, since the stop-everything stopped its channel; the ambient's channel
/// is not restarted and stays silent until something starts one), puts the display mode back, and
/// tells the script that asked `videoended(id)` (with the script and id kept
/// when the film was asked for). The film state's frame then sees the film gone and, with the start-up
/// phase long over, goes back to the game (flags 0x5f), which also
/// forgets the listener's sector so the place's sector loops start again on the next frame.
///
/// **Where it is drawn.** At its own size, at the left of the 640×480 screen: the film copies each
/// frame with `BinkCopyToBuffer(…, 480, 0, y, …)`, `y` being **25** when the request's flag
/// (from the main sequence) is set and 0 when not. `PlayVideo` always sets it
/// (the request word resets it to 1 after each film), so a story film's top edge is 25 lines down,
/// with black round the rest — not centred.
/// </summary>
public sealed class StoryFilm(Func<string, IFilm?> open)
{
    /// <summary>The line a flagged film's top edge is drawn on (25).</summary>
    public const int FlaggedTop = 25;

    /// <summary>The line an unflagged film's top edge is drawn on — the logo's (0).</summary>
    public const int UnflaggedTop = 0;

    /// <summary>The mixer the film's sound goes through, and whose music the film silences.</summary>
    public GameAudio? Audio { get; set; }

    /// <summary>
    /// Told when the film is over, however it ended — the `videoended` the script hears.
    /// </summary>
    public Action? Ended { get; set; }

    /// <summary>
    /// Told as a film takes the screen, after the sounds are stopped: where the main sequence lets the
    /// end text go (<see cref="Ui.EndTextHold"/>).
    /// </summary>
    public Action? Opened { get; set; }

    /// <summary>The film showing, or null.</summary>
    public IFilm? Film { get; private set; }

    /// <summary>Its name as the script gave it.</summary>
    public string? Name { get; private set; }

    /// <summary>Whether a film holds the screen: the game is in state 1 and nothing else runs.</summary>
    public bool Playing => Film is not null;

    /// <summary>How far in the picture is, in milliseconds of the film's own clock.</summary>
    public int ElapsedMs { get; private set; }

    /// <summary>Where its top edge goes on the 640×480 screen: 25 for every film a script asks for.</summary>
    public int Top { get; private set; } = FlaggedTop;

    int channel;

    /// <summary>
    /// Main sequence request 14: opens the film and takes the screen.
    /// Answers false when it cannot be opened, in which case the caller tells the script at once, as
    /// 1.1 does; true means the film is showing and <see cref="Ended"/> will be called when it is over.
    /// A film already open is closed first, and its own script told.
    /// </summary>
    public bool Start(string name, bool flagged = true) =>
        open(name) is { } film && Show(name, film, flagged);

    /// <summary>
    /// <see cref="Start"/> with the film already opened: the host opens it where the request is
    /// taken, to answer whether it will play, and hands it over at the end of the frame — where 1.1
    /// both opens it and stops the sounds, after everything else in the frame
    /// has had its say, so nothing the frame started is still heard under the film.
    /// </summary>
    public bool Show(string name, IFilm film, bool flagged = true)
    {
        if (Playing) Close();
        Film = film;
        Name = name;
        ElapsedMs = 0;
        Top = flagged ? FlaggedTop : UnflaggedTop;
        if (Audio is { } audio)
        {
            audio.Silence(true);
            audio.Mixer.StopAll();
            // Ours: Bink played its own sound through Miles, beside the mixer; ours goes through the
            // mixer as an ordinary streaming channel, started after the stop so it is not stopped too —
            // under the Sound slider with the QoL `filmvolume` on, at 1.1's full volume with it off (`FilmSound`).
            channel = Vitomir.Engine.Audio.FilmSound.Play(audio.Mixer, film.Sound());
        }
        Opened?.Invoke();                          // The end text let go
        return true;
    }

    /// <summary>
    /// One frame of the film state: Escape or Space pressed this frame closes the film;
    /// otherwise the picture is brought up to <paramref name="elapsedMs"/>,
    /// and a film that has run out is closed.
    /// </summary>
    public void Frame(int elapsedMs, bool skip)
    {
        if (Film is not { } film) return;
        if (skip) { Close(); return; }
        ElapsedMs = elapsedMs;
        if (!film.Advance(elapsedMs)) Close();
    }

    /// <summary>
    /// The film's end, however it came (its last frame, or a skip): the file closed, the
    /// silence lifted, the script told, and the game given back its screen with its sector forgotten.
    /// </summary>
    public void Close()
    {
        if (Film is not { } film) return;
        Film = null;
        film.Dispose();
        if (Audio is { } audio)
        {
            if (channel != 0) audio.Mixer.Stop(channel);
            audio.Silence(false);                  // The music comes back
        }
        channel = 0;
        Ended?.Invoke();
        Audio?.ForgetSector();
        Name = null;
    }
}
