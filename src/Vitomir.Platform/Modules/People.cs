// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Modules;

/// <summary>What a person the program makes is to the game: the one the player drives, or one of the townsfolk.</summary>
public enum PersonRole { Player, Townsperson }

/// <summary>
/// The extension point by which modules dress the people the program makes (ours): the program makes a
/// body (the player at a start, the townsfolk round it) and says so here; each module that registered a handler
/// gives it what its rules need (the enhanced module: an actor sheet). The program so needs to know no module's
/// types. Handlers run at once, in the order the modules registered them (the game's load order), so the world
/// comes out the same as when the program gave the sheets itself, digest for digest. Registered by
/// every <see cref="WorldBuilder"/> (<see cref="WorldBuilder.People"/>) and a service of the world it builds.
/// </summary>
public sealed class PeopleHooks
{
    readonly List<Action<EntityId, PersonRole, string?>> handlers = [];

    /// <summary>Adds a handler, run for every person made from now on.</summary>
    public void OnMade(Action<EntityId, PersonRole> handler) => handlers.Add((e, role, _) => handler(e, role));

    /// <summary>
    /// Adds a handler that is also told the character the person is drawn as: a 1.1 catalogue or template name
    /// (<c>Christof</c>, <c>rudolfGuard</c>, <c>KnightOfStJohn_torch</c>), or null when the program has none. The enhanced
    /// module picks a fighter's sheet by it.
    /// </summary>
    public void OnMade(Action<EntityId, PersonRole, string?> handler) => handlers.Add(handler);

    /// <summary>Says that <paramref name="person"/> was made as <paramref name="role"/>, drawn as <paramref name="character"/> (null for none): every handler runs, in order.</summary>
    public void Made(EntityId person, PersonRole role, string? character = null)
    {
        for (int i = 0; i < handlers.Count; i++) handlers[i](person, role, character);
    }
}
