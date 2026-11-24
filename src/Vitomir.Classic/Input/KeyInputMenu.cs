// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A key let go while a menu is up: `Engine.Ui.MenuReleases`.
using Vitomir.Engine.Ui;
using Vitomir.Platform.Modules;

namespace Vitomir;

static partial class KeyInput
{
    /// <summary>The releases a key let go under a menu owes the game (<see cref="MenuReleases"/>), handed on after the menu's own.</summary>
    static readonly Queue<LoopInput> worldReleases = new();

    /// <summary>
    /// Whether the event being handled is the release of a key held into a menu and let go there: the game's
    /// handlers take it, not the menu, which has had the key as it came (1.1's key-state array follows the keyboard
    /// whatever is up; <see cref="MenuReleases"/>).
    /// </summary>
    public static bool WorldRelease { get; private set; }

    /// <summary>The next release owed to the game, if any; <see cref="WorldRelease"/> set for it.</summary>
    static bool OwedRelease(out LoopInput e)
    {
        WorldRelease = false;
        if (!worldReleases.TryDequeue(out e)) return false;
        WorldRelease = true;
        return true;
    }

    /// <summary>
    /// An event that went to a menu or a text field as it came: a key it lets go that the binding layer holds is
    /// released for the game too, after it (<see cref="MenuReleases.Lift"/>; <see cref="Release"/> as a focus change does).
    /// </summary>
    static void UnderMenu(in LoopInput e)
    {
        int input = e.Kind == LoopInputKind.KeyUp ? Scan(e) : -1;
        if (!MenuReleases.Lift(down, input, keyUp: input >= 0, button: false, out var was)) return;
        // Said as a focus change says what it let go of, so a key that seems stuck can be followed — but not for
        // 1.1's keys that open what then has the keyboard (the console's `, the map's M, the chat line's Enter), whose
        // own release always comes under it.
        if (was.Any(w => w.Kind != LoopInputKind.KeyDown || !Opens(w.Code)))
            Console.WriteLine($"  keys: {new KeyChord(input)} let go under a menu; released for the game too");
        Release(was, e);
        while (pending.TryDequeue(out var up)) worldReleases.Enqueue(up);
    }

    /// <summary>1.1's keys that open something with the keyboard: the console, the map, the chat line.</summary>
    static bool Opens(int key) =>
        key is LoopScancode.Grave or LoopScancode.M or LoopScancode.Return;
}
