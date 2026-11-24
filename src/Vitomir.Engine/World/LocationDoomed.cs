// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A destroyed thing's scripts, as its deletion reaches them (a comparison with 1.1).
namespace Vitomir.Engine.World;

public sealed partial class Location
{
    /// <summary>
    /// A thing <see cref="Remove"/> has just destroyed, handed to the script layer. The destroy
    /// frees nothing: it sets `THING_FLAG_REMOVE`, and the thing's own next tick
    /// calls its deleting destructor, whose teardown sends its own script and
    /// every script capturing it `destroyed`, drops the holds on it and frees
    /// its script with the script's timers and holds. The engine half only says which
    /// thing; `CodexScenes.Destroyed.cs` does the rest. Null with no scripts.
    /// </summary>
    public Action<WorldThing>? Doomed { get; set; }
}
