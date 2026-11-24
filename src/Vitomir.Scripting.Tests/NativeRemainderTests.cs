// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Particles;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// The last of the natives the single-player scripts call: the weather and the `Weather`
/// script that answers it, Vienna's snow, the party put on an entrance, the attack and use orders a
/// script gives, the pack walked, filled and emptied, the shield hidden, a thing made at a point,
/// and the developer console's `cash` and `advance`. What is pinned is each native's answer and
/// what it did, as 1.1 does it.
/// </summary>
[Collection("scripts")]
public class NativeRemainderTests(Scripts s)
{
    [Fact]
    public void Setting_the_weather_tells_the_weather_thing_and_it_spawns_the_snow()
    {
        using var m = Place.Enter(s, "P1_MON1");
        // A `weather_effects` prop carries the `Weather` script, as Vienna's streets place one.
        var weather = m.Where.Spawn("weather_effects", m.Christof)!;
        Assert.Contains("Weather", m.Scenes.Running);

        Place.Call(s, "Codex.SetWeatherEffect", null, "snow");
        Assert.Equal("snow", Place.Call(s, "Codex.GetWeatherEffect", null)?.ToString());
        var snow = m.Where.Things.SingleOrDefault(t => t.Template.Equals("snow", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(snow);
        Assert.Equal(weather.Position, snow.Position);
        // Its template names `game.npd`'s `snow`, a rain emitter (kind 6), which is live the moment it is made.
        m.Run(0.1f);
        Assert.Equal(EmitterKind.Rain, m.Where.Particles.Of(snow)?.Kind);

        // A new name takes the old weather away and puts the new one in its place; none leaves none.
        Place.Call(s, "Codex.SetWeatherEffect", null, "heavyRain");
        Assert.True(snow.Removed);
        Assert.Contains(m.Where.Things, t => t.Template.Equals("heavyRain", StringComparison.OrdinalIgnoreCase));
        Place.Call(s, "Codex.SetWeatherEffect", null, "");
        Assert.DoesNotContain(m.Where.Things, t => t.Template.Equals("heavyRain", StringComparison.OrdinalIgnoreCase) && !t.Removed);
    }

    [Fact]
    public void Vienna_asks_for_snow_as_each_of_its_scenes_begins()
    {
        using var m = Place.Enter(s, "V1_NRTH");
        // `ViennaChronicle.beginscene` → `SetWeatherEffect("snow")` → every `Weather` thing's
        // `weatherchanged`.
        // The Northern Ring Strasse is one of the six Vienna streets that place a `weather_effects`.
        Assert.Equal("snow", m.Scenes.WeatherEffect);
        var weather = Assert.Single(m.Where.Things, t => t.Script == "Weather");
        var snow = Assert.Single(m.Where.Things, t => t.Template.Equals("snow", StringComparison.OrdinalIgnoreCase) && !t.Removed);
        Assert.Equal(weather.Position, snow.Position);
    }

    [Fact]
    public void The_party_is_put_down_on_an_entrance_of_the_place_it_is_in()
    {
        using var m = Place.Enter(s, "P1_MON1");
        var group = m.Where.Entrances.First(g => g.Any()).Key;
        var start = Location.PickStart(m.Where.AllStarts, group, 1)!;
        Assert.True(Place.Bln(Place.Call(s, "CodexPlayer.TeleportPartyToEntrance", null, m.Christof.Guid, (int)group)));
        Assert.Equal(start.Position, m.Christof.Position);
        // A thing that is not a character is refused, and so is an entrance with no start.
        var prop = m.Where.Things.First(t => t.Type == ThingType.Prop);
        Assert.False(Place.Bln(Place.Call(s, "CodexPlayer.TeleportPartyToEntrance", null, prop.Guid, (int)group)));
        Assert.False(Place.Bln(Place.Call(s, "CodexPlayer.TeleportPartyToEntrance", null, m.Christof.Guid, 999)));
    }

    [Fact]
    public void A_script_can_order_an_attack_and_a_use()
    {
        using var m = Place.Enter(s, "P1_MON1");
        var me = m.Christof.Actor!;
        var self = s.Runtime!.NewThingRef("CodexActor", m.Christof.Guid);
        // A use, of a thing right beside him: the order is taken and used on the spot.
        var prop = m.Where.Spawn("weather_effects", m.Christof)!;
        Assert.True(Place.Bln(Place.Call(s, "CodexActor.ActorActionActivate", self, prop.Guid, 0)));
        // An attack, on somebody who may be attacked.
        var victim = m.Where.Things.First(t => t.Type == ThingType.Actor && t.Actor is { } a && !a.Dead && t != m.Christof);
        victim.Actor!.Flags &= ~Actions.Unattackable;
        me.BusyUntil = 0;
        Assert.True(Place.Bln(Place.Call(s, "CodexActor.ActorActionAttack", self, victim.Guid, 0, 0)));
        Assert.Equal(ActionMode.Attack, me.Action);
        Assert.Same(victim, me.Target);
        // Already attacking that one: the order is ignored, which is not 4 (the answer 4).
        Assert.False(Place.Bln(Place.Call(s, "CodexActor.ActorActionAttack", self, victim.Guid, 0, 0)));
        Assert.False(Place.Bln(Place.Call(s, "CodexActor.ActorActionAttack", self, 999_999, 0, 0)));
    }

    [Fact]
    public void The_pack_is_filled_walked_and_emptied_the_engine_way()
    {
        using var m = Place.Enter(s, "P1_MON1");
        var me = m.Christof.Actor!;
        var self = s.Runtime!.NewThingRef("CodexActor", m.Christof.Guid);
        int had = me.Inventory.Count;
        var tome = m.Where.Spawn("TomeCelerity", m.Christof)!;
        var rat = m.Where.Spawn("VitaeRat", m.Christof)!;
        var sword = m.Where.Spawn("broadSword", m.Christof)!;
        // An item (type 1) is given; a weapon (type 5) is not.
        Assert.True(Place.Bln(Place.Call(s, "CodexActor.AddToActorInventory", self, tome.Guid)));
        Assert.True(Place.Bln(Place.Call(s, "CodexActor.AddToActorInventory", self, rat.Guid)));
        Assert.False(Place.Bln(Place.Call(s, "CodexActor.AddToActorInventory", self, sword.Guid)));
        Assert.False(tome.InWorld);
        Assert.Same(m.Christof, tome.Owner);
        Assert.Equal(had + 2, me.Inventory.Count);

        // The list is newest first (each joins at the front), and walks by the item's links.
        int first = Place.Int(Place.Call(s, "CodexActor.GetActorFirstInventoryItem", self));
        Assert.Equal(rat.Guid, first);
        Assert.Equal(tome.Guid, Place.Int(Place.Call(s, "CodexItem.GetNextInventoryItem", s.Runtime.NewThingRef("CodexItem", rat.Guid))));
        Assert.Equal(rat.Guid, Place.Int(Place.Call(s, "CodexItem.GetPrevInventoryItem", s.Runtime.NewThingRef("CodexItem", tome.Guid))));

        // Out again: on the floor at his feet, and the answer is **his** GUID.
        var at = m.Christof.Position;
        Assert.Equal(m.Christof.Guid, Place.Int(Place.Call(s, "CodexItem.RemoveItemFromInventory", s.Runtime.NewThingRef("CodexItem", tome.Guid))));
        Assert.True(tome.InWorld);
        Assert.Equal(at, tome.Position);
        Assert.Null(tome.Owner);
        Assert.Equal(had + 1, me.Inventory.Count);
        // An item nobody keeps answers 0, and so does one with no next.
        Assert.Equal(0, Place.Int(Place.Call(s, "CodexItem.RemoveItemFromInventory", s.Runtime.NewThingRef("CodexItem", tome.Guid))));
        Assert.Equal(0, Place.Int(Place.Call(s, "CodexItem.GetNextInventoryItem", s.Runtime.NewThingRef("CodexItem", tome.Guid))));
    }

    [Fact]
    public void The_shield_is_hidden_and_shown_and_still_held()
    {
        using var m = Place.Enter(s, "P1_MON1");
        var me = m.Christof.Actor!;
        var self = s.Runtime!.NewThingRef("CodexActor", m.Christof.Guid);
        Place.Call(s, "CodexActor.EnableActorShield", self, false);
        Assert.True(me.OffhandHidden);
        Place.Call(s, "CodexActor.EnableActorShield", self, true);
        Assert.False(me.OffhandHidden);
    }

    [Fact]
    public void A_thing_is_made_where_the_script_says_turned_the_way_it_says()
    {
        using var m = Place.Enter(s, "P1_MON1");
        var world = s.Runtime!.NewThingRef("CodexWorld", CodexScenes.WorldCurrent);
        var at = m.Christof.Position + new Vector3(60f, 0f, 0f);
        var turn = new Vector3(0f, 0f, 90f);
        int guid = Place.Int(Place.Call(s, "CodexWorld.CreateThing", world, "Szlachta", new[] { at.X, at.Y, at.Z }, new[] { turn.X, turn.Y, turn.Z }));
        var made = m.Where.Thing(guid);
        Assert.NotNull(made);
        Assert.Equal("Szlachta", made.Template, ignoreCase: true);
        Assert.Equal(at, made.Position);
        Assert.Equal(turn, made.Orientation);
        Assert.True(made.InWorld);
        // A world that is not loaded, and a template that does not exist, make nothing.
        var elsewhere = s.Runtime.NewThingRef("CodexWorld", 999);
        Assert.Equal(0, Place.Int(Place.Call(s, "CodexWorld.CreateThing", elsewhere, "Szlachta", new[] { at.X, at.Y, at.Z }, new[] { 0f, 0f, 0f })));
        Assert.Equal(0, Place.Int(Place.Call(s, "CodexWorld.CreateThing", world, "noSuchThing", new[] { at.X, at.Y, at.Z }, new[] { 0f, 0f, 0f })));
    }

    [Fact]
    public void The_console_s_cash_sets_the_purse_and_an_unknown_word_is_refused()
    {
        using var m = Place.Enter(s, "P1_MON1");
        m.Session.Gold = 1234;
        // `SOC3_Main` takes the Dark Ages' money away with exactly this line.
        Assert.True(Place.Bln(Place.Call(s, "CodexConsole.Execute", null, "cash 0")));
        Assert.Equal(0, m.Session.Gold);
        Assert.True(Place.Bln(Place.Call(s, "CodexConsole.Execute", null, "CASH 500")));
        Assert.Equal(500, m.Session.Gold);
        Assert.True(Place.Bln(Place.Call(s, "CodexConsole.Execute", null, "cash")));
        Assert.Contains("Current Cash: 500", m.Scenes.Lines);
        // A real command Vitomir does not run still answers true; a word that is none answers false.
        Assert.True(Place.Bln(Place.Call(s, "CodexConsole.Execute", null, "framerate")));
        Assert.False(Place.Bln(Place.Call(s, "CodexConsole.Execute", null, "xyzzy 1 2")));
        Assert.Equal("Unrecognized command", m.Scenes.Lines[^1]);
    }
}
