// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the director's bow steps, a partial of the director work's ScenarioDirector (it replaces the director work's stand-in).
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Control;

public sealed partial class ScenarioDirector
{
    /// <summary>
    /// One tick of a bow step for actor <paramref name="a"/>: each writes the archer's <see cref="BowIntent"/> — the same
    /// intent the player's controls will write — and the <see cref="BowSystem"/> admits it or not; nothing is loosed or met by a
    /// shortcut. <see cref="BowIntent"/> is no channel of the director's and nobody else writes it, so what a step sets there stands
    /// after it ends (the bow stays up, the draw held, through a <c>wait</c>), until another bow step changes it.
    /// <list type="bullet">
    /// <item><see cref="EquipStep"/>: the bow in hand (<c>ranged</c>) or put away (<c>melee</c>: aim and draw let go too); instant.
    /// Refused <see cref="StepRefusal.NoBow"/> for <c>ranged</c> when the actor carries none.</item>
    /// <item><see cref="AimStep"/>: the bow in hand and up, the aim held — on a region of an actor (its <see cref="ScenarioAim.Actor"/>,
    /// else the foe; the side nearer the archer when none is given, as a blow's aim), kept on it by the bow each tick with the arrow's
    /// drop allowed for; on a point (the pitch solved once, at the step's start); or by angles (the yaw from the body's facing). The body
    /// is turned to the aim while the step runs. Done once an arrow is nocked; refused <see cref="StepRefusal.NoArrows"/> with an empty
    /// quiver and none on the string.</item>
    /// <item><see cref="DrawStep"/>: the draw held (aim mode too, if the bow was not up) to <see cref="DrawStep.ToShare"/> of full; done
    /// when the draw reaches it (it is then held there).</item>
    /// <item><see cref="LooseStep"/>: the draw let go; done when the arrow has left (<see cref="ArrowLoosed"/>, the same tick); refused
    /// <see cref="StepRefusal.NotDrawn"/> with nothing drawn; timed out when the draw was too little and was let down instead.</item>
    /// <item><see cref="CancelDrawStep"/>: the string let down (the arrow stays nocked, the bow up); instant.</item>
    /// </list>
    /// While a bow step runs with the bow up, the body faces the bow's aim (<see cref="Bow.AimYaw"/>).
    /// </summary>
    StepResult RunBow(World world, ActorRun a, Track t, ScenarioStep step, bool first)
    {
        var e = a.Entity;
        bool has = world.TryStore<Bow>(out var bows) && bows.Has(e) && world.TryStore<BowIntent>(out _);
        if (!has)
            return step is EquipStep { What: EquipWhat.Melee } or CancelDrawStep ? StepResult.Done : StepResult.Refused(StepRefusal.NoBow);
        ref var bow = ref bows!.Get(e);
        ref var intent = ref world.Get<BowIntent>(e);
        a.Written |= ControlChannels.Attack;   // the bow's in hand: the melee attack is the bow's (BowSystem clears it)
        switch (step)
        {
            case EquipStep eq:
                intent.Ranged = eq.What == EquipWhat.Ranged;
                if (!intent.Ranged) { intent.Aim = false; intent.Draw = false; intent.AimsAt = false; intent.AimGiven = false; }
                return StepResult.Done;

            case AimStep aim:
                if (first)
                {
                    if (!bow.OnString && bow.Arrows <= 0) return StepResult.Refused(StepRefusal.NoArrows);
                    intent.Ranged = true;
                    intent.Aim = true;
                    if (!AimBowAt(world, a, ref intent, aim.At)) return StepResult.Refused(StepRefusal.NoTarget);
                }
                FaceBow(world, a, in bow);
                if (bow.State is BowState.Nocked or BowState.Drawing or BowState.Held) return StepResult.Done;
                if (bow.State == BowState.Ready && !bow.OnString && bow.Arrows <= 0) return StepResult.Refused(StepRefusal.NoArrows);
                return StepResult.Running;

            case DrawStep draw:
                if (first)
                {
                    if (!bow.OnString && bow.Arrows <= 0) return StepResult.Refused(StepRefusal.NoArrows);
                    intent.Ranged = true;
                    intent.Aim = true;
                    intent.Draw = true;
                    intent.DrawToShare = Math.Clamp(draw.ToShare, 0.01f, 1f);
                }
                FaceBow(world, a, in bow);
                if (bow.Drawn && bow.Share >= intent.DrawToShare - 1e-4f) return StepResult.Done;
                return StepResult.Running;

            case LooseStep:
                if (first)
                {
                    if (!bow.Drawn) return StepResult.Refused(StepRefusal.NotDrawn);
                    t.Run.Index = bow.Shots;
                    intent.Loose = true;
                    intent.Draw = false;
                    return StepResult.Running;
                }
                if (bow.Shots > t.Run.Index) return StepResult.Done;
                if (!bow.Drawn) return StepResult.TimedOut("the draw was too little to loose: let down");
                return StepResult.Running;

            case CancelDrawStep:
                intent.LetDown = bow.Drawn;
                intent.Draw = false;
                return StepResult.Done;
        }
        return StepResult.Done;
    }

