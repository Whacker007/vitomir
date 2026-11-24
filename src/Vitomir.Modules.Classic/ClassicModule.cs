// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Classic;

/// <summary>
/// **The Classic module**: 1.1, the base game, as a module on the platform — release 1.
/// Its rules, story and simulation are Vitomir's remake of 1.1 as it stands (<c>Vitomir.Engine</c> and
/// <c>Vitomir.Scripting</c>, shipped in its <c>bin\</c>), so it registers nothing in the platform's world: it offers its own
/// frame loop (<see cref="ClassicLoop"/>), which the program runs frame by frame with each frame's wall time. It reads no
/// data of its own through the registry yet (1.1's files are the player's install, given to the loop). It never draws:
/// it runs headless only.
/// </summary>
public sealed class ClassicModule : IModule
{
    /// <summary>The module's version, as its <c>module.json</c> says.</summary>
    public const string Version = "0.1.0";

    /// <summary>What its stamp names as its formulae: the original game's, version 1.1.</summary>
    public const string Formulae = "1.1";

    ClassicLoop? loop;

    public ModuleInfo Info { get; } = new("classic", Version, Formulae);

    public ModuleStamp Stamp { get; private set; } = new("classic", "1.1", Formulae, false, "");

    /// <summary>Nothing to read or refuse yet: the game's files are the player's 1.1 install, which the loop is given.</summary>
    public void Configure(ModuleConfigContext context) => Stamp = new("classic", context.Profile ?? "1.1", Formulae, false, "");

    /// <summary>1.1 keeps its own time and its own world: nothing goes into the platform's (<see cref="Loop"/>).</summary>
    public void Register(WorldBuilder world) { }

    /// <summary>The game's frame loop; one for the module's life (one Classic per process: the engine's statics).</summary>
    public IGameLoop? Loop => loop ??= new ClassicLoop();
}
