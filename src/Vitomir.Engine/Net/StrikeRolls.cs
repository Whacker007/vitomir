// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The war cry on a player's machine: the strike's roll on
// each machine's own rand().
using System.Runtime.CompilerServices;
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **What a strike's war cry is rolled on**. The strike's tail draws the engine's one `rand()`
/// against 0.5 and, under it, plays `vox_attack` with the sound's last argument 0 — this
/// machine's own sound, which a host does not relay. Every player's machine replays each strike it is
/// sent (message 0xc), so each rolls and cries on its own `rand()`: the
/// host's character's strikes, the other players', and its own. Vitomir rolled it on the place's minds
/// (<see cref="Minds.Random"/>), which a player's machine does not have — it thinks nothing (`--no-ai`,
/// the host carries out its orders) — so it played no war cry at all. A player's machine now rolls on the launch's fight
/// stream (<see cref="Streams.Combat"/> — the one its other replayed rolls draw on,
/// `ClientSeen`), and a test, which starts no launch, on the place's own generator at 1, as every
/// subsystem's default is. Everywhere else the roll is the minds', as before. **Ours**: which of the
/// launch's streams — 1.1 has one `rand()` for everything.
/// </summary>
public static class StrikeRolls
{
    static readonly ConditionalWeakTable<Location, Rng> own = new();

    /// <summary>The generator a strike by <paramref name="thing"/> rolls its war cry on, or null when it is in no place.</summary>
    public static Rng? For(WorldThing thing)
    {
        if (thing.Place is not { } place) return null;
        if (place.Minds?.Random is { } minds) return minds;
        if (NetOrders.Mode(place) != NetMode.Client) return null;               // a place nobody thinks in: no roll (as before)
        return Chance.Game?.Combat ?? own.GetValue(place, _ => new Rng(1));
    }
}
