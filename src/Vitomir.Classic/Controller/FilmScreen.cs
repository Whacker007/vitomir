// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The film state in the game run: a story film on the screen while the game is held (state 1).
using Vitomir.Engine.Audio;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.Video;
using Vitomir.Platform.Audio;
using Vitomir.Classic;
using Vitomir.Platform.Modules;

namespace Vitomir.Classic;

/// <summary>
/// The host's half of <see cref="StoryFilm"/>: it opens the films with the player's FFmpeg, and while
/// one shows it **is** the frame — the game loop hands it each frame whole and runs nothing of its own
/// (state 1: no world, no interface). Escape or Space closes the film; the window's close
/// ends the run. The picture is drawn on black at its own size, its top edge 25 lines down the
/// 640×480 screen, through a <see cref="ScreenFrame"/> — the same black-and-a-pass
/// frame the front end draws with.
/// </summary>
sealed unsafe class FilmScreen : IDisposable
{
    readonly IClassicHost host;
    readonly VideoPass pass;
    readonly ScreenFrame screen;
    readonly AudioOutput speakers;
    readonly SoundTape? tape;
    ulong begin, previous;
    // A film asked for this frame and opened, waiting for the frame to end (the main loop's request 14).
    IFilm? waiting;
    string waitingName = "";

    /// <summary>The film itself: the state, the sound and the `videoended`.</summary>
    public StoryFilm Film { get; }

    /// <summary>Whether a film holds the screen this frame — or will, the moment this frame ends.</summary>
    public bool Playing => Film.Playing || waiting is not null;

    readonly GameFiles files;
    readonly FFmpegTool? ffmpeg;

    /// <summary>Whether this run is a network game's (the host's or a player's), where `skipstoryfilms` does nothing.</summary>
    public Func<bool> Networked { get; set; } = () => false;

    public FilmScreen(IClassicHost host, IClassicRenderer gpu, IClassicInterface backend, UiCanvas ui, GameFiles files, FFmpegTool? ffmpeg, GameAudio audio,
                      AudioOutput speakers, SoundTape? tape)
    {
        this.host = host;
        this.files = files;
        this.ffmpeg = ffmpeg;
        this.speakers = speakers;
        this.tape = tape;
        pass = new VideoPass(ui);
        screen = new ScreenFrame(gpu.Screen()) { After = backend.Of(pass) };
        Film = new StoryFilm(name => Open(files, ffmpeg, name)) { Audio = audio };
    }

    // A film the player's FFmpeg can decode, or null — which the caller takes as films off (1.1's
    // the films switch off) or a file that would not open, and tells the script at once.
    static IFilm? Open(GameFiles files, FFmpegTool? ffmpeg, string name)
    {
        if (ffmpeg is null) return null;
        if (Films.Find(files, name) is not { } path)
        {
            Console.WriteLine($"video: no {name} in the install, so it ends at once");
            return null;
        }
        var clip = VideoClip.Open(ffmpeg, path);
        if (clip is null) Console.WriteLine($"video: ffmpeg would not open {name}, so it ends at once");
        return clip;
    }

    /// <summary>
    /// `CodexScenes.PlayFilm`: the film is opened now, to answer whether it will play, and takes the
    /// screen when this frame is over; false when it would not open.
    /// </summary>
    public bool Start(string name)
    {
        // The QoL `skipstoryfilms` (ours): the film ends as it is asked for, as Escape on its first frame ends it — single
        // player only (Networked), a network game's films as they always were.
        if (Films.StorySkipped(Vitomir.Engine.World.Qol.Current, Networked()))
        {
            Console.WriteLine($"video: {name} skipped (skipstoryfilms)");
            return false;
        }
        if (Film.Film is null && waiting is null && Open(files, ffmpeg, name) is { } film)
        {
            waiting = film;
            waitingName = name;
            return true;
        }
        // Ours: a second request in the same frame, or during a film, is not reachable in 1.1 (one
        // request word, and state 1 runs no scripts); it is refused so its script hears at once.
        return false;
    }

    // The frame is over: the waiting film takes the screen. `now` is the frame's time.
    void Begin(ulong now)
    {
        if (waiting is not { } film) return;
        waiting = null;
        Film.Show(waitingName, film);
        pass.Release();
        pass.Top = Film.Top;
        pass.Enabled = true;
        // The film is the whole frame, no world behind it: a sheet shorter than the picture is centred (ours).
        UiLayout.Seat = SheetSeat.Centre;
        begin = previous = InputTape.Clock(now);   // a tape's clock, from the frame's time
        var info = film.Info;
        Console.WriteLine($"video: {waitingName} — {info.Width}x{info.Height}, {info.Fps:0.##} fps, {info.Seconds:0.#} s; the game is held");
    }

    /// <summary>
    /// One frame of the film state, in the window: the events (Escape or Space ends the film, closing
    /// the window ends the run — anything else is swallowed, as 1.1's state 1 reads no other input),
    /// the picture brought up to the clock, drawn, and the mixer run for the film's sound — its
    /// channels only, with no listener and no sector, since the world is not running.
    /// Answers false when the window was closed. <paramref name="now"/> is the frame's time (the host's clock as the frame
    /// began, handed in; read from the host here until then).
    /// </summary>
    public bool Frame(ulong now)
    {
        Begin(now);
        bool open = true, skip = false;
        while (InputTape.Poll(out var e))
        {
            if (e.Kind == LoopInputKind.Quit) open = false;
            // Alt+Enter over a film in a game too, as over the opening films and the menus (ours, "anywhere").
            else if (KeyInput.FullScreenKey(e, capturing: false)) continue;
            // The Escape and Space key stamps against this frame's.
            else if (e.Kind == LoopInputKind.KeyDown && !e.Repeat
                     && e.Code is LoopScancode.Escape or LoopScancode.Space)
                skip = true;
        }
        now = InputTape.Clock(now);
        string? name = Film.Name;
        Film.Frame((int)((now - begin) / 1_000_000), skip || !open);
        if (Film.Playing)
        {
            pass.Show((VideoClip)Film.Film!);
            var picture = host.Acquire();
            if (picture.Acquired) picture.Draw(screen);
            host.Submit(picture);
        }
        else
        {
            pass.Enabled = false;
            pass.Release();
            // The game's frames again, the world behind the sheet.
            UiLayout.Seat = SheetSeat.Bottom;
            Console.WriteLine($"video: {name} {(skip ? "skipped" : "ended")} after {(now - begin) / 1e9:0.#} s; the game goes on");
        }
        float dt = Math.Min((now - previous) / 1e9f, 0.1f);
        previous = now;
        var audio = Film.Audio!;
        audio.Frame(dt, audio.Mixer.GameMs, System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitY, System.Numerics.Vector3.UnitX, -1);
        if (speakers.Live) speakers.Pump(audio.Mixer);              // the platform's output, no amount hook (as before)
        else tape?.Render(audio.Mixer, dt);
        return open;
    }

    /// <summary>
    /// A headless still of the film state: the film brought up to <paramref name="atMs"/> and drawn
    /// as the window would show it. Harness only; the film keeps playing.
    /// </summary>
    public byte[]? Still(int width, int height, int atMs)
    {
        Begin(host.Now());
        if (Film.Film is not VideoClip clip) return null;
        Film.Frame(atMs, false);
        if (!Film.Playing) return null;
        pass.Show(clip);
        return screen.RenderToImage(width, height);
    }

    public void Dispose()
    {
        waiting?.Dispose();
        waiting = null;
        Film.Audio = null;
        Film.Ended = null;
        Film.Close();
        pass.Dispose();
    }
}
