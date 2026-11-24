// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game's keys as a harness's scripted input asks for them (the scripted input is the platform's SDL host's since).
using Vitomir.Engine.Ui;
using Vitomir.Platform.Render.Classic;

namespace Vitomir;

/// <summary>
/// **The game's keys for a harness's scripted input**: what <c>ScriptedInput</c> read of the engine itself while it was
/// <c>vitomir.exe</c>'s — a key's name as the key page reads it (<see cref="KeyChord.Parse"/>), the mouse's base
/// (<see cref="KeyChord.MouseBase"/>), a position's US keycode (<see cref="KeyInput.UsKey"/>), the keys the harness holds
/// (<see cref="KeyInput.HarnessHeld"/>) and a key's name for the log — given to the host, which names no engine type.
/// </summary>
static class ScriptedKeys
{
    /// <summary>The one set: every member reads the engine's own, as the moved code did.</summary>
    internal static readonly ScriptKeys Keys = new()
    {
        Parse = name => KeyChord.Parse(name) is { } chord ? new ScriptKey(chord.Input, (int)chord.Mods) : null,
        MouseBase = KeyChord.MouseBase,
        UsKey = KeyInput.UsKey,
        Held = KeyInput.HarnessHeld,
        Name = key => new KeyChord(key.Input, (KeyMods)key.Mods).ToString(),
    };
}
