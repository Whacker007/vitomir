// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **The Fix `wakeonhit` on the soldiers it was built for**: one of the Society of Leopold's second
/// level, held by the shipped `ActorAIOff_TriggerOn` (`THING_AF_AIPAUSED` from its `beginscene`), hurt by
/// Christof with the Fix read from the place's own switches (<c>Location.Fixes</c>) — never the process's,
/// which every other test running beside this one reads (`QolTests.No_test_in_this_assembly_installs_a_register`).
/// </summary>
[Collection("scripts")]
public class WakeOnHitScriptTests(Scripts s)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_soldier_the_level_holds_is_let_go_by_a_party_members_shot_only_with_the_fix(bool fix)
    {
        using var m = Place.Enter(s, "L1_SOC2", "SOC2_27_2");
        var held = m.Where.Things.Where(t => t.Template.Equals("leosoldier", StringComparison.OrdinalIgnoreCase)
                                             && t.Actor is { } a && a.Has(Actor.Flag.AiPaused))
                                 .OrderBy(t => System.Numerics.Vector3.Distance(t.Position, m.Christof.Position)).First();
        m.Christof.Actor!.Flags |= Actor.Flag.Party;
        m.Where.Fixes = new Fixes { WakeOnHit = fix };
        Damage.Apply(held, m.Christof, HitKind.Projectile, DamageType.Lethal, 5f, new Rng(1), m.Where.Milliseconds);
        Assert.Equal(!fix, held.Actor!.Has(Actor.Flag.AiPaused));
        Assert.Same(m.Christof, held.Mind!.Target);
        var from = held.Position;
        m.Run(4f);
        bool acted = System.Numerics.Vector3.Distance(from, held.Position) > 20f || held.Actor.Action != ActionMode.None;
        Assert.Equal(fix, acted);
    }
}
