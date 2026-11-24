// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// Shapeshifting through the game's own scripts: Shape of the Beast cast from
/// `DisciplineShapeOfTheBeast`, whose effect's script (`EffectDiscShapeOfTheBeast`) crouches,
/// calls `ActorShapeShift(GetActorClanShapeTemplate(), 0)` when the crouch is over and
/// `ActorEndShapeShift` when the effect ends or the character is killed; and Mask of 1,000 Faces
/// cast at a monk, whose `EffectDiscMask1000Faces` takes the monk's template a second later with
/// `NOFOLEY + WEAPONLESS`. The classes are the shipped ones; the change
/// itself is pinned in the engine's own `ShapeShiftTests`.
/// </summary>
[Collection("scripts")]
public class ShapeShiftTests(Scripts s)
{
    static Place Monastery(Scripts s)
    {
        var m = Place.Enter(s, "P1_MON1");
        var me = m.Christof.Actor!;
        me.Type = ActorType.Vampire;
        me.Stat[(int)ActorStat.BloodPool] = 100f;
        me.SetBaseStat((int)ActorStat.Blood, 100f);
        me.Stat[(int)ActorStat.Blood] = 100f;
        me.Flags2 |= Actor.Flag2.ScrollCast;                        // `Discipline.Fizzled` never fizzles a scroll
        return m;
    }

    [Fact]
    public void Shape_of_the_Beast_makes_a_Brujah_Christof_a_grey_wolf_and_gives_him_back()
    {
        using var m = Monastery(s);
        var me = m.Christof.Actor!;
        var fists = me.Weapon;
        Assert.Equal("brujah", me.ClanRecord?.Name);
        var beast = DisciplineBook.SetLevel(m.Christof, m.Powers, "ShapeOfTheBeast", 0);
        Assert.NotNull(beast);
        Assert.Equal(OrderResult.Started, Cast.Order(m.Christof, null, null, beast!, InputModifier.Space));
        Assert.True(m.RunUntil(() => m.Christof.Effects.Find("ef_disc_shapeofthebeast") is not null, 5f));
        // All three effects, and the script's two flags, from the start.
        Assert.NotNull(m.Christof.Effects.Find("ef_disc_shapeofthebeast2"));
        Assert.NotNull(m.Christof.Effects.Find("ef_disc_shapeofthebeast3"));
        Assert.True(me.Has(Actor.Flag.NoDiscipline));
        Assert.True(me.Has(Actor.Flag.NoFeed));

        // The crouch runs out and the timer takes the clan's shape: `wolf_grey` for a Brujah.
        Assert.True(m.RunUntil(() => ShapeShift.IsShifted(me), 5f));
        Assert.Equal("wolf_grey", me.ShapeChange!.Template);
        Assert.Equal(0u, me.ShapeChange.Flags);
        Assert.Equal("Wolf.nod", m.Christof.Model, ignoreCase: true);
        Assert.Equal("wolf.nms", m.Christof.MotionSet, ignoreCase: true);
        Assert.Equal("Bite_wolfshape", me.Weapon?.Name);
        Assert.Equal("HEADwolf.nod", me.HeadModel, ignoreCase: true);
        var self = s.Runtime!.NewThingRef("CodexActor", m.Christof.Guid);
        Assert.True(Place.Bln(Place.Call(s, "CodexActor.IsActorShapeShifted", self)));
        // MOVESPEED × 1.2 at the first level (`ef_disc_shapeofthebeast3`).
        Assert.Equal(1.2f, me.MoveSpeedScale, 3);

        // SHAPEOFTHEBEAST_DURATIONS[0] is 15 000 ms; the effect ends and the script ends the shape.
        Assert.True(m.RunUntil(() => m.Christof.Effects.Find("ef_disc_shapeofthebeast") is null, 20f));
        Assert.False(ShapeShift.IsShifted(me));
        Assert.Equal("christof.nod", m.Christof.Model, ignoreCase: true);
        Assert.Same(fists, me.Weapon);
        Assert.Equal("christofH.nod", me.HeadModel, ignoreCase: true);
        Assert.False(me.Has(Actor.Flag.NoDiscipline));
        Assert.False(me.Has(Actor.Flag.NoFeed));
        Assert.Null(m.Christof.Effects.Find("ef_disc_shapeofthebeast2"));
        Assert.Null(m.Christof.Effects.Find("ef_disc_shapeofthebeast3"));
    }

