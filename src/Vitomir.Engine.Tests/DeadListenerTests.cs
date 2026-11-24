// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The dead still hear**. The think's gate turns a dead character away, but nothing
/// on the way to an event does: the broadcast delivers to every thing in the sector flood
/// with a mind (asked with no dead filter), and the event switch asks nothing of the
/// listener but that it has a thing — so event 14 makes a dead pedestrian at heart afraid of the one who
/// struck, as it does a live one. In 1.1's Monastery
/// the two halfcorpses, lying dead from the start (health 0,
/// `THING_AF_DEAD`), go from `pedwander` to `afraid` and swap what they fear again and again in a
/// fight — the Cappadocian, Christof, the corpse, a rat — each at the moment that one strikes. Vitomir
/// already did the same (a replay of such a fight starts from a save where the halfcorpse
/// is alive, so this pins it instead).
/// </summary>
public class DeadListenerTests
{
    [Fact]
    public void A_dead_pedestrian_is_frightened_of_whoever_strikes_near_it()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1);
        var christof = place.Steered!;
        bool Frightenable(Mind m) => m.BaseMode == AiMode.PedWander && m.Class.MindFlags == 0
                                     && (m.Flags & Mind.FlagUnaffected) == 0 && m.Target is null;
        var near = minds.All.Where(m => Frightenable(m)
                                        && Vector3.Distance(m.Thing.Position, christof.Position) < Minds.AssaultShoutRange)
                            .OrderBy(m => Vector3.Distance(m.Thing.Position, christof.Position)).ToList();
        Assert.True(near.Count >= 2);
        var victim = near[0];
        var body = near[1];
        body.Thing.Actor!.Health = 0f;
        body.Thing.Actor.Flags |= Actor.Flag.Dead;

        minds.AttackedBy(victim, christof);

        Assert.Equal(AiMode.Afraid, body.Mode);
        Assert.Same(christof, body.Target);
    }
}
