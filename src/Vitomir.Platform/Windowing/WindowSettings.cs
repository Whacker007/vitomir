// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the window keeps between starts, as the window code asks for it: the window's rules and
// the host's window code read and write `[window]` through this and know nothing of the file behind it. The Classic game's
// is the player's `vitomir.ini` (the engine's `IniWindowSettings`), the same file the old program always kept it in;
// moves it into the module's own settings, which changes only what is behind this.
namespace Vitomir.Platform.Windowing;

/// <summary>
/// **The window's keys as the player's settings have them at one moment**: the <c>[window]</c> section's keys
/// (<c>fullscreen</c>, <c>fullscreenmode</c>, <c>worldsize</c>, <c>borderless</c>, <c>x</c>, <c>y</c>, <c>width</c>,
/// <c>height</c>, <c>display</c>, <c>maximised</c>), without the section's name, matched without regard to case, each
/// value as the file gives it (trimmed).
/// </summary>
public sealed class WindowKeys
{
    readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>No keys at all (a first start, or a run whose settings cannot be read).</summary>
    public static readonly WindowKeys None = new([]);

    /// <summary>The keys given, a later one of a name over an earlier one.</summary>
    public WindowKeys(IEnumerable<KeyValuePair<string, string>> keys)
    {
        foreach (var kv in keys) values[kv.Key] = kv.Value;
    }

    /// <summary>A key's value, or null when it is not there.</summary>
    public string? this[string key] => values.TryGetValue(key, out var v) ? v : null;

    /// <summary>How many keys there are.</summary>
    public int Count => values.Count;

    /// <summary>INI's idea of true, as the engine's <c>IniFile.IsTrue</c>: <c>1</c>, <c>true</c>, <c>yes</c>, <c>on</c>.</summary>
    public static bool IsTrue(string? value) =>
        value is not null && (value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// **Where the window keeps what it keeps**: <see cref="Read"/> gives <c>[window]</c> as it is now (read afresh each
/// time, so a hand edit between starts is seen), <see cref="Write"/> changes some of its keys, every other line of the
/// settings left as it is. A write may throw <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>; the
/// window code says so in the log and goes on. The game whose window it is gives it (the Classic game's
/// <c>WindowAsk.Settings</c>); a replay's writes are the game's to refuse (1.1's settings are never written by a replay).
/// </summary>
public interface IWindowSettings
{
    /// <summary>The window's keys now.</summary>
    WindowKeys Read();

    /// <summary>Some of the window's keys set, the rest of the settings as they were.</summary>
    void Write(IEnumerable<KeyValuePair<string, string>> values);
}

/// <summary>Settings that keep nothing: every read is <see cref="WindowKeys.None"/> and every write is dropped (a host given none).</summary>
public sealed class NoWindowSettings : IWindowSettings
{
    /// <summary>The one there needs to be.</summary>
    public static readonly NoWindowSettings Instance = new();

    NoWindowSettings() { }

    public WindowKeys Read() => WindowKeys.None;

    public void Write(IEnumerable<KeyValuePair<string, string>> values) { }
}