    [Fact]
    public void Killed_as_a_wolf_Christof_falls_in_his_own_shape()
    {
        using var m = Monastery(s);
        var me = m.Christof.Actor!;
        var beast = DisciplineBook.SetLevel(m.Christof, m.Powers, "ShapeOfTheBeast", 0)!;
        Assert.Equal(OrderResult.Started, Cast.Order(m.Christof, null, null, beast, InputModifier.Space));
        Assert.True(m.RunUntil(() => ShapeShift.IsShifted(me), 8f));
        // The effect's script captured him (`CaptureThing`), so it hears `killed` before the death
        // does anything else, and ends the shape.
        Assert.True(Damage.Kill(m.Christof, null, HitKind.Script, DamageType.Normal, m.Where.Milliseconds));
        Assert.False(ShapeShift.IsShifted(me));
        Assert.Equal("christof.nod", m.Christof.Model, ignoreCase: true);
    }

    [Fact]
    public void Mask_of_1000_Faces_gives_Christof_a_monk_s_body_with_empty_hands_and_his_own_voice()
    {
        using var m = Monastery(s);
        var me = m.Christof.Actor!;
        var fists = me.Weapon;
        string foley = m.Christof.Foley;
        var monk = m.Where.Things.FirstOrDefault(t => t.Actor is { } a && a.Has(Actor.Flag.ValidShape)
                                                     && !a.Has(Actor.Flag.Dead) && t.Template.Contains("monk", StringComparison.OrdinalIgnoreCase));
        Assert.SkipWhen(monk is null, "no monk in the Monastery's scene");
        var mask = DisciplineBook.SetLevel(m.Christof, m.Powers, "Mask1000Faces", 0)!;
        me.Stat[(int)ActorStat.Wits] = 30f;                          // the row's requirement: Wits 30 (game.ndd)
        m.Where.Move(m.Christof, monk!.Position + new System.Numerics.Vector3(80f, 0f, 0f));
        Assert.True(Cast.InRange(mask, m.Christof, monk.Position));
        Assert.True(Cast.CanCast(m.Christof, mask));
        Assert.True(DisciplineTarget.MayTarget(mask.Definition, m.Christof, monk));
        Assert.Equal(OrderResult.Started, Cast.Order(m.Christof, monk, null, mask, InputModifier.Space));
        Assert.True(m.RunUntil(() => m.Christof.Effects.Find("ef_disc_mask1000faces") is not null, 8f));
        // The effect keeps the monk's GUID in its int parameter and the monk's team as its value.
        Assert.Equal(monk!.Guid, m.Christof.Effects.Find("ef_disc_mask1000faces")!.IntParam);

        // A second later: the monk's template, NOFOLEY + WEAPONLESS.
        Assert.True(m.RunUntil(() => ShapeShift.IsShifted(me), 3f));
        Assert.Equal(monk.Template, me.ShapeChange!.Template, ignoreCase: true);
        Assert.Equal(ShapeShift.NoFoley | ShapeShift.Weaponless, me.ShapeChange.Flags);
        Assert.Equal(monk.Model, m.Christof.Model, ignoreCase: true);
        Assert.Equal(monk.MotionSet, m.Christof.MotionSet, ignoreCase: true);
        Assert.Equal(foley, m.Christof.Foley);
        Assert.Null(me.Weapon);
        Assert.True(me.Has(Actor.Flag.NoDiscipline));

        // MASK1000FACES_DURATIONS[0] is 15 000 ms.
        Assert.True(m.RunUntil(() => m.Christof.Effects.Find("ef_disc_mask1000faces") is null, 20f));
        Assert.False(ShapeShift.IsShifted(me));
        Assert.Equal("christof.nod", m.Christof.Model, ignoreCase: true);
        Assert.Same(fists, me.Weapon);
        Assert.False(me.Has(Actor.Flag.NoDiscipline));
    }

