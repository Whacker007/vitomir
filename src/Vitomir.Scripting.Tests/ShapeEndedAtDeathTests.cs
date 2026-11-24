// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **A body given back as its wearer dies is animated** (Count Orsi, killed
/// as a wolf, *"was invisible"* through the scene in which Christof throws him out of the window).
/// `EffectDiscShapeOfTheBeast.killed` ends the shape and fades the body in over two seconds;
/// `FAC4_Main.killed` refuses the death and plays `MOTION_SPECIAL1` on him. Each change of shape makes
/// the thing's geometry again (`Location.Reshape`), which takes its entry out of the mesh's animated
/// list and appends a new one — and the simulation's index of that list counted only appended entries,
/// so the body he came back to was never advanced: he stood in his first pose while Christof threw
/// the air.
/// </summary>
[Collection("scripts")]
public class ShapeEndedAtDeathTests(Scripts s)
{
    [Fact]
    public void Orsi_killed_as_a_wolf_is_thrown_in_his_own_body()
    {
        using var m = Place.Enter(s, "n1_fac4", "FAC4_37_4");
        var orsi = m.Where.Things.First(t => t.Template.Equals("orsimod", StringComparison.OrdinalIgnoreCase));
        string own = orsi.Model;
        EffectRuntime.Apply(orsi, "ef_disc_shapeofthebeast", 0, durationMs: 60_000, creator: orsi);
        m.Run(4f);
        Assert.NotEqual(own, orsi.Model);                                   // the wolf
        float wolfFrame = orsi.Animation!.Motion!.Player.Frame;
        m.Run(0.5f);
        Assert.NotEqual(wolfFrame, orsi.Animation!.Motion!.Player.Frame);    // which is animated too

        Damage.Kill(orsi, m.Christof, HitKind.Melee, DamageType.Lethal, m.Where.Milliseconds);
        Assert.False(orsi.Actor!.Dead);                                     // FAC4_Main refuses the death
        Assert.Equal(own, orsi.Model);                                      // and the effect's script gave the body back
        m.Run(2.5f);
        Assert.Equal("special1", orsi.Animation?.Action?.Mode.Name);
        Assert.True(orsi.Animation!.Action!.Player.Frame > 20f, $"the toss stands at frame {orsi.Animation.Action.Player.Frame}");
        Assert.Equal(1f, orsi.ModelAlpha);                                  // faded in (SetAlpha(1, 2000))
    }
}
