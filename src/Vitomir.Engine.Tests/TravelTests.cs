// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Leaving one place and arriving in the next — the half of the level change that is the engine's.
/// `CodexSequence.TakeExit` records the request; <see cref="Campaign"/> is what acts on it.
/// </summary>
public class TravelTests
{
    static (Campaign Campaign, GameFiles Files) Open(string level)
    {
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel(level);
        Assert.NotNull(where);
        var chronicle = where!.Value.Chronicle;
        chronicle.Current = chronicle.Find(where.Value.Entry!.Name);

        Location Build(string name, string? scene, int entrance) =>
            Location.Load(files, name, sky: false, scene: scene, player: "Christof", entrance: entrance);

        var here = Build(level, where.Value.Entry.SceneName, -1);
        return (new Campaign { Chronicle = chronicle, Build = Build, Here = here }, files);
    }

    [Fact]
    public void A_start_s_group_is_its_entrance_and_the_group_holds_one_start_per_party_slot()
    {
        // The measurement the whole feature rests on, kept as a test because it is the kind of
        // reading that would be easy to get subtly wrong and impossible to notice.
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        int leading = 0, resolved = 0, four = 0;
        foreach (var c in set.All)
            for (int i = 0; i < c.Count; i++)
            {
                var loc = c.At(i)!;
                for (int n = 0; n < loc.ExitCount; n++)
                {
                    if (loc.Exits[n].To < 0) continue;
                    leading++;
                    var dest = c.At(loc.Exits[n].To)!;
                    if (!files.TryRead($"scenes/{dest.LevelName}.nsd", out var d)) continue;
                    var groups = NsdScene.Read(d, dest.LevelName).Starts
                                         .GroupBy(s => (int)s.Group).ToDictionary(g => g.Key, g => g.Count());
                    if (!groups.TryGetValue(loc.Exits[n].Entrance, out int many)) continue;
                    resolved++;
                    if (many == 4) four++;
                }
            }
        Assert.Equal(510, leading);
        // Ten of the game's exits name an entrance their destination has no group for — level
        // builders' slips, and the fallback to the first start is what has to answer for them.
        Assert.Equal(500, resolved);
        Assert.Equal(499, four);
    }

    [Fact]
    public void Opening_a_place_at_an_entrance_stands_you_on_that_group_s_starts()
    {
        using var files = Install.Open();
        var scene = NsdScene.Read(files.Read("scenes/P1_MON1.nsd"), "P1_MON1");
        var groups = scene.Starts.Select(s => (int)s.Group).Distinct().Order().ToArray();
        Assert.True(groups.Length > 1, "P1_MON1 has only one entrance, so this proves nothing");

        foreach (int g in groups)
        {
            var place = Location.Load(files, "P1_MON1", sky: false, entrance: g);
            Assert.Equal(g, place.Entrance);
            Assert.NotEmpty(place.Arrivals);
            Assert.All(place.Arrivals, s => Assert.Equal((uint)g, s.Group));
            Assert.Equal(place.Arrivals[0].Position + new Vector3(0, 0, Location.EyeHeight),
                         place.StartPosition);
        }

        // An entrance nobody built falls back to the first start rather than dropping the player
        // through the floor — ten of the game's own exits need that.
        var missing = Location.Load(files, "P1_MON1", sky: false, entrance: 99);
        Assert.Single(missing.Arrivals);
        Assert.Equal(scene.Starts[0].Position, missing.Arrivals[0].Position);
    }

    [Fact]
    public void Taking_the_monastery_s_exit_puts_Christof_in_the_next_place_with_his_character_intact()
    {
        // A click once logged "exit 0 taken from Monastery to PetrinHillDay, entrance 1
        // — the place does not change yet". This is the place changing.
        var (campaign, files) = Open("P1_MON1");
        using var _ = files;
        var before = campaign.Here;
        var christof = before.Player!.Thing.Actor!;

        // Mark the character so there is no doubt the same one arrives.
        christof.Health = 41f;
        christof.SetBaseStat((int)ActorStat.Strength, 7f);
        christof.Stat[(int)ActorStat.Strength] = 7f;
        int packed = christof.Inventory.Items.Count;

        Assert.True(campaign.Chronicle.TakeExit(0), "the monastery's exit 0 does not lead anywhere");
        var arrival = campaign.Travel();

        Assert.NotNull(arrival);
        Assert.NotSame(before, campaign.Here);
        Assert.Equal("PetrinHillDay", campaign.Chronicle.NameOf(campaign.Chronicle.Current));
        Assert.Equal(1, arrival!.Value.Entrance);
        Assert.Equal(1, arrival.Value.Entrance);

        // The same character object, not a fresh one built from the template.
        var arrived = campaign.Here.Player!.Thing.Actor!;
        Assert.Same(christof, arrived);
        Assert.Equal(41f, arrived.Health);
        Assert.Equal(7f, arrived.Stat[(int)ActorStat.Strength]);
        Assert.Equal(packed, arrived.Inventory.Items.Count);

        // Standing on the entrance the exit named, on ground he can walk off.
        Assert.Equal(1, campaign.Here.Entrance);
        Assert.NotEmpty(campaign.Here.Arrivals);
        Assert.True(campaign.Here.Walk!.Find(campaign.Here.Player!.Thing.Position) >= 0,
                    "arrived somewhere with no floor");
        Assert.Null(campaign.Chronicle.Leaving);
    }

