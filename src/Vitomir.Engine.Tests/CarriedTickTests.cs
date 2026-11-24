// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A character's carried things' own ticks** (`World/CarriedTick.cs`). 1.1's character tick
/// walks everything the character owns and then the item on the pointer, after its effects and its
/// action, and runs the generic thing tick on each. Of that tick only the first
/// step (the expiry, and the deletion of a thing destroyed) acts on a thing out of the world, and
/// the model and riders only on one in it — which, of carried things, only the utility item on the
/// hand is. Until then Vitomir ran none of it, so a carried item a script destroyed stayed in the pack,
/// and one whose time ran out never went. Pinned on Christof in the monastery with a torch: in the
/// pack, on the hand, and loose on the floor for comparison.
/// </summary>
public class CarriedTickTests
{
    sealed class Recorder : ISceneScripts
    {
        public readonly List<WorldThing> Deleted = [];
        public void Enter(Location location) { }
        public void Leave(int exitNumber = 0) { }
        public void Step(float seconds) { }
        public void StepTimers(float seconds) { }
        public void DeleteThing(WorldThing thing) => Deleted.Add(thing);
    }

    const float Frame = 1f / 30f;

    static (Location Place, WorldThing Christof, Simulation Sim, Recorder Scripts) Monastery(Vfs.GameFiles files)
    {
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var scripts = new Recorder();
        return (place, place.Player!.Thing, new Simulation(place) { Scripts = scripts }, scripts);
    }

    /// <summary>A torch in Christof's pack.</summary>
    static Item Packed(Location place, WorldThing christof)
    {
        var torch = place.Make("torchU")!.Item!;
        Ground.Keep(christof, torch, Slot.Inventory, (0, 0));
        return torch;
    }

    /// <summary>A torch on Christof's hand.</summary>
    static Item OnTheHand(Vfs.GameFiles files, Location place, WorldThing christof)
    {
        var torch = place.Make("torchU")!.Item!;
        christof.Actor!.Inventory.Add(torch);
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(christof, torch, Slot.None, new EffectDefinitions(files), 0));
        return torch;
    }

    /// <summary>
    /// A thing in the pack whose expiry passes is destroyed in its owner's tick and deleted there:
    /// out of the pack, and its scripts hear of it. Not before.
    /// </summary>
    [Fact]
    public void A_carried_thing_whose_time_runs_out_leaves_the_pack()
    {
        using var files = Install.Open();
        var (place, christof, sim, scripts) = Monastery(files);
        var torch = Packed(place, christof);
        var body = torch.Thing!;
        body.ExpireAt = sim.Milliseconds + 200;

        sim.Step(Frame);
        Assert.Contains(torch, christof.Actor!.Inventory.Items);
        for (int i = 0; i < 10; i++) sim.Step(Frame);

        Assert.DoesNotContain(torch, christof.Actor!.Inventory.Items);
        Assert.True(body.Removed);
        Assert.Null(body.Owner);
        Assert.Equal([body], scripts.Deleted);
    }

    /// <summary>
    /// A script's `Remove` on something in a pack only flags it; the owner's next tick
    /// deletes it and it leaves the pack.
    /// </summary>
    [Fact]
    public void A_carried_thing_a_script_removes_leaves_the_pack_on_its_owner_s_tick()
    {
        using var files = Install.Open();
        var (place, christof, sim, scripts) = Monastery(files);
        var torch = Packed(place, christof);

        Assert.True(place.Remove(torch.Thing!));
        Assert.Contains(torch, christof.Actor!.Inventory.Items);
        sim.Step(Frame);

        Assert.DoesNotContain(torch, christof.Actor!.Inventory.Items);
        Assert.Equal([torch.Thing!], scripts.Deleted);
    }

    /// <summary>
    /// A worn thing whose time runs out comes off first: the torch leaves the
    /// hand and the world, and the flame riding it is let go with it.
    /// </summary>
    [Fact]
    public void A_worn_thing_whose_time_runs_out_comes_off_and_leaves_the_pack()
    {
        using var files = Install.Open();
        var (place, christof, sim, _) = Monastery(files);
        var torch = OnTheHand(files, place, christof);
        var body = torch.Thing!;
        var flame = place.Place("torchFlame_nosave", body.Position, body.Orientation, body.Sector)!;
        Assert.True(Attachments.Attach(body, flame, -1, new Vector3(0f, 0f, 10f), Matrix4x4.Identity, 0));
        body.ExpireAt = sim.Milliseconds + 100;

        for (int i = 0; i < 6; i++) sim.Step(Frame);

        Assert.Null(christof.Actor!.Utility);
        Assert.DoesNotContain(torch, christof.Actor.Inventory.Items);
        Assert.False(body.InWorld);
        Assert.DoesNotContain(christof.Attachments, a => ReferenceEquals(a.Child, body));
        Assert.Null(flame.AttachedTo);
    }

    /// <summary>
    /// The carried torch is in the world and in the pack, so 1.1 moves its model on twice a frame:
    /// in its own place in the world's walk, and in its holder's tick.
    /// An alpha ramp on it runs at twice the pace of the same ramp on a torch on the floor.
    /// </summary>
    [Fact]
    public void The_carried_torch_s_model_moves_on_in_its_holder_s_tick_as_well()
    {
        using var files = Install.Open();
        var (place, christof, sim, _) = Monastery(files);
        var carried = OnTheHand(files, place, christof).Thing!;
        var loose = place.Place("torchU", christof.Position + new Vector3(48f, 0f, 0f), christof.Orientation, christof.Sector)!;
        carried.Model3D.SetAlpha(0f, 1000f);
        loose.Model3D.SetAlpha(0f, 1000f);

        for (int i = 0; i < 6; i++) sim.Step(Frame);

        Assert.Equal(0.8f, loose.Fx!.Alpha, 0.02f);
        Assert.Equal(0.6f, carried.Fx!.Alpha, 0.02f);
    }

    /// <summary>
    /// And what rides it stays posed: the flame on the torch and the torch on the hand, as the holder
    /// turns — the holder's riders pass carries both (it recurses), after its carried things'
    /// ticks; the torch, riding the hand, poses nothing itself.
    /// </summary>
    [Fact]
    public void The_carried_torch_and_its_flame_stay_posed_on_the_hand()
    {
        using var files = Install.Open();
        var (place, christof, sim, _) = Monastery(files);
        var torch = OnTheHand(files, place, christof).Thing!;
        var flame = place.Place("torchFlame_nosave", torch.Position, torch.Orientation, torch.Sector)!;
        var offset = new Vector3(0f, 0f, 10f);
        Assert.True(Attachments.Attach(torch, flame, -1, offset, Matrix4x4.Identity, 0));
        var tag = christof.Animation!.Set.Tags.Single(t => t.Index == 30);

        for (int i = 0; i < 20; i++)
        {
            christof.Orientation += new Vector3(0f, 0f, 9f);
            sim.Step(Frame);
            var hand = (christof.Animation.BoneMatrix(tag.Bone) * christof.World).Translation;
            Assert.True(Vector3.Distance(hand, torch.Position) < 40f, $"frame {i}: the torch is at {torch.Position}, the hand at {hand}");
            Assert.True(Vector3.Distance(Vector3.Transform(offset, torch.World), flame.Position) < 0.5f,
                        $"frame {i}: the flame is at {flame.Position}, the torch's point at {Vector3.Transform(offset, torch.World)}");
        }
        Assert.Same(torch, flame.AttachedTo);
    }
}
