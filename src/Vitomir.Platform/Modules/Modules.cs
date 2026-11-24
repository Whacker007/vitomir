// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Modules;

/// <summary>A module's name and versions.</summary>
public sealed record ModuleInfo(string Id, string Version, string FormulaVersion);

/// <summary>
/// The stamp every trace carries (the rules package's <c>ModuleStamp</c>): which module and profile
/// decided, which version of its formulae, and whether it ran as non-shipping research (the sandbox).
/// </summary>
public sealed record ModuleStamp(string ModuleId, string ProfileId, string FormulaVersion, bool NonshippingResearch, string ConfigHash);

/// <summary>
/// A module refusing to run (design principle 8: fail closed): its configuration names something it
/// will not run with — an unresolved value in a shipping profile, a sandbox not switched on, a malformed
/// file — and every reason is listed.
/// </summary>
public sealed class ModuleRefusedException(string moduleId, IReadOnlyList<string> reasons)
    : Exception($"module {moduleId} refused to start: {string.Join("; ", reasons)}")
{
    public string ModuleId { get; } = moduleId;
    public IReadOnlyList<string> Reasons { get; } = reasons;

    /// <summary>What the player can do about it, for the program to show after the reasons (the loader names the switches the module's manifest declares), or empty.</summary>
    public string Hint { get; init; } = "";
}

/// <summary>
/// What the loader tells a module before it registers: its data and assets, read by id through the registry
/// (<see cref="ModuleAssets"/>: its own ids, its dependencies' and 1.1's; never a path), and the switches and
/// profile it was started with. (Earlier, it was given a folder, <c>DataFolder</c>; a module reads only by
/// id now, so its folder is read only to everything else and an override can stand in for any file.)
/// </summary>
public sealed class ModuleConfigContext
{
    /// <summary>The module's view of the assets: <c>Assets.Own("profiles/x")</c> is <c>&lt;its id&gt;:profiles/x</c>.</summary>
    public required ModuleAssets Assets { get; init; }

    /// <summary>The switches given on the command line that its manifest declares (for example <c>sandbox</c>).</summary>
    public IReadOnlySet<string> Switches { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The profile asked for by name, or null for the module's default.</summary>
    public string? Profile { get; init; }
}

/// <summary>
/// A game module: the rules of one game on the platform. It reads and checks its own
/// data in <see cref="Configure"/> (throwing <see cref="ModuleRefusedException"/> to refuse), then adds its
/// components, systems, events and services to the world in <see cref="Register"/>. It never draws and
/// never references the renderer.
/// </summary>
public interface IModule
{
    ModuleInfo Info { get; }

    /// <summary>Reads and validates the module's data; throws <see cref="ModuleRefusedException"/> to refuse.</summary>
    void Configure(ModuleConfigContext context);

    /// <summary>The stamp of the configuration <see cref="Configure"/> settled on.</summary>
    ModuleStamp Stamp { get; }

    /// <summary>
    /// A line the program must show on screen while the module runs as configured (the enhanced module's research
    /// sandbox says so), or null. The program knows a module only through this interface.
    /// </summary>
    string? Banner => null;

    /// <summary>
    /// The module's own frame loop (<see cref="IGameLoop"/>), or null for a module whose game runs in the platform's
    /// world (the enhanced game: <see cref="Register"/> and the program's fixed ticks, unchanged). A game whose module offers
    /// one is run by the program frame by frame, each frame given its wall time; such a module registers nothing.
    /// </summary>
    IGameLoop? Loop => null;

    void Register(WorldBuilder world);
}

/// <summary>
/// Builds a <see cref="World"/>: the platform's services and every module register their component
/// stores, events, services and systems here, then <see cref="Build"/> fixes the schedule. Registration
/// order is the digest's order, so it must itself be fixed (it is: the program registers the platform,
/// then the modules in the game's order).
/// </summary>
public sealed class WorldBuilder
{
    readonly World world;
    bool built;

    public WorldBuilder(ulong seed)
    {
        world = new World(seed);
        People = new PeopleHooks();
        world.AddService(People);
    }

    /// <summary>The hooks by which modules dress the people the program makes (also the world's service).</summary>
    public PeopleHooks People { get; }

    public WorldBuilder Component<T>(bool digested = true) where T : struct
    {
        Check();
        world.AddStore(new Store<T>(digested));
        return this;
    }

    public WorldBuilder Event<T>() where T : struct
    {
        Check();
        world.AddService(new EventQueue<T>());
        return this;
    }

    public WorldBuilder Service<T>(T service) where T : class
    {
        Check();
        world.AddService(service);
        return this;
    }

    public WorldBuilder System(Phase phase, int order, string name, ISystem system)
    {
        Check();
        world.Schedule.Add(phase, order, name, system);
        return this;
    }

    public WorldBuilder System(Phase phase, int order, string name, Action<World> run) =>
        System(phase, order, name, new DelegateSystem(run));

    /// <summary>The world as registered so far, for a service that needs it before the build.</summary>
    public World Peek => world;

    public World Build()
    {
        Check();
        built = true;
        _ = world.Schedule.Order;
        return world;
    }

    /// <summary>The systems in their run order, for diagnostics and tests.</summary>
    public static IReadOnlyList<string> RunOrder(World world) =>
        [.. world.Schedule.Order.Select(e => $"{e.Phase}/{e.Order}/{e.Name}")];

    void Check()
    {
        if (built) throw new InvalidOperationException("the world is already built");
    }
}
