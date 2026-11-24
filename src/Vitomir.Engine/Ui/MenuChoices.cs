// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the player chose in the Options screens during this session, laid back over each new run's
// settings so that nothing chosen with OK is undone by the next load, new game or trip to the main
// menu (ours).
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The session's own choices** (ours). All settings in the options menu
/// apply as soon as OK is pressed: Fixes, key mappings, graphics settings, anything. Every
/// setting is put in force the moment OK is pressed;
/// what could still take one back is the **next run** — a load, a new game, the main menu,
/// each of which reads the settings afresh:
///
/// - a <c>--fix</c>, <c>--qol</c>, <c>--no-qol</c> or <c>--gamma</c> on the command line is a setting
///   that carries from run to run, and each run applied it again over what the player had chosen;
/// - a <c>vitomir.ini</c> that could not be written (read-only, or locked) was read back unchanged.
///
/// So each choice is kept here as it is made, and the three <c>Over</c> lay them over a new run's settings: the command line still decides
/// a session until the player chooses otherwise, and then the player's choice stands until the program
/// ends. A recorded tape's settings are laid over these in turn when it is replayed (<c>InputTape</c>).
/// </summary>
public sealed class MenuChoices
{
    /// <summary>The program's: the Options screens write here, and each run's start reads it.</summary>
    public static MenuChoices Session { get; } = new();

    /// <summary>The Options screens' settings as last written (OK, Exit, a speaker radio), or null.</summary>
    public GameOptions? Options { get; private set; }

    /// <summary>The brightness last kept with the Display screen's OK, or null — which is what outranks <c>--gamma</c>.</summary>
    public float? Gamma { get; private set; }

    /// <summary>Every Fix and quality-of-life switch as the Fixes page's OK left them, or null.</summary>
    public (Dictionary<string, bool> Fixes, Dictionary<string, bool> Qol)? Switches { get; private set; }

    /// <summary>The key page's bindings as its OK left them, or null.</summary>
    public KeyBindings? Keys { get; private set; }

    /// <summary>The Options screens wrote their settings (every OK, the Sound screen's Exit, a speaker radio).</summary>
    public void Chose(GameOptions options) => Options = options.Clone();

    /// <summary>The Display screen's OK kept a brightness.</summary>
    public void ChoseGamma(float gamma) => Gamma = gamma;

    /// <summary>The Fixes page's OK.</summary>
    public void Chose(Fixes fixes, Qol qol) =>
        Switches = (fixes.All.ToDictionary(f => f.Name, f => f.On), qol.All.ToDictionary(q => q.Name, q => q.On));

    /// <summary>The key page's OK.</summary>
    public void Chose(KeyBindings keys) => Keys = keys.Clone();

    /// <summary>A new run's switches, read from the ini and the command line, with the session's choice laid back over them.</summary>
    public void Over(Fixes fixes, Qol qol)
    {
        if (Switches is not { } chosen) return;
        foreach (var (name, on) in chosen.Fixes) fixes[name] = on;
        foreach (var (name, on) in chosen.Qol) qol[name] = on;
    }

    /// <summary>
    /// A new run's options: the session's choice in place of what was read — keeping the fresh read's
    /// developer bit, which is the run's own <c>--devmode</c> and not a menu setting.
    /// </summary>
    public GameOptions Over(GameOptions read)
    {
        if (Options is not { } chosen) return read;
        var kept = chosen.Clone();
        kept.Flags = (kept.Flags & ~GameOptions.DevMode) | (read.Flags & GameOptions.DevMode);
        return kept;
    }

    /// <summary>A new run's keys: the key page's choice this session in place of what was read.</summary>
    public KeyBindings Over(KeyBindings read) => Keys?.Clone() ?? read;

    /// <summary>
    /// The brightness a run starts a place with: the player's choice this session when there is one, else
    /// <c>--gamma</c> (<paramref name="flag"/>) when given, else the settings'.
    /// </summary>
    public float Brightness(float? flag, float settings) => Gamma ?? flag ?? settings;
}
