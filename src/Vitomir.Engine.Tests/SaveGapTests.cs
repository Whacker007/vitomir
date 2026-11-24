// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The gaps the save review found**, each shut and pinned here: what 1.1's thing record
/// keeps and Vitomir's did not (the collide type, a region's flags, a prop's owner, the finalized bit,
/// a track mover's own state), and the things a load put back that the next save could not name.
/// </summary>
public partial class SaveTests
{
    /// <summary>A game started in another place than <see cref="Start"/>'s, the same way.</summary>
    static Game StartIn(GameFiles files, string level, string? scene)
    {
        var g0 = Start(files, awaken: false);
        var place = Location.Load(files, level, things: true, sky: false, scene: scene, player: "Christof");
        place.Awaken(new AiClasses(files), 1, g0.Effects, g0.Powers);
        var session = new Session { Here = place, Chronicle = g0.Chronicle };
        session.Quests.Read = g0.Session.Quests.Read;
        place.Session = session;
        return new Game(place, session, g0.Chronicle, g0.Effects, g0.Powers);
    }

    static WorldThing Back(Location from, WorldThing t, Location into) =>
        into.Things.First(x => ThingIds.Of(into, x) == ThingIds.Of(from, t));

    /// <summary>
    /// **A thing's collide type, a region's flags and a prop's owner come back** (1.1: the 0x28-byte
    /// block, tag 0x30, tag 0x2f). A script turning a door's collision off —
    /// `FLS3_LibussaHelp`'s heart-room door, `HHX1_23_1`'s — left an invisible wall after a load.
    /// </summary>
    [Fact]
    public void A_collide_type_a_region_s_flags_and_a_prop_s_owner_come_back()
    {
        using var files = Install.Open();
        var g = Start(files);
        var place = g.Place;
        var npc = place.Things.First(t => t.Actor is not null && !ReferenceEquals(t, place.Player!.Thing)
                                          && t.Collide == CollideShape.Cylinder && ThingIds.Placed(place, t) is not null);
        npc.Collide = CollideShape.None;
        var region = place.Things.First(t => t.Type == ThingType.Region && ThingIds.Placed(place, t) is not null);
        uint flags = region.RegionFlags ^ 0x4;
        region.RegionFlags = flags;
        var prop = place.Things.First(t => t.Type == ThingType.Prop && ThingIds.Placed(place, t) is not null);
        prop.PropOwner = place.Player!.Thing.Guid;

        var loaded = Load(files, SaveArchive.Read(Write(SaveCapture.Take(place, g.Session))));
        Assert.Equal(CollideShape.None, Back(place, npc, loaded.Place).Collide);
        Assert.Equal(flags, Back(place, region, loaded.Place).RegionFlags);
        Assert.Equal(place.Player.Thing.Guid, Back(place, prop, loaded.Place).PropOwner);
    }

    /// <summary>
    /// **A door or lift saved on its way carries on and arrives** (1.1 keeps the tracker's 0x90 bytes,
    /// the physics flags). It stood frozen part-way, and its `arrived` never came.
    /// </summary>
    [Fact]
    public void A_track_mover_saved_on_its_way_arrives_after_the_load()
    {
        using var files = Install.Open();
        Game g = null!;
        WorldThing? mover = null;
        foreach (var level in new[] { "P1_CNVT", "P1_MON1", "P1_OLDT", "P1_UNIV" })
        {
            g = StartIn(files, level, null);
            mover = g.Place.Things.FirstOrDefault(t => t.MoveType == WorldThing.MoveTrack && t.Keyframes.Length >= 2
                && ThingIds.Of(g.Place, t) is not null && Vector3.Distance(t.Position, t.Keyframes[1].Position) > 20f);
            if (mover is not null) break;
        }
        Assert.NotNull(mover);
        var place = g.Place;
        var goal = mover!.Keyframes[1];
        Assert.True(mover.MoveToFrame(1, 20f));
        for (int i = 0; i < 15; i++) mover.Advance(1f / 30f);                      // half a second of the way
        var saved = SaveCapture.Take(place, g.Session);
        Assert.NotNull(saved.World[saved.Header.Location].Things[ThingIds.Of(place, mover)!].Track?.Move);

        var loaded = Load(files, SaveArchive.Read(Write(saved)));
        var back = Back(place, mover, loaded.Place);
        Assert.Equal(mover.Position, back.Position);
        Assert.NotNull(back.Motion);
        int arrivedAt = -1;
        for (int i = 0; i < 30 * 60 && arrivedAt < 0; i++) arrivedAt = back.Advance(1f / 30f);
        Assert.Equal(1, arrivedAt);
        Assert.Equal(goal.Position, back.Position);
        Assert.Equal(goal.Orientation, back.TrackerAngles);
    }

