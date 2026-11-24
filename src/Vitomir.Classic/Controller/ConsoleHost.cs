// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The developer console, given to a game: the console (`Engine.Ui.DevConsole`), its commands
// (`Engine.Ui.ConsoleCommands`, 1.1's table read handler by handler) and what the game itself owns
// that the commands reach — the renderer's fog and brightness, the panes, the clipboard.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;

namespace Vitomir;

/// <summary>
/// Gives a game's <see cref="FrontEnd"/> its <see cref="DevConsole"/> and the commands it can run.
/// Everything the commands touch is asked for when they run, because a journey replaces the place,
/// its minds and its renderer.
/// </summary>
static class ConsoleHost
{
    public static DevConsole Attach(FrontEnd menus, ConsoleCommands.Game game, Action<string> post)
    {
        var console = new DevConsole(menus);
        menus.DevConsole = console;
        console.Posted += post;
        ConsoleCommands.Register(console, game);
        return console;
    }

    /// <summary>
    /// A file for `totals` and `enemycount` to write their report to, named as typed and put in the
    /// user's own Vitomir folder (1.1 wrote it wherever the game ran from).
    /// </summary>
    public static TextWriter? Report(string name)
    {
        try
        {
            string path = Path.Combine(Diag.UserDataFolder, Path.GetFileName(name));
            return File.CreateText(path);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
