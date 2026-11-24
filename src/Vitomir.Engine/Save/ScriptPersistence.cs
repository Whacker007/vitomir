// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The script layer's half of a save. The engine cannot see Java, so the scripts are asked through
// this: what each one's `save()` writes, which timers are pending, what has been captured — and, on
// a load, to rebuild them in 1.1's order (constructor, `restore`, timers, captures, `restored`, and
// only then `beginscene`).
using Vitomir.Engine.World;

namespace Vitomir.Engine.Save;

/// <summary>Implemented by the script layer (<c>Vitomir.Scripting.CodexScenes</c>).</summary>
public interface IScriptPersistence
{
    /// <summary>
    /// What the scripts of <paramref name="place"/> — the place being played, or one kept loaded —
    /// have to keep. <paramref name="thingKey"/> names a thing by its GUID; <paramref name="looseKey"/>
    /// names an effect's or a discipline's script object.
    /// </summary>
    PlaceScripts Save(Location place, Func<int, string?> thingKey, Func<object, string?> looseKey);

    /// <summary>One engine-made script's own state — an effect's or a discipline's — or null when its class keeps none.</summary>
    ScriptSave? SaveObject(object script);

    /// <summary>The chronicle's own scripts, by class.</summary>
    Dictionary<string, ScriptSave> SaveChronicle();

    /// <summary>
    /// Arms a restore: the next time <paramref name="place"/> is entered its scripts are built from
    /// these instead of started fresh — no `created`, no `effectstarted` — and `beginscene` comes
    /// after all of it, as it does in 1.1.
    /// </summary>
    void Arm(Location place, ScriptRestore restore);

    /// <summary>The chronicle scripts' saved states, given back the next time a place is entered.</summary>
    void ArmChronicle(IReadOnlyDictionary<string, ScriptSave> saved);

    /// <summary>
    /// The weather a script asked for (`SetWeatherEffect`) — the chronicle's field, which a save keeps
    /// and a load puts back before the scene begins (<see cref="ChronicleSave.Weather"/>).
    /// </summary>
    string WeatherEffect { get; set; }

    /// <summary>
    /// Forgets every script there is — the place being played, every kept place, the engine-made
    /// objects, the chronicle's — which is what a load does before it builds the saved game.
    /// </summary>
    void Reset();
}

/// <summary>What a place's scripts keep, as <see cref="IScriptPersistence.Save"/> answers it.</summary>
public sealed class PlaceScripts
{
    /// <summary>Each thing's script state, by the thing's GUID. A thing whose script keeps nothing is absent.</summary>
    public Dictionary<int, ScriptSave> ByThing { get; } = [];
    /// <summary>The timers that have not fired, their scripts named by key.</summary>
    public List<TimerSave> Timers { get; } = [];
    /// <summary>Every capture, by the GUID of the thing captured.</summary>
    public List<(int ThingGuid, CaptureSave Capture)> Captures { get; } = [];
    /// <summary>The scripts' clock, in milliseconds.</summary>
    public long ClockMs { get; set; }
}

/// <summary>What a load hands the script layer for one place (<see cref="IScriptPersistence.Arm"/>).</summary>
public sealed class ScriptRestore
{
    /// <summary>Each thing's saved script state, by the GUID the thing has now.</summary>
    public Dictionary<int, ScriptSave> ByThing { get; } = [];
    /// <summary>The saved state of an engine-made script object, asked for once it has been made again.</summary>
    public Func<object, ScriptSave?>? Loose { get; set; }
    /// <summary>A script key that names a thing's script, turned into that thing's GUID (0 when it names none).</summary>
    public Func<string, int>? ThingOf { get; set; }
    /// <summary>A script key that names an engine-made script, turned into the object.</summary>
    public Func<string, object?>? LooseOf { get; set; }
    public List<TimerSave> Timers { get; } = [];
    public List<(int ThingGuid, CaptureSave Capture)> Captures { get; } = [];
    public long ClockMs { get; set; }
}
