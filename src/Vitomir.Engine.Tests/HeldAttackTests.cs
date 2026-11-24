// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Holding the left button repeats the attack**. Holding down the left button counts
/// as auto repeat attack in the base game. The end of a swing waits out the
/// busy time the strike set — the attack's RELOADTIME, over the attack speed for a swing —
/// and then orders the attack again at whatever is under the pointer, or cancels. 1.1's click
/// handler acts only on the frame a button goes down, so the held repeat is this and nothing
/// else. Pinned here: the next swing comes on the tick the reload runs out and not before; nobody asking
/// ends it; a target that has died ends it with the button down; and the middle button, while
/// <c>[qol] camera</c> makes it the camera's drag (1.1's X), is no click held — Vitomir swung the second
/// attack under a swivel until then.
/// </summary>
public class HeldAttackTests
{
    static readonly int Left = KeyChord.MouseBase + 1, Middle = KeyChord.MouseBase + 2;

    static WorldThing Body(Vector3 at, ThingType type, int guid) => new()
    {
        Index = -1, Guid = guid, Scene = new NsdThing(), Template = "t", Type = type,
        Collide = CollideShape.Cylinder, Radius = 32f, Height = 72f, Position = at,
        Actor = new Actor { Type = ActorType.Human, Health = 100f, MaxHealth = 100f, Team = Actor.Teams.PlayerFirst },
    };

    /// <summary>An enemy in reach: of another team than the attacker's (`Sight.Friendly`).</summary>
    static WorldThing Foe()
    {
        var foe = Body(new Vector3(0f, 60f, 0f), ThingType.Actor, 2);
        foe.Actor!.Team = Actor.Teams.PlayerFirst + 1;
        return foe;
    }

    static Weapon Sword()
    {
        var w = new Weapon { Name = "sword" };
        w.Attacks[0].ReloadTime = 1500;
        return w;
    }

    [Fact]
    public void The_held_swing_comes_again_on_the_tick_the_reload_runs_out()
    {
        var me = Body(Vector3.Zero, ThingType.Player, 1);
        var foe = Foe();
        var sword = me.Actor!.Weapon = Sword();
        // In reach, the order strikes there and then, not a tick later.
        Assert.Equal(OrderResult.Started, Actions.Attack(me, foe, sword, 0, 1000));
        Assert.Equal(ActionMode.Swing, me.Actor.Action);
        Assert.Equal(2500, me.Actor.BusyUntil);                  // now + RELOADTIME / attack speed 1
        me.Actor.Repeating = new Actions.Asking(foe);

        Actions.Tick(me, 2499);                                   // busy: nothing yet
        Assert.Equal(2500, me.Actor.BusyUntil);
        Actions.Tick(me, 2500);                                   // run out: the attack again, at once
        Assert.Equal(ActionMode.Swing, me.Actor.Action);
        Assert.Equal(4000, me.Actor.BusyUntil);

        me.Actor.Repeating = null;                                // let go
        Actions.Tick(me, 4000);
        Assert.Equal(ActionMode.None, me.Actor.Action);
    }

