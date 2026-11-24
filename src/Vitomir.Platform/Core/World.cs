// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Core;

/// <summary>
/// The simulation's world: its entities, the component stores that hold everything
/// they are, its services (the level's space, the random streams, the event queues …), and the schedule
/// of systems run on every tick. The world steps only by whole ticks of <see cref="TickSeconds"/>; it
/// never reads a clock. Built by a <see cref="WorldBuilder"/>.
/// </summary>
public sealed class World
{
    /// <summary>The simulation's rate: 120 ticks a second.</summary>
    public const int TicksPerSecond = 120;

    /// <summary>One tick, in seconds.</summary>
    public const float TickSeconds = 1f / TicksPerSecond;

    readonly List<int> generations = [];
    readonly List<bool> alive = [];
    readonly Stack<int> free = new();
    readonly List<EntityId> doomed = [];
    readonly Dictionary<Type, IStore> stores = [];
    readonly List<IStore> storeOrder = [];
    readonly Dictionary<Type, object> services = [];
    readonly List<IEventQueue> queues = [];
    readonly List<IDigestedService> digestedServices = [];
    internal Schedule Schedule { get; } = new();

    internal World(ulong seed)
    {
        Random = new RandomStreams(seed);
    }

    /// <summary>Ticks stepped since the world began.</summary>
    public long Tick { get; private set; }

    /// <summary>Seconds of simulation since the world began.</summary>
    public double Seconds => Tick / (double)TicksPerSecond;

    public RandomStreams Random { get; }

    /// <summary>Entities alive now.</summary>
    public int EntityCount => generations.Count - free.Count;

    // ---- entities ----

    public EntityId Create()
    {
        if (free.Count > 0)
        {
            int index = free.Pop();
            alive[index] = true;
            return new EntityId(index, generations[index]);
        }
        generations.Add(1);
        alive.Add(true);
        return new EntityId(generations.Count - 1, 1);
    }

    public bool Alive(EntityId e) =>
        e.Index >= 0 && e.Index < generations.Count && generations[e.Index] == e.Generation && alive[e.Index];

    /// <summary>
    /// Marks an entity to be destroyed at the end of this tick (after the Post phase), so no system meets
    /// a hole while another is walking a store. Its components go, and its slot's generation moves on.
    /// </summary>
    public void Destroy(EntityId e)
    {
        if (Alive(e) && !doomed.Contains(e)) doomed.Add(e);
    }

    void Reap()
    {
        foreach (var e in doomed)
        {
            foreach (var s in storeOrder) s.Remove(e);
            generations[e.Index]++;
            alive[e.Index] = false;
            free.Push(e.Index);
        }
        doomed.Clear();
    }

    // ---- components ----

    internal void AddStore<T>(Store<T> store) where T : struct
    {
        if (stores.ContainsKey(typeof(T))) throw new InvalidOperationException($"{typeof(T).Name} is registered twice");
        stores[typeof(T)] = store;
        storeOrder.Add(store);
    }

    /// <summary>The store of a registered component type.</summary>
    public Store<T> Store<T>() where T : struct =>
        stores.TryGetValue(typeof(T), out var s) ? (Store<T>)s : throw new InvalidOperationException($"{typeof(T).Name} is not a registered component");

    public bool Has<T>(EntityId e) where T : struct => Store<T>().Has(e);

    /// <summary>The store of a component type, when the world registers it (no exception, no allocation).</summary>
    public bool TryStore<T>(out Store<T> store) where T : struct
    {
        if (stores.TryGetValue(typeof(T), out var s)) { store = (Store<T>)s; return true; }
        store = null!;
        return false;
    }

    public ref T Get<T>(EntityId e) where T : struct => ref Store<T>().Get(e);

    public ref T Set<T>(EntityId e, in T value) where T : struct
    {
        if (!Alive(e)) throw new InvalidOperationException($"{e} is not alive");
        return ref Store<T>().Set(e, value);
    }

    public void Remove<T>(EntityId e) where T : struct => Store<T>().Remove(e);

    /// <summary>The registered stores in registration order.</summary>
    public IReadOnlyList<IStore> Stores => storeOrder;

    // ---- services ----

    internal void AddService<T>(T service) where T : class
    {
        if (services.ContainsKey(typeof(T))) throw new InvalidOperationException($"service {typeof(T).Name} is registered twice");
        services[typeof(T)] = service;
        if (service is IEventQueue q) queues.Add(q);
        if (service is IDigestedService d) digestedServices.Add(d);
    }

    public T Service<T>() where T : class =>
        services.TryGetValue(typeof(T), out var s) ? (T)s : throw new InvalidOperationException($"service {typeof(T).Name} is not registered");

    public bool TryService<T>(out T service) where T : class
    {
        if (services.TryGetValue(typeof(T), out var s)) { service = (T)s; return true; }
        service = null!;
        return false;
    }

    /// <summary>The queue of an event type registered with <see cref="WorldBuilder.Event{T}"/>.</summary>
    public EventQueue<T> Events<T>() where T : struct => Service<EventQueue<T>>();

    // ---- stepping ----

    /// <summary>
    /// One tick: the event queues turn over (last tick's events stay readable for one more tick), every
    /// system runs in its phase and place, the doomed entities go, and the tick count moves on.
    /// </summary>
    public void Step()
    {
        foreach (var q in queues) q.TurnOver();
        Schedule.Run(this);
        Reap();
        Tick++;
    }

    /// <summary>
    /// The digest of the world's simulation state now: the tick, every digested store's live entities and
    /// bytes, then every <see cref="IDigestedService"/>'s own state, each in registration order. Two runs from the same start and inputs give the same digest at
    /// every tick, on any machine.
    /// </summary>
    public ulong Digest()
    {
        var h = new Fnv1a64();
        h.Add(Tick);
        foreach (var s in storeOrder)
        {
            if (!s.Digested) continue;
            h.Add(s.ComponentType.FullName ?? s.ComponentType.Name);
            s.Digest(ref h);
        }
        foreach (var d in digestedServices)
        {
            h.Add(d.GetType().FullName ?? d.GetType().Name);
            d.Digest(ref h);
        }
        return h.Value;
    }
}

/// <summary>
/// A service that holds simulation state outside the component stores (data of variable size, such as a
/// module's actor sheets) and so must feed it to the world's digest itself, in a fixed order.
/// </summary>
public interface IDigestedService
{
    void Digest(ref Fnv1a64 hash);
}
