// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The half of the peds' ready test that is about the body rather than the mind (the
// wander and the flier). Found by a comparison with 1.1 and
// built.
namespace Vitomir.Engine.Ai;

public sealed partial class Minds
{
    /// <summary>
    /// **The body's half of the peds' ready test** — `pedwander`'s and
    /// `pedfly`'s, the same five tests in the same order: not walking,
    /// the busy stamp run out, not dead (flag `0x10000000`), not restrained
    /// (second flag word `0x80`), and in a place. Only then is a spot rolled.
    ///
    /// The mind's half was always there (<c>Ready</c>: walking, the AI-written busy time, the
    /// pause); this adds the actor's — the other half of the busy time (a swing, a cast, an order still
    /// playing), death and restraint. Earlier, a wanderer with teeth in its neck rolled a spot every
    /// think and the move order refused it, so each wander think of a feeding victim took one roll
    /// more off the stream than 1.1's; nothing visible, but every roll after it moved.
    /// </summary>
    bool PedFree(Mind mind) =>
        mind.Thing.Actor is not { } me
        || (me.BusyUntil <= Now && !me.Has(World.Actor.Flag.Dead) && !me.Has2(World.Actor.Flag2.Restrained));
}