    /// <summary>
    /// **The Fix <c>heldattack</c>**: 1.1 asks the held button only at a swing's end, so an attack held through a cast (busy,
    /// doing nothing) or a flinch is never made. Off, as 1.1: free again, the character stands. On: the attack starts on the
    /// tick it is free, the asked attack (Shift's second) with it, and again after a flinch; let go, nothing.
    /// </summary>
    [Fact]
    public void With_heldattack_on_an_attack_held_through_a_cast_or_a_flinch_starts_the_moment_the_character_is_free()
    {
        foreach (bool on in new[] { false, true })
        {
            var place = new Location
            {
                Name = "held", Level = new NilLevel { Sectors = [] }, Materials = new Scene.MaterialCache(null!),
                Mesh = new Scene.SceneMesh(), Fixes = new Fixes { HeldAttack = on },
            };
            var me = Body(Vector3.Zero, ThingType.Player, 1);
            var foe = Foe();
            me.Place = foe.Place = place;
            var sword = me.Actor!.Weapon = Sword();
            sword.Attacks[1].ReloadTime = 1500;
            sword.AttackCount = 2;
            // A discipline's cast: its action, which lingers after it (1.1 has no tick for it), busy until 2000. The click on
            // the foe was refused as busy; the button held.
            me.Actor.Action = ActionMode.Cast;
            me.Actor.BusyUntil = 2000;
            Assert.Equal(OrderResult.Busy, Actions.Attack(me, foe, sword, 0, 1000));
            me.Actor.Repeating = new Actions.Asking(foe, 1);
            Actions.Tick(me, 1999);
            Assert.Equal(ActionMode.Cast, me.Actor.Action);
            Actions.Tick(me, 2000);
            Assert.Equal(on ? ActionMode.Swing : ActionMode.Cast, me.Actor.Action);
            if (!on) continue;
            Assert.Equal(1, sword.CurrentAttack);                          // the second attack, as asked
            // A blow makes it flinch in the swing: done flinching, the held attack starts again.
            me.Actor.Action = ActionMode.Hurt;
            me.Actor.BusyUntil = 4000;
            Actions.Tick(me, 3999);
            Assert.Equal(ActionMode.Hurt, me.Actor.Action);
            Actions.Tick(me, 4000);
            Assert.Equal(ActionMode.Swing, me.Actor.Action);
            // Let go: free again, it stands.
            me.Actor.Repeating = null;
            Actions.Tick(me, 5500);
            Assert.Equal(ActionMode.None, me.Actor.Action);
            Actions.Tick(me, 6000);
            Assert.Equal(ActionMode.None, me.Actor.Action);
        }
    }

    [Fact]
    public void A_target_that_dies_ends_the_held_swing_with_the_button_still_down()
    {
        var me = Body(Vector3.Zero, ThingType.Player, 1);
        var foe = Foe();
        var sword = me.Actor!.Weapon = Sword();
        Actions.Attack(me, foe, sword, 0, 0);
        me.Actor.Repeating = new Actions.Asking(foe);
        foe.Actor!.Flags |= Actor.Flag.Dead;                      // its verb is no longer attack
        Actions.Tick(me, 1500);
        Assert.Equal(ActionMode.None, me.Actor.Action);
    }

    [Fact]
    public void The_middle_button_is_no_click_held_while_it_is_the_cameras_drag()
    {
        var keys = new KeyBindings();
        Func<int, bool> middle = i => i == Middle, left = i => i == Left;
        Assert.False(keys.HeldClick(KeyCommand.SecondaryClick, middle, KeyMods.None, camera: true));
        Assert.True(keys.HeldClick(KeyCommand.SecondaryClick, middle, KeyMods.None, camera: false));
        Assert.True(keys.HeldClick(KeyCommand.Act, left, KeyMods.None, camera: true));
        // The drag moved to a key gives the middle button back to 1.1's secondary click.
        var moved = new KeyBindings();
        moved.Apply(IniFile.Parse("[keys]\ndragswivel = V\n"));
        Assert.True(moved.HeldClick(KeyCommand.SecondaryClick, middle, KeyMods.None, camera: true));
        // And Held, which the camera's own keys read, still sees the button.
        Assert.True(keys.Held(KeyCommand.SecondaryClick, middle, KeyMods.None));
    }

    [Fact]
    public void The_pick_is_re_run_each_frame_only_while_a_click_is_held_in_the_game_camera()
    {
        Assert.True(HeldPick.Wanted(clickHeld: true, menuUp: false, gameCamera: true));
        Assert.False(HeldPick.Wanted(false, false, true));
        Assert.False(HeldPick.Wanted(true, true, true));
        Assert.False(HeldPick.Wanted(true, false, false));
    }
}
