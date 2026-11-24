// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The floating captions (`Vitomir.Engine.Ui.Captions`) — the chapter title, the end text — drawn over the frame.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>Draws whatever captions are up, each in its own font and at its own fade.</summary>
public sealed class CaptionPass(UiCanvas ui, Captions captions) : IUiPass
{
    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    public Captions Captions { get; } = captions;

    /// <summary>The clock the fades run on. Whoever owns the frame keeps it fed.</summary>
    public int NowMs { get; set; }

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        Captions.Prune(NowMs);
        if (Captions.Up.Count == 0 || width <= 0 || height <= 0) return;
        ui.Begin(width, height);
        foreach (var c in Captions.Up)
        {
            var font = ui.Assets.Font(c.Font);
            var tint = new Tint(1f, 1f, 1f, c.Alpha(NowMs));
            float penY = c.PenYAt(font.LineHeight, NowMs);
            if (c.Width > 0f) ui.TextClipped(font, c.X, penY, c.Text, tint, c.X, 0f, c.X + c.Width, 480f);
            else ui.Text(font, c.X, penY, c.Text, tint);
        }
    }
}
