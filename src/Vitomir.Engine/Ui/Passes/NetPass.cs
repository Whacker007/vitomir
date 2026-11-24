// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a network game adds to the picture: the multiplayer status bar's chat box and its edit line
// (`statusBarMP`'s `sbChatbox` and `sbChatEdit`), and the Storyteller's panes — which are
// ordinary interface panes and are drawn by a `MenuPass` of their own — with the check boxes those
// panes are full of, which that pass draws right after each pane (`MenuPass.Pictures`).
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>Draws the chat and the Storyteller's panes over the frame.</summary>
public sealed class NetPass(UiCanvas ui) : IUiPass
{
    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    /// <summary>Whether a network game is on at all.</summary>
    public bool Enabled { get; set; }
    /// <summary>The chat box's view of the chat log, laid out in `sbChatbox`; null draws none.</summary>
    public MessageBox? Chat { get; set; }
    /// <summary>The edit line (`sbChatEdit`), or null to draw the line at the foot of the screen.</summary>
    public UiControl? Edit { get; set; }
    /// <summary>What is being typed, or null when nobody is typing.</summary>
    public string? Typing { get; set; }
    /// <summary>The edit's caret, blinking every half second.</summary>
    public bool CaretOn { get; set; }
    /// <summary>The Storyteller's interface (its bar and panes), drawn with the front end's rules.</summary>
    public MenuPass? Panes { get; set; }
    /// <summary>
    /// Pictures drawn over the panes, in the 640 x 480 space: art, x, y, w, h — the host's pane on a player's machine (`NetPane`).
    /// The Storyteller's panes' check boxes are drawn by their pass, each right after its pane (`MenuPass.Pictures`).
    /// </summary>
    public Func<IEnumerable<(string Art, float X, float Y, float W, float H)>>? Extra { get; set; }
    /// <summary>Words drawn by the controller: the current target, the elapsed time. Text, font, x, y, colour.</summary>
    public Func<IEnumerable<(string Text, string Font, float X, float Y, uint Colour)>>? Words { get; set; }

    public const string ChatFont = "defaultFont10";

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (!Enabled || width <= 0 || height <= 0) return;
        Panes?.Emit(width, height, camera, sim);
        ui.Begin(width, height);
        if (Extra?.Invoke() is { } extra)
            foreach (var (art, x, y, w, h) in extra) ui.Draw(art, x, y, w, h);
        var font = ui.Assets.Font(ChatFont);
        if (Chat is { } box)
        {
            box.Sync();
            var at = box.Box;
            foreach (var (text, y, colour) in box.Drawn())
                ui.TextClipped(font, at.X, at.Y + y, text, ReadPass.Colour(colour), at.X, at.Y, at.X + at.Width, at.Y + at.Height);
        }
        if (Typing is { } line)
        {
            string shown = CaretOn ? line + MenuEdit.Caret : line;
            if (Edit is { } e)
            {
                int over = Math.Max(0, font.Measure(shown) - e.Width);
                ui.TextClipped(font, e.X - over, e.Y + e.Height - font.LineHeight, shown, Tint.White, e.X, e.Y, e.X + e.Width, e.Y + e.Height);
            }
            else ui.Text(font, 8, 132, "> " + shown, Tint.White);
        }
        if (Words?.Invoke() is { } words)
            foreach (var (text, f, x, y, colour) in words) ui.Text(ui.Assets.Font(f), x, y, text, Tint.From(colour));
    }
}
