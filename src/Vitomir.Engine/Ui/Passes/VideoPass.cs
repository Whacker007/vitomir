// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A film on the screen.
using Vitomir.Engine.Video;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>
/// Draws the frame a <see cref="VideoClip"/> is holding, into the interface's own 640x480 space so
/// it lands wherever the window is. The picture keeps its shape: it is fitted to the screen and
/// centred, with black either side of it or above and below, because a film is 640x346 and the
/// screen is not.
///
/// The picture is a streamed one of the canvas's (sent by id through the draw list's resources), made the size of
/// the film and repainted as frames arrive.
/// </summary>
public sealed class VideoPass(UiCanvas ui) : IUiPass, IDisposable
{
    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    readonly int picture = ui.NewPicture();
    bool shown;
    int width, height;

    /// <summary>Whether a film's frame has been handed over and is ready to draw.</summary>
    public bool Ready => shown;

    /// <summary>
    /// Takes the clip's current frame. Call it once a frame, after <c>Advance</c>; it does nothing
    /// if the clip has no frame yet.
    /// </summary>
    public void Show(VideoClip clip)
    {
        var pixels = clip.Pixels;
        if (clip.FrameIndex < 0 || pixels.IsEmpty) return;
        if (!shown || width != clip.Info.Width || height != clip.Info.Height)
        {
            Release();
            width = clip.Info.Width;
            height = clip.Info.Height;
            shown = true;
        }
        ui.Repaint(picture, width, height, pixels);
    }

    /// <summary>Whether there is a film to draw at all. Off, the pass costs a branch.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Where the film's top edge goes on the 640×480 screen, drawn at its own size from the left
    /// edge, as 1.1 copies it (`BinkCopyToBuffer(…, 480, 0, top, …)`, top 25 for a flagged
    /// film and 0 for the logo; `StoryFilm`). Null fits it to the screen and centres it (ours).
    /// </summary>
    public float? Top { get; set; }

    /// <summary>The picture over the black the screen was cleared to: at <see cref="Top"/>, or fitted and centred.</summary>
    public void Emit(int surfaceWidth, int surfaceHeight, FreeCamera camera, Simulation sim)
    {
        if (!Enabled || !shown || width <= 0 || height <= 0) return;
        if (surfaceWidth <= 0 || surfaceHeight <= 0) return;
        ui.Begin(surfaceWidth, surfaceHeight);
        // The interface's y runs up from the bottom and 1.1's surface's runs down from the top.
        if (Top is { } top) ui.Picture(picture, 0f, UiAssets.Height - top - height, width, height);
        else
        {
            float scale = Math.Min(UiAssets.Width / (float)width, UiAssets.Height / (float)height);
            float w = width * scale, h = height * scale;
            ui.Picture(picture, (UiAssets.Width - w) / 2f, (UiAssets.Height - h) / 2f, w, h);
        }
    }

    /// <summary>Lets the film's texture go, which is what ends one film before the next begins.</summary>
    public void Release()
    {
        if (shown) ui.Forget(picture);
        shown = false;
        width = height = 0;
    }

    public void Dispose() => Release();
}