    /// <summary>Sets the bow's aim from a step's <see cref="ScenarioAim"/>: on a region of an actor (held by the bow), a point, or angles; false when it names an actor not in the world.</summary>
    bool AimBowAt(World world, ActorRun a, ref BowIntent intent, ScenarioAim aim)
    {
        ref var body = ref bodies.Find(a.Entity);
        if (Unsafe.IsNullRef(ref body)) return false;
        intent.AimsAt = false;
        intent.AimGiven = false;
        if (aim.Region is not null)
        {
            var who = aim.Actor is { } name ? Entity(name) : a.Foe;
            if (who.IsNone || !world.Alive(who)) return false;
            AimPoint(a.Entity, in body, aim with { Actor = null }, who, out _, out var region);
            intent.AimsAt = true;
            intent.AimAt = who;
            intent.AimRegion = region == BodyRegion.None ? BodyRegion.Chest : region;
            return true;
        }
        if (aim.Point is { } p && world.TryService<BowConfig>(out var config) && world.TryStore<Bow>(out var bows) && (uint)bows.Get(a.Entity).Weapon < (uint)config.Bows.Count)
        {
            var d = config.Bows[bows.Get(a.Entity).Weapon];
            var to = new Vector3(p.X, p.Y, p.Z ?? body.Feet.Z + 1.2f);
            float yaw = DetMath.Atan2(to.Y - body.Feet.Y, to.X - body.Feet.X);
            var from = BowServices.ReleasePoint(world, a.Entity, in body, yaw, d);
            intent.AimGiven = true;
            intent.AimYaw = DetMath.Atan2(to.Y - from.Y, to.X - from.X);
            intent.AimPitch = Ballistics.SolvePitch(from, to, d.SpeedAt(1f), d.ArrowDragPerMetre);
            return true;
        }
        intent.AimGiven = true;
        intent.AimYaw = DetMath.WrapAngle(body.Yaw + (aim.YawDegrees ?? 0f) * DetMath.DegToRad);
        intent.AimPitch = (aim.PitchDegrees ?? 0f) * DetMath.DegToRad;
        return true;
    }

    /// <summary>Turns the archer to the bow's aim while a bow step runs with the bow up (the face channel).</summary>
    void FaceBow(World world, ActorRun a, in Bow bow)
    {
        if (bow.State < BowState.Raising) return;
        ref var m = ref MoveOf(a, ControlChannels.Face);
        if (Unsafe.IsNullRef(ref m)) return;
        FaceAngle(a, ref m, DetMath.WrapAngle(bow.AimYaw - BowShots.TurnOf(world, a.Entity)));   // the drawn arrow along the aim (0 for the look)
    }
}