    [Fact]
    public void The_shape_natives_answer_as_the_engine_does()
    {
        using var m = Monastery(s);
        var me = m.Christof.Actor!;
        var self = s.Runtime!.NewThingRef("CodexActor", m.Christof.Guid);
        // The clan's shape column, lower case as the loader read it.
        Assert.Equal("wolf_grey", Place.Call(s, "CodexActor.GetActorClanShapeTemplate", self)?.ToString());
        Assert.False(Place.Bln(Place.Call(s, "CodexActor.IsActorShapeShifted", self)));
        // No such template: false before anything is touched.
        Assert.False(Place.Bln(Place.Call(s, "CodexActor.ActorShapeShift", self, "noSuchTemplate", 0)));
        Assert.True(Place.Bln(Place.Call(s, "CodexActor.ActorShapeShift", self, "wolf_red", 0)));
        Assert.Equal("wolf_red.nod", m.Christof.Model, ignoreCase: true);
        // A second shape over the first: false, and nothing changes (not forced).
        Assert.False(Place.Bln(Place.Call(s, "CodexActor.ActorShapeShift", self, "wolf_white", 0)));
        Assert.Equal("wolf_red.nod", m.Christof.Model, ignoreCase: true);
        Assert.True(Place.Bln(Place.Call(s, "CodexActor.IsActorShapeShifted", self)));
        Assert.True(Place.Bln(Place.Call(s, "CodexActor.ActorEndShapeShift", self)));
        Assert.False(Place.Bln(Place.Call(s, "CodexActor.ActorEndShapeShift", self)));
        Assert.False(ShapeShift.IsShifted(me));
        // Not a character: false, all three.
        var prop = s.Runtime.NewThingRef("CodexActor", m.Where.Things.First(t => t.Type == ThingType.Prop).Guid);
        Assert.False(Place.Bln(Place.Call(s, "CodexActor.ActorShapeShift", prop, "wolf_red", 0)));
        Assert.False(Place.Bln(Place.Call(s, "CodexActor.ActorEndShapeShift", prop)));
        Assert.False(Place.Bln(Place.Call(s, "CodexActor.IsActorShapeShifted", prop)));
    }

    /// <summary>
    /// **The moment of the change** (*"it kind of abruptly faded into the wolf form as
    /// Christof started to kneel down on all 4s"*). `EffectDiscShapeOfTheBeast.effectstarted` fades
    /// the model out over 1,500 ms and plays the crouch (Christof's `ChrisDisciplineWolf`, 44 frames
    /// at 30 a second: `PlayMotionSetMode` answers 1,466 ms) — and sets its timer
    /// to `PlayMotionSetMode(...) / 1000`, which is **Java's integer division** of an `int`: one
    /// second, not 1.47. So the shape goes on two-thirds of the way down the crouch, with Christof
    /// still a third visible, and the wolf starts from nothing (`SetAlpha(0)`) and takes two seconds
    /// to come in. The script's timing, and so 1.1's; Vitomir does the same. And the caster is left in
    /// ACTION 13 until the next order, as 1.1 leaves it.
    /// </summary>
    [Fact]
    public void Shape_of_the_Beast_changes_one_second_into_the_crouch_as_its_script_s_integer_division_asks()
    {
        using var m = Monastery(s);
        var me = m.Christof.Actor!;
        var beast = DisciplineBook.SetLevel(m.Christof, m.Powers, "ShapeOfTheBeast", 0)!;
        var crouch = m.Where.Motions!.Find(m.Christof.Animation!.Set, "crouch")!.Value;
        int crouchMs = (int)(crouch.Animation.Duration / 30f * 1000f);
        Assert.InRange(crouchMs, 1400, 1500);

        Assert.Equal(OrderResult.Started, Cast.Order(m.Christof, null, null, beast, InputModifier.Space));
        Assert.True(m.RunUntil(() => m.Christof.Effects.Find("ef_disc_shapeofthebeast") is not null, 5f));
        int started = m.Where.Milliseconds;
        Assert.Equal("crouch", m.Christof.Animation!.Action?.Mode.Name);
        Assert.True(m.Christof.Model3D.AlphaRamping);

        Assert.True(m.RunUntil(() => ShapeShift.IsShifted(me), 3f));
        int changed = m.Where.Milliseconds - started;
        Assert.InRange(changed, 1000, 1000 + 2 * 34);                              // a second, to the frame
        Assert.True(changed < crouchMs);
        // The old model, kept in the record, a third faded (1 − 1000/1500) — and the wolf from nothing.
        Assert.InRange(me.ShapeChange!.Fx!.Alpha, 0.25f, 0.4f);
        Assert.InRange(m.Christof.Model3D.Alpha, 0f, 0.05f);
        Assert.True(m.Christof.Model3D.AlphaRamping);
        m.Run(1f);
        Assert.InRange(m.Christof.Model3D.Alpha, 0.45f, 0.6f);                    // half way at a second
        m.Run(1.1f);
        Assert.Equal(1f, m.Christof.Model3D.Alpha);

        // And the caster is still in ACTION 13, the cast, long after it fired and the script's
        // `StopActorAction` stopped only the model's action channel: the action
        // tick has no case 13. The next order's cancel sets it back (case 0xd).
        Assert.Null(m.Christof.Animation!.Action);
        Assert.Equal(ActionMode.Cast, me.Action);
        Actions.Cancel(m.Christof);
        Assert.Equal(ActionMode.None, me.Action);
    }
}
