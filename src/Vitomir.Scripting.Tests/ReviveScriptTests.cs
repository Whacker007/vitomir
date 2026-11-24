// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **A revive resets the mind** (as the original's revive does), played through
/// the shipped <c>Pedestrian</c> script: every <c>beginscene</c> it revives its townsman if he is
/// dead (<c>ReviveActor(100, 100)</c>). Earlier, Vitomir's revive left the mind as the death found
/// it, so a townsman killed while running from Christof got up still afraid of him.
/// </summary>
[Collection("scripts")]
public class ReviveScriptTests(Scripts s)
{
    [Fact]
    public void A_townsman_revived_as_the_scene_begins_again_has_forgotten_his_killer()
    {
        using var m = Place.Enter(s, "P1_OLDT", "OLDT_7_1");
        var minds = m.Where.Minds!;
        var ped = m.Where.Things.FirstOrDefault(t => string.Equals(t.Script, "Pedestrian", StringComparison.OrdinalIgnoreCase)
                                                     && t.Mind is not null && t.Actor is { Dead: false });
        Assert.SkipWhen(ped is null, "no Pedestrian in Old Town by night");
        var mind = ped!.Mind!;
        var home = mind.BaseMode;

        // Running from Christof, marked as a behead marks its victim, with an approach pending.
        mind.Enter(AiMode.Afraid, andBase: false);
        minds.SetTarget(mind, m.Christof);
        mind.Flags |= Mind.FlagUnaffected;
        mind.ApproachUntil = int.MaxValue;
        mind.NextThink = int.MaxValue;
        Assert.Same(m.Christof, mind.Target);
        Assert.True(Damage.Kill(ped, m.Christof, HitKind.Melee, DamageType.Lethal, m.Where.Milliseconds));

        // The scene ends and begins again, with the same scripts: `Pedestrian.beginscene` revives him.
        m.Scenes.Leave(0, keep: true);
        m.Scenes.Enter(m.Where);

        Assert.False(ped.Actor!.Dead);
        Assert.Equal(home, mind.Mode);                         // straight back to the home mode
        Assert.Null(mind.Target);                              // his killer forgotten
        Assert.Equal(0u, mind.Flags);                          // every AI flag but "noticed" gone
        Assert.Equal(0, mind.ApproachUntil);
        Assert.True(mind.NextThink <= minds.Now, $"thinks at {mind.NextThink}, now {minds.Now}");
    }
}