    /// <summary>
    /// **An item picked up, saved, loaded and then dropped is kept** — it came back into the pack with
    /// no name a place could file it under, and the next save skipped it: gone at the next load.
    /// </summary>
    [Fact]
    public void An_item_picked_up_then_dropped_after_a_load_is_still_there_after_the_next()
    {
        using var files = Install.Open();
        var g = StartIn(files, "P1_MON1", "MON1_8_2");
        var place = g.Place;
        var christof = place.Player!.Thing;
        var item = place.Things.First(t => t.Item is not null && t.InWorld && t.Owner is null && ThingIds.Placed(place, t) is not null
                                           && !t.Template.Contains("coin", StringComparison.OrdinalIgnoreCase)
                                           && !t.Template.Contains("gold", StringComparison.OrdinalIgnoreCase)
                                           && !t.Template.Contains("cash", StringComparison.OrdinalIgnoreCase));
        string template = item.Template;
        Assert.Contains(Ground.Take(place, christof, item, InputModifier.None, steering: true, nowMs: 0), new[] { GroundResult.Stowed, GroundResult.ToHand });
        int before = place.Things.Count(t => t.Template == template && t.InWorld);

        var loaded = Load(files, SaveArchive.Read(Write(SaveCapture.Take(place, g.Session))));
        var him = loaded.Place.Player!.Thing;
        var carried = him.Actor!.Inventory.Items.Concat(him.Actor.Held is { } h ? [h] : [])
                         .First(i => i.Thing?.Template == template);
        Ground.Drop(loaded.Place, him, carried, 0, him.Position + new Vector3(40, 0, 0));
        Assert.True(carried.Thing!.InWorld);

        var again = Load(files, SaveArchive.Read(Write(SaveCapture.Take(loaded.Place, loaded.Session))));
        Assert.Equal(before + 1, again.Place.Things.Count(t => t.Template == template && t.InWorld));
    }

    /// <summary>
    /// **A companion who leaves the party after a load stays in the place** — the load made them with no
    /// name, and the next save had nothing to file them under.
    /// </summary>
    [Fact]
    public void A_companion_who_leaves_after_a_load_stays_in_the_place()
    {
        using var files = Install.Open();
        var g = Start(files);
        var place = g.Place;
        var christof = place.Player!.Thing;
        var wilhem = place.Place("Wilhem", christof.Position + new Vector3(96, 0, 0), christof.Orientation, christof.Sector)!;
        wilhem.Actor!.Flags |= Actor.Flag.Party;

        var loaded = Load(files, SaveArchive.Read(Write(SaveCapture.Take(place, g.Session))));
        var back = loaded.Place.Things.First(t => t.Template.Equals("Wilhem", StringComparison.OrdinalIgnoreCase));
        back.Actor!.Flags &= ~Actor.Flag.Party;                                     // RemoveFromParty

        var second = SaveCapture.Take(loaded.Place, loaded.Session);
        Assert.Contains(second.World[second.Header.Location].Things.Values, t => t.Template.Equals("Wilhem", StringComparison.OrdinalIgnoreCase));
        var again = Load(files, SaveArchive.Read(Write(second)));
        Assert.Contains(again.Place.Things, t => t.Template.Equals("Wilhem", StringComparison.OrdinalIgnoreCase) && t.InWorld);
    }