    [Fact]
    public void What_he_is_carrying_comes_with_him_and_can_still_be_put_down()
    {
        var (campaign, files) = Open("P1_MON1");
        using var _ = files;
        var christof = campaign.Here.Player!.Thing.Actor!;
        // The starting kit is the harness's job, not `Location.Load`'s, so put something in his
        // pack by hand — which is also the shortest statement of what has to survive the journey.
        var body = campaign.Here.Make("broadsword");
        Assert.NotNull(body);
        var kept = body!.Item!;
        body.Owner = campaign.Here.Player.Thing;
        christof.Inventory.Add(kept);
        var oldBody = kept.Thing;
        Assert.NotNull(oldBody);

        Assert.True(campaign.Chronicle.TakeExit(0));
        var arrival = campaign.Travel();
        Assert.NotNull(arrival);
        Assert.True(arrival!.Value.Carried > 0, "nothing came across in the pack");

        // The same Item — its charges, its condition, its place in the pack — with a new body in
        // the new world, because the old one belonged to the level that has gone.
        Assert.Contains(kept, christof.Inventory.Items);
        Assert.NotNull(kept.Thing);
        Assert.NotSame(oldBody, kept.Thing);
        // Registered in the new place — a script can resolve its GUID — and carried rather than
        // lying about: `Place` stays null, which is what `InWorld` reads, because a thing in
        // somebody's pack is not in the world.
        Assert.Same(kept.Thing, campaign.Here.Thing(kept.Thing!.Guid));
        Assert.False(kept.Thing.InWorld);
        Assert.Same(campaign.Here.Player!.Thing, kept.Thing.Owner);
        Assert.Same(kept, kept.Thing.Item);
    }

    /// <summary>
    /// **An item on the pointer through an exit is still held on the far side** — its holder the
    /// character, no owner, not in the pack — as in 1.1 (Wilhem taking the
    /// smithy's door with a quarterstaff on the pointer still has it after). Once the new body
    /// was marked owned, and a put-down there took the pack's arm.
    /// </summary>
    [Fact]
    public void An_item_on_the_pointer_comes_through_an_exit_still_held()
    {
        var (campaign, files) = Open("P1_MON1");
        using var _ = files;
        var me = campaign.Here.Player!.Thing;
        var body = campaign.Here.Make("broadsword");
        Assert.NotNull(body);
        var held = body!.Item!;
        Assert.True(Vitomir.Engine.Rpg.Ground.Hand(me, held));

        Assert.True(campaign.Chronicle.TakeExit(0));
        Assert.NotNull(campaign.Travel());

        var now = campaign.Here.Player!.Thing;
        Assert.Same(held, now.Actor!.Held);
        Assert.DoesNotContain(held, now.Actor.Inventory.Items);
        Assert.Same(now, held.Thing!.Holder);
        Assert.Null(held.Thing.Owner);
    }

    [Fact]
    public void A_pending_exit_that_leads_to_a_location_with_no_level_is_dropped_rather_than_obeyed()
    {
        var (campaign, files) = Open("P1_MON1");
        using var _ = files;
        var before = campaign.Here;

        // The chronicles do carry locations that exist only to be locked; pointing an exit at one
        // must not take the world down with it.
        string here = campaign.Chronicle.NameOf(campaign.Chronicle.Current);
        int empty = Enumerable.Range(0, campaign.Chronicle.Count)
                              .FirstOrDefault(i => campaign.Chronicle.At(i)!.LevelName.Length == 0, -1);
        if (empty < 0) return;                       // this chronicle has none; nothing to prove

        Assert.True(campaign.Chronicle.ChangeExit(here, 0, campaign.Chronicle.NameOf(empty), 0));
        Assert.True(campaign.Chronicle.TakeExit(0));
        Assert.Null(campaign.Travel());
        Assert.Same(before, campaign.Here);
        Assert.Null(campaign.Chronicle.Leaving);
    }
}
