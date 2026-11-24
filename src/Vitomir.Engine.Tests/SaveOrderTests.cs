// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Save;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **After a load the world's thing list is the one saved, turned round** (`Save/SaveOrder.cs`).
/// 1.1 writes a place's things from the list's head, newest first, and its load links each
/// one it reads in at the head, so after a load the oldest thing
/// ticks first. Vitomir kept no order and built the place again in the scene's, so a load ran on in
/// the order the place had before the save — who acted first, and whose roll was whose, differed from
/// 1.1's after every load. The `.vsave` keeps the order now (`listOrder`); a save without it loads as
/// it always did. Pinned through a save file and a load the way the game loads one, with the
/// proximity test (part of each thing's own tick) showing the order the walk ran in.
/// </summary>
public class SaveOrderTests
{
    sealed class Recorder : ISceneScripts
    {
        public readonly List<WorldThing> Touched = [];
        public void Enter(Location location) { }
        public void Leave(int exitNumber = 0) { }
        public void Step(float seconds) { }
        public void StepTimers(float seconds) { }
        public void TouchThing(WorldThing thing) => Touched.Add(thing);
        public void DeleteThing(WorldThing thing) { }
    }

    static Location Build(Vitomir.Engine.Vfs.GameFiles files) =>
        Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");

    /// <summary>
    /// A place with three of its own things and one made at run time, each set to test for touches;
    /// saved to a file and read back. Answers the saved game and the four things' ids, oldest first.
    /// </summary>
    static (SaveGame Game, string[] Ids) Saved(Vitomir.Engine.Vfs.GameFiles files, Location place)
    {
        var things = place.Things;
        var middle = things[things.Count / 2];
        var made = place.Place(middle.Template, middle.Position + new Vector3(0f, 0f, 64f), middle.Orientation, middle.Sector);
        Assert.NotNull(made);
        WorldThing[] four = [things[0], middle, things[^2], made!];     // ^1 is the made one
        foreach (var t in four) t.Flags |= WorldThing.Flag.ProximityTrigger;
        string[] ids = [.. four.Select(t => ThingIds.Of(place, t)!)];

        var session = new Session { Here = place };
        place.Session = session;
        var saved = SaveCapture.Take(place, session);
        string path = Path.Combine(Path.GetTempPath(), $"vitomir-order-{Guid.NewGuid():N}.vsave");
        try { SaveArchive.Write(saved, path); return (SaveArchive.Read(path), ids); }
        finally { File.Delete(path); }
    }

    static List<string> TickOrder(Location place, string[] ids)
    {
        var scripts = new Recorder();
        new Simulation(place) { Scripts = scripts }.Step(1f / 30f);
        return [.. scripts.Touched.Select(t => ThingIds.Of(place, t)!).Where(ids.Contains)];
    }

    /// <summary>
    /// Before the save the newest ticks first — the thing made at run time, then the scene's last,
    /// its middle, its first. After the load the list is that turned round: the scene's first thing
    /// ticks first and the made one last, as 1.1's load leaves it.
    /// </summary>
    [Fact]
    public void After_a_load_the_list_is_the_saved_one_reversed_and_the_oldest_ticks_first()
    {
        using var files = Install.Open();
        var place = Build(files);
        var (game, ids) = Saved(files, place);
        var record = game.World.Values.Single();
        Assert.Equal(ids.Reverse(), record.ListOrder!.Where(ids.Contains));       // written head first

        var loaded = Build(files);
        SaveApply.Place(loaded, record, null, null);

        Assert.Equal(ids, TickOrder(loaded, ids));
        Assert.Equal(Enumerable.Reverse(record.ListOrder!), loaded.ListOrder.Select(t => ThingIds.Of(loaded, t)!));
    }

    /// <summary>
    /// The list goes on from there: a thing that arrives after the load goes in at the head
    /// and ticks before the loaded ones.
    /// </summary>
    [Fact]
    public void A_thing_made_after_the_load_ticks_before_the_loaded_ones()
    {
        using var files = Install.Open();
        var (game, ids) = Saved(files, Build(files));
        var loaded = Build(files);
        SaveApply.Place(loaded, game.World.Values.Single(), null, null);
        var anchor = loaded.Things.First(t => ThingIds.Of(loaded, t) == ids[1]);
        var fresh = loaded.Place(anchor.Template, anchor.Position + new Vector3(0f, 0f, 96f), anchor.Orientation, anchor.Sector)!;
        fresh.Flags |= WorldThing.Flag.ProximityTrigger;
        string freshId = ThingIds.Of(loaded, fresh)!;

        Assert.Equal([freshId, .. ids], TickOrder(loaded, [freshId, .. ids]));
    }

    /// <summary>
    /// A save written before the order was kept has none, and loads as it always did: the scene's
    /// things in the scene's order and what was made after them, so the newest still ticks first.
    /// </summary>
    [Fact]
    public void A_save_without_the_order_loads_in_the_scene_s_order()
    {
        using var files = Install.Open();
        var (game, ids) = Saved(files, Build(files));
        var record = game.World.Values.Single();
        record.ListOrder = null;

        var loaded = Build(files);
        SaveApply.Place(loaded, record, null, null);

        Assert.Equal(ids.Reverse(), TickOrder(loaded, ids));
    }
    /// <summary>
    /// The party is not in the place's file (the save leaves out a party player): it is linked in
    /// once the place stands, each member at the head, so after a load the
    /// leader is the newest thing and ticks first — not where the scene put the player.
    /// </summary>
    [Fact]
    public void After_a_load_the_party_is_the_newest()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var session = new Session { Here = place };
        place.Session = session;
        var leader = place.Player!.Thing;
        var made = place.Place(place.Things.First(t => t.Type == ThingType.Item).Template, leader.Position + new Vector3(0f, 0f, 64f), leader.Orientation, leader.Sector);
        Assert.Same(made, place.ListOrder[0]);                            // before the save, what was made last is the newest
        var saved = SaveCapture.Take(place, session);
        string path = Path.Combine(Path.GetTempPath(), $"vitomir-order-{Guid.NewGuid():N}.vsave");
        SaveGame game;
        try { SaveArchive.Write(saved, path); game = SaveArchive.Read(path); }
        finally { File.Delete(path); }

        var loaded = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var restore = SaveApply.Place(loaded, game.World.Values.Single(), null, null);
        SaveApply.Party(loaded, game.Party, restore);

        Assert.Same(loaded.Player!.Thing, loaded.ListOrder[0]);
    }
}
