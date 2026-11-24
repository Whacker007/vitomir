// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Taking over another member of the party (the take-over and the three ways in: F6–F9,
/// Tab and a click on the portrait). The party's characters can be switched between,
/// as in 1.1.
/// </summary>
public class SteeringTests
{
    /// <summary>The University with Christof and Wilhem both in the party, the way a script puts them there.</summary>
    static (Location Place, Minds Minds, Mind Wilhem, Mind Christof) Party(GameFiles files)
    {
        var place = Location.Load(files, "P1_UNIV", things: true, sky: false, scene: "UNIV_7_7", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var christof = minds.All.First(m => ReferenceEquals(m.Thing, place.Player!.Thing));
        var wilhem = minds.All.First(m => !ReferenceEquals(m, christof) && m.Thing.Type == ThingType.Player);
        foreach (var m in new[] { christof, wilhem })
        {
            m.Thing.Actor!.Flags |= Actor.Flag.Party;
            m.Thing.Actor.Flags2 |= Actor.Flag2.InFormation;
            m.Enter(AiMode.Party);
        }
        return (place, minds, wilhem, christof);
    }

    /// <summary>
    /// F6 to F9 are the party's slots, and the slots do not move when somebody else is
    /// steered: F6 is Christof, F7 the next to have joined. The walker the mouse drives is the new
    /// one's own, and the white shell flashes over it as it is taken.
    /// </summary>
    [Fact]
    public void F6_to_F9_take_over_the_party_by_slot()
    {
        using var files = Install.Open();
        var (place, minds, wilhem, christof) = Party(files);
        var told = new List<WorldThing>();
        place.SteeredChanged += told.Add;
        Assert.Same(christof.Thing, place.Steered);

        Assert.True(place.SteerSlot(1));
        Assert.Same(wilhem.Thing, place.Steered);
        Assert.Same(wilhem.Thing, minds.Steered);
        Assert.Same(wilhem.Walker, place.SteeredWalker);
        Assert.Equal(Location.SteerShell, wilhem.Thing.Model3D.Shell);
        Assert.Equal(ModelFx.ShellInOut, wilhem.Thing.Model3D.ShellFlags);
        Assert.Equal([christof.Thing, wilhem.Thing], place.PartyMembers().ToArray());   // unchanged by it

        Assert.False(place.SteerSlot(2));                  // nobody in the third slot
        Assert.Same(wilhem.Thing, place.Steered);
        Assert.True(place.SteerSlot(0));
        Assert.Same(christof.Thing, place.Steered);
        Assert.Same(place.Player, place.SteeredWalker);
        Assert.Equal([wilhem.Thing, christof.Thing], told);
    }

    /// <summary>
    /// Only a player-type character in the party can be taken over — a
    /// townsperson, or a companion who has left, cannot.
    /// </summary>
    [Fact]
    public void Only_a_party_member_can_be_taken_over()
    {
        using var files = Install.Open();
        var (place, minds, wilhem, christof) = Party(files);
        var stranger = minds.All.First(m => m.Thing.Type == ThingType.Actor).Thing;
        Assert.False(place.Steer(stranger));
        wilhem.Thing.Actor!.Flags &= ~Actor.Flag.Party;
        Assert.False(place.Steer(wilhem.Thing));
        Assert.Same(christof.Thing, place.Steered);
    }

    /// <summary>
    /// Tab is the next member round, and it does **not** pass over the
    /// dead — a fallen companion can be taken over by Tab, and so can nobody else's.
    /// </summary>
    [Fact]
    public void Tab_goes_round_the_party_dead_or_alive()
    {
        using var files = Install.Open();
        var (place, _, wilhem, christof) = Party(files);
        Assert.Same(wilhem.Thing, place.SteerTab(() => 1f));
        Assert.Same(christof.Thing, place.SteerTab(() => 1f));
        wilhem.Thing.Actor!.Flags |= Actor.Flag.Dead;
        Assert.Same(wilhem.Thing, place.SteerTab(() => 1f));
    }

    /// <summary>
    /// **The one let go follows the one taken.** The party follows whoever is steered (the follow's
    /// follow reads the selected member), so after F7 it is Christof who walks after Wilhem.
    /// </summary>
    [Fact]
    public void The_one_let_go_follows_the_one_taken()
    {
        using var files = Install.Open();
        var (place, minds, wilhem, christof) = Party(files);
        place.Move(wilhem.Thing, christof.Thing.Position + new Vector3(0f, 0f, 0f));
        wilhem.Walker.Ground();
        Assert.True(place.SteerSlot(1));

        // Wilhem is the player's now: walk him off with a click, the way the app would.
        var spot = wilhem.Thing.Position + new Vector3(500f, 0f, 0f);
        int node = place.Walk!.Find(spot);
        Assume(node >= 0, "no floor 500 east of the start");
        var there = spot with { Z = place.Walk.Nodes[node].Height(spot.X, spot.Y) };
        Assert.True(place.SteeredWalker!.WalkTo(there, node));
        var from = christof.Thing.Position;
        for (int i = 0; i < 30 * 6; i++)
        {
            minds.Tick(1f / 30f);
            minds.Follow(1f / 30f);
            if (place.SteeredWalker is { Walking: true } w) w.Follow(1f / 30f);
        }
        Assert.True(Vector3.Distance(christof.Thing.Position, from) > 100f,
                    $"Christof stayed at {christof.Thing.Position}");
        Assert.True(Vector3.Distance(christof.Thing.Position, wilhem.Thing.Position)
                    < Vector3.Distance(from, wilhem.Thing.Position));
    }

    /// <summary>
    /// **Control passes on when the steered one falls**: with a
    /// companion still standing the game goes on through them. And when they leave the party
    /// the same. With nobody left standing the search asks for the party's end
    /// and control stays where it was.
    /// </summary>
    [Fact]
    public void Control_passes_on_when_the_steered_one_falls_or_leaves()
    {
        using var files = Install.Open();
        var (place, _, wilhem, christof) = Party(files);
        var chronicle = ChronicleSet.Load(files).ForLevel("P1_UNIV")!.Value.Chronicle;
        place.Session = new Session { Here = place, Chronicle = chronicle };
        Damage.Kill(christof.Thing, null, HitKind.Melee, DamageType.Lethal, nowMs: place.Milliseconds);
        Assert.Same(wilhem.Thing, place.Steered);
        Assert.Null(chronicle.Pending);

        Damage.Kill(wilhem.Thing, null, HitKind.Melee, DamageType.Lethal, nowMs: place.Milliseconds);
        Assert.Same(wilhem.Thing, place.Steered);
        Assert.Equal(new SequenceRequest(SequenceKind.EndGame, Reason: Chronicle.EndPartyDead), chronicle.Pending);

        // Tab still goes round — it passes nobody over, so it never finds the party gone.
        chronicle.Take();
        place.SteerTab(() => 1f);
        Assert.Same(christof.Thing, place.Steered);
        Assert.Null(chronicle.Pending);
    }

    /// <summary>
    /// A plain click on a portrait: with nothing on the cursor it takes that character
    /// over; with something on it, it hands it over instead — within 400 units, into their pack.
    /// </summary>
    [Fact]
    public void A_click_on_a_portrait_takes_over_or_hands_over()
    {
        using var files = Install.Open();
        var (place, _, wilhem, christof) = Party(files);
        var bar = new StatusBar(new UiAssets(files).Pane(StatusBar.PaneName), place);
        place.Move(wilhem.Thing, christof.Thing.Position + new Vector3(80f, 0f, 0f));
        bar.Update();
        int row = bar.PartyThings.ToList().IndexOf(wilhem.Thing);
        Assert.Equal(1, row);                               // slot order: Christof's row first
        var hotspot = bar[$"statusHotspot{row}"]!;

        // Something in Christof's hand: Wilhem is given it, and nobody changes hands.
        var potion = place.Make("potionhealth")!;
        var carried = potion.Item!;
        potion.Owner = christof.Thing;
        christof.Thing.Actor!.Inventory.Add(carried);
        Assert.True(Ground.Hand(christof.Thing, carried));
        bar.Click(hotspot.X + 1, hotspot.Y + 1);
        Assert.Contains(carried, wilhem.Thing.Actor!.Inventory.Items);
        Assert.Null(christof.Thing.Actor.Held);
        Assert.Same(christof.Thing, place.Steered);

        // An empty hand takes him over.
        bar.Click(hotspot.X + 1, hotspot.Y + 1);
        Assert.Same(wilhem.Thing, place.Steered);
        Assert.Same(wilhem.Thing.Actor, bar.Selected);
        Assert.Equal(1, bar.Party.ToList().IndexOf(wilhem.Thing.Actor!));    // the rows do not move
    }

    static void Assume(bool condition, string why) { if (!condition) Assert.Fail(why); }
}
