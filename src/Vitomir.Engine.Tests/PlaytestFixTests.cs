// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What the first play-tests turned up, each checked against
/// 1.1 before it was fixed.
/// </summary>
public class PlaytestFixTests
{
    static (Campaign Campaign, GameFiles Files) Monastery()
    {
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        var chronicle = where.Chronicle;
        chronicle.Current = chronicle.Find(where.Entry!.Name);
        var classes = new AiClasses(files);
        Location Build(string name, string? scene, int entrance)
        {
            var place = Location.Load(files, name, sky: false, scene: scene, player: "Christof", entrance: entrance);
            place.Awaken(classes);
            return place;
        }
        var here = Location.Load(files, "P1_MON1", sky: false, scene: where.Entry.SceneName, player: "Christof");
        var christof = here.Player!.Thing;
        christof.Actor!.Flags |= Actor.Flag.Party;
        var wilhem = here.Place("wilhem", christof.Position + new Vector3(96, 0, 0), christof.Orientation, christof.Sector)!;
        wilhem.Actor!.Flags |= Actor.Flag.Party;
        wilhem.Actor.Flags2 |= Actor.Flag2.InFormation;         // what `--companion` and `AddToParty` set
        here.Awaken(classes);
        return (new Campaign { Chronicle = chronicle, Build = Build, Here = here }, files);
    }

    [Fact]
    public void A_companion_carried_through_an_exit_has_a_mind_in_the_new_place_and_follows()
    {
        // *"I went outside the Monastery, and noticed that Wilhem stopped following."* The place
        // at the far end had woken up before he was rebuilt in it, and minds were made only then.
        var (campaign, files) = Monastery();
        using var _ = files;
        Assert.True(campaign.Chronicle.TakeExit(0));
        Assert.NotNull(campaign.Travel());
        var there = campaign.Here;
        var wilhem = there.Things.Single(t => t.Template == "wilhem");
        Assert.NotNull(wilhem.Mind);
        Assert.Equal(AiMode.Party, wilhem.Mind!.Mode);
        there.Minds!.Tick(0.1f);                               // it joins at the start of a tick
        Assert.Contains(wilhem.Mind, there.Minds.All);
        // And it does follow: walk Christof away and give the party a few seconds.
        var christof = there.Player!.Thing;
        var from = wilhem.Position;
        // Somewhere Christof can walk to, well away from where Wilhem stands.
        var spot = there.Things.Where(t => t.Type == ThingType.Prop)
                        .Select(t => t.Position).Where(p => Vector3.Distance(p, christof.Position) is > 500f and < 1500f)
                        .Select(p => (p, n: there.Walk!.Find(p, 256f))).First(e => e.n >= 0);
        Assert.True(there.Player.WalkTo(spot.p, spot.n));
        var christofFrom = christof.Position;
        for (int i = 0; i < 300; i++) { there.Minds.Tick(1f / 30f); there.Minds.Follow(1f / 30f); there.Player.Follow(1f / 30f); }
        Assert.True(Vector3.Distance(christofFrom, christof.Position) > 100f, "Christof never left");
        Assert.True(Vector3.Distance(from, wilhem.Position) > 32f, "Wilhem stood where he was put");
    }

    [Fact]
    public void The_clock_goes_on_through_a_door()
    {
        var (campaign, files) = Monastery();
        using var _ = files;
        var before = new Simulation(campaign.Here);
        for (int i = 0; i < 90; i++) before.Step(1f / 30f);
        Assert.True(campaign.Chronicle.TakeExit(0));
        campaign.Travel();
        var after = new Simulation(campaign.Here);
        after.Resume(before.Seconds);
        Assert.Equal(before.Milliseconds, after.Milliseconds);
        Assert.Equal(before.Milliseconds, campaign.Here.Minds!.Now);
    }

