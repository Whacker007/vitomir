// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A player's machine puts on and takes off only what the host sends** (the effect's add and removal;
/// the effects' and the items' scripts on a player's machine). Both
/// answer 0 at once on a network client unless the call is the network's own — the effect message's (on and
/// off) or the whole list's clear — so the engine's own callers there are refused as a script's are:
/// an effect put on by name, one taken off, one run out in the tick, a revive's clear. The
/// host's word puts on and takes off as before.
/// </summary>
public class NetEffectGateTests
{
    sealed class Sink : IOrderSink
    {
        public void Send(NetOrder order) { }
        public void Send(NetCast cast) { }
        public void Send(PackGesture move) { }
    }

    [Fact]
    public void On_a_player_s_machine_only_the_host_s_word_puts_an_effect_on_or_takes_one_off()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var effects = new EffectDefinitions(files);
        place.Awaken(new Ai.AiClasses(files), 1, effects, new DisciplineDefinitions(files));
        var who = place.Player!.Thing;
        var potence = effects["ef_disc_potence"]!;

        NetOrders.Attach(place, NetMode.Client, new Sink());
        try
        {
            // The engine's own put-on, not from the host: refused.
            Assert.Null(EffectRuntime.Apply(who, "ef_disc_potence", 1, durationMs: 1000));
            Assert.DoesNotContain(who.Effects.All, e => e.Name == "ef_disc_potence");

            // The host's (with its network flag 1): put on.
            var e = EffectRuntime.Apply(who, potence, 1, place.Milliseconds, 1000, network: true);
            Assert.NotNull(e);

            // Taken off here, not by the host: refused, and it stays on.
            Assert.False(EffectRuntime.Remove(who, e!));
            Assert.False(e!.Spent);
            // Run out: the tick's take-off  is refused too — on until the host says.
            EffectRuntime.Tick(who, place.Milliseconds + 5000, new Ai.Rng(1));
            Assert.False(e.Spent);
            Assert.Contains(who.Effects.All, x => ReferenceEquals(x, e));
            // A revive's or a death's clear: refused.
            EffectRuntime.RemoveAll(who);
            Assert.False(e.Spent);

            // The host's end (with its network flag 1): off.
            Assert.True(EffectRuntime.Remove(who, e, notify: false, network: true));
            Assert.True(e.Spent);

            // The whole list's clear (which passes the network's 1) is not refused.
            var again = EffectRuntime.Apply(who, potence, 1, place.Milliseconds, 0, network: true)!;
            EffectRuntime.RemoveAll(who, worn: true);
            Assert.True(again.Spent);
        }
        finally { NetOrders.Detach(place); }

        // Single player: as ever.
        var mine = EffectRuntime.Apply(who, "ef_disc_potence", 1, durationMs: 1000);
        Assert.NotNull(mine);
        Assert.True(EffectRuntime.Remove(who, mine!));
    }
}