    /// <summary>
    /// **A party member standing in the place whose scene placed them keeps that placement**, so the
    /// place's scripts that name them get them after a load (`UNIV_7_7`'s Wilhem; 1.1's saved parameters
    /// keep the GUID) — not the stand-in number of the placed thing the load took away.
    /// </summary>
    [Fact]
    public void A_party_member_in_the_scene_that_placed_them_is_who_its_scripts_name_after_a_load()
    {
        using var files = Install.Open();
        var g0 = Start(files);
        var place = Location.Load(files, "P1_UNIV", things: true, sky: false, scene: "UNIV_7_7", player: "Christof");
        place.Awaken(new AiClasses(files), 1, g0.Effects, g0.Powers);
        var session = new Session { Here = place, Chronicle = g0.Chronicle };
        place.Session = session;
        var wilhem = place.Things.First(t => t.Type == ThingType.Player && !ReferenceEquals(t, place.Player!.Thing));
        wilhem.Actor!.Flags |= Actor.Flag.Party;
        int index = wilhem.Index;

        var first = SaveCapture.Take(place, session);
        Assert.Equal(ThingIds.Placed(place, wilhem), first.Party.Members.Single(m => m.Template == wilhem.Template).Placed);
        var world = first.World[first.Header.Location];
        var fresh = Start(files, awaken: false);
        SaveApply.Chronicle(fresh.Chronicle, first.Chronicle!);
        var into = Location.Load(files, world.Level, things: true, sky: false, scene: world.Scene, player: "Christof",
                                 entrance: world.Entrance, guids: SaveGuids.ForLoad(first));
        var restore = SaveApply.Place(into, world, fresh.Effects, fresh.Powers);
        SaveApply.Party(into, first.Party, restore, fresh.Effects, fresh.Powers);
        var back = into.Things.First(t => t.Type == ThingType.Player && !ReferenceEquals(t, into.Player!.Thing));
        Assert.Equal(wilhem.Guid, back.Guid);
        Assert.Equal(back.Guid, into.Gone[index]);
        Assert.Same(back, into.Thing(into.Gone[index]));
        Assert.Equal(index, back.Index);
    }

    /// <summary>
    /// **An item made straight into a pack and never in the world stays unfinalized** (1.1 keeps the
    /// 0x10000000 bit in the flag word it saves), so its first drop still finalizes it and tells its
    /// script `created` (`ItemWeaponSatchel` arms itself there).
    /// </summary>
    [Fact]
    public void An_item_never_in_the_world_is_still_unfinalized_after_a_load()
    {
        using var files = Install.Open();
        var g = Start(files);
        var christof = g.Place.Player!.Thing;
        var made = g.Place.Make("vitaered")!;
        Assert.False(made.Finalized);
        made.Item!.Thing = made;
        made.Owner = christof;
        christof.Actor!.Inventory.Add(made.Item);

        var loaded = Load(files, SaveArchive.Read(Write(SaveCapture.Take(g.Place, g.Session))));
        var back = loaded.Place.Player!.Thing.Actor!.Inventory.Items.Where(i => i.Thing?.Template == "vitaered")
                         .Select(i => i.Thing!).ToList();
        Assert.Contains(back, t => !t.Finalized);
    }

    /// <summary>
    /// **A fade under way is kept where it was heading** (ours: 1.1's record keeps no model alpha). A
    /// thing saved half-faded stayed half-seen for good.
    /// </summary>
    [Fact]
    public void A_fade_under_way_is_saved_where_it_was_heading()
    {
        using var files = Install.Open();
        var g = Start(files);
        var npc = g.Place.Things.First(t => t.Actor is not null && !ReferenceEquals(t, g.Place.Player!.Thing) && ThingIds.Placed(g.Place, t) is not null);
        npc.Model3D.SetAlpha(0f, 2000f);
        npc.Model3D.Tick(0.5f);
        Assert.InRange(npc.ModelAlpha, 0.5f, 0.9f);
        var loaded = Load(files, SaveArchive.Read(Write(SaveCapture.Take(g.Place, g.Session))));
        Assert.Equal(0f, Back(g.Place, npc, loaded.Place).ModelAlpha);
    }

    /// <summary>
    /// **A slot saved over keeps its previous version**, and a save is only put in the slot once it
    /// has been written whole and read back (ours).
    /// </summary>
    [Fact]
    public void A_slot_saved_over_keeps_its_previous_version()
    {
        using var files = Install.Open();
        var g = Start(files);
        string path = Path.Combine(Path.GetTempPath(), $"vitomir-slot-{Guid.NewGuid():n}{SaveArchive.Extension}");
        var first = SaveCapture.Take(g.Place, g.Session);
        first.Header.Notes = "first";
        SaveArchive.Write(first, path);
        Assert.False(File.Exists(SaveArchive.PreviousOf(path)));
        var second = SaveCapture.Take(g.Place, g.Session);
        second.Header.Notes = "second";
        SaveArchive.Write(second, path);
        Assert.Equal("second", SaveArchive.Read(path).Header.Notes);
        Assert.Equal("first", SaveArchive.Read(SaveArchive.PreviousOf(path)).Header.Notes);
        Assert.False(File.Exists(path + ".writing"));
    }
}
