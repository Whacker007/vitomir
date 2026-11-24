// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using static Vitomir.Engine.Tests.NetShopsTests;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The war cry on the players' machines** (`Net.StrikeRolls`, the strike's foley, replayed
/// on each machine). 1.1 rolls a strike's `vox_attack` on each machine's own `rand()` and plays it there; a
/// player's machine replays every strike it is sent, so it rolls one for each. Vitomir's player's machine
/// has no minds to roll on and played none; it rolls now — on the launch's fight stream in a game, on the
/// place's own generator at 1 here (a test starts no launch) — once for each strike, as the host rolls
/// its own on its minds. The cry itself is heard only where there is sound; the roll is what is pinned.
/// </summary>
public class NetWarCryTests
{
    [Fact]
    public void A_players_machine_rolls_its_own_war_cry_for_each_replayed_strike()
    {
        using var files = Install.Open();
        using var game = Host(files);
        using var anna = Join(files, game, "Anna", "MP_Serena");
        using var bob = Join(files, game, "Bob", "MP_Erik");
        void All() { game.Step(); anna.Step(); bob.Step(); }
        Assert.True(Until(() => anna.Client.Avatar is not null && bob.Client.Avatar is not null, All), "never played");
        var his = Body(game, "Bob");
        var her = Body(game, "Anna");
        Assert.True(Until(() => anna.Client.Mirror?.Find(his.Guid) is not null, All), "never met");
        var hisThere = anna.Client.Mirror!.Find(his.Guid)!;
        her.Actor!.MaxHealth = her.Actor.Health = 10_000f;

        // Her machine has no minds, but a generator for his strikes: its own, at 1, with nothing drawn yet.
        Assert.Null(hisThere.Place!.Minds);
        var dice = StrikeRolls.For(hisThere);
        Assert.NotNull(dice);
        Assert.Same(dice, StrikeRolls.For(anna.Client.Avatar!));             // one a place
        Assert.Same(game.Place.Minds?.Random, StrikeRolls.For(his));        // the host's are its minds', as before

        var weapon = his.Actor!.Weapon ?? Weapon.From("fist", game.Place.Templates!.Resolve("fist"));
        Assert.NotNull(weapon);
        Actions.Strike(his, her, weapon!, game.Place.Milliseconds, game.Place.Motions);
        Assert.True(Until(() => anna.Said.Any(s => s.StartsWith("strike:")), All), "her machine never struck it again");

        // Exactly one roll drawn for the replayed strike: the next is the second of a stream at 1.
        var fresh = new Rng(1);
        fresh.Next();
        Assert.Equal(fresh.Next(), dice!.Next());
    }

    [Fact]
    public void A_place_nobody_thinks_in_outside_a_network_game_rolls_no_war_cry()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_INN4", things: false, sky: false, player: "Christof");
        Assert.Null(place.Minds);
        Assert.Null(StrikeRolls.For(place.Player!.Thing));
    }
}