    [Fact]
    public void Repeating_game_messages_follow_the_engine_and_its_three_bugs()
    {
        var log = new MessageLog();
        var notices = new GameMessages { Log = log };
        // The first add answers slot 0 — the documented "failure" — and is due at once.
        Assert.Equal(0, notices.AddUser("HELLO", 0, 0, 3, 1000, now: 100));
        notices.Tick(100);
        Assert.Single(log.Lines);
        notices.Tick(500);                                     // not due again yet
        Assert.Single(log.Lines);
        notices.Tick(1100);
        notices.Tick(2100);                                    // the third and last
        Assert.Equal(3, log.Lines.Count);
        notices.Tick(3100);
        Assert.Equal(3, log.Lines.Count);
        Assert.Null(notices.All[0]);

        // Removing by what the add returned (the slot) misses: it compares the GUID.
        int slot = notices.AddSystem(1, 0, 0, 0, 1000, now: 0);
        Assert.Equal(0, slot);
        notices.RemoveByGuid(slot);
        Assert.NotNull(notices.All[0]);
        // Removing by system id never finds a system message — the id is never stored.
        notices.RemoveBySystemId(1);
        Assert.NotNull(notices.All[0]);
        // A user message goes on any string id at all, its own or not.
        Assert.Equal(1, notices.AddUser("HELLO", 0, 0, 0, 1000, now: 0));
        notices.RemoveByStringId("SOMETHING_ELSE");
        Assert.Null(notices.All[1]);
        Assert.NotNull(notices.All[0]);
    }

    [Fact]
    public void An_expired_message_is_still_put_in_the_box_once_on_the_frame_it_goes()
    {
        var log = new MessageLog();
        var notices = new GameMessages { Log = log };
        notices.AddUser("HELLO", 0, expireMs: 500, repeat: 0, frequencyMs: 1000, now: 0);
        notices.Tick(0);
        Assert.Single(log.Lines);
        notices.Tick(1000);                                    // expired and due: once more, then gone
        Assert.Equal(2, log.Lines.Count);
        Assert.Null(notices.All[0]);
    }

    [Fact]
    public void Attaching_refuses_twice_and_letting_go_can_take_the_child_with_it()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false);
        var parent = place.Things.First(t => t.Type == ThingType.Prop);
        var child = place.Place("torchFlame", parent.Position, parent.Orientation, parent.Sector)!;
        var other = place.Place("torchFlame", parent.Position, parent.Orientation, parent.Sector)!;
        Assert.True(Attachments.Attach(parent, child, -1, new Vector3(0, 0, 10), child.Rotation, 0));
        Assert.False(Attachments.Attach(other, child, -1, Vector3.Zero, child.Rotation, 0));   // already riding
        Attachments.Update(parent);
        Assert.Equal(parent.Position.Z + 10f, child.Position.Z, 3);
        // Plain detach: free, where it stands.
        Assert.True(Attachments.Detach(parent, child));
        Assert.Null(child.AttachedTo);
        Assert.False(child.Removed);
        // Attached to go with its parent: detaching destroys it.
        Assert.True(Attachments.Attach(parent, other, -1, Vector3.Zero, other.Rotation, Attachments.AutoRemove));
        Assert.True(Attachments.Detach(parent, other));
        Assert.True(other.Removed);
    }
    [Fact]
    public void Christof_gets_out_of_bed_in_another_model_and_his_sword_can_be_hidden()
    {
        // The Convent's morning: `SetModel("christof_human.nod")` and
        // `EnableActorWeapon(false)` while he lies in bed.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_CNVT", sky: false, player: "Christof");
        var christof = place.Player!.Thing;
        int slot = place.Drawn.ToList().IndexOf(christof);
        Assert.True(slot >= 0);
        Assert.False(place.SetModel(christof, "no_such_model.nod"));
        Assert.True(place.SetModel(christof, "christof_human.nod"));
        Assert.Equal("christof_human.nod", christof.Model);
        Assert.False(place.Drawn[slot].InWorld);                 // the old geometry is retired
        Assert.Same(christof, place.Drawn[^1]);                  // and the new one appended
        Assert.NotNull(christof.Animation);

        var animated = place.Mesh.Animated.Single(a => a.Thing == place.Drawn.Count - 1);
        christof.Actor!.Weapon = Vitomir.Engine.Rpg.Weapon.From("broadsword", new TemplateSet(files).Resolve("broadsword"));
        place.Mesh.Rearm(animated, christof, files, place.Materials);
        Assert.Contains(animated.Carried, p => p.Shown);
        christof.Actor.WeaponHidden = true;
        Assert.True(place.Mesh.Rearm(animated, christof, files, place.Materials));
        Assert.DoesNotContain(animated.Carried, p => p.Shown);
        christof.Actor.WeaponHidden = false;
        Assert.True(place.Mesh.Rearm(animated, christof, files, place.Materials));
        Assert.Contains(animated.Carried, p => p.Shown);
    }
}
