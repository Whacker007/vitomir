// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Events 4 and 5 — one of my side hurt or killed by something I cannot notice:
/// in single player the hearer reaches for a sense discipline and thinks again in a second; in a
/// network game, host or client, 1.1 hears them and does nothing (the event handler tests
/// <c>*</c>). Pinned by the pass of a comparison with 1.1, which found Vitomir's
/// hearers reacting on a host too.
/// </summary>
public class UnseenEventTests
{
    [Fact]
    public void A_hurt_neighbour_sends_the_hearer_to_its_senses_in_single_player_only()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        static bool Party(Mind m) => m.Thing.Type == ThingType.Player && m.Thing.Actor?.Has(Actor.Flag.Party) == true;
        var others = minds.All.Where(m => m.Thing.Actor is not null && !Party(m) && !ReferenceEquals(m.Thing, minds.Steered)).ToList();
        var pair = (from a in others from b in others
                    where !ReferenceEquals(a, b) && Vector3.Distance(a.Thing.Position, b.Thing.Position) < 1500f
                    select (Sender: a, Hearer: b)).FirstOrDefault();
        Assert.SkipWhen(pair.Sender is null, "no two strangers within earshot in this scene");
        // Somebody nobody can notice: stealth far above anybody's perception.
        var by = place.Player!.Thing;
        by.Actor!.Stealth = 1000;

        pair.Hearer.NextThink = 0;
        minds.Damaged(pair.Sender, by, 1f);
        Assert.Equal(minds.Now + 1000, pair.Hearer.NextThink);

        NetOrders.Attach(place, NetMode.Server);
        try
        {
            pair.Hearer.NextThink = 0;
            minds.Damaged(pair.Sender, by, 1f);
            Assert.Equal(0, pair.Hearer.NextThink);
        }
        finally { NetOrders.Detach(place); }
    }
}
