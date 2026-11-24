// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Two behaviours of the AI that a comparison with 1.1 found in
/// 1.1 and missing from Vitomir, each a few lines: the think clearing its per-think
/// flags word before any gate, which is what makes a frightened or dazed character's
/// heading fresh every think; and the two script switches that stop the searches
/// (<c>AIMAIN_FLAG_NOENEMYSEARCH</c> and <c>AIMAIN_FLAG_NOGUARDSEARCH</c>),
/// which <c>OLDT_7_1</c> and <c>DOCK_33_1_Main</c> set.
/// </summary>
public class AiAuditTests
{
    static (Minds Minds, WorldThing Christof, Mind Mind) Open(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Steered!;
        var mind = minds.All.First(m => !ReferenceEquals(m.Thing, christof) && m.Thing.Actor is not null
                                        && m.Class.MindFlags == 0 && (m.Flags & Mind.FlagUnaffected) == 0);
        return (minds, christof, mind);
    }

    [Fact]
    public void A_dazed_character_takes_its_heading_from_where_its_target_is_now()
    {
        using var files = Install.Open();
        var (minds, christof, mind) = Open(files);
        christof.Position = mind.Thing.Position + new Vector3(200f, 0f, 0f);
        minds.Daze(mind, christof);
        minds.Tick(0.2f);
        Assert.Equal(AiMode.Dazed, mind.Mode);
        Assert.True(mind.Heading.X > 0.9f, $"first heading {mind.Heading}");

        // The target moves a quarter turn round; the walk the daze started is stopped so the next
        // think is not skipped for it.
        christof.Position = mind.Thing.Position + new Vector3(0f, 200f, 0f);
        mind.Walker.Stop();
        minds.Tick(0.6f);
        var toward = Vector3.Normalize(christof.Position - mind.Thing.Position);
        Assert.True(Vector3.Dot(mind.Heading, toward) > 0.95f,
                    $"heading {mind.Heading} should follow the target, now toward {toward}");
    }

    [Fact]
    public void A_search_the_script_has_switched_off_asks_again_in_a_second_and_takes_nobody()
    {
        using var files = Install.Open();
        var (minds, christof, mind) = Open(files);
        christof.Position = mind.Thing.Position + new Vector3(100_000f, 0f, 0f);   // nobody to take
        mind.Enter(AiMode.EnemySearch);
        uint off = mind.Class.SearchStyle == Minds.GuardSearchStyle
            ? AiMainFlag.NoGuardSearch : AiMainFlag.NoEnemySearch;

        minds.MainFlags |= off;
        mind.NextThink = 0;
        minds.Tick(0.001f);
        Assert.Equal(minds.Now + Minds.SearchOffThink, mind.NextThink);
        Assert.Null(mind.Target);

        minds.MainFlags &= ~off;
        mind.NextThink = 0;
        minds.Tick(0.001f);
        Assert.Equal(minds.Now + Minds.SearchThink, mind.NextThink);
    }
}
