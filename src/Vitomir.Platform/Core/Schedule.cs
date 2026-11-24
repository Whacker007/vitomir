// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Core;

/// <summary>
/// The phases of a tick, in the order they run. The platform and every module put
/// their systems into these; within a phase a system's place is its order number, then its name.
/// </summary>
public enum Phase
{
    /// <summary>The tick's recorded actions turned into intents (where the controlled body wants to go, what it wants to do).</summary>
    Input,
    /// <summary>AI and the modules' decisions: where the others want to go and what they want to do.</summary>
    Think,
    /// <summary>Actions admitted or refused, their phases (prepare, active, recover) advanced.</summary>
    Act,
    /// <summary>Bodies moved through the level.</summary>
    Move,
    /// <summary>Blows and missiles swept, overlaps found: the evidence of what met what.</summary>
    Contact,
    /// <summary>The modules' rules applied to the evidence.</summary>
    Resolve,
    /// <summary>Who sees and hears whom.</summary>
    Perceive,
    /// <summary>Cameras, the reticle, clean-up, the snapshot for the picture.</summary>
    Post,
}

/// <summary>A system: one pass over the world in its phase, every tick.</summary>
public interface ISystem
{
    void Run(World world);
}

/// <summary>A system made from a delegate.</summary>
public sealed class DelegateSystem(Action<World> run) : ISystem
{
    public void Run(World world) => run(world);
}

/// <summary>The systems of a world, sorted once into their run order.</summary>
internal sealed class Schedule
{
    readonly List<Entry> entries = [];
    Entry[] order = [];
    bool dirty = true;

    internal readonly record struct Entry(Phase Phase, int Order, string Name, ISystem System);

    public void Add(Phase phase, int order, string name, ISystem system)
    {
        if (entries.Any(e => e.Name == name)) throw new InvalidOperationException($"a system named {name} is registered twice");
        entries.Add(new Entry(phase, order, name, system));
        dirty = true;
    }

    public IReadOnlyList<Entry> Order
    {
        get
        {
            if (dirty)
            {
                order = [.. entries.OrderBy(e => e.Phase).ThenBy(e => e.Order).ThenBy(e => e.Name, StringComparer.Ordinal)];
                dirty = false;
            }
            return order;
        }
    }

    public void Run(World world)
    {
        _ = Order;                                        // sorts once after a registration
        var run = order;                                  // the array itself: no enumerator allocated per tick
        for (int i = 0; i < run.Length; i++) run[i].System.Run(world);
    }
}
