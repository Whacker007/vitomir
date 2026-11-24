// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **1.1's order of things in a frame**. A thing enters its
/// world at the head of the world's list, the world's tick walks the list
/// from the head, and each thing's whole tick runs before the next is read —
/// so the newest thing acts first, and nothing of an older thing's tick happens before a newer
/// thing's tick is over. Earlier, Vitomir ran the frame stage by stage over every thing in the
/// order of entry, so who acted first, and which roll of a shared stream was whose, differed.
///
/// A stand-in for the scripts writes down each thing's proximity test, which is part of the thing's
/// own tick after its move, and the deletions the walk reaches; a spin on a
/// physics thing (applied whether or not the move is blocked) shows when its move ran.
/// </summary>
public class ThingOrderTests
{
    /// <summary>What the scripts saw, in the order they saw it.</summary>
    sealed class Recorder : ISceneScripts
    {
        public readonly List<WorldThing> Touched = [];
        public readonly List<WorldThing> Deleted = [];
        public Action<WorldThing>? OnTouch;

        public void Enter(Location location) { }
        public void Leave(int exitNumber = 0) { }
        public void Step(float seconds) { }
        public void StepTimers(float seconds) { }
        public void TouchThing(WorldThing thing) { Touched.Add(thing); OnTouch?.Invoke(thing); }
        public void DeleteThing(WorldThing thing) => Deleted.Add(thing);
    }

    static (Location place, Recorder scripts, Simulation sim) Open(Vitomir.Engine.Vfs.GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var scripts = new Recorder();
        return (place, scripts, new Simulation(place) { Scripts = scripts });
    }

    /// <summary>Three things of the place, in the order they entered it: the first, one from the middle, the last.</summary>
    static WorldThing[] Three(Location place)
    {
        var things = place.Things;
        Assert.True(things.Count >= 3);
        WorldThing[] three = [things[0], things[things.Count / 2], things[^1]];
        foreach (var t in three) t.Flags |= WorldThing.Flag.ProximityTrigger;
        return three;
    }

    /// <summary>
    /// The level's things are read in the scene's order and each is put at the head, so the last
    /// one read ticks first and the first one read ticks last.
    /// </summary>
    [Fact]
    public void The_newest_thing_ticks_first()
    {
        using var files = Install.Open();
        var (place, scripts, sim) = Open(files);
        var t = Three(place);
        var (first, middle, last) = (t[0], t[1], t[2]);

        sim.Step(1f / 30f);

        Assert.Equal([last, middle, first], scripts.Touched);
        Assert.Equal(Enumerable.Reverse(place.Things), place.ListOrder);
    }

    /// <summary>
    /// A thing that leaves the world and comes back (a sword picked up and dropped) goes in at the
    /// head again: it is the newest, and ticks first.
    /// </summary>
    [Fact]
    public void A_thing_that_comes_back_into_the_world_is_the_newest()
    {
        using var files = Install.Open();
        var (place, scripts, sim) = Open(files);
        var t = Three(place);
        var (first, middle, last) = (t[0], t[1], t[2]);

        Assert.True(place.Withdraw(first));
        place.Enter(first);
        sim.Step(1f / 30f);

        Assert.Equal([first, last, middle], scripts.Touched);
    }

    /// <summary>
    /// **One thing's whole tick before the next's.** Two spinning physics things: when the newer
    /// one's tick reaches its proximity test it has turned and the older one has not — the older
    /// one's move is part of its own tick, which has not begun. Stage by stage, both would have
    /// turned before either's test.
    /// </summary>
    [Fact]
    public void A_thing_s_whole_tick_runs_before_the_next_thing_s()
    {
        using var files = Install.Open();
        var (place, scripts, sim) = Open(files);
        var t = Three(place);
        var (older, newer) = (t[0], t[2]);
        foreach (var spinner in (WorldThing[])[older, newer])
        {
            spinner.MoveType = WorldThing.MovePhysics;
            spinner.PhysicsFlags = 0;                                  // awake, no gravity
            spinner.Velocity = Vector3.Zero;
            spinner.RotVelocity = new Vector3(0f, 0f, 90f);
        }
        float olderYaw = older.Orientation.Z, newerYaw = newer.Orientation.Z;
        var seen = new List<(WorldThing Who, bool OlderTurned, bool NewerTurned)>();
        scripts.OnTouch = who => seen.Add((who, older.Orientation.Z != olderYaw, newer.Orientation.Z != newerYaw));

        sim.Step(1f / 30f);

        var mine = seen.Where(s => s.Who == older || s.Who == newer).ToList();
        Assert.Equal([(newer, false, true), (older, true, true)], mine);
    }

    /// <summary>
    /// **A thing made during the walk first ticks on the next frame**: it goes in at the head,
    /// which the walk has passed (the walk reads each next before the tick). On the next frame it
    /// is the newest, and ticks first.
    /// </summary>
    [Fact]
    public void A_thing_made_during_the_walk_ticks_from_the_next_frame()
    {
        using var files = Install.Open();
        var (place, scripts, sim) = Open(files);
        var t = Three(place);
        var (first, middle, last) = (t[0], t[1], t[2]);
        WorldThing? made = null;
        scripts.OnTouch = who =>
        {
            if (who != last || made is not null) return;
            made = place.Place(middle.Template, middle.Position + new Vector3(0f, 0f, 64f), middle.Orientation, middle.Sector);
            Assert.NotNull(made);
            made!.Flags |= WorldThing.Flag.ProximityTrigger;
        };

        sim.Step(1f / 30f);
        Assert.Equal([last, middle, first], scripts.Touched);

        scripts.Touched.Clear();
        sim.Step(1f / 30f);
        Assert.Equal([made!, last, middle, first], scripts.Touched);
    }

    /// <summary>
    /// A thing destroyed by a newer thing's tick does not tick: the walk reaches it and deletes it
    /// there, which is where its scripts hear of it. One destroyed by an older thing's
    /// tick — already passed — has ticked, and is not reached again.
    /// </summary>
    [Fact]
    public void A_thing_destroyed_by_an_earlier_tick_is_deleted_where_the_walk_reaches_it()
    {
        using var files = Install.Open();
        var (place, scripts, sim) = Open(files);
        var t = Three(place);
        var (first, middle, last) = (t[0], t[1], t[2]);
        scripts.OnTouch = who =>
        {
            if (who == last) place.Remove(first, drop: false);        // ahead of the walk
            if (who == middle) place.Remove(last, drop: false);       // behind it
        };

        sim.Step(1f / 30f);

        Assert.Equal([last, middle], scripts.Touched);
        Assert.Equal([first], scripts.Deleted);
    }
}
