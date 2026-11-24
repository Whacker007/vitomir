// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// `misc/effects.ned`, loaded once for the game. It is the RPG system's data layer — 187 effects,
/// everything a discipline, a weapon, a poison or a wound does — and the one file that says what
/// each of the 36 effect types means.
/// </summary>
public sealed class EffectDefinitions
{
    /// <summary>Where the game keeps it. There is exactly one `.ned` in the whole install.</summary>
    public const string Path = "misc/effects.ned";

    public NedFile? File { get; }

    public EffectDefinitions(GameFiles files)
    {
        var log = Diag.For<EffectDefinitions>();
        if (!files.TryRead(Path, out var data)) { log.LogWarning("no {Path} in this install", Path); return; }
        File = NedFile.Read(data, Path);
        foreach (var complaint in File.Dropped) log.LogWarning("{Complaint}", complaint);
        log.LogInformation("effects: {Count} definitions from {Path}", File.All.Count, Path);
    }

    /// <summary>An effect by name, or null.</summary>
    public NedEffect? this[string name] => File?[name];

    /// <summary>What a guard puts on somebody it has decided is a criminal.</summary>
    public NedEffect? Wanted => this["ef_wanted"];
}
