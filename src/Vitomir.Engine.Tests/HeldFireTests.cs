// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Held automatic fire**: at an attack animation's `Repeat` tag, with a `REPEATABLE` attack
/// and the left or middle button down, the steered character turns to what the pointer is on and
/// the animation jumps back to its `RepeatTo` tag, busy for another reload —
/// one shot a loop for as long as the button is held. Earlier, Vitomir fired one shot a click,
/// because nothing answered tag 16. Christof's Uzi: `RepeatTo` at 9, `Fire` at 10.67, `Repeat` at 19
/// of 39 frames, played to last its 1.333 s strike time. Also the multiplayer server's copy of the
/// check, which tests `REPEATABLE` and read the ranged flag until then.
/// </summary>
public class HeldFireTests
{
    static (Location Place, Simulation Sim, WorldThing Christof) Open(GameFiles files, string weapon = "submachineGun")
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        foreach (var m in minds.All) if (m.Thing.Actor is { } a) a.Flags |= Actor.Flag.AiPaused;
        var christof = place.Steered!;
        var gun = Weapon.From(weapon, place.Templates!.Resolve(weapon))!;
        foreach (var attack in gun.Attacks) attack.Ammo = attack.MaxAmmo = 1000;
        christof.Actor!.Weapon = gun;
        return (place, new Simulation(place), christof);
    }

    static void Run(Simulation sim, int ms)
    {
        for (int t = 0; t < ms; t += 33) sim.Step(1f / 30f);
    }

    /// <summary>Shots fired, from the rounds spent (the Uzi spends three a shot).</summary>
    static int Spent(WorldThing who) => (1000 - who.Actor!.Weapon!.Current.Ammo) / who.Actor.Weapon.Current.AmmoUse;

    /// <summary>
    /// The loop: the button held, the Uzi goes on firing a shot every ten frames and the attack
    /// does not end; let go, and the animation plays out, the reload runs down and it is over.
    /// </summary>
    [Fact]
    public void Held_down_an_Uzi_keeps_firing_until_the_button_is_let_go()
    {
        using var files = Install.Open();
        var (place, sim, christof) = Open(files);
        var floor = christof.Position + new Vector3(0f, 300f, 0f);
        christof.Actor!.Pointing = new HeldFire.Pointer(true, null, floor);
        Actions.StrikeAt(christof, floor, christof.Actor.Weapon!, place.Milliseconds, place.Motions);

        Run(sim, 3000);
        // A shot at 0.36 s and one every 0.34 s after: eight or nine in three seconds, against the
        // single one a click fired before.
        Assert.InRange(Spent(christof), 7, 10);
        Assert.Equal(ActionMode.Swing, christof.Actor.Action);

        christof.Actor.Pointing = new HeldFire.Pointer(false, null, floor);
        int shots = Spent(christof);
        Run(sim, 2000);
        Assert.Equal(shots, Spent(christof));
        Assert.Equal(ActionMode.None, christof.Actor.Action);
    }

    /// <summary>
    /// Fired at the floor, each loop takes the ground under the pointer as the new point and turns
    /// to it (the held fire's `collidePos` branch) — the stream of fire follows the mouse.
    /// </summary>
    [Fact]
    public void Fire_at_the_floor_follows_the_pointer()
    {
        using var files = Install.Open();
        var (place, sim, christof) = Open(files);
        var ahead = christof.Position + new Vector3(0f, 300f, 0f);
        christof.Actor!.Pointing = new HeldFire.Pointer(true, null, ahead);
        Actions.StrikeAt(christof, ahead, christof.Actor.Weapon!, place.Milliseconds, place.Motions);
        Run(sim, 500);

        var aside = christof.Position + new Vector3(300f, 0f, 0f);
        christof.Actor.Pointing = new HeldFire.Pointer(true, null, aside);
        Run(sim, 500);
        Assert.Equal(aside, christof.Actor.ActionPoint);
        Assert.Equal(-90f, christof.Orientation.Z, 1);                   // facing +x
    }

    /// <summary>
    /// The conditions, one at a time, at the tag itself: an attack on a thing loops only while the
    /// pointer is on that thing; nobody but the steered character loops; no button, no loop; and
    /// an attack without `REPEATABLE` — the shotgun, ranged as it is — fires once a click.
    /// </summary>
    [Fact]
    public void Only_the_steered_character_with_the_button_on_its_target_loops()
    {
        using var files = Install.Open();
        var (place, _, christof) = Open(files);
        var other = place.Minds!.All.Select(m => m.Thing).First(t => !ReferenceEquals(t, christof) && t.Actor is not null);
        int now = place.Milliseconds;
        var me = christof.Actor!;
        Actions.Strike(christof, other, me.Weapon!, now, place.Motions);
        Assert.Equal(ActionMode.Swing, me.Action);

        me.Pointing = new HeldFire.Pointer(true, null, null);            // pointer off the target
        Assert.False(HeldFire.Repeat(christof, now));
        me.Pointing = new HeldFire.Pointer(false, other, null);          // no button
        Assert.False(HeldFire.Repeat(christof, now));
        me.Pointing = new HeldFire.Pointer(true, other, null);
        Assert.True(HeldFire.Repeat(christof, now));
        Assert.Equal(9f, christof.Animation!.Action!.Player.Frame);       // back to `RepeatTo`
        Assert.Equal(now + me.Weapon!.Current.ReloadTime, me.BusyUntil);

        // Somebody the player is not steering.
        other.Actor!.Action = ActionMode.Swing;
        other.Actor.Weapon = me.Weapon;
        other.Actor.Pointing = me.Pointing;
        Assert.False(HeldFire.Repeat(other, now));

        // Not repeatable: the shotgun.
        me.Weapon = Weapon.From("gun2hand", place.Templates!.Resolve("gun2hand"));
        Assert.True(me.Weapon!.Current.Is(WeaponFlag.Ranged));
        Assert.False(HeldFire.Repeat(christof, now));
    }

    /// <summary>
    /// The server's copy (order 0x1e) tests `REPEATABLE` too: a crossbow — ranged, not
    /// repeatable — is refused the aim, an Uzi takes it — and, with no thing named, swings at the point
    /// (the swing; `Net.ServerAim` — earlier, it only turned).
    /// </summary>
    [Fact]
    public void The_servers_aim_order_asks_for_a_repeatable_weapon()
    {
        using var files = Install.Open();
        var (place, _, christof) = Open(files, "crossBow");
        var point = christof.Position + new Vector3(0f, 200f, 0f);
        var order = new NetOrder(christof.Guid, OrderKind.Aim, 0, point, 0);
        Assert.True(christof.Actor!.Weapon!.Current.Is(WeaponFlag.Ranged));
        Assert.Equal(OrderResult.Refused, ServerOrders.Apply(place, christof, order, place.Milliseconds));

        christof.Actor.Weapon = Weapon.From("submachineGun", place.Templates!.Resolve("submachineGun"));
        Assert.Equal(OrderResult.Started, ServerOrders.Apply(place, christof, order, place.Milliseconds));
    }
}
