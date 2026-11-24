// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Core;

internal interface IEventQueue
{
    void TurnOver();
}

/// <summary>
/// A typed, ordered queue of events. What is published during a tick can be read for
/// the rest of that tick (by later phases) and through the whole next tick (by earlier ones), then it is
/// gone; so every system sees every event once, whichever phase it runs in, if it reads each tick.
/// Order is publication order.
/// </summary>
public sealed class EventQueue<T> : IEventQueue where T : struct
{
    // Room for a few events a tick from the start, so the first tick that
    // publishes one does not grow the list mid-game (the per-tick allocation
    // tests count that growth).
    List<T> current = new(4), previous = new(4);

    public void Publish(in T e) => current.Add(e);

    /// <summary>This tick's events so far.</summary>
    public IReadOnlyList<T> ThisTick => current;

    /// <summary>Last tick's events.</summary>
    public IReadOnlyList<T> LastTick => previous;

    /// <summary>Last tick's then this tick's, in order.</summary>
    public IEnumerable<T> All => previous.Concat(current);

    void IEventQueue.TurnOver()
    {
        (previous, current) = (current, previous);
        current.Clear();
    }
}
