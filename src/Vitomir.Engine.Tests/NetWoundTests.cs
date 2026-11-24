// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Two things a network game does not do** (a review of the docs found them): the wound penalties are
/// laid only on player things (type 7 or 0), so an actor on a
/// host keeps its full stride and strength however hurt; and **a player's machine tests no region** (the region
/// test skips a network client), so nobody there is heard entering or leaving one — the host's regions and
/// scripts are the game's. Single player is unchanged in both.
/// </summary>
public class NetWoundTests
{
    [Fact]
    public void In_a_network_game_only_player_things_take_the_wound_penalties()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        place.Awaken(new Ai.AiClasses(files), 1, new EffectDefinitions(files));
        var christof = place.Player!.Thing;
        Assert.Equal(ThingType.Player, christof.Type);
        var other = place.Things.First(t => t.Type == ThingType.Actor && t.Actor is { Dead: false } && t.InWorld);

        NetOrders.Attach(place, NetMode.Server);
        try
        {
            Wounds.Apply(other, 2, frenzied: false);
            Assert.Null(other.Effects.Find("ef_dmg_dexterity"));
            Assert.Null(other.Effects.Find("ef_dmg_movespeed"));
            Wounds.Apply(christof, 2, frenzied: false);
            Assert.NotNull(christof.Effects.Find("ef_dmg_dexterity"));
        }
        finally { NetOrders.Detach(place); }

        // Single player: anybody.
        Wounds.Apply(other, 2, frenzied: false);
        Assert.NotNull(other.Effects.Find("ef_dmg_dexterity"));
        Assert.NotNull(other.Effects.Find("ef_dmg_movespeed"));

        // And a network game does not take them off either: the wound update returns before its removals.
        NetOrders.Attach(place, NetMode.Client);
        try
        {
            Wounds.Apply(other, 0, frenzied: false);
            Assert.NotNull(other.Effects.Find("ef_dmg_dexterity"));
        }
        finally { NetOrders.Detach(place); }
    }

    [Fact]
    public void A_players_machine_hears_nobody_enter_a_region()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var christof = place.Player!.Thing;
        var region = place.Things.First(t => t.Type == ThingType.Region && (t.RegionFlags & Regions.NoTracking) == 0
                                             && t.Scene.Model is { Planes.Length: > 0 }
                                             && Regions.Contains(t, t.Position));
        var start = christof.Position;
        var seen = new List<string>();
        place.Regions!.Entered = (r, who) => seen.Add($"in {r.Guid}");

        NetOrders.Attach(place, NetMode.Client);
        try
        {
            place.Move(christof, region.Position);
            Assert.Empty(seen);
            Assert.Equal(0u, christof.InRegions);                                   // no bit set there either
        }
        finally { NetOrders.Detach(place); }

        place.Move(christof, start);
        place.Move(christof, region.Position);
        Assert.Equal([$"in {region.Guid}"], seen);                                   // single player hears it
    }
}
